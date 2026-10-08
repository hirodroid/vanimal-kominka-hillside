using UdonSharp;
using UnityEngine;
namespace KominkaHillside
{
    /// <summary>Local visual only: rigid telescopic sleeves follow the displayed table, outside its lift and scale.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WashSupportVisual : UdonSharpBehaviour
    {
        public BoxCollider tableCollider;
        public SkinnedMeshRenderer sleeves;
        public int extendIndex;
        public float strokeMeters = .9f;
        public float baseLocalY;
        public float mountBelowCollider;
        private void LateUpdate() { _RefreshVisual(); }

        public void _RefreshVisual()
        {
            if (tableCollider == null || sleeves == null || transform.parent == null || strokeMeters <= 0f) return;
            Vector3 bottom = tableCollider.center;
            bottom.y -= tableCollider.size.y * .5f;
            Vector3 mount = tableCollider.transform.TransformPoint(bottom);
            mount.y -= mountBelowCollider;
            Vector3 target = transform.parent.InverseTransformPoint(mount);
            transform.localPosition = new Vector3(target.x, baseLocalY, target.z);
            sleeves.SetBlendShapeWeight(extendIndex, Mathf.Clamp01((target.y - baseLocalY) / strokeMeters) * 100f);
        }
    }
}
