using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    // IMP-DOG-025 (DES-DOG-006 §1-5 / §1-6, prototype §7): a dog that is being washed stands still, looks at the washer and
    // tilts its head when the stroke moves to another body part. Only the unleashed case is here; the leashed stop, the gates
    // (StartFetch / CanPlay / ReqAttach) and the other reactions are IMP-DOG-027.
    //
    // Where it runs (Update, main file): after the proximity update (_paused), before ContinueSelectedBehavior. Everything that
    // must win over washing has already returned above that point: the leash (UpdateHillsideLeash), the Candy performance,
    // "fetchController == null", and Fetch / Frisbee (!fetchController.IsIdle || FrisbeeActive). So no extra check for them here.
    //
    // Every client runs this branch (Update runs the decisions on all clients, IMP-NET-005). _WashHold() comes from the synced
    // washMs / washSeen, so all clients reach the same answer; Coast / FaceTowards write the root only on the Owner, and the head
    // gaze / tilt is injected locally on every client (KD_DogPetController._SetGaze is per frame, called from Update).
    //
    // Writers are not added: the root is written only through the existing Owner-only movers (Coast eases the dog to a stop,
    // FaceTowards turns it), no pose Bool is raised (the dog is washed standing),
    // the legs go through ApplyMoveAnim (via Coast), the head through the pet controller's single additive writer.
    public partial class KD_DogWanderController
    {
        public KominkaHillside.KH_DogWash wash;   // null = washing does nothing (unwired scenes keep the old behaviour)

        // Frame flag: lowered at the top of Update, raised by the wash branch only (same lifetime as _treatBusy etc.).
        private bool _washActive;
        private int _washLastFrame = -10;      // last frame the branch ran (entry edge = not the previous frame)
        private bool _washTurning;             // Owner: body turn toward the washer in progress (hysteresis)
        private int _washSeenPart = -1;        // last stroke part seen, for the tilt trigger
        private float _washTiltStart = -1000f; // start of the current head tilt
        private float _washTiltNextAt;         // earliest start of the next tilt (4 s spacing)
        private float _washTiltSign = 1f;      // alternates left / right

        const float WashTiltDegrees = 22f;     // prototype §7 / design §1-6: 18-25 deg
        const float WashTiltSeconds = 0.9f;    // out and back
        const float WashTiltSpacing = 4f;      // "開始を 4 秒あける"
        const float WashGazeRise = 0f;         // _WasherPosition() returns the washer's head (prototype §6), so nothing is added
        // Body turn: the head does most of the looking. Turning the body every time the washer walks around the dog would
        // make it hard to wash (the part being washed turns away). So the body turns only when the washer is well to the side
        // (60 deg < angle <= 120 deg), slowly (FaceTowards' idle rate), and stops at 30 deg. From behind (> 120 deg, washing the
        // rear / tail) it does not turn at all and only looks back as far as the head allows. Set WashTurnMax to 180 to always turn.
        const float WashTurnStart = 60f, WashTurnStop = 30f, WashTurnMax = 120f;

        /// <summary>Being washed this frame (the wash branch ran). Frame flag, every client.</summary>
        public bool IsBeingWashed { get { return _washActive; } } // net: callable-ok: read-only

        /// <summary>Update (every client): the wash stop. True = this frame is handled (the caller returns).
        /// False on every path where nothing was touched, so the existing branches run exactly as before.</summary>
        bool UpdateHillsideWash()
        {
            if (wash == null || fetchController == null) return false;
            if (!wash._WashHold()) return false;
            // Water is stronger than washing (design §1-5): in the water (synced pose, every client) or in the Owner's water play
            // (walking out / shaking on the bank; non-Owners always see WaterOff) the dog is not stopped. It stops once landed.
            if (fetchController.WaterPose != 0 || waterState != WaterOff) return false;
            // Gathering and a call to sleep beside someone are stronger than washing (design §1-5: 集合・呼び戻し). Both are synced
            // (every client agrees); the wash stop simply yields and their own branches run below.
            if (activities != null && activities.Mode == 1) return false;
            if (sleepBoard != null && dogIndex >= 0 && sleepBoard.LiveSleeperOf(dogIndex) >= 0) return false;

            if (_washLastFrame != Time.frameCount - 1) WashEnter();
            _washLastFrame = Time.frameCount;
            _washActive = true;
            // Handled every frame while washed; ended -> the next frame reselects from the top (design §1-5).
            _behaviorSelectionValid = false;
            // _paused is left as the proximity update above set it (the washer is usually within pauseRadius, and the play
            // partner reading IsPausedForPlayer should see this dog as unavailable anyway)

            Coast();   // brake to a stop (Owner writes, everyone picks the legs from _speed)

            Vector3 washer = wash._WasherPosition();
            Vector3 dogPos = dogRoot.position;
            Vector3 flat = washer - dogPos;
            flat.y = 0f;
            bool hasWasher = flat.sqrMagnitude > 0.01f;   // "no washer" returns the dog's own position

            // Body turn (Owner only inside FaceTowards; the angle is computed everywhere, cheap)
            if (hasWasher)
            {
                Vector3 fwd = dogRoot.forward;
                fwd.y = 0f;
                float angle = fwd.sqrMagnitude > 0.0001f ? Vector3.Angle(fwd, flat) : 0f;
                if (_washTurning) { if (angle <= WashTurnStop || angle > WashTurnMax) _washTurning = false; }
                else if (angle > WashTurnStart && angle <= WashTurnMax) _washTurning = true;
                if (_washTurning) FaceTowards(washer);
            }
            else _washTurning = false;

            // Head tilt: when the stroke moves to another part, at most one start every 4 s, alternating sides
            int part = wash._LastStrokePart();
            if (part >= 0 && part != _washSeenPart)
            {
                if (_washSeenPart >= 0 && Time.time >= _washTiltNextAt)
                {
                    _washTiltStart = Time.time;
                    _washTiltNextAt = Time.time + WashTiltSpacing;
                    _washTiltSign = -_washTiltSign;
                }
                _washSeenPart = part;
            }

            if (petController != null)
            {
                float roll = 0f;
                float t = Time.time - _washTiltStart;
                if (t >= 0f && t < WashTiltSeconds) roll = _washTiltSign * WashTiltDegrees * Mathf.Sin(Mathf.PI * t / WashTiltSeconds);
                Vector3 gaze = hasWasher ? washer : dogPos + dogRoot.forward * 1.5f;
                gaze.y += WashGazeRise;
                petController._SetGaze(gaze, roll);   // valid for this frame only: passed every washed frame
            }
            return true;
        }

        /// <summary>The first washed frame: fold what must not resume after the wash (same set as the Fetch branch, minus
        /// what a dog that stays in place can keep: the sleep / live freezes stay, the Animator is not handed to anyone).</summary>
        void WashEnter()
        {
            _holderId = -1;            // feint watch
            _treatEating = false;      // a treat still claimed resumes from its synced state after the wash
            _thanksUntil = -1f;        // the thank-you look would point at someone else after the wash
            if (_restPhase != RestPhaseNone) EndRest();   // EndRest: cooldown and debt settled as on every exit
            _washTurning = false;
            // The part at entry is the reference; the first tilt comes when the stroke moves on
            _washSeenPart = wash._LastStrokePart();
        }
    }
}
