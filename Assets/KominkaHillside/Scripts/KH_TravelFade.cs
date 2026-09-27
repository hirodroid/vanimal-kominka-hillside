using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    /// <summary>IMP-WLD-001 段階 2: ワープの暗転 (設計書 §D.1)。ローカルだけ (同期なし)。
    ///
    /// 手順: <see cref="_Begin"/> で 0.2 秒かけて黒へ → 黒 (alpha 1) を書いたフレームの**次**から <see cref="_IsBlack"/> が true
    /// (= 黒が一度描かれている) → 呼び出し側 (KH_LeashManager.Travel.cs) が TeleportTo → <see cref="_Release"/> → 1 フレーム待って
    /// 0.3 秒で戻す。黒のまま 3 秒放っておかれたら自分で戻す (画面が黒いまま残らない床)。
    ///
    /// 板は KH_NightMode と同じ全画面の quad (KH_FullscreenQuad) に KH_TravelFade.mat。材質は Renderer.material の複製を使い、
    /// 共有アセットは書き換えない。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_TravelFade : UdonSharpBehaviour
    {
        [Header("Wiring (Tools/KominkaHillside/Course/84 writes these)")]
        public Renderer overlayRenderer;
        [Header("Values (設計書 §D.1 の提案。Play で決める)")]
        public float fadeOutSeconds = 0.2f;
        public float fadeInSeconds = 0.3f;
        public float blackTimeout = 3f;

        public const int OverlayQueue = 5001;   // ナイトモードの乗算板 (4999) とライブの粒 (5000) より上

        private Material _mat;
        private bool _ready;
        private int _state;          // 0 なし / 1 暗くしている / 2 黒 / 3 移動した・1 フレーム待ち / 4 明るくしている
        private float _alpha;
        private int _blackFrame = -1;
        private int _releaseFrame = -1;
        private float _blackAt;

        void Start()
        {
            if (overlayRenderer == null) { Debug.LogError("[KH_TravelFade] overlayRenderer is not wired; run Tools/KominkaHillside/Course/84."); return; }
            _mat = overlayRenderer.material;
            _mat.renderQueue = OverlayQueue;
            _mat.SetFloat("_Alpha", 0f);
            overlayRenderer.enabled = false;
            _ready = true;
        }

        /// <summary>黒へ向かい始める。ローカル専用。</summary>
        public void _Begin()
        {
            if (!_ready) return;
            _state = 1;
            overlayRenderer.enabled = true;
        }

        /// <summary>黒が少なくとも 1 フレーム描かれた。板が無ければ (未配線) 常に true = 暗転なしで進める。</summary>
        public bool _IsBlack()
        {
            if (!_ready) return true;
            return _state == 2 && Time.frameCount > _blackFrame;
        }

        /// <summary>移動が済んだ (または取りやめた)。1 フレーム待ってから明るくする。ローカル専用。</summary>
        public void _Release()
        {
            if (!_ready || _state == 0) return;
            _state = 3;
            _releaseFrame = Time.frameCount;
        }

        public bool _Busy() { return _ready && _state != 0; }
        public float _Alpha() { return _alpha; }

        void Update()
        {
            if (!_ready || _state == 0) return;
            if (_state == 1)
            {
                _alpha = fadeOutSeconds <= 0f ? 1f : Mathf.Min(1f, _alpha + Time.deltaTime / fadeOutSeconds);
                Apply();
                if (_alpha >= 1f) { _state = 2; _blackFrame = Time.frameCount; _blackAt = Time.time; }
                return;
            }
            if (_state == 2)
            {
                if (Time.time - _blackAt > blackTimeout) { Debug.LogWarning("[KH_TravelFade] black held too long; releasing"); _Release(); }
                return;
            }
            if (_state == 3)
            {
                if (Time.frameCount > _releaseFrame) _state = 4;
                return;
            }
            _alpha = fadeInSeconds <= 0f ? 0f : Mathf.Max(0f, _alpha - Time.deltaTime / fadeInSeconds);
            Apply();
            if (_alpha <= 0f) { _state = 0; overlayRenderer.enabled = false; }
        }

        private void Apply() { if (_mat != null) _mat.SetFloat("_Alpha", _alpha); }
    }
}
