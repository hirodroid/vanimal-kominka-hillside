using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;

namespace KominkaDogRun.Editor
{
    public static class KD_PlushShelfEditor
    {
        public static readonly string[] MeshKeys = { "PawCushion", "TaiyakiPlush", "EbiTenPlush" };
        public static readonly string[] ProductKeys = { "toy.paw_cushion", "toy.taiyaki_plush", "toy.ebiten_plush" };
        public const float TintStrength = .35f;
        public static void Guard()
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != KD_Hillside.ScenePath /* 裏山改変: シーンの正本は KD_Hillside */)
                throw new InvalidOperationException("KominkaDogRun Edit scene required");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Plush Shelf/0 - Survey")]
        public static void Survey()
        {
            Guard(); var log = new StringBuilder();
            var shelf = KD_SceneObjects.Find(KD_DogDefs.ShopShelfName);
            log.AppendLine("Shelf world=" + shelf.transform.position.ToString("F3") + " rotation=" + shelf.transform.eulerAngles + " scale=" + shelf.transform.lossyScale);
            foreach (var item in shelf.GetComponentsInChildren<KD_ShopItem>(true))
                log.AppendLine(item.productKey + " position=" + item.transform.localPosition.ToString("F3") + " rotation=" + item.transform.localEulerAngles.ToString("F1") + " scale=" + item.transform.localScale);
            foreach (var renderer in KD_ProductsEditor.SceneComponents<MeshRenderer>())
            {
                Vector3 center = shelf.transform.InverseTransformPoint(renderer.bounds.center);
                if (center.y < -.15f && center.y > -1f && Mathf.Abs(center.x) < 1.6f && Mathf.Abs(center.z) < .9f && renderer.bounds.size.magnitude > .4f)
                    log.AppendLine("SURFACE " + renderer.name + " center=" + center.ToString("F3") + " size=" + renderer.bounds.size.ToString("F3"));
            }
            Directory.CreateDirectory("Logs/plush-v3"); File.WriteAllText("Logs/plush-v3/shelf-survey.txt", log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Plush Shelf/2 - Add Candidates")]
        public static void AddCandidates()
        {
            Guard(); KD_PlushReviewEditor.Clear();
            // Preserve the pre-existing unsaved scene in a separate copy before changing it.
            Directory.CreateDirectory("Temp/plush-v3");
            string backup = "Temp/plush-v3/before-shelf-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
            if (!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), backup, true))
                throw new InvalidOperationException("Could not back up current scene");
            KD_ToySetupEditor.ApplyPlushCandidates();
            KD_ShopSetupEditor.AddPlushCandidates();
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Plush Shelf/4 - Unlock Candidates for Development")]
        public static void UnlockDevelopment()
        {
            Guard();
            foreach (string key in ProductKeys)
            {
                var item = KD_SceneObjects.Find(KD_ProductDefinitions.SceneName(key)).GetComponent<KD_ShopItem>();
                item.ceListingId = ""; KD_ProductsEditor.Copy(item);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[PlushV3] Three development candidates selectable. Shop CE Relock restores candidate locks; release checks detect this override.");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Plush Shelf/2b - Place New Display")]
        public static void PlaceNewDisplay()
        {
            Guard();
            if (KD_SceneObjects.Find("KD_PlushDisplayBox") != null) return;
            var shelf = KD_SceneObjects.Find(KD_DogDefs.ShopShelfName).transform;
            // Reuse the existing crate mesh/material, without editing its purchased source.
            var source = KD_ProductsEditor.SceneComponents<MeshRenderer>().Single(r => r.name == "Box02" && Vector3.Distance(r.bounds.center, shelf.position) < 2f);
            var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
            var box = new GameObject("KD_PlushDisplayBox");
            box.transform.SetParent(KD_SceneObjects.EnsureContainer(KD_DogDefs.PlacesPath), false);
            box.transform.SetPositionAndRotation(source.transform.position + shelf.TransformVector(new Vector3(.394f, 0f, .005f)), source.transform.rotation);
            box.transform.localScale = source.transform.lossyScale;
            box.AddComponent<MeshFilter>().sharedMesh = sourceMesh;
            box.AddComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
            var collider = box.AddComponent<BoxCollider>();
            collider.center = sourceMesh.bounds.center; collider.size = sourceMesh.bounds.size;
            var positions = new[] { new Vector3(.30f, -.514f, .18f), new Vector3(.49f, -.516f, .18f), new Vector3(.40f, -.524f, .39f) };
            for (int i = 0; i < ProductKeys.Length; i++)
            {
                var item = KD_SceneObjects.Find(KD_ProductDefinitions.SceneName(ProductKeys[i]));
                item.transform.localPosition = positions[i]; item.transform.localRotation = Quaternion.identity;
            }
            EditorSceneManager.MarkSceneDirty(shelf.gameObject.scene);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Plush Shelf/3 - Verify Candidates")]
        public static void Verify()
        {
            Guard(); var log = new StringBuilder(); int failures = 0;
            void Check(bool ok, string label) { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
            var catalog = KD_SceneObjects.Find(KD_DogDefs.ShopCatalogName).GetComponent<KD_ShopCatalog>();
            var shelf = KD_SceneObjects.Find(KD_DogDefs.ShopShelfName).GetComponent<KD_ShopShelf>();
            var toyBox = shelf.toyBox; var dogs = KD_DogDefs.All;
            Check(toyBox.productKeys.Length == KD_ToyCatalogEditor.Keys.Length && toyBox.meshNodes.Length == KD_ToyCatalogEditor.Keys.Length * dogs.Length, "adopted toys x 6 dogs");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(toyBox);
            string[] backingKeys; GameObject[] backingNodes;
            Check(backing.publicVariables.TryGetVariableValue("productKeys", out backingKeys) && backingKeys.SequenceEqual(toyBox.productKeys), "ToyBox backing key order");
            Check(backing.publicVariables.TryGetVariableValue("meshNodes", out backingNodes) && backingNodes.SequenceEqual(toyBox.meshNodes), "ToyBox backing mesh references");
            for (int i = 0; i < ProductKeys.Length; i++)
            {
                string key = ProductKeys[i];
                var item = KD_SceneObjects.Find(KD_ProductDefinitions.SceneName(key)).GetComponent<KD_ShopItem>();
                Check(catalog.items.Contains(item) && catalog.ledger.products.Contains(item) && shelf.items.Contains(item) && item.shelf == shelf, key + " catalog/ledger/shelf");
                Check(!string.IsNullOrEmpty(item.displayNameKey) && item.GetComponents<Collider>().Length == 1, key + " name/interaction");
                Check(item.price == 0 && (item.ceListingId == KD_ProductDefinitions.Get(key).ceListingId || string.IsNullOrEmpty(item.ceListingId)), key + " candidate metadata");
                var itemBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(item);
                string listing; int price;
                Check(itemBacking.publicVariables.TryGetVariableValue("ceListingId", out listing) && listing == item.ceListingId
                    && itemBacking.publicVariables.TryGetVariableValue("price", out price) && price == item.price, key + " backing entitlement metadata");
                Check(item.Owned() == string.IsNullOrEmpty(item.ceListingId), key + " candidate lock/free state");
                int index = Array.IndexOf(toyBox.productKeys, key);
                for (int d = 0; d < dogs.Length; d++)
                {
                    var expected = KD_SceneObjects.Find(dogs[d].BallName).transform.Find("KD_ToyMesh_" + MeshKeys[i]);
                    Check(index >= 0 && expected != null && toyBox.meshNodes[index * dogs.Length + d] == expected.gameObject, key + " " + dogs[d].key + " scene mesh");
                }
            }
            KD_LocalizationEditor.ValidateScene(KD_SceneObjects.Find("KD_Language").GetComponent<KD_Language>(), KD_LocalizationEditor.Load());
            log.AppendLine("failures=" + failures);
            Directory.CreateDirectory("Logs/plush-v3"); File.WriteAllText("Logs/plush-v3/shelf-verify.txt", log.ToString());
            if (failures != 0) throw new InvalidOperationException(log.ToString());
        }
    }
}
