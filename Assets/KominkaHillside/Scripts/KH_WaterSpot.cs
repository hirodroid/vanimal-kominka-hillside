using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    // IMP-DOG-020 step 2 (DES-DOG-004 A-1 / A-3): the one water-play spot, beside the destination "川辺" on the east
    // (outer) bank of the bend at z 285. Baked by Tools/KominkaHillside/Course/76 - Build Water Spot; all clients hold the
    // same data (sync None).
    //   region  : polygon in x/z (polyX / polyZ). Inside it, and only for a dog in WaterPlay, the river-band rule is lifted
    //             (KD_DogWanderController.HillsideWater._BlockedByRiver).
    //   entry   : where a dog walks in (bank NavMesh, outside the riverbed exclusion).
    //   exits   : bank NavMesh points; exits[0] is the entry. returns[k] is the bank point behind exits[k], with a complete
    //             NavMesh path from the "川辺" pass point (Course/77 check 3).
    //   grid    : riverbed height every 0.5 m over the region's bounding box (bed, gridNx * gridNz, row-major in z); -1000 =
    //             no data (older builds baked only the inside; callers treat it as unknown, not as dry).
    // Depth = water surface (KH_RiverData) - riverbed (grid, bilinear). Return values only (no out parameters).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WaterSpot : UdonSharpBehaviour
    {
        public const float NoBed = -1000f;
        public KH_RiverData riverData;
        public float[] polyX;
        public float[] polyZ;
        public Vector3 entry;
        public Vector3[] exits;
        public Vector3[] returns;
        public float gridX0, gridZ0, gridStep = 0.5f;
        public int gridNx, gridNz;
        public float[] bed;
        public float entryRadius = 25f;    // B-1 (decision 2026-09-27, 2nd): a free dog within this of the entry starts the first water play
        // ---- water-play tuning: the single source (DES-DOG-004 B-2 trial values; Codex 2026-09-27 M-低). The dogs, Course/76-77
        // and the harness all read these. M = the dog's standing mouth height (KD_DogWanderController.waterMouthHeight).
        // The constants are the one place to change a value: the fields start from them and Course/76 writes them into the
        // scene again, so an existing spot never keeps an old value.
        public const float DefaultWadeLimitRatio = 0.45f, DefaultSwimEnterRatio = 0.75f, DefaultSwimLeaveRatio = 0.6f, DefaultSwimLeaveHold = 0.5f;
        // swimClear 0.04 -> 0.06 (run 20260927-072549): with the walk clip the Pome's real mouth sat 2.6 cm below root + M,
        // leaving 0.014 m; 0.06 gives every breed >= 0.03 m at the real mouth (Pome about 0.034).
        public const float DefaultSwimClear = 0.06f, DefaultSwimSpeedLab = 0.8f, DefaultSwimMouthScale = 1f, DefaultSwimBob = 0.01f, DefaultSwimBobHz = 0.8f;
        public float wadeLimitRatio = DefaultWadeLimitRatio;   // stage 0 / non-swimmers: never deeper than 0.45 M
        public float swimEnterRatio = DefaultSwimEnterRatio;   // into swimming when the ground ahead is >= 0.75 M deep
        public float swimLeaveRatio = DefaultSwimLeaveRatio;   // back to wading after < 0.6 M for swimLeaveHold s
        public float swimLeaveHold = DefaultSwimLeaveHold;
        public float swimClear = DefaultSwimClear;             // afloat: root = water + swimClear - Ms (mouth just above the water)
        public float swimSpeedLab = DefaultSwimSpeedLab;       // swim speed = swimSpeedLab x sqrt(M / 0.556) m/s
        public float swimMouthScale = DefaultSwimMouthScale;   // Ms = M x this (mouth height with the walk clip; no swim clip)
        public float swimBob = DefaultSwimBob;                 // +- m on the dog's mesh skeleton root (0 when there is none)
        // Ms (KD_DogWanderController.waterSwimMouth) = M x swimMouthScale - the breed's measured walk-clip head drop; the drop
        // table is in KH_WaterSpotEditor.WalkHeadDrop (build time only; U# has no static fields).
        // Walking (not afloat), the walk-clip mouth stays at least this far above the water: a swimmer starts to float before
        // that, and anyone else stops (sink guard). Design floor 0.03 m + 1 cm.
        public const float DefaultWadeClear = 0.04f;
        public float wadeClear = DefaultWadeClear;
        // Step 6 (floating ball retrieval): while carrying, the swimming mouth sits the ball's radius higher (B-1, 0.5 x 0.085 m).
        public const float DefaultCarryExtra = 0.043f;
        public float carryExtra = DefaultCarryExtra;
        // Floating ball (C-8): y = water - radius x floatSink, drifting downstream (-z: the water surface rises with z) at
        // floatDrift m/s until the region's downstream end.
        // PCVR 2026-09-28 (user): the Pome floats with its belly out of the water, the others could sit a touch lower. Per dog
        // (dogIndex order Corgi, Pome, Lab, Dachs, Shiba, Samoyed): swimSink lowers the afloat root by this many metres below the
        // B-2 height (surface + swimClear - Ms); swimPitch tilts the visual (mesh child / hips bone) nose-up by degrees while afloat
        // so the mouth stays out although the body sits lower. Course/76 writes the defaults; the harness reads the real mouth bone.
        public const float DefaultSwimSinkSmall = 0.015f, DefaultSwimSinkPome = 0.035f, DefaultSwimPitchPome = 10f;
        public float[] swimSink;
        public float[] swimPitch;
        public float _SwimSink(int dog) { return swimSink != null && dog >= 0 && dog < swimSink.Length ? swimSink[dog] : 0f; }
        public float _SwimPitch(int dog) { return swimPitch != null && dog >= 0 && dog < swimPitch.Length ? swimPitch[dog] : 0f; }
        public const float DefaultFloatSink = 0.3f, DefaultFloatDrift = 0.15f;
        public float floatSink = DefaultFloatSink, floatDrift = DefaultFloatDrift;

        // Step 5: the temporary sign "犬の水あそび" (the real sign is DES-UI-004). Local text, refreshed once a second so a
        // language change reaches it; sync None, no events.
        public KominkaDogRun.KD_Language language;
        public TMPro.TextMeshPro signLabel;
        public TMPro.TextMeshPro signShadow;   // the dark copy just behind the label (contrast on the wood), same text
        public string signKey = "course.water.sign";
        private float _nextSign;
        private string _signText;

        void Update()
        {
            if (signLabel == null || language == null || Time.time < _nextSign) return;
            _nextSign = Time.time + 1f;
            string t = language._Text(signKey);
            if (t != _signText) { _signText = t; signLabel.text = t; if (signShadow != null) signShadow.text = t; }
        }

        /// <summary>The downstream end of the region (smallest z, 0.5 m inside): a floating ball stops here.</summary>
        public float _DownstreamZ()
        {
            if (polyZ == null || polyZ.Length == 0) return 0f;
            float m = polyZ[0];
            for (int i = 1; i < polyZ.Length; i++) if (polyZ[i] < m) m = polyZ[i];
            return m + 0.5f;
        }

        /// <summary>Diagonal of the region's bounding box (the water Fetch timeout, B-1: 1.5 x diagonal / swim speed + 5 s).</summary>
        public float _Diagonal()
        {
            if (polyX == null || polyX.Length == 0) return 0f;
            float x0 = polyX[0], x1 = polyX[0], z0 = polyZ[0], z1 = polyZ[0];
            for (int i = 1; i < polyX.Length; i++)
            {
                if (polyX[i] < x0) x0 = polyX[i]; if (polyX[i] > x1) x1 = polyX[i];
                if (polyZ[i] < z0) z0 = polyZ[i]; if (polyZ[i] > z1) z1 = polyZ[i];
            }
            float dx = x1 - x0, dz = z1 - z0;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The ball floats here: inside the region, inside the river band, and not far above the water.</summary>
        public bool _FloatsAt(Vector3 p)
        {
            return _Contains(p) && riverData != null && riverData._InBand(p, KH_RiverData.BandMargin) && p.y < riverData._SurfaceY(p.z) + 0.3f;
        }
        public float swimBobHz = DefaultSwimBobHz;

        // IMP-DOG-034 A2: _Ready() and the polygon's bounding box are cached. The inputs are baked by Course/76 and only ever replaced
        // as whole arrays (the harness swaps `exits` for a same-length copy); array lengths cannot change in place, so a pass with the
        // same references, grid size and a live riverData returns what the full test would. Any other change re-runs the full test.
        private bool _readyOk;
        private float[] _readyPolyX, _readyPolyZ, _readyBed;
        private Vector3[] _readyExits, _readyReturns;
        private int _readyNx, _readyNz;
        private float _boxX0, _boxX1, _boxZ0, _boxZ1;   // polygon bounds padded by BoxPad (set with _readyOk)
        const float BoxPad = 0.01f;   // >> the float error of the crossing x below (~1e-4 m at these coordinates)

        public bool _Ready()
        {
            if (_readyOk && riverData != null && polyX == _readyPolyX && polyZ == _readyPolyZ && exits == _readyExits
                && returns == _readyReturns && bed == _readyBed && gridNx == _readyNx && gridNz == _readyNz) return true;
            _readyOk = riverData != null && polyX != null && polyZ != null && polyX.Length >= 3 && polyZ.Length == polyX.Length
                && exits != null && returns != null && exits.Length > 0 && returns.Length == exits.Length
                && bed != null && gridNx >= 2 && gridNz >= 2 && bed.Length == gridNx * gridNz;
            if (_readyOk)
            {
                _readyPolyX = polyX; _readyPolyZ = polyZ; _readyExits = exits; _readyReturns = returns; _readyBed = bed;
                _readyNx = gridNx; _readyNz = gridNz;
                float x0 = polyX[0], x1 = polyX[0], z0 = polyZ[0], z1 = polyZ[0];
                bool finite = x0 == x0 && z0 == z0;   // NaN != NaN
                for (int i = 1; i < polyX.Length; i++)
                {
                    if (polyX[i] != polyX[i] || polyZ[i] != polyZ[i]) finite = false;
                    if (polyX[i] < x0) x0 = polyX[i]; if (polyX[i] > x1) x1 = polyX[i];
                    if (polyZ[i] < z0) z0 = polyZ[i]; if (polyZ[i] > z1) z1 = polyZ[i];
                }
                _boxX0 = x0 - BoxPad; _boxX1 = x1 + BoxPad; _boxZ0 = z0 - BoxPad; _boxZ1 = z1 + BoxPad;
                // A NaN vertex is skipped by the min / max above, so the box would not bound the old loop: no pre-check then.
                if (!finite) { _boxX0 = -1e30f; _boxX1 = 1e30f; _boxZ0 = -1e30f; _boxZ1 = 1e30f; }
                BuildSlabs(finite);   // IMP-DOG-034 W-1
            }
            return _readyOk;
        }

        // ---- IMP-DOG-034 W-1: the polygon test by z slabs (same result as the edge loop, bit for bit) ----
        // The edge loop toggles `inside` for every edge (i, j = i - 1) with (zi > pz) != (zj > pz), at the crossing
        // x = polyX[i] + (pz - zi) * (polyX[j] - polyX[i]) / (zj - zi), when px < x. Sort the distinct vertex z values into _slabZ.
        // For pz strictly between two neighbours _slabZ[k] < pz < _slabZ[k + 1], "zi > pz" is the same as "zi >= _slabZ[k + 1]"
        // (every zi is one of the values and none lies in between), so the set of edges that straddle pz is the same for the whole
        // open slab: exactly the edges with min(zi, zj) <= _slabZ[k] and max(zi, zj) >= _slabZ[k + 1]. They are listed once here
        // (_slabEdge[_slabStart[k] .. _slabStart[k + 1]]). The query tests only those edges with the SAME crossing expression and the
        // same comparison; the parity of toggles does not depend on their order. pz equal to a vertex z, outside (first, last),
        // or NaN, and any non-finite polygon, take the edge loop (the old code, with the reads hoisted). About 34 edges x ~8 extern
        // per call -> a binary search (~6 steps) and ~2 edges.
        private float[] _slabZ;       // sorted distinct vertex z; null = no table (non-finite polygon)
        private int[] _slabStart;     // per slab k: first index into _slabEdge (length = slabs + 1)
        private int[] _slabEdge;      // edge i (its j is i - 1, wrapping to n - 1)

        void BuildSlabs(bool finite)
        {
            _slabZ = null; _slabStart = null; _slabEdge = null;
            if (!finite) return;
            int n = polyZ.Length;
            float[] zs = new float[n];
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                float z = polyZ[i];
                int at = 0;
                while (at < m && zs[at] < z) at++;
                if (at < m && zs[at] == z) continue;   // already listed
                for (int k = m; k > at; k--) zs[k] = zs[k - 1];
                zs[at] = z; m++;
            }
            if (m < 2) return;
            float[] sz = new float[m];
            for (int k = 0; k < m; k++) sz[k] = zs[k];
            int slabs = m - 1;
            int[] start = new int[slabs + 1];
            int[] edges = new int[slabs * n];
            int count = 0;
            for (int k = 0; k < slabs; k++)
            {
                start[k] = count;
                float lo = sz[k], hi = sz[k + 1];
                for (int i = 0; i < n; i++)
                {
                    int j = i == 0 ? n - 1 : i - 1;
                    float zi = polyZ[i], zj = polyZ[j];
                    float mn = zi < zj ? zi : zj, mx = zi < zj ? zj : zi;
                    if (mn <= lo && mx >= hi) { edges[count] = i; count++; }
                }
            }
            start[slabs] = count;
            _slabZ = sz; _slabStart = start; _slabEdge = edges;
        }

        public bool _Contains(Vector3 p)
        {
            if (!_Ready()) return false;
            float px = p.x, pz = p.z;   // IMP-DOG-034 W-1: read once (each read of a Vector3 field is an extern)
            // IMP-DOG-034 A2: outside the (padded) bounding box the loop below cannot toggle an odd number of times: z outside
            // [min, max] makes every (zi > p.z) equal, and x left of / right of every crossing toggles an even number (closed polygon)
            // or none. NaN fails these tests and falls through to the loop, as before.
            if (px < _boxX0 || px > _boxX1 || pz < _boxZ0 || pz > _boxZ1) return false;
            bool inside = false;
            int n = polyX.Length;
            float[] sz = _slabZ;
            if (sz != null)
            {
                int hi = sz.Length - 1;
                if (pz > sz[0] && pz < sz[hi])
                {
                    int lo = 0;
                    bool onVertexZ = false;
                    while (hi - lo > 1)
                    {
                        int mid = (lo + hi) / 2;
                        float zm = sz[mid];
                        if (pz > zm) lo = mid;
                        else if (pz < zm) hi = mid;
                        else { onVertexZ = true; break; }
                    }
                    if (!onVertexZ)
                    {
                        int e1 = _slabStart[lo + 1];
                        for (int e = _slabStart[lo]; e < e1; e++)
                        {
                            int i = _slabEdge[e];
                            int j = i == 0 ? n - 1 : i - 1;
                            float zi = polyZ[i], zj = polyZ[j];
                            float x = polyX[i] + (pz - zi) * (polyX[j] - polyX[i]) / (zj - zi);
                            if (px < x) inside = !inside;
                        }
                        return inside;
                    }
                }
            }
            // The edge loop (the old code; zj / its comparison carried from the previous edge instead of re-read: same values)
            float zjc = polyZ[n - 1];
            bool aj = zjc > pz;
            int jc = n - 1;
            for (int i = 0; i < n; i++)
            {
                float zi = polyZ[i];
                bool ai = zi > pz;
                if (ai != aj)
                {
                    float x = polyX[i] + (pz - zi) * (polyX[jc] - polyX[i]) / (zjc - zi);
                    if (px < x) inside = !inside;
                }
                jc = i; zjc = zi; aj = ai;
            }
            return inside;
        }

        /// <summary>IMP-DOG-034 W-2: _Bed(p) and _Depth(p) in one call (the callers asked both, and _Depth asked _Bed again).
        /// NoBed where _Bed(p) is NoBed (<= NoBed + 1); otherwise exactly _Depth(p). Reads p.x / p.z only.</summary>
        public float _WetDepth(Vector3 p)
        {
            float b = _Bed(p);
            if (b <= NoBed + 1f) return NoBed;
            float d = riverData._SurfaceY(p.z) - b;
            return d > 0f ? d : 0f;
        }

        /// <summary>Riverbed height at p (bilinear); NoBed outside the grid or where any corner is outside the region.</summary>
        public float _Bed(Vector3 p)
        {
            if (!_Ready()) return NoBed;
            float fx = (p.x - gridX0) / gridStep, fz = (p.z - gridZ0) / gridStep;
            if (fx < 0f || fz < 0f || fx > gridNx - 1 || fz > gridNz - 1) return NoBed;
            int ix = (int)fx, iz = (int)fz;
            if (ix > gridNx - 2) ix = gridNx - 2;
            if (iz > gridNz - 2) iz = gridNz - 2;
            float tx = fx - ix, tz = fz - iz;
            float a = bed[iz * gridNx + ix], b = bed[iz * gridNx + ix + 1], c = bed[(iz + 1) * gridNx + ix], d = bed[(iz + 1) * gridNx + ix + 1];
            if (a <= NoBed + 1f || b <= NoBed + 1f || c <= NoBed + 1f || d <= NoBed + 1f) return NoBed;
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        /// <summary>Water depth at p (surface - bed). 0 where there is no water or no grid.</summary>
        public float _Depth(Vector3 p)
        {
            float b = _Bed(p);
            if (b <= NoBed + 1f) return 0f;
            float d = riverData._SurfaceY(p.z) - b;
            return d > 0f ? d : 0f;
        }

        public int _ExitCount() { return exits == null ? 0 : exits.Length; }
        public Vector3 _Exit(int k) { return exits[k]; }
        public Vector3 _ReturnPointFor(int k) { return returns[k]; }

        public int _NearestExit(Vector3 p)
        {
            if (exits == null || exits.Length == 0) return -1;
            int best = 0; float bestD = 1e9f;
            for (int k = 0; k < exits.Length; k++)
            {
                Vector3 d = exits[k] - p; d.y = 0f;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = k; }
            }
            return best;
        }

        /// <summary>Nearest exit other than k (recovery stage 2, C-6); k itself when it is the only one.</summary>
        public int _OtherExit(Vector3 p, int k)
        {
            if (exits == null || exits.Length < 2) return k;
            int best = -1; float bestD = 1e9f;
            for (int i = 0; i < exits.Length; i++)
            {
                if (i == k) continue;
                Vector3 d = exits[i] - p; d.y = 0f;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = i; }
            }
            return best;
        }

        public int _CellCount() { return bed == null ? 0 : bed.Length; }

        /// <summary>World point of grid cell i (y = bed); y = NoBed when the cell is outside the region.</summary>
        public Vector3 _CellPoint(int i)
        {
            int ix = i % gridNx, iz = i / gridNx;
            return new Vector3(gridX0 + ix * gridStep, bed[i], gridZ0 + iz * gridStep);
        }

        /// <summary>The region's cell nearest to p (x/z) with minDepth <= depth <= maxDepth; y = NoBed when none. One call scans
        /// the grid here (the water surface is read once per row), instead of hundreds of cross-behaviour calls from the dog.</summary>
        public Vector3 _NearestWetCell(Vector3 p, float minDepth, float maxDepth)
        {
            Vector3 best = new Vector3(0f, NoBed, 0f);
            if (!_Ready()) return best;
            float bestD = 1e9f;
            for (int iz = 0; iz < gridNz; iz++)
            {
                float z = gridZ0 + iz * gridStep, surface = riverData._SurfaceY(z);
                for (int ix = 0; ix < gridNx; ix++)
                {
                    float b = bed[iz * gridNx + ix];
                    if (b <= NoBed + 1f) continue;
                    float d = surface - b;
                    if (d < minDepth || d > maxDepth) continue;
                    Vector3 c = new Vector3(gridX0 + ix * gridStep, b, z);
                    float dx = c.x - p.x, dz = c.z - p.z, dd = dx * dx + dz * dz;
                    if (dd >= bestD || !_Contains(c)) continue;
                    bestD = dd; best = c;
                }
            }
            return best;
        }

        /// <summary>A random region cell with minDepth <= depth <= maxDepth (48 draws); y = NoBed when none was found.</summary>
        public Vector3 _RandomWetCell(float minDepth, float maxDepth)
        {
            if (!_Ready()) return new Vector3(0f, NoBed, 0f);
            int n = bed.Length;
            for (int t = 0; t < 48; t++)
            {
                int i = Random.Range(0, n);
                float b = bed[i];
                if (b <= NoBed + 1f) continue;
                int ix = i % gridNx, iz = i / gridNx;
                float z = gridZ0 + iz * gridStep;
                float d = riverData._SurfaceY(z) - b;
                if (d < minDepth || d > maxDepth) continue;
                Vector3 c = new Vector3(gridX0 + ix * gridStep, b, z);
                if (_Contains(c)) return c;
            }
            return new Vector3(0f, NoBed, 0f);
        }

        public bool _NearEntry(Vector3 p)
        {
            Vector3 d = entry - p; d.y = 0f;
            return d.magnitude <= entryRadius;
        }
    }
}
