using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    // IMP-DOG-036 stages 1-2 (user decision 2026-10-02): the dog DECIDES every `decisionEvery` frames (5, always - not fps-gated -,
    // phased by dogIndex, the interval capped at MaxDecisionGap at low fps) and its LEGS move every frame.
    //
    //   decision frame : UpdateTick() as before (all branches, handshakes, pose flags, gaze injections). While it runs, the movers
    //                    (Coast / StepToward / GatherStep / LeashGo / FaceTowards / FaceTowardsRate) record what they were asked to
    //                    do (LegRecord*), the last call of each kind wins, nested calls inside a mover are not recorded.
    //   legs frame     : RunLegs() replays that record for exactly one frame: the same mover with the same arguments (LeashGo and
    //                    GatherStep only advance the corners of the path the decision planned: no NavMesh re-plan; LeashGo's wall
    //                    clearance is consumed by the distance walked and cast again only when a new corner is reached). The
    //                    leashed dog's gaze / tail is held by the pet controller until the next decision (S2b). Nothing else:
    //                    no route / holder / stopover / invite / pose / errand decision, no WaterTick / WashErrandTick.
    //   per frame      : TickBusy() (as before, extended) decides whether this frame may be a legs frame at all. Busy = decide now
    //                    (and every frame while busy): the frame-count handshakes and one-frame injections of Fetch / disc / wash /
    //                    water / station / travel / greeting / treat / play / candy keep their old per-frame behaviour (stages 3-4).
    //
    // dt accounting (no double or lost movement): the movers take MoveDt() = this frame's Time.deltaTime, on decision frames and on
    // legs frames alike, and exactly one of the two runs per frame. Integrators that measure over the decision interval (the holder
    // speed EMA, LeashPose's yaw rate and stillness clock, _pauseHeld, ObserveSpeed, the breath in LateUpdate, the placed-sleep lift
    // that only the decision moves) take Dt() = _dt = the time since the previous decision (this frame included). With
    // decisionEvery = 1 every frame decides, _dt == Time.deltaTime and the dog runs exactly the pre-IMP-DOG-034 per-frame code.
    //
    // IMP-DOG-036 S3 (user decision 2026-10-02: "川の中の犬はじっと見ないので 10 フレームに 1 回"): a dog whose last decision ran
    // the water play (UpdateWaterPlay, HillsideWater.cs) decides every `waterDecisionEvery` frames (10, capped at MaxWaterDecisionGap)
    // and its legs frames are WaterRunLegs(): the same RunLegs replay, with the river / depth checks answered from the line the
    // decision validated (no spot query on a legs frame), the swim height from the recorded water surface, and the swim bob / pitch
    // on the visual child and the hips (LateUpdate) from the recorded pose. The gate is WaterLegsOk() instead of TickBusy().
    // The Fetch's / disc's water states, the RC-14 veto and the ease back after the play keep deciding every frame (TickBusy).
    //
    // IMP-DOG-036 S2b: a dog whose last decision found it resting (RestThinRecord: lying in the rest, or standing / lying in the
    // wander or the post-Fetch calm, still, no turn, nobody within pauseRadius, none of TickBusy's own-field states, the breath
    // silent) decides every `restDecisionEvery` frames (15, capped at MaxRestDecisionGap). Its legs frames are the usual RunLegs
    // (a still Coast: nothing to write); the gate is TickBusy() as before (ownership, leash attach, Fetch / disc, water, candy...)
    // plus RestLegsOk() (the root moved by anyone, a wash hold). Every other wake (treat, sleep call, live, gathering, nudge,
    // greeting, play window, wander waypoint, rest end, an approaching player) is polled by the decision and waits for it.
    public partial class KD_DogWanderController
    {
        const int DefaultDecisionEvery = 5;
        const float MaxDecisionGap = 0.2f;    // s: a decision is forced once this has passed (and the frame interval shrinks at low fps)
        const float DtClamp = 0.5f;           // s: IMP-DOG-036 S1/S2 fix 2 - the longest interval the integrators see (see Update)
        const int DefaultWaterDecisionEvery = 10;   // IMP-DOG-036 S3
        const float MaxWaterDecisionGap = 0.35f;    // s: IMP-DOG-036 S3 - the water play's cap (the legs' checked reach is built on it)
        const int DefaultRestDecisionEvery = 15;    // IMP-DOG-036 S2b
        // s: IMP-DOG-036 S2b - the rest cadence's cap. Below DtClamp by a frame at >= 20 fps, so the integrators (the breath, the
        // ObserveSpeed interval) still see the whole interval (_dt is not clamped)
        const float MaxRestDecisionGap = 0.45f;
        const float RestMoveEps = 0.01f;            // m: IMP-DOG-036 S2b - a root moved this far since the resting decision decides now

        [Tooltip("IMP-DOG-036: 判断を何フレームに 1 回にするか (既定 5)。1 = 毎フレーム判断 (A/B 用、IMP-DOG-034 以前と同じ経路)。0 以下は既定値")]
        public int decisionEvery = DefaultDecisionEvery;
        [Tooltip("IMP-DOG-036 S3: 水遊び中 (川の中) の判断を何フレームに 1 回にするか (既定 10、上限 0.35 秒)。1 = 毎フレーム判断。0 以下は既定値")]
        public int waterDecisionEvery = DefaultWaterDecisionEvery;
        [Tooltip("IMP-DOG-036 S2b: 休んでいる犬 (伏せ・立ち止まり。動かない・向きを変えない・近くに人が居ない・リード / 洗浄 / 水 / Fetch 無し) の判断を何フレームに 1 回にするか (既定 15、上限 0.45 秒)。1 = 使わない (decisionEvery のまま)。0 以下は既定値")]
        public int restDecisionEvery = DefaultRestDecisionEvery;
        // IMP-DOG-036 S2b: the last decision found the dog resting (RestThinRecord), and where its root was then
        private bool _restThin;
        private Vector3 _restAt;
        private bool _reachedSelection;   // this decision got past the leash / candy / Fetch / wash returns (UpdateTick)
        private int _dbgRestDecisions, _dbgRestSkips;   // IMP-DOG-036 S2b: decisions / legs frames while _restThin (the harness zeroes them)
        // IMP-DOG-036 S3: the frames of the previous and of this decision (WaterTick's "seen at the previous decision")
        private int _prevDecisionFrame = -1, _thisDecisionFrame = -1;
        private float _dt;                // this decision's delta time: the time since the previous decision (this frame included)
        private float _dtSince;           // time accumulated since the previous decision
        private bool _tickedThisFrame = true;   // LateUpdate: did this frame decide
        private bool _inTick;             // inside UpdateTick (Dt() returns _dt only there)
        private bool _candyTicked;        // last tick's UpdateCandyPerformance result (TickBusy)
        private int _decisionCount, _legsFrames;   // harness / measurement read-outs (IMP-DOG-036)
        private int _dbgWaterDecisions, _dbgWaterLegs;   // IMP-DOG-036 S3: decisions / legs frames while _waterThin (the harness zeroes them)
        private bool _decideNow;          // the next frame decides whatever its slot (OnEnable)

        /// <summary>IMP-DOG-036 S1/S2: decide on this dog's slot (or when busy, or once MaxDecisionGap has passed), else move the legs.</summary>
        void Update()
        {
            _dtSince += Time.deltaTime;
            // IMP-DOG-036 S3: the last decision left the dog in the water play -> the water cadence, cap and gate
            bool water = _waterThin;
            // IMP-DOG-036 S2b: the last decision found the dog resting -> the rest cadence, cap and gate (never with the water play)
            bool rest = !water && _restThin;
            // IMP-DOG-036 S2b: woken (the root moved, a wash hold) -> back to the normal cadence from this frame on: this frame
            // decides if it is the normal slot or MaxDecisionGap has passed, exactly as if the rest cadence had never been on
            if (rest && !RestStillOk()) { rest = false; _restThin = false; }
            int every = water ? WaterDecisionFrames() : (rest ? RestDecisionFrames() : DecisionEvery());
            float gap = water ? MaxWaterDecisionGap : (rest ? MaxRestDecisionGap : MaxDecisionGap);
            Vector3 probeFrom = StepProbeBegin();   // IMP-DOG-036 step probe (harness read-out)
            // IMP-DOG-036 S1/S2 fix 2: the slot only staggers the dogs; elapsed time guarantees the cap. `every` follows the fps
            // (4 <-> 5) and its modulo phase can then skip past a slot, so a decision is also forced once MaxDecisionGap has passed
            if (every > 1 && !_decideNow && ((Time.frameCount + dogIndex) % every) != 0
                && _dtSince < gap && (water ? WaterLegsOk() : !TickBusy()))
            {
                _tickedThisFrame = false;
                if (water) WaterRunLegs(); else RunLegs();
                if (rest) _dbgRestSkips++;   // IMP-DOG-036 S2b harness read-out
                StepProbeEnd(probeFrom, DbgSrcLegs, Time.deltaTime);
                return;
            }
            _decideNow = false;
            _prevDecisionFrame = _thisDecisionFrame; _thisDecisionFrame = Time.frameCount;   // IMP-DOG-036 S3
            if (water) _dbgWaterDecisions++;   // IMP-DOG-036 S3 harness read-out: decisions taken while thinned (legs: WaterRunLegs)
            if (rest) _dbgRestDecisions++;     // IMP-DOG-036 S2b harness read-out: decisions taken on the rest cadence
            _waterPlayedNow = false;                                                          // IMP-DOG-036 S3 (UpdateWaterPlay raises it)
            // IMP-DOG-036 S1/S2 fix 2: with the forced decision _dtSince is at most MaxDecisionGap + one frame (Unity already clamps a
            // frame's deltaTime to maximumDeltaTime, 0.333 s). The integrators (holder-speed EMA: Min(1, dt/tau); yaw rate and
            // ObserveSpeed: divide by dt; _pauseHeld / breath: real time) are bounded for any dt, but the jump tolerances scale with
            // dt (max(1 m, 10 m/s x dt), max(1.5 m, 12 m/s x dt)), so the interval is clamped at DtClamp to keep them below 5 m / 6 m
            _dt = _dtSince < DtClamp ? _dtSince : DtClamp;
            _dtSince = 0f;
            _tickedThisFrame = true;
            _decisionCount++;
            // the record of this decision's movers (RunLegs replays it until the next decision)
            _legKind = LegNone; _legFaceKind = LegNone; _legRec = true; _legDepth = 0;
            _standGaze = false;   // IMP-DOG-036 S2b: raised by LeashPose when this decision holds the gaze (HeldGazeEnd)
            _inTick = true;
            UpdateTick();
            _inTick = false;
            _legRec = false;
            WaterThinRecord();   // IMP-DOG-036 S3: may the next frames be water legs frames, and what they replay (HillsideWater.cs)
            RestThinRecord();    // IMP-DOG-036 S2b: may the next decision wait the rest cadence (after WaterThinRecord: never both)
            HeldGazeEnd();       // IMP-DOG-036 S2b: a held gaze this decision did not renew ends on this frame, as before
            StepProbeEnd(probeFrom, DbgSrcDecision, _dt);
        }

        // ---------------------------------------------------------------- IMP-DOG-036 S2b: the leashed dog's held gaze
        // LeashPose used to inject a one-frame gaze / tail (KD_DogPetController._SetGaze / _SetUrge) and RunLegs re-sent it on every
        // legs frame with the holder's current head (GetTrackingData + 2 cross-behaviour calls per frame per dog, on every client).
        // Now LeashPose sends it once per decision with an expiry (_SetGazeFor / _SetUrgeFor, LeashGazeHold s > the longest legs run,
        // MaxDecisionGap) and the legs frames send nothing. A decision that does not renew it clears it on its own frame
        // (HeldGazeEnd), so the gaze ends on the same frame as before; the only difference is that the look point is the holder's
        // head at the decision (up to MaxDecisionGap old) instead of this frame's.
        const float LeashGazeHold = 0.3f;   // s: MaxDecisionGap (0.2) + margin. A legs frame is always < MaxDecisionGap after its decision
        private bool _gazeHeld;              // a held gaze was sent and not cleared yet

        void HeldGazeEnd()
        {
            if (_gazeHeld && !_standGaze && petController != null) petController._ClearHeld();
            _gazeHeld = _standGaze;
        }

        // ---------------------------------------------------------------- IMP-DOG-036 S2b: the rest cadence
        /// <summary>The rest cadence in frames: restDecisionEvery (15), shortened at low fps so it stays within MaxRestDecisionGap.</summary>
        int RestDecisionFrames()
        {
            return DecisionFrames(restDecisionEvery <= 0 ? DefaultRestDecisionEvery : restDecisionEvery, MaxRestDecisionGap);
        }

        /// <summary>End of a decision: did it leave the dog resting, so that the next decision may wait the rest cadence? Own fields
        /// only. Everything TickBusy reads from other behaviours is still read on every legs frame (Update), so this only has to
        /// exclude what TickBusy would not see and what a longer gap would show (motion, a turn, the breath fade, a player near).</summary>
        void RestThinRecord()
        {
            _restThin = false;
            if (!_reachedSelection || _waterThin || dogRoot == null) return;
            if (restDecisionEvery == 1 || decisionEvery == 1) return;   // off / the per-frame A/B mode
            // the branches that rest: lying in the rest (after the drink and the move), the wander (outside its window, or arrived
            // at its waypoint), the post-Fetch calm. Each of them ended in a Coast this decision
            int b = _selectedBehavior;
            if (!(b == BehaviorWander || b == BehaviorCalm || (b == BehaviorRest && _restPhase == RestPhaseLie))) return;
            // still: a Coast at speed 0 (RunLegs skips it) and no turn recorded (the rest's turn before lying down is done)
            if (_speed != 0f || _legKind != LegCoast || _legFaceKind != LegNone) return;
            // nobody within pauseRadius (petting, the idle pant, the approach wake), and none of TickBusy's own-field states
            if (_paused || _wasLeashed || _waterLeashVeto) return;
            if (waterState != WaterOff || _waterSwimming || _swimYValid || _swimPitchNow != 0f || _hipsWrote) return;
            if (_washActive || _washLeashStop || _stationIdx >= 0 || _activityBusy) return;
            if (_leaveActive || _leaveLowering) return;   // 2026-10-02: walking out of a wash station (HillsideStationLeave.cs)
            if (_greetingActive || _nudging || _treatBusy || _playing) return;
            if (candyPerformanceActive || _candyTicked || _candyPetResumePending || _writtenCallTime != 0f) return;
            // the breath fades are integrated and written on decision frames only: a longer gap would step the volume
            if (breathSource != null && !_breathSilent) return;
            // a wash hold the wash branch did not take (it yields to an errand): stay on the normal cadence (RestStillOk would end it)
            if (wash != null && wash._WashHold()) return;
            _restThin = true;
            _restAt = dogRoot.position;
        }

        /// <summary>IMP-DOG-036 S2b: every frame of the rest cadence (before the slot test): may it go on? False = back to the normal
        /// cadence (Update), never "decide every frame": a dog pushed on every frame by KD_DogPack, or a wash hold the wash branch
        /// yields (an errand), then decides exactly as often as without S2b, not more. TickBusy still runs on every legs frame.</summary>
        bool RestStillOk()
        {
            // anything moved the root since the decision (KD_DogPack's push, VRCObjectSync on a non-Owner, a teleport, a harness write)
            Vector3 moved = dogRoot.position - _restAt;
            if (moved.sqrMagnitude > RestMoveEps * RestMoveEps) return false;
            // a wash hold started (the washer touching the dog): the wash stop starts within the normal cadence, as before
            if (wash != null && wash._WashHold()) return false;
            return true;
        }

        // ---------------------------------------------------------------- IMP-DOG-036 step probe (debug read-out)
        // Who moved this dog's root, for the harness (GetProgramVariable). Per-interval maxima (each split separately):
        //   _dbgMaxStep        metres (3D, as the harness measures) of the largest single piece
        //   _dbgMaxStepSrc     1 = this dog's decision frame, 2 = a legs frame (RunLegs), 3 = between two of our Updates (someone
        //                      else moved the root: KD_DogPack's push, Fetch / disc legs, VRCObjectSync on a non-Owner, a harness write)
        //   _dbgMaxStepMover   the mover recorded / replayed that frame (_legKind: 0 none, 1 Coast, 2 StepToward, 3 GatherStep, 4 LeashGo)
        //   _dbgMaxStepWriter  a direct root write in that frame (0 none, 1 TeleportTo, 2 travel apply, 3 station placement,
        //                      4 water recover teleport, 5 GroundAfterPlacedSleep (sleep / station release),
        //                      6 IMP-DOG-036 S3: the water legs frame's swim height (WaterSwimHeight from the recorded surface))
        //   _dbgMaxStepMode / _dbgMaxStepBehavior / _dbgMaxStepWater   leashMode, _selectedBehavior, waterState at the end of the frame
        //   _dbgMaxStepDt      the dt that frame's movers / integrators used (decision: _dt; legs: Time.deltaTime; between: deltaTime)
        //   _dbgMaxStepFrame, _dbgMaxStepFrom, _dbgMaxStepTo, _dbgBigSteps (count of pieces > DbgBigStep), _dbgTeleports
        // IMP-DOG-036 S1/S2 fix 4b - whole-frame displacement (everything any script did to the root in one frame: the start of this
        // dog's previous Update -> the start of this one): _dbgMaxFrameStep, _dbgMaxFrameStepFrame, _dbgMaxFrameStepFrom / To.
        // IMP-DOG-036 S1/S2 fix 4c - the last DbgRingSize pieces or whole frames above DbgRingStep (src 4 = whole frame):
        //   _dbgRingFrame[], _dbgRingSrc[], _dbgRingMover[], _dbgRingWriter[], _dbgRingDt[], _dbgRingFrom[], _dbgRingTo[];
        //   _dbgRingCount = how many were ever recorded (the newest is at (_dbgRingCount - 1) % DbgRingSize).
        // IMP-DOG-036 S1/S2 fix 4a: reset only by _DebugResetStep(), which also turns the probe on (off until then, ~12 extern/frame).
        const int DbgSrcDecision = 1, DbgSrcLegs = 2, DbgSrcBetween = 3, DbgSrcFrame = 4;
        const int DbgWTeleport = 1, DbgWTravel = 2, DbgWStation = 3, DbgWWaterRecover = 4, DbgWGround = 5, DbgWSwimLegs = 6;
        const float DbgBigStep = 0.5f, DbgRingStep = 0.3f;
        const int DbgRingSize = 4;
        private bool _dbgStepOn;   // off by default (cost); the harness turns it on through _DebugResetStep()
        private int _dbgWriter;
        private float _dbgMaxStep, _dbgMaxStepDt;
        private int _dbgMaxStepSrc, _dbgMaxStepMover, _dbgMaxStepWriter, _dbgMaxStepMode, _dbgMaxStepBehavior, _dbgMaxStepWater, _dbgMaxStepFrame;
        private Vector3 _dbgMaxStepFrom, _dbgMaxStepTo, _dbgLastEnd;
        private bool _dbgLastEndValid;
        private int _dbgBigSteps, _dbgTeleports;
        private float _dbgMaxFrameStep;
        private int _dbgMaxFrameStepFrame;
        private Vector3 _dbgMaxFrameStepFrom, _dbgMaxFrameStepTo, _dbgPrevStart;
        private bool _dbgPrevStartValid;
        private int[] _dbgRingFrame, _dbgRingSrc, _dbgRingMover, _dbgRingWriter;
        private float[] _dbgRingDt;
        private Vector3[] _dbgRingFrom, _dbgRingTo;
        private int _dbgRingCount;

        public void _DebugResetStep()
        {
            _dbgMaxStep = 0f; _dbgMaxStepDt = 0f; _dbgMaxStepSrc = 0; _dbgMaxStepMover = 0; _dbgMaxStepWriter = 0;
            _dbgMaxStepMode = 0; _dbgMaxStepBehavior = 0; _dbgMaxStepWater = 0; _dbgMaxStepFrame = 0; _dbgBigSteps = 0; _dbgTeleports = 0;
            _dbgMaxFrameStep = 0f; _dbgMaxFrameStepFrame = 0; _dbgRingCount = 0;
            _dbgLastEndValid = false; _dbgPrevStartValid = false;
            _dbgStepOn = true;
        }

        Vector3 StepProbeBegin()
        {
            if (!_dbgStepOn || dogRoot == null) return Vector3.zero;
            _dbgWriter = 0;
            Vector3 p = dogRoot.position;
            if (_dbgLastEndValid) StepProbeRecord(_dbgLastEnd, p, DbgSrcBetween, Time.deltaTime);
            // fix 4b: the whole frame (our previous Update, the scripts after it, the scripts before this one)
            if (_dbgPrevStartValid)
            {
                float w = Vector3.Distance(_dbgPrevStart, p);
                if (w > _dbgMaxFrameStep) { _dbgMaxFrameStep = w; _dbgMaxFrameStepFrame = Time.frameCount; _dbgMaxFrameStepFrom = _dbgPrevStart; _dbgMaxFrameStepTo = p; }
                if (w > DbgRingStep) StepProbeRing(DbgSrcFrame, 0, 0, Time.deltaTime, _dbgPrevStart, p);
            }
            _dbgPrevStart = p; _dbgPrevStartValid = true;
            return p;
        }

        void StepProbeEnd(Vector3 from, int src, float dt)
        {
            if (!_dbgStepOn || dogRoot == null) return;
            Vector3 p = dogRoot.position;
            StepProbeRecord(from, p, src, dt);
            _dbgLastEnd = p; _dbgLastEndValid = true;
        }

        void StepProbeRecord(Vector3 from, Vector3 to, int src, float dt)
        {
            float d = Vector3.Distance(from, to);
            int mover = src == DbgSrcBetween ? 0 : _legKind;
            int writer = src == DbgSrcBetween ? 0 : _dbgWriter;
            if (d > DbgBigStep) _dbgBigSteps++;
            if (src != DbgSrcBetween && _dbgWriter == DbgWTeleport) _dbgTeleports++;
            if (d > DbgRingStep) StepProbeRing(src, mover, writer, dt, from, to);
            if (d <= _dbgMaxStep) return;
            _dbgMaxStep = d; _dbgMaxStepSrc = src; _dbgMaxStepDt = dt; _dbgMaxStepFrame = Time.frameCount;
            _dbgMaxStepMover = mover; _dbgMaxStepWriter = writer;
            _dbgMaxStepMode = leashMode; _dbgMaxStepBehavior = _selectedBehavior; _dbgMaxStepWater = waterState;
            _dbgMaxStepFrom = from; _dbgMaxStepTo = to;
        }

        void StepProbeRing(int src, int mover, int writer, float dt, Vector3 from, Vector3 to)
        {
            if (_dbgRingFrame == null)
            {
                _dbgRingFrame = new int[DbgRingSize]; _dbgRingSrc = new int[DbgRingSize]; _dbgRingMover = new int[DbgRingSize];
                _dbgRingWriter = new int[DbgRingSize]; _dbgRingDt = new float[DbgRingSize];
                _dbgRingFrom = new Vector3[DbgRingSize]; _dbgRingTo = new Vector3[DbgRingSize];
            }
            int k = _dbgRingCount % DbgRingSize;
            _dbgRingFrame[k] = Time.frameCount; _dbgRingSrc[k] = src; _dbgRingMover[k] = mover; _dbgRingWriter[k] = writer;
            _dbgRingDt[k] = dt; _dbgRingFrom[k] = from; _dbgRingTo[k] = to;
            _dbgRingCount++;
        }

        /// <summary>The decision interval in frames: decisionEvery (5), shortened at low fps so it stays within MaxDecisionGap
        /// (20 fps -> 4). 1 = every frame.</summary>
        int DecisionEvery()
        {
            return DecisionFrames(decisionEvery <= 0 ? DefaultDecisionEvery : decisionEvery, MaxDecisionGap);
        }

        /// <summary>IMP-DOG-036 S3: the water play's interval in frames: waterDecisionEvery (10), shortened at low fps so it stays
        /// within MaxWaterDecisionGap (20 fps -> 7). 1 = every frame.</summary>
        int WaterDecisionFrames()
        {
            return DecisionFrames(waterDecisionEvery <= 0 ? DefaultWaterDecisionEvery : waterDecisionEvery, MaxWaterDecisionGap);
        }

        int DecisionFrames(int e, float gap)
        {
            if (e <= 1) return 1;
            float sdt = Time.smoothDeltaTime;
            if (sdt > 0.0001f)
            {
                int cap = (int)(gap / sdt);
                if (cap < 1) cap = 1;
                if (cap < e) e = cap;
            }
            return e;
        }

        /// <summary>Integrators: inside this dog's decision, the time since the previous decision (_dt); when another behaviour
        /// calls in (Fetch's _WaterFetchStep, KD_DogPack, the disc's water legs...) the plain Time.deltaTime, exactly as before.</summary>
        float Dt()
        {
            return _inTick ? _dt : Time.deltaTime;
        }

        /// <summary>IMP-DOG-036: the movers' step time. Always this frame's Time.deltaTime - the legs move every frame (on a decision
        /// frame through the decision's own call, on the other frames through RunLegs), so one frame is one step.</summary>
        float MoveDt()
        {
            return Time.deltaTime;
        }

        // ---------------------------------------------------------------- IMP-DOG-036: the legs record
        const int LegNone = 0, LegCoast = 1, LegStep = 2, LegGather = 3, LegLeash = 4;   // _legKind
        const int LegFace = 1, LegFaceRate = 2;                                         // _legFaceKind
        private bool _legRec;          // inside a decision: the movers record what they were asked
        private int _legDepth;         // > 0 inside a mover (its inner calls are not recorded)
        private int _legKind, _legFaceKind;
        private Vector3 _legTarget, _legFacePos;
        private float _legStop, _legSpeed, _legFaceRate;
        private bool _legNav;          // StepToward's _navStep at the recorded call
        // LeashGo's wall clearance, decided on the decision (or when a legs frame reaches a new corner), consumed by the walk
        private float _legClear;
        private int _legClearCorner = -1;
        private Vector3 _legClearFrom;

        bool LegRecording() { return _legRec && _legDepth == 0; }

        /// <summary>One legs frame (not a decision): replay the last decision's movers for this frame only.</summary>
        void RunLegs()
        {
            _legsFrames++;
            // S1: the leashed dog's look at its holder (LeashPose) was re-sent here every legs frame. IMP-DOG-036 S2b: LeashPose now
            // sends it with an expiry that outlasts the legs run (_SetGazeFor, HeldGazeEnd above), so nothing is sent here
            // Positions are the Owner's (VRCObjectSync carries them); a non-Owner's legs would only pick the legs from the observed
            // speed, which does not change between decisions
            if (!_dogOwner) return;
            int kind = _legKind;
            if (kind == LegCoast)
            {
                // S2: a dog standing still with nothing left to write (lying, sitting, asleep, waiting) skips Coast: with _speed 0
                // Coast only calls ApplyMoveAnim, and under these conditions ApplyMoveAnim writes nothing (moving / running / look /
                // eat equal to what it last wrote, the stop hold over, the playback speed 1 written, Fetch / disc idle - TickBusy
                // has just read them for an unleashed dog; a leashed one always runs Coast)
                if (!(_speed == 0f && !_wasLeashed && !_wasMoving && !_wasRunning && !_heldRunning && !_leashAnimationDirty
                    && _wroteAnimSpeed == 1f && _wasLooking == (_look && !_eatPose && !_wantSit) && _wasEatPose == (_eatPose && !_wantSit)
                    && Time.time >= _stopHoldUntil))
                    Coast();
            }
            else if (kind == LegStep)
            {
                _navStep = _legNav;
                StepToward(_legTarget, _legStop, _legSpeed);
                _navStep = false;
            }
            else if (kind == LegGather) GatherLegs(_legTarget, _legSpeed);
            else if (kind == LegLeash) LeashGoLegs();
            // The turn the decision asked for, after the legs (every recorded call site turns after its Coast / step). Not while a
            // static pose is shown: those call sites stop turning once the pose is written (BUG-DOG-001), the next decision agrees
            if (_legFaceKind != LegNone && !_wasLieDown && !_wasSleep && !_wasSit && !_wasEatPose)
            {
                if (_legFaceKind == LegFace) FaceTowards(_legFacePos);
                else FaceTowardsRate(_legFacePos, _legFaceRate);
            }
        }

        /// <summary>Is the dog doing anything that must decide this frame? Read on non-slot frames only, before UpdateTick lowers
        /// its per-frame flags, so the own-field checks see the last decision's conclusion. When in doubt a state counts as busy.
        /// Own fields first; the cross-behaviour reads last.</summary>
        bool TickBusy()
        {
            // IMP-DOG-036: an ownership change decides now (a legs frame must never move a root this client no longer owns)
            if (_dogOwner != IsDogOwner()) return true;
            // A leash vetoed in the water: every frame (RC-14, the water play walks the dog out)
            if (_waterLeashVeto) return true;
            // IMP-DOG-036 S1: a leashed dog takes legs frames unless something in LeashLegsOk is live
            if (_wasLeashed) return !LeashLegsOk();
            // IMP-DOG-036 S2: a leash attached since the last decision decides now (the attach edge, RC-14's check, the walk).
            // S2b: walkers[dog] read directly (= KH_LeashManager._IsLeashed, one field read instead of a call)
            if (LegsWalker() >= 0) return true;
            // IMP-DOG-036 S2: a moving dog whose decision recorded no mover (a branch switch returning early) decides again now,
            // so its legs never freeze mid-step (a still dog with nothing to replay - the placed sleep - just waits)
            if (_dogOwner && _legKind == LegNone && _speed > 0f) return true;
            // Water play (Owner) and the swim height / nose-up pitch / hips bob still easing back after the water.
            // (IMP-DOG-036 S3: a water play the last decision ran is gated by WaterLegsOk instead - Update never asks TickBusy then)
            if (waterState != WaterOff || _waterSwimming || _swimYValid || _swimPitchNow != 0f || _hipsWrote) return true;
            // Being washed (the wash branches raise it; _washLastFrame handshake).
            // 2026-10-02: riding a raised wash stand down writes the root itself every frame (HillsideStationLeave.cs), as on the stand
            if (_washActive || _leaveLowering) return true;
            // Candy performance / mount / exit hold, and a pending petting resume.
            if (candyPerformanceActive || _candyTicked || _candyPetResumePending) return true;
            // Branches that inject the one-frame gaze / tail into the pet controller, or bind the dog to others.
            if (_greetingActive || _nudging || _treatBusy || _activityBusy) return true;
            int b = _selectedBehavior;
            if (b == BehaviorTreat || b == BehaviorNudge || b == BehaviorGreeting || b == BehaviorActivity
                || b == BehaviorPlay || b == BehaviorWater) return true;
            // Rest: walking to the water bowl looks at it, drinking wags the tail. Lying down is idle.
            if (b == BehaviorRest && (_restPhase == RestPhaseToWater || _restPhase == RestPhaseDrink)) return true;
            // Called to sleep beside someone: walking there looks at the caller. Once frozen (asleep) it is idle.
            if (b == BehaviorSleep && !_sleepFrozen) return true;
            // IMP-DOG-036 S1/S2 fix 3: frozen on a placed bed but still turning / settling onto the slot (UpdatePlacedSleep writes the root
            // itself, not through a replayable mover): every frame until it has arrived, as before
            if (b == BehaviorSleep && _placedSleepActive && _placedSettling) return true;
            // A companion stretch writes a running CallTime every frame (LateUpdate)
            if (_writtenCallTime != 0f) return true;
            // Fetch / disc (cross-behaviour reads). No Fetch at all keeps the old per-frame path.
            if (fetchController == null) return true;
            // S2b: IsWaterFetching (dogState >= StateWaterChase = 6) is never true when IsIdle (dogState == StateIdle = 0) holds,
            // so after `!IsIdle` it was a dead read; dropped (the same answer)
            if (!fetchController.IsIdle || fetchController.WaterPose != 0) return true;
            if (FrisbeeActive) return true;
            return false;
        }

        /// <summary>IMP-DOG-036 S2b: walkers[dogIndex] read straight from the synced array (KH_LeashManager._IsLeashed is
        /// `walkers[dog] >= 0`; the same direct read as WaterLegsOk). -1 = no manager / not leashed.</summary>
        int LegsWalker()
        {
            if (hillsideLeashes == null) return -1;
            int[] w = hillsideLeashes.walkers;
            if (w == null || dogIndex < 0 || dogIndex >= w.Length) return -1;
            return w[dogIndex];
        }

        /// <summary>IMP-DOG-036 S1: may a leashed dog take a legs frame? False (= decide now, and every frame while it stays false) for
        /// every frame-count handshake and every event the last decision could not have seen.
        /// IMP-DOG-036 S2b: what the legs frame still reads from other behaviours is the leash (walkers), the root taken by Fetch / the
        /// disc (AnimatorYielded) and the holder walking off. The travel ticket, the wash hold / satisfied sign and the non-Owner's
        /// synced wet pose moved to the decision (read there as before, at most one decision gap later): each of them only starts
        /// something the decision does (apply the ticket, stop for the wash, pose wet), no legs frame writes anything they guard
        /// (no teleport, no wash frame, no water handshake on a legs frame), and once started they are own fields read below.</summary>
        bool LeashLegsOk()
        {
            // the last decision went through the leash branch normally (not yielded to Fetch / disc, holder valid)
            if (!_legLeashArmed || _leashYielded) return false;
            // a decision that switched mode and returned before any mover (a pause / stopover / invitation starting, a warp):
            // the new branch walks from the next decision, which is this frame
            if (_dogOwner && _legKind == LegNone) return false;
            // water: any water state, the swim height or pitch still easing, the hips bob (RC-14 / _waterSeenFrame)
            if (waterState != WaterOff || _waterSwimming || _swimYValid || _swimPitchNow != 0f || _hipsWrote) return false;
            // wash: being washed / the leashed wash stop (_washLastFrame), held at a wash station
            if (_washActive || _washLeashStop || _stationIdx >= 0 || _activityBusy) return false;
            // leash detached or another holder since the decision. S2b: one direct read of walkers[dog]; _legHolderId is >= 0
            // whenever _legLeashArmed (the decision armed it with walkers[dog] after _IsLeashed), so a detach (-1) differs too
            if (LegsWalker() != _legHolderId) return false;
            // Fetch / the disc took the root and the Animator: the yield edge is the decision's
            if (AnimatorYielded()) return false;
            // wet at the last pose refresh (Owner: its own write). S2b: the non-Owner's synced WaterPose is the decision's (its legs
            // frames write nothing; the hips / swim flags its decision raises are the own fields above)
            if (_dogOwner && _waterLastWet) return false;
            // the holder stood still at the decision and has moved since (walking off: decide on this frame, as C-1 did)
            if (_legStanding && Utilities.IsValid(_standHolder))
            {
                Vector3 moved = _standHolder.GetPosition() - _standHolderAt; moved.y = 0f;
                if (moved.sqrMagnitude > StandHolderMove * StandHolderMove) return false;
            }
            return true;
        }
    }
}
