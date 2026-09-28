using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    // IMP-DOG-020 stage 1 (DES-DOG-004 A-1): the river of the loop course, baked by the Editor
    // (Tools/KominkaHillside/Course/73 - Build River Data) from KH_CourseGroundEditor.RiverAt / HalfWidth
    // at 1 m steps in z. The generating formula is NOT ported to Udon; runtime reads these arrays with linear
    // interpolation, and Course/74 compares them with the saved water-surface meshes (Editor check 1).
    //
    // "Band" = centre ± (half width + margin). With margin 1 m it is the same strip the course NavMesh
    // excludes (KH_CourseNavigationEditor Riverbed_* volumes), measured along x like those volumes.
    // A bridge deck crosses the band: a point within the deck's z half-width of a bridge and higher than
    // surface + deckRise counts as outside the band (so a dog on the deck is not stopped, while a dog under
    // the bridge or stepping off the deck side is).
    //
    // Sync None, no synced variable: every client holds the same baked data. All public methods are local
    // (underscore) and return values only (no out parameters across behaviours).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_RiverData : UdonSharpBehaviour
    {
        public const float BandMargin = 1f;   // = NavMesh riverbed exclusion (half width + 1 m)
        public const float BankExtra = 2f;    // design C-6b: bank point = centre ± (half width + 2 m)

        // Written by the Editor build (SerializedObject + CopyProxyToUdon). Null until built.
        public float zStart;
        public float zStep = 1f;
        public float[] centerX;
        public float[] surfaceY;
        public float[] halfWidth;
        public float[] bridgeZ;
        public float bridgeHalfZ = 1.75f;     // deck BoxCollider size.z 3.5 / 2
        public float deckRise = 0.6f;         // deck top is surface + 0.9; the NavMesh exclusion top is + 0.25

        public bool _Ready()
        {
            return centerX != null && surfaceY != null && halfWidth != null && centerX.Length >= 2
                && surfaceY.Length == centerX.Length && halfWidth.Length == centerX.Length && zStep > 0.0001f;
        }

        public bool _Covers(float z)
        {
            if (!_Ready()) return false;
            return z >= zStart && z <= zStart + (centerX.Length - 1) * zStep;
        }

        float Sample(float[] a, float z)
        {
            int n = a.Length;
            float f = (z - zStart) / zStep;
            if (f <= 0f) return a[0];
            if (f >= n - 1) return a[n - 1];
            int i = (int)f;
            if (i > n - 2) i = n - 2;
            float t = f - i;
            return a[i] + (a[i + 1] - a[i]) * t;
        }

        public float _CenterX(float z) { return _Ready() ? Sample(centerX, z) : 0f; }
        public float _SurfaceY(float z) { return _Ready() ? Sample(surfaceY, z) : 0f; }
        public float _HalfWidth(float z) { return _Ready() ? Sample(halfWidth, z) : 0f; }

        /// <summary>Signed horizontal distance (x) from the band edge: negative = inside the band.</summary>
        public float _Lateral(Vector3 p, float margin)
        {
            if (!_Covers(p.z)) return 1000f;
            return Mathf.Abs(p.x - Sample(centerX, p.z)) - (Sample(halfWidth, p.z) + margin);
        }

        public bool _OnBridgeDeck(Vector3 p)
        {
            if (bridgeZ == null) return false;
            for (int i = 0; i < bridgeZ.Length; i++)
                if (Mathf.Abs(p.z - bridgeZ[i]) <= bridgeHalfZ && p.y > _SurfaceY(p.z) + deckRise) return true;
            return false;
        }

        public bool _InBand(Vector3 p, float margin)
        {
            if (_Lateral(p, margin) >= 0f) return false;
            return !_OnBridgeDeck(p);
        }

        /// <summary>Should a straight step from → to be refused? Entering the band is refused. A body that is
        /// already inside (placed, teleported, pushed) may only move outward, so it can never be trapped.</summary>
        public bool _BlocksStep(Vector3 from, Vector3 to, float margin)
        {
            if (!_InBand(to, margin)) return false;
            if (!_InBand(from, margin)) return true;
            return _Lateral(to, margin) < _Lateral(from, margin) - 0.0001f;
        }

        /// <summary>Point on the given side (+1 = +x, -1 = -x) at centre ± (half width + extra), at the water height.</summary>
        public Vector3 _BankPoint(Vector3 p, float side, float extra)
        {
            float s = side >= 0f ? 1f : -1f;
            return new Vector3(_CenterX(p.z) + s * (_HalfWidth(p.z) + extra), _SurfaceY(p.z), p.z);
        }

        // ================================================================ IMP-DOG-021 (DES-DOG-004 §E-1): river-wide fetch data
        // Baked by Tools/KominkaHillside/Course/90 - Build River Fetch Data (checked by 91). Same object as above, so no new wiring:
        // the dogs, the Fetch and KH_ToyRecall already hold this reference. Sync None; every client holds the same arrays.
        //   riverbed grid: rows every fetchStep (0.5 m) in z from fetchZ0; row r covers x = fetchRowX0[r] + i x fetchStep for
        //                  i < fetchRowStart[r + 1] - fetchRowStart[r] (centre +- (half width + BankExtra) at that z), values in
        //                  fetchBed[fetchRowStart[r] + i] (Terrain height, NoBed where there is no terrain). Linear within a row,
        //                  linear between rows (the rows do not share an x origin).
        //   bank slices  : every sliceStep (1.5 m) in z from sliceZ0; code = 2 x slice + (0 = -x side, 1 = +x side). sliceBank is
        //                  the bank NavMesh point where a dog goes in / comes out (outside the NavMesh riverbed exclusion),
        //                  sliceReturn the point behind it with a complete path from the nearest route node, sliceOk the checks of
        //                  Course/76 on that side (exit slope <= 25 deg, the path, no stone in the water on that side, outside the
        //                  bridge zone). The water-slope limit of the play spot is not applied (a ball only floats there).
        // The tuning fields are written by 90; a value that arrived as 0 (a field added to an existing scene) falls back to the
        // constant in its accessor.
        public const float NoBed = -1000f;
        public const float DefaultFetchWadeRatio = 0.85f;   // E-3: large dogs walk the channel (0.40 m) during a Fetch; 0.45 = "shallows only"
        public const float DefaultBankReach = 30f;          // E-1: an ok slice within this (z, same bridge section) or the ball goes back to the bank
        public const float DefaultBridgeZoneHalfZ = 8f;     // E-1: the bridges' no-detour zone (KH_CourseRouteEditor z +-8 m)
        public const float FloatAbove = 0.3f;               // a ball floats while its centre is below surface + this (as KH_WaterSpot._FloatsAt)
        public float fetchZ0;
        public float fetchStep = 0.5f;
        public int fetchRows;
        public float[] fetchRowX0;
        public int[] fetchRowStart;
        public float[] fetchBed;
        public float sliceZ0;
        public float sliceStep = 1.5f;
        public Vector3[] sliceBank;
        public Vector3[] sliceReturn;
        public bool[] sliceOk;
        public float fetchWadeRatio = DefaultFetchWadeRatio;
        public float bankReach = DefaultBankReach;
        public float bridgeZoneHalfZ = DefaultBridgeZoneHalfZ;

        public float _FetchWadeRatio() { return fetchWadeRatio > 0.01f ? fetchWadeRatio : DefaultFetchWadeRatio; }
        public float _BankReach() { return bankReach > 0.01f ? bankReach : DefaultBankReach; }
        public float _BridgeZoneHalfZ() { return bridgeZoneHalfZ > 0.01f ? bridgeZoneHalfZ : DefaultBridgeZoneHalfZ; }

        public bool _FetchReady()
        {
            return _Ready() && fetchRows >= 2 && fetchStep > 0.0001f && fetchRowX0 != null && fetchRowX0.Length == fetchRows
                && fetchRowStart != null && fetchRowStart.Length == fetchRows + 1 && fetchBed != null && fetchBed.Length == fetchRowStart[fetchRows]
                && sliceStep > 0.0001f && sliceBank != null && sliceReturn != null && sliceOk != null
                && sliceBank.Length == sliceOk.Length && sliceReturn.Length == sliceOk.Length && sliceOk.Length >= 2;
        }

        float RowBed(int r, float x)
        {
            int s = fetchRowStart[r], n = fetchRowStart[r + 1] - s;
            if (n < 2) return NoBed;
            float fx = (x - fetchRowX0[r]) / fetchStep;
            if (fx < 0f || fx > n - 1) return NoBed;
            int i = (int)fx;
            if (i > n - 2) i = n - 2;
            float a = fetchBed[s + i], b = fetchBed[s + i + 1];
            if (a <= NoBed + 1f || b <= NoBed + 1f) return NoBed;
            return a + (b - a) * (fx - i);
        }

        /// <summary>Riverbed height under p from the river-wide grid; NoBed outside it.</summary>
        public float _BedAt(Vector3 p)
        {
            if (!_FetchReady()) return NoBed;
            float fz = (p.z - fetchZ0) / fetchStep;
            if (fz < 0f || fz > fetchRows - 1) return NoBed;
            int r = (int)fz;
            if (r > fetchRows - 2) r = fetchRows - 2;
            float a = RowBed(r, p.x), b = RowBed(r + 1, p.x);
            if (a <= NoBed + 1f || b <= NoBed + 1f) return NoBed;
            return a + (b - a) * (fz - r);
        }

        /// <summary>Water depth at p (surface - riverbed), 0 where there is no water or no grid.</summary>
        public float _DepthAt(Vector3 p)
        {
            float b = _BedAt(p);
            if (b <= NoBed + 1f) return 0f;
            float d = _SurfaceY(p.z) - b;
            return d > 0f ? d : 0f;
        }

        /// <summary>Inside a bridge's no-detour zone (bridge z +- bridgeZoneHalfZ + extra).</summary>
        public bool _InBridgeZone(float z, float extra)
        {
            if (bridgeZ == null) return false;
            float h = _BridgeZoneHalfZ() + extra;
            for (int i = 0; i < bridgeZ.Length; i++) if (Mathf.Abs(z - bridgeZ[i]) <= h) return true;
            return false;
        }

        /// <summary>The stretch of river between bridges that z belongs to (0 below the first bridge ...).</summary>
        public int _Section(float z)
        {
            if (bridgeZ == null) return 0;
            int n = 0;
            for (int i = 0; i < bridgeZ.Length; i++) if (z > bridgeZ[i]) n++;
            return n;
        }

        /// <summary>Where a fetching dog may wade or swim: the band (centre +- (half width + 1 m)), outside the bridge zones, with
        /// riverbed data.</summary>
        public bool _FetchWater(Vector3 p)
        {
            return _InBand(p, BandMargin) && !_InBridgeZone(p.z, 0f) && _BedAt(p) > NoBed + 1f;
        }

        /// <summary>The ok bank slice nearest p (horizontal distance of its bank point) on the given side (-1 = -x, +1 = +x, 0 =
        /// either) whose z is within reach of p and in the same bridge section; except = a code to skip (-1 none). Returns the code
        /// (2 x slice + side 0/1), or -1.</summary>
        public int _NearestSlice(Vector3 p, int side, float reach, int except)
        {
            if (!_FetchReady()) return -1;
            int count = sliceOk.Length / 2;
            int c = Mathf.RoundToInt((p.z - sliceZ0) / sliceStep);
            int span = Mathf.CeilToInt(reach / sliceStep) + 1;
            int sec = _Section(p.z);
            int best = -1;
            float bestD = 1e12f;
            for (int o = 0; o <= span; o++)
            {
                if (best >= 0 && (o - 1) * sliceStep > Mathf.Sqrt(bestD)) break;   // nothing farther in z can be nearer
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    if (o == 0 && sgn > 0) continue;
                    int k = c + o * sgn;
                    if (k < 0 || k >= count) continue;
                    for (int s = 0; s < 2; s++)
                    {
                        if ((side < 0 && s == 1) || (side > 0 && s == 0)) continue;
                        int code = 2 * k + s;
                        if (code == except || !sliceOk[code]) continue;
                        Vector3 b = sliceBank[code];
                        if (Mathf.Abs(b.z - p.z) > reach || _Section(b.z) != sec) continue;
                        float dx = b.x - p.x, dz = b.z - p.z, d = dx * dx + dz * dz;
                        if (d < bestD) { bestD = d; best = code; }
                    }
                }
            }
            return best;
        }

        public Vector3 _SliceBank(int code) { return sliceBank[code]; }
        public Vector3 _SliceReturn(int code) { return sliceReturn[code]; }
        public int _SliceSide(int code) { return (code & 1) == 1 ? 1 : -1; }

        /// <summary>E-2: a ball floats here anywhere along the river: in the water (|x - centre| <= half width), outside the bridge
        /// zones, riverbed data present, not far above the water, and a bank a dog can use within reach (else the old bank return
        /// of KH_ToyRecall).</summary>
        public bool _FloatsAt(Vector3 p)
        {
            if (!_FetchReady() || !_Covers(p.z)) return false;
            if (Mathf.Abs(p.x - Sample(centerX, p.z)) > Sample(halfWidth, p.z)) return false;
            if (_InBridgeZone(p.z, 0f) || p.y >= Sample(surfaceY, p.z) + FloatAbove) return false;
            if (_BedAt(p) <= NoBed + 1f) return false;
            return _NearestSlice(p, 0, _BankReach(), -1) >= 0;
        }

        /// <summary>E-2: the drift (downstream = -z) stops before a place where the ball would no longer float: 1 m on is a
        /// bridge zone, the end of the data, or has no usable bank.</summary>
        public bool _DriftStops(Vector3 p)
        {
            float z1 = p.z - 1f;
            if (!_Covers(z1) || _InBridgeZone(z1, 0f)) return true;
            Vector3 q = new Vector3(p.x + _CenterX(z1) - _CenterX(p.z), p.y, z1);
            return _NearestSlice(q, 0, _BankReach(), -1) < 0;
        }
    }
}
