using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // IMP-ENV-010: Android/iOS builds swap every Terrain to KominkaHillside/Terrain Lite with cheaper LOD.
    // Changes only the build's scene copy (same pattern as KD_AndroidToonMaterialsEditor); PC, Editor and Play keep Standard.
    // TerrainData is a shared asset, so it is never touched here.
    public sealed class KH_MobileTerrainBuildEditor : IProcessSceneWithReport
    {
        internal const string ShaderName = "KominkaHillside/Terrain Lite";
        internal const string BaseShaderName = "Hidden/KominkaHillside/Terrain Lite Base";
        internal const string BaseGenShaderName = "Hidden/TerrainEngine/Splatmap/Diffuse-BaseGen";
        internal const string StandardShaderName = "Nature/Terrain/Standard";
        internal const string StandardMaterialName = "Default-Terrain-Standard.mat";
        internal const string MaterialPath = "Assets/KominkaHillside/Materials/KH_TerrainLite.mat";
        internal const string WorldRoot = "KH_World";
        internal const string BuildLog = "Temp/kh-mobile-terrain-build.txt";
        internal const string VerifyPath = "Temp/kh-mobile-terrain-verify.json";
        internal const float MobilePixelError = 20f, MobileBasemapDistance = 60f, MobileDetailDistance = 20f;
        // Authoring values (Issue §1); the preview restores these and refuses to start from anything else.
        internal const float PcPixelError = 8f, PcBasemapDistance = 1000f, PcDetailDistance = 30f;
        public int callbackOrder => 1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // AssetBundle builds may have a null report; null is not a Play-mode test.
            var target = report != null ? report.summary.platform : EditorUserBuildSettings.activeBuildTarget;
            if (!ShouldProcess(scene, target, BuildPipeline.isBuildingPlayer, EditorApplication.isPlayingOrWillChangePlaymode)) return;
            var lite = LiteMaterial(); // Validate before any mutation.
            int replaced = Apply(Terrains(scene), lite);
            Directory.CreateDirectory("Temp");
            File.AppendAllText(BuildLog, DateTime.Now.ToString("s") + " target=" + target + " scene=" + scene.path
                + " replaced=" + replaced + " remainingStandard=" + CountStandard(Terrains(scene)) + "\n");
        }

        internal static bool ShouldProcess(Scene scene, BuildTarget target, bool building, bool playing)
            => building && !playing && (target == BuildTarget.Android || target == BuildTarget.iOS) && scene.IsValid()
                && scene.GetRootGameObjects().Any(g => g.name == WorldRoot);

        internal static Terrain[] Terrains(Scene scene)
            => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>(true)).ToArray();

        internal static Material LiteMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null || mat.shader == null || mat.shader.name != ShaderName)
                throw new BuildFailedException("Missing/stale " + MaterialPath + ". Run Tools/KominkaHillside/Mobile/10 - Create Terrain Lite Material");
            // Without the base shader distant tiles fall back to Unity's default; fail loudly instead.
            if (Shader.Find(BaseShaderName) == null) throw new BuildFailedException("Shader missing: " + BaseShaderName);
            return mat;
        }

        internal static bool IsStandard(Terrain t)
            => t.materialTemplate == null || t.materialTemplate.shader == null || t.materialTemplate.shader.name == StandardShaderName;

        internal static int CountStandard(IEnumerable<Terrain> terrains) => terrains.Count(IsStandard);

        // Only the build callback and the preview menu call this. treeBillboardDistance and drawInstanced stay as authored.
        internal static int Apply(Terrain[] terrains, Material lite)
        {
            foreach (var t in terrains)
            {
                t.materialTemplate = lite;
                t.heightmapPixelError = MobilePixelError;
                t.basemapDistance = MobileBasemapDistance;
                t.detailObjectDistance = MobileDetailDistance;
            }
            return terrains.Length;
        }

        [MenuItem("Tools/KominkaHillside/Mobile/10 - Create Terrain Lite Material")]
        public static void CreateMaterial()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("Shader " + ShaderName + " not found (import first)");
            if (Shader.Find(BaseShaderName) == null) throw new InvalidOperationException("Shader " + BaseShaderName + " not found (import first)");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "KH_TerrainLite" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.shader = shader;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[KH Mobile Terrain] Material ready: " + MaterialPath + " (" + ShaderName + "). Authoring scene unchanged.");
        }

        [MenuItem("Tools/KominkaHillside/Mobile/11 - Preview Terrain Lite (toggle)")]
        public static void PreviewToggle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.GetRootGameObjects().Any(g => g.name == WorldRoot))
                throw new InvalidOperationException("Active scene has no " + WorldRoot + " root");
            var terrains = Terrains(scene);
            if (terrains.Length == 0) throw new InvalidOperationException("No Terrain in " + scene.path);
            var standard = AssetDatabase.GetBuiltinExtraResource<Material>(StandardMaterialName);
            if (standard == null) throw new InvalidOperationException("Built-in " + StandardMaterialName + " not found");
            // OFF must work even if the Lite material/shader broke while the preview was on: anything not Standard turns off.
            bool turnOff = terrains.Any(t => t.materialTemplate != standard);
            Material lite = null;
            if (!turnOff)
            {
                lite = LiteMaterial();
                // OFF restores hardcoded authoring values, so only start from exactly those values.
                var odd = terrains.Where(t => t.materialTemplate != standard || t.heightmapPixelError != PcPixelError
                    || t.basemapDistance != PcBasemapDistance || t.detailObjectDistance != PcDetailDistance).Select(t => t.name).ToArray();
                if (odd.Length > 0) throw new InvalidOperationException("Terrains not at authoring values (Standard/8/1000/30), refusing preview: " + string.Join(", ", odd));
            }
            Undo.RecordObjects(terrains.Cast<UnityEngine.Object>().ToArray(), "Preview Terrain Lite");
            if (turnOff)
            {
                foreach (var t in terrains)
                {
                    t.materialTemplate = standard;
                    t.heightmapPixelError = PcPixelError;
                    t.basemapDistance = PcBasemapDistance;
                    t.detailObjectDistance = PcDetailDistance;
                }
            }
            else Apply(terrains, lite);
            EditorSceneManager.MarkSceneDirty(scene);
            if (turnOff) Debug.Log("[KH Mobile Terrain] PREVIEW OFF - " + terrains.Length + " terrains back to " + StandardMaterialName + " / 8 / 1000 / 30. Scene is dirty; saving now is harmless but not needed.");
            else Debug.LogWarning("[KH Mobile Terrain] PREVIEW ON - do not save. " + terrains.Length + " terrains on " + ShaderName + " / "
                + MobilePixelError + " / " + MobileBasemapDistance + " / " + MobileDetailDistance + ". Run this menu again to turn it off.");
        }

        [Serializable] class VerifyReport
        {
            public string utc, scene, status;
            public bool liteShaderFound, baseShaderFound, baseGenShaderFound, materialExists, materialUsesLite;
            public string materialShader;
            public int terrainCount, onStandard, onLite, onOther;
            public string[] baseMapResolutions;
            public float mobilePixelError = MobilePixelError, mobileBasemapDistance = MobileBasemapDistance, mobileDetailDistance = MobileDetailDistance;
            public float pcPixelError = PcPixelError, pcBasemapDistance = PcBasemapDistance, pcDetailDistance = PcDetailDistance;
            public bool shouldAndroidBuild, shouldIosBuild, shouldPcBuild, shouldAndroidNotBuilding;
            public List<string> errors = new List<string>();
        }

        [MenuItem("Tools/KominkaHillside/Mobile/12 - Verify Mobile Terrain")]
        public static void Verify()
        {
            var scene = SceneManager.GetActiveScene();
            var r = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = scene.path };
            r.liteShaderFound = Shader.Find(ShaderName) != null;
            r.baseShaderFound = Shader.Find(BaseShaderName) != null;
            r.baseGenShaderFound = Shader.Find(BaseGenShaderName) != null;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            r.materialExists = mat != null;
            r.materialShader = mat != null && mat.shader != null ? mat.shader.name : "";
            r.materialUsesLite = r.materialShader == ShaderName;
            var terrains = Terrains(scene);
            r.terrainCount = terrains.Length;
            r.onStandard = terrains.Count(IsStandard);
            r.onLite = terrains.Count(t => mat != null && t.materialTemplate == mat);
            r.onOther = r.terrainCount - r.onStandard - r.onLite;
            // Read-only: base map memory scales with this (one RGBA texture per tile).
            r.baseMapResolutions = terrains.Where(t => t.terrainData != null).Select(t => t.terrainData.baseMapResolution.ToString()).Distinct().ToArray();
            r.shouldAndroidBuild = ShouldProcess(scene, BuildTarget.Android, true, false);
            r.shouldIosBuild = ShouldProcess(scene, BuildTarget.iOS, true, false);
            r.shouldPcBuild = ShouldProcess(scene, BuildTarget.StandaloneWindows64, true, false);
            r.shouldAndroidNotBuilding = ShouldProcess(scene, BuildTarget.Android, false, false);

            void Req(bool ok, string what) { if (!ok) r.errors.Add("FAIL " + what); }
            Req(r.liteShaderFound, ShaderName + " found");
            Req(r.baseShaderFound, BaseShaderName + " found");
            Req(r.baseGenShaderFound, BaseGenShaderName + " found (BaseMapGenShader dependency)");
            Req(r.materialExists && r.materialUsesLite, MaterialPath + " uses " + ShaderName);
            Req(r.terrainCount > 0, "terrains in active scene");
            Req(r.onLite == 0 && r.onOther == 0 && r.onStandard == r.terrainCount, "authoring scene all on Standard (preview OFF)");
            Req(r.shouldAndroidBuild && r.shouldIosBuild && !r.shouldPcBuild && !r.shouldAndroidNotBuilding, "ShouldProcess Android/iOS/PC/not-building = true/true/false/false");
            r.status = r.errors.Count == 0 ? "PASS" : "FAIL";
            Directory.CreateDirectory("Temp");
            File.WriteAllText(VerifyPath, JsonUtility.ToJson(r, true));
            Debug.Log("[KH Mobile Terrain] Verify " + r.status + " -> " + VerifyPath + (r.errors.Count > 0 ? "\n" + string.Join("\n", r.errors) : ""));
        }
    }
}
