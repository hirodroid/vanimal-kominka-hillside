using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-PRG-004 Phase 1: **Live 中の夜。** 設計の正本は
    /// [DES-PRG-002](../../../docs/development/issues/DES-PRG-002-particle-live-player.md) §4.3。
    ///
    /// **2026-09-06 ユーザー決定で単純化**: 暗くなるかならないかだけ。層の制限 (アバターだけ明るく残す) は廃止。
    /// 画面いっぱいの板 (KD_NightOverlay。乗算) を renderQueue 4999 で描き、**粒 (5000) 以外の全部**を暗くする。
    /// 代わりに**暗さの上限を作品ごとに持つ** (`KD_LiveSystem.CurrentDarkness`)。
    ///
    /// **暗さは同期時計の純関数** (Codex 指摘 2026-09-06): `KD_LiveSystem` の `Elapsed` から
    /// 「フェードイン 3 秒 → 上限 → 終了後 3 秒でフェードアウト」を毎フレーム導出する。
    /// 途中入室でもその時点の暗さから始まる (Timeline と順序が逆転しない)。**同期変数 0 本**。
    ///
    /// 木と塀は「暗くなり切ってから」`Renderer.enabled = false` (GameObject は消さない —
    /// 外壁の MeshCollider が要る)。明け方は暗さが戻り始めた瞬間に戻す。
    ///
    /// **空は材質を差し替えない** ([IMP-ENV-010](../../../docs/development/issues/IMP-ENV-010-skybox-build-budget.md))。
    /// `KD_Skybox.mat` は `Skybox/Cubemap` = `texCUBE(_Tex) * _Tint * _Exposure` の**単純な乗算**なので、
    /// `_Exposure` を落とすだけで**テクスチャに焼き込まれた太陽も同じ倍率で沈む** (月として残る)。
    /// 夜用の 2 枚目の空を持たないぶん、ビルドサイズが減る。**駆動値は覆いと同じ `Darkness`** —
    /// 暗さの上限が低い作品では空も浅くしか沈まない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_LiveNight : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/Live System が設定する)")]
        public KD_LiveSystem live;

        [Tooltip("Live 中に消す Renderer (庭の木 KD_SeasonTree (幹 + 葉) + 外壁 5 つ。桜があれば Mesh + 花びらの ParticleSystemRenderer も)。GameObject は消さない")]
        public Renderer[] hideRenderers;

        [Tooltip("Live 中に止める ParticleSystem (桜の花びら。桜が無ければ空)。StopEmittingAndClear で残粒も消す")]
        public ParticleSystem[] hideParticles;

        [Tooltip("空の材質 (KD_Skybox.mat == RenderSettings.skybox)。露出と色味をここへ書いて暗くする")]
        public Material skyMaterial;

        [Tooltip("夜の覆い (画面いっぱいの板)。KD_NightOverlay の材質を持つ")]
        public Renderer overlayRenderer;

        [Tooltip("手元メニューの設定 (KD_MenuSettings)。SleepKit ナイトモードの明るさを読み、Live の夜との積が下限を割らないよう暗さを縮める (破壊的監査 v4 FUNC-09/BUG-02)。未配線なら縮めない")]
        public KD_MenuSettings menuSettings;

        /// <summary>覆いの renderQueue。粒 (5000) より下、それ以外 (UI 4000 / アバター / 世界) より上。</summary>
        public const int OverlayQueue = 4999;

        /// <summary>この割合 (上限に対する) を越えたら木と塀を消す。戻るときは下回った瞬間に戻す。</summary>
        const float HideThreshold = 0.97f;

        /// <summary>昼の露出 (`KD_Skybox.mat` の既定値)。Setup と Verify が同じ値を見る。</summary>
        public const float DaySkyExposure = 1f;

        /// <summary>夜の露出。空は覆いの下なので、覆いと二重に暗くなる — 控えめに落とす。</summary>
        public const float NightSkyExposure = 0.15f;

        // 色味 `_Tint`。**UdonSharp は const 以外の static を持てない**ので Color ではなく成分で置く。
        // 昼 = `Skybox/Cubemap` の既定 (中立)。夜 = 青だけ残して赤緑を引く (露出と違って**色を変える**ぶん)
        public const float DayTintR = 0.5f;
        public const float DayTintG = 0.5f;
        public const float DayTintB = 0.5f;
        const float NightTintR = 0.30f;
        const float NightTintG = 0.34f;
        const float NightTintB = 0.50f;
        /// <summary>`_Tint` のアルファ。昼夜で変えない (`Skybox/Cubemap` の既定)。</summary>
        public const float SkyTintA = 0.5f;

        // ---- SleepKit ナイトモードとの積 (破壊的監査 v4 FUNC-09/BUG-02) ----
        //
        // SleepKit の覆い (RBS NightMode.shader) は GrabPass の結果に pow(明るさ, 2.5) を掛け、こちらの覆いは
        // lerp(1, _Tint, 暗さ) を掛ける。**2 枚は独立に画面へ掛かるので積になる** — ナイトモード 100% (明るさ 0.4 = 0.10 倍)
        // に stageDarkness 1 の作品を重ねると 0.02〜0.04 倍で、手元メニュー (queue 4000、両方の下) も読めない。
        // BUG-ENV-002 の下限 0.4 は SleepKit 単独にしか効かないので、ここで**積が「ナイトモード 100% 単独」を下回らない**
        // ように Live の暗さを縮める。演出値 (stageDarkness) は触らない。
        /// <summary>SleepKit NightMode.shader の減光指数 (pow(_Brightness, 2.5))。</summary>
        const float SleepKitGamma = 2.5f;
        /// <summary>SleepKit はこの明るさ以上では覆いを描かない (shader の _Brightness < 0.95)。</summary>
        const float SleepKitBypass = 0.95f;
        /// <summary>覆いの乗算係数の最小成分 (KD_NightOverlay.shader の _Tint R)。暗さ 1 で画面は少なくともこの割合まで落ちる。</summary>
        public const float OverlayTintMin = 0.22f;

        private float _phase = -1f;     // 今の進み 0〜1 (-1 = 未適用)。暗さ = _phase × 作品の上限 (SleepKit との積の下限で頭打ち)
        private float _sleepFactor = 1f; // 適用済みの SleepKit 減光係数 (1 = ナイトモード OFF)
        private bool _hidden;
        private Material _overlayMat;   // renderer.material の複製 (共有アセットを書かない)

        /// <summary>今の暗さ (0 = 昼)。覆いと空の駆動値。SleepKit ナイトモードとの積が下限を割らない範囲に縮めてある。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public float Darkness
        {
            get
            {
                if (_phase < 0f) return 0f;
                float d = _phase * (live != null ? live.CurrentDarkness : 1f);
                return Mathf.Min(d, MaxDarknessUnderSleepKit(_sleepFactor));
            }
        }

        /// <summary>SleepKit の減光係数 s と掛け合わせても画面が「ナイトモード 100% 単独」(pow(0.4, 2.5) ≈ 0.10) を
        /// 下回らない暗さの上限。s がその下限以下 (= ナイトモード 100%) なら Live の夜を重ねない (0)。</summary>
        // net: callable-ok: 純関数 (Verify の自己検査が呼ぶ)
        public float MaxDarknessUnderSleepKit(float s)
        {
            float floorF = Mathf.Pow(KD_MenuSettings.MinNightBrightness, SleepKitGamma);
            if (s <= floorF) return 0f;
            float minFactor = floorF / s;                 // 覆いに許される乗算係数の下限
            if (minFactor <= OverlayTintMin) return 1f;   // 制限不要 (ナイトモード OFF 〜 浅い)
            return Mathf.Clamp01((1f - minFactor) / (1f - OverlayTintMin));
        }

        /// <summary>今の SleepKit 減光係数。未配線 / 明るさ 0.95 以上 (覆いを描かない) は 1。
        /// **Android / iOS は常に 1** — SleepKit の覆いは GrabPass で、モバイルでは描かれない (ナイトモードは効いていない) ので、
        /// 存在しない減光を補償して Live の夜だけを浅くしない (KD_MenuSettings.RadioAvailable と同じコンパイル時ゲート)。</summary>
        private float SleepFactorNow()
        {
            float factor = 1f;
#if UNITY_ANDROID || UNITY_IOS
            return factor;
#else
            if (menuSettings == null) return factor;
            float b = Mathf.Clamp01(menuSettings.NightBrightness());
            factor = b >= SleepKitBypass ? 1f : Mathf.Pow(b, SleepKitGamma);
            return factor;
#endif
        }

        void Start()
        {
            if (live == null)
                Debug.LogError("[KD_LiveNight] live が未配線 - 夜にならない。Run Tools/KominkaDogRun/Live System setup.");
            if (overlayRenderer == null)
                Debug.LogError("[KD_LiveNight] overlayRenderer が未配線 - 夜にならない。Run Tools/KominkaDogRun/Live System setup.");
            else
            {
                _overlayMat = overlayRenderer.material;   // 複製 (vrc-udonsharp §1)
                _overlayMat.renderQueue = OverlayQueue;
                overlayRenderer.enabled = false;
            }
            if (skyMaterial == null)
                Debug.LogError("[KD_LiveNight] skyMaterial が未配線 - 空が暗くならない。Run Tools/KominkaDogRun/Live System setup.");
            // **現在の状態から出力を作り直す** (§6 の冪等)。途中入室ならこの時点で夜になる
            _sleepFactor = SleepFactorNow();
            Apply(DerivedPhase());
        }

        void Update()
        {
            float d = DerivedPhase();
            float s = SleepFactorNow();   // Live 中にナイトモードを動かしても追随する
            if (d == _phase && s == _sleepFactor) return;
            _sleepFactor = s;
            Apply(d);
        }

        /// <summary>同期時計から進み (0〜1) を導く。**ローカルの状態を 1 つも読まない。**</summary>
        private float DerivedPhase()
        {
            if (live == null || live.CurrentLiveId < 0) return 0f;
            float t = live.Elapsed;
            float nightIn = KD_LiveSystem.NightInSeconds;
            float end = nightIn + live.CurrentDuration;
            if (t < 0f) return 0f;
            if (t < nightIn) return nightIn > 0.01f ? t / nightIn : 1f;
            if (t < end) return 1f;
            float outSec = KD_LiveSystem.NightOutSeconds;
            float o = outSec > 0.01f ? (t - end) / outSec : 1f;
            return o >= 1f ? 0f : 1f - o;
        }

        private void Apply(float phase)
        {
            _phase = phase;

            // 木と塀: 暗くなり切ったら消す / 明け始めたら戻す
            bool wantHidden = phase >= HideThreshold;
            if (wantHidden != _hidden) { _hidden = wantHidden; ApplyHide(); }

            ApplySky();
            ApplyOverlay();
        }

        // ============================================================

        private void ApplyHide()
        {
            bool show = !_hidden;
            if (hideRenderers != null)
                for (int i = 0; i < hideRenderers.Length; i++)
                    if (hideRenderers[i] != null) hideRenderers[i].enabled = show;
            if (hideParticles != null)
                for (int i = 0; i < hideParticles.Length; i++)
                {
                    ParticleSystem p = hideParticles[i];
                    if (p == null) continue;
                    // 引数なしの Stop は放出を止めるだけで粒が寿命まで残る (Codex 指摘) → 残粒も消す
                    if (show) p.Play(); else p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
        }

        /// <summary>
        /// 空を暗くする。**材質は差し替えない** — 同じ `KD_Skybox.mat` の `_Exposure` / `_Tint` を書く。
        /// 覆いと同じ `Darkness` で動かすので、0.5 の段差が無く連続に沈む。
        /// </summary>
        private void ApplySky()
        {
            if (skyMaterial == null) return;
            float d = Darkness;
            skyMaterial.SetFloat("_Exposure", Mathf.Lerp(DaySkyExposure, NightSkyExposure, d));
            skyMaterial.SetColor("_Tint", new Color(
                Mathf.Lerp(DayTintR, NightTintR, d),
                Mathf.Lerp(DayTintG, NightTintG, d),
                Mathf.Lerp(DayTintB, NightTintB, d),
                SkyTintA));
        }

        private void ApplyOverlay()
        {
            if (overlayRenderer == null || _overlayMat == null) return;
            float dark = Darkness;
            bool on = dark > 0.001f;
            if (on) _overlayMat.SetFloat("_Darkness", dark);
            if (overlayRenderer.enabled != on) overlayRenderer.enabled = on;
        }
    }
}
