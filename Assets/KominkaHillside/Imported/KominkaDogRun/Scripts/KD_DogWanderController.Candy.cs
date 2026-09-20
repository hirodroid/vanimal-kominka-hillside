using UnityEngine;

namespace KominkaDogRun
{
    // Opt-in performance reservation. The normal world never sets these fields.
    public partial class KD_DogWanderController
    {
        public bool candyPerformanceActive;
        public Transform candyPerformanceSlot;
        public float candyPerformanceYaw;
        public float candyPerformanceEnergy;
        public bool candyPerformanceSit;
        public bool candyMotionEnabled;
        public bool candyMotionGathering;
        public Vector3 candyMotionExpected;
        public Vector3 candyMotionTarget;
        public Vector3 candyMotionFacing;
        public float candyMotionSpeedLimit = 2.2f;
        public int candyMotionPose;
        public Transform candyMotionPartner;
        public float candyMotionError;
        public float candyMotionTravelled;
        public bool candyMotionBlocked;
        private Vector3 _candyLastPosition;
        private bool _candyReady;
        private float _candyWakeUntil;
        private float _candyRiseUntil;
        private bool _candyLieActive;
        private bool _candyPetResumePending;
        // net: callable-ok: read-only arrival evidence
        public bool CandyReady { get { return candyPerformanceActive && _candyReady; } }

        public void _BeginCandyPerformance()
        {
            if (!candyPerformanceActive || candyPerformanceSlot == null) return;
            ResetCandyMount();
            _candyReady = false;
            _candyWakeUntil = Time.time + 1.2f;
            _candyLieActive = false;
            _candyLastPosition = dogRoot.position;
            candyMotionTravelled = 0f;
            candyMotionBlocked = false;
            if (_restPhase != RestPhaseNone) EndRest();
            _sleepFrozen = false;
            _liveFrozen = false;
            if (petController != null) petController._SuppressPetting();
        }

        /// <summary>演目中 (CandyReserved) に Fetch の同期が Idle を配ったとき、非 Owner の撫で抑制が解けないようにする
        /// (破壊的監査 v4 FUNC-02/BUG-01)。抑制と同時に再開の予約 (_candyPetResumePending) を立てるので、
        /// 演目・着地・退場が終わった時点で UpdateCandyPerformance が全クライアントで撫でを戻す。</summary>
        public void _SuppressPettingForCandy()
        {
            if (petController != null) petController._SuppressPetting();
            _candyPetResumePending = true;
        }

        public void _EndCandyPerformance()
        {
            BeginCandyMountRecovery();
            bool holdForRider = candyMountRider != null && candyMountRider.CandyMountBusy;
            // Preserve the exit hold after Cancel/Fail, before normal movement branches.
            if (!holdForRider && _candyLieActive) _candyRiseUntil = Mathf.Max(_candyRiseUntil, Time.time + .8f);
            if (!holdForRider && (_candyLieActive || Time.time < _candyRiseUntil)) _wantLieDown = false;
            if (!holdForRider) _candyLieActive = false;
            candyPerformanceActive = false;
            candyMotionEnabled = false;
            candyMotionGathering = false;
            candyMotionPartner = null;
            candyMotionBlocked = false;
            candyPerformanceSit = false;
            candyPerformanceYaw = 0f;
            candyPerformanceEnergy = 0f;
            _candyReady = false;
            _liveFrozen = false;
            _lastBusyTime = Time.time;
            _candyPetResumePending = true;
        }

        private bool UpdateCandyPerformance()
        {
            if (_candyPetResumePending && !CandyReserved && fetchController != null && fetchController.IsIdle)
            {
                if (petController != null) petController._ResumePetting();
                _candyPetResumePending = false;
            }
            if (UpdateCandyMount()) return true;
            if (!candyPerformanceActive && Time.time < candyExitHoldUntil)
            {
                _liveCalled = true; _groupCalled = true; _look = false; _groupSit = false;
                _speed = 0; _moveDir = Vector3.zero; ApplyMoveAnim(); return true;
            }
            if (!candyPerformanceActive && Time.time < _candyRiseUntil)
            {
                _look = false; _groupSit = false;
                Coast();
                return true;
            }
            if (!candyPerformanceActive || candyPerformanceSlot == null) return false;
            _liveCalled = true;
            _groupCalled = true;
            _candyReady = false;
            // Finish an already consumed treat through its existing bounded eating path.
            // New claims are excluded by TreatBox; never refund/re-consume its inventory.
            if (treatBox != null && treatBox.EatingNowOf(dogIndex))
            { UpdateTreat(); return true; }
            if (_restPhase != RestPhaseNone) EndRest();
            if (Time.time < _candyWakeUntil) { Coast(); return true; }
            if (candyMotionEnabled) { UpdateCandyChoreography(); return true; }
            Vector3 delta = candyPerformanceSlot.position - dogRoot.position;
            delta.y = 0f;
            if (delta.magnitude > .08f || _speed > stopSpeedThreshold)
            {
                StepToward(candyPerformanceSlot.position, .04f,
                    delta.magnitude > runDistance ? runSpeed : walkSpeed);
                return true;
            }
            // Established per-breed taimen clips change the torso/neck posture. Use the
            // existing LookAt transition rather than sliding the root back with planted feet.
            // Anticipate the musical arrival, settle afterwards, and yield to final sitting.
            _look = !candyPerformanceSit && candyPerformanceEnergy > .18f;
            Coast();
            Vector3 facing = Quaternion.Euler(0f, candyPerformanceYaw, 0f) * candyPerformanceSlot.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude > .0001f)
            {
                FaceTowards(dogRoot.position + facing);
                _candyReady = Vector3.Angle(dogRoot.forward, facing) < 7f;
            }
            _groupSit = candyPerformanceSit && _speed <= stopSpeedThreshold;
            if (petController != null)
            {
                // The purchased idle/standing animation continues; gaze and tail add life.
                petController._SetGaze(dogRoot.position + facing * 3f + Vector3.up * .6f,
                    Mathf.Clamp(candyPerformanceYaw * .32f, -6f, 6f));
                petController._SetUrge(.18f + Mathf.Clamp01(candyPerformanceEnergy) * .45f);
            }
            return true;
        }

