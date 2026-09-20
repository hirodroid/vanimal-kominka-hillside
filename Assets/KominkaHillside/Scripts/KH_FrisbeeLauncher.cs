using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-DOG-005 prototype only: Interact repeats one fixed throw (or just puts the disc back on the
    // stand when recallOnly), so flight, timing and the four poses can be tuned against the same throw.
    // Ownership of the disc is requested first and the write happens once it has arrived (KH_ToyRecall).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_FrisbeeLauncher : UdonSharpBehaviour
    {
        public KH_Frisbee frisbee;
        public KH_FrisbeeCatch catcher;
        public Transform muzzle;
        public bool recallOnly;
        [Header("Fixed throw (provisional)")]
        public float speed = 8f;
        public float elevationDeg = 10f;
        public float sideDeg = 0f;
        private bool _pending;
        private float _requestAt;

        public override void Interact()
        {
            if (frisbee == null || muzzle == null) return;
            if (frisbee.IsHeldNow) { Debug.Log("[KH_FrisbeeLauncher] refused: disc is held"); return; }
            if (catcher != null && (catcher.IsCarrying || catcher.IsAirborne)) { Debug.Log("[KH_FrisbeeLauncher] refused: dog is carrying or in the air"); return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            GameObject go = frisbee.gameObject;
            if (!Networking.IsOwner(go)) Networking.SetOwner(lp, go); // net: setowner-ok: disc only; write waits for ownership
            _pending = true; _requestAt = Time.time;
        }

        void Update()
        {
            if (!_pending) return;
            if (Time.time - _requestAt > 4f) { _pending = false; Debug.Log("[KH_FrisbeeLauncher] ownership timed out"); return; }
            if (!Networking.IsOwner(frisbee.gameObject)) return;
            _pending = false;
            if (frisbee.IsHeldNow || (catcher != null && (catcher.IsCarrying || catcher.IsAirborne))) return;
            frisbee._Recall(); // ends the current throw for the dog too (review U5)
            frisbee.transform.position = muzzle.position;
            frisbee.transform.rotation = muzzle.rotation;
            if (recallOnly) return;
            Vector3 dir = Quaternion.AngleAxis(sideDeg, Vector3.up) * Quaternion.AngleAxis(-elevationDeg, muzzle.right) * muzzle.forward;
            if (!frisbee._Launch(muzzle.position, dir.normalized * speed)) Debug.Log("[KH_FrisbeeLauncher] launch refused");
        }
    }
}
