using UdonSharp;
using UnityEngine;

namespace KominkaDogRun
{
    /// <summary>
    /// BUG-CAM-003: ギャラリー板 1 枚の Interact。タップ / Use で
    /// <see cref="KD_PhotoViewer"/> にこの板の写真を大きく出す。
    ///
    /// **なぜ復活させたか** — 板の Interact は 2026-08-11 に一度廃止した
    /// (持ち帰り台の表示切替が仕事で、板から直接持ち帰れるなら不要だった)。
    /// iOS では吸着 (写真カメラのパスで板を全画面化する方式) が**真っ黒**になり、
    /// 実機 3 回の切り分けでシェーダー側からは手が出せないと確定したため、
    /// **カメラのパスに依存しない持ち帰り**としてタップ → 大きく表示 → 端末の
    /// スクリーンショット、の導線を足した。PC の吸着はそのまま残る (両立)。
    ///
    /// Interact は SDK コールバックなので板ごとに Collider + UdonBehaviour が要る
    /// (1 枚の Controller に集約できない)。すべてローカル。
    ///
    /// BUG-CAM-004: **スマホ以外では Interact を出さない。** ビューアは頭の前に黒背景ごと出るので
    /// VR では視界を乗っ取る。判定は <see cref="KD_PhotoViewer._TouchDevice"/> に一本化し、
    /// 板は Start で <c>DisableInteractive</c> を立てて「大きく見る」の表示ごと消す
    /// (ビューア側も <c>_Open</c> で弾くので、表示が消え損ねても開かない)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_PhotoBoard : UdonSharpBehaviour
    {
        /// <summary>Interact の表示文言。Setup が UdonBehaviour.interactText へ書く。</summary>
        public const string InteractWord = "大きく見る";

        [Tooltip("大きく表示する先 (KD_PhotoSystem 直下の KD_PhotoViewer)。Setup が配線する")]
        public KD_PhotoViewer viewer;
        [Tooltip("この板が映すスロット番号 (= KD_GalleryController.slotTextures の添字)")]
        public int slotIndex;

        void Start()
        {
            // 配線が無ければ開けるものが無い → こちらも消す
            if (viewer == null || !viewer._TouchDevice()) DisableInteractive = true;
        }

        public override void Interact()
        {
            if (viewer != null) viewer._Open(slotIndex);
        }
    }
}
