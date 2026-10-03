using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-CAM-001: 決定的瞬間の写真を 1 枚確定させる。
    /// KanjiFind KF_PhotoCapture の移植 (テロップ帯は M5 まで持たない構成に変更。
    /// メソッドはローカル専用 public の `_` 規約に合わせて改名)。
    ///
    /// **Udon からディスクへは書けず、画像の永続化もできない。** 持ち帰りは
    /// 「プレイヤー自身の VRChat カメラで撮ってもらう」一本で、構図合わせは
    /// KD_PhotoPrint.shader が描画側で解決する。このスクリプトの責務は
    /// **静止画を 1 枚確定させることだけ**。
    ///
    /// **カメラを無効化することで RenderTexture の内容が静止する。**
    /// RenderTexture は描かれなくなっても最後の内容を保持し続けるため、
    /// 「1 フレーム描いて止める」だけで静止画になる。
    /// **有効化と同じフレームで無効化すると何も描かれない** —
    /// SendCustomEventDelayedFrames で 1 フレーム跨ぐ。
    ///
    /// **描き先は共有の撮影用 RT 1 枚で、確定後にスロットへ色だけ写す** (IMP-GAL-003)。
    /// カメラが描くには深度バッファが要るが、それが要るのは**描いている一瞬だけ**で、
    /// 撮り終えたスロットはただの表示用テクスチャ。スロットへ直接描いていた頃は
    /// 全枚数が深度を抱えており (1280x720 の深度ステンシルは 7.03 MiB/枚)、
    /// **20 枚中 19 枚が死に荷重**だった。深度付きを 1 枚に集約すると
    /// 24 枚でも RT 総量は約 95 MiB (旧 20 枚の 211 MiB の半分以下) に収まる。
    ///
    /// どのスロットへ確定するかは呼び出し側 (KD_PhotoDirector) が _CaptureTo で渡す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_PhotoCapture : UdonSharpBehaviour
    {
        [Tooltip("撮影用カメラ。姿勢は KD_PhotoDirector が撮影ごとに設定する")]
        public Camera photoCamera;
        [Tooltip("撮影用 RT (深度付き。全撮影で共有する 1 枚)。カメラは常にここへ描く")]
        public RenderTexture captureTexture;
        public KD_PhotoSettings settings;
        // 持ち帰り台 (printRoot) は 2026-08-11 に廃止 — 板から直接持ち帰れるため不要。
        // 未撮影の板はギャラリー側 (KD_GalleryController) が非表示にしている

        // 裏山改変 (IMP-WLD-005): 撮れたことを並べ直し役 (KH_PhotoAlbum) へ知らせる。型は U# 一般にして、
        // KH_PhotoAlbum の Program Asset が無い段階でもこのファイルがコンパイルできるようにする。null なら知らせない (古民家と同じ)
        [Tooltip("撮れた直後に _OnPhotoCaptured を送る先 (裏山: KH_PhotoAlbum)。Photo/1 が配線")]
        public UdonSharpBehaviour capturedListener;

        RenderTexture _pendingSlot;
        RenderTexture _lastSlot;

        /// <summary>裏山改変 (IMP-WLD-005): 最後に色を写し終えたスロット (撮れた写真の RT)。KH_PhotoAlbum が並び替えに読む。</summary>
        public RenderTexture LastSlot { get { return _lastSlot; } }

        /// <summary>今すぐ撮れるか。**スロットを確保する前に**これを見ること —
        /// 逆にすると、撮れないのにスロットだけ消費されて
        /// 「黒い板が撮影済みとして表示される」(Codex 指摘・高)。</summary>
        public bool CanCapture
        {
            get { return photoCamera != null && captureTexture != null && !photoCamera.enabled && (settings == null || settings.CaptureAllowed); }
        }

        void Start()
        {
            if (photoCamera != null)
            {
                photoCamera.targetTexture = captureTexture;
                photoCamera.enabled = false;
            }
            if (captureTexture == null)
                Debug.LogError("[KD_PhotoCapture] captureTexture 未配線. Run Tools/KominkaDogRun/Photo System setup.");
        }

        /// <summary>撮影を要求する。いつ・どこを撮るかは KD_PhotoDirector の責務 —
        /// ここは呼ばれて撮るだけ。撮り終えたら slot へ色を写す。</summary>
        public bool _CaptureTo(RenderTexture slot)
        {
            if (!CanCapture || slot == null) return false;
            _pendingSlot = slot;
            // 毎回入れ直す — 他所が targetTexture を触っていてもスロットへ直接描かせない
            photoCamera.targetTexture = captureTexture;
            photoCamera.enabled = true;
            SendCustomEventDelayedFrames("_FinishCapture", 1);
            return true;
        }

        /// <summary>1 フレーム描き終えたので止め、スロットへ色を確定させる。
        /// 以後スロット RT の内容は静止画として残る。</summary>
        public void _FinishCapture()
        {
            if (photoCamera != null) photoCamera.enabled = false;
            bool copied = false;
            if (_pendingSlot != null && captureTexture != null)
            {
                VRCGraphics.Blit(captureTexture, _pendingSlot);
                _lastSlot = _pendingSlot;
                copied = true;
            }
            _pendingSlot = null;
            // 裏山改変 (IMP-WLD-005): 色が入ってから知らせる (並び替えた先で前の写真が 1 フレーム出ない)
            if (copied && capturedListener != null) capturedListener.SendCustomEvent("_OnPhotoCaptured");
        }
    }
}
