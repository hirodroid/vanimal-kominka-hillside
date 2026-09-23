using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinProductionEditor
    {
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pumpkin Production/1 - Apply")]
        public static void Apply()
        {
            Guard(); Directory.CreateDirectory(Evidence);
            if (SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveOpenScenes();
            File.Copy(Main, Evidence + "/before-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", false);
            var log = new StringBuilder();
            KD_ApprovedLivesEditor.Import(Source, Slug, true, log, "KD_PumpkinCostume_", "KD_PumpkinDogPerformance");
            var stage = KD_SceneObjects.Find(KD_DogDefs.LiveStageNameOf(Slug));
            int lights = 0;
            foreach (var light in stage.GetComponentsInChildren<Light>(true))
            {
                if (!light.name.StartsWith("KD_PumpkinFaceFill_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected show Light: " + light.name);
                Object.DestroyImmediate(light); lights++;
            }
            log.AppendLine("Excluded preview FaceFill Light components: " + lights);
            var live = KD_SceneObjects.Find(KD_DogDefs.LiveSystemName).GetComponent<KD_LiveSystem>();
            var performance = KD_SceneObjects.Find("KD_PumpkinDogPerformance").GetComponent<KD_CandyDogPerformance>();
            performance.previewSceneOnly = false; performance.autoStartPreview = false; performance.autoBeginPerformance = false;
            performance.liveSystem = live; performance.gameObject.SetActive(true);
            var registrations = new KD_CandyDogPerformance[KD_DogDefs.LiveShows.Length];
            if (live.dogPerformances != null) Array.Copy(live.dogPerformances, registrations, Math.Min(registrations.Length, live.dogPerformances.Length));
            registrations[live.candyLiveId] = live.candyPerformance;
            registrations[Array.FindIndex(KD_DogDefs.LiveShows, s => s.slug == Slug)] = performance;
            live.dogPerformances = registrations;
            var rider = performance.dogs[performance.mountRider];
            var controller = rider.dogAnimator.runtimeAnimatorController as AnimatorController;
            string path = AssetDatabase.GetAssetPath(controller);
            if (controller == null || !path.StartsWith("Assets/KominkaHillside/Imported/KominkaDogRun/", StringComparison.Ordinal)
                || path == KD_PumpkinPreviewEditor.MountControllerPath)
                throw new InvalidOperationException("Production must retain its latest self-owned Pome controller.");
            if (!File.Exists(Evidence + "/Pome-controller-before.controller")) File.Copy(path, Evidence + "/Pome-controller-before.controller");
            var definition = KD_DogDefs.All.Single(d => d.key == "Pome");
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(definition.idle.guid));
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(KD_PumpkinPreviewEditor.MountHopPath) == null)
                throw new InvalidOperationException("Restore the approved native-clip-derived Pumpkin hop before applying production.");
            performance.mountAnimatorLayer = KD_PumpkinPreviewEditor.ConfigureMountLayer(controller, idle);
            EditorUtility.SetDirty(controller);
            KD_ProductsEditor.Copy(performance); KD_ProductsEditor.Copy(live);
            KD_LiveSetupEditor.Setup();
            var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            KD_AccessorySetsEditor.RefreshCandyIntegration(performance.accessoryBox, pouch, log);
            var catalog = KD_ProductsEditor.SceneComponents<KD_ShopCatalog>().Single(c => !c.name.StartsWith("Test"));
            var ledger = KD_ProductsEditor.SceneComponents<KD_ProgressLedger>().Single(c => !c.name.StartsWith("Test"));
            KD_ProductsEditor.BindCatalog(catalog, ledger);
            KD_HandMenuScrollEditor.RefreshLiveCatalog(pouch, log);
            KD_DogPreviewEditor.RefreshAccessoryItems(pouch, log);
            var language = KD_ProductsEditor.SceneComponents<KD_Language>().Single();
            var translations = KD_LocalizationEditor.Load();
            KD_LocalizationEditor.Configure(language, translations, log);
            KD_LocalizationEditor.Bind(language, translations, log);
            var font = pouch.sectionTitleLabel.font;
            string missing = new string(KD_LocalizationEditor.BundledCharacters(translations)
                .Where(c => !char.IsControl(c) && c != '\u200b' && !KD_LocalizationEditor.HasGlyph(font, c)).Distinct().ToArray());
            log.AppendLine("Font: " + font.atlasWidth + "x" + font.atlasHeight + ", glyphs=" + font.glyphTable.Count + ", missing=" + missing.Length);
            if (missing.Length > 0)
            {
                var sourceFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/KominkaHillside/Font/Noto_Serif_JP/static/NotoSerifJP-Regular.ttf");
                if (sourceFont == null) throw new InvalidOperationException("Japanese source font missing.");
                var serialized = new SerializedObject(font);
                serialized.FindProperty("m_SourceFontFile").objectReferenceValue = sourceFont;
                serialized.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue = sourceFont;
                serialized.ApplyModifiedPropertiesWithoutUndo(); font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                font.TryAddCharacters(missing, out _); EditorUtility.SetDirty(font);
            }
            KD_LocalizationEditor.ValidateScene(language, translations);
            KD_ProductsEditor.Copy(language);
            live.hostingItems = pouch.liveItems; KD_ProductsEditor.Copy(live);
            stage.SetActive(false);
            foreach (var node in performance.costumeNodes) node.SetActive(false);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            File.WriteAllText(Evidence + "/apply.txt", log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pumpkin Production/2 - Verify")]
        public static void Verify()
        {
            Guard(); var v = new KD_VerifyLog("Pumpkin production");
            var scene = SceneManager.GetActiveScene();
            var live = KD_SceneObjects.Find(KD_DogDefs.LiveSystemName).GetComponent<KD_LiveSystem>();
            var p = KD_SceneObjects.Find("KD_PumpkinDogPerformance").GetComponent<KD_CandyDogPerformance>();
            var stage = KD_SceneObjects.Find(KD_DogDefs.LiveStageNameOf(Slug));
            v.Check(KD_DogDefs.LiveShows.Length == 9 && live.stageRoots.Length == 9 && live.hostingItems.Length == 9, "nine works, existing IDs retained");
            v.Check(live.candyLiveId == 7 && live.DogPerformanceOf(7) == live.candyPerformance && live.DogPerformanceOf(8) == p, "work registry: Candy7 Pumpkin8");
            v.Check(live.stageRoots[8] == stage && live.stageDirectors[8] == p.director, "stage and director identity");
            v.Check(Mathf.Abs(live.stageDurations[8] - 120f) < .01f && Math.Abs(p.duration - 120f) < .01f, "120-second musical boundary");
            v.Check(!stage.activeSelf && !p.director.playOnAwake && !p.previewSceneOnly && !p.autoStartPreview && !p.autoBeginPerformance, "explicit production start only");
            v.Check(p.LiveConfigurationValid() && p.useMount && p.useChoreography, "runtime configuration with mount");
            v.Check(stage.GetComponentsInChildren<Light>(true).Length == 0, "no Unity Lights in imported show");
            v.Check(p.costumeNodes.Length == 18 && p.costumeGlowRenderers.Length == 18 && p.costumeNodes.Distinct().Count() == 18, "eighteen unique fitted costumes with emission");
            var costumes = p.costumeNodes.ToHashSet();
            v.Check(p.accessoryBox.nodes.Skip(126).Take(18).ToHashSet().SetEquals(costumes), "normal selection uses the same Pumpkin costumes");
            var item = live.hostingItems[8];
            v.Check(item.productKey == "live.pumpkin_parade_set" && item.IsCE() && !item.IsFree() && !item.Owned() && !item.CanBuy(), "unlisted product fails closed");
            v.Check(p.accessoryBox.setItems[4] == item && p.accessoryBox.productItems.Skip(21).Take(3).All(i => i == item), "one entitlement for show and costumes");
            for (int i = 0; i < 6; i++)
                v.Check(p.dogs[i].dogRoot.name == "KD_Dog_" + KD_CandyDogSetupEditor.DogKeys[i] && p.slots[i].IsChildOf(stage.transform), "main roster and stage slot " + i);
            var controller = p.dogs[p.mountRider].dogAnimator.runtimeAnimatorController as AnimatorController;
            v.Check(controller != null && controller.layers[p.mountAnimatorLayer].name == KD_PumpkinPreviewEditor.MountLayerName
                && controller.layers[p.mountAnimatorLayer].defaultWeight == 0, "mount layer starts at zero on current controller");
            foreach (var node in p.costumeNodes)
                v.Check(!node.activeSelf && node.GetComponentInChildren<Renderer>(true).sharedMaterial.GetFloat("_EmissionGain") == 0f, "idle costume " + node.name);
            foreach (var component in stage.GetComponentsInChildren<Component>(true).Concat(new Component[] { live, p }))
            {
                if (component == null) { v.Check(false, "missing component"); continue; }
                var serialized = new SerializedObject(component); var property = serialized.GetIterator();
                while (property.Next(true)) if (property.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var value = property.objectReferenceValue;
                    if (value == null || EditorUtility.IsPersistent(value)) continue;
                    var go = value as GameObject ?? (value as Component)?.gameObject;
                    if (go != null) v.Check(go.scene == scene, component.name + "/" + property.propertyPath + " is in main scene");
                }
            }
            var timeline = (TimelineAsset)p.director.playableAsset;
            foreach (var track in timeline.GetOutputTracks())
                if (!(track is MarkerTrack)) v.Check(p.director.GetGenericBinding(track) != null, "Timeline binding " + track.name);
            string audioPath = KD_LiveSetupEditor.ShowFolderOf(Slug) + "/audio.wav";
            using (var hash = System.Security.Cryptography.SHA256.Create())
                v.Check(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(audioPath))).Replace("-", "").ToLowerInvariant()
                    == "4d285b28684be0123580c1e6c8d44271c30db3e6780c0adfaed683c2d1531ae1", "accepted music010 hash");
            v.Finish(Evidence + "/verify.txt");
            if (v.Fail != 0) throw new InvalidOperationException("Pumpkin production verification failed.");
        }
    }
}
