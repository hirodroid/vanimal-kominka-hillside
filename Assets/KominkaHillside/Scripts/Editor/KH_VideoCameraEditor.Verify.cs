using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KominkaDogRun;
using KominkaDogRun.Editor;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-008 Video/2 - Verify (read-only). Results: Temp/kh-video/verify.json and one VIDEO_VERIFY_PASS / _FAIL line.</summary>
    public static partial class KH_VideoCameraEditor
    {
        [Serializable] class VerifyReport { public string utc, status; public int pass, fail, warn; public List<string> lines = new List<string>(); }

        [MenuItem(Menu + "2 - Verify")]
        public static void Verify()
        {
            var r = new VerifyReport { utc = DateTime.UtcNow.ToString("o") };
            void Check(bool ok, string what) { if (ok) r.pass++; else r.fail++; r.lines.Add((ok ? "PASS " : "FAIL ") + what); }
            void Warn(string what) { r.warn++; r.lines.Add("WARN " + what); }
            try { VerifyInto(Check, Warn); }
            catch (Exception e) { Check(false, "exception: " + e.Message); }
            r.status = r.fail == 0 ? "PASS" : "FAIL";
            WriteJson(OutDir + "/verify.json", r);
            string head = "[KH Video] VIDEO_VERIFY_" + r.status + " pass " + r.pass + " fail " + r.fail + " warn " + r.warn + " (" + OutDir + "/verify.json)";
            if (r.fail == 0) Debug.Log(head); else Debug.LogError(head + " first: " + r.lines.First(l => l.StartsWith("FAIL")));
        }

        static void VerifyInto(Action<bool, string> Check, Action<string> Warn)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var p in Programs) Check(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(p + ".asset") != null, "program asset " + p + ".asset");
            var root = SceneRoot(scene, SystemName, out int n);
            Check(n == 1 && root != null, SystemName + " x" + n + " at the scene root");
            if (root == null) return;
            KD_SceneObjects.Find(SystemName, out int anywhere);
            Check(anywhere == 1, SystemName + " exists once in the scene (" + anywhere + ")");
            Check(root.activeSelf && root.layer == 0, "root active, layer 0");
            var all = UnityEngine.Object.FindObjectsOfType<KH_VideoCamera>(true);
            Check(all.Length == 1, "one KH_VideoCamera in the scene (" + all.Length + ")");
            var v = root.GetComponent<KH_VideoCamera>();
            Check(v != null, "KH_VideoCamera on the root");
            if (v == null) return;
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(v);
            Check(backing != null && backing.SyncMethod == Networking.SyncType.None, "sync None (the backing UdonBehaviour)");
            Check(v.enabled && (backing == null || backing.enabled), "behaviour enabled in the scene (Start runs, then disables itself)");

            // ---- camera ----
            var cam = v.cam;
            Check(cam != null && cam.transform.parent == root.transform && cam.name == CameraName, "camera " + CameraName + " wired");
            if (cam != null)
            {
                Check(!cam.enabled && cam.targetTexture == null, "camera disabled, no target texture in the scene");
                int want = KH_PhotoGalleryEditor.PhotoCullingMask & ~(1 << RecordSnapLayer) & ~(1 << MonitorLayer);
                Check(cam.cullingMask == want, "camera mask = the photo mask minus RecordSnap / monitor layers (" + KH_PhotoGalleryEditor.DescribeMask(cam.cullingMask) + ")");
                Check((cam.cullingMask & (1 << MonitorLayer)) == 0, "the camera does not draw the monitor (layer " + MonitorLayer + ")");
                Check((cam.cullingMask & (1 << 18)) != 0 && (cam.cullingMask & (1 << 10)) == 0, "own avatar via MirrorReflection 18, not PlayerLocal 10");
                Check(!cam.allowHDR && !cam.allowMSAA && cam.stereoTargetEye == StereoTargetEyeMask.None, "HDR / MSAA off, no stereo");
                Check(Mathf.Approximately(cam.nearClipPlane, CamNear) && Mathf.Approximately(cam.farClipPlane, CamFar), "near " + cam.nearClipPlane + " far " + cam.farClipPlane);
                Check(cam.GetComponent<AudioListener>() == null, "no AudioListener on the video camera");
                var hooks = cam.GetComponents<KH_VideoCameraHook>();
                Check(hooks.Length == 1 && hooks[0].rig == v && hooks[0].enabled, "KH_VideoCameraHook once on the camera object, wired to the rig, enabled (OnPostRender is sent to scripts on the camera's GameObject)");
                var hb = hooks.Length == 1 ? UdonSharpEditorUtility.GetBackingUdonBehaviour(hooks[0]) : null;
                Check(hb != null && hb.SyncMethod == Networking.SyncType.None && hb.enabled, "hook backing: sync None, enabled");
                Check(UnityEngine.Object.FindObjectsOfType<KH_VideoCameraHook>(true).Length == 1, "one KH_VideoCameraHook in the scene");
            }
            // ---- RecordSnap: the layer / mask pair (Codex review: not a fixed "layer 0") ----
            var snap = v.snapRenderer;
            Check(snap != null && snap.name == SnapName && snap.transform.parent == root.transform, "RecordSnap wired");
            if (snap != null)
            {
                Check(!snap.gameObject.activeSelf, "RecordSnap hidden in the scene");
                Check(snap.gameObject.layer == RecordSnapLayer, "RecordSnap layer " + snap.gameObject.layer + " = RecordSnapLayer " + RecordSnapLayer + " (" + LayerMask.LayerToName(RecordSnapLayer) + ")");
                if (RecordSnapLayer != 0)
                {
                    Check(cam == null || (cam.cullingMask & (1 << RecordSnapLayer)) == 0, "the video camera does not draw the RecordSnap layer");
                    var pc = UnityEngine.Object.FindObjectsOfType<KD_PhotoCapture>(true).Select(p => p.photoCamera).Where(x => x != null).ToArray();
                    Check(pc.All(x => (x.cullingMask & (1 << RecordSnapLayer)) == 0), "the auto photo camera does not draw the RecordSnap layer (" + pc.Length + " cameras)");
                    int others = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).Count(x => x.gameObject.layer == RecordSnapLayer && x != snap);
                    if (others > 0) Warn(others + " other renderers on layer " + RecordSnapLayer + " (they vanish from the video and the auto photos)");
                }
                else Warn("RecordSnapLayer is 0 (Default): our own cameras draw it; only _VRChatCameraMode == 0 in their pass keeps it out (unverified)");
                var mf = snap.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                Check(mesh != null && AssetDatabase.GetAssetPath(mesh) == SnapMeshPath, "RecordSnap mesh " + SnapMeshPath);
                if (mesh != null)
                {
                    var vs = mesh.vertices;
                    Check(vs.Length == 4 && vs.All(p => Mathf.Abs(Mathf.Abs(p.x) - 0.5f) < 1e-4f && Mathf.Abs(Mathf.Abs(p.y) - 0.5f) < 1e-4f && Mathf.Abs(p.z) < 1e-4f), "RecordSnap vertices are the ±0.5 quad (the shader contract)");
                    Check(mesh.bounds.size.x >= 2000f && mesh.bounds.size.y >= 2000f && mesh.bounds.size.z >= 2000f, "RecordSnap bounds " + mesh.bounds.size + " (never culled)");
                }
                Check(snap.transform.lossyScale == Vector3.one && snap.transform.position == Vector3.zero, "RecordSnap at the origin, scale 1 (the bounds cover the world)");
                Check(!snap.allowOcclusionWhenDynamic && snap.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off, "RecordSnap: dynamic occlusion off, no shadows");
                Check(snap.GetComponent<Collider>() == null, "RecordSnap has no collider");
                var m = snap.sharedMaterial;
                Check(m != null && AssetDatabase.GetAssetPath(m) == SnapMatPath && m.shader != null && m.shader.name == PrintShaderName, "RecordSnap material " + SnapMatPath + " with " + PrintShaderName);
                if (m != null)
                {
                    Check(m.GetFloat("_AttachDistance") >= 10000f && m.GetFloat("_AttachMargin") < 0f && m.GetFloat("_HideFromEyes") > 0.5f && m.GetFloat("_DebugProbe") < 0.5f,
                        "RecordSnap material: attach " + m.GetFloat("_AttachDistance") + ", aim off, hidden from eyes, debug probe off");
                    Check(m.renderQueue == SnapRenderQueue && m.mainTexture == null, "RecordSnap renderQueue " + m.renderQueue + " (above viewer 4000/4001, frames 4100/4101, night 4999, fade 5000), no texture in the asset");
                }
            }
            // ---- monitor ----
            Check(v.monitorRoot != null && v.monitorRoot.name == MonitorName && !v.monitorRoot.activeSelf, "monitor wired and hidden");
            if (v.monitorRoot != null)
            {
                var wrong = v.monitorRoot.GetComponentsInChildren<Transform>(true).Where(t => t.gameObject.layer != MonitorLayer).Select(t => t.name).ToArray();
                Check(wrong.Length == 0, "every monitor object on layer " + MonitorLayer + (wrong.Length > 0 ? " (wrong: " + string.Join(",", wrong.Take(5)) + ")" : ""));
                var panel = v.monitorRoot.transform.Find(PanelName);
                var canvas = panel != null ? panel.GetComponent<Canvas>() : null;
                Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace && panel.GetComponent<GraphicRaycaster>() != null && panel.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null,
                    "panel: world-space canvas + GraphicRaycaster + VRCUiShape");
                var box = panel != null ? panel.GetComponent<BoxCollider>() : null;
                Check(box != null && box.isTrigger, "panel collider is a trigger (never blocks a player)");
                Check(v.monitorRoot.GetComponentsInChildren<Collider>(true).All(x => x.isTrigger), "no solid collider on the monitor");
                var back = v.monitorRoot.transform.Find(BackName);
                var backR = back != null ? back.GetComponent<MeshRenderer>() : null;
                Check(back != null && backR != null && backR.sharedMaterial != null && backR.sharedMaterial.shader != null && backR.sharedMaterial.shader.name == BackShaderName,
                    "back plate " + BackName + " (" + BackShaderName + ") behind the panel");
                if (back != null)
                {
                    Check(back.localPosition.z > 0.001f && Vector3.Dot(back.forward, v.monitorRoot.transform.forward) < -0.99f, "back plate sits behind the canvas and faces away from the viewer (visible from behind only)");
                    Check(back.localScale.x >= CanvasPx.x * Px && back.localScale.y >= CanvasPx.y * Px, "back plate " + back.localScale.x + " x " + back.localScale.y + " m covers the canvas " + CanvasPx.x * Px + " x " + CanvasPx.y * Px + " m");
                    Check(back.GetComponent<Collider>() == null, "back plate has no collider");
                }
            }
            Check(v.screenRenderer != null && v.screenRenderer.sharedMaterial != null && AssetDatabase.GetAssetPath(v.screenRenderer.sharedMaterial) == ScreenMatPath
                && v.screenRenderer.sharedMaterial.shader.name == ScreenShaderName && v.screenRenderer.sharedMaterial.mainTexture == null, "screen: " + ScreenMatPath + " (Unlit/Texture, no texture in the asset)");
            // ---- buttons ----
            var vb = backing;
            void Wired(GameObject b, string evt, string what)
            {
                var btn = b != null ? b.GetComponent<Button>() : null;
                if (btn == null) { Check(false, what + ": no Button"); return; }
                int count = btn.onClick.GetPersistentEventCount();
                var target = count > 0 ? btn.onClick.GetPersistentTarget(0) : null;
                string method = count > 0 ? btn.onClick.GetPersistentMethodName(0) : "";
                // the string argument is not public API: read the serialized persistent call
                var so = new SerializedObject(btn);
                var arg = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_StringArgument");
                var mode = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Mode");
                string sent = arg != null ? arg.stringValue : "(none)";
                bool stringMode = mode != null && mode.intValue == (int)UnityEngine.Events.PersistentListenerMode.String;
                Check(count == 1 && target == vb && method == "SendCustomEvent" && stringMode && sent == evt,
                    what + " -> " + evt + " (calls " + count + ", target " + (target == vb ? "the rig's UdonBehaviour" : target != null ? target.name : "null") + ", method " + method + ", argument '" + sent + "')");
            }
            Transform P(string path) { return v.monitorRoot != null ? v.monitorRoot.transform.Find(PanelName + "/" + path) : null; }
            GameObject G(Transform t) { return t != null ? t.gameObject : null; }
            Wired(G(P("Close")), "_Close", "panel ×"); Wired(G(P("ModeDog")), "_ModeDog", "panel 犬追従"); Wired(G(P("ModeSelf")), "_ModeSelf", "panel じぶん");
            Wired(G(P("DogPicker/Prev")), "_DogPrev", "panel ◀"); Wired(G(P("DogPicker/Next")), "_DogNext", "panel ▶"); Wired(G(P("Snap")), "_ToggleSnap", "panel snap toggle");
            Check(v.stateLabel != null && v.snapLabel != null && v.dogLabel != null && v.modeDogImage != null && v.modeSelfImage != null && v.snapImage != null && v.dogPicker != null, "panel labels / images wired");
            // ---- entry ----
            var pouch = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true).FirstOrDefault();
            var page = pouch != null ? pouch.photoPage : null;
            Check(page != null, "photo page present (Photo/1)");
            foreach (var (go, evt, nm) in new[] { (v.entryOpen, "_Open", EntryOpenName), (v.entryRecall, "_Recall", EntryRecallName), (v.entryClose, "_Close", EntryCloseName) })
            {
                Check(go != null && go.name == nm && page != null && go.transform.parent == page.transform && !go.activeSelf, nm + " on the photo page, hidden in the scene");
                Wired(go, evt, nm);
            }
            if (page != null && v.entryOpen != null)
            {
                var large = page.transform.Find("Large");
                Check(large == null || v.entryOpen.transform.GetSiblingIndex() < large.GetSiblingIndex(), "entry buttons sit below the enlarged-photo overlay");
            }
            // ---- dogs / refs / tuning ----
            Check(v.dogRoots != null && v.dogRoots.Length == 6 && v.dogHeads != null && v.dogHeads.Length == 6 && v.dogNameKeys != null && v.dogNameKeys.Length == 6, "6 dogs wired (roots, heads, name keys)");
            if (v.dogRoots != null && v.dogRoots.Length == 6)
                for (int i = 0; i < 6; i++) Check(v.dogRoots[i] != null && v.dogRoots[i].name == "KD_Dog_" + KH_PhotoGalleryEditor.Keys[i], "dog " + i + " = KD_Dog_" + KH_PhotoGalleryEditor.Keys[i]);
            Check(v.language != null, "language wired");
            if (v.manager == null) Warn("manager not wired"); if (v.fade == null) Warn("fade not wired");
            foreach (var kv in TuningMatches(v)) Check(kv.Value, "tuning " + kv.Key);
            float mib = RenderWidth * RenderHeight * (4f + 4f) / (1024f * 1024f);
            Check(mib <= 11f, "RT memory while open " + mib.ToString("F1") + " MiB (colour RGBA8 + depth D24S8; 0 when closed)");
            string glyphs = KD_ShopSetupEditor.MissingGlyphs(AllWords());
            Check(glyphs.Length == 0, "glyphs of the panel texts are in the font" + (glyphs.Length > 0 ? " (missing " + glyphs + ")" : ""));

            // ---- source rules (all partial files) ----
            var files = Directory.GetFiles("Assets/KominkaHillside/Scripts", "KH_VideoCamera*.cs").Where(f => !f.EndsWith(".meta")).ToArray();
            Check(files.Length >= 3, "partial files found: " + string.Join(", ", files.Select(Path.GetFileName)));
            // rendering (W1 手直し 3): normal camera phase. No manual Render() (it drew the local avatar before VRChat placed it, real PCVR client
            // 2026-10-04); the camera is enabled in exactly one place (EnableForTick) and disabled by the OnPostRender callback, close and the safety net
            string allSrc = string.Concat(files.Select(File.ReadAllText));
            Check(!Regex.IsMatch(allSrc, @"\.Render\(\)"), "no manual Camera.Render() call");
            Check(Regex.Matches(allSrc, @"cam\.enabled\s*=\s*true").Count == 1 && Regex.IsMatch(allSrc, @"void EnableForTick\(\)\s*\{\s*cam\.enabled\s*=\s*true"), "the camera is enabled in one place only (EnableForTick)");
            Check(Regex.IsMatch(allSrc, @"public void _OnCamRendered\(\)[\s\S]{0,120}cam\.enabled\s*=\s*false"), "_OnCamRendered disables the camera");
            string hookSrc = File.Exists(HookProgramPath + ".cs") ? File.ReadAllText(HookProgramPath + ".cs") : "";
            Check(Regex.IsMatch(hookSrc, @"void OnPostRender\(\)[\s\S]{0,80}_OnCamRendered\(\)") && !hookSrc.Contains("[UdonSynced]") && !hookSrc.Contains("SendCustomNetworkEvent"),
                "KH_VideoCameraHook: OnPostRender → rig._OnCamRendered, no sync, no network");
            foreach (var f in files)
            {
                string src = File.ReadAllText(f), name = Path.GetFileName(f);
                Check(!src.Contains("[UdonSynced]") && !src.Contains("NetworkCallable") && !src.Contains("SendCustomNetworkEvent") && !src.Contains("RequestSerialization"), name + ": no synced variable / network entry (W1)");
                Check(!src.Contains("GetPlayerById") && !src.Contains("GetPlayers("), name + ": never looks up another player (W4 adds TryPersonPose in Subjects.cs only)");
                bool readsPlayer = Regex.IsMatch(src, @"\.(GetTrackingData|GetPosition|GetRotation|GetBonePosition)\(");
                if (!name.EndsWith(".Subjects.cs")) Check(!readsPlayer, name + ": reads no player pose (only KH_VideoCamera.Subjects.cs does)");
                foreach (Match mm in Regex.Matches(src, @"public\s+(?:override\s+)?void\s+(\w+)\s*\("))
                {
                    string m = mm.Groups[1].Value;
                    Check(m.StartsWith("_") || m == "PostLateUpdate", name + ": public method " + m + " is local-only (_ prefix)");
                }
            }
        }
    }
}
