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
        private const string KeyQueued = "video.player.state.queued";
        /// <summary>[次へ] が 5 秒制限に記録する印 (URL ではない。URL 欄の二重判定と一致しない)。</summary>
        private const string SkipMark = "\u0001skip";

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
        [Tooltip("操作ボタン。文字は書かない (記号のスプライト。IMP-UI-011 §7 依頼 2: 多言語化しない)")]
        public UnityEngine.UI.Button submitButton, playButton, pauseButton, stopButton, resyncButton, muteButton;
        public UnityEngine.UI.Slider volumeSlider;
        public TMPro.TextMeshProUGUI stateLabel, noteLabel, volumeLabel, urlPlaceholder;
        [Tooltip("[もどる] の Label (gift.back)")]
        public TMPro.TextMeshProUGUI backLabel;
        [Tooltip("ヘッダの [動画] の Label (任意)。video.player.menu を描く")]
        public TMPro.TextMeshProUGUI headerLabel;

        [Header("消音ボタンの記号 (Muted で差し替え)")]
        public UnityEngine.UI.Image muteIcon;
        [Tooltip("音が出ているときの記号 (押すと消音)")]
        public Sprite spriteUnmuted;
        [Tooltip("消音中の記号 (押すと音を出す)")]
        public Sprite spriteMuted;

        [Header("全画面 (IMP-UI-011 §7 依頼 1。panelRoot/VideoFull、既定は非表示)")]
        [Tooltip("板いっぱいの覆い。面が閉じた (タブ切替・板を閉じる) ら _Tick が隠す。_PageOpened でも隠す (開き直しは常に通常表示から)")]
        public GameObject fullRoot;
        [Tooltip("Core.screenTargets の 2 枚目 (mode 4)。地の色は screen と同じ規則")]
        public UnityEngine.UI.RawImage fullScreen;
        [Tooltip("面の [全画面] (→ _ToggleFull) と覆いの [×] (→ _FullClose)")]
        public UnityEngine.UI.Button fullButton, fullCloseButton;

        [Header("次へ・リピート・一覧 (IMP-UI-011 §8)")]
        [Tooltip("[次へ] (→ _Skip = FrontendHandler._Skip。Owner の取得は VizVid 側)")]
        public UnityEngine.UI.Button skipButton;
        [Tooltip("[リピート] (→ _CycleRepeat: オフ → 全部 → 1 曲 → オフ)")]
        public UnityEngine.UI.Button repeatButton;
        [Tooltip("リピートの記号。1 曲は spriteRepeatOne、それ以外 spriteRepeat。オフはアルファ 0.4")]
        public UnityEngine.UI.Image repeatIcon;
        public Sprite spriteRepeat, spriteRepeatOne;
        [Tooltip("[一覧] (→ _ToggleList。画面と一覧を入れ替える。再生は続く)")]
        public UnityEngine.UI.Button listButton;

        [Header("一覧 (画面と同じ矩形。既定は非表示)")]
        public GameObject listRoot;
        [Tooltip("→ _ListTabHistory / _ListTabQueue。選んでいるタブは押せなくする (今どちらかの印)")]
        public UnityEngine.UI.Button tabHistoryButton, tabQueueButton;
        [Tooltip("video.player.list.history / .queue + \" (件数)\"")]
        public TMPro.TextMeshProUGUI tabHistoryLabel, tabQueueLabel;
        [Tooltip("video.player.list.empty (表示中のタブが空のとき)")]
        public TMPro.TextMeshProUGUI listEmptyLabel;
        [Tooltip("→ _ListPagePrev / _ListPageNext")]
        public UnityEngine.UI.Button pagePrevButton, pageNextButton;
        public TMPro.TextMeshProUGUI pageLabel;
        [Tooltip("行 6 本 (上から)。余った行は非表示")]
        public GameObject[] rowRoots;
        public TMPro.TextMeshProUGUI[] rowLabels;
        [Tooltip("[▶] → _RowPlay0..5 (今すぐ再生)")]
        public UnityEngine.UI.Button[] rowPlayButtons;
        [Tooltip("2 つ目 → _RowSecond0..5 (履歴: キューへ追加 / キュー: 外す)")]
        public UnityEngine.UI.Button[] rowSecondButtons;
        public UnityEngine.UI.Image[] rowSecondIcons;
        [Tooltip("履歴タブの 2 つ目 (追加) / キュータブの 2 つ目 (外す)")]
        public Sprite spriteAdd, spriteRemove;

        /// <summary>一覧の 1 ページの行数。</summary>
        public const int RowCount = 6;

        private bool _inited;
        private int _listTab;                // 0 = 履歴 / 1 = キュー
        private int _listPage;
        private string[] _rowUrls;           // 各行に描いた URL (押した時に、描いた後で並びが変わっていないかを照合する)
        private int _rowKey = -1;            // 行のボタンの interactable と 2 つ目の記号の組 (unlocked | タブ)。-1 = まだ
        private int _repeatShown = -1;       // 0 = オフ / 1 = 全部 / 2 = 1 曲 / -1 = まだ
        private float _lastSubmitAt = -100f;
        private string _lastSubmitUrl = "";
        private string _noticeKey = "";      // 一時の 1 行 (待って / エラー / ロック / キューに追加)。_noticeUntil を過ぎたら状態の行へ戻す
        private string _noticeArg = "";      // 一時の 1 行の {0} (待っては残り秒を毎回数えるので使わない)
        private float _noticeUntil = -1f;
        private bool _tickPending;
        private float _tickAt = -100f;
        private string _textLang;            // DrawTexts が最後に描いた言語 (null = まだ)
        private int _shownButtons = -1;      // interactable の組 (ビット)。-1 = まだ書いていない
        private int _screenTint = -1;        // 0 = 暗い / 1 = 白 / -1 = まだ
        private int _muteShown = -1;         // 0 = 音あり / 1 = 消音 / -1 = まだ (muteIcon.sprite の差分)

        private string L(string key) { return language != null ? language._Text(key) : key; }

        private void Init()
        {
            if (_inited) return;
            _inited = true;
            _rowUrls = new string[RowCount];
            if (frontend != null) frontend._AddListener(this);
            else Debug.LogError("[KH_VideoPage] frontend が未配線。Run Tools/KominkaHillside/Player/1.");
            if (core == null) Debug.LogError("[KH_VideoPage] core が未配線。Run Tools/KominkaHillside/Player/1.");
        }

        private void Start()
        {
            Init();
            SetFull(false);
            SetList(false);
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
            byte type = core.GetSuitablePlayerType(url);
            if (type == 0) type = FallbackPlayerType;
            string[] names = core.PlayerNames;
            if (names == null || type > names.Length)
            {
                Debug.LogError("[KH_VideoPage] VizVid のバックエンドが無い。Run Tools/KominkaHillside/Player/1.");
                ShowNotice(KeyError, now + 3f);
                return;
            }
            // onEndEdit と [入れる] の二重 (同じ URL) は黙って捨てて欄を空に。別の URL は欄に残して待ってもらう
            int gate = RateGate(s, true);
            if (gate == 1) { ClearInput(); return; }
            if (gate != 0) return;
            int before = frontend.QueueUrls.Length;
            frontend.PlayUrl(url, url, type);   // キューが有効なら、再生中・読み込み中・キューありのときは末尾へ (VizVid の仕様)
            ClearInput();
            NoticeIfQueued(before);
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

        /// <summary>面の [全画面]。映像を板いっぱいの覆い (fullRoot) に出す。ローカル専用。</summary>
        public void _ToggleFull()
        {
            Init();
            if (fullRoot == null) return;
            SetFull(!fullRoot.activeSelf);
            if (fullRoot.activeSelf) { DrawState(); ScheduleTick(); }   // 見回りが面の閉じを見て覆いを隠す
        }

        /// <summary>覆いのどこか / [×]、および KD_TreatPouch._VideoBack から。通常表示へ戻す。ローカル専用。</summary>
        public void _FullClose()
        {
            SetFull(false);
        }

        // ---- §8: 次へ・リピート・一覧 ----

        /// <summary>[次へ]。VizVid の UI と同じく FrontendHandler._Skip (非 Owner なら Owner を取ってから次へ。Locked は VizVid 側で弾く)。
        /// 次の項目の読み込みが始まる操作なので 5 秒制限を通す (制限中は待機表示)。ローカル専用。</summary>
        public void _Skip()
        {
            Init();
            if (frontend == null || frontend.Locked) return;
            if (RateGate(SkipMark, false) != 0) return;
            frontend._Skip();
            DrawState();
        }

        /// <summary>[リピート]。オフ → 全部 → 1 曲 → オフ。setter が同期を要求し Owner を取る (VizVid の RequestSync)。ローカル専用。</summary>
        public void _CycleRepeat()
        {
            Init();
            if (frontend == null || frontend.Locked) return;
            if (frontend.RepeatOne) frontend.NoRepeat();
            else if (frontend.RepeatAll) frontend.RepeatOne = true;
            else frontend.RepeatAll = true;
            DrawState();
        }

        /// <summary>[一覧]。画面と一覧を入れ替える (再生は続く)。ローカル専用。</summary>
        public void _ToggleList()
        {
            Init();
            if (listRoot == null) return;
            SetList(!listRoot.activeSelf);
            DrawState();
        }

        public void _ListTabHistory() { Init(); _listTab = 0; _listPage = 0; DrawState(); }
        public void _ListTabQueue() { Init(); _listTab = 1; _listPage = 0; DrawState(); }
        public void _ListPagePrev() { Init(); if (_listPage > 0) _listPage--; DrawState(); }
        public void _ListPageNext() { Init(); _listPage++; DrawState(); }   // 上限は DrawList が詰める

        // 行のボタン (onClick → SendCustomEvent。引数のある呼び出しを使わない)。番号は今のページの中の行 (0 = 上)
        public void _RowPlay0() { RowPlay(0); }
        public void _RowPlay1() { RowPlay(1); }
        public void _RowPlay2() { RowPlay(2); }
        public void _RowPlay3() { RowPlay(3); }
        public void _RowPlay4() { RowPlay(4); }
        public void _RowPlay5() { RowPlay(5); }
        public void _RowSecond0() { RowSecond(0); }
        public void _RowSecond1() { RowSecond(1); }
        public void _RowSecond2() { RowSecond(2); }
        public void _RowSecond3() { RowSecond(3); }
        public void _RowSecond4() { RowSecond(4); }
        public void _RowSecond5() { RowSecond(5); }

        // ================================================================ 受け口 (他の Behaviour から)

        /// <summary>KD_TreatPouch._VideoOpen (と板の開き直し) から。開き直しは常に通常表示から。文言を今の言語で描き直し、状態を描き、見回りを始める。ローカル専用。</summary>
        public void _PageOpened()
        {
            Init();
            SetFull(false);
            SetList(false);   // §8: 開き直しは常に画面から
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
            if (!PageVisible()) { SetFull(false); return; }   // タブ切替・板を閉じた: 全画面も自動で戻す
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
            _noticeArg = "";
            _noticeUntil = until;
            DrawState();
            ScheduleTick();   // 待ちの秒数を数え直し、過ぎたら状態の行へ戻す
        }

        private void SetFull(bool on)
        {
            if (fullRoot != null && fullRoot.activeSelf != on) fullRoot.SetActive(on);
        }

        /// <summary>§8: 一覧と画面を入れ替える。一覧が無ければ何もしない (画面は出したまま)。</summary>
        private void SetList(bool on)
        {
            if (listRoot == null) return;
            if (listRoot.activeSelf != on) listRoot.SetActive(on);
            if (screen != null)
            {
                GameObject sg = ((Component)screen).gameObject;
                if (sg.activeSelf == on) sg.SetActive(!on);
            }
        }

        /// <summary>投入・[＋] の直後。キューの件数が増えていたら「キューに追加 ({0} 件目)」を 3 秒 (欄が消えるだけで分からないので)。</summary>
        private void NoticeIfQueued(int before)
        {
            if (frontend == null) return;
            int after = frontend.QueueUrls.Length;
            if (after <= before) return;
            ShowNotice(KeyQueued, Time.time + 3f);
            _noticeArg = after.ToString();
            DrawState();
        }

        /// <summary>URL の読み込みの 5 秒制限 (URL 投入・一覧の行・[次へ] で共通)。
        /// 0 = 通す (時刻と URL を記録した) / 1 = 同じ URL の二重 (黙って捨てる。silentDup = URL 欄の onEndEdit と [入れる] の二重だけ) / 2 = 待って (状態の行に出した)。</summary>
        private int RateGate(string s, bool silentDup)
        {
            float now = Time.time;
            if (now - _lastSubmitAt < SubmitInterval)
            {
                if (silentDup && s == _lastSubmitUrl) return 1;
                ShowNotice(KeyWait, _lastSubmitAt + SubmitInterval);
                return 2;
            }
            _lastSubmitAt = now;
            _lastSubmitUrl = s;
            _noticeUntil = -1f;
            return 0;
        }

        /// <summary>行 r が今指す項目の番号。描いた後に並びが変わっていたら (同期で履歴・キューが動いた) -1 を返して描き直す。</summary>
        private int RowIndex(int r, VRCUrl[] urls)
        {
            int idx = _listPage * RowCount + r;
            if (urls == null || idx < 0 || idx >= urls.Length || VRCUrl.IsNullOrEmpty(urls[idx]) || _rowUrls == null || r >= _rowUrls.Length) return -1;
            if (urls[idx].Get() != _rowUrls[r]) return -1;
            return idx;
        }

        /// <summary>[▶]。履歴: キューへ足してから末尾を今すぐ再生 (VizVid の UI の「今すぐ」と同じ)。キュー: その項目を今すぐ (キューから消える)。</summary>
        private void RowPlay(int r)
        {
            Init();
            if (frontend == null || frontend.Locked) return;
            VRCUrl[] urls = _listTab == 0 ? frontend.HistoryUrls : frontend.QueueUrls;
            int idx = RowIndex(r, urls);
            if (idx < 0) { DrawState(); return; }
            if (RateGate(urls[idx].Get(), false) != 0) return;
            if (_listTab == 0)
            {
                frontend.PlayAt(-1, idx, false);
                int n = frontend.QueueUrls.Length;   // 再生中などで末尾に積まれたなら、それを今すぐ (空いていれば PlayAt(-1) が即再生済みで n = 0)
                if (n > 0) frontend.PlayAt(0, n - 1, false);
            }
            else frontend.PlayAt(0, idx, false);
            DrawState();
        }

        /// <summary>2 つ目。履歴: キューへ追加 (空いていれば即再生になるのは VizVid の仕様)。キュー: 外すだけ (読み込まないので 5 秒制限は通さない)。</summary>
        private void RowSecond(int r)
        {
            Init();
            if (frontend == null || frontend.Locked) return;
            VRCUrl[] urls = _listTab == 0 ? frontend.HistoryUrls : frontend.QueueUrls;
            int idx = RowIndex(r, urls);
            if (idx < 0) { DrawState(); return; }
            if (_listTab == 0)
            {
                if (RateGate(urls[idx].Get(), false) != 0) return;
                int before = frontend.QueueUrls.Length;
                frontend.PlayAt(-1, idx, false);
                NoticeIfQueued(before);
            }
            else frontend.PlayAt(0, idx, true);
            DrawState();
        }

        /// <summary>題の 1 行: 「\u200B入れた人:\n\u200B題」から印を除き、改行を空白に。空なら URL。</summary>
        private string RowText(string title, string url)
        {
            if (title == null || title.Length == 0) return url;
            string t = title.Replace("\u200B", "").Replace("\n", " ").Trim();
            return t.Length > 0 ? t : url;
        }

        /// <summary>リピートの記号 (同期値を毎回読み直す。他人が変えても追従)。</summary>
        private void DrawRepeat()
        {
            if (frontend == null || repeatIcon == null) return;
            int rep = frontend.RepeatOne ? 2 : frontend.RepeatAll ? 1 : 0;
            if (rep == _repeatShown) return;
            _repeatShown = rep;
            Sprite want = rep == 2 ? spriteRepeatOne : spriteRepeat;
            if (want != null) repeatIcon.sprite = want;
            Color c = repeatIcon.color;
            c.a = rep == 0 ? 0.4f : 1f;
            repeatIcon.color = c;
        }

        /// <summary>一覧 (表示中のときだけ行を描く)。タブ・件数・ページ・行・空の文。差分で書く。</summary>
        private void DrawList()
        {
            if (frontend == null || listRoot == null || !listRoot.activeSelf) return;
            bool unlocked = !frontend.Locked;
            VRCUrl[] hUrls = frontend.HistoryUrls;
            VRCUrl[] qUrls = frontend.QueueUrls;
            int hCount = hUrls != null ? hUrls.Length : 0;
            int qCount = qUrls != null ? qUrls.Length : 0;
            SetText(tabHistoryLabel, L("video.player.list.history") + " (" + hCount.ToString() + ")");
            SetText(tabQueueLabel, L("video.player.list.queue") + " (" + qCount.ToString() + ")");
            if (tabHistoryButton != null && tabHistoryButton.interactable != (_listTab != 0)) tabHistoryButton.interactable = _listTab != 0;
            if (tabQueueButton != null && tabQueueButton.interactable != (_listTab != 1)) tabQueueButton.interactable = _listTab != 1;

            VRCUrl[] urls = _listTab == 0 ? hUrls : qUrls;
            string[] titles = _listTab == 0 ? frontend.HistoryTitles : frontend.QueueTitles;
            int count = _listTab == 0 ? hCount : qCount;
            int pages = count <= 0 ? 1 : (count + RowCount - 1) / RowCount;
            if (_listPage >= pages) _listPage = pages - 1;
            if (_listPage < 0) _listPage = 0;

            int key = (unlocked ? 1 : 0) | (_listTab << 1);
            bool rekey = key != _rowKey;
            _rowKey = key;
            Sprite second = _listTab == 0 ? spriteAdd : spriteRemove;
            for (int r = 0; r < RowCount; r++)
            {
                int idx = _listPage * RowCount + r;
                bool show = idx < count;
                if (rowRoots != null && r < rowRoots.Length && rowRoots[r] != null && rowRoots[r].activeSelf != show) rowRoots[r].SetActive(show);
                string u = show && urls[idx] != null ? urls[idx].Get() : "";
                if (_rowUrls != null) _rowUrls[r] = u;
                if (show && rowLabels != null && r < rowLabels.Length)
                    SetText(rowLabels[r], RowText(titles != null && idx < titles.Length ? titles[idx] : "", u));
                if (!rekey) continue;
                if (rowPlayButtons != null && r < rowPlayButtons.Length && rowPlayButtons[r] != null) rowPlayButtons[r].interactable = unlocked;
                if (rowSecondButtons != null && r < rowSecondButtons.Length && rowSecondButtons[r] != null) rowSecondButtons[r].interactable = unlocked;
                if (second != null && rowSecondIcons != null && r < rowSecondIcons.Length && rowSecondIcons[r] != null) rowSecondIcons[r].sprite = second;
            }

            if (listEmptyLabel != null)
            {
                GameObject eg = ((Component)listEmptyLabel).gameObject;
                bool empty = count == 0;
                if (eg.activeSelf != empty) eg.SetActive(empty);
                if (empty) SetText(listEmptyLabel, L("video.player.list.empty"));
            }
            SetText(pageLabel, (_listPage + 1).ToString() + "/" + pages.ToString());
            bool canPrev = _listPage > 0;
            bool canNext = _listPage < pages - 1;
            if (pagePrevButton != null && pagePrevButton.interactable != canPrev) pagePrevButton.interactable = canPrev;
            if (pageNextButton != null && pageNextButton.interactable != canNext) pageNextButton.interactable = canNext;
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
            // 操作ボタンは記号 (文字を書かない)。翻訳するのはヘッダ・[もどる]・注意文・欄の placeholder と状態の 1 行だけ
            SetText(headerLabel, L("video.player.menu"));
            SetText(backLabel, L("gift.back"));
            SetText(noteLabel, L("video.player.note"));
            SetText(urlPlaceholder, L("video.player.url"));
            _textLang = language != null ? language.CurrentLanguage : "";   // _Text の後 (言語の初期化を済ませてから読む)
            DrawVolume();
        }

        /// <summary>音量の % と [消音] の記号。消音中は Core.Volume が 0 を返すので、スライダーは本人が選んだ位置のまま動かさない。</summary>
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
            SetText(volumeLabel, pct.ToString() + "%");
            int m = muted ? 1 : 0;
            if (muteIcon != null && m != _muteShown)
            {
                Sprite want = muted ? spriteMuted : spriteUnmuted;
                if (want != null) { muteIcon.sprite = want; _muteShown = m; }
            }
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
                else if (_noticeArg.Length > 0) line = line.Replace("{0}", _noticeArg);
            }
            else line = StateLine(st);
            SetText(stateLabel, line);

            bool isStatic = core.IsStatic;
            bool canPlay = unlocked && (st == 3 || st == 5) && !isStatic;
            bool canPause = unlocked && st == 4 && !isStatic;
            bool canStop = unlocked && st != 0;
            bool canSync = !VRCUrl.IsNullOrEmpty(core.Url);
            bool canSkip = unlocked && (st != 0 || (frontend != null && frontend.QueueUrls.Length > 0));
            int mask = (canPlay ? 1 : 0) | (canPause ? 2 : 0) | (canStop ? 4 : 0) | (canSync ? 8 : 0) | (unlocked ? 16 : 0) | (canSkip ? 32 : 0);
            if (mask != _shownButtons)
            {
                _shownButtons = mask;
                if (playButton != null) playButton.interactable = canPlay;
                if (pauseButton != null) pauseButton.interactable = canPause;
                if (stopButton != null) stopButton.interactable = canStop;
                if (resyncButton != null) resyncButton.interactable = canSync;
                if (submitButton != null) submitButton.interactable = unlocked;
                if (urlInput != null) urlInput.interactable = unlocked;
                if (skipButton != null) skipButton.interactable = canSkip;
                if (repeatButton != null) repeatButton.interactable = unlocked;
            }
            DrawRepeat();
            DrawList();

            int tint = st >= 3 ? 1 : 0;
            if (tint != _screenTint)
            {
                _screenTint = tint;
                Color c = tint == 1 ? Color.white : idleScreenColor;
                if (screen != null) screen.color = c;
                if (fullScreen != null) fullScreen.color = c;
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

        /// <summary>今の動画を入れた人。FrontendHandler.PlayUrl は履歴の題を「\u200B名前:\n\u200BURL」で残す (同期される) ので、
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
                if (t != null && t.Length > 2 && t[0] == '\u200B')
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
