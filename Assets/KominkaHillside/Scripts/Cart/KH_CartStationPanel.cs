using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M1: 乗り場の板 1 枚 (KH_TravelNotice と同じ WorldSpace Canvas)。同期しない — 表示は KH_CartManager の同期状態から作る。
    ///
    /// - 常時: 題 (cart.title)、注意書き (cart.notice)、[ここから出発 · 自分の行き先] (行き先はさんぽの設定と共用)。
    /// - 合流の行: 「Running かつ受付中」のグループだけを上から詰めて出す。行 N → グループは _Refresh が _rowGroup に覚える。
    ///   表示名は joinLabels (表示名の補助フォント)、「· 行き先 · 人数」は joinInfoLabels (共通フォント) に分けて書く。
    ///   押下には描いたときのグループの世代を添える (解散後に同じ番号で作られた別グループへ合流しない)。
    /// - 結果: cart.result.* を resultLabel に 4 秒出す (_ClearResult は遅延イベントから呼ぶので `_` 付き public)。
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
        public TextMeshProUGUI[] joinInfoLabels;   // 「· <行き先> · <人数>」(共通フォント。補助フォントにはハングル・キリルが無い。レビュー #7)
        [Header("M2 掲示 (Build が配線。null 可)")]
        public TextMeshProUGUI prLabel;            // cart.station.pr (注意書きの下)
        public UnityEngine.UI.RawImage photo;      // 写真 (Runtime は触らない)

        const float ResultSeconds = 4f;
        const string Dot = " · ";

        private int[] _rowGroup, _rowGen;   // 行 N → グループ番号と、描いたときの世代
        private float _resultUntil;
        private string _resultKey = "";

        void Start()
        {
            _Refresh();
        }

        /// <summary>manager が ApplyState (状態の変化)、1 秒ごと (近くにいるとき)、言語の変化で呼ぶ。</summary>
        public void _Refresh()
        {
            EnsureRows();
            if (titleLabel != null) titleLabel.text = T("cart.title");
            if (noticeLabel != null) noticeLabel.text = T("cart.notice");
            if (prLabel != null) prLabel.text = T("cart.station.pr");   // M2: 注意書きの下の 1 行
            if (manager == null)
            {
                if (startButton != null && startButton.activeSelf) startButton.SetActive(false);
                for (int r = 0; joinButtons != null && r < joinButtons.Length; r++) if (joinButtons[r] != null && joinButtons[r].activeSelf) joinButtons[r].SetActive(false);
                return;
            }
            if (startButton != null && !startButton.activeSelf) startButton.SetActive(true);
            if (startLabel != null)
            {
                string dest = DestName(manager._MyDestination());
                startLabel.text = dest.Length > 0 ? T("cart.start") + Dot + dest : T("cart.start");
            }
            int row = 0;
            int rows = joinButtons != null ? joinButtons.Length : 0;
            for (int g = 0; g < KH_CartManager.MaxGroups && row < rows; g++)
            {
                // M2: 受付 OFF・到着中のグループも行に出し、人数の後ろに短い理由を付けてボタンを無効にする (押しても CartClosed)
                int state = manager._GroupState(g);
                if (state == KH_CartManager.GroupFree) continue;
                bool open = state == KH_CartManager.GroupRunning && manager._GroupAccepting(g);
                string tail = open ? "" : Dot + T(state == KH_CartManager.GroupEnding ? "cart.row.ending" : "cart.row.closed");
                _rowGroup[row] = g;
                _rowGen[row] = manager._GroupGen(g);   // 押したときにこの世代を添える (レビュー #4)
                if (joinLabels != null && row < joinLabels.Length && joinLabels[row] != null)
                {
                    VRCPlayerApi p = VRCPlayerApi.GetPlayerById(manager._GroupMasterPlayerId(g));
                    joinLabels[row].text = Utilities.IsValid(p) ? p.displayName : "?";
                }
                if (joinInfoLabels != null && row < joinInfoLabels.Length && joinInfoLabels[row] != null)
                    joinInfoLabels[row].text = "· " + DestName(manager._GroupDestination(g)) + Dot + manager._GroupCount(g).ToString() + tail;
                if (joinButtons[row] != null)
                {
                    if (!joinButtons[row].activeSelf) joinButtons[row].SetActive(true);
                    UnityEngine.UI.Button b = joinButtons[row].GetComponent<UnityEngine.UI.Button>();
                    if (b != null && b.interactable != open) b.interactable = open;
                }
                row++;
            }
            for (int r = row; r < rows; r++)
            {
                _rowGroup[r] = -1; _rowGen[r] = -1;
                if (joinButtons[r] != null && joinButtons[r].activeSelf) joinButtons[r].SetActive(false);
            }
            // 結果の文言は言語が変わったら引き直す
            if (resultLabel != null && _resultKey.Length > 0 && Time.time < _resultUntil) resultLabel.text = T(_resultKey);
        }

        public void _PressStart()
        {
            if (manager == null) return;
            int code = manager._Start(spot);
            if (code != KH_CartManager.CartOk) _ShowResult(CodeKey(code));
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
            int code = manager._Join(spot, g, _rowGen[row]);
            if (code != KH_CartManager.CartOk) _ShowResult(CodeKey(code));
        }

        /// <summary>結果の文言を 4 秒出す (manager からも呼ぶ: 出発・合流の確定、返事待ちの時間切れ)。</summary>
        public void _ShowResult(string key)
        {
            if (resultLabel == null || key == null || key.Length == 0) return;
            _resultKey = key;
            resultLabel.text = T(key);
            _resultUntil = Time.time + ResultSeconds;
            SendCustomEventDelayedSeconds(nameof(_ClearResult), ResultSeconds);
        }

        public void _ClearResult()
        {
            if (Time.time < _resultUntil - 0.05f) return;   // 後から出した結果の 4 秒を待つ
            _resultKey = "";
            if (resultLabel != null) resultLabel.text = "";
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
