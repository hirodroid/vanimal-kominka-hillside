using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-DOG-025: a wash tool (0 sponge / 1 shower / 2 towel) on the same GameObject as VRC_Pickup + VRCObjectSync.
    // Contract: docs/plans/2026-09-30-dog-wash-prototype.md §6.
    //
    // Only the holder tests contact (Raycast / overlap) and hands this frame's contact point to the dog's KH_DogWash
    // (_LocalContact; the dog keeps the history in mesh space). KH_DogWash draws it, sends it, and plays the contact
    // sound and splash at the stroke (for everyone). This tool only shows the spray itself (particles + nozzle loop),
    // which every client derives from KH_WashToolState. When a dog stops being touched (no hit this frame, Use
    // released, dropped) the tool ends that dog's contact explicitly (_EndContact), so a short gap is never bridged.
    //
    // Hit -> dog: a table built in Start from every washes[i].partColliders (Collider -> dog, index), searched
    // linearly with a one-entry cache. Nothing is written onto the colliders' GameObjects, so the builder keeps one
    // source of truth (partColliders).
    //
    // One stroke per dog per frame per tool: the shower's centre ray wins over the four ring rays, the nearest
    // overlap wins for the sponge, so a dog never gets several parts per frame from one tool (the send side cuts
    // a segment on every part change, which would flood the 8 Hz budget).
    //
    // Return to the shelf (IMP-ENV-007): a tool nobody holds that has been away from its home (a Transform under
    // KH_WashStation/Homes, or KH_WashStations/{name}/Homes along the course (IMP-ENV-008), so it moves with the shelf) for returnSeconds goes back there. Every client keeps its own
    // clock (reset while it sees the tool held, and when the tool leaves home); only the Owner of this GameObject (the
    // last holder: the Pickup hands it over, or whoever inherited it) moves it, the same way KH_ToyRecall parks a toy
    // (write the position, FlagDiscontinuity). The others see it through VRCObjectSync. Nobody takes the ownership
    // for it, so a tool in someone's hand is never pulled away. The spray and the contacts end before the move.
    // Decided 2026-09-30 (user): tools stay where they were put; the automatic return is off (returnSeconds 0, written
    // by the builder; the code stays). They go home only from the station's reset button (KH_WashStationReset ->
    // _RequestReturn): the presser takes the ownership of each tool nobody holds, and writes the home position once the
    // transfer is confirmed (OnOwnershipTransferred; vrc-networking §4), as KH_TreatPouch's recall does.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class KH_WashTool : UdonSharpBehaviour
    {
        public int tool;
        public Transform nozzle;
        public KH_DogWash[] washes;
        public LayerMask washLayer;
        public ParticleSystem spray;
        public AudioSource sprayAudio;
        public KH_WashToolState state;

        [Header("Prototype values (Play で決め直す)")]
        public float sprayRange = 1.5f;
        [Tooltip("ノズルの直前でのブラシ半径 (m)")]
        public float sprayRadiusNear = 0.07f;
        [Tooltip("距離 1 m でのブラシ半径 (m)。距離で線形に広がる (上限 0.2)")]
        public float sprayRadiusAt1m = 0.11f;
        [Tooltip("周囲 4 本の Raycast の開き (度)")]
        public float sprayRingDegrees = 4.5f;
        [Tooltip("スポンジ・タオルの先端の球の半径 (m)")]
        public float contactRadius = 0.08f;
        [Tooltip("Desktop / Touch: 視線の中心からの届く距離 (m)")]
        public float desktopReach = 2f;
        public float hapticInterval = 0.1f;
        public float hapticDuration = 0.03f;
        public float hapticAmplitude = 0.2f;
        public float hapticFrequency = 120f;

        [Header("置き場へ戻る (IMP-ENV-007。ビルダーが書く)")]
        [Tooltip("棚の定位置 (KH_WashStation/Homes または KH_WashStations/{名前}/Homes の子。棚と一緒に動く)")]
        public Transform home;
        [Tooltip("誰も持たずに定位置から離れてからこの秒数で戻る。0 以下 = 自動では戻らない (2026-09-30 ユーザー決定: リセットボタンで戻す)")]
        public float returnSeconds = 0f;
        [Tooltip("定位置からこの距離 (m) 以内なら戻っているとみなす")]
        public float homeTolerance = 0.05f;
        [Tooltip("ハーネス専用: 1 = 持っている扱い / -1 = 持っていない扱い / 0 = 実値。本番は 0 (Wash/12 が確かめる)")]
        public int debugHeldOverride = 0;

        private VRC_Pickup _pickup;
        private VRCObjectSync _sync;
        private float _awaySince = -1f;    // local clock: last seen held or left home (-1 = at home)
        private int _returnCount;          // harness read-out only
        private bool _returnPending;       // the reset asked for this tool's ownership; write home when it lands
        private float _returnAskedAt = -100f;
        const float ReturnAskTimeout = 4f;
        private bool _held;
        private bool _useHeld;
        private bool _sprayShown;
        private float _lastHaptic;

        private Collider[] _mapCol;
        private int[] _mapKey;             // dog * 256 + collider index
        private Collider _cacheCol;
        private int _cacheKey = -1;

        private int[] _strokeFrame;        // per dog: the frame it last got a stroke from this tool
        private bool[] _touching;          // per dog: this tool's contact is open on that dog
        private Collider[] _overlap = new Collider[8];
        private int[] _ovKey = new int[8];
        private Vector3[] _ovPoint = new Vector3[8];
        private float[] _ovDist = new float[8];

        void Start()
        {
            _pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            _sync = GetComponent<VRCObjectSync>();
            BuildMap();
            // Force the spray off once (a particle system may be Play On Awake).
            _sprayShown = true;
            ApplySpray(false);
        }

        void BuildMap()
        {
            int dogs = washes != null ? washes.Length : 0;
            _strokeFrame = new int[dogs];
            _touching = new bool[dogs];
            int total = 0;
            for (int d = 0; d < dogs; d++)
            {
                _strokeFrame[d] = -1;
                if (washes[d] != null && washes[d].partColliders != null) total += washes[d].partColliders.Length;
            }
            _mapCol = new Collider[total];
            _mapKey = new int[total];
            int k = 0;
            for (int d = 0; d < dogs; d++)
            {
                if (washes[d] == null || washes[d].partColliders == null) continue;
                Collider[] cols = washes[d].partColliders;
                for (int i = 0; i < cols.Length && i < 256; i++)
                {
                    _mapCol[k] = cols[i];
                    _mapKey[k] = d * 256 + i;
                    k++;
                }
            }
        }

        int Lookup(Collider c)
        {
            if (c == null || _mapCol == null) return -1;
            if (c == _cacheCol) return _cacheKey;
            int key = -1;
            for (int i = 0; i < _mapCol.Length; i++)
            {
                if (_mapCol[i] == c) { key = _mapKey[i]; break; }
            }
            _cacheCol = c;
            _cacheKey = key;
            return key;
        }

        // For KH_DogWash's receive check (remote senders only; the own echo is trusted there): the player actually
        // holds this tool now (VRC_Pickup.currentPlayer, which every client sees). The pickup state and a stroke travel
        // separately, so two short graces: the player held it until HoldGrace ago (the last strokes after a drop or a
        // hand-over), or became its Owner within HoldGrace while nobody is seen holding it yet (the first strokes
        // before the held state arrives; the Pickup hands the ownership to the holder). Owning it alone is not enough:
        // the Owner stays the Owner after the drop.
        public bool _IsHeldBy(int playerId)
        {
            TrackHolder();
            if (_holderId >= 0) { if (_holderId == playerId) return true; }
            float now = Time.time;
            if (_prevHolderId == playerId && now <= _prevHolderUntil) return true;
            if (_holderId >= 0 || _ownerSinceId != playerId || now - _ownerSinceAt > HoldGrace) return false;
            VRCPlayerApi o = Networking.GetOwner(gameObject);
            return Utilities.IsValid(o) && o.playerId == playerId;
        }

        const float HoldGrace = 1.5f;
        private int _holderId = -1;
        private int _prevHolderId = -1;
        private float _prevHolderUntil = -100f;
        private int _ownerSinceId = -1;
        private float _ownerSinceAt = -100f;

        // Every client, every frame (and on demand): who holds the tool, and who held it last.
        void TrackHolder()
        {
            int id = -1;
            if (_pickup != null)
            {
                VRCPlayerApi p = _pickup.currentPlayer;
                if (Utilities.IsValid(p)) id = p.playerId;
            }
            if (id == _holderId) return;
            if (_holderId >= 0)
            {
                _prevHolderId = _holderId;
                _prevHolderUntil = Time.time + HoldGrace;
            }
            _holderId = id;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return;
            _ownerSinceId = player.playerId;
            _ownerSinceAt = Time.time;
            // A new Owner (a pickup hand-over, or the old Owner left) counts the return from now: its own clock may have missed
            // the last hold (seen late / joined late), so it waits the full returnSeconds instead of snapping the tool away at once.
            if (_awaySince >= 0f) _awaySince = Time.time;
            // The reset's request landed: write home now, unless someone picked it up meanwhile (then the pickup took it).
            if (_returnPending)
            {
                if (Networking.IsOwner(gameObject))
                {
                    _returnPending = false;
                    if (!HeldForReturn() && home != null) ReturnHome();
                }
                else if (Utilities.IsValid(Networking.LocalPlayer) && player.playerId != Networking.LocalPlayer.playerId) _returnPending = false;   // someone else took it: give up
            }
        }

        // ---- Pickup (these fire only on the holder's client) ----

        public override void OnPickup()
        {
            _held = true;
            _useHeld = false;
            EndAllContacts();
        }

        public override void OnDrop()
        {
            _held = false;
            _useHeld = false;
            EndAllContacts();
            if (tool == KH_DogWash.ToolShower && state != null) state._SetSpraying(false);
            ApplySpray(Want());
        }

        public override void OnPickupUseDown()
        {
            _useHeld = true;
            if (tool == KH_DogWash.ToolShower && state != null) state._SetSpraying(true);
            ApplySpray(Want());
        }

        public override void OnPickupUseUp()
        {
            _useHeld = false;
            if (tool == KH_DogWash.ToolShower && state != null) state._SetSpraying(false);
            // The VR sponge keeps working without Use; everything else stops touching here.
            if (tool == KH_DogWash.ToolShower || !IsLocalVR()) EndAllContacts();
            ApplySpray(Want());
        }

        // Called by KH_WashToolState (local C# call, not a network event).
        public void _OnStateChanged()
        {
            ApplySpray(Want());
        }

        bool Want()
        {
            if (tool != KH_DogWash.ToolShower) return false;
            if (_held) return _useHeld;               // the holder's own truth, no round trip
            return state != null && state.spraying;
        }

        bool IsLocalVR()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            return Utilities.IsValid(lp) && lp.IsUserInVR();
        }

        void ApplySpray(bool on)
        {
            if (_sprayShown == on) return;
            _sprayShown = on;
            if (spray != null) { if (on) spray.Play(); else spray.Stop(); }
            if (sprayAudio != null) { if (on) sprayAudio.Play(); else sprayAudio.Stop(); }
        }

        void EndAllContacts()
        {
            if (_touching == null) return;
            for (int d = 0; d < _touching.Length; d++) EndContact(d);
        }

        void EndContact(int d)
        {
            if (!_touching[d]) return;
            _touching[d] = false;
            if (washes[d] != null) washes[d]._EndContact(tool);
        }

        // ---- return to the shelf (every client counts; only the Owner moves it) ----

        bool HeldForReturn()
        {
            if (debugHeldOverride > 0) return true;
            if (debugHeldOverride < 0) return false;
            return _held || _holderId >= 0 || (_pickup != null && _pickup.IsHeld);
        }

        void ReturnTick()
        {
            if (_returnPending && Time.time - _returnAskedAt >= ReturnAskTimeout) _returnPending = false;
            if (home == null || returnSeconds <= 0f) return;   // 0 or less: never returns by itself (the production value)
            float now = Time.time;
            if (HeldForReturn()) { _awaySince = now; return; }
            Vector3 d = transform.position - home.position;
            if (d.sqrMagnitude <= homeTolerance * homeTolerance) { _awaySince = -1f; return; }
            if (_awaySince < 0f) { _awaySince = now; return; }   // just left home (moved by someone else's sync, or put down)
            if (now - _awaySince < returnSeconds) return;
            if (!Networking.IsOwner(gameObject)) return;          // the Owner moves it; do not take the ownership for this
            ReturnHome();
        }

        void ReturnHome()
        {
            _awaySince = -1f;
            _useHeld = false;
            EndAllContacts();
            if (tool == KH_DogWash.ToolShower && state != null) state._SetSpraying(false);
            ApplySpray(false);
            transform.position = home.position;
            transform.rotation = home.rotation;
            if (_sync != null) _sync.FlagDiscontinuity();          // a teleport: others do not see it slide there
            _returnCount++;
        }

        /// <summary>The reset button (KH_WashStationReset, local, from Interact): send this tool home unless someone holds it.
        /// Already the Owner: at once. Otherwise take the ownership (a tool nobody holds: nobody's hand is emptied) and write
        /// home in OnOwnershipTransferred. A request that has not landed in ReturnAskTimeout is dropped (press again).
        /// Returns false when the tool is held (skipped).</summary>
        public bool _RequestReturn()
        {
            if (home == null) return true;
            if (HeldForReturn()) return false;
            if (Networking.IsOwner(gameObject)) { _returnPending = false; ReturnHome(); return true; }
            if (_returnPending && Time.time - _returnAskedAt < ReturnAskTimeout) return true;   // already asked: do not fight (vrc-networking §4)
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return true;
            _returnPending = true;
            _returnAskedAt = Time.time;
            Networking.SetOwner(lp, gameObject); // net: setowner-ok: reset button, only a tool nobody holds; home is written after the transfer
            return true;
        }

        /// <summary>Held as far as this client can see (the reset skips it; the harness reads it).</summary>
        public bool _HeldNow() { return HeldForReturn(); }

        /// <summary>Harness only (Course/100 W-tool-spray): the return itself, now, even while debugHeldOverride plays a
        /// holder, so the spray stop on the return path is seen apart from the Owner's clean-up of an orphaned spray.
        /// Owner only (a no-op for anyone else). Local; nothing in the world calls it.</summary>
        public void _DebugReturnHome()
        {
            if (home == null || !Networking.IsOwner(gameObject)) return;
            ReturnHome();
        }

        void Update()
        {
            TrackHolder();
            ReturnTick();
            ApplySpray(Want());   // catches a missed deserialization; no-op when unchanged

            // A spray left on by a holder who is gone: the state's new Owner clears it (vrc-networking §6).
            // (debugHeldOverride > 0: the harness plays a holder, so the spray it switched on stays on.)
            if (!_held && state != null && state.spraying && Networking.IsOwner(state.gameObject)
                && (_pickup == null || !_pickup.IsHeld) && debugHeldOverride <= 0)
                state._SetSpraying(false);

            if (!_held || nozzle == null || washes == null || _strokeFrame == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            bool vr = lp.IsUserInVR();
            bool any;
            if (tool == KH_DogWash.ToolShower) any = _useHeld && SprayRays();
            else if (vr) any = ContactSphere();
            else any = _useHeld && ContactGaze(lp);
            if (any && vr) Haptic(lp);

            // A dog touched last frame and not this one: its contact ends now (no bridging across the gap).
            int frame = Time.frameCount;
            for (int d = 0; d < _touching.Length; d++)
            {
                if (_touching[d] && _strokeFrame[d] != frame) EndContact(d);
            }
        }

        // Centre ray + 4 ring rays; the brush widens with distance.
        bool SprayRays()
        {
            Vector3 o = nozzle.position;
            Vector3 f = nozzle.forward;
            int frame = Time.frameCount;
            bool any = false;
            for (int r = 0; r < 5; r++)
            {
                Vector3 dir = f;
                if (r == 1) dir = Quaternion.AngleAxis(sprayRingDegrees, nozzle.up) * f;
                else if (r == 2) dir = Quaternion.AngleAxis(-sprayRingDegrees, nozzle.up) * f;
                else if (r == 3) dir = Quaternion.AngleAxis(sprayRingDegrees, nozzle.right) * f;
                else if (r == 4) dir = Quaternion.AngleAxis(-sprayRingDegrees, nozzle.right) * f;
                RaycastHit hit;
                if (!Physics.Raycast(o, dir, out hit, sprayRange, washLayer, QueryTriggerInteraction.Collide)) continue;
                int key = Lookup(hit.collider);
                if (key < 0) continue;
                int d = key / 256;
                if (_strokeFrame[d] == frame) continue;
                _strokeFrame[d] = frame;
                float rad = Mathf.Clamp(sprayRadiusNear + (sprayRadiusAt1m - sprayRadiusNear) * hit.distance, sprayRadiusNear, 0.2f);
                Stroke(d, key % 256, hit.point, rad);
                any = true;
            }
            return any;
        }

        // VR sponge / towel: the tip sphere against the part colliders; the nearest per dog is the contact.
        bool ContactSphere()
        {
            Vector3 p = nozzle.position;
            int n = Physics.OverlapSphereNonAlloc(p, contactRadius, _overlap, washLayer, QueryTriggerInteraction.Collide);
            if (n > _overlap.Length) n = _overlap.Length;
            for (int i = 0; i < n; i++)
            {
                _ovKey[i] = Lookup(_overlap[i]);
                if (_ovKey[i] < 0) continue;
                Vector3 cp = _overlap[i].ClosestPoint(p);
                _ovPoint[i] = cp;
                _ovDist[i] = (cp - p).sqrMagnitude;
            }
            bool any = false;
            int frame = Time.frameCount;
            for (int i = 0; i < n; i++)
            {
                if (_ovKey[i] < 0) continue;
                int d = _ovKey[i] / 256;
                if (_strokeFrame[d] == frame) continue;
                int best = i;
                for (int j = i + 1; j < n; j++)
                {
                    if (_ovKey[j] < 0 || _ovKey[j] / 256 != d) continue;
                    if (_ovDist[j] < _ovDist[best]) best = j;
                }
                _strokeFrame[d] = frame;
                Stroke(d, _ovKey[best] % 256, _ovPoint[best], -1f);
                any = true;
            }
            return any;
        }

        // Desktop / Touch sponge / towel: while Use is held, the point the view centre hits within desktopReach.
        bool ContactGaze(VRCPlayerApi lp)
        {
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            RaycastHit hit;
            if (!Physics.Raycast(head.position, head.rotation * Vector3.forward, out hit, desktopReach, washLayer, QueryTriggerInteraction.Collide)) return false;
            int key = Lookup(hit.collider);
            if (key < 0) return false;
            int d = key / 256;
            int frame = Time.frameCount;
            if (_strokeFrame[d] == frame) return false;
            _strokeFrame[d] = frame;
            Stroke(d, key % 256, hit.point, -1f);
            return true;
        }

        void Stroke(int d, int ci, Vector3 point, float radius)
        {
            KH_DogWash w = washes[d];
            if (w == null) return;
            _touching[d] = true;
            w._LocalContact(tool, ci, point, Time.deltaTime, radius);
        }

        void Haptic(VRCPlayerApi lp)
        {
            if (_pickup == null || hapticInterval <= 0f) return;
            float now = Time.time;
            if (now - _lastHaptic < hapticInterval) return;
            VRC_Pickup.PickupHand hand = _pickup.currentHand;
            if (hand == VRC_Pickup.PickupHand.None) return;
            _lastHaptic = now;
            lp.PlayHapticEventInHand(hand, hapticDuration, hapticAmplitude, hapticFrequency);
        }
    }
}