        private void UpdateCandyChoreography()
        {
            Vector3 travelled = dogRoot.position - _candyLastPosition; travelled.y = 0f;
            candyMotionTravelled += travelled.magnitude;
            _candyLastPosition = dogRoot.position;
            Vector3 delta = candyMotionTarget - dogRoot.position; delta.y = 0f;
            float distance = delta.magnitude;
            Vector3 facing = candyMotionFacing; facing.y = 0f;
            candyMotionBlocked = false;
            // LieDown uses the existing single Animator writer in LateUpdate. A held
            // lying dog must stand before its foot motor or facing resumes.
            if (_wasLieDown && (candyMotionPose != 3 || distance > .035f || _speed > stopSpeedThreshold
                || facing.sqrMagnitude > .0001f && Vector3.Angle(dogRoot.forward, facing) >= 7f))
            {
                _candyRiseUntil = Time.time + .8f;
                _candyLieActive = false;
            }
            if (Time.time < _candyRiseUntil)
            {
                _look = false; _groupSit = false;
                Coast();
                return;
            }
            if (distance > .035f || _speed > stopSpeedThreshold)
            {
                // Gathering uses the existing direct approach. Authored-path contact caps
                // assume dogs already occupy separate stage lanes and can deadlock arrivals.
                if (candyMotionGathering)
                {
                    StepToward(candyMotionTarget, .015f, distance > runDistance ? runSpeed : walkSpeed);
                    return;
                }
                // A short look-ahead is an aim for the existing foot motor, never a root-position Lerp.
                float speed = Mathf.Min(candyMotionSpeedLimit, Mathf.Min(runSpeed, distance / .18f));
                if (distance > .001f && Vector3.Angle(dogRoot.forward, delta) > 65f) speed = Mathf.Min(speed, .22f);
                if (_packRoots != null && _packRadius != null)
                    for (int i = 0; i < _packRoots.Length; i++)
                    {
                        if (i == dogIndex || _packRoots[i] == null || !_packRoots[i].gameObject.activeInHierarchy) continue;
                        Vector3 other = _packRoots[i].position - dogRoot.position; other.y = 0f; // sync: local-ok: forward contact-speed cap only; authored route is unchanged
                        // People/dogs behind the departure do not block a safe move away.
                        if (Vector3.Dot(other, delta) <= 0f) continue;
                        float cap = ContactSpeedCap(_packRoots[i].position, (_selfRadius + _packRadius[i]) + .12f, speed); // sync: local-ok: slowdown before visible dog contact only
                        if (cap < speed) { speed = cap; candyMotionBlocked = true; }
                    }
                StepToward(candyMotionTarget, .015f, speed);
                return;
            }
            _playing = candyMotionPose == 1;
            _look = candyMotionPose != 3 && (candyMotionPose == 1 || (!candyPerformanceSit && candyPerformanceEnergy > .18f));
            Coast();
            if (candyMotionPose == 1 && candyMotionPartner != null) facing = candyMotionPartner.position - dogRoot.position;
            facing.y = 0f;
            if (!_wasLieDown && facing.sqrMagnitude > .0001f) FaceTowards(dogRoot.position + facing);
            _groupSit = candyPerformanceSit && _speed <= stopSpeedThreshold;
            _wantLieDown = candyMotionPose == 3 && _speed <= stopSpeedThreshold
                && (facing.sqrMagnitude < .0001f || Vector3.Angle(dogRoot.forward, facing) < 7f);
            _candyLieActive = _wantLieDown;
            _candyReady = facing.sqrMagnitude < .0001f || Vector3.Angle(dogRoot.forward, facing) < 7f;
            if (petController != null)
            {
                petController._SetGaze(dogRoot.position + facing.normalized * 2f + Vector3.up * .5f, 0f);
                petController._SetUrge(candyMotionPose == 1 ? .8f : .25f + candyPerformanceEnergy * .35f);
            }
        }
    }
}
