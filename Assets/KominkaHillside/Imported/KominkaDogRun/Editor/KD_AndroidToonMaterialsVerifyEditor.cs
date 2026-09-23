using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaDogRun.Editor
{
    public static class KD_AndroidToonMaterialsVerifyEditor
    {
        const string ProbePath = "Assets/KominkaHillside/Imported/KominkaDogRun/KD_AndroidToonBuildProbe.unity";

        static GameObject Fixture(Scene scene)
        {
            var root = new GameObject(KD_AndroidToonMaterialsEditor.WorldRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            for (int i = 0; i < KD_AndroidToonMaterialsEditor.SourceGuids.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = i == KD_AndroidToonMaterialsEditor.CatEyeIndex ? "KD_ProbeCatEye"
                    : i == 4 ? "KD_ProbeBoard" : i == 0 ? "KD_ProbePoster" : "KD_ProbeBlanket" + i;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = Vector3.right * i;
                go.GetComponent<Renderer>().sharedMaterial = KD_AndroidToonMaterialsEditor.Source(i);
            }
            return root;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Android Toon Materials/2 - Verify")]
        public static void Verify()
        {
            KD_PlushShelfEditor.Guard();
            var authoring = SceneManager.GetActiveScene();
            var log = new KD_VerifyLog("Android and iOS poster, blanket and cat eye verification");
            var sourceTexts = KD_AndroidToonMaterialsEditor.SourceGuids.Select(g => File.ReadAllText(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            var originals = KD_AndroidToonMaterialsEditor.Renderers(authoring).Select(r => r.sharedMaterials).ToArray();
            for (int i = 0; i < KD_AndroidToonMaterialsEditor.SourceGuids.Length; i++)
            {
                var source = KD_AndroidToonMaterialsEditor.Source(i);
                var mat = KD_AndroidToonMaterialsEditor.Replacement(i);
                string originalShader = i == 4 ? "Hidden/lilToonTransparent" : "lilToon";
                log.Check(source != mat && source.shader.name == originalShader, source.name + " original shader preserved");
                string expectedShader = i == 4 ? "KominkaDogRun/Mobile/Poster Board Transparent" : i == 0 ? "Unlit/Texture" : "VRChat/Mobile/Toon Lit";
                log.Check(mat.shader.name == expectedShader, mat.name + " shader=" + expectedShader);
                log.Info(mat.name + " texture=" + AssetDatabase.GetAssetPath(mat.mainTexture)
                    + " scale=" + mat.mainTextureScale + " offset=" + mat.mainTextureOffset);
                var matches = KD_AndroidToonMaterialsEditor.Renderers(authoring).Where(r => r.sharedMaterials.Contains(source)).ToArray();
                int expectedCount = i == 4 ? 4 : i == 0 || i == KD_AndroidToonMaterialsEditor.CatEyeIndex ? 1 : 2;
                log.Check(matches.Length == expectedCount, source.name + " expected authoring renderers=" + expectedCount + " actual=" + matches.Length);
                foreach (var renderer in matches) log.Info("SOURCE " + AnimationUtility.CalculateTransformPath(renderer.transform, null));
            }
            log.Check(KD_AndroidToonMaterialsEditor.Replacement(1).mainTextureScale == new Vector2(12, 12), "Blanket plaid 12x12 preserved");
            log.Check(KD_AndroidToonMaterialsEditor.Replacement(2).mainTextureScale == new Vector2(6, 6), "Blanket houndstooth 6x6 preserved");
            log.Check(KD_AndroidToonMaterialsEditor.Replacement(3).mainTextureScale == new Vector2(6, 6), "Blanket block 6x6 preserved");
            log.Check(KD_AndroidToonMaterialsEditor.CountSourceSlots(authoring, BuildTarget.Android) == 12, "Poster, six blankets, four board cubes and one cat eye: 12 source slots");
            log.Check(KD_AndroidToonMaterialsEditor.CountSourceSlots(authoring, BuildTarget.iOS) == 12, "iOS authoring targets: poster, six blankets, four boards and the cat eye");
            var boards = KD_AndroidToonMaterialsEditor.Renderers(authoring)
                .Where(r => r.sharedMaterials.Contains(KD_AndroidToonMaterialsEditor.Source(4))).ToArray();
            log.Check(boards.All(r => r.name == "Cube") && boards.Select(r => r.transform.parent.name).OrderBy(n => n)
                .SequenceEqual(new[] { "ポスターボードYSS", "ポスターボードmamenoco", "ポスターボードnocotto", "ポスターボードyukilens" }.OrderBy(n => n)),
                "Exactly YSS and three BoothPoster board cubes use the transparent source");
            var boardMaterial = KD_AndroidToonMaterialsEditor.Replacement(4);
            log.Check(boardMaterial.GetFloat("_AlphaMaskScale") == -1f && boardMaterial.GetFloat("_AlphaMaskValue") == 1.1f,
                "Board retains inverse mask: saturate(1.1 - sampled red)");
            VerifyBoardBlend(log, boardMaterial);
            var boardContract = new Material(KD_AndroidToonMaterialsEditor.Source(4));
            try
            {
                boardContract.SetTextureScale("_MainTex", Vector2.one * 2f);
                boardContract.SetTextureScale("_AlphaMask", Vector2.one * 2f);
                bool rejected = false;
                try { KD_AndroidToonMaterialsEditor.ValidateSource(boardContract, 4); }
                catch (UnityEditor.Build.BuildFailedException) { rejected = true; }
                log.Check(rejected, "Reject compounded main/mask UV transforms instead of changing transparency silently");
                boardContract.SetTextureScale("_AlphaMask", Vector2.one);
                KD_AndroidToonMaterialsEditor.ValidateSource(boardContract, 4);
                log.Check(true, "Main texture UV transform with identity mask transform remains supported");
            }
            finally { UnityEngine.Object.DestroyImmediate(boardContract); }
            // Native callback called outside a build must leave the authoring scene alone.
            new KD_AndroidToonMaterialsEditor().OnProcessScene(authoring, null);

            var testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(testScene);
            try
            {
                var root = Fixture(testScene);
                log.Check(!KD_AndroidToonMaterialsEditor.ShouldProcess(testScene, BuildTarget.StandaloneWindows64, true, false), "PC builds excluded");
                log.Check(KD_AndroidToonMaterialsEditor.ShouldProcess(testScene, BuildTarget.iOS, true, false), "iOS world build accepted");
                log.Check(!KD_AndroidToonMaterialsEditor.ShouldProcess(testScene, BuildTarget.iOS, false, false), "iOS editing excluded");
                log.Check(!KD_AndroidToonMaterialsEditor.ShouldProcess(testScene, BuildTarget.iOS, true, true), "iOS Play excluded");
                log.Check(!KD_AndroidToonMaterialsEditor.ShouldProcess(testScene, BuildTarget.Android, false, false), "Editing excluded");
                log.Check(!KD_AndroidToonMaterialsEditor.ShouldProcess(testScene, BuildTarget.Android, true, true), "Play excluded");
                root.name = "KD_UnrelatedWorld";
                log.Check(!KD_AndroidToonMaterialsEditor.ShouldProcess(testScene, BuildTarget.Android, true, false), "Other worlds excluded");
                log.Check(!KD_AndroidToonMaterialsEditor.ShouldProcess(testScene, BuildTarget.iOS, true, false), "iOS other worlds excluded");
                root.name = KD_AndroidToonMaterialsEditor.WorldRoot;
                log.Check(KD_AndroidToonMaterialsEditor.ShouldProcess(testScene, BuildTarget.Android, true, false), "Android world build accepted");
                var control = GameObject.CreatePrimitive(PrimitiveType.Quad);
                control.transform.SetParent(root.transform, false);
                var untouched = control.GetComponent<Renderer>().sharedMaterial;
                root.transform.GetChild(4).gameObject.SetActive(false);
                root.transform.GetChild(1).gameObject.SetActive(false);
                log.Check(KD_AndroidToonMaterialsEditor.ReplaceForBuild(testScene, BuildTarget.StandaloneWindows64) == 0, "PC replacement changes no slots");
                log.Check(KD_AndroidToonMaterialsEditor.ReplaceForBuild(testScene, BuildTarget.iOS) == KD_AndroidToonMaterialsEditor.SourceGuids.Length, "iOS replaces all six materials including inactive blanket and board");
                log.Check(KD_AndroidToonMaterialsEditor.CountSourceSlots(testScene, BuildTarget.iOS) == 0, "iOS has no eligible source slots remaining");
                log.Check(KD_AndroidToonMaterialsEditor.CountSourceSlots(testScene, BuildTarget.Android) == 0, "iOS also replaces all three blanket source slots");
                for (int i = 0; i < KD_AndroidToonMaterialsEditor.SourceGuids.Length; i++)
                    log.Check(root.transform.GetChild(i).GetComponent<Renderer>().sharedMaterial ==
                        KD_AndroidToonMaterialsEditor.Replacement(i),
                        "iOS fixture material mapping " + i);
                log.Check(control.GetComponent<Renderer>().sharedMaterial == untouched, "iOS unrelated material unchanged");
                log.Check(KD_AndroidToonMaterialsEditor.ReplaceForBuild(testScene, BuildTarget.iOS) == 0, "iOS repeated processing is idempotent");
                for (int i = 0; i < KD_AndroidToonMaterialsEditor.SourceGuids.Length; i++)
                    root.transform.GetChild(i).GetComponent<Renderer>().sharedMaterial = KD_AndroidToonMaterialsEditor.Source(i);
                var inactive = root.transform.GetChild(1).gameObject;
                inactive.SetActive(false);
                log.Check(KD_AndroidToonMaterialsEditor.ReplaceForBuild(testScene, BuildTarget.Android) == KD_AndroidToonMaterialsEditor.SourceGuids.Length, "All six target materials replaced, including inactive object");
                log.Check(KD_AndroidToonMaterialsEditor.CountSourceSlots(testScene, BuildTarget.Android) == 0, "No source target slots remain in processed fixture");
                log.Check(inactive.GetComponent<Renderer>().sharedMaterial == KD_AndroidToonMaterialsEditor.Replacement(1), "Blanket gets its own mapped material");
                log.Check(root.transform.GetChild(0).GetComponent<Renderer>().sharedMaterial.shader.name == "Unlit/Texture", "Processed poster uses Unlit/Texture");
                for (int i = 2; i < 4; i++)
                    log.Check(root.transform.GetChild(i).GetComponent<Renderer>().sharedMaterial == KD_AndroidToonMaterialsEditor.Replacement(i), "Added blanket variant " + i + " gets its own pattern");
                log.Check(root.transform.GetChild(4).GetComponent<Renderer>().sharedMaterial == boardMaterial, "Board gets the transparent mask material");
                log.Check(root.transform.GetChild(KD_AndroidToonMaterialsEditor.CatEyeIndex).GetComponent<Renderer>()
                    .sharedMaterial.shader.name == "VRChat/Mobile/Toon Lit", "Processed cat eye stays lit instead of falling back to Unlit/Texture");
                log.Check(control.GetComponent<Renderer>().sharedMaterial == untouched, "Unrelated material unchanged");
                log.Check(KD_AndroidToonMaterialsEditor.ReplaceForBuild(testScene, BuildTarget.Android) == 0, "Repeated processing is idempotent");
            }
            catch (Exception e) { log.Check(false, e.ToString()); }
            finally
            {
                SceneManager.SetActiveScene(authoring);
                EditorSceneManager.CloseScene(testScene, true);
            }
            var after = KD_AndroidToonMaterialsEditor.Renderers(authoring);
            log.Check(after.Length == originals.Length && after.Select((r, i) => r.sharedMaterials.SequenceEqual(originals[i])).All(x => x),
                "Every authoring renderer material assignment preserved");
            log.Check(KD_AndroidToonMaterialsEditor.SourceGuids.Select((g, i) => File.ReadAllText(AssetDatabase.GUIDToAssetPath(g)) == sourceTexts[i]).All(x => x),
                "All source material files unchanged");
            log.Finish("Temp/kd-android-toon-verify.txt");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Android Toon Materials/3 - Verify Android Build Callback")]
        public static void VerifyBuildCallback() => VerifyBuildCallback(BuildTarget.Android);

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Android Toon Materials/4 - Verify iOS Build Callback")]
        public static void VerifyIOSBuildCallback() => VerifyBuildCallback(BuildTarget.iOS);

        static void VerifyBuildCallback(BuildTarget target)
        {
            KD_PlushShelfEditor.Guard();
            if (EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException("Select " + target + " first; this probe does not switch platforms");
            if (File.Exists(ProbePath)) throw new InvalidOperationException("Probe path already exists; will not overwrite");
            var original = SceneManager.GetActiveScene();
            if (original.isDirty && !EditorSceneManager.SaveScene(original))
                throw new InvalidOperationException("Could not preserve current scene");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var log = new KD_VerifyLog("Native " + target + " AssetBundle callback probe (not a VRChat upload)");
            string output = "Temp/kd-" + target.ToString().ToLowerInvariant() + "-toon-bundle";
            int expected = KD_AndroidToonMaterialsEditor.SourceGuids.Length;
            int before = File.Exists(KD_AndroidToonMaterialsEditor.BuildLog) ? File.ReadAllText(KD_AndroidToonMaterialsEditor.BuildLog).Length : 0;
            try
            {
                Fixture(scene);
                if (!EditorSceneManager.SaveScene(scene, ProbePath)) throw new InvalidOperationException("Could not save disposable fixture");
                EditorSceneManager.CloseScene(scene, true);
                SceneManager.SetActiveScene(original);
                Directory.CreateDirectory(output);
                var manifest = BuildPipeline.BuildAssetBundles(output,
                    new[] { new AssetBundleBuild { assetBundleName = "kd-toon-probe", assetNames = new[] { ProbePath } } },
                    BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression, target);
                log.Check(manifest != null, target + " fixture bundle built");
                string evidence = File.Exists(KD_AndroidToonMaterialsEditor.BuildLog)
                    ? File.ReadAllText(KD_AndroidToonMaterialsEditor.BuildLog).Substring(before) : "";
                log.Check(evidence.Contains("target=" + target + " scene=" + ProbePath + " replaced=" + expected + " remainingSourceSlots=0"), "Native build callback replaced exactly " + expected + " eligible targets");
                log.Info(evidence);
                log.Check(KD_AndroidToonMaterialsEditor.CountSourceSlots(original, target) == 12, "Live authoring scene still uses original materials");
            }
            catch (Exception e) { log.Check(false, e.ToString()); }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                SceneManager.SetActiveScene(original);
                AssetDatabase.DeleteAsset(ProbePath); // Only the fixture created above.
            }
            log.Info("Real device appearance and lighting remain unverified.");
            log.Finish("Temp/kd-" + target.ToString().ToLowerInvariant() + "-toon-build-verify.txt");
        }

        static void VerifyBoardBlend(KD_VerifyLog log, Material board)
        {
            var test = new Material(board);
            var texture = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            var pixels = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
            var target = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            bool fog = RenderSettings.fog;
            try
            {
                RenderSettings.fog = false;
                target.Create();
                foreach (float red in new[] { 0f, .8f, 1f })
                {
                    // Source texture alpha is deliberately zero: mode 1 replaces it with mask R.
                    var sample = new Color(red, .4f, .2f, 0f);
                    texture.SetPixels(new[] { sample, sample, sample, sample }); texture.Apply();
                    RenderTexture.active = target; GL.Clear(true, true, Color.blue);
                    Graphics.Blit(texture, target, test);
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 8, 8), 0, 0); pixels.Apply();
                    var actual = pixels.GetPixel(4, 4);
                    float alpha = Mathf.Clamp01(1.1f - red);
                    var expected = sample * alpha + Color.blue * (1f - alpha);
                    log.Check(Mathf.Abs(actual.r - expected.r) < .02f && Mathf.Abs(actual.g - expected.g) < .02f
                        && Mathf.Abs(actual.b - expected.b) < .02f,
                        "GPU board blend red=" + red + " alpha=" + alpha + " actual=" + actual + " expected RGB=" + expected);
                }
                log.Check(!ShaderUtil.ShaderHasError(board.shader), "Board shader compiles without errors");
            }
            finally
            {
                RenderSettings.fog = fog; RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(test); UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(pixels); target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
