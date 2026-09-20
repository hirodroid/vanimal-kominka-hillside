using UnityEngine;

namespace KominkaDogRun
{
    public partial class KD_DogWanderController
    {
        // Opt-in copied-scene configuration. The coordinator arms this only after
        // checking the saved mount plan, controller layer and locally owned pair.
        public bool candyMountArmed;
        public KD_DogWanderController candyMountSupport;
        public KD_DogWanderController candyMountRider;
        public KD_DogWanderController candyRecoveryRider;
        public Transform candyMountAnchor;
        public Vector3 candyMountAttachmentPosition;
        public Quaternion candyMountAttachmentRotation = Quaternion.identity;
        public Vector3 candyMountTakeoff;
        public Vector3 candyMountSeat;
        public Vector3 candyMountLanding;
        public Vector3 candyMountSupportPosition;
        public Quaternion candyMountSupportYaw = Quaternion.identity;
        public Quaternion candyMountYaw = Quaternion.identity;
        public float candyMountClock;
        public float candyMountStart = 104;
        public float candyMountTouch = 105.4f;
        public float candyMountLeave = 108;
        public float candyMountLand = 109.4f;
        public float candyMountAscentArc = .55f;
        public float candyMountDescentArc = .5f;
        public int candyMountLayer = -1;
        public bool candyMountFailed;
        public float candyExitHoldUntil;
        private bool _candyMountOccupied;
        private bool _candyMountRecovering;
        private bool _candyMountFinished;
        private float _candyMountRecoveryElapsed;
        private Vector3 _candyMountRecoveryFrom;
        private Vector3 _candyMountRecoveryTo;
        private Quaternion _candyMountRecoveryRotation;
        private float _candyMountRecoveryPoseFrom;
        private Vector3 _candyMountRecoveryLastPosition;
        private Quaternion _candyMountRecoveryLastRotation;
        private float _candyMountReleaseUntil;
        private Vector3 _candyMountActualTakeoff;
        private float _candyMountLastClock;

        // net: callable-ok: read-only support/cleanup state
        public bool CandyMountBusy { get { return _candyMountOccupied || _candyMountRecovering
            || (candyMountLive != null && candyMountEpoch == candyMountLive.CandyEpoch
                && (candyMountLive.DogMountState == 1 || candyMountLive.DogRecovering)); } }
        // net: callable-ok: read-only gate against rearming during the previous recovery
        public bool CandyMountAvailable { get { return !CandyMountBusy && (candyMountRider == null || !candyMountRider.CandyMountBusy)
            && (candyRecoveryRider == null || !candyRecoveryRider.CandyMountBusy)
            && Time.time >= _candyMountReleaseUntil && Time.time >= candyExitHoldUntil; } }
        // net: callable-ok: read-only reservation through post-stop landing and rise
        public bool CandyReserved { get { return candyPerformanceActive || !CandyMountAvailable || Time.time < _candyRiseUntil; } }
        // net: callable-ok: read-only actual held LieDown state
        public bool CandyLying { get { return _wasLieDown && _candyLieActive && _speed <= stopSpeedThreshold; } }

        private void ResetCandyMount()
        {
            // A new preparation must wait for an old landing to finish.
            if (CandyMountBusy || Time.time < _candyMountReleaseUntil) return;
            candyMountArmed = false; candyMountFailed = false; _candyMountFinished = false;
            candyMountSupport = null; candyMountRider = null; candyMountAnchor = null; candyRecoveryRider = null;
            candyMountLive = null; candyMountEpoch = -1; _mountReportAt = 0; _mountRecoveryObserved = false; _mountMotionObserved = false;
        }

        private void BeginCandyMountRecovery()
        {
            candyMountArmed = false;
            if (candyMountLive != null) return; // production recovery is committed by the shared ledger
            if (!_candyMountOccupied || _candyMountRecovering) return;
            _candyMountRecovering = true; _candyMountRecoveryElapsed = 0;
            _candyMountRecoveryFrom = dogRoot.position; _candyMountRecoveryRotation = dogRoot.rotation;
            _candyMountRecoveryLastPosition = dogRoot.position; _candyMountRecoveryLastRotation = dogRoot.rotation;
            _candyMountRecoveryPoseFrom = dogAnimator != null && dogAnimator.GetBool("PumpkinMountAir")
                ? Mathf.Clamp01(dogAnimator.GetFloat("PumpkinMountTime")) : 0;
            _candyMountRecoveryTo = Vector3.Distance(dogRoot.position, candyMountTakeoff) < Vector3.Distance(dogRoot.position, candyMountLanding)
                ? candyMountTakeoff : candyMountLanding;
            // Endpoints were surveyed on the world floor before launch. Do not ray
            // snap onto the support dog, and do not depend on a stopped coordinator.
        }

