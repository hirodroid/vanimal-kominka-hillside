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
            KD_ShopItem it = FxAt(row);
            if (it == null || catalog == null) return;

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

    }
}
