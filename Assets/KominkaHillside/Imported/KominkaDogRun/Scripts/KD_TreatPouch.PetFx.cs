using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (PetFx)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_TreatPouch
    {
        // ============================================================
        // パーティクルタブ — 所持品を切り替え、未所持品は確認帯から購入する。
        //
        // 押すとカタログの選択が変わり、既存の `KD_PetFxBox._OnShopSelectionChanged` が
        // そのまま走る。**消費側のコードは 1 行も変えていない。**
        //
        // Pointは台帳、セット同梱品は対応するStoreのListingへ。購入で自動装備しない。
        // ============================================================

        public void _Fx0() { Equip(0); }
        public void _Fx1() { Equip(1); }
        public void _Fx2() { Equip(2); }
        public void _Fx3() { Equip(3); }
        public void _Fx4() { Equip(4); }
        public void _Fx5() { Equip(5); }
        public void _Fx6() { Equip(6); }
        public void _Fx7() { Equip(7); }
        public void _Fx8() { Equip(8); }
        public void _Fx9() { Equip(9); }
        public void _Fx10() { Equip(10); }

        private string FxBundleLabel(KD_ShopItem item)
        {
            if (item == null || !item.IsCE()) return "";
            if (item.productKey == "petfx.candy") return L("fx.bundle.candy");
            if (item.productKey == "petfx.pumpkin") return L("fx.bundle.pumpkin");
            return L("fx.bundle.livePack");
        }

        /// <summary>行を押した。**所持していれば着け替え、買えるなら確認帯を開く。**
        ///
        /// **押した瞬間には払わない** — 2 段階購入は棚と同じ規律で、
        /// 「触るのはパネルを開くだけ、pt が減るのは [購入] を押したときだけ」。</summary>
        private void Equip(int row)
        {
            // 裏山改変 (IMP-DOG-015 / 計画書 D-2): 撫でFX の行は かざりのカタログ階層にだけ出る。それ以外の面からは来ない (念のため弾く)
            if (_tab != TabAcc || _settingsOpen || _welcome || !HillsideAccCatalogLevel()) return;
            KD_ShopItem it = FxAt(row);
            if (it == null || catalog == null) return;
            // 裏山改変: 確認カードは共有 1 枚。撫でFX の確認を開くときは かざりの購入・ギフトの確認を閉じる (同じ面で 2 つ開かない)
            _accConfirm = false; _accGiftRow = -1;

            if (it.Owned())
            {
                _fxConfirmRow = -1;
                bool selected = catalog._RequestSelect(it.productKey);
                Feedback(selected ? "KD_TreatPouch.WordApplied" : "KD_ShopShelf.WordCantSelect", selected);
                Redraw();
                return;
            }

            if (it.IsCE()) { if (it.CePurchasesLoaded()) _fxConfirmRow = row; Redraw(); return; }
            // 買えないものはボタンを隠してあるので普通は来ない。念のため弾く
            if (!it.CanBuy()) { Redraw(); return; }
            _fxConfirmRow = row;
            Redraw();
        }

        /// <summary>確認帯の [購入]。**pt が減る唯一の口。** ローカル専用なので `_` 始まり。</summary>
        public void _FxBuy()
        {
            KD_ShopItem it = FxAt(_fxConfirmRow);
            _fxConfirmRow = -1;
            if (it == null || catalog == null || LiveSaleClosed(it)) { Feedback("KD_ShopShelf.WordCantBuy", false); Redraw(); return; }
            if (it.IsCE()) { it._OpenStore(); Redraw(); return; }
            // **買えても選び直さない。** 買った直後に着くかどうかは別の操作 —
            // 「買う」と「つける」を 1 回の押下で混ぜると、取り違えたときに戻せない
            bool bought = catalog._RequestBuy(it.productKey);
            Feedback(bought ? "KD_ShopShelf.WordBought" : "KD_ShopShelf.WordCantBuy", bought);
            Redraw();
        }

        /// <summary>確認帯の [やめる]。ローカル専用なので `_` 始まり。</summary>
        public void _FxCancel()
        {
            _fxConfirmRow = -1;
            Redraw();
        }

        // ============================================================
        // 裏山改変 (IMP-DOG-015 / 計画書 D-2 (a)(b)(c)): 撫でFX は かざり (TabAcc) へ合流する。
        //
        // 古民家は TabPetFx → RedrawFx / TabAcc → RedrawAcc の排他分岐だった。裏山はタブ 2 が さんぽ なので、
        // 行の描画を RedrawFxRows に分け、RedrawAcc が**カタログ階層 (_accSetMenu == 0 かつ _accProduct 未選択) のときだけ**
        // 表示で呼ぶ (セット中身・装着方法・犬選択の階層では隠す)。確認カードの kind Fx はそのまま (b)。
        // 戻るは かざりの _AccBack だけ (c。撫でFX 専用の戻るは作らない — 撫でFX はカタログ階層で完結する)。
        // 行は 0-b のビルダーが Page_Acc の Content に AccRow0..15 の後ろへ FxRow0..10 として作る (D-2 (d)。既定は非表示)。
        // ============================================================

        [Header("裏山: 撫でFX の群見出し (かざりの Content。FxRow0 の前のセル。無ければ見出し無しで描く)")]
        [Tooltip("群見出しのセル (見出し + 右の空きセル)。カタログ階層で FxRow と一緒に出し入れする")]
        public GameObject[] hillsideFxHeadRows;
        [Tooltip("群見出しの文字 (KD_TreatPouch.WordFxGroup = 撫でエフェクト)")]
        public TextMeshProUGUI hillsideFxHeadLabel;

        /// <summary>かざりのカタログ階層 (商品一覧の面) か。セットの面 (_accSetMenu 1 / 2) と犬選択の面 (_accProduct 選択中) は false。</summary>
        private bool HillsideAccCatalogLevel()
        {
            return _accSetMenu == 0 && _accProduct < 0 && _collarPage < 0;   // 裏山改変 (IMP-DOG-050): 首輪とリードのセットの面もカタログ階層ではない
        }

        /// <summary>板の下端 (feedbackLabel) に撫でFX の結果 (つけた / 買えない 等) を出すか。
        /// 古民家は `_tab == TabPetFx` だったが、裏山のタブ 2 は さんぽ なので かざりのカタログ階層に置き換える。</summary>
        private bool HillsideFxFeedbackVisible()
        {
            return !_settingsOpen && !_welcome && _tab == TabAcc && HillsideAccCatalogLevel() && _fxConfirmRow < 0
                && _feedbackTab == _tab && Time.time < _feedbackUntil;
        }

        /// <summary>かざりの面の下端 1 行。撫でFX の結果が生きていればそれ、無ければ (カタログ階層で) 支度ポイントの未受取の案内。
        /// 段階 1 は準備中の行のサブ文だった案内 (HillsideGrantLineVisible) を、段階 2 ではここへ移す。</summary>
        private void HillsideAccFooter(bool catalogLevel)
        {
            if (!catalogLevel) return;   // セット・犬選択の面は Redraw の先頭が feedbackLabel を隠したまま
            if (HillsideFxFeedbackVisible()) { HillsideFooter(L(_feedbackText)); return; }
            if (HillsideGrantLineVisible()) HillsideFooter(L("KD_TreatPouch.WordGrantLine"));
        }

        /// <summary>撫でFX の行 (FxRow0..10) を描く。<paramref name="visible"/> = false なら行・群見出しを隠し、撫でFX の確認を閉じる。
        /// 行の中身は古民家の RedrawFx と同じ (Point 不足は「あと N pt」、CE はセット名と Credits、使用中は押す先が無い)。</summary>
        private void RedrawFxRows(bool visible)
        {
            int headN = hillsideFxHeadRows == null ? 0 : hillsideFxHeadRows.Length;
            for (int h = 0; h < headN; h++)
                if (hillsideFxHeadRows[h] != null && hillsideFxHeadRows[h].activeSelf != visible) hillsideFxHeadRows[h].SetActive(visible);
            if (visible && hillsideFxHeadLabel != null) hillsideFxHeadLabel.text = L("KD_TreatPouch.WordFxGroup");

            int n = fxLabels == null ? 0 : fxLabels.Length;
            if (!visible)
            {
                for (int i = 0; i < n; i++)
                {
                    if (fxLabels[i] == null) continue;
                    GameObject hideRow = HillsideRowOf(fxLabels[i]);
                    if (hideRow != null && hideRow.activeSelf) hideRow.SetActive(false);
                }
                _fxConfirmRow = -1;
                _HideConfirm(ConfirmFx);
                return;
            }

            string applied = petFxBox == null ? KD_PetFxBox.Unset : petFxBox.AppliedProduct();
            for (int i = 0; i < n; i++)
            {
                TextMeshProUGUI lbl = fxLabels[i];
                if (lbl == null) continue;
                KD_ShopItem it = FxAt(i);
                // 商品の無い枠 (段階 1 で null のまま) は行ごと隠す — 押しても何も起きない行を出さない
                GameObject rowGo = HillsideRowOf(lbl);
                bool rowOn = it != null;
                if (rowGo != null && rowGo.activeSelf != rowOn) rowGo.SetActive(rowOn);
                if (!rowOn) continue;

                bool show = false;
                bool owned = it.Owned();
                string s = L(it.displayNameKey);
                if (applied != KD_PetFxBox.Unset && applied == it.productKey)
                {
                    s += "  " + L("KD_ShopShelf.WordInUse");   // 着けている = 押す先が無い
                }
                else if (owned)
                {
                    show = true;                           // 持っている = 着け替えられる
                }
                else if (LiveSaleClosed(it)) { s += "  " + L("live.saleClosed"); }   // 期間限定の同梱 FX: 販売終了後は買えない (買った人はそのまま)
                else if (it.IsCE())
                {
                    s += "  " + (it.CePurchasesLoaded() ? it.ceCredits.ToString() + " C" : L("KD_ShopShelf.WordLoading"));
                    show = it.CePurchasesLoaded();
                }
                else
                {
                    // **「あと N pt」が goal-gradient の主表示** (棚と同じ)
                    int gap = it.Shortfall();
                    if (gap > 0) s += "  " + L("KD_TreatPouch.WordAccWaitPrefix") + " " + gap.ToString() + "pt";
                    else { s += "  " + it.price.ToString() + "pt"; show = it.CanBuy(); }
                }

                lbl.text = s;
                if (fxSubLabels != null && i < fxSubLabels.Length && fxSubLabels[i] != null)
                    fxSubLabels[i].text = FxBundleLabel(it);

                // **ボタンの文言が状態を語る** — つける / 買う を取り違えない
                if (fxButtonLabels != null && i < fxButtonLabels.Length && fxButtonLabels[i] != null)
                    fxButtonLabels[i].text = owned ? L("KD_TreatPouch.WordAccPut") : L("KD_TreatPouch.WordBuyHere");
                if (fxButtonImages != null && i < fxButtonImages.Length && fxButtonImages[i] != null)
                    fxButtonImages[i].color = owned ? equipOnColor : recallOnColor;

                GameObject btn = (fxButtons != null && i < fxButtons.Length) ? fxButtons[i] : null;
                if (btn != null && btn.activeSelf != show) btn.SetActive(show);
            }

            // ---- 確認カード (kind Fx。共有 1 枚) ----
            // **開いたままでも状態へ追いつく** — 開きっぱなしのまま pt が閾値を跨いだとき「あと 1pt」で固まらないように
            KD_ShopItem c = FxAt(_fxConfirmRow);
            if (c != null && (c.Owned() || LiveSaleClosed(c))) { _fxConfirmRow = -1; c = null; }   // 買えた / 別経路で入手した / 販売が終わった
            if (c != null) _ShowItemConfirm(ConfirmFx, c, FxBundleLabel(c));
            else _HideConfirm(ConfirmFx);
        }

    }
}
