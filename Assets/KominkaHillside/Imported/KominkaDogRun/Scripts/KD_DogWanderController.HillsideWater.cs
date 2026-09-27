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
    // Look: splash / ripple are local on every client from the synced pose (no events); no water SE exists yet (null = silent).
    public partial class KD_DogWanderController
    {
        public KominkaHillside.KH_RiverData riverData;
        public KominkaHillside.KH_WaterSpot waterSpot;
        public float waterMouthHeight = 0.3f;   // standing mouth height M (Frisbee facts), written by Course/76
        public ParticleSystem waterSplash;      // shared by the six dogs: moved to the feet, then Emit
        public ParticleSystem waterRipple;
        public AudioSource waterAudio;          // optional; null = silent
        public AudioClip waterStepClip;
        private bool _navStep;              // true only while LeashGo / GatherStep call StepToward to a NavMesh corner
        const float RiverProbe = 0.1f;      // look this far ahead even when the step itself is tiny

        const int BehaviorWater = 12;
        const int WaterOff = 0, WaterApproach = 1, WaterWade = 2, WaterExit = 3, WaterShake = 4;
        const float WaterWet = 0.02f, WaterDepthRatio = 0.45f, WaterFirstDelay = 0.5f;
        const float WaterRollInterval = 45f, WaterRollChance = 0.3f, WaterStayMin = 20f, WaterStayMax = 40f;
        const float WaterApproachTimeout = 20f, WaterExitTimeout = 10f, WaterShakeSeconds = 1.2f, WaterLookSeconds = 1f;
        const float WaterStuckStep = 0.1f, WaterFxRange = 20f, WaterSplashInterval = 0.25f;
        [HideInInspector] public int waterState;   // read by the harness; Owner local, never synced
        private float _waterUntil, _waterHoldUntil, _waterStageAt, _waterProgressAt, _waterFirstDue, _waterNextRoll;
        private float _waterAskAt = -1000f, _waterLookUntil, _nextSplashAt, _nextRippleAt, _nextWaterSoundAt;
        private Vector3 _waterTarget, _waterProgressPos;
        private int _waterExit, _waterRecover, _waterStuck, _waterSeenFrame, _waterAskPlayer = -1, _waterLookPlayer = -1;
        private bool _waterFirstUsed, _waterWasLeashed, _waterHasTarget;

        bool _BlockedByRiver(Vector3 from, Vector3 next)
        {
            if (riverData == null || _navStep) return false;
            // The water-play spot: a dog in WaterPlay may move inside it, but not deeper than its stage-0 limit.
            if (waterState != WaterOff && waterSpot != null && waterSpot._Contains(next)
                && waterSpot._Bed(next) > KominkaHillside.KH_WaterSpot.NoBed + 1f)
            {
                float d = waterSpot._Depth(next);
                return d > WaterDepthLimit() && d > waterSpot._Depth(from);
            }
            // Outside the spot, or a spot cell without riverbed data (Codex 2026-09-27 D-中): the plain band rule, so a step
            // deeper into the band stops and the exit watch / recovery takes over.
            return riverData._BlocksStep(from, next, KominkaHillside.KH_RiverData.BandMargin);
        }

        float WaterDepthLimit() { return WaterDepthRatio * waterMouthHeight; }

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
            if (!_dogOwner) { waterState = WaterOff; return; }   // the local play belongs to the Owner (C-11)
            if (waterState != WaterOff && _waterSeenFrame < Time.frameCount - 1) WaterEnd("preempted");
            bool leashed = hillsideLeashes != null && hillsideLeashes._IsLeashed(dogIndex);
            if (_waterWasLeashed && !leashed && !_waterFirstUsed && waterSpot._NearEntry(dogRoot.position))
                _waterFirstDue = Time.time + WaterFirstDelay;
            _waterWasLeashed = leashed;
            Vector3 p = dogRoot.position;
            fetchController._SetWaterPose(WaterWetAt(p) ? 1 : 0);
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
            return true;
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
            if (dist < 0.5f) { waterState = WaterWade; _waterUntil = Time.time + Random.Range(WaterStayMin, WaterStayMax); _waterHasTarget = false; WaterResetProgress(); return; }
            if (Time.time - _waterStageAt > WaterApproachTimeout) { WaterEnd("approach-timeout"); Coast(); return; }
            // At a run all the way (Codex 2026-09-27: B-1's 5 s runs to the first wet step, not to the start of the play).
            if (hillsideLeashes == null || !GatherStep(goal, runSpeed)) StepToward(goal, 0f, runSpeed);
        }

        void WaterStepWade(Vector3 p)
        {
            if (Time.time >= _waterUntil) { WaterBeginExit(_waterExit, "stay-over"); return; }
            if (Time.time < _waterHoldUntil) { Coast(); WaterResetProgress(); return; }
            if (!_waterHasTarget) { _waterTarget = WaterPickCell(); _waterHasTarget = true; WaterResetProgress(); }
            if (Horizontal(p, _waterTarget) < 0.3f || WaterStuck(p))
            {
                bool stuck = _waterStuck >= 2;
                _waterHasTarget = false; _waterHoldUntil = Time.time + Random.Range(1f, 3f);
                if (stuck)
                {
                    _waterRecover++;
                    if (_waterRecover >= 3) { WaterBeginExit(waterSpot._NearestExit(p), "stuck"); return; }
                }
                Coast(); return;
            }
            // In the water never faster than walking (the 0.3 M rule); the dry strip between the entry and the band (about 2.4 m
            // at this spot) is crossed at a run, so the first wet step comes within about 5 s of the release (B-1, W0).
            StepToward(_waterTarget, 0f, waterSpot._Depth(p) > WaterWet ? walkSpeed : runSpeed);
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
                    _speed = 0f;
                    Debug.Log("[KD_DogWanderController] water recover dog=" + dogIndex + " stage=3 teleport exit=" + _waterExit);
                    waterState = WaterShake; _waterHoldUntil = Time.time + WaterShakeSeconds;
                    ApplyMoveAnim();
                    return;
                }
            }
            StepToward(goal, 0f, walkSpeed);
        }

        Vector3 WaterPickCell()
        {
            int n = waterSpot._CellCount();
            float limit = WaterDepthLimit() * 0.9f;
            for (int t = 0; t < 16 && n > 0; t++)
            {
                Vector3 c = waterSpot._CellPoint(Random.Range(0, n));
                if (c.y <= KominkaHillside.KH_WaterSpot.NoBed + 1f || !waterSpot._Contains(c)) continue;
                float d = waterSpot._Depth(c);
                if (d < 0.03f || d > limit) continue;
                return c;
            }
            return waterSpot.entry;
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
                _nextRippleAt = Time.time + (moving ? 0.5f : 1.2f);
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
