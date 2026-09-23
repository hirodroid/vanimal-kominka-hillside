using System;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PhotoSetupEditor
    {
        const string GalleryFontPath = "Assets/KominkaHillside/Font/KD_NotoSerif SDF.asset";
        const string GalleryBackgroundPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_GalleryTextBackground.mat";

        internal static void RemoveLegacyGalleryReferences(KD_Language language)
        {
            // Deleted U# fields can remain in the backing variable table after a script reload.
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(language);
            if (backing != null)
            {
                backing.publicVariables.RemoveVariable("translatedRenderers");
                backing.publicVariables.RemoveVariable("localizedMaterials");
                backing.publicVariables.RemoveVariable("japaneseMaterials");
                backing.publicVariables.RemoveVariable("englishMaterials");
                EditorUtility.SetDirty(backing);
            }
        }

        static bool IsLegacyGalleryMaterial(Material material)
        {
            string path = AssetDatabase.GetAssetPath(material);
            const string prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_";
            if (path == prefix + "PhotoCaption.mat" || path == prefix + "GalleryENCaption.mat") return true;
            for (int i = 0; i < SlotGroupCount; i++)
                if (path == prefix + "PhotoLabel" + i + ".mat" || path == prefix + "GalleryEN" + i + ".mat") return true;
            return false;
        }

        // Keep the existing quad anchors: the photo/share panels use their dimensions and positions.
        internal static void BuildGalleryText(Transform gallery, TMP_FontAsset font,
            KD_LocalizationEditor.Catalog catalog, StringBuilder log)
        {
            if (gallery == null || font == null) throw new InvalidOperationException("Gallery/font missing");
            // The SDK also retains materials in the descriptor even after renderers stop using them.
            var descriptor = UnityEngine.Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (descriptor != null && descriptor.DynamicMaterials != null)
            {
                var kept = descriptor.DynamicMaterials.Where(m => !IsLegacyGalleryMaterial(m)).ToList();
                if (kept.Count != descriptor.DynamicMaterials.Count)
                {
                    descriptor.DynamicMaterials = kept;
                    EditorUtility.SetDirty(descriptor);
                }
            }
            var background = AssetDatabase.LoadAssetAtPath<Material>(GalleryBackgroundPath);
            if (background == null)
            {
                background = new Material(Shader.Find("Unlit/Color"));
                background.color = new Color(0.13f, 0.11f, 0.09f);
                AssetDatabase.CreateAsset(background, GalleryBackgroundPath);
            }
            for (int i = 0; i < KD_LocalizationEditor.GallerySigns; i++)
            {
                bool caption = i == SlotGroupCount;
                var anchor = gallery.Find(caption ? "Caption" : "Label" + i);
                if (anchor == null) throw new InvalidOperationException("Gallery sign anchor missing: " + i);
                anchor.GetComponent<MeshRenderer>().sharedMaterial = background;
                var child = anchor.Find("Text");
                if (child == null)
                {
                    child = new GameObject("Text", typeof(TextMeshPro)).transform;
                    child.SetParent(anchor, false);
                }
                child.gameObject.layer = anchor.gameObject.layer;
                child.localPosition = new Vector3(0, 0, -0.001f);
                child.localRotation = Quaternion.identity;
                // Cancel the quad's nonuniform size so glyphs retain their aspect ratio.
                child.localScale = new Vector3(0.001f / anchor.localScale.x, 0.001f / anchor.localScale.y, 0.001f);
                var text = child.GetComponent<TextMeshPro>();
                text.rectTransform.sizeDelta = new Vector2(anchor.localScale.x, anchor.localScale.y) * 1000f;
                text.font = font;
                text.fontSharedMaterial = font.material;
                text.isOrthographic = true;
                text.color = Color.white;
                text.alignment = TextAlignmentOptions.Center;
                text.enableAutoSizing = true;
                text.fontSize = text.fontSizeMax = caption ? 38f : 96f;
                text.fontSizeMin = caption ? 26f : 64f;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.margin = caption ? new Vector4(24, 18, 24, 18) : new Vector4(16, 8, 16, 8);
                text.richText = true;
                text.text = KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage,
                    caption ? "gallery.caption" : "gallery.label." + i);
                var renderer = text.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                text.ForceMeshUpdate();
            }
            log.AppendLine("Gallery: 5 TMP signs, shared font/background; no localized image materials");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo System/7 - Setup Gallery Text")]
        public static void SetupGalleryText()
        {
            var language = KD_SceneObjects.Find("KD_Language").GetComponent<KD_Language>();
            var gallery = KD_SceneObjects.Find(SystemName).transform.Find("Gallery");
            var catalog = KD_LocalizationEditor.Load();
            var log = new StringBuilder();
            BuildGalleryText(gallery, language.pouch.sectionTitleLabel.font, catalog, log);
            KD_LocalizationEditor.Configure(language, catalog, log);
            KD_LocalizationEditor.Bind(language, catalog, log);
            EditorUtility.SetDirty(language);
            UdonSharpEditorUtility.CopyProxyToUdon(language);
            EditorSceneManager.MarkSceneDirty(gallery.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        internal static void ValidateGalleryText(KD_Language language)
        {
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(language);
            if (backing != null && backing.publicVariables.VariableSymbols.Any(s => s == "translatedRenderers" || s == "localizedMaterials"
                || s == "japaneseMaterials" || s == "englishMaterials"))
                throw new InvalidOperationException("Legacy gallery variables remain in backing Udon");
            var descriptor = UnityEngine.Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (descriptor != null && descriptor.DynamicMaterials != null && descriptor.DynamicMaterials.Any(IsLegacyGalleryMaterial))
                throw new InvalidOperationException("Legacy gallery materials remain in scene descriptor");
            var gallery = KD_SceneObjects.Find(SystemName).transform.Find("Gallery");
            for (int i = 0; i < KD_LocalizationEditor.GallerySigns; i++)
            {
                var anchor = gallery.Find(i == SlotGroupCount ? "Caption" : "Label" + i);
                var text = anchor == null ? null : anchor.Find("Text")?.GetComponent<TextMeshPro>();
                string key = i == SlotGroupCount ? "gallery.caption" : "gallery.label." + i;
                int binding = Array.IndexOf(language.fixedKeys, key);
                if (text == null || binding < 0 || language.fixedLabels[binding] != text
                    || text.font != language.pouch.sectionTitleLabel.font
                    || AssetDatabase.GetAssetPath(anchor.GetComponent<MeshRenderer>().sharedMaterial) != GalleryBackgroundPath
                    || anchor.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Invalid gallery TMP sign: " + key);
            }
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo System/8 - Verify Gallery Text")]
        public static void VerifyGalleryText()
        {
            var language = KD_SceneObjects.Find("KD_Language").GetComponent<KD_Language>();
            var catalog = KD_LocalizationEditor.Load();
            KD_LocalizationEditor.ValidateScene(language, catalog);
            var log = new StringBuilder();
            var labels = new TMP_Text[KD_LocalizationEditor.GallerySigns];
            var keys = new string[labels.Length];
            var saved = new string[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                keys[i] = i == SlotGroupCount ? "gallery.caption" : "gallery.label." + i;
                labels[i] = language.fixedLabels[Array.IndexOf(language.fixedKeys, keys[i])];
                saved[i] = labels[i].text;
            }
            // Exercise the runtime Apply path without changing the scene language or player preferences.
            var preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("KD_GalleryLanguageFixture");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                var fixture = root.AddUdonSharpComponent<KD_Language>();
                KD_LocalizationEditor.Configure(fixture, catalog, log);
                fixture.fixedLabels = labels;
                fixture.fixedKeys = keys;
                typeof(KD_Language).GetField("_initialized", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).SetValue(fixture, true);
                foreach (var entry in catalog.languages)
                {
                    fixture._SelectLanguage(entry.code);
                    for (int i = 0; i < labels.Length; i++)
                    {
                        var text = labels[i];
                        text.ForceMeshUpdate();
                        var rect = text.rectTransform.rect;
                        var bounds = text.textBounds;
                        bool outside = bounds.min.x < rect.xMin + text.margin.x - 0.5f
                            || bounds.max.x > rect.xMax - text.margin.z + 0.5f
                            || bounds.min.y < rect.yMin + text.margin.w - 0.5f
                            || bounds.max.y > rect.yMax - text.margin.y + 0.5f;
                        if (text.text != KD_LocalizationEditor.Text(catalog, entry.code, keys[i]))
                            throw new InvalidOperationException("Gallery text mismatch: " + entry.code + ":" + keys[i]);
                        // IMP-INF-010: glyphs=client の言語は Editor で幅・行数を測れない (未収録字は □/空白幅)。配置は実機で確認する
                        if (!KD_LocalizationEditor.Bundled(entry)) { log.AppendLine("CLIENT_UNMEASURED " + entry.code + ":" + keys[i]); continue; }
                        if (text.isTextOverflowing || outside || text.textInfo.lineCount != (i == SlotGroupCount ? 3 : 1))
                            throw new InvalidOperationException("Gallery text overflow: " + entry.code + ":" + keys[i]);
                        log.AppendLine(entry.code + ":" + keys[i] + " size=" + text.fontSize + " lines=" + text.textInfo.lineCount
                            + " atMinimum=" + (text.fontSize <= text.fontSizeMin) + " bounds=" + bounds.size);
                    }
                    KD_GalleryRenderEditor.Render();
                    System.IO.File.Copy("Temp/gallery/gallery-wall.png", "Temp/gallery/gallery-" + entry.code + ".png", true);
                }
                log.AppendLine("RESULT: PASS (5 signs; runtime language switch, layout, shared font/background)");
                KD_VerifyLog.Write("Logs/gallery-text-verify.txt", log);
            }
            finally
            {
                for (int i = 0; i < labels.Length; i++) { labels[i].text = saved[i]; labels[i].ForceMeshUpdate(); }
                UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
    }
}
