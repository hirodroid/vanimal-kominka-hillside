using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KominkaDogRun;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 §15.5 (2026-10-04 user): the camera controls and the camera drone.
    ///   Video/1 (already) writes the 10 mode buttons (2 × 5), the control row (Ctl0..8), the drone toggle and the control / drone tuning.
    ///   Video/6 - Build Camera Drone   the drone under the KH_TravelMailbox PlayerObject template: child "FilmDrone" (layer 17, MeshFilter = the
    ///                                  imported KH_VideoDrone.fbx mesh, MeshRenderer disabled, M_KH_VideoDrone.mat on KominkaHillside/Scenery Lite,
    ///                                  no shadows / collider) + KH_FilmDrone (Manual); wires KH_TravelMailbox.drone; assigns network IDs (the SDK's
    ///                                  ConfigureNetworkIDs, as Menu/19b); writes the camera's droneShowDefault. Needs Video/0 (KH_FilmDrone.asset) and Course/85.
    ///   Video/7 - Verify Controls and Drone (read-only) → Temp/kh-video/drone-verify.json and one VIDEO_DRONE_VERIFY_PASS / _FAIL line.
    /// </summary>
    public static partial class KH_VideoCameraEditor
    {
        public const string DroneName = "FilmDrone";
        public const string DroneFbxPath = "Assets/KominkaHillside/Models/VideoDrone/KH_VideoDrone.fbx";
        public const string DroneMatPath = MatDir + "/M_KH_VideoDrone.mat", DroneShaderName = "KominkaHillside/Scenery Lite";
        public const int DroneLayer = MonitorLayer;   // 17: the player sees it (the monitor is on it); the video camera and the auto photo camera do not draw it
        public const int DroneMaxTriangles = 700;
        public const float DroneRemoteTau = 0.15f;
        public const bool DroneShowDefault = true;

        // ================================================================ Video/6

        [MenuItem(Menu + "6 - Build Camera Drone")]
        public static void BuildDrone()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            string missing = ProgramMissing();
            if (missing != null) { Debug.LogError("[KH Video] VIDEO_DRONE_BUILD_FAIL " + missing + " missing: run Video/0, compile U#, then Video/1 and Video/6; nothing changed"); return; }
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, menu = "Video/6" };
            var scene = EditorSceneManager.GetActiveScene();
            var root = SceneRoot(scene, SystemName, out _);
            var video = root != null ? root.GetComponent<KH_VideoCamera>() : null;
            var boxes = UnityEngine.Object.FindObjectsOfType<KH_TravelMailbox>(true);
            if (video == null || boxes.Length != 1) { Debug.LogError("[KH Video] VIDEO_DRONE_BUILD_FAIL needs Video/1 (the camera) and exactly one KH_TravelMailbox template (Course/85); nothing changed"); return; }
            var mesh = AssetDatabase.LoadAllAssetsAtPath(DroneFbxPath).OfType<Mesh>().FirstOrDefault();
            if (mesh == null) { Debug.LogError("[KH Video] VIDEO_DRONE_BUILD_FAIL no mesh in " + DroneFbxPath + "; nothing changed"); return; }
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-drone.unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, true);
            try
            {
                var mat = EnsureMat(DroneMatPath, DroneShaderName, r.log);
                mat.mainTexture = null; mat.color = Color.white; mat.SetFloat("_VertexTint", 1f); mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                var box = boxes[0];
                var t = Child(box.transform, DroneName, DroneLayer);
                t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
                foreach (var col in t.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
                var mf = t.GetComponent<MeshFilter>(); if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = t.GetComponent<MeshRenderer>(); if (mr == null) mr = t.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off; mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                mr.enabled = false;   // shown only by the owner's camera / the subject's mailbox, at runtime
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                var d = t.GetComponent<KH_FilmDrone>();
                if (d == null) { d = t.gameObject.AddUdonSharpComponent<KH_FilmDrone>(); r.log.Add("added KH_FilmDrone on " + box.name + "/" + DroneName); }
                var db = UdonSharpEditorUtility.GetBackingUdonBehaviour(d);
                if (db == null) throw new InvalidOperationException("KH_FilmDrone backing missing (compile U# and run again)");
                if (db.SyncMethod != Networking.SyncType.Manual) { db.SyncMethod = Networking.SyncType.Manual; EditorUtility.SetDirty(db); }
                d.droneRenderer = mr; d.remoteTau = DroneRemoteTau; d.shown = false; d.forTarget = -1; d.forSeq = 0; d.camPos = Vector3.zero; d.camRot = Quaternion.identity;
                UdonSharpEditorUtility.CopyProxyToUdon(d); EditorUtility.SetDirty(d);
                box.drone = d;
                UdonSharpEditorUtility.CopyProxyToUdon(box); EditorUtility.SetDirty(box);
                video.droneShowDefault = DroneShowDefault;
                UdonSharpEditorUtility.CopyProxyToUdon(video); EditorUtility.SetDirty(video);
                r.log.Add("drone: " + box.name + "/" + DroneName + " layer " + DroneLayer + ", mesh " + mesh.name + " (" + mesh.triangles.Length / 3 + " tris), " + DroneMatPath + " (" + DroneShaderName + "), renderer off, sync Manual");
                // network IDs for the new synced child of the PlayerObject (the SDK does the same at build; ClientSim needs them at Play)
                var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
                if (descriptor == null) throw new InvalidOperationException("no VRC_SceneDescriptor");
                var res = VRC.SDKBase.Network.NetworkIDAssignment.ConfigureNetworkIDs(descriptor, out List<VRC.SDKBase.Network.NetworkIDAssignment.SetErrorLocation> errors, new VRC.SDKBase.Network.NetworkIDAssignment.SetError[0]);
                int added = 0; foreach (var pair in res.Item2) { added++; r.log.Add("network id " + pair.ID + " " + (pair.gameObject != null ? pair.gameObject.name : "(null)")); }
                if (errors != null && errors.Count > 0) throw new InvalidOperationException("ConfigureNetworkIDs: " + string.Join(" | ", errors.Select(x => x.ToString())));
                EditorUtility.SetDirty(descriptor);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("scene save failed");
                AssetDatabase.SaveAssets();
                r.status = "BUILT";
                r.log.Add("network ids added " + added);
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message);
                WriteJson(OutDir + "/drone-build.json", r);
                Debug.LogError("[KH Video] VIDEO_DRONE_BUILD_FAIL " + e.Message + ". The scene before this run is at " + r.backup + "; reopen the scene without saving to discard partial changes.");
                throw;
            }
            WriteJson(OutDir + "/drone-build.json", r);
            Debug.Log("[KH Video] VIDEO_DRONE_BUILD_OK (" + OutDir + "/drone-build.json). Next: Video/7, Video/2, Video/5.");
        }

        // ================================================================ Video/7

        [Serializable] class DroneVerifyReport { public string utc, status; public int pass, fail, warn; public List<string> lines = new List<string>(); }

        [MenuItem(Menu + "7 - Verify Controls and Drone")]
        public static void VerifyDrone()
        {
            var r = new DroneVerifyReport { utc = DateTime.UtcNow.ToString("o") };
            void Check(bool ok, string what) { if (ok) r.pass++; else r.fail++; r.lines.Add((ok ? "PASS " : "FAIL ") + what); }
            void Warn(string what) { r.warn++; r.lines.Add("WARN " + what); }
            try { VerifyDroneInto(Check, Warn); }
            catch (Exception e) { Check(false, "exception: " + e.Message); }
            r.status = r.fail == 0 ? "PASS" : "FAIL";
            WriteJson(OutDir + "/drone-verify.json", r);
            string head = "[KH Video] VIDEO_DRONE_VERIFY_" + r.status + " pass " + r.pass + " fail " + r.fail + " warn " + r.warn + " (" + OutDir + "/drone-verify.json)";
            if (r.fail == 0) Debug.Log(head); else Debug.LogError(head + " first: " + r.lines.First(l => l.StartsWith("FAIL")));
        }

        static Rect PxRect(Vector2 pos, Vector2 size) { return new Rect(pos - size / 2f, size); }

        static void VerifyDroneInto(Action<bool, string> Check, Action<string> Warn)
        {
            var scene = EditorSceneManager.GetActiveScene();
            var root = SceneRoot(scene, SystemName, out _);
            var v = root != null ? root.GetComponent<KH_VideoCamera>() : null;
            Check(v != null, "KH_VideoCamera (Video/1)");
            if (v == null) return;
            var vb = UdonSharpEditorUtility.GetBackingUdonBehaviour(v);
            // ---- 10 modes (§16: 手元 / 見下ろす out, 背中ごし / みち in; the list itself is checked by Video/2) ----
            Check(KH_VideoCamera.ModeCount == 10 && KH_VideoCamera.ModeWord(KH_VideoCamera.ModeTps) == "背中ごし" && KH_VideoCamera.ModeWord(KH_VideoCamera.ModePath) == "みち", "10 modes with 背中ごし and みち");
            var words = Enumerable.Range(0, KH_VideoCamera.ModeCount).Select(KH_VideoCamera.ModeWord).ToArray();
            Check(!words.Contains("おかえり") && !words.Contains("ふれあい") && !words.Contains("手元") && !words.Contains("見下ろす") && words.Distinct().Count() == words.Length && !words.Contains("?"),
                "おかえり / ふれあい / 手元 / 見下ろす are gone; every mode has its own label");
            var panel = v.monitorRoot != null ? v.monitorRoot.transform.Find(PanelName) : null;
            Check(panel != null, "monitor panel");
            if (panel == null) return;
            for (int m = KH_VideoCamera.ModeCount; m < 16; m++) Check(panel.Find(ModeButtonName(m)) == null, "no stale button " + ModeButtonName(m));
            Check(v.modeImages != null && v.modeImages.Length == KH_VideoCamera.ModeCount, "modeImages has " + KH_VideoCamera.ModeCount);
            // ---- the control row and the drone toggle ----
            string[] cw = KH_VideoCamera.CtlWords(), ckeys = KH_VideoCamera.CtlKeys();
            Check(cw.Length == CtlEvents.Length && v.ctlImages != null && v.ctlImages.Length == cw.Length, "control row: " + cw.Length + " buttons, ctlImages wired");
            for (int i = 0; i < cw.Length; i++)
            {
                var t = panel.Find(CtlName(i));
                CheckEvt(Check, t != null ? t.gameObject : null, vb, CtlEvents[i], "control " + cw[i]);
                var rt = t as RectTransform; var lbl = t != null ? LabelOf(t.gameObject) : null;
                Check(rt != null && rt.sizeDelta.x >= 80f && rt.sizeDelta.y >= 48f && lbl != null && lbl.text == (ckeys[i].Length > 0 ? Ja(ckeys[i]) : cw[i]) && v.ctlImages != null && i < v.ctlImages.Length && v.ctlImages[i] == t.GetComponent<Image>(),
                    "control " + i + " '" + cw[i] + "' ≥ 80 × 48 px, label, image wired");
            }
            var dt = panel.Find(DroneToggleName);
            CheckEvt(Check, dt != null ? dt.gameObject : null, vb, "_ToggleDrone", "drone toggle");
            Check(v.droneImage != null && v.droneLabel != null && dt != null && v.droneImage == dt.GetComponent<Image>(), "drone toggle image / label wired");
            // the lower panel rows do not overlap and stay inside the canvas
            var rects = new List<(string, Rect)>();
            for (int m = 0; m < KH_VideoCamera.ModeCount; m++) rects.Add(("mode" + m, PxRect(ModePos(m), ModeSize)));
            rects.Add(("picker", PxRect(PickerPos, PickerSize)));
            for (int i = 0; i < cw.Length; i++) rects.Add(("ctl" + i, PxRect(CtlPos(i), CtlSize)));
            rects.Add(("snap", PxRect(SnapPos, SnapSize))); rects.Add(("drone", PxRect(DroneTogglePos, DroneToggleSize)));
            rects.Add(("note", PxRect(NotePos, NoteSize))); rects.Add(("state", PxRect(StatePos, StateSize)));
            var canvas = PxRect(Vector2.zero, CanvasPx);
            var screenBottom = ScreenCenterPxY - ScreenM.y / Px / 2f;
            int overlaps = 0;
            for (int a = 0; a < rects.Count; a++)
            {
                var ra = rects[a].Item2;
                if (ra.xMin < canvas.xMin || ra.xMax > canvas.xMax || ra.yMin < canvas.yMin || ra.yMax > screenBottom) { overlaps++; Check(false, rects[a].Item1 + " outside the panel below the screen"); }
                for (int b = a + 1; b < rects.Count; b++) if (ra.Overlaps(rects[b].Item2)) { overlaps++; Check(false, rects[a].Item1 + " overlaps " + rects[b].Item1); }
            }
            if (overlaps == 0) Check(true, "the lower panel (modes, picker, controls, snap / drone, note, state) has no overlap and stays below the screen and inside the canvas");
            // tuning: Video/2's TuningMatches covers the ctl* / drone* rows (ModeTuning); here only the drone default
            Check(v.droneShowDefault == DroneShowDefault, "droneShowDefault " + v.droneShowDefault);
            // ---- the drone ----
            var boxes = UnityEngine.Object.FindObjectsOfType<KH_TravelMailbox>(true);
            Check(boxes.Length == 1, "one KH_TravelMailbox template");
            if (boxes.Length != 1) return;
            var box = boxes[0];
            var dtf = box.transform.Find(DroneName);
            var d = dtf != null ? dtf.GetComponent<KH_FilmDrone>() : null;
            Check(d != null && box.drone == d, "FilmDrone under the mailbox template, wired to KH_TravelMailbox.drone");
            var bb = UdonSharpEditorUtility.GetBackingUdonBehaviour(box);
            Check(bb != null && bb.publicVariables.TryGetVariableValue("drone", out object dv) && dv != null, "mailbox.drone in the backing heap");
            if (d == null) return;
            var db = UdonSharpEditorUtility.GetBackingUdonBehaviour(d);
            Check(db != null && db.SyncMethod == Networking.SyncType.Manual, "KH_FilmDrone sync Manual");
            Check(!d.shown && d.forTarget == -1 && d.forSeq == 0, "drone sync defaults: not shown, for nobody");
            Check(UnityEngine.Object.FindObjectsOfType<KH_FilmDrone>(true).Length == 1, "one KH_FilmDrone (the template's)");
            var mr = d.GetComponent<MeshRenderer>(); var mf = d.GetComponent<MeshFilter>();
            Check(mr != null && d.droneRenderer == mr && !mr.enabled, "renderer wired and off in the scene");
            Check(d.gameObject.layer == DroneLayer && v.cam != null && (v.cam.cullingMask & (1 << DroneLayer)) == 0, "drone layer " + DroneLayer + " is not drawn by the video camera");
            var pcs = UnityEngine.Object.FindObjectsOfType<KD_PhotoCapture>(true).Select(p => p.photoCamera).Where(x => x != null).ToArray();
            Check(pcs.All(x => (x.cullingMask & (1 << DroneLayer)) == 0), "nor by the auto photo camera");
            Check(mr != null && mr.shadowCastingMode == ShadowCastingMode.Off && !mr.receiveShadows && d.GetComponentsInChildren<Collider>(true).Length == 0, "no shadows, no collider");
            Check(mr != null && mr.sharedMaterial != null && AssetDatabase.GetAssetPath(mr.sharedMaterial) == DroneMatPath && mr.sharedMaterial.shader != null && mr.sharedMaterial.shader.name == DroneShaderName && mr.sharedMaterial.mainTexture == null,
                "material " + DroneMatPath + " (" + DroneShaderName + ", no texture, vertex colours)");
            Check(mf != null && mf.sharedMesh != null && AssetDatabase.GetAssetPath(mf.sharedMesh) == DroneFbxPath && mf.sharedMesh.triangles.Length / 3 <= DroneMaxTriangles,
                "mesh from " + DroneFbxPath + " (" + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.triangles.Length / 3 : 0) + " tris ≤ " + DroneMaxTriangles + ")");
            var desc = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            Check(desc != null && desc.NetworkIDCollection.Any(x => x.gameObject == d.gameObject), "FilmDrone has a network ID (Video/6 assigns it)");
            // ---- source rules ----
            string drone = File.ReadAllText(DroneProgramPath + ".cs");
            Check(!Regex.IsMatch(drone, @"void (Update|LateUpdate|FixedUpdate|PostLateUpdate)\("), "KH_FilmDrone has no per-frame method (nothing runs unless the owner's camera or a subject's mailbox calls it)");
            Check(!drone.Contains("NetworkCallable") && !drone.Contains("SendCustomNetworkEvent"), "KH_FilmDrone has no network entry");
            Check(Regex.Matches(drone, @"RequestSerialization\(\)").Count == 2 && Regex.Matches(drone, @"if \(!Networking\.IsOwner\(gameObject\)").Count == 2, "KH_FilmDrone serialises only in _Send / _StopSend, owner-guarded");
            foreach (Match mm in Regex.Matches(drone, @"public\s+(?:override\s+)?void\s+(\w+)\s*\(")) Check(mm.Groups[1].Value.StartsWith("_"), "KH_FilmDrone public " + mm.Groups[1].Value + " is local-only");
            string cam = File.ReadAllText(ProgramPath + ".cs"), modes = File.ReadAllText(ProgramPath + ".Modes.cs"), ctl = File.ReadAllText(ProgramPath + ".Controls.cs");
            Check(Regex.IsMatch(modes, @"bool ok = ModePose\(dt, now\);\s*if \(ok\) ApplyControls\(\);"), "the controls act on the mode's wanted pose before the shared occlusion / floor / push-out");
            Check(Regex.IsMatch(cam, @"EnableForTick\(\); _subjectPoses\+\+;[^\n]*\n\s*DroneTick\(now\);"), "the drone follows the camera on every render tick");
            Check(Regex.IsMatch(ctl, @"bool send = DroneMaySend\(box\);") && Regex.IsMatch(ctl, @"bool DroneMaySend\(KH_TravelMailbox box\)[\s\S]{0,200}FilmInUse && box\._FilmGate\(box\._FilmTarget\(\)\)"),
                "the drone pose is sent only while the permission gate holds (in use AND the subject's table)");
            Check(Regex.IsMatch(cam, @"PersonDrop\(\);\s*DroneSendCheck\(\);[^\n]*\n\s*if \(now < _nextRender\) return;"), "the send is re-checked every frame before the render-tick wait and the fade return");
            Check(Regex.IsMatch(ctl, @"off \+= Vector3\.up \* up;[\s\S]{0,200}if \(len < ctlMinDist\)"), "the minimum distance applies to the final position (after the height)");
            Check(Regex.IsMatch(ctl, @"void ApplyControls\(\)[\s\S]{0,200}if \(CtlKind\(_mode\) != CtlKindAll"), "§16 the shared offset controls act only on the CtlKindAll modes (みち / 一周 use CtlDist / CtlUp themselves)");
            Check(Regex.IsMatch(ctl, @"m == ModeOrbit && which == CtlLeftIdx\) _orbitSpeed = Mathf\.Clamp\(") && Regex.IsMatch(ctl, @"m == ModeOrbit && which == CtlRightIdx\) _orbitSpeed = Mathf\.Clamp\("),
                "§16 in 一周 [←][→] change the speed (clamped)");
            Check(Regex.IsMatch(ctl, @"void DroneTick\(float now\)\s*\{\s*if \(!_droneShow\) return;"), "nothing is placed or sent while the drone is hidden");
            Check(!ctl.Contains("GetPlayerById") && !Regex.IsMatch(ctl, @"\.(GetTrackingData|GetPosition|GetRotation)\("), "the controls read no player (only the mode's pose, which went through TryPersonPose)");
        }
    }
}
