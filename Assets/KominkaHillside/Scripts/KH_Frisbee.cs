using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // IMP-DOG-005 prototype disc. Once thrown, the Owner steps the disc along a closed-form curve
    // (kinematic; VRCObjectSync distributes the transform). The dog predicts with the very same
    // function (_PositionAt), so flight and prediction cannot disagree on the thrower's client.
    // No synced variables: VRCObjectSync shares this GameObject (same reason as KD_BallController).
    // Launch facts reach the dog Owner as a network event with parameters; flight end as another.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class KH_Frisbee : UdonSharpBehaviour
    {
        public KH_FrisbeeCatch catcher;
        [Header("Flight model (provisional; tune in Play)")]
        public float gravity = 9.81f;
        [Tooltip("1/s. Horizontal speed decays as e^(-drag*t); lift decays as e^(-2 drag t)")]
        public float drag = 0.15f;
        [Tooltip("1/m. Upward acceleration = lift * horizontalSpeed^2 (capped), so a fast disc floats and sinks as it slows. " +
            "With 0.3 anything above ~5.6 m/s starts at full lift; the first Play (drag 0.30 / lift 0.12) dropped an 8 m/s throw in 1.2 s")]
        public float lift = 0.30f;
        [Tooltip("Lift never exceeds this fraction of gravity (a hard flat throw must not climb)")]
        public float liftCap = 0.95f;
        public float maxSpeed = 11f;
        public float maxFlightSeconds = 8f;
        public float radius = 0.12f;
        public float spinDegPerSec = 720f;
        public float noseUpDeg = 6f;
        public LayerMask obstacleMask = (1 << 0) | (1 << 11);
        [Header("Throw detection (same shape as KD_BallController)")]
        public float throwSpeedThreshold = 1.5f;
        public int throwCheckFrames = 6;
        [Header("Desktop throw")]
        public float desktopMaxChargeSeconds = 1.0f;
        public float desktopMinThrowSpeed = 4f;
        public float desktopMaxThrowSpeed = 10f;
        public float desktopThrowRise = 0.15f;

        public const int EndUnknown = 0, EndWall = 1, EndLanded = 2, EndTimeout = 3, EndGrabbed = 4, EndCaught = 5;

        private Rigidbody _rb;
        private VRC_Pickup _pickup;
        private VRCObjectSync _sync;
        private bool _isDesktop;
        private bool _charging;
        private float _chargeStartTime;
        private bool _hasPendingThrow;
        private Vector3 _pendingThrowVelocity;
        private int _checksRemaining;
        private float _maxObservedSpeed;

        // Flight (meaningful on the Owner while _flying; other clients only see the synced transform).
        private bool _flying;
        private Vector3 _p0, _v0;
        private double _t0;
        private float _spin;
        private int _gen;      // throw generation (this Owner's count; the catcher matches start and end notices)

        public bool IsFlying { get { return _flying; } }
        public bool IsHeldNow { get { return _pickup != null && _pickup.IsHeld; } }
        // Holder (synced Owner while held). Read by the dog to take its stand-off position.
        public VRCPlayerApi HolderPlayer
        {
            get
            {
                if (_pickup == null || !_pickup.IsHeld) return null;
                VRCPlayerApi p = Networking.GetOwner(gameObject);
                return Utilities.IsValid(p) ? p : null;
            }
        }

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            _sync = (VRCObjectSync)GetComponent(typeof(VRCObjectSync));
            VRCPlayerApi lp = Networking.LocalPlayer;
            _isDesktop = Utilities.IsValid(lp) && !lp.IsUserInVR();
            if (_isDesktop && _pickup != null) _pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
        }

        // ---- Closed-form flight. Pure: callers pass the launch facts they hold. ----
        // Horizontal: v = v0 e^(-k t). Vertical: a = -g + L v_h^2 (capped), integrated in closed form.
        public Vector3 _PositionAt(Vector3 p0, Vector3 v0, float t)
        {
            if (t < 0f) t = 0f;
            float k = drag;
            float vh2 = LiftSpeedSquared(v0);
            float s = k > 1e-4f ? (1f - Mathf.Exp(-k * t)) / k : t;
            float liftTerm;
            if (k > 1e-4f) liftTerm = lift * vh2 * (t / (2f * k) - (1f - Mathf.Exp(-2f * k * t)) / (4f * k * k));
            else liftTerm = 0.5f * lift * vh2 * t * t;
            return new Vector3(p0.x + v0.x * s, p0.y + v0.y * t - 0.5f * gravity * t * t + liftTerm, p0.z + v0.z * s);
        }

        public Vector3 _VelocityAt(Vector3 v0, float t)
        {
            if (t < 0f) t = 0f;
            float k = drag;
            float vh2 = LiftSpeedSquared(v0);
            float e = Mathf.Exp(-k * t);
            float vy = v0.y - gravity * t + (k > 1e-4f ? lift * vh2 * (1f - Mathf.Exp(-2f * k * t)) / (2f * k) : lift * vh2 * t);
            return new Vector3(v0.x * e, vy, v0.z * e);
        }

        private float LiftSpeedSquared(Vector3 v0)
        {
            float vh2 = v0.x * v0.x + v0.z * v0.z;
            float cap = lift > 1e-6f ? liftCap * gravity / lift : 0f;
            return vh2 > cap ? cap : vh2;
        }

        // Elapsed flight time from synced server time (same clock on every client).
        public float _ElapsedSince(double launchServerTime)
        {
            return (float)(Networking.GetServerTimeInSeconds() - launchServerTime);
        }

        // ---- Launch (Owner only). Used by the thrower's client and by the fixed launcher. ----
        public bool _Launch(Vector3 p0, Vector3 v0)
        {
            if (!Networking.IsOwner(gameObject) || _sync == null) return false;
            float speed = v0.magnitude;
            if (speed < 0.01f) return false;
            if (speed > maxSpeed) v0 = v0 * (maxSpeed / speed);
            _p0 = p0; _v0 = v0; _t0 = Networking.GetServerTimeInSeconds(); _spin = 0f; _flying = true; _gen++;
            _sync.SetKinematic(true);
            transform.position = p0;
            VRCPlayerApi lp = Networking.LocalPlayer;
            int thrower = Utilities.IsValid(lp) ? lp.playerId : -1;
            Debug.Log("[KH_Frisbee] launch v=" + speed.ToString("F2") + " thrower=" + thrower);
            if (catcher != null)
            {
                if (Networking.IsOwner(catcher.gameObject)) catcher.ReqCatch(_gen, p0, v0, _t0, thrower);
                else catcher.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(KH_FrisbeeCatch.ReqCatch), _gen, p0, v0, _t0, thrower);
            }
            return true;
        }

        // The dog Owner took the disc at the mouth. It is the disc Owner by then (SetOwner in the catcher).
        public void _Caught()
        {
            _flying = false;
            if (_sync != null) _sync.SetKinematic(true);
            if (_rb != null && !_rb.isKinematic) { _rb.velocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
        }

        // Released from the mouth (Owner of the disc).
        public void _Release(Vector3 velocity)
        {
            _flying = false;
            if (_sync != null) _sync.SetKinematic(false);
            if (_rb != null && !_rb.isKinematic) { _rb.velocity = velocity; _rb.angularVelocity = Vector3.zero; }
        }

        // Launcher recall (disc Owner): the flight is over for the dog as if a hand took it.
        public void _Recall()
        {
            if (!Networking.IsOwner(gameObject)) return;
            bool wasFlying = _flying;
            _flying = false;
            if (_sync != null) _sync.SetKinematic(false);
            if (_rb != null && !_rb.isKinematic) { _rb.velocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
            if (wasFlying || (catcher != null && !catcher.IsIdle)) NotifyEnd(EndGrabbed); // gen may not match: accepted by reason
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // Only the Owner steps the flight; a transfer mid-air (grab, catch, launcher) ends it here.
            if (!Networking.IsOwner(gameObject)) { _flying = false; return; }
            // Inherited a kinematic disc that nobody holds or carries (previous Owner left mid-flight): let it fall.
            if (_flying || (_pickup != null && _pickup.IsHeld) || (catcher != null && catcher.IsCarrying)) return;
            if (_rb != null && _rb.isKinematic && _sync != null) _sync.SetKinematic(false);
        }

        public override void OnPickup()
        {
            _charging = false; _hasPendingThrow = false;
            bool wasFlying = _flying;
            _flying = false;
            if (_sync != null)
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (Utilities.IsValid(lp) && !Networking.IsOwner(gameObject)) Networking.SetOwner(lp, gameObject); // net: setowner-ok: disc only (grabber)
                _sync.SetKinematic(false);
            }
            if (wasFlying) NotifyEnd(EndGrabbed);
        }

        public override void OnPickupUseDown()
        {
            if (!_isDesktop) return;
            _charging = true; _chargeStartTime = Time.time;
        }

        public override void OnPickupUseUp()
        {
            if (!_isDesktop || !_charging) return;
            _charging = false;
            if (_pickup == null || !_pickup.IsHeld) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 dir = (head.rotation * Vector3.forward) + (Vector3.up * desktopThrowRise);
            if (dir.sqrMagnitude < 0.0001f) return;
            float charge = Mathf.Clamp01((Time.time - _chargeStartTime) / Mathf.Max(0.01f, desktopMaxChargeSeconds));
            _pendingThrowVelocity = dir.normalized * Mathf.Lerp(desktopMinThrowSpeed, desktopMaxThrowSpeed, charge);
            _hasPendingThrow = true;
            _pickup.Drop();
        }

        public override void OnDrop()
        {
            _checksRemaining = throwCheckFrames; _maxObservedSpeed = 0f;
            SendCustomEventDelayedFrames(nameof(_CheckThrow), 1);
        }

        public void _CheckThrow()
        {
            if (_rb == null) return;
            if (_pickup != null && _pickup.IsHeld) { _hasPendingThrow = false; return; }
            Vector3 v = _rb.velocity;
            if (_hasPendingThrow) { v = _pendingThrowVelocity; _hasPendingThrow = false; }
            float speed = v.magnitude;
            if (speed > _maxObservedSpeed) _maxObservedSpeed = speed;
            if (speed >= throwSpeedThreshold)
            {
                if (!_Launch(transform.position, v)) Debug.Log("[KH_Frisbee] launch refused (not owner)");
                return;
            }
            _checksRemaining--;
            if (_checksRemaining > 0) { SendCustomEventDelayedFrames(nameof(_CheckThrow), 1); return; }
            Debug.Log("[KH_Frisbee] drop ignored (max v=" + _maxObservedSpeed.ToString("F2") + ")");
        }

        void Update()
        {
            if (!_flying) return;
            if (!Networking.IsOwner(gameObject)) { _flying = false; return; }
            float t = _ElapsedSince(_t0);
            Vector3 prev = transform.position;
            Vector3 next = _PositionAt(_p0, _v0, t);
            Vector3 vel = _VelocityAt(_v0, t);
            Vector3 d = next - prev;
            float len = d.magnitude;
            RaycastHit hit;
            if (len > 1e-4f && Physics.Raycast(prev, d / len, out hit, len + radius * 0.6f, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                // Wall or ground in the way: stop there and drop (no bounce back into the air).
                bool ground = hit.normal.y > 0.6f;
                EndFlight(hit.point + hit.normal * radius * 0.6f, ground ? vel * 0.25f : Vector3.zero, ground ? EndLanded : EndWall);
                return;
            }
            if (Physics.Raycast(next + Vector3.up * 0.05f, Vector3.down, out hit, radius * 0.5f + 0.05f, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                EndFlight(hit.point + Vector3.up * 0.02f, vel * 0.25f, EndLanded);
                return;
            }
            transform.position = next;
            Vector3 heading = vel; heading.y = 0f;
            _spin += spinDegPerSec * Time.deltaTime;
            if (heading.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up) * Quaternion.Euler(-noseUpDeg, 0f, 0f) * Quaternion.Euler(0f, _spin, 0f);
            if (t > maxFlightSeconds) EndFlight(next, Vector3.zero, EndTimeout);
        }

        private void EndFlight(Vector3 pos, Vector3 velocity, int reason)
        {
            _flying = false;
            transform.position = pos;
            if (_sync != null) _sync.SetKinematic(false);
            if (_rb != null && !_rb.isKinematic) { _rb.velocity = velocity; _rb.angularVelocity = Vector3.zero; }
            NotifyEnd(reason);
        }

        private void NotifyEnd(int reason)
        {
            if (catcher == null) return;
            Vector3 pos = transform.position;
            if (Networking.IsOwner(catcher.gameObject)) catcher.ReqFlightEnded(_gen, reason, pos);
            else catcher.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(KH_FrisbeeCatch.ReqFlightEnded), _gen, reason, pos);
        }
    }
}
