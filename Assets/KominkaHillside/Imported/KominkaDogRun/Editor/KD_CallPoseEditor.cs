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

        /// <summary>裏山改変: 犬の Animator Controller (古民家からコピー済み) に呼ぶポーズの 7 状態が既に揃っているか。
        /// 揃っていれば作り直さない (作り直すと状態と遷移の fileID が変わり、同じ中身でも .controller の差分が出る)。</summary>
        internal static bool AnimatorConfigured(AnimatorController ctrl)
        {
            if (ctrl == null) return false;
            foreach (var p in new[] { "CallReady", "CallPose", "CallTime" })
                if (!ctrl.parameters.Any(x => x.name == p)) return false;
            var sm = ctrl.layers[0].stateMachine;
            foreach (var name in States)
                if (sm.states.Count(x => x.state.name == name && x.state.motion != null) != 1) return false;
            return sm.anyStateTransitions.Count(t => t.destinationState != null && States.Contains(t.destinationState.name)) == States.Length;
        }

        /// <summary>裏山改変: ポーズのミニチュア・配置ゴーストから外すさんぽの道具。首輪 (KH_Collar_*) は含めない。</summary>
        internal static bool IsWalkingGear(string name)
        {
            return name.StartsWith("KH_Lead") || name.StartsWith("KH_ConnectedHandle") || name.StartsWith("KH_Roam")
                || name.StartsWith("KH_LeashClip") || name.StartsWith("KH_Handle");
        }

        /// <summary>裏山改変: 追加動作 4 種の商品キー。添字は KD_SleepSlot.BellyUp / ChinRest / GivePaw / StretchYawn。
        /// 商品 (KH_Dogs/KD_ShopCatalog/KD_ShopItem_Action_*) は段階 1 の Menu/4 Setup Shop が作る。ここでは配線だけ。</summary>
        internal static readonly string[] ActionKeys = { "action.belly_up", "action.chin_rest", "action.give_paw", "action.stretch_yawn" };

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

        /// <summary>裏山改変: IMP-DOG-016。Menu/6j。KH_Dogs/KD_CallPose (ポーズ面 + ミニチュア 30 + 場所決めのゴースト 30) を作る / 作り直す。
        /// Menu/7 (BuildPouchPanel) と Menu/18 (DogPreview) からも RebuildIfPresent で呼ばれる。
        /// 前提: Menu/6 Setup Sleep、Menu/6i Setup Sleep Placement、段階 1 の KD_ShopItem_Action_* 4 行。
        /// 古民家から変えた点: シーンの控え (Temp/call-poses/before.unity) を保存しない / 台帳と場所決めは名前で引く /
        /// Animator が既に構成済みなら作り直さない / 首輪は焼き、さんぽの道具 (IsWalkingGear) だけ外す /
        /// 追加動作の商品は作らず段階 1 の行を配線するだけ (KD_ActionProductsEditor.Setup は呼ばない)。**シーンは保存しない。**</summary>
        [MenuItem("Tools/KominkaHillside/Menu/6j Setup Call Poses")]
        public static void Setup()
        {
            if (EditorApplication.isPlaying || !KD_Hillside.InScene()) throw new InvalidOperationException("Open " + KD_Hillside.ScenePath + " in Edit mode");
            Directory.CreateDirectory("Temp/call-poses");
            var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            // 裏山改変: 台帳と場所決めは名前で引く (Menu/7 の途中から呼ばれても配線順に依存しない)
            var boardGo = KD_SceneObjects.Find(KD_DogDefs.SleepBoardName);
            if (boardGo != null && boardGo.GetComponent<KD_SleepBoard>() != null) pouch.sleepBoard = boardGo.GetComponent<KD_SleepBoard>();
            var placementGo = KD_SceneObjects.Find(KD_SleepPlacementEditor.SystemName);
            if (placementGo != null && placementGo.GetComponent<KD_SleepPlacement>() != null) pouch.sleepPlacement = placementGo.GetComponent<KD_SleepPlacement>();
            if (pouch.sleepBoard == null || pouch.sleepPlacement == null || pouch.sleepPlacement.markerDogs == null || pouch.sleepPlacement.markerDogs.Length != 6)
                throw new InvalidOperationException("Run Menu/6 Setup Sleep and Menu/6i Setup Sleep Placement first");
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
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
                // 裏山改変: 古民家からコピーした Controller は構成済み。揃っていれば作り直さない
                if (AnimatorConfigured(controller)) log.AppendLine(def.key + " animator: 構成済み (作り直さない)");
                else { ConfigureAnimator(def, controller); log.AppendLine(def.key + " animator: 呼ぶポーズの 7 状態を構成した"); }
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    for (int pose = 0; pose < PoseCount + 2; pose++)
                    {
                        var clone = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(clone, scene);
                        clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.SetActive(true);
                        // 裏山改変: 首輪 (KH_Collar_*) は焼く (司令塔の決定: かざり試着の Menu/18 と同じ見た目)。
                        // 犬に付いて見えるさんぽの道具 (リード・クリップ・持ち手・徘徊の目印) だけ外す
                        foreach (var part in clone.GetComponentsInChildren<Transform>(true))
                            if (part != clone.transform && IsWalkingGear(part.name)) part.gameObject.SetActive(false);
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
            // 裏山改変: 追加動作 4 種の商品 (段階 1 で作成済み) を面と 6 枠へ配線する。商品の中身 (価格・区分・台帳) は触らない
            HillsideActionProducts(c, pouch, log);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine("**シーンは保存していない。** 次: Menu/6b Verify Sleep System");
            KD_VerifyLog.Write(KD_Hillside.LogDir + "setup-call-poses.txt", log);
        }

        /// <summary>裏山改変: KD_ActionProductsEditor.Setup の配線部分だけ。商品は KD_ProductDefinitions の sceneName で引き、
        /// 無ければ止める (段階 1 の Menu/4 Setup Shop が先)。価格・区分・台帳・カタログの束ね直しはしない (段階 1 の持ち物)。</summary>
        static void HillsideActionProducts(KD_CallPose c, KD_TreatPouch p, StringBuilder log)
        {
            var items = new KD_ShopItem[ActionKeys.Length];
            for (int i = 0; i < ActionKeys.Length; i++)
            {
                string sceneName = KD_ProductDefinitions.SceneName(ActionKeys[i]);
                var itemGo = KD_SceneObjects.Find(sceneName, out int count);
                items[i] = (count == 1 && itemGo != null) ? itemGo.GetComponent<KD_ShopItem>() : null;
                if (items[i] == null) throw new InvalidOperationException("Action product " + sceneName + " missing (" + count + "). Run Menu/4 Setup Shop (stage 1) first");
            }
            c.actionProducts = items;
            for (int d = 0; d < 6; d++)
            {
                var slot = c.board.SlotOf(d);
                if (slot == null) throw new InvalidOperationException("Six existing slots required");
                slot.actionProducts = items; EditorUtility.SetDirty(slot); UdonSharpEditorUtility.CopyProxyToUdon(slot);
            }
            // P13 (2026-09-16): 追加動作の確認も板の手前の共有カード (KD_TreatPouch.confirmRoot)。旧 Purchase パネルは消す
            var legacyPurchase = c.panel.transform.Find("Purchase");
            if (legacyPurchase != null) { Object.DestroyImmediate(legacyPurchase.gameObject); log.AppendLine("removed CallPosePanel/Purchase (P13)"); }
            if (p.confirmRoot == null) throw new InvalidOperationException("Shared confirm card missing — run Menu/7 Build Menu first");
            c.purchaseControls = new[] { c.placeButton.gameObject, c.applyButton.gameObject, c.releaseButton.gameObject,
                c.pawButton.gameObject, c.stretchButton.gameObject, c.panel.transform.Find("Back").gameObject, c.status.gameObject };
            EditorUtility.SetDirty(c); UdonSharpEditorUtility.CopyProxyToUdon(c);
            log.AppendLine("action products: " + string.Join(", ", items.Select(x => x.name + " " + x.price + "pt")) + " → KD_CallPose + 6 枠");
        }

        /// <summary>裏山改変: 古民家の 3 - Verify (例外で止める) を Menu/6b の一部 (件数を数える) にした。裏山の追加: 置き場所・language・追加動作の商品。</summary>
        internal static void VerifyInto(KD_VerifyLog v)
        {
            void Check(bool ok, string label) { v.Check(ok, label); }
            var cgo = KD_SceneObjects.Find(Name);
            if (!v.Check(cgo != null && cgo.GetComponent<KD_CallPose>() != null, "KD_CallPose がある (Menu/6j)")) return;
            var c = cgo.GetComponent<KD_CallPose>();
            Check(c.pouch.callPose == c && c.board != null && c.placement != null, "wiring");
            // ---- 裏山改変: IMP-DOG-016 ----
            Check(cgo.transform.parent != null && cgo.transform.parent.name == KD_Hillside.DogsRootName && cgo.transform.parent.parent == null, "KD_CallPose が KH_Dogs 直下");
            Check(c.language != null && c.language == c.pouch.language, "language 配線");
            Check(c.panel != null && c.panel.transform.parent == c.pouch.tabPages[KD_TreatPouch.TabSleep].transform, "CallPosePanel が Page_Sleep の子");
            Check(c.dogListParts != null && c.dogListParts.Length > 0 && !c.dogListParts.Contains(c.panel) && c.dogListParts.All(x => x != null && x.name != "OpenToAllButton"),
                "犬の一覧 (dogListParts) = Page_Sleep の子から面と許可ボタンを除いたもの (" + (c.dogListParts == null ? 0 : c.dogListParts.Length) + ")");
            Check(c.actionProducts != null && c.actionProducts.Length == ActionKeys.Length, "追加動作の商品 4 行");
            for (int i = 0; c.actionProducts != null && i < ActionKeys.Length && i < c.actionProducts.Length; i++)
            {
                var item = c.actionProducts[i];
                var scene = KD_SceneObjects.Find(KD_ProductDefinitions.SceneName(ActionKeys[i]));
                Check(item != null && scene != null && item.gameObject == scene && item.productKey == ActionKeys[i] && item.price == 30 && !item.IsCE() && !item.IsStocked() && item.menuOnly,
                    ActionKeys[i] + " = " + KD_ProductDefinitions.SceneName(ActionKeys[i]) + " (段階 1 の行) / 30pt / 永続 / menuOnly");
                for (int d = 0; d < 6; d++)
                {
                    var slot = c.board.SlotOf(d);
                    object raw = null;
                    bool wired = slot != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(slot).publicVariables.TryGetVariableValue("actionProducts", out raw);
                    var values = raw as Object[];
                    Check(slot != null && slot.actionProducts != null && slot.actionProducts.Length == 4 && slot.actionProducts[i] == item && wired && values != null && values.Length == 4
                        && item != null && values[i] == UdonSharpEditorUtility.GetBackingUdonBehaviour(item), ActionKeys[i] + " 枠 " + d + " proxy/backing (ActionOwnedBy の課金ゲート)");
                }
            }
            Check(c.pouch.confirmRoot != null && c.panel.transform.Find("Purchase") == null && c.purchaseControls != null && c.purchaseControls.Length == 7, "確認は共有カード (P13)。旧 Purchase パネルが無い");
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
                // 裏山改変: IMP-DOG-054 同期の hasStretch (bool) は trickKind (int) に置き換えた (同期変数の数は同じ)
                foreach (var field in new[] { "chinHand", "stretchMs", "trickKind" })
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

        /// <summary>裏山改変: 古民家の 5 - Verify Animator Playback を Menu/6b の一部にした (Preview Scene だけで回す。シーンは触らない)。</summary>
        internal static void VerifyPlaybackInto(KD_VerifyLog v)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            void Check(bool ok, string label) { v.Check(ok, label); }
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
        }
    }
}
