#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System;
using System.Reflection;
using UnityEngine;

namespace KominkaHillside
{
    // Edit-mode plan probe (harness only; 2026-09-28, after a day of Play -> fix -> Play on case PLACEMENT). The whole file is
    // outside Udon (UNITY_EDITOR && !COMPILER_UDONSHARP): UdonSharp never sees it, the world build carries nothing of it, and the
    // runtime decision code is not touched. The Editor calls _ProbePlan on the U# proxy in Edit mode (Tools/KominkaHillside/
    // Frisbee/10 Plan Probe, Course/95 - Frisbee Plan Probe) and gets back the decision the dog's FIRST Plan tick would commit for
    // one throw: the same Plan(tNow) the Chase calls (Solve: band scan -> SolveBand -> EvalAround / EvalRun -> JumpGround /
    // AirLanding / JumpLand / DobonAt / LaneClear, then PathOk, then the D / D' / E fallbacks and Reachable(landing)).
    //
    // What Plan reads, and how the probe supplies it (checked 2026-09-28 against Planner / Water / Motion / Diagnostics):
    //   _p0 / _v0 / _curve     the launch facts (AcceptLaunch)            -> arguments (v0 clamped to frisbee.maxSpeed as _Launch does)
    //   dogRoot position / fwd  the dog's pose (SnapToGround, Eta, paths) -> a temporary hidden Transform put in dogRoot for the call
    //                                                                        (the real dog is never moved, so the scene is not changed)
    //   _speed                  Eta turn rate, C take-off speed           -> argument (0 = standing, as at the throw)
    //   _mouthCatch/_crouchDur/_riseDur  ApplyVariant (Start / OnDeserialization) -> ApplyVariant() with the variant argument
    //   _debugWaterFetchOff     WaterFetchOn (the M2 switch of Course/94) -> argument
    //   per-throw resets        AcceptLaunch's plan fields                -> the same assignments below
    //   frisbee / riverData / waterSpot / wander   serialized fields and pure methods on their proxies (no Start state, no Time, no
    //                                                                        Networking); Physics.Raycast hits the scene's colliders
    //   Time.time / Networking  NOT read by Plan (tNow is an argument; only Motion / Water ticks and CanPlay's cache read Time)
    // Every instance field of this class is copied before and written back after the call (reflection), so nothing the probe
    // writes survives it. Refused in Play mode (the proxy is not the running heap there, and the live dog must not be disturbed).
    public partial class KH_FrisbeeCatch
    {
        /// <summary>Harness only (Edit mode). The first Plan tick for one throw; see the file header.</summary>
        // variantOverride < 0 = the scene's variant; curveOverride outside [-1, 1] = the variant's FlightCurve (as _Launch);
        // tNow < 0 = noticeDelay (the first Plan tick of UpdateChase).
        public KH_FrisbeePlanProbeResult _ProbePlan(Vector3 p0, Vector3 v0, Vector3 dogPos, Vector3 dogFwd, int variantOverride, float curveOverride, float tNow, float dogSpeed, bool waterFetchOff, KH_FrisbeePlanProbeStep[] cont = null)
        {
            if (tNow < 0f) tNow = noticeDelay;
            var r = new KH_FrisbeePlanProbeResult();
            if (Application.isPlaying) { r.error = "Play mode: the probe runs on the Edit-mode proxy only"; return r; }
            if (dogRoot == null || frisbee == null) { r.error = "dogRoot / frisbee not wired"; return r; }
            FieldInfo[] fields = typeof(KH_FrisbeeCatch).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            var saved = new object[fields.Length];
            for (int i = 0; i < fields.Length; i++) saved[i] = fields[i].GetValue(this);
            GameObject temp = null;
            var last = new string[2];   // [0] the last plan= line, [1] the last solve rays= line
            Application.LogCallback hook = (condition, stack, type) =>
            {
                if (condition.StartsWith("[KH_FrisbeeCatch] plan=")) last[0] = condition;
                else if (condition.StartsWith("[KH_FrisbeeCatch] solve rays=")) last[1] = condition;
            };
            Application.logMessageReceived += hook;
            try
            {
                string realName = dogRoot.name;
                temp = new GameObject(realName) { hideFlags = HideFlags.HideAndDontSave };
                Vector3 f = dogFwd; f.y = 0f;
                temp.transform.SetPositionAndRotation(dogPos, f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized, Vector3.up) : Quaternion.identity);
                dogRoot = temp.transform;
                if (variantOverride >= 0) variant = variantOverride;
                ApplyVariant();
                float speed = v0.magnitude;
                if (speed > frisbee.maxSpeed) v0 = v0 * (frisbee.maxSpeed / speed);   // KH_Frisbee._Launch
                float curve = curveOverride >= -1f && curveOverride <= 1f ? curveOverride : FlightCurve;   // KH_Frisbee._Launch reads catcher.FlightCurve
                r.p0 = p0; r.v0 = v0; r.curve = curve; r.dogPos = dogPos; r.variant = variant; r.tNow = tNow;
                // AcceptLaunch's per-throw state (no Networking, no log, no TransitionTo)
                _haveLaunch = true; _p0 = p0; _v0 = v0; _curve = curve; _flightEnded = false;
                _plan = PlanNone; _nextPlan = 0f; _deviationSince = -1f; _landingValid = false; _localPlanValid = true;
                _landingChecked = false; _landingReachable = false; _pathFailValid = false; _groundStop = false;
                _settledSince = -1f; _holding = false; _runningCatch = false; _faceErrAtTakeoff = -1f;
                _mode = ModeAround; _viaActive = false; _viaDone = false; _motionLatched = false;
                _solved = false; _bannedModes = 0; _runThrough = false; _runThroughClear = false; _dropReason = "";
                _takeoffMin = RunJumpMinTakeoff; _takeoffCap = RunJumpMaxTakeoff;
                _speed = dogSpeed; _debugWaterFetchOff = waterFetchOff;
                _planRays = 0; _solveRuns = 0; _solveRayMax = 0; _planScans = 0; _planEvaluations = 0; _planHookTries = 0;
                // The throw's CanPlay ground half (ReqCatch: CanPlay(p0) -> PlayableCached -> _PlayableAt). The other CanPlay terms
                // (leash / Fetch / gathering / treat / rest / waterPose) are live state and are not probed.
                r.p0Playable = _PlayableAt(p0);
                _planRays = 0;

                Plan(tNow);
                r.planLine = last[0] ?? ""; r.solveLine = last[1] ?? "";

                r.chosen = _plan;
                r.kind = _plan == PlanJump ? "jump" : _plan == PlanRetrieve ? "ground" : _plan == PlanStandOrOvertake ? (_runningCatch ? "over" : "stand") : "none";
                r.mode = _plan == PlanJump || _plan == PlanStandOrOvertake ? ModeName() : "-";
                r.tail = SolveTail(_plan);
                r.whyA = _whyA; r.whyB = _whyB; r.whyC = _whyC; r.solveCount = _solveCount; r.drop = _dropReason; r.bannedModes = _bannedModes;
                r.bandIn = _bandIn; r.bandOut = _bandOut; r.bandIn2 = _bandIn2; r.bandOut2 = _bandOut2; r.bandUneven = _bandUneven;
                r.planRays = _planRays; r.solveRays = _solveRayMax; r.solveCapped = _solveCapped; r.laneRefused = _solveLaneRefused;
                bool committed = _plan == PlanJump || _plan == PlanStandOrOvertake;
                if (committed)
                {
                    r.catchT = _catchT; r.rise = _rootRise; r.tUp = _tUp; r.tDown = _tDown; r.crouch = _crouchReal; r.jumpSpeed = _jumpSpeed;
                    r.takeoffY = _takeoffY; r.landY = _landY; r.dY = _landY - _takeoffY; r.landWater = _landWater;
                    r.standRoot = _standRoot; r.catchRoot = _catchRoot; r.landPt = _landPt; r.forward = _forward;
                    r.vReq = _pickVReq; r.vCap = _pickVCap; r.slack = _planSlack; r.eta = _planEta; r.path = _planPath;
                    r.disc = frisbee._PositionAt(_p0, _v0, _curve, _catchT);
                }
                r.landingValid = _landingValid;
                if (_landingValid)
                {
                    r.landing = _landing;
                    r.landingReachable = _landingChecked && _landingReachable;
                    r.landingFloat = DiscInFloatWater(_landing);
                    r.waterFetchOn = WaterFetchOn();
                    r.landingDeep = r.landingFloat && r.waterFetchOn && !WaterDepthOk(_landing);
                }
                // Where the runtime would go next with this commit (Motion.UpdateChase, the same tests on the same facts):
                //   none + a landing it cannot reach -> CancelLaunch("cannot-play") at the next tick
                //   none / ground whose landing floats -> the water states at the flight's end (WaterIntercept) when they are on
                if (!r.p0Playable) r.runtimeNext = "reject cannot-play (the thrower's ground, CanPlay)";
                // (Motion.UpdateChase: PlanNone with a checked, unreachable landing cancels cannot-play at the next tick, before the flight
                // ends - so a disc that will float deeper than the Fetch limit never reaches WaterIntercept's deep-disc on this path)
                else if (_plan == PlanNone && _landingValid && !r.landingReachable) r.runtimeNext = "cancel cannot-play (landing not Reachable" + (r.landingFloat ? (r.landingDeep ? "; floats too deep" : "; floats") : "") + ")";
                else if (_plan == PlanJump && _landWater) r.runtimeNext = "dobon (jump lands in the water)";
                else if (r.landingFloat && (_plan == PlanNone || _plan == PlanRetrieve)) r.runtimeNext = r.waterFetchOn ? (r.landingDeep ? "deep-disc" : "water fetch (11) after it floats") : "float, not fetched (M2 rules)";
                else r.runtimeNext = _plan == PlanNone ? (_landingValid ? "none" : "follow the disc (no landing predicted yet)") : r.kind;
                r.ok = true;
                // Continuation (2026-09-28, Codex consult): the committed plan is KEPT (no reset) and Plan runs again with each step's
                // input, so KeepPlan / ReshootJumpGround / DropMode are exercised as in the Chase. The one save / restore above covers the
                // whole sequence.
                if (cont != null) ProbeContinue(r, cont, temp.transform, fields, last);
            }
            catch (Exception e) { r.ok = false; r.error = e.GetType().Name + ": " + e.Message; }
            finally
            {
                Application.logMessageReceived -= hook;
                for (int i = 0; i < fields.Length; i++) { try { fields[i].SetValue(this, saved[i]); } catch (Exception) { } }
                if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
            }
            return r;
        }

