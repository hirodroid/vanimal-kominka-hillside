using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    public partial class KD_DogWanderController
    {
        const int BehaviorInvitation = 14;
        private int _invitationStation = -1;
        private float _invitationRetryAt;

        // This is an invitation, not a reservation: it must not raise ShowEngaged or make the collar unattachable.
        public bool _InvitationAvailable()
        {
            if (!enabled || !gameObject.activeInHierarchy || fetchController == null || !fetchController.IsIdle
                || fetchController.PetActive || fetchController.WaterPose != 0 || FrisbeeActive
                || CandyReserved || candyPerformanceActive || WashHeld || _leaveActive
                || _restPhase == RestPhaseToWater || _restPhase == RestPhaseDrink
                || _treatEating || Time.time < _thanksUntil || Time.time < _invitationRetryAt) return false;
            if (petController != null && (petController.IsPetting || petController.IsShaking)) return false;
            if (treatBox != null && treatBox.LiveSlotOf(dogIndex) != null) return false;
            if (sleepBoard != null && sleepBoard._IsCalled(dogIndex)) return false;
            if (liveSystem != null && liveSpot != null && liveSystem.IsPlaying) return false;
            if (activities != null && ((activities.Mode == 1 && activities._IsParticipant(dogIndex))
                || (activities.Mode == 2 && activities.RaceBall >= 0 && activities.RaceWinner < 0)
                || (activities.Mode == 3 && (activities.DogA == dogIndex || activities.DogB == dogIndex)))) return false;
            return true;
        }

        bool UpdateInvitation()
        {
            // VER-ENV-002 (2026-10-12): the cheap "is this dog invited at all" test runs before the two availability
            // checks (about 30 cross-behaviour reads). Every exit below is the same `_invitationStation = -1; return false`,
            // and both availability checks are pure reads (the WashHeld memo is a frame cache), so the result is unchanged.
            if (hillsideLeashes == null || dogIndex < 0 || hillsideLeashes.inviteSpots == null)
            { _invitationStation = -1; return false; }
            int[] dogStations = hillsideLeashes.inviteDogStation;
            if (dogStations == null || dogIndex >= dogStations.Length)
            { _invitationStation = -1; return false; }
            int station = dogStations[dogIndex] - 1;
            if (station < 0) { _invitationStation = -1; return false; }
            double now = Networking.GetServerTimeInSeconds();
            if (now >= hillsideLeashes.inviteUntil[station]
                || !_InvitationAvailable() || !hillsideLeashes._IntroDogAvailable(dogIndex))
            { _invitationStation = -1; return false; }
            Vector3 target = hillsideLeashes.inviteSpots[station * hillsideLeashes.inviteDogs.Length + dogIndex].position;
            if (!KeepOutClear(target, .3f)) { _invitationStation = -1; return false; }
            if (station != _invitationStation)
            {
                _invitationStation = station;
                _nextGatherPath = 0f; _gatherPathOk = false; _leashCorners = null;
            }
            if (_restPhase != RestPhaseNone) EndRest();
            if (now < hillsideLeashes.inviteDogAt[dogIndex]) { Coast(); return true; }
            float distance = Horizontal(dogRoot.position, target);
            if (distance > .25f)
            {
                // GatherStep plans COMPLETE NavMesh paths. A failed path never falls back to a straight line or teleport.
                if (!GatherStep(target, distance > 3f ? runSpeed : walkSpeed))
                {
                    _invitationRetryAt = Time.time + 3f; _invitationStation = -1;
                    Coast(); return false;
                }
            }
            else
            {
                Coast();
                int viewer = hillsideLeashes.inviteLookIds[station];
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(viewer);
                Vector3 look = Utilities.IsValid(p) ? p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position
                    : hillsideLeashes.inviteStations[station].position + Vector3.up * .7f;
                FaceTowards(look);
                if (_speed <= stopSpeedThreshold) _look = true;
                if (petController != null) { petController._SetGaze(look, 0f); petController._SetUrge(.55f); }
            }
            return true;
        }
    }
}