        private bool UpdateCandyMount()
        {
            if (candyMountLive != null && UpdateLiveMount()) return true;
            // A disabled/non-owned rider pauses its recovery. The other actors
            // remain reserved, including after the coordinator itself is disabled.
            if (candyRecoveryRider != null && candyRecoveryRider.CandyMountBusy)
                candyExitHoldUntil = Time.time + 3f;
            bool supportHeld = candyMountRider != null && candyMountRider.CandyMountBusy;
            if (supportHeld)
            {
                _liveCalled = true; _groupCalled = true;
                _wantLieDown = true; _candyLieActive = true; _look = false; _groupSit = false;
                _speed = 0; _moveDir = Vector3.zero; ApplyMoveAnim();
                return true;
            }
            if (candyMountRider != null && !candyPerformanceActive)
            {
                candyMountRider = null;
                if (_candyLieActive) { _candyRiseUntil = Time.time + .8f; _candyLieActive = false; }
            }
            if (_candyMountRecovering)
            {
                if (!_dogOwner || dogRoot == null || !dogRoot.gameObject.activeInHierarchy) return true;
                // Another owner/tool may have moved the rider while recovery was
                // paused. Keep the preview safely reserved; never pull it back
                // onto an old arc. Stopping Play restores the saved preview.
                if (Vector3.Distance(dogRoot.position, _candyMountRecoveryLastPosition) > .03f
                    || Quaternion.Angle(dogRoot.rotation, _candyMountRecoveryLastRotation) > 5f)
                { candyMountFailed = true; return true; }
                // Accumulate only executed movement frames. Re-enabling after a
                // pause must not jump directly to a wall-clock landing endpoint.
                _candyMountRecoveryElapsed += Mathf.Min(Time.deltaTime, .05f);
                float u = Mathf.Clamp01(_candyMountRecoveryElapsed / 1.4f);
                float e = Mathf.SmoothStep(0, 1, u);
                var position = Vector3.Lerp(_candyMountRecoveryFrom, _candyMountRecoveryTo, e);
                position.y += Mathf.Max(candyMountAscentArc, candyMountDescentArc) * Mathf.Sin(Mathf.PI * u);
                MoveCandyMount(position, Quaternion.Slerp(_candyMountRecoveryRotation, candyMountYaw, e), true,
                    Mathf.Lerp(_candyMountRecoveryPoseFrom, 1, u), 0);
                _candyMountRecoveryLastPosition = dogRoot.position; _candyMountRecoveryLastRotation = dogRoot.rotation;
                if (u >= 1 && Vector3.Distance(dogRoot.position, _candyMountRecoveryTo) < .005f) FinishCandyMount();
                return true;
            }
            if (Time.time < _candyMountReleaseUntil)
            {
                _liveCalled = true; _groupCalled = true; _look = false; _groupSit = false;
                _speed = 0; _moveDir = Vector3.zero; ApplyMoveAnim();
                if (dogAnimator != null && candyMountLayer >= 0)
                    dogAnimator.SetLayerWeight(candyMountLayer, Mathf.Clamp01((_candyMountReleaseUntil - Time.time) / .2f));
                return true;
            }
            if (_candyMountReleaseUntil > 0)
            {
                if (dogAnimator != null && candyMountLayer >= 0) dogAnimator.SetLayerWeight(candyMountLayer, 0);
                _candyMountReleaseUntil = 0;
            }
            if (!candyPerformanceActive || !candyMountArmed || _candyMountFinished) return false;
            float t = candyMountClock;
            if (t < candyMountStart) return false;
            if (!_dogOwner) { candyMountFailed = true; BeginCandyMountRecovery(); return true; }
            if (candyMountFailed) { _speed = 0; _moveDir = Vector3.zero; ApplyMoveAnim(); return true; }
            if (_candyMountOccupied && (t < _candyMountLastClock || t - _candyMountLastClock > .25f))
            { candyMountFailed = true; BeginCandyMountRecovery(); return true; }
            if (candyMountSupport == null || candyMountAnchor == null || dogAnimator == null || candyMountLayer < 0
                || candyMountSupport.dogRoot == null || !candyMountSupport.CandyLying || !candyMountSupport.dogRoot.gameObject.activeInHierarchy
                || Vector3.Distance(candyMountSupport.dogRoot.position, candyMountSupportPosition) > .08f
                || Quaternion.Angle(candyMountSupport.dogRoot.rotation, candyMountSupportYaw) > 8)
            {
                candyMountFailed = true; BeginCandyMountRecovery(); return true;
            }
            if (!_candyMountOccupied)
            {
                if (t > candyMountStart + .15f || Vector3.Distance(dogRoot.position, candyMountTakeoff) > .04f
                    || Quaternion.Angle(dogRoot.rotation, candyMountYaw) > 8 || _speed > stopSpeedThreshold)
                { candyMountFailed = true; return true; }
                _candyMountOccupied = true;
                _candyMountActualTakeoff = dogRoot.position;
            }
            _candyMountLastClock = t;
            var seat = candyMountAnchor.TransformPoint(candyMountAttachmentPosition);
            var seatRotation = candyMountAnchor.rotation * candyMountAttachmentRotation;
            if (t < candyMountTouch)
            {
                float u = Mathf.InverseLerp(candyMountStart, candyMountTouch, t), e = Mathf.SmoothStep(0, 1, u);
                var position = Vector3.Lerp(_candyMountActualTakeoff, candyMountSeat, e) + (seat - candyMountSeat) * e;
                position.y += candyMountAscentArc * Mathf.Sin(Mathf.PI * u);
                MoveCandyMount(position, Quaternion.Slerp(candyMountYaw, seatRotation, e), true, u, 0);
            }
            else if (t < candyMountLeave) MoveCandyMount(seat, seatRotation, false, 0, t - candyMountTouch);
            else
            {
                float u = Mathf.InverseLerp(candyMountLeave, candyMountLand, t), e = Mathf.SmoothStep(0, 1, u);
                var position = Vector3.Lerp(candyMountSeat, candyMountLanding, e) + (seat - candyMountSeat) * (1 - e);
                position.y += candyMountDescentArc * Mathf.Sin(Mathf.PI * u);
                MoveCandyMount(position, Quaternion.Slerp(seatRotation, candyMountYaw, e), true, u, 0);
                if (t >= candyMountLand) FinishCandyMount();
            }
            return true;
        }

