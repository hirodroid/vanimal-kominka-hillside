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
    /// calls _ReleaseDog on its accept edge, on every client; only the Owner's call acts), the caller leaving, the dog leashed by
    /// someone other than the caller, and idleReleaseSeconds since the last stroke (or the call) while the caller is farther than
    /// callerFarMeters from the anchor. The stand goes back to 0 whenever no dog is held.
    ///
    /// Busy dogs (Fetch / the disc / in the water / a gathering participant / called to sleep): the call waits callWaitSeconds for
    /// the dog to be free, then is refused (refuseSerial / refusedDog / refusedReason for the board's 「いまは来られない」).</summary>
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

        public const int RefuseBusy = 1, RefuseLeashedByOther = 2, RefuseInvalid = 3;

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
        // Every client
        private float _liftNow;
        private Vector3 _liftBase;
        private bool _liftBaseOk;

        void Start()
        {
            if (liftRoot != null) { _liftBase = liftRoot.localPosition; _liftBaseOk = true; }
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

        /// <summary>Owner: the rules of §3 for a call of `dog` by player `pid`.</summary>
        void Arbitrate(int dog, int pid)
        {
            if (!DogValid(dog)) { Refuse(dog, RefuseInvalid); return; }
            // Codex 6: a new choice cancels an older wait (only the latest selection is processed)
            _pendingDog = -1;
            // Somebody else's leashed dog cannot be called (never bind another player's dog); the caller's own leashed dog can.
            // Codex 1: checked before the "already held" return, so a re-call cannot bypass it
            KH_LeashManager m = dogs[dog].hillsideLeashes;
            if (m != null && m._IsLeashed(dog) && m.walkers[dog] != pid) { Refuse(dog, RefuseLeashedByOther); return; }
            // Calling the held dog again: it stays, and the caller does not change (Codex 1: the caller's leave / distance rules
            // keep following the player who called it)
            if (dog == heldDog) return;
            if (DogBusy(dog))
            {
                _pendingDog = dog; _pendingCaller = pid; _pendingUntil = Time.time + Mathf.Max(0.5f, callWaitSeconds);
                return;
            }
            Accept(dog, pid);
        }

        /// <summary>Fetch / the disc (their water retrieves included: Fetch is not Idle, the disc not Idle) / a gathering participant /
        /// called to sleep: comes once that is over. 2026-10-01 user decision: the water play itself is not busy - the call is
        /// accepted and the dog walks out by the play's own exit (KD_DogWanderController.WaterRequester), then comes.</summary>
        bool DogBusy(int dog)
        {
            KominkaDogRun.KD_DogWanderController w = dogs[dog];
            KominkaDogRun.KD_DogFetchController f = w.fetchController;
            if (f != null && !f.IsIdle) return true;
            if (w.frisbeeCatch != null && !w.frisbeeCatch.IsIdle) return true;
            if (w.activities != null && w.activities.Mode == 1 && w.activities._IsParticipant(dog)) return true;
            if (w.sleepBoard != null && w.sleepBoard.LiveSleeperOf(dog) >= 0) return true;
            return false;
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
            // The pending call: comes as soon as the dog is free, refused after callWaitSeconds
            if (_pendingDog >= 0)
            {
                int d = _pendingDog;
                if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(_pendingCaller))) _pendingDog = -1;
                else if (!DogBusy(d))
                {
                    KH_LeashManager m = dogs[d].hillsideLeashes;
                    if (m != null && m._IsLeashed(d) && m.walkers[d] != _pendingCaller) { _pendingDog = -1; Refuse(d, RefuseLeashedByOther); }
                    else Accept(d, _pendingCaller);
                }
                else if (Time.time > _pendingUntil) { _pendingDog = -1; Refuse(d, RefuseBusy); }
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
            if (Networking.IsOwner(gameObject)) OwnerTick();
            // Every client: ease the stand toward the synced height (0 with no dog)
            if (liftRoot == null || !_liftBaseOk) return;
            float want = heldDog >= 0 ? lift : 0f;
            if (_liftNow == want) return;
            _liftNow = Mathf.MoveTowards(_liftNow, want, Mathf.Max(0.05f, liftSpeed) * Time.deltaTime);
            liftRoot.localPosition = _liftBase + Vector3.up * _liftNow;
        }
    }
}
