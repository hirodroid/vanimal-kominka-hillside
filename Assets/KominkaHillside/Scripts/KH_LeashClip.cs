using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // Local pickup: every player can physically attach a tip; the shared manager arbitrates ownership.
    // Tools live at leash stations. After an attach, a drop or a timeout the tool goes to the nearest
    // station with a free slot of its side, never to the ground where it was released.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_LeashClip : UdonSharpBehaviour
    {
        public KH_LeashManager manager;
        public VRC_Pickup pickup;
        public Transform tip;
        public Renderer indicator;
        public Material readyMaterial;
        public Material blockedMaterial;
        public int desktopHand = 1;
        public float attachRadius = 0.22f;
        public float dwellSeconds = 0.25f;
        private Vector3 _home;
        private Quaternion _rotation;
        private int _candidate = -1;
        private int _pending = -1;
        private int _hand;
        private float _since;
        private float _requestAt;
        private float _retryAfter;

        private void Start() { _home = transform.position; _rotation = transform.rotation; }
        public override void OnPickup() { _candidate = -1; _pending = -1; }
        public override void OnDrop() { _candidate = -1; if (_pending < 0) ReturnHome(); }
        private void ReturnHome()
        {
            if (manager != null) _home = manager._ReturnSlot(transform.position, desktopHand, this);
            transform.SetPositionAndRotation(_home, _rotation);
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }
        public override void OnPickupUseDown() { if (_candidate >= 0 && _pending < 0) Connect(); }
        private void Connect()
        {
            if (!pickup.IsHeld || Time.time < _retryAfter) return;
            _pending = _candidate; _requestAt = Time.time;
            manager._RequestAttach(_pending, _hand);
        }
        private void Update()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (manager == null || pickup == null || !Utilities.IsValid(local)) return;
            if (_pending >= 0)
            {
                if (manager.walkers[_pending] == local.playerId && manager.hands[_pending] == _hand)
                {
                    _pending = -1; pickup.Drop(); ReturnHome(); _retryAfter = Time.time + 0.5f;
                }
                else if (Time.time - _requestAt > 2f)
                {
                    _pending = -1; _candidate = -1; _retryAfter = Time.time + 1f;
                    if (!pickup.IsHeld) ReturnHome();
                }
                return;
            }
            if (!pickup.IsHeld) return;
            _hand = local.IsUserInVR() ? (pickup.currentHand == VRC_Pickup.PickupHand.Left ? 0 : 1) : desktopHand;
            int count = 0;
            for (int i = 0; i < manager.walkers.Length; i++) if (manager.walkers[i] == local.playerId && manager.hands[i] == _hand) count++;
            int nearest = -1; float distance = attachRadius;
            if (count < 3) for (int i = 0; i < manager.collarAnchors.Length; i++)
            {
                if (manager._IsLeashed(i) || manager.collarAnchors[i] == null || manager.fetchers[i] == null || !manager.fetchers[i].IsIdle) continue;
                float d = Vector3.Distance(tip.position, manager.collarAnchors[i].position);
                if (d < distance) { distance = d; nearest = i; }
            }
            if (indicator != null) indicator.sharedMaterial = nearest >= 0 ? readyMaterial : blockedMaterial;
            if (nearest != _candidate) { _candidate = nearest; _since = Time.time; }
            if (_candidate >= 0 && Time.time - _since >= dwellSeconds) Connect();
        }
    }
}
