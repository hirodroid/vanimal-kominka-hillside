using System;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    public static class KD_SleepPlacementChecksEditor
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Sleep Placement/7 - Apply Call Safety Language")]
        public static void ApplyCallSafetyLanguage()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            // Preserve the user's current in-memory scene before the narrowly scoped text update.
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Temp/before-call-safety.unity", true);
            var log = new StringBuilder();
            var language = KD_SceneObjects.Find("KD_Language").GetComponent<KD_Language>();
            var catalog = KD_LocalizationEditor.Load();
            var font = language.pouch.sectionTitleLabel.font;
            foreach (char ch in KD_LocalizationEditor.BundledCharacters(catalog))
                if (!char.IsControl(ch) && ch != '\u200b' && !KD_LocalizationEditor.HasGlyph(font, ch))
                    throw new InvalidOperationException("Missing glyph: U+" + ((int)ch).ToString("X4"));
            KD_LocalizationEditor.Configure(language, catalog, log);
            EditorUtility.SetDirty(language);
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(language);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            File.WriteAllText("Temp/call-safety-language-apply.txt", log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Sleep Placement/8 - Verify Call Safety")]
        public static void VerifyCallSafety()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var log = new StringBuilder(); int failures = 0;
            void Check(bool ok, string label) { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
            var p = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            var c = p.callPose; var placement = p.sleepPlacement;
            Check(c != null && placement != null && c.placement == placement && placement.pouch == p, "call UI/placement references");
            Check(c.gameObject.activeInHierarchy && placement.gameObject.activeInHierarchy
                && !c.transform.IsChildOf(p.panelRoot.transform) && !placement.transform.IsChildOf(p.panelRoot.transform), "controllers survive menu hiding");
            // Tab は VRChat 2026.3.3 で HUD 操作に取られた (2026-09-15)。F は投げ、Q / E は配置の回転、Y はチャット
            Check(p.desktopToggleKey == KeyCode.T, "serialized desktop key is T (not Tab / F / Q / E / Y)");
            Check(p.sleepBoard.slots.Length == KD_DogDefs.All.Length, "six existing slots retained");
            foreach (var slot in p.sleepBoard.slots)
            {
                var backing = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(slot);
                Check(backing != null && backing.SyncMethod == VRC.SDKBase.Networking.SyncType.Manual, slot.name + " backing Manual");
                Check(slot.board == p.sleepBoard && slot.DogRoot() != null, slot.name + " board and dog linked");
            }
            var language = KD_SceneObjects.Find("KD_Language").GetComponent<KD_Language>();
            var catalog = KD_LocalizationEditor.Load();
            KD_LocalizationEditor.ValidateScene(language, catalog);
            Check(true, "full language catalog, bindings and glyphs match");
            var fixture = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("DesktopToggleFixture"); SceneManager.MoveGameObjectToScene(go, fixture);
                var test = go.AddComponent<KD_TreatPouch>();
                test.panelRoot = new GameObject("Panel"); test.panelRoot.transform.SetParent(go.transform);
                test.panelRoot.SetActive(false);
                test._DesktopToggle(); Check(test.panelRoot.activeSelf, "desktop key opens hidden menu");
                test._DesktopToggle(); Check(!test.panelRoot.activeSelf, "desktop key closes visible menu");
                test._DesktopToggle(); Check(test.panelRoot.activeSelf, "desktop key reopens menu");
                test._Toggle(); Check(test.panelRoot.activeSelf, "Touch/VR recall remains open");
                test._Close(); Check(!test.panelRoot.activeSelf, "close button remains available");
            }
            finally { EditorSceneManager.ClosePreviewScene(fixture); }
            log.AppendLine("RESULT failures=" + failures);
            File.WriteAllText("Temp/call-safety-verify.txt", log.ToString());
            if (failures > 0) throw new InvalidOperationException(log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Sleep Placement/6 - Check Input Hands")]
        public static void CheckInputHands()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var scene = EditorSceneManager.NewPreviewScene();
            var log = new StringBuilder(); int failures = 0;
            void Check(bool ok, string what) { log.AppendLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }
            try
            {
                var go = new GameObject("InputFixture"); SceneManager.MoveGameObjectToScene(go, scene);
                var p = go.AddComponent<KD_SleepPlacement>();
                var progress = new GameObject("Progress", typeof(RectTransform), typeof(Image));
                progress.transform.SetParent(go.transform);
                p.confirmProgress = progress.GetComponent<Image>();
                p.confirmProgress.type = Image.Type.Filled;
                bool Held(string field) { return (bool)typeof(KD_SleepPlacement).GetField(field, Private).GetValue(p); }
                var right = new VRC.Udon.Common.UdonInputEventArgs(true, VRC.Udon.Common.HandType.RIGHT);
                var left = new VRC.Udon.Common.UdonInputEventArgs(true, VRC.Udon.Common.HandType.LEFT);
                log.AppendLine("SDK RIGHT=" + (int)right.handType + " LEFT=" + (int)left.handType);
                // The real event handler tracks hands even before a LocalPlayer exists.
                p.InputUse(true, right);
                Check(Held("_rightHeld") && !Held("_leftHeld"), "right trigger tracks confirmation hand only");
                p.InputUse(true, left);
                Check(Held("_rightHeld") && Held("_leftHeld"), "both triggers tracked independently");
                typeof(KD_SleepPlacement).GetField("_holdingConfirm", Private).SetValue(p, true);
                p.confirmProgress.fillAmount = .5f;
                p.InputUse(false, left);
                Check(Held("_rightHeld") && !Held("_leftHeld") && Held("_holdingConfirm") && p.confirmProgress.fillAmount == .5f,
                    "left release leaves right hold progress intact");
                p.InputUse(false, right);
                Check(!Held("_rightHeld") && !Held("_leftHeld") && !Held("_holdingConfirm") && p.confirmProgress.fillAmount == 0f && !progress.activeSelf,
                    "right release clears hold progress");
                p.InputUse(true, left);
                Check(!Held("_rightHeld") && Held("_leftHeld"), "left trigger tracks board hand only (no cancel, IMP-DOG-031)");
                p.InputUse(false, left);
                Check(!Held("_rightHeld") && !Held("_leftHeld"), "both released permits placement arming");
                // 板の ◀▶: 指定中 (_state == 1) だけ受け付け、タップは TurnStep。離しの重複は無視。
                var yawField = typeof(KD_SleepPlacement).GetField("_yaw", Private);
                p._TurnRightDown(); p._TurnUp();
                Check((float)yawField.GetValue(p) == 0f, "turn ignored while placement is closed");
                typeof(KD_SleepPlacement).GetField("_state", Private).SetValue(p, 1);
                p._TurnRightDown(); p._TurnUp(); p._TurnUp();
                Check(Mathf.Abs((float)yawField.GetValue(p) - KD_SleepPlacement.TurnStep) < .001f, "board tap turns one step, duplicate release adds none");
                p._TurnLeftDown(); p._TurnUp();
                Check(Mathf.Abs((float)yawField.GetValue(p)) < .001f, "left tap turns back");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            log.AppendLine("RESULT failures=" + failures);
            File.WriteAllText("Temp/sleep-placement-input-hands.txt", log.ToString());
            if (failures > 0) throw new InvalidOperationException(log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Sleep Placement/4 - Check Movement")]
        public static void CheckMovement()
        {
            var scene = EditorSceneManager.NewPreviewScene(); var log = new StringBuilder(); int failures = 0;
            void Check(bool ok, string what) { log.AppendLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }
            try
            {
                var go = new GameObject("Fixture"); SceneManager.MoveGameObjectToScene(go, scene);
                var w = go.AddComponent<KD_DogWanderController>();
                var s = go.AddComponent<KD_SleepSlot>();
                w.dogRoot = go.transform; w.dogAnimator = go.AddComponent<Animator>();
                var real = KD_SceneObjects.Find(KD_DogDefs.All[0].WanderSystemName).GetComponent<KD_DogWanderController>();
                w.dogAnimator.runtimeAnimatorController = real.dogAnimator.runtimeAnimatorController;
                void Set(object target, string name, object value) { target.GetType().GetField(name, Private).SetValue(target, value); }
                object Call(object target, string name, params object[] args) { return target.GetType().GetMethod(name, Private).Invoke(target, args); }
                Vector3 spot = new Vector3(1000, 12, 1000);
                Set(s, "spot", spot); Set(s, "spotYaw", 75f); Set(s, "hasSpot", true);
                go.transform.SetPositionAndRotation(spot, Quaternion.Euler(0,75,0));
                Set(w, "_dogOwner", true); Set(w, "_speed", 2f); Set(w, "_wasMoving", true); Set(w, "_wasRunning", true);
                w.dogAnimator.SetBool("Run", true);
                Call(w, "UpdatePlacedSleep", s);
                Check(go.transform.position == spot, "Owner holds exact elevated target without ground snap");
                Check((bool)w.GetType().GetField("_wantSleep", Private).GetValue(w), "Sleep pose begins at target");
                Check(!w.dogAnimator.GetBool("Run") && !w.dogAnimator.GetBool("Walk"), "lift clears walking/running");
                go.transform.position = spot - Vector3.up;
                Vector3 before = go.transform.position; Quaternion rotation = go.transform.rotation;
                Set(w, "_dogOwner", false); Call(w, "UpdatePlacedSleep", s);
                Check(go.transform.position == before && go.transform.rotation == rotation, "nonOwner never writes transform during lift");
                Check((bool)Call(s,"ValidPlacement",spot,75f), "finite placement accepted");
                Check(!(bool)Call(s,"ValidPlacement",new Vector3(0,float.NaN,0),75f), "NaN coordinate rejected");
                Check(!(bool)Call(s,"ValidPlacement",spot,float.PositiveInfinity), "infinite yaw rejected");
                Check(s.HasSpot && s.Spot == spot && s.SpotYaw == 75, "shared slot retains world target");
                w.groundLayerMask = real.groundLayerMask; w.areaCenter = real.areaCenter;
                Vector3 high = real.dogRoot.position + Vector3.up * 100;
                Vector3 grounded = (Vector3)Call(w, "GroundAfterPlacedSleep", high);
                Check(grounded.y < high.y - 4 && Mathf.Abs(grounded.x-high.x) < .01f && Mathf.Abs(grounded.z-high.z) < .01f,
                    "high sleeping position returns to scene floor beyond ordinary 4m ray");

                // ---- 裏山改変: IMP-DOG-032 (古民家どおり置いた高さへ浮く・押し出されない) ----
                // 裏山の配線 (hillsideLeashes) があっても、収まる途中に接地しない。実シーンの地面の 1.2 m 上 (膝・胸の高さ) に置き、
                // 犬はその真下 (地面) から真上へだけ浮く = 水平の残りが 0 でも収まり中 (3D で測る)
                w.hillsideLeashes = real.hillsideLeashes; w.groundRayOffset = real.groundRayOffset; w.groundRayDistance = real.groundRayDistance;
                w.placedSleepLiftSpeed = .5f; w.placedSleepLiftRadius = .4f;
                Check(w.hillsideLeashes != null, "fixture carries the Hillside wiring (hillsideLeashes) like the scene dogs");
                // 地面の点: 犬 (実シーンの 6 匹) の体に当たらない所を 2 m おきに探す
                Vector3 floor = Vector3.zero; bool floorOk = false;
                for (int k = 0; k < 8 && !floorOk; k++)
                {
                    Vector3 probe = real.dogRoot.position + Quaternion.Euler(0, k * 45f, 0) * Vector3.forward * (2f + k);
                    RaycastHit hit;
                    if (!Physics.Raycast(probe + Vector3.up * 100, Vector3.down, out hit, Mathf.Infinity, real.groundLayerMask, QueryTriggerInteraction.Ignore)) continue;
                    bool onDog = false;
                    foreach (var d in KD_DogDefs.All) { var dg = KD_SceneObjects.Find(d.DogName); if (dg != null && hit.transform.IsChildOf(dg.transform)) onDog = true; }
                    if (!onDog) { floor = hit.point; floorOk = true; }
                }
                Check(floorOk, "found open ground near " + real.name + " for the elevated-spot fixture");
                Vector3 lap = floor + Vector3.up * 1.2f;
                Set(s, "spot", lap); Set(s, "spotYaw", 30f); Set(s, "hasSpot", true);
                go.transform.SetPositionAndRotation(floor, Quaternion.Euler(0, 30, 0));
                Set(w, "_dogOwner", true); Set(w, "_sleepFrozen", false); Set(w, "_placedSleepActive", true);
                Set(w, "_inTick", true); Set(w, "_dt", .1f);   // Dt() = 0.1 s: one 0.05 m step of the lift
                Call(w, "UpdatePlacedSleep", s);
                float risen = go.transform.position.y - floor.y;
                Check(risen > .04f && risen < .06f, "lift above the ground is not snapped back (rose " + risen.ToString("F3") + " m in 0.1 s, IMP-DOG-016 change 3 removed)");
                Check((bool)w.GetType().GetField("_placedSettling", Private).GetValue(w), "a vertical-only lift still counts as settling (busy every frame until it arrives)");
                Check(w.PlacedSleepHeld, "PlacedSleepHeld while settling onto the placed spot (push-apart exempt)");
                Set(w, "_dt", 100f);
                Call(w, "UpdatePlacedSleep", s);
                Check(go.transform.position == lap, "dog sleeps at the placed height (" + go.transform.position.y.ToString("F2") + " = ground + 1.2)");
                Check(!(bool)w.GetType().GetField("_placedSettling", Private).GetValue(w), "arrived: settling ends");

                // 隣の犬が重なっても押し出されない (KD_DogPack は接地スナップするので、押されると地面へ落ちる)
                var otherGo = new GameObject("Neighbour"); SceneManager.MoveGameObjectToScene(otherGo, scene);
                otherGo.transform.position = lap + Vector3.right * .05f;
                var packGo = new GameObject("Pack"); SceneManager.MoveGameObjectToScene(packGo, scene);
                var pack = packGo.AddComponent<KD_DogPack>();
                pack.dogRoots = new[] { go.transform, otherGo.transform }; pack.dogRadius = new[] { .3f, .3f };
                pack.dogWander = new[] { w, null }; pack.dogFetch = new KD_DogFetchController[2]; pack.groundLayerMask = real.groundLayerMask;
                w.hillsideLeashes = null;   // the pack borrows the leash manager from here; its roster is not this fixture's (no warning, no exemption)
                Call(pack, "Start");
                string packNote = "";
                try { Call(pack, "LateUpdate"); }
                catch (TargetInvocationException e) { packNote = " (the free neighbour's Networking.IsOwner is not available in Edit mode: " + e.InnerException?.GetType().Name + ")"; }
                bool scanned = ((bool[])pack.GetType().GetField("_prevValid", Private).GetValue(pack))[0];
                if (!scanned) log.AppendLine("SKIP push-apart: Time.deltaTime is 0 in Edit mode, the pack did not scan");
                else Check(go.transform.position == lap, "an overlapping neighbour does not push / ground-snap the placed dog" + packNote);

                // 起きる: 呼び出しが終わったら (LiveSleeperOf < 0) 地面へ戻り、押し出しの対象へ戻る
                var board = go.AddComponent<KD_SleepBoard>(); board.slots = new KD_SleepSlot[0];
                w.sleepBoard = board; w.dogIndex = 0;
                Set(w, "_inTick", false);
                Call(w, "UpdateSleep");
                Check(Mathf.Abs(go.transform.position.y - floor.y) < .01f, "wake: back on the ground (" + (go.transform.position.y - floor.y).ToString("F3") + " m)");
                Check(!w.PlacedSleepHeld, "wake: push-apart applies again");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            log.AppendLine("RESULT failures=" + failures); File.WriteAllText("Temp/sleep-placement-movement.txt", log.ToString());
            if (failures > 0) throw new InvalidOperationException(log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Sleep Placement/5 - Preview")]
        public static void Preview()
        {
            var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            Render(pouch.panelRoot, "Temp/sleep-placement-menu.png", true);
            Render(pouch.sleepPlacement.screenPanel.gameObject, "Temp/sleep-placement-controls.png", false);
        }

        static void Render(GameObject source, string output, bool menu)
        {
            var scene = EditorSceneManager.NewPreviewScene(); var previous = RenderTexture.active;
            var rt = new RenderTexture(1140, menu ? 780 : 420, 24); Texture2D image = null;
            try
            {
                var clone = UnityEngine.Object.Instantiate(source); clone.SetActive(false); SceneManager.MoveGameObjectToScene(clone, scene);
                foreach(var b in clone.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true)) UnityEngine.Object.DestroyImmediate(b);
                foreach(var b in clone.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true)) UnityEngine.Object.DestroyImmediate(b);
                foreach(var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.transform.localScale = Vector3.one * .001f;
                var canvas = clone.GetComponent<Canvas>(); if (canvas == null) canvas = clone.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                if (menu)
                {
                    foreach (Transform child in clone.transform)
                        if(child.name.StartsWith("Page_")) child.gameObject.SetActive(child.name == "Page_Sleep");
                    clone.transform.Find("SectionTitle").GetComponent<TextMeshProUGUI>().text = "ねる";
                    for (int i=0; i<KD_DogDefs.All.Length; i++)
                    {
                        var page = clone.transform.Find("Page_Sleep");
                        var row = page.Find("SleepRow" + i);
                        if(row == null) row = page.Find("Scroll/Viewport/Content/SleepRow" + i);
                        row.Find("Label").GetComponent<TextMeshProUGUI>().text = KD_DogDefs.All[i].displayName;
                        row.Find("Status").GetComponent<TextMeshProUGUI>().text = i == 0 ? "いっしょに寝ている" : "";
                        if (i==0) row.Find("Button/Label").GetComponent<TextMeshProUGUI>().text = "起こす";
                    }
                }
                clone.SetActive(true);
                var cameraGo = new GameObject("PreviewCamera"); SceneManager.MoveGameObjectToScene(cameraGo, scene);
                cameraGo.transform.position = new Vector3(0,0,-2);
                var camera = cameraGo.AddComponent<Camera>(); camera.scene=scene; camera.orthographic=true;
                camera.orthographicSize=menu?.26f:.14f; camera.aspect=760f/(menu?520:280);
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.1f,.1f,.1f);
                camera.cullingMask=1<<31; camera.nearClipPlane=.1f; camera.farClipPlane=10; camera.targetTexture=rt;
                Canvas.ForceUpdateCanvases(); foreach(var t in clone.GetComponentsInChildren<TextMeshProUGUI>(true)) t.ForceMeshUpdate(true);
                camera.Render(); RenderTexture.active=rt;
                image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply(); File.WriteAllBytes(output,image.EncodeToPNG()); camera.targetTexture=null;
            }
            finally { RenderTexture.active=previous; EditorSceneManager.ClosePreviewScene(scene); UnityEngine.Object.DestroyImmediate(rt); if(image!=null)UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
