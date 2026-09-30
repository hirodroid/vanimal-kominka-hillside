using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    public partial class KD_DogWanderController
    {
        public KD_LiveSystem candyMountLive;
        public int candyMountEpoch = -1;
        private float _mountReportAt;
        private int _mountRecoveryStamp = int.MinValue;
        private bool _mountRecoveryObserved;
        private bool _mountMotionObserved;
        private Vector3 _mountLastPosition;

        // A disabled coordinator/dog must not strand an airborne rider. The active
        // production coordinator may drive this bounded recovery when Update cannot.
        public void _RecoverDisabledLiveMount()
        {
            if (enabled && gameObject.activeInHierarchy && dogRoot != null && dogRoot.gameObject.activeInHierarchy) return;
            if (candyMountLive == null || (!candyMountLive.DogRecovering && candyMountLive.DogMountState != 4) || dogRoot == null) return;
            _dogOwner = Networking.IsOwner(dogRoot.gameObject);
            UpdateCandyMount();
        }

        private void ReportLiveMount(int result)
        {
            if (!_dogOwner || (result == 1 && (fetchController == null || !Networking.IsOwner(fetchController.gameObject)))
                || Time.time < _mountReportAt) return;
            _mountReportAt = Time.time + .3f;
            candyMountLive._ReportDogMount(candyMountEpoch, result,
                result == 1 ? _candyMountActualTakeoff : dogRoot.position, dogRoot.rotation);
        }

        private bool UpdateLiveMount()
        {
            if (candyMountLive == null || candyMountEpoch != candyMountLive.CandyEpoch) return false;
            int state = candyMountLive.DogMountState;
            if (state == 2 || state == 3)
            {
                _candyMountOccupied = true;
                _liveCalled = true; _groupCalled = true; _look = false; _groupSit = false;
                _speed = 0; _moveDir = Vector3.zero; ApplyMoveAnim();
                if (state == 2)
                {
                    _mountRecoveryObserved = false;
                    ReportLiveMount(3);
                    return true;
                }
                float elapsed = candyMountLive.DogMountElapsed;
                int stamp = candyMountLive.DogMountStamp;
                if (_dogOwner && (Networking.LocalPlayer.playerId != candyMountLive.DogMountOwner
                    || (_mountRecoveryObserved && stamp == _mountRecoveryStamp
                        && Vector3.Distance(dogRoot.position, _candyMountRecoveryLastPosition) > .15f)))
                {
                    // Rebase from the actual synchronized position after ownership or
                    // external displacement; never resume an old arc by snapping back.
                    ReportLiveMount(3); return true;
                }
                _mountRecoveryObserved = true; _mountRecoveryStamp = stamp;
                float u = Mathf.Clamp01(elapsed / 1.4f), e = Mathf.SmoothStep(0, 1, u);
                var from = candyMountLive.DogMountFrom; var to = candyMountLive.DogMountTo;
                var position = Vector3.Lerp(from, to, e);
                float arc = Vector3.Distance(from, to) < .08f ? 0 : Mathf.Max(candyMountAscentArc, candyMountDescentArc);
                position.y += arc * Mathf.Sin(Mathf.PI * u);
                // A delayed state packet must not skip the visible start of a landing.
                // The shared curve is the aim; only the root Owner approaches it.
                if (_dogOwner) position = Vector3.MoveTowards(dogRoot.position, position, 3f * Mathf.Min(Dt(), .05f));
                var rotation = Quaternion.Slerp(candyMountLive.DogMountRotation, candyMountYaw, e);
                if (_dogOwner) rotation = Quaternion.RotateTowards(dogRoot.rotation, rotation, 360f * Mathf.Min(Dt(), .05f));
                MoveCandyMount(position, rotation, true, u, 0);
                _candyMountRecoveryLastPosition = dogRoot.position;
                if (u >= 1) ReportLiveMount(4);
                return true;
            }
            if (state == 4)
            {
                if (!_candyMountFinished) FinishCandyMount();
                return false; // common release fade below still owns the animation layer
            }
            if (!candyPerformanceActive || !candyMountArmed) return false;
            float t = candyMountClock;
            if (t < candyMountStart) return false;
            // Observers can lose the support before the cancellation packet arrives.
            // Everyone waits safely; only the root Owner may report the failure.
            if (candyMountSupport == null || candyMountAnchor == null || candyMountSupport.dogRoot == null)
            { candyMountFailed = true; ReportLiveMount(2); return true; }
            if (_dogOwner && (Networking.LocalPlayer.playerId != candyMountLive.DogMountOwner
                || !candyMountSupport.enabled
                || !candyMountSupport.dogRoot.gameObject.activeInHierarchy
                || Networking.GetOwner(candyMountSupport.dogRoot.gameObject) != Networking.LocalPlayer))
            { candyMountFailed = true; ReportLiveMount(2); return true; }
            if (candyMountFailed) { ReportLiveMount(2); return true; }
            if (_dogOwner && _mountMotionObserved && Vector3.Distance(dogRoot.position, _mountLastPosition) > .15f)
            { candyMountFailed = true; ReportLiveMount(2); return true; }
            if (state == 0)
            {
                if (!_dogOwner) return true; // wait for actual launch, not a local inferred one
                if (!_candyMountOccupied)
                {
                    if (t > candyMountStart + .15f || !candyMountSupport.CandyLying
                        || Vector3.Distance(dogRoot.position, candyMountTakeoff) > .04f
                        || Quaternion.Angle(dogRoot.rotation, candyMountYaw) > 8f
                        || Vector3.Distance(candyMountSupport.dogRoot.position, candyMountSupportPosition) > .08f
                        || Quaternion.Angle(candyMountSupport.dogRoot.rotation, candyMountSupportYaw) > 8f
                        || _speed > stopSpeedThreshold)
                    { candyMountFailed = true; ReportLiveMount(2); return true; }
                    _candyMountOccupied = true; _candyMountActualTakeoff = dogRoot.position;
                }
                ReportLiveMount(1);
            }
            else _candyMountOccupied = true;
            if (_dogOwner && (!candyMountSupport.CandyLying
                || Vector3.Distance(candyMountSupport.dogRoot.position, candyMountSupportPosition) > .08f
                || Quaternion.Angle(candyMountSupport.dogRoot.rotation, candyMountSupportYaw) > 8f))
            { candyMountFailed = true; ReportLiveMount(2); return true; }
            var seat = candyMountAnchor.TransformPoint(candyMountAttachmentPosition);
            var seatRotation = candyMountAnchor.rotation * candyMountAttachmentRotation;
            if (t < candyMountTouch)
            {
                float u = Mathf.InverseLerp(candyMountStart, candyMountTouch, t), e = Mathf.SmoothStep(0, 1, u);
                var from = state == 1 ? candyMountLive.DogMountFrom : _candyMountActualTakeoff;
                var position = Vector3.Lerp(from, seat, e);
                position.y += candyMountAscentArc * Mathf.Sin(Mathf.PI * u);
                MoveCandyMount(position, Quaternion.Slerp(candyMountYaw, seatRotation, e), true, u, 0);
            }
            else if (t < candyMountLeave) MoveCandyMount(seat, seatRotation, false, 0, t - candyMountTouch);
            else
            {
                float u = Mathf.InverseLerp(candyMountLeave, candyMountLand, t), e = Mathf.SmoothStep(0, 1, u);
                var position = Vector3.Lerp(seat, candyMountLanding, e);
                position.y += candyMountDescentArc * Mathf.Sin(Mathf.PI * u);
                MoveCandyMount(position, Quaternion.Slerp(seatRotation, candyMountYaw, e), true, u, 0);
                if (t >= candyMountLand) ReportLiveMount(4);
            }
            _mountMotionObserved = true;
            _mountLastPosition = dogRoot.position;
            return true;
        }
    }
}
