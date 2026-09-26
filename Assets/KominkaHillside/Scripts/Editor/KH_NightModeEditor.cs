using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // IMP-ENV-002: sky shared with KominkaDogRun + local night mode. Setup writes the wiring and the design values
    // (code is the source: KH_NightMode.Default*, the day sky constants); _Rotation of the sky is written only when the
    // material is first created (the sun's bearing is tuned in the Inspector). Verify counts the expected state from a
    // separate menu. Design: docs/plans/2026-09-21-sky-and-night-mode.md.
    public static class KH_NightModeEditor
    {
        const string WorldScene = "Assets/Scenes/VRCDefaultWorldScene.unity";
        const string ProgramPath = "Assets/KominkaHillside/Scripts/KH_NightMode";
        public const string SkyMaterialPath = "Assets/KominkaHillside/Materials/KH_Skybox.mat";
        public const string OverlayMaterialPath = "Assets/KominkaHillside/Materials/KH_NightOverlay.mat";
        public const string QuadMeshPath = "Assets/KominkaHillside/Materials/KH_FullscreenQuad.asset";
        public const string OverlayShaderPath = "Assets/KominkaHillside/Shaders/KH_NightOverlay.shader";
        public const string OverlayShaderName = "KominkaHillside/KH_NightOverlay";
        // Purchased Allsky cubemap (git-ignored). Same GUID as KominkaDogRun's KD_Skybox.mat, so the reference survives a clone.
        public const string CubemapPath = "Assets/Allsky/Day Sun High/Day Sun High Clear/Day Sun High Clear.png";
        public const string CubemapGuid = "34019144ed5734479a3edb1af4ba307d";
        public const string SkyShaderName = "Skybox/Cubemap";
        public const float SkyRotationOnCreate = 180f;   // KD_Skybox.mat value; Inspector owns it afterwards
        public const int MobileMaxTextureSize = 1024;     // Android / iPhone override (IMP-ENV-010); PC stays 2048
        public const string RootName = "KH_NightMode", OverlayName = "Overlay", SkyHolderName = "SkyHolder", QuadMeshName = "KH_FullscreenQuad";
        const float QuadBounds = 200000f;
        static readonly string[] MobilePlatforms = { "Android", "iPhone" };
        static Color OverlayTint { get { return new Color(KH_NightMode.OverlayTintR, KH_NightMode.OverlayTintG, KH_NightMode.OverlayTintB, 1f); } }

        static void Report(string name, string text) { Directory.CreateDirectory("Temp/KH_Night"); File.WriteAllText("Temp/KH_Night/" + name + ".txt", text); Debug.Log("[KH Night] " + text); }
        static void Guard() { if (Application.isPlaying || EditorSceneManager.GetActiveScene().path != WorldScene) throw new InvalidOperationException("Open the hillside scene in Edit mode."); }

        [MenuItem("Tools/KominkaHillside/Night/1 Setup Sky And Night")]
        public static void Setup()
        {
            Guard(); var scene = EditorSceneManager.GetActiveScene(); var log = new StringBuilder();
            // U# program asset first: other steps add the component, which needs a compiled program.
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") == null)
            {
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ProgramPath + ".cs");
                AssetDatabase.CreateAsset(asset, ProgramPath + ".asset"); AssetDatabase.SaveAssets();
                UdonSharpProgramAsset.CompileAllCsPrograms(true);
                Report("night-setup", "created KH_NightMode program asset; wait for the U# compile, then run Setup again.");
                return;
            }

            // Sky: same material settings as KominkaDogRun's KD_Skybox.mat.
            var cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(CubemapPath);
            if (cubemap == null) throw new InvalidOperationException("Allsky cubemap missing at " + CubemapPath + " (purchased asset, git-ignored).");
            var skyShader = Shader.Find(SkyShaderName); if (skyShader == null) throw new InvalidOperationException("Shader " + SkyShaderName + " not found.");
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (sky == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SkyMaterialPath));
                sky = new Material(skyShader); sky.SetFloat("_Rotation", SkyRotationOnCreate);
                AssetDatabase.CreateAsset(sky, SkyMaterialPath); log.AppendLine("created " + SkyMaterialPath + " (_Rotation " + SkyRotationOnCreate + ")");
            }
            if (sky.shader != skyShader) sky.shader = skyShader;
            sky.SetTexture("_Tex", cubemap);
            sky.SetFloat("_Exposure", KH_NightMode.DaySkyExposure);
            sky.SetColor("_Tint", new Color(KH_NightMode.DayTintR, KH_NightMode.DayTintG, KH_NightMode.DayTintB, KH_NightMode.SkyTintA));
            EditorUtility.SetDirty(sky);
            if (RenderSettings.skybox != sky) { RenderSettings.skybox = sky; log.AppendLine("RenderSettings.skybox -> KH_Skybox"); }
            // Mobile build size: cubemap 2048 -> 1024 on Android / iPhone only.
            var importer = AssetImporter.GetAtPath(CubemapPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("TextureImporter missing for " + CubemapPath);
            bool reimport = false;
            foreach (string platform in MobilePlatforms)
            {
                var ps = importer.GetPlatformTextureSettings(platform);
                if (ps.overridden && ps.maxTextureSize == MobileMaxTextureSize) continue;
                ps.overridden = true; ps.maxTextureSize = MobileMaxTextureSize; importer.SetPlatformTextureSettings(ps); reimport = true;
                log.AppendLine("override " + platform + " maxTextureSize " + MobileMaxTextureSize);
            }
            if (reimport) importer.SaveAndReimport();

            // Overlay: shader (hillside copy), material at 4999, full-screen quad mesh.
            var overlayShader = AssetDatabase.LoadAssetAtPath<Shader>(OverlayShaderPath);
            if (overlayShader == null || overlayShader.name != OverlayShaderName) throw new InvalidOperationException("Overlay shader missing or misnamed at " + OverlayShaderPath);
            var overlayMat = AssetDatabase.LoadAssetAtPath<Material>(OverlayMaterialPath);
            if (overlayMat == null) { overlayMat = new Material(overlayShader); AssetDatabase.CreateAsset(overlayMat, OverlayMaterialPath); log.AppendLine("created " + OverlayMaterialPath); }
            if (overlayMat.shader != overlayShader) overlayMat.shader = overlayShader;
            if (overlayMat.renderQueue != KH_NightMode.OverlayQueue) overlayMat.renderQueue = KH_NightMode.OverlayQueue;
            overlayMat.SetFloat("_Darkness", 0f); overlayMat.SetColor("_Tint", OverlayTint); EditorUtility.SetDirty(overlayMat);
            var quad = EnsureQuad(log);

            // Scene: root object (outside KH_World, which the terrain Build recreates) + Overlay + SkyHolder.
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (root == null) { var found = GameObject.Find(RootName); root = found != null ? found : new GameObject(RootName); if (root.transform.parent != null) root.transform.SetParent(null, true); log.AppendLine("root " + (found != null ? "reparented" : "created")); }
            root.transform.position = Vector3.zero; root.transform.rotation = Quaternion.identity; root.transform.localScale = Vector3.one;
            root.SetActive(true);   // Start/Update must run; the renderers (not the objects) carry the off state
            var overlay = Child(root.transform, OverlayName);
            var of = overlay.GetComponent<MeshFilter>(); if (of == null) of = overlay.AddComponent<MeshFilter>(); of.sharedMesh = quad;
            var or = overlay.GetComponent<MeshRenderer>(); if (or == null) or = overlay.AddComponent<MeshRenderer>();
            or.sharedMaterial = overlayMat; Quiet(or); or.enabled = false;
            var holder = Child(root.transform, SkyHolderName);
            var hf = holder.GetComponent<MeshFilter>(); if (hf == null) hf = holder.AddComponent<MeshFilter>(); hf.sharedMesh = quad;
            var hr = holder.GetComponent<MeshRenderer>(); if (hr == null) hr = holder.AddComponent<MeshRenderer>();
            hr.sharedMaterial = sky; Quiet(hr); hr.enabled = false;

            var night = root.GetComponent<KH_NightMode>(); if (night == null) night = root.AddUdonSharpComponent<KH_NightMode>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(night);
            if (backing.SyncMethod != Networking.SyncType.None) { backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing); }
            if (!backing.enabled) { backing.enabled = true; EditorUtility.SetDirty(backing); }
            if (!night.enabled) { night.enabled = true; }
            night.overlayRenderer = or; night.skyHolder = hr;
            night.onDarkness = KH_NightMode.DefaultOnDarkness; night.fadeSeconds = KH_NightMode.DefaultFadeSeconds; night.liveDarkness = 0f;
            UdonSharpEditorUtility.CopyProxyToUdon(night); EditorUtility.SetDirty(night);
            // Adding an Udon object leaves NetworkID entries; drop the dangling, typeless ones (vrc-scene-edit-safety).
            var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            if (descriptor != null) { int dropped = descriptor.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0)); if (dropped > 0) { EditorUtility.SetDirty(descriptor); log.AppendLine("dropped dangling NetworkIDs " + dropped); } }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            log.AppendLine("night setup: sky=KH_Skybox overlay=queue" + KH_NightMode.OverlayQueue + " onDarkness=" + KH_NightMode.DefaultOnDarkness + " fade=" + KH_NightMode.DefaultFadeSeconds + "s");
            Report("night-setup", log.ToString());
        }

        static GameObject Child(Transform parent, string name)
        {
            var t = parent.Find(name); GameObject go = t != null ? t.gameObject : null;
            if (go == null) { go = new GameObject(name); go.transform.SetParent(parent, false); }
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            go.SetActive(true);
            return go;
        }
        static void Quiet(MeshRenderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
        // Full-screen quad: vertices xy in -1..1 become clip coordinates in the shader; huge bounds so no camera culls it.
        static Mesh EnsureQuad(StringBuilder log)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(QuadMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = QuadMeshName };
                mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f) };
                mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * QuadBounds);
                AssetDatabase.CreateAsset(mesh, QuadMeshPath); log.AppendLine("created " + QuadMeshPath);
            }
            else if (mesh.bounds.size.x < QuadBounds * .5f) { mesh.bounds = new Bounds(Vector3.zero, Vector3.one * QuadBounds); EditorUtility.SetDirty(mesh); }
            return mesh;
        }

        [MenuItem("Tools/KominkaHillside/Night/2 Verify Sky And Night")]
        public static void Verify()
        {
            Guard(); var log = new StringBuilder(); int total = 0, failed = 0;
            void Check(bool ok, string what) { total++; if (!ok) failed++; log.AppendLine((ok ? "PASS " : "FAIL ") + what); }
            var scene = EditorSceneManager.GetActiveScene();
            // Sky
            var sky = RenderSettings.skybox;
            Check(sky != null && AssetDatabase.GetAssetPath(sky) == SkyMaterialPath, "RenderSettings.skybox is " + SkyMaterialPath + " (actual " + (sky == null ? "null" : AssetDatabase.GetAssetPath(sky)) + ")");
            Check(sky != null && sky.shader != null && sky.shader.name == SkyShaderName, "sky shader " + SkyShaderName);
            var tex = sky == null ? null : sky.GetTexture("_Tex");
            string texGuid = tex == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tex));
            Check(tex is Cubemap && texGuid == CubemapGuid, "sky _Tex is the Allsky cubemap (guid " + texGuid + ")");
            Check(sky != null && Mathf.Abs(sky.GetFloat("_Exposure") - KH_NightMode.DaySkyExposure) < 1e-4f, "sky _Exposure == day (" + (sky == null ? "-" : sky.GetFloat("_Exposure").ToString("F2")) + ")");
            var dayTint = new Color(KH_NightMode.DayTintR, KH_NightMode.DayTintG, KH_NightMode.DayTintB, KH_NightMode.SkyTintA);
            Check(sky != null && sky.GetColor("_Tint") == dayTint, "sky _Tint == day " + dayTint);
            var importer = AssetImporter.GetAtPath(CubemapPath) as TextureImporter;
            foreach (string platform in MobilePlatforms)
            {
                var ps = importer == null ? null : importer.GetPlatformTextureSettings(platform);
                Check(ps != null && ps.overridden && ps.maxTextureSize == MobileMaxTextureSize, platform + " override " + MobileMaxTextureSize + " (actual " + (ps == null ? "-" : (ps.overridden ? ps.maxTextureSize.ToString() : "not overridden")) + ")");
            }
            var pcPs = importer == null ? null : importer.GetPlatformTextureSettings("Standalone");
            Check(pcPs != null && !pcPs.overridden && importer.maxTextureSize == 2048, "Standalone keeps 2048 (no override)");
            // Overlay + holder + Udon
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            Check(root != null, RootName + " exists at scene root (outside KH_World)");
            var night = root == null ? null : root.GetComponent<KH_NightMode>();
            Check(night != null, "KH_NightMode component on root");
            if (night == null) { Report("night-verify", log + "TOTAL " + total + " FAILED " + failed); return; }
            var overlay = root.transform.Find(OverlayName); var holder = root.transform.Find(SkyHolderName);
            var or = overlay == null ? null : overlay.GetComponent<MeshRenderer>(); var hr = holder == null ? null : holder.GetComponent<MeshRenderer>();
            Check(or != null && night.overlayRenderer == or && !or.enabled, "Overlay renderer wired and disabled at start");
            Check(or != null && or.sharedMaterial != null && AssetDatabase.GetAssetPath(or.sharedMaterial) == OverlayMaterialPath && or.sharedMaterial.shader.name == OverlayShaderName, "Overlay material is KH_NightOverlay.mat with shader " + OverlayShaderName);
            Check(or != null && or.sharedMaterial != null && or.sharedMaterial.renderQueue == KH_NightMode.OverlayQueue, "Overlay renderQueue " + KH_NightMode.OverlayQueue);
            Check(or != null && or.sharedMaterial != null && Mathf.Abs(or.sharedMaterial.GetFloat("_Darkness")) < 1e-4f, "Overlay asset _Darkness 0 (runtime writes the instance)");
            Check(or != null && or.sharedMaterial != null && or.sharedMaterial.GetColor("_Tint") == OverlayTint, "Overlay asset _Tint == " + OverlayTint + " (code constant)");
            var omf = overlay == null ? null : overlay.GetComponent<MeshFilter>(); var mesh = omf == null ? null : omf.sharedMesh;
            Check(mesh != null && AssetDatabase.GetAssetPath(mesh) == QuadMeshPath && mesh.name == QuadMeshName && mesh.vertexCount == 4 && mesh.triangles.Length == 6, "Overlay mesh is " + QuadMeshPath + " (4 verts, 2 tris)");
            bool quadXY = mesh != null && mesh.vertices.All(v => Mathf.Abs(Mathf.Abs(v.x) - 1f) < 1e-5f && Mathf.Abs(Mathf.Abs(v.y) - 1f) < 1e-5f && Mathf.Abs(v.z) < 1e-5f);
            Check(quadXY && mesh.bounds.center == Vector3.zero && mesh.bounds.size.x >= QuadBounds * .5f && mesh.bounds.size.y >= QuadBounds * .5f && mesh.bounds.size.z >= QuadBounds * .5f, "quad vertices at xy +-1 (clip space), bounds centered and huge on xyz");
            var hmf = holder == null ? null : holder.GetComponent<MeshFilter>();
            Check(hmf != null && hmf.sharedMesh == mesh, "SkyHolder MeshFilter holds the same quad");
            Check(root.activeInHierarchy && overlay != null && overlay.gameObject.activeInHierarchy && holder != null && holder.gameObject.activeInHierarchy, "root / Overlay / SkyHolder GameObjects active (only the renderers are off)");
            Check(root.transform.position == Vector3.zero && root.transform.rotation == Quaternion.identity && root.transform.localScale == Vector3.one && overlay != null && overlay.localPosition == Vector3.zero && holder != null && holder.localPosition == Vector3.zero, "transforms at identity");
            bool quiet = or != null && hr != null && new[] { or, hr }.All(r => r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off && !r.receiveShadows && r.lightProbeUsage == UnityEngine.Rendering.LightProbeUsage.Off && r.reflectionProbeUsage == UnityEngine.Rendering.ReflectionProbeUsage.Off);
            Check(quiet, "renderers cast/receive no shadows, no light or reflection probes");
            Check(hr != null && night.skyHolder == hr && !hr.enabled && hr.sharedMaterial == sky, "SkyHolder wired, disabled, holds the sky material");
            Check(overlay != null && holder != null && overlay.GetComponent<Collider>() == null && holder.GetComponent<Collider>() == null, "no colliders on Overlay / SkyHolder");
            Check(Mathf.Approximately(night.onDarkness, KH_NightMode.DefaultOnDarkness) && Mathf.Approximately(night.fadeSeconds, KH_NightMode.DefaultFadeSeconds) && night.liveDarkness == 0f, "onDarkness / fadeSeconds == defaults, liveDarkness 0");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(night);
            Check(backing != null && backing.SyncMethod == Networking.SyncType.None, "backing Udon sync None");
            Check(backing != null && backing.enabled && night.enabled, "backing Udon and proxy enabled");
            var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset");
            Check(program != null && program.sourceCsScript != null && AssetDatabase.GetAssetPath(program.sourceCsScript) == ProgramPath + ".cs" && backing != null && backing.programSource == program, "program asset points at KH_NightMode.cs and the backing uses it");
            object v; var vars = backing == null ? null : backing.publicVariables;
            bool refs = vars != null && vars.TryGetVariableValue("overlayRenderer", out v) && v == (object)or && vars.TryGetVariableValue("skyHolder", out v) && v == (object)hr;
            Check(refs, "backing Udon holds overlayRenderer / skyHolder");
            bool values = vars != null && vars.TryGetVariableValue("onDarkness", out v) && v is float && Mathf.Approximately((float)v, KH_NightMode.DefaultOnDarkness)
                && vars.TryGetVariableValue("fadeSeconds", out v) && v is float && Mathf.Approximately((float)v, KH_NightMode.DefaultFadeSeconds)
                && vars.TryGetVariableValue("liveDarkness", out v) && v is float && (float)v == 0f;
            Check(values, "backing Udon holds onDarkness / fadeSeconds / liveDarkness == defaults");
            var others = UnityEngine.Object.FindObjectsOfType<KH_NightMode>(true);
            Check(others.Length == 1, "exactly one KH_NightMode in the scene (" + others.Length + ")");
            // Menu rebuild stage 0-b (D-5): the entry is the settings slider of KD_TreatPouch's settings page (KD_MenuSettings), not KH_HandMenu.
            var settings = UnityEngine.Object.FindObjectsOfType<KominkaDogRun.KD_MenuSettings>(true);
            Check(settings.Length == 1 && settings[0].hillsideNight == night && settings[0].nightMode != null && settings[0].nightModeValue != null,
                "KD_MenuSettings.hillsideNight wired with slider and value label (run Tools/KominkaHillside/Menu/7 Build Menu after Setup; found " + settings.Length + ")");
            Check(settings.Length == 1 && settings[0].pouch != null, "KD_MenuSettings.pouch wired (settings page of the hand menu)");
            // DES-LIV-001: the live's darkness reaches this night mode only through KH_LiveNight (Menu/26a). Absent = no live yet.
            var liveNights = UnityEngine.Object.FindObjectsOfType<KH_LiveNight>(true);
            if (liveNights.Length == 0) log.AppendLine("SKIP KH_LiveNight is not in the scene (no live set up yet: Tools/KominkaHillside/Menu/26a Setup Live)");
            else Check(liveNights.Length == 1 && liveNights[0].nightMode == night, "KH_LiveNight.nightMode is this KH_NightMode (found " + liveNights.Length + " KH_LiveNight)");
            var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            Check(descriptor != null && descriptor.NetworkIDCollection.Count(x => x.gameObject == null) == 0, "no dangling NetworkIDs");
            Report("night-verify", log + "TOTAL " + total + " FAILED " + failed);
        }
    }
}
