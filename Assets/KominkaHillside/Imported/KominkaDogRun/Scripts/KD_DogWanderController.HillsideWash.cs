using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    // IMP-DOG-025 (DES-DOG-006 §1-5 / §1-6, prototype §7): a dog that is being washed stands still, looks at the washer and
    // tilts its head when the stroke moves to another body part.
    // IMP-DOG-027 (design §1-5): the leashed stop (WashLeashStop / WashLeashHold, called from UpdateHillsideLeash), the gates
    // (StartFetch / CanPlay / ReqAttach read WashHeld) and the stronger errands (gathering, the sleep call, the travel ticket).
    //
    // Unleashed (UpdateHillsideWash, Update in the main file): after the proximity update (_paused), before
    // ContinueSelectedBehavior. Everything that must win over washing has already returned above that point: the leash
    // (UpdateHillsideLeash), the Candy performance, "fetchController == null", and Fetch / Frisbee (!fetchController.IsIdle ||
    // FrisbeeActive). So no extra check for them here.
    //
    // Leashed (UpdateHillsideLeash, HillsideLeash.cs): Update returns at the leash line, so the branch above is never reached.
    // WashLeashStop decides on every client before LeashPose and the non-Owner return, so the head (toward the washer) and the
    // absence of LieDown / sniff poses are the same on every screen; the Owner stops the dog after the Return block and before
    // rest (WashLeashHold). Only the holder's own stroke stops the dog, only while the holder stands and the leash is slack.
    //
    // Every client runs these branches (Update runs the decisions on all clients, IMP-NET-005). _WashHold() comes from the synced
    // washMs / washSeen, so all clients reach the same answer; Coast / FaceTowards write the root only on the Owner, and the head
    // gaze / tilt is injected locally on every client (KD_DogPetController._SetGaze is per frame, called from Update).
    //
    // Writers are not added: the root is written only through the existing Owner-only movers (Coast eases the dog to a stop,
    // FaceTowards turns it), no pose Bool is raised (the dog is washed standing),
    // the legs go through ApplyMoveAnim (via Coast), the head through the pet controller's single additive writer.
    public partial class KD_DogWanderController
    {
        public KominkaHillside.KH_DogWash wash;   // null = washing does nothing (unwired scenes keep the old behaviour)

        // Frame flag: lowered at the top of Update, raised by the wash branches only (same lifetime as _treatBusy etc.).
        private bool _washActive;
        private int _washLastFrame = -10;      // last frame a wash branch ran (entry edge = not the previous frame)
        private bool _washTurning;             // Owner: body turn toward the washer in progress (hysteresis)
        private int _washSeenPart = -1;        // last stroke part seen, for the tilt trigger
        private float _washTiltStart = -1000f; // start of the current head tilt
        private float _washTiltNextAt;         // earliest start of the next tilt (4 s spacing)
        private float _washTiltSign = 1f;      // alternates left / right
        // IMP-DOG-027 leashed stop. Local, never synced (every client derives it from synced facts).
        private bool _washLeashStop;           // this frame's decision (read by the harness; also the slack hysteresis)

        const float WashTiltDegrees = 22f;     // prototype §7 / design §1-6: 18-25 deg
        const float WashTiltSeconds = 0.9f;    // out and back
        const float WashTiltSpacing = 4f;      // "開始を 4 秒あける"
        const float WashGazeRise = 0f;         // _WasherPosition() returns the washer's head (prototype §6), so nothing is added
        // Body turn: the head does most of the looking. Turning the body every time the washer walks around the dog would
        // make it hard to wash (the part being washed turns away). So the body turns only when the washer is well to the side
        // (60 deg < angle <= 120 deg), slowly (FaceTowards' idle rate), and stops at 30 deg. From behind (> 120 deg, washing the
        // rear / tail) it does not turn at all and only looks back as far as the head allows. Set WashTurnMax to 180 to always turn.
        const float WashTurnStart = 60f, WashTurnStop = 30f, WashTurnMax = 120f;
        // Leashed stop (provisional until Play): the hand must be well inside the leash (FollowHand walks below 0.7x), with a
        // wider band to keep a stop that has started (the hand moves a little while washing).
        const float WashLeashSlackEnter = 0.7f, WashLeashSlackHold = 0.85f;

        /// <summary>Being washed this frame (a wash branch ran). Frame flag, every client.</summary>
        public bool IsBeingWashed { get { return _washActive; } } // net: callable-ok: read-only

        /// <summary>IMP-DOG-027: the wash hold is on (a stroke within holdSeconds, synced). What the gates read: StartFetch
        /// (KD_DogFetchController), CanPlay (KH_FrisbeeCatch) and ReqAttach (KH_LeashManager). Not the frame flag - the gates run
        /// outside this Update and on clients that did not run the branch (design §1-5 「入口が読むのは有効期限から求めた状態」).</summary>
        public bool WashHeld { get { return wash != null && wash._WashHold(); } } // net: callable-ok: read-only

        /// <summary>Update (every client): the wash stop. True = this frame is handled (the caller returns).
        /// False on every path where nothing was touched, so the existing branches run exactly as before.</summary>
        bool UpdateHillsideWash()
        {
            if (wash == null || fetchController == null) return false;
            if (!wash._WashHold()) return false;
            // Water is stronger than washing (design §1-5): in the water (synced pose, every client) or in the Owner's water play
            // (walking out / shaking on the bank; non-Owners always see WaterOff) the dog is not stopped. It stops once landed.
            // Nothing below is touched on that path, so WaterTick's veto / preempt checks and UpdateWaterPlay (further down in
            // Update, which keeps _waterSeenFrame fresh) run exactly as they do for an unwashed dog: the walk-out is never blocked.
            if (WashYieldsToWater()) return false;
            // Gathering (a participant), the call to sleep beside someone (呼び戻し) and the travel ticket are stronger than
            // washing (design §1-5). All synced (every client agrees); the wash stop simply yields and their own branches run
            // below. Their accept edge also released the hold (WashErrandTick), so the old strokes do not stop the dog again when
            // they end; this check keeps new strokes during the errand from stopping it. TravelPending only matches a leashed
            // dog, so here it is a guard for a ticket that outlives its leash by a frame.
            if (WashYieldsToErrand()) return false;

            WashFrame();
            // Handled every frame while washed; ended -> the next frame reselects from the top (design §1-5).
            _behaviorSelectionValid = false;
            // _paused is left as the proximity update above set it (the washer is usually within pauseRadius, and the play
            // partner reading IsPausedForPlayer should see this dog as unavailable anyway)

            Coast();   // brake to a stop (Owner writes, everyone picks the legs from _speed)

            Vector3 washer = wash._WasherPosition();
            WashTurn(washer);
            WashLook(washer);
            return true;
        }

        bool WashYieldsToWater()
        {
            return fetchController.WaterPose != 0 || waterState != WaterOff;
        }

        private bool _washErrandPrev;   // last frame's WashYieldsToErrand (the accept edge)

        /// <summary>Codex 4-1: Update top (every client, every frame, before the early returns). On the accept edge of a gathering
        /// (participant), a sleep call or a travel ticket, the wash hold is released once: KH_DogWash._ReleaseHold expires the hold
        /// (Owner: synced; non-Owner: the local boundary) and strokes older than that point never stop the dog or close the gates
        /// again. Without it the errand only bypassed the stop, and ending within holdSeconds re-stopped the dog on the old
        /// stroke with Fetch / disc / leash still refused. wash == null: returns at once (unwired dogs untouched).</summary>
        void WashErrandTick()
        {
            if (wash == null) return;
            bool now = WashYieldsToErrand();
            if (now && !_washErrandPrev && wash._WashHold()) wash._ReleaseHold();
            _washErrandPrev = now;
        }

        bool WashYieldsToErrand()
        {
            if (activities != null && activities.Mode == 1 && activities._IsParticipant(dogIndex)) return true;
            if (sleepBoard != null && dogIndex >= 0 && sleepBoard.LiveSleeperOf(dogIndex) >= 0) return true;
            if (TravelPending()) return true;
            return false;
        }

        /// <summary>UpdateHillsideLeash (every client), before LeashPose: does the leashed dog stop for the wash this frame?
        /// Holder's own stroke within the hold, dry, no travel ticket, the leash slack, and (Owner) the holder standing.
        /// Non-Owners have no pace history (TrackHolderSpeed is Owner only), so for them the answer only picks the pose, and
        /// LeashPose applies it only while the observed dog is stopped - i.e. once the Owner has stopped it.</summary>
        bool WashLeashStop(VRCPlayerApi holder, float handDist, float leash)
        {
            // The wider band only for a stop that ran last frame (a decision left over from before a Fetch / disc hand-over,
            // which returns above this call, must not widen it)
            bool was = _washLeashStop && _washLastFrame == Time.frameCount - 1;
            _washLeashStop = false;
            if (wash == null || fetchController == null || !wash._WashHold()) return false;
            if (WashYieldsToWater() || TravelPending()) return false;
            // Owner: the Return branch owns a stretched leash (handDist > 0.6x is still "returning" there)
            if (_dogOwner && leashMode == LeashReturn) return false;
            // Codex 4-3: the slack (and its enter / hold hysteresis) is the Owner's alone. A non-Owner sees the hand and the dog at
            // its own moment and cannot know which band the Owner is in, so it would refuse a stop the Owner keeps. Instead the
            // non-Owner poses from the synced hold + the holder's stroke + the observed stop (LeashPose's `stopped`): the dog only
            // stands still there because the Owner stopped it. Chosen over carrying the Owner's decision in a synced value (none of
            // the dog's synced objects is ours to extend here, and KH_DogWash does not sync it); the cost is that a dog stopped for
            // another reason (waiting at the leash end) while the holder washes it is shown with the wash gaze - the same washer.
            if (_dogOwner && handDist > leash * (was ? WashLeashSlackHold : WashLeashSlackEnter)) return false;
            // Someone else's stroke changes the mask and the values only (design §1-5: 他人のストロークでは足を止めない).
            // The holder's own last stroke (KH_DogWash._StrokeAgeOf, per player; 1e9 = none) within holdSeconds. A second washer
            // stroking after the holder does not cut a stop the holder's own stroke started, and never starts one.
            if (wash._StrokeAgeOf(holder.playerId) >= wash.holdSeconds) return false;
            // The holder walking off ends the stop at once (TrackHolderSpeed's tier, the same one the walk paces from)
            if (_dogOwner && HolderMoving()) return false;
            _washLeashStop = true;
            return true;
        }

        /// <summary>LeashPose (every client) when the wash stop holds: no pose Bool (washed standing), the head toward the washer.</summary>
        void WashLeashPose()
        {
            WashFrame();
            WashLook(wash._WasherPosition());
        }

        /// <summary>UpdateHillsideLeash, Owner only, after the Return block and before rest: stand still for the holder's wash.
        /// LeashWait (the dog standing on its leash) makes a detour / stopover in progress end as "interrupted" at the top of the
        /// next frame (EndPause, the same path rest and the return take). The next unwashed frame walks from step 7 as usual.</summary>
        void WashLeashHold()
        {
            leashMode = LeashWait;
            WashFrame();
            Coast();
            WashTurn(wash._WasherPosition());
        }

        /// <summary>Every wash frame (both branches, every client): the frame flag and the entry edge. Twice in one frame is one.</summary>
        void WashFrame()
        {
            if (_washLastFrame == Time.frameCount) return;
            if (_washLastFrame != Time.frameCount - 1) WashEnter();
            _washLastFrame = Time.frameCount;
            _washActive = true;
        }

        /// <summary>Body turn toward the washer (Owner only inside FaceTowards; the angle is computed everywhere, cheap).</summary>
        void WashTurn(Vector3 washer)
        {
            Vector3 flat = washer - dogRoot.position;
            flat.y = 0f;
            if (flat.sqrMagnitude <= 0.01f) { _washTurning = false; return; }   // "no washer" returns the dog's own position
            Vector3 fwd = dogRoot.forward;
            fwd.y = 0f;
            float angle = fwd.sqrMagnitude > 0.0001f ? Vector3.Angle(fwd, flat) : 0f;
            if (_washTurning) { if (angle <= WashTurnStop || angle > WashTurnMax) _washTurning = false; }
            else if (angle > WashTurnStart && angle <= WashTurnMax) _washTurning = true;
            if (_washTurning) FaceTowards(washer);
        }

        /// <summary>Head gaze and tilt (every client). The tilt starts when the stroke moves to another part, at most one start
        /// every 4 s, alternating sides.</summary>
        void WashLook(Vector3 washer)
        {
            Vector3 dogPos = dogRoot.position;
            Vector3 flat = washer - dogPos;
            flat.y = 0f;
            bool hasWasher = flat.sqrMagnitude > 0.01f;

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
        }

        /// <summary>The first washed frame: fold what must not resume after the wash (same set as the Fetch branch, minus
        /// what a dog that stays in place can keep: the sleep / live freezes stay, the Animator is not handed to anyone).
        /// IMP-DOG-027: the leashed entry calls this too. A leashed dog's own rest is re-derived every frame (settling, LeashRest)
        /// and resumes only if the holder is still standing after the wash - that is the normal rest, not a stale one. The
        /// unleashed rest FSM and a claimed treat are frozen while leashed (Update returns at the leash line); folding them here
        /// as well keeps one entry for both branches and means neither replays an old performance when the leash comes off.</summary>
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
