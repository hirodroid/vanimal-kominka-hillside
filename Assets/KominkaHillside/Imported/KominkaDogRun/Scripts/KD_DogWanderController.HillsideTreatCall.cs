using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    // IMP-DOG-006 (2026-10-03 user decision, all five recommended answers): call one of my leashed dogs with a treat.
    // Plan: docs/plans/2026-10-03-treat-call.md
    //
    //   1. The nearest of MY leashed dogs comes as soon as the treat appears in my hand (the existing "show" beat of UpdateTreat).
    //      Who gets which slot is KD_TreatBox's (master): BuildAllowWith + ResolveClaimsAllowWith. Other players' leashed dogs never.
    //   2. I have leashed dogs but none can come -> the pouch refuses with a reason (KD_TreatBox.AllDogsEngaged / HillsideBusyOwnLeash).
    //   3. The dog I just released may come (0 leashed dogs = the nearest dog, as before).
    //   4. After eating (and the thank-you look) it waits LeashTreatWaitBase s facing me, while being petted up to LeashTreatWaitCap s.
    //   5. A treat out of reach: it goes no farther than leash x LeashTreatReach from the leash hand, then stands and looks at it.
    //
    // Hook: UpdateHillsideLeashFull calls UpdateLeashTreat after the wash station branch (a held station wins) and before the wash
    // stop / LeashPose / travel / Return. The treat beats (show, walk to the treat, eat, thanks) are UpdateTreat itself, run with
    // _treatOnLeash raised: the steps go straight (StepToward) toward a point clamped to the leash reach, and a dog that is itself
    // beyond the reach is left to the leash Return (review 2026-10-03), the stand-off in front
    // of the holder is LeashTreatStandoff (hand reaches the head and the collar), and the gaze / tail are held until the next decision
    // (_SetGazeFor, as LeashPose does) so the leash's legs frames need nothing per frame.
    //
    // Sync: nothing new. The claim / eat start are KD_TreatBox's synced arrays (master), the giver is the slot's giverId, the leash is
    // KH_LeashManager.walkers; the treat beats are derived on every client from those (as for an unleashed dog). The wait too (review
    // 2026-10-03): it starts at the end of the thanks, which every client derives from the synced eat start, and ends by time, by the
    // holder's displacement, by the leash reach (positions every client sees) - so every screen shows the same standing dog (not
    // LeashPose's LieDown on a non-Owner). The extension reads the synced pet state. Only the Owner's movers move the root.
    // An attach, a detach or a holder change drops the wait (LeashTreatReset); an Owner change keeps it.
    //
    // Cost: the decision (every 5 frames, IMP-DOG-036) of a leashed dog adds one KD_TreatBox.ClaimedSlotOf call while nothing is going
    // on. Legs frames: nothing (TickBusy for a leashed dog is LeashLegsOk, unchanged). The master's reconcile (4 Hz, only while a treat
    // is out or a claim is held) adds the 6 x 3 allow table and one walkers read per dog.
    public partial class KD_DogWanderController
    {
        const int LeashTreat = 9;               // leashMode: the treat beats on the leash (Owner's bookkeeping, like every leashMode)
        const int LeashTreatWait = 10;          // leashMode: waiting for the pat / the collar after eating (Owner)
        const float LeashTreatReach = 1.25f;    // decision 5: x leash length from the leash hand
        const float LeashTreatReachPad = 0.15f; // the clamp stops this far inside the reach (the Return threshold is the same 1.25x)
        const float LeashTreatStandoff = 0.45f; // m in front of the holder (+ body radius) while the treat is in the hand. Provisional (PCVR)
        const float LeashTreatWaitBase = 8f;    // decision 4 (provisional)
        const float LeashTreatWaitCap = 20f;    // decision 4: the longest wait while being petted (provisional)
        const float LeashTreatPetGrace = 2f;    // a pat keeps at least this much of the wait left (within the cap)
        // The holder moving this far (horizontal) from where they stood when the wait began ends it (about 1 s of walking: the Issue's
        // "1 s walking", measured as a displacement every client sees instead of the Owner-only holder-speed estimate)
        const float LeashTreatWalkOff = 1.2f;

        // UpdateTreat reads these while it runs from the leash branch (Treat.cs: TreatStep / TreatGaze / TreatUrge / the stand-off)
        private bool _treatOnLeash;
        private Vector3 _treatLeashHand;
        private float _treatLeashReach;

        private bool _ltActive;        // the last decision ran the treat beats on the leash
        private bool _ltAte;           // ... and the dog ate during them (only an eaten treat leads to the wait)
        private bool _ltWaiting;       // waiting after the treat (every client derives it, see UpdateLeashTreat)
        private float _ltWaitStart, _ltWaitUntil;
        private Vector3 _ltHolderAt;   // where the holder stood when the wait began
        // Harness read-outs (int / bool writes at the edges only)
        private int _ltCalls;          // treat beats started on the leash
        private int _ltWaits;          // waits started
        private int _ltWaitEndReason;  // 1 time, 2 holder walked on, 3 stretched / holder gone, 4 another treat, 5 reset (attach / detach / holder change)
        private int _ltOutOfReach;     // decisions that stopped the treat beats because the dog itself was beyond the leash reach (harness)
        private bool _ltPetExtended;   // the current / last wait was extended by petting
        private int _ltClamped;        // decisions whose treat step was clamped to the leash reach

        /// <summary>IMP-DOG-006: this leashed dog cannot answer a treat call now (KD_TreatBox reads it: the master's assignment and the
        /// giver's prediction, which may be different clients - so only facts every client has: the stations' synced holds, the synced
        /// travel ticket, the synced water pose of the fetch controller). Called to a wash station (or riding its stand down), a travel
        /// ticket pending, wet / swimming. Fetch / the disc / petting / sleeping are KD_TreatBox.DogCanTake's own checks.
        /// (Review 2026-10-03: the Owner-local waterState / _waterLeashVeto / _waterSwimming are not read here: WaterTick clears them on
        /// non-Owners, so the giver's menu and the master's assignment could disagree.)</summary>
        public bool HillsideLeashTreatBlocked // net: callable-ok: read-only
        {
            get
            {
                if (_leaveLowering || HeldStation() != null) return true;
                if (fetchController != null && fetchController.WaterPose != 0) return true;
                return TravelPending();
            }
        }

        /// <summary>The leash branch (every client, every decision, after the station branch). True = handled this decision.</summary>
        bool UpdateLeashTreat(VRCPlayerApi holder, int walkerId, Vector3 handPos, float handDist, float leash)
        {
            if (treatBox == null || dogIndex < 0) return false;
            // Nothing going on (the usual case): one call
            if (treatBox.ClaimedSlotOf(dogIndex) < 0 && !_treatEating && Time.time >= _thanksUntil && !_ltActive && !_ltWaiting) return false;

            KD_TreatSlot slot = treatBox.LiveSlotOf(dogIndex);
            bool mine = slot != null && slot.GiverId() == walkerId;   // the master never gives another player's slot to a leashed dog
            bool ownEat = (_treatEating || treatBox.EatingNowOf(dogIndex)) && _thanksPlayerId == walkerId;
            bool ownThanks = Time.time < _thanksUntil && _thanksPlayerId == walkerId;
            // Review 2026-10-03: the limit is on the dog itself, not only on its target. Beyond the reach the treat beats stop and the
            // leash's own Return (> 1.25x, the same threshold) brings the dog back; they resume once it is inside again
            bool far = handDist > leash * LeashTreatReach;
            if (far && (mine || ownEat || ownThanks)) _ltOutOfReach++;
            if ((mine || ownEat || ownThanks) && !far && !TravelPending())
            {
                if (_ltWaiting) { _ltWaiting = false; _ltWaitEndReason = 4; }   // another treat while waiting: this one wins
                if (_dogOwner) WaterClearFloat();   // the leash owns the root here (as before the travel branch)
                _treatOnLeash = true; _treatLeashHand = handPos; _treatLeashReach = leash * LeashTreatReach;
                bool handled = UpdateTreat();
                _treatOnLeash = false;
                if (handled)
                {
                    if (!_ltActive) { _ltActive = true; _ltAte = false; _ltCalls++; }
                    if (_treatEating) _ltAte = true;
                    leashMode = LeashTreat;
                    stopoverIndex = -1;   // standing elsewhere now: the spot is free for a sibling
                    return true;
                }
            }

            // The treat beats ended on this decision (eaten and thanked, put away, given to someone else, or out of reach)
            if (_ltActive)
            {
                bool ate = _ltAte;
                _ltActive = false; _ltAte = false;
                // Every client: the end of the thanks is derived from the synced eat start (dogEatMs + eatDuration, then the local
                // treatThanksDuration), so every screen starts the same wait at about the same moment (review 2026-10-03)
                if (ate && !far && Utilities.IsValid(holder)) LeashTreatStartWait(holder);
                else LeashTreatToWalk();
            }
            if (_ltWaiting) return UpdateLeashTreatWait(holder, walkerId, handPos, handDist, leash);
            return false;
        }

        void LeashTreatStartWait(VRCPlayerApi holder)
        {
            _ltWaiting = true;
            _ltWaitStart = Time.time;
            _ltWaitUntil = Time.time + LeashTreatWaitBase;
            _ltHolderAt = holder.GetPosition();
            _ltPetExtended = false;
            _ltWaits++;
            leashMode = LeashTreatWait;
        }

        /// <summary>Every client (review 2026-10-03: the pose must not depend on who owns the dog): stand facing the holder so they can
        /// pat the dog or take the collar off (KH_LeashCollar's Interact, their own dog only). The Owner's Coast / FaceTowards move the
        /// root; the others only pick the same pose (LookAt, never LeashPose's LieDown). Ends by time (started from the synced eat event),
        /// by the holder moving LeashTreatWalkOff from where they stood, by a stretched leash; the extension reads the synced pet state;
        /// a detach ends it through the unleash edge. Inputs are positions and synced state only.</summary>
        bool UpdateLeashTreatWait(VRCPlayerApi holder, int walkerId, Vector3 handPos, float handDist, float leash)
        {
            if (!Utilities.IsValid(holder)) { LeashTreatEndWait(3); return false; }
            if (handDist > leash * LeashTreatReach) { LeashTreatEndWait(3); return false; }
            Vector3 moved = holder.GetPosition() - _ltHolderAt; moved.y = 0f;
            if (moved.sqrMagnitude > LeashTreatWalkOff * LeashTreatWalkOff) { LeashTreatEndWait(2); return false; }
            // Being petted keeps the wait going (up to the cap)
            if (fetchController != null && fetchController.PetActive)
            {
                float want = Mathf.Min(_ltWaitStart + LeashTreatWaitCap, Mathf.Max(_ltWaitUntil, Time.time + LeashTreatPetGrace));
                if (want > _ltWaitUntil) { _ltWaitUntil = want; _ltPetExtended = true; }
            }
            if (Time.time >= _ltWaitUntil) { LeashTreatEndWait(1); return false; }

            leashMode = LeashTreatWait;
            Vector3 holderPos = holder.GetPosition();
            // `_look` before Coast (the thank-you look's order)
            if (_speed <= stopSpeedThreshold) _look = true;
            Coast();
            FaceTowards(holderPos);
            if (petController != null)
            {
                _standGaze = true;   // held until the next decision (LeashPose's S2b gaze, Tick.cs HeldGazeEnd)
                petController._SetGazeFor(holder.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position, 0f, LeashGazeHold);
                petController._SetUrgeFor(0.7f, LeashGazeHold);
            }
            return true;
        }

        void LeashTreatEndWait(int reason)
        {
            _ltWaiting = false;
            _ltWaitEndReason = reason;
            LeashTreatToWalk();
        }

        /// <summary>Back to the walk: re-enter the trail from where the dog stands (the leash Return's bookkeeping).</summary>
        void LeashTreatToWalk()
        {
            if (leashMode != LeashTreat && leashMode != LeashTreatWait) return;
            leashMode = LeashWalk;
            if (!_destCacheLoop) _routeResume = -1;
            else if (_routeNext >= 0) _routeResume = _routeNext;
            _routeNext = -1;
        }

        /// <summary>Attach / detach / holder change: nothing of a treat call carries over. The treat beats themselves come back from the
        /// synced claim on the next decision. An Owner change keeps the wait: every client has been running it (review 2026-10-03).</summary>
        void LeashTreatReset()
        {
            if (_ltWaiting) _ltWaitEndReason = 5;
            _ltWaiting = false; _ltActive = false; _ltAte = false;
            if (leashMode == LeashTreat || leashMode == LeashTreatWait) leashMode = LeashWalk;
        }

        // ---------------------------------------------------------------- UpdateTreat's leash-aware movers (Treat.cs)

        /// <summary>UpdateTreat's step. Unleashed: StepToward as before. On the leash: the target clamped to the leash reach and walked in
        /// a straight line (StepToward, as unleashed; UpdateTreat has already shortened it at a wall). Review 2026-10-03: not LeashGo - its
        /// NavMesh corners (and the 2 m goal sample) can leave the circle, while a straight step from a dog inside the circle to a point
        /// inside it stays inside on every frame, the legs frames included (the disc is convex). A dog beyond the reach never gets here
        /// (UpdateLeashTreat hands it to the leash's Return). A clamped target that is reached stands and faces the treat.</summary>
        void TreatStep(Vector3 target, float stop, float speed)
        {
            if (!_treatOnLeash) { StepToward(target, stop, speed); return; }
            Vector3 goal = TreatClamp(target);
            bool clamped = goal != target;
            if (clamped) _ltClamped++;
            Vector3 to = goal - dogRoot.position; to.y = 0f;
            if (clamped && to.magnitude <= 0.2f)
            {
                Coast();
                FaceTowards(target);
                return;
            }
            StepToward(goal, stop, speed);
        }

        /// <summary>The point inside the leash reach (decision 5) closest to p, horizontally, around the leash hand.</summary>
        Vector3 TreatClamp(Vector3 p)
        {
            Vector3 d = p - _treatLeashHand; d.y = 0f;
            float r = _treatLeashReach - LeashTreatReachPad;
            if (r < 0.3f) r = 0.3f;
            float m = d.magnitude;
            if (m <= r) return p;
            Vector3 c = _treatLeashHand + d / m * r;
            c.y = p.y;
            return c;
        }

        /// <summary>UpdateTreat's gaze + tail. On the leash they are held until the next decision (the legs frames send nothing).</summary>
        void TreatGaze(Vector3 worldPos, float urge)
        {
            if (petController == null) return;
            if (_treatOnLeash)
            {
                _standGaze = true;
                petController._SetGazeFor(worldPos, 0f, LeashGazeHold);
                petController._SetUrgeFor(urge, LeashGazeHold);
                return;
            }
            petController._SetGaze(worldPos, 0f);
            petController._SetUrge(urge);
        }

        /// <summary>UpdateTreat's tail only (eating).</summary>
        void TreatUrge(float urge)
        {
            if (petController == null) return;
            if (_treatOnLeash) { _standGaze = true; petController._SetUrgeFor(urge, LeashGazeHold); return; }
            petController._SetUrge(urge);
        }

        /// <summary>The stand-off in front of the giver while the treat is in their hand.</summary>
        float TreatStandoff() { return _treatOnLeash ? LeashTreatStandoff : nudgeStandoff; }
    }
}
