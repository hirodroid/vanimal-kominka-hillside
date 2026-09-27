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
    //   grid    : riverbed height every 0.5 m (bed, gridNx * gridNz, row-major in z); -1000 = outside the region.
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

        public bool _NearEntry(Vector3 p)
        {
            Vector3 d = entry - p; d.y = 0f;
            return d.magnitude <= entryRadius;
        }
    }
}