        private void MoveCandyMount(Vector3 position, Quaternion rotation, bool airborne, float progress, float holdSeconds)
        {
            _liveCalled = true; _groupCalled = true; _look = false; _groupSit = false; _wantLieDown = false;
            _speed = 0; _moveDir = Vector3.zero; ApplyMoveAnim();
            if (_dogOwner)
            {
                dogRoot.position = position; // net: owner-only: _dogOwner; opt-in surveyed airborne route, never applied by non-owner
                dogRoot.rotation = rotation; // net: owner-only: _dogOwner; measured support attachment or ground-facing landing
            }
            if (dogAnimator != null && candyMountLayer >= 0)
            {
                dogAnimator.SetLayerWeight(candyMountLayer, 1);
                dogAnimator.SetBool("PumpkinMountAir", airborne);
                if (airborne) dogAnimator.SetFloat("PumpkinMountTime", progress);
                else dogAnimator.SetFloat("PumpkinMountHoldTime", holdSeconds / 13.75f);
            }
        }

        private void FinishCandyMount()
        {
            _candyMountOccupied = false; _candyMountRecovering = false; _candyMountFinished = true;
            candyMountArmed = false; _candyMountReleaseUntil = Time.time + .2f;
            if (dogAnimator != null) dogAnimator.CrossFade("Idle", .2f, 0, 0);
            _candyLastPosition = dogRoot.position;
        }
    }
}
