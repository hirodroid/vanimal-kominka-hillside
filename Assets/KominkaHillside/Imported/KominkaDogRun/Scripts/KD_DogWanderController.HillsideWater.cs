using UdonSharp;
using UnityEngine;
using UnityEngine.AI;
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
    //   start  : (decision 2026-09-27, 2nd) a free dog within 25 m of the spot's entry starts its first play of the stay within
    //            5 s (100 %), whatever released it; later, near the entry, a 30 % roll every 45 s. (The first version waited
    //            for a release within 12 m.) Never while leashed / fetching / in a stronger behaviour
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
    // IMP-DOG-021 (DES-DOG-004 §E-3): the Fetch's water states also run along the river outside the spot (_WaterRiverStep /
    // _WaterRiverHold: the river-wide grid and banks of KH_RiverData, the same depth rules; a non-swimmer walks to the Fetch limit
    // 0.85 M in a Fetch frame, spot or river). A wet dog outside the spot walks out to the nearest usable river bank (strand).
    public partial class KD_DogWanderController
    {
        public KominkaHillside.KH_RiverData riverData;
        public KominkaHillside.KH_WaterSpot waterSpot;
        public float waterMouthHeight = 0.3f;   // standing mouth height M (Frisbee facts), written by Course/76
        public bool waterSwimAllowed;           // step 4: breed table (Course/76)
        public float waterSwimMouth = 0.3f;     // Ms: mouth height with the walk clip (Course/76: = M x KH_WaterSpot.swimMouthScale)
        public Transform waterBob;              // visual child for the swim bob (Course/76); null = no bob
        public Transform waterBobHips;          // single-tier rigs (Pome / Dachs / Samoyed): the hips bone, bobbed in LateUpdate (Course/76)
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
        private int _waterFetchFrame = -1, _waterCarryFrame = -1;
        // Last refusal of _BlockedByRiver (1 sink-guard / 2 wade-limit / 3 band outside the spot / 4 leaving the spot into the band /
        // 5 river rules: into the band where the river-wide water is not, IMP-DOG-021) and its frame: local, read by the
        // harness trace only (run 20260927-093227: W3 dogs stood 90 s on the bank, every step refused by the band rule).
        private int _waterBlock, _waterBlockFrame = -1;
        // The last stage-3 recovery teleport: its frame and both ends (local, not synced). The harness splits an observed step
        // at it instead of guessing from the log time (run 20260927-102045, W6-all-blocked).
        private int _waterRecoverFrame = -1;
        private Vector3 _waterRecoverFrom, _waterRecoverTo;
        private Vector3 _bobBase;
        private bool _hipsWrote;
        private Vector3 _hipsLast, _hipsLastOff;
        private float _nextSinkLog;
        private float _swimShallowSince = -1f, _waterSwimY;
        [HideInInspector] public int waterState;   // read by the harness; Owner local, never synced
        private float _waterUntil, _waterHoldUntil, _waterStageAt, _waterProgressAt, _waterFirstDue, _waterNextRoll;
        private float _waterAskAt = -1000f, _waterLookUntil, _nextSplashAt, _nextRippleAt, _nextWaterSoundAt;
        private Vector3 _waterTarget, _waterProgressPos;
        private int _waterExit, _waterRecover, _waterStuck, _waterSeenFrame, _waterAskPlayer = -1, _waterLookPlayer = -1;
        private bool _waterFirstUsed, _waterWasLeashed, _waterHasTarget, _waterFirstCellDone, _waterFirstLeg;

        // ---- IMP-DOG-021 (DES-DOG-004 §E-3): river-wide rules for the Fetch's water states outside the spot, and for the walk out of
        // the river (a stranded dog outside the spot). Owner-local frame marks; nothing synced. Depth = the spot's grid wherever it has
        // one (the old values unchanged), else the river-wide grid (KH_RiverData._BedAt); both are the saved Terrain.
        private int _waterRiverFrame = -1;
        private bool _waterExitRiver;
        private int _waterExitCode = -1;
        private Vector3 _waterExitPt, _waterReturnPt;

        bool RiverFetchNow() { return _waterRiverFrame == Time.frameCount; }
        bool RiverExitNow() { return _waterExitRiver && waterState == WaterExit; }

        float WDepth(Vector3 p)
        {
            if (waterSpot != null && waterSpot._Bed(p) > KominkaHillside.KH_WaterSpot.NoBed + 1f) return waterSpot._Depth(p);
            return riverData != null ? riverData._DepthAt(p) : 0f;
        }

        /// <summary>Water depth under p as the dogs use it (the spot's grid, else the river-wide one). Read by the Fetch.</summary>
        public float _WaterDepthAt(Vector3 p) { return WDepth(p); }

        /// <summary>The Fetch's wading limit for a non-swimmer (E-3: 0.85 M, KH_RiverData.fetchWadeRatio); free play keeps the spot's.</summary>
        float FetchWadeRatio() { return riverData != null ? riverData._FetchWadeRatio() : waterSpot.wadeLimitRatio; }

        bool _BlockedByRiver(Vector3 from, Vector3 next)
        {
            if (riverData == null || _navStep) return false;
            // IMP-DOG-021: along the river (Fetch water states in river mode, or walking out of the river). Inside the river-wide
            // water the spot's depth rules apply (sink guard; a non-swimmer no deeper than the Fetch limit); the band where that
            // water is not (a bridge zone, no riverbed data) is never entered, and a dog already there may only move outward.
            if (RiverFetchNow() || RiverExitNow())
            {
                if (riverData._FetchWater(next))
                {
                    float rd = WDepth(next), rdFrom = WDepth(from);
                    if (_waterSwimming || rd <= rdFrom) return false;
                    if (waterSwimMouth - rd < SinkGuard)
                    {
                        _waterBlock = 1; _waterBlockFrame = Time.frameCount;
                        if (Time.time >= _nextSinkLog) { _nextSinkLog = Time.time + 2f; Debug.Log("[KD_DogWanderController] water step refused dog=" + dogIndex + " reason=sink-guard depth=" + rd.ToString("F3")); }
                        return true;
                    }
                    if (!waterSwimAllowed && rd > FetchWadeRatio() * waterMouthHeight) { _waterBlock = 2; _waterBlockFrame = Time.frameCount; return true; }
                    return false;
                }
                if (!riverData._InBand(next, KominkaHillside.KH_RiverData.BandMargin)) return false;   // the bank
                bool fromZone = riverData._InBand(from, KominkaHillside.KH_RiverData.BandMargin) && !riverData._FetchWater(from);
                if (fromZone && riverData._Lateral(next, KominkaHillside.KH_RiverData.BandMargin) >= riverData._Lateral(from, KominkaHillside.KH_RiverData.BandMargin) - 0.0001f) return false;
                _waterBlock = 5; _waterBlockFrame = Time.frameCount;
                return true;
            }
            // The water-play spot: a dog in WaterPlay may move inside it, but not deeper than its stage-0 limit.
            if ((waterState != WaterOff || _waterFetchFrame == Time.frameCount) && waterSpot != null && waterSpot._Contains(next)
                && waterSpot._Bed(next) > KominkaHillside.KH_WaterSpot.NoBed + 1f)
            {
                float d = waterSpot._Depth(next), dFrom = waterSpot._Depth(from);
                if (_waterSwimming || d <= dFrom) return false;
                // Walking: never where the mouth would come within 0.03 m of the water (sink guard), and a non-swimmer never
                // past its wading limit. A swimmer switches to swimming before this matters (WaterSwimLookAhead).
                if (waterSwimMouth - d < SinkGuard)   // Ms: the mouth with the walk clip (head drop included)
                {
                    _waterBlock = 1; _waterBlockFrame = Time.frameCount;
                    if (Time.time >= _nextSinkLog) { _nextSinkLog = Time.time + 2f; Debug.Log("[KD_DogWanderController] water step refused dog=" + dogIndex + " reason=sink-guard depth=" + d.ToString("F3")); }
                    return true;
                }
                if (!waterSwimAllowed && d > WaterDepthLimit()) { _waterBlock = 2; _waterBlockFrame = Time.frameCount; return true; }
                return false;
            }
            // Leaving the spot into the band is refused (run 20260927-114759, W2-Pome: 97 frames in the band outside the spot; the
            // plain band rule below lets a body inside the band move sideways, so a swimmer at the region's river-side edge or its
            // up/downstream end could slide out of the polygon). The landward side of the region lies outside the band (exits and
            // return points are outside the NavMesh riverbed exclusion), so walking out through an exit is not affected.
            if (waterSpot != null && waterSpot._Contains(from) && !waterSpot._Contains(next) && riverData._InBand(next, KominkaHillside.KH_RiverData.BandMargin))
            {
                _waterBlock = 4; _waterBlockFrame = Time.frameCount;
                return true;
            }
            // Outside the spot, or a spot cell without riverbed data (Codex 2026-09-27 D-中): the plain band rule, so a step
            // deeper into the band stops and the exit watch / recovery takes over.
            bool band = riverData._BlocksStep(from, next, KominkaHillside.KH_RiverData.BandMargin);
            if (band) { _waterBlock = 3; _waterBlockFrame = Time.frameCount; }
            return band;
        }

        // IMP-DOG-021 (E-3): in a Fetch frame (the Fetch's water states, its push-apart) a non-swimmer may walk to the Fetch limit
        // (0.85 M: the Lab / Samoyed take a ball in the channel); free play (WaterPlay) keeps the spot's 0.45 M.
        float WaterDepthLimit() { return (_waterFetchFrame == Time.frameCount ? FetchWadeRatio() : waterSpot.wadeLimitRatio) * waterMouthHeight; }

        /// <summary>Afloat (read by KD_DogPack so its push-apart keeps the height instead of snapping the dog to the riverbed:
        /// W1-Dachs run 20260927-071307 was pushed under by a second dog in the water).</summary>
        // The Owner's own state, not the synced pose: waterPose follows one frame later, and in that frame the push-apart snapped
        // a just-afloat dog to the riverbed (W1-Dachs run 20260927-072549, a 0.376 m step). Also true while easing back down.
        public bool _WaterFloating() { return _waterSwimming || _swimYValid; }

        /// <summary>KD_DogPack (Owner): would a push from -> to take this dog, on its feet, where its own step is refused? Same
        /// rule as a step (_BlockedByRiver: the wading limit, the sink guard, the band outside the spot); a push toward shallower
        /// water is always allowed. Afloat dogs keep the existing handling (height kept, at most 0.1 m per frame).</summary>
        public bool _WaterPushBlocked(Vector3 from, Vector3 to)
        {
            // Afloat: the height is kept by KD_DogPack; only a push out of the spot into the band is refused (W2-Pome, see above)
            if (_WaterFloating())
            {
                // IMP-DOG-021: a river swimmer is never pushed into the band where the river-wide water is not (a bridge zone)
                if (fetchController != null && fetchController.IsRiverWaterFetching && riverData != null
                    && riverData._InBand(to, KominkaHillside.KH_RiverData.BandMargin) && !riverData._FetchWater(to)) return true;
            }
            if (_WaterFloating())
                return waterSpot != null && riverData != null && waterSpot._Contains(from) && !waterSpot._Contains(to)
                    && riverData._InBand(to, KominkaHillside.KH_RiverData.BandMargin);
            int old = _waterFetchFrame, oldRiver = _waterRiverFrame;
            if (fetchController != null && fetchController.IsWaterFetching) _waterFetchFrame = Time.frameCount;   // the spot exception, as for its steps
            if (fetchController != null && fetchController.IsRiverWaterFetching) _waterRiverFrame = Time.frameCount;   // IMP-DOG-021: river rules
            bool blocked = _BlockedByRiver(from, to);
            _waterFetchFrame = old; _waterRiverFrame = oldRiver;
            return blocked;
        }

        bool WaterWetAt(Vector3 p)
        {
            if (waterSpot._Contains(p))
            {
                if (waterSpot._Bed(p) <= KominkaHillside.KH_WaterSpot.NoBed + 1f)
                    return riverData != null && riverData._InBand(p, KominkaHillside.KH_RiverData.BandMargin);   // unknown = assume wet
                return waterSpot._Depth(p) > WaterWet;
            }
            // IMP-DOG-021: in the river outside the spot (a river fetch, or a dog left there by a give-up) from the river-wide grid
            if (riverData == null || !riverData._InBand(p, KominkaHillside.KH_RiverData.BandMargin)) return false;
            return riverData._DepthAt(p) > WaterWet;
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
            if (!_dogOwner) { waterState = WaterOff; _waterWasOwner = false; _waterLeashVeto = false; return; }   // the local play belongs to the Owner (C-11)
            if (!_waterWasOwner)
            {
                // A new Owner inherits the synced pose and the synced height, so a swimming dog does not sink (Codex D-高).
                _waterWasOwner = true;
                if (fetchController.WaterPose == 2) { _waterSwimming = true; _waterSwimY = dogRoot.position.y; _swimYValid = true; _swimShallowSince = -1f; }
            }
            if (waterState != WaterOff && _waterSeenFrame < Time.frameCount - 1) WaterEnd("preempted");
            bool wet = WaterPoseRefresh();
            WaterLeashTick(wet);
        }

        /// <summary>Owner: the pose from where the dog stands now. Runs at the top of Update and again after every water move
        /// (UpdateWaterPlay, _WaterFetchStep / Hold / WalkTo). RC-14 (破壊監査 2026-09-28, FUNC-04 BUG-02): with the top-of-Update
        /// write only, the frame that stepped into the water still showed pose 0 until the next frame, and a leash request
        /// arriving in between was accepted. Returns whether the root is wet.</summary>
        bool WaterPoseRefresh()
        {
            Vector3 p = dogRoot.position;
            bool wetNow = WaterWetAt(p);
            if (!wetNow) { _waterSwimming = false; _swimShallowSince = -1f; }
            else if (_waterSwimming)
            {
                if (WDepth(p) < WaterSwimLeaveDepth())
                {
                    if (_swimShallowSince < 0f) _swimShallowSince = Time.time;
                    else if (Time.time - _swimShallowSince >= waterSpot.swimLeaveHold) { _waterSwimming = false; _swimShallowSince = -1f; Debug.Log("[KD_DogWanderController] water swim end dog=" + dogIndex); }
                }
                else _swimShallowSince = -1f;
            }
            fetchController._SetWaterPose(!wetNow ? 0 : _waterSwimming ? 2 : 1);
            return wetNow;
        }

        // ---- RC-14: a leash that arrives while the dog is in the water (破壊監査 2026-09-28, FUNC-04 BUG-02) ----
        // The arbiter refuses a wet dog (synced pose + the root's depth, KH_LeashManager.Water), but its view can be older than the
        // dog: another client's pose and position arrive later, and a request can be judged in the frame before the pose update.
        // So the dog Owner checks the connection itself when it first sees it: wet (or afloat) at that moment = the leash is not
        // taken up (Update skips UpdateHillsideLeash, the water play keeps running and walks out, turning to the holder) and the
        // arbiter is asked to release it (ReqWaterRelease with the vetoed holder, once a second, at most WaterLeashTries sends).
        // Released -> back to normal; attaching again after landing is accepted as usual. The veto itself lasts until the
        // connection is gone or the dog has landed (dry, out of the play or shaking on the bank): running out of sends never
        // takes up the refused connection in the water (Codex review 2026-09-28 中). A connection first seen on land is
        // untouched (a leashed walk may still get its feet wet on the bank). Owner-local; nothing synced.
        const int WaterLeashTries = 10;
        private bool _waterLeashVeto;
        private int _waterLeashTries, _waterLeashHolder = -1;
        private float _waterLeashSentAt = -1000f;

        void WaterLeashTick(bool wet)
        {
            bool leashed = hillsideLeashes != null && hillsideLeashes._IsLeashed(dogIndex);
            int holder = leashed ? hillsideLeashes.walkers[dogIndex] : -1;
            bool edge = leashed && (!_waterWasLeashed || holder != _waterLeashHolder);
            _waterWasLeashed = leashed; _waterLeashHolder = holder;
            if (!leashed)
            {
                if (_waterLeashVeto) { _waterLeashVeto = false; Debug.Log("[KD_DogWanderController] water leash released dog=" + dogIndex); }
                return;
            }
            if (edge && (wet || _waterSwimming))
            {
                _waterLeashVeto = true; _waterLeashTries = 0;   // _waterLeashSentAt kept: once a second across vetoes
                _WaterExitAsked(holder);
                Debug.Log("[KD_DogWanderController] water leash veto dog=" + dogIndex + " holder=" + holder + " state=" + waterState);
            }
            if (!_waterLeashVeto) return;
            if (!wet && !_waterSwimming && (waterState == WaterOff || waterState == WaterShake))
            {
                _waterLeashVeto = false;   // landed: a leash on the bank is a normal one
                Debug.Log("[KD_DogWanderController] water leash veto ended dog=" + dogIndex + " (landed, leash taken up)");
                return;
            }
            if (_waterLeashTries >= WaterLeashTries || Time.time - _waterLeashSentAt < 1f) return;
            _waterLeashTries++; _waterLeashSentAt = Time.time;
            if (_waterLeashTries == WaterLeashTries) Debug.Log("[KD_DogWanderController] water leash veto last request dog=" + dogIndex + " (kept until landing)");
            hillsideLeashes._RequestWaterRelease(dogIndex, holder);
        }

        /// <summary>The leash arbiter (KH_LeashManager._InWater, via KD_DogFetchController._WetAtRoot): wet in the spot at p.</summary>
        public bool _WaterWetAt(Vector3 p) { return waterSpot != null && WaterWetAt(p); }

        bool UpdateWaterPlay()
        {
            if (waterSpot == null || fetchController == null || !waterSpot._Ready()) return false;
            if (!_dogOwner)
            {
                // Non-Owners only pick the legs (observed speed); the pose itself comes from the synced waterPose.
                if (fetchController.WaterPose == 0) return false;
                Coast(); return true;
            }
            // RC-14: a leash vetoed in the water (WaterLeashTick) is not taken up; the play keeps walking the dog out
            if (hillsideLeashes != null && hillsideLeashes._IsLeashed(dogIndex) && !_waterLeashVeto) { if (waterState != WaterOff) WaterEnd("leashed"); return false; }
            Vector3 p = dogRoot.position;
            bool wet = fetchController.WaterPose != 0 || WaterWetAt(p);
            // Cheap exit for the usual dry dog far from the spot (the errand lookups below are cross-behaviour calls).
            if (waterState == WaterOff && !wet && (!waterSpot._NearEntry(p) || (_waterFirstUsed && Time.time < _waterNextRoll))) { _waterFirstDue = 0f; return false; }
            int requester = WaterRequester();
            if (waterState == WaterOff)
            {
                if (wet) { if (requester >= 0) _WaterExitAsked(requester); WaterBeginExitFrom(p, "stranded"); }
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
            WaterPoseRefresh();   // RC-14: the pose of where the dog stands after this frame's step
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

        /// <summary>Every client, LateUpdate (after the Animator): the same bob on the hips of a single-tier rig. The clips animate
        /// the hips, so the offset is added to what the Animator wrote this frame; when nothing overwrote our last value (a clip
        /// without a position curve there), the base is our value minus our offset, so it never accumulates. Vertical in world
        /// space (the bone's local axes are the armature's). Local only, never synced.</summary>
        void WaterHipsBobLate()
        {
            if (waterBobHips == null || waterSpot == null || fetchController == null) return;
            Vector3 cur = waterBobHips.localPosition;
            Vector3 basePos = (_hipsWrote && cur == _hipsLast) ? cur - _hipsLastOff : cur;
            float off = fetchController.WaterPose == 2 ? waterSpot.swimBob * Mathf.Sin(Time.time * waterSpot.swimBobHz * 2f * Mathf.PI) : 0f;
            if (off == 0f)
            {
                if (_hipsWrote) { waterBobHips.localPosition = basePos; _hipsWrote = false; }
                return;
            }
            Transform parent = waterBobHips.parent;
            Vector3 up = parent != null ? parent.InverseTransformDirection(Vector3.up) : Vector3.up;
            _hipsLastOff = up * off;
            _hipsLast = basePos + _hipsLastOff;
            waterBobHips.localPosition = _hipsLast;
            _hipsWrote = true;
        }

        // ---- step 6: the Fetch's water states (6 WaterChase / 7 WaterReturn / 8 WaterExit) move the dog through these legs, so
        // the swim height, the look-ahead, the depth rules and the spot's band exception are the same as in WaterPlay.

        /// <summary>From KD_DogFetchController (Owner, every frame of a water state): one step toward goal.</summary>
        public void _WaterFetchStep(Vector3 goal, bool carrying)
        {
            if (!_dogOwner || waterSpot == null) { ApplyMoveAnim(); return; }
            _waterFetchFrame = Time.frameCount;
            if (carrying) _waterCarryFrame = Time.frameCount;
            Vector3 p = dogRoot.position;
            // From the bank the way in is the entry, as for WaterPlay (WaterStepApproach): the strip between the bank and the
            // spot polygon is band outside the spot, where every straight step is refused (run 20260927-093227, W3 x 6).
            if (!carrying && !waterSpot._Contains(p) && Horizontal(p, waterSpot.entry) >= 0.5f)
            {
                if (hillsideLeashes == null || !GatherStep(waterSpot.entry, runSpeed)) StepToward(waterSpot.entry, 0f, runSpeed);
                WaterPoseRefresh();   // RC-14: the pose after the step
                return;
            }
            WaterSwimLookAhead(p, goal);
            StepToward(goal, 0f, WaterStepSpeed(p));
            WaterApplySwimHeight();
            WaterPoseRefresh();
        }

        /// <summary>Stand (WaterExit's 1.2 s on the bank): legs and height only.</summary>
        public void _WaterFetchHold(bool carrying)
        {
            if (!_dogOwner || waterSpot == null) { ApplyMoveAnim(); return; }
            _waterFetchFrame = Time.frameCount;
            if (carrying) _waterCarryFrame = Time.frameCount;
            Coast();
            WaterApplySwimHeight();
            WaterPoseRefresh();
        }

        /// <summary>Fetch state 8 on a far-bank thrower: walk (NavMesh) to the entered bank's return point, carrying.</summary>
        public void _WaterFetchWalkTo(Vector3 goal)
        {
            if (!_dogOwner || waterSpot == null) { ApplyMoveAnim(); return; }
            _waterFetchFrame = Time.frameCount; _waterCarryFrame = Time.frameCount;
            if (hillsideLeashes == null || !GatherStep(goal, walkSpeed)) StepToward(goal, 0f, walkSpeed);
            WaterApplySwimHeight();
            WaterPoseRefresh();
        }

        // ---- IMP-DOG-021 (DES-DOG-004 §E-3): the same legs for a ball floating in the river outside the spot ----

        /// <summary>From KD_DogFetchController (Owner, every frame of a river water state): one step toward goal under the river
        /// rules. Before the dog has reached the planned bank point (entered = false; the Fetch latches it), the way there is the
        /// NavMesh (GatherStep), as for the spot's entry.</summary>
        public void _WaterRiverStep(Vector3 goal, bool carrying, Vector3 entry, bool entered)
        {
            if (!_dogOwner || waterSpot == null || riverData == null) { ApplyMoveAnim(); return; }
            _waterFetchFrame = Time.frameCount;
            if (carrying) _waterCarryFrame = Time.frameCount;
            Vector3 p = dogRoot.position;
            if (!carrying && !entered)
            {
                // Not under the river rules yet: a straight fallback (no complete path) stops at the band like any land move,
                // so the dog never goes into the water anywhere but through the planned bank
                if (hillsideLeashes == null || !GatherStep(entry, runSpeed)) StepToward(entry, 0f, runSpeed);
                WaterPoseRefresh();
                return;
            }
            _waterRiverFrame = Time.frameCount;
            WaterSwimLookAhead(p, goal);
            StepToward(goal, 0f, WaterStepSpeed(p));
            WaterApplySwimHeight();
            WaterPoseRefresh();
        }

        /// <summary>IMP-DOG-021 (Codex review 2026-09-28 高): Fetch state 8 after a river exit: carry the ball to goal (the thrower)
        /// over the NavMesh at the given speed (GatherStep; no complete path = the straight step, which stops at the band).</summary>
        public void _WaterFetchLandTo(Vector3 goal, float speed)
        {
            if (!_dogOwner || waterSpot == null) { ApplyMoveAnim(); return; }
            _waterFetchFrame = Time.frameCount; _waterCarryFrame = Time.frameCount;
            if (hillsideLeashes == null || !GatherStep(goal, speed)) StepToward(goal, 0f, speed);
            WaterApplySwimHeight();
            WaterPoseRefresh();
        }
        /// <summary>River counterpart of _WaterFetchHold (the claim wait in 6, the stand in 8).</summary>
        public void _WaterRiverHold(bool carrying)
        {
            if (!_dogOwner || waterSpot == null) { ApplyMoveAnim(); return; }
            _waterFetchFrame = Time.frameCount; _waterRiverFrame = Time.frameCount;
            if (carrying) _waterCarryFrame = Time.frameCount;
            Coast();
            WaterApplySwimHeight();
            WaterPoseRefresh();
        }

        /// <summary>The Fetch's wading limit (m) for this dog: the deep-ball rule of a non-swimmer (E-3).</summary>
        public float _WaterFetchWadeLimit() { return waterSpot == null ? 0f : FetchWadeRatio() * waterMouthHeight; }

        public bool _WaterCanSwim() { return waterSwimAllowed; }
        public float _WaterWadeLimit() { return waterSpot == null ? 0f : WaterDepthLimit(); }
        public float _WaterSwimSpeedNow() { return waterSpot == null ? walkSpeed : WaterSwimSpeed(); }

        float WaterStepSpeed(Vector3 p)
        {
            if (_waterSwimming) return WaterSwimSpeed();
            return WDepth(p) > WaterWet ? walkSpeed : runSpeed;
        }

        /// <summary>Into swimming at once when the ground 0.3 m ahead is at least 0.75 M deep (B-2: the next step decides).</summary>
        void WaterSwimLookAhead(Vector3 p, Vector3 goal)
        {
            if (!waterSwimAllowed || _waterSwimming) return;
            Vector3 d = goal - p; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.001f) return;
            Vector3 ahead = p + d / len * Mathf.Min(len, SwimLookAhead);
            // IMP-DOG-021: along the river too, when this frame runs under the river rules
            bool water = waterSpot._Contains(ahead) || ((RiverFetchNow() || RiverExitNow()) && riverData != null && riverData._FetchWater(ahead));
            if (water && WDepth(ahead) >= WaterSwimEnterDepth())
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
                // the bob is on the visual child; carrying a ball (Fetch state 7/8) lifts the mouth by the ball's radius (B-1)
                float want = riverData._SurfaceY(q.z) + waterSpot.swimClear + (_waterCarryFrame == Time.frameCount ? waterSpot.carryExtra : 0f) - waterSwimMouth;
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
            if (!_waterFirstUsed)
            {
                // First play of the stay: near (25 m) and free is enough; a short settle so a dog passing by does not jerk.
                if (!waterSpot._NearEntry(p)) { _waterFirstDue = 0f; return false; }
                if (_waterFirstDue <= 0f) _waterFirstDue = Time.time + WaterFirstDelayFor();
                if (Time.time < _waterFirstDue) return false;
                _waterFirstDue = 0f; _waterFirstUsed = true;
                return true;
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

        /// <summary>The exit of a wet dog with no play: from inside the spot through its exits (as before); IMP-DOG-021: from the
        /// river outside the spot through the nearest usable bank of the river-wide slices (design C-6b, the river side of it).</summary>
        void WaterBeginExitFrom(Vector3 p, string reason)
        {
            if (!waterSpot._Contains(p) && WaterRiverExitPlan(p, -1))
            {
                WaterBeginExit(0, reason);
                _waterExitRiver = true;
                Debug.Log("[KD_DogWanderController] water exit dog=" + dogIndex + " river bank=" + _waterExitPt.ToString("F2") + " code=" + _waterExitCode);
                return;
            }
            WaterBeginExit(waterSpot._NearestExit(p), reason);
        }

        /// <summary>IMP-DOG-021: the bank to walk out to from the river at p: the ok slice nearest p on its own side, else on the
        /// other side (within twice the bank reach, same bridge section); else the bank point on its side projected on the NavMesh
        /// within 4 m (C-6b). except = the code to skip (recovery stage 2). False = none found (the spot's exits are used).</summary>
        bool WaterRiverExitPlan(Vector3 p, int except)
        {
            if (riverData == null || !riverData._FetchReady()) return false;
            int side = p.x >= riverData._CenterX(p.z) ? 1 : -1;
            float reach = 2f * riverData._BankReach();
            int code = riverData._NearestSlice(p, side, reach, except);
            if (code < 0) code = riverData._NearestSlice(p, -side, reach, except);
            if (code >= 0) { _waterExitCode = code; _waterExitPt = riverData._SliceBank(code); _waterReturnPt = riverData._SliceReturn(code); return true; }
            Vector3 bp = riverData._BankPoint(p, side, KominkaHillside.KH_RiverData.BandMargin + KominkaHillside.KH_RiverData.BankExtra);
            NavMeshHit h;
            if (NavMesh.SamplePosition(bp, out h, 4f, NavMesh.AllAreas) && !riverData._InBand(h.position, KominkaHillside.KH_RiverData.BandMargin))
            { _waterExitCode = -1; _waterExitPt = h.position; _waterReturnPt = h.position; return true; }
            return false;
        }

        void WaterBeginExit(int exit, string reason)
        {
            _waterExitRiver = false;
            waterState = WaterExit; _waterExit = exit < 0 ? 0 : exit; _waterRecover = 0; _waterStageAt = Time.time;
            WaterResetProgress();
            if (Time.time - _waterAskAt < 2f) { _waterLookPlayer = _waterAskPlayer; _waterLookUntil = Time.time + WaterLookSeconds; }
            _waterAskAt = -1000f;
            Debug.Log("[KD_DogWanderController] water exit dog=" + dogIndex + " exit=" + _waterExit + " reason=" + reason);
        }

        void WaterEnd(string reason)
        {
            if (waterState != WaterOff) Debug.Log("[KD_DogWanderController] water end dog=" + dogIndex + " reason=" + reason);
            waterState = WaterOff; _waterHasTarget = false; _waterLookUntil = 0f; _waterExitRiver = false;
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
            // IMP-DOG-021: out of the river (outside the spot) the goal is the planned bank, not a spot exit
            Vector3 goal = _waterExitRiver ? _waterExitPt : waterSpot._Exit(_waterExit);
            if (Horizontal(p, goal) < 0.4f && WDepth(p) <= WaterWet)
            { waterState = WaterShake; _waterHoldUntil = Time.time + WaterShakeSeconds; Coast(); return; }
            if (Time.time - _waterStageAt > WaterExitTimeout || WaterStuck(p))
            {
                if (_waterRecover == 0)
                {
                    _waterRecover = 1; _waterStageAt = Time.time; WaterResetProgress();
                    if (_waterExitRiver) { if (_waterExitCode >= 0) WaterRiverExitPlan(p, _waterExitCode); }   // another bank (keeps the old one if none)
                    else _waterExit = waterSpot._OtherExit(p, _waterExit);
                    Debug.Log("[KD_DogWanderController] water recover dog=" + dogIndex + " stage=2 exit=" + (_waterExitRiver ? "river " + _waterExitCode : "" + _waterExit));
                }
                else
                {
                    // Stage 3: trust only the destination (checked on the NavMesh by Course/77 / Course/91), not a path from here.
                    Vector3 back = _waterExitRiver ? _waterReturnPt : waterSpot._ReturnPointFor(_waterExit);
                    VRCObjectSync sync = dogRoot.GetComponent<VRCObjectSync>();
                    if (sync != null) sync.FlagDiscontinuity();
                    _waterRecoverFrame = Time.frameCount; _waterRecoverFrom = dogRoot.position; _waterRecoverTo = back;
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
        /// <summary>The first play's wait: 0.2 s + 0.3 s x dogIndex (0.2-1.7 s, at most 2 s), so six dogs arriving together do not
        /// all head for the entry in the same frame (Codex 2026-09-27 U-中).</summary>
        float WaterFirstDelayFor() { return Mathf.Min(2f, WaterFirstDelay + 0.3f * Mathf.Max(0, dogIndex)); }

        Vector3 WaterPickCell(bool nearest)
        {
            float limit = WaterDepthLimit() * 0.9f;
            // The first leg must end clearly in the water (W0 run 20260927-063601: the nearest cell above 0.03 m had 0.04 m, the
            // dog "arrived" 0.22 m short of it on dry ground and then held 3 s). Half the dog's limit, 0.03-0.08 m (Pome 0.035).
            float firstMin = Mathf.Clamp(0.5f * limit, 0.03f, 0.08f);
            // The first leg: the cell nearest a point 1 m per dog along the bank (dogIndex - 2.5 m in z), so the dogs spread out
            Vector3 firstFrom = dogRoot.position + new Vector3(0f, 0f, (dogIndex - 2.5f) * 1f);
            Vector3 c = nearest ? waterSpot._NearestWetCell(firstFrom, firstMin, limit) : waterSpot._RandomWetCell(0.03f, waterSwimAllowed ? 5f : limit);   // swimmers may aim at deep cells
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
