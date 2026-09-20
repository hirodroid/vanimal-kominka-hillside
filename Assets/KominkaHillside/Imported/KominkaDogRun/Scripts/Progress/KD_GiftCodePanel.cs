using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaDogRun
{
    /// <summary>IMP-PRG-010: 手元メニュー「ポイント」の面 (IMP-INF-006 P11)。
    /// 入手経路 4 行 + ギフトコードの入力欄 + 受け取ったコード。
    /// コードは英数字。正規化 (大文字化・英数字以外を捨てる) → FNV-1a → 定義表のハッシュと照合 →
    /// 台帳の <see cref="KD_ProgressLedger._GrantBonusOnce"/> ("gift.{codeKey}") で 1 人 1 回だけ付与する。
    /// 平文はワールドに置かない (設計: docs/development/design/gift-code.md)。完全ローカル。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_GiftCodePanel : UdonSharpBehaviour
    {
        public const string HashSalt = "KD_Gift_v1:";
        public const string BonusPrefix = "gift.";
        public KD_TreatPouch pouch;
        public KD_ProgressLedger ledger;
        public KD_Language language;
        public GameObject page;
        public TMP_InputField input;
        public Button useButton;
        public TMP_Text heading, sourcesLabel, inputTitle, placeholder, useLabel, band, historyTitle, historyLabel, backLabel;
        /// <summary>定義表 (Editor の KD_GiftCodeDefinitions) から Setup が配る。3 本とも同じ長さ。</summary>
        public string[] codeKeys;
        public int[] codeHashes;
        public int[] codePoints;
        public string[] codeNameKeys;
        public float bandSeconds = 3f;
        private int _returnTab = -1;
        private float _next, _bandUntil;
        private string _bandKey = "";
        private int _bandPoints;

        private string L(string key) { return language != null ? language._Text(key) : key; }

        public void _Open()
        {
            if (pouch == null || page == null) return;
            _returnTab = pouch.CurrentTab();
            _bandKey = ""; _bandUntil = 0f;
            if (input != null) input.SetTextWithoutNotify("");
            pouch._OpenExtraSettings(page, "gift.title");
            Draw();
        }
        public void _Back()
        {
            if (pouch == null) return;
            int tab = _returnTab; _returnTab = -1;
            if (tab == 0) pouch._Tab0(); else if (tab == 1) pouch._Tab1(); else if (tab == 2) pouch._Tab2();
            else if (tab == 3) pouch._Tab3(); else if (tab == 4) pouch._Tab4(); else pouch._Tab5();
        }
        public void _DraftChanged() { _bandKey = ""; Draw(); }

        /// <summary>[使う]。復元前は何もしない (ボタンも押せない)。一致 → 受け取り済み → 付与の順で判定する。</summary>
        public void _Use()
        {
            if (ledger == null || !ledger.Loaded() || input == null) return;
            string normalized = ledger.NormalizeCode(input.text);
            int index = IndexOfCode(normalized);
            if (index < 0) { ShowBand("gift.result.invalid", 0); return; }
            string bonusKey = BonusPrefix + codeKeys[index];
            if (ledger.BonusReceived(bonusKey)) { ShowBand("gift.result.used", 0); return; }
            if (!ledger._GrantBonusOnce(bonusKey, codePoints[index])) { ShowBand("gift.result.failed", 0); return; }
            input.SetTextWithoutNotify("");
            ShowBand("gift.result.ok", codePoints[index]);
        }
        /// <summary>正規化済みの文字列を定義表と照合する。空・短すぎる入力は照合しない。</summary>
        public int IndexOfCode(string normalized) // net: callable-ok: read only
        {
            if (string.IsNullOrEmpty(normalized) || normalized.Length < 4 || codeHashes == null || ledger == null) return -1;
            int hash = ledger.Fnv1a31(HashSalt + normalized);
            for (int i = 0; i < codeHashes.Length; i++) if (codeHashes[i] != 0 && codeHashes[i] == hash) return i;
            return -1;
        }
        private void ShowBand(string key, int points)
        {
            _bandKey = key; _bandPoints = points; _bandUntil = Time.time + Mathf.Max(bandSeconds, 1f);
            // 棚と同じ音 (成功 / 却下)。板の AudioSource とクリップは pouch が持つ
            if (pouch != null && pouch.uiAudio != null)
            {
                AudioClip clip = points > 0 ? pouch.uiSuccessClip : pouch.uiRejectClip;
                if (clip != null) pouch.uiAudio.PlayOneShot(clip);
            }
            Draw();
        }

        private void Update()
        {
            if (page == null || !page.activeInHierarchy || Time.time < _next) return;
            _next = Time.time + .5f;
            Draw();
        }

        private void Draw()
        {
            if (page == null || !page.activeInHierarchy) return;
            bool loaded = ledger != null && ledger.Loaded();
            if (heading != null) heading.text = L("gift.heading");
            if (sourcesLabel != null)
            {
                string initial = ledger != null ? ledger.initialPoints.ToString() : "?";
                string keeper = ledger != null ? ledger.keeperPoints.ToString() : "?";
                sourcesLabel.text =
                    L("gift.source.initial") + "  +" + initial + " pt  " + State(loaded && ledger.BonusReceived(KD_ProgressLedger.BonusInitial), loaded) + "\n"
                    + L("gift.source.keeper") + "  +" + keeper + " pt  " + State(loaded && ledger.BonusReceived(KD_ProgressLedger.BonusKeeper), loaded) + "\n"
                    + L("gift.source.stay") + "\n"
                    + L("gift.source.code");
            }
            if (inputTitle != null) inputTitle.text = L("gift.input.title");
            if (placeholder != null) placeholder.text = L("gift.input.placeholder");
            if (useLabel != null) useLabel.text = L("gift.use");
            if (backLabel != null) backLabel.text = L("gift.back");
            if (historyTitle != null) historyTitle.text = L("gift.history");
            if (input != null) input.interactable = loaded;
            if (useButton != null) useButton.interactable = loaded;
            if (band != null)
            {
                if (!loaded) band.text = L("KD_ShopShelf.WordLoading") + "...";
                else if (_bandKey != "" && Time.time < _bandUntil)
                    band.text = _bandPoints > 0 ? L(_bandKey).Replace("{0}", _bandPoints.ToString()) : L(_bandKey);
                else band.text = "";
            }
            if (historyLabel != null)
            {
                string list = "";
                if (loaded && codeKeys != null)
                    for (int i = 0; i < codeKeys.Length; i++)
                        if (ledger.BonusReceived(BonusPrefix + codeKeys[i]))
                            list += (list == "" ? "" : "  /  ") + L(codeNameKeys[i]) + " +" + codePoints[i].ToString() + " pt";
                historyLabel.text = list == "" ? L("gift.history.none") : list;
            }
        }
        private string State(bool received, bool loaded)
        {
            return !loaded ? "" : "[" + L(received ? "gift.received" : "gift.pending") + "]";
        }
    }
}
