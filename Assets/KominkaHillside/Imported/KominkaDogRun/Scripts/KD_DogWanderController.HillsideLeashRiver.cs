using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    // IMP-DOG-053 (DES-DOG-010 method B, user decisions 2026-10-11; the way in decided 2026-10-12 = mixed, IMP-DOG-053 §0):
    // a leashed dog follows its holder into the river. The NavMesh is unchanged (the riverbed stays excluded, so LeashGo never
    // enters the band); in and near the water the dog moves with the river Fetch's legs (HillsideWater.cs): the frame mark
    // _waterRiverFrame (the river rules of _BlockedByRiver: sink guard 0.03 m, a non-swimmer no deeper than the Fetch limit 0.85 M,
    // never into the band where the river-wide water is not = the bridge zones / no riverbed data), WaterSwimLookAhead,
    // WaterApplySwimHeight and WaterPoseRefresh, in that order. The 0.85 M for the leash is a new design choice (DES-DOG-010 2);
    // free play in the water-play spot keeps its 0.45 M. Owner-local state; nothing synced is added (the position through
    // VRCObjectSync, waterPose, walkers). Every other client shows the swim from the synced pose as before.
    //
    //   states  : Off (the usual leash walk) -> Approach (LeashGo on the NavMesh to the planned way in) -> River (under the river
    //             rules toward the walking target, shortened to the last point the dog can reach: it stops there and faces the
    //             holder, which is not a stall) -> Exit (under the river rules to a bank on the holder's side) -> Shake (1.2 s on the
    //             bank after a swim, skipped when the holder walks on) -> Off. The shake itself is the wash's shake (Codex review
    //             2026-10-12 F3): the Owner requests it through KH_DogWash's synced shakeSerial (wash._RequestShake), and every client
    //             starts it from that counter's edge in the leash branch (LeashRiverShakeEdge -> StartWashShake: the procedural roll,
    //             the sound). Without a wash wired there is only the 1.2 s stand.
    //   enter   : the holder's hand is inside the band (KH_RiverData._InBand, margin 1 m) and the walking target (hand + the dog's
    //             offset, KH_LeashManager._WalkingTarget) is in the river-wide water with water in it (> WaterWet). A dog that is wet
    //             (a leash taken up in the water, an Owner change) goes to River or Exit at once: the state is rebuilt from the
    //             position and the pose, as WaterTick's ownerEdge does for the swim height.
    //   stay    : River while the hand is in the band, the target in the band, the hand within catchUpDistance (10 m) and no wash
    //             station holds the dog. Otherwise Exit: never a teleport from the water. The same band as the enter test: the
    //             difference is that staying does not ask for water under the target (the dry strip of the band, a bridge zone or
    //             a stretch without riverbed data keep the dog following, shortened by the clamp), entering does.
    //   way in  : _LeashRiverPlanEntry, once when Approach starts: verified ok bank slices only (Codex review 2026-10-12 F1: the
    //             "direct" entry from the nearest NavMesh point was removed - at runtime it could not check the path, the slope or the
    //             stones, and Course/131 counted 39 such samples: 16 unconnected, 12 stones, 11 steep). The ok slice within LrSliceNear
    //             of the crossing point, else the nearest ok slice within the bank reach (own side, then the other). None = stay on
    //             the bank. A direct entry may come back only with those three checks guaranteed (baked like the slices).
    //             Approach -> River needs the arrival at that slice's bank point (F2): a dry dog never skips the chosen way in, even
    //             where dry NavMesh lies inside the band. A dog that is already wet goes to River / Exit by its own rule (above).
    //   way out : _LeashRiverPlanExit: the water-play spot's nearest exit when the dog is in the spot and the holder is on the spot's
    //             side, else the ok slice nearest the dog on the holder's side (KH_RiverData._ExitSlice's choice), else the other
    //             side, else the bank point projected on the NavMesh (WaterRiverExitPlan's fallback). Recovery as the water play's
    //             (C-9): 10 s or stuck -> another bank -> teleport to its return point (FlagDiscontinuity).
    //   speed   : in the water WaterStepSpeed (swim / walk; run on the dry strip of the band), the Approach at most runSpeed: never
    //             the Return's dash / sprint near or in the water.
    //   legs    : every frame while the state is not Off or the hand is inside the band (_lrNear; LeashLegsOk, Tick.cs): a
    //             legs frame replays a StepToward without the frame mark, which the plain band rule would refuse.
    public partial class KD_DogWanderController
    {
        const int LrOff = 0, LrApproach = 1, LrRiver = 2, LrExit = 3, LrShake = 4;
        const int LeashRiver = 12;              // leashMode while the river branch moves the dog (9/10 treat, 11 cart)
        const float LrSliceNear = 2f;           // m: an ok slice this close to the crossing point is the way in
        const float LrArrive = 0.5f;            // m: at the way in
        const float LrApproachTimeout = 15f, LrRetry = 5f, LrNoWayRetry = 3f;
        const float LrClampStep = 0.25f, LrClampMax = 8f, LrClampEvery = 0.25f;
        const float LrStop = 0.25f;             // m: at the (shortened) goal
        const float LrInward = 0.5f;            // m inside the band edge: the first goal of a dog still on the bank
        private int _lrState;                   // Owner local (harness: GetProgramVariable)
        private float _lrStageAt, _lrRetryAt;
        private Vector3 _lrEntryPt, _lrPlanPt;
        private int _lrEntryCode = -2;
        private Vector3 _lrExitPt, _lrReturnPt;
        private int _lrExitCode = -1, _lrExitSpot = -1, _lrRecover;
        private Vector3 _lrProgressPos;
        private float _lrProgressAt;
        private int _lrStuck;
        private bool _lrNear, _lrSwam, _lrWaiting;
        private Vector3 _lrClampFrom, _lrClampGoal, _lrClampOut;
        private float _lrClampAt = -1000f;
        // harness read-outs (Owner local, int writes at the edges only)
        private int _lrEntries, _lrDirect, _lrSlice, _lrExits, _lrLandings, _lrTeleports, _lrWaitDecisions, _lrFarDecisions, _lrShakes;

        /// <summary>River rules for the push-apart (KD_DogPack via _WaterPushBlocked): in the water or walking out of it.</summary>
        bool LeashRiverRules() { return _lrState == LrRiver || _lrState == LrExit; }

        /// <summary>Back to the usual leash walk (or nothing, unleashed / another Owner). The swim height is left to its owners:
        /// the river legs' last WaterApplySwimHeight, the caller's WaterClearFloat after a move.</summary>
        void LeashRiverReset(string reason)
        {
            if (_lrState != LrOff) Debug.Log("[KD_DogWanderController] leash river end dog=" + dogIndex + " reason=" + reason);
            _lrState = LrOff; _lrNear = false; _lrWaiting = false; _lrRecover = 0;
            _leashCorners = null; _nextLeashPath = 0f;
            if (leashMode == LeashRiver) { leashMode = LeashWalk; _routeNext = -1; }   // the walk picks its trail entry again from here
        }

        /// <summary>IMP-DOG-053: the leash branch (Owner, after the travel ticket, before the Return). True = this decision was the
        /// river's. False = the usual leash walk (nothing touched but the bookkeeping).</summary>
        bool UpdateLeashRiver(Vector3 holderPos, Vector3 handPos, float handDist, float leash)
        {
            if (riverData == null || waterSpot == null || fetchController == null) { _lrNear = false; if (_lrState != LrOff) LeashRiverReset("unwired"); return false; }
            Vector3 p = dogRoot.position;
            float latHand = riverData._Lateral(handPos, KominkaHillside.KH_RiverData.BandMargin);
            bool handIn = latHand < 0f && !riverData._OnBridgeDeck(handPos);
            // wet = the last pose refresh (top of this decision) or afloat. Not _swimYValid: a dog easing down on dry ground is the land's
            // (the leash branch's WaterClearFloat above), else it would start and end a walk out on every decision of the ease
            bool wet = _waterLastWet || _waterSwimming;
            // legs frames only while the hand is in the band (the way in may start; a legs frame replays LeashGo, which never enters the
            // band, so nothing is missed outside it - and a holder on the riverside trail keeps the thinning of IMP-DOG-036 S1)
            _lrNear = handIn;
            if (_lrState == LrOff && !wet && !handIn) return false;   // the usual case: dry, the hand away from the river
            if (!riverData._FetchReady()) { if (_lrState != LrOff) LeashRiverReset("no-data"); return false; }
            bool station = washCalls != null && HeldStation() != null;   // IMP-DOG-053 #11: called to a wash station: come out first
            Vector3 target = hillsideLeashes._WalkingTarget(dogIndex);
            bool tIn = riverData._InBand(target, KominkaHillside.KH_RiverData.BandMargin);
            bool enterOk = handIn && !station && handDist <= hillsideLeashes.catchUpDistance && riverData._FetchWater(target) && WDepth(target) > WaterWet;
            bool stayOk = handIn && tIn && !station && handDist <= hillsideLeashes.catchUpDistance;
            if (handDist > hillsideLeashes.catchUpDistance && wet) _lrFarDecisions++;   // the old Return would have tried a teleport here

            if (_lrState == LrOff)
            {
                if (wet)
                {
                    _lrEntries++; _lrSwam = _waterSwimming;
                    if (stayOk) LeashRiverBegin(LrRiver, "wet");
                    else LeashRiverBeginExit(p, holderPos, station ? "station" : "wet");
                }
                else
                {
                    if (!enterOk || Time.time < _lrRetryAt) return false;
                    int code = _LeashRiverPlanEntry(p, target);
                    if (code < -1) { _lrRetryAt = Time.time + LrNoWayRetry; return false; }
                    _lrEntries++; _lrSwam = false;
                    _lrEntryCode = code; _lrEntryPt = _lrPlanPt;
                    _lrSlice++;   // ok slices only (F1); _lrDirect stays 0
                    LeashRiverBegin(LrApproach, "slice " + code);
                }
            }
            else if (_lrState == LrApproach && wet)
            {
                // F2: the wet hand-over, separate from the dry way in (wet ground at the bank edge on the way)
                if (stayOk) LeashRiverBegin(LrRiver, "wet-on-the-way");
                else LeashRiverBeginExit(p, holderPos, station ? "station" : "wet-on-the-way");
            }
            else if (_lrState == LrApproach)
            {
                if (!enterOk && !stayOk) { LeashRiverReset(station ? "station" : "target-left"); return false; }
                if (Time.time - _lrStageAt > LrApproachTimeout) { _lrRetryAt = Time.time + LrRetry; LeashRiverReset("approach-timeout"); return false; }
                // F2 (Codex review 2026-10-12): only at the chosen slice's bank point (being in the band is not enough: dry NavMesh inside
                // the band would let the dog skip the verified way in). A dog that got wet on the way is taken by the branch above
                if (Horizontal(p, _lrEntryPt) < LrArrive) LeashRiverBegin(LrRiver, "entered");
            }
            else if (_lrState == LrRiver)
            {
                if (!stayOk) LeashRiverBeginExit(p, holderPos, station ? "station" : handDist > hillsideLeashes.catchUpDistance ? "far" : "target-left");
            }
            else if (_lrState == LrExit)
            {
                // the holder came back into the water (and close): follow again from where the dog is, if it is still in the band
                if (enterOk && handDist < leash * 1.25f && riverData._InBand(p, KominkaHillside.KH_RiverData.BandMargin)) LeashRiverBegin(LrRiver, "back-in");
            }
            else if (_lrState == LrShake)
            {
                if (enterOk && handDist < leash * 1.25f) { LeashRiverReset("back-in"); return false; }
            }

            leashMode = LeashRiver;   // a detour / stopover in progress ends as "interrupted" on the next decision
            stopoverIndex = -1;
            if (_waterSwimming) _lrSwam = true;
            if (_lrState == LrApproach) { LeashRiverApproachStep(p); return true; }
            if (_lrState == LrShake)
            {
                Coast();
                if (_speed <= stopSpeedThreshold) FaceTowards(holderPos);
                WaterApplySwimHeight();
                if (Time.time - _lrStageAt >= WaterShakeSeconds || HolderMoving()) LeashRiverReset("landed");
                return true;
            }
            if (_lrState == LrExit) { LeashRiverExitStep(p, holderPos); return true; }
            LeashRiverFollowStep(p, target, holderPos);
            return true;
        }

        void LeashRiverBegin(int state, string why)
        {
            _lrState = state; _lrStageAt = Time.time; _lrWaiting = false; _lrRecover = 0; _lrClampAt = -1000f;
            _leashCorners = null; _nextLeashPath = 0f;
            Debug.Log("[KD_DogWanderController] leash river " + (state == LrApproach ? "approach" : state == LrRiver ? "follow" : "state " + state)
                + " dog=" + dogIndex + " why=" + why + " entry=" + _lrEntryPt.ToString("F2"));
        }

        void LeashRiverBeginExit(Vector3 p, Vector3 holderPos, string why)
        {
            _lrState = LrExit; _lrStageAt = Time.time; _lrWaiting = false; _lrRecover = 0; _lrExits++;
            LeashRiverResetProgress();
            if (!_LeashRiverPlanExit(p, holderPos, -1))
            {
                // nothing planned: straight out on the holder's side (the band rule lets a body in the band move outward)
                float side = holderPos.x >= riverData._CenterX(holderPos.z) ? 1f : -1f;
                _lrExitPt = riverData._BankPoint(p, side, KominkaHillside.KH_RiverData.BandMargin + KominkaHillside.KH_RiverData.BankExtra);
                _lrReturnPt = _lrExitPt; _lrExitCode = -1; _lrExitSpot = -1;
            }
            Debug.Log("[KD_DogWanderController] leash river exit dog=" + dogIndex + " why=" + why + " bank=" + _lrExitPt.ToString("F2")
                + " code=" + _lrExitCode + " spotExit=" + _lrExitSpot);
        }

        void LeashRiverApproachStep(Vector3 p)
        {
            // on the NavMesh (LeashGo: the riverbed is excluded, it stops at the bank): run while far, walk the last metres
            float d = Horizontal(p, _lrEntryPt);
            LeashGo(_lrEntryPt, d > 3f ? runSpeed : walkSpeed, 0.2f);
        }

        void LeashRiverFollowStep(Vector3 p, Vector3 target, Vector3 holderPos)
        {
            Vector3 goal;
            bool inBand = riverData._InBand(p, KominkaHillside.KH_RiverData.BandMargin);
            if (!inBand)
            {
                // still on the bank (the way in): straight across the band edge first, at the dog's z (the slice's own line)
                float side = p.x >= riverData._CenterX(p.z) ? 1f : -1f;
                goal = riverData._BankPoint(p, side, KominkaHillside.KH_RiverData.BandMargin - LrInward);
            }
            else goal = LeashRiverClampCached(p, target);
            _waterRiverFrame = Time.frameCount;   // the river rules for this frame's step / coast (HillsideWater.cs)
            bool at = Horizontal(p, goal) < LrStop;
            if (at) Coast();
            else
            {
                WaterSwimLookAhead(p, goal);
                StepToward(goal, 0.2f, WaterStepSpeed(p));
            }
            bool refused = _waterBlockFrame == Time.frameCount;
            bool shortened = Horizontal(goal, target) > 0.05f;
            _lrWaiting = inBand && (at || refused) && shortened;
            if (_lrWaiting) _lrWaitDecisions++;
            if ((at || refused) && _speed <= stopSpeedThreshold) FaceTowards(holderPos);   // waiting at the limit faces the holder
            WaterApplySwimHeight();   // BUG-DOG-012: the swim floor is inside (was LeashRiverSwimHeight)
            WaterPoseRefresh();
        }

        void LeashRiverExitStep(Vector3 p, Vector3 holderPos)
        {
            _waterRiverFrame = Time.frameCount;
            bool wetHere = WDepth(p) > WaterWet;
            bool landed = (Horizontal(p, _lrExitPt) < 0.4f && !wetHere)
                || (!wetHere && !riverData._InBand(p, KominkaHillside.KH_RiverData.BandMargin) && LeashRiverOnNav(p));
            if (landed)
            {
                Coast();
                WaterApplySwimHeight();   // BUG-DOG-012: the swim floor is inside (was LeashRiverSwimHeight)
                WaterPoseRefresh();
                _lrLandings++;
                if (_lrSwam && !HolderMoving())
                {
                    // F3: the wash's shake on every client: the Owner raises KH_DogWash's synced shakeSerial (as the wash's wet shake does,
                    // a no-op where this client does not own the wash object), and LeashRiverShakeEdge starts it from that edge everywhere
                    // (the Owner too, on its next decision). A holder who walks on skips it (the condition above)
                    _lrState = LrShake; _lrStageAt = Time.time;
                    if (wash != null && !(petController != null && petController.IsShaking)) wash._RequestShake();
                    Debug.Log("[KD_DogWanderController] leash river landed dog=" + dogIndex + " shake");
                }
                else LeashRiverReset("landed");
                return;
            }
            if (Time.time - _lrStageAt > WaterExitTimeout || LeashRiverStuck(p))
            {
                if (_lrRecover == 0)
                {
                    _lrRecover = 1; _lrStageAt = Time.time; LeashRiverResetProgress();
                    int except = _lrExitCode;
                    if (_lrExitSpot >= 0 && waterSpot._Ready())
                    {
                        _lrExitSpot = waterSpot._OtherExit(p, _lrExitSpot);
                        _lrExitPt = waterSpot._Exit(_lrExitSpot); _lrReturnPt = waterSpot._ReturnPointFor(_lrExitSpot);
                    }
                    else _LeashRiverPlanExit(p, holderPos, except < 0 ? -2 : except);   // keeps the old bank when there is none
                    Debug.Log("[KD_DogWanderController] leash river recover dog=" + dogIndex + " stage=2 bank=" + _lrExitPt.ToString("F2") + " code=" + _lrExitCode);
                }
                else
                {
                    // stage 3: only the destination is trusted (a return point checked on the NavMesh by Course/77 / Course/91). A plan
                    // without a slice or a spot exit (the bank point fallback) is sampled on the NavMesh first; none = no teleport, the
                    // walk out goes on and stage 3 is tried again after the next timeout
                    if (_lrExitCode < 0 && _lrExitSpot < 0)
                    {
                        NavMeshHit back;
                        if (!NavMesh.SamplePosition(_lrReturnPt, out back, 4f, NavMesh.AllAreas))
                        {
                            _lrStageAt = Time.time; LeashRiverResetProgress();
                            Debug.Log("[KD_DogWanderController] leash river recover dog=" + dogIndex + " stage=3 no-bank (no NavMesh near " + _lrReturnPt.ToString("F2") + ")");
                            WaterSwimLookAhead(p, _lrExitPt);
                            StepToward(_lrExitPt, 0f, _waterSwimming ? WaterSwimSpeed() : walkSpeed);
                            WaterApplySwimHeight();   // BUG-DOG-012: the swim floor is inside (was LeashRiverSwimHeight)
                            WaterPoseRefresh();
                            return;
                        }
                        _lrReturnPt = back.position;
                    }
                    VRCObjectSync sync = dogRoot.GetComponent<VRCObjectSync>();
                    if (sync != null) sync.FlagDiscontinuity();
                    _waterRecoverFrame = Time.frameCount; _waterRecoverFrom = dogRoot.position; _waterRecoverTo = _lrReturnPt;
                    _dbgWriter = DbgWWaterRecover; dogRoot.position = _lrReturnPt; // net: owner-only: the leash branch returns before this for non-Owners
                    WaterClearFloat();
                    _speed = 0f; _lrTeleports++;
                    Debug.Log("[KD_DogWanderController] leash river recover dog=" + dogIndex + " stage=3 teleport to=" + _lrReturnPt.ToString("F2"));
                    LeashRiverReset("recover");
                    WaterPoseRefresh();
                    ApplyMoveAnim();
                    return;
                }
            }
            WaterSwimLookAhead(p, _lrExitPt);
            StepToward(_lrExitPt, 0f, _waterSwimming ? WaterSwimSpeed() : wetHere ? walkSpeed : runSpeed);
            WaterApplySwimHeight();   // BUG-DOG-012: the swim floor is inside (was LeashRiverSwimHeight)
            WaterPoseRefresh();
        }

        /// <summary>F3 (every client, every leash decision, before LeashPose): the shake edge of KH_DogWash's synced shakeSerial
        /// (WashCleanTick records it, every client, the first-receive rule included) starts the wash's shake here when the wash branch
        /// does not take it (the leashed wash stop consumes its own edges in WashReact). So the shake requested on landing from the
        /// river plays on every screen; a pending edge expires after 1 s as in the wash (WashCleanTick).</summary>
        void LeashRiverShakeEdge(bool washStop)
        {
            if (!_washShakePending || washStop || wash == null) return;
            if (petController != null && petController.IsShaking) return;
            _washShakePending = false;
            StartWashShake(1);
            _lrShakes++;
        }

        bool LeashRiverOnNav(Vector3 p)
        {
            NavMeshHit h;
            return NavMesh.SamplePosition(p, out h, 0.5f, NavMesh.AllAreas);
        }

        void LeashRiverResetProgress() { _lrProgressPos = dogRoot.position; _lrProgressAt = Time.time + 1f; _lrStuck = 0; }

        /// <summary>The water play's stuck rule (C-9: under 0.1 m per 1 s, twice in a row), for the walk out only (a dog waiting at
        /// the limit in River is never "stuck").</summary>
        bool LeashRiverStuck(Vector3 p)
        {
            if (Time.time < _lrProgressAt) return _lrStuck >= 2;
            if (Horizontal(p, _lrProgressPos) < WaterStuckStep) _lrStuck++; else _lrStuck = 0;
            _lrProgressPos = p; _lrProgressAt = Time.time + 1f;
            return _lrStuck >= 2;
        }

        /// <summary>The non-swimmer's limit under the river rules (the Fetch limit, KH_RiverData.fetchWadeRatio x M).</summary>
        float LeashRiverWalkLimit() { return FetchWadeRatio() * waterMouthHeight; }

        Vector3 LeashRiverClampCached(Vector3 p, Vector3 target)
        {
            if (Time.time < _lrClampAt && Horizontal(target, _lrClampGoal) < 0.3f && Horizontal(p, _lrClampFrom) < 0.5f) return _lrClampOut;
            _lrClampAt = Time.time + LrClampEvery; _lrClampGoal = target; _lrClampFrom = p;
            _lrClampOut = _LeashRiverClamp(p, target, LeashRiverWalkLimit(), waterSwimAllowed);
            return _lrClampOut;
        }

        // ---------------------------------------------------------------- the planners (also called by Course/131 in Edit mode)

        /// <summary>IMP-DOG-053 #3: the farthest point on the line from -> goal that a walk in LrClampStep steps reaches under the river
        /// rules (LeashRiverRefuses: the same answers as _BlockedByRiver's river branch for a dog on its feet; canSwim = depth never
        /// refuses, the dog floats before the sink guard). Only the first LrClampMax metres are marched: a goal farther than that
        /// whose first metres are clear is returned as it is (the dog asks again on the way). Pure: no field is written.</summary>
        public Vector3 _LeashRiverClamp(Vector3 from, Vector3 goal, float walkLimit, bool canSwim)
        {
            if (riverData == null) return goal;
            Vector3 d = goal - from; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.001f) return goal;
            Vector3 dir = d / len;
            float span = len < LrClampMax ? len : LrClampMax;
            int n = Mathf.CeilToInt(span / LrClampStep);
            if (n < 1) n = 1;
            float step = span / n;
            Vector3 prev = from, last = from;
            for (int i = 1; i <= n; i++)
            {
                Vector3 q = i == n && span >= len ? goal : from + dir * (step * i);
                if (LeashRiverRefuses(prev, q, walkLimit, canSwim)) { last.y = goal.y; return last; }
                last = q; prev = q;
            }
            return goal;
        }

        /// <summary>Pure: would a step from -> next be refused under the river rules (KH_RiverData + this dog's Ms)?</summary>
        bool LeashRiverRefuses(Vector3 from, Vector3 next, float walkLimit, bool canSwim)
        {
            if (riverData._FetchWater(next))
            {
                if (canSwim) return false;
                float rd = WDepth(next);
                if (rd <= WDepth(from)) return false;
                return waterSwimMouth - rd < SinkGuard || rd > walkLimit;
            }
            if (!riverData._InBand(next, KominkaHillside.KH_RiverData.BandMargin)) return false;   // the bank
            bool fromZone = riverData._InBand(from, KominkaHillside.KH_RiverData.BandMargin) && !riverData._FetchWater(from);
            return !(fromZone && riverData._Lateral(next, KominkaHillside.KH_RiverData.BandMargin) >= riverData._Lateral(from, KominkaHillside.KH_RiverData.BandMargin) - 0.0001f);
        }

        /// <summary>IMP-DOG-053 §0: the way into the river for a dog at `dog` heading for `target` in the water, through verified ok bank
        /// slices only (Codex review 2026-10-12 F1: the direct entry from the nearest NavMesh point is removed - it could not check the
        /// path, the slope or the stones at runtime; Course/131 found 39 risky samples). The crossing point C is the band edge on the
        /// dog's side at the target's z. Returns the ok slice code (>= 0) whose bank point is within LrSliceNear of C, else the nearest
        /// ok slice within the bank reach on the dog's side, then the other side; else -2 (no way in: the dog stays on the bank).
        /// -1 (direct) is never returned any more. The way-in point: _LeashRiverPlanPoint().</summary>
        public int _LeashRiverPlanEntry(Vector3 dog, Vector3 target)
        {
            _lrPlanPt = dog;
            if (riverData == null || !riverData._FetchReady()) return -2;
            float side = dog.x >= riverData._CenterX(dog.z) ? 1f : -1f;
            int s = side > 0f ? 1 : -1;
            Vector3 c = riverData._BankPoint(target, side, KominkaHillside.KH_RiverData.BandMargin);
            c.y += 0.3f;   // the bank stands about 0.25 m above the water at the band edge (Course/130 median)
            int code = riverData._NearestSlice(c, s, LrSliceNear, -1);
            if (code >= 0 && Horizontal(riverData._SliceBank(code), c) <= LrSliceNear) { _lrPlanPt = riverData._SliceBank(code); return code; }
            float reach = riverData._BankReach();
            code = riverData._NearestSlice(c, s, reach, -1);
            if (code < 0) code = riverData._NearestSlice(c, -s, reach, -1);
            if (code >= 0) { _lrPlanPt = riverData._SliceBank(code); return code; }
            return -2;
        }

        /// <summary>The way-in point of the last _LeashRiverPlanEntry (the dog's own position when it returned -2).</summary>
        public Vector3 _LeashRiverPlanPoint() { return _lrPlanPt; }

        /// <summary>IMP-DOG-053 #4: the bank to walk out to from p, the holder at holderPos. except: -1 = a fresh plan; a slice code
        /// = skip it (recovery stage 2); -2 = a fresh plan without the spot's exits. Writes the plan (_lrExitPt / _lrReturnPt /
        /// _lrExitCode / _lrExitSpot) and returns true, or false with the old plan kept.</summary>
        public bool _LeashRiverPlanExit(Vector3 p, Vector3 holderPos, int except)
        {
            if (riverData == null || !riverData._FetchReady()) return false;
            int hs = holderPos.x >= riverData._CenterX(holderPos.z) ? 1 : -1;
            if (except == -1 && waterSpot != null && waterSpot._Ready() && waterSpot._ExitCount() > 0 && SpotContains(p))
            {
                int k = waterSpot._NearestExit(p);
                Vector3 e = waterSpot._Exit(k);
                if ((e.x >= riverData._CenterX(e.z) ? 1 : -1) == hs)
                { _lrExitSpot = k; _lrExitCode = -1; _lrExitPt = e; _lrReturnPt = waterSpot._ReturnPointFor(k); return true; }
            }
            int skip = except >= 0 ? except : -1;
            float reach = riverData._BankReach();
            int code = riverData._NearestSlice(p, hs, reach, skip);
            if (code < 0) code = riverData._NearestSlice(p, -hs, reach, skip);
            if (code >= 0)
            { _lrExitSpot = -1; _lrExitCode = code; _lrExitPt = riverData._SliceBank(code); _lrReturnPt = riverData._SliceReturn(code); return true; }
            Vector3 bp = riverData._BankPoint(p, hs, KominkaHillside.KH_RiverData.BandMargin + KominkaHillside.KH_RiverData.BankExtra);
            NavMeshHit h;
            if (NavMesh.SamplePosition(bp, out h, 4f, NavMesh.AllAreas) && !riverData._InBand(h.position, KominkaHillside.KH_RiverData.BandMargin))
            { _lrExitSpot = -1; _lrExitCode = -1; _lrExitPt = h.position; _lrReturnPt = h.position; return true; }
            return false;
        }

        /// <summary>The bank point of the current / last exit plan (harness and Course/131).</summary>
        public Vector3 _LeashRiverExitPoint() { return _lrExitPt; }
    }
}
