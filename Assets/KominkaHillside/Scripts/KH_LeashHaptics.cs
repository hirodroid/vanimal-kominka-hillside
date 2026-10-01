using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-DOG-007: the pull of a dog on the leash, felt in the hand that holds it.
    //
    // Local only, one instance beside KH_LeashManager. It reads the shared connection state and the
    // player's own tracking, never writes anything: no synced variable, no dog, no network event.
    // Everything here is derived from what every client already knows, exactly like KH_LeashCollar's rope.
    //
    // There is no rope simulation in this world (game-design section 4), so "being pulled" is measured,
    // not simulated: the horizontal distance from the hand to the collar over the leash length, plus the
    // speed at which the collar is leaving the hand. Two triggers, deliberately different in feel:
    //   stretch   - the leash is taut (ratio >= pullRatio) and still growing: repeated short pulses whose
    //               strength rises with the stretch. This is the dog walking on ahead, pulling you along.
    //   still-pull- you are standing (holder speed < stillSpeed) and the dog leaves anyway: one firm tug
    //               at a slower rhythm. This reads as "come on", not as a buzz.
    // Aggregation is per hand, not per dog: with three dogs on one hand the hand buzzes once per interval
    // at the strongest of the three, so a full hand is never three times as loud as one dog.
    //
    // VR only. _HandPosition falls back to a synthetic body anchor on Desktop/Touch, which does not
    // describe a hand that could be pulled, and those devices have no controller to shake anyway;
    // the walk stays complete without haptics (VER-WLD-001 confirms Quest/iOS on device).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_LeashHaptics : UdonSharpBehaviour
    {
        public KH_LeashManager manager;
        // Inspector switch today, a settings item later (DES-UI-001). Off = this script does nothing at all.
        public bool hapticsOn = true;
        [Header("Tuning: provisional until PCVR Play")]
        public float pullRatio = 1.0f;        // taut: hand-to-collar distance / leashLength
        public float pullFullRatio = 1.25f;   // ratio at which the pulse reaches full strength (the dog's own return threshold)
        public float stillSpeed = 0.4f;       // m/s below which the holder counts as standing
        public float stillPullRatio = 0.85f;  // while standing, the dog only has to reach the waiting band to be felt
        public float awaySpeed = 0.3f;        // m/s the collar must be leaving the hand for a still-pull
        public float pulseInterval = 0.12f;   // stretch: rhythm
        public float pulseDuration = 0.08f;
        public float tugInterval = 0.5f;      // still-pull: slower and longer, so it reads as a tug
        public float tugDuration = 0.15f;
        public float minAmplitude = 0.3f;     // the first moment the leash goes taut
        public float frequency = 0.5f;
        public float teleportJump = 1.0f;     // m in one frame: respawn / station teleport, never a pull
        public bool logPulls = false;         // at most one line per second per hand, for the PCVR record. IMP-DOG-034 A7: off in production (string building + Debug.Log)

        private const int ModeNone = 0, ModePulse = 1, ModeTug = 2;
        private const float SpeedTau = 0.3f;  // EMA time constant for the holder's own speed
        // IMP-DOG-034 B3: the pull is evaluated at 10 Hz, not every frame (lightness over realtime; the pulse
        // rhythm is 0.12 s and the tug 0.5 s, so a 0.1 s decision is below what the hand can tell apart).
        // Pulse playback below still runs every frame from the last decision, so pulse timing and duration
        // are unchanged; only the start/stop of a pull can lag by up to one evaluation.
        private const float EvalInterval = 0.1f;
        private float _nextEval;
        private float _lastEval;
        private bool _evalVR;                 // last evaluation found the local player valid and in VR
        private bool _pulsing;                // last evaluation left a pull on at least one hand
        private VRCPlayerApi _local;

        private float[] _prevDist;            // per dog, -1 = no usable previous frame (detached / just attached / teleport)
        private float[] _nextPulse;           // per hand
        private float[] _nextLog;             // per hand
        private float[] _handAmp;             // per hand, this frame
        private int[] _handMode;              // per hand, this frame
        private int[] _logDog;
        private float[] _logRatio;
        private float[] _logAway;
        private Vector3 _prevPos;
        private bool _hasPrevPos;
        private float _holderSpeed;

        private void Start()
        {
            _nextPulse = new float[2];
            _nextLog = new float[2];
            _handAmp = new float[2];
            _handMode = new int[2];
            _logDog = new int[2];
            _logRatio = new float[2];
            _logAway = new float[2];
        }

        private void Update()
        {
            if (!hapticsOn || _handMode == null) return;
            float now = Time.time;
            // IMP-DOG-034 B3: between evaluations only the per-hand pulse playback runs (from the last decision).
            if (now < _nextEval)
            {
                if (_pulsing) PlayPulses(now);
                return;
            }
            _nextEval = now + EvalInterval;
            Evaluate(now);
            _pulsing = _evalVR && (_handMode[0] != ModeNone || _handMode[1] != ModeNone);
            if (_pulsing) PlayPulses(now);
        }

        // IMP-DOG-034 B3: the whole pull decision, at 10 Hz. The manager's arrays and values are read once into
        // locals, and each hand's tracking at most once (was: _IsMine + _HandPosition + 3 array reads per dog).
        private void Evaluate(float now)
        {
            _evalVR = false;
            _handMode[0] = ModeNone; _handMode[1] = ModeNone;
            _handAmp[0] = 0f; _handAmp[1] = 0f;
            if (manager == null) return;
            int[] walkers = manager.walkers;
            if (walkers == null) return;
            if (!Utilities.IsValid(_local)) _local = Networking.LocalPlayer;
            VRCPlayerApi local = _local;
            // Desktop/Touch: no tracked hand to pull, no controller to shake. Forget the history as well,
            // so a player who switches into VR does not inherit a stale position.
            if (!Utilities.IsValid(local) || !local.IsUserInVR()) { _hasPrevPos = false; return; }
            // The speeds below are per second over the real time since the previous evaluation (~0.1 s),
            // never Time.deltaTime: a 0.1 s distance change divided by one frame would read 3-6x too fast.
            float dt = now - _lastEval;
            _lastEval = now;
            if (dt <= 0f) return;
            int dogs = walkers.Length;
            int[] hands = manager.hands;
            if (hands == null || hands.Length < dogs) return;   // Build keeps them the same length
            Transform[] collars = manager.collarAnchors;
            int collarCount = collars == null ? 0 : collars.Length;
            float leash = manager.leashLength;
            int localId = local.playerId;
            _evalVR = true;
            if (_prevDist == null || _prevDist.Length != dogs)
            {
                _prevDist = new float[dogs];
                for (int i = 0; i < dogs; i++) _prevDist[i] = -1f;
            }

            // --- the holder's own movement (teleports are not movement) ---
            // teleportJump is now per evaluation (~0.1 s, or one frame below 10 fps): a 4 m/s run covers 0.4 m.
            // A gap of more than a second (haptics switched off and on, a long hitch) is history, not a pull.
            Vector3 pos = local.GetPosition();
            bool jumped = !_hasPrevPos || dt > 1f;
            if (_hasPrevPos)
            {
                Vector3 delta = pos - _prevPos;
                float moved = delta.magnitude;
                if (moved > teleportJump) jumped = true;
                else _holderSpeed = Mathf.Lerp(_holderSpeed, moved / dt, Mathf.Clamp01(dt / SpeedTau));
            }
            _prevPos = pos;
            _hasPrevPos = true;
            if (jumped)
            {
                _holderSpeed = 0f;
                for (int i = 0; i < dogs; i++) _prevDist[i] = -1f;
            }

            // IMP-DOG-034 B3: each hand's position is read at most once per evaluation, through the same
            // manager._HandPosition as before (VR here, so it is that hand's tracking position).
            bool hasHand0 = false, hasHand1 = false;
            Vector3 hand0 = Vector3.zero, hand1 = Vector3.zero;

            // --- per dog: how far the leash is stretched and whether it is still stretching ---
            for (int i = 0; i < dogs; i++)
            {
                int walker = walkers[i];
                int hand = hands[i];
                // Same test as manager._IsMine(i): leashed (walker >= 0) by the local player.
                Transform collar = (walker >= 0 && walker == localId && i < collarCount) ? collars[i] : null;
                if (collar == null || hand < 0 || hand > 1)
                {
                    _prevDist[i] = -1f;   // detached, or somebody else's dog: no history, no vibration
                    continue;
                }
                Vector3 h;
                if (hand == 0)
                {
                    if (!hasHand0) { hand0 = manager._HandPosition(local, 0); hasHand0 = true; }
                    h = hand0;
                }
                else
                {
                    if (!hasHand1) { hand1 = manager._HandPosition(local, 1); hasHand1 = true; }
                    h = hand1;
                }
                Vector3 c = collar.position;
                // Horizontal: a dog on a slope below the trail must not feel like a pull.
                float dx = c.x - h.x, dz = c.z - h.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float prev = _prevDist[i];
                _prevDist[i] = dist;
                if (prev < 0f || jumped || leash <= 0.01f) continue;  // first evaluation after attach / after a teleport
                float ratio = dist / leash;
                float away = (dist - prev) / dt;

                int mode = ModeNone;
                float amp = 0f;
                if (ratio >= pullRatio && away > 0f)
                {
                    float span = pullFullRatio - pullRatio;
                    float t = span > 0.0001f ? Mathf.Clamp01((ratio - pullRatio) / span) : 1f;
                    mode = ModePulse;
                    amp = Mathf.Lerp(minAmplitude, 1f, t);
                }
                if (_holderSpeed < stillSpeed && ratio >= stillPullRatio && away >= awaySpeed)
                {
                    mode = ModeTug;   // a tug wins over a pulse on the same hand
                    amp = 1f;
                }
                if (mode == ModeNone) continue;
                // Per hand: strongest mode and strongest amplitude across that hand's dogs.
                if (mode > _handMode[hand] || (mode == _handMode[hand] && amp > _handAmp[hand]))
                {
                    _logDog[hand] = i; _logRatio[hand] = ratio; _logAway[hand] = away;
                }
                if (mode > _handMode[hand]) _handMode[hand] = mode;
                if (amp > _handAmp[hand]) _handAmp[hand] = amp;
            }
        }

        // --- one haptic event per hand per interval, whatever the number of dogs ---
        // IMP-DOG-034 B3: every frame, from the last 10 Hz decision, so the 0.12 s / 0.5 s rhythm and the
        // pulse durations are exactly as before.
        private void PlayPulses(float now)
        {
            VRCPlayerApi local = _local;
            for (int hand = 0; hand < 2; hand++)
            {
                if (_handMode[hand] == ModeNone) continue;
                if (now < _nextPulse[hand]) continue;
                if (!Utilities.IsValid(local)) return;
                bool tug = _handMode[hand] == ModeTug;
                float amplitude = Mathf.Clamp01(_handAmp[hand]);
                local.PlayHapticEventInHand(hand == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right,
                    tug ? tugDuration : pulseDuration, amplitude, frequency);
                _nextPulse[hand] = now + (tug ? tugInterval : pulseInterval);
                if (logPulls && now >= _nextLog[hand])
                {
                    _nextLog[hand] = now + 1f;
                    Debug.Log("[KH_LeashHaptics] hand=" + (hand == 0 ? "L" : "R") + " dog=" + _logDog[hand]
                        + " ratio=" + _logRatio[hand].ToString("F2") + " away=" + _logAway[hand].ToString("F1")
                        + " holder=" + _holderSpeed.ToString("F1") + " mode=" + (tug ? "tug" : "pulse")
                        + " amp=" + amplitude.ToString("F2"));
                }
            }
        }
    }
}
