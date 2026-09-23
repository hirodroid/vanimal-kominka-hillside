using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    // Bring approved scene geometry across, including hand-fitted costumes and director bindings.
    // Apply and Verify are separate; neither saves the production scene.
    public static class KD_ApprovedLivesEditor
    {
        const string Main = "Assets/Scenes/KominkaDogRun.unity";
        const string CandyScene = "Assets/KominkaHillside/Imported/KominkaDogRun/Scenes/CandyStepPreview002.unity";
        const string LotusScene = "Assets/VirtualCampers.LiveGenerator/Scenes/KominkaDogRun_Lotus003.unity";
        const string Candy = "kd-candystep-002";
        const string Lotus = "kd-gardenlights-004-lotus-r003";

        static void Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != Main
                || SceneManager.sceneCount != 1) throw new InvalidOperationException("Stopped production scene alone required");
        }
        static GameObject Find(Scene scene, string name)
        {
            var found = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name == name).ToArray();
            if (found.Length != 1) throw new InvalidOperationException(name + ": expected one, found " + found.Length);
            return found[0].gameObject;
        }
        static string PathOf(Transform t)
        { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }

        static void MapTree(GameObject source, GameObject copy, Dictionary<Object, Object> map)
        {
            map[source] = copy;
            var a = source.GetComponents<Component>(); var b = copy.GetComponents<Component>();
            if (a.Length != b.Length) throw new InvalidOperationException("Clone components changed");
            for (int i = 0; i < a.Length; i++) if (a[i] != null) map[a[i]] = b[i];
            for (int i = 0; i < source.transform.childCount; i++)
                MapTree(source.transform.GetChild(i).gameObject, copy.transform.GetChild(i).gameObject, map);
        }
        static GameObject Clone(GameObject source, Scene target, Transform parent, Dictionary<Object, Object> map)
        {
            var copy = Object.Instantiate(source); copy.name = source.name;
            SceneManager.MoveGameObjectToScene(copy, target);
            copy.transform.SetParent(parent, true);
            MapTree(source, copy, map);
            return copy;
        }
        static Object Resolve(Object value, Scene source, Dictionary<Object, Object> map, Dictionary<string, Transform> targets)
        {
            if (value == null || EditorUtility.IsPersistent(value)) return value;
            if (map.TryGetValue(value, out var mapped)) return mapped;
            var go = value as GameObject;
            var component = value as Component;
            if (go == null && component != null) go = component.gameObject;
            if (go == null || go.scene != source) return value;
            if (!targets.TryGetValue(PathOf(go.transform), out var t))
                throw new InvalidOperationException("Unmapped source reference: " + PathOf(go.transform));
            if (component == null) return t.gameObject;
            int index = Array.IndexOf(go.GetComponents(component.GetType()), component);
            return t.GetComponents(component.GetType())[index];
        }
        public static void Import(string scenePath, string slug, bool withDogs, StringBuilder log,
            string costumePrefix = "KD_CandyCostume_", string coordinatorName = "KD_CandyDogPerformance")
        {
            Guard();
            var target = SceneManager.GetActiveScene();
            string name = KD_DogDefs.LiveStageNameOf(slug);
            if (target.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Any(t => t.name == name))
            { log.AppendLine("Preserved existing " + name); return; }
            var targets = target.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .GroupBy(PathOf).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
            var source = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            var map = new Dictionary<Object, Object>();
            var created = new List<GameObject>();
            try
            {
                var stage = Clone(Find(source, "LiveStage_" + slug), target, Find(target, "KD_Places").transform, map);
                created.Add(stage); stage.name = name; stage.SetActive(false);
                if (withDogs)
                {
                    foreach (var node in source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                        .Where(t => t.name.StartsWith(costumePrefix, StringComparison.Ordinal)))
                    {
                        var parent = targets[PathOf(node.parent)];
                        var copy = Clone(node.gameObject, target, parent, map);
                        copy.transform.localPosition = node.localPosition;
                        copy.transform.localRotation = node.localRotation;
                        copy.transform.localScale = node.localScale;
                        copy.SetActive(false); created.Add(copy);
                    }
                    var coordinator = Clone(Find(source, "KD_CandyDogPerformance"), target,
                        Find(target, KD_DogDefs.LiveSystemName).transform, map);
                    coordinator.name = coordinatorName; created.Add(coordinator);
                }
                foreach (var copy in created)
                    foreach (var component in copy.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null || component is Transform) continue;
                        var serialized = new SerializedObject(component); var p = serialized.GetIterator();
                        while (p.Next(true)) if (p.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            var current = p.objectReferenceValue;
                            var resolved = Resolve(current, source, map, targets);
                            if (current != resolved) p.objectReferenceValue = resolved;
                        }
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                foreach (var proxy in created.SelectMany(g => g.GetComponentsInChildren<UdonSharpBehaviour>(true)))
                    KD_ProductsEditor.Copy(proxy);
                log.AppendLine("Imported " + slug + ": objects=" + created.Count + ", mapped references=" + map.Count);
            }
            catch
            {
                foreach (var copy in created) if (copy != null) Object.DestroyImmediate(copy);
                throw;
            }
            finally { EditorSceneManager.CloseScene(source, true); SceneManager.SetActiveScene(target); }
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Approved Lives/1 - Apply")]
        public static void Apply()
        {
            Guard(); var log = new StringBuilder(); var scene = SceneManager.GetActiveScene();
            Import(LotusScene, Lotus, false, log); Import(CandyScene, Candy, true, log);
            var live = Find(scene, KD_DogDefs.LiveSystemName).GetComponent<KD_LiveSystem>();
            var performance = Find(scene, "KD_CandyDogPerformance").GetComponent<KD_CandyDogPerformance>();
            performance.previewSceneOnly = false; performance.autoStartPreview = false; performance.autoBeginPerformance = false;
            performance.liveSystem = live; live.candyPerformance = performance; live.candyLiveId = 7;
            performance.gameObject.SetActive(true);
            KD_ProductsEditor.Copy(performance); KD_ProductsEditor.Copy(live);
            KD_LiveSetupEditor.Setup();
            var catalog = KD_ProductsEditor.SceneComponents<KD_ShopCatalog>().Single(c => !c.name.StartsWith("Test"));
            var ledger = KD_ProductsEditor.SceneComponents<KD_ProgressLedger>().Single(c => !c.name.StartsWith("Test"));
            var existing = KD_ProductsEditor.SceneComponents<KD_ShopItem>();
            foreach (var definition in KD_ProductDefinitions.All.Where(d => d.key == "live.candy_step_set"))
                if (!existing.Any(i => i.name == definition.sceneName))
                {
                    var go = new GameObject(definition.sceneName);
                    go.transform.SetParent(existing.Single(i => i.productKey == "live.kominka_pack").transform.parent, false);
                    var item = go.AddUdonSharpComponent<KD_ShopItem>(); item.menuOnly = true;
                    KD_ProductDefinitions.Apply(item, definition.key); KD_ProductsEditor.Copy(item);
                }
            KD_ProductsEditor.BindCatalog(catalog, ledger);
            var pouch = Find(scene, KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            KD_HandMenuScrollEditor.RefreshLiveCatalog(pouch, log);
            KD_AccessorySetsEditor.RefreshCandyIntegration(performance.accessoryBox, pouch, log);
            KD_DogPreviewEditor.RefreshAccessoryItems(pouch, log);
            var language = KD_ProductsEditor.SceneComponents<KD_Language>().Single();
            var translations = KD_LocalizationEditor.Load();
            KD_LocalizationEditor.Configure(language, translations, log);
            KD_LocalizationEditor.Bind(language, translations, log);
            KD_ProductsEditor.Copy(language);
            live.hostingItems = pouch.liveItems; KD_ProductsEditor.Copy(live);
            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Logs/live-add"); File.WriteAllText("Logs/live-add/apply.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Approved Lives/2 - Verify")]
        public static void Verify()
        {
            Guard(); var v = new KD_VerifyLog("Approved lives"); var scene = SceneManager.GetActiveScene();
            var live = Find(scene, KD_DogDefs.LiveSystemName).GetComponent<KD_LiveSystem>();
            v.Check(live.stageRoots.Length == KD_DogDefs.LiveShows.Length && live.hostingItems.Length == KD_DogDefs.LiveShows.Length, "all registered live entries");
            for (int i = 0; i < 8; i++)
            {
                var show = KD_DogDefs.LiveShows[i]; var stage = Find(scene, KD_DogDefs.LiveStageNameOf(show.slug));
                v.Check(live.stageRoots[i] == stage && live.stageDirectors[i] == stage.GetComponent<PlayableDirector>(), "stage " + i);
                v.Check(!stage.activeSelf && !live.stageDirectors[i].playOnAwake, "idle " + i);
                v.Check(live.hostingItems[i].productKey == show.productKey, "entitlement " + i);
            }
            var candy = live.candyPerformance;
            v.Check(live.candyLiveId == 7 && candy != null && candy.liveSystem == live && !candy.previewSceneOnly
                && !candy.autoStartPreview && !candy.autoBeginPerformance && candy.gameObject.activeInHierarchy, "production coordinator");
            v.Check(candy.dogs.Length == 6 && candy.slots.Length == 6 && candy.costumeNodes.Length == 18, "six dogs and eighteen fitted costume nodes");
            v.Check(Mathf.Abs(live.stageDurations[6] - 112) < .01f && Mathf.Abs(live.stageDurations[7] - 124.8f) < .01f, "music boundaries");
            for (int i = 6; i < 8; i++)
            {
                var expected = AssetDatabase.LoadAssetAtPath<AudioClip>(KD_LiveSetupEditor.ShowFolderOf(KD_DogDefs.LiveShows[i].slug) + "/master.wav");
                var timeline = live.stageDirectors[i].playableAsset as TimelineAsset;
                var audio = timeline.GetOutputTracks().OfType<AudioTrack>().SelectMany(t => t.GetClips())
                    .Select(c => c.asset as AudioPlayableAsset).Where(a => a != null).ToArray();
                v.Check(audio.Length == 1 && audio[0].clip == expected, "Timeline uses canonical audio " + i);
            }
            for (int i = 0; i < 6; i++) v.Check(candy.dogs[i].dogRoot.name == "KD_Dog_" + KD_CandyDogSetupEditor.DogKeys[i], "dog roster " + i);
            v.Check(candy.dogLightLayers.Length == 54, "54 dog-following light bindings");
            v.Check(candy.LiveConfigurationValid(), "runtime production configuration");
            foreach (var proxy in new UdonSharpBehaviour[] { live, candy })
            {
                var serialized = new SerializedObject(proxy); var p = serialized.GetIterator();
                while (p.Next(true)) if (p.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var c = p.objectReferenceValue as Component; var g = p.objectReferenceValue as GameObject;
                    if (c != null && !EditorUtility.IsPersistent(c)) v.Check(c.gameObject.scene == scene, proxy.name + "/" + p.propertyPath);
                    if (g != null && !EditorUtility.IsPersistent(g)) v.Check(g.scene == scene, proxy.name + "/" + p.propertyPath);
                }
            }
            v.Finish("Logs/live-add/verify.txt");
            if (v.Fail > 0) throw new InvalidOperationException("Approved lives verification failed");
        }
    }
}
