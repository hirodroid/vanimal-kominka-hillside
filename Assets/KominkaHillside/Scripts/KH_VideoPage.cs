using KominkaDogRun;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-UI-011: 手元メニューの副画面 Page_Settings/VideoPage (動画プレイヤー) の中身を描き、VizVid を操作する。
    /// 設計は docs/development/issues/IMP-UI-011-menu-video-player.md §3。開く・戻るは KD_TreatPouch.Video.cs、配線は Tools/KominkaHillside/Player/1。
    ///
    /// - 操作はすべて FrontendHandler 経由 (URL = PlayUrl。履歴・投入者・同期を通す。Core 直呼びはそれを迂回する)。音量・消音だけ Core (ローカル値)
    /// - 状態は Core.State (0 なし / 1 読み込み / 2 エラー / 3 準備 / 4 再生 / 5 一時停止) から描く (VizVid の UIHandler と同じ読み方)
    /// - 描画の契機: FrontendHandler のリスナー (_OnUIUpdate / _OnVolumeChange) と、面が開いている間だけの 0.5 s の自己再予約 (_Tick)。Update は持たない
    /// - 描画は差分 (同じ文字列・同じ interactable を書き直さない)
    /// - URL は利用者ごとに 5 秒に 1 回 (VRChat 公式)。最後の投入から 5 秒未満なら投入せず「{0} 秒待って」。同じ URL の二重投入 (onEndEdit と [入れる]) は黙って捨てる
    /// - 同期変数 0 本。すべてローカル専用 (公開メソッドは `_` 始まり。vrc-networking §1)
    /// - このコンポーネントは常に有効な GameObject に置く (面は閉じている間 非アクティブ)。面が開いているかは page.activeInHierarchy で見る
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_VideoPage : UdonSharpBehaviour
    {
        /// <summary>URL の読み込みは利用者ごとに 5 秒に 1 回 (VRChat 公式 Video Players)。</summary>
        public const float SubmitInterval = 5f;
        /// <summary>面が開いている間の見回りの間隔。</summary>
        public const float TickSeconds = 0.5f;
        /// <summary>GetSuitablePlayerType が 0 (どのバックエンドも名乗らない。youtu.be や拡張子の無い URL) のときの既定。VizVid の UIHandler の selectedPlayer の既定と同じ。</summary>
        public const byte FallbackPlayerType = 1;

        private const string KeyIdle = "video.player.state.idle";
        private const string KeyLoading = "video.player.state.loading";
        private const string KeyPlaying = "video.player.state.playing";
        private const string KeyPlayingAnon = "video.player.state.playing.anon";
        private const string KeyPaused = "video.player.state.paused";
        private const string KeyError = "video.player.state.error";
        private const string KeyUntrusted = "video.player.state.untrusted";
        private const string KeyRateLimited = "video.player.state.ratelimited";
        private const string KeyWait = "video.player.state.wait";
        private const string KeyLocked = "video.player.state.locked";

        [Header("VizVid (Player/1 が配線)")]
        public JLChnToZ.VRC.VVMW.FrontendHandler frontend;
        public JLChnToZ.VRC.VVMW.Core core;
        public KD_Language language;

        [Header("面 (Page_Settings/VideoPage)")]
        [Tooltip("Page_Settings/VideoPage。開いているかの判定 (_Tick の自己再予約はこれが activeInHierarchy の間だけ)")]
        public GameObject page;
        public VRC.SDK3.Components.VRCUrlInputField urlInput;
        [Tooltip("Core.screenTargets に入る RawImage (mode 4)。準備・再生・一時停止のときは白 (映像そのまま)、それ以外は idleScreenColor")]
        public UnityEngine.UI.RawImage screen;
        public Color idleScreenColor = new Color(0.06f, 0.06f, 0.07f, 1f);
        public UnityEngine.UI.Button submitButton, playButton, pauseButton, stopButton, resyncButton, muteButton;
        public UnityEngine.UI.Slider volumeSlider;
        public TMPro.TextMeshProUGUI stateLabel, noteLabel, volumeLabel, urlPlaceholder;
        public TMPro.TextMeshProUGUI submitLabel, playLabel, pauseLabel, stopLabel, resyncLabel, muteLabel, backLabel;
        [Tooltip("ヘッダの [動画] の Label (任意)。video.player.menu を描く")]
        public TMPro.TextMeshProUGUI headerLabel;

        private bool _inited;
        private float _lastSubmitAt = -100f;
        private string _lastSubmitUrl = "";
        private string _noticeKey = "";      // 一時の 1 行 (待って / エラー)。_noticeUntil を過ぎたら状態の行へ戻す
        private float _noticeUntil = -1f;
        private bool _tickPending;
        private float _tickAt = -100f;
        private string _textLang;            // DrawTexts が最後に描いた言語 (null = まだ)
        private int _shownButtons = -1;      // interactable の組 (ビット)。-1 = まだ書いていない
        private int _screenTint = -1;        // 0 = 暗い / 1 = 白 / -1 = まだ

        private string L(string key) { return language != null ? language._Text(key) : key; }

        private void Init()
        {
            if (_inited) return;
            _inited = true;
            if (frontend != null) frontend._AddListener(this);
            else Debug.LogError("[KH_VideoPage] frontend が未配線。Run Tools/KominkaHillside/Player/1.");
            if (core == null) Debug.LogError("[KH_VideoPage] core が未配線。Run Tools/KominkaHillside/Player/1.");
        }

        private void Start()
        {
            Init();
            DrawTexts();
            DrawState();
        }

        // ================================================================ 受け口 (面の UI。onClick / onEndEdit / onValueChanged → SendCustomEvent)

        /// <summary>URL 欄の onEndEdit と [入れる]。ローカル専用。</summary>
        public void _SubmitUrl()
        {
            Init();
            if (urlInput == null || frontend == null || core == null) return;
            VRCUrl url = urlInput.GetUrl();
            if (VRCUrl.IsNullOrEmpty(url)) return;
            string s = url.Get();
            if (s == null || s.Trim().Length == 0) { ClearInput(); return; }
            float now = Time.time;
            // ロック中 (FrontendHandler.Locked。外部の権限の仕組み用) は投入しない。URL は欄に残す
            if (frontend.Locked) { ShowNotice(KeyLocked, now + 3f); return; }
            if (now - _lastSubmitAt < SubmitInterval)
            {
                // onEndEdit と [入れる] の二重 (同じ URL) は黙って捨てる。別の URL は欄に残して待ってもらう
                if (s == _lastSubmitUrl) { ClearInput(); return; }
                ShowNotice(KeyWait, _lastSubmitAt + SubmitInterval);
                return;
            }
            byte type = core.GetSuitablePlayerType(url);
            if (type == 0) type = FallbackPlayerType;
            string[] names = core.PlayerNames;
            if (names == null || type > names.Length)
            {
                Debug.LogError("[KH_VideoPage] VizVid のバックエンドが無い。Run Tools/KominkaHillside/Player/1.");
                ShowNotice(KeyError, now + 3f);
                return;
            }
            _lastSubmitAt = now;
            _lastSubmitUrl = s;
            _noticeUntil = -1f;
            frontend.PlayUrl(url, url, type);
            ClearInput();
            DrawState();
        }

        public void _Play() { Init(); if (frontend != null) frontend._Play(); DrawState(); }
        public void _Pause() { Init(); if (frontend != null) frontend._Pause(); DrawState(); }
        public void _Stop() { Init(); if (frontend != null) frontend._Stop(); DrawState(); }

        /// <summary>[再同期]。自分の端末だけ読み直す (FrontendHandler._LocalSync)。ローカル専用。</summary>
        public void _Resync() { Init(); if (frontend != null) frontend._LocalSync(); DrawState(); }

        /// <summary>音量スライダー (0〜1) → Core.Volume (本人だけ。同期しない)。ローカル専用。</summary>
        public void _VolumeChanged()
        {
            Init();
            if (core == null || volumeSlider == null) return;
            core.Volume = Mathf.Clamp01(volumeSlider.value);   // 0 より大きければ消音も解ける (Core の仕様)
            DrawVolume();
        }

        /// <summary>[消音] / [音を出す]。ローカル専用。</summary>
        public void _ToggleMute()
        {
            Init();
            if (core == null) return;
            core.Muted = !core.Muted;
            DrawVolume();
        }

        // ================================================================ 受け口 (他の Behaviour から)

        /// <summary>KD_TreatPouch._VideoOpen が面を開いた直後。文言を今の言語で描き直し、状態を描き、見回りを始める。ローカル専用。</summary>
        public void _PageOpened()
        {
            Init();
            DrawTexts();
            DrawState();
            ScheduleTick();
        }

        /// <summary>文言だけ今の言語で描き直す。ローカル専用。</summary>
        public void _RefreshLanguage()
        {
            Init();
            DrawTexts();
            DrawState();
        }

        /// <summary>面が開いている間だけ 0.5 s ごと (自己再予約)。閉じたら止まる。ローカル専用。</summary>
        public void _Tick()
        {
            if (Time.time - _tickAt < TickSeconds * 0.5f) return;   // 二重に予約された鎖の後の方は捨てる (1 本にまとまる)
            _tickPending = false;
            if (!PageVisible()) return;
            Init();
            if (LanguageChanged()) DrawTexts();
            DrawState();
            ScheduleTick();
        }

        // ---- VizVid (FrontendHandler._AddListener) から。FrontendHandler が送るのは _OnUIUpdate / _OnVolumeChange
        // (Core の _OnVideoBeginLoad / _OnVideoError は FrontendHandler が受けて _OnUIUpdate に変える)。残り 2 つは Core を直接購読したときの受け口 ----
        public void _OnUIUpdate() { Init(); DrawState(); }
        public void _OnVolumeChange() { Init(); DrawVolume(); }
        public void _OnVideoBeginLoad() { Init(); DrawState(); }
        public void _OnVideoError() { Init(); DrawState(); }

        // ================================================================ 中身

        private bool PageVisible()
        {
            if (page != null) return page.activeInHierarchy;
            if (stateLabel != null) return ((Component)stateLabel).gameObject.activeInHierarchy;
            return gameObject.activeInHierarchy;
        }

        private void ScheduleTick()
        {
            if (_tickPending && Time.time - _tickAt < TickSeconds * 4f) return;   // 生きている予約がある
            _tickPending = true;
            _tickAt = Time.time;
            SendCustomEventDelayedSeconds(nameof(_Tick), TickSeconds);
        }

        private bool LanguageChanged() { return (language != null ? language.CurrentLanguage : "") != _textLang; }

        private void ShowNotice(string key, float until)
        {
            _noticeKey = key;
            _noticeUntil = until;
            DrawState();
            ScheduleTick();   // 待ちの秒数を数え直し、過ぎたら状態の行へ戻す
        }

        private void ClearInput()
        {
            if (urlInput != null) urlInput.SetUrl(VRCUrl.Empty);
        }

        private void SetText(TextMeshProUGUI label, string s)
        {
            if (label != null && label.text != s) label.text = s;
        }

        /// <summary>言語で変わる固定の文言。状態の行は DrawState。</summary>
        private void DrawTexts()
        {
            SetText(headerLabel, L("video.player.menu"));
            SetText(submitLabel, L("video.player.submit"));
            SetText(playLabel, L("video.player.play"));
            SetText(pauseLabel, L("video.player.pause"));
            SetText(stopLabel, L("video.player.stop"));
            SetText(resyncLabel, L("video.player.resync"));
            SetText(backLabel, L("gift.back"));
            SetText(noteLabel, L("video.player.note"));
            SetText(urlPlaceholder, L("video.player.url"));
            _textLang = language != null ? language.CurrentLanguage : "";   // _Text の後 (言語の初期化を済ませてから読む)
            DrawVolume();
        }

        /// <summary>音量の % と [消音] の文言。消音中は Core.Volume が 0 を返すので、スライダーは本人が選んだ位置のまま動かさない。</summary>
        private void DrawVolume()
        {
            if (core == null) return;
            bool muted = core.Muted;
            float v = core.Volume;
            if (volumeSlider != null)
            {
                if (!muted && volumeSlider.value != v) volumeSlider.SetValueWithoutNotify(v);
                v = volumeSlider.value;
            }
            int pct = Mathf.RoundToInt(Mathf.Clamp01(v) * 100f);
            SetText(volumeLabel, L("video.player.volume") + " " + pct.ToString() + "%");
            SetText(muteLabel, L(muted ? "video.player.unmute" : "video.player.mute"));
        }

        /// <summary>状態の 1 行、ボタンの押せる / 押せない、画面の地の色。</summary>
        private void DrawState()
        {
            if (core == null) { SetText(stateLabel, ""); return; }
            int st = core.State;
            bool unlocked = frontend == null || !frontend.Locked;
            float now = Time.time;
            string line;
            if (_noticeUntil > now)
            {
                line = L(_noticeKey);
                if (_noticeKey == KeyWait)
                {
                    int sec = Mathf.CeilToInt(_noticeUntil - now);
                    if (sec < 1) sec = 1;
                    line = line.Replace("{0}", sec.ToString());
                }
            }
            else line = StateLine(st);
            SetText(stateLabel, line);

            bool isStatic = core.IsStatic;
            bool canPlay = unlocked && (st == 3 || st == 5) && !isStatic;
            bool canPause = unlocked && st == 4 && !isStatic;
            bool canStop = unlocked && st != 0;
            bool canSync = !VRCUrl.IsNullOrEmpty(core.Url);
            int mask = (canPlay ? 1 : 0) | (canPause ? 2 : 0) | (canStop ? 4 : 0) | (canSync ? 8 : 0) | (unlocked ? 16 : 0);
            if (mask != _shownButtons)
            {
                _shownButtons = mask;
                if (playButton != null) playButton.interactable = canPlay;
                if (pauseButton != null) pauseButton.interactable = canPause;
                if (stopButton != null) stopButton.interactable = canStop;
                if (resyncButton != null) resyncButton.interactable = canSync;
                if (submitButton != null) submitButton.interactable = unlocked;
                if (urlInput != null) urlInput.interactable = unlocked;
            }

            int tint = st >= 3 ? 1 : 0;
            if (screen != null && tint != _screenTint)
            {
                _screenTint = tint;
                screen.color = tint == 1 ? Color.white : idleScreenColor;
            }
        }

        private string StateLine(int st)
        {
            if (st == 1) return L(KeyLoading);
            if (st == 2) return ErrorLine();
            if (st == 3 || st == 5) return L(KeyPaused);
            if (st == 4)
            {
                string who = SubmitterName();
                return who.Length > 0 ? L(KeyPlaying).Replace("{0}", who) : L(KeyPlayingAnon);
            }
            return L(KeyIdle);
        }

        /// <summary>Core.LastError の種別で言い分ける (VizVid の UIHandler と同じ分け方: 許可リスト外は AccessDenied かつ IsTrusted でないとき)。</summary>
        private string ErrorLine()
        {
            VRC.SDK3.Components.Video.VideoError e = core.LastError;
            if (e == VRC.SDK3.Components.Video.VideoError.RateLimited) return L(KeyRateLimited);
            if (e == VRC.SDK3.Components.Video.VideoError.AccessDenied && !core.IsTrusted) return L(KeyUntrusted);
            return L(KeyError);
        }

        /// <summary>今の動画を入れた人。FrontendHandler.PlayUrl は履歴の題を「​名前:\n​URL」で残す (同期される) ので、
        /// 履歴の先頭が今の URL ならその名前。取れなければ FrontendHandler の Owner (PlayUrl は Owner を取る)。</summary>
        private string SubmitterName()
        {
            if (frontend == null || core == null) return "";
            string[] titles = frontend.HistoryTitles;
            VRCUrl[] urls = frontend.HistoryUrls;
            VRCUrl cur = core.Url;
            if (titles != null && urls != null && titles.Length > 0 && urls.Length > 0
                && !VRCUrl.IsNullOrEmpty(cur) && !VRCUrl.IsNullOrEmpty(urls[0]) && urls[0].Get() == cur.Get())
            {
                string t = titles[0];
                if (t != null && t.Length > 2 && t[0] == '​')
                {
                    int nl = t.IndexOf('\n');
                    if (nl > 2 && t[nl - 1] == ':') return t.Substring(1, nl - 2);
                }
            }
            VRCPlayerApi owner = Networking.GetOwner(frontend.gameObject);
            return Utilities.IsValid(owner) ? owner.displayName : "";
        }
    }
}
