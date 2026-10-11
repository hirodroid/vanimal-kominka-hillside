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
            _w5Catalog = null;   // W5: read UITranslations.json afresh (Ja / VerifyLanguage)
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
                    var others = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).Where(x => x.gameObject.layer == RecordSnapLayer && x != snap).ToArray();
                    // the photo menu's snap quad (KH_PhotoGalleryEditor.MenuSnapName) moved to the same layer on 2026-10-04 on purpose (the layer the
                    // VRChat camera is proven to draw): hidden from our own cameras is what it needs too
                    var expected = others.Where(x => x.name == KH_PhotoGalleryEditor.MenuSnapName).ToArray();
                    var unexpected = others.Except(expected).ToArray();
                    if (expected.Length > 0) Check(true, "layer " + RecordSnapLayer + " also holds the photo menu snap (" + KH_PhotoGalleryEditor.MenuSnapName + ", expected: hidden from our cameras)");
                    if (unexpected.Length > 0) Warn(unexpected.Length + " other renderers on layer " + RecordSnapLayer + " (they vanish from the video and the auto photos): " + string.Join(", ", unexpected.Take(5).Select(x => x.name)));
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
            Wired(G(P("Close")), "_Close", "panel ×");
            Wired(G(P(HideMonitorName)), HideMonitorEvent, "panel [モニターを隠す] (DES-WLD-004 M1.1; run Video/1)");
            // W2/W3: every mode button sends its own _ModeN, shows its label and is at least 120 × 50 px (a VR laser / a Desktop click)
            for (int m = 0; m < KH_VideoCamera.ModeCount; m++)
            {
                var mb = P(ModeButtonName(m));
                Wired(G(mb), ModeEvent(m), "panel mode " + m + " " + KH_VideoCamera.ModeWord(m));
                var lbl = mb != null ? LabelOf(mb.gameObject) : null;
                var rt = mb as RectTransform;
                Check(lbl != null && lbl.text == Ja(KH_VideoCamera.ModeKey(m)) && rt != null && rt.sizeDelta.x >= 120f && rt.sizeDelta.y >= 50f, "mode " + m + " label '" + (lbl != null ? lbl.text : "") + "', size " + (rt != null ? rt.sizeDelta.ToString() : "-"));
                Check(v.modeImages != null && v.modeImages.Length == KH_VideoCamera.ModeCount && mb != null && v.modeImages[m] == mb.GetComponent<Image>(), "modeImages[" + m + "] is that button");
            }
            Check(P("ModeDog") == null && P("ModeSelf") == null, "the W1 buttons ModeDog / ModeSelf are gone");
            Wired(G(P("DogPicker/Prev")), "_DogPrev", "panel ◀"); Wired(G(P("DogPicker/Next")), "_DogNext", "panel ▶"); Wired(G(P("Snap")), "_ToggleSnap", "panel snap toggle");
            Check(v.stateLabel != null && v.snapLabel != null && v.dogLabel != null && v.snapImage != null && v.dogPicker != null, "panel labels / images wired");
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
            // the backing heap (what runs) agrees with the proxy for the W2/W3 rows that it already holds (an empty heap before the first save is not a failure; vrc-udonsharp §4)
            int heapMissing = 0;
            foreach (var kv in ModeTuning())
            {
                if (backing == null || !backing.publicVariables.TryGetVariableValue(kv.Key, out object hv)) { heapMissing++; continue; }
                Check(hv is float f && Mathf.Abs(f - kv.Value) < 1e-4f, "backing " + kv.Key + " " + hv + " (want " + kv.Value + ")");
            }
            if (heapMissing > 0) Warn(heapMissing + " W2/W3 tuning rows not in the backing heap yet (saved / built scenes carry them; rerun Video/2 after a reload)");
            // ---- W2 / W3 references and data ----
            int nd = KH_PhotoGalleryEditor.Keys.Length;
            Check(v.catchers != null && v.catchers.Length == nd && v.discs != null && v.discs.Length == nd && v.fetches != null && v.fetches.Length == nd
                && v.wanders != null && v.wanders.Length == nd, "per-dog arrays (catchers, discs, fetches, wanders) have " + nd + " entries");
            // every array is guarded before indexing (a short / null array is a FAIL line, never an exception)
            T At<T>(T[] a, int i) where T : class { return a != null && i >= 0 && i < a.Length ? a[i] : null; }
            for (int i = 0; i < nd; i++)
            {
                var cat = At(v.catchers, i); var disc = At(v.discs, i); var fe = At(v.fetches, i); var wa = At(v.wanders, i);
                Check(cat != null && cat.dogIndex == i && disc != null && disc == cat.frisbee, "dog " + i + ": catcher dogIndex " + i + " and its disc");
                Check(fe != null && wa != null, "dog " + i + ": fetch / wander wired");
            }
            var calls = WashCalls();
            Check(v.washCalls != null && v.washCalls.Length == calls.Length && calls.Select((x, k) => At(v.washCalls, k) == x).All(b => b), "washCalls = the " + calls.Length + " wash stations in hierarchy order");
            int ns = v.spotPositions != null ? v.spotPositions.Length : 0;
            Check(ns > 0 && v.spotNames != null && v.spotNames.Length == ns && v.spotLooks != null && v.spotLooks.Length == ns && v.spotFovs != null && v.spotFovs.Length == ns
                && v.spotStations != null && v.spotStations.Length == ns, ns + " spots with names / looks / FOVs / stations of the same length");
            for (int k = 0; k < ns && v.spotLooks != null && v.spotLooks.Length == ns && v.spotPositions != null; k++)
            {
                bool ok = PointUsable(v.spotPositions[k], v.spotLooks[k], out string why);
                Check(ok, "spot " + (v.spotNames != null && k < v.spotNames.Length ? v.spotNames[k] : k.ToString()) + " at " + v.spotPositions[k].ToString("F1") + ": above ground, sees its look point" + (ok ? "" : " (" + why + ")"));
                int st = v.spotStations != null && k < v.spotStations.Length ? v.spotStations[k] : -2;
                Check(st == -1 || (v.washCalls != null && st >= 0 && st < v.washCalls.Length), "spot " + k + " station index " + st + " is the river (-1) or a wash station");
            }
            if (calls.Length > 0) Check(v.spotStations != null && v.spotStations.Any(x => x >= 0), "at least one wash-station spot");
            if (v.waterSpot != null) Check(v.spotStations != null && v.spotStations.Any(x => x == -1), "at least one river spot");
            else Warn("no KH_WaterSpot: no river spot");
            int np = v.passPositions != null ? v.passPositions.Length : 0;
            if (np == 0) Warn("no pass-by points (通り過ぎ falls back to the じぶん framing)");
            for (int k = 0; k < np; k++)
            {
                bool ok = !Physics.CheckSphere(v.passPositions[k], 0.25f, KH_PhotoGalleryEditor.CameraBlockMask, QueryTriggerInteraction.Ignore)
                    && Physics.Raycast(v.passPositions[k], Vector3.down, SpotGroundMax, KH_PhotoGalleryEditor.CameraBlockMask, QueryTriggerInteraction.Ignore);
                Check(ok, "pass point " + k + " at " + v.passPositions[k].ToString("F1") + " is free and above ground");
            }
            VerifyModes16(v, scene, Check, Warn);
            VerifyConvoy(v, Check, Warn);   // DES-WLD-004 M2 隊列
            VerifyPatterns(v, Check, Warn);   // §17 (KH_VideoCameraEditor.Patterns.cs)
            VerifyLanguage(v, Check, Warn);   // W5 13 languages (KH_VideoCameraEditor.Lang.cs)
            float mib = RenderWidth * RenderHeight * (4f + 4f) / (1024f * 1024f);
            Check(mib <= 11f, "RT memory while open " + mib.ToString("F1") + " MiB (colour RGBA8 + depth D24S8; 0 when closed)");
            string glyphs = KD_ShopSetupEditor.MissingGlyphs(AllWords() + BundledW5Text());
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
                Check(!src.Contains("[UdonSynced]") && !src.Contains("NetworkCallable") && !src.Contains("SendCustomNetworkEvent") && !src.Contains("RequestSerialization"), name + ": no synced variable / network entry (the permission's network lives in KH_TravelMailbox)");
                // W4: players are looked up only by Subjects.cs (TryPersonPose) and People.cs (the picker list: names / ids, never a pose)
                bool lookup = src.Contains("GetPlayerById") || src.Contains("GetPlayers(") || src.Contains("GetPlayerObjects(");
                if (!name.EndsWith(".Subjects.cs") && !name.EndsWith(".People.cs")) Check(!lookup, name + ": never looks up another player (only Subjects.cs / People.cs)");
                if (name.EndsWith(".Subjects.cs")) Check(!src.Contains("GetPlayers(") && !src.Contains("GetPlayerObjects("), name + ": no player list / player objects (TryPersonPose takes one id; my mailbox is found in People.cs)");
                bool readsPlayer = Regex.IsMatch(src, @"\.(GetTrackingData|GetPosition|GetRotation|GetBonePosition)\(");
                if (!name.EndsWith(".Subjects.cs")) Check(!readsPlayer, name + ": reads no player pose (only KH_VideoCamera.Subjects.cs does)");
                else CheckPersonGate(src, name, Check);
                foreach (Match mm in Regex.Matches(src, @"public\s+(?:override\s+)?void\s+(\w+)\s*\("))
                {
                    string m = mm.Groups[1].Value;
                    Check(m.StartsWith("_") || m == "PostLateUpdate", name + ": public method " + m + " is local-only (_ prefix)");
                }
            }
            // W4 (design §13.3): the person framing goes through TryPersonPose, and the camera re-checks the session right before it is enabled
            Check(Regex.IsMatch(allSrc, @"bool PersonFollow\(float dt\)\s*\{\s*if \(!TryPersonPose\(_person\)\)"), "PersonFollow reads the person only through TryPersonPose");
            Check(Regex.IsMatch(allSrc, @"_personLive && !PersonStillUsable\(\)\) \{ PersonDrop\(\); return; \}\s*EnableForTick\(\);"), "the permission is re-checked right before EnableForTick (§13.3)");
            Check(Regex.IsMatch(allSrc, @"bool PersonStillUsable\(\)[\s\S]{0,160}_FilmGate\(_person\)"), "the re-check uses the mailbox's film gate");
        }

        /// <summary>§16 (IMP-WLD-012): the 10 modes after 手元 / 見下ろす went out and 背中ごし / みち came in, the pickers of みち and 一周, the controls each
        /// mode takes, the route of みち, and nothing of the removed two left (constants, labels, tuning fields, pose functions, scene arrays).</summary>
        static void VerifyModes16(KH_VideoCamera v, UnityEngine.SceneManagement.Scene scene, Action<bool, string> Check, Action<string> Warn)
        {
            string[] want = { "犬追従", "じぶん", "背中ごし", "犬目線", "定点", "ディスク", "キャッチ待ち", "一周", "通り過ぎ", "みち", "隊列" };   // DES-WLD-004 M2: + 隊列
            var words = Enumerable.Range(0, KH_VideoCamera.ModeCount).Select(KH_VideoCamera.ModeWord).ToArray();
            Check(words.SequenceEqual(want), "§16 / M2 the " + want.Length + " modes in panel order: " + string.Join(" / ", words));
            Check(!words.Contains("手元") && !words.Contains("見下ろす"), "§16 手元 / 見下ろす are not modes any more");
            var moves = Enumerable.Range(0, KH_VideoCamera.PathMoveCount).Select(KH_VideoCamera.PathMoveWord).ToArray();
            Check(moves.SequenceEqual(new[] { "待って寄る", "後ずさり", "横ならび", "追いこし", "寄って引く" }), "§16 みち moves: " + string.Join(" / ", moves));
            var kinds = Enumerable.Range(0, KH_VideoCamera.OrbitKindCount).Select(KH_VideoCamera.OrbitKindWord).ToArray();
            Check(kinds.SequenceEqual(new[] { "一定", "近づく", "離れる" }), "§16 一周 ways: " + string.Join(" / ", kinds));
            Check(KH_VideoCamera.PickerKind(KH_VideoCamera.ModePath) == 3 && KH_VideoCamera.PickerKind(KH_VideoCamera.ModeOrbit) == 4 && KH_VideoCamera.PickerKind(KH_VideoCamera.ModeTps) == 0,
                "§16 pickers: みち = the move (3), 一周 = the way (4), 背中ごし none");
            Check(KH_VideoCamera.CtlKind(KH_VideoCamera.ModeTps) == KH_VideoCamera.CtlKindAll && KH_VideoCamera.CtlKind(KH_VideoCamera.ModePath) == KH_VideoCamera.CtlKindDistUp
                && KH_VideoCamera.CtlKind(KH_VideoCamera.ModeOrbit) == KH_VideoCamera.CtlKindDistUp && KH_VideoCamera.CtlKind(KH_VideoCamera.ModeSpot) == KH_VideoCamera.CtlKindNone
                && KH_VideoCamera.CtlKind(KH_VideoCamera.ModePassBy) == KH_VideoCamera.CtlKindNone, "§16 controls: 背中ごし all, みち / 一周 their own distance and height, 定点 / 通り過ぎ none");
            Check(KH_VideoCamera.ShotMoveDone == 5 && KH_VideoCamera.ShotOrbitEnd == 6, "§16 shot events ShotMoveDone 5 / ShotOrbitEnd 6");
            // nothing of 手元 / 見下ろす is left in the behaviour (fields) or the sources (constants, labels, pose functions)
            string[] goneFields = { "droneFov", "droneBack", "droneHeight", "droneAhead", "droneTau", "droneGroundMin", "washFov", "washDist", "washUp", "washLookTau", "washes" };
            var left = goneFields.Where(f => typeof(KH_VideoCamera).GetField(f) != null).ToArray();
            Check(left.Length == 0, "§16 the tuning / wiring fields of 手元 and 見下ろす are gone" + (left.Length > 0 ? " (left: " + string.Join(", ", left) + ")" : ""));
            var files = Directory.GetFiles("Assets/KominkaHillside/Scripts", "KH_VideoCamera*.cs").Where(f => !f.EndsWith(".meta")).ToArray();
            string src = string.Concat(files.Select(File.ReadAllText));
            string[] goneSrc = { "ModeWash", "ModeDrone", "WordModeWash", "WordModeDrone", "WordNoWash", "bool Wash(", "bool Drone(", "WashPos(", "DronePos(", "DroneLook(", "PartPoint(", "\"手元\"", "\"見下ろす\"" };
            var hits = goneSrc.Where(s => src.Contains(s)).ToArray();
            Check(hits.Length == 0, "§16 no source of 手元 / 見下ろす is left in KH_VideoCamera*.cs" + (hits.Length > 0 ? " (found: " + string.Join(", ", hits) + ")" : ""));
            Check(files.Any(f => f.EndsWith(".ModesPath.cs")), "§16 KH_VideoCamera.ModesPath.cs (背中ごし / みち) is there");
            // the TPS body facing is read in Subjects.cs only (the general "reads no player pose" rule above covers the other files)
            Check(Regex.IsMatch(src, @"_selfBody\s*=\s*new Vector3\(body\.x, 0f, body\.z\)"), "§16 背中ごし's body facing comes from SelfHead (lp.GetRotation, Subjects.cs)");
            // the route of みち: the leash manager's route (or the only closed loop)
            var pouch = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true).FirstOrDefault();
            var mgrRoute = pouch != null && pouch.manager != null ? pouch.manager.route : null;
            var loops = UnityEngine.Object.FindObjectsOfType<KH_WalkRoute>(true).Where(w => w.closedLoop).ToArray();
            var expect = mgrRoute != null ? mgrRoute : (loops.Length == 1 ? loops[0] : null);
            if (expect == null) Warn("§16 no walking route in the scene: みち uses the heading only");
            else Check(v.route == expect, "§16 みち route = " + expect.name + " (" + (mgrRoute != null ? "KH_LeashManager.route" : "the only closed loop") + "), wired " + (v.route != null ? v.route.name : "null"));
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(v);
            if (backing != null && backing.publicVariables.TryGetVariableValue("route", out object rv))
                Check(expect != null && (rv as UnityEngine.Object) == UdonSharpEditorUtility.GetBackingUdonBehaviour(expect), "§16 backing route is the route's UdonBehaviour");
            else Warn("§16 'route' not in the backing heap yet (a saved / built scene carries it; rerun Video/2 after a reload)");
        }

        /// <summary>DES-WLD-004 M2 隊列: the wiring to the cart manager (Cart/1 / Video/1), its picker and controls, not in any pattern, the switch
        /// and the fallback in the source, and the pose math on fixed numbers (no Play).</summary>
        static void VerifyConvoy(KH_VideoCamera v, Action<bool, string> Check, Action<string> Warn)
        {
            var field = typeof(KH_VideoCamera).GetField("cartManager");
            Check(field != null && field.FieldType == typeof(KH_CartManager), "M2 隊列: KH_VideoCamera.cartManager (KH_CartManager) exists");
            var mgrs = UnityEngine.Object.FindObjectsOfType<KH_CartManager>(true);
            if (mgrs.Length == 1)
            {
                if (v.cartManager == null) Warn("M2 隊列: cartManager not wired (run Cart/1 or Video/1); 隊列 films like 背中ごし until then");
                else Check(v.cartManager == mgrs[0], "M2 隊列: cartManager = the scene's KH_CartManager (" + mgrs[0].name + ")");
                var vb = UdonSharpEditorUtility.GetBackingUdonBehaviour(v);
                if (v.cartManager != null && vb != null && vb.publicVariables.TryGetVariableValue("cartManager", out object cv))
                    Check((cv as UnityEngine.Object) == UdonSharpEditorUtility.GetBackingUdonBehaviour(mgrs[0]), "M2 隊列: backing cartManager is the manager's UdonBehaviour");
                else if (v.cartManager != null) Warn("M2 隊列: 'cartManager' not in the backing heap yet (a saved / built scene carries it; rerun Video/2 after a reload)");
            }
            else if (mgrs.Length == 0) Warn("M2 隊列: no KH_CartManager in the scene (隊列 films like 背中ごし)");
            else Check(false, "M2 隊列: " + mgrs.Length + " KH_CartManager in the scene (exactly one expected)");
            Check(KH_VideoCamera.PickerKind(KH_VideoCamera.ModeConvoy) == 0 && KH_VideoCamera.CtlKind(KH_VideoCamera.ModeConvoy) == KH_VideoCamera.CtlKindAll,
                "M2 隊列: no picker, controls all (around the convoy's centre)");
            float[] t = KH_VideoCamera.CutTable();
            int cols = KH_VideoCamera.PatCols, rows = t != null ? t.Length / cols : 0, inTable = 0;
            for (int r = 0; r < rows; r++) if (Mathf.RoundToInt(t[r * cols + KH_VideoCamera.ColMode]) == KH_VideoCamera.ModeConvoy) inTable++;
            Check(inTable == 0, "M2 隊列 is in no pattern (" + inTable + " rows of the cut table)");
            string src = string.Concat(Directory.GetFiles("Assets/KominkaHillside/Scripts", "KH_VideoCamera*.cs").Where(f => !f.EndsWith(".meta")).Select(File.ReadAllText));
            Check(src.Contains("case ModeConvoy: return Convoy(dt);") && src.Contains("public void _Mode10() { SetMode(10); }"),
                "M2 隊列: the switch runs Convoy and the button event _Mode10 exists");
            Check(Regex.IsMatch(src, @"if \(!riding\) \{[^}\n]*return Tps\(dt\); \}"), "M2 隊列: while I do not ride it is 背中ごし (Tps)");
            // the pose math (the same static functions the runtime calls)
            Vector3 p = KH_VideoCamera.ConvoyPos(Vector3.zero, Vector3.forward, ConvoyBackDeg, ConvoyElevDeg, 10f);
            float elev = Mathf.Asin(Mathf.Clamp(p.y / 10f, -1f, 1f)) * Mathf.Rad2Deg, back = Mathf.Atan2(p.x, -p.z) * Mathf.Rad2Deg;
            Check(Mathf.Abs(p.magnitude - 10f) < 1e-3f && Mathf.Abs(elev - ConvoyElevDeg) < 0.01f && Mathf.Abs(back - ConvoyBackDeg) < 0.01f && p.z < 0f && p.x > 0f,
                "M2 隊列 pose: 10 m out, behind (z " + p.z.ToString("F2") + "), to the right (x " + p.x.ToString("F2") + "), back " + back.ToString("F1") + "°, up " + elev.ToString("F1") + "°");
            float s2 = KH_VideoCamera.ConvoySize(1.5f, 1.5f, ConvoyDogMargin, ConvoyCartSize), sDog = KH_VideoCamera.ConvoySize(1.5f, 4f, ConvoyDogMargin, ConvoyCartSize);
            float dSolo = KH_VideoCamera.ConvoyDistance(KH_VideoCamera.ConvoySize(0f, 0f, ConvoyDogMargin, ConvoyCartSize), ConvoyFill, ConvoyFov, ConvoyDistMin, ConvoyDistMax);
            float d2 = KH_VideoCamera.ConvoyDistance(s2, ConvoyFill, ConvoyFov, ConvoyDistMin, ConvoyDistMax);
            float d8 = KH_VideoCamera.ConvoyDistance(KH_VideoCamera.ConvoySize(10.5f, 10.5f, ConvoyDogMargin, ConvoyCartSize), ConvoyFill, ConvoyFov, ConvoyDistMin, ConvoyDistMax);
            float d0 = KH_VideoCamera.ConvoyDistance(0f, ConvoyFill, ConvoyFov, ConvoyDistMin, ConvoyDistMax), dNan = KH_VideoCamera.ConvoyDistance(float.NaN, ConvoyFill, ConvoyFov, ConvoyDistMin, ConvoyDistMax);
            float want2 = (s2 / ConvoyFill) * 0.5f / Mathf.Tan(ConvoyFov * 0.5f * Mathf.Deg2Rad);
            Check(Mathf.Abs(s2 - (3f + ConvoyDogMargin + ConvoyCartSize)) < 1e-4f && Mathf.Abs(sDog - (8f + ConvoyCartSize)) < 1e-4f && Mathf.Abs(d2 - want2) < 1e-3f
                && d0 == ConvoyDistMin && dNan == ConvoyDistMin && d8 == ConvoyDistMax && dSolo >= ConvoyDistMin && dSolo < d2 && d2 < d8,
                "M2 隊列 distance: 1 cart " + dSolo.ToString("F2") + " m, 2 carts 3 m apart " + d2.ToString("F2") + " m (size " + s2.ToString("F1") + "), 8 carts " + d8.ToString("F1") + " m (max), empty / NaN " + d0 + " m; a dog further out widens (" + sDog.ToString("F1") + ")");
        }

        /// <summary>Subjects.cs: a pose read on any player other than the local one (`lp.`) happens only inside TryPersonPose, after the mailbox's
        /// _FilmGate check (design §5.5: the single entry for another player).</summary>
        static void CheckPersonGate(string src, string name, Action<bool, string> Check)
        {
            var head = Regex.Match(src, @"bool TryPersonPose\(int pid\)\s*\{");
            Check(head.Success, name + ": TryPersonPose(int pid) exists");
            if (!head.Success) return;
            int bodyStart = head.Index + head.Length, depth = 1, bodyEnd = bodyStart;
            for (; bodyEnd < src.Length && depth > 0; bodyEnd++) { if (src[bodyEnd] == '{') depth++; else if (src[bodyEnd] == '}') depth--; }
            int gate = src.IndexOf("!box._FilmGate(pid)", bodyStart, Math.Max(0, bodyEnd - bodyStart), StringComparison.Ordinal);
            Check(gate > 0, name + ": TryPersonPose checks my mailbox's _FilmGate(pid)");
            int bad = 0;
            foreach (Match m in Regex.Matches(src, @"(\w+)\.(GetTrackingData|GetPosition|GetRotation|GetBonePosition)\("))
            {
                string who = m.Groups[1].Value;
                if (who == "lp") continue;   // the local player
                bool inside = m.Index > bodyStart && m.Index < bodyEnd && gate > 0 && m.Index > gate;
                if (!inside) { bad++; Check(false, name + ": pose read on '" + who + "' outside TryPersonPose's gated part (offset " + m.Index + ")"); }
            }
            if (bad == 0) Check(true, name + ": another player's pose is read only after TryPersonPose's gate");
        }
    }
}
