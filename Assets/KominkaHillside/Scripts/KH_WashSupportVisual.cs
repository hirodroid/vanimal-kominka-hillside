using UdonSharp;
using UnityEngine;
namespace KominkaHillside
{
    /// <summary>Local visual only: rigid telescopic sleeves follow the displayed table, outside its lift and scale.
    /// 2026-10-12 (VER-ENV-002 perf): no LateUpdate. The table moves only through KH_WashStationCall (the lift, _StandPose: it marks
    /// the support dirty and refreshes it at the end of its own Update on the frame the stand moved) and KH_WashSelection._Apply (the
    /// platform's mount depth: refreshes at once). Nothing reads the support between that Update and LateUpdate. Start covers the
    /// first frame.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WashSupportVisual : UdonSharpBehaviour
    {
        public BoxCollider tableCollider;
        public SkinnedMeshRenderer sleeves;
        public int extendIndex;
        public float strokeMeters = .9f;
        public float baseLocalY;
        public float mountBelowCollider;
        // The last written position and weight: an unchanged result writes nothing (exact compares).
        private bool _written;
        private float _lastX, _lastY, _lastZ, _lastWeight;
        void Start() { _RefreshVisual(); }

        public void _RefreshVisual()
        {
            if (tableCollider == null || sleeves == null || transform.parent == null || strokeMeters <= 0f) return;
            Vector3 bottom = tableCollider.center;
            bottom.y -= tableCollider.size.y * .5f;
            Vector3 mount = tableCollider.transform.TransformPoint(bottom);
            mount.y -= mountBelowCollider;
            Vector3 target = transform.parent.InverseTransformPoint(mount);
            float x = target.x, z = target.z;
            float weight = Mathf.Clamp01((target.y - baseLocalY) / strokeMeters) * 100f;
            if (_written && x == _lastX && baseLocalY == _lastY && z == _lastZ && weight == _lastWeight) return;
            _written = true; _lastX = x; _lastY = baseLocalY; _lastZ = z; _lastWeight = weight;
            transform.localPosition = new Vector3(x, baseLocalY, z);
            sleeves.SetBlendShapeWeight(extendIndex, weight);
        }
    }
}
