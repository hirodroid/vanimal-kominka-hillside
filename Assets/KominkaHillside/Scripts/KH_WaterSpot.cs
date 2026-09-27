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
        public float entryRadius = 12f;    // B-1: a release within this of the entry starts the first water play
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
        public float swimBobHz = DefaultSwimBobHz;

        public bool _Ready()
        {
            return riverData != null && polyX != null && polyZ != null && polyX.Length >= 3 && polyZ.Length == polyX.Length
                && exits != null && returns != null && exits.Length > 0 && returns.Length == exits.Length
                && bed != null && gridNx >= 2 && gridNz >= 2 && bed.Length == gridNx * gridNz;
        }

        public bool _Contains(Vector3 p)
        {
            if (!_Ready()) return false;
            bool inside = false;
            int n = polyX.Length;
            int j = n - 1;
            for (int i = 0; i < n; i++)
            {
                float zi = polyZ[i], zj = polyZ[j];
                if ((zi > p.z) != (zj > p.z))
                {
                    float x = polyX[i] + (p.z - zi) * (polyX[j] - polyX[i]) / (zj - zi);
                    if (p.x < x) inside = !inside;
                }
                j = i;
            }
            return inside;
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
