using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    public static class KD_SleepPlacementEditor
    {
        public const string SystemName = "KD_SleepPlacement";
        const string ProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepPlacement.asset";
        const string MeshDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/SleepPlacement";

        internal static string Words()
        {
            string words = "0123456789.-";
            foreach (var f in typeof(KD_SleepPlacement).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(string)) words += (string)f.GetRawConstantValue();
            return words;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Sleep Placement/1 - Create Program Asset")]
        public static void CreateProgram()
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) != null) return;
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepPlacement.cs");
            AssetDatabase.CreateAsset(asset, ProgramPath);
            AssetDatabase.SaveAssets();
        }

        /// <summary>裏山改変: IMP-DOG-016。Menu/6i。KH_Dogs/KD_SleepPlacement (場所決め) を作る / 作り直す。
        /// 前提: Menu/6 Setup Sleep (KH_Dogs/KD_SleepBoard) と Menu/7 (手元メニューの呼ぶタブ SleepRow0..5)。
        /// 古民家から変えた点: 器は KH_Dogs 直下 / language の配線 / BuildUI から Scroll.Build を呼ばない (Menu/7 が最後に呼ぶ)。
        /// IMP-DOG-032: マーカーは古民家どおり手 / 頭の前にそのまま置く (接地・斜面判定なし)。IMP-DOG-016 変更 2 の赤い印
        /// (markerDogs[i]/Invalid) は作らず、残っていれば消す (backing の invalidMarks も外す)。**シーンは保存しない。**</summary>
        [MenuItem("Tools/KominkaHillside/Menu/6i Setup Sleep Placement")]
        public static void Setup()
        {
            if (EditorApplication.isPlaying || !KD_Hillside.InScene()) throw new InvalidOperationException("Open " + KD_Hillside.ScenePath + " in Edit mode");
            var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            // 裏山改変: 台帳は名前で引く (Menu/7 の BuildPouchPanel の途中からも呼ばれるので、pouch.sleepBoard の配線順に依存しない)
            var boardGo = KD_SceneObjects.Find(KD_DogDefs.SleepBoardName);
            var board = boardGo == null ? null : boardGo.GetComponent<KD_SleepBoard>();
            if (board != null && pouch.sleepBoard != board) pouch.sleepBoard = board;
            if (pouch.sleepButtons == null || pouch.sleepButtons.Length != KD_DogDefs.All.Length || pouch.sleepBoard == null)
                throw new InvalidOperationException("Build sleep system (Menu/6) and hand menu (Menu/7) first");
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) == null)
                throw new InvalidOperationException("Create program and compile first");
            foreach (var def in KD_DogDefs.All)
                if (KD_SceneObjects.Find(def.DogName) == null || KD_SceneObjects.Find(def.WanderSystemName) == null)
                    throw new InvalidOperationException("Missing dog " + def.key);
            int count;
            var go = KD_SceneObjects.Find(SystemName, out count);
            if (count > 1) throw new InvalidOperationException("Duplicate placement system");
            if (go == null)
            {
                go = new GameObject(SystemName);
                Undo.RegisterCreatedObjectUndo(go, "Create sleep placement");
                KD_SceneObjects.PlaceIfNew(go, KD_Hillside.DogsRootName);   // 裏山改変: KH_Dogs 直下
            }
            var placement = go.GetComponent<KD_SleepPlacement>();
            if (placement == null) placement = go.AddUdonSharpComponent<KD_SleepPlacement>();
            var log = new StringBuilder();
            UdonSharpEditorUtility.GetBackingUdonBehaviour(placement).SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            KD_ShopSetupEditor.AddGlyphs(Words(), log);
            BuildMarker(placement, log);
            BuildUI(pouch, placement, log);
            foreach (var def in KD_DogDefs.All)
            {
                var wander = KD_SceneObjects.Find(def.WanderSystemName).GetComponent<KD_DogWanderController>();
                var rig = KD_DogRig.Resolve(KD_SceneObjects.Find(def.DogName));
                wander.placedSleepLiftRadius = .4f * rig.BodyRatio;
                wander.placedSleepLiftSpeed = .5f * rig.BodyRatio;
                EditorUtility.SetDirty(wander); UdonSharpEditorUtility.CopyProxyToUdon(wander);
                var slot = KD_SceneObjects.Find(KD_DogDefs.SleepSlotNameOf(def.key)).GetComponent<KD_SleepSlot>();
                EditorUtility.SetDirty(slot); UdonSharpEditorUtility.CopyProxyToUdon(slot);
            }
            EditorUtility.SetDirty(placement); EditorUtility.SetDirty(pouch);
            UdonSharpEditorUtility.CopyProxyToUdon(placement); UdonSharpEditorUtility.CopyProxyToUdon(pouch);
            // IMP-DOG-032: IMP-DOG-016 変更 2 の配線の残り (フィールドは消した) を backing から外す
            var placementBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(placement);
            if (placementBacking.publicVariables.TryGetVariableType(StaleInvalidMarks, out _))
            {
                placementBacking.publicVariables.RemoveVariable(StaleInvalidMarks);
                EditorUtility.SetDirty(placementBacking);
                log.AppendLine("IMP-DOG-032: backing の " + StaleInvalidMarks + " を外した");
            }
            EditorSceneManager.MarkSceneDirty(go.scene);
            AssetDatabase.SaveAssets();
            log.AppendLine("裏山: IMP-DOG-032 マーカーは古民家どおり手 / 頭の前にそのまま (接地・斜面判定・赤い印なし)");
            log.AppendLine("**シーンは保存していない。** 次: Menu/6j Setup Call Poses → Menu/6b Verify Sleep System");
            KD_VerifyLog.Write(KD_Hillside.LogDir + "setup-sleep-placement.txt", log);
        }

        internal static void RebuildIfPresent(KD_TreatPouch pouch, StringBuilder log)
        {
            var go = KD_SceneObjects.Find(SystemName);
            var placement = go == null ? null : go.GetComponent<KD_SleepPlacement>();
            if (placement == null) return;
            BuildUI(pouch, placement, log);
            EditorUtility.SetDirty(placement); UdonSharpEditorUtility.CopyProxyToUdon(placement);
        }

        static GameObject Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go;
        }

        static TextMeshProUGUI Label(Transform parent, string name, Vector2 pos, Vector2 size, string text, TMP_FontAsset font, float points = 24)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var tmp = go.GetComponent<TextMeshProUGUI>(); if (tmp == null) tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = font; tmp.fontSize = points; tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = text; tmp.color = Color.white; tmp.raycastTarget = false; tmp.enableWordWrapping = false;
            return tmp;
        }

        static Button Button(Transform parent, string name, Vector2 pos, Vector2 size, string label, TMP_FontAsset font,
            VRC.Udon.UdonBehaviour target, string evt, StringBuilder log, float points = 24)
        {
            var go = KD_ShopSetupEditor.EnsureButton(parent, name, pos, size, KD_UiSpriteEditor.ActionNeutral, font, label, target, evt, log);
            go.GetComponentInChildren<TextMeshProUGUI>().fontSize = points;
            go.GetComponentInChildren<TextMeshProUGUI>().rectTransform.sizeDelta = size - new Vector2(8, 4);
            var button = go.GetComponent<Button>(); button.navigation = new Navigation { mode = Navigation.Mode.None }; return button;
        }

        static Slider Slider(Transform parent, string name, Vector2 pos, Vector2 size, bool vertical, float min, float max,
            VRC.Udon.UdonBehaviour udon, string evt)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var bg = go.GetComponent<Image>(); if (bg == null) bg = go.AddComponent<Image>();
            bg.color = KD_UiSpriteEditor.SurfaceRaised; bg.raycastTarget = true;
            KD_UiSpriteEditor.ApplyRoundSkin(bg, KD_UiSpriteEditor.LoadRound());
            var area = KD_ShopSetupEditor.EnsureUiChild(go.transform, "HandleArea", Vector2.zero, size - Vector2.one * 16);
            var handle = KD_ShopSetupEditor.EnsureUiChild(area.transform, "Handle", Vector2.zero, new Vector2(36, 36));
            var hi = handle.GetComponent<Image>(); if (hi == null) hi = handle.AddComponent<Image>();
            hi.color = Color.white; KD_UiSpriteEditor.ApplyRoundSkin(hi, KD_UiSpriteEditor.LoadRound());
            var slider = go.GetComponent<Slider>(); if (slider == null) slider = go.AddComponent<Slider>();
            slider.handleRect = (RectTransform)handle.transform; slider.targetGraphic = hi;
            slider.direction = vertical ? UnityEngine.UI.Slider.Direction.BottomToTop : UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min; slider.maxValue = max;
            slider.SetValueWithoutNotify(vertical ? 1.5f : 0);
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            while (slider.onValueChanged.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(slider.onValueChanged, 0);
            UnityEventTools.AddStringPersistentListener(slider.onValueChanged, udon.SendCustomEvent, evt);
            return slider;
        }

        static void BuildUI(KD_TreatPouch pouch, KD_SleepPlacement placement, StringBuilder log)
        {
            placement.pouch = pouch; placement.board = pouch.sleepBoard; pouch.sleepPlacement = placement;
            placement.language = pouch.language;   // 裏山改変: 実行時の文言 (ヒント・平らな場所が必要) を言語表から引く
            var font = pouch.sectionTitleLabel.font;
            var su = UdonSharpEditorUtility.GetBackingUdonBehaviour(placement);
            // Calling now always goes through pose selection and placement; retire the duplicate entry.
            foreach (var entry in pouch.sleepButtons)
            {
                var old = entry.transform.parent.Find("PlaceButton");
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            var screen = KD_ShopSetupEditor.EnsureUiChild(placement.transform, "ScreenControls", Vector2.zero, new Vector2(760, 260));
            var canvas = screen.GetComponent<Canvas>(); if (canvas == null) canvas = screen.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
            var scaler = screen.GetComponent<CanvasScaler>(); if (scaler == null) scaler = screen.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (screen.GetComponent<GraphicRaycaster>() == null) screen.AddComponent<GraphicRaycaster>();
            var panel = KD_ShopSetupEditor.EnsureUiChild(screen.transform, "Panel", new Vector2(0, 155), new Vector2(760, 280));
            var rect = (RectTransform)panel.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            var bg = panel.GetComponent<Image>(); if (bg == null) bg = panel.AddComponent<Image>();
            bg.color = KD_UiSpriteEditor.SurfacePanel; bg.raycastTarget = true; bg.sprite = null; bg.type = Image.Type.Simple;
            placement.statusLabel = Label(panel.transform, "Hint", new Vector2(0, 105), new Vector2(720, 48), KD_SleepPlacement.TouchHint, font, 22);
            Label(panel.transform, "Distance", new Vector2(-275, 55), new Vector2(160, 36), KD_SleepPlacement.Distance, font);
            placement.distanceSlider = Slider(panel.transform, "DistanceSlider", new Vector2(-275, -36), new Vector2(52, 130), true, .5f, 3, su, "_DistanceChanged");
            Label(panel.transform, "Direction", new Vector2(65, 55), new Vector2(420, 36), KD_SleepPlacement.Direction, font);
            placement.yawSlider = Slider(panel.transform, "YawSlider", new Vector2(65, 10), new Vector2(450, 48), false, 0, 360, su, "_YawChanged");
            placement.confirmButton = Button(panel.transform, "Confirm", new Vector2(175, -80), new Vector2(210, 60), KD_SleepPlacement.Confirm, font, su, "_Confirm", log);
            placement.confirmButton.GetComponent<Image>().color = KD_UiSpriteEditor.ActionPrimary;
            Button(panel.transform, "Cancel", new Vector2(-55, -80), new Vector2(190, 60), KD_SleepPlacement.Cancel, font, su, "_Cancel", log);
            placement.screenCanvas = screen; placement.screenPanel = rect; screen.SetActive(false);
            var vr = KD_ShopSetupEditor.EnsureUiChild(placement.transform, "VrHint", Vector2.zero, VrBoardSize);
            placement.vrLabel = Label(vr.transform, "Hint", VrHintPos, VrHintSize, KD_SleepPlacement.VrHint, font, 24);
            ConfigureVrInputUI(placement, log);
            log.AppendLine("UI: 6 place buttons, local screen sliders/confirm/cancel, VR board (hint + turn/cancel)");
            // 裏山改変: 古民家はここで KD_HandMenuScrollEditor.Build を呼んでいた (行の PlaceButton を消したあとの詰め直し)。
            // 裏山の行に PlaceButton は無く、Menu/7 の BuildPouchPanel の途中 (Scroll.Build の前) からも呼ばれるので、ここでは呼ばない
        }

        // 頭に追従する VR の板 (IMP-DOG-031): ヒント 2 行 + [◀] [▶] (進捗円) [やめる]。scale .0009 で約 0.68m × 0.15m。
        internal static readonly Vector2 VrBoardSize = new Vector2(760, 170);
        internal static readonly Vector2 VrHintPos = new Vector2(0, 45), VrHintSize = new Vector2(720, 70);
        internal static readonly Vector2 VrButtonsPos = new Vector2(0, -40), VrButtonsSize = new Vector2(760, 60);
        internal static readonly Vector2 VrTurnSize = new Vector2(120, 48), VrCancelSize = new Vector2(180, 48);
        internal const float VrTurnLeftX = -260, VrTurnRightX = -120, VrCancelX = 200;

        internal static void ConfigureVrInputUI(KD_SleepPlacement placement, StringBuilder log)
        {
            KD_ShopSetupEditor.AddGlyphs(Words(), log);
            var label = placement.vrLabel;
            if (label == null) throw new InvalidOperationException("Missing placement hint");
            label.text = KD_SleepPlacement.VrHint;
            var board = label.transform.parent.gameObject;
            var su = UdonSharpEditorUtility.GetBackingUdonBehaviour(placement);
            // 板は頭に追従して動くので、Collider がプレイヤーを押さない層 (手元メニューと同じ) に置く。子は EnsureUiChild が親の層を写す。
            board.layer = KD_HandMenuCollisionEditor.InputLayer;
            var rect = (RectTransform)board.transform; rect.sizeDelta = VrBoardSize; rect.localScale = Vector3.one * .0009f;
            var canvas = board.GetComponent<Canvas>(); if (canvas == null) canvas = board.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            if (board.GetComponent<CanvasScaler>() == null) board.AddComponent<CanvasScaler>();
            if (board.GetComponent<GraphicRaycaster>() == null) board.AddComponent<GraphicRaycaster>();
            if (board.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null) board.AddComponent<VRC.SDK3.Components.VRCUiShape>();
            KD_ShopSetupEditor.EnsureUiChild(board.transform, "Hint", VrHintPos, VrHintSize);
            label.gameObject.SetActive(true);
            var row = KD_ShopSetupEditor.EnsureUiChild(board.transform, "Buttons", VrButtonsPos, VrButtonsSize);
            var left = Button(row.transform, "TurnLeft", new Vector2(VrTurnLeftX, 0), VrTurnSize, KD_SleepPlacement.TurnLeft, label.font, su, "_TurnUp", log, 28);
            HoldEvents(left.gameObject, su, "_TurnLeftDown");
            var right = Button(row.transform, "TurnRight", new Vector2(VrTurnRightX, 0), VrTurnSize, KD_SleepPlacement.TurnRight, label.font, su, "_TurnUp", log, 28);
            HoldEvents(right.gameObject, su, "_TurnRightDown");
            Button(row.transform, "Cancel", new Vector2(VrCancelX, 0), VrCancelSize, KD_SleepPlacement.Cancel, label.font, su, "_Cancel", log);
            placement.vrButtons = row;
            var go = KD_ShopSetupEditor.EnsureUiChild(board.transform, "ConfirmProgress", VrButtonsPos, new Vector2(42, 42));
            var image = go.GetComponent<Image>();
            if (image == null) image = go.AddComponent<Image>();
            // 円形の進捗専用。9-slice のボタン背景には使わない。
            image.sprite = KD_UiSpriteEditor.LoadCircle();
            if (image.sprite == null) throw new InvalidOperationException("Missing circle sprite");
            image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = 2; image.fillClockwise = true; image.fillAmount = 0f;
            image.color = KD_UiSpriteEditor.AccentGold; image.raycastTarget = false;
            placement.confirmProgress = image; go.SetActive(false);
            // 板は閉じた状態で保存する (ランタイムは Close で板ごと消し、指定中だけ UpdatePreview が出す)。
            board.SetActive(false);
            EditorUtility.SetDirty(label); EditorUtility.SetDirty(placement);
            UdonSharpEditorUtility.CopyProxyToUdon(placement);
        }

        /// <summary>押しっぱなしで回すための EventTrigger。PointerDown → down、PointerUp / PointerExit → `_TurnUp`。
        /// onClick (`_TurnUp`) は EnsureButton 側にあり、離しの保険になる。毎回作り直すので冪等。</summary>
        static void HoldEvents(GameObject button, VRC.Udon.UdonBehaviour udon, string down)
        {
            var trigger = button.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null) trigger = button.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            trigger.triggers = new List<UnityEngine.EventSystems.EventTrigger.Entry>();
            AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.PointerDown, udon, down);
            AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.PointerUp, udon, "_TurnUp");
            AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.PointerExit, udon, "_TurnUp");
        }

        static void AddTrigger(UnityEngine.EventSystems.EventTrigger trigger, UnityEngine.EventSystems.EventTriggerType type,
            VRC.Udon.UdonBehaviour udon, string evt)
        {
            var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type, callback = new UnityEngine.EventSystems.EventTrigger.TriggerEvent() };
            UnityEventTools.AddStringPersistentListener(entry.callback, udon.SendCustomEvent, evt);
            trigger.triggers.Add(entry);
        }

        static void BuildMarker(KD_SleepPlacement placement, StringBuilder log)
        {
            Directory.CreateDirectory(MeshDir); AssetDatabase.Refresh();
            var root = Child(placement.transform, "Marker"); placement.marker = root.transform;
            placement.markerDogs = new GameObject[KD_DogDefs.All.Length];
            for (int i = 0; i < KD_DogDefs.All.Length; i++)
            {
                var def = KD_DogDefs.All[i];
                var source = KD_SceneObjects.Find(def.DogName);
                var rig = KD_DogRig.Resolve(source); float ratio = rig.BodyRatio;
                Vector3 head;
                var preview = EditorSceneManager.NewPreviewScene();
                try
                {
                    var clone = UnityEngine.Object.Instantiate(source);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone, preview);
                    clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(def.sleepLoop.guid));
                    if (clip == null) throw new InvalidOperationException("Missing sleep clip " + def.key);
                    clip.SampleAnimation(clone, 0);
                    var sampled = KD_DogRig.Resolve(clone);
                    // 二段リグの回転ソースには頭の平行移動が無い。メッシュ側へ制約を適用して測る。
                    foreach (var bone in clone.GetComponentsInChildren<Transform>(true))
                    {
                        var pc = bone.GetComponent<PositionConstraint>();
                        if (pc != null && pc.constraintActive && pc.weight > .99f)
                        {
                            if (pc.sourceCount != 1 || pc.translationAxis != (Axis.X | Axis.Y | Axis.Z))
                                throw new InvalidOperationException("Unsupported head position constraint " + def.key + "/" + bone.name);
                            bone.position = pc.GetSource(0).sourceTransform.position + pc.translationOffset;
                        }
                        var rc = bone.GetComponent<RotationConstraint>();
                        if (rc != null && rc.constraintActive && rc.weight > .99f)
                        {
                            if (rc.sourceCount != 1 || rc.rotationAxis != (Axis.X | Axis.Y | Axis.Z))
                                throw new InvalidOperationException("Unsupported head rotation constraint " + def.key + "/" + bone.name);
                            bone.rotation = rc.GetSource(0).sourceTransform.rotation * Quaternion.Euler(rc.rotationOffset);
                        }
                    }
                    head = sampled.head.position;
                }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
                var meshPath = MeshDir + "/" + def.key + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, meshPath); }
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                void Tri(Vector3 a, Vector3 b, Vector3 c)
                {
                    int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
                    triangles.Add(n); triangles.Add(n+1); triangles.Add(n+2);
                    triangles.Add(n+2); triangles.Add(n+1); triangles.Add(n);
                }
                for (int s = 0; s < 32; s++)
                {
                    float a = s * Mathf.PI / 16, b = (s+1) * Mathf.PI / 16;
                    Vector3 p = new Vector3(Mathf.Sin(a)*.24f, .012f, Mathf.Cos(a)*.4f)*ratio;
                    Vector3 q = new Vector3(Mathf.Sin(b)*.24f, .012f, Mathf.Cos(b)*.4f)*ratio;
                    Tri(p, q, q*.9f); Tri(p, q*.9f, p*.9f);
                }
                Tri(new Vector3(-.1f, .02f, .35f)*ratio, new Vector3(.1f, .02f, .35f)*ratio, new Vector3(0, .02f, .55f)*ratio);
                float r = .055f * ratio;
                for (int s = 0; s < 4; s++)
                {
                    Vector3 a = head + new Vector3(Mathf.Sin(s*Mathf.PI/2), 0, Mathf.Cos(s*Mathf.PI/2))*r;
                    Vector3 b = head + new Vector3(Mathf.Sin((s+1)*Mathf.PI/2), 0, Mathf.Cos((s+1)*Mathf.PI/2))*r;
                    Tri(a, b, head + Vector3.up*r); Tri(b, a, head - Vector3.up*r);
                }
                mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                var matPath = MeshDir + "/" + def.key + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) { mat = new Material(Shader.Find("Unlit/Color")); AssetDatabase.CreateAsset(mat, matPath); }
                mat.color = def.ballColor; EditorUtility.SetDirty(mat);
                var go = Child(root.transform, def.key); go.layer = 2;
                var filter = go.GetComponent<MeshFilter>(); if (filter == null) filter = go.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>(); if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = mat; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                placement.markerDogs[i] = go; go.SetActive(i == 0);
                // IMP-DOG-032: IMP-DOG-016 変更 2 の赤い印 (子 "Invalid") は作らない。前の Setup の残りは消す
                var stale = go.transform.Find(StaleInvalidChild);
                if (stale != null) { UnityEngine.Object.DestroyImmediate(stale.gameObject); log.AppendLine(def.key + ": 赤い印 " + StaleInvalidChild + " を消した (IMP-DOG-032)"); }
                log.AppendLine(def.key + " head=" + head.ToString("F3") + " ratio=" + ratio + " triangles=" + triangles.Count/3);
            }
            root.SetActive(false);
        }

        /// <summary>IMP-DOG-032: IMP-DOG-016 変更 2 の名残 (犬の印の子の赤い印と、KD_SleepPlacement.invalidMarks の backing 変数)。
        /// Setup が消し、Verify が残っていないことを見る。材質 Models/SleepPlacement/Invalid.mat はどこからも参照されなくなる。</summary>
        internal const string StaleInvalidChild = "Invalid", StaleInvalidMarks = "invalidMarks";

        /// <summary>裏山改変: 単独の検査は Menu/6b (KD_SleepSetupEditor.Verify) にまとめた。ここは古民家の 3 - Verify の中身 + 裏山の追加。</summary>
        internal static void VerifyInto(KD_VerifyLog v)
        {
            void Check(bool ok, string what) { v.Check(ok, what); }
            void Event(UnityEngine.Object component, string field, VRC.Udon.UdonBehaviour target, string evt)
            {
                Calls(new SerializedObject(component).FindProperty(field + ".m_PersistentCalls.m_Calls"), target, evt);
            }
            void Calls(SerializedProperty calls, VRC.Udon.UdonBehaviour target, string evt)
            {
                Check(calls != null && calls.arraySize == 1
                    && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue == target
                    && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_MethodName").stringValue == "SendCustomEvent"
                    && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == evt, "binding " + evt);
            }
            var go = KD_SceneObjects.Find(SystemName); var p = go == null ? null : go.GetComponent<KD_SleepPlacement>();
            Check(p != null, "placement system");
            if (p != null)
            {
                // ---- 裏山改変: IMP-DOG-016 ----
                Check(go.transform.parent != null && go.transform.parent.name == KD_Hillside.DogsRootName && go.transform.parent.parent == null, "KD_SleepPlacement が KH_Dogs 直下");
                Check(p.language != null && p.language == p.pouch.language, "language 配線 (ヒントの文言)");
                // IMP-DOG-032: 赤い印 (IMP-DOG-016 変更 2) が残っていない。残っていれば Menu/6i が消す
                int staleMarks = 0;
                for (int i = 0; p.markerDogs != null && i < p.markerDogs.Length; i++)
                    if (p.markerDogs[i] != null && p.markerDogs[i].transform.Find(StaleInvalidChild) != null) staleMarks++;
                Check(staleMarks == 0, "赤い印 (犬の印の子 " + StaleInvalidChild + ") が残っていない (IMP-DOG-032。actual " + staleMarks + "。残っていれば Menu/6i)");
                Check(!UdonSharpEditorUtility.GetBackingUdonBehaviour(p).publicVariables.TryGetVariableType(StaleInvalidMarks, out _),
                    "backing に " + StaleInvalidMarks + " が残っていない (IMP-DOG-032。残っていれば Menu/6i)");
                v.Info("   IMP-DOG-032: マーカーは古民家どおり手の前 0.15 m (VR) / 頭の前 0.5〜3 m (Desktop・Touch) にそのまま置く。接地・斜面判定・赤い印なし。向きは水平の yaw だけ");
                Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(p).SyncMethod == VRC.SDKBase.Networking.SyncType.None, "local only");
                Check(p.marker != null && !p.marker.gameObject.activeSelf, "marker hidden initially");
                Check(p.screenCanvas != null && !p.screenCanvas.activeSelf, "screen hidden initially");
                Check(p.pouch != null && p.board != null && p.pouch.sleepPlacement == p, "controller wiring");
                Check(p.markerDogs.Length == 6 && p.pouch.sleepButtons.Length == 6, "six dogs");
                for (int i=0; i<6; i++)
                {
                    var b = p.pouch.sleepButtons[i].GetComponent<Button>(); var mesh = p.markerDogs[i].GetComponent<MeshFilter>().sharedMesh;
                    Check(b != null && b.onClick.GetPersistentEventCount() == 1 && b.onClick.GetPersistentTarget(0) == UdonSharpEditorUtility.GetBackingUdonBehaviour(p.pouch), "button " + i);
                    Event(b, "m_OnClick", UdonSharpEditorUtility.GetBackingUdonBehaviour(p.pouch), "_Sleep" + i);
                    Check(b.transform.parent.GetComponentsInChildren<Button>(true).Length == 1, "single call entry " + i);
                    Check(mesh != null && mesh.triangles.Length/3 < 300, "marker budget " + i);
                    Check(p.markerDogs[i].GetComponent<Renderer>().sharedMaterial.shader.name == "Unlit/Color", "marker shader " + i);
                    var def = KD_DogDefs.All[i]; var w = KD_SceneObjects.Find(def.WanderSystemName).GetComponent<KD_DogWanderController>();
                    Check(w.placedSleepLiftRadius > 0 && w.placedSleepLiftSpeed > 0, "lift parameters " + i);
                }
                Check(p.distanceSlider.minValue == .5f && p.distanceSlider.maxValue == 3 && p.distanceSlider.direction == UnityEngine.UI.Slider.Direction.BottomToTop, "distance drag range");
                Check(p.yawSlider.minValue == 0 && p.yawSlider.maxValue == 360, "yaw drag range");
                Check(p.confirmButton.onClick.GetPersistentEventCount() == 1 && p.distanceSlider.onValueChanged.GetPersistentEventCount() == 1 && p.yawSlider.onValueChanged.GetPersistentEventCount() == 1, "control events");
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
                Event(p.confirmButton, "m_OnClick", backing, "_Confirm");
                Event(p.screenPanel.Find("Cancel").GetComponent<Button>(), "m_OnClick", backing, "_Cancel");
                Event(p.distanceSlider, "m_OnValueChanged", backing, "_DistanceChanged");
                Event(p.yawSlider, "m_OnValueChanged", backing, "_YawChanged");
                // VR の板 (IMP-DOG-031): 閉じて保存 / 入力層 / UiShape / ◀▶ の押下・離し 3 経路 / やめる
                var board = p.vrLabel == null ? null : p.vrLabel.transform.parent.gameObject;
                Check(board != null && !board.activeSelf && p.vrLabel.gameObject.activeSelf, "VR board hidden initially, hint enabled");
                Check(board != null && board.layer == KD_HandMenuCollisionEditor.InputLayer && board.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null
                    && board.GetComponent<GraphicRaycaster>() != null && board.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace, "VR board input layer / UiShape");
                Check(p.vrButtons != null && p.vrButtons.transform.parent == board.transform, "VR buttons row wired");
                foreach (var name in new[] { "TurnLeft", "TurnRight" })
                {
                    var b = p.vrButtons.transform.Find(name);
                    Check(b != null && b.gameObject.layer == KD_HandMenuCollisionEditor.InputLayer, "turn button " + name);
                    if (b == null) continue;
                    Event(b.GetComponent<Button>(), "m_OnClick", backing, "_TurnUp");
                    var trigger = b.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                    Check(trigger != null && trigger.triggers.Count == 3, "hold trigger " + name);
                    if (trigger == null) continue;
                    var delegates = new SerializedObject(trigger).FindProperty("m_Delegates");
                    for (int i = 0; i < trigger.triggers.Count; i++)
                    {
                        var entry = trigger.triggers[i];
                        string expect = entry.eventID == UnityEngine.EventSystems.EventTriggerType.PointerDown ? (name == "TurnLeft" ? "_TurnLeftDown" : "_TurnRightDown")
                            : entry.eventID == UnityEngine.EventSystems.EventTriggerType.PointerUp || entry.eventID == UnityEngine.EventSystems.EventTriggerType.PointerExit ? "_TurnUp" : "?";
                        Calls(delegates.GetArrayElementAtIndex(i).FindPropertyRelative("callback.m_PersistentCalls.m_Calls"), backing, expect);
                    }
                }
                var boardCancel = p.vrButtons == null ? null : p.vrButtons.transform.Find("Cancel");
                Check(boardCancel != null, "VR board cancel");
                if (boardCancel != null) Event(boardCancel.GetComponent<Button>(), "m_OnClick", backing, "_Cancel");
                Transform wiredMarker; GameObject wiredScreen;
                Check(backing.publicVariables.TryGetVariableValue("marker", out wiredMarker) && wiredMarker == p.marker, "backing marker");
                Check(backing.publicVariables.TryGetVariableValue("screenCanvas", out wiredScreen) && wiredScreen == p.screenCanvas, "backing screen");
                Check(KD_ShopSetupEditor.MissingGlyphs(Words()).Length == 0, "Japanese glyphs");
                // 裏山改変: ヒントの文字列は実行時に言語表で差し替わるので、幅の検査は古民家どおり既定 (ja の const) で見る
                foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
                    Check(t.GetPreferredValues(t.text).x <= t.rectTransform.rect.width + 1, "text width " + t.name);
                foreach (var b in p.pouch.sleepButtons)
                {
                    var t = b.GetComponentInChildren<TextMeshProUGUI>(); Check(t.GetPreferredValues(t.text).x <= t.rectTransform.rect.width + 1, "button text fit");
                }
            }
        }
    }
}
