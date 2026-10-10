using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Enums;

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
        private int _invitationStation = -1;
        public int _InvitationStation() { return _invitationStation; }
        private float _since;
        private float _requestAt;
        private float _retryAfter;

        // Read-only onboarding observations. Pending uses the request hand; a fresh pickup uses the SDK hand.
        public bool _IntroPending() { return _pending >= 0; }
        public int _IntroHand()
        {
            if (_pending >= 0) return _hand;
            VRCPlayerApi local = Networking.LocalPlayer;
            return Utilities.IsValid(local) && local.IsUserInVR() && pickup != null
                ? (pickup.currentHand == VRC_Pickup.PickupHand.Left ? 0 : 1) : desktopHand;
        }

        private void Start() { _home = transform.position; _rotation = transform.rotation; _started = true; }

        // IMP-DOG-036 S5: a disabled behaviour got no Update, but delayed events still run on a disabled UdonBehaviour (SDK 3.10.5
        // RunProgram has no enabled check). _ClipTick stops itself when disabled (no body) and OnEnable restarts it for a tip that is
        // held or has a request in flight. OnEnable runs before Start on the first activation; nothing is active then.
        private bool _started;
        private void OnEnable()
        {
            if (!_started) return;
            if (pickup != null && (_pending >= 0 || pickup.IsHeld)) _Wake();
        }
        // A re-pick cannot cancel a request already sent to the Owner. ClipStep resolves it or times out.
        public override void OnPickup()
        {
            _candidate = -1;
            _invitationStation = manager != null ? manager._InviteStationAt(_started ? _home : transform.position) : -1;
            if (manager != null) manager._InvitationChanged();
            _Wake();
        }

        // IMP-DOG-036 S5: the frame below used to be Update, which ran every frame for all six tips and returned at once for a tip
        // that is not held and has no attach request in flight. A tip is a local pickup (no sync), so it only becomes active
        // through this client's own OnPickup; the loop runs from there (EventTiming.Update: still the Update phase, after the
        // Update callbacks; it reads the synced walkers / hands, which change only on deserialization, so the order does not
        // matter) until the frame where it is neither held nor pending (the old early return). The behaviour stays enabled.
        private bool _looping;

        /// <summary>Starts the per-frame loop if it is not running. Safe to call any number of times (local; not a network target).</summary>
        public void _Wake()
        {
            if (_looping) return;
            _looping = true;
            SendCustomEventDelayedFrames(nameof(_ClipTick), 1, EventTiming.Update);
        }

        /// <summary>IMP-DOG-036 S5: local loop (delayed event). Not a network target.</summary>
        public void _ClipTick()
        {
            if (!enabled) { _looping = false; return; }   // disabled: no body (OnEnable restarts)
            if (!ClipFrame()) { _looping = false; return; }
            SendCustomEventDelayedFrames(nameof(_ClipTick), 1, EventTiming.Update);
        }
        public override void OnDrop()
        {
            _candidate = -1; _invitationStation = -1;
            if (manager != null) manager._InvitationChanged();
            if (_pending < 0) ReturnHome();
        }
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
            _Wake();   // IMP-DOG-036 S5: the request's resolution / timeout is watched by the loop (already running while held)
            manager._RequestAttach(_pending, _hand);
        }
        // IMP-DOG-036 S5: the old Update body. Returns whether the loop must run next frame: held or a request in flight
        // (every other frame of the old Update returned at "!pickup.IsHeld" with nothing to do).
        private bool ClipFrame()
        {
            ClipStep();
            return pickup != null && (_pending >= 0 || pickup.IsHeld);
        }

        private void ClipStep()
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
