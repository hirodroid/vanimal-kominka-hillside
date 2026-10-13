using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace KominkaHillside.Editor
{
    public static partial class KH_WashSelectionEditor
    {
        const string SharedFontPath = "Assets/KominkaHillside/Font/KD_NotoSerif SDF.asset";
        const string OldCareFontGuid = "fb81e2f2c0d1a134c9e77503690ff420";
        const string OldLatinFontGuid = "3c45b7dcd97b5fe468e910290c32b348";
        const string OldSourceGuid = "ffdd824a78645bf41b291f7e042e0ff6";
        const string PhotoBrand = "VAnimal  /  CARE COLLECTION";
        const string PhotoFooter = "古民家の裏山  ·  お手入れの時間";

        // IMP-UI-007: no font generation. Missing glyphs use the VRChat client's fallback.
        [MenuItem(Menu + "43 - Apply Shared Font")]
        public static void PrepareFont()
        {
            Guard();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Reconcile/save scene edits before applying fonts");
            var font = Font();
            var boards = UnityEngine.Object.FindObjectsOfType<KH_WashSelectionBoard>(true)
                .Where(b => b.gameObject.scene == scene).ToArray();
            if (boards.Length != 4) throw new InvalidOperationException("Expected four selection boards");
            var labels = boards.SelectMany(b => b.GetComponentsInChildren<TMP_Text>(true)).Distinct().ToArray();
            // Reject unrelated font overrides before changing anything.
            foreach (var text in labels)
            {
                var path = AssetDatabase.GetAssetPath(text.font);
                if (path != SharedFontPath && path != Data + "CareFont.asset")
                    throw new InvalidOperationException("Unexpected font on " + text.name + ": " + path);
            }
            int changed = 0;
            foreach (var text in labels)
            {
                if (text.font == font && text.fontSharedMaterial == font.material) continue;
                Undo.RecordObject(text, "Use shared wash font");
                text.font = font;
                text.fontSharedMaterial = font.material;
                EditorUtility.SetDirty(text);
                changed++;
            }
            if (changed > 0) EditorSceneManager.MarkSceneDirty(scene);
            Log("font", "Shared font applied: labels=" + labels.Length + " changed=" + changed
                + "; no glyph baking; scene not saved. Save this scene explicitly, then run 51.");
        }

        [MenuItem(Menu + "51 - Verify Font Policy")]
        public static void VerifyFontPolicy()
        {
            Guard();
            var log = new StringBuilder();
            int failures = 0;
            Action<bool, string> check = (ok, message) => { log.AppendLine((ok ? "PASS " : "FAIL ") + message); if (!ok) failures++; };
            var scene = EditorSceneManager.GetActiveScene();
            var font = Font();
            var boards = UnityEngine.Object.FindObjectsOfType<KH_WashSelectionBoard>(true).Where(b => b.gameObject.scene == scene).ToArray();
            var words = ReadWords();
            var labels = boards.SelectMany(b => b.GetComponentsInChildren<TMP_Text>(true)).Distinct().ToArray();
            check(boards.Length == 4 && labels.Length > 0, "four boards, labels=" + labels.Length);
            check(font.atlasPopulationMode == AtlasPopulationMode.Static && font.sourceFontFile == null, "shared font remains Static without runtime source");
            check(words.languages.Length == 13 && words.languages.All(l => l.words.Length == WordCount), "13 language word tables");
            var expectedWords = words.languages.SelectMany(l => l.words).ToArray();
            foreach (var board in boards)
                check(board.words != null && board.words.SequenceEqual(expectedWords), "unchanged words: " + board.name);
            foreach (var text in labels)
                check(text.font == font && text.fontSharedMaterial == font.material, "shared font/material: " + text.name);
            foreach (var sub in boards.SelectMany(b => b.GetComponentsInChildren<TMP_SubMeshUI>(true)))
                check(!IsOldFontAsset(sub.fontAsset) && !IsOldFontAsset(sub.sharedMaterial), "no old submesh font/material: " + sub.name);
            var oldDependencies = AssetDatabase.GetDependencies(scene.path, true).Where(p => IsOldFontGuid(AssetDatabase.AssetPathToGUID(p))).ToArray();
            check(oldDependencies.Length == 0, "saved old font dependencies=" + oldDependencies.Length + " " + string.Join(", ", oldDependencies));
            var savedScene = File.ReadAllText(scene.path);
            check(!savedScene.Contains(OldCareFontGuid) && !savedScene.Contains(OldLatinFontGuid) && !savedScene.Contains(OldSourceGuid),
                "no old GUIDs in saved scene (including missing asset references)");
            foreach (var lang in words.languages)
            {
                var missing = new string(string.Concat(lang.words).Where(c => !char.IsControl(c) && !font.HasCharacter(c, true)).Distinct().OrderBy(c => c).ToArray());
                log.AppendLine("CLIENT_CHECK " + lang.code + " fallback characters=" + missing.Length + " " + missing);
            }
            log.AppendLine("NOT_TESTED VRChat fallback glyphs, wrapping and overflow on target devices (13 languages / 5 tabs / status text)");
            var photoMissing = MissingPhotoCharacters();
            log.AppendLine("PHOTO_PREFLIGHT missing=" + photoMissing.Length + " " + photoMissing + "; image-only temporary Static font uses existing source when needed");
            Log("font-policy", (failures == 0 ? "PASS" : "FAIL=" + failures) + "\n" + log);
            if (failures != 0) throw new InvalidOperationException("Font policy failures=" + failures);
        }

        static bool IsOldFontGuid(string guid) => guid == OldCareFontGuid || guid == OldLatinFontGuid || guid == OldSourceGuid;
        static bool IsOldFontAsset(UnityEngine.Object asset) => asset != null && IsOldFontGuid(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)));

        // Only the captions baked into Editor-generated PNGs need a local, complete font.
        static string PhotoCharacters()
        {
            var words = ReadWords().languages[0].words;
            var titles = Enumerable.Range(0, 5).Select(g => words[10 + g] + "セット");
            return new string((PhotoBrand + PhotoFooter + string.Concat(titles) + string.Concat(ListingSubtitles))
                .Where(c => !char.IsControl(c)).Distinct().OrderBy(c => c).ToArray());
        }

        static string MissingPhotoCharacters() => new string(PhotoCharacters().Where(c => !Font().HasCharacter(c, true)).ToArray());

        static void WithListingFont(Action<TMP_FontAsset> render)
        {
            TMP_FontAsset temporary = null;
            try
            {
                var font = Font();
                if (MissingPhotoCharacters().Length > 0)
                {
                    // Reuse the existing source. No new font file/asset and no changes to the scene font.
                    var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/KominkaHillside/Font/Noto_Serif_JP/static/NotoSerifJP-Regular.ttf");
                    if (source == null) throw new InvalidOperationException("Existing listing font source missing; PNGs retained");
                    temporary = TMP_FontAsset.CreateFontAsset(source, 48, 4, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                        1024, 1024, AtlasPopulationMode.Dynamic, false);
                    if (temporary == null) throw new InvalidOperationException("Could not create temporary listing font");
                    temporary.name = "KH_TemporaryListingFont";
                    temporary.hideFlags = HideFlags.HideAndDontSave;
                    temporary.TryAddCharacters(PhotoCharacters(), out _);
                    temporary.atlasPopulationMode = AtlasPopulationMode.Static;
                    var serialized = new SerializedObject(temporary);
                    serialized.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue = null;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    temporary.material.name = temporary.name + " Material";
                    temporary.material.hideFlags = HideFlags.HideAndDontSave;
                    foreach (var texture in temporary.atlasTextures) if (texture != null) { texture.name = temporary.name + " Atlas"; texture.hideFlags = HideFlags.HideAndDontSave; }
                    font = temporary;
                }
                var missing = new string(PhotoCharacters().Where(c => !font.HasCharacter(c, true)).ToArray());
                if (missing.Length > 0 || font.atlasPopulationMode != AtlasPopulationMode.Static || font.sourceFontFile != null)
                    throw new InvalidOperationException("Listing Static font preflight failed; PNGs retained. Missing: " + missing);
                render(font);
            }
            finally
            {
                if (temporary != null)
                {
                    var material = temporary.material;
                    var textures = temporary.atlasTextures;
                    UnityEngine.Object.DestroyImmediate(temporary);
                    if (material != null) UnityEngine.Object.DestroyImmediate(material);
                    if (textures != null) foreach (var texture in textures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        [MenuItem(Menu + "52 - Preview Listing Font")]
        public static void PreviewListingFont()
        {
            Guard();
            WithListingFont(font =>
            {
                Directory.CreateDirectory("Temp/font-cleanup");
                var words = ReadWords().languages[0].words;
                Shoot(Order, "Temp/font-cleanup/listing-font-preview.png", words[14] + "セット", ListingSubtitles[4], 1024, font);
                Log("font-photo", "PASS all caption characters=" + PhotoCharacters().Length + " glyphs=" + font.glyphTable.Count
                    + " Static=" + (font.atlasPopulationMode == AtlasPopulationMode.Static) + " sourceNull=" + (font.sourceFontFile == null)
                    + " persistent=" + EditorUtility.IsPersistent(font));
            });
            if (Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Any(o => o.name.StartsWith("KH_TemporaryListingFont", StringComparison.Ordinal)))
                throw new InvalidOperationException("Temporary listing font was not released");
        }
    }
}
