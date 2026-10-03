using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Enums;

namespace KominkaHillside
{
    // IMP-DOG-039: lathering with the bare hands (one per scene, KH_WashStation/HandWash). Contract:
    // docs/plans/2026-10-03-hand-wash.md §3.1-3.3.
    //
    // Local only (no sync, no network entry). The local VR player's free hands are tested against the part colliders of the one
    // dog that a station holds and has arrived (KH_WashStationCall._HeldDog / _Arrived, synced), while the head is within
    // nearMeters (horizontal) of that station's anchor. Each touching hand hands its contact point to the dog's KH_DogWash
    // (_HandContact(hand, colliderIndex, point, handPosition, seconds); hand 0 = left, 1 = right; the hand position measures the rub). The dog decides whether it lathers (moving
    // over foam it can see) and draws / sends it; this side only gives the contact and a weak haptic when the dog says it lathered.
    // A hand that stops touching ends its contact explicitly (_EndHand), so no line is drawn across a gap or between the hands.
    //
    // Free hand: VRCPlayerApi.GetPickupInHand(hand) is null (a hand holding the bottle, a sponge, a leash end or the disc is not a
    // bare hand). Valid hand: not near the origin and within HandValidMax of the head (the petting's rule, KD_DogPetController).
    //
    // Cost: no Update. _HandIdle runs every idleInterval (5 s while not in VR) and reads the four stations; only near an arrived dog
    // does _HandFrame run every frame, testing contact every sampleInterval and re-picking the target every idleInterval.
    //
    // Hit -> collider index: only the target dog's own partColliders are searched (Codex review of the implementation, 7), with a
    // last-hit cache per hand so the two hands on different colliders do not evict each other. The held dog index maps to the
    // slot (index in washes) whose KH_DogWash.dogIndex matches (washes are by dog index as the builder writes them; the search
    // only guards a different order).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_HandWash : UdonSharpBehaviour
    {
        [Header("配線 (ビルダー)")]
        public KH_WashStationCall[] calls;
        public KH_DogWash[] washes;          // all six, by dog index
        public LayerMask washLayer;

        [Header("試作値 (ビルダーの HandWashTuning が書く)")]
        [Tooltip("手の接触の球の半径 (m)")]
        public float handContactRadius = 0.07f;
        [Tooltip("接触の判定の間隔 (s)")]
        public float sampleInterval = 0.0333f;
        [Tooltip("着いた犬の洗い場の anchor から頭までの水平距離 (m)。これより遠いと毎フレームの処理を止める")]
        public float nearMeters = 2.5f;
        [Tooltip("待機の見直し・対象の見直しの間隔 (s)")]
        public float idleInterval = 0.5f;
        public float hapticInterval = 0.1f;
        public float hapticDuration = 0.03f;
        public float hapticAmplitude = 0.15f;
        public float hapticFrequency = 120f;

        [Header("ハーネス専用 (本番は debugHands 0 / debugVr false。Wash/12 が確かめる)")]
        [Tooltip("1 = 下の debugLeft / debugRight と debug*Free を手の値として使う (頭からの距離の検査はしない)")]
        public int debugHands = 0;
        public Vector3 debugLeft;
        public Vector3 debugRight;
        public bool debugLeftFree;
        public bool debugRightFree;
        [Tooltip("true = VR として扱う")]
        public bool debugVr;
        [Tooltip("読み取り専用 (ハーネス): 毎フレームのループが動いている")]
        public bool frameLoop;
        [Tooltip("読み取り専用 (ハーネス): _HandContact を呼んだ回数")]
        public int contactCalls;

        const float HandValidMax = 1.2f;     // m from the head (KD_DogPetController.handValidMaxDistance)
        const float NonVrRecheck = 5f;       // s: not in VR, look again this rarely

        private bool _started;
        private bool _idleLoop;              // _HandIdle is scheduled
        private int _target = -1;            // slot in washes of the dog the hands work on (-1 = none)
        private float _retargetAt;
        private float _sampleAt;
        private float _lastSampleAt = -1f;

        // per hand: the last collider looked up, the slot it was looked up in and its index there (-1 = not the target's)
        private Collider[] _cacheCol = new Collider[2];
        private int[] _cacheSlot = new int[2];
        private int[] _cacheIdx = new int[2];
        private Collider[] _overlap = new Collider[8];
        private Vector3 _nearPoint;

        private bool[] _open = new bool[2];          // per hand: a contact is open on _openSlot[hand]
        private int[] _openSlot = new int[2];
        private float[] _lastHaptic = new float[2];

        void Start()
        {
            _cacheSlot[0] = -1;
            _cacheSlot[1] = -1;
            _started = true;
            ScheduleIdle(UnityEngine.Random.Range(0f, idleInterval));   // a phase of its own
        }

        // Delayed events still run on a disabled UdonBehaviour (KH_WashTool's note): both loops stop themselves when disabled and
        // OnEnable starts the idle loop again (not before Start).
        void OnEnable()
        {
            if (!_started || frameLoop) return;
            ScheduleIdle(0f);
        }

        // The index of c in the target dog's partColliders (-1 = not one of them). Cached per hand.
        int Lookup(int hand, Collider c)
        {
            if (c == null || washes == null || _target < 0 || _target >= washes.Length || washes[_target] == null) return -1;
            if (c == _cacheCol[hand] && _cacheSlot[hand] == _target) return _cacheIdx[hand];
            int idx = -1;
            Collider[] cols = washes[_target].partColliders;
            if (cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] == c) { idx = i; break; }
                }
            }
            _cacheCol[hand] = c;
            _cacheSlot[hand] = _target;
            _cacheIdx[hand] = idx;
            return idx;
        }

        int SlotOf(int dog)
        {
            if (washes == null || dog < 0) return -1;
            if (dog < washes.Length && washes[dog] != null && (washes[dog].dogIndex == dog || washes[dog].dogIndex < 0)) return dog;
            for (int i = 0; i < washes.Length; i++)
            {
                if (washes[i] != null && washes[i].dogIndex == dog) return i;
            }
            return -1;
        }

        bool IsVR(VRCPlayerApi lp)
        {
            return debugVr || lp.IsUserInVR();
        }

        // The dog the hands may work on: held and arrived at a station whose anchor is within nearMeters (horizontal) of the head.
        // Stations are 80 m+ apart, so the first match is the only one.
        int PickTarget(VRCPlayerApi lp)
        {
            if (calls == null) return -1;
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            float near2 = nearMeters * nearMeters;
            for (int i = 0; i < calls.Length; i++)
            {
                KH_WashStationCall c = calls[i];
                if (c == null || c.anchor == null) continue;
                int dog = c._HeldDog();
                if (dog < 0 || !c._Arrived()) continue;
                Vector3 a = c.anchor.position;
                float dx = a.x - head.x, dz = a.z - head.z;
                if (dx * dx + dz * dz > near2) continue;
                int slot = SlotOf(dog);
                if (slot >= 0) return slot;
            }
            return -1;
        }

        // ---- loops (delayed events, no Update) ----

        void ScheduleIdle(float wait)
        {
            if (_idleLoop) return;
            _idleLoop = true;
            SendCustomEventDelayedSeconds(nameof(_HandIdle), wait > 0f ? wait : 0f, EventTiming.Update);
        }

        void StartFrames()
        {
            if (frameLoop) return;
            frameLoop = true;
            float now = Time.time;
            _retargetAt = now + idleInterval;
            _sampleAt = now;
            _lastSampleAt = -1f;
            SendCustomEventDelayedFrames(nameof(_HandFrame), 1, EventTiming.Update);
        }

        void StopFrames(float idleWait)
        {
            EndHands();
            _target = -1;
            frameLoop = false;
            if (enabled) ScheduleIdle(idleWait);
        }

        /// <summary>IMP-DOG-039: local loop (delayed event). Not a network target.</summary>
        public void _HandIdle()
        {
            _idleLoop = false;
            if (!enabled) return;                       // disabled: stop (OnEnable restarts)
            if (frameLoop) return;                      // the frame loop runs; it restarts this one when it stops
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { ScheduleIdle(idleInterval); return; }
            if (!IsVR(lp)) { ScheduleIdle(NonVrRecheck); return; }
            int t = PickTarget(lp);
            if (t < 0) { ScheduleIdle(idleInterval); return; }
            _target = t;
            StartFrames();
        }

        /// <summary>IMP-DOG-039: local loop (delayed event). Not a network target.</summary>
        public void _HandFrame()
        {
            if (!_started || !enabled) { EndHands(); _target = -1; frameLoop = false; return; }   // OnEnable restarts
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { StopFrames(idleInterval); return; }
            float now = Time.time;
            if (now >= _retargetAt)
            {
                _retargetAt = now + idleInterval;
                if (!IsVR(lp)) { StopFrames(NonVrRecheck); return; }
                int t = PickTarget(lp);
                if (t < 0) { StopFrames(idleInterval); return; }
                if (t != _target) { EndHands(); _target = t; }
            }
            if (now >= _sampleAt)
            {
                _sampleAt = now + sampleInterval;
                Sample(lp, now);
            }
            SendCustomEventDelayedFrames(nameof(_HandFrame), 1, EventTiming.Update);
        }

        // ---- contact (§3.3) ----

        void Sample(VRCPlayerApi lp, float now)
        {
            float elapsed = _lastSampleAt >= 0f ? now - _lastSampleAt : sampleInterval;
            _lastSampleAt = now;
            if (_target < 0 || _target >= washes.Length || washes[_target] == null) { EndHands(); return; }

            bool debug = debugHands == 1;
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            bool headValid = head.sqrMagnitude > 0.0001f;
            for (int hand = 0; hand < 2; hand++)
            {
                Vector3 pos;
                bool free;
                if (debug)
                {
                    pos = hand == 0 ? debugLeft : debugRight;
                    free = hand == 0 ? debugLeftFree : debugRightFree;
                }
                else
                {
                    pos = lp.GetTrackingData(hand == 0 ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand).position;
                    free = !Utilities.IsValid(lp.GetPickupInHand(hand == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right));
                }
                bool valid = free && pos.sqrMagnitude > 0.0001f;
                // Tracking lost / not initialised (the petting's rule). The harness's hands skip the head check (ClientSim's head is
                // wherever the harness left the player).
                if (valid && !debug && headValid && (pos - head).sqrMagnitude > HandValidMax * HandValidMax) valid = false;

                int ci = valid ? Nearest(hand, pos) : -1;
                if (ci < 0) { EndHand(hand); continue; }
                Vector3 point = _nearPoint;

                if (_open[hand] && _openSlot[hand] != _target) EndHand(hand);
                _open[hand] = true;
                _openSlot[hand] = _target;
                contactCalls++;
                // the coat point paints; the tracked (or debug) hand position measures the rub speed (a hand held still on a moving
                // leg does not lather)
                bool lather = washes[_target]._HandContact(hand, ci, point, pos, elapsed);
                if (lather && !debug) Haptic(lp, hand, now);
            }
        }

        // The nearest part collider of the target dog touching the hand sphere; -1 = none. Part colliders are trigger capsules
        // (KH_DogWashSetupEditor.Dog.cs), so ClosestPoint is valid on them. The point goes to _nearPoint (no out on U# methods).
        int Nearest(int hand, Vector3 p)
        {
            Vector3 point = p;
            int n = Physics.OverlapSphereNonAlloc(p, handContactRadius, _overlap, washLayer, QueryTriggerInteraction.Collide);
            if (n > _overlap.Length) n = _overlap.Length;
            int bestCi = -1;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int ci = Lookup(hand, _overlap[i]);
                if (ci < 0) continue;
                Vector3 cp = _overlap[i].ClosestPoint(p);
                float dd = (cp - p).sqrMagnitude;
                if (dd < best) { best = dd; bestCi = ci; point = cp; }
            }
            _nearPoint = point;
            return bestCi;
        }

        void EndHand(int hand)
        {
            if (!_open[hand]) return;
            _open[hand] = false;
            int s = _openSlot[hand];
            if (washes != null && s >= 0 && s < washes.Length && washes[s] != null) washes[s]._EndHand(hand);
        }

        void EndHands()
        {
            EndHand(0);
            EndHand(1);
        }

        void Haptic(VRCPlayerApi lp, int hand, float now)
        {
            if (hapticInterval <= 0f || now - _lastHaptic[hand] < hapticInterval) return;
            _lastHaptic[hand] = now;
            lp.PlayHapticEventInHand(hand == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right,
                hapticDuration, hapticAmplitude, hapticFrequency);
        }
    }
}
