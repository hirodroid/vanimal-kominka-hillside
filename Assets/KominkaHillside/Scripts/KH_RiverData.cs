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
    }
}
