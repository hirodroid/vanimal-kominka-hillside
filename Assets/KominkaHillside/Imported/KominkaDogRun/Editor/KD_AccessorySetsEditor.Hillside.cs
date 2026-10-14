using System;
using System.Linq;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>裏山改変 (IMP-DOG-015): 飾りの商品表の配線を**商品を書き換えずに**行う版。
    ///
    /// 古民家の <see cref="Wire"/> は KD_ShopItem を探して無ければ作り、価格・表示名・台帳まで上書きする (棚と飾りの器が同じ Setup にあったため)。
    /// 裏山では商品 (KH_Dogs/KD_ShopCatalog/KD_ShopItem_Acc_* など 14 行) は段階 1 の Menu/4 Setup Shop (P1) が正本なので、
    /// ここは**探して参照するだけ**。無ければ FAIL で止める (作らない)。
    /// 品目表・セット表・一覧の並び (Keys / Selections / Goods / SetGoods / ProductGoods) は古民家のまま (<see cref="Wire"/> と同じ表を読む)。
    ///
    /// キャンディ・パンプキン (ID 18..23) は古民家ではライブの演出 (KD_CandyDogPerformance) が合わせたノードを
    /// <c>BindCandyNodes</c> で束ねていた。裏山にライブは無いので、ノードは Menu/6e が古民家の実測値から作り、
    /// <c>candyPerformanceNodes</c> (ライブ中の上書き表示) は空にする。</summary>
    public static partial class KD_AccessorySetsEditor
    {
        /// <summary>古民家の BindCandyNodes と同じ固定値。セット 100..104 = 王様 / 夏 / お花 / キャンディ / パンプキン。</summary>
        internal static readonly int[] HillsideSetIds = { 100, 101, 102, 103, 104 };
        internal static readonly int[] HillsideSetMembers = { 0, 5, 6, 9, 7, 8, 10, 11, 12, 18, 19, 20, 21, 22, 23 };

        /// <summary>品目 ID の数 (0..23)。</summary>
        internal static int HillsideProductCount { get { return Keys.Length; } }

        /// <summary>一覧の並び (29 件。セットの構成品は各セットの直後)。古民家の Selections の写しを返す。</summary>
        internal static int[] HillsideSelections() { return Selections.ToArray(); }

        /// <summary>裏山改変 IMP-PRG-002 (2026-10-14): 手元メニューの一覧に出す並び。古民家の Selections から、公開しない CE 商品
        /// (<see cref="KD_ProductDefinitions.IsWithheldKey"/>: 王様 100 / 夏 101 / キャンディ 103 / パンプキン 104 とその構成品 0,5,6 / 7,8,9 / 18..23) を抜く。
        /// 台帳 (KD_AccessoryBox) の恒久 ID・productItems・setIds・setMembers・nodes は 0..23 / 100..104 のまま (同期値 _slotProduct が恒久 ID を運ぶ)。
        /// 一覧の配列 (accProductIds ほか) は同期も保存もしない表示用なので、ここで短くしてよい。</summary>
        internal static int[] HillsidePublishedSelections()
        {
            return Selections.Where(id => !KD_ProductDefinitions.IsWithheldKey(GoodsKeyOf(id))).ToArray();
        }

        /// <summary>一覧の 1 件 (単品の恒久 ID 0..23 / セット 100..104) が属する商品キー。</summary>
        internal static string GoodsKeyOf(int selection)
        {
            return Goods[selection >= 100 ? SetGoods[selection - 100] : ProductGoods[selection]];
        }

        /// <summary>品目 ID × 犬のシーン上のノード名。0..17 = KD_Acc_{Key}_{Dog}、18..20 = KD_CandyCostume_*、21..23 = KD_PumpkinCostume_*
        /// (古民家の BindCandyNodes と同じ命名)。</summary>
        internal static string HillsideNodeName(int product, string dogKey)
        {
            if (product < 18) return "KD_Acc_" + Keys[product] + "_" + dogKey;
            return (product < 21 ? "KD_CandyCostume_" : "KD_PumpkinCostume_") + CandyModels[product - 18] + "_" + dogKey;
        }

        /// <summary>キャンディ・パンプキン 6 品の装着枠 (頭 / むね / しっぽ の順。BindCandyNodes と同じ)。</summary>
        internal static KD_AccessorySetupEditor.Slot HillsideCandySlot(int product)
        {
            int k = (product - 18) % 3;
            return k == 0 ? KD_AccessorySetupEditor.Slot.Head : k == 1 ? KD_AccessorySetupEditor.Slot.Chest : KD_AccessorySetupEditor.Slot.Tail;
        }

        /// <summary>14 商品 (Goods の順) の商品キー。</summary>
        internal static string[] HillsideGoods() { return Goods.ToArray(); }

        /// <summary>14 商品を名前で探す (KD_ProductDefinitions.SceneName)。**作らない・書き換えない。** 1 つでも無ければ null を返して FAIL を書く。</summary>
        internal static KD_ShopItem[] HillsideFindGoods(StringBuilder log)
        {
            var items = new KD_ShopItem[Goods.Length];
            bool ok = true;
            for (int i = 0; i < Goods.Length; i++)
            {
                string name = KD_ProductDefinitions.SceneName(Goods[i]);
                var go = KD_SceneObjects.Find(name, out int count);
                items[i] = count == 1 && go != null ? go.GetComponent<KD_ShopItem>() : null;
                if (items[i] == null || items[i].productKey != Goods[i])
                {
                    log.AppendLine("   FAIL 飾りの商品 " + name + " (" + Goods[i] + ") が 1 個ない (" + count + " 個) / productKey 不一致。先に Menu/4 Setup Shop");
                    items[i] = null; ok = false;
                }
            }
            return ok ? items : null;
        }

        /// <summary>台帳 (KD_AccessoryBox) の商品参照とセット表、手元メニューの一覧を配線する。**商品 (KD_ShopItem) には書き込まない。**
        /// <paramref name="pouch"/> が null なら台帳だけ。成功で true。呼び手が CopyProxyToUdon する。</summary>
        internal static bool HillsideWire(KD_AccessoryBox box, KD_TreatPouch pouch, StringBuilder log)
        {
            if (box == null) { log.AppendLine("   FAIL KD_AccessoryBox が無い"); return false; }
            var items = HillsideFindGoods(log);
            if (items == null) return false;
            if (box.productIds == null || box.productIds.Length != Keys.Length)
            { log.AppendLine("   FAIL 台帳の品目が " + (box.productIds == null ? 0 : box.productIds.Length) + " 件 (" + Keys.Length + " 件が要る。先にノードを配線)"); return false; }

            box.productItems = ProductGoods.Select(i => items[i]).ToArray();
            box.setIds = HillsideSetIds.ToArray();
            box.setMembers = HillsideSetMembers.ToArray();
            box.setItems = SetGoods.Select(i => items[i]).ToArray();
            // 裏山にライブ (キャンディ・ステップの演出) は無い。上書き表示の対象は空 (通常の着せ替えだけ)
            box.candyPerformanceNodes = new GameObject[0];
            EditorUtility.SetDirty(box);

            if (pouch != null)
            {
                var catalog = KD_LocalizationEditor.Load();
                // 裏山改変 IMP-PRG-002 (2026-10-14): 一覧は公開する品だけ (公開しない CE セットとその構成品を出さない)
                var shown = HillsidePublishedSelections();
                pouch.accessories = box;
                pouch.accProductIds = shown.ToArray();
                pouch.accItems = shown.Select(id => id >= 100 ? items[SetGoods[id - 100]] : box.productItems[id]).ToArray();
                pouch.accNameKeys = shown.Select(id => "accessory." + (id >= 100 ? GoodsKeys[SetGoods[id - 100]] : Keys[id])).ToArray();
                pouch.accSlotNameKeys = shown.Select(id => id >= 100 ? "accessory.set" : "accessory.slot." + box.productSlots[id]).ToArray();
                pouch.accNames = pouch.accNameKeys.Select(k => KD_LocalizationEditor.Text(catalog, "ja", k)).ToArray();
                pouch.accSlotNames = pouch.accSlotNameKeys.Select(k => KD_LocalizationEditor.Text(catalog, "ja", k)).ToArray();
                EditorUtility.SetDirty(pouch);
                log.AppendLine("   IMP-PRG-002: 一覧から公開しない " + (Selections.Length - shown.Length) + " 件を外した ("
                    + string.Join(",", Selections.Except(shown)) + ")。台帳の恒久 ID・セット表は 24 / 5 のまま");
            }
            int visibleRows = HillsidePublishedSelections().Count(id => !HillsideSetMembers.Contains(id));
            log.AppendLine("   配線: 台帳の商品参照 24 / セット 5 (構成品 15)" + (pouch != null ? " / 手元メニューの一覧 " + HillsidePublishedSelections().Length + " 件 (見える行 " + visibleRows + ")" : "") + "。商品は書き換えていない");
            return true;
        }

        /// <summary>Verify 用: 台帳と一覧の配線が古民家の表どおりか。</summary>
        internal static void HillsideVerifyTables(KD_VerifyLog v, KD_AccessoryBox box, KD_TreatPouch pouch)
        {
            var sb = new StringBuilder();
            var items = HillsideFindGoods(sb);
            v.Check(items != null, "飾りの商品 14 行が KD_ShopCatalog 側に 1 個ずつある (" + string.Join(", ", Goods) + ")" + (items == null ? "\n" + sb : ""));
            if (items == null || box == null) return;
            v.Check(box.productItems != null && box.productItems.Length == ProductGoods.Length
                && Enumerable.Range(0, ProductGoods.Length).All(i => box.productItems[i] == items[ProductGoods[i]]), "台帳の productItems 24 件が商品表 (ProductGoods) どおり (セットの構成品はセットの商品を共有)");
            v.Check(box.setIds != null && box.setIds.SequenceEqual(HillsideSetIds) && box.setMembers != null && box.setMembers.SequenceEqual(HillsideSetMembers),
                "セット表 setIds 100..104 / setMembers 15 件");
            v.Check(box.setItems != null && box.setItems.Length == SetGoods.Length && Enumerable.Range(0, SetGoods.Length).All(i => box.setItems[i] == items[SetGoods[i]]),
                "setItems 5 件 (王様 / 夏 / お花 / キャンディ / パンプキン)");
            for (int set = 0; box.setMembers != null && box.productSlots != null && set < HillsideSetIds.Length; set++)
            {
                var slots = new System.Collections.Generic.HashSet<int>();
                for (int m = 0; m < 3; m++) slots.Add(box.productSlots[HillsideSetMembers[set * 3 + m]]);
                v.Check(slots.Count == 3, "セット " + HillsideSetIds[set] + " の 3 品は枠が別々");
            }
            v.Check(box.candyPerformanceNodes != null && box.candyPerformanceNodes.Length == 0, "candyPerformanceNodes は空 (裏山にキャンディの演出は無い)");
            var ledger = UnityEngine.Object.FindObjectsOfType<KD_ProgressLedger>(true);
            var shop = UnityEngine.Object.FindObjectsOfType<KD_ShopCatalog>(true);
            for (int i = 0; i < Goods.Length; i++)
            {
                var it = items[i]; var def = KD_ProductDefinitions.Get(Goods[i]);
                bool ce = !string.IsNullOrEmpty(def.ceListingId);
                // 裏山改変 IMP-PRG-002 (2026-10-14): 公開しない CE 商品は UdonProduct を持たない (Listing だけ = CeWithheld)
                bool withheld = KD_ProductDefinitions.IsWithheld(def);
                v.Check(it.price == def.price && it.IsCE() == ce && (!ce || (withheld ? it.ceProduct == null : it.ceProduct != null)),
                    "商品 " + Goods[i] + ": " + (ce ? def.ceCredits + " C (UdonProduct " + (it.ceProduct != null ? it.ceProduct.name : "null") + (withheld ? "。IMP-PRG-002 で公開しない = null が正" : "") + ")" : it.price + " pt"));
                v.Check(shop.Length == 1 && shop[0].items != null && shop[0].items.Count(x => x == it) == 1
                    && ledger.Length == 1 && ledger[0].products != null && ledger[0].products.Count(x => x == it) == 1, "商品 " + Goods[i] + " がカタログと台帳に 1 回ずつ登録");
            }
            if (pouch == null) { v.Note("KD_TreatPouch が無い — 一覧の配線は検査しない"); return; }
            v.Check(pouch.accessories == box, "KD_TreatPouch.accessories = KH_Dogs/" + KD_DogDefs.AccessoryBoxName);
            // 裏山改変 IMP-PRG-002 (2026-10-14): 一覧は公開する品だけ (古民家の 29 件から公開しない CE セットと構成品を抜いた並び)
            var shown = HillsidePublishedSelections();
            v.Check(pouch.accProductIds != null && pouch.accProductIds.SequenceEqual(shown), "accProductIds が一覧の並び " + shown.Length + " 件 (安い → 高い、CE は最後。構成品はセットの直後。IMP-PRG-002 の除外後)");
            v.Check(pouch.accItems != null && pouch.accItems.Length == shown.Length && Enumerable.Range(0, shown.Length).All(i => pouch.accItems[i] == (shown[i] >= 100 ? items[SetGoods[shown[i] - 100]] : box.productItems[shown[i]])),
                "accItems " + shown.Length + " 件が商品表どおり");
            v.Check(pouch.accItems != null && pouch.accItems.All(x => x != null && !x.CeWithheld()) && pouch.accProductIds != null && pouch.accProductIds.All(id => !KD_ProductDefinitions.IsWithheldKey(GoodsKeyOf(id))),
                "IMP-PRG-002: 一覧に公開しない CE 商品 (王様 / 夏 / キャンディ / パンプキン) とその構成品が無い");
            int shownRows = shown.Count(id => !HillsideSetMembers.Contains(id));
            v.Check(shownRows % 2 == 0, "見える行 " + shownRows + " が偶数 (2 列の格子で撫でFX の見出しが段の左端に来る)");
            var catalog = KD_LocalizationEditor.Load();
            int missing = 0; string first = "";
            foreach (var k in (pouch.accNameKeys ?? new string[0]).Concat(pouch.accSlotNameKeys ?? new string[0]))
            {
                try { KD_LocalizationEditor.Text(catalog, "ja", k); }
                catch (InvalidOperationException) { missing++; if (first.Length == 0) first = k; }
            }
            v.Check(pouch.accNameKeys != null && pouch.accNameKeys.Length == shown.Length && pouch.accSlotNameKeys != null && pouch.accSlotNameKeys.Length == shown.Length && missing == 0,
                "accNameKeys / accSlotNameKeys " + shown.Length + " 件が文言表にある (欠け " + missing + (first.Length > 0 ? " 例 " + first : "") + ")");
            v.Check(shownRows <= MenuCount && pouch.accRows != null && pouch.accRows.Length >= MenuCount, "見える行 " + shownRows + " 品 <= 行の器 " + MenuCount);
            // 裏山改変 IMP-PRG-002 (2026-10-14): 公開しない品の見た目ノードは残すが (容量は別件)、既定で外れていて、台帳が着けない (KD_AccessoryBox.Apply の CeWithheld)
            if (box.nodes != null && box.productItems != null && box.dogCount > 0)
            {
                int withheldNodes = 0, activeWithheld = 0;
                for (int p = 0; p < box.productItems.Length && (p + 1) * box.dogCount <= box.nodes.Length; p++)
                {
                    if (box.productItems[p] == null || !box.productItems[p].CeWithheld()) continue;
                    for (int d = 0; d < box.dogCount; d++)
                    {
                        var n = box.nodes[p * box.dogCount + d];
                        if (n == null) continue;
                        withheldNodes++;
                        if (n.activeSelf) activeWithheld++;
                    }
                }
                v.Check(activeWithheld == 0, "IMP-PRG-002: 公開しない品の見た目ノード " + withheldNodes + " 個 (12 品 × " + box.dogCount + " 匹) がすべて外れている (active " + activeWithheld + ")");
                v.Check(typeof(KD_ShopItem).GetMethod("CeWithheld") != null, "IMP-PRG-002: 台帳は CeWithheld の品を同期値が指していても着けない (KD_AccessoryBox.Apply)");
            }
        }
    }
}
