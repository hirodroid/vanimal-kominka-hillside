using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon.Common;
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
        [Tooltip("Below this height the flight ends and VRCObjectSync's respawn returns the disc (set from the scene descriptor by Build)")]
        public float respawnBelowY = -20f;
        public float radius = 0.12f;
        public float spinDegPerSec = 720f;
        public float noseUpDeg = 6f;
        public LayerMask obstacleMask = (1 << 0) | (1 << 11);
        [Header("Throw detection (same shape as KD_BallController)")]
        public float throwSpeedThreshold = 1.5f;
        public int throwCheckFrames = 6;
        [Header("Aim launch (user decision 2026-09-21: trigger + trajectory preview, not a physical throw)")]
        [Tooltip("While held, the holder sees the predicted path and Use launches the disc along it. Off = physical throw / desktop charge")]
        public bool aimLaunch = true;
        [Tooltip("Launch speed at power 0.5. Power (0..1) maps aimSpeedMin..aimSpeedMax: VR right stick up/down, desktop mouse wheel")]
        public float aimSpeed = 7.5f;
        public float aimSpeedMin = 4f, aimSpeedMax = 11f;
        [Tooltip("Power change per second at full stick deflection")]
        public float aimPowerRate = 0.8f;
        [Tooltip("Power change per mouse wheel notch (desktop)")]
        public float aimPowerWheel = 0.08f;
        public float aimMinPitchDeg = -5f, aimMaxPitchDeg = 40f;
        [Tooltip("Swapped in while held by the local player so the preview line shows through the disc")]
        public Renderer discRenderer;
        public Material discMaterial, discHeldMaterial;
        public LineRenderer aimLine;
        public Transform aimMarker;
        [Header("Play area feedback (Codex U/V/P review 2026-09-21; values are provisional until Play)")]
        [Tooltip("Preview materials: inside the play area / landing outside it (grey)")]
        public Material aimMaterial, aimOutsideMaterial;
        public Renderer aimMarkerRenderer;
        [Tooltip("Child of the marker shown only when the landing point is outside (a shape, not just a colour)")]
        public GameObject aimMarkerCross;
        [Tooltip("Ring on the ground around the play area, shown to the holder only")]
        public LineRenderer areaRing;
        public int areaRingPoints = 64;
        public float areaRingLift = 0.03f;
        [Tooltip("Short hint near the marker while holding outside the play area or aiming out of it")]
        public Transform aimHint; // the hint text object (TextMeshPro itself is not exposed to Udon; text is set by Build)
        public int aimPoints = 40;
        public float aimStep = 0.08f;
        [Header("Desktop throw (aimLaunch off)")]
        public float desktopMaxChargeSeconds = 1.0f;
        public float desktopMinThrowSpeed = 6f;
        public float desktopMaxThrowSpeed = 11f;
        public float desktopThrowRise = 0.25f;

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
        private float _aimPower = 0.5f;
        private bool _aimOutside;      // last preview ended outside the play area (material state)
        private bool _ringBuilt;
        private float _lookAxis;
        private bool _heldLocal;
        private bool _flying;
        private Vector3 _p0, _v0;
        private double _t0;
        private float _curve;  // per-throw fact (IMP-DOG-008 v5), captured at launch beside p0 / v0 / the generation
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
        // Sideways (IMP-DOG-008 v5): a = curve * v_h^2 * e^(-2 k t) along `side` (left/right of the throw), so a
        // banked disc drifts sideways as it slows. Unlike the lift, the sideways velocity it builds is horizontal
        // and is dragged like the rest of the horizontal velocity (v_s' = curve v_h^2 e^(-2kt) - k v_s), which is
        // why its integral is not the lift's. curve is 1/m and is a per-throw fact (0 for every preset but the
        // curving one), never read from the Inspector.
        public Vector3 _PositionAt(Vector3 p0, Vector3 v0, float curve, float t)
        {
            if (t < 0f) t = 0f;
            float k = drag;
            float vh2 = LiftSpeedSquared(v0);
            float s = k > 1e-4f ? (1f - Mathf.Exp(-k * t)) / k : t;
            float liftTerm;
            float sideTerm;
            if (k > 1e-4f)
            {
                liftTerm = lift * vh2 * (t / (2f * k) - (1f - Mathf.Exp(-2f * k * t)) / (4f * k * k));
                sideTerm = curve * vh2 / k * ((1f - Mathf.Exp(-k * t)) / k - (1f - Mathf.Exp(-2f * k * t)) / (2f * k));
            }
            else { liftTerm = 0.5f * lift * vh2 * t * t; sideTerm = 0.5f * curve * vh2 * t * t; }
            Vector3 side = SideDirection(v0);
            return new Vector3(p0.x + v0.x * s + side.x * sideTerm, p0.y + v0.y * t - 0.5f * gravity * t * t + liftTerm, p0.z + v0.z * s + side.z * sideTerm);
        }

        public Vector3 _VelocityAt(Vector3 v0, float curve, float t)
        {
            if (t < 0f) t = 0f;
            float k = drag;
            float vh2 = LiftSpeedSquared(v0);
            float e = Mathf.Exp(-k * t);
            float vy = v0.y - gravity * t + lift * vh2 * (k > 1e-4f ? (1f - Mathf.Exp(-2f * k * t)) / (2f * k) : t);
            Vector3 side = SideDirection(v0);
            // Built up by the sideways lift, then dragged like every other horizontal component.
            float sideV = curve * vh2 * (k > 1e-4f ? (e - Mathf.Exp(-2f * k * t)) / k : t);
            return new Vector3(v0.x * e + side.x * sideV, vy, v0.z * e + side.z * sideV);
        }

        // Horizontal right of the launch direction (positive curve = the disc bends right). A throw with no
        // horizontal speed has no side to bend toward, so the term vanishes.
        private Vector3 SideDirection(Vector3 v0)
        {
            Vector3 flat = new Vector3(v0.x, 0f, v0.z);
            if (flat.magnitude < 1e-4f) return Vector3.zero;
            return Vector3.Cross(Vector3.up, flat.normalized);
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
            // The curve is read once, here: a flight already in the air keeps the bend it was launched with even
            // if the preset is switched mid-flight (the dog is told the same number with ReqCatch).
            _curve = catcher != null ? catcher.FlightCurve : 0f;
            _p0 = p0; _v0 = v0; _t0 = Networking.GetServerTimeInSeconds(); _spin = 0f; _flying = true; _gen++;
            _sync.SetKinematic(true);
            transform.position = p0;
            VRCPlayerApi lp = Networking.LocalPlayer;
            int thrower = Utilities.IsValid(lp) ? lp.playerId : -1;
            Debug.Log("[KH_Frisbee] launch v=" + speed.ToString("F2") + " thrower=" + thrower + " curve=" + _curve.ToString("F2"));
            if (catcher != null)
            {
                // net: 6 parameters, within the NetworkCallable limit of 8, all basic types.
                if (Networking.IsOwner(catcher.gameObject)) catcher.ReqCatch(_gen, p0, v0, _t0, thrower, _curve);
                else catcher.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(KH_FrisbeeCatch.ReqCatch), _gen, p0, v0, _t0, thrower, _curve);
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
            _charging = false; _hasPendingThrow = false; _heldLocal = true; _lookAxis = 0f;
            if (discRenderer != null && discHeldMaterial != null) discRenderer.sharedMaterial = discHeldMaterial; // local: the holder sees through it
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

        float AimSpeedNow() { return Mathf.Lerp(aimSpeedMin, aimSpeedMax, _aimPower); }

        // Power input: VR right stick vertical (unused by VRChat in VR), desktop mouse wheel. Both local.
        public override void InputLookVertical(float value, UdonInputEventArgs args) { _lookAxis = value; }

        void UpdateAimPower()
        {
            if (!_heldLocal) return;
            if (_isDesktop)
            {
                float wheel = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(wheel) > 0.0001f) _aimPower = Mathf.Clamp01(_aimPower + Mathf.Sign(wheel) * aimPowerWheel);
            }
            else if (Mathf.Abs(_lookAxis) > 0.2f)
            {
                _aimPower = Mathf.Clamp01(_aimPower + _lookAxis * aimPowerRate * Time.deltaTime);
            }
        }

        // Where a launch from the hand would go right now: the holding hand's pointing direction in VR, the
        // head direction on desktop, pitch clamped so a casual aim still flies.
        Vector3 AimDirection()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return Vector3.forward;
            Vector3 dir;
            if (_isDesktop || _pickup == null) dir = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation * Vector3.forward;
            else
            {
                VRCPlayerApi.TrackingDataType hand = _pickup.currentHand == VRC_Pickup.PickupHand.Left ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand;
                dir = lp.GetTrackingData(hand).rotation * Vector3.forward;
            }
            Vector3 flat = dir; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) flat = lp.GetRotation() * Vector3.forward;
            flat.Normalize();
            float pitch = Mathf.Clamp(Mathf.Atan2(dir.y, Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z)) * Mathf.Rad2Deg, aimMinPitchDeg, aimMaxPitchDeg);
            return Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat)) * flat;
        }

        // Preview for the holder only (local). Same _PositionAt as the flight, stopped at the first obstacle.
        // The play area is the catcher's (the dog's flat); no catcher or radius 0 = no area feedback at all.
        bool HasArea() { return catcher != null && catcher.playAreaRadius > 0f; }
        bool InArea(Vector3 p)
        {
            if (!HasArea()) return true;
            Vector3 d = p - catcher.playAreaCenter; d.y = 0f;
            return d.magnitude <= catcher.playAreaRadius;
        }

        // Ring = the range the dog may run in (same circle the catcher checks; no margin, so the ring never
        // promises more than the dog does). Built once per pickup from ground hits, shown to the holder only.
        void BuildRing()
        {
            if (areaRing == null || !HasArea()) return;
            int n = areaRingPoints < 8 ? 8 : areaRingPoints;
            areaRing.positionCount = n; areaRing.loop = true;
            RaycastHit hit;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                Vector3 p = catcher.playAreaCenter + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * catcher.playAreaRadius;
                float y = catcher.playAreaCenter.y;
                if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out hit, 8f, catcher.groundLayerMask, QueryTriggerInteraction.Ignore)) y = hit.point.y;
                areaRing.SetPosition(i, new Vector3(p.x, y + areaRingLift, p.z));
            }
            _ringBuilt = true;
        }

        void SetAimOutside(bool outside)
        {
            if (outside == _aimOutside) return;
            _aimOutside = outside;
            Material m = outside && aimOutsideMaterial != null ? aimOutsideMaterial : aimMaterial;
            if (m != null)
            {
                if (aimLine != null) aimLine.sharedMaterial = m;
                if (aimMarkerRenderer != null) aimMarkerRenderer.sharedMaterial = m;
            }
            if (aimMarkerCross != null) aimMarkerCross.SetActive(outside);
        }

        void UpdateAim()
        {
            bool show = aimLaunch && _pickup != null && _pickup.IsHeld && Networking.IsOwner(gameObject) && aimLine != null;
            if (!show)
            {
                if (aimLine != null && aimLine.enabled) aimLine.enabled = false;
                if (aimMarker != null && aimMarker.gameObject.activeSelf) aimMarker.gameObject.SetActive(false);
                if (areaRing != null && areaRing.enabled) { areaRing.enabled = false; _ringBuilt = false; }
                if (aimHint != null && aimHint.gameObject.activeSelf) aimHint.gameObject.SetActive(false);
                return;
            }
            if (areaRing != null && HasArea())
            {
                if (!_ringBuilt) BuildRing();
                if (!areaRing.enabled) areaRing.enabled = true;
            }
            UpdateAimPower();
            Vector3 p0 = transform.position;
            Vector3 v0 = AimDirection() * AimSpeedNow();
            // Live, not latched: the line bends the moment the curving preset is selected (the flight latches
            // its own value in _Launch).
            float curve = catcher != null ? catcher.FlightCurve : 0f;
            int n = aimPoints < 2 ? 2 : aimPoints;
            aimLine.positionCount = n;
            Vector3 prev = p0; Vector3 end = p0; int count = 0;
            RaycastHit hit;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = _PositionAt(p0, v0, curve, i * aimStep);
                Vector3 d = p - prev; float len = d.magnitude;
                if (i > 0 && len > 1e-4f && Physics.Raycast(prev, d / len, out hit, len, obstacleMask, QueryTriggerInteraction.Ignore)) { p = hit.point; aimLine.SetPosition(i, p); count = i + 1; end = p; break; }
                aimLine.SetPosition(i, p); count = i + 1; end = p; prev = p;
            }
            aimLine.positionCount = count;
            if (!aimLine.enabled) aimLine.enabled = true;
            if (aimMarker != null)
            {
                if (!aimMarker.gameObject.activeSelf) aimMarker.gameObject.SetActive(true);
                aimMarker.position = end + Vector3.up * 0.02f;
                aimMarker.rotation = Quaternion.identity;
            }
            // Outside: the path is still drawn to where the disc really lands (never cut short), but grey with a
            // cross on the marker, and a one-line hint says why the dog will not come.
            bool outside = HasArea() && (!InArea(end) || !InArea(p0));
            SetAimOutside(outside);
            if (aimHint != null)
            {
                if (aimHint.gameObject.activeSelf != outside) aimHint.gameObject.SetActive(outside);
                if (outside)
                {
                    VRCPlayerApi lp = Networking.LocalPlayer;
                    Vector3 headPos = Utilities.IsValid(lp) ? lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position : p0 + Vector3.up;
                    Vector3 at = end + Vector3.up * 0.5f;
                    aimHint.position = at;
                    Vector3 face = at - headPos; face.y = 0f;
                    if (face.sqrMagnitude > 1e-4f) aimHint.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
                }
            }
        }

        public override void OnPickupUseDown()
        {
            if (aimLaunch)
            {
                if (_pickup == null || !_pickup.IsHeld) return;
                _pendingThrowVelocity = AimDirection() * AimSpeedNow();
                _hasPendingThrow = true;
                _pickup.Drop(); // Drop -> _CheckThrow applies the aimed velocity a frame later (after the SDK's own drop velocity)
                return;
            }
            if (!_isDesktop) return;
            _charging = true; _chargeStartTime = Time.time;
        }

        public override void OnPickupUseUp()
        {
            if (aimLaunch || !_isDesktop || !_charging) return;
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
            _heldLocal = false;
            if (discRenderer != null && discMaterial != null) discRenderer.sharedMaterial = discMaterial;
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
            if (!_flying) { UpdateAim(); return; }
            if (!Networking.IsOwner(gameObject)) { _flying = false; return; }
            float t = _ElapsedSince(_t0);
            Vector3 prev = transform.position;
            Vector3 next = _PositionAt(_p0, _v0, _curve, t);
            Vector3 vel = _VelocityAt(_v0, _curve, t);
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
            if (next.y < respawnBelowY) { EndFlight(next, Vector3.zero, EndTimeout); return; } // out of the world: let Respawn bring it home
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
            // BUG-DOG-003: the catcher matches an end notice against the thrower it is holding (the generation
            // counter is per client), so it has to be told who sent this one, and WHEN it happened - a grab from
            // before the throw it would end must not cancel it (Codex D4, 2nd review). net: 5 parameters, still
            // within the NetworkCallable limit of 8, all basic types.
            VRCPlayerApi lp = Networking.LocalPlayer;
            int sender = Utilities.IsValid(lp) ? lp.playerId : -1;
            double opTime = Networking.GetServerTimeInSeconds();
            if (Networking.IsOwner(catcher.gameObject)) catcher.ReqFlightEnded(_gen, reason, pos, sender, opTime);
            else catcher.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(KH_FrisbeeCatch.ReqFlightEnded), _gen, reason, pos, sender, opTime);
        }
    }
}
