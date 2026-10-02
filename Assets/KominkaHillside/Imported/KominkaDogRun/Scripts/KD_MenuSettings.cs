using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
#if KH_WITH_SLEEPKIT
using RBS.SleepKit2.Udon;
#endif

namespace KominkaDogRun
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_MenuSettings : UdonSharpBehaviour
    {
        public KD_TreatPouch pouch;
        public KD_Language language;
        public KD_LiveAccess liveAccess;
        public GameObject radioPage;
        public GameObject informationPage;
        public GameObject aiInformationPage;
        public ScrollRect aiInformationScroll;
        public GameObject vanimalInformationPage, trainerInformationPage, photoSettingsPage;
        // IMP-INF-024: 「はじめに」は初回案内と同じ内容を、段落付きで再読する。
        public GameObject welcomeInformationPage;
        public ScrollRect welcomeInformationScroll;
        public ScrollRect vanimalInformationScroll, trainerInformationScroll;
        // 裏山改変: クレジットは設定面の頁 (古民家の KD_CreditBoard は置かない。IMP-UI-001 段階 3)。
        // 入口は設定ホームの群「読む」の [クレジット]。頁は pouch.extraSettingsPages に登録し、開閉は _OpenExtraSettings に任せる
        public GameObject creditPage;
        public ScrollRect creditScroll;
        // IMP-ENV-019: HoboRadio は PC のみ (配布元の公式回答、2026-09-18)。Android / iOS ビルドでは本体が
        // ビルド時に外され (KD_MobileStripEditor)、この Button は Start() で押せなくなる。
        // ラジオへ触る入口は全部 RadioAvailable() で先に閉じる — 外された参照は Udon が null ではなく
        // 自分自身 (UdonBehaviour / Transform) へ置き換えるので、null ガードでは足りない (UdonBehaviour.cs:319-325)。
        public Button radioButton;
        public UdonBehaviour radio;
        public AudioSource radioAudio;
        public Transform radioKnob;
        public float knobMin;
        public float knobMax;
        public Slider radioVolume;
        public TextMeshProUGUI radioState;
        public TextMeshProUGUI radioVolumeValue;
        public TextMeshProUGUI liveAccessLabel;
        public Button liveAccessButton;
        // IMP-ENV-015: SleepKit のナイトモード。タブレットの呼び出しを止めたので、手元の入口はこの Slider だけ
        // (寝室の RBS 常設操作盤にも同じ値の Slider が残る)。
        // 値の正本と PlayerData 保存は SleepKitSettingsManager (購入アセット。改変しない)。
        // Slider は「暗さ」(0 = OFF)、SleepKit 側は明るさ (1 = 昼のまま)。
        // BUG-ENV-002: SleepKit の覆いは pow(明るさ, 2.5) で減光するので明るさ 0 は真っ暗で手元メニューも見えない。
        // 暗さ 0〜1 を明るさ 1〜MinNightBrightness へ線形に写し、どの入口から下回っても (RBS 操作盤・保存値の復元)
        // 復元後の Update で下限へ戻す (ユーザー決定 2026-09-18: 真っ暗にできる経路は残さない)。
        // 0.4 は減光係数 ≈ 10% (0.4^2.5)。知覚上の明るさではないので確定値は Play で決める。
        public const float MinNightBrightness = 0.4f;
#if KH_WITH_SLEEPKIT
        public SleepKitSettingsManager sleepKit;
#endif
        public Slider nightMode;
        public TextMeshProUGUI nightModeValue;
        // 裏山改変: ナイトモードの正本は KH_NightMode (SleepKit は無い。KominkaHillside 段階 0-b、計画書 D-5)。
        // Slider の「暗さ」0〜1 をそのまま渡し、PlayerData の保存・復元も KH_NightMode が持つ (自分だけに反映)。
        public KominkaHillside.KH_NightMode hillsideNight;
        // 裏山改変 (IMP-ENV-005): ナイトモードの行に [オート][マニュアル]。選んでいる方を pouch.equipOnColor、他方を tabOffColor で塗る。
        // 注記はモードで差し替え (2 行を同じ位置に置き、片方だけ表示)。オート中のスライダーは共通の値を表示するだけで Fill を淡色にする
        // (触るとマニュアルへ切り替わる。KH_NightMode._SetDarkness)。配線は Menu/15 (KD_MenuFinishEditor.BuildNightMode)
        public UnityEngine.UI.Image nightAutoImage, nightManualImage, nightModeFill;
        public GameObject nightAutoNote, nightManualNote;
        public const float NightAutoFillAlpha = 0.4f;   // オート中の Fill の不透明度 (元の色に掛ける)
        private Color _nightFillManual;
        private bool _nightFillKnown;
        private float _nextRefresh;
        private bool _restored;

        private string L(string text) { return language != null ? language._Text(text) : text; }
        // net: callable-ok: read-only platform capability
        public bool RadioAvailable()
        {
            bool available = true;
#if UNITY_ANDROID || UNITY_IOS
            available = false;
#endif
#if !KH_WITH_HOBORADIO
            available = false;   // 裏山改変: HoboRadio を入れていない裏山ではラジオの入口を閉じる (radioPage は作らない)
#endif
            return available;
        }
        private void Start()
        {
            if (radioButton != null) radioButton.interactable = RadioAvailable();
            if (nightModeFill != null) { _nightFillManual = nightModeFill.color; _nightFillKnown = true; }
        }
        public void _OpenRadio() { if (!RadioAvailable() || radioPage == null) return; pouch._OpenExtraSettings(radioPage, "ui.extra.028"); RefreshRadio(); }
        public void _OpenInformation() { pouch._OpenExtraSettings(informationPage, "information.title"); }
        public void _OpenVanimalInformation() { OpenReading(vanimalInformationPage, vanimalInformationScroll, "information.vanimal.title"); }
        public void _OpenTrainerInformation() { OpenReading(trainerInformationPage, trainerInformationScroll, "information.trainer.title"); }
        public void _OpenPhotoSettings() { pouch._OpenExtraSettings(photoSettingsPage, "photo.settings.title"); }
        public void _OpenWelcomeInformation() { OpenReading(welcomeInformationPage, welcomeInformationScroll, "welcome.reread.title"); }
        // 裏山改変: 設定ホームの [クレジット] (見出しは ui.extra.022「クレジット」)。頁が無い段階では何もしない
        public void _OpenCredits() { if (creditPage == null) return; OpenReading(creditPage, creditScroll, "ui.extra.022"); }
        private void OpenReading(GameObject page, ScrollRect scroll, string title)
        {
            pouch._OpenExtraSettings(page, title);
            if (scroll == null) return;
            scroll.velocity = Vector2.zero; scroll.content.anchoredPosition = Vector2.zero; scroll.verticalNormalizedPosition = 1f;
        }
        public void _OpenAiInformation()
        {
            pouch._OpenExtraSettings(aiInformationPage, "information.ai.title");
            if (aiInformationScroll != null)
            {
                aiInformationScroll.velocity = Vector2.zero;
                aiInformationScroll.content.anchoredPosition = Vector2.zero;
                aiInformationScroll.verticalNormalizedPosition = 1f;
            }
        }
        public void _RadioPower() { if (!RadioAvailable()) return; if (radio != null) radio.SendCustomEvent("InteractButtonPower"); RefreshRadio(); }
        public void _RadioChannel() { if (!RadioAvailable()) return; if (radio != null) radio.SendCustomEvent("InteractSwitchChannel"); RefreshRadio(); }
        public void _RadioVolume()
        {
            if (!RadioAvailable() || radioAudio == null || radioVolume == null) return;
            float value = Mathf.Clamp01(radioVolume.value);
            radioAudio.volume = value;
            if (radioKnob != null)
            {
                Vector3 pos = radioKnob.localPosition;
                pos.z = Mathf.Lerp(knobMin, knobMax, value);
                radioKnob.localPosition = pos;
            }
            RefreshRadio();
        }
        // Max は float の丸め対策: Lerp(1, 0.4, 1) は 0.4 をわずかに下回り、Update の補正がもう 1 回 SaveSettings を呼んでしまう。
        // net: callable-ok: pure mapping (dark 0..1 -> SleepKit brightness 1..MinNightBrightness)
        public float DarkToBrightness(float dark) { return Mathf.Max(MinNightBrightness, Mathf.Lerp(1f, MinNightBrightness, Mathf.Clamp01(dark))); }
        // net: callable-ok: pure mapping (brightness below the floor reads as dark 1 = Slider 100%)
        public float BrightnessToDark(float brightness) { return Mathf.Clamp01(Mathf.InverseLerp(1f, MinNightBrightness, brightness)); }
        /// <summary>SleepKit のナイトモードの現在の明るさ (1 = 昼のまま)。KD_LiveNight が Live の夜との積を下限で止めるために読む
        /// (破壊的監査 v4 FUNC-09/BUG-02)。SleepKit 未配線なら 1。</summary>
        // net: callable-ok: read-only local setting
        public float NightBrightness()
        {
#if KH_WITH_SLEEPKIT
            return sleepKit != null ? sleepKit.GetNightModeValue() : 1f;
#else
            return 1f; // Hillside has no SleepKit night-mode service.
#endif
        }
        public void _NightMode()
        {
            // 裏山改変: KH_NightMode へ暗さ 0〜1 を渡す (保存も KH_NightMode)
            if (hillsideNight != null && nightMode != null) hillsideNight._SetDarkness(Mathf.Clamp01(nightMode.value));
#if KH_WITH_SLEEPKIT
            if (sleepKit == null || nightMode == null) return;
            sleepKit.SetNightModeValue(DarkToBrightness(nightMode.value));
#endif
            RefreshNightMode();
        }
        // 裏山改変 (IMP-ENV-005): モードの選択は自分だけ (共通の時計は変えない)。保存は KH_NightMode が持つ
        public void _NightAuto() { if (hillsideNight != null) hillsideNight._UseAuto(); RefreshNightMode(); }
        public void _NightManual() { if (hillsideNight != null) hillsideNight._UseManual(); RefreshNightMode(); }
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            // SleepKit も同じイベントで保存値を読み直す。順序は保証されないので、ここでは印だけ立てて補正は Update で行う
            // (復元前に SetNightModeValue を呼ぶと SleepKit がアラーム等も一緒に保存し、復元値と競合しうる)。
            if (player != null && player.isLocal) _restored = true;
        }
        public void _ToggleLiveAccess()
        {
            if (liveAccess == null || liveAccess.Pending) return;
            liveAccess._SetOpen(!liveAccess.IsOpenToAll);
        }
        private void Update()
        {
            if (Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + .25f;
            if (radioPage != null && radioPage.activeInHierarchy) RefreshRadio();
            // BUG-ENV-002: 下限の補正は設定ホームの表示有無に関わらず掛ける。下限未満のときだけ書くので通常時の保存は増えない。
#if KH_WITH_SLEEPKIT
            if (_restored && sleepKit != null && sleepKit.GetNightModeValue() < MinNightBrightness) sleepKit.SetNightModeValue(MinNightBrightness);
#endif
            if (pouch != null && pouch.settingsHome != null && pouch.settingsHome.activeInHierarchy) RefreshNightMode();
            if (liveAccessLabel != null && liveAccessLabel.gameObject.activeInHierarchy && liveAccess != null)
                liveAccessLabel.text = liveAccess.Pending ? L("ui.extra.039") : L(liveAccess.IsOpenToAll ? "ui.extra.046" : "ui.extra.045");
            // 押せるのは共通の管理者だけ (オーナー優先、不在時だけ Master。判定は KD_LiveAccess → KD_SleepBoard)
            // IMP-DOG-034 C1-misc: only while the button is visible (settings home open). The result feeds nothing but this
            // button's interactable (the press itself re-checks CanManageNow in KD_LiveAccess._SetOpen), and KD_SleepBoard's
            // presence cache is lazy (every reader rescans when older than 0.5 s), so no other reader depends on this poll.
            // Bounded difference: after opening, interactable reflects the state within <= 0.25 s (same 4 Hz tick bound as a
            // state change while open today), instead of carrying a value <= 0.25 s old from while closed.
            if (liveAccessButton != null && liveAccess != null && liveAccessButton.gameObject.activeInHierarchy)
                liveAccessButton.interactable = liveAccess.IsLocalManager && !liveAccess.Pending;
        }
        private void RefreshNightMode()
        {
#if KH_WITH_SLEEPKIT
            if (sleepKit == null || nightMode == null) return;
            float dark = BrightnessToDark(sleepKit.GetNightModeValue());
            bool auto = false;   // 古民家 (SleepKit) にオートは無い
#else
            if (nightMode == null) return;
            // 裏山改変: 表示は KH_NightMode の目標の暗さ (復元値を含む)。未配線なら OFF。
            // IMP-ENV-005: オート中は共通の暗さ (_Darkness がオートの値を返す)。SetValueWithoutNotify なので表示だけではマニュアルにならない
            float dark = hillsideNight != null ? hillsideNight._Darkness() : 0f;
            bool auto = hillsideNight != null && hillsideNight.IsAuto;
            RefreshNightModeChoice(auto);
#endif
            if (!Mathf.Approximately(nightMode.value, dark)) nightMode.SetValueWithoutNotify(dark);
            // オート中は 0% も数字で出す (「OFF」はマニュアルで切っている状態だけ)
            if (nightModeValue != null) nightModeValue.text = !auto && dark < .005f ? "OFF" : Mathf.RoundToInt(dark * 100f).ToString() + "%";
        }
        /// <summary>IMP-ENV-005: [オート][マニュアル] の色、注記の差し替え、オート中のスライダーの淡色。Graphic.color は値が同じなら再描画しない。</summary>
        private void RefreshNightModeChoice(bool auto)
        {
            if (pouch != null)
            {
                if (nightAutoImage != null) nightAutoImage.color = auto ? pouch.equipOnColor : pouch.tabOffColor;
                if (nightManualImage != null) nightManualImage.color = auto ? pouch.tabOffColor : pouch.equipOnColor;
            }
            if (nightAutoNote != null && nightAutoNote.activeSelf != auto) nightAutoNote.SetActive(auto);
            if (nightManualNote != null && nightManualNote.activeSelf == auto) nightManualNote.SetActive(!auto);
            if (nightModeFill != null && _nightFillKnown)
                nightModeFill.color = auto ? new Color(_nightFillManual.r, _nightFillManual.g, _nightFillManual.b, _nightFillManual.a * NightAutoFillAlpha) : _nightFillManual;
        }
        private void RefreshRadio()
        {
            if (!RadioAvailable() || radio == null || radioAudio == null) return;
            bool power = (bool)radio.GetProgramVariable("radioPowerOn");
            int channel = (int)radio.GetProgramVariable("currentChannelIndex");
            bool loading = (bool)radio.GetProgramVariable("waitingPlay");
            if (radioState != null) radioState.text = L("ui.extra.031") + ": " + (power ? "ON" : "OFF") + "   CH " + (channel + 1).ToString()
                + (loading ? "\n" + L("KD_ShopShelf.WordLoading") : "");
            if (radioVolume != null && !Mathf.Approximately(radioVolume.value, radioAudio.volume)) radioVolume.SetValueWithoutNotify(radioAudio.volume);
            if (radioVolumeValue != null) radioVolumeValue.text = Mathf.RoundToInt(radioAudio.volume * 100f).ToString() + "%";
        }
    }
}
