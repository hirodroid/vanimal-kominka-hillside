using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    // IMP-DOG-020 stage 1 (DES-DOG-004 C-1): straight moves (free roaming, play, greeting, treat, nudge, rest, the
    // gathering's "no complete path, walk straight" fallback...) must not walk into the river. They all go through
    // StepToward / Coast, which ask _BlockedByRiver for every step and stop short of the band instead.
    //
    // NavMesh moves (LeashGo / GatherStep) are left as they were: they mark their StepToward with _navStep, since the
    // baked NavMesh already excludes the riverbed (bank ground above the exclusion's top can remain walkable inside the
    // band, and a leash walk must not freeze there). The gathering re-plans its NavMesh path every 0.5 s in GatherStep,
    // so a dog that walked straight to the bank waits there and takes the bridge as soon as a complete path appears.
    //
    // IMP-DOG-020 steps 2-3 (DES-DOG-004 B-1, B-2 "stage 0", C-6/6b/9/11): WaterPlay, the Owner's local state.
    //   start  : a release within 12 m of the spot's entry -> the first play within 5 s (100 %, once per dog per stay);
    //            later, near the entry, a 30 % roll every 45 s. Never while leashed / fetching / in a stronger behaviour
    //            (those branches come first in Update), never while a gathering runs.
    //   states : 1 walk to the entry -> 2 wade (random grid cells, 20-40 s) -> 3 walk out to the saved exit -> 4 stand 1.2 s.
    //   pose   : waterPose (KD_DogFetchController, synced) = 1 while the feet are wet in the spot (depth > 0.02 m), else 0.
    //            Stage 0 has no swimming: a step deeper than 0.45 M (M = standing mouth height, B-2 / C-2) is refused, so
    //            the mouth always stays >= 0.55 M above the water, and wading is at walking speed (the 0.3 M rule).
    //   break  : in the water, WaterPlay runs before the people's errands (Update: right after the behaviour selection). A
    //            call to sleep beside someone, a treat, a live show or a gathering starts the exit first (turning to the
    //            asker) and is taken up by its own branch once the dog has landed (B-1 "受理は上陸後"; Codex 2026-09-27 D-高:
    //            a preempted play could leave the dog stuck at the band with no exit watch). A throw is held by the Fetch
    //            (KD_DogFetchController.HillsideWater) and started after landing if it is at most 10 s old. A leash / toy
    //            request is refused and asks the dog to come out. The first asker is kept until the exit starts.
    //   strand : a dog that is wet with no play (Owner change C-11, a dropped play) starts the exit at once (6b: the nearest
    //            exit from the spot's static data, which every client holds).
    //   recover: exit not done in 10 s or stuck (< 0.1 m per 1 s for 2 s, C-9) -> 2nd: another exit -> 3rd: teleport to
    //            that exit's return point (FlagDiscontinuity), which does not need a NavMesh path from inside the river (C-6).
    // IMP-DOG-020 step 4 = design stage 1 (DES-DOG-004 B-2, decision 2026-09-27): swimming breeds (waterSwimAllowed: Pome,
    // Corgi, Dachs; Shiba on trial) float. No swim clip: the Owner writes the root at water + c - Ms (c 0.04 m, Ms = mouth
    // height with the walk clip, Build: = M for now); the +-1 cm / 0.8 Hz bob goes on the dog's visual child on every client
    // (waterBob, Course/76 wires it only where no clip animates that transform; else no bob). Walk plays for as long as
    // waterPose is 2, on every client, whatever the speed (ApplyMoveAnim), and a swimming dog has no sniffing pauses.
    // Tuning values live in KH_WaterSpot (single source). Switching: into swimming as soon as a step 0.3 m
    // ahead is >= 0.75 M deep (look-ahead, immediate); back to wading after < 0.6 M for 0.5 s (hysteresis), easing the root
    // back to the ground at 1.2 m/s. waterPose 2 carries it to everyone (no new synced variable); others see the Owner's
    // height through VRCObjectSync and pick Walk from the observed speed. Swim speed 0.8 x sqrt(M / 0.556) m/s (trial).
    // Sink guard: a walking dog never takes a step where mouth - water < 0.03 m. Large breeds stay at the 0.45 M wading limit.
    // Look: splash / ripple are local on every client from the synced pose (no events); no water SE exists yet (null = silent).
    public partial class KD_DogWanderController
    {
        public KominkaHillside.KH_RiverData riverData;
        public KominkaHillside.KH_WaterSpot waterSpot;
        public float waterMouthHeight = 0.3f;   // standing mouth height M (Frisbee facts), written by Course/76
        public bool waterSwimAllowed;           // step 4: breed table (Course/76)
        public float waterSwimMouth = 0.3f;     // Ms: mouth height with the walk clip (Course/76: = M x KH_WaterSpot.swimMouthScale)
        public Transform waterBob;              // visual child for the swim bob (Course/76); null = no bob
        public ParticleSystem waterSplash;      // shared by the six dogs: moved to the feet, then Emit
        public ParticleSystem waterRipple;
        public AudioSource waterAudio;          // optional; null = silent
        public AudioClip waterStepClip;
        private bool _navStep;              // true only while LeashGo / GatherStep call StepToward to a NavMesh corner
        const float RiverProbe = 0.1f;      // look this far ahead even when the step itself is tiny

        const int BehaviorWater = 12;
        const int WaterOff = 0, WaterApproach = 1, WaterWade = 2, WaterExit = 3, WaterShake = 4;
        const float WaterWet = 0.02f, WaterFirstDelay = 0.2f;
        const float WaterRollInterval = 45f, WaterRollChance = 0.3f, WaterStayMin = 20f, WaterStayMax = 40f;
        const float WaterApproachTimeout = 20f, WaterExitTimeout = 10f, WaterShakeSeconds = 1.2f, WaterLookSeconds = 1f;
        const float WaterStuckStep = 0.1f, WaterFxRange = 20f, WaterSplashInterval = 0.25f;
        const float SwimEaseRate = 1.2f, SinkGuard = 0.03f, SwimLookAhead = 0.3f;
        private bool _waterSwimming, _swimYValid, _waterWasOwner, _bobBaseValid, _swimSettled;
        private Vector3 _bobBase;
        private float _nextSinkLog;
        private float _swimShallowSince = -1f, _waterSwimY;
        [HideInInspector] public int waterState;   // read by the harness; Owner local, never synced
        private float _waterUntil, _waterHoldUntil, _waterStageAt, _waterProgressAt, _waterFirstDue, _waterNextRoll;
        private float _waterAskAt = -1000f, _waterLookUntil, _nextSplashAt, _nextRippleAt, _nextWaterSoundAt;
        private Vector3 _waterTarget, _waterProgressPos;
        private int _waterExit, _waterRecover, _waterStuck, _waterSeenFrame, _waterAskPlayer = -1, _waterLookPlayer = -1;
        private bool _waterFirstUsed, _waterWasLeashed, _waterHasTarget, _waterFirstCellDone, _waterFirstLeg;

        bool _BlockedByRiver(Vector3 from, Vector3 next)
        {
            if (riverData == null || _navStep) return false;
            // The water-play spot: a dog in WaterPlay may move inside it, but not deeper than its stage-0 limit.
            if (waterState != WaterOff && waterSpot != null && waterSpot._Contains(next)
                && waterSpot._Bed(next) > KominkaHillside.KH_WaterSpot.NoBed + 1f)
            {
                float d = waterSpot._Depth(next), dFrom = waterSpot._Depth(from);
                if (_waterSwimming || d <= dFrom) return false;
                // Walking: never where the mouth would come within 0.03 m of the water (sink guard), and a non-swimmer never
                // past its wading limit. A swimmer switches to swimming before this matters (WaterSwimLookAhead).
                if (waterSwimMouth - d < SinkGuard)   // Ms: the mouth with the walk clip (head drop included)
                {
                    if (Time.time >= _nextSinkLog) { _nextSinkLog = Time.time + 2f; Debug.Log("[KD_DogWanderController] water step refused dog=" + dogIndex + " reason=sink-guard depth=" + d.ToString("F3")); }
                    return true;
                }
                return !waterSwimAllowed && d > WaterDepthLimit();
            }
            // Outside the spot, or a spot cell without riverbed data (Codex 2026-09-27 D-中): the plain band rule, so a step
            // deeper into the band stops and the exit watch / recovery takes over.
            return riverData._BlocksStep(from, next, KominkaHillside.KH_RiverData.BandMargin);
        }

        float WaterDepthLimit() { return waterSpot.wadeLimitRatio * waterMouthHeight; }

        /// <summary>Afloat (read by KD_DogPack so its push-apart keeps the height instead of snapping the dog to the riverbed:
        /// W1-Dachs run 20260927-071307 was pushed under by a second dog in the water).</summary>
        // The Owner's own state, not the synced pose: waterPose follows one frame later, and in that frame the push-apart snapped
        // a just-afloat dog to the riverbed (W1-Dachs run 20260927-072549, a 0.376 m step). Also true while easing back down.
        public bool _WaterFloating() { return _waterSwimming || _swimYValid; }

        bool WaterWetAt(Vector3 p)
        {
            if (!waterSpot._Contains(p)) return false;
            if (waterSpot._Bed(p) <= KominkaHillside.KH_WaterSpot.NoBed + 1f)
                return riverData != null && riverData._InBand(p, KominkaHillside.KH_RiverData.BandMargin);   // unknown = assume wet
            return waterSpot._Depth(p) > WaterWet;
        }

        /// <summary>From KD_DogFetchController (leash or toy asked while wading). Dog Owner only.</summary>
        public void _WaterExitAsked(int playerId)
        {
            // First asker wins while the request is still unhandled (Codex 2026-09-27 U-中).
            if (Time.time - _waterAskAt < 2f) return;
            _waterAskPlayer = playerId; _waterAskAt = Time.time;
        }

        /// <summary>Someone's errand that outranks idle play: the asker's playerId, -1 for an errand without one person
        /// (live show, gathering), -2 for none. The errands' own branches decide acceptance once the dog is on land.</summary>
        int WaterRequester()
        {
            if (sleepBoard != null && dogIndex >= 0) { int sleeper = sleepBoard.LiveSleeperOf(dogIndex); if (sleeper >= 0) return sleeper; }
            if (treatBox != null && dogIndex >= 0) { KD_TreatSlot slot = treatBox.LiveSlotOf(dogIndex); if (slot != null) return slot.GiverId(); }
            if (liveSystem != null && liveSystem.IsPlaying) return -1;
            if (activities != null && activities.Mode == 1) return -1;
            return -2;
        }

        // Called once at the top of Update on every client (before the early returns).
        void WaterTick()
        {
            if (waterSpot == null || fetchController == null) return;
            WaterFx();
            WaterBobTick();
            if (!_dogOwner) { waterState = WaterOff; _waterWasOwner = false; return; }   // the local play belongs to the Owner (C-11)
            if (!_waterWasOwner)
            {
                // A new Owner inherits the synced pose and the synced height, so a swimming dog does not sink (Codex D-高).
                _waterWasOwner = true;
                if (fetchController.WaterPose == 2) { _waterSwimming = true; _waterSwimY = dogRoot.position.y; _swimYValid = true; _swimShallowSince = -1f; }
            }
            if (waterState != WaterOff && _waterSeenFrame < Time.frameCount - 1) WaterEnd("preempted");
            bool leashed = hillsideLeashes != null && hillsideLeashes._IsLeashed(dogIndex);
            if (_waterWasLeashed && !leashed && !_waterFirstUsed && waterSpot._NearEntry(dogRoot.position))
                _waterFirstDue = Time.time + WaterFirstDelay;
            _waterWasLeashed = leashed;
            Vector3 p = dogRoot.position;
            bool wetNow = WaterWetAt(p);
            if (!wetNow) { _waterSwimming = false; _swimShallowSince = -1f; }
            else if (_waterSwimming)
            {
                if (waterSpot._Depth(p) < WaterSwimLeaveDepth())
                {
                    if (_swimShallowSince < 0f) _swimShallowSince = Time.time;
                    else if (Time.time - _swimShallowSince >= waterSpot.swimLeaveHold) { _waterSwimming = false; _swimShallowSince = -1f; Debug.Log("[KD_DogWanderController] water swim end dog=" + dogIndex); }
                }
                else _swimShallowSince = -1f;
            }
            fetchController._SetWaterPose(!wetNow ? 0 : _waterSwimming ? 2 : 1);
        }

        bool UpdateWaterPlay()
        {
            if (waterSpot == null || fetchController == null || !waterSpot._Ready()) return false;
            if (!_dogOwner)
            {
                // Non-Owners only pick the legs (observed speed); the pose itself comes from the synced waterPose.
                if (fetchController.WaterPose == 0) return false;
                Coast(); return true;
            }
            if (hillsideLeashes != null && hillsideLeashes._IsLeashed(dogIndex)) { if (waterState != WaterOff) WaterEnd("leashed"); return false; }
            Vector3 p = dogRoot.position;
            bool wet = fetchController.WaterPose != 0 || WaterWetAt(p);
            // Cheap exit for the usual dry dog far from the spot (the errand lookups below are cross-behaviour calls).
            if (waterState == WaterOff && !wet && _waterFirstDue <= 0f && (Time.time < _waterNextRoll || !waterSpot._NearEntry(p))) return false;
            int requester = WaterRequester();
            if (waterState == WaterOff)
            {
                if (wet) { if (requester >= 0) _WaterExitAsked(requester); WaterBeginExit(waterSpot._NearestExit(p), "stranded"); }
                else if (requester == -2 && WaterWantsToStart(p)) WaterBegin();
                else return false;
            }
            bool asked = Time.time - _waterAskAt < 2f;
            if ((waterState == WaterApproach || waterState == WaterWade) && (asked || requester != -2))
            {
                // Still dry on the way in: just drop the play and let the errand run now.
                if (waterState == WaterApproach && !wet) { WaterEnd(asked ? "asked-dry" : "errand-dry"); return false; }
                if (requester >= 0) _WaterExitAsked(requester);
                WaterBeginExit(waterState == WaterApproach ? 0 : _waterExit, asked ? "asked" : "errand");
            }
            _waterSeenFrame = Time.frameCount;

            if (waterState == WaterApproach) WaterStepApproach(p);
            else if (waterState == WaterWade) WaterStepWade(p);
            else if (waterState == WaterExit) WaterStepExit(p);
            else if (waterState == WaterShake)
            {
                Coast();
                if (Time.time >= _waterHoldUntil) WaterEnd("landed");
            }
            WaterApplySwimHeight();
            return true;
        }

        /// <summary>Start floating at 0.75 M, or earlier when walking there would bring the walk-clip mouth within wadeClear of
        /// the water (Pome: Ms 0.131 - 0.04 = 0.091 m = 0.58 M). Leave at 0.6 M, never above 0.8 x the entry depth (hysteresis).</summary>
        float WaterSwimEnterDepth() { return Mathf.Min(waterSpot.swimEnterRatio * waterMouthHeight, waterSwimMouth - waterSpot.wadeClear); }
        float WaterSwimLeaveDepth() { return Mathf.Min(waterSpot.swimLeaveRatio * waterMouthHeight, 0.8f * WaterSwimEnterDepth()); }

        float WaterSwimSpeed() { return waterSpot.swimSpeedLab * Mathf.Sqrt(Mathf.Max(0.05f, waterMouthHeight) / 0.556f); }

        /// <summary>Speed for a step from p: afloat = swim speed; wet = walk (0.3 M rule); dry strip = run.</summary>
        /// <summary>Every client: the +-bob on the visual child while waterPose is 2, back to rest otherwise. Local only.</summary>
        void WaterBobTick()
        {
            if (waterBob == null) return;
            if (!_bobBaseValid) { _bobBase = waterBob.localPosition; _bobBaseValid = true; }
            float off = fetchController.WaterPose == 2 ? waterSpot.swimBob * Mathf.Sin(Time.time * waterSpot.swimBobHz * 2f * Mathf.PI) : 0f;
            waterBob.localPosition = _bobBase + Vector3.up * off;
        }

        float WaterStepSpeed(Vector3 p)
        {
            if (_waterSwimming) return WaterSwimSpeed();
            return waterSpot._Depth(p) > WaterWet ? walkSpeed : runSpeed;
        }

        /// <summary>Into swimming at once when the ground 0.3 m ahead is at least 0.75 M deep (B-2: the next step decides).</summary>
        void WaterSwimLookAhead(Vector3 p, Vector3 goal)
        {
            if (!waterSwimAllowed || _waterSwimming) return;
            Vector3 d = goal - p; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.001f) return;
            Vector3 ahead = p + d / len * Mathf.Min(len, SwimLookAhead);
            if (waterSpot._Contains(ahead) && waterSpot._Depth(ahead) >= WaterSwimEnterDepth())
            {
                _waterSwimming = true; _swimShallowSince = -1f; _swimSettled = false;
                Debug.Log("[KD_DogWanderController] water swim start dog=" + dogIndex);
            }
        }

        /// <summary>Owner, after the step: afloat, the root rides at water + c - Ms (+ bob); after swimming it eases back to the
        /// ground. StepToward / Coast snapped it to the riverbed first; this only overrides the height.</summary>
        void WaterApplySwimHeight()
        {
            Vector3 q = dogRoot.position;
            if (_waterSwimming)
            {
                float want = riverData._SurfaceY(q.z) + waterSpot.swimClear - waterSwimMouth;   // the bob is on the visual child
                if (!_swimYValid) { _waterSwimY = q.y; _swimYValid = true; _swimSettled = false; }
                // Rising only at the start; once afloat the height follows the water surface exactly every frame, so a slope
                // or a push cannot leave the mouth low for a few frames (W2-Pome run 20260927-073904).
                if (_swimSettled) _waterSwimY = want;
                else { _waterSwimY = Mathf.MoveTowards(_waterSwimY, want, SwimEaseRate * Time.deltaTime); _swimSettled = Mathf.Abs(_waterSwimY - want) < 0.002f; }
            }
            else if (_swimYValid)
            {
                float ground = SnapToGround(q).y;
                _waterSwimY = Mathf.MoveTowards(_waterSwimY, ground, SwimEaseRate * Time.deltaTime);
                if (Mathf.Abs(_waterSwimY - ground) < 0.005f) _swimYValid = false;
            }
            else return;
            q.y = _waterSwimY;
            dogRoot.position = q; // net: owner-only: UpdateWaterPlay returns before this for non-Owners
        }

        bool WaterWantsToStart(Vector3 p)
        {
            if (activities != null && activities.Mode == 1) return false;
            if (_waterFirstDue > 0f)
            {
                if (Time.time < _waterFirstDue) return false;
                _waterFirstDue = 0f; _waterFirstUsed = true;
                return waterSpot._NearEntry(p);
            }
            if (!waterSpot._NearEntry(p) || Time.time < _waterNextRoll) return false;
            _waterNextRoll = Time.time + WaterRollInterval;
            return Random.Range(0f, 1f) < WaterRollChance;
        }

        void WaterBegin()
        {
            waterState = WaterApproach; _waterExit = 0; _waterRecover = 0; _waterStageAt = Time.time; _waterHasTarget = false;
            WaterResetProgress();
            Debug.Log("[KD_DogWanderController] water begin dog=" + dogIndex);
        }

        void WaterBeginExit(int exit, string reason)
        {
            waterState = WaterExit; _waterExit = exit < 0 ? 0 : exit; _waterRecover = 0; _waterStageAt = Time.time;
            WaterResetProgress();
            if (Time.time - _waterAskAt < 2f) { _waterLookPlayer = _waterAskPlayer; _waterLookUntil = Time.time + WaterLookSeconds; }
            _waterAskAt = -1000f;
            Debug.Log("[KD_DogWanderController] water exit dog=" + dogIndex + " exit=" + _waterExit + " reason=" + reason);
        }

        void WaterEnd(string reason)
        {
            if (waterState != WaterOff) Debug.Log("[KD_DogWanderController] water end dog=" + dogIndex + " reason=" + reason);
            waterState = WaterOff; _waterHasTarget = false; _waterLookUntil = 0f;
        }

        void WaterStepApproach(Vector3 p)
        {
            Vector3 goal = waterSpot.entry;
            float dist = Horizontal(p, goal);
            if (dist < 0.5f) { waterState = WaterWade; _waterUntil = Time.time + Random.Range(WaterStayMin, WaterStayMax); _waterHasTarget = false; _waterFirstCellDone = false; _waterHoldUntil = 0f; WaterResetProgress(); return; }
            if (Time.time - _waterStageAt > WaterApproachTimeout) { WaterEnd("approach-timeout"); Coast(); return; }
            // At a run all the way (Codex 2026-09-27: B-1's 5 s runs to the first wet step, not to the start of the play).
            if (hillsideLeashes == null || !GatherStep(goal, runSpeed)) StepToward(goal, 0f, runSpeed);
        }

        void WaterStepWade(Vector3 p)
        {
            if (Time.time >= _waterUntil) { WaterBeginExit(_waterExit, "stay-over"); return; }
            if (Time.time < _waterHoldUntil) { Coast(); WaterResetProgress(); return; }
            if (!_waterHasTarget) { _waterFirstLeg = !_waterFirstCellDone; _waterTarget = WaterPickCell(!_waterFirstCellDone); _waterFirstCellDone = true; _waterHasTarget = true; WaterResetProgress(); }
            if (Horizontal(p, _waterTarget) < 0.3f || WaterStuck(p))
            {
                bool stuck = _waterStuck >= 2;
                // No pause after the first leg (the way in); the sniffing pauses belong to the wading itself.
                _waterHasTarget = false; _waterHoldUntil = (_waterFirstLeg || _waterSwimming) ? Time.time : Time.time + Random.Range(1f, 3f); _waterFirstLeg = false;
                if (stuck)
                {
                    _waterRecover++;
                    if (_waterRecover >= 3) { WaterBeginExit(waterSpot._NearestExit(p), "stuck"); return; }
                }
                Coast(); return;
            }
            // In the water never faster than walking (the 0.3 M rule); the dry strip between the entry and the band (about 2.4 m
            // at this spot) is crossed at a run, so the first wet step comes within about 5 s of the release (B-1, W0).
            WaterSwimLookAhead(p, _waterTarget);
            StepToward(_waterTarget, 0f, WaterStepSpeed(p));
        }

        void WaterStepExit(Vector3 p)
        {
            if (Time.time < _waterLookUntil)
            {
                VRCPlayerApi asker = VRCPlayerApi.GetPlayerById(_waterLookPlayer);
                if (Utilities.IsValid(asker)) { _look = true; FaceTowards(asker.GetPosition()); }   // LookAt before the legs are written
                Coast();
                WaterResetProgress(); _waterStageAt = Time.time;
                return;
            }
            Vector3 goal = waterSpot._Exit(_waterExit);
            if (Horizontal(p, goal) < 0.4f && waterSpot._Depth(p) <= WaterWet)
            { waterState = WaterShake; _waterHoldUntil = Time.time + WaterShakeSeconds; Coast(); return; }
            if (Time.time - _waterStageAt > WaterExitTimeout || WaterStuck(p))
            {
                if (_waterRecover == 0)
                {
                    _waterRecover = 1; _waterExit = waterSpot._OtherExit(p, _waterExit); _waterStageAt = Time.time; WaterResetProgress();
                    Debug.Log("[KD_DogWanderController] water recover dog=" + dogIndex + " stage=2 exit=" + _waterExit);
                }
                else
                {
                    // Stage 3: trust only the destination (checked on the NavMesh by Course/77), not a path from here.
                    Vector3 back = waterSpot._ReturnPointFor(_waterExit);
                    VRCObjectSync sync = dogRoot.GetComponent<VRCObjectSync>();
                    if (sync != null) sync.FlagDiscontinuity();
                    dogRoot.position = back; // net: owner-only: _dogOwner checked in UpdateWaterPlay
                    _waterSwimming = false; _swimYValid = false; _swimShallowSince = -1f;   // no float height on the bank (Codex D-高)
                    _speed = 0f;
                    Debug.Log("[KD_DogWanderController] water recover dog=" + dogIndex + " stage=3 teleport exit=" + _waterExit);
                    waterState = WaterShake; _waterHoldUntil = Time.time + WaterShakeSeconds;
                    ApplyMoveAnim();
                    return;
                }
            }
            WaterSwimLookAhead(p, goal);
            StepToward(goal, 0f, _waterSwimming ? WaterSwimSpeed() : walkSpeed);
        }

        // First target of a stay: the nearest wadeable cell, so the dog goes straight into the water from the entry (W0 run
        // 20260927-062317: random draws over the whole baked box often missed, fell back to the entry itself, "arrived" there at
        // once and waited 1-3 s before drawing again: wet 7.07 s). Later targets are random. Both searches run in KH_WaterSpot.
        Vector3 WaterPickCell(bool nearest)
        {
            float limit = WaterDepthLimit() * 0.9f;
            // The first leg must end clearly in the water (W0 run 20260927-063601: the nearest cell above 0.03 m had 0.04 m, the
            // dog "arrived" 0.22 m short of it on dry ground and then held 3 s). Half the dog's limit, 0.03-0.08 m (Pome 0.035).
            float firstMin = Mathf.Clamp(0.5f * limit, 0.03f, 0.08f);
            Vector3 c = nearest ? waterSpot._NearestWetCell(dogRoot.position, firstMin, limit) : waterSpot._RandomWetCell(0.03f, waterSwimAllowed ? 5f : limit);   // swimmers may aim at deep cells
            if (c.y <= KominkaHillside.KH_WaterSpot.NoBed + 1f) c = waterSpot._NearestWetCell(dogRoot.position, 0.03f, limit);
            return c.y <= KominkaHillside.KH_WaterSpot.NoBed + 1f ? waterSpot.entry : c;
        }

        void WaterResetProgress() { _waterProgressPos = dogRoot.position; _waterProgressAt = Time.time + 1f; _waterStuck = 0; }

        bool WaterStuck(Vector3 p)
        {
            if (Time.time < _waterProgressAt) return _waterStuck >= 2;
            if (Horizontal(p, _waterProgressPos) < WaterStuckStep) _waterStuck++; else _waterStuck = 0;
            _waterProgressPos = p; _waterProgressAt = Time.time + 1f;
            return _waterStuck >= 2;
        }

        // Local look on every client. The shared particle systems are moved to each wet dog's feet and emitted at once
        // (world simulation, so earlier particles stay where they were). Within 20 m of the viewer only (D-4).
        void WaterFx()
        {
            if (fetchController.WaterPose == 0 || Time.time < _nextSplashAt) return;
            _nextSplashAt = Time.time + WaterSplashInterval;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            Vector3 p = dogRoot.position;
            if ((lp.GetPosition() - p).sqrMagnitude > WaterFxRange * WaterFxRange) return;
            Vector3 at = new Vector3(p.x, riverData != null ? riverData._SurfaceY(p.z) : p.y, p.z);
            bool moving = _speed > stopSpeedThreshold;
            if (moving && waterSplash != null) { waterSplash.transform.position = at; waterSplash.Emit(3); }
            if (waterRipple != null && Time.time >= _nextRippleAt)
            {
                _nextRippleAt = Time.time + (moving || fetchController.WaterPose == 2 ? 0.5f : 1.2f);
                waterRipple.transform.position = at + Vector3.up * 0.01f; waterRipple.Emit(1);
            }
            if (moving && waterAudio != null && waterStepClip != null && Time.time >= _nextWaterSoundAt)
            {
                _nextWaterSoundAt = Time.time + 0.6f;
                waterAudio.transform.position = at; waterAudio.PlayOneShot(waterStepClip, 0.5f);
            }
        }
    }
}