        /// <summary>Hold check: T0's horizontal move allowed across a kept tick (numerical noise only).</summary>
        public const float HoldT0Tolerance = 0.05f;
        /// <summary>Hold check with m3's fixed T0 (_fxT0): it must not move at all (numerical noise).</summary>
        public const float HoldFixedT0Tolerance = 0.001f;

        /// <summary>Harness only (Edit mode): JumpGround at p near refY (the take-off rule: slope / bridge / river / noground). "" = a
        /// valid take-off. Every field is restored.</summary>
        public string _ProbeJumpGround(Vector3 p, float refY)
        {
            if (Application.isPlaying) return "play-mode";
            FieldInfo[] fields = typeof(KH_FrisbeeCatch).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            var saved = new object[fields.Length];
            for (int i = 0; i < fields.Length; i++) saved[i] = fields[i].GetValue(this);
            try { _inSolve = false; return JumpGround(p, refY) ? "" : _jgWhy; }
            catch (Exception e) { return "error " + e.Message; }
            finally { for (int i = 0; i < fields.Length; i++) { try { fields[i].SetValue(this, saved[i]); } catch (Exception) { } } }
        }

        void ProbeContinue(KH_FrisbeePlanProbeResult r, KH_FrisbeePlanProbeStep[] cont, Transform dog, FieldInfo[] fields, string[] last)
        {
            Vector3 prev = dog.position;
            foreach (var input in cont)
            {
                if (input == null) continue;
                var st = new KH_FrisbeePlanProbeStep { tNow = input.tNow, speed = input.speed, dogPos = input.dogPos, dogFwd = input.dogFwd };
                Vector3 f = input.dogFwd; f.y = 0f;
                if (f.sqrMagnitude < 1e-6f) { f = input.dogPos - prev; f.y = 0f; }
                if (f.sqrMagnitude < 1e-6f) f = dog.forward;
                dog.SetPositionAndRotation(input.dogPos, Quaternion.LookRotation(f.normalized, Vector3.up));
                _speed = input.speed;
                st.planBefore = _plan; st.modeBefore = ModeName(); st.bannedBefore = _bannedModes;
                int modeBefore = _mode; bool waterBefore = _landWater;
                st.fxOnBefore = _fxOn; st.fxT0Before = _fxT0;
                st.catchTBefore = _catchT;
                st.t0Before = _fxOn ? _fxT0 : _standRoot + _forward * (_jumpSpeed * _crouchReal);
                if (_plan == PlanJump && _crouchReal > 1e-4f)
                {
                    Vector3 d = st.t0Before - input.dogPos; d.y = 0f;
                    Vector3 fw = _forward; fw.y = 0f; fw = fw.sqrMagnitude > 1e-6f ? fw.normalized : Vector3.forward;
                    Vector3 right = Vector3.Cross(Vector3.up, fw);
                    st.crouchV = d.magnitude / _crouchReal; st.dv = st.crouchV - input.speed;
                    st.aheadRemain = Vector3.Dot(d, fw); st.lateral = Vector3.Dot(d, right);
                    Vector3 end = input.dogPos + fw * (input.speed * _crouchReal); end -= st.t0Before; end.y = 0f; st.endErr = end.magnitude;
                    Vector3 air = _catchRoot - st.t0Before; air.y = 0f;
                    st.vhAir = _tUp > 1e-4f ? air.magnitude / _tUp : 0f; st.airCap = st.vhAir > _takeoffCap + 1e-3f;
                }
                float tNow = input.tNow;
                st.held = _catchT - tNow;
                bool committed = _plan == PlanJump || _plan == PlanStandOrOvertake;
                st.expired = committed && st.held <= -(catchWindow + 0.02f);
                if (committed && !st.expired)
                {
                    Vector3 dogPos = dog.position;
                    if (_plan == PlanJump && (_mode == ModeChase || _mode == ModeFront))
                    {
                        // Planner.cs KeepPlan, the same lines on the same fields (read only; the real outcome comes from the call below)
                        float vTake = Mathf.Clamp(_speed, _takeoffMin, _takeoffCap);
                        if (vTake < _jumpSpeed) vTake = _jumpSpeed;
                        Vector3 toCur = _standRoot - dogPos; toCur.y = 0f;
                        if (toCur.magnitude <= RunJumpTrimDistance) vTake = _jumpSpeed;
                        Vector3 ground = new Vector3(_catchRoot.x, _catchRoot.y - _rootRise, _catchRoot.z);
                        Vector3 toGround = ground - dogPos; toGround.y = 0f;
                        float fit = toGround.magnitude / (_tUp + _crouchReal);
                        st.fit = fit;
                        if (vTake > fit) vTake = fit < RunJumpShortTakeoff ? RunJumpShortTakeoff : fit;
                        Vector3 cs = ground - _forward * (vTake * (_tUp + _crouchReal));
                        Vector3 slid = cs - _jumpRayAt; slid.y = 0f;
                        st.vTake = vTake; st.crouchStart = cs; st.t0 = cs + _forward * (vTake * _crouchReal);
                        st.slid = slid.magnitude; st.reshoot = slid.sqrMagnitude >= 0.25f;
                        st.availR = (_catchT - _crouchReal - _tUp) - tNow;
                        Vector3 toR = cs - dogPos; toR.y = 0f; st.pathR = toR.magnitude;
                    }
                    // KeepPlan on a copy of the state: its _jgWhy / drop are overwritten by the re-Solve inside the real tick
                    var snap = new object[fields.Length];
                    for (int i = 0; i < fields.Length; i++) snap[i] = fields[i].GetValue(this);
                    _groundY = SnapToGround(dogPos).y; _jgWhy = "";
                    st.keepCalled = true;
                    int raysBefore = _planRays;
                    st.kept = KeepPlan(tNow, dogPos, st.held);
                    st.groundChecked = _planRays > raysBefore;   // an empty _jgWhy only means "ground ok" when a ray was cast
                    st.keepWhy = _jgWhy; st.keepDrop = _dropReason; st.bannedAfterKeep = _bannedModes;
                    for (int i = 0; i < fields.Length; i++) { try { fields[i].SetValue(this, snap[i]); } catch (Exception) { } }
                }
                last[0] = null;
                Plan(tNow);
                st.planLine = last[0] ?? "(no plan= line: the plan was kept unchanged)";
                st.kind = _plan == PlanJump ? "jump" : _plan == PlanRetrieve ? "ground" : _plan == PlanStandOrOvertake ? (_runningCatch ? "over" : "stand") : "none";
                st.mode = _plan == PlanJump || _plan == PlanStandOrOvertake ? ModeName() : "-";
                st.tail = SolveTail(_plan); st.whyA = _whyA; st.whyB = _whyB; st.whyC = _whyC; st.bannedAfter = _bannedModes;
                if (_plan == PlanJump || _plan == PlanStandOrOvertake)
                {
                    st.landWater = _landWater; st.catchT = _catchT; st.takeoffY = _takeoffY; st.landY = _landY; st.dY = _landY - _takeoffY;
                    st.standRoot = _standRoot; st.landPt = _landPt;
                }
                st.next = _plan == PlanJump && _landWater ? "dobon" : st.kind;
                st.fxOnAfter = _fxOn; st.fxT0After = _fxT0; st.standRootAfter = _standRoot;
                st.fxAhead = _fxAhead; st.fxLat = _fxLat; st.fxCrouchV = _fxCrouchV; st.fxDv = _fxDv; st.fxEndErr = _fxEndErr;
                // T0: the runtime's fixed T0 when the plan carries one (m3 (a)), else C0 + f x _jumpSpeed x crouch
                st.t0After = st.fxOnBefore && _fxOn ? _fxT0 : _standRoot + _forward * (_jumpSpeed * _crouchReal);
                if (st.fxOnBefore) st.t0Before = st.fxT0Before;
                Vector3 mv = st.t0After - st.t0Before; mv.y = 0f; st.t0Move = mv.magnitude;
                st.sameMode = _plan == st.planBefore && _mode == modeBefore;
                st.sameCatch = Mathf.Abs(_catchT - st.catchTBefore) < 1e-4f;
                var why = new System.Collections.Generic.List<string>();
                if (!st.keepCalled) why.Add("KeepPlan not called" + (st.expired ? " (expired)" : ""));
                else if (!st.kept) why.Add("not kept drop=" + st.keepDrop + (st.keepWhy.Length > 0 ? " jgWhy=" + st.keepWhy : "") + (st.groundChecked ? "" : " (no ground ray)"));
                if (!st.sameMode) why.Add("plan/mode changed " + st.modeBefore + "->" + st.mode);
                if (!st.sameCatch) why.Add("catchT " + st.catchTBefore.ToString("F3") + "->" + _catchT.ToString("F3"));
                if (st.t0Move > (st.fxOnBefore ? HoldFixedT0Tolerance : HoldT0Tolerance)) why.Add("T0 moved " + st.t0Move.ToString("F4") + " m");
                if (st.fxOnBefore && !_fxOn) why.Add("fixed T0 lost (_fxOn false)");
                if (!_landWater) why.Add(waterBefore ? "water landing lost" : "not a water landing");
                if (st.keepDrop.Length > 0) why.Add("drop=" + st.keepDrop);
                if ((st.bannedAfter & ~st.bannedBefore) != 0) why.Add("banned +bits " + st.bannedBefore + "->" + st.bannedAfter);
                st.holdOk = why.Count == 0;
                st.holdWhy = string.Join("; ", why.ToArray());
                if (input.beginCrouch && _plan == PlanJump && _fxOn)
                {
                    // BeginJump, as Motion does it: the arc anchored to the planned instant (flight time here: _t0 = 0), the crouch now.
                    var snapC = new object[fields.Length];
                    for (int i = 0; i < fields.Length; i++) snapC[i] = fields[i].GetValue(this);
                    try
                    {
                        _t0 = 0.0;
                        double now = _catchT - _tUp - _crouchReal; if (now < tNow) now = tNow;
                        _jumpStart = _t0 + (_catchT - _tUp - _crouchReal); if (_jumpStart > now) _jumpStart = now;
                        _fxCrouchV = -1f; _fxDv = 0f;
                        st.crouchCalled = true; st.crouchNow = (float)now;
                        st.crouchOk = FixedCrouchBegin(now);
                        st.crouchRefused = st.crouchOk ? "" : (_dropReason.StartsWith("crouch-") ? _dropReason.Substring(7) : _dropReason);
                        st.crouchVBegin = _fxCrouchV; st.crouchDvBegin = _fxDv; st.crouchAhead = _fxAhead; st.crouchLat = _fxLat;
                        if (st.crouchOk)
                        {
                            // Motion.UpdateJump's first air frame (read-only mirror of its lines; the gate itself is the runtime's
                            // JumpGround / AirLanding): FixedCrouchEnd, the air velocity to the catch root, the vertical speed, the gate
                            FixedCrouchEnd();
                            Vector3 start = dogRoot.position; start.y = _groundY;
                            Vector3 need = _catchRoot - start; need.y = 0f;
                            Vector3 vel = need / Mathf.Max(0.05f, _tUp);
                            float cap = chaseRunSpeed * 1.2f;
                            if (vel.magnitude > cap) vel = vel.normalized * cap;
                            if (Vector3.Dot(vel, _forward) < 0f) vel = Vector3.zero;
                            float g = frisbee.gravity;
                            float vy = (_catchRoot.y - _groundY) / Mathf.Max(0.05f, _tUp) + 0.5f * g * _tUp;
                            float hv = vel.magnitude;
                            Vector3 hdir = hv > 1e-4f ? vel / hv : _forward;
                            _inSolve = false; _jgWhy = "";
                            bool takeoffOk = JumpGround(start, _groundY);
                            st.liftoffCalled = true; st.liftoffVh = hv; st.liftoffVy = vy;
                            st.liftoffOk = takeoffOk && AirLanding(start, _groundY, hdir, hv, vy);
                            st.liftoffWhy = st.liftoffOk ? "" : (takeoffOk ? "landing outside (" : "take-off (") + _jgWhy + ")";
                            st.liftoffWater = st.liftoffOk && _alWater; st.liftoffLandY = st.liftoffOk ? _alY : 0f;
                        }
                    }
                    finally { for (int i = 0; i < fields.Length; i++) { try { fields[i].SetValue(this, snapC[i]); } catch (Exception) { } } }
                }
                r.steps.Add(st);
                prev = input.dogPos;
            }
        }
    }

    /// <summary>One continuation step: input (tNow, dogPos, dogFwd, speed) and what the next Plan tick did with the kept plan.</summary>
    [Serializable]
    public class KH_FrisbeePlanProbeStep
    {
        // input (dogFwd zero = the direction from the previous dog position). beginCrouch: after the tick, also run the crouch start
        // (BeginJump's FixedCrouchBegin, on a copy) at the planned crouch instant from this position and speed.
        public float tNow, speed; public Vector3 dogPos, dogFwd; public bool beginCrouch;
        // before the tick
        public int planBefore, bannedBefore; public string modeBefore = ""; public float held; public bool expired;
        // KeepPlan's momentum-jump numbers (B / C only; a read-only mirror of Planner.cs KeepPlan on the same fields)
        public float vTake, fit, slid, availR, pathR; public Vector3 crouchStart, t0; public bool reshoot;
        // KeepPlan itself, called on a copy of the state (restored before the real Plan tick): kept, _jgWhy before any re-Solve, drop=
        public bool keepCalled, kept; public string keepWhy = "", keepDrop = ""; public int bannedAfterKeep;
        // the real Plan tick
        public string kind = "", mode = "", planLine = "", tail = "", whyA = "", whyB = "", whyC = "", next = "";
        public bool landWater; public int bannedAfter; public float catchT, takeoffY, landY, dY; public Vector3 standRoot, landPt;
        // Hold check (Codex fix-a consult 2026-09-28 item 4). T0 = C0 + f x _jumpSpeed x crouch (ReshootJumpGround's definition), read
        // before and after the tick; slid (C0 - _jumpRayAt) is NOT a criterion - C0 may move, T0 must not.
        public Vector3 t0Before, t0After; public float t0Move, catchTBefore; public bool sameMode, sameCatch, groundChecked, holdOk;
        public string holdWhy = "";
        // The crouch correction to the FIXED T0, from this input (names as m3's Diagnostics): crouchV = |T0 - dog| / crouch (the speed a
        // crouch started here needs), dv = crouchV - speed, aheadRemain = (T0 - dog).f (signed; < 0 = past T0), lateral = (T0 - dog).right,
        // endErr = |dog + f x speed x crouch - T0| (where today's straight crouch at the current speed ends), vhAir = |catch - T0|xz / tUp,
        // airCap = vhAir above the plan's take-off cap.
        public float crouchV, dv, aheadRemain, lateral, endErr, vhAir; public bool airCap;
        // M3 fixed take-off (m3's (a), Water.cs KeepFixedT0 / FixedCrouchBegin): the runtime's own values before / after the tick
        public bool fxOnBefore, fxOnAfter; public Vector3 fxT0Before, fxT0After, standRootAfter;
        public float fxAhead, fxLat, fxCrouchV, fxDv, fxEndErr;
        // FixedCrouchBegin on a copy (beginCrouch): crouchOk, refused= (passed / no-time / fast / dv), its numbers, the instant used
        public bool crouchCalled, crouchOk; public string crouchRefused = ""; public float crouchNow, crouchVBegin, crouchDvBegin, crouchAhead, crouchLat;
        // The liftoff gate after an accepted crouch (Motion UpdateJump's first air frame, on the same copy): FixedCrouchEnd (root on
        // T0, _groundY = T0.y), then the take-off JumpGround and AirLanding from there. liftoffWater = the arc still comes down in the water.
        public bool liftoffCalled, liftoffOk, liftoffWater; public string liftoffWhy = ""; public float liftoffVh, liftoffVy, liftoffLandY;
    }

    /// <summary>What _ProbePlan returns (Editor side only). kind: jump / stand / over / ground / none (the harness's words).</summary>
    [Serializable]
    public class KH_FrisbeePlanProbeResult
    {
        public bool ok; public string error = "";
        public Vector3 p0, v0, dogPos; public float curve, tNow; public int variant;
        public bool p0Playable;
        public int chosen; public string kind = "", mode = "", tail = "", planLine = "", solveLine = "", runtimeNext = "";
        public string whyA = "", whyB = "", whyC = "", drop = ""; public int solveCount, bannedModes;
        public float bandIn = -1f, bandOut = -1f, bandIn2 = -1f, bandOut2 = -1f; public bool bandUneven;
        public int planRays, solveRays, laneRefused; public bool solveCapped;
        public float catchT, rise, tUp, tDown, crouch, jumpSpeed, takeoffY, landY, dY, vReq, vCap, slack, eta, path;
        public bool landWater;
        public Vector3 standRoot, catchRoot, landPt, forward, disc;
        public bool landingValid, landingReachable, landingFloat, landingDeep, waterFetchOn;
        public Vector3 landing;
        public System.Collections.Generic.List<KH_FrisbeePlanProbeStep> steps = new System.Collections.Generic.List<KH_FrisbeePlanProbeStep>();
    }
}
#endif
