using System;
using System.IO;
using System.Linq;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    // Metadata only: never rebuilds meshes, UI, transforms or listener references.
    public static class KD_ProductsEditor
    {
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Products/0 - Survey")]
        public static void Survey()
        {
            KD_ProductDefinitions.Validate(KD_ProductDefinitions.All,KD_ProductDefinitions.Categories);
            var log=new StringBuilder(); int keys=2+KD_ProductDefinitions.Categories.Length;
            foreach(var d in KD_ProductDefinitions.All)
            {
                string kind=d.IsCE?"CE":d.packCount>0?"Stock":d.price==0?"Free":"Point";
                keys+=kind=="Stock"?2:kind=="Point"?1:0;
                log.AppendLine(d.key+" / "+d.categoryKey+" / "+kind+" / "+d.price+"pt / pack="+d.packCount+" / initial="+d.initialStock+" / cap="+d.stockCap
                    +(d.IsCE?" / "+d.ceCredits+"C asset="+d.ceAsset+" product="+d.ceProductId+" listing="+d.ceListingId:""));
            }
            log.AppendLine("Products="+KD_ProductDefinitions.All.Length+" possible commerce keys="+keys+". Missing keys are not pre-created. Actual bytes require VRChat measurement.");
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/commerce-survey.txt",log.ToString());
        }
        public static T[] SceneComponents<T>() where T : Component
        {
            return EditorSceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
        }
        public static void Copy(UdonSharpBehaviour value)
        {
            EditorUtility.SetDirty(value);
            UdonSharpEditorUtility.CopyProxyToUdon(value);
            EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(value));
        }
        public static bool HasAccessoryMenuEntry(KD_ShopItem item)
        {
            return item != null && item.categoryKey == "accessory" && item.menuOnly && item.GetComponent<Collider>() == null
                && SceneComponents<KD_TreatPouch>().Any(p => p.accItems != null && p.accItems.Contains(item));
        }
        public static bool HasMenuEntry(KD_ShopItem item)
        {
            if (item == null || !item.menuOnly || item.GetComponent<Collider>() != null) return false;
            if (item.categoryKey == "accessory") return HasAccessoryMenuEntry(item);
            if (item.categoryKey == "experience")
                return SceneComponents<KD_ActivitiesMenu>().Any(m => m.items != null && m.items.Contains(item));
            if (item.categoryKey == "action")
                return SceneComponents<KD_CallPose>().Any(m => m.actionProducts != null && m.actionProducts.Contains(item));
            return SceneComponents<KD_TreatPouch>().Any(p =>
                (item.categoryKey == "petfx" && p.fxItems != null && p.fxItems.Contains(item))
                || (item.categoryKey == "live" && p.liveProducts != null && p.liveProducts.Contains(item)));
        }
        public static void BindCatalog(KD_ShopCatalog catalog, KD_ProgressLedger ledger)
        {
            KD_ProductDefinitions.Validate(KD_ProductDefinitions.All, KD_ProductDefinitions.Categories);
            // CE の所有判定に使う UdonProduct アセット (Product ID の器)。無ければ作る — アセットは
            // シーンから参照されて初めてワールドに入る (vrc-udonsharp §7) ので、Apply が参照を刺す
            KD_ProductDefinitions.EnsureCeAssets(null);
            var existing = SceneComponents<KD_ShopItem>();
            var items = KD_ProductDefinitions.All.Select(d => existing.Single(i => i.name == d.sceneName)).ToArray();
            foreach (var item in items)
            {
                var def = KD_ProductDefinitions.All.Single(d => d.sceneName == item.name);
                item.ledger = ledger;
                KD_ProductDefinitions.Apply(item, def.key);
                Copy(item);
            }
            ledger.storageNamespace = KD_ProgressLedger.ProductionNamespace;
            ledger.products = items;
            catalog.ledger = ledger; catalog.items = items;
            catalog.categoryKeys = KD_ProductDefinitions.Categories.ToArray();
            catalog.defaultProducts = KD_ProductDefinitions.Defaults.ToArray();
            Copy(ledger); Copy(catalog);
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Products/1 - Apply Metadata")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != KD_Hillside.ScenePath /* 裏山改変: シーンの正本は KD_Hillside */)
                throw new InvalidOperationException("Target Edit scene required");
            var catalog = SceneComponents<KD_ShopCatalog>().Single(c => !c.name.StartsWith("Test"));
            var ledger = KD_SceneObjects.Find(KD_DogDefs.ProgressSystemName).GetComponent<KD_ProgressLedger>();
            BindCatalog(catalog, ledger);
            EditorSceneManager.MarkSceneDirty(catalog.gameObject.scene);
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Products/2 - Verify Metadata")]
        public static void Verify()
        {
            var log = new StringBuilder(); int fail = 0;
            void Check(bool ok, string message) { log.AppendLine((ok ? "PASS " : "FAIL ") + message); if (!ok) fail++; }
            try
            {
                KD_ProductDefinitions.Validate(KD_ProductDefinitions.All, KD_ProductDefinitions.Categories);
                var catalog = SceneComponents<KD_ShopCatalog>().Single(c => !c.name.StartsWith("Test"));
                Check(catalog.items.Length == KD_ProductDefinitions.All.Length, "all products in catalog");
                Check(catalog.items.Select(i=>i.productKey).OrderBy(k=>k).SequenceEqual(KD_ProductDefinitions.All.Select(d=>d.key).OrderBy(k=>k)), "exact product set without duplicates or omissions");
                Check(catalog.items.SequenceEqual(catalog.ledger.products), "ledger uses identical registry");
                Check(catalog.categoryKeys.SequenceEqual(KD_ProductDefinitions.Categories), "category keys");
                Check(catalog.defaultProducts.SequenceEqual(KD_ProductDefinitions.Defaults), "default product keys");
                foreach (var item in catalog.items)
                {
                    KD_ProductDefinitions.ValidateItem(item);
                    Check(item.ledger == catalog.ledger, item.productKey + " ledger");
                    Check(SceneComponents<KD_ShopItem>().Count(i => i.productKey == item.productKey) == 1, item.productKey + " unique");
                    string key;
                    Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(item).publicVariables.TryGetVariableValue("productKey", out key) && key == item.productKey, item.productKey + " backing");
                    if (item.IsCE() && !item.CeWithheld())   // 裏山改変 IMP-PRG-002 (2026-10-14): 公開しない CE 行は Product を持たない (ValidateItem が見る)
                    {
                        // 実行時に読まれるのは backing 側の参照。プロキシだけ刺さっていても実機では null になる
                        VRC.Economy.UdonProduct backingProduct;
                        Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(item).publicVariables.TryGetVariableValue("ceProduct", out backingProduct) && backingProduct == item.ceProduct, item.productKey + " CE asset in backing heap");
                        Check(!item.ceListingId.StartsWith("TODO-"), item.productKey + " real listing id");
                    }
                    else if (item.IsCE())
                    {
                        // 裏山改変 IMP-PRG-002 (2026-10-14): 公開しない CE 行は backing にも Product が残っていない (U# は null を消すので「無い / null」が正。Menu/4a と同じ)
                        VRC.Economy.UdonProduct backingProduct;
                        Check(!UdonSharpEditorUtility.GetBackingUdonBehaviour(item).publicVariables.TryGetVariableValue("ceProduct", out backingProduct) || backingProduct == null,
                            item.productKey + " withheld: no CE asset in backing heap");
                    }
                }
                // セット商品: 同じアセットを指す行は同じ Listing を指す (ぬいぐるみ 3 行 / トレーナー 2 行)
                foreach (var g in catalog.items.Where(i => i.IsCE() && !i.CeWithheld()).GroupBy(i => i.ceProduct))
                    Check(g.Select(i => i.ceListingId).Distinct().Count() == 1, "CE set rows share one listing: " + (g.Key == null ? "null" : g.Key.name));
                bool Keys(string[] keys, string category) { return keys != null && keys.Length > 0 && keys.All(k => catalog.items.Any(i => i.productKey == k && i.categoryKey == category)); }
                foreach (var toy in SceneComponents<KD_ToyBox>()) Check(Keys(toy.productKeys,"toy"), toy.name + " toy keys");
                foreach (var pet in SceneComponents<KD_DogPetController>()) Check(Keys(pet.fxProductKeys,"petfx") && pet.fxProductKeys.Length == pet.fxVariants.Length, pet.name + " fx keys");
                foreach (var treat in SceneComponents<KD_TreatController>()) Check(Keys(treat.productKeys,"treat"), treat.name + " treat keys");
                foreach (var fx in SceneComponents<KD_TreatFx>()) Check(Keys(fx.productKeys,"treat"), fx.name + " treat fx keys");
            }
            catch (Exception ex) { Check(false, ex.ToString()); }
            log.AppendLine("FAILURES=" + fail); Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/commerce-verify.txt", log.ToString());
            if (fail > 0) throw new InvalidOperationException("Product metadata verification: " + fail);
        }
    }
}
