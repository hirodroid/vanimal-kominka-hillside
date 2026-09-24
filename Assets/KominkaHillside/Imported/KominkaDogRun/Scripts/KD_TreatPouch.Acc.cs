using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (Acc)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_TreatPouch
    {
        // Product/set definitions are fixed during a visit; ownership and UI text are not cached.
        // Reference checks also let Editor fixtures replace the definition tables safely.
        private int[] _accCatalogRows;
        private int _accCatalogCount;
        private int[] _accCatalogProductsSource;
        private int[] _accCatalogSetsSource;
        private int[] _accCatalogMembersSource;
        private KD_AccessoryBox _accCatalogOwner;
        private bool _accPreviewReady;
        private bool _accPreviewPage;
        private int _accPreviewSelection;
        private GameObject[] _accPreviewDogsSource;
        private GameObject[] _accPreviewItemsSource;
        private KD_AccessoryBox _accPreviewOwner;

        /// <summary>**タブの外を描いてから、開いているタブ 1 枚だけを描く。**
        ///
        /// 隠れているタブの行は 1 つも触らない (IMP-TRT-004) — 0.5 秒ごとに回るので、
        /// 3 面ぶん毎回作ると**見えない文字列を作るためだけに Udon が 3 倍回る。**</summary>
        // ============================================================
        // かざりタブ (DES-DOG-004)
        //
        // **同じ 6 行を 2 ページで使い回す。** 1 ページ目 = 飾りを選ぶ /
        // 2 ページ目 = その飾りをどの犬へ着けるか。
        // 台帳 (`KD_AccessoryBox`) を読むだけで、犬の Ownership には触らない。
        // ============================================================

        public void _Acc0() { AccRow(0); }
        public void _Acc1() { AccRow(1); }
        public void _Acc2() { AccRow(2); }
        public void _Acc3() { AccRow(3); }
        public void _Acc4() { AccRow(4); }
        public void _Acc5() { AccRow(5); }
        public void _Acc6() { AccRow(6); }
        public void _Acc7() { AccRow(7); }
        public void _Acc8() { AccRow(8); }
        public void _Acc9() { AccRow(9); }
        public void _Acc10() { AccRow(10); }
        public void _Acc11() { AccRow(11); }
        public void _Acc12() { AccRow(12); }
        public void _Acc13() { AccRow(13); }
        public void _Acc14() { AccRow(14); }
        public void _Acc15() { AccRow(15); }

        public void _AccGift0() { AccGift(0); }
        public void _AccGift1() { AccGift(1); }
        public void _AccGift2() { AccGift(2); }
        public void _AccGift3() { AccGift(3); }
        public void _AccGift4() { AccGift(4); }
        public void _AccGift5() { AccGift(5); }
        public void _AccGift6() { AccGift(6); }
        public void _AccGift7() { AccGift(7); }
        public void _AccGift8() { AccGift(8); }
        public void _AccGift9() { AccGift(9); }
        public void _AccGift10() { AccGift(10); }
        public void _AccGift11() { AccGift(11); }
        public void _AccGift12() { AccGift(12); }
        public void _AccGift13() { AccGift(13); }
        public void _AccGift14() { AccGift(14); }
        public void _AccGift15() { AccGift(15); }

        /// <summary>贈る確認を開いている商品 (accItems の添字)。購入の確認 (_accConfirm) とは別に持つ — 所有で閉じない (IMP-PRG-012)。</summary>
        private int _accGiftRow = -1;

        /// <summary>裏山改変 (IMP-DOG-015): CE セットの [ギフト] を出すか。**裏山ではギフトが未決定** (Issue の「やらないこと」) なので false。
        /// 決まったらここを true にする (古民家の IMP-PRG-012 の経路はそのまま残してある)。</summary>
        private const bool HillsideAccGiftEnabled = false;

        /// <summary>1 ページ目 (商品一覧) の行の [ギフト] (IMP-PRG-012)。行 = 一覧の何行目か (AccRow と同じ添字)。
        /// 所有済みの買い切り CE セットだけ。贈る確認カードを開くだけで、Listing はカードの [購入画面へ] が開く。</summary>
        private void AccGift(int row)
        {
            if (_accProduct >= 0 || _accSetMenu != 0) return;   // 商品一覧の面だけ
            EnsureAccessoryCatalog();
            if (_accCatalogRows == null || row < 0 || row >= _accCatalogCount) return;
            int product = _accCatalogRows[row];
            if (!AccCanGift(product)) return;
            _accConfirm = false; _accGiftRow = product;
            Redraw();
        }

        /// <summary>[ギフト] を出す条件: 買い切りの CE セットを所有していて、購入情報が届いていて、販売中 (IMP-PRG-012)。
        /// セットの中身 (単品) は商品ではないので出さない。所有判定は KD_ShopItem のまま。</summary>
        private bool AccCanGift(int product)
        {
            // 裏山改変 (IMP-DOG-015 「やらないこと」): CE セットの [ギフト] は裏山では未決定。決まるまで出さない (行の [ギフト] は隠れたまま)
            if (!HillsideAccGiftEnabled) return false;
            if (accItems == null || product < 0 || product >= accItems.Length) return false;
            if (accProductIds == null || product >= accProductIds.Length || AccIsSetMember(accProductIds[product])) return false;
            return CanGift(accItems[product]);   // 条件はライブと同じ 1 か所 (KD_TreatPouch.Live.CanGift)
        }

        /// <summary>犬 → セットの中身/装着方法 → 商品一覧へ一段ずつ戻る。</summary>
        public void _AccBack()
        {
            _accConfirm = false; _accGiftRow = -1;
            if (_accSet >= 0)
            {
                if (_accProduct >= 0) _accSetMenu = _accProduct == _accSet ? 1 : 2;
                else if (_accSetMenu == 2) _accSetMenu = 1;
                else { _accSet = -1; _accSetMenu = 0; }
            }
            _accProduct = -1;
            Redraw();
            ResetAccessoryScroll();
        }

        private void ResetAccessoryScroll()
        {
            if (tabScrolls != null && TabAcc < tabScrolls.Length && tabScrolls[TabAcc] != null)
                tabScrolls[TabAcc].verticalNormalizedPosition = 1f;
        }

        public void _AccConfirmNo() { _accConfirm = false; _accGiftRow = -1; Redraw(); }

        /// <summary>カードの [購入] / [購入画面へ]。贈る確認 (_accGiftRow) からも来る — 所有済みでも同じ Listing を開く (IMP-PRG-012)。</summary>
        public void _AccConfirmBuy()
        {
            if (_accGiftRow >= 0)
            {
                int product = _accGiftRow;
                _accGiftRow = -1;
                if (_tab == TabAcc && !_settingsOpen && panelRoot != null && panelRoot.activeInHierarchy && AccCanGift(product))
                    accItems[product]._OpenStore();
                Redraw();
                return;
            }
            if (!_accConfirm || _tab != TabAcc || _settingsOpen || panelRoot == null || !panelRoot.activeInHierarchy) return;
            if (accItems == null || _accProduct < 0 || _accProduct >= accItems.Length) return;
            if (accProductIds == null || _accProduct >= accProductIds.Length) return;
            if (AccIsSetMember(accProductIds[_accProduct])) return;
            KD_ShopItem item = accItems[_accProduct];
            if (LiveSaleClosed(item)) { _accConfirm = false; Redraw(); return; }   // 期間限定のセット: 販売終了後は買えない
            int state = item == null ? KD_ShopItem.ConfirmLoading : item.ConfirmationState(false);
            // CE (王様 / 夏休みセット): Storefront を開くだけ。所有は購入イベントで戻る (IMP-FET-010)
            if (state == KD_ShopItem.ConfirmSpecial) { _accConfirm = false; item._OpenStore(); Redraw(); return; }
            if (state != KD_ShopItem.ConfirmBuy) { Redraw(); return; }
            _accConfirm = false; // Consume this confirmation before invoking purchase/listener callbacks.
            item._Buy();
            Redraw(); // Buying never equips a dog; the next explicit dog action does that.
        }

        private void AccRow(int row)
        {
            if (accessories == null) return;

            if (_accSetMenu > 0)
            {
                if (!AccOwned(_accSet)) return;
                if (_accSetMenu == 1)
                {
                    if (row == 0) { _accProduct = _accSet; _accSetMenu = 0; }
                    else if (row == 1) _accSetMenu = 2;
                    else return;
                }
                else
                {
                    int member = AccMemberRow(row);
                    if (member < 0) return;
                    _accProduct = member; _accSetMenu = 0;
                }
                Redraw(); ResetAccessoryScroll(); return;
            }

            // ---- 1 ページ目: 飾りを選ぶ ----
            if (_accProduct < 0)
            {
                int choice = AccCatalogRow(row);
                if (choice < 0) return;
                // Browsing and trying on never require ownership.
                _accProduct = choice;
                Redraw();
                ResetAccessoryScroll();
                return;
            }

            // ---- 2 ページ目: どの犬へ着けるか ----
            int dog = row;
            if (dog < 0 || dog >= accessories.dogCount) return;
            if (accProductIds == null || _accProduct >= accProductIds.Length) return;
            if (_accConfirm) return;
            if (AccDogBlocked(dog)) return;   // 裏山改変 (IMP-DOG-015 案 C): 首輪と重なる組は着けない・買う入口にもしない (行は暗い)
            if (!AccOwned(_accProduct))
            {
                if (AccIsSetMember(accProductIds[_accProduct])) return;
                _accConfirm = true; Redraw(); return;
            }

            int product = accProductIds[_accProduct];
            if (accessories.IndexOfSet(product) >= 0)
            {
                accessories._EquipSet(dog, product); Redraw(); return;
            }
            int slot = accessories.SlotOfProduct(product);
            if (slot < 0) return;

            // **待ち時間中は無言で弾かない** — 行のボタンが既に「あと N 秒」で暗い。
            // ここでも弾くのは二重の安全 (台帳側も CanChange を見る)
            if (!accessories.CanChange(dog)) return;

            if (accessories.EquippedOf(dog, slot) == product) accessories._Clear(dog, slot);
            else accessories._Equip(dog, product);

            Redraw();
        }

        /// <summary>その飾りを自分が使えるか。無料品も商品参照を必要とする。</summary>
        private bool AccOwned(int row)
        {
            if (accItems == null || row < 0 || row >= accItems.Length) return false;
            KD_ShopItem it = accItems[row];
            if (it == null) return false;
            return it.Owned();
        }

        private string AccNameAt(int i)
        {
            if (accNameKeys == null || i < 0 || i >= accNameKeys.Length) return "-";
            string s = L(accNameKeys[i]);
            return (s == null || s.Length == 0) ? "-" : s;
        }

        private bool AccIsSetMember(int product)
        {
            if (accessories == null || accessories.setIds == null) return false;
            for (int s = 0; s < accessories.setIds.Length; s++)
                if (accessories.SelectionContains(accessories.setIds[s], product)) return true;
            return false;
        }

        // Catalog arrays also hold member metadata. Cache only the visible-row to catalog-index map.
        private void EnsureAccessoryCatalog()
        {
            int[] sets = accessories == null ? null : accessories.setIds;
            int[] members = accessories == null ? null : accessories.setMembers;
            if (_accCatalogRows != null && _accCatalogProductsSource == accProductIds
                && _accCatalogOwner == accessories && _accCatalogSetsSource == sets && _accCatalogMembersSource == members) return;

            _accCatalogProductsSource = accProductIds;
            _accCatalogOwner = accessories;
            _accCatalogSetsSource = sets;
            _accCatalogMembersSource = members;
            _accCatalogRows = new int[accProductIds == null ? 0 : accProductIds.Length];
            _accCatalogCount = 0;
            for (int i = 0; accProductIds != null && i < accProductIds.Length; i++)
                if (!AccIsSetMember(accProductIds[i]))
                    _accCatalogRows[_accCatalogCount++] = i;
            _accPreviewReady = false;
        }

        private int AccCatalogRow(int row)
        {
            EnsureAccessoryCatalog();
            return row >= 0 && row < _accCatalogCount ? _accCatalogRows[row] : -1;
        }

        private void UpdateAccessoryPreview(bool page2, int selection)
        {
            if (_accPreviewReady && _accPreviewPage == page2 && _accPreviewSelection == selection
                && _accPreviewDogsSource == accPreviewDogs && _accPreviewItemsSource == accPreviewItems
                && _accPreviewOwner == accessories) return;

            _accPreviewReady = true;
            _accPreviewPage = page2;
            _accPreviewSelection = selection;
            _accPreviewDogsSource = accPreviewDogs;
            _accPreviewItemsSource = accPreviewItems;
            _accPreviewOwner = accessories;
            for (int i = 0; accPreviewDogs != null && i < accPreviewDogs.Length; i++)
                if (accPreviewDogs[i] != null && accPreviewDogs[i].activeSelf != page2) accPreviewDogs[i].SetActive(page2);
            int dogCount = accessories == null ? 0 : accessories.dogCount;
            for (int i = 0; accPreviewItems != null && i < accPreviewItems.Length; i++)
            {
                GameObject preview = accPreviewItems[i];
                if (preview == null) continue;
                bool visible = page2 && dogCount > 0 && i / dogCount < accessories.productIds.Length
                    && accessories.SelectionContains(selection, accessories.productIds[i / dogCount])
                    && !accessories.Blocked(i % dogCount, accessories.productIds[i / dogCount]);   // 裏山改変: 首輪と重なる組は試着にも出さない
                if (preview.activeSelf != visible) preview.SetActive(visible);
            }
        }

        private int AccMemberRow(int row)
        {
            if (row < 0 || row >= 3 || _accSet < 0 || accProductIds == null || _accSet >= accProductIds.Length || accessories == null) return -1;
            int set = accessories.IndexOfSet(accProductIds[_accSet]);
            if (set < 0 || accessories.setMembers == null || set * 3 + row >= accessories.setMembers.Length) return -1;
            return AccRowOfProduct(accessories.setMembers[set * 3 + row]);
        }

        private void RedrawAcc()
        {
            if (accRows == null) return;
            EnsureAccessoryCatalog();

            // Ownership may arrive after purchase or restoration while the preview is open.
            if (_accSet < 0 && _accProduct >= 0 && accProductIds != null && _accProduct < accProductIds.Length
                && accessories != null && accessories.IndexOfSet(accProductIds[_accProduct]) >= 0 && AccOwned(_accProduct))
            { _accSet = _accProduct; _accProduct = -1; _accSetMenu = 1; ResetAccessoryScroll(); }
            if (_accSet >= 0 && !AccOwned(_accSet))
            { _accSet = -1; _accSetMenu = 0; _accProduct = -1; _accConfirm = false; }

            bool page2 = _accProduct >= 0 && accProductIds != null && _accProduct < accProductIds.Length;
            // 贈る確認 (IMP-PRG-012) は商品一覧の面だけ。面を離れた / 贈れなくなったら閉じる (所有では閉じない)
            if (_accGiftRow >= 0 && (page2 || _accSetMenu != 0 || !AccCanGift(_accGiftRow))) _accGiftRow = -1;
            KD_ShopItem previewItem = page2 && accItems != null && _accProduct < accItems.Length ? accItems[_accProduct] : null;
            if (previewItem == null || previewItem.Owned() || LiveSaleClosed(previewItem)) _accConfirm = false;
            int previewSelection = page2 ? accProductIds[_accProduct] : -1;
            UpdateAccessoryPreview(page2, previewSelection);
            // P13: 板の手前のカード 1 枚 (帯は廃止)。中身はセットなら「3点セット」
            if (page2 && _accConfirm && previewItem != null)
                _ShowItemConfirm(ConfirmAcc, previewItem, previewItem.categoryKey == "live" ? LiveBundleLabel(previewItem)
                    : accessories != null && accProductIds != null && accessories.IndexOfSet(accProductIds[_accProduct]) >= 0 ? L("accessory.set") : "");
            else if (_accGiftRow >= 0)
            {
                KD_ShopItem giftItem = accItems[_accGiftRow];
                _ShowGiftConfirm(ConfirmAcc, giftItem, giftItem.categoryKey == "live" ? LiveBundleLabel(giftItem) : L("accessory.set"));
            }
            else _HideConfirm(ConfirmAcc);

            bool canBack = page2 || _accSetMenu > 0;
            if (accBackButton != null && accBackButton.activeSelf != canBack)
                accBackButton.SetActive(canBack);
            if (accHeaderLabel != null)
                accHeaderLabel.text = page2 ? (_accConfirm && previewItem != null ? L(previewItem.displayNameKey) : AccNameAt(_accProduct)) : _accSetMenu > 0 ? AccNameAt(_accSet) : L("KD_TreatPouch.WordTabAcc");

            int shown = page2
                ? (accessories == null ? 0 : accessories.dogCount)
                : _accSetMenu == 1 ? 2 : _accSetMenu == 2 ? 3 : _accCatalogCount;

            for (int i = 0; i < accRows.Length; i++)
            {
                GameObject rowGo = accRows[i];
                if (rowGo == null) continue;
                bool live = i < shown;
                if (rowGo.activeSelf != live) rowGo.SetActive(live);
                if (!live) continue;

                string label, sub, button;
                bool enabled;
                bool gift = false;

                if (page2) AccDogRow(i, out label, out sub, out button, out enabled);
                else if (_accSetMenu == 1)
                {
                    label = L(i == 0 ? "accessory.chooseSet" : "accessory.chooseIndividual");
                    sub = L(i == 0 ? "accessory.keepOther" : "accessory.chooseMember");
                    button = L("KD_TreatPouch.WordAccChoose"); enabled = AccOwned(_accSet);
                }
                else
                {
                    int choice = _accSetMenu == 2 ? AccMemberRow(i) : _accCatalogRows[i];
                    AccProductRow(choice, out label, out sub, out button, out enabled);
                    if (_accSetMenu == 2) sub = choice >= 0 && accSlotNameKeys != null && choice < accSlotNameKeys.Length ? L(accSlotNameKeys[choice]) : "";
                    else if (choice >= 0 && AccOwned(choice)) { button = L("KD_TreatPouch.WordAccPut"); gift = AccCanGift(choice); }   // 所有済みの買い切り CE セットは [ギフト] も (IMP-PRG-012)
                }

                if (accRowLabels != null && i < accRowLabels.Length && accRowLabels[i] != null)
                {
                    accRowLabels[i].text = label;
                    // 裏山改変 (IMP-DOG-015 案 C): 首輪と重なる犬の行は暗く (理由はサブ文)。ほかの行は白へ戻す
                    Color labelColor = page2 && AccDogBlocked(i) ? HillsideAccBlockedColor : Color.white;
                    accRowLabels[i].color = labelColor;
                    RectTransform labelRect = (RectTransform)accRowLabels[i].transform;
                    // [ギフト] (右上 160×40、x 86..246) が出る行は名前の幅を詰める (左寄せなので文字は動かない)
                    labelRect.anchoredPosition = new Vector2(page2 ? 80f : gift ? -90f : 0f, 32f);
                    labelRect.sizeDelta = new Vector2(page2 ? 320f : gift ? 300f : 480f, 36f);
                }
                if (accRowSubLabels != null && i < accRowSubLabels.Length && accRowSubLabels[i] != null)
                {
                    accRowSubLabels[i].text = sub;
                    RectTransform subRect = (RectTransform)accRowSubLabels[i].transform;
                    subRect.anchoredPosition = new Vector2(page2 ? 0f : -96f, -18f);
                    subRect.sizeDelta = new Vector2(page2 ? 136f : 288f, 60f);
                }
                if (accButtonLabels != null && i < accButtonLabels.Length && accButtonLabels[i] != null)
                    accButtonLabels[i].text = button;
                if (accButtonComponents != null && i < accButtonComponents.Length
                    && accButtonComponents[i] != null)
                    accButtonComponents[i].interactable = enabled;
                GameObject giftBtn = (accGiftButtons != null && i < accGiftButtons.Length) ? accGiftButtons[i] : null;
                if (giftBtn != null && giftBtn.activeSelf != gift) giftBtn.SetActive(gift);
                if (gift && accGiftLabels != null && i < accGiftLabels.Length && accGiftLabels[i] != null)
                    accGiftLabels[i].text = L("product.gift");
            }

            // 裏山改変 (IMP-DOG-015 / 計画書 D-2 (a)): 撫でFX の行 (Content の AccRow の後ろの FxRow0..10) は
            // **カタログ階層 (_accSetMenu == 0 かつ _accProduct 未選択) のときだけ**描く。セット中身・装着方法・犬選択では隠す。
            // 下端の 1 行は 撫でFX の結果 → 無ければ支度ポイントの未受取の案内 (KD_TreatPouch.PetFx.cs)
            bool fxLevel = !page2 && _accSetMenu == 0;
            RedrawFxRows(fxLevel);
            HillsideAccFooter(fxLevel);
        }

        /// <summary>1 ページ目の 1 行 — 飾りそのもの。</summary>
        private void AccProductRow(int row, out string label, out string sub,
            out string button, out bool enabled)
        {
            if (row < 0) { label = "-"; sub = ""; button = "-"; enabled = false; return; }
            label = AccNameAt(row);
            sub = (accSlotNameKeys != null && row < accSlotNameKeys.Length && accSlotNameKeys[row] != null)
                ? L(accSlotNameKeys[row]) : "";
            bool owned = AccOwned(row);
            button = L("KD_TreatPouch.WordAccChoose");
            KD_ShopItem item = accItems != null && row < accItems.Length ? accItems[row] : null;
            // P8 (2026-09-16): ライブと同梱のセット (Candy / Pumpkin) は飾り側からもライブが付くことが分かるように
            if (item != null && item.categoryKey == "live") sub = L("accessory.liveSet");
            if (!owned && LiveSaleClosed(item)) { sub += " / " + L("live.saleClosed"); enabled = false; return; }   // 期間限定: 販売終了後は押せない
            sub += " / " + (owned ? L("accessory.owned") : item == null ? "-"
                : item.IsCE() ? (item.ceCredits > 0 ? item.ceCredits.ToString() + " C" : L("KD_ShopShelf.WordSpecial")) : item.price.ToString() + " pt");
            enabled = true;
        }

        /// <summary>2 ページ目の 1 行 — 犬。**「いま何が着いているか」と
        /// 「押すと何が起きるか」を分けて出す** (置き換わるのを押す前に言う)。</summary>
        private void AccDogRow(int dog, out string label, out string sub,
            out string button, out bool enabled)
        {
            label = ToyNameAt(dog);
            sub = "";
            button = L("KD_TreatPouch.WordAccPut");
            enabled = false;

            if (accessories == null || accProductIds == null) return;
            int product = accProductIds[_accProduct];
            bool isSet = accessories.IndexOfSet(product) >= 0;
            int slot = accessories.SlotOfProduct(product);
            if (slot < 0 && !isSet) return;

            // 裏山改変 (IMP-DOG-015 案 C): 首輪と物理的に重なる組 (Lab のバンダナ等) は暗く、理由を出す。所持の有無より先に判定する
            if (AccDogBlocked(dog))
            {
                sub = L("KD_TreatPouch.WordAccNotForDog");
                enabled = false;
                return;
            }

            if (!AccOwned(_accProduct))
            {
                button = L("KD_TreatPouch.WordBuyHere");
                enabled = !_accConfirm;
                return;
            }

            if (accessories.requestState == 1) { button = L("accessory.pending"); return; }
            int now = accessories.EquippedOf(dog, slot);

            // いま何が着いているか
            if (now != KD_AccessoryBox.Unset)
            {
                int idx = AccRowOfProduct(now);
                sub = idx >= 0 ? AccNameAt(idx) : "";
            }

            // **待ち時間が最優先。** 何が着いていても、変えられないなら残り秒を出す
            int wait = accessories.RemainingSeconds(dog);
            if (wait > 0)
            {
                button = L("KD_TreatPouch.WordAccWaitPrefix") + " " + wait.ToString() + L("KD_TreatPouch.WordLiveSeconds");
                enabled = false;
                return;
            }

            enabled = true;
            if (isSet)
            {
                bool equipped = accessories.SetEquipped(dog, product);
                button = L(equipped ? "accessory.equipped" : "accessory.equipSet");
                sub = L("accessory.keepOther");
                if (accessories.requestDog == dog && accessories.requestSelection == product && accessories.requestState == 3) sub = L("accessory.rejected");
                if (accessories.requestDog == dog && accessories.requestSelection == product && accessories.requestState == 4) sub = L("accessory.unknown");
                enabled = !equipped; return;
            }
            if (now == product) button = L("KD_TreatPouch.WordAccTakeOff");
            else if (now != KD_AccessoryBox.Unset) button = L("KD_TreatPouch.WordAccReplaces");
            else button = L("KD_TreatPouch.WordAccPut");
            if (accessories.requestDog == dog && accessories.requestSelection == product && accessories.requestState == 3) sub = L("accessory.rejected");
            if (accessories.requestDog == dog && accessories.requestSelection == product && accessories.requestState == 4) sub = L("accessory.unknown");
        }

        /// <summary>裏山改変 (IMP-DOG-015 案 C): 暗くした犬の行の名前の色。</summary>
        private Color HillsideAccBlockedColor = new Color(0.55f, 0.55f, 0.55f, 1f);

        /// <summary>裏山改変 (IMP-DOG-015 案 C、2026-09-24 ユーザー決定): 犬選択の面で、選んでいる単品がこの犬の首輪と物理的に重なるか。
        /// 除外表の正本は Editor の定数 (KD_AccessorySetupEditor.HillsideCollarExcluded) → KD_AccessoryBox.blockedDogs / blockedProducts。
        /// セットは中身の該当品だけ飛ばして着けられるので false (台帳側の PrepareSelection が飛ばす)。</summary>
        private bool AccDogBlocked(int dog)
        {
            if (accessories == null || accProductIds == null || _accProduct < 0 || _accProduct >= accProductIds.Length) return false;
            int product = accProductIds[_accProduct];
            if (accessories.IndexOfSet(product) >= 0) return false;
            return accessories.Blocked(dog, product);
        }

        /// <summary>恒久 ID から行番号を引く (表示名を出すため)。</summary>
        private int AccRowOfProduct(int product)
        {
            if (accProductIds == null) return -1;
            for (int i = 0; i < accProductIds.Length; i++)
                if (accProductIds[i] == product) return i;
            return -1;
        }

        private void Feedback(string message, bool success)
        {
            _feedbackText = message;
            _feedbackTab = _tab;
            _feedbackUntil = Time.time + 4f;
            AudioClip clip = success ? uiSuccessClip : uiRejectClip;
            if (uiAudio != null && clip != null) uiAudio.PlayOneShot(clip);
        }

        private void PollLiveRequest()
        {
            if (!_awaitingLive || live == null) return;
            int state = live.LocalRequestState;
            if (state == KD_LiveSystem.RequestPending) return;
            _awaitingLive = false;
            // 成立後も開催者・残り時間を表示する。成功・失敗で板を閉じない。
            if (state != KD_LiveSystem.RequestStarted)
            {
                Feedback(state == KD_LiveSystem.RequestUnknown ? "ui.extra.040" : "KD_TreatPouch.WordLiveRejected", false);
                _feedbackTab = TabLive;
            }
        }

    }
}
