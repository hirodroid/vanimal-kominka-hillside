using TMPro;
using UdonSharp;
using UnityEngine;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (Collar)。裏山改変 (IMP-DOG-050、2026-10-13 ユーザー決定「推奨で」): 首輪とリードのおそろいセット。
    ///
    /// かざりタブの商品一覧の面 (カタログ階層) の撫でFX の行の後ろに「首輪とリード」の小見出しと 5 行 (基本 + 4 セット) を出す
    /// (撫でFX の行と同じ作り。行と見出しは Collar/1 が Content に足す。Menu/7 は周回版で実行禁止なのでビルダーには載せない)。
    ///
    /// - 行の [えらぶ] (所持) → セットの面: 1 行目 = 自分のリード (カタログの _RequestSelect。保存は PlayerData Sel.collar)、
    ///   2〜7 行目 = 犬 6 匹 (KH_CollarPattern._Request。Owner が所持と 60 秒を確かめて同期)。かざりの AccRow を使い回す。
    /// - 行の [買う] (未所持) → 共通の確認カード (kind ConfirmCollar、DES-UI-005) → [購入] でカタログの _RequestBuy
    ///   (払う → 解放 → リードの柄として選ぶ。犬の首輪は次の明示の操作で変える)。pt が減る口は _CollarConfirmBuy の 1 か所。
    /// - 戻るは かざりの _AccBack (セットの面 → 一覧)。
    /// 部品が未配線 (collarPatterns == null) なら何も描かない。</summary>
    public partial class KD_TreatPouch
    {
        /// <summary>確認カードの種別 (Confirm.cs の Confirm* の続き。ToyPattern 17 と重ならない値)。</summary>
        public const int ConfirmCollar = 18;
        /// <summary>セットの面の行数: 自分のリード 1 + 犬 6。</summary>
        private const int CollarPageRows = 7;

        [Header("裏山: 首輪とリード (IMP-DOG-050、Collar/1 が配線)")]
        [Tooltip("犬 6 匹の首輪の柄の正本 (KH_Dogs/KH_CollarPattern、Manual)。商品の表もここから読む")]
        public KominkaHillside.KH_CollarPattern collarPatterns;
        [Tooltip("小見出しのセル 3 つ: [0] 段の頭をそろえる空き (前のセルが奇数のときだけ出す) / [1] 見出し / [2] 右の空き")]
        public GameObject[] collarHeadRows;
        public TextMeshProUGUI collarHeadLabel;
        [Tooltip("一覧の行 (CollarRow0..4)。行 i = KH_CollarPattern の表の i 番目")]
        public TextMeshProUGUI[] collarLabels;
        public TextMeshProUGUI[] collarSubLabels;
        public GameObject[] collarButtons;
        public UnityEngine.UI.Image[] collarButtonImages;
        public TextMeshProUGUI[] collarButtonLabels;

        /// <summary>開いているセットの面 (表の添字。-1 = 一覧)。</summary>
        private int _collarPage = -1;
        /// <summary>確認カードを開いている行 (-1 = 閉じている)。</summary>
        private int _collarConfirm = -1;

        public void _Collar0() { CollarRow(0); }
        public void _Collar1() { CollarRow(1); }
        public void _Collar2() { CollarRow(2); }
        public void _Collar3() { CollarRow(3); }
        public void _Collar4() { CollarRow(4); }

        /// <summary>一覧の行。所持 → セットの面へ / 未所持 → 確認カード (買えない間も開く。理由はカードの注記)。</summary>
        private void CollarRow(int row)
        {
            if (_tab != TabAcc || _settingsOpen || _welcome || !HillsideAccCatalogLevel() || collarPatterns == null) return;
            KD_ShopItem it = collarPatterns.ItemAt(row);
            if (it == null) return;
            // 確認カードは共有 1 枚。かざり・撫でFX の確認を閉じる
            _accConfirm = false; _accGiftRow = -1; _fxConfirmRow = -1;
            if (it.Owned())
            {
                _collarConfirm = -1; _HideConfirm(ConfirmCollar);
                _collarPage = row;
                Redraw(); ResetAccessoryScroll();
                return;
            }
            _collarConfirm = row;
            _ShowItemConfirm(ConfirmCollar, it, L("collar.setBody"));
            Redraw();
        }

        /// <summary>確認カードの [購入] (Confirm.cs の _ConfirmYes が kind ConfirmCollar のとき)。押した時点で確かめ直す。ローカル専用。</summary>
        public void _CollarConfirmBuy()
        {
            int row = _collarConfirm;
            _collarConfirm = -1;
            _HideConfirm(ConfirmCollar);
            KD_ShopItem it = collarPatterns == null ? null : collarPatterns.ItemAt(row);
            if (it == null || catalog == null || it.ConfirmationState(false) != KD_ShopItem.ConfirmBuy)
            { Feedback("KD_ShopShelf.WordCantBuy", false); Redraw(); return; }
            bool bought = catalog._RequestBuy(it.productKey);   // 払う → 解放 → リードの柄として選ぶ (KH_CollarLeash が描く)
            Feedback(bought ? "KD_ShopShelf.WordBought" : "KD_ShopShelf.WordCantBuy", bought);
            Redraw();
        }

        /// <summary>確認カードの [やめる] と暗幕。ローカル専用。</summary>
        public void _CollarConfirmNo()
        {
            _collarConfirm = -1;
            _HideConfirm(ConfirmCollar);
            Redraw();
        }

        /// <summary>かざりの _AccBack から: セットの面なら一覧へ戻して true。</summary>
        private bool CollarBack()
        {
            if (_collarPage < 0) return false;
            _collarPage = -1;
            Redraw(); ResetAccessoryScroll();
            return true;
        }

        /// <summary>かざりの AccRow から: セットの面の行 (0 = 自分のリード、1..6 = 犬) なら処理して true。</summary>
        private bool CollarPageRow(int row)
        {
            if (_collarPage < 0) return false;
            KD_ShopItem it = collarPatterns == null ? null : collarPatterns.ItemAt(_collarPage);
            if (it == null || !it.Owned()) { _collarPage = -1; Redraw(); return true; }
            if (row == 0)
            {
                if (catalog == null || CollarLeashKey() == it.productKey) return true;
                catalog._RequestSelect(it.productKey);   // → KH_CollarLeash._OnShopSelectionChanged (保存は PlayerData Sel.collar)
                Redraw();
                return true;
            }
            int dog = row - 1;
            if (dog < 0 || dog >= KominkaHillside.KH_CollarPattern.DogCount) return true;
            if (collarPatterns.PatternOf(dog) == _collarPage || !collarPatterns.CanChange(dog)) return true;
            collarPatterns._Request(dog, _collarPage);
            Redraw();
            return true;
        }

        /// <summary>自分のリードの柄 (カタログの選択。復元前・未選択は表の [0] = 基本)。</summary>
        private string CollarLeashKey()
        {
            string k = catalog == null ? "" : catalog.SelectedProductOf(KominkaHillside.KH_CollarPattern.CategoryKey);
            return string.IsNullOrEmpty(k) && collarPatterns != null ? collarPatterns.ProductAt(0) : k;
        }

        private string CollarNameAt(int option)
        {
            KD_ShopItem it = collarPatterns == null ? null : collarPatterns.ItemAt(option);
            return it == null ? "-" : L(it.displayNameKey);
        }

        /// <summary>RedrawAcc の先頭から: セットの面を描いて true。面でない / 閉じた (未所持になった等) なら false で通常の描画へ。</summary>
        private bool RedrawCollarPage()
        {
            if (_accProduct >= 0 || _accSetMenu != 0) _collarPage = -1;   // ライブの詳細などから飾りの面へ直接来た
            if (_collarPage < 0) return false;
            KD_ShopItem it = collarPatterns == null ? null : collarPatterns.ItemAt(_collarPage);
            if (it == null || !it.Owned()) { _collarPage = -1; return false; }

            _accConfirm = false; _accGiftRow = -1;
            _HideConfirm(ConfirmAcc);
            UpdateAccessoryPreview(false, -1);
            RedrawFxRows(false);
            RedrawCollarRows(false);
            if (accBackButton != null && !accBackButton.activeSelf) accBackButton.SetActive(true);
            if (accHeaderLabel != null) accHeaderLabel.text = L(it.displayNameKey);

            string leash = CollarLeashKey();
            bool catalogReady = catalog != null && catalog.Resolved();
            for (int i = 0; i < accRows.Length; i++)
            {
                GameObject rowGo = accRows[i];
                if (rowGo == null) continue;
                bool live = i < CollarPageRows;
                if (rowGo.activeSelf != live) rowGo.SetActive(live);
                if (!live) continue;

                string label, sub, button;
                bool enabled = false;
                if (i == 0)
                {
                    label = L("collar.myLeash");
                    sub = CollarNameAt(collarPatterns.IndexOfProduct(leash));
                    if (leash == it.productKey) button = L("KD_ShopShelf.WordInUse");
                    else if (!catalogReady) button = L("KD_ShopShelf.WordLoading");
                    else { button = L("KD_TreatPouch.WordAccPut"); enabled = true; }
                }
                else
                {
                    int dog = i - 1;
                    label = ToyNameAt(dog);
                    int now = collarPatterns.PatternOf(dog);
                    sub = CollarNameAt(now);
                    int wait = collarPatterns.RemainingSeconds(dog);
                    if (collarPatterns.requestState == 1) button = L("accessory.pending");
                    else if (now == _collarPage) button = L("KD_ShopShelf.WordInUse");
                    else if (wait > 0) button = L("KD_TreatPouch.WordAccWaitPrefix") + " " + wait.ToString() + L("KD_TreatPouch.WordLiveSeconds");
                    else { button = L("KD_TreatPouch.WordAccPut"); enabled = true; }
                    if (collarPatterns.requestDog == dog && collarPatterns.requestOption == _collarPage)
                    {
                        if (collarPatterns.requestState == 3) sub = L("accessory.rejected");
                        else if (collarPatterns.requestState == 4) sub = L("accessory.unknown");
                    }
                }

                if (accRowLabels != null && i < accRowLabels.Length && accRowLabels[i] != null)
                {
                    accRowLabels[i].text = label;
                    accRowLabels[i].color = Color.white;
                    RectTransform labelRect = (RectTransform)accRowLabels[i].transform;
                    labelRect.anchoredPosition = new Vector2(0f, 32f);
                    labelRect.sizeDelta = new Vector2(480f, 36f);
                }
                if (accRowSubLabels != null && i < accRowSubLabels.Length && accRowSubLabels[i] != null)
                {
                    accRowSubLabels[i].text = sub;
                    RectTransform subRect = (RectTransform)accRowSubLabels[i].transform;
                    subRect.anchoredPosition = new Vector2(-96f, -18f);
                    subRect.sizeDelta = new Vector2(288f, 60f);
                }
                if (accButtonLabels != null && i < accButtonLabels.Length && accButtonLabels[i] != null) accButtonLabels[i].text = button;
                if (accButtonComponents != null && i < accButtonComponents.Length && accButtonComponents[i] != null) accButtonComponents[i].interactable = enabled;
                GameObject giftBtn = (accGiftButtons != null && i < accGiftButtons.Length) ? accGiftButtons[i] : null;
                if (giftBtn != null && giftBtn.activeSelf) giftBtn.SetActive(false);
            }
            return true;
        }

        /// <summary>一覧の小見出しと 5 行 (撫でFX の行の後ろ)。<paramref name="visible"/> = false なら隠し、首輪の確認を閉じる。</summary>
        private void RedrawCollarRows(bool visible)
        {
            if (collarPatterns == null) visible = false;
            int n = collarLabels == null ? 0 : collarLabels.Length;
            if (!visible)
            {
                HillsideHideAll(collarHeadRows);
                for (int i = 0; i < n; i++)
                {
                    if (collarLabels[i] == null) continue;
                    GameObject hideRow = HillsideRowOf(collarLabels[i]);
                    if (hideRow != null && hideRow.activeSelf) hideRow.SetActive(false);
                }
                if (_collarConfirm >= 0) { _collarConfirm = -1; _HideConfirm(ConfirmCollar); }
                return;
            }

            // 2 列の格子なので、見出しが段の左端に来るよう、前に並ぶセルの数が奇数なら空きセル [0] を出す
            int before = _accCatalogCount;
            int headN = hillsideFxHeadRows == null ? 0 : hillsideFxHeadRows.Length;
            for (int h = 0; h < headN; h++) if (hillsideFxHeadRows[h] != null && hillsideFxHeadRows[h].activeSelf) before++;
            int fxN = fxLabels == null ? 0 : fxLabels.Length;
            for (int i = 0; i < fxN; i++)
            {
                if (fxLabels[i] == null) continue;
                GameObject fxRow = HillsideRowOf(fxLabels[i]);
                if (fxRow != null && fxRow.activeSelf) before++;
            }
            int cells = collarHeadRows == null ? 0 : collarHeadRows.Length;
            for (int h = 0; h < cells; h++)
            {
                if (collarHeadRows[h] == null) continue;
                bool on = h != 0 || (before % 2) == 1;
                if (collarHeadRows[h].activeSelf != on) collarHeadRows[h].SetActive(on);
            }
            if (collarHeadLabel != null) collarHeadLabel.text = L("collar.group");

            string leash = CollarLeashKey();
            for (int i = 0; i < n; i++)
            {
                TextMeshProUGUI lbl = collarLabels[i];
                if (lbl == null) continue;
                KD_ShopItem it = collarPatterns.ItemAt(i);
                GameObject rowGo = HillsideRowOf(lbl);
                bool rowOn = it != null;
                if (rowGo != null && rowGo.activeSelf != rowOn) rowGo.SetActive(rowOn);
                if (!rowOn) continue;

                bool owned = it.Owned();
                lbl.text = L(it.displayNameKey);
                string sub;
                if (owned) sub = leash == it.productKey ? L("collar.onLeash") : L("accessory.owned");
                else
                {
                    int state = it.ConfirmationState(false);
                    int gap = it.Shortfall();
                    if (state == KD_ShopItem.ConfirmLoading) sub = L("KD_ShopShelf.WordLoading");
                    else if (gap > 0) sub = it.price.ToString() + " pt / " + L("KD_TreatPouch.WordAccWaitPrefix") + " " + gap.ToString() + " pt";
                    else sub = it.price.ToString() + " pt";
                }
                if (collarSubLabels != null && i < collarSubLabels.Length && collarSubLabels[i] != null) collarSubLabels[i].text = sub;
                if (collarButtonLabels != null && i < collarButtonLabels.Length && collarButtonLabels[i] != null)
                    collarButtonLabels[i].text = owned ? L("KD_TreatPouch.WordAccChoose") : L("KD_TreatPouch.WordBuyHere");
                if (collarButtonImages != null && i < collarButtonImages.Length && collarButtonImages[i] != null)
                    collarButtonImages[i].color = owned ? equipOnColor : recallOnColor;
                GameObject btn = (collarButtons != null && i < collarButtons.Length) ? collarButtons[i] : null;
                if (btn != null && !btn.activeSelf) btn.SetActive(true);
            }

            // 確認カード: 開いたまま状態へ追いつく。別の操作で閉じられた (種別が変わった) / 所持になったら捨てる
            if (_collarConfirm >= 0)
            {
                KD_ShopItem open = collarPatterns.ItemAt(_collarConfirm);
                if (_confirmKind != ConfirmCollar || open == null || open.Owned())
                { if (_confirmKind == ConfirmCollar) _HideConfirm(ConfirmCollar); _collarConfirm = -1; }
                else _ShowItemConfirm(ConfirmCollar, open, L("collar.setBody"));
            }
        }
    }
}
