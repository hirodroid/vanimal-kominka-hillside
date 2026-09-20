using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    public partial class KD_LiveSystem
    {
        public KD_CandyDogPerformance candyPerformance;
        public int candyLiveId = -1;
        // Indexed by liveId. Legacy Candy fields remain the fallback for saved scenes.
        public KD_CandyDogPerformance[] dogPerformances = new KD_CandyDogPerformance[0];
        private KD_CandyDogPerformance _localDogPerformance;

        public KD_CandyDogPerformance DogPerformanceOf(int id) // net: callable-ok: read-only work configuration lookup
        {
            if (id < 0) return null;
            if (dogPerformances != null && id < dogPerformances.Length && dogPerformances[id] != null)
                return dogPerformances[id];
            return id == candyLiveId ? candyPerformance : null;
        }

        public bool HasDogPerformance(KD_CandyDogPerformance performance) // net: callable-ok: read-only registration check
        {
            if (performance == null) return false;
            if (stageDirectors != null) for (int i = 0; i < stageDirectors.Length; i++)
                if (DogPerformanceOf(i) == performance) return true;
            return false;
        }

        public bool _ClaimDogPerformance(KD_CandyDogPerformance performance)
        {
            if (performance == null || DogPerformanceOf(liveId) != performance || !CandyReserved) return false;
            // Release synchronously before another coordinator writes shared dogs/outfits.
            // Unity's Update order must not decide which show's cleanup wins.
            if (_localDogPerformance != null && _localDogPerformance != performance)
                _localDogPerformance._ReleaseLiveDogs();
            _localDogPerformance = performance;
            return true;
        }
        public const float CandyGatherSeconds = 45f;
        [UdonSynced] private bool candyPreparing;
        [UdonSynced] private int candyEpoch;
        [UdonSynced] private int candyPrepareMs;
        // Early cancellation cannot be derived from the ordinary end-of-track clock:
        // StopFor deliberately moves that clock to the end to preserve cooldown.
        [UdonSynced] private bool candyStopped;
        // Readiness is an expiring authority-local lease, not a second playback clock.
        // A new Owner requests no ownership: the dog/slot Owners keep reporting.
        private int[] _candyDogReporters = new int[6];
        private int[] _candySlotReporters = new int[6];
        private float[] _candyDogReadyUntil = new float[6];
        private float[] _candySlotReadyUntil = new float[6];
        private float _candyAllReadySince = -1f;
        private float _nextCandyCheck;

        public bool CandyPreparing { get { return candyPreparing && DogPerformanceOf(liveId) != null; } } // net: callable-ok: read-only shared preparation state
        public int CandyEpoch { get { return candyEpoch; } } // net: callable-ok: read-only preparation generation
        public bool CandyStopped { get { return candyStopped; } } // net: callable-ok: read-only explicit cancellation result
        public float CandyPreparationElapsed { get { return (Networking.GetServerTimeInMilliseconds() - candyPrepareMs) * .001f; } } // net: callable-ok: read-only shared preparation clock
        public bool CandyReserved // net: callable-ok: reservation derived from shared preparation/playback and restoration tail
        {
            get
            {
                if (DogPerformanceOf(liveId) == null || candyStopped) return false;
                return candyPreparing || (Elapsed >= 0f && Elapsed < NightInSeconds + DurationOf(liveId) + NightOutSeconds);
            }
        }

        private bool BeginCandyPreparation(int id, int host)
        {
            // Called only after the existing host/access/work-ownership checks.
            KD_CandyDogPerformance performance = DogPerformanceOf(id);
            if (performance == null || !performance.LiveConfigurationValid()) return false;
            liveId = id; hostId = host;
            candyEpoch = candyEpoch == int.MaxValue ? 1 : candyEpoch + 1;
            candyPrepareMs = Networking.GetServerTimeInMilliseconds();
            candyPreparing = true;
            candyStopped = false;
            dogMountState = 0; dogMountOwner = -1;
            ResetCandyReadiness();
            RequestSerialization();
            return true;
        }

        private void ResetCandyReadiness()
        {
            _candyAllReadySince = -1f;
            _nextCandyCheck = 0f;
            for (int i = 0; i < 6; i++)
            {
                _candyDogReporters[i] = -1; _candySlotReporters[i] = -1;
                _candyDogReadyUntil[i] = -1f; _candySlotReadyUntil[i] = -1f;
            }
        }

        private void CancelCandyPreparation()
        {
            candyPreparing = false; candyStopped = true; liveId = -1; hostId = -1;
            ResetCandyReadiness();
            RequestSerialization();
        }

        public void _ReportCandyReadiness(int epoch, int dogReadyMask, int slotReadyMask)
        {
            if (!CandyPreparing || epoch != candyEpoch || !Utilities.IsValid(Networking.LocalPlayer)) return;
            if (Networking.IsOwner(gameObject)) AcceptCandyReadiness(Networking.LocalPlayer, epoch, dogReadyMask, slotReadyMask);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(CandyReadiness), epoch, dogReadyMask, slotReadyMask);
        }

        [NetworkCallable(maxEventsPerSecond: 30)]
        public void CandyReadiness(int epoch, int dogReadyMask, int slotReadyMask) // net: callable-ok: current epoch and each dog/slot Owner authenticate every readiness bit
        { AcceptCandyReadiness(NetworkCalling.CallingPlayer, epoch, dogReadyMask, slotReadyMask); }

        private void AcceptCandyReadiness(VRCPlayerApi caller, int epoch, int dogMask, int slotMask)
        {
            KD_CandyDogPerformance performance = DogPerformanceOf(liveId);
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(caller) || !CandyPreparing
                || epoch != candyEpoch || dogMask < 0 || dogMask > 63 || slotMask < 0 || slotMask > 63
                || performance == null || performance.dogs == null || performance.dogs.Length != 6) return;
            for (int i = 0; i < 6; i++)
            {
                KD_DogWanderController dog = performance.dogs[i];
                if (dog == null || dog.dogRoot == null || dog.fetchController == null) continue;
                if (Networking.GetOwner(dog.dogRoot.gameObject) == caller && Networking.GetOwner(dog.fetchController.gameObject) == caller)
                {
                    _candyDogReporters[i] = caller.playerId;
                    _candyDogReadyUntil[i] = (dogMask & (1 << i)) != 0 ? Time.time + 2f : -1f;
                }
                KD_SleepSlot slot = dog.sleepBoard == null ? null : dog.sleepBoard.SlotOf(dog.dogIndex);
                if (slot != null && Networking.GetOwner(slot.gameObject) == caller)
                {
                    _candySlotReporters[i] = caller.playerId;
                    _candySlotReadyUntil[i] = (slotMask & (1 << i)) != 0 ? Time.time + 2f : -1f;
                }
            }
        }

        private bool CandyOwnersReady()
        {
            KD_CandyDogPerformance performance = DogPerformanceOf(liveId);
            if (performance == null || !performance.LiveConfigurationValid() || !MountPairReady(performance)) return false;
            for (int i = 0; i < 6; i++)
            {
                KD_DogWanderController dog = performance.dogs[i];
                VRCPlayerApi owner = Networking.GetOwner(dog.dogRoot.gameObject);
                if (!Utilities.IsValid(owner) || owner.playerId != _candyDogReporters[i]
                    || Networking.GetOwner(dog.fetchController.gameObject) != owner || Time.time > _candyDogReadyUntil[i]) return false;
                KD_SleepSlot slot = dog.sleepBoard == null ? null : dog.sleepBoard.SlotOf(dog.dogIndex);
                if (slot == null) continue;
                VRCPlayerApi slotOwner = Networking.GetOwner(slot.gameObject);
                if (!Utilities.IsValid(slotOwner) || slotOwner.playerId != _candySlotReporters[i]
                    || Time.time > _candySlotReadyUntil[i] || slot.LiveSleeperId >= 0 || slot.LiveReservationId >= 0) return false;
            }
            return true;
        }

        private void UpdateCandyAuthority()
        {
            if (!Networking.IsOwner(gameObject) || !CandyPreparing) return;
            if (Time.time < _nextCandyCheck) return;
            _nextCandyCheck = Time.time + .1f;
            if (CandyPreparationElapsed >= CandyGatherSeconds)
            { CancelCandyPreparation(); return; }
            if (!CandyOwnersReady()) { _candyAllReadySince = -1f; return; }
            if (_candyAllReadySince < 0f) _candyAllReadySince = Time.time;
            if (Time.time - _candyAllReadySince < .75f) return;
            // Authorization was committed with this preparation. Host departure does not
            // erase it, just as an ordinary live continues after its host has left.
            startMs = Networking.GetServerTimeInMilliseconds();
            var performance = DogPerformanceOf(liveId);
            if (performance.useMount) dogMountOwner = Networking.GetOwner(performance.dogs[performance.mountRider].dogRoot.gameObject).playerId;
            candyPreparing = false;
            RequestSerialization();
        }
    }
}
