using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    public static class KD_ToyCatalogEditor
    {
        public static readonly string[] Keys = { "toy.basic_ball", "toy.tennis_ball", "toy.donut", "toy.bone", "toy.cloth_ball", "toy.temari_claude", "toy.paw_cushion", "toy.taiyaki_plush", "toy.ebiten_plush" };
        static readonly string[] Meshes = { "BasicBall", "TennisBall", "Donut", "Bone", "ClothBall", "Temari_Claude", "PawCushion", "TaiyakiPlush", "EbiTenPlush" };
        static readonly string[] RetiredNodes = { "KD_ToyMesh_PawPlush", "KD_ToyMesh_Temari_Codex" };
        static readonly string[] RetiredItems = { "KD_ShopItem_Toy_3", "KD_ShopItem_Toy_7" };
        static readonly string[] DogKeys = { "ui.extra.010", "dog.pomeranian", "dog.labrador", "ui.extra.012", "ui.extra.008", "ui.extra.009" };

        public static void WirePickupText(KD_ToyBox toyBox)
        {
            var language = KD_SceneObjects.Find("KD_Language")?.GetComponent<KD_Language>();
            if (language == null) return; // Initial project setup may precede localization setup.
            toyBox.language = language; language.toyBox = toyBox;
            var dogs = KD_DogDefs.All;
            toyBox.pickups = dogs.Select(d => KD_SceneObjects.Find(d.BallName).GetComponent<VRC_Pickup>()).ToArray();
            toyBox.dogNameKeys = DogKeys.ToArray(); toyBox.dogNames = dogs.Select(d => d.displayName).ToArray();
            var items = toyBox.productKeys.Select(k => toyBox.catalog.items.Single(i => i.productKey == k)).ToArray();
            toyBox.productNameKeys = items.Select(i => i.displayNameKey).ToArray();
            toyBox.productNames = items.Select(i => i.displayName).ToArray();
            KD_ProductsEditor.Copy(toyBox); KD_ProductsEditor.Copy(language);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Catalog/1 - Adopt Selected Toys")]
        public static void Apply()
        {
            KD_PlushShelfEditor.Guard();
            Directory.CreateDirectory("Temp/toy-catalog");
            if (!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Temp/toy-catalog/before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true))
                throw new InvalidOperationException("Scene backup failed");
            var shelf = KD_SceneObjects.Find(KD_DogDefs.ShopShelfName).GetComponent<KD_ShopShelf>();
            var catalog = shelf.catalog; var toyBox = shelf.toyBox;
            // Remove only the two explicitly retired candidates, including their own NetworkIDs.
            var removed = new HashSet<GameObject>();
            foreach (var name in RetiredItems)
            {
                var item = KD_SceneObjects.Find(name);
                if (item != null) foreach (var t in item.GetComponentsInChildren<Transform>(true)) removed.Add(t.gameObject);
            }
            var descriptor = KD_ProductsEditor.SceneComponents<VRCSceneDescriptor>().Single();
            for (int i = descriptor.NetworkIDCollection.Count - 1; i >= 0; i--)
                if (removed.Contains(descriptor.NetworkIDCollection[i].gameObject)) descriptor.NetworkIDCollection.RemoveAt(i);
            EditorUtility.SetDirty(descriptor);
            catalog.items = catalog.items.Where(i => i != null && !removed.Contains(i.gameObject)).ToArray();
            catalog.ledger.products = catalog.items;
            shelf.items = shelf.items.Where(i => i != null && !removed.Contains(i.gameObject)).ToArray();
            foreach (var name in RetiredItems) { var go = KD_SceneObjects.Find(name); if (go != null) Object.DestroyImmediate(go); }
            foreach (var dog in KD_DogDefs.All)
            {
                var root = PrefabUtility.LoadPrefabContents(dog.BallPrefabPath);
                try
                {
                    foreach (var name in RetiredNodes) { var t = root.transform.Find(name); if (t != null) Object.DestroyImmediate(t.gameObject); }
                    PrefabUtility.SaveAsPrefabAsset(root, dog.BallPrefabPath, out bool saved);
                    if (!saved) throw new InvalidOperationException("Prefab save failed: " + dog.key);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (string key in new[] { "toy.cloth_ball", "toy.temari_claude" })
            {
                var item = catalog.items.Single(i => i.productKey == key);
                KD_ProductDefinitions.Apply(item, key);
                if (key == "toy.temari_claude") item.displayName = "手毬";
                KD_ProductsEditor.Copy(item);
            }
            var byKey = catalog.items.ToDictionary(i => i.productKey);
            shelf.items = Keys.Select(k => byKey[k]).Concat(shelf.items.Where(i => i.categoryKey != "toy")).ToArray();
            toyBox.productKeys = Keys.ToArray(); toyBox.dogCount = KD_DogDefs.All.Length;
            toyBox.meshNodes = Meshes.SelectMany(m => KD_DogDefs.All.Select(d => KD_SceneObjects.Find(d.BallName).transform.Find("KD_ToyMesh_" + m).gameObject)).ToArray();
            KD_ProductsEditor.Copy(catalog); KD_ProductsEditor.Copy(catalog.ledger); KD_ProductsEditor.Copy(shelf);
            var language = KD_SceneObjects.Find("KD_Language").GetComponent<KD_Language>();
            var log = new StringBuilder(); var translations = KD_LocalizationEditor.Load();
            KD_LocalizationEditor.Configure(language, translations, log); KD_LocalizationEditor.Bind(language, translations, log);
            WirePickupText(toyBox);
            // Serialized initial prompt is the starter toy; runtime selects each client's language.
            for (int d = 0; d < toyBox.pickups.Length; d++)
            {
                toyBox.pickups[d].InteractionText = KD_LocalizationEditor.Text(translations, "ja", "item.basic") + " (" + KD_LocalizationEditor.Text(translations, "ja", DogKeys[d]) + ")";
                EditorUtility.SetDirty(toyBox.pickups[d]);
            }
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(shelf.gameObject.scene);
        }

        // Layout is explicit, separate from setup so repeated setup preserves hand placement.
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Catalog/2 - Arrange By Price")]
        public static void Arrange()
        {
            KD_PlushShelfEditor.Guard();
            var shelf = KD_SceneObjects.Find(KD_DogDefs.ShopShelfName).transform;
            for (int i = 0; i < Keys.Length; i++)
            {
                var item = KD_SceneObjects.Find(KD_ProductDefinitions.SceneName(Keys[i])).transform;
                float x = i < 6 ? .48f - .24f * i : -.24f - .24f * (i - 6);
                item.localPosition = new Vector3(x, -.47f, i < 6 ? .40f : .155f);
                item.localRotation = i == 2 || i == 3 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
                var renderers = item.GetComponentsInChildren<MeshRenderer>();
                float minY = renderers.Min(r => r.bounds.min.y);
                var position = item.position; position.y += shelf.TransformPoint(new Vector3(0f, -.545f, 0f)).y + .001f - minY;
                item.position = position;
            }
            EditorSceneManager.MarkSceneDirty(shelf.gameObject.scene);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Catalog/3 - Verify")]
        public static void Verify()
        {
            KD_PlushShelfEditor.Guard(); var log = new StringBuilder(); int fail = 0;
            void Check(bool ok, string message) { log.AppendLine((ok ? "PASS " : "FAIL ") + message); if (!ok) fail++; }
            var shelf = KD_SceneObjects.Find(KD_DogDefs.ShopShelfName).GetComponent<KD_ShopShelf>(); var box = shelf.toyBox;
            Check(box.productKeys.SequenceEqual(Keys) && box.meshNodes.Length == 54, "9 toys / 54 mesh nodes");
            Check(shelf.items.Where(i => i.categoryKey == "toy").Select(i => i.productKey).SequenceEqual(Keys), "shelf price order");
            foreach (var name in RetiredItems) Check(KD_SceneObjects.Find(name) == null, name + " retired");
            foreach (var dog in KD_DogDefs.All)
                foreach (var name in RetiredNodes) Check(KD_SceneObjects.Find(dog.BallName).transform.Find(name) == null, dog.key + " " + name + " absent");
            Check(box.pickups.Length == 6 && box.pickups.All(p => p != null) && box.language.toyBox == box, "pickup/language references");
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(box);
            string[] keys; GameObject[] nodes; object pickupValue;
            Check(ub.publicVariables.TryGetVariableValue("productKeys", out keys) && keys.SequenceEqual(Keys), "backing keys");
            Check(ub.publicVariables.TryGetVariableValue("meshNodes", out nodes) && nodes.SequenceEqual(box.meshNodes), "backing mesh references");
            // Udon serializes the SDK base pickup alias as SDK3.VRCPickup[]. Read the actual array,
            // then compare every component instance, rather than requiring an exact base-array type.
            bool pickupFound = ub.publicVariables.TryGetVariableValue("pickups", out pickupValue);
            var pickupArray = pickupValue as Array;
            Check(pickupFound && pickupArray != null && pickupArray.Length == box.pickups.Length
                && Enumerable.Range(0, box.pickups.Length).All(i => pickupArray.GetValue(i) as Component == box.pickups[i]), "backing pickup references");
            for (int i = 0; i < Keys.Length; i++)
            {
                var item = shelf.items.Single(s => s.productKey == Keys[i]);
                Check(item.price == (i < 6 ? i * 10 : 0), Keys[i] + " price=" + item.price);
                Check(i >= 6 || !item.IsCE(), Keys[i] + " Point classification");
                var pos = item.transform.localPosition;
                Check(Mathf.Abs(pos.x - (i < 6 ? .48f - .24f * i : -.24f - .24f * (i - 6))) < .001f
                    && Mathf.Abs(pos.z - (i < 6 ? .40f : .155f)) < .001f, Keys[i] + " row and column");
                for (int j = i + 1; j < Keys.Length; j++)
                {
                    var other = shelf.items.Single(s => s.productKey == Keys[j]);
                    Check(!item.GetComponent<Collider>().bounds.Intersects(other.GetComponent<Collider>().bounds), Keys[i] + " separated from " + Keys[j]);
                }
            }
            VerifyPromptChanges(box, Check);
            log.AppendLine("failures=" + fail); Directory.CreateDirectory("Logs/toy-catalog"); File.WriteAllText("Logs/toy-catalog/verify.txt", log.ToString());
            if (fail != 0) throw new InvalidOperationException(log.ToString());
        }

        // Editor-only: exercise the actual runtime Apply and language refresh, restoring all state.
        static void VerifyPromptChanges(KD_ToyBox box, Action<bool, string> check)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var applied = typeof(KD_ToyBox).GetField("_applied", flags);
            var applyCount = typeof(KD_ToyBox).GetField("_dbgApplyCount", flags);
            var apply = typeof(KD_ToyBox).GetMethod("Apply", flags);
            var init = typeof(KD_Language).GetField("_initialized", flags);
            var langIndex = typeof(KD_Language).GetField("_languageIndex", flags);
            var language = box.language;
            var oldApplied = applied.GetValue(box); var oldInit = init.GetValue(language); var oldIndex = langIndex.GetValue(language);
            var oldApplyCount = applyCount.GetValue(box);
            var active = box.meshNodes.Select(n => n.activeSelf).ToArray(); var texts = box.pickups.Select(p => p.InteractionText).ToArray();
            try
            {
                init.SetValue(language, true);
                foreach (string code in new[] { "ja", "en" })
                {
                    langIndex.SetValue(language, language.ResolveLanguage(code));
                    foreach (string key in Keys)
                    {
                        apply.Invoke(box, new object[] { key }); box._RefreshPickupText();
                        int index = Array.IndexOf(Keys, key);
                        for (int d = 0; d < 6; d++)
                        {
                            string expected = language._TextForLanguage(box.productNameKeys[index], code) + " (" + language._TextForLanguage(DogKeys[d], code) + ")";
                            check(box.pickups[d].InteractionText == expected, code + " " + key + " " + KD_DogDefs.All[d].key + " prompt");
                        }
                    }
                }
                // A language notification must update the already selected toy without another selection.
                langIndex.SetValue(language, language.ResolveLanguage("ja"));
                box._RefreshPickupText();
                check(box.pickups[0].InteractionText.StartsWith("エビ天ぬいぐるみ"), "language switch retains selected toy");
            }
            finally
            {
                applied.SetValue(box, oldApplied); init.SetValue(language, oldInit); langIndex.SetValue(language, oldIndex);
                applyCount.SetValue(box, oldApplyCount);
                for (int i = 0; i < active.Length; i++) box.meshNodes[i].SetActive(active[i]);
                for (int d = 0; d < texts.Length; d++) box.pickups[d].InteractionText = texts[d];
            }
        }
    }
}
