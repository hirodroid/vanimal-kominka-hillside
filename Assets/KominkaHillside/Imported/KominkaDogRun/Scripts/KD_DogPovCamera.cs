using UdonSharp;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaDogRun
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_DogPovCamera : UdonSharpBehaviour
    {
        public KD_ShopItem item;
        public KD_Language language;
        public Camera povCamera;
        public Transform[] heads, roots;
        public Quaternion[] headCorrections;
        public float[] forwardOffsets;
        public string[] dogNameKeys;
        public RenderTexture desktopTexture, mobileTexture;
        public Renderer screen;
        public GameObject monitor;
        public VRC_Pickup pickup;
        public TMP_Text dogLabel;
        /// <summary>モニター右上の [×] (IMP-CAM-008)。onClick → _Close。Verify とハーネスの取っ手で、実行時は読まない。</summary>
        public Button closeButton;
        public int selectedDog;
        public bool followView;
        private bool _open;
        private bool _mobile;
        private bool _rendering;
        private float _nextFrame;
        private Material _screenMaterial;
        public bool IsOpen { get { return _open; } } // net: callable-ok: read-only local monitor state
        private void Start()
        {
#if UNITY_ANDROID || UNITY_IOS
            _mobile = true;
#endif
            povCamera.enabled = false;
            povCamera.targetTexture = _mobile ? mobileTexture : desktopTexture;
            _screenMaterial = screen.material;
            _screenMaterial.mainTexture = povCamera.targetTexture;
            monitor.SetActive(false);
        }
        public void _Show(int dog)
        {
            if (item == null || !item.Owned() || heads == null || dog < 0 || dog >= heads.Length || heads[dog] == null) return;
            selectedDog = dog; _open = true; monitor.SetActive(true); _Recall();
        }
        public void _Recall()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!_open || !Utilities.IsValid(lp)) return;
            if (pickup != null && pickup.IsHeld) pickup.Drop();
            var head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 forward = head.rotation * Vector3.forward;
            monitor.transform.position = head.position + forward * .85f - Vector3.up * .18f;
            monitor.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
        public void _Close()
        {
            _open = false; _rendering = false; povCamera.enabled = false;
            if (pickup != null && pickup.IsHeld) pickup.Drop();
            monitor.SetActive(false);
        }
        public void _ToggleView() { followView = !followView; }
        public override void PostLateUpdate()
        {
            if (_rendering) { povCamera.enabled = false; _rendering = false; }
            if (!_open) return;
            if (item == null || !item.Owned() || !monitor.activeInHierarchy || roots[selectedDog] == null || !roots[selectedDog].gameObject.activeInHierarchy)
            { _Close(); return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (dogLabel != null) dogLabel.text = language._Text(dogNameKeys[selectedDog]);
            if (Time.time < _nextFrame) return;
            var playerHead = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 toMonitor = monitor.transform.position - playerHead.position;
            if (toMonitor.sqrMagnitude > 100f || Vector3.Dot(playerHead.rotation * Vector3.forward, toMonitor.normalized) < .3f) return;
            Quaternion rotation = heads[selectedDog].rotation * headCorrections[selectedDog];
            Vector3 offset = followView ? new Vector3(0, .35f, -.8f) : new Vector3(0, .03f, forwardOffsets[selectedDog]);
            povCamera.transform.SetPositionAndRotation(heads[selectedDog].position + rotation * offset, rotation);
            povCamera.enabled = true; _rendering = true; _nextFrame = Time.time + (_mobile ? .125f : .033f);
        }
        private void OnDisable() { if (povCamera != null) povCamera.enabled = false; }
    }
}
