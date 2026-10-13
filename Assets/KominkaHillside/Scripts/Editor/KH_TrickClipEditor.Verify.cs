using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    // IMP-DOG-054 wave 1-A: 2b Verify (read-only) and 2c Render (evidence sheets).
    public static partial class KH_TrickClipEditor
    {
        [MenuItem("Tools/KominkaHillside/Tricks/2b Verify Trick Clips and States")]
        public static void VerifyClipsAndStates()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var log = new StringBuilder(); var failures = new List<string>(); int checks = 0, clips = 0;
            void Check(bool ok, string what) { checks++; if (!ok) failures.Add(what); log.AppendLine((ok ? "PASS " : "FAIL ") + what); }
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var def in Defs)
                {
                    string breed = def.key;
                    log.AppendLine("##### " + breed);
                    // ---- clips
                    AnimationClip sit = null;
                    try { sit = KD_CallPoseEditor.SitClip(def); } catch (Exception e) { Check(false, breed + ": KD_Sit " + e.Message); }
                    var sitBindings = sit == null ? new EditorCurveBinding[0] : AnimationUtility.GetCurveBindings(sit);
                    var trickClips = new AnimationClip[Tricks.Length];
                    for (int k = 0; k < Tricks.Length; k++)
                    {
                        string trick = Tricks[k], tag = breed + "/" + trick, path = ClipPathFor(trick, breed);
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                        trickClips[k] = clip;
                        Check(clip != null, tag + ": clip exists " + path);
                        if (clip == null) continue;
                        clips++;
                        Check(clip.name == "KH_" + trick + "_" + breed, tag + ": clip name " + clip.name);
                        Check(Mathf.Abs(clip.length - LengthOf(trick)) <= LengthTolerance, tag + ": length " + clip.length.ToString("F3") + " == " + LengthOf(trick).ToString("F1"));
                        Check(!clip.isLooping && !AnimationUtility.GetAnimationClipSettings(clip).loopTime, tag + ": not looping");
                        Check(!clip.legacy, tag + ": not legacy");
                        var bindings = AnimationUtility.GetCurveBindings(clip);
                        Check(!bindings.Any(b => b.path.Length == 0), tag + ": no DogRoot (path \"\") curves - the called dog stays on its placement point");
                        {
                            // Size: a constant property group (rotation / position of one transform, or one float) is stored as 2 keys.
                            int groups = 0, constantGroups = 0, bloated = 0, keyTotal = 0;
                            foreach (var g in bindings.GroupBy(b => b.path + "|" + b.type.Name + "|" + (b.type == typeof(Transform) ? b.propertyName.Split('.')[0] : b.propertyName)))
                            {
                                groups++;
                                var curves = g.Select(b => AnimationUtility.GetEditorCurve(clip, b)).Where(cv => cv != null).ToArray();
                                keyTotal += curves.Sum(cv => cv.length);
                                bool same = curves.All(cv => cv.keys.All(key => Mathf.Abs(key.value - cv.keys[0].value) <= ConstantCurveEpsilon));
                                if (!same) continue;
                                constantGroups++;
                                if (curves.Any(cv => cv.length > 2)) bloated++;
                            }
                            log.AppendLine("  " + tag + " curve groups=" + groups + " constant=" + constantGroups + " keys=" + keyTotal + " asset bytes=" + new FileInfo(path).Length);
                            Check(bloated == 0, tag + ": constant curve groups are 2 keys (" + bloated + " of " + constantGroups + " constant groups have more)");
                        }
                        if (sit != null)
                        {
                            var missing = sitBindings.Where(b => !bindings.Contains(b)).ToArray();
                            Check(missing.Length == 0, tag + ": every KD_Sit binding is animated (" + sitBindings.Length + ")" + (missing.Length > 0 ? " missing " + string.Join(", ", missing.Take(5).Select(b => b.path + "/" + b.propertyName)) : ""));
                            float worst0 = 0f, worst1 = 0f; string at0 = "-", at1 = "-";
                            foreach (var b in sitBindings)
                            {
                                var cv = AnimationUtility.GetEditorCurve(clip, b); if (cv == null) continue;
                                float target = AnimationUtility.GetEditorCurve(sit, b).Evaluate(0f);
                                float d0 = Mathf.Abs(cv.Evaluate(0f) - target), d1 = Mathf.Abs(cv.Evaluate(clip.length) - target);
                                if (d0 > worst0) { worst0 = d0; at0 = b.path.Substring(b.path.LastIndexOf('/') + 1) + "/" + b.propertyName; }
                                if (d1 > worst1) { worst1 = d1; at1 = b.path.Substring(b.path.LastIndexOf('/') + 1) + "/" + b.propertyName; }
                            }
                            Check(worst0 <= EndpointTolerance, tag + ": first frame == KD_Sit (worst " + worst0.ToString("G3") + " at " + at0 + ", tol " + EndpointTolerance + ")");
                            Check(worst1 <= EndpointTolerance, tag + ": last frame == KD_Sit (worst " + worst1.ToString("G3") + " at " + at1 + ", tol " + EndpointTolerance + ")");
                        }
                    }
                    // ---- ground (clip sampled on the scene dog's clone, constraints applied, body meshes baked)
                    if (trickClips.All(x => x != null))
                    {
                        try
                        {
                            using (var c = Prepare(def, scene))
                            {
                                for (int k = 0; k < Tricks.Length; k++)
                                {
                                    string trick = Tricks[k], tag = breed + "/" + trick;
                                    var a = Analyze(c, trickClips[k], trick);
                                    log.AppendLine("  " + tag + " " + Describe(a, trick));
                                    Check(a.minY >= -GroundTolerance, tag + ": nothing below the ground (lowest vertex " + a.minY.ToString("F4") + " at " + a.minYAt.ToString("F2") + " s >= -" + GroundTolerance + ")");
                                    Check(a.groundedMaxY <= Mathf.Max(SeatY, a.sitMinY) + GroundedMaxFloat, tag + ": grounded frames rest on the ground (highest lowest-vertex " + a.groundedMaxY.ToString("F4") + " at " + a.groundedMaxAt.ToString("F2") + " s <= max(SeatY, KD_Sit " + a.sitMinY.ToString("F4") + ") + " + GroundedMaxFloat + ")");
                                    if (IsBang(trick))
                                    {
                                        var side = DorsalAt(trickClips[k], c.dog, c.rig, BangSideAt); var up = DorsalAt(trickClips[k], c.dog, c.rig, BangBellyAt + 1f);
                                        log.AppendLine("  " + tag + " dorsal side=" + side.ToString("F2") + " bellyUp=" + up.ToString("F2"));
                                        Check(Vector3.Dot(side, c.right) > 0.5f, tag + ": falls to the dog's RIGHT (back toward +right at the side key, dorsal " + side.ToString("F2") + ")");
                                        Check(Vector3.Dot(up, Vector3.down) > 0.5f, tag + ": belly up during the hold (dorsal " + up.ToString("F2") + ")");
                                    }
                                    if (!IsBang(trick))
                                        Check(a.apexClearance >= JumpClearanceMin && a.apexClearance <= JumpClearanceMax, tag + ": apex clearance " + a.apexClearance.ToString("F3") + " in [" + JumpClearanceMin + ", " + JumpClearanceMax + "] (pelvis + tail lifted; pelvis rise over sit " + a.pelvisRise.ToString("F3") + ")");
                                }
                            }
                        }
                        catch (Exception e) { Check(false, breed + ": ground sampling threw " + e.GetType().Name + ": " + e.Message); }
                    }
                    // ---- controller
                    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
                    Check(ctrl != null, breed + ": controller " + def.ControllerPath);
                    if (ctrl == null) continue;
                    Check(ctrl.parameters.Any(p => p.name == "CallReady" && p.type == AnimatorControllerParameterType.Bool)
                        && ctrl.parameters.Any(p => p.name == "CallPose" && p.type == AnimatorControllerParameterType.Int)
                        && ctrl.parameters.Any(p => p.name == "CallTime" && p.type == AnimatorControllerParameterType.Float), breed + ": parameters CallReady (Bool) / CallPose (Int) / CallTime (Float)");
                    Check(KD_CallPoseEditor.AnimatorConfigured(ctrl), breed + ": the 7 existing call states are still configured (KD_CallPoseEditor.AnimatorConfigured)");
                    var sm = ctrl.layers[0].stateMachine;
                    for (int i = 0; i < TrickStates.Length; i++)
                    {
                        string name = TrickStates[i], tag = breed + "/" + name; int pose = TrickCallPoses[i];
                        var named = sm.states.Select(x => x.state).Where(x => x.name == name).ToArray();
                        Check(named.Length == 1, tag + ": exactly one state (" + named.Length + ")");
                        if (named.Length != 1) continue;
                        var st = named[0];
                        Check(st.motion != null && st.motion == trickClips[i], tag + ": plays " + ClipPathFor(Tricks[i], breed));
                        Check(st.timeParameterActive && st.timeParameter == "CallTime", tag + ": time parameter CallTime");
                        var entries = sm.anyStateTransitions.Where(t => t.destinationState == st).ToArray();
                        Check(entries.Length == 1, tag + ": exactly one AnyState entry (" + entries.Length + ")");
                        Check(entries.Length == 1 && EntryOk(entries[0], pose), tag + ": entry CallReady && CallPose == " + pose + ", no exit time, fixed " + EntryDuration + " s, no self transition");
                        Check(sm.anyStateTransitions.Count(t => t.destinationState != st && t.conditions.Any(c => c.parameter == "CallPose" && c.mode == AnimatorConditionMode.Equals && Mathf.Approximately(c.threshold, pose))) == 0,
                            tag + ": no other AnyState entry uses CallPose == " + pose);
                        string why;
                        Check(ExitsOk(sm, st, out why), tag + ": exits Run / Walk (!CallReady && move, 0 s) and default " + (sm.defaultState == null ? "-" : sm.defaultState.name) + " (!CallReady, " + ExitDefaultDuration + " s)" + (why == null ? "" : " - " + why));
                        Check(!sm.states.Any(x => x.state.transitions.Any(t => t.destinationState == st)), tag + ": no ordinary state transitions into it");
                    }
                    // ---- Animator playback in the preview scene (CallPose 8 / 9, CallTime 0..1)
                    try { VerifyPlayback(def, scene, trickClips, Check, log); }
                    catch (Exception e) { Check(false, breed + ": playback threw " + e.GetType().Name + ": " + e.Message); }
                }
                Check(clips == Defs.Length * Tricks.Length, "clips " + clips + " / " + Defs.Length * Tricks.Length);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            log.AppendLine();
            log.AppendLine("After Menu/6j (which may rebuild the 7 call states): run Tricks/2b; run Tricks/2 only if 2b reports NG for CallBang / CallJump, then 2b again.");
            string summary = failures.Count == 0 ? "TRICKS_CLIPS_OK " + checks : "TRICKS_CLIPS_NG " + failures.Count + " : " + string.Join(" | ", failures);
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/clips-verify.txt", summary + "\n" + log);
            if (failures.Count == 0) Debug.Log(summary); else Debug.LogError(summary);
        }

        // Drives the dog's own Animator like KD_CallPoseEditor.VerifyPlaybackInto: enter CallBang / CallJump, then scrub CallTime and
        // compare the pelvis with the clip sampled at CallTime x length, and record the lowest vertex / paw / trunk bone.
        static void VerifyPlayback(KD_DogDef def, Scene scene, AnimationClip[] trickClips, Action<bool, string> check, StringBuilder log)
        {
            if (trickClips.Any(x => x == null)) return;
            var dog = CloneSceneDog(def, scene); GameObject sample = null;
            try
            {
                var animator = dog.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.runtimeAnimatorController != null);
                check(animator != null, def.key + ": dog has an Animator with a controller");
                if (animator == null) return;
                animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
                var ctrl = animator.runtimeAnimatorController as AnimatorController;
                check(ctrl != null && AssetDatabase.GetAssetPath(ctrl) == def.ControllerPath, def.key + ": scene dog uses " + def.ControllerPath);
                bool HasParam(string n) { return animator.parameters.Any(p => p.name == n); }
                var rig = KH_DogRig.Resolve(dog, MouthAnchorName);
                sample = CloneSceneDog(def, scene);
                var sampleRig = KH_DogRig.Resolve(sample, MouthAnchorName);
                for (int i = 0; i < TrickStates.Length; i++)
                {
                    string tag = def.key + "/" + TrickStates[i];
                    if (HasParam("Sleep")) animator.SetBool("Sleep", false);
                    foreach (var move in MoveStates) if (HasParam(move)) animator.SetBool(move, false);
                    animator.SetBool("CallReady", true); animator.SetInteger("CallPose", 2); animator.SetFloat("CallTime", 0f);
                    for (int f = 0; f < 30; f++) animator.Update(.05f);
                    animator.SetInteger("CallPose", TrickCallPoses[i]);
                    for (int f = 0; f < 30; f++) animator.Update(.05f);
                    check(animator.GetCurrentAnimatorStateInfo(0).IsName(TrickStates[i]), tag + ": Animator enters " + TrickStates[i] + " on CallPose " + TrickCallPoses[i]);
                    float lowest = float.MaxValue, paws = float.MaxValue, trunk = float.MaxValue, drift = 0f;
                    for (int s = 0; s <= 20; s++)
                    {
                        float fraction = s / 20f;
                        animator.SetFloat("CallTime", fraction); animator.Update(.01f);
                        KH_DogRig.ApplyConstraints(dog);
                        lowest = Mathf.Min(lowest, KH_FrisbeeJumpClipEditor.BodyBounds(dog).min.y);
                        foreach (var n in new[] { "Bone.014_L", "Bone.014_R", "Bone.016_L", "Bone.016_R" }) { var b = rig.Bone(n); if (b != null) paws = Mathf.Min(paws, b.position.y); }
                        foreach (var n in new[] { "Bone", "Bone.001", "Bone.002", "Bone.003" }) { var b = rig.Bone(n); if (b != null) trunk = Mathf.Min(trunk, b.position.y); }
                        trickClips[i].SampleAnimation(sample, trickClips[i].length * fraction);
                        drift = Mathf.Max(drift, Vector3.Distance(rig.AnimationRoot.position, sampleRig.AnimationRoot.position));
                    }
                    log.AppendLine("  " + tag + " playback lowestVertex=" + lowest.ToString("F4") + " pawBoneMin=" + paws.ToString("F3") + " trunkBoneMin=" + trunk.ToString("F3") + " pelvisVsClip=" + drift.ToString("F4"));
                    check(drift < 0.005f, tag + ": CallTime drives the clip time (pelvis vs clip sample " + drift.ToString("F4") + " m)");
                    check(lowest >= -GroundTolerance, tag + ": playback lowest vertex " + lowest.ToString("F4") + " >= -" + GroundTolerance);
                    animator.SetInteger("CallPose", 2);
                    for (int f = 0; f < 30; f++) animator.Update(.05f);
                    check(animator.GetCurrentAnimatorStateInfo(0).IsName("CallSit"), tag + ": returns to CallSit on CallPose 2");
                }
            }
            finally { if (sample != null) Object.DestroyImmediate(sample); Object.DestroyImmediate(dog); }
        }

        // ---------------------------------------------------------------- 2c render
        static readonly float[] BangStages = { 0f, BangFlinchPeak, 0.60f, BangSideAt, 1.20f, BangBellyAt, 3.00f, BangRiseSideAt, BangSitAt, BangLength };
        static readonly float[] JumpStages = { 0f, JumpCrouchAt, JumpTakeoffAt, 0.62f, 0.82f, 1.02f, JumpTouchdownAt, JumpAbsorbAt, 1.60f, JumpLength };

        // One sheet per breed and trick: stages left to right, top row seen from behind (the dog's right = image right, so the
        // bang must fall to the right), bottom row from the dog's right side. Ground = the light slab at y = 0.
        [MenuItem("Tools/KominkaHillside/Tricks/2c Render Trick Stages")]
        public static void RenderStages()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            Directory.CreateDirectory(EvidenceDir);
            var log = new StringBuilder();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var def in Defs)
                    for (int k = 0; k < Tricks.Length; k++)
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPathFor(Tricks[k], def.key));
                        if (clip == null) throw new InvalidOperationException("Build the clips first (Tricks/1): " + ClipPathFor(Tricks[k], def.key));
                        RenderSheet(def, clip, IsBang(Tricks[k]) ? BangStages : JumpStages, EvidenceDir + "/" + def.key + "-" + Tricks[k] + ".png", scene, log);
                    }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            File.WriteAllText(EvidenceDir + "/render.txt", log.ToString());
            Debug.Log("[KH Tricks] rendered " + Defs.Length * Tricks.Length + " sheets to " + EvidenceDir + "\n" + log);
        }

        static void RenderSheet(KD_DogDef def, AnimationClip clip, float[] stages, string file, Scene scene, StringBuilder log)
        {
            const int W = 300, H = 260;
            var owned = new List<Object>();
            T Own<T>(T v) where T : Object { owned.Add(v); return v; }
            var previous = RenderTexture.active;
            try
            {
                var dog = Own(CloneSceneDog(def, scene));
                var meshes = new List<Mesh>[stages.Length]; bool first = true; var frame = new Bounds();
                var line = new StringBuilder(def.key + " " + clip.name + ":");
                for (int i = 0; i < stages.Length; i++)
                {
                    clip.SampleAnimation(dog, Mathf.Min(stages[i], clip.length)); KH_DogRig.ApplyConstraints(dog);
                    meshes[i] = new List<Mesh>();
                    foreach (var r in KH_FrisbeeJumpClipEditor.Bodies(dog))
                    {
                        var m = Own(new Mesh()); r.BakeMesh(m, true);
                        var v = m.vertices; var n = m.normals; var nm = r.transform.localToWorldMatrix.inverse.transpose;
                        for (int j = 0; j < v.Length; j++) v[j] = r.transform.TransformPoint(v[j]);
                        for (int j = 0; j < n.Length; j++) n[j] = nm.MultiplyVector(n[j]).normalized;
                        m.vertices = v; m.normals = n; m.RecalculateBounds();
                        meshes[i].Add(m);
                        if (first) { frame = m.bounds; first = false; } else frame.Encapsulate(m.bounds);
                    }
                    line.Append(" t" + stages[i].ToString("F2") + "=" + KH_FrisbeeJumpClipEditor.BodyBounds(dog).min.y.ToString("F3"));
                }
                log.AppendLine(line.ToString());
                dog.SetActive(false);
                frame.Encapsulate(new Vector3(frame.center.x, 0f, frame.center.z));
                var center = frame.center; float ortho = Mathf.Max(frame.size.x, frame.size.y, frame.size.z) * 0.6f;
                var mat = Own(new Material(Shader.Find("Standard"))); mat.color = new Color(.70f, .55f, .30f); mat.SetFloat("_Glossiness", .1f);
                var camGo = Own(new GameObject("Camera")); SceneManager.MoveGameObjectToScene(camGo, scene); var cam = camGo.AddComponent<Camera>(); cam.enabled = false; cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .20f, .22f); cam.orthographic = true; cam.useOcclusionCulling = false;
                cam.orthographicSize = ortho; cam.nearClipPlane = .01f; cam.farClipPlane = 20; cam.aspect = (float)W / H;
                var lightGo = Own(new GameObject("Light")); SceneManager.MoveGameObjectToScene(lightGo, scene); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; lightGo.transform.rotation = Quaternion.Euler(40, -30, 0);
                var slab = Own(GameObject.CreatePrimitive(PrimitiveType.Cube)); SceneManager.MoveGameObjectToScene(slab, scene); slab.transform.position = new Vector3(0, -.006f, 0); slab.transform.localScale = new Vector3(3f, .012f, 3f);
                var slabMat = Own(new Material(Shader.Find("Standard"))); slabMat.color = new Color(.85f, .85f, .8f); slab.GetComponent<Renderer>().sharedMaterial = slabMat;
                var rt = Own(new RenderTexture(W, H, 24));
                var sheet = Own(new Texture2D(W * stages.Length, H * 2, TextureFormat.RGB24, false));
                var tile = Own(new Texture2D(W, H, TextureFormat.RGB24, false));
                for (int i = 0; i < stages.Length; i++)
                {
                    var display = new GameObject("Stage"); SceneManager.MoveGameObjectToScene(display, scene);
                    try
                    {
                        foreach (var m in meshes[i])
                        {
                            var obj = new GameObject("Body"); obj.transform.SetParent(display.transform, false);
                            obj.AddComponent<MeshFilter>().sharedMesh = m; obj.AddComponent<MeshRenderer>().sharedMaterials = Enumerable.Repeat(mat, m.subMeshCount).ToArray();
                        }
                        // Row 1 (top): from behind (camera on -Z looking +Z). Row 0 (bottom): from the dog's right (+X).
                        for (int row = 0; row < 2; row++)
                        {
                            var offset = row == 1 ? new Vector3(0, .6f, -3f) : new Vector3(3f, .3f, 0);
                            cam.transform.position = center + offset; cam.transform.LookAt(center);
                            cam.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1, 1, -1)) * cam.transform.worldToLocalMatrix;
                            float hh = cam.orthographicSize, hw = hh * cam.aspect;
                            cam.projectionMatrix = Matrix4x4.Ortho(-hw, hw, -hh, hh, cam.nearClipPlane, cam.farClipPlane);
                            cam.cullingMatrix = cam.projectionMatrix * cam.worldToCameraMatrix;
                            cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                            tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
                            sheet.SetPixels(i * W, row * H, W, H, tile.GetPixels());
                        }
                    }
                    finally { cam.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(display); }
                }
                sheet.Apply();
                File.WriteAllBytes(file, sheet.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); }
        }
    }
}
