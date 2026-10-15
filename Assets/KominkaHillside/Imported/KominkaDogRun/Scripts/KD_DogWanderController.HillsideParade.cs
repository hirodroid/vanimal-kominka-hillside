using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    public partial class KD_DogWanderController
    {
        public KominkaHillside.KH_HalloweenParadeDog paradeDog;
        [System.NonSerialized] public bool paradeActive;
        [System.NonSerialized] public Vector3 paradeTarget, paradeFacing;
        [System.NonSerialized] public float paradePoseTime;
        [System.NonSerialized] public int paradeDancePose;
        [System.NonSerialized] public float paradeDanceTime;
        public bool ParadeReserved { get { return paradeDog != null && paradeDog.BlocksNewUses; } }
        private bool _paradePose;
        private bool _paradePetSuppressed;
        private float _paradeStillSince = -1f;
        public void _SuppressPettingForParade()
        {
            _paradePetSuppressed = true;
            if (petController != null) petController._SuppressPetting();
        }

        private bool UpdateParade()
        {
            if (!paradeActive) return false;
            _paradePose = true; _liveCalled = true; _groupCalled = true;
            _sleepFrozen = false; _liveFrozen = false; _look = false; _groupSit = false;
            if (_restPhase != RestPhaseNone) EndRest();
            Vector3 delta = paradeTarget - dogRoot.position; delta.y = 0f;
            _SuppressPettingForParade();
            if (delta.sqrMagnitude > .0009f) StepToward(paradeTarget, .025f, walkSpeed);
            else
            {
                Coast();
                Vector3 f = paradeFacing; f.y = 0f;
                if (f.sqrMagnitude > .001f) FaceTowardsRate(dogRoot.position + f, 120f);
            }
            paradeDancePose = 0; paradeDanceTime = 0f;
            Vector3 heading=paradeFacing;heading.y=0f;
            bool settled=delta.sqrMagnitude<.0009f && _speed<.02f && heading.sqrMagnitude>.001f && Vector3.Angle(dogRoot.forward,heading)<4f;
            if (!settled) _paradeStillSince=-1f;
            else if (_paradeStillSince<0f) _paradeStillSince=Time.time;
            if (paradeDog != null && paradeDog.parade.Phase == KominkaHillside.KH_HalloweenParade.Playing && settled && Time.time-_paradeStillSince>.15f)
            { paradeDancePose = paradeDog.parade._DancePose(paradePoseTime,dogIndex); paradeDanceTime = paradeDog.parade._DanceTime(paradePoseTime,dogIndex); }
            return true;
        }
        public bool _PlaceForParade(Transform slot)
        {
            if (paradeDog == null || !paradeDog.Committed || !IsDogOwner() || dogRoot == null || slot == null) return false;
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(slot.position, out hit, .25f, NavMesh.AllAreas)) return false;
            VRCObjectSync sync = dogRoot.GetComponent<VRCObjectSync>();
            if (sync != null) sync.FlagDiscontinuity();
            dogRoot.position = hit.position; // net: owner-only: IsDogOwner above; genuine preparation teleport
            dogRoot.rotation = slot.rotation;
            _speed = 0f; _moveDir = Vector3.zero; _sleepFrozen = false; _liveFrozen = false;
            WaterClearFloat();
            return true;
        }
        public void _EndParadePose()
        {
            paradeActive = false;
            paradeDancePose = 0; paradeDanceTime = 0f; _companionAnimationValid = false;
            _paradeStillSince=-1f;
            if (_paradePetSuppressed)
            {
                _paradePetSuppressed = false;
                if (fetchController != null) fetchController._RefreshPettingAfterParade();
            }
            if (!_paradePose) return;
            _paradePose = false; _liveCalled = false; _groupCalled = false; _look = false; _groupSit = false;
            _lastBusyTime = Time.time;
            if (IsDogOwner() && areaCenter != null && dogRoot != null) areaCenter.position = dogRoot.position;
        }
    }
}
