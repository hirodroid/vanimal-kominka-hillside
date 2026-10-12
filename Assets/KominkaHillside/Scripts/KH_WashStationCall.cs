using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    /// <summary>IMP-DOG-035: one wash station's call (docs/plans/2026-10-01-wash-call-and-height.md §3 / §4). Which dog stays on the
    /// station, who called it, whether it has arrived, and the stand's height. Manual sync; the Owner (left as it is: the instance
    /// master, never taken on a request) arbitrates every request by the rules of §3.
    ///
    /// Authority split: this object's Owner decides and syncs heldDog / callerId / lift / arrived. The dog's root Owner (usually the
    /// same player, not guaranteed) only reads them and moves the dog (KD_DogWanderController.HillsideWash.cs); it reports the arrival
    /// back through _DogArrived (direct when it also owns this object, else ReqArrived, which checks the sender owns that dog's root).
    /// Nobody takes ownership of anything here.
    ///
    /// Release (Owner): 「おわり」 (ReqRelease, anyone), another dog called, the dog's own gathering / sleep call / travel ticket (the dog
    /// calls _ReleaseDog on its accept edge - 2026-10-07 audit RC-1: and every tick while the errand lasts - on every client; only
    /// the Owner's call acts), the caller leaving, the dog leashed by
    /// someone other than the caller, and idleReleaseSeconds since the last stroke (or the call) while the caller is farther than
    /// callerFarMeters from the anchor. The stand goes back to 0 whenever no dog is held (the synced lift; IMP-DOG-044 2026-10-11:
    /// the stand itself rests at minLift, > 0 only while a platform that would sink into the ground is selected here).
    /// 2026-10-02: on that release edge the dog side (every client, from the synced heldDog) drops the wash hold and walks out past
    /// keepOutRadius at once, riding a raised stand down first; not when an errand or a leash took it, or another station holds it
    /// (KD_DogWanderController.HillsideStationLeave.cs). Nothing new is synced here.
    ///
    /// Busy dogs (Fetch / the disc / in the water / a gathering participant / called to sleep): the call waits callWaitSeconds for
    /// the dog to be free, then is refused (refuseSerial / refusedDog / refusedReason for the board's 「いまは来られない」).
    ///
    /// IMP-DOG-037 (docs/plans/2026-10-01-wash-call-and-height.md §7): one rule, _Availability(dog, player), decides both what the
    /// board shows for each dog and what the Owner accepts - the same function on the same synced inputs (the leash walkers, every
    /// station's heldDog, the Fetch / disc / gathering / sleep-call states, the wash hold, the dog root synced by VRCObjectSync).
    /// Refused at once: leashed by someone else, held by another station, farther than callRange (horizontal, from the anchor).
    /// 2026-10-07 audit RC-1: also the caller's own travel ticket (RefuseTraveling); RC-8 (a): another dog on this station washed
    /// by someone else is waited for like the called dog's own (AvWashedByOther).
    /// Waited for (callWaitSeconds, then refused as busy): Fetch, the disc, a gathering, the sleep call, washed by someone else.
    /// callRange stays below the dog side's StationFar (30 m, Wash/12), so an accepted call never needs the "appear near the
    /// station" teleport and a far dog cannot be pulled in through it. Nothing new is synced.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_WashStationCall : UdonSharpBehaviour
    {
        [Header("配線 (ビルダー)")]
        public int stationIndex;             // 0 = main station, 1.. = along the walk
        public Transform anchor;             // where the dog stands: the top centre of the stand (a child of liftRoot)
        public Transform liftRoot;           // the stand (slats + collider); its local y follows lift
        public float standYaw;               // the dog's facing (parallel to the shelf), world degrees
        public KominkaDogRun.KD_DogWanderController[] dogs;   // all six, by dog index
        public KH_DogWash[] washes;                           // all six, by dog index

        [Header("調整値 (試作値)")]
        public float liftMax = 0.9f;
        public float liftStep = 0.1f;
        public float idleReleaseSeconds = 120f;
        public float callerFarMeters = 10f;
        public float liftSpeed = 0.5f;       // m/s, every client eases liftRoot toward the synced lift
        public float callWaitSeconds = 5f;   // a busy dog has this long to become free
        public float operateRange = 6f;      // Codex 5: release / lift only from a player this close to the anchor (Owner-checked)
        public float releaseInterval = 1f;   // Codex 5: Owner-side spacing of releases
        public float liftInterval = 0.2f;    // Codex 5: Owner-side spacing of lift steps
        // 2026-10-02 user request 「洗い場には他の犬は勝手に近寄ってこない、台の上で休憩し始めたりもしない」: the horizontal circle
        // around the anchor (the duckboard's centre) a free dog never picks as a destination / rest place and walks out of; the dog
        // released from here walks to keepOutRadius + its stationLeaveMargin (KD_DogWanderController.HillsideStationLeave.cs).
        // 4 m covers the duckboard, the shelf (1.2 m behind) and the 5 x 5 m personal floor (corners 3.54 m) with 0.46 m to spare.
        // Read once by each dog (static: the anchor moves on y only). Written by Wash/11, checked by Wash/12. 0 = no keep-out.
        public float keepOutRadius = 4f;
        // IMP-DOG-037: only dogs within this horizontal distance of the anchor can be called (m). Per station, written by Wash/11
        // (KH_DogWashSetupEditor.CallRanges), checked by Wash/12 (keepOutRadius < callRange < the dog side's StationFar, no two
        // stations' ranges overlap). 0 or less = no limit (the behaviour before IMP-DOG-037).
        public float callRange = 25f;

        // Refusal reasons (synced refusedReason) and, with the Av* values, what _Availability returns. One list for both, so the
        // board and the Owner name the same reason.
        public const int RefuseBusy = 1, RefuseLeashedByOther = 2, RefuseInvalid = 3, RefuseTooFar = 4, RefuseOtherStation = 5;
        public const int AvCallable = 0;
        // busy: the call waits callWaitSeconds (AvFetch .. AvWashedByOther; a refusal after the wait is RefuseBusy)
        public const int AvFetch = 6, AvFrisbee = 7, AvGathering = 8, AvSleepCall = 9, AvWashedByOther = 10;
        public const int AvHeldHere = 11;   // this station holds it (on its way or on the stand)
        // 2026-10-07 audit RC-1: the caller's own travel ticket is out (refused at once; the board has no word for it yet)
        public const int RefuseTraveling = 12;
        const int TravelTicketLifeMs = 15000;   // = KD_DogWanderController.HillsideTravel.cs TravelTicketLifeMs (the dog side's rule)

        [UdonSynced] private int heldDog = -1;
        [UdonSynced] private int callerId = -1;
        [UdonSynced] private float lift;
        [UdonSynced] private bool arrived;
        [UdonSynced] private int refuseSerial;
        [UdonSynced] private int refusedDog = -1;
        [UdonSynced] private int refusedReason;
        // Codex 2 / 4: the hold's generation = the server time (ms) it was accepted. Two stations may have different Owners, and
        // _ReleaseDog on a station this client does not own does nothing, so a dog could stay held at two stations. Rule instead of
        // a shared Owner: when two stations hold the same dog, the newer hold (heldMs, ties: lower stationIndex) wins - the dog side
        // follows the newest (KD_DogWanderController.HeldStation), and the older station's own Owner lets the dog go as soon as it
        // sees the newer hold synced (OwnerTick). Chosen over moving every station to one Owner: no ownership is taken (design
        // §4), it heals itself after an Owner change / a late sync, and it needs no Program Asset or new manager. The generation
        // also tags the arrival report (ReqArrived), so an old report never marks a later call of the same dog as arrived.
        [UdonSynced] private int heldMs;

        // Owner-local
        private int _pendingDog = -1, _pendingCaller = -1;
        private float _pendingUntil;
        private float _heldAt;               // when this client (as Owner) first saw the current hold
        private int _heldSeen = -1;
        private float _nextCheck;
        private float _nextReleaseAt, _nextLiftAt;
        // Every client: the arrival report sent for this generation (Codex 4: once, then at most once a second)
        private int _arrivedSentGen;
        private float _arrivedSentAt = -1000f;
        // IMP-DOG-044 (2026-10-11, the main station's slope): the lowest height this stand may rest at, for the platform selected here.
        // Not synced: every client derives it from the synced KH_WashSelection.selectedBath (KH_WashSelection._Apply writes
        // bathMinLifts[selectedBath], or 0 with no platform / no table). The stand stands at max(synced lift with a dog / 0 without,
        // minLift) - with a dog or without one: a premium platform whose bottom would be under the ground is raised clear of it
        // (WashPremium/50 measures it per station and platform; 0 where the ground under it is not higher than its bottom). The synced
        // lift and the lift buttons are unchanged (the stand simply stays at minLift while lift is below it).
        [System.NonSerialized] public float minLift;
        // Every client
        private float _liftNow;
        private Vector3 _liftBase;
        private bool _liftBaseOk;

        // ---- IMP-ENV-013 (2026-10-12): the stand's size per dog (docs/plans/2026-10-01-wash-call-and-height.md §8) ----
        // Not synced: every client derives the size from the synced heldDog (nothing new on the wire). Written by Wash/22 (measured from
        // the six dogs' paw colliders, per station), checked by Wash/23. A size is (length along Lift X, width along Lift Z, the centre's
        // offset along Lift X), m. Only standTable's localScale x / z and localPosition x change: never its y, the anchor or the lift.
        [Header("台の大きさ (IMP-ENV-013、Wash/22)")]
        public Transform standTable;         // Lift/WashTable (its BoxCollider and every child scale with it)
        public Vector3[] standSize;          // by dog index; declared null, Wash/22 allocates and wires it (vrc-udonsharp §4)
        public Vector3 standIdle;            // no dog: the smallest rectangle holding all six dogs' stands (Q4: the largest dog's size)
        public Vector3 standBaseSize;        // the table's own collider size at scale 1 (1.3 x 0.03 x 0.7)
        public Vector3 standBasePos;         // standTable's localPosition / localScale in the scene (Edit mode keeps them)
        public Vector3 standBaseScale;
        public float standSpeed = 1.5f;      // m/s, every client eases the (length, width, offset) toward the target
        // The selected platform keeps the table at its own size (IMP-DOG-044 pool: 1.3 x 0.7, never scaled). Not synced: every client
        // takes it from the synced KH_WashSelection.selectedBath (KH_WashSelection._Apply writes _KeepsSize(selectedBath) here).
        [System.NonSerialized] public bool standKeep;
        private bool _standOk, _standMoving, _standSynced, _standSnapNext;
        private int _standKey = -3;          // -2 the platform keeps the size, -1 no dog, 0.. the held dog
        private Vector3 _standNow, _standWant;

        void Start()
        {
            if (liftRoot != null) { _liftBase = liftRoot.localPosition; _liftBaseOk = true; }
            // IMP-ENV-013: at once to the current target (whoever joins sees no shrinking stand); unwired / zero sizes = left as it is
            _standOk = standTable != null && standBaseSize.x > 0f && standBaseSize.z > 0f;
            if (_standOk)
            {
                _standKey = StandKey();
                _standWant = _StandSize(); _standNow = _standWant; _standMoving = false;
                _StandPose(_standNow);
            }
        }

        // IMP-ENV-013: the first state received (a late joiner, after Start) snaps the stand to the held dog's size instead of easing
        public override void OnDeserialization()
        {
            if (_standSynced) return;
            _standSynced = true; _standSnapNext = true;
        }

        // Codex 3: a station switched off lets its dog go (the dog side also ignores inactive stations, so it is free at once even
        // if this serialization does not leave a disabled object), and switching it back on clears whatever state it still holds.
        void OnDisable()
        {
            if (Networking.IsOwner(gameObject)) Release();
            _pendingDog = -1;
        }

        void OnEnable()
        {
            if (Networking.IsOwner(gameObject) && heldDog >= 0) Release();
        }

        // ================= Reads (dog side, board) =================

        public int _HeldDog() { return heldDog; }
        public bool _Arrived() { return heldDog >= 0 && arrived; }
        public Vector3 _AnchorPosition() { return anchor != null ? anchor.position : transform.position; }
        public float _StandYaw() { return standYaw; }
        public float _Lift01() { return liftMax > 0f ? Mathf.Clamp01(lift / liftMax) : 0f; }
        public float _LiftMeters() { return lift; }
        /// <summary>This client's eased stand height (m above the base; _LiftRest() when down). The dog released from a raised stand
        /// rides it down before it walks off (KD_DogWanderController.UpdateLeaveLowering).</summary>
        public float _LiftNow() { return _liftNow; }
        /// <summary>IMP-DOG-044: the height the stand rests at with no dog / the lift at 0 (m): minLift of the selected platform, 0 at
        /// most stations. The released dog's ride down ends here, not at 0.</summary>
        public float _LiftRest() { return Mathf.Clamp(minLift, 0f, Mathf.Max(0f, liftMax)); }
        /// <summary>IMP-DOG-044: the height every client eases the stand toward: the synced lift while a dog is held (0 without), never
        /// below _LiftRest(). Pure (WashPremium/44 runs it in Edit mode).</summary>
        public float _LiftTarget() { return Mathf.Max(heldDog >= 0 ? lift : 0f, _LiftRest()); }
        public int _CallerId() { return heldDog >= 0 ? callerId : -1; }
        public int _RefuseSerial() { return refuseSerial; }
        public int _RefusedDog() { return refusedDog; }
        public int _RefusedReason() { return refusedReason; }
        public int _PendingDog() { return _pendingDog; }   // Owner only meaningful (the board shows "呼んでいます" locally)
        public int _HeldMs() { return heldMs; }            // the hold's generation (newer wins between stations)

        /// <summary>True when this station's hold of `dog` loses to another active station's hold of the same dog (newer heldMs,
        /// ties: lower stationIndex). Every client reaches the same answer from the synced values.</summary>
        public bool _Superseded(int dog)
        {
            if (dog < 0 || dog != heldDog || dogs == null || dog >= dogs.Length || dogs[dog] == null) return false;
            KH_WashStationCall[] all = dogs[dog].washCalls;
            if (all == null) return false;
            for (int i = 0; i < all.Length; i++)
            {
                KH_WashStationCall o = all[i];
                if (o == null || o == this || !o.gameObject.activeInHierarchy || o._HeldDog() != dog) continue;
                int d = o._HeldMs() - heldMs;
                if (d > 0 || (d == 0 && o.stationIndex < stationIndex)) return true;
            }
            return false;
        }

        /// <summary>IMP-DOG-037: can player `pid` call `dog` to this station now, and if not, why. The board reads it for every dog
        /// (local player) and the Owner arbitrates with it (the caller), so the two never disagree on the rule; they can differ
        /// only by what each client has received so far (the Owner's answer is the one that counts, and a refusal names it).
        /// Order (the first match wins): unwired > leashed by someone else > held here > held by another station > the caller's
        /// travel ticket (2026-10-07 audit RC-1) > too far > Fetch > the disc > gathering > the sleep call > washed by someone
        /// else > another dog on this station washed by someone else (2026-10-07 audit RC-8 (a)) > callable. Leashed is checked before "held
        /// here" as Arbitrate always did (Codex 1: a re-call cannot bypass it). Every input is synced or the synced dog root.</summary>
        public int _Availability(int dog, int pid)
        {
            if (!DogValid(dog)) return RefuseInvalid;
            KominkaDogRun.KD_DogWanderController w = dogs[dog];
            KH_LeashManager m = w.hillsideLeashes;
            bool leashed = m != null && m._IsLeashed(dog);
            if (leashed && m.walkers[dog] != pid) return RefuseLeashedByOther;
            if (dog == heldDog) return AvHeldHere;
            if (HeldElsewhere(dog)) return RefuseOtherStation;
            // 2026-10-07 audit RC-1: the caller is on the way somewhere else (FUNC-03/BUG-06, codex-03/BUG-05, FUNC-07/BUG-01)
            if (CallerTraveling(m, pid)) return RefuseTraveling;
            if (callRange > 0f)
            {
                Vector3 d = w.dogRoot.position - _AnchorPosition();
                d.y = 0f;
                if (d.sqrMagnitude > callRange * callRange) return RefuseTooFar;
            }
            KominkaDogRun.KD_DogFetchController f = w.fetchController;
            if (f != null && !f.IsIdle) return AvFetch;
            if (w.frisbeeCatch != null && !w.frisbeeCatch.IsIdle) return AvFrisbee;
            if (w.activities != null && w.activities.Mode == 1 && w.activities._IsParticipant(dog)) return AvGathering;
            if (w.sleepBoard != null && w.sleepBoard.LiveSleeperOf(dog) >= 0) return AvSleepCall;
            // Somebody else is washing it with a tool right now (the synced hold, KH_DogWash.holdSeconds after their last stroke).
            // The caller's own strokes and the caller's own leashed dog never count. The washer is the synced holder of that hold
            // (KH_DogWash.washBy, written with washMs), never the local "last stroke seen here" (review of 8faf572: a late joiner
            // and a new Owner did not have it, so the board and the acceptance could disagree).
            KH_DogWash wash = washes != null && dog < washes.Length ? washes[dog] : null;
            if (wash != null && !leashed && wash._WashHold())
            {
                int washer = wash._WashHolderId();
                if (washer >= 0 && washer != pid) return AvWashedByOther;
            }
            // 2026-10-07 audit RC-8 (a): another dog on this station is being washed by somebody else (FUNC-03/BUG-01) - calling
            // this one would let that dog go under the washer's hands (Accept: one dog per station). Waited for as the dog's own
            // "washed by someone else" (same reason and wait; the hold runs out holdSeconds after their last stroke). The held
            // dog leashed by the caller is the caller's to swap, as for `dog` above.
            if (StationWashedByOther(pid)) return AvWashedByOther;
            return AvCallable;
        }

        /// <summary>2026-10-07 audit RC-8 (a): this station holds another dog whose synced wash hold belongs to someone other than
        /// `pid`. Synced inputs only (heldDog, the wash hold / its holder, the leash walkers).</summary>
        bool StationWashedByOther(int pid)
        {
            int h = heldDog;
            if (h < 0 || washes == null || h >= washes.Length || washes[h] == null || !DogValid(h)) return false;
            KH_LeashManager hm = dogs[h].hillsideLeashes;
            if (hm != null && hm._IsLeashed(h) && hm.walkers[h] == pid) return false;
            KH_DogWash hw = washes[h];
            if (!hw._WashHold()) return false;
            int washer = hw._WashHolderId();
            return washer >= 0 && washer != pid;
        }

        /// <summary>2026-10-07 audit RC-1: player `pid` holds a travel ticket now - the same rule as the dog side's
        /// TravelPendingRead (Phase 1 ready or 4 moved, the ticket's holder still walks that dog, within the ticket's life), over
        /// all dogs. Reads the manager's synced arrays only (KH_LeashManager has no per-player read).</summary>
        bool CallerTraveling(KH_LeashManager m, int pid)
        {
            if (m == null || pid < 0 || m.travelPhase == null || m.travelHolder == null || m.travelAtMs == null || m.walkers == null) return false;
            int n = m.travelPhase.Length;
            if (m.travelHolder.Length < n) n = m.travelHolder.Length;
            if (m.travelAtMs.Length < n) n = m.travelAtMs.Length;
            if (m.walkers.Length < n) n = m.walkers.Length;
            int now = Networking.GetServerTimeInMilliseconds();
            for (int i = 0; i < n; i++)
            {
                int ph = m.travelPhase[i];
                if (ph != KH_LeashManager.TravelPhaseReady && ph != KH_LeashManager.TravelPhaseMoved) continue;
                if (m.travelHolder[i] != pid || m.walkers[i] != pid) continue;
                int age = now - m.travelAtMs[i];
                if (age >= -2000 && age <= TravelTicketLifeMs) return true;
            }
            return false;
        }

        /// <summary>Another active station holds `dog` (its synced heldDog).</summary>
        bool HeldElsewhere(int dog)
        {
            KH_WashStationCall[] all = dogs[dog].washCalls;
            if (all == null) return false;
            for (int i = 0; i < all.Length; i++)
            {
                KH_WashStationCall o = all[i];
                if (o != null && o != this && o.gameObject.activeInHierarchy && o._HeldDog() == dog) return true;
            }
            return false;
        }

        // ================= Local entries (board buttons) =================

        public void _Call(int dog)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (Networking.IsOwner(gameObject)) Arbitrate(dog, lp.playerId);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqCall), dog);
        }

        public void _Release()
        {
            if (Networking.IsOwner(gameObject)) OperatorRelease(Networking.LocalPlayer);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqRelease));
        }

        public void _Lift(int steps)
        {
            if (Networking.IsOwner(gameObject)) OperatorLift(Networking.LocalPlayer, steps);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqLift), steps);
        }

        /// <summary>The dog side, on the accept edge of its gathering / sleep call / travel ticket (every client). Only the Owner's
        /// call changes anything; the other clients' calls are no-ops (the Owner's client sees the same synced edge).</summary>
        public void _ReleaseDog(int dog)
        {
            if (!Networking.IsOwner(gameObject) || dog < 0 || dog != heldDog) return;
            Release();
        }

        /// <summary>The dog's root Owner: the dog stands on the anchor.</summary>
        public void _DogArrived(int dog)
        {
            if (dog < 0 || dog != heldDog || arrived) return;
            if (Networking.IsOwner(gameObject)) { arrived = true; RequestSerialization(); return; }
            // Codex 4: once per generation, re-sent at most once a second until `arrived` comes back synced
            if (_arrivedSentGen == heldMs && Time.time - _arrivedSentAt < 1f) return;
            _arrivedSentGen = heldMs; _arrivedSentAt = Time.time;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqArrived), dog, heldMs);
        }

        // ================= Network requests (Owner arbitrates; every argument is checked) =================

        // net: network-callable; anyone may call. The dog index is range-checked, the caller comes from CallingPlayer, the rules
        // (§3) are applied by the Owner here.
        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqCall(int dog)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            Arbitrate(dog, caller.playerId);
        }

        // net: network-callable; anyone may release (design §4: so a stuck dog can always be sent back), but only from within
        // operateRange of the station and at most once per releaseInterval (Codex 5: no remote spamming)
        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqRelease()
        {
            if (!Networking.IsOwner(gameObject)) return;
            OperatorRelease(NetworkCalling.CallingPlayer);
        }

        // net: network-callable; steps is clamped to +-1 (one tick per press), only while a dog is held, only from within
        // operateRange, at most once per liftInterval (Codex 5)
        [NetworkCallable(maxEventsPerSecond: 8)]
        public void ReqLift(int steps)
        {
            if (!Networking.IsOwner(gameObject)) return;
            OperatorLift(NetworkCalling.CallingPlayer, steps);
        }

        bool NearStation(VRCPlayerApi p)
        {
            if (!Utilities.IsValid(p)) return false;
            Vector3 d = p.GetPosition() - _AnchorPosition();
            return d.sqrMagnitude <= operateRange * operateRange;
        }

        void OperatorRelease(VRCPlayerApi p)
        {
            if (!NearStation(p) || Time.time < _nextReleaseAt) return;
            _nextReleaseAt = Time.time + Mathf.Max(0f, releaseInterval);
            Release();
        }

        void OperatorLift(VRCPlayerApi p, int steps)
        {
            if (!NearStation(p) || Time.time < _nextLiftAt) return;
            _nextLiftAt = Time.time + Mathf.Max(0f, liftInterval);
            ApplyLift(steps);
        }

        // net: network-callable; only the Owner of that dog's root may report its arrival, and only for the current generation
        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqArrived(int dog, int gen)
        {
            if (!Networking.IsOwner(gameObject) || dog < 0 || dog != heldDog || arrived || gen != heldMs) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller) || !DogValid(dog)) return;
            VRCPlayerApi owner = Networking.GetOwner(dogs[dog].dogRoot.gameObject);
            if (!Utilities.IsValid(owner) || owner.playerId != caller.playerId) return;
            arrived = true;
            RequestSerialization();
        }

        // ================= Owner =================

        bool DogValid(int dog)
        {
            return dogs != null && dog >= 0 && dog < dogs.Length && dogs[dog] != null && dogs[dog].dogRoot != null;
        }

        /// <summary>Owner: the rules of §3 / §7 for a call of `dog` by player `pid` (_Availability).</summary>
        void Arbitrate(int dog, int pid)
        {
            int a = _Availability(dog, pid);
            if (a == RefuseInvalid) { Refuse(dog, RefuseInvalid); return; }
            // Codex 6: a new choice cancels an older wait (only the latest selection is processed)
            _pendingDog = -1;
            // Somebody else's leashed dog cannot be called (never bind another player's dog; the caller's own leashed dog can),
            // nor a dog another station holds, nor one farther than callRange (IMP-DOG-037: refused here, before anything moves -
            // the dog side's far-call teleport is never reached for it)
            // 2026-10-07 audit RC-1: and not while the caller's own travel ticket is out (refused at once, nothing is held)
            if (a == RefuseLeashedByOther || a == RefuseOtherStation || a == RefuseTooFar || a == RefuseTraveling) { Refuse(dog, a); return; }
            // Calling the held dog again: it stays, and the caller does not change (Codex 1: the caller's leave / distance rules
            // keep following the player who called it)
            if (a == AvHeldHere) return;
            // Fetch / the disc (their water retrieves included) / a gathering participant / called to sleep / washed by someone
            // else: comes once that is over. 2026-10-01 user decision: the water play itself is not busy - the call is accepted and
            // the dog walks out by the play's own exit (KD_DogWanderController.WaterRequester), then comes.
            if (a >= AvFetch && a <= AvWashedByOther)
            {
                _pendingDog = dog; _pendingCaller = pid; _pendingUntil = Time.time + Mathf.Max(0.5f, callWaitSeconds);
                return;
            }
            Accept(dog, pid);
        }

        void Accept(int dog, int pid)
        {
            _pendingDog = -1;
            // One dog per station: the previous dog is simply let go (its side sees heldDog change and walks off)
            heldDog = dog; callerId = pid; arrived = false;
            heldMs = Networking.GetServerTimeInMilliseconds();
            _heldAt = Time.time; _heldSeen = dog;
            // A dog held at another station is let go there (one station per dog; effective when this client owns that one too)
            KH_WashStationCall[] all = dogs[dog].washCalls;
            if (all != null) for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i] != this) all[i]._ReleaseDog(dog);
            Debug.Log("[KH_WashStationCall] station " + stationIndex + " call dog=" + dog + " by " + pid);
            RequestSerialization();
        }

        void Refuse(int dog, int reason)
        {
            refusedDog = dog; refusedReason = reason; refuseSerial++;
            Debug.Log("[KH_WashStationCall] station " + stationIndex + " refuse dog=" + dog + " reason=" + reason);
            RequestSerialization();
        }

        void Release()
        {
            _pendingDog = -1;
            if (heldDog < 0 && lift == 0f) return;
            Debug.Log("[KH_WashStationCall] station " + stationIndex + " release dog=" + heldDog);
            heldDog = -1; callerId = -1; arrived = false; lift = 0f; _heldSeen = -1;
            RequestSerialization();
        }

        void ApplyLift(int steps)
        {
            if (heldDog < 0) return;   // "犬がいない時は 0"
            steps = steps > 0 ? 1 : (steps < 0 ? -1 : 0);
            if (steps == 0) return;
            float step = liftStep > 0.001f ? liftStep : 0.1f;
            float next = Mathf.Clamp(Mathf.Round(lift / step + steps) * step, 0f, Mathf.Max(0f, liftMax));
            if (Mathf.Abs(next - lift) < 0.0001f) return;
            lift = next;
            RequestSerialization();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(player)) return;
            if (heldDog >= 0 && player.playerId == callerId) Release();
            if (player.playerId == _pendingCaller) _pendingDog = -1;
        }

        void OwnerTick()
        {
            // The pending call: comes as soon as the dog is free, refused after callWaitSeconds. IMP-DOG-037: the whole rule again at
            // that moment (a dog that finished its Fetch out of range, leashed meanwhile, or taken by another station is refused
            // with that reason, never pulled in)
            if (_pendingDog >= 0)
            {
                int d = _pendingDog;
                if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(_pendingCaller))) _pendingDog = -1;
                else
                {
                    int a = _Availability(d, _pendingCaller);
                    if (a == AvCallable) Accept(d, _pendingCaller);
                    else if (a >= AvFetch && a <= AvWashedByOther) { if (Time.time > _pendingUntil) { _pendingDog = -1; Refuse(d, RefuseBusy); } }
                    else if (a == AvHeldHere) _pendingDog = -1;
                    else { _pendingDog = -1; Refuse(d, a); }
                }
            }
            if (heldDog < 0) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.5f;
            if (!DogValid(heldDog)) { Release(); return; }
            // Codex 2: a newer hold of the same dog at another station wins; this one lets go
            if (_Superseded(heldDog)) { Release(); return; }
            // A hold this client inherited (Owner change, or synced before it became Owner) counts from now
            if (_heldSeen != heldDog) { _heldSeen = heldDog; _heldAt = Time.time; }
            VRCPlayerApi caller = VRCPlayerApi.GetPlayerById(callerId);
            if (!Utilities.IsValid(caller)) { Release(); return; }
            KH_LeashManager lm = dogs[heldDog].hillsideLeashes;
            if (lm != null && lm._IsLeashed(heldDog) && lm.walkers[heldDog] != callerId) { Release(); return; }
            // 120 s since the last stroke (or the call) while the caller is more than 10 m away
            float last = _heldAt;
            KH_DogWash w = washes != null && heldDog < washes.Length ? washes[heldDog] : null;
            if (w != null)
            {
                int washer = w._WasherPlayerId();
                if (washer >= 0)
                {
                    float age = w._StrokeAgeOf(washer);
                    if (age < 1e8f) last = Mathf.Max(last, Time.time - age);
                }
            }
            if (Time.time - last > idleReleaseSeconds)
            {
                Vector3 d = caller.GetPosition() - _AnchorPosition(); d.y = 0f;
                if (d.sqrMagnitude > callerFarMeters * callerFarMeters) Release();
            }
        }

        void Update()
        {
            // IMP-DOG-036 S5: OwnerTick does nothing with no pending call and no held dog (its first two branches), so those own
            // fields are tested before IsOwner (which ran every frame on every client). Same result.
            if ((_pendingDog >= 0 || heldDog >= 0) && Networking.IsOwner(gameObject)) OwnerTick();
            // IMP-ENV-013: the size before the height's early returns below (a settled height must not freeze the size)
            if (_standOk) StandStep();
            // Every client: ease the stand toward the synced height (0 with no dog), never below the platform's minLift (IMP-DOG-044).
            // Recomputed every frame, so a change of minLift (the platform switched) wakes the settled stand up as a lift change does.
            if (liftRoot == null || !_liftBaseOk) return;
            // = _LiftTarget() with no call / extern on the usual frame (minLift 0); lift <= liftMax, so the clamp never goes below it
            float want = heldDog >= 0 ? lift : 0f;
            if (minLift > want) want = _LiftRest();
            if (_liftNow == want) return;
            _liftNow = Mathf.MoveTowards(_liftNow, want, Mathf.Max(0.05f, liftSpeed) * Time.deltaTime);
            liftRoot.localPosition = _liftBase + Vector3.up * _liftNow;
        }

        // ================= IMP-ENV-013: the stand's size =================

        int StandKey() { return standKeep ? -2 : heldDog; }

        /// <summary>IMP-ENV-013: the stand's target (length along Lift X, width along Lift Z, centre offset along Lift X; m) on this
        /// client: the base size while the selected platform keeps it (the pool) or nothing is wired, the held dog's size (Q3: from the
        /// accepted call, not the arrival), standIdle with no dog. Pure (Wash/23 runs it in Edit mode).</summary>
        public Vector3 _StandSize()
        {
            Vector3 b = new Vector3(standBaseSize.x, standBaseSize.z, 0f);
            if (standKeep || standTable == null) return b;
            Vector3 s = standIdle;
            int d = heldDog;
            if (standSize != null && d >= 0 && d < standSize.Length) s = standSize[d];
            if (s.x <= 0f || s.y <= 0f) return b;
            return s;
        }

        /// <summary>IMP-ENV-013: puts standTable at size s (see _StandSize): x / z scale by the ratio to standBaseSize, x position by the
        /// offset. y (height, thickness) is never touched. Wash/23 runs it in Edit mode and puts the table back.</summary>
        public void _StandPose(Vector3 s)
        {
            if (standTable == null || standBaseSize.x <= 0f || standBaseSize.z <= 0f) return;
            standTable.localScale = new Vector3(standBaseScale.x * s.x / standBaseSize.x, standBaseScale.y, standBaseScale.z * s.y / standBaseSize.z);
            standTable.localPosition = new Vector3(standBasePos.x + s.z, standBasePos.y, standBasePos.z);
        }

        /// <summary>IMP-ENV-013 (Codex review): the target, the current size and the transform at once, now. KH_WashSelection._Apply calls
        /// it when a platform that keeps the size (the pool) is selected, before that platform's model is shown, so a squeezed pool is
        /// never drawn (whatever the order of the two Updates). Before this Start nothing happens (Start snaps to the same target).</summary>
        public void _StandSnapNow()
        {
            if (!_standOk) return;
            _standKey = StandKey();
            _standWant = _StandSize(); _standNow = _standWant; _standMoving = false;
            _StandPose(_standNow);
        }

        /// <summary>Every client, every frame (Update): a new key (the held dog / no dog / the platform keeping the size) takes a new
        /// target, eased at standSpeed; the first received state and a platform that keeps the size snap at once (no squeezed pool, no
        /// shrinking stand for a late joiner). Idle frames cost the key compare only.</summary>
        void StandStep()
        {
            int key = StandKey();
            if (key != _standKey)
            {
                _standKey = key;
                _standWant = _StandSize();
                if (_standSnapNext || key == -2)
                {
                    _standSnapNext = false; _standMoving = false; _standNow = _standWant;
                    _StandPose(_standNow);
                    return;
                }
                _standMoving = true;
            }
            _standSnapNext = false;
            if (!_standMoving) return;
            _standNow = Vector3.MoveTowards(_standNow, _standWant, Mathf.Max(0.05f, standSpeed) * Time.deltaTime);
            _StandPose(_standNow);
            if ((_standNow - _standWant).sqrMagnitude < 1e-12f) _standMoving = false;   // MoveTowards lands on the target exactly
        }
    }
}
