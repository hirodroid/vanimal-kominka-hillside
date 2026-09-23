using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaDogRun.Editor
{
    // Changes only the build's scene copy, never source materials or the authoring scene.
    public sealed class KD_AndroidToonMaterialsEditor : IProcessSceneWithReport
    {
        internal const int BoardIndex = 4;
        internal const int CatEyeIndex = 5;
        // The cat eye keeps the default lit shader: its red iris only reads as an eye under lighting (IMP-CAT-001).
        internal static string ShaderName(int index) => index == BoardIndex ? "KominkaDogRun/Mobile/Poster Board Transparent"
            : index == 0 ? "Unlit/Texture" : "VRChat/Mobile/Toon Lit";
        internal const string WorldRoot = "====KominkaDogRun====";
        internal const string BuildLog = "Temp/kd-android-toon-build.txt";
        internal static readonly string[] SourceGuids = {
            "e9e0a60e77689824fbc5ad1b36042379", // PosterMaterial_YSS
            "e2d4e001700dd464fa3c08b471b4bc19", // OrangeBlue plaid material variant
            "3e83599542621fa449d43ddb3befbb31", // ClassicHoundstoo Blue
            "97556e10241bed149a4a61712e9eb14b", // BlockCheck Warm
            "a38a7026028569f4ea7702e9e0cf531a", // Poster board Body: YSS and three BoothPoster cubes
            "c41080f527d0b9848b89dcdcd8634705"  // Cat eye: TORINOSUKE kijineko "me 4"
        };
        internal static readonly string[] Destinations = {
            "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_PosterYSS_Android.mat",
            "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_BlanketPlaid_Android.mat",
            "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_BlanketHoundstooth_Android.mat",
            "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_BlanketBlock_Android.mat",
            "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_PosterBoard_Android.mat",
            "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_CatEye_Android.mat"
        };
        public int callbackOrder => 1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // AssetBundle builds may have a null report; null is not a Play-mode test.
            var target = report != null ? report.summary.platform : EditorUserBuildSettings.activeBuildTarget;
            if (!ShouldProcess(scene, target, BuildPipeline.isBuildingPlayer, EditorApplication.isPlayingOrWillChangePlaymode)) return;
            int changed = ReplaceForBuild(scene, target);
            Directory.CreateDirectory("Temp");
            File.AppendAllText(BuildLog, DateTime.Now.ToString("s") + " target=" + target
                + " scene=" + scene.path + " replaced=" + changed + " remainingSourceSlots=" + CountSourceSlots(scene, target) + "\n");
        }

        internal static bool ShouldProcess(Scene scene, BuildTarget target, bool building, bool playing)
            => building && !playing && (target == BuildTarget.Android || target == BuildTarget.iOS) && scene.IsValid()
                && scene.GetRootGameObjects().Any(g => g.name == WorldRoot);

        // Both mobile targets share the poster and blanket fixes (BUG-ENV-003).
        internal static int[] TargetIndices(BuildTarget target)
            => Enumerable.Range(0, SourceGuids.Length).Where(i => target == BuildTarget.Android
                || target == BuildTarget.iOS).ToArray();

        internal static Material Source(int index)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(SourceGuids[index]));
            if (mat == null) throw new BuildFailedException("Missing Android Toon source material: " + SourceGuids[index]);
            return mat;
        }

        // Only the blankets promote lilToon's second texture; every other source converts its main texture.
        internal static string TextureProperty(int index) => index > 0 && index < BoardIndex ? "_Main2ndTex" : "_MainTex";

        internal static void ValidateSource(Material source, int index)
        {
            string expectedShader = index == BoardIndex ? "Hidden/lilToonTransparent" : "lilToon";
            if (source.shader == null || source.shader.name != expectedShader || source.GetColor("_Color") != Color.white
                || source.GetVector("_MainTexHSVG") != new Vector4(0f, 1f, 1f, 1f)
                || source.GetFloat("_UseMain3rdTex") != 0f)
                throw new BuildFailedException("Source appearance changed; review Android Toon conversion: " + source.name);
            if ((index == 0 || index == BoardIndex) && source.GetFloat("_UseMain2ndTex") != 0f)
                throw new BuildFailedException("Poster now uses layered color; review Android conversion");
            if (index > 0 && index < BoardIndex && (source.GetTexture("_MainTex") != null || source.GetFloat("_UseMain2ndTex") != 1f
                || source.GetColor("_Color2nd") != Color.white || source.GetFloat("_Main2ndTexBlendMode") != 3f
                || source.GetTexture("_Main2ndBlendMask") != null || source.GetFloat("_Main2ndTex_UVMode") != 0f
                || source.GetFloat("_Main2ndTexAngle") != 0f || source.GetFloat("_Main2ndTexIsDecal") != 0f))
                throw new BuildFailedException("Blanket no longer has a simple multiply plaid layer; review Android conversion");
            if (source.GetTexture(TextureProperty(index)) == null)
                throw new BuildFailedException("Missing Android base texture: " + source.name);
            if (index == BoardIndex && (source.GetFloat("_AlphaMaskMode") != 1f || source.GetFloat("_Cull") != 2f
                || source.GetTexture("_AlphaMask") != source.GetTexture("_MainTex")
                // lilToon applies mask ST after main ST; a single sample needs identity mask ST.
                || source.GetTextureScale("_AlphaMask") != Vector2.one
                || source.GetTextureOffset("_AlphaMask") != Vector2.zero
                || source.GetVector("_MainTex_ScrollRotate") != Vector4.zero))
                throw new BuildFailedException("Poster board mask is no longer the shared static texture in replace-alpha mode");
            // The eye is opaque with no alpha anywhere; what Unlit/Texture dropped was lighting and the gold MatCap.
            if (index == CatEyeIndex && (source.GetFloat("_UseMain2ndTex") != 0f || source.GetFloat("_TransparentMode") != 0f
                || source.renderQueue != 2000 || source.GetFloat("_AsUnlit") != 0f || source.GetFloat("_Invisible") != 0f
                || source.GetFloat("_UseMatCap") != 1f || source.GetTexture("_MatCapTex") == null
                || source.GetFloat("_UseMatCap2nd") != 0f))
                throw new BuildFailedException("Cat eye is no longer an opaque lit lilToon with one MatCap; review Android conversion: " + source.name);
        }

        internal static Material Replacement(int index)
        {
            var source = Source(index);
            ValidateSource(source, index);
            string prop = TextureProperty(index);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(Destinations[index]);
            if (mat == null || mat.shader == null || mat.shader.name != ShaderName(index)
                || mat.GetTexture("_MainTex") != source.GetTexture(prop)
                || mat.GetTextureScale("_MainTex") != source.GetTextureScale(prop)
                || mat.GetTextureOffset("_MainTex") != source.GetTextureOffset(prop))
                throw new BuildFailedException("Missing/stale Android material. Run Android Toon Materials/1 - Prepare: " + Destinations[index]);
            if (index == BoardIndex && (mat.GetColor("_Color") != source.GetColor("_Color")
                || mat.GetFloat("_AlphaMaskScale") != source.GetFloat("_AlphaMaskScale")
                || mat.GetFloat("_AlphaMaskValue") != source.GetFloat("_AlphaMaskValue") || mat.renderQueue != 3000))
                throw new BuildFailedException("Stale Android board alpha mask. Run Android Toon Materials/1 - Prepare");
            return mat;
        }

        internal static Renderer[] Renderers(Scene scene)
            => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToArray();

        internal static int CountSourceSlots(Scene scene, BuildTarget target)
        {
            var sources = TargetIndices(target).Select(Source).ToArray();
            return Renderers(scene).Sum(r => r.sharedMaterials.Count(m => sources.Contains(m)));
        }

        // Only the native build callback and disposable verification fixtures call this.
        internal static int ReplaceForBuild(Scene scene, BuildTarget target)
        {
            var indices = TargetIndices(target);
            var sources = indices.Select(Source).ToArray();
            var replacements = indices.Select(Replacement).ToArray(); // Validate selected targets before any mutation.
            int changed = 0;
            foreach (var renderer in Renderers(scene))
            {
                var materials = renderer.sharedMaterials;
                bool modified = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    int index = Array.IndexOf(sources, materials[i]);
                    if (index < 0) continue;
                    materials[i] = replacements[index];
                    modified = true;
                    changed++;
                }
                if (modified) renderer.sharedMaterials = materials;
            }
            return changed;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Android Toon Materials/1 - Prepare")]
        public static void Prepare()
        {
            KD_PlushShelfEditor.Guard();
            var shaders = Enumerable.Range(0, SourceGuids.Length).Select(i => Shader.Find(ShaderName(i))).ToArray();
            if (shaders.Any(s => s == null)) throw new InvalidOperationException("Android replacement shader missing");
            for (int i = 0; i < SourceGuids.Length; i++) ValidateSource(Source(i), i);
            for (int i = 0; i < SourceGuids.Length; i++)
            {
                var source = Source(i);
                var shader = shaders[i];
                string prop = TextureProperty(i);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(Destinations[i]);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, Destinations[i]);
                }
                mat.shader = shader;
                mat.SetTexture("_MainTex", source.GetTexture(prop));
                mat.SetTextureScale("_MainTex", source.GetTextureScale(prop));
                mat.SetTextureOffset("_MainTex", source.GetTextureOffset(prop));
                if (i == BoardIndex)
                {
                    mat.SetColor("_Color", source.GetColor("_Color"));
                    mat.SetFloat("_AlphaMaskScale", source.GetFloat("_AlphaMaskScale"));
                    mat.SetFloat("_AlphaMaskValue", source.GetFloat("_AlphaMaskValue"));
                    mat.renderQueue = 3000;
                    mat.enableInstancing = true;
                }
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
            KD_VerifyLog.Write("Temp/kd-android-toon-prepare.txt", "Prepared " + SourceGuids.Length + " owned materials. Authoring scene and source materials were not replaced.");
        }
    }
}
