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
        [Tooltip("IMP-DOG-020 段階 7: 川の帯に落ちたら岸へ戻す。null = 判定なし (試作シーン)。Course/89 が配線する")]
        public KH_RiverData riverData;
        public KH_WaterSpot waterSpot;
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
        [Tooltip("IMP-DOG-018: the dog face prints (FaceTop / FaceBottom). Hidden for the holder while held, like the see-through disc (set by Build)")]
        public Renderer[] faceRenderers;
        public LineRenderer aimLine;
        public Transform aimMarker;
        [Header("Play area feedback (Codex U/V/P review 2026-09-21; values are provisional until Play)")]
        [Tooltip("Preview materials: inside the play area / landing outside it (grey)")]
        public Material aimMaterial, aimOutsideMaterial;
        public Renderer aimMarkerRenderer;
        [Tooltip("Child of the marker shown only when the landing point is outside (a shape, not just a colour)")]
        public GameObject aimMarkerCross;
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
        // DES-DOG-005 C-4: the cross is judged at this rate and reused in between (the holder's client only).
        const float AimJudgeInterval = 0.1f;
        private float _aimJudgeNext;
        private bool _aimBad;
        private float _lookAxis;
        private bool _heldLocal;
        private bool _flying;
        private Vector3 _p0, _v0;
        private double _t0;
        private float _curve;  // per-throw fact (IMP-DOG-008 v5), captured at launch beside p0 / v0 / the generation
        private float _spin;
        private int _gen;      // throw generation (this Owner's count; the catcher matches start and end notices)
        // DES-DOG-005 D-2 (M2): floating in the river / the water-play spot, as KH_ToyRecall floats a ball (Owner only; the
        // physics - gravity off, velocity - reach everyone and late joiners through VRCObjectSync, nothing new is synced).
        private bool _floating;       // this Owner floated it (local history; the gravity itself is reconciled on every look)
        private bool _waterRefused;   // this flight went under the surface where it does not float (checked once per flight)

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
            _p0 = p0; _v0 = v0; _t0 = Networking.GetServerTimeInSeconds(); _spin = 0f; _flying = true; _gen++; _waterRefused = false;
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
            // D-2: in the mouth = normal physics once let go. Only the disc's Owner writes it (Codex M2 review 2: ClaimDisc does not
            // wait for the ownership; a transfer that arrives later re-applies it in OnOwnershipTransferred)
            if (Networking.IsOwner(gameObject)) StopFloat();
        }

        // Released from the mouth (Owner of the disc).
        public void _Release(Vector3 velocity)
        {
            _flying = false;
            if (_sync != null) _sync.SetKinematic(false);
            // D-2 (the ball's RC-09, KD_DogFetchController.ReleaseBall): gravity follows where it is let go, at once - on land it
            // falls, in floating water it floats (CheckRiver holds it from its next look). M2 never releases in the water (M3 does).
            _floating = false;
            if (_sync != null && Networking.IsOwner(gameObject)) _sync.SetGravity(!FloatsHere(transform.position));   // Owner only (review 2)
            if (_rb != null && !_rb.isKinematic) { _rb.velocity = velocity; _rb.angularVelocity = Vector3.zero; }
        }

        // Launcher recall (disc Owner): the flight is over for the dog as if a hand took it.
        public void _Recall()
        {
            if (!Networking.IsOwner(gameObject)) return;
            bool wasFlying = _flying;
            _flying = false;
            if (_sync != null) _sync.SetKinematic(false);
            StopFloat();   // D-2: the menu recall / the park lands it with gravity at once (KH_ToyRecall.Place / Park)
            if (_rb != null && !_rb.isKinematic) { _rb.velocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
            if (wasFlying || (catcher != null && !catcher.IsIdle)) NotifyEnd(EndGrabbed); // gen may not match: accepted by reason
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // Only the Owner steps the flight; a transfer mid-air (grab, catch, launcher) ends it here.
            if (!Networking.IsOwner(gameObject)) { _flying = false; return; }
            // DES-DOG-005 M2 (Codex M2 review 2): the dog's claim arrived after _Caught ran as a non-Owner - the gravity it could not
            // write is written now (a disc in a mouth has gravity; it floats again only after the release, by position).
            if (catcher != null && catcher.IsCarrying && !_flying) { _floating = false; if (_sync != null) _sync.SetGravity(true); }
            // Inherited a kinematic disc that nobody holds or carries (previous Owner left mid-flight): let it fall.
            if (_flying || (_pickup != null && _pickup.IsHeld) || (catcher != null && catcher.IsCarrying)) return;
            if (_rb != null && _rb.isKinematic && _sync != null) _sync.SetKinematic(false);
        }

        public override void OnPickup()
        {
            _charging = false; _hasPendingThrow = false; _heldLocal = true; _lookAxis = 0f;
            if (discRenderer != null && discHeldMaterial != null) discRenderer.sharedMaterial = discHeldMaterial; // local: the holder sees through it
            SetFacesVisible(false); // local: the opaque face prints would block the see-through disc
            bool wasFlying = _flying;
            _flying = false;
            if (_sync != null)
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (Utilities.IsValid(lp) && !Networking.IsOwner(gameObject)) Networking.SetOwner(lp, gameObject); // net: setowner-ok: disc only (grabber)
                _sync.SetKinematic(false);
                _sync.SetGravity(true);   // D-2: taken out of the water, thrown with gravity (as KD_BallController.OnPickup)
            }
            _floating = false;
            if (wasFlying) NotifyEnd(EndGrabbed);
        }

        // IMP-DOG-018: local only (Renderer.enabled is not synced). Null entries are skipped.
        void SetFacesVisible(bool visible)
        {
            if (faceRenderers == null) return;
            for (int i = 0; i < faceRenderers.Length; i++)
            {
                Renderer r = faceRenderers[i];
                if (r != null) r.enabled = visible;
            }
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
        // DES-DOG-005 C-4 / M4: the cross is decided by the catcher's Reachable / PlayableAt. The play-area circle and its ring
        // (HasArea / InArea / BuildRing / areaRing) were removed in M4; Course/92 deletes the ring GameObjects.

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
                if (aimHint != null && aimHint.gameObject.activeSelf) aimHint.gameObject.SetActive(false);
                _aimJudgeNext = 0f;   // DES-DOG-005: the next hold judges its cross on its first frame
                return;
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
            // cross on the marker, and a one-line hint says why the dog will not come. DES-DOG-005 C-4: "outside" is
            // !Reachable(predicted landing, dog) or !PlayableAt(throw point), judged at 10 Hz and reused in between
            // (<= 28 rays x 10 Hz, the holder's client only). Jumpable-or-not is NOT shown (15-25 deg, bridge decks).
            if (catcher != null && Time.time >= _aimJudgeNext)
            {
                _aimJudgeNext = Time.time + AimJudgeInterval;
                _aimBad = !catcher._PlayableAt(p0) || !catcher._Reachable(end);
            }
            bool outside = catcher != null && _aimBad;
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
            SetFacesVisible(true);
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

        // IMP-DOG-020 段階 7 (Codex W6 D-中): 飛行の終わり以外で帯に入ったディスク (投球速度未満の落下・壁で止まった後・岸から転がり込んだ) も、
        // Owner が 0.25 s ごとに見て岸へ戻す (KH_ToyRecall.CheckRiverBalls の作法。ディスクは ToyRecall に載せない方針なのでここで持つ)
        const float RiverCheckInterval = 0.25f;
        private float _nextRiverAt;

        void Update()
        {
            if (!_flying) { UpdateAim(); CheckRiver(); return; }
            if (!Networking.IsOwner(gameObject)) { _flying = false; return; }
            float t = _ElapsedSince(_t0);
            Vector3 prev = transform.position;
            Vector3 next = _PositionAt(_p0, _v0, _curve, t);
            Vector3 vel = _VelocityAt(_v0, _curve, t);
            Vector3 d = next - prev;
            float len = d.magnitude;
            RaycastHit hit;
            // DES-DOG-005 D-2 (M2): under the water surface where a disc floats = landed on the water, at the surface (no new end
            // reason). Cheapest first: in the water (half width), below the surface, then FloatsHere (the slice search) once per
            // flight - a flight that went under where it does not float (bridge zone, no bank) keeps its old ending on the riverbed.
            if (!_waterRefused && riverData != null && riverData._InBand(next, 0f))
            {
                float surface = riverData._SurfaceY(next.z);
                if (next.y < surface)
                {
                    Vector3 w = next;
                    if (prev.y > surface && prev.y - next.y > 1e-5f) w = Vector3.Lerp(prev, next, (prev.y - surface) / (prev.y - next.y));
                    w.y = surface;
                    if (FloatsHere(w))
                    {
                        // A wall or a bank before the water still wins (the ray below finds it on this same frame)
                        Vector3 dw = w - prev; float lw = dw.magnitude;
                        if (lw <= 1e-4f || !Physics.Raycast(prev, dw / lw, lw, obstacleMask, QueryTriggerInteraction.Ignore))
                        {
                            EndFlight(w, Vector3.zero, EndLanded);
                            return;
                        }
                    }
                    else _waterRefused = true;
                }
            }
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
            // DES-DOG-005 D-2 (M2, 0-6 reordered): landed where a disc floats (river water with a usable bank within reach, or the
            // water-play spot's water) = it floats there, BankFor is not reached. IMP-DOG-020 段階 7: the rest of the band (bridge
            // zones, stretches without a bank, the band-edge slope; D-6) still sends it to the nearer bank. The water surface has no
            // collider, so a landing found on the riverbed is lifted to the float height here.
            bool moved = false, floatNow = false;
            int kind = reason == EndLanded ? FloatKind(pos) : FloatNone;
            if (kind != FloatNone)
            {
                floatNow = true;
                float fy = FloatY(pos);
                if (pos.y < fy) pos.y = fy;
                velocity = Vector3.zero;
                Debug.Log("[KH_Frisbee] landed in the water; floating at " + pos + (kind == FloatSpot ? " (water spot)" : " (river)") + " disc=" + gameObject.name);
            }
            else if (reason == EndLanded && InRiverBand(pos))
            {
                pos = BankFor(pos); velocity = Vector3.zero; moved = true;
                Debug.Log("[KH_Frisbee] landed in the river band; moved to the bank " + pos + " disc=" + gameObject.name);
            }
            transform.position = pos;
            if (_sync != null) _sync.SetKinematic(false);
            if (floatNow) { transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f); FloatDisc(pos, kind == FloatSpot); }
            else
            {
                if (_floating || GravityOff()) StopFloat();   // D-2: a landing on land always has gravity
                if (_rb != null && !_rb.isKinematic) { _rb.velocity = velocity; _rb.angularVelocity = Vector3.zero; }
            }
            if (moved && _sync != null) _sync.FlagDiscontinuity();   // 瞬間移動: 他の端末で補間させない (Codex W6 D-中)
            NotifyEnd(reason);
        }

        /// <summary>Owner, every RiverCheckInterval, for a disc that is not flying, held or in a mouth (DES-DOG-005 D-2, in this order):
        /// a kinematic disc nobody carries is unfrozen (the ball's UnfreezeIfFree); where a disc floats it floats (every Owner does the
        /// same from the position, so an Owner change keeps it afloat); a disc that no longer floats gets its gravity back (judged from
        /// the physics too, RC-09); anything else in the band goes to the nearer bank as before (IMP-DOG-020 段階 7).</summary>
        void CheckRiver()
        {
            if (riverData == null || Time.time < _nextRiverAt) return;
            _nextRiverAt = Time.time + RiverCheckInterval;
            if (!Networking.IsOwner(gameObject) || IsHeldNow || (catcher != null && catcher.IsCarrying)) return;
            // D-2 放した後の復旧 (the ball's RC-17 / UnfreezeIfFree): not flying (Update only calls this then), not held, not carried,
            // yet kinematic - a dog that lost it while carrying gives up without touching its physics. Looked at again next time.
            if (_rb != null && _rb.isKinematic)
            {
                if (_sync != null) { _sync.SetKinematic(false); Debug.Log("[KH_Frisbee] unfrozen (kinematic, not flying, held or carried) disc=" + gameObject.name); }
                return;
            }
            Vector3 p = transform.position;
            int kind = FloatKind(p);
            if (kind != FloatNone) { FloatDisc(p, kind == FloatSpot); return; }
            if (_floating || GravityOff()) StopFloat();
            if (!InRiverBand(p)) return;
            Vector3 bank = BankFor(p);
            Debug.Log("[KH_Frisbee] resting in the river band; moved to the bank " + bank + " disc=" + gameObject.name);
            transform.position = bank;
            if (_rb != null && !_rb.isKinematic) { _rb.velocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
            if (_sync != null) _sync.FlagDiscontinuity();
        }

        // ---- DES-DOG-005 D-2 (M2): floating, the same rules as KH_ToyRecall.FloatBall ----
        const int FloatNone = 0, FloatSpot = 1, FloatRiver = 2;

        /// <summary>D-1 「浮く」: the water-play spot's water (KH_WaterSpot._FloatsAt) or the river's (KH_RiverData._FloatsAt: in the water,
        /// outside the bridge zones, riverbed data, below surface + 0.3 m, a usable bank within reach).</summary>
        int FloatKind(Vector3 p)
        {
            if (riverData == null) return FloatNone;
            if (waterSpot != null && waterSpot._FloatsAt(p)) return FloatSpot;
            return riverData._FloatsAt(p) ? FloatRiver : FloatNone;
        }

        bool FloatsHere(Vector3 p) { return FloatKind(p) != FloatNone; }

        float FloatY(Vector3 p)
        {
            float sink = waterSpot != null ? waterSpot.floatSink : KH_WaterSpot.DefaultFloatSink;
            return riverData._SurfaceY(p.z) - radius * sink;
        }

        /// <summary>Gravity off (synced by VRCObjectSync), a velocity that holds y = surface - radius x floatSink and drifts as the
        /// ball does (KH_RiverData._DriftVelocity: the spot's downstream end / the river's stops), no spin, level. Not kinematic.</summary>
        void FloatDisc(Vector3 p, bool inSpot)
        {
            if (_rb == null || _rb.isKinematic || _sync == null) return;
            if (!_floating)
            {
                _floating = true;
                Debug.Log("[KH_Frisbee] floating at " + p + (inSpot ? " (water spot)" : " (river)") + " disc=" + gameObject.name);
            }
            if (_rb.useGravity) _sync.SetGravity(false);
            float drift = waterSpot != null ? waterSpot.floatDrift : KH_WaterSpot.DefaultFloatDrift;
            Vector3 h = riverData._DriftVelocity(p, drift, inSpot, inSpot ? waterSpot._DownstreamZ() : 0f);
            _rb.velocity = new Vector3(h.x, Mathf.Clamp((FloatY(p) - p.y) * 4f, -1f, 1f), h.z);
            _rb.angularVelocity = Vector3.zero;
            if (transform.up.y < 0.9999f) transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);   // level (flat on the water)
        }

        bool GravityOff() { return _rb != null && !_rb.isKinematic && !_rb.useGravity; }

        /// <summary>Gravity back (Owner). Called on a pickup, a catch, a recall, a landing on land and when the disc leaves the water.</summary>
        void StopFloat()
        {
            if (_floating) Debug.Log("[KH_Frisbee] float ended at " + transform.position + " disc=" + gameObject.name);
            _floating = false;
            if (_sync != null) _sync.SetGravity(true);
        }

        bool InRiverBand(Vector3 p)
        {
            return riverData != null && riverData._InBand(p, KH_RiverData.BandMargin) && (waterSpot == null || !waterSpot._Contains(p));
        }

        /// <summary>近い方の岸 (帯の外 BankExtra) の地面。</summary>
        Vector3 BankFor(Vector3 p)
        {
            float side = p.x >= riverData._CenterX(p.z) ? 1f : -1f;
            Vector3 bank = riverData._BankPoint(p, side, KH_RiverData.BankExtra);
            RaycastHit g;
            if (Physics.Raycast(bank + Vector3.up * 5f, Vector3.down, out g, 12f, obstacleMask, QueryTriggerInteraction.Ignore)) bank = g.point + Vector3.up * 0.02f;
            return bank;
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
