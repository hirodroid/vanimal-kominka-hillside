using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    // 裏山改変 (2026-10-02 ユーザー要望、Play の後): docs/plans/2026-10-01-wash-call-and-height.md §3 / §4 / §6.
    //   A 「終わりにしたら、犬は洗い場の台の上からすぐにトコトコトコって出ていくようにしてほしい」
    //   B 「洗い場には他の犬は勝手に近寄ってこない、台の上には休憩し始めたりもしないようにしたい」
    //
    // A (leave): on the station's release edge (StationTick, every client, from the synced heldDog) the dog that stood there drops
    //   the wash hold and the satisfied sign (KH_DogWash._ReleaseHold, as the errands do), rides a raised stand down with it
    //   (UpdateLeaveLowering: the Owner keeps the root on the anchor while KH_WashStationCall eases liftRoot to 0 - no pop), then walks
    //   to a point stationLeaveMargin outside the station's keep-out (UpdateStationLeave, SleepStep = the station walk's own feet:
    //   GatherStep on a complete NavMesh path, else StepToward) and only then goes back to its usual behaviour. No sit / rest / wait.
    //   Not started when an errand (gathering / sleep call / travel ticket) or a leash took the dog, nor when another station holds
    //   it now, nor for a dog released on its way there (not inside the circle): those are only freed.
    // B (keep-out): every station's KH_WashStationCall.keepOutRadius around its anchor (washCalls = the same 4 stations).
    //   - destinations: the wander waypoint (WanderTarget) and the play's shared anchors (WanderAnchorAt / FleeHopTargetByHop /
    //     FaceStopPoint / PlayBreakOff) pass KeepOutFilter, a pure function of the static circles and the river band (the same
    //     answer on every client and for every dog: the shared-derivation rule of IMP-NET-002 holds)
    //   - rest: UpdateRest neither starts nor keeps lying inside a circle (the rest lies where the dog stands: waterPlace is null here)
    //   - a free dog found inside (pushed, placed, just unleashed, the end of a fetch, a home inside) walks out through the same
    //     leave walk, before rest / the paused stop / the post-Fetch calm / play / wander - not mid-play (a chase may cross a circle)
    //   - the welcome / drop-by never starts toward, or keeps walking to, a person inside a circle
    //   - round 2 (coordinator 2026-10-02): a dog unleashed inside a circle stands there for stationUnleashGrace (20 s; washing
    //     restarts it from the last wash frame) so the walker can pick up a tool; a stroke / a call hand it to the wash / station
    //     hold. After the grace it leaves at stationLeaveGait (the wash is over); a dog that merely drifted in walks out (walkSpeed)
    //   Exempt (they return above the leave branch in UpdateTick): a dog called to a station (its own hold), a leashed dog (whoever
    //   leads it), a dog being washed by a tool anywhere (the wash hold), Fetch / the disc, water play, the sleep call, live,
    //   gathering, a treat, the nudge (a player holds its ball). Their ends are not filtered: the keep-out walk takes over afterwards.
    //
    //   - round 3 (review 2026-10-02): a home near a station is moved out for the wander (KeepOutHome: its whole disc outside), the
    //     wander never walks a straight line through a circle (WanderPass: re-roll, rim detour, or stand the hop out), a walk out
    //     ends only once the dog is LeaveDoneClear outside every circle, failed walks (deadline / no point) give up after
    //     LeaveMaxFails for LeaveCooldown, an Owner change re-chooses the point, a switched-off station's raised dog eases itself down
    //
    // Cost (IMP-DOG-034 / 036): nothing per frame. The circle test (KeepOutAt: <= 4 squared distances) runs in the decision tick
    // at reselection (UpdateStationLeave), in UpdateRest, in the greeting and once per waypoint / shared anchor; the segment test
    // (KeepOutCrossing) once per wander hop (+ <= 3 re-rolls and a detour only when it crosses). The exit point: <= 24 NavMesh
    // samples, once when a walk starts (8 in the usual first ring). The walk itself is SleepStep = GatherStep, whose
    // GatherStepBody re-plans every 0.5 s on the Owner (2 SamplePosition + 1 CalculatePath), as the walk to the station already
    // did. The ride down decides every frame for at most liftMax / liftSpeed + 1 s (TickBusy), as the station branch on the stand
    // already did.
    // Sync: nothing new. Every client derives the edge and the circles from synced / static facts; the Owner moves the root.
    public partial class KD_DogWanderController
    {
        [Header("2026-10-02 洗い場から出る・洗い場へ近寄らない (試作値。Wash/11 が書き Wash/12 が検査する)")]
        [Tooltip("おわりの後に向かう点: 洗い場の立入り半径 (KH_WashStationCall.keepOutRadius) のさらに外へこの距離 (m)。0.3 未満は 0.3")]
        public float stationLeaveMargin = DefaultStationLeaveMargin;
        [Tooltip("おわりの後に出ていく足: 0 = 歩き (walkSpeed)、1 = 小走り (走りの脚に入る最低の速さ runAnimSpeed + 0.1。既定。「トコトコ」)、2 = 走り (runSpeed)。" +
            "円にふらっと入っただけの犬が出る時は歩き (walkSpeed)")]
        public int stationLeaveGait = DefaultStationLeaveGait;
        [Tooltip("洗い場の円の中でリードを外された犬は、この秒数だけ円から出されない (その場に立つ。伏せない)。洗っている間は最後に洗われた時から数え直す。0 = 猶予なし")]
        public float stationUnleashGrace = DefaultStationUnleashGrace;
        [Tooltip("洗い場の円 (keepOutRadius) のさらに外へこの距離までは休憩を始めない・続けない (m)。出ていった直後にすぐそばで伏せないように。0 = 円の中だけ")]
        public float stationRestMargin = DefaultStationRestMargin;
        public const float DefaultStationLeaveMargin = 1.0f;
        public const int DefaultStationLeaveGait = 1;      // round 2 (coordinator 2026-10-02): 「すぐにトコトコトコって出ていく」= brisk, not a stroll
        public const float DefaultStationUnleashGrace = 20f;
        public const float DefaultStationRestMargin = 1.5f;   // round 7: the walk out ends 0.5 m outside; a dog with run debt would lie down right there

        const int BehaviorLeave = 13;          // _selectedBehavior while walking out (Performance.cs 1-11, HillsideWater.cs BehaviorWater 12)
        const float KeepOutPad = 0.3f;         // a filtered destination lies this far outside the circle (arriveRadius 0.25 stops outside)
        const float LeaveArrive = 0.4f;        // at its point (within this) but not LeaveDoneClear outside = a failed walk (cannot happen with LeaveTargetClear)
        const float LeaveShelfClear = 0.8f;    // the straight way out keeps this far from the shelf's centre (1.2 m wide)
        const float LeavePlayerClear = 0.6f;   // Owner: and from every player (beyond the first 0.5 m: the washer stands beside the dog)
        const float LeaveLowerDone = 0.005f;   // m: the stand counts as down
        // Round 3 (review 2026-10-02)
        const float LeaveDoneClear = 0.5f;     // the walk out ends once the dog is this far outside EVERY circle (not "near its point")
        const float LeaveTargetClear = 0.9f;   // a walk-out point (after the NavMesh correction) lies this far outside every circle
        const int LeaveMaxFails = 2;           // walks out that ended inside (deadline / no point) before the keep-out rests
        const float LeaveCooldown = 5f;        // s: then B is not started again (rest inside stays refused; wander avoids the circle). Round 5: 15 -> 5 (a point is always found now; this only covers a dog physically stuck)
        const float KeepOutDetourPad = 0.8f;   // wander detour: a rim point this far outside the circle, 45 degrees around per hop

        // A / B walk out. Local on every client (derived from synced / static facts); the Owner's walk is the one that moves the root.
        private bool _leaveActive;
        private bool _leaveLowering;           // A: still riding the stand down (Owner holds the root on the anchor)
        private bool _leaveFromStation;        // read-out: A (true) or B (false)
        private int _leaveStation = -1;        // washCalls index
        private Vector3 _leaveTarget;
        private float _leaveUntil, _leaveLowerUntil;
        private int _leaveTickFrame = -1;      // the decision frame the walk last ran (a decision without it = something stronger won)
        private int _leaveCount, _keepOutExits;   // harness read-outs: walks started (A + B), B starts
        private bool _leaveBrisk;              // the walk uses stationLeaveGait (A, and B after an unleash grace); else walkSpeed
        // Round 2: unleashed inside a circle -> a grace before B applies (the walker picks up the sponge). Local on every client: the
        // unleash edge is the leash branch's own (_wasLeashed -> walkers[dog] < 0, synced), seen by every client whoever released
        private int _graceStation = -1;        // the circle the leash came off in (-1 = none)
        private float _graceUntil;             // unleash time + stationUnleashGrace
        private float _washLastAt = -1000f;    // last time a wash branch ran (WashFrame): washing keeps the grace going
        private int _graceFrames;              // harness read-out: decisions spent standing in the grace
        // Round 3
        private bool _leaveOwnerChose;         // _dogOwner when _leaveTarget was chosen (an Owner change re-chooses it as the Owner)
        private bool _leaveTargetOk;           // LeaveTarget found a checked point (false = no point: the attempt fails at once)
        private int _leaveFails;               // consecutive walks out that did not get out
        private float _keepOutRestUntil;       // B cool-down after LeaveMaxFails
        private bool _leaveOwnEase;            // riding down without the station's Update (it is disabled): the root eases itself
        private float _leaveGroundY;           // ... to this ground height
        private int _leaveTimeouts;            // harness read-out: walks out that ended inside (failed)
        // Round 5 read-outs (int / float writes only: one or two per decision, the rest at a walk's start)
        private int _dbgTickExit, _dbgWashExit;            // where UpdateTick / UpdateHillsideWash returned (KD_DogWanderController.cs / HillsideWash.cs)
        private int _dbgLtTried, _dbgLtShelf, _dbgLtNav, _dbgLtClose, _dbgLtRiver, _dbgLtPlayer, _dbgLtPick, _dbgLtRing;   // the last LeaveTarget
        private int _dbgLeaveFail;                          // last LeaveFailed reason: 1 deadline, 2 no point, 3 at its point but inside
        private int _dbgKoK = -1;                           // the circle the last walk-out branch found the dog in (-1 none)
        private float _dbgKoAt;                             // Time.time of that check
        private Vector3 _dbgHome;                           // the wander's (shifted) home at the last waypoint computation
        // The keep-out circles, by washCalls index, read once (the anchor rides the lift on y only; the stations do not move)
        private bool _koReady;
        private Vector3[] _koCenter, _koShelf;
        private float[] _koRadius;
        // Harness probe read-outs (_DebugKeepOutProbe)
        private int _dbgKoHop0, _dbgKoSamples, _dbgKoRawIn, _dbgKoFilteredIn, _dbgKoPlayIn, _dbgKoPlaySamples;
        private float _dbgKoMaxShift;

        // ================= the circles =================

        void KeepOutInit()
        {
            _koReady = true;
            int n = washCalls != null ? washCalls.Length : 0;
            _koCenter = new Vector3[n]; _koShelf = new Vector3[n]; _koRadius = new float[n];
            for (int i = 0; i < n; i++)
            {
                KominkaHillside.KH_WashStationCall s = washCalls[i];
                if (s == null) continue;   // radius 0: no circle
                _koCenter[i] = s.anchor != null ? s.anchor.position : s.transform.position;   // field reads: works on a switched-off station too
                _koShelf[i] = s.transform.position;   // the Call sits at the station's origin = the shelf's centre on the ground
                _koRadius[i] = s.keepOutRadius > 0f ? s.keepOutRadius : 0f;
            }
        }

        /// <summary>The washCalls index of the keep-out circle containing p (horizontal), or -1. Static circles only.</summary>
        int KeepOutAt(Vector3 p)
        {
            if (!_koReady) KeepOutInit();
            for (int i = 0; i < _koRadius.Length; i++)
            {
                float r = _koRadius[i];
                if (r <= 0f) continue;
                float dx = p.x - _koCenter[i].x, dz = p.z - _koCenter[i].z;
                if (dx * dx + dz * dz < r * r) return i;
            }
            return -1;
        }

        /// <summary>Is p outside every circle by at least `extra` metres (horizontal)?</summary>
        bool KeepOutClear(Vector3 p, float extra)
        {
            if (!_koReady) KeepOutInit();
            for (int i = 0; i < _koRadius.Length; i++)
            {
                float r = _koRadius[i];
                if (r <= 0f) continue;
                float dx = p.x - _koCenter[i].x, dz = p.z - _koCenter[i].z, re = r + extra;
                if (dx * dx + dz * dz < re * re) return false;
            }
            return true;
        }

        /// <summary>Round 3: the circle whose interior the straight way a -> b passes through (segment-circle test), or -1.</summary>
        int KeepOutCrossing(Vector3 a, Vector3 b)
        {
            if (!_koReady) KeepOutInit();
            for (int i = 0; i < _koRadius.Length; i++)
            {
                float r = _koRadius[i];
                if (r <= 0f) continue;
                if (SegmentDistXZ(_koCenter[i], a, b) < r) return i;
            }
            return -1;
        }

        /// <summary>Round 3, shared derivation: a roaming home (areaCenter) closer to a station than its radius + the wander disc
        /// (+ KeepOutPad) is moved straight away from it until the whole disc lies outside the circle. Static circles only, so every
        /// client and every dog (WanderAnchorAt) gets the same home. The scene's homes are not changed.</summary>
        Vector3 KeepOutHome(Vector3 home, float discRadius)
        {
            if (!_koReady) KeepOutInit();
            for (int i = 0; i < _koRadius.Length; i++)
            {
                float r = _koRadius[i];
                if (r <= 0f) continue;
                float need = r + discRadius + KeepOutPad;
                Vector3 d = home - _koCenter[i]; d.y = 0f;
                float m = d.magnitude;
                if (m >= need) continue;
                Vector3 dir = m > 0.01f ? d / m : KeepOutFront(i);
                home = new Vector3(_koCenter[i].x + dir.x * need, home.y, _koCenter[i].z + dir.z * need);
            }
            return home;
        }

        /// <summary>Round 3: the wander's detour around circle k from `from` toward `to`: a rim point at radius + KeepOutDetourPad, 45
        /// degrees round the circle the shorter way (a 45 degree chord at that radius stays outside the circle). _koDetourOk = false
        /// when it lies in the river band or another circle (no out parameter: U# user methods).</summary>
        Vector3 KeepOutDetour(int k, Vector3 from, Vector3 to)
        {
            Vector3 c = _koCenter[k];
            Vector3 a = from - c; a.y = 0f;
            Vector3 b = to - c; b.y = 0f;
            Vector3 dir = a.sqrMagnitude > 0.0001f ? a.normalized : KeepOutFront(k);
            float sign = Vector3.Angle(Quaternion.Euler(0f, 45f, 0f) * dir, b) <= Vector3.Angle(Quaternion.Euler(0f, -45f, 0f) * dir, b) ? 1f : -1f;   // toward the target
            float reach = _koRadius[k] + KeepOutDetourPad;
            // 45 degrees, smaller when the dog stands close to the rim (the chord must not cut the circle)
            for (int j = 0; j < 3; j++)
            {
                float ang = j == 0 ? 45f : (j == 1 ? 30f : 18f);
                Vector3 q = Quaternion.Euler(0f, sign * ang, 0f) * dir;
                Vector3 p = new Vector3(c.x + q.x * reach, from.y, c.z + q.z * reach);
                if (KeepOutAt(p) >= 0 || KeepOutCrossing(from, p) >= 0) continue;
                if (riverData != null && riverData._InBand(p, KominkaHillside.KH_RiverData.BandMargin)) continue;
                _koDetourOk = true;
                return p;
            }
            _koDetourOk = false;
            return from;
        }
        private bool _koDetourOk;

        /// <summary>From the shelf toward the duckboard (the station's open front), horizontal unit vector.</summary>
        Vector3 KeepOutFront(int k)
        {
            Vector3 f = _koCenter[k] - _koShelf[k]; f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        /// <summary>dir turned by the j-th step of 0, +45, -45, +90, -90, +135, -135, 180 degrees (about y).</summary>
        Vector3 KeepOutTurn(Vector3 dir, int j)
        {
            if (j <= 0) return dir;
            float a = j >= 7 ? 180f : ((j + 1) / 2) * 45f * ((j & 1) == 1 ? 1f : -1f);
            return Quaternion.Euler(0f, a, 0f) * dir;
        }

        /// <summary>B, shared derivation: a destination inside a keep-out moves out to the circle + KeepOutPad, straight away from the
        /// centre (from the front when it is the centre), turned in 45 degree steps when that point is in the river band or another
        /// circle. Inputs: the point, the static circles and the river band only - every client and every dog gets the same point.
        /// No NavMesh query (the original waypoint was never checked either; StepToward stops at the river band as before).</summary>
        Vector3 KeepOutFilter(Vector3 p)
        {
            int k = KeepOutAt(p);
            if (k < 0) return p;
            Vector3 c = _koCenter[k];
            Vector3 d = p - c; d.y = 0f;
            float m = d.magnitude;
            Vector3 dir = m > 0.01f ? d / m : KeepOutFront(k);
            // Round 3: widen before giving up - 8 directions at +0.3 m, then +1.3 m and +2.3 m; the first outside every circle and
            // the river band. If the river band rules out all 24, the first one outside every circle (the walk stops at the band, as
            // any waypoint did; it never enters a circle). Only if every point is in another circle (stations are >= 20 m apart, so
            // not in this world) the radial point.
            bool hasOut = false;
            Vector3 outPt = Vector3.zero;
            for (int ring = 0; ring < 3; ring++)
            {
                float reach = _koRadius[k] + KeepOutPad + ring;
                for (int j = 0; j < 8; j++)
                {
                    Vector3 q = KeepOutTurn(dir, j);
                    Vector3 cand = new Vector3(c.x + q.x * reach, p.y, c.z + q.z * reach);
                    if (KeepOutAt(cand) >= 0) continue;
                    if (!hasOut) { hasOut = true; outPt = cand; }
                    if (riverData != null && riverData._InBand(cand, KominkaHillside.KH_RiverData.BandMargin)) continue;
                    return cand;
                }
            }
            if (hasOut) return outPt;
            return new Vector3(c.x + dir.x * (_koRadius[k] + KeepOutPad), p.y, c.z + dir.z * (_koRadius[k] + KeepOutPad));
        }

        // ================= A: the release edge (every client, StationTick) =================

        /// <summary>The station k let this dog go (not to another station). True = the dog rides the stand down first (StationTick
        /// then skips its ground snap). Every client runs it on the same synced edge.</summary>
        bool StationLeaveEdge(int k)
        {
            // An explicit end: the wash hold, the satisfied sit and a paw lift do not keep the dog on the stand (holdSeconds would)
            _washSatisfyUntil = -1000f; _washSatisfySitAt = -1000f; _washPawUntil = -1000f;
            if (wash != null && wash._WashHold()) wash._ReleaseHold();
            // An errand (it released the station on its accept edge) or a leash takes the dog: it is only freed
            if (WashYieldsToErrand() || LegsWalker() >= 0 || dogRoot == null) return false;
            if (!_koReady) KeepOutInit();
            if (k < 0 || k >= _koRadius.Length || washCalls[k] == null) return false;
            // Only the dog that is at the station (on the stand, or inside its circle); one released on its way there is just free
            bool onStand = _stationLocked;
            _graceStation = -1;   // a call ended any unleash grace: the station's own end applies
            if (!onStand && KeepOutAt(dogRoot.position) != k) return false;
            _leaveFails = 0; _keepOutRestUntil = 0f;   // a wash that ended is a fresh start for the walk out
            StartLeave(k, onStand, true);
            if (!onStand) return false;
            // Ride down. Round 3: a station that was switched off has no Update to lower its stand, so the dog eases itself down to
            // the ground (UpdateLeaveLowering); the deadline covers the full lift either way.
            KominkaHillside.KH_WashStationCall st = washCalls[k];
            // Above the ground under its centre at all (a raised stand, or the duckboard's top over a dip)? Then ride / ease down
            // instead of StationTick's snap. With the stand still up and its station on: ride the stand; else ease itself.
            _leaveGroundY = GroundAfterPlacedSleep(dogRoot.position).y;   // a read (the debug writer tag only); nothing is written here
            if (dogRoot.position.y <= _leaveGroundY + 0.02f) { _leaveOwnEase = false; return false; }
            _leaveOwnEase = !st.gameObject.activeInHierarchy || st._LiftNow() <= LeaveLowerDone;
            _leaveStation = k;   // round 6: the ride down needs it even when the walk's point failed (LeaveClear cleared it)
            _leaveLowering = true;
            float down = st.liftMax / Mathf.Max(0.05f, st.liftSpeed) + 1f;
            _leaveLowerUntil = Time.time + down;
            _leaveUntil += down;   // the walk's deadline starts once the stand is down
            return true;
        }

        /// <summary>Start a walk out of circle k (A: fromStand, head first along standYaw; B: straight away from the centre).
        /// brisk = the wash is over (A, or B once an unleash grace ran out): stationLeaveGait; else a calm walk (walkSpeed).
        /// Round 3: no checked point = the attempt fails at once (LeaveFailed); the wander's hop is re-chosen after any walk out.</summary>
        void StartLeave(int k, bool fromStand, bool brisk)
        {
            _leaveBrisk = brisk;
            Vector3 from = dogRoot.position;
            Vector3 baseDir;
            if (fromStand) baseDir = Quaternion.Euler(0f, washCalls[k].standYaw, 0f) * Vector3.forward;   // field read (the station may be switched off)
            else baseDir = KeepOutRadial(k, from);
            _leaveTarget = LeaveTarget(k, from, baseDir);
            _leaveOwnerChose = _dogOwner;
            _leaveActive = true; _leaveStation = k; _leaveFromStation = fromStand;
            Vector3 d = _leaveTarget - from; d.y = 0f;
            _leaveUntil = Time.time + 3f + 2f * d.magnitude / Mathf.Max(0.3f, LeaveSpeed());
            _leaveTickFrame = Time.frameCount;
            if (_restPhase != RestPhaseNone) EndRest();
            _behaviorSelectionValid = false;
            _wanderPassHop = int.MinValue;   // round 3 (review 1): the hop that led in is not resumed; re-chosen from where the dog is
            _leaveCount++;
            if (!_leaveTargetOk) LeaveFailed(2);
        }

        /// <summary>Straight away from circle k's centre through p (the front when p is the centre).</summary>
        Vector3 KeepOutRadial(int k, Vector3 p)
        {
            Vector3 d = p - _koCenter[k]; d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? d.normalized : KeepOutFront(k);
        }

        /// <summary>Round 3 (review 3): a walk out that did not get out (deadline, no point, at its point but still inside). After
        /// LeaveMaxFails in a row the keep-out walk rests for LeaveCooldown: the dog goes back to its usual behaviour (it still never
        /// rests inside a circle - UpdateRest - and its wander never crosses one), so it is never stuck in a walk-out loop.</summary>
        void LeaveFailed(int why)
        {
            _dbgLeaveFail = why;   // round 5 read-out: 1 deadline, 2 no point, 3 at its point but inside
            LeaveClear();
            _leaveTimeouts++;
            _leaveFails++;
            _behaviorSelectionValid = false;
            if (_leaveFails >= LeaveMaxFails) { _leaveFails = 0; _keepOutRestUntil = Time.time + LeaveCooldown; }
        }

        /// <summary>Where the walk out goes. Ring 0: keepOutRadius + max(stationLeaveMargin, LeaveTargetClear + 0.1) from the anchor in 8
        /// directions (baseDir, then +-45 ... 180); rings 1-2 (round 3): 1.5 m and 3 m further, turned 22.5 degrees, sampled wider. A
        /// candidate counts when its straight way keeps clear of the shelf, its NavMesh point (sampled) lies LeaveTargetClear outside
        /// EVERY circle (review 2: the arrival tolerance cannot end inside) and outside the river band; the Owner also prefers one clear
        /// of the players.
        /// Round 5 (coordinator: the dog must get out even without a clean line): when no candidate counts, the best available one
        /// is used instead of failing - the first candidate (ring order) that keeps clear of the shelf and the river band and whose own
        /// point lies LeaveTargetClear outside every circle, whatever the NavMesh sample or the players said (the walk then goes
        /// straight: SleepStep falls back to StepToward; the river band still stops it). Only when even that is missing (every
        /// direction through the shelf or into the river: not possible with 24 directions round a 1.2 m shelf) _leaveTargetOk is false.
        /// Read-outs (written at a walk's start only): _dbgLt* counts per rejection reason, _dbgLtPick (1 clean, 2 player-blocked first,
        /// 3 best available, 0 none), _dbgLtRing.</summary>
        Vector3 LeaveTarget(int k, Vector3 from, Vector3 baseDir)
        {
            Vector3 c = _koCenter[k];
            int players = 0;
            if (_dogOwner)
            {
                players = VRCPlayerApi.GetPlayerCount();
                if (players > _players.Length) players = _players.Length;
                VRCPlayerApi.GetPlayers(_players);
            }
            _dbgLtTried = 0; _dbgLtShelf = 0; _dbgLtNav = 0; _dbgLtClose = 0; _dbgLtRiver = 0; _dbgLtPlayer = 0; _dbgLtPick = 0; _dbgLtRing = -1;
            bool hasFirst = false, hasBest = false;
            Vector3 first = Vector3.zero, best = Vector3.zero;
            int firstRing = -1, bestRing = -1;
            for (int ring = 0; ring < 3; ring++)
            {
                float reach = _koRadius[k] + Mathf.Max(LeaveTargetClear + 0.1f, stationLeaveMargin) + ring * 1.5f;
                Vector3 dir = ring == 0 ? baseDir : Quaternion.Euler(0f, 22.5f, 0f) * baseDir;
                float sample = ring == 0 ? 1.5f : 2.5f;
                for (int j = 0; j < 8; j++)
                {
                    _dbgLtTried++;
                    Vector3 q = KeepOutTurn(dir, j);
                    Vector3 cand = new Vector3(c.x + q.x * reach, from.y, c.z + q.z * reach);
                    if (ShelfBlocks(k, from, cand)) { _dbgLtShelf++; continue; }
                    bool candRiver = riverData != null && riverData._InBand(cand, KominkaHillside.KH_RiverData.BandMargin);
                    if (!hasBest && !candRiver && KeepOutClear(cand, LeaveTargetClear)) { hasBest = true; best = cand; bestRing = ring; }
                    UnityEngine.AI.NavMeshHit hit;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(cand, out hit, sample, UnityEngine.AI.NavMesh.AllAreas)) { _dbgLtNav++; continue; }
                    Vector3 p = hit.position;
                    if (!KeepOutClear(p, LeaveTargetClear)) { _dbgLtClose++; continue; }
                    if (riverData != null && riverData._InBand(p, KominkaHillside.KH_RiverData.BandMargin)) { _dbgLtRiver++; continue; }
                    if (!hasFirst) { hasFirst = true; first = p; firstRing = ring; }
                    if (LeaveNearPlayer(from, p, players)) { _dbgLtPlayer++; continue; }
                    _leaveTargetOk = true; _dbgLtPick = 1; _dbgLtRing = ring;
                    return p;
                }
                if (hasFirst) { _leaveTargetOk = true; _dbgLtPick = 2; _dbgLtRing = firstRing; return first; }   // a ring with only player-blocked points
            }
            if (hasBest) { _leaveTargetOk = true; _dbgLtPick = 3; _dbgLtRing = bestRing; return best; }
            _leaveTargetOk = false;
            return from;
        }

        /// <summary>Round 6 (C-keepout-leash: a dog 0.6 m from the shelf's centre had every direction rejected): does the straight
        /// way from -> to pass the shelf? Rejected when it comes closer to the shelf's centre than LeaveShelfClear AND closer than the
        /// dog already stands (less 0.05 m). So a dog standing inside the clearance (beside the shelf, where a player stands with a
        /// leashed dog) may always move away from it; only lines that cut past the shelf are refused.</summary>
        bool ShelfBlocks(int k, Vector3 from, Vector3 to)
        {
            Vector3 sc = _koShelf[k];
            float start = Horizontal(sc, from);
            float near = SegmentDistXZ(sc, from, to);
            return near < LeaveShelfClear && near < start - 0.05f;
        }

        /// <summary>Round 6: a walk out that ended (done, dropped, failed, guard) leaves no stale point / station / deadline behind
        /// (the dump showed the River station's from a much earlier release). The station stays while the dog still rides it down.</summary>
        void LeaveClear()
        {
            _leaveActive = false;
            _leaveTarget = Vector3.zero;
            _leaveUntil = 0f;
            if (!_leaveLowering) _leaveStation = -1;
        }

        /// <summary>Owner: does the way from -> to (past its first 0.5 m) pass within LeavePlayerClear of a player?</summary>
        bool LeaveNearPlayer(Vector3 from, Vector3 to, int players)
        {
            Vector3 d = to - from; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.6f) return false;
            Vector3 a = from + d / len * 0.5f;
            for (int i = 0; i < players; i++)
            {
                VRCPlayerApi pl = _players[i];
                if (!Utilities.IsValid(pl)) continue;
                if (SegmentDistXZ(pl.GetPosition(), a, to) < LeavePlayerClear) return true;
            }
            return false;
        }

        /// <summary>Horizontal distance from p to the segment a-b.</summary>
        float SegmentDistXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x, abz = b.z - a.z, apx = p.x - a.x, apz = p.z - a.z;
            float len2 = abx * abx + abz * abz;
            float t = len2 > 1e-6f ? Mathf.Clamp01((apx * abx + apz * abz) / len2) : 0f;
            float dx = apx - abx * t, dz = apz - abz * t;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The walk out's pace: stationLeaveGait when the wash is over (_leaveBrisk), else walkSpeed.</summary>
        float LeaveSpeed()
        {
            if (!_leaveBrisk) return walkSpeed;
            if (stationLeaveGait >= 2) return runSpeed;
            if (stationLeaveGait == 1) return Mathf.Min(runSpeed, runAnimSpeed + 0.1f);
            return walkSpeed;
        }

        // ================= A: riding the stand down (UpdateHillsideWash, right after the station branch) =================

        /// <summary>Every client, every frame while it lasts (TickBusy): the dog stands still while it comes down. Normally on the anchor
        /// while the stand eases to 0 (KH_WashStationCall.Update, liftSpeed on every client), the Owner writing the root's y from the
        /// anchor - the same "the root's y IS the stand's height" as on the stand. Round 3 (review 5): when the station is switched off
        /// (its Update no longer lowers the stand) the Owner eases the root itself to the ground at the same liftSpeed. Then a ground
        /// snap (the duckboard's 0.03 m) and the walk out. False = not lowering (also on the frame it ends: the walk / whoever took the
        /// dog runs in this same tick).</summary>
        bool UpdateLeaveLowering()
        {
            if (!_leaveLowering) return false;
            KominkaHillside.KH_WashStationCall st = _leaveStation >= 0 && washCalls != null && _leaveStation < washCalls.Length ? washCalls[_leaveStation] : null;
            if (st != null && !_leaveOwnEase && !st.gameObject.activeInHierarchy)
            {
                _leaveOwnEase = true;   // switched off mid-way
                _leaveGroundY = GroundAfterPlacedSleep(dogRoot.position).y;
            }
            // Round 3 (C-release 0.1 m): the stand's top at 0 is the duckboard (0.03 m over the HIGHEST ground under it), the ground
            // under the dog's centre can be lower still on a slope; that last step is eased at liftSpeed too instead of snapped
            if (st != null && !_leaveOwnEase && st._LiftNow() <= LeaveLowerDone)
            {
                _leaveOwnEase = true;
                _leaveGroundY = GroundAfterPlacedSleep(dogRoot.position).y;
            }
            bool down = st == null || (_leaveOwnEase && dogRoot.position.y <= _leaveGroundY + LeaveLowerDone);
            if (down || Time.time > _leaveLowerUntil || WashYieldsToErrand() || LegsWalker() >= 0)
            {
                _leaveLowering = false; _leaveOwnEase = false;
                if (!_leaveActive) _leaveStation = -1;   // round 6: nothing stale once down without a walk
                if (_dogOwner && dogRoot != null) dogRoot.position = GroundAfterPlacedSleep(dogRoot.position); // net: owner-only: _dogOwner、台が下り切った (残り 0.03 m) ので接地
                _leaveTickFrame = Time.frameCount;   // the walk out starts from here, not "something stronger won"
                return false;
            }
            _behaviorSelectionValid = false;
            _speed = 0f; _moveDir = Vector3.zero; _stopHoldUntil = 0f;
            ApplyMoveAnim();
            if (_dogOwner)
            {
                Vector3 p = dogRoot.position;
                float y = _leaveOwnEase ? Mathf.MoveTowards(p.y, _leaveGroundY, Mathf.Max(0.05f, st.liftSpeed) * MoveDt()) : st._AnchorPosition().y;
                _dbgWriter = DbgWStation;
                dogRoot.position = new Vector3(p.x, y, p.z); // net: owner-only: _dogOwner、台と一緒に下りる (はね落ちない)
            }
            return true;
        }

        /// <summary>WashErrandTick (every decision, every client): a ride down that nobody finishes (a leash or a stronger branch took
        /// the dog above UpdateHillsideWash) ends by its own deadline, so WashHeld / StationLocked / TickBusy never stay up.</summary>
        void LeaveGuardTick()
        {
            if (!_leaveLowering) return;
            if (Time.time <= _leaveLowerUntil && LegsWalker() < 0) return;
            _leaveLowering = false; _leaveOwnEase = false;
            LeaveClear();
        }

        // ================= A / B: the walk out (UpdateTick, after the greeting's continuation, before rest) =================

        /// <summary>Every client (the decision), the Owner's legs move. Continues a walk out (A or B), or starts B when this free dog
        /// stands inside a keep-out circle. True = handled this tick (the caller sets BehaviorLeave).
        /// Round 3 ends: done once the dog itself is LeaveDoneClear outside EVERY circle (review 2; also ends a walk left over from
        /// before a teleport at once); dropped when a decision went by without it (something stronger took the dog: the leash, Fetch /
        /// the disc, water, an errand, a treat... - that wins); failed (LeaveFailed: counts toward the cool-down) on its deadline or at
        /// its point while still inside. An Owner change re-chooses the point as the new Owner (review 4: the player test is Owner-only).</summary>
        bool UpdateStationLeave()
        {
            if (washCalls == null || washCalls.Length == 0 || dogRoot == null) return false;
            Vector3 pos = dogRoot.position;
            if (_leaveActive)
            {
                if (_leaveTickFrame != Time.frameCount && _leaveTickFrame != _prevDecisionFrame) LeaveClear();
                else if (KeepOutClear(pos, LeaveDoneClear)) { LeaveClear(); _leaveFails = 0; _behaviorSelectionValid = false; return false; }
                else if (Time.time > _leaveUntil) LeaveFailed(1);
                else if (_leaveOwnerChose != _dogOwner && _leaveStation >= 0)
                {
                    _leaveTarget = LeaveTarget(_leaveStation, pos, KeepOutRadial(_leaveStation, pos));
                    _leaveOwnerChose = _dogOwner;
                    if (!_leaveTargetOk) LeaveFailed(2);
                }
            }
            if (!_leaveActive)
            {
                // B: not mid-play (the chase may cross a circle; its anchors are filtered, so it never ends inside)
                if (_playingPrev) { _dbgKoK = -2; _dbgKoAt = Time.time; return false; }   // round 5 read-out: -2 = skipped mid-play
                int k = KeepOutAt(pos);
                _dbgKoK = k; _dbgKoAt = Time.time;
                if (k < 0) return false;
                // Round 2: unleashed in this circle -> stand here for the grace (the walker picks up a tool). No rest: UpdateRest is
                // below this branch and refuses inside a circle anyway. A stroke takes the dog into the wash hold (above this branch)
                // and restarts the grace from the last wash frame; a call takes it into the station hold.
                if (_graceStation == k)
                {
                    if (Time.time < Mathf.Max(_graceUntil, _washLastAt + stationUnleashGrace))
                    {
                        // Round 4 (C-keepout-leash restPhase 4): the free rest FSM is frozen, not ended, while leashed (Update returns
                        // at the leash line), so a rest from before the leash came back here. Inside a circle it ends: the dog stands
                        if (_restPhase != RestPhaseNone) EndRest();
                        _graceFrames++;
                        Coast();
                        return true;
                    }
                }
                // Round 3 cool-down after repeated failures: the usual behaviour (never a rest inside, a wander that avoids the circle)
                if (Time.time < _keepOutRestUntil) return false;
                bool graced = _graceStation == k;   // the grace ran out: the wash is over, leave as briskly as after 「おわり」
                _graceStation = -1;
                StartLeave(k, false, graced);
                _keepOutExits++;
                if (!_leaveActive) return false;   // no point found: failed at once
            }
            _leaveTickFrame = Time.frameCount;
            Vector3 to = _leaveTarget - pos; // sync: local-ok: 到着判定のみ — 目標は洗い場の固定の円と NavMesh から
            to.y = 0f;
            if (to.magnitude <= LeaveArrive) { LeaveFailed(3); return false; }   // at its point but not clear of the circles
            SleepStep(_leaveTarget, LeaveSpeed());
            return true;
        }

        // ================= Round 3 (review 1): the wander never walks a straight line through a circle =================
        private int _wanderPassHop = int.MinValue;   // the hop the pass below was chosen for (int.MinValue = choose again)
        private Vector3 _wanderPassFor;              // ... and the waypoint it was chosen for (a moved home re-chooses)
        private Vector3 _wanderPassTarget;
        private bool _wanderPassStand;
        private int _wanderDetours, _wanderStands;   // harness read-outs

        /// <summary>UpdateWander, once per hop (cached; re-chosen after a walk out, StartLeave): the point to walk to for this hop's
        /// waypoint. The waypoint itself when the straight way from where the dog stands now does not pass through a circle; else
        /// up to 3 other points of the same disc (re-rolled from the hop, KeepOutHome / KeepOutFilter as WanderTarget) whose way is
        /// clear; else a detour 45 degrees round the circle on its rim (KeepOutDetour; the next hop goes on from there); else none:
        /// _wanderPassStand (the dog stands this hop out, as on an early arrival). Uses the dog's own position, so on a non-Owner it
        /// only picks the legs (the Owner's choice moves the root); the shared waypoint (WanderTarget / WanderAnchorAt) is unchanged.</summary>
        Vector3 WanderPass(int hop, Vector3 target)
        {
            if (washCalls == null || washCalls.Length == 0) { _wanderPassStand = false; return target; }
            if (hop == _wanderPassHop && target == _wanderPassFor) return _wanderPassTarget;
            _wanderPassHop = hop; _wanderPassFor = target; _wanderPassStand = false;
            Vector3 from = dogRoot.position;
            int k = KeepOutCrossing(from, target);
            if (k < 0) { _wanderPassTarget = target; return target; }
            Vector3 home = KeepOutHome(areaCenter.position, areaRadius);
            for (int a = 0; a < 3; a++)
            {
                float r = Mathf.Sqrt(Rand01(hop * 7 + a + 1, RandStreamWaypointRadius)) * areaRadius;
                float ang = Rand01(hop * 7 + a + 1, RandStreamWaypointAngle) * Mathf.PI * 2f;
                Vector3 alt = KeepOutFilter(home + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r));
                if (KeepOutCrossing(from, alt) >= 0) continue;
                _wanderPassTarget = alt;
                return alt;
            }
            Vector3 det = KeepOutDetour(k, from, target);
            if (_koDetourOk) { _wanderDetours++; _wanderPassTarget = det; return det; }
            _wanderStands++;
            _wanderPassStand = true;
            _wanderPassTarget = from;
            return from;
        }

        /// <summary>Round 2: the leash branch's unleash edge (every client, from the synced walkers[dog] going to -1; whoever released
        /// it - the walker, the menu, the Owner's maintenance - and whoever owns the dog). Inside a circle: that circle's grace starts.</summary>
        void KeepOutUnleashed()
        {
            _graceStation = -1;
            if (washCalls == null || washCalls.Length == 0 || dogRoot == null || !(stationUnleashGrace > 0f)) return;
            int k = KeepOutAt(dogRoot.position);
            if (k < 0) return;
            _graceStation = k;
            _graceUntil = Time.time + stationUnleashGrace;
            // Round 4: a rest frozen by the leash does not come back on the stand / in the circle (it would lie down where unleashed)
            if (_restPhase != RestPhaseNone) EndRest();
        }

        /// <summary>UpdateRest: the rest neither starts nor goes on inside a keep-out (it lies where the dog stands). Round 7: nor within
        /// stationRestMargin (1.5 m) outside its rim - the walk out ends 0.5 m outside, and a dog that ran before (Fetch, the brisk
        /// walk out, a call from afar) would otherwise lie down right at the exit, beside the 5 x 5 m floor. The rest just waits until
        /// the dog's wander has taken it further (nothing pushes it; it stands meanwhile).</summary>
        bool KeepOutHere()
        {
            return washCalls != null && washCalls.Length > 0 && dogRoot != null && !KeepOutClear(dogRoot.position, stationRestMargin > 0f ? stationRestMargin : 0f);
        }

        /// <summary>UpdateGreeting: is this player inside a keep-out (a welcome / drop-by does not start toward them)?</summary>
        bool KeepOutPlayer(int playerId)
        {
            if (washCalls == null || washCalls.Length == 0) return false;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(playerId);
            return Utilities.IsValid(p) && KeepOutAt(p.GetPosition()) >= 0;
        }

        // ================= harness =================

        /// <summary>Harness (Course/100f C-keepout-probe): from the current areaCenter, hops _dbgKoHop0 .. +199 through the wander's own
        /// WanderTarget (its raw point = areaCenter + the cached offset) and the play's shared anchors of this dog (WanderAnchorAt,
        /// FleeHopTargetByHop). Counts raw waypoints inside a circle (the control), filtered ones inside (must be 0), play anchors
        /// inside (must be 0) and the largest move the filter made. Local only; the wander cache is keyed by hop and recomputes.</summary>
        public void _DebugKeepOutProbe()
        {
            _dbgKoSamples = 0; _dbgKoRawIn = 0; _dbgKoFilteredIn = 0; _dbgKoPlayIn = 0; _dbgKoPlaySamples = 0; _dbgKoMaxShift = 0f;
            if (areaCenter == null) return;
            bool play = _packAreaCenter != null && _packFleeInner != null && dogIndex >= 0 && dogIndex < _packAreaCenter.Length && _packAreaCenter[dogIndex] != null;
            for (int h = _dbgKoHop0; h < _dbgKoHop0 + 200; h++)
            {
                Vector3 f = WanderTarget(h);
                Vector3 raw = areaCenter.position + _wanderTargetOffset;
                _dbgKoSamples++;
                if (KeepOutAt(raw) >= 0) _dbgKoRawIn++;
                if (KeepOutAt(f) >= 0) _dbgKoFilteredIn++;
                float s = Horizontal(raw, f);
                if (s > _dbgKoMaxShift) _dbgKoMaxShift = s;
                if (play)
                {
                    if (KeepOutAt(FleeHopTargetByHop(h, h & 7, dogIndex)) >= 0) _dbgKoPlayIn++;
                    if (KeepOutAt(WanderAnchorAt(h * 37.0, dogIndex)) >= 0) _dbgKoPlayIn++;
                    _dbgKoPlaySamples += 2;   // round 3: the harness fails when the play part did not run
                }
            }
        }
    }
}
