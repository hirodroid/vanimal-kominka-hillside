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
    // 2026-10-11 の決定 (DES-DOG-010) で上書き: the leash no longer stops at the bank. IMP-DOG-053 (HillsideLeashRiver.cs): a
    // leashed dog whose holder takes it into the river leaves LeashGo there and moves under the river rules below (the frame
    // mark _waterRiverFrame, the Fetch's legs); LeashGo itself still never enters the band (the NavMesh is unchanged).
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
    //            (KD_DogFetchController.HillsideWater) and started after landing if it is at most 10 s old. A toy request
    //            asks the dog to come out (the first asker is kept until the exit starts). A leash is no longer refused
    //            (2026-10-11 の決定 DES-DOG-010 で上書き, IMP-DOG-053): the attach edge ends the play here (WaterLeashTick,
    //            "leashed") with the swim height kept, and the leash's river follow takes the dog (HillsideLeashRiver.cs).
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
        // BUG-DOG-012 (2026-10-12): an afloat root is never lower than the riverbed - this (WaterSwimHeight; was IMP-DOG-053's
        // LrSwimFloor on the leash's river legs only). Public: the harness and Course/132 read the same value
        public const float WaterSwimFloorDepth = 0.03f;
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
        private float _swimPitchNow;             // PCVR 2026-09-28: nose-up pitch (deg) blended in 0.3 s while afloat, on the visual only
        private Quaternion _bobBaseRot; private bool _bobBaseRotValid;
        const float SwimPitchRate = 30f;         // deg/s

        /// <summary>Every client: the nose-up pitch target for this frame (0 unless afloat). Local only.</summary>
        // IMP-DOG-034 C1-5: the caller passes this frame's fetchController.WaterPose (it read it already; same frame, same value)
        float SwimPitchTick(int pose)
        {
            float target = (fetchController != null && pose == 2 && waterSpot != null) ? waterSpot._SwimPitch(dogIndex) : 0f;
            // IMP-DOG-036 S3: one frame (MoveDt) - the water legs frames turn it too (SwimPitchLegs); outside the tick it was deltaTime already
            _swimPitchNow = Mathf.MoveTowards(_swimPitchNow, target, SwimPitchRate * MoveDt());
            return _swimPitchNow;
        }
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
            // IMP-DOG-034 W-2/W-3: "_Bed(p) > NoBed + 1 ? _Depth(p)" is one _WetDepth (same value), through the per-frame memo
            if (waterSpot != null) { float s = SpotWetDepth(p); if (s > KominkaHillside.KH_WaterSpot.NoBed + 1f) return s; }
            return riverData != null ? riverData._DepthAt(p) : 0f;
        }

        // ---- IMP-DOG-034 W-3: per-frame memo of the spot's polygon test and depth ----
        // In one wading frame the same horizontal point is asked several times: the root p0 (the pose at the top, WaterStepSpeed,
        // _BlockedByRiver's "from" twice, the swim-leave check) and the step's end, which is where the root stands for the pose
        // refresh after it (SnapToGround and the swim height change y only). KH_WaterSpot._Contains / _Bed / _Depth read p.x and
        // p.z only, and the spot's arrays and the river surface are baked (never written at runtime), so a hit on the same
        // (x, z) within the same frame returns what the call would. Two slots each, least recently used is replaced. A NaN never
        // hits (NaN != NaN). Any caller (this tick, the Fetch's water legs, KD_DogPack's push check) may use it: the key is the frame.
        // IMP-DOG-036 S3: unchanged with decisions 10 frames apart - the frame stays in the key (a hit is only ever the same frame's
        // answer, and the data is baked). A water legs frame asks only for steps WaterLegSafe cannot clear.
        private int _wcF0 = -1, _wcF1 = -1;
        private float _wcX0, _wcZ0, _wcX1, _wcZ1;
        private bool _wcV0, _wcV1, _wcNext1;
        private int _wdF0 = -1, _wdF1 = -1;
        private float _wdX0, _wdZ0, _wdX1, _wdZ1, _wdV0, _wdV1;
        private bool _wdNext1;

        /// <summary>= waterSpot._Contains(p) (the caller has checked waterSpot).</summary>
        bool SpotContains(Vector3 p)
        {
            int f = Time.frameCount; float x = p.x, z = p.z;
            if (_wcF0 == f && _wcX0 == x && _wcZ0 == z) { _wcNext1 = true; return _wcV0; }
            if (_wcF1 == f && _wcX1 == x && _wcZ1 == z) { _wcNext1 = false; return _wcV1; }
            bool v = waterSpot._Contains(p);
            if (_wcNext1) { _wcF1 = f; _wcX1 = x; _wcZ1 = z; _wcV1 = v; _wcNext1 = false; }
            else { _wcF0 = f; _wcX0 = x; _wcZ0 = z; _wcV0 = v; _wcNext1 = true; }
            return v;
        }

        /// <summary>= waterSpot._WetDepth(p): NoBed where the spot has no riverbed at p, else waterSpot._Depth(p).</summary>
        float SpotWetDepth(Vector3 p)
        {
            int f = Time.frameCount; float x = p.x, z = p.z;
            if (_wdF0 == f && _wdX0 == x && _wdZ0 == z) { _wdNext1 = true; return _wdV0; }
            if (_wdF1 == f && _wdX1 == x && _wdZ1 == z) { _wdNext1 = false; return _wdV1; }
            float v = waterSpot._WetDepth(p);
            if (_wdNext1) { _wdF1 = f; _wdX1 = x; _wdZ1 = z; _wdV1 = v; _wdNext1 = false; }
            else { _wdF0 = f; _wdX0 = x; _wdZ0 = z; _wdV0 = v; _wdNext1 = true; }
            return v;
        }

        /// <summary>= waterSpot._Depth(p) (0 where the spot has no riverbed).</summary>
        float SpotDepth(Vector3 p)
        {
            float s = SpotWetDepth(p);
            return s > KominkaHillside.KH_WaterSpot.NoBed + 1f ? s : 0f;
        }

        /// <summary>Water depth under p as the dogs use it (the spot's grid, else the river-wide one). Read by the Fetch.</summary>
        public float _WaterDepthAt(Vector3 p) { return WDepth(p); }

        /// <summary>The Fetch's wading limit for a non-swimmer (E-3: 0.85 M, KH_RiverData.fetchWadeRatio); free play keeps the spot's.</summary>
        float FetchWadeRatio() { return riverData != null ? riverData._FetchWadeRatio() : waterSpot.wadeLimitRatio; }

        bool _BlockedByRiver(Vector3 from, Vector3 next)
        {
            if (riverData == null || _navStep) return false;
            // IMP-DOG-036 S3: on a water legs frame, a step into a cell proven safe for this decision's limits is allowed without the
            // spot queries (WaterLegSafe: the same answer the checks below give); anything else asks in full, as before
            if (_waterLegTrust && WaterLegSafe(next)) return false;
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
            // IMP-DOG-034 W-2/W-3: _Contains / _Bed / _Depth through the per-frame memo; "_Bed(next) > NoBed + 1, then _Depth(next)" is
            // one _WetDepth (same value), _Depth(from) is SpotDepth (the root: usually a memo hit). All pure reads, same order of answers
            bool spotStep = (waterState != WaterOff || _waterFetchFrame == Time.frameCount) && waterSpot != null && SpotContains(next);
            float dNext = spotStep ? SpotWetDepth(next) : 0f;
            if (spotStep && dNext > KominkaHillside.KH_WaterSpot.NoBed + 1f)
            {
                float d = dNext;
                if (_waterSwimming) return false;   // (was evaluated after the pure dFrom read: same answer)
                if (d <= SpotDepth(from)) return false;
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
            if (waterSpot != null && SpotContains(from) && !SpotContains(next) && riverData._InBand(next, KominkaHillside.KH_RiverData.BandMargin))   // IMP-DOG-034 W-3
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

        /// <summary>Owner: drop the afloat height hold (the swim flag and the held height). For a dog whose root the leash or a travel
        /// ticket now owns on land: the leashed branch returns before UpdateWaterPlay, so an ease back to the ground that was not
        /// finished when the leash took over (landing / Shake) would otherwise keep _WaterFloating true (the push-apart keeps the
        /// height) and make the next water entry start from the old _waterSwimY (map-and-travel §F.4). Local only: waterPose is
        /// rewritten from the position by WaterPoseRefresh and nothing synced changes.</summary>
        void WaterClearFloat()
        {
            _waterSwimming = false; _swimYValid = false; _swimSettled = false; _swimShallowSince = -1f;
        }

        /// <summary>KD_DogPack (Owner): would a push from -> to take this dog, on its feet, where its own step is refused? Same
        /// rule as a step (_BlockedByRiver: the wading limit, the sink guard, the band outside the spot); a push toward shallower
        /// water is always allowed. Afloat dogs keep the existing handling (height kept, at most 0.1 m per frame).</summary>
        public bool _WaterPushBlocked(Vector3 from, Vector3 to)
        {
            // Afloat: the height is kept by KD_DogPack; only a push out of the spot into the band is refused (W2-Pome, see above)
            // DES-DOG-005 D-4 (M3): the disc's water states (KH_FrisbeeCatch 11-13) always run under the river rules
            bool discWater = FrisbeeWaterBusy;
            // IMP-DOG-053 #9: a leashed dog in the river (or walking out of it) is pushed under the river rules too, afloat and on its
            // feet. Two dogs of the same holder never push each other (KD_DogPack), so this is another holder's or a free dog's push
            bool leashRiver = LeashRiverRules();
            if (_WaterFloating())
            {
                // IMP-DOG-021: a river swimmer is never pushed into the band where the river-wide water is not (a bridge zone)
                if (((fetchController != null && fetchController.IsRiverWaterFetching) || discWater || leashRiver) && riverData != null
                    && riverData._InBand(to, KominkaHillside.KH_RiverData.BandMargin) && !riverData._FetchWater(to)) return true;
            }
            if (_WaterFloating())
                return waterSpot != null && riverData != null && SpotContains(from) && !SpotContains(to)
                    && riverData._InBand(to, KominkaHillside.KH_RiverData.BandMargin);
            int old = _waterFetchFrame, oldRiver = _waterRiverFrame;
            if ((fetchController != null && fetchController.IsWaterFetching) || discWater) _waterFetchFrame = Time.frameCount;   // the spot exception, as for its steps
            if ((fetchController != null && fetchController.IsRiverWaterFetching) || discWater || leashRiver) _waterRiverFrame = Time.frameCount;   // IMP-DOG-021: river rules
            bool blocked = _BlockedByRiver(from, to);
            _waterFetchFrame = old; _waterRiverFrame = oldRiver;
            return blocked;
        }

        bool WaterWetAt(Vector3 p)
        {
            if (SpotContains(p))   // IMP-DOG-034 W-3
            {
                float s = SpotWetDepth(p);   // IMP-DOG-034 W-2: _Bed then _Depth in one call (same values)
                if (s <= KominkaHillside.KH_WaterSpot.NoBed + 1f)
                    return riverData != null && riverData._InBand(p, KominkaHillside.KH_RiverData.BandMargin);   // unknown = assume wet
                return s > WaterWet;
            }
            // IMP-DOG-021: in the river outside the spot (a river fetch, or a dog left there by a give-up) from the river-wide grid
            if (riverData == null || !riverData._InBand(p, KominkaHillside.KH_RiverData.BandMargin)) return false;
            return riverData._DepthAt(p) > WaterWet;
        }

        // IMP-DOG-034 A5: WaterWetAt は焼き込みデータだけを読む純関数 (_Contains の 34 頂点走査を含む)。同じ tick で
        // **まったく同じ位置** を 2 度聞く (先頭の WaterPoseRefresh と、選び直しの UpdateWaterPlay) ので、直前の 1 件を控えて
        // 使い回す。位置は成分の完全一致でだけ使い回すので結果は同一。UpdateTick の先頭で捨てる
        private bool _wetMemoValid, _wetMemoValue;
        private Vector3 _wetMemoAt;

        bool WaterWetAtMemo(Vector3 p)
        {
            if (_wetMemoValid && p.x == _wetMemoAt.x && p.y == _wetMemoAt.y && p.z == _wetMemoAt.z) return _wetMemoValue;
            _wetMemoValue = WaterWetAt(p); _wetMemoAt = p; _wetMemoValid = true;
            return _wetMemoValue;
        }

        /// <summary>From KD_DogFetchController (a toy asked while wading; the disc's catcher). Dog Owner only. (The leash no longer
        /// asks: 2026-10-11 の決定 DES-DOG-010 で上書き, IMP-DOG-053.)</summary>
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
            // IMP-DOG-035 (2026-10-01 user decision 「川で遊んでいる時は、呼んだら来てほしい」): a call to a wash station is an errand
            // like the ones above: the play walks out (turning to the caller) and no new play starts while the dog is held
            if (washCalls != null) { KominkaHillside.KH_WashStationCall st = HeldStation(); if (st != null) { int c = st._CallerId(); return c >= 0 ? c : -1; } }
            return -2;
        }

        // Called once at the top of Update on every client (before the early returns).
        void WaterTick()
        {
            if (waterSpot == null || fetchController == null) return;
            int pose = fetchController.WaterPose;   // IMP-DOG-034 C1-5: one read for WaterFx / WaterBobTick (nothing writes it before them)
            WaterFx(pose);
            WaterBobTick(pose);
            // the local play belongs to the Owner (C-11); so does the leash's river follow (IMP-DOG-053: a new Owner rebuilds it from the
            // position and the pose)
            if (!_dogOwner) { waterState = WaterOff; _waterWasOwner = false; _lrNear = false; if (_lrState != LrOff) LeashRiverReset("owner-lost"); return; }
            bool ownerEdge = !_waterWasOwner;
            if (!_waterWasOwner)
            {
                // A new Owner inherits the synced pose and the synced height, so a swimming dog does not sink (Codex D-高).
                _waterWasOwner = true;
                if (pose == 2) { _waterSwimming = true; _waterSwimY = dogRoot.position.y; _swimYValid = true; _swimShallowSince = -1f; }
            }
            // IMP-DOG-036 S3: "the previous decision did not run the play" (was: the previous frame; the same with per-frame decisions).
            // _prevDecisionFrame is the frame of the decision before this one (Tick.cs); OnEnable moves it past a pause
            if (waterState != WaterOff && _waterSeenFrame < _prevDecisionFrame) WaterEnd("preempted");
            // The leash read moved up from WaterLeashTick (same reads, same frame) so the edge is known before the pose refresh.
            // UpdateHillsideLeash reuses it this frame (_wlFrame) instead of asking KH_LeashManager again (IMP-DOG-034 C1-1e).
            bool leashed = hillsideLeashes != null && hillsideLeashes._IsLeashed(dogIndex);
            int holder = leashed ? hillsideLeashes.walkers[dogIndex] : -1;
            _wlFrame = Time.frameCount; _wlLeashed = leashed; _wlHolder = holder;
            bool edge = leashed && (!_waterWasLeashed || holder != _waterLeashHolder);
            // IMP-DOG-034 C1-2 (W1): a leash already taken up on a dry dog with no water state refreshes the pose at most every
            // WaterDryRefresh s (phased by dogIndex) or as soon as the root has moved WaterDryMove m from the last refresh - instead of
            // every frame. WaterWetAt is a pure function of the root position (baked data), so an unmoved dog gets the same answer;
            // the bounded difference is a leashed dog stepping onto wet ground inside WaterDryMove of the last refresh: its pose 1
            // (splash) comes up to 0.25 s late. Leashed walks off the river run on the NavMesh (the riverbed is excluded), so this is
            // the bank's edge only. 2026-10-11 の決定 (DES-DOG-010) で上書き: a leashed dog may now go into the river (IMP-DOG-053), so
            // the leash's river follow (any state but Off: the way in, the water, the way out) refreshes every frame like a water
            // state; the attach edge (and a new holder) always refreshes on that frame, and anything wet / afloat / in a water state /
            // pose != 0 refreshes every frame as before.
            bool wet;
            if (leashed && !edge && !ownerEdge && _lrState == LrOff && waterState == WaterOff && !_waterSwimming && !_swimYValid
                && pose == 0 && !_waterLastWet && !WaterDryRefreshDue())
                wet = false;   // = the last refresh's answer (_waterLastWet is false)
            else
            {
                wet = WaterPoseRefresh();
                _waterDryNextAt = Time.time + (edge ? WaterDryRefresh * (dogIndex + 1) / 6f : WaterDryRefresh);   // phase the six dogs
            }
            WaterLeashTick(wet, leashed, holder);
        }

        // ---- IMP-DOG-034 C1-2 (W1) ----
        const float WaterDryRefresh = 0.25f;   // s
        const float WaterDryMove = 0.5f;       // m since the last refresh
        private float _waterDryNextAt;
        private bool _waterLastWet;            // WaterPoseRefresh's last answer (every caller)
        private Vector3 _waterLastAt;          // ... and where it was asked
        // This frame's leash read (WaterTick, Owner only): reused by UpdateHillsideLeash when _wlFrame is this frame
        private int _wlFrame = -1;
        private bool _wlLeashed;
        private int _wlHolder = -1;

        bool WaterDryRefreshDue()
        {
            if (Time.time >= _waterDryNextAt) return true;
            Vector3 moved = dogRoot.position - _waterLastAt;
            return moved.sqrMagnitude >= WaterDryMove * WaterDryMove;
        }

        /// <summary>Owner: the pose from where the dog stands now. Runs at the top of Update and again after every water move
        /// (UpdateWaterPlay, _WaterFetchStep / Hold / WalkTo, the leash's river follow). RC-14 (破壊監査 2026-09-28, FUNC-04 BUG-02):
        /// with the top-of-Update write only, the frame that stepped into the water still showed pose 0 until the next frame (then a
        /// leash request arriving in between was accepted; IMP-DOG-053 accepts it anyway, but the splash, the pose and the readers of
        /// the synced pose still want the pose of where the dog stands). Returns whether the root is wet.</summary>
        bool WaterPoseRefresh()
        {
            Vector3 p = dogRoot.position;
            bool wetNow = WaterWetAtMemo(p);   // IMP-DOG-034 A5
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
            _waterLastWet = wetNow; _waterLastAt = p;   // IMP-DOG-034 C1-2
            return wetNow;
        }

        // ---- RC-14 (破壊監査 2026-09-28, FUNC-04 BUG-02) -> IMP-DOG-053: a leash that arrives while the dog is in the water ----
        // Until 2026-10-11 a wet dog was not leashed: the arbiter refused it (KH_LeashManager._InWater) and a connection that got
        // through anyway was vetoed here by the dog Owner (_waterLeashVeto: the leash not taken up, the play walking the dog out,
        // ReqWaterRelease sent once a second). 2026-10-11 の決定 (DES-DOG-010) で上書き: the leash is accepted in the water (IMP-DOG-053),
        // so the veto, its release request and the arbiter's refusal are retired. What stays is the edge itself: on the frame a leash
        // (or a new holder) is first seen, a water play in progress ends here explicitly ("leashed"; it used to end as "preempted" one
        // decision later), and the swim height is kept (_waterSwimming / _swimYValid / _waterSwimY are not touched, and the leash
        // branch drops the float only for a dry dog that is not in its river follow: HillsideLeash.cs). The leash's river follow
        // (HillsideLeashRiver.cs) takes the dog from the next lines of this decision, from where it is: afloat stays afloat, the
        // walk out or the follow is chosen from the position and the holder. Owner-local; nothing synced.
        private int _waterLeashHolder = -1;

        // IMP-DOG-034 C1-2: leashed / holder are read by WaterTick just before (the same two reads, same frame)
        void WaterLeashTick(bool wet, bool leashed, int holder)
        {
            bool edge = leashed && (!_waterWasLeashed || holder != _waterLeashHolder);
            _waterWasLeashed = leashed; _waterLeashHolder = holder;
            if (!edge) return;
            if (waterState != WaterOff) WaterEnd("leashed");
            if (wet || _waterSwimming)
                Debug.Log("[KD_DogWanderController] water leash taken dog=" + dogIndex + " holder=" + holder + " swimming=" + _waterSwimming + " (IMP-DOG-053 river follow)");
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
                Coast(); _waterPlayedNow = true; return true;   // IMP-DOG-036 S3
            }
            // IMP-DOG-053: a leashed dog belongs to the leash (in the water too: its river follow); the play ends on the attach edge
            // (WaterLeashTick), this is the same rule for any later decision. IMP-DOG-034 W-5: this frame's leash read from WaterTick
            // when there is one (same value: nothing between writes walkers), else the call as before
            bool leashedNow = _wlFrame == Time.frameCount ? _wlLeashed : (hillsideLeashes != null && hillsideLeashes._IsLeashed(dogIndex));
            if (leashedNow) { if (waterState != WaterOff) WaterEnd("leashed"); return false; }
            Vector3 p = dogRoot.position;
            bool wet = fetchController.WaterPose != 0 || WaterWetAtMemo(p);   // IMP-DOG-034 A5: 先頭の WaterPoseRefresh と同じ位置なら再計算しない
            // Cheap exit for the usual dry dog far from the spot (the errand lookups below are cross-behaviour calls).
            if (waterState == WaterOff && !wet && (!waterSpot._NearEntry(p) || (_waterFirstUsed && Time.time < _waterNextRoll))) { _waterFirstDue = 0f; return false; }
            // 2026-10-02 (wash keep-out round 4, Course/100f C-keepout-free / -wander): a dry approach starts within entryRadius (25 m)
            // and walks toward the entry, so a dog far beyond that was moved there (a teleport / a placement), not walking in: drop the
            // play instead of walking hundreds of metres back to the river (which also outranked the wash station's walk out).
            // Twice the radius: a NavMesh detour on the way in may bend a little outside it. Wet / wading / exiting are untouched.
            if (waterState == WaterApproach && !wet && Horizontal(p, waterSpot.entry) > 2f * waterSpot.entryRadius) { WaterEnd("displaced"); return false; }
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
            _waterPlayedNow = true;   // IMP-DOG-036 S3: this decision ran the play (WaterThinRecord)
            return true;
        }

        /// <summary>Start floating at 0.75 M, or earlier when walking there would bring the walk-clip mouth within wadeClear of
        /// the water (Pome: Ms 0.131 - 0.04 = 0.091 m = 0.58 M). Leave at 0.6 M, never above 0.8 x the entry depth (hysteresis).</summary>
        float WaterSwimEnterDepth() { return Mathf.Min(waterSpot.swimEnterRatio * waterMouthHeight, waterSwimMouth - waterSpot.wadeClear); }
        float WaterSwimLeaveDepth() { return Mathf.Min(waterSpot.swimLeaveRatio * waterMouthHeight, 0.8f * WaterSwimEnterDepth()); }

        float WaterSwimSpeed() { return waterSpot.swimSpeedLab * Mathf.Sqrt(Mathf.Max(0.05f, waterMouthHeight) / 0.556f); }

        /// <summary>Speed for a step from p: afloat = swim speed; wet = walk (0.3 M rule); dry strip = run.</summary>
        /// <summary>Every client: the +-bob on the visual child while waterPose is 2, back to rest otherwise. Local only.</summary>
        void WaterBobTick(int pose)   // IMP-DOG-034 C1-5: pose = this frame's fetchController.WaterPose (WaterTick)
        {
            if (waterBob == null) return;
            if (!_bobBaseValid) { _bobBase = waterBob.localPosition; _bobBaseValid = true; }
            float off = pose == 2 ? waterSpot.swimBob * Mathf.Sin(Time.time * waterSpot.swimBobHz * 2f * Mathf.PI) : 0f;
            waterBob.localPosition = _bobBase + Vector3.up * off;
            if (!_bobBaseRotValid) { _bobBaseRot = waterBob.localRotation; _bobBaseRotValid = true; }
            float pitch = SwimPitchTick(pose);
            waterBob.localRotation = pitch != 0f ? _bobBaseRot * Quaternion.AngleAxis(-pitch, Vector3.right) : _bobBaseRot;   // nose up
        }

        /// <summary>Every client, LateUpdate (after the Animator): the same bob on the hips of a single-tier rig. The clips animate
        /// the hips, so the offset is added to what the Animator wrote this frame; when nothing overwrote our last value (a clip
        /// without a position curve there), the base is our value minus our offset, so it never accumulates. Vertical in world
        /// space (the bone's local axes are the armature's). Local only, never synced.</summary>
        void WaterHipsBobLate()
        {
            if (waterBobHips == null || waterSpot == null || fetchController == null) return;
            int pose = fetchController.WaterPose;   // IMP-DOG-034 C1-4: one read (SwimPitchTick and the offset read the same frame's value)
            // IMP-DOG-034 C1-4: on land with the pitch at rest and nothing of ours on the hips, the code below changes nothing (target 0
            // and _swimPitchNow 0 -> MoveTowards stays 0, no rotation, off 0 and !_hipsWrote -> return), so skip its reads
            if (pose != 2 && _swimPitchNow == 0f && !_hipsWrote) return;
            float hipsPitch = SwimPitchTick(pose);
            WaterHipsBobApply(pose, hipsPitch, pose == 2 ? waterSpot.swimBob : 0f, pose == 2 ? waterSpot.swimBobHz : 0f);
        }

        /// <summary>IMP-DOG-036 S3: LateUpdate of a water legs frame (every client): the same hips bob / pitch from the decision's
        /// record (pose, bob, pitch target). The Animator rewrites the hips every frame, so this cannot wait for the decision.</summary>
        void WaterHipsBobLegs()
        {
            if (waterBobHips == null) return;
            int pose = _wrecPose;
            if (pose != 2 && _swimPitchNow == 0f && !_hipsWrote) return;
            WaterHipsBobApply(pose, SwimPitchLegs(), _wrecBobAmp, _wrecBobHz);
        }

        void WaterHipsBobApply(int pose, float hipsPitch, float bobAmp, float bobHz)
        {
            if (hipsPitch != 0f) waterBobHips.rotation = Quaternion.AngleAxis(-hipsPitch, dogRoot.right) * waterBobHips.rotation;   // nose up, visual only
            Vector3 cur = waterBobHips.localPosition;
            Vector3 basePos = (_hipsWrote && cur == _hipsLast) ? cur - _hipsLastOff : cur;
            float off = pose == 2 ? bobAmp * Mathf.Sin(Time.time * bobHz * 2f * Mathf.PI) : 0f;
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
            if (!carrying && !SpotContains(p) && Horizontal(p, waterSpot.entry) >= 0.5f)
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

        // ---- DES-DOG-005 D-3 (M3): a disc catch that lands in the water (ドボン, KH_FrisbeeCatch) ----

        /// <summary>The afloat root height at p (the formula of WaterApplySwimHeight, no max with the riverbed): water + swimClear
        /// (+ carryExtra while carrying) - Ms - the dog's swimSink. Local, read by the disc planner. BUG-DOG-012: WaterSwimHeight
        /// calls this, then raises the result to _WaterSwimFloorY (the floor is not applied here: the planner's landY is unchanged;
        /// at the swim start depth no breed's float is below the floor except the Shiba by 0.007 m, from the scene's values; Course/132
        /// checks it).</summary>
        public float _WaterSwimRootY(Vector3 p, bool carrying)
        {
            if (waterSpot == null || riverData == null) return p.y;
            return riverData._SurfaceY(p.z) + waterSpot.swimClear + (carrying ? waterSpot.carryExtra : 0f) - waterSwimMouth - waterSpot._SwimSink(dogIndex);
        }

        /// <summary>Where a jump that comes down in the water at p leaves the root: afloat (a swimmer where the water is at least the
        /// swim entry depth) or on the riverbed. Local, read by the disc planner (design D-3 landY).</summary>
        public float _WaterLandY(Vector3 p, bool carrying)
        {
            if (waterSpot == null || riverData == null) return p.y;
            float depth = WDepth(p);
            if (waterSwimAllowed && depth >= WaterSwimEnterDepth()) return _WaterSwimRootY(p, carrying);
            return riverData._SurfaceY(p.z) - depth;
        }

        /// <summary>The walk-clip mouth height Ms (the sink guard of a wading dog: never where Ms - depth < 0.03 m).</summary>
        public float _WaterWalkMouth() { return waterSwimMouth; }

        /// <summary>BUG-DOG-012: the riverbed under p as the swim rules see it (the same source as WDepth: the spot's grid wherever it
        /// has a riverbed, else the river-wide grid); NoBed (-1000) where neither has one. Pure (baked data). Read by the swim floor,
        /// the harness and Course/132.</summary>
        public float _WaterBedAt(Vector3 p)
        {
            if (waterSpot != null) { float b = waterSpot._Bed(p); if (b > KominkaHillside.KH_WaterSpot.NoBed + 1f) return b; }
            return riverData != null ? riverData._BedAt(p) : KominkaHillside.KH_RiverData.NoBed;
        }

        /// <summary>BUG-DOG-012: the lowest root height an afloat dog may have at p (riverbed - WaterSwimFloorDepth); NoBed (-1000) where
        /// there is no riverbed data (no floor).</summary>
        public float _WaterSwimFloorY(Vector3 p)
        {
            float b = _WaterBedAt(p);
            return b > KominkaHillside.KH_RiverData.NoBed + 1f ? b - WaterSwimFloorDepth : KominkaHillside.KH_RiverData.NoBed;
        }

        /// <summary>The swim start / end depths of this dog (WaterSwimEnterDepth / WaterSwimLeaveDepth). Read by Course/132.</summary>
        public float _WaterSwimEnterDepth() { return waterSpot == null ? 0f : WaterSwimEnterDepth(); }
        public float _WaterSwimLeaveDepth() { return waterSpot == null ? 0f : WaterSwimLeaveDepth(); }

        /// <summary>Dog Owner, the frame a jump lands in the water at p (the root is already there): afloat from here when p is at least
        /// the swim entry depth for a swimmer (the height then eases onto the water line, as a swim start does), and the pose now.</summary>
        public void _WaterBeginAt(Vector3 p, bool carrying)
        {
            if (!_dogOwner || waterSpot == null || riverData == null) return;
            _swimShallowSince = -1f;
            if (waterSwimAllowed && WDepth(p) >= WaterSwimEnterDepth())
            {
                _waterSwimming = true; _waterSwimY = p.y; _swimYValid = true; _swimSettled = false;
                Debug.Log("[KD_DogWanderController] water swim start dog=" + dogIndex + " (landed in the water)");
            }
            if (carrying) _waterCarryFrame = Time.frameCount;
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
            bool water = SpotContains(ahead) || ((RiverFetchNow() || RiverExitNow()) && riverData != null && riverData._FetchWater(ahead));
            if (water && WDepth(ahead) >= WaterSwimEnterDepth())
            {
                _waterSwimming = true; _swimShallowSince = -1f; _swimSettled = false;
                Debug.Log("[KD_DogWanderController] water swim start dog=" + dogIndex);
            }
        }

        /// <summary>Owner, after the step: afloat, the root rides at water + c - Ms (+ bob); after swimming it eases back to the
        /// ground. StepToward / Coast snapped it to the riverbed first; this only overrides the height.</summary>
        void WaterApplySwimHeight() { WaterSwimHeight(false); }

        /// <summary>legs = IMP-DOG-036 S3 water legs frame: the water surface from the decision's record (WaterThinRecord: the
        /// surface at the decision's z and its slope along z, the per-dog offset) instead of KH_RiverData / KH_WaterSpot.</summary>
        void WaterSwimHeight(bool legs)
        {
            Vector3 q = dogRoot.position;
            if (_waterSwimming)
            {
                // the bob is on the visual child; carrying a ball (Fetch state 7/8) lifts the mouth by the ball's radius (B-1)
                // BUG-DOG-012: the formula is _WaterSwimRootY (one copy: Course/132 evaluates the same function). PCVR 2026-09-28: per-dog sink
                float want = legs ? _wrecSurfY + _wrecSurfSlope * (q.z - _wrecSurfZ) + _wrecSwimOff
                    : _WaterSwimRootY(q, _waterCarryFrame == Time.frameCount);
                if (!_swimYValid) { _waterSwimY = q.y; _swimYValid = true; _swimSettled = false; }
                // Rising only at the start; once afloat the height follows the water surface exactly every frame, so a slope
                // or a push cannot leave the mouth low for a few frames (W2-Pome run 20260927-073904).
                // IMP-DOG-036 S3: the ease is one frame (MoveDt): the water legs frames ease too (outside the tick it was deltaTime already)
                if (_swimSettled) _waterSwimY = want;
                else { _waterSwimY = Mathf.MoveTowards(_waterSwimY, want, SwimEaseRate * MoveDt()); _swimSettled = Mathf.Abs(_waterSwimY - want) < 0.002f; }
                // BUG-DOG-012 (2026-10-12; IMP-DOG-053's LeashRiverSwimFloor, now for every leg): the float hangs the feet the draft
                // (Ms + sink - swimClear: Corgi 0.175 m) below the water, and the swim ends only after 0.5 s below the leave depth or at
                // the dry edge, so a swimmer walking out reached water 0.02 m deep still afloat with its root up to the draft under the
                // riverbed (78m run 20261012-030041: -0.155 m). The root is never put lower than the riverbed - WaterSwimFloorDepth
                // (_WaterSwimFloorY: the riverbed of the swim rules, WDepth's grid). Only the height: the swim flag, the pose and
                // _swimSettled are not touched. A legs frame never needs it: WaterLegsSwimOk lets an afloat legs frame run only where
                // every cell it can touch is at least _wrecFloatLow deep (the floor cannot act there). The ease back to the ground
                // below is not floored: it starts from a floored height and only rises toward the ground (a floor there could hold
                // _swimYValid forever where the physics ground lies more than 0.03 m under the grid's riverbed)
                if (!legs) { float floorY = _WaterSwimFloorY(q); if (_waterSwimY < floorY) _waterSwimY = floorY; }
            }
            else if (_swimYValid)
            {
                float ground = SnapToGround(q).y;   // physics only (no spot query): also on a water legs frame
                _waterSwimY = Mathf.MoveTowards(_waterSwimY, ground, SwimEaseRate * MoveDt());   // IMP-DOG-036 S3: as above
                if (Mathf.Abs(_waterSwimY - ground) < 0.005f) _swimYValid = false;
            }
            else return;
            q.y = _waterSwimY;
            if (legs) _dbgWriter = DbgWSwimLegs;   // IMP-DOG-036 S3 step probe
            dogRoot.position = q; // net: owner-only: UpdateWaterPlay returns before this for non-Owners; WaterRunLegs calls it for the Owner only
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
            if (!SpotContains(p) && WaterRiverExitPlan(p, -1))
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
            // 2026-10-07 audit RC-15: 次の再入水抽選は遊びの終わりから 45 s 後 (初回の遊びも数える)。先の値 (ハーネスの +600 s 固定) は Max で残す
            if (waterState != WaterOff) _waterNextRoll = Mathf.Max(_waterNextRoll, Time.time + WaterRollInterval);
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
                    _dbgWriter = DbgWWaterRecover; dogRoot.position = back; // net: owner-only: _dogOwner checked in UpdateWaterPlay
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
        void WaterFx(int pose)   // IMP-DOG-034 C1-5: pose = this frame's fetchController.WaterPose (WaterTick)
        {
            if (pose == 0 || Time.time < _nextSplashAt) return;
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
                _nextRippleAt = Time.time + (moving || pose == 2 ? 0.5f : 1.2f);
                waterRipple.transform.position = at + Vector3.up * 0.01f; waterRipple.Emit(1);
            }
            if (moving && waterAudio != null && waterStepClip != null && Time.time >= _nextWaterSoundAt)
            {
                _nextWaterSoundAt = Time.time + 0.6f;
                waterAudio.transform.position = at; waterAudio.PlayOneShot(waterStepClip, 0.5f);
            }
        }

        // ---------------------------------------------------------------- IMP-DOG-036 S3: the water play decides every 10 frames
        // A dog whose decision ran the water play (UpdateWaterPlay returned true: Owner in a water state, or a non-Owner seeing a wet
        // pose) decides every waterDecisionEvery frames (Tick.cs). Between decisions WaterRunLegs replays the decision's mover for one
        // frame (RunLegs), then the swim height and the bob. What a per-frame decision did on every frame and the legs frames do instead:
        //   river / depth checks per step (_BlockedByRiver, spot queries): kept, with a cheap pre-test (Codex review 2026-10-02: a
        //       checked line was not enough - a 4 cm push or the bilinear bed between 15 cm samples let a step past the wading
        //       limit). WaterLegSafe answers "allowed" only where KH_WaterSpot._DepthBoundTable proved that every point of the grid
        //       cell is inside the spot polygon with a riverbed and no deeper than this decision's limit; anywhere else (near the
        //       polygon's edge, the bank, a cell that may reach the limit, the river rules) the full per-step check runs as before.
        //       So a legs frame's step is allowed or refused exactly as a per-frame decision's step was, wherever the dog was pushed.
        //   swim height: the decision records the water surface at its z and the slope along z (KH_RiverData._SurfaceY is linear in z
        //       between slices) and the per-dog offset; the legs follow it every frame (WaterSwimHeight(true)), the ease is MoveDt.
        //   bob / nose-up pitch (visual child in Update, hips in LateUpdate): from the decision's pose / bob / pitch target.
        //   pose (_SetWaterPose), WaterFx, the swim start / end, the state machine, the errands, the leash's attach edge: decision only.
        // Stays per frame (TickBusy, as before): the Fetch's and the disc's water states (driven every frame by their own Update through
        // _WaterFetchStep / _WaterRiverStep, handshakes _waterFetchFrame / _waterCarryFrame / _waterRiverFrame), the ease back to the
        // ground / the pitch back to 0 after the play ended, a leashed dog in the water (IMP-DOG-053: its river follow, LeashLegsOk;
        // the RC-14 veto that used to be listed here is retired, 2026-10-11 の決定 DES-DOG-010 で上書き).
        const float WaterLegLimitEps = 0.0001f;   // m below the real limits (the float rounding of "Ms - d < SinkGuard" / "d > limit")
        private bool _waterThin;          // the last decision ran the water play: the next frames may be water legs frames (Update)
        private bool _waterPlayedNow;     // this decision's UpdateWaterPlay returned true (lowered by Update before every decision)
        private bool _waterLegTrust;      // inside WaterRunLegs' replay only: _BlockedByRiver may take WaterLegSafe's "allowed"
        // the cell table and grid of KH_WaterSpot, the depth limit and the swim flag of the decision (Owner; null table = no pre-test)
        private float[] _wrecCells;
        private float _wrecGX0, _wrecGZ0, _wrecGStep, _wrecLimit;
        private int _wrecNx, _wrecNz;
        private bool _wrecSwim;
        private float[] _wrecLow;                    // swimming breeds: KH_WaterSpot._DepthLowTable
        private float _wrecEnter, _wrecLeave;        // WaterSwimEnterDepth / WaterSwimLeaveDepth at the decision
        private float _wrecFloatLow;                 // BUG-DOG-012: an afloat legs frame needs every touched cell at least this deep
        private float _wrecZLo, _wrecZHi;            // the surface piece of the swim-height record
        private float _wrecSurfY, _wrecSurfZ, _wrecSurfSlope, _wrecSwimOff;   // swim height record (Owner, swimming)
        private int _wrecPose;              // the visual record (every client): the pose after the decision,
        private float _wrecBobAmp, _wrecBobHz, _wrecPitch;                  // the bob and the nose-up pitch target for that pose

        /// <summary>End of every decision (Tick.cs Update, after UpdateTick): may the next frames be water legs frames, and the
        /// record they replay. Everything TickBusy would keep per frame besides the water play itself keeps the dog per frame.</summary>
        void WaterThinRecord()
        {
            _waterThin = false; _wrecCells = null;
            if (!_waterPlayedNow) return;
            int e = waterDecisionEvery <= 0 ? DefaultWaterDecisionEvery : waterDecisionEvery;
            if (e <= 1) return;
            // a leashed (IMP-DOG-053: in the water too, its river follow) / washed / candy / companion-stretch dog is TickBusy's
            if (_wasLeashed || _washActive || candyPerformanceActive || _candyPetResumePending || _writtenCallTime != 0f) return;
            if (AnimatorYielded()) return;   // Fetch / disc own the root and the Animator (their water states: every frame)
            if (_dogOwner)
            {
                // the play itself (Approach / Wade / Exit / Shake); the movers the play uses (never LeashGo)
                if (waterState == WaterOff || _legKind == LegLeash) return;
                WaterLegsRecordCells();
                if (_waterSwimming)
                {
                    WaterRecordSurface(dogRoot.position.z);
                    _wrecSwimOff = waterSpot.swimClear + (_waterCarryFrame == Time.frameCount ? waterSpot.carryExtra : 0f) - waterSwimMouth - waterSpot._SwimSink(dogIndex);
                    // BUG-DOG-012: afloat legs frames only where the swim floor cannot act (depth >= -offset - floor) nor the swim end
                    // timer start (>= the leave depth): WaterLegsSwimOk. _wrecLeave was set by WaterLegsRecordCells just above
                    _wrecFloatLow = Mathf.Max(_wrecLeave, -_wrecSwimOff - WaterSwimFloorDepth);
                }
            }
            _wrecPose = fetchController.WaterPose;   // after this decision's WaterPoseRefresh (non-Owner: the synced pose)
            if (waterBob != null || waterBobHips != null)
            {
                _wrecBobAmp = _wrecPose == 2 ? waterSpot.swimBob : 0f;
                _wrecBobHz = _wrecPose == 2 ? waterSpot.swimBobHz : 0f;
                _wrecPitch = _wrecPose == 2 ? waterSpot._SwimPitch(dogIndex) : 0f;   // = SwimPitchTick's target
            }
            _waterThin = true;
        }

        /// <summary>Owner, end of a water-play decision: what WaterLegSafe needs (one cross-behaviour call for the table, the grid
        /// reads, the limit). The bed branch of _BlockedByRiver refuses a step only when the step goes deeper AND (Ms - d < SinkGuard
        /// OR, for a non-swimmer, d > WaterDepthLimit()); afloat it never refuses. So d <= min(Ms - SinkGuard, limit) - eps at next
        /// means "allowed" whatever the depth at from. No table (spot / river not ready) = no pre-test: every step asks in full.</summary>
        void WaterLegsRecordCells()
        {
            _wrecLow = null;
            float[] t = waterSpot._DepthBoundTable();
            if (t == null) return;
            _wrecGX0 = waterSpot.gridX0; _wrecGZ0 = waterSpot.gridZ0; _wrecGStep = waterSpot.gridStep;
            _wrecNx = waterSpot.gridNx; _wrecNz = waterSpot.gridNz;
            if (_wrecGStep <= 0f || _wrecNx < 2 || _wrecNz < 2 || t.Length != (_wrecNx - 1) * (_wrecNz - 1)) return;
            float lim = waterSwimMouth - SinkGuard;
            if (!waterSwimAllowed) { float w = WaterDepthLimit(); if (w < lim) lim = w; }
            lim -= WaterLegLimitEps;
            _wrecSwim = _waterSwimming;
            // afloat: any proven cell (an unproven one holds float.MaxValue, above 1e30); wading: the limit, never <= 0 (the bound
            // may be negative on a dry cell where _WetDepth clamps to 0); no positive limit: no step passes the pre-test
            if (_waterSwimming) _wrecLimit = 1e30f;
            else _wrecLimit = lim > 0f ? lim : -1e30f;
            // a swimming breed: the swim start / end thresholds and the lower bounds for WaterLegsSwimOk
            if (waterSwimAllowed) { _wrecLow = waterSpot._DepthLowTable(); _wrecEnter = WaterSwimEnterDepth(); _wrecLeave = WaterSwimLeaveDepth(); }
            _wrecCells = t;
        }

        /// <summary>IMP-DOG-036 S3 (2nd review, optional fix): the surface record for the legs' swim height is the exact linear piece
        /// of KH_RiverData._SurfaceY that contains z (its knots zLo / zHi; constant outside the table). WaterLegsSwimOk decides
        /// before the dog's z can leave [_wrecZLo, _wrecZHi], so the replayed height equals _SurfaceY up to float rounding.</summary>
        void WaterRecordSurface(float z)
        {
            float zs = riverData.zStart, zk = riverData.zStep;
            float[] a = riverData.surfaceY;
            int n = a != null ? a.Length : 0;
            if (n >= 2 && zk > 0.0001f)
            {
                float f = (z - zs) / zk;
                if (f <= 0f) { _wrecZLo = -1e30f; _wrecZHi = zs; _wrecSurfZ = zs; _wrecSurfY = riverData._SurfaceY(zs); _wrecSurfSlope = 0f; return; }
                float zEnd = zs + (n - 1) * zk;
                if (f >= n - 1) { _wrecZLo = zEnd; _wrecZHi = 1e30f; _wrecSurfZ = zEnd; _wrecSurfY = riverData._SurfaceY(zEnd); _wrecSurfSlope = 0f; return; }
                int i = (int)f;
                if (i > n - 2) i = n - 2;
                float lo = zs + i * zk;
                _wrecZLo = lo; _wrecZHi = lo + zk; _wrecSurfZ = lo;
                _wrecSurfY = riverData._SurfaceY(lo);
                _wrecSurfSlope = (riverData._SurfaceY(lo + zk) - _wrecSurfY) / zk;
                return;
            }
            // no table (not ready): the old secant, no interval limit
            _wrecZLo = -1e30f; _wrecZHi = 1e30f; _wrecSurfZ = z; _wrecSurfY = riverData._SurfaceY(z);
            _wrecSurfSlope = riverData._SurfaceY(z + 1f) - _wrecSurfY;
        }

        /// <summary>Water legs frame (_waterLegTrust): is a step to next certainly allowed by the full check? True only when the
        /// whole of _BlockedByRiver would return false for any from: the spot rules apply (a water state, not under the river rules),
        /// next lies in a cell KH_WaterSpot proved to be inside the polygon with a riverbed everywhere (so SpotContains(next) and
        /// the spot's bed branch are taken), and that cell's depth bound is within this decision's limit (_wrecLimit: the sink
        /// guard and, for a non-swimmer, the wading limit; afloat, the bed branch never refuses). False = ask the full check
        /// (the per-step rule, spot queries included) exactly as a per-frame decision did. Same answer either way, never stricter.</summary>
        bool WaterLegSafe(Vector3 next)
        {
            float[] t = _wrecCells;
            if (t == null || waterState == WaterOff || RiverFetchNow() || RiverExitNow() || _waterSwimming != _wrecSwim) return false;
            // the same index arithmetic as KH_WaterSpot._Bed (the cell whose corners it interpolates)
            float fx = (next.x - _wrecGX0) / _wrecGStep, fz = (next.z - _wrecGZ0) / _wrecGStep;
            if (!(fx >= 0f && fz >= 0f && fx <= _wrecNx - 1 && fz <= _wrecNz - 1)) return false;   // NaN fails too
            int ix = (int)fx, iz = (int)fz;
            if (ix > _wrecNx - 2) ix = _wrecNx - 2;
            if (iz > _wrecNz - 2) iz = _wrecNz - 2;
            return t[iz * (_wrecNx - 1) + ix] <= _wrecLimit;
        }

        /// <summary>May this frame be a water legs frame (read only while _waterThin, instead of TickBusy)? False = decide now: the
        /// events the last decision could not have seen and that must not wait.</summary>
        bool WaterLegsOk()
        {
            // ownership change: a legs frame must never move a root this client no longer owns (and the new Owner's WaterTick edge)
            if (_dogOwner != IsDogOwner()) return false;
            // Fetch / the disc took the root and the Animator
            if (AnimatorYielded()) return false;
            // a leash that appeared since the decision is taken up on this frame (the play ends, the leash's river follow starts:
            // IMP-DOG-053; RC-14's veto retired). KH_LeashManager._IsLeashed
            // reads walkers[dog] >= 0; read the synced array directly (one cross-behaviour read instead of a call)
            if (hillsideLeashes != null)
            {
                int[] w = hillsideLeashes.walkers;
                if (w != null && dogIndex >= 0 && dogIndex < w.Length && w[dogIndex] >= 0) return false;
            }
            if (!_dogOwner) return true;
            // a moving dog whose decision recorded no mover (a state switch returning early) decides again, as in TickBusy
            if (_legKind == LegNone && _speed > 0f) return false;
            // (a push by KD_DogPack between our frames needs nothing here: WaterLegSafe judges each step's own end point)
            // a swimming breed near a swim start / end decides on this frame (WaterLegsSwimOk)
            if (waterSwimAllowed && !WaterLegsSwimOk()) return false;
            return true;
        }

        /// <summary>IMP-DOG-036 S3 (2nd review 2026-10-02): may a swimming breed take this legs frame without changing when it starts
        /// or stops swimming? Per frame, the decision did two things a legs frame does not:
        ///   wading (not afloat), before a StepToward (WaterStepWade / WaterStepExit): WaterSwimLookAhead starts the swim when the
        ///     point min(len, SwimLookAhead) toward the goal has WDepth >= WaterSwimEnterDepth().
        ///   afloat, before and after the step (WaterPoseRefresh): WDepth(root) < WaterSwimLeaveDepth() starts / continues the
        ///     swimLeaveHold timer, anything deeper resets it (_swimShallowSince = -1).
        /// True only when every grid cell the frame can touch - the root, the step's end (toward the target, and a coast along
        /// _moveDir) and, wading, the look-ahead point - is proven by the KH_WaterSpot tables to be shallower than the enter depth
        /// (wading) / at least the leave depth and deep enough for the swim floor not to act (afloat: _wrecFloatLow, BUG-DOG-012).
        /// Then the per-frame decision would not have started the swim and would have
        /// reset the timer (done here). Anything else decides on this frame, so the look-ahead and the pose refresh run before the
        /// step exactly as they did. Afloat, it also decides before z leaves the recorded surface piece (WaterRecordSurface).</summary>
        bool WaterLegsSwimOk()
        {
            float[] up = _wrecCells, low = _wrecLow;
            if (up == null || low == null || waterState == WaterOff || RiverFetchNow() || RiverExitNow() || _waterSwimming != _wrecSwim) return false;
            bool afloat = _waterSwimming;
            int kind = _legKind;
            // wading: only a StepToward can be preceded by the look-ahead (the approach's GatherStep, the holds' and the look's
            // Coast never were); afloat: every mover (the timer reads the root whatever moved it)
            if (!afloat && kind != LegStep) return true;
            if (afloat && kind == LegGather) return false;
            Vector3 p = dogRoot.position;
            float x0 = p.x, x1 = p.x, z0 = p.z, z1 = p.z;
            float dt = Time.deltaTime;
            if (kind == LegStep)
            {
                // StepToward: never faster than max(_speed, desired), never past the target
                float reach = (_speed > _legSpeed ? _speed : _legSpeed) * dt;
                float a = afloat || reach > SwimLookAhead ? reach : SwimLookAhead;
                float tx = _legTarget.x - p.x, tz = _legTarget.z - p.z;
                float len = Mathf.Sqrt(tx * tx + tz * tz);
                if (len > 0.0001f)
                {
                    if (a > len) a = len;
                    float qx = p.x + tx / len * a, qz = p.z + tz / len * a;
                    if (qx < x0) x0 = qx; if (qx > x1) x1 = qx; if (qz < z0) z0 = qz; if (qz > z1) z1 = qz;
                }
            }
            if (_speed > 0f)
            {
                // a coast (Coast itself, or StepToward inside its stop distance) runs along _moveDir
                float c = _speed * dt;
                float qx = p.x + _moveDir.x * c, qz = p.z + _moveDir.z * c;
                if (qx < x0) x0 = qx; if (qx > x1) x1 = qx; if (qz < z0) z0 = qz; if (qz > z1) z1 = qz;
            }
            if (afloat && (z0 < _wrecZLo || z1 > _wrecZHi)) return false;   // the swim height's surface piece (WaterRecordSurface)
            if (afloat && !(_wrecLeave > WaterWet)) return false;            // proven >= leave must also mean wet (WaterPoseRefresh)
            // the cells _Bed would use for any point of the box (the index arithmetic of WaterLegSafe, monotonic in x and in z)
            int ix0 = WaterCellIndex(x0, _wrecGX0, _wrecNx), ix1 = WaterCellIndex(x1, _wrecGX0, _wrecNx);
            int iz0 = WaterCellIndex(z0, _wrecGZ0, _wrecNz), iz1 = WaterCellIndex(z1, _wrecGZ0, _wrecNz);
            if (ix0 < 0 || ix1 < 0 || iz0 < 0 || iz1 < 0 || ix1 - ix0 > 2 || iz1 - iz0 > 2) return false;
            int cx = _wrecNx - 1;
            for (int iz = iz0; iz <= iz1; iz++)
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    int ci = iz * cx + ix;
                    // BUG-DOG-012: afloat, also deep enough that the swim floor cannot act (_wrecFloatLow >= _wrecLeave): the legs
                    // frame's height (no riverbed query) is then what the decision's floored height would be
                    if (afloat) { if (!(low[ci] >= _wrecFloatLow)) return false; }
                    else if (!(up[ci] < _wrecEnter)) return false;
                }
            if (afloat) _swimShallowSince = -1f;   // what the per-frame WaterPoseRefresh did at this depth (before and after the step)
            return true;
        }

        /// <summary>The grid cell index along one axis exactly as KH_WaterSpot._Bed computes it; -1 outside the grid (NaN too).</summary>
        int WaterCellIndex(float v, float origin, int count)
        {
            float f = (v - origin) / _wrecGStep;
            if (!(f >= 0f && f <= count - 1)) return -1;
            int i = (int)f;
            if (i > count - 2) i = count - 2;
            return i;
        }

        /// <summary>One water legs frame: the decision's mover (RunLegs), its river checks pre-tested by WaterLegSafe, then the
        /// swim height (Owner) and the bob / pitch on the visual child (every client; the hips follow in LateUpdate).</summary>
        void WaterRunLegs()
        {
            _waterLegTrust = _dogOwner;
            RunLegs();
            _waterLegTrust = false;
            if (_dogOwner) WaterSwimHeight(true);
            _dbgWaterLegs++;   // IMP-DOG-036 S3 harness read-out (Course/78 W0, Course/79 W1-*)
            WaterBobLegs();
        }

        /// <summary>WaterBobTick from the decision's record. At rest (not afloat, pitch 0) the decision already wrote the rest pose.</summary>
        void WaterBobLegs()
        {
            if (waterBob == null || !_bobBaseValid || !_bobBaseRotValid) return;
            if (_wrecPose != 2 && _swimPitchNow == 0f) return;
            float off = _wrecPose == 2 ? _wrecBobAmp * Mathf.Sin(Time.time * _wrecBobHz * 2f * Mathf.PI) : 0f;
            waterBob.localPosition = _bobBase + Vector3.up * off;
            float pitch = SwimPitchLegs();
            waterBob.localRotation = pitch != 0f ? _bobBaseRot * Quaternion.AngleAxis(-pitch, Vector3.right) : _bobBaseRot;   // nose up
        }

        /// <summary>SwimPitchTick from the decision's target (one frame).</summary>
        float SwimPitchLegs()
        {
            _swimPitchNow = Mathf.MoveTowards(_swimPitchNow, _wrecPitch, SwimPitchRate * MoveDt());
            return _swimPitchNow;
        }
    }
}
