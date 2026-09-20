using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_BedroomPhotoDirector : UdonSharpBehaviour
    {
        public KD_PhotoCapture capture;
        public KD_GalleryController gallery;
        public KD_SleepAudio room;
        public Transform[] cameraPositions;
        public Transform floor;
        public Transform[] dogRoots;
        public Transform[] dogHeads;
        public KD_DogWanderController[] wander;
        public KD_DogPetController[] pet;
        public float togetherSeconds = 150f;
        public float petSeconds = 45f;
        public float reclineSeconds = 90f;
        public float minimumInterval = 30f;
        public float settleSeconds = 8f;
        public float reclineHeight = .5f;
        public float yawLimit = 65f;
        public float pitchLimit = 40f;
        [System.NonSerialized] public Vector3 requestedSubject;
        [System.NonSerialized] public bool requestedLow;
        private float _nextTick;
        private float _lastShot = -1000f;
        private float _lastTogether = -1000f;
        private float _lastPet = -1000f;
        private float _lastRecline = -1000f;
        private float _settledAt = -1f;
        private float _petAt = -1f;
        private float _reclineAt = -1f;
        private int _dog = -1;
        private bool _wasAsleep;

        private void Update()
        {
            if (capture != null && capture.settings != null && !capture.settings.CaptureAllowed)
            {
                _dog = -1; _settledAt = -1f; _petAt = -1f; _reclineAt = -1f;
                return;
            }
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + .25f;
            var lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || room == null || room.roomBounds == null) return;
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            int nearest = -1;
            float distance = float.MaxValue;
            if (room.GainAt(head) <= 0f || room.GainAt(lp.GetPosition()) <= 0f)
                for (int i = 0; dogRoots != null && i < dogRoots.Length; i++)
                {
                    if (dogRoots[i] == null || wander == null || i >= wander.Length || wander[i] == null
                        || room.GainAt(dogRoots[i].position) > 0f) continue;
                    float d = (head - dogRoots[i].position).sqrMagnitude;
                    if (wander[i].IsAsleep) d -= 100f;
                    if (wander[i].IsAsleep && pet != null && i < pet.Length && pet[i] != null && pet[i].IsPetting) d -= 1000f;
                    if (d < distance) { distance = d; nearest = i; }
                }
            if (nearest != _dog)
            {
                _dog = nearest; _settledAt = nearest < 0 ? -1f : Time.time;
                _petAt = -1f; _reclineAt = -1f;
            }
            if (nearest < 0) return;
            bool asleep = wander[nearest].IsAsleep;
            if (asleep != _wasAsleep) { _settledAt = Time.time; _wasAsleep = asleep; }
            bool petting = asleep && pet != null && nearest < pet.Length && pet[nearest] != null && pet[nearest].IsPetting;
            float height = floor == null ? float.MaxValue : head.y - floor.position.y;
            bool reclining = height >= 0f && height <= reclineHeight;
            if (!petting) _petAt = -1f; else if (_petAt < 0f) _petAt = Time.time;
            if (!reclining) _reclineAt = -1f; else if (_reclineAt < 0f) _reclineAt = Time.time;
            int kind = _petAt >= 0f && Time.time - _petAt >= 3f && Time.time - _lastPet >= petSeconds ? 1
                : _reclineAt >= 0f && Time.time - _reclineAt >= 3f && Time.time - _lastRecline >= reclineSeconds ? 2
                : asleep && Time.time - _settledAt >= settleSeconds && Time.time - _lastTogether >= togetherSeconds ? 3 : 0;
            if (kind == 0) return;
            Vector3 dogHead = dogHeads != null && nearest < dogHeads.Length && dogHeads[nearest] != null
                ? dogHeads[nearest].position : dogRoots[nearest].position + Vector3.up * .2f;
            requestedSubject = Vector3.Lerp(dogHead, head, .5f);
            requestedLow = true;
            if (!_TryCapture()) return;
            if (kind == 1) _lastPet = Time.time;
            if (kind == 2) _lastRecline = Time.time;
            _lastTogether = Time.time;
        }

        public bool _TryCapture()
        {
            if (capture == null || !capture.CanCapture || gallery == null || room == null
                || room.GainAt(requestedSubject) > 0f || Time.time - _lastShot < minimumInterval
                || cameraPositions == null || cameraPositions.Length < 2) return false;
            Transform anchor = cameraPositions[requestedLow ? 0 : 1];
            if (anchor == null || room.GainAt(anchor.position) > 0f) return false;
            Vector3 local = anchor.InverseTransformDirection(requestedSubject - anchor.position);
            if (local.sqrMagnitude < .0001f) return false;
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -yawLimit, yawLimit);
            float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -pitchLimit, pitchLimit);
            RenderTexture target = gallery._AcquireNextSlot(0);
            if (target == null) return false;
            capture.photoCamera.transform.SetPositionAndRotation(anchor.position, anchor.rotation * Quaternion.Euler(pitch, yaw, 0f));
            if (!capture._CaptureTo(target)) return false;
            _lastShot = Time.time;
            return true;
        }
    }
}
