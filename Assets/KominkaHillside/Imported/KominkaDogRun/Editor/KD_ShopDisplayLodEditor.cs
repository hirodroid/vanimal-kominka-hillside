using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>Fixed shop displays only. Native per-camera culling; no Udon polling.</summary>
    public static class KD_ShopDisplayLodEditor
    {
        // Reference distance, NOT a fixed runtime radius: FOV and QualitySettings.lodBias affect LOD.
        const float ReferenceDistance = 10f;
        const float ReferenceFov = 60f;

        static KD_ShopItem[] Displays()
        {
            KD_PlushShelfEditor.Guard();
            var own = KD_SceneObjects.Find("====KominkaDogRun====");
            var shelves = new[] { KD_SceneObjects.Find(KD_DogDefs.ShopShelfName),
                KD_SceneObjects.Find("KD_ShopShelf_Treat") };
            if (own == null || shelves.Any(s => s == null || !s.transform.IsChildOf(own.transform)))
                throw new InvalidOperationException("Own shop shelves required");
            var items = shelves.SelectMany(s => s.GetComponentsInChildren<KD_ShopItem>(true))
                .Where(i => !i.menuOnly && (i.categoryKey == "toy" || i.categoryKey == "treat"))
                .OrderBy(i => i.productKey).ToArray();
            if (items.Length == 0 || items.Select(i => i.productKey).Distinct().Count() != items.Length)
                throw new InvalidOperationException("Missing or duplicate display products");
            foreach (var item in items)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(item) || item.GetComponentInParent<Rigidbody>() != null)
                    throw new InvalidOperationException("Prefab or movable display: " + item.name);
                var renderers = Meshes(item);
                if (renderers.Length == 0 || renderers.Any(r => r.GetComponent<MeshFilter>()?.sharedMesh == null))
                    throw new InvalidOperationException("Missing display meshes: " + item.name);
                if (item.GetComponentsInChildren<LODGroup>(true).Any(g => g.gameObject != item.gameObject)
                    || item.transform.parent.GetComponentInParent<LODGroup>() != null)
                    throw new InvalidOperationException("Nested LOD: " + item.name);
            }
            return items;
        }

        static MeshRenderer[] Meshes(KD_ShopItem item)
        {
            string prefix = item.categoryKey == "toy" ? "KD_ToyMesh_" : "KD_TreatMesh_";
            return item.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        }

        static float Threshold(LODGroup group)
        {
            var scale = group.transform.lossyScale;
            float worldSize = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            return Mathf.Clamp(worldSize / (2f * ReferenceDistance * Mathf.Tan(ReferenceFov * Mathf.Deg2Rad * .5f)), .0001f, .5f);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Shop Display LOD/1 - Survey")]
        public static void Survey()
        {
            var log = new KD_VerifyLog("Shop display LOD survey");
            foreach (var item in Displays())
                log.Info(item.productKey + " | " + string.Join(", ", Meshes(item).Select(r =>
                    r.name + " size=" + r.bounds.size.ToString("F3") + " triangles=" +
                    (r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3))));
            log.Finish("Temp/shop-display-lod-survey.txt");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Shop Display LOD/2 - Apply")]
        public static void Apply()
        {
            var items = Displays(); // Preflight entire scope before mutation.
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty && !EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save existing scene");
            Directory.CreateDirectory("Temp/shop-display-lod");
            string backup = "Temp/shop-display-lod/before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".unity";
            if (!EditorSceneManager.SaveScene(scene, backup, true))
                throw new InvalidOperationException("Could not back up scene");
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Shop display LOD");
            foreach (var item in items)
            {
                var group = item.GetComponent<LODGroup>();
                if (group == null) group = Undo.AddComponent<LODGroup>(item.gameObject);
                Undo.RecordObject(group, "Shop display LOD");
                group.enabled = true;
                group.fadeMode = LODFadeMode.None;
                group.animateCrossFading = false;
                group.SetLODs(new[] { new LOD(.01f, Meshes(item)) });
                group.RecalculateBounds();
                group.SetLODs(new[] { new LOD(Threshold(group), Meshes(item)) });
                group.ForceLOD(-1);
                EditorUtility.SetDirty(group);
            }
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            KD_VerifyLog.Write("Temp/shop-display-lod-apply.txt", "Applied " + items.Length + " displays. Backup: " + backup);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Shop Display LOD/3 - Verify")]
        public static void Verify()
        {
            var items = Displays();
            var log = new KD_VerifyLog("Shop display LOD verification");
            int triangles = 0;
            foreach (var item in items)
            {
                var group = item.GetComponent<LODGroup>();
                if (!log.Check(group != null, item.productKey + " LODGroup")) continue;
                var lods = group.GetLODs();
                var meshes = Meshes(item);
                log.Check(group.enabled && group.fadeMode == LODFadeMode.None && !group.animateCrossFading,
                    item.productKey + " native hard cull");
                log.Check(lods.Length == 1 && lods[0].renderers.Length == meshes.Length
                    && lods[0].renderers.All(r => meshes.Contains(r)), item.productKey + " display mesh references only");
                log.Check(lods.Length == 1 && Mathf.Abs(lods[0].screenRelativeTransitionHeight - Threshold(group)) < .000001f,
                    item.productKey + " reference threshold");
                log.Check(item.gameObject.activeSelf && meshes.All(r => r.enabled && r.gameObject.activeSelf)
                    && item.GetComponent<Collider>() != null && item.GetComponent<Collider>().enabled
                    && item.shelf != null, item.productKey + " visible near / interaction retained");
                triangles += meshes.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
                if (lods.Length > 0)
                    log.Info(item.productKey + " threshold=" + lods[0].screenRelativeTransitionHeight.ToString("F6"));
            }
            log.Info("Displays=" + items.Length + " source triangles=" + triangles + " lodBias=" + QualitySettings.lodBias);
            log.Finish("Temp/shop-display-lod-verify.txt");
        }
    }
}
