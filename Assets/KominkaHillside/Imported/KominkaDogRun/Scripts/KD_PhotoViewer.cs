using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// BUG-CAM-003: 写真を**自分の目の前に大きく出す**ビューア (案 B)。
    ///
    /// 一文で: 板をタップすると、その写真が頭の前 <see cref="distance"/> m に黒い背景ごと出る。
    /// 端末のスクリーンショットで持ち帰る。もう一度タップ (どこでも) で閉じる。
    ///
    /// ## なぜこの形か
    ///
    /// - **カメラのパスに依存しない。** iOS では写真カメラのパスで板を全画面化する吸着が
    ///   真っ黒になる (原因はシェーダーの外)。頭に追従する普通のジオメトリなら、
    ///   通常の視界に写るものは何でも端末のスクリーンショットに写る
    /// - **黒い背景を後ろに敷く** — 完了条件「写真の外に背景が透けない」。
    ///   端末の画面比 (19.5:9 等) と写真 (16:9) は必ずずれるので、余白は黒で埋める
    /// - **頭に追従させる** (PostLateUpdate。IK の後なので VR でも揺れない)。
    ///   板の位置に大きく出すと、壁に埋まる / 隣の人の視界を塞ぐ
    ///
    /// ## 他人には見えない
    ///
    /// GameObject の active はローカルなので、開いた本人のクライアントでしか描かれない。
    /// 自分のワールド撮影カメラ (KD_PhotoDirector) に写り込まないよう、描画物は
    /// **UI レイヤー (5)** に置く (撮影カメラの許可リストに UI は無い — BUG-CAM-001)。
    /// 当たり判定は Interact の光線が確実に当たる Default レイヤーの **Trigger** に置く
    /// (Trigger でないと、頭に貼り付いたコライダーに自分の体が押される)。
    ///
    /// すべてローカル (BehaviourSyncMode.None、同期変数ゼロ)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_PhotoViewer : UdonSharpBehaviour
    {
        /// <summary>閉じる Interact の表示文言。Setup が UdonBehaviour.interactText へ書く。</summary>
        public const string InteractWord = "閉じる";

        [Header("配線 (Tools/KominkaDogRun/Photo System が設定する)")]
        [Tooltip("スロット RT (KD_GalleryController.slotTextures と同じ並び)")]
        public RenderTexture[] slotTextures;
        [Tooltip("写真を出す Quad の Renderer (UI レイヤー)")]
        public Renderer photoRenderer;
        [Tooltip("背景 + 写真の親。開いている間だけ active")]
        public GameObject panelRoot;
        [Tooltip("閉じる用の Trigger (この GameObject に付いている)。開いている間だけ有効")]
        public Collider closeCollider;

        [Tooltip("手元メニュー (IMP-TRT-006)。開いている間は画面端の [メニュー] を隠してもらう — " +
                 "スマホの持ち帰りは端末スクショなので、Screen-Space Overlay が写る。Photo System/2 が配線")]
        public KD_TreatPouch pouch;
        [Tooltip("頭からの距離 (m)")]
        public float distance = 0.8f;

        Material _photoMat;
        bool _open;
        VRCPlayerApi _lp;

        void Start()
        {
            if (slotTextures == null || slotTextures.Length == 0 || photoRenderer == null
                || panelRoot == null || closeCollider == null)
                Debug.LogError("[KD_PhotoViewer] wiring incomplete. Run Tools/KominkaDogRun/Photo System setup.");
            // **共有マテリアルを触らない** — インスタンス化して自分の分だけ差し替える
            if (photoRenderer != null) _photoMat = photoRenderer.material;
            _lp = Networking.LocalPlayer;
            _Close();
        }

        /// <summary>
        /// BUG-CAM-004: このビューアを出してよい端末か = **スマホ (Android / iOS ビルドで、VR ではない)** だけ。
        ///
        /// 頭の前に黒い背景ごと出すので、**VR では視界が丸ごと乗っ取られる** (2026-09-05 PCVR で
        /// 板を Use した人の視界が塞がれた)。PC の持ち帰りは吸着 (IMP-GAL-002) があるので、
        /// この導線は「写真カメラのパスが真っ黒になるスマホ」(BUG-CAM-003) にだけ要る。
        ///
        /// - define で決めるのは**フラグだけ** (本体は全プラットフォームでコンパイルさせる — KD_GalleryController と同じ理由)
        /// - Android は Quest とスマホの両方 → **<c>IsUserInVR()</c> で Quest を外す**
        ///   (見たいのは入力装置ではなく「表示が頭に付いているか」なので、この 1 軸で足りる)
        /// - LocalPlayer が取れないときは **出さない側** へ倒す (視界を塞ぐ方が害が大きい)
        /// </summary>
        public bool _TouchDevice()
        {
            bool mobile = false;
#if UNITY_ANDROID || UNITY_IOS
            mobile = true;
#endif
            if (!mobile) return false;
            VRCPlayerApi lp = Networking.LocalPlayer;
            return Utilities.IsValid(lp) && !lp.IsUserInVR();
        }

        /// <summary>この写真を大きく出す (KD_PhotoBoard が呼ぶ)。範囲外は無視。
        /// スマホ以外では何もしない (<see cref="_TouchDevice"/>。BUG-CAM-004)。</summary>
        public void _Open(int slot)
        {
            if (!_TouchDevice()) return;
            if (slotTextures == null || slot < 0 || slot >= slotTextures.Length) return;
            RenderTexture rt = slotTextures[slot];
            if (rt == null || panelRoot == null) return;
            if (_photoMat != null) _photoMat.mainTexture = rt;
            _open = true;
            Follow();   // 出した瞬間から頭の前に居る (1 フレーム古い位置で出さない)
            panelRoot.SetActive(true);
            if (closeCollider != null) closeCollider.enabled = true;
            // IMP-TRT-006: 持ち帰りは端末スクショなので、画面端の [メニュー] を写さない
            if (pouch != null) pouch._SuppressLauncher();
        }

        /// <summary>閉じる。Interact (どこをタップしても) と Start が呼ぶ。</summary>
        public void _Close()
        {
            _open = false;
            if (panelRoot != null) panelRoot.SetActive(false);
            if (closeCollider != null) closeCollider.enabled = false;
            if (pouch != null) pouch._ReleaseLauncher();
        }

        public override void Interact()
        {
            _Close();
        }

        public override void PostLateUpdate()
        {
            if (_open) Follow();
        }

        void Follow()
        {
            if (!Utilities.IsValid(_lp))
            {
                _lp = Networking.LocalPlayer;
                if (!Utilities.IsValid(_lp)) return;
            }
            VRCPlayerApi.TrackingData head = _lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            transform.SetPositionAndRotation(
                head.position + head.rotation * Vector3.forward * distance, head.rotation);
        }
    }
}
