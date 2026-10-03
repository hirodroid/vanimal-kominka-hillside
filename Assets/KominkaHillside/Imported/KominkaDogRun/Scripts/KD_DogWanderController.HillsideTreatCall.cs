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
    // _treatOnLeash raised: the steps go through LeashGo (NavMesh) toward a point clamped to the leash reach, the stand-off in front
    // of the holder is LeashTreatStandoff (hand reaches the head and the collar), and the gaze / tail are held until the next decision
    // (_SetGazeFor, as LeashPose does) so the leash's legs frames need nothing per frame.
    //
    // Sync: nothing new. The claim / eat start are KD_TreatBox's synced arrays (master), the giver is the slot's giverId, the leash is
    // KH_LeashManager.walkers; the treat beats are derived on every client from those (as for an unleashed dog). The wait is local to
    // the dog's Owner (the decisions that move the root); other clients see a dog standing beside its holder through VRCObjectSync
    // and LeashPose. An Owner change, an attach or a detach drops the wait (LeashTreatReset).
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
        const float LeashTreatFar = 1.6f;       // beyond this x leash the normal Return (and its catch-up teleport) brings the dog back first
        const float LeashTreatStandoff = 0.45f; // m in front of the holder (+ body radius) while the treat is in the hand. Provisional (PCVR)
        const float LeashTreatWaitBase = 8f;    // decision 4 (provisional)
        const float LeashTreatWaitCap = 20f;    // decision 4: the longest wait while being petted (provisional)
        const float LeashTreatPetGrace = 2f;    // a pat keeps at least this much of the wait left (within the cap)
        const float LeashTreatWalkOff = 1f;     // the holder walking this long ends the wait (Issue design 2026-09-29)

        // UpdateTreat reads these while it runs from the leash branch (Treat.cs: TreatStep / TreatGaze / TreatUrge / the stand-off)
        private bool _treatOnLeash;
        private Vector3 _treatLeashHand;
        private float _treatLeashReach;

        private bool _ltActive;        // the last decision ran the treat beats on the leash
        private bool _ltAte;           // ... and the dog ate during them (only an eaten treat leads to the wait)
        private bool _ltWaiting;       // Owner: waiting after the treat
        private float _ltWaitStart, _ltWaitUntil;
        private float _ltMoveSince = -1f;   // the holder has been walking since (-1 = standing)
        // Harness read-outs (int / bool writes at the edges only)
        private int _ltCalls;          // treat beats started on the leash
        private int _ltWaits;          // waits started
        private int _ltWaitEndReason;  // 1 time, 2 holder walked on, 3 stretched / holder gone, 4 another treat, 5 reset (attach / detach / Owner)
        private bool _ltPetExtended;   // the current / last wait was extended by petting
        private int _ltClamped;        // decisions whose treat step was clamped to the leash reach

        /// <summary>IMP-DOG-006: this leashed dog cannot answer a treat call now (KD_TreatBox reads it: the master's assignment and the
        /// giver's prediction). Called to a wash station (or riding its stand down), a travel ticket pending, in the water.
        /// Fetch / the disc / petting / sleeping are KD_TreatBox's own checks.</summary>
        public bool HillsideLeashTreatBlocked // net: callable-ok: read-only
        {
            get
            {
                if (_stationIdx >= 0 || _leaveLowering || HeldStation() != null) return true;
                if (waterState != WaterOff || _waterLeashVeto || _waterSwimming) return true;
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
            bool far = handDist > leash * LeashTreatFar;
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
                if (ate && _dogOwner && !far && Utilities.IsValid(holder)) LeashTreatStartWait();
                else LeashTreatToWalk();
            }
            if (_ltWaiting) return UpdateLeashTreatWait(holder, walkerId, handPos, handDist, leash);
            return false;
        }

        void LeashTreatStartWait()
        {
            _ltWaiting = true;
            _ltWaitStart = Time.time;
            _ltWaitUntil = Time.time + LeashTreatWaitBase;
            _ltMoveSince = -1f;
            _ltPetExtended = false;
            _ltWaits++;
            leashMode = LeashTreatWait;
        }

        /// <summary>Owner: stand facing the holder so they can pat the dog or take the collar off (KH_LeashCollar's Interact, their
        /// own dog only). Ends by time, by the holder walking on, by a stretched leash; a detach ends it through the unleash edge.</summary>
        bool UpdateLeashTreatWait(VRCPlayerApi holder, int walkerId, Vector3 handPos, float handDist, float leash)
        {
            if (!_dogOwner || !Utilities.IsValid(holder)) { LeashTreatEndWait(3); return false; }
            if (handDist > leash * LeashTreatReach) { LeashTreatEndWait(3); return false; }
            if (HolderMoving())
            {
                if (_ltMoveSince < 0f) _ltMoveSince = Time.time;
                else if (Time.time - _ltMoveSince >= LeashTreatWalkOff) { LeashTreatEndWait(2); return false; }
            }
            else _ltMoveSince = -1f;
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
            _ltMoveSince = -1f;
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

        /// <summary>Attach / detach / Owner (or holder) change: nothing of a treat call carries over. The treat beats themselves come
        /// back from the synced claim on the next decision.</summary>
        void LeashTreatReset()
        {
            if (_ltWaiting) _ltWaitEndReason = 5;
            _ltWaiting = false; _ltActive = false; _ltAte = false; _ltMoveSince = -1f;
            if (leashMode == LeashTreat || leashMode == LeashTreatWait) leashMode = LeashWalk;
        }

        // ---------------------------------------------------------------- UpdateTreat's leash-aware movers (Treat.cs)

        /// <summary>UpdateTreat's step. Unleashed: StepToward as before. On the leash: the target clamped to the leash reach, walked
        /// along the NavMesh (LeashGo) by the Owner; a clamped target that is reached stands and faces the treat.</summary>
        void TreatStep(Vector3 target, float stop, float speed)
        {
            if (!_treatOnLeash) { StepToward(target, stop, speed); return; }
            Vector3 goal = TreatClamp(target);
            bool clamped = goal != target;
            if (clamped) _ltClamped++;
            if (!_dogOwner) { StepToward(goal, stop, speed); return; }
            Vector3 to = goal - dogRoot.position; to.y = 0f;
            if (clamped && to.magnitude <= 0.2f)
            {
                Coast();
                FaceTowards(target);
                return;
            }
            LeashGo(goal, speed, stop > 0.1f ? stop : 0.1f);
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
