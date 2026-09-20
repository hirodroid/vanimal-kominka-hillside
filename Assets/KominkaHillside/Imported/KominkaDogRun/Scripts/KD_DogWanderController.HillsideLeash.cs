using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    // Hillside-only adaptation. Existing Owner-only locomotion and animation remain the single writer.
    public partial class KD_DogWanderController
    {
        public KominkaHillside.KH_LeashManager hillsideLeashes;
        private NavMeshPath _leashPath;
        private Vector3[] _leashCorners;
        private int _leashCorner;
        private float _nextLeashPath;
        private bool _wasLeashed;

        public void _InvalidateLeashAnimation() { _leashAnimationDirty = true; }
        private bool _leashAnimationDirty;

        private bool UpdateHillsideLeash()
        {
            if (hillsideLeashes == null || dogRoot == null) return false;
            if (!hillsideLeashes._IsLeashed(dogIndex))
            {
                if (_wasLeashed) { _wasLeashed = false; _speed = 0f; _nextLeashPath = 0f; }
                return false;
            }
            if (!_wasLeashed) _leashAnimationDirty = true;
            _wasLeashed = true;
            // Fetch cancels on its own owner tick. Never write the root concurrently with it.
            if (fetchController != null && !fetchController.IsIdle) return true;
            _wantLieDown = false; _wantSleep = false; _paused = false;
            _lastBusyTime = Time.time;
            if (!_dogOwner) { ApplyMoveAnim(); return true; }
            VRCPlayerApi holder = VRCPlayerApi.GetPlayerById(hillsideLeashes.walkers[dogIndex]);
            if (!Utilities.IsValid(holder)) { _speed = 0f; ApplyMoveAnim(); return true; }
            if (Time.time >= _nextLeashPath)
            {
                _nextLeashPath = Time.time + 0.35f;
                if (_leashPath == null) _leashPath = new NavMeshPath();
                NavMeshHit from, to;
                bool fromValid = NavMesh.SamplePosition(dogRoot.position, out from, 2f, NavMesh.AllAreas);
                bool toValid = NavMesh.SamplePosition(hillsideLeashes._WalkingTarget(dogIndex), out to, 2f, NavMesh.AllAreas);
                _leashCorners = null;
                if (fromValid && toValid && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _leashPath))
                {
                    _leashCorners = _leashPath.corners; _leashCorner = 1;
                    // Recover only to a baked, reachable surface after a large separation/teleport.
                    if (Vector3.Distance(dogRoot.position, holder.GetPosition()) > hillsideLeashes.catchUpDistance
                        && _leashPath.status == NavMeshPathStatus.PathComplete)
                    {
                        VRCObjectSync sync = dogRoot.GetComponent<VRCObjectSync>();
                        if (sync != null) sync.FlagDiscontinuity();
                        dogRoot.position = to.position; // net: owner-only: _dogOwner checked above
                        _speed = 0f; _leashCorners = null;
                    }
                }
            }
            if (_leashCorners == null || _leashCorners.Length < 2) { _speed = 0f; ApplyMoveAnim(); return true; }
            while (_leashCorner < _leashCorners.Length - 1 && Vector3.Distance(dogRoot.position, _leashCorners[_leashCorner]) < 0.35f) _leashCorner++;
            Vector3 target = _leashCorners[_leashCorner];
            Vector3 delta = target - dogRoot.position; delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 0.15f) { _speed = 0f; ApplyMoveAnim(); return true; }
            float clearance = WallClearance(dogRoot.position, delta / distance, distance);
            if (clearance < 0.05f) { _speed = 0f; ApplyMoveAnim(); return true; }
            target = dogRoot.position + delta / distance * clearance;
            float speed = Vector3.Distance(dogRoot.position, holder.GetPosition()) > hillsideLeashes.leashLength * 0.7f
                ? hillsideLeashes.followSpeed : walkSpeed;
            StepToward(target, _leashCorner == _leashCorners.Length - 1 ? 0.2f : 0.05f, speed);
            return true;
        }
    }
}
