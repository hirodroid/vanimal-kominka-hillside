using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M1: 乗り場の板 1 枚 (KH_TravelNotice と同じ WorldSpace Canvas)。同期しない — 表示は KH_CartManager の同期状態から作る。
    /// DES-UI-007 (2026-10-14): 承認済み HTML B 案 (docs/plans/auto-walk-cart/join-guidance-mock.html) の構成。
    ///
    /// - 上から: 題・説明 2 行・手順 1/2 → 空きカート (あり / なし) → 「一緒に走る」(受付中 N / 全M組・説明・合流の行 8 枠 or 0 件の案内)
    ///   → 「新しく出発する」(説明・自分の目的地・[新しいグループで出発]・結果) → 撮影カメラの案内 (PC だけ)・別々に出発した後の手順・注意書き・写真・PR。
    /// - 固定の文言 (guideLabels / guideKeys) は言語が変わったときだけ書き直す。
    /// - 合流の行: Free 以外のグループを g の順に上から詰める (状態で並べ替えない)。行 N → グループは _rowGroup に覚える。
    ///   ボタンの中は 表示名 (joinLabels、表示名の補助フォント) と「さんと / 一緒に走る」(joinInfoLabels、共通フォント)。
    ///   ボタンの外 (Board 直下) に「目的地：… · N人」(joinMetaLabels)・状態 (joinStateLabels)・理由か結果の 1〜2 行 (joinNoteLabels)。
    ///   押下には描いたときのグループの世代を添える (解散後に同じ番号で作られた別グループへ合流しない)。
    /// - 結果: 合流は押した行の直下 (joinNoteLabels)、出発は出発ボタンの下 (resultLabel)。CartOk は「確認中」で、成功・失敗は manager の既存の経路
    ///   (_ShowResult) から来る。どちらを押したかはパネルが覚える (_pressRow)。_ClearResult は遅延イベントから呼ぶので `_` 付き public。
    /// - 空きカート: manager._CartAvailable (読み取りだけ) を _Refresh (状態変化) のたびと、30 秒ごとの遅延イベント (_AvailabilityTick、1 本だけ) で書く。
    ///   _Refresh は manager の状態変化・言語・自分の行き先の変化のときだけ呼ばれ、1 秒ごとではない (KH_CartManager.LocalTick の RefreshPanels 条件)。
    ///   乗り場は常時、残ったカートのパネルは表示中だけ仕掛ける。表示は目安で、押したときは従来どおり manager が最新の条件で判定する。
    /// - 撮影カメラの案内 (cameraTip) は KH_CartControls の撮影モニターと同じ判定で Android / iOS では出さない。
    /// - U# の onClick は引数を渡せないので、合流は行ごとの _PressJoin0..7。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_CartStationPanel : UdonSharpBehaviour
    {
        public KH_CartManager manager;
        public KominkaDogRun.KD_Language language;
        public int spot;
        public TextMeshProUGUI titleLabel, noticeLabel, startLabel, resultLabel;
        public GameObject startButton;
        public GameObject[] joinButtons;           // MaxGroups 行
        public TextMeshProUGUI[] joinLabels;       // マスターの表示名だけ (表示名の補助フォント KH_DisplayName)
        public TextMeshProUGUI[] joinInfoLabels;   // 「さんと / 一緒に走る →」(共通フォント。補助フォントにはハングル・キリルが無い。レビュー #7)
        [Header("M2 掲示 (Build が配線。null 可)")]
        public TextMeshProUGUI prLabel;            // cart.station.pr (注意書きの下)
        public UnityEngine.UI.RawImage photo;      // 写真 (Runtime は触らない)
        [Header("M3 カート上メニュー (spot = -1 のとき)")]
        public int cart = -1;                      // 乗り場のパネルは -1。カート上メニューはそのカートの番号 (開始点はそのカートの置かれた位置)
        [Tooltip("Parked でないときに隠す中身の親 (任意)。null なら直下の子をすべて切り替える")]
        public GameObject content;
        [Header("DES-UI-007 合流の案内 (Build が配線。null 可)")]
        public TextMeshProUGUI[] guideLabels;      // 固定の文言 (言語が変わったときだけ書く)
        public string[] guideKeys;                 // guideLabels と同じ並びの翻訳キー
        public TextMeshProUGUI availLabel;         // 空きカート：あり / なし
        public TextMeshProUGUI joinCountLabel;     // 受付中 N / 全M組 (0 組のときは空)
        public TextMeshProUGUI startDestLabel;     // 自分の目的地：…
        public GameObject emptyState;              // 一緒に走る相手がいないときの案内 (行が 0 のときだけ出す)
        public GameObject cameraTip;               // 撮影カメラの案内 (Android / iOS では隠す)
        public TextMeshProUGUI[] joinMetaLabels;   // 行ごと: 目的地：… · N人
        public TextMeshProUGUI[] joinStateLabels;  // 行ごと: 受付中 / 受付停止 / 到着中
        public TextMeshProUGUI[] joinNoteLabels;   // 行ごと: 選べない理由と次の行動、または押した結果
        private int _visible = -1;
        private Collider _hit;

        const float ResultSeconds = 4f;
        const float CheckingSeconds = 4f;          // 「確認中」の保険の消去 (manager の返事待ち PendingTimeout 2 秒の後に必ず結果が来る。来なければ消す)
        const float AvailabilitySeconds = 30f;     // 空きカートの定期確認 (ユーザー了承 30 秒〜1 分。HTML は 30 秒)
        public const float NameLineEm = 12.5f;     // 表示名 1 行の目安の幅 (em)。18 pt で 225 = ラベル幅 248 の約 9 割 (Cart/2 が照合)
        const string ResultColor = "<color=#EAD5A8>", ResultColorEnd = "</color>", AvailYesColor = "<color=#D3E8AC>";
        public const string KeyCheckingStart = "cart.board.checking.start", KeyCheckingJoin = "cart.board.checking.join";

        private int[] _rowGroup, _rowGen;   // 行 N → グループ番号と、描いたときの世代
        private float _resultUntil;
        private string _resultKey = "";
        private int _resultGroup = -1, _resultGen = -1;   // 合流の結果を出すグループ (-1 = 出発ボタンの下)
        private int _pressGroup = -1, _pressGen = -1;     // 最後に押したのが合流ならそのグループと世代。出発なら -1 (manager からの結果の出し先)
        private string _lang = "";
        private bool _availArmed;
        private bool _pressing, _resultDuringPress;   // manager の呼び出し中か / その間に _ShowResult が来たか

        void Start()
        {
            _Refresh();
        }

        /// <summary>manager が ApplyState (状態の変化)、言語・自分の行き先の変化で呼ぶ (1 秒ごとの処理は変化が無ければ呼ばない)。</summary>
        public void _Refresh()
        {
            EnsureRows();
            bool onCart = spot < 0;
            if (onCart)
            {
                // M3: カート上メニューはそのカートがその場に残っている (Parked) ときだけ
                bool show = manager != null && manager._CartParked(cart);
                _SetVisible(show);
                if (!show) return;
            }
            string lang = language != null ? language.CurrentLanguage : "";
            if (lang != _lang)
            {
                _lang = lang;
                if (titleLabel != null) titleLabel.text = T("cart.title");
                if (noticeLabel != null) noticeLabel.text = T(onCart ? "cart.parked" : "cart.notice");
                if (prLabel != null) prLabel.text = T("cart.station.pr");   // M2: 注意書きの下の 1 行
                if (startLabel != null) startLabel.text = T("cart.board.start.button");
                int n = guideLabels != null && guideKeys != null ? Mathf.Min(guideLabels.Length, guideKeys.Length) : 0;
                for (int i = 0; i < n; i++) if (guideLabels[i] != null && guideKeys[i] != null) guideLabels[i].text = T(guideKeys[i]);
            }
#if UNITY_ANDROID || UNITY_IOS
            if (cameraTip != null && cameraTip.activeSelf) cameraTip.SetActive(false);   // 撮影モニターは PC 専用 (KH_CartControls と同じ判定。IMP-WLD-008)
#endif
            if (manager == null)
            {
                if (startButton != null && startButton.activeSelf) startButton.SetActive(false);
                for (int r = 0; joinButtons != null && r < joinButtons.Length; r++) { if (joinButtons[r] != null && joinButtons[r].activeSelf) joinButtons[r].SetActive(false); ClearRowText(r); }
                return;
            }
            if (startButton != null && !startButton.activeSelf) startButton.SetActive(true);
            if (startDestLabel != null)
            {
                string dest = DestName(manager._MyDestination());
                startDestLabel.text = T("cart.board.start.dest").Replace("{0}", dest.Length > 0 ? dest : "-");
            }
            WriteAvailability();
            ArmAvailability();

            int row = 0, open = 0;
            int rows = joinButtons != null ? joinButtons.Length : 0;
            for (int g = 0; g < KH_CartManager.MaxGroups && row < rows; g++)
            {
                // M2: 受付 OFF・到着中のグループも行に出し、状態と理由を添えてボタンを無効にする (押しても CartClosed)
                int state = manager._GroupState(g);
                if (state == KH_CartManager.GroupFree) continue;
                bool isOpen = state == KH_CartManager.GroupRunning && manager._GroupAccepting(g);
                bool ending = state == KH_CartManager.GroupEnding;
                if (isOpen) open++;
                _rowGroup[row] = g;
                _rowGen[row] = manager._GroupGen(g);   // 押したときにこの世代を添える (レビュー #4)
                if (joinLabels != null && row < joinLabels.Length && joinLabels[row] != null)
                {
                    VRCPlayerApi p = VRCPlayerApi.GetPlayerById(manager._GroupMasterPlayerId(g));
                    joinLabels[row].text = Utilities.IsValid(p) ? _TwoLineName(p.displayName) : "?";
                }
                if (joinInfoLabels != null && row < joinInfoLabels.Length && joinInfoLabels[row] != null)
                    joinInfoLabels[row].text = T("cart.board.join.suffix") + "\n" + T("cart.board.join.action");
                if (joinMetaLabels != null && row < joinMetaLabels.Length && joinMetaLabels[row] != null)
                    joinMetaLabels[row].text = T("cart.board.row.dest").Replace("{0}", DestName(manager._GroupDestination(g))).Replace("{1}", manager._GroupCount(g).ToString());
                if (joinStateLabels != null && row < joinStateLabels.Length && joinStateLabels[row] != null)
                    joinStateLabels[row].text = T(isOpen ? "cart.accepting.on" : (ending ? "cart.row.ending" : "cart.row.closed"));
                if (joinButtons[row] != null)
                {
                    if (!joinButtons[row].activeSelf) joinButtons[row].SetActive(true);
                    UnityEngine.UI.Button b = joinButtons[row].GetComponent<UnityEngine.UI.Button>();
                    if (b != null && b.interactable != isOpen) b.interactable = isOpen;
                }
                row++;
            }
            for (int r = row; r < rows; r++)
            {
                _rowGroup[r] = -1; _rowGen[r] = -1;
                if (joinButtons[r] != null && joinButtons[r].activeSelf) joinButtons[r].SetActive(false);
                ClearRowText(r);
            }
            if (emptyState != null && emptyState.activeSelf != (row == 0)) emptyState.SetActive(row == 0);
            if (joinCountLabel != null)
                joinCountLabel.text = row > 0 ? T("cart.board.join.count").Replace("{0}", open.ToString()).Replace("{1}", row.ToString()) : "";
            // 行ごとの理由と結果。結果の文言は言語が変わったら引き直す。合流の結果は、そのグループの行が今ある位置へ (無ければ出発ボタンの下へ)
            WriteResult();
        }

        public void _PressStart()
        {
            if (manager == null) return;
            int prevGroup = _pressGroup, prevGen = _pressGen;
            BeginPress(-1, -1);
            int code = spot < 0 ? manager._StartFrom(cart) : manager._Start(spot);   // M3: カート上メニューはそのカートから
            EndPress(code, -1, -1, KeyCheckingStart, prevGroup, prevGen);
        }

        public void _PressJoin0() { PressJoin(0); }
        public void _PressJoin1() { PressJoin(1); }
        public void _PressJoin2() { PressJoin(2); }
        public void _PressJoin3() { PressJoin(3); }
        public void _PressJoin4() { PressJoin(4); }
        public void _PressJoin5() { PressJoin(5); }
        public void _PressJoin6() { PressJoin(6); }
        public void _PressJoin7() { PressJoin(7); }

        private void PressJoin(int row)
        {
            EnsureRows();
            if (manager == null || row < 0 || row >= _rowGroup.Length) return;
            int g = _rowGroup[row];
            if (g < 0) return;
            int gen = _rowGen[row];
            int prevGroup = _pressGroup, prevGen = _pressGen;
            BeginPress(g, gen);
            int code = spot < 0 ? manager._JoinFrom(cart, g, gen) : manager._Join(spot, g, gen);
            EndPress(code, g, gen, KeyCheckingJoin, prevGroup, prevGen);
        }

        /// <summary>manager を呼ぶ前に今回の出し先を用意する。Owner 自身では _Start / _Join の中で Receive → Commit → ApplyState → _ShowResult (成功) まで
        /// 済むので、呼び出しの後で用意すると成功が古い出し先へ行き、その上を「確認中」で上書きしてしまう (Codex 2026-10-14 [高])。</summary>
        private void BeginPress(int group, int gen)
        {
            _pressGroup = group; _pressGen = gen;
            _resultDuringPress = false;
            _pressing = true;
        }

        /// <summary>CartOk: 呼び出し中に結果が来ていなければ「確認中」。即時の拒否: 先に待っている要求の出し先を戻し (返事待ち中の CartBusy で先の結果の行を変えない)、
        /// 拒否の文言を押した所に出す (出発も合流も BoardKey を通し、押下時と要求後で同じ失敗には同じ板の文言)。</summary>
        private void EndPress(int code, int group, int gen, string checkingKey, int prevGroup, int prevGen)
        {
            _pressing = false;
            if (code == KH_CartManager.CartOk)
            {
                if (!_resultDuringPress) ShowAt(group, gen, checkingKey, CheckingSeconds);
                return;
            }
            _pressGroup = prevGroup; _pressGen = prevGen;
            ShowAt(group, gen, BoardKey(CodeKey(code), group, gen), ResultSeconds);
        }

        /// <summary>結果の文言を 4 秒出す (manager からも呼ぶ: 出発・合流の確定、返事待ちの時間切れ)。出し先は最後に押したもの (合流 = その行の直下、出発 = 出発ボタンの下)。</summary>
        public void _ShowResult(string key)
        {
            if (key == null || key.Length == 0) return;
            if (_pressing) _resultDuringPress = true;   // Owner 自身の同期の確定 (EndPress が「確認中」で上書きしない)
            ShowAt(_pressGroup, _pressGen, BoardKey(key, _pressGroup, _pressGen), ResultSeconds);
        }

        public void _ClearResult()
        {
            if (Time.time < _resultUntil - 0.05f) return;   // 後から出した結果の秒数を待つ
            _resultKey = "";
            WriteResult();
        }

        /// <summary>空きカートの定期確認 (遅延イベント。1 本だけ回す)。乗り場は常時、残ったカートのパネルは表示中だけ仕掛け直す。</summary>
        public void _AvailabilityTick()
        {
            _availArmed = false;
            if (spot < 0 && _visible != 1) return;   // 隠れたら止める (_SetVisible(true) / _Refresh で再開)
            WriteAvailability();
            ArmAvailability();
        }

        /// <summary>M3: カート上メニューの表示切替。content があればそれだけ、無ければ直下の子すべて。パネル自身の Collider (VRCUiShape の当たり) も切る。
        /// Behaviour 自身は止めない (manager から呼ばれ続ける)。変わったときだけ書く。</summary>
        public void _SetVisible(bool on)
        {
            int want = on ? 1 : 0;
            if (want == _visible) return;
            _visible = want;
            if (content != null) { if (content.activeSelf != on) content.SetActive(on); }
            else
            {
                Transform t = transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    GameObject child = t.GetChild(i).gameObject;
                    if (child.activeSelf != on) child.SetActive(on);
                }
            }
            if (_hit == null) _hit = GetComponent<Collider>();
            if (_hit != null) _hit.enabled = on;
            if (on) ArmAvailability();
        }

        // ---- 空きカート ----

        private void WriteAvailability()
        {
            if (manager == null) return;
            bool yes = manager._CartAvailable(spot, cart);
            if (availLabel != null) availLabel.text = (yes ? AvailYesColor : ResultColor) + T(yes ? "cart.board.avail.yes" : "cart.board.avail.no") + ResultColorEnd;   // あり = 緑 / なし = 金 (HTML と同じ。文字でも区別)
        }

        private void ArmAvailability()
        {
            if (_availArmed || manager == null) return;
            if (spot < 0 && _visible != 1) return;
            _availArmed = true;
            SendCustomEventDelayedSeconds(nameof(_AvailabilityTick), AvailabilitySeconds);
        }

        // ---- 結果 ----

        /// <summary>結果を出す。group >= 0 なら合流 (その行の直下)、-1 なら出発ボタンの下。前の結果は消す。</summary>
        private void ShowAt(int group, int gen, string key, float seconds)
        {
            _resultKey = key;
            _resultGroup = group; _resultGen = gen;
            _resultUntil = Time.time + seconds;
            SendCustomEventDelayedSeconds(nameof(_ClearResult), seconds);
            WriteResult();
        }

        /// <summary>結果の表示を今の行の並びへ書き直す (出発の欄と全行の理由の行)。結果の無い行は選べない理由 (または空)。</summary>
        private void WriteResult()
        {
            bool active = _resultKey.Length > 0 && Time.time < _resultUntil;
            int resultRow = -1;
            if (active && _resultGroup >= 0)
            {
                for (int r = 0; _rowGroup != null && r < _rowGroup.Length; r++)
                    if (_rowGroup[r] == _resultGroup && _rowGen[r] == _resultGen) { resultRow = r; break; }
            }
            bool atStart = active && resultRow < 0;   // 出発の結果、または合流先の行が消えた (解散など) とき
            if (resultLabel != null) resultLabel.text = atStart ? T(_resultKey) : "";
            int rows = joinNoteLabels != null ? joinNoteLabels.Length : 0;
            for (int r = 0; r < rows; r++)
            {
                if (r == resultRow) { if (joinNoteLabels[r] != null) joinNoteLabels[r].text = ResultColor + T(_resultKey) + ResultColorEnd; continue; }
                WriteRowNote(r, RowReason(r));
            }
        }

        /// <summary>行 r の選べない理由 (受付中・空き行は空)。</summary>
        private string RowReason(int r)
        {
            if (manager == null || _rowGroup == null || r >= _rowGroup.Length || _rowGroup[r] < 0) return "";
            int g = _rowGroup[r];
            int state = manager._GroupState(g);
            if (state == KH_CartManager.GroupEnding) return T("cart.board.row.endingNote");
            if (state == KH_CartManager.GroupRunning && !manager._GroupAccepting(g)) return T("cart.board.row.closedNote");
            return "";
        }

        private void WriteRowNote(int r, string text)
        {
            if (joinNoteLabels == null || r < 0 || r >= joinNoteLabels.Length || joinNoteLabels[r] == null) return;
            joinNoteLabels[r].text = text;
        }

        private void ClearRowText(int r)
        {
            if (joinMetaLabels != null && r < joinMetaLabels.Length && joinMetaLabels[r] != null) joinMetaLabels[r].text = "";
            if (joinStateLabels != null && r < joinStateLabels.Length && joinStateLabels[r] != null) joinStateLabels[r].text = "";
            if (joinNoteLabels != null && r < joinNoteLabels.Length && joinNoteLabels[r] != null) joinNoteLabels[r].text = "";
        }

        /// <summary>manager の結果キーを板の文言へ (既存キーの文言は変えず、HTML B 案の「次の行動」付きの新キーへ置き換える)。
        /// 合流の CartClosed は、そのグループが同じ世代のまま受付を止めているなら「受付停止」、それ以外 (解散・世代変更・到着) は「利用できない」。</summary>
        private string BoardKey(string key, int group, int gen)
        {
            if (key == KH_CartManager.KeyStarted) return "cart.board.result.started";
            if (key == KH_CartManager.KeyJoined) return "cart.board.result.joined";
            if (key == KH_CartManager.KeyFull) return "cart.board.result.full";
            if (key == KH_CartManager.KeyClosed && group >= 0)
            {
                bool knownClosed = manager != null && manager._GroupState(group) == KH_CartManager.GroupRunning && manager._GroupGen(group) == gen && !manager._GroupAccepting(group);
                return knownClosed ? "cart.board.result.closed" : "cart.board.result.unavailable";
            }
            return key;
        }

        /// <summary>表示名を 2 行に分ける。ラベルは Cart/2 の既存の約束 (折返しなし + 省略記号) のまま、1 行目は字幅の目安で必ず収まる所まで、残りを 2 行目へ。
        /// 2 行目が長ければ TMP が末尾を … にする。目安 (大きめに見積もる。Cart/2 の実測で W 0.85 em は 1 行目が溢れたので 2026-10-14 に引き上げ): U+1100 以上 (CJK・ハングル・記号) 1 em、M/W/m/w 1.1、ラテン大文字 0.8、他 0.65。最悪の W だけの名前は 1 行目 11 字 (W が 1.19 em = 21.4 px 以下なら幅 248 の 95 % に収まる)。</summary>
        public string _TwoLineName(string s)   // Cart/2 (VerifyPanel) も同じ分け方で測る
        {
            if (s == null) return "?";
            float used = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                int u = (int)s[i];   // 'M' 77 / 'W' 87 / 'm' 109 / 'w' 119 / 'A'..'Z' 65..90
                float em = u >= 0x1100 ? 1f : (u == 77 || u == 87 || u == 109 || u == 119) ? 1.1f : (u >= 65 && u <= 90) ? 0.8f : 0.65f;
                used += em;
                if (used <= NameLineEm) continue;
                int k = i;
                if (k > 0 && (int)s[k - 1] >= 0xD800 && (int)s[k - 1] <= 0xDBFF) k--;   // サロゲートの組を分けない
                if (k <= 0) return s;
                return s.Substring(0, k) + "\n" + s.Substring(k);
            }
            return s;
        }

        private string CodeKey(int code)
        {
            if (code == KH_CartManager.CartBusy) return KH_CartManager.KeyBusy;
            if (code == KH_CartManager.CartFull) return KH_CartManager.KeyFull;
            if (code == KH_CartManager.CartHere) return KH_CartManager.KeyHere;
            return KH_CartManager.KeyClosed;   // Closed と Unavailable (経路・乗り場の未配線)
        }

        private string DestName(int destination)
        {
            if (manager == null) return "";
            string key = manager._DestinationKey(destination);
            return key.Length > 0 ? T(key) : "";
        }

        // Start の順は保証されない (manager の Start が先に _Refresh を呼ぶことがある) ので、行の表はここで用意する
        private void EnsureRows()
        {
            int n = joinButtons != null ? joinButtons.Length : 0;
            if (_rowGroup != null && _rowGroup.Length == n) return;
            _rowGroup = new int[n];
            _rowGen = new int[n];
            for (int r = 0; r < n; r++) { _rowGroup[r] = -1; _rowGen[r] = -1; }
        }

        private string T(string key) { return language != null ? language._Text(key) : key; }
    }
}
