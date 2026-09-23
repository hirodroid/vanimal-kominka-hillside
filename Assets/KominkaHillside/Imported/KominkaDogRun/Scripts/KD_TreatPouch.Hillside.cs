using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の裏山差分 (KominkaHillside 段階 0-b。計画書 docs/plans/2026-09-23-menu-rebuild.md D-1 / D-2)。
    ///
    /// **古民家の本体には手を入れず、段階表と「準備中」の描画をここに隔離する。**
    /// 段階 (hillsideStage) ごとに「その段階では null が正 (意図的な未導入)」と「本来必要な参照の欠落」を分け、
    /// 未導入のタブは行 0 に「準備中」1 行だけを描く。Verify (KD_TreatSetupEditor.Hillside) も同じ段階表を持つ。
    ///
    /// | 参照 | 段階 0 | 必須へ戻る段階 |
    /// |---|---|---|
    /// | manager (KH_LeashManager) / recall (KH_ToyRecall) / statusLabel | 必須 | 0-b |
    /// | ledger / catalog | null 可 | 1 (IMP-PRG-001) |
    /// | box / accessories / petFxBox / sleepBoard | null 可 | 2 (IMP-DOG-014 / 015 / 016) |
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
            if (live == null) planned += " live";   // DES-LIV-001 の採用まで常に未導入
            if (planned.Length > 0)
                Debug.Log("[KD_TreatPouch] 段階 " + hillsideStage.ToString() + ":" + planned + " は意図的な未導入");

            HillsideHideBalance();
        }

        /// <summary>残高 (ledger) が無い段階では「読み込み中...」を出し続けないよう、ヘッダの残高を隠す (IMP-PRG-001 で戻す)。</summary>
        private void HillsideHideBalance()
        {
            if (balanceLabel == null || ledger != null) return;
            if (balanceLabel.gameObject.activeSelf) balanceLabel.gameObject.SetActive(false);
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
                HillsideClearAll(rowHintLabels);
                if (putAwayButton != null && putAwayButton.activeSelf) putAwayButton.SetActive(false);
                if (statusLabel != null) statusLabel.text = "";
                return true;
            }
            if (_tab == TabAcc && accessories == null)
            {
                HillsidePreparingRows(accRowLabels, L("KD_TreatPouch.WordPreparing"));
                HillsideHideRows(fxLabels);   // Page_Acc の Content に並ぶ撫でFX の行 (FxRow0..) も隠す
                HillsideHideAll(accButtons);
                HillsideHideAll(accGiftButtons);
                HillsideClearAll(accRowSubLabels);
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
