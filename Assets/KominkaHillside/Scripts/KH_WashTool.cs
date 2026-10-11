using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon.Common.Enums;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // IMP-DOG-025: a wash tool (0 sponge / 1 shower / 2 towel / 4 bottle) on the same GameObject as VRC_Pickup + VRCObjectSync.
    // Contract: docs/plans/2026-09-30-dog-wash-prototype.md §6; the bottle: docs/plans/2026-10-03-hand-wash.md §2 (IMP-DOG-039).
    //
    // Bottle (tool 4): Use down puts one dose at once (Squirt), and again every bottleRepeatSeconds while Use is held. Each dose
    // is one _LocalContact of bottleSquirtSeconds followed at once by _EndContact (a dot: never a line to the previous dose),
    // a one-shot haptic and a few particles from the nozzle (the holder's screen only; everyone sees the foam on the dog).
    // It never opens the sponge's continuous contact, so the per-frame contact bookkeeping below stays empty for it.
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
        // Optional premium input; effect kind remains tool. Existing free tools keep these defaults.
        public bool sprayInput;
        public float pulseSeconds;     // 0 toggle / > 0 one shot / PulseHold hold-to-spray (OnPickupUseDown)
        public int finishKind;
        public KH_WashFinishFx[] finishTargets;
        public KH_WashSelection selection;
        private float _pulseUntil;
        public bool selectionActive = true;
        public bool _IsSprayer() { return tool == KH_DogWash.ToolShower || sprayInput; }

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
        [Tooltip("シャワーの ON/OFF: 前の切り替えからこの秒数以内の Use は無視 (連打の誤操作)")]
        public float toggleGuardSeconds = 0.15f;

        [Header("ボトル (tool 4 だけ。ビルダーの BottleTuning が書く)")]
        [Tooltip("VR: 注ぎ口 (nozzle) から前へ届く距離 (m)")]
        public float bottleReach = 0.6f;
        [Tooltip("VR: 狙いの SphereCast の半径 (m)")]
        public float bottleAimRadius = 0.02f;
        [Tooltip("1 回分の泡の秒数 (KH_DogWash の Stroke の上限 0.25 s)")]
        public float bottleSquirtSeconds = 0.25f;
        [Tooltip("Use を押したままの繰り返し間隔 (s)")]
        public float bottleRepeatSeconds = 0.4f;
        public float bottleHapticDuration = 0.06f;
        public float bottleHapticAmplitude = 0.35f;
        [Tooltip("注ぎ口の粒 (任意)。1 回ごとに Emit(8)。持ち主の画面だけ")]
        public ParticleSystem squirtParticles;

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
        private bool _useHeld;             // sponge / towel: Use is held (Desktop contact)
        private bool _sprayOn;             // shower: the holder's toggle
        private float _lastToggleAt = -100f;
        private bool _sprayShown;
        private float _lastHaptic;
        private float _nextSquirt;         // bottle: when the held Use puts the next dose

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
            _idleNext = Time.time + UnityEngine.Random.Range(0f, IdleInterval);   // IMP-DOG-034 B2: a phase per tool
            _started = true;
            if (Busy()) WakeFrames(); else StartIdleLoop();   // IMP-DOG-036 S5
        }

        // IMP-DOG-036 S5: a disabled behaviour got no Update, but delayed events still run on a disabled UdonBehaviour (SDK 3.10.5
        // RunProgram has no enabled check). Both ticks stop themselves when disabled (no body) and OnEnable starts the loop that fits
        // the state. OnEnable runs before Start on the first activation (the map and the phase are not built yet): Start starts it then.
        private bool _started;
        void OnEnable()
        {
            if (!_started) return;
            if (Busy()) WakeFrames(); else StartIdleLoop();
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

        // 2026-10-07 audit RC-7 (a): public so KH_DogWash defers a stroke that arrives before the holder / Owner change for the
        // same time (the stroke before both was dropped here; KH_DogWash re-asks _IsHeldBy within this)
        public const float HoldGrace = 1.5f;
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

        // The shower is a toggle (user decision 2026-09-30: holding the trigger down is tiring): Use down flips the
        // spray (off -> on -> off), Use up does nothing, and letting go (or the reset's return) turns it off. The same
        // for VR and Desktop. The sponge and the towel are unchanged (VR: touch; Desktop: while Use is held).
        public override void OnPickup()
        {
            _held = true;
            _useHeld = false;
            _sprayOn = false;                     // picked up: always starts off
            EndAllContacts();
            if (_IsSprayer() && state != null && state.spraying) state._SetSpraying(false);
            ApplySpray(Want());
            WakeFrames();   // IMP-DOG-036 S5: held = every frame
        }

        public override void OnDrop()
        {
            _held = false;
            _useHeld = false;
            _sprayOn = false;
            EndAllContacts();
            if (_IsSprayer() && state != null) state._SetSpraying(false);
            ApplySpray(Want());
        }

        // IMP-DOG-049 (2026-10-11): pulseSeconds picks the spray input of a sprayer (KH_WashSelection writes it per item):
        //   0          = toggle (Use down flips on / off; the free shower, the watering can)
        //   > 0        = one shot per press: on for pulseSeconds; a press while the shot runs is ignored, so the cadence is pulseSeconds
        //                (the water pistol)
        //   PulseHold  = hold to spray: on while Use is held, off on Use up (the hose; plan 2026-10-11 §3.2)
        // Toggle and hold go through state._SetSpraying as before; a shot goes through state._Shot (a synced shot serial + its server time,
        // so the others play one burst per shot and a late joiner replays none: Codex review 2026-10-11).
        public const float PulseHold = -1f;
        public override void OnPickupUseDown()
        {
            if (_IsSprayer())
            {
                float now = Time.time;
                if (pulseSeconds > 0f && _sprayOn) return;              // one shot at a time
                if (pulseSeconds >= 0f && now - _lastToggleAt < toggleGuardSeconds) return;   // a double tap: ignore the second flip
                _lastToggleAt = now;
                _sprayOn = pulseSeconds != 0f || !_sprayOn;
                // one shot: a serial on the state (KH_WashToolState._Shot), not the spraying bit; toggle / hold: the spraying bit as before
                if (pulseSeconds > 0f) { _pulseUntil = now + pulseSeconds; if (state != null) state._Shot(_ShownItemHere()); }
                else if (state != null) state._SetSpraying(_sprayOn);
                if (!_sprayOn) EndAllContacts();
                ApplySpray(Want());
                return;
            }
            _useHeld = true;
            if (tool == KH_DogWash.ToolBottle)
            {
                // One dose now; BusyFrame repeats it every bottleRepeatSeconds while Use stays held (VR and Desktop alike).
                _nextSquirt = Time.time + bottleRepeatSeconds;
                Squirt();
            }
        }

        public override void OnPickupUseUp()
        {
            if (_IsSprayer())
            {
                // the toggle and the shot ignore the release; hold-to-spray (the hose) stops here
                if (pulseSeconds < 0f && _sprayOn)
                {
                    _sprayOn = false;
                    if (state != null) state._SetSpraying(false);
                    EndAllContacts();
                    ApplySpray(Want());
                }
                return;
            }
            if (tool == KH_DogWash.ToolBottle)
            {
                _useHeld = false;                         // stop repeating; every dose already closed its contact
                EndAllContacts();
                return;
            }
            _useHeld = false;
            // The VR sponge keeps working without Use; the Desktop sponge / towel stops touching here.
            if (!IsLocalVR()) EndAllContacts();
        }

        // Called by KH_WashToolState (local C# call, not a network event).
        public void _OnStateChanged()
        {
            ApplySpray(Want());
        }

        bool Want()
        {
            if (!selectionActive) return false;
            if (!_IsSprayer()) return false;
            if (_held) return _sprayOn;               // the holder's own truth, no round trip
            return (state != null && state.spraying) || (pulseSeconds > 0f && Time.time < _shotShownUntil);   // + a remote one-shot (_OnShot); only while this slot still shows a one-shot item (Codex 2026-10-11)
        }

        // IMP-DOG-049: another player's shot (the serial advanced on the state, KH_WashToolState.OnDeserialization). Local only, not a network
        // event. Acts only when this slot still shows that one-shot item and is not spraying in another mode (a late notice after the slot
        // switched to the hose / the can must not spray it). Shows one burst for pulseSeconds; the frame loop turns it off through Want().
        private float _shotShownUntil = -100f;
        public void _OnShot(int item)
        {
            if (!selectionActive || _held || pulseSeconds <= 0f) return;    // the holder shows its own shot from the input
            if (state != null && state.spraying) return;                     // a toggle / hold spray is on: not a shot
            if (item < 0 || item != _ShownItemHere()) return;                // the slot shows another item now
            _shotShownUntil = Time.time + pulseSeconds;
            if (_sprayShown && spray != null) { spray.Stop(); spray.Play(); }   // still showing the previous shot: restart the burst
            ApplySpray(Want());
        }

        // IMP-DOG-049 段 B (Codex review 3): is this tool spraying now, as far as this client can tell? KH_DogWash.WaterToolMode asks it
        // only when one player holds two shower-kind tools, to tell which one a stroke came from. The holder: its own input (spray on,
        // or a shot within pulseSeconds + StrokeSourceGrace: the stroke's echo comes a round trip later). Others: the synced spraying
        // bit (toggle / hold), or a shot whose input time is within KH_WashToolState.ShotFreshMs. Read only; local, not a network event.
        public const float StrokeSourceGrace = 1f;
        public bool _SprayActive()
        {
            if (!selectionActive || !_IsSprayer()) return false;
            if (_held) return _sprayOn || (pulseSeconds > 0f && Time.time < _pulseUntil + StrokeSourceGrace);
            if (state == null) return false;
            if (state.spraying) return true;
            if (pulseSeconds <= 0f || state.shotSerial == 0) return false;
            int age = Networking.GetServerTimeInMilliseconds() - state.shotServerMs;
            return age > -KH_WashToolState.ShotFreshMs && age < KH_WashToolState.ShotFreshMs;
        }

        // The item this premium slot shows (KH_WashSelection), -1 for a free tool or none.
        int _ShownItemHere()
        {
            if (selection == null || selection.slots == null || selection.slotCategories == null) return -1;
            for (int s = 0; s < selection.slots.Length && s < selection.slotCategories.Length; s++)
                if (selection.slots[s] == this) return selection._ShownItem(selection.slotCategories[s]);
            return -1;
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
            if (on) WakeFrames();   // IMP-DOG-036 S5: spraying as shown here = every frame (incl. a remote spray from OnDeserialization)
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
            _sprayOn = false;
            EndAllContacts();
            if (_IsSprayer() && state != null) state._SetSpraying(false);
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
            TrackHolder();   // IMP-DOG-034 B2: the idle Update tracks the holder at 2 Hz only
            if (HeldForReturn()) return false;
            if (Networking.IsOwner(gameObject)) { _returnPending = false; ReturnHome(); return true; }
            if (_returnPending && Time.time - _returnAskedAt < ReturnAskTimeout) return true;   // already asked: do not fight (vrc-networking §4)
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return true;
            _returnPending = true;
            _returnAskedAt = Time.time;
            WakeFrames();   // IMP-DOG-036 S5: a reset in flight = every frame (its timeout is checked there)
            Networking.SetOwner(lp, gameObject); // net: setowner-ok: reset button, only a tool nobody holds; home is written after the transfer
            return true;
        }

        /// <summary>Held as far as this client can see (the reset skips it; the harness reads it).</summary>
        public bool _HeldNow() { TrackHolder(); return HeldForReturn(); }   // IMP-DOG-034 B2: TrackHolder (idle Update is 2 Hz)

        /// <summary>Harness only (Course/100 W-tool-spray): the return itself, now, even while debugHeldOverride plays a
        /// holder, so the spray stop on the return path is seen apart from the Owner's clean-up of an orphaned spray.
        /// Owner only (a no-op for anyone else). Local; nothing in the world calls it.</summary>
        public void _DebugReturnHome()
        {
            if (home == null || !Networking.IsOwner(gameObject)) return;
            ReturnHome();
        }

        // IMP-DOG-034 B2: a tool that is not held here, not spraying (as shown here) and has no reset request in flight does
        // its checks (another player's hold, the shelf return clock, a missed spray state) at IdleInterval on its own random
        // phase, so the 15 tools do not share a frame. Held here, spraying or a reset in flight = every frame, as before.
        // The receive check (_IsHeldBy) and the reset (_RequestReturn / _HeldNow) track the holder themselves when called.
        const float IdleInterval = 0.5f;
        private float _idleNext;

        // IMP-DOG-036 S5: no Update. The same two paths run from two self-rescheduling delayed events (the behaviour stays enabled):
        //  - _ToolIdle (seconds, on _idleNext): the idle checks above. Started in Start and whenever the frame loop stops.
        //  - _ToolFrame (one frame, EventTiming.Update): the busy frame below, while Busy(). Woken by OnPickup (_held), ApplySpray(on)
        //    (_sprayShown: the holder's toggle, OnDeserialization through _OnStateChanged, the idle SprayChecks), and _RequestReturn
        //    (_returnPending). Those are the only writers that make Busy() true. It stops on the first frame that is not busy, where it
        //    runs the idle checks if they are due (what the old Update did on that frame) and hands over to _ToolIdle.
        // Order: a delayed Update-timed event runs after every Update callback of that frame. The stroke handshake (_strokeFrame) is
        // written and read inside this one frame body; KH_DogWash takes the contact through _LocalContact at once and its draw budget
        // (CanBlit) is per frame whichever runs first; the dog reads the wash by time (_WashHold / _StrokeAgeOf), not by frame. The old
        // Update order against KH_DogWash / the dog was not fixed either (Udon runs Updates in no set order), so this is one of the
        // orders the code already had to accept.
        private bool _frameLoop;   // _ToolFrame is scheduled
        private bool _idleLoop;    // _ToolIdle is scheduled

        bool Busy() { return selectionActive && (_held || _sprayShown || _returnPending); }

        void WakeFrames()
        {
            if (_frameLoop) return;
            _frameLoop = true;
            SendCustomEventDelayedFrames(nameof(_ToolFrame), 1, EventTiming.Update);
        }

        void StartIdleLoop()
        {
            if (_idleLoop) return;
            _idleLoop = true;
            float wait = _idleNext - Time.time;
            SendCustomEventDelayedSeconds(nameof(_ToolIdle), wait > 0f ? wait : 0f, EventTiming.Update);
        }

        /// <summary>IMP-DOG-036 S5: local loop (delayed event). Not a network target.</summary>
        public void _ToolIdle()
        {
            _idleLoop = false;
            if (!enabled) return;                      // disabled: stop (OnEnable restarts)
            if (_frameLoop) return;                    // the frame loop runs; it restarts this one when it stops
            if (Busy()) { WakeFrames(); return; }
            IdleStep();
            if (_frameLoop) return;                    // the checks woke it (a missed spray state)
            StartIdleLoop();
        }

        /// <summary>IMP-DOG-036 S5: local loop (delayed event). Not a network target.</summary>
        public void _ToolFrame()
        {
            // disabled: no body, no idle loop (OnEnable restarts). Before Start (a spray state deserialized first): Start restarts it
            if (!_started || !enabled) { _frameLoop = false; return; }
            if (!Busy())
            {
                _frameLoop = false;
                IdleStep();
                if (!_frameLoop) StartIdleLoop();
                return;
            }
            BusyFrame();
            SendCustomEventDelayedFrames(nameof(_ToolFrame), 1, EventTiming.Update);
        }

        // The old Update's idle branch (IMP-DOG-034 B2).
        void IdleStep()
        {
            float now = Time.time;
            if (now < _idleNext) return;
            _idleNext = now + IdleInterval;
            TrackHolder();
            ReturnTick();
            if (_IsSprayer()) SprayChecks();   // a sponge / towel never sprays (only the shower sets it)
        }

        // The old Update's busy branch.
        void BusyFrame()
        {
            if (_held && pulseSeconds > 0f && _sprayOn && Time.time >= _pulseUntil)
            {
                _sprayOn = false; if (state != null) state._SetSpraying(false); EndAllContacts(); ApplySpray(false);
            }
            TrackHolder();
            ReturnTick();
            if (_IsSprayer()) SprayChecks();

            if (!_held || washes == null || _strokeFrame == null) return;
            if (tool == KH_DogWash.ToolBottle && !sprayInput)
            {
                // Doses only (no per-frame contact, no continuous haptic). The first one came from OnPickupUseDown.
                if (_useHeld && Time.time >= _nextSquirt)
                {
                    _nextSquirt = Time.time + bottleRepeatSeconds;
                    Squirt();
                }
                return;
            }
            if (nozzle == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            bool vr = lp.IsUserInVR();
            bool any;
            if (_IsSprayer()) any = _sprayOn && SprayRays();   // spray on while held (the toggle)
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

        // Shower only (IMP-DOG-034 B2: the sponge / towel state never sprays, so these were no-ops for them).
        void SprayChecks()
        {
            ApplySpray(Want());   // catches a missed deserialization; no-op when unchanged

            // A spray left on by a holder who is gone: the state's new Owner clears it (vrc-networking §6).
            // (debugHeldOverride > 0: the harness plays a holder, so the spray it switched on stays on.)
            if (!_held && state != null && state.spraying && Networking.IsOwner(state.gameObject)
                && (_pickup == null || !_pickup.IsHeld) && debugHeldOverride <= 0)
                state._SetSpraying(false);
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
                Stroke(d, key % 256, hit.point, Time.deltaTime, rad);
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
                Stroke(d, _ovKey[best] % 256, _ovPoint[best], Time.deltaTime, -1f);
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
            Stroke(d, key % 256, hit.point, Time.deltaTime, -1f);
            return true;
        }

        // seconds: this contact's dwell (the continuous tools pass Time.deltaTime; the bottle passes one dose).
        void Stroke(int d, int ci, Vector3 point, float seconds, float radius)
        {
            if (finishKind > 0)
            {
                if (finishTargets != null && d < finishTargets.Length && finishTargets[d] != null) finishTargets[d]._LocalFinish(finishKind, point);
                return;
            }
            KH_DogWash w = washes[d];
            if (w == null) return;
            _touching[d] = true;
            w._LocalContact(tool, ci, point, seconds, radius);
        }

        // ---- bottle (tool 4) ----

        // One dose from the holder: VR aims from the nozzle (a thin SphereCast, bottleReach), Desktop / Touch from the view centre
        // (the same ray as ContactGaze, desktopReach). Particles and the one-shot haptic come with every dose, hit or miss.
        void Squirt()
        {
            if (washes == null || _touching == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (squirtParticles != null) squirtParticles.Emit(8);
            if (lp.IsUserInVR())
            {
                Transform n = nozzle != null ? nozzle : transform;
                SquirtAt(n.position, n.forward, bottleReach, bottleAimRadius);
                BottleHaptic(lp);
            }
            else
            {
                VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                SquirtAt(head.position, head.rotation * Vector3.forward, desktopReach, 0f);
            }
        }

        // The first mapped part along the aim gets one dose, closed at once. radius > 0 = SphereCast; a SphereCast does not report
        // a collider the sphere already overlaps at its start (the nozzle pressed into the coat), so that case takes the nearest
        // overlapping part at the start instead.
        bool SquirtAt(Vector3 origin, Vector3 dir, float reach, float radius)
        {
            if (dir.sqrMagnitude < 1e-8f) return false;
            dir = dir.normalized;
            RaycastHit hit;
            Collider col = null;
            Vector3 point = origin;
            bool got;
            if (radius > 0f) got = Physics.SphereCast(origin, radius, dir, out hit, reach, washLayer, QueryTriggerInteraction.Collide);
            else got = Physics.Raycast(origin, dir, out hit, reach, washLayer, QueryTriggerInteraction.Collide);
            if (got) { col = hit.collider; point = hit.point; }
            else if (radius > 0f)
            {
                int n = Physics.OverlapSphereNonAlloc(origin, radius, _overlap, washLayer, QueryTriggerInteraction.Collide);
                if (n > _overlap.Length) n = _overlap.Length;
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (Lookup(_overlap[i]) < 0) continue;
                    Vector3 cp = _overlap[i].ClosestPoint(origin);
                    float dd = (cp - origin).sqrMagnitude;
                    if (dd < best) { best = dd; col = _overlap[i]; point = cp; }
                }
            }
            int key = Lookup(col);
            if (key < 0) return false;
            int d = key / 256;
            if (washes[d] == null) return false;
            Stroke(d, key % 256, point, bottleSquirtSeconds, -1f);
            EndContact(d);                         // a dot: the next dose never joins this one
            return true;
        }

        void BottleHaptic(VRCPlayerApi lp)
        {
            if (_pickup == null) return;
            VRC_Pickup.PickupHand hand = _pickup.currentHand;
            if (hand == VRC_Pickup.PickupHand.None) return;
            lp.PlayHapticEventInHand(hand, bottleHapticDuration, bottleHapticAmplitude, hapticFrequency);
        }

        /// <summary>Harness only (Course/100g H-bottle / 100p cosmetic isolation): one dose along (origin, direction) with the VR aim (SphereCast of
        /// bottleAimRadius, bottleReach), as a Use press would put it, without the pickup. Particles too, no haptic.
        /// Local; nothing in the world calls it.</summary>
        public void _DebugSquirt(Vector3 origin, Vector3 direction)
        {
            if ((tool != KH_DogWash.ToolBottle && finishKind <= 0) || washes == null || _touching == null) return;
            if (squirtParticles != null) squirtParticles.Emit(8);
            SquirtAt(origin, direction, bottleReach, bottleAimRadius);
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
        public void _SetSelected(bool value)
        {
            selectionActive = value;
            if (value) return;
            _useHeld = false; _sprayOn = false; _shotShownUntil = -100f; EndAllContacts(); ApplySpray(false);
            if (state != null && Networking.IsOwner(state.gameObject)) state._SetSpraying(false);
        }
        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqPremiumReturn()
        {
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Networking.IsOwner(gameObject) || selection == null || !Utilities.IsValid(p) || !Networking.IsOwner(p, selection.gameObject) || _HeldNow()) return;
            if (home != null) ReturnHome();
        }

    }
}
