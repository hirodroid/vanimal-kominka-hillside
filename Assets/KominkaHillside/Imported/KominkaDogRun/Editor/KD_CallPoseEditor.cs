using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    public static class KD_CallPoseEditor
    {
        const string Name = "KD_CallPose";
        const string Program = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CallPose.asset";
        const int PoseCount = KD_SleepSlot.MaxPoseId + 1;
        static readonly string[] States = { "CallLie", "CallSit", "CallPaw", "CallCurl", "CallBellyUp", "CallChin", "CallStretch" };
        static AnimationClip Clip(KD_ClipRef r) { return AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(r.guid)); }
        internal static AnimationClip PoseClip(KD_DogDef d, int pose)
        {
            if (pose == 3 || pose == 4) return CompanionClip(d, pose == 3 ? "Curl" : "BellyUp");
            return pose == 0 ? Clip(d.sleepLoop) : pose == 2 ? SitClip(d)
                : d.LieDownIsDerived ? AssetDatabase.LoadAssetAtPath<AnimationClip>(d.LieDownDerivedPath) : Clip(d.lieDown);
        }
        internal static AnimationClip SitClip(KD_DogDef d) => CompanionClip(d,"Sit");
        static AnimationClip CompanionClip(KD_DogDef d, string name)
        {
            string path = "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/Companion/KD_" + name + "_" + d.key + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            float length = name == "StretchYawn" ? 6f : 5f;
            if (clip == null || Mathf.Abs(clip.length - length) > .02f) throw new InvalidOperationException("Missing or invalid companion clip " + path);
            return clip;
        }
        internal static AnimationClip PawClip(KD_DogDef d)
        {
            string path = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(d.sit.guid)).Replace('\\', '/') + "/ote.anim";
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (source == null) throw new InvalidOperationException("Missing paw clip " + d.key);
            string output = "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/KD_PawHold_" + d.key + ".anim";
            var hold = AssetDatabase.LoadAssetAtPath<AnimationClip>(output);
            if (hold == null) { hold = new AnimationClip(); AssetDatabase.CreateAsset(hold, output); }
            hold.ClearCurves(); hold.name = "KD_PawHold_" + d.key;
            int pawCurves = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                // The raised paw relies on the matching chest/shoulder pose as well as the foreleg.
                // Preserve the complete authored pose so the foot remains above the ground.
                float value = AnimationUtility.GetEditorCurve(source, binding).Evaluate(2f);
                AnimationUtility.SetEditorCurve(hold, binding, AnimationCurve.Constant(0, 1, value));
                pawCurves++;
            }
            if (pawCurves == 0) throw new InvalidOperationException("Paw curves missing " + d.key);
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
                var value = keys.FirstOrDefault().value;
                AnimationUtility.SetObjectReferenceCurve(hold, binding, new[] { new ObjectReferenceKeyframe { time = 0, value = value } });
            }
            var settings = AnimationUtility.GetAnimationClipSettings(hold); settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(hold, settings); EditorUtility.SetDirty(hold);
            return hold;
        }

        internal static void ConfigureAnimator(KD_DogDef d, AnimatorController ctrl)
        {
            foreach (var p in new[] { "CallReady", "CallPose" })
                if (!ctrl.parameters.Any(x => x.name == p)) ctrl.AddParameter(p, p == "CallReady" ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Int);
            if (!ctrl.parameters.Any(x => x.name == "CallTime")) ctrl.AddParameter("CallTime", AnimatorControllerParameterType.Float);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var t in sm.anyStateTransitions.ToArray())
                if (t.destinationState != null && States.Contains(t.destinationState.name)) sm.RemoveAnyStateTransition(t);
            foreach (var s in sm.states.ToArray()) if (States.Contains(s.state.name)) sm.RemoveState(s.state);
            var clips = new[] { PoseClip(d, 1), PoseClip(d, 2), PawClip(d), PoseClip(d, 3), PoseClip(d, 4), CompanionClip(d, "ChinRest"), CompanionClip(d, "StretchYawn") };
            for (int i = 0; i < States.Length; i++)
            {
                var state = sm.AddState(States[i], new Vector3(950, i * 90)); state.motion = clips[i];
                state.timeParameterActive = i == 6; state.timeParameter = "CallTime";
                var enter = sm.AddAnyStateTransition(state); enter.hasExitTime = false; enter.hasFixedDuration = true;
                enter.duration = .2f; enter.canTransitionToSelf = false;
                enter.AddCondition(AnimatorConditionMode.If, 0, "CallReady");
                enter.AddCondition(AnimatorConditionMode.Equals, i + 1, "CallPose");
                foreach (string move in new[] { "Run", "Walk" })
                {
                    var target = sm.states.First(s => s.state.name == move).state;
                    var t = state.AddTransition(target); t.hasExitTime = false; t.hasFixedDuration = true; t.duration = 0;
                    t.AddCondition(AnimatorConditionMode.IfNot, 0, "CallReady");
                    t.AddCondition(AnimatorConditionMode.If, 0, move);
                }
                var leave = state.AddTransition(sm.defaultState); leave.hasExitTime = false; leave.hasFixedDuration = true; leave.duration = .2f;
                leave.AddCondition(AnimatorConditionMode.IfNot, 0, "CallReady");
            }
            EditorUtility.SetDirty(ctrl);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Call Poses/1 - Create Program")]
        public static void CreateProgram()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Program);
            if (asset == null) { asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>(); AssetDatabase.CreateAsset(asset, Program); }
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CallPose.cs");
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets();
        }

        static GameObject Child(Transform parent, string name)
        {
            var t = parent.Find(name); if (t != null) return t.gameObject;
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go;
        }

        internal static void RebuildIfPresent()
        {
            if (KD_SceneObjects.Find(Name) != null) Setup();
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Call Poses/2 - Setup")]
        public static void Setup()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            Directory.CreateDirectory("Temp/call-poses");
            var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            if (!File.Exists("Temp/call-poses/before.unity")) EditorSceneManager.SaveScene(pouch.gameObject.scene, "Temp/call-poses/before.unity", true);
            var go = KD_SceneObjects.Find(Name);
            if (go == null) go = Child(pouch.sleepBoard.transform.parent, Name);
            var c = go.GetComponent<KD_CallPose>(); if (c == null) c = go.AddUdonSharpComponent<KD_CallPose>();
            c.pouch = pouch; c.board = pouch.sleepBoard; c.placement = pouch.sleepPlacement; c.language = pouch.language; pouch.callPose = c;
            c.dogs = new KD_DogWanderController[6]; c.pawTargets = new Vector3[6]; c.pawRadii = new float[6];
            c.chinTargets = new Vector3[6]; c.chinRadii = new float[6];
            var page = pouch.tabPages[KD_TreatPouch.TabSleep].transform;
            c.panel = KD_ShopSetupEditor.EnsureUiChild(page, "CallPosePanel", new Vector2(0, -20), new Vector2(1050, 460));
            // OpenToAllButton は権限者にだけ実行時に出す (RedrawSleep が持ち主)。ここに含めると Setup / 面を閉じた瞬間に全員へ出る (2026-09-16 実測)
            c.dogListParts = page.Cast<Transform>().Where(t => t.gameObject != c.panel && t.name != "OpenToAllButton").Select(t => t.gameObject).ToArray();
            var font = pouch.sleepLabels[0].font; var log = new StringBuilder();
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            udon.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            var catalog = KD_LocalizationEditor.Load();
            TextMeshProUGUI Label(string name, Vector2 pos, Vector2 size, string key, int pt = 22)
            {
                var obj = KD_ShopSetupEditor.EnsureUiChild(c.panel.transform, name, pos, size);
                var t = obj.GetComponent<TextMeshProUGUI>() ?? obj.AddComponent<TextMeshProUGUI>();
                t.font = font; t.fontSize = pt; t.text = KD_LocalizationEditor.Text(catalog, "ja", key);
                t.alignment = TextAlignmentOptions.Center; t.color = Color.white; t.raycastTarget = false; t.enableWordWrapping = true;
                return t;
            }
            Button Button(string name, Vector2 pos, Vector2 size, string key, string evt)
            {
                var b = KD_ShopSetupEditor.EnsureButton(c.panel.transform, name, pos, size, KD_UiSpriteEditor.ActionNeutral, font,
                    KD_LocalizationEditor.Text(catalog, "ja", key), udon, evt, log).GetComponent<Button>();
                var label = b.GetComponentInChildren<TextMeshProUGUI>(); label.fontSize = 20; label.rectTransform.sizeDelta = size - new Vector2(12, 4);
                b.navigation = new Navigation { mode = Navigation.Mode.None }; return b;
            }
            c.title = Label("Title", new Vector2(0, 200), new Vector2(1000, 40), "call.choose", 24);
            c.poseButtons = new Button[PoseCount];
            for (int p = 0; p < PoseCount; p++) c.poseButtons[p] = Button("Pose" + p, new Vector2((p - 2) * 200, -30), new Vector2(184, 52), "call.pose" + p, "_Pose" + p);
            c.status = Label("Hint", new Vector2(0, -92), new Vector2(1000, 62), "call.hint", 20);
            Button("Back", new Vector2(-320, -210), new Vector2(290, 52), "call.back", "_Back");
            c.stretchButton = Button("Stretch", new Vector2(-320, -150), new Vector2(290, 52), "call.stretch", "_Stretch");
            c.placeButton = Button("Place", new Vector2(320, -150), new Vector2(290, 52), "call.place", "_Place");
            c.applyButton = Button("Apply", new Vector2(0, -150), new Vector2(290, 52), "call.apply", "_Apply");
            c.releaseButton = Button("Release", new Vector2(320, -210), new Vector2(290, 52), "call.release", "_Release");
            c.pawButton = Button("Paw", new Vector2(0, -210), new Vector2(290, 52), "call.paw", "_Paw");
            c.miniatures = new GameObject[6 * PoseCount]; c.placement.poseGhosts = new GameObject[6 * PoseCount];
            var ghostMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_SleepPreview.mat");
            for (int d = 0; d < 6; d++)
            {
                var def = KD_DogDefs.All[d]; var source = KD_SceneObjects.Find(def.DogName);
                c.dogs[d] = source.GetComponentsInChildren<KD_DogWanderController>(true).FirstOrDefault();
                if (c.dogs[d] == null) c.dogs[d] = KD_SceneObjects.Find(def.WanderSystemName).GetComponent<KD_DogWanderController>();
                ConfigureAnimator(def, AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath));
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    for (int pose = 0; pose < PoseCount + 2; pose++)
                    {
                        var clone = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(clone, scene);
                        clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.SetActive(true);
                        var clip = pose < PoseCount ? PoseClip(def, pose) : pose == PoseCount ? PawClip(def) : CompanionClip(def, "ChinRest");
                        KD_DogPreviewEditor.SamplePose(clone, new KD_ClipRef(clip.name, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)), clip.length));
                        if (pose < PoseCount)
                        {
                            // The ghost and the miniature are the same pose and differ only by material,
                            // so bake once and mirror. Baking both cost 90 duplicate meshes / 3.7MB of
                            // the download (2026-09-10).
                            var mini = Child(c.panel.transform, "Mini_" + def.key + "_" + pose);
                            var art = Child(mini.transform, "Art");
                            KD_DogPreviewEditor.BakeBody(clone, art.transform, def.key + "_call" + pose, null);
                            var ghost = Child(c.placement.markerDogs[d].transform, pose == 0 ? "PosePreview" : "PosePreview" + pose);
                            KD_DogPreviewEditor.MirrorBakedMeshes(art.transform, ghost.transform, ghostMat);
                            c.placement.poseGhosts[d * PoseCount + pose] = ghost; ghost.SetActive(pose == 0);
                            var bounds = new Bounds(); bool first = true;
                            foreach (var f in art.GetComponentsInChildren<MeshFilter>()) { if (first) { bounds = f.sharedMesh.bounds; first = false; } else bounds.Encapsulate(f.sharedMesh.bounds); }
                            art.transform.localPosition = -bounds.center;
                            mini.transform.localPosition = new Vector3((pose - 2) * 200, 80, -85);
                            Vector3 viewpoint = pose == 3 ? new Vector3(2f, .35f, 0f)
                                : pose == 4 ? new Vector3(2f, 1.2f, 1f) : new Vector3(1f, .7f, 2f);
                            mini.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(-viewpoint, Vector3.up));
                            // Fit the displayed silhouette, not the unrotated dog bounds.
                            var projected = new Bounds(Vector3.zero, Vector3.zero);
                            for (int corner = 0; corner < 8; corner++)
                            {
                                var point = Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                                projected.Encapsulate(mini.transform.localRotation * point);
                            }
                            mini.transform.localScale = Vector3.one * Mathf.Min(160 / Mathf.Max(.1f, projected.size.x), 130 / Mathf.Max(.1f, projected.size.y));
                            c.miniatures[d * PoseCount + pose] = mini; mini.SetActive(false);
                        }
                        else if (pose == PoseCount)
                        {
                            // The foreleg chain grows from the chest (Bone.002), unlike hind legs at Bone.
                            var candidates = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(t => t != null && t.name == "Bone.014_L").Distinct().ToArray();
                            if (candidates.Length == 0) throw new InvalidOperationException("Paw bones missing " + def.key);
                            var raised = candidates.First();
                            c.pawTargets[d] = clone.transform.InverseTransformPoint(raised.position);
                            c.pawRadii[d] = Mathf.Clamp(Mathf.Abs(c.pawTargets[d].x) * 1.5f, .08f, .16f);
                            log.AppendLine(def.key + " paw target=" + c.pawTargets[d] + " radius=" + c.pawRadii[d]);
                        }
                        else
                        {
                            var pet = c.dogs[d].petController;
                            if (pet == null || pet.chinAnchor == null || pet.headAnchor == null || pet.sourceBone == null)
                                throw new InvalidOperationException("Chin rig missing " + def.key);
                            Transform Copy(Transform original) => clone.transform.Find(AnimationUtility.CalculateTransformPath(original, source.transform));
                            var chin = Copy(pet.chinAnchor); var head = Copy(pet.headAnchor); var driver = Copy(pet.sourceBone);
                            if (chin == null || head == null || driver == null) throw new InvalidOperationException("Chin sample missing " + def.key);
                            c.chinTargets[d] = clone.transform.InverseTransformPoint(chin.position);
                            c.chinRadii[d] = Mathf.Clamp(Vector3.Distance(chin.position, head.position) * .55f, .06f, .1f);
                            pet.companionSlot = c.board.SlotOf(d);
                            pet.chinRestVector = Quaternion.Inverse(driver.rotation) * (chin.position - head.position);
                            pet.chinRestTarget = c.chinTargets[d]; pet.chinRestRadius = c.chinRadii[d];
                            EditorUtility.SetDirty(pet); UdonSharpEditorUtility.CopyProxyToUdon(pet);
                            log.AppendLine(def.key + " chin target=" + c.chinTargets[d] + " radius=" + c.chinRadii[d]);
                        }
                        Object.DestroyImmediate(clone);
                    }
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            c.panel.SetActive(false);
            foreach (var part in c.dogListParts) part.SetActive(true);
            KD_LocalizationEditor.Configure(pouch.language, catalog, log);
            KD_ShopSetupEditor.AddGlyphs(KD_LocalizationEditor.BundledCharacters(catalog), log);
            KD_LocalizationEditor.Bind(pouch.language, catalog, log);
            foreach (var b in new UdonSharp.UdonSharpBehaviour[] { c, pouch, c.placement, pouch.language })
            { EditorUtility.SetDirty(b); UdonSharpEditorUtility.CopyProxyToUdon(b); }
            for (int d = 0; d < c.dogs.Length; d++)
            {
                foreach (var b in new UdonSharpBehaviour[] { c.dogs[d], c.board.SlotOf(d) })
                { EditorUtility.SetDirty(b); UdonSharpEditorUtility.CopyProxyToUdon(b); }
            }
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(go.scene);
            File.WriteAllText("Temp/call-poses/setup.txt", log.ToString());
            KD_ActionProductsEditor.Setup();
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Call Poses/3 - Verify")]
        public static void Verify()
        {
            var log = new StringBuilder(); int failures = 0;
            void Check(bool ok, string label) { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
            var c = KD_SceneObjects.Find(Name).GetComponent<KD_CallPose>();
            Check(c.pouch.callPose == c && c.board != null && c.placement != null, "wiring");
            Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(c).SyncMethod == VRC.SDKBase.Networking.SyncType.None, "local UI sync mode");
            Check(c.dogs.Length == 6 && c.miniatures.Length == 30 && c.placement.poseGhosts.Length == 30 && c.poseButtons.Length == 5, "six dogs / thirty previews / five poses");
            Check(!c.panel.activeSelf, "menu closed initially");
            var uiBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            GameObject[] savedPreviews;
            Check(uiBacking.publicVariables.TryGetVariableValue("miniatures", out savedPreviews) && savedPreviews.SequenceEqual(c.miniatures), "thirty backing miniatures");
            var placementBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(c.placement);
            Check(placementBacking.publicVariables.TryGetVariableValue("poseGhosts", out savedPreviews) && savedPreviews.SequenceEqual(c.placement.poseGhosts), "thirty backing placement ghosts");
            var uiProgram = uiBacking.programSource.SerializedProgramAsset.RetrieveProgram();
            foreach (var entry in new[] { "_Pose3", "_Pose4", "_Stretch" })
                Check(uiProgram.EntryPoints.GetExportedSymbols().Contains(entry), "compiled UI entry " + entry);
            for (int d = 0; d < 6; d++)
            {
                var def = KD_DogDefs.All[d]; var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath); var sm = ctrl.layers[0].stateMachine;
                Check(c.dogs[d] != null && c.pawTargets[d].sqrMagnitude > .0001f && c.pawRadii[d] > .05f, def.key + " paw wiring");
                Check(c.pawTargets[d].y > .025f, def.key + " raised paw above placement floor");
                var pet = c.dogs[d].petController;
                Check(pet != null && pet.companionSlot == c.board.SlotOf(d) && pet.chinRestVector.sqrMagnitude > .00001f
                    && pet.chinRestTarget == c.chinTargets[d] && c.chinRadii[d] >= .06f, def.key + " chin wiring");
                var petUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pet);
                object wiredSlot;
                Check(petUdon.publicVariables.TryGetVariableValue("companionSlot", out wiredSlot)
                    && wiredSlot as Object == UdonSharpEditorUtility.GetBackingUdonBehaviour(c.board.SlotOf(d)), def.key + " chin backing slot");
                Vector3 target;
                Check(petUdon.publicVariables.TryGetVariableValue("chinRestTarget", out target) && target == c.chinTargets[d], def.key + " chin backing target");
                var slotProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(c.board.SlotOf(d)).programSource.SerializedProgramAsset.RetrieveProgram();
                foreach (var field in new[] { "chinHand", "stretchMs", "hasStretch" })
                    Check(slotProgram.SymbolTable.GetSymbols().Contains(field), def.key + " compiled slot field " + field);
                Check(ctrl.parameters.Any(p => p.name == "CallReady") && ctrl.parameters.Any(p => p.name == "CallPose"), def.key + " parameters");
                foreach (var name in States) Check(sm.states.Count(s => s.state.name == name && s.state.motion != null) == 1, def.key + " " + name);
                Check(sm.anyStateTransitions.Count(t => t.destinationState != null && States.Contains(t.destinationState.name)) == States.Length, def.key + " priority entries");
                var stretch = sm.states.First(s => s.state.name == "CallStretch").state;
                Check(stretch.timeParameterActive && stretch.timeParameter == "CallTime" && !((AnimationClip)stretch.motion).isLooping, def.key + " synchronized one-shot");
                for (int p = 0; p < PoseCount; p++)
                {
                    int i = d * PoseCount + p;
                    Check(c.miniatures[i] != null && c.miniatures[i].GetComponentsInChildren<MeshFilter>(true).Length > 0, "mini " + i);
                    Check(c.placement.poseGhosts[i] != null && c.placement.poseGhosts[i].transform.IsChildOf(c.placement.markerDogs[d].transform), "ghost " + i);
                }
            }
            foreach (var b in c.panel.GetComponentsInChildren<Button>(true))
                Check(b.onClick.GetPersistentEventCount() == 1 && b.onClick.GetPersistentTarget(0) == UdonSharpEditorUtility.GetBackingUdonBehaviour(c), "button " + b.name);
            log.AppendLine("failures=" + failures); Directory.CreateDirectory("Temp/call-poses"); File.WriteAllText("Temp/call-poses/verify.txt", log.ToString());
            if (failures > 0) throw new InvalidOperationException(log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Call Poses/4 - Preview")]
        public static void Preview()
        {
            var c = KD_SceneObjects.Find(Name).GetComponent<KD_CallPose>(); var p = c.pouch;
            var catalog = KD_LocalizationEditor.Load(); var previous = RenderTexture.active;
            Directory.CreateDirectory("Temp/call-poses");
            var fit = new StringBuilder(); int overflow = 0;
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (string lang in new[] { "ja", "en" })
                foreach (string state in new[] { "owned", "locked", "confirm" })
                {
                    var clone = Object.Instantiate(p.panelRoot); clone.SetActive(false); SceneManager.MoveGameObjectToScene(clone, scene);
                    foreach (var b in clone.GetComponentsInChildren<UdonSharpBehaviour>(true)) Object.DestroyImmediate(b);
                    foreach (var b in clone.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true)) Object.DestroyImmediate(b);
                    foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                    clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.transform.localScale = Vector3.one * .001f;
                    clone.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                    foreach (var page in p.tabPages) clone.transform.Find(page.name).gameObject.SetActive(page == p.tabPages[KD_TreatPouch.TabSleep]);
                    for (int i = 0; i < p.tabImages.Length; i++)
                    {
                        var tabImage = clone.transform.Find(AnimationUtility.CalculateTransformPath(p.tabImages[i].transform, p.panelRoot.transform)).GetComponent<Image>();
                        tabImage.color = i == KD_TreatPouch.TabSleep ? p.tabOnColor : p.tabOffColor;
                        var underline = clone.transform.Find(AnimationUtility.CalculateTransformPath(p.tabUnderlines[i].transform, p.panelRoot.transform));
                        underline.gameObject.SetActive(i == KD_TreatPouch.TabSleep);
                    }
                    clone.transform.Find("Page_Settings").gameObject.SetActive(false);
                    var panel = clone.transform.Find(p.tabPages[KD_TreatPouch.TabSleep].name + "/CallPosePanel");
                    foreach (Transform t in panel.parent) t.gameObject.SetActive(t == panel);
                    for (int i = 0; i < c.miniatures.Length; i++) panel.Find(c.miniatures[i].name).gameObject.SetActive(i < PoseCount);
                    for (int i = 0; i < p.language.fixedLabels.Length; i++)
                    {
                        var original = p.language.fixedLabels[i];
                        if (original == null || !original.transform.IsChildOf(p.panelRoot.transform)) continue;
                        string path = AnimationUtility.CalculateTransformPath(original.transform, p.panelRoot.transform);
                        clone.transform.Find(path).GetComponent<TMP_Text>().text = KD_LocalizationEditor.Text(catalog, lang, p.language.fixedKeys[i]);
                    }
                    clone.transform.Find("SectionTitle").GetComponent<TMP_Text>().text = KD_LocalizationEditor.Text(catalog, lang, "KD_TreatPouch.WordSleepTitle");
                    panel.Find("Title").GetComponent<TMP_Text>().text = "Corgi / " + KD_LocalizationEditor.Text(catalog, lang, "call.choose") + " / " + KD_LocalizationEditor.Text(catalog, lang, "call.current") + KD_LocalizationEditor.Text(catalog, lang, "call.pose2");
                    panel.Find("Hint").GetComponent<TMP_Text>().text = KD_LocalizationEditor.Text(catalog, lang, "call.pawHint");
                    panel.Find("Pose2").GetComponent<Image>().color = p.tabOnColor;
                    bool locked = state != "owned", confirming = state == "confirm";
                    panel.Find("Hint").GetComponent<TMP_Text>().text = KD_LocalizationEditor.Text(catalog, lang, locked ? "call.lockedHint" : "call.pawHint");
                    panel.Find("Pose4/Label").GetComponent<TMP_Text>().text = KD_LocalizationEditor.Text(catalog, lang, "action.belly_up") + (locked ? "  30 pt" : "");
                    panel.Find("Paw/Label").GetComponent<TMP_Text>().text = KD_LocalizationEditor.Text(catalog, lang, locked ? "action.give_paw" : "call.paw") + (locked ? "  30 pt" : "");
                    panel.Find("Stretch/Label").GetComponent<TMP_Text>().text = KD_LocalizationEditor.Text(catalog, lang, "action.stretch_yawn") + (locked ? "  30 pt" : "");
                    // P13: 確認は板の手前の共有カード。プレビューでは制御の表示だけを切り替える (カードは Treat の Preview が担当)
                    foreach (var control in c.purchaseControls) panel.Find(control.name).gameObject.SetActive(!confirming);
                    clone.SetActive(true); Canvas.ForceUpdateCanvases();
                    foreach (var t in clone.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate(true);
                    foreach (var t in panel.GetComponentsInChildren<TMP_Text>())
                    {
                        var size = t.GetPreferredValues(t.text, t.rectTransform.rect.width, Mathf.Infinity);
                        bool ok = size.x <= t.rectTransform.rect.width + 1 && size.y <= t.rectTransform.rect.height + 1;
                        if (!ok) overflow++;
                        fit.AppendLine((ok ? "PASS " : "FAIL ") + lang + "/" + state + "/" + t.transform.parent.name + "/" + t.name + " preferred=" + size + " rect=" + t.rectTransform.rect.size);
                    }
                    foreach (var m in clone.GetComponentsInChildren<RectMask2D>()) m.PerformClipping();
                    var camGo = new GameObject("CallPreviewCamera"); SceneManager.MoveGameObjectToScene(camGo, scene);
                    var camera = camGo.AddComponent<Camera>(); camera.scene = scene; camera.transform.position = new Vector3(0, 0, -2);
                    camera.useOcclusionCulling = false;
                    camera.orthographic = true; camera.orthographicSize = .35f; camera.aspect = 1140f / 700f; camera.cullingMask = 1 << 31;
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
                    var rt = new RenderTexture(1140, 700, 24); var image = new Texture2D(1140, 700, TextureFormat.RGB24, false);
                    try { camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1140, 700), 0, 0); image.Apply(); File.WriteAllBytes("Temp/call-poses/" + lang + "-" + state + ".png", image.EncodeToPNG()); }
                    finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(rt); Object.DestroyImmediate(image); Object.DestroyImmediate(camGo); Object.DestroyImmediate(clone); }
                }
            }
            finally { RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(scene); }
            fit.AppendLine("FIT_OVERFLOWS=" + overflow); File.WriteAllText("Temp/call-poses/fit.txt", fit.ToString());
            if (overflow > 0) throw new InvalidOperationException("Call pose text overflow: " + overflow);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Call Poses/5 - Verify Animator Playback")]
        public static void VerifyPlayback()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var log = new StringBuilder(); int failures = 0;
            void Check(bool ok, string label) { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var def in KD_DogDefs.All)
                {
                    var source = KD_SceneObjects.Find(def.DogName);
                    var dog = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(dog, scene);
                    try
                    {
                        dog.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); dog.SetActive(true);
                        var animator = dog.GetComponentsInChildren<Animator>(true).First(a => a.runtimeAnimatorController != null);
                        animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
                        void Advance() { for (int frame = 0; frame < 30; frame++) animator.Update(.05f); }
                        for (int state = 1; state <= States.Length; state++)
                        {
                            animator.SetBool("Sleep", false); animator.SetBool("CallReady", true);
                            animator.SetInteger("CallPose", state); animator.SetFloat("CallTime", .5f); Advance();
                            Check(animator.GetCurrentAnimatorStateInfo(0).IsName(States[state - 1]), def.key + " enters " + States[state - 1]);
                            var clip = animator.GetCurrentAnimatorClipInfo(0).FirstOrDefault().clip;
                            Check(clip != null && clip == ((AnimatorController)animator.runtimeAnimatorController).layers[0].stateMachine.states.First(s => s.state.name == States[state - 1]).state.motion,
                                def.key + " bound clip " + States[state - 1]);
                        }
                        // One-shot uses the shared server timeline, including late-join samples.
                        var sample = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(sample, scene);
                        try
                        {
                            var motion = CompanionClip(def, "StretchYawn");
                            var paths = AnimationUtility.GetCurveBindings(motion).Where(b => b.type == typeof(Transform)).Select(b => b.path).Distinct().ToArray();
                            foreach (float fraction in new[] { .25f, .5f, .75f })
                            {
                                animator.SetFloat("CallTime", fraction); animator.Update(.01f);
                                motion.SampleAnimation(sample, motion.length * fraction);
                                float maxAngle = 0f, maxPosition = 0f;
                                foreach (var path in paths)
                                {
                                    var expected = sample.transform.Find(path); var actual = dog.transform.Find(path);
                                    maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(actual.localRotation, expected.localRotation));
                                    maxPosition = Mathf.Max(maxPosition, Vector3.Distance(actual.localPosition, expected.localPosition));
                                }
                                // State normalizedTime is elapsed state time, not parameter-controlled motion time.
                                Check(maxAngle < .1f && maxPosition < .0001f, def.key + " sampled timeline " + fraction + " angle=" + maxAngle + " position=" + maxPosition);
                            }
                        }
                        finally { Object.DestroyImmediate(sample); }
                        animator.SetInteger("CallPose", 1); Advance();
                        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("CallLie"), def.key + " returns from stretch to selected pose");
                        animator.SetBool("CallReady", false); animator.SetBool("Sleep", true); Advance();
                        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Sleep"), def.key + " returns to legacy sleep");
                        animator.SetBool("Sleep", false); animator.SetBool("Run", true); Advance();
                        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Run"), def.key + " released dog can move");
                    }
                    finally { Object.DestroyImmediate(dog); }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Directory.CreateDirectory("Temp/call-poses"); log.AppendLine("failures=" + failures);
            File.WriteAllText("Temp/call-poses/playback.txt", log.ToString());
            if (failures > 0) throw new InvalidOperationException(log.ToString());
        }
    }
}
