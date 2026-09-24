using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の裏山差分 (KominkaHillside 段階 0-b / 1 / 2。計画書 docs/plans/2026-09-23-menu-rebuild.md D-1 / D-2)。
    ///
    /// **古民家の本体には手を入れず、段階表と「準備中」の描画をここに隔離する。**
    /// 段階 (hillsideStage) ごとに「その段階では null が正 (意図的な未導入)」と「本来必要な参照の欠落」を分け、
    /// 未導入のタブは行 0 に「準備中」1 行だけを描く。Verify (KD_TreatSetupEditor.Hillside) も同じ段階表を持つ。
    ///
    /// | 参照 | 段階 0 | 必須へ戻る段階 |
    /// |---|---|---|
    /// | manager (KH_LeashManager) / recall (KH_ToyRecall) / statusLabel | 必須 | 0-b |
    /// | ledger / catalog | null 可 | 1 (IMP-PRG-001) |
    /// | box / accessories / petFxBox / sleepBoard / sleepPlacement / callPose | null 可 | 2 (IMP-DOG-014 / 015 / 016) |
    /// | live | null 可 | DES-LIV-001 の採用後 |
    /// </summary>
    public partial class KD_TreatPouch
    {
        // ============================================================
        // 段階フラグと裏山の参照
        // ============================================================

        [Header("裏山 (KominkaHillside)")]
        [Tooltip("0 = 段階 0-b (器 + さんぽ / おもちゃ / あそぶ)。1 = 台帳後 (IMP-PRG-001)。2 = おやつ / かざり / 呼ぶ後")]
        public int hillsideStage = 0;

        [Tooltip("さんぽの正本 (接続 / 解除 / 行き先)。段階 0 から必須")]
        public KominkaHillside.KH_LeashManager manager;

        [Tooltip("おもちゃ (ボール / ディスク) を手元へ呼ぶ口。段階 0 から必須")]
        public KominkaHillside.KH_ToyRecall recall;

        [Tooltip("おもちゃ行の [ディスク] (ToyRow{i}/Disc)。添字 = 犬 (KD_DogDefs.All の順)")]
        public GameObject[] discButtons;
        public UnityEngine.UI.Button[] discButtonComponents;
        public UnityEngine.UI.Image[] discButtonImages;
        public TextMeshProUGUI[] discButtonLabels;

        [Tooltip("戻る・選ぶ (灰) の色。KH_HandMenu の actionNeutral と同じ値")]
        public Color hillsideNeutralColor = new Color(0.34f, 0.30f, 0.28f);

        /// <summary>犬の呼び名の既定キー (toyNameKeys が未配線のとき)。KD_DogDefs.All の順。</summary>
        private string[] _hillsideDogKeys = { "activities.dog.Corgi", "activities.dog.Pome", "activities.dog.Lab", "activities.dog.Dachs", "activities.dog.Shiba", "activities.dog.Samoyed" };

        // ---- 板の下端の 1 行。枠はタブごとに 3 つ (0 さんぽ / 1 おもちゃ / 2 あそぶ) —
        // さんぽとおもちゃを同時に待たせても互いの通知を消さない。表示は今のタブの枠だけ、期限切れは枠ごと ----
        private string[] _hsNoteKeys = { "", "", "" };
        private string[] _hsNoteArgs = { "", "", "" };
        private float[] _hsNoteUntil = { 0f, 0f, 0f };

        // ============================================================
        // Start のガード (本体 Start の LogError ブロックの置き換え)
        // ============================================================

        private void HillsideStartGuard()
        {
            if (statusLabel == null)
                Debug.LogError("[KD_TreatPouch] statusLabel 未配線 - 理由コードの出し先が無い = **無言で失敗する**。Run Tools/KominkaHillside/Menu/7 Build Menu.");
            if (manager == null)
                Debug.LogError("[KD_TreatPouch] manager (KH_LeashManager) 未配線 - さんぽが使えない。Run Tools/KominkaHillside/Menu/7 Build Menu.");
            if (recall == null)
                Debug.LogError("[KD_TreatPouch] recall (KH_ToyRecall) 未配線 - おもちゃを呼べない。Run Tools/KominkaHillside/Menu/7 Build Menu.");

            // 段階表 (D-1): その段階で null が正なら 1 行の Log、戻す段階を過ぎていれば LogError
            string planned = "";
            if (ledger == null) { if (hillsideStage < 1) planned += " ledger"; else Debug.LogError("[KD_TreatPouch] ledger 未配線 (段階 " + hillsideStage.ToString() + ")"); }
            if (catalog == null) { if (hillsideStage < 1) planned += " catalog"; else Debug.LogError("[KD_TreatPouch] catalog 未配線 (段階 " + hillsideStage.ToString() + ")"); }
            if (box == null) { if (hillsideStage < 2) planned += " box"; else Debug.LogError("[KD_TreatPouch] box 未配線 (段階 " + hillsideStage.ToString() + ")"); }
            if (accessories == null) { if (hillsideStage < 2) planned += " accessories"; else Debug.LogError("[KD_TreatPouch] accessories 未配線 (段階 " + hillsideStage.ToString() + ")"); }
            if (petFxBox == null) { if (hillsideStage < 2) planned += " petFxBox"; else Debug.LogError("[KD_TreatPouch] petFxBox 未配線 (段階 " + hillsideStage.ToString() + ")"); }
            if (sleepBoard == null) { if (hillsideStage < 2) planned += " sleepBoard"; else Debug.LogError("[KD_TreatPouch] sleepBoard 未配線 (段階 " + hillsideStage.ToString() + ")"); }
            // 段階 2 (IMP-DOG-016): 呼ぶタブは台帳・場所決め・ポーズ面の 3 つで 1 組。欠けると [ポーズを選ぶ] が無言で何もしない
            if (hillsideStage >= 2 && sleepBoard != null && (sleepPlacement == null || callPose == null))
                Debug.LogError("[KD_TreatPouch] sleepPlacement / callPose 未配線 (段階 " + hillsideStage.ToString() + ") - Run Tools/KominkaHillside/Menu/6i・6j → Menu/7");
            if (live == null) planned += " live";   // DES-LIV-001 の採用まで常に未導入
            if (planned.Length > 0)
                Debug.Log("[KD_TreatPouch] 段階 " + hillsideStage.ToString() + ":" + planned + " は意図的な未導入");

            HillsideHideBalance();
        }

        /// <summary>残高 (ledger) が無い段階 (0) では「読み込み中...」を出し続けないよう、ヘッダの残高とポイントの入口を隠す。
        /// 段階 1 (IMP-PRG-001) で ledger が配線されたら出す (未復元の間は Redraw が「読み込み中...」を書く。古民家どおり)。</summary>
        private void HillsideHideBalance()
        {
            bool show = ledger != null;
            if (balanceLabel != null && balanceLabel.gameObject.activeSelf != show) balanceLabel.gameObject.SetActive(show);
            if (hillsidePointsButton != null && hillsidePointsButton.activeSelf != show) hillsidePointsButton.SetActive(show);
        }

        // ============================================================
        // 段階 1 (IMP-PRG-001): ポイントの面と未受取の案内
        // ============================================================

        [Header("裏山 段階 1: ポイントの面 (ヘッダの残高を押す)")]
        [Tooltip("残高ラベルの裏の入口 (PointsButton → _HillsidePoints)。ledger が無い段階では隠す")]
        public GameObject hillsidePointsButton;
        [Tooltip("Page_Settings/PointsPage。extraSettingsPages に登録する。**ギフトコードの器 (KD_GiftCodePanel・入力欄) は置かない** (計画書 D-5)")]
        public GameObject hillsidePointsPage;
        public TextMeshProUGUI hillsidePointsHeading;
        /// <summary>入手経路 2 行 (0 = おじいさんの支度ポイント / 1 = 滞在)。名前と補足。</summary>
        public TextMeshProUGUI[] hillsidePointsRows;
        public TextMeshProUGUI[] hillsidePointsSubs;
        public TextMeshProUGUI hillsidePointsBackLabel;

        /// <summary>ポイントの面を開く前のタブ ([もどる] で戻す)。</summary>
        private int _hsPointsReturnTab = -1;

        /// <summary>ヘッダの残高を押した。古民家の KD_GiftCodePanel._Open の代わり (ギフトコードの器は無い)。ローカル専用。</summary>
        public void _HillsidePoints()
        {
            if (hillsidePointsPage == null || ledger == null) return;
            if (!(_settingsOpen && _extraSettingsPage == hillsidePointsPage)) _hsPointsReturnTab = _tab;
            _OpenExtraSettings(hillsidePointsPage, "gift.title");
        }

        /// <summary>ポイントの面の [もどる]。開く前のタブへ (古民家の KD_GiftCodePanel._Back と同じ)。</summary>
        public void _HillsidePointsBack()
        {
            int tab = _hsPointsReturnTab;
            _hsPointsReturnTab = -1;
            if (tab < 0 || tab >= TabCount) tab = TabWalk;
            SetTab(tab);
        }

        /// <summary>設定の枠の中でポイントの面を開いているときだけ描く (Redraw の設定分岐から毎回呼ぶ)。</summary>
        private void HillsideRedrawPoints()
        {
            if (hillsidePointsPage == null || !_settingsOpen || _extraSettingsPage != hillsidePointsPage) return;
            bool loaded = ledger != null && ledger.Loaded();
            string loading = L("KD_ShopShelf.WordLoading") + "...";
            if (hillsidePointsHeading != null)
                hillsidePointsHeading.text = L("gift.heading") + "    " + (loaded ? L("KD_TreatPouch.WordPointsNow").Replace("{0}", ledger.Points().ToString()) : loading);
            if (hillsidePointsRows != null && hillsidePointsRows.Length >= 2)
            {
                string keeper = ledger != null ? ledger.keeperPoints.ToString() : "?";
                string state = !loaded ? loading : L(ledger.BonusReceived(KD_ProgressLedger.BonusKeeper) ? "gift.received" : "gift.pending");
                if (hillsidePointsRows[0] != null) hillsidePointsRows[0].text = L("KD_TreatPouch.WordPointsKeeper") + "  +" + keeper + " pt  [" + state + "]";
                if (hillsidePointsRows[1] != null) hillsidePointsRows[1].text = L("gift.source.stay");
            }
            if (hillsidePointsSubs != null && hillsidePointsSubs.Length >= 2)
            {
                if (hillsidePointsSubs[0] != null) hillsidePointsSubs[0].text = L("KD_TreatPouch.WordPointsKeeperSub");
                if (hillsidePointsSubs[1] != null) hillsidePointsSubs[1].text = L("KD_TreatPouch.WordPointsStaySub");
            }
            if (hillsidePointsBackLabel != null) hillsidePointsBackLabel.text = L("gift.back");
        }

        /// <summary>おじいさんの支度ポイント (Bonus.keeper) が未受取の案内を出すか。**復元前は出さない** (受取済みの人に一瞬出さない)。
        /// 段階 1 は おやつ・かざりの準備中の行のサブ文、段階 2 は おやつ = 状態行 (statusLabel が空のとき。KD_TreatPouch.Treat.cs の HillsideRedrawTreat) /
        /// かざり = 板の下端の 1 行 (KD_TreatPouch.PetFx.cs の HillsideAccFooter。撫でFX の結果が出ている間はそちらを優先)。</summary>
        private bool HillsideGrantLineVisible()
        {
            return ledger != null && ledger.Loaded() && !ledger.BonusReceived(KD_ProgressLedger.BonusKeeper);
        }

        // ============================================================
        // 準備中 (Redraw のタブ分岐の直前で呼ぶ)
        // ============================================================

        /// <summary>いまのタブのバックエンドが未導入なら、行 0 に「準備中」だけを描いて true。
        /// 他の行と全ボタンは隠す。Treat (box) / Acc (accessories) / Sleep (sleepBoard) / Live (live。当面は常に)。</summary>
        private bool HillsidePreparing()
        {
            HillsideHideBalance();
            if (_tab == TabTreat && box == null)
            {
                HillsidePreparingRows(rowLabels, L("KD_TreatPouch.WordPreparing"));
                HillsideHideAll(rowButtons);
                HillsideHideAll(treatBuyButtons);   // 裏山改変 (IMP-DOG-014): [買う] も box が来るまで出さない
                HillsideClearAll(rowHintLabels);
                // 段階 1: 未受取の案内を準備中の行のサブ文に (段階 2 は おやつタブの状態行へ。KD_TreatPouch.Treat.cs)
                if (HillsideGrantLineVisible() && rowHintLabels != null && rowHintLabels.Length > 0 && rowHintLabels[0] != null)
                    rowHintLabels[0].text = L("KD_TreatPouch.WordGrantLine");
                if (putAwayButton != null && putAwayButton.activeSelf) putAwayButton.SetActive(false);
                if (statusLabel != null) statusLabel.text = "";
                return true;
            }
            if (_tab == TabAcc && accessories == null)
            {
                HillsidePreparingRows(accRowLabels, L("KD_TreatPouch.WordPreparing"));
                HillsideHideRows(fxLabels);   // Page_Acc の Content に並ぶ撫でFX の行 (FxRow0..) も隠す
                HillsideHideAll(hillsideFxHeadRows);   // 裏山改変 (IMP-DOG-015): 撫でFX の群見出しも (KD_TreatPouch.PetFx.cs)
                HillsideHideAll(accButtons);
                HillsideHideAll(accGiftButtons);
                HillsideClearAll(accRowSubLabels);
                if (HillsideGrantLineVisible() && accRowSubLabels != null && accRowSubLabels.Length > 0 && accRowSubLabels[0] != null)
                    accRowSubLabels[0].text = L("KD_TreatPouch.WordGrantLine");
                if (accBackButton != null && accBackButton.activeSelf) accBackButton.SetActive(false);
                if (accHeaderLabel != null) accHeaderLabel.text = "";
                return true;
            }
            if (_tab == TabSleep && sleepBoard == null)
            {
                HillsidePreparingRows(sleepLabels, L("KD_TreatPouch.WordPreparing"));
                HillsideHideAll(sleepButtons);
                HillsideClearAll(sleepStatusLabels);
                if (sleepNoteLabel != null) sleepNoteLabel.text = "";
                return true;
            }
            if (_tab == TabLive && live == null)
            {
                if (tabScrolls != null && TabLive < tabScrolls.Length && tabScrolls[TabLive] != null
                    && !tabScrolls[TabLive].gameObject.activeSelf) tabScrolls[TabLive].gameObject.SetActive(true);
                if (liveDetailRoot != null && liveDetailRoot.activeSelf) liveDetailRoot.SetActive(false);
                HillsidePreparingRows(liveLabels, L("KD_TreatPouch.WordLivePreparing"));
                HillsideHideAll(liveButtons);
                HillsideHideAll(liveGiftButtons);
                HillsideClearAll(liveSubLabels);
                if (liveSubLabels != null && liveSubLabels.Length > 0 && liveSubLabels[0] != null)
                    liveSubLabels[0].text = L("KD_TreatPouch.WordLivePreparingSub");
                if (liveStatusLabel != null) liveStatusLabel.text = L("KD_TreatPouch.WordLivePreparingStatus");
                return true;
            }
            return false;
        }

        /// <summary>行 0 だけを出して名前ラベルに文言を書き、行 1 以降を隠す。行 = 名前ラベルの親 (Row{i} / AccRow{i} / SleepRow{i} / LiveRow{i})。</summary>
        private void HillsidePreparingRows(TextMeshProUGUI[] labels, string text)
        {
            int n = labels == null ? 0 : labels.Length;
            for (int i = 0; i < n; i++)
            {
                TextMeshProUGUI lbl = labels[i];
                if (lbl == null) continue;
                GameObject row = HillsideRowOf(lbl);
                bool on = i == 0;
                if (row != null && row.activeSelf != on) row.SetActive(on);
                if (on) lbl.text = text;
            }
        }

        private void HillsideHideRows(TextMeshProUGUI[] labels)
        {
            int n = labels == null ? 0 : labels.Length;
            for (int i = 0; i < n; i++)
            {
                if (labels[i] == null) continue;
                GameObject row = HillsideRowOf(labels[i]);
                if (row != null && row.activeSelf) row.SetActive(false);
            }
        }

        /// <summary>名前ラベルの親 = 行。**面そのもの・板そのものは返さない** (誤配線で面ごと消さない床)。</summary>
        private GameObject HillsideRowOf(TextMeshProUGUI lbl)
        {
            Transform parent = lbl.transform.parent;
            if (parent == null) return null;
            GameObject row = parent.gameObject;
            if (row == panelRoot) return null;
            if (tabPages != null)
                for (int i = 0; i < tabPages.Length; i++)
                    if (tabPages[i] == row) return null;
            return row;
        }

        private void HillsideHideAll(GameObject[] objects)
        {
            int n = objects == null ? 0 : objects.Length;
            for (int i = 0; i < n; i++)
                if (objects[i] != null && objects[i].activeSelf) objects[i].SetActive(false);
        }

        private void HillsideClearAll(TextMeshProUGUI[] labels)
        {
            int n = labels == null ? 0 : labels.Length;
            for (int i = 0; i < n; i++)
                if (labels[i] != null) labels[i].text = "";
        }

        // ============================================================
        // 共通
        // ============================================================

        /// <summary>犬の呼び名 (さんぽ / おもちゃ共通)。**同じ犬の呼び名を板の中で 2 つ持たない** — toyNameKeys が正、未配線なら既定キー。</summary>
        private string HillsideDogName(int i)
        {
            if (toyNameKeys != null && i >= 0 && i < toyNameKeys.Length && toyNameKeys[i] != null && toyNameKeys[i].Length > 0)
                return L(toyNameKeys[i]);
            if (i >= 0 && i < _hillsideDogKeys.Length) return L(_hillsideDogKeys[i]);
            return "-";
        }

        /// <summary>下端の 1 行へ出す (4 秒)。arg は文言の {0} に入る。</summary>
        private void HillsideNote(int tab, string key, string arg)
        {
            int slot = HillsideNoteSlot(tab);
            if (slot < 0) return;
            _hsNoteKeys[slot] = key;
            _hsNoteArgs[slot] = arg;
            _hsNoteUntil[slot] = Time.time + 4f;
        }

        /// <summary>通知の枠 (0 さんぽ / 1 おもちゃ / 2 あそぶ)。それ以外のタブは -1 (枠を持たない)。</summary>
        private int HillsideNoteSlot(int tab)
        {
            if (tab == TabWalk) return 0;
            if (tab == TabToy) return 1;
            if (tab == TabPlay) return 2;
            return -1;
        }

        /// <summary>このタブの下端の 1 行。通知が生きていればそれ、無ければ fallback。</summary>
        private string HillsideNoteOr(int tab, string fallback)
        {
            int slot = HillsideNoteSlot(tab);
            if (slot < 0 || Time.time >= _hsNoteUntil[slot] || _hsNoteKeys[slot] == null || _hsNoteKeys[slot].Length == 0) return fallback;
            return L(_hsNoteKeys[slot]).Replace("{0}", _hsNoteArgs[slot]);
        }

        /// <summary>板の下端 (Feedback) に 1 行出す。古民家はパーティクルタブでしか使わない枠を、裏山はさんぽ / おもちゃの状態行に使う。</summary>
        private void HillsideFooter(string text)
        {
            if (feedbackLabel == null) return;
            if (!feedbackLabel.gameObject.activeSelf) feedbackLabel.gameObject.SetActive(true);
            feedbackLabel.text = text;
        }
    }
}
