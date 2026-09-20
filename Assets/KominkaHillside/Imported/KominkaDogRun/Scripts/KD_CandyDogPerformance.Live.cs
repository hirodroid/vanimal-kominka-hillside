using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    public partial class KD_CandyDogPerformance
    {
        public KD_LiveSystem liveSystem;
        private int _liveEpoch = -1;
        private bool[] _liveDogAuthority = new bool[6];
        private bool[] _liveSlotAuthority = new bool[6];
        private float _nextLiveReport;
        private bool _liveConfigurationWarned;
        private int _liveRecoveryEpoch = -1;

        public bool LiveConfigurationValid() // net: callable-ok: read-only production wiring validation; never starts or owns a live
        {
            if (previewSceneOnly || liveSystem == null || !liveSystem.HasDogPerformance(this)
                || director == null || dogs == null || dogs.Length != 6 || slots == null || slots.Length != 6
                || accessoryBox == null || costumeNodes == null || costumeNodes.Length != 18
                || cueTimes == null || cueTimes.Length == 0 || cueMasks == null || cueYaw == null
                || cueTimes.Length != cueMasks.Length || cueTimes.Length != cueYaw.Length) return false;
            for (int i = 0; i < 6; i++)
            {
                if (dogs[i] == null || dogs[i].dogRoot == null || dogs[i].fetchController == null || slots[i] == null
                    || !dogs[i].gameObject.activeInHierarchy || !dogs[i].dogRoot.gameObject.activeInHierarchy) return false;
                for (int j = 0; j < i; j++) if (dogs[j] == dogs[i]) return false;
            }
            for (int i = 0; i < costumeNodes.Length; i++) if (costumeNodes[i] == null) return false;
            if (!useChoreography || !MountValid()) return false;
            if (choreographySpace == null || motionTimes == null || motionTimes.Length < 2) return false;
            int count = motionTimes.Length * 6;
            return motionPositions != null && motionPositions.Length == count && motionYaws != null && motionYaws.Length == count
                && motionPoses != null && motionPoses.Length == count && motionPartners != null && motionPartners.Length == count
                && motionSpeedLimits != null && motionSpeedLimits.Length == count
                && dogLightLayers != null && dogLightDogs != null && dogLightOrigins != null && dogLightBaselines != null
                && dogLightLayers.Length == dogLightDogs.Length && dogLightLayers.Length == dogLightOrigins.Length
                && dogLightLayers.Length == dogLightBaselines.Length;
        }

        public void _ReleaseLiveDogs()
        {
            if (previewSceneOnly || !_reserved) return;
            Cleanup();
            phase = -1;
        }

        private void ReserveLiveDogs()
        {
            if (!liveSystem._ClaimDogPerformance(this)) return;
            _reserved = true;
            SetCostumeGlow(0f);
            _liveEpoch = liveSystem.CandyEpoch;
            _nextLiveReport = 0f;
            _motionCursor = 0;
            _lastTimelineTime = 0f;
            _motionBadSince = -1f;
            maxMotionError = 0f;
            failureReason = "";
            phase = 1;
            for (int i = 0; i < 6; i++)
            {
                _liveDogAuthority[i] = false; _liveSlotAuthority[i] = false;
                dogs[i].candyPerformanceSlot = slots[i];
                dogs[i].candyPerformanceActive = true;
                dogs[i].candyMotionEnabled = false;
                dogs[i].candyPerformanceSit = false;
                dogs[i].candyPerformanceYaw = 0f;
                dogs[i].candyPerformanceEnergy = 0f;
                dogs[i]._BeginCandyPerformance();
            }
            UpdateBodyCues(0f);
            UpdateChoreography(0f);
            ConfigureMount();
            accessoryBox.candyPerformanceNodes = costumeNodes;
            accessoryBox._SetCandyPerformanceOverlay(true);
            for (int i = 0; i < costumeNodes.Length; i++) costumeNodes[i].SetActive(true);
        }

        public void _ServiceLiveRecovery()
        {
            if (liveSystem != null && liveSystem.DogPerformanceOf(liveSystem.CurrentLiveId) == this
                && useMount && dogs != null && mountRider >= 0 && mountRider < dogs.Length && dogs[mountRider] != null
                && (liveSystem.DogRecovering || liveSystem.DogMountState == 4))
            {
                // Late joining a cancellation must install the recovery references
                // even though the ordinary show reservation has already ended.
                if (_liveRecoveryEpoch != liveSystem.CandyEpoch)
                {
                    if (!_reserved) ConfigureMount();
                    _liveRecoveryEpoch = liveSystem.CandyEpoch;
                    for (int i = 0; i < dogs.Length; i++)
                    {
                        if (dogs[i] == null) continue;
                        dogs[i].candyRecoveryRider = dogs[mountRider];
                        dogs[i].candyExitHoldUntil = Time.time + 3f;
                    }
                }
                dogs[mountRider]._RecoverDisabledLiveMount();
            }
        }

        private void UpdateLivePerformance()
        {
            bool wanted = liveSystem != null && liveSystem.DogPerformanceOf(liveSystem.CurrentLiveId) == this && liveSystem.CandyReserved;
            if (!wanted)
            {
                if (_reserved)
                {
                    bool completed = liveSystem != null && liveSystem.DogPerformanceOf(liveSystem.CurrentLiveId) == this && !liveSystem.CandyStopped;
                    Cleanup(); phase = completed ? 3 : -1;
                }
                return;
            }
            if (!LiveConfigurationValid())
            {
                if (!_liveConfigurationWarned)
                {
                    failureReason = "Production actor or wiring became unavailable.";
                    // An actor disappearing during a show follows shared cancellation.
                    // Reserve the setup diagnostic for invalid wiring before reservation.
                    if (!_reserved) Debug.LogError("[KD_CandyDogPerformance] Production wiring is incomplete; readiness will not be acknowledged.");
                    _liveConfigurationWarned = true;
                }
                if (_reserved) Cleanup();
                return;
            }
            _liveConfigurationWarned = false;
            if (_reserved && _liveEpoch != liveSystem.CandyEpoch) Cleanup();
            if (!_reserved) ReserveLiveDogs();
            if (!_reserved) return;

            // Every client mirrors the reservation. Only each object's current authority
            // cancels its normal work, once on acquisition; a later ownership change retries.
            int dogMask = 0, slotMask = 0;
            bool reportsOwnedRole = false;
            for (int i = 0; i < 6; i++)
            {
                KD_DogWanderController dog = dogs[i];
                bool owner = Networking.IsOwner(dog.dogRoot.gameObject) && Networking.IsOwner(dog.fetchController.gameObject);
                if (owner && !_liveDogAuthority[i]) dog.fetchController._EndForCandyPerformance();
                _liveDogAuthority[i] = owner;
                KD_SleepSlot slot = dog.sleepBoard == null ? null : dog.sleepBoard.SlotOf(dog.dogIndex);
                bool slotOwner = slot != null && Networking.IsOwner(slot.gameObject);
                if (slotOwner && !_liveSlotAuthority[i]) slot._ReleaseForCandyPerformance();
                _liveSlotAuthority[i] = slotOwner;
                bool released = slot == null || (slot.LiveSleeperId < 0 && slot.LiveReservationId < 0);
                if (owner && released && dog.CandyReady && dog.fetchController.IsIdle) dogMask |= 1 << i;
                if (slotOwner && released) slotMask |= 1 << i;
                if (owner || slotOwner) reportsOwnedRole = true;
            }
            if (liveSystem.CandyPreparing && reportsOwnedRole && Time.time >= _nextLiveReport)
            {
                _nextLiveReport = Time.time + .4f;
                liveSystem._ReportCandyReadiness(_liveEpoch, dogMask, slotMask);
            }

            float t = liveSystem.CandyPreparing ? 0f : Mathf.Clamp(liveSystem.Elapsed - KD_LiveSystem.NightInSeconds, 0f, duration);
            UpdateBodyCues(t);
            // During preparation and the existing night fade, retain the exact t=0 targets.
            // Late joiners start directly at the shared current cue, never at frame zero.
            if (!liveSystem.CandyPreparing && liveSystem.Elapsed >= KD_LiveSystem.NightInSeconds)
            {
                if (t < _lastTimelineTime) _motionCursor = 0;
                phase = 2;
                if (liveSystem.IsPlaying) UpdateCostumeGlow(t);
                else SetCostumeGlow(0f);
                if (!UpdateMountClock(t)) return;
            }
            else { phase = 1; SetCostumeGlow(0f); }
            UpdateChoreography(t);
            _lastTimelineTime = t;
        }

        // Arrival times come from the MIDI event provenance; shared by preview and live.
        private void UpdateBodyCues(float t)
        {
            for (int d = 0; d < 6; d++)
            {
                float yaw = 0f, energy = 0f;
                for (int c = 0; c < cueTimes.Length; c++)
                {
                    if ((cueMasks[c] & (1 << d)) == 0) continue;
                    float delta = t - cueTimes[c];
                    if (delta < -.65f || delta > 1.1f) continue;
                    float e = delta < 0f ? Mathf.SmoothStep(0f, 1f, (delta + .65f) / .65f)
                        : Mathf.SmoothStep(1f, 0f, delta / 1.1f);
                    if (e > energy) { energy = e; yaw = Mathf.Clamp(cueYaw[c], -18f, 18f) * e; }
                }
                dogs[d].candyPerformanceYaw = yaw;
                dogs[d].candyPerformanceEnergy = energy;
                dogs[d].candyPerformanceSit = t >= sitTime;
            }
        }
    }
}
