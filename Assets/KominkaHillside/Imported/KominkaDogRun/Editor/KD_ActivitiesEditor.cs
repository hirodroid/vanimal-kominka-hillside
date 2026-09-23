using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaDogRun.Editor
{
    public static class KD_ActivitiesEditor
    {
        static readonly string[] Programs = { "KD_GroupActivities", "KD_DogPovCamera", "KD_ActivitiesMenu" };
        static readonly string[] ProductKeys = { "experience.formation", "experience.hosting_play", "experience.dog_pov" };
        static StringBuilder log;
        [MenuItem("Tools/KominkaHillside/Menu/8 Create Activities Programs")]
        public static void CreatePrograms()
        {
            foreach (var name in Programs)
            {
                string path = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/" + name;
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") != null) continue;
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path + ".cs");
                AssetDatabase.CreateAsset(asset, path + ".asset");
            }
            AssetDatabase.SaveAssets();
        }
        static T Find<T>() where T : Component { return KD_ProductsEditor.SceneComponents<T>().Single(); }
        static T Add<T>(GameObject go, Networking.SyncType sync) where T : UdonSharpBehaviour
        {
            T component = go.GetComponent<T>(); if (component == null) component = go.AddUdonSharpComponent<T>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(component);
            backing.SyncMethod = sync; EditorUtility.SetDirty(backing); return component;
        }
        static GameObject Host(string name, string parent)
        {
            var go = KD_SceneObjects.Find(name, out int count); if (count > 1) throw new Exception("Duplicate " + name);
            if (go == null) { go = new GameObject(name); KD_SceneObjects.PlaceIfNew(go, parent); }
            return go;
        }
        [MenuItem("Tools/KominkaHillside/Menu/9 Setup Activities")]
        public static void Build()
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/KominkaDogRun.unity") throw new Exception("Target Edit scene required");
            log = new StringBuilder();
            var pouch = Find<KD_TreatPouch>(); var live = Find<KD_LiveSystem>(); var pack = Find<KD_DogPack>();
            var language = Find<KD_Language>(); var photos = Find<KD_PhotoDirector>();
            var activities = Add<KD_GroupActivities>(live.gameObject, Networking.SyncType.Manual);
            var host = Host("KD_Activities", KD_DogDefs.SystemsPath);
            var menu = Add<KD_ActivitiesMenu>(host, Networking.SyncType.None);
            var pov = Add<KD_DogPovCamera>(host, Networking.SyncType.None);
            var ledger = Find<KD_ProgressLedger>();
            var items = new KD_ShopItem[3];
            for (int i = 0; i < 3; i++)
            {
                var itemGo = Host(KD_ProductDefinitions.SceneName(ProductKeys[i]), KD_DogDefs.SystemsPath);
                items[i] = Add<KD_ShopItem>(itemGo, Networking.SyncType.None);
                KD_ProductDefinitions.Apply(items[i], ProductKeys[i]); items[i].ledger = ledger; items[i].menuOnly = true;
                items[i].displayNameKey = "activities.home." + i;
                items[i].displayName = i == 0 ? "集合モード" : i == 1 ? "みんなで遊ぶセット" : "犬目線カメラ";
                KD_ProductsEditor.Copy(items[i]);
            }
            KD_ProductsEditor.BindCatalog(Find<KD_ShopCatalog>(), ledger);
            activities.live = live; activities.pack = pack; activities.formationItem = items[0]; activities.playItem = items[1]; activities.photos = photos;
            activities.balls = pack.dogFetch.Select(f => f.ballController).ToArray(); live.activities = activities;
            for (int i = 0; i < pack.dogFetch.Length; i++)
            {
                pack.dogFetch[i].activities = activities; pack.dogWander[i].activities = activities;
                // Prefab-to-scene references are assigned from scene scripts at Start, never stored inside the ball prefab.
                KD_ProductsEditor.Copy(pack.dogFetch[i]); KD_ProductsEditor.Copy(pack.dogWander[i]);
            }
            string[] dogKeys = KD_DogDefs.All.Select(d => "activities.dog." + d.key).ToArray();
            var translations = KD_LocalizationEditor.Load();
            foreach (var lang in translations.languages)
            {
                var entries = lang.texts.ToList();
                for (int i = 0; i < dogKeys.Length; i++)
                {
                    if (entries.Any(e => e.key == dogKeys[i])) continue;
                    // 追加言語には空 (未訳 = 代替言語で表示) を入れる。英字キー名を訳文として書くと未訳が見えなくなる (IMP-INF-010)
                    entries.Add(new KD_LocalizationEditor.TextEntry { key = dogKeys[i], text = lang.code == "ja" ? pouch.toyNames[i] : lang.code == "en" ? KD_DogDefs.All[i].key : "" });
                }
                lang.texts = entries.ToArray();
            }
            string translationJson = JsonUtility.ToJson(translations, true);
            // Match the existing two-space catalog format instead of rewriting every line's indentation.
            translationJson = System.Text.RegularExpressions.Regex.Replace(translationJson, @"(?m)^( +)", m => new string(' ', m.Length / 2));
            File.WriteAllText(KD_MenuFinishEditor.CatalogPath, translationJson + "\n", new UTF8Encoding(false));
            KD_LocalizationEditor.Configure(language, translations, log);
            KD_ShopSetupEditor.AddGlyphs(KD_LocalizationEditor.BundledCharacters(translations), log);
            // P2 (2026-09-16): あそぶは 7 枚目のタブ。面は Treat System/2 が作る Page_Play。旧「設定の面 + 上段の [あそぶ]」は消す
            if (pouch.tabPages == null || pouch.tabPages.Length <= KD_TreatPouch.TabPlay || pouch.tabPages[KD_TreatPouch.TabPlay] == null)
                throw new Exception("Page_Play missing: run Tools/KominkaHillside/Menu/7 Build Menu first (7 tabs)");
            var page = pouch.tabPages[KD_TreatPouch.TabPlay];
            var legacyPage = pouch.settingsPage.transform.Find("ActivitiesPage");
            if (legacyPage != null) { UnityEngine.Object.DestroyImmediate(legacyPage.gameObject); log.AppendLine("removed legacy Page_Settings/ActivitiesPage"); }
            pouch.extraSettingsPages = (pouch.extraSettingsPages ?? new GameObject[0]).Where(p => p != null).ToArray();
            var legacyEntry = pouch.panelRoot.transform.Find("ActivitiesButton");
            if (legacyEntry != null) { UnityEngine.Object.DestroyImmediate(legacyEntry.gameObject); log.AppendLine("removed legacy ActivitiesButton"); }
            menu.page = page; menu.pouch = pouch; menu.activities = activities; menu.pov = pov; menu.language = language; menu.items = items; menu.dogKeys = dogKeys;
            pouch.activitiesMenu = menu;
            var font = pouch.sectionTitleLabel.font;
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(menu);
            var color = pouch.tabOffColor;
            GameObject Button(Transform parent, string name, Vector2 pos, Vector2 size, string text, string ev)
            {
                var button = KD_ShopSetupEditor.EnsureButton(parent, name, pos, size, color, font, text, udon, ev, log);
                var label = button.GetComponentInChildren<TextMeshProUGUI>(); label.fontSize = 22; label.enableWordWrapping = true;
                return button;
            }
            // P2 の面: 見出し 1 行 → 群「犬目線カメラ」+ 3 行 → 群「ばにまるトレーナー」+ 案内 → 3 行 → 状態 → [戻る] [購入画面をひらく]
            // 行は 3 列 × 2 段 (330×88)。実行時に KD_ActivitiesMenu.Draw がホーム / ほかの面で Y を動かす (ホーム +72 / -140、ほか +80 / -20)
            // 2026-09-17: タブの下端 201 に合わせて全体を 12px 下げた (見出し 160 / 群 120 / 行 60 / 群 2 / 案内 -60 / 行 -150)
            menu.heading = Text(page.transform, "Heading", new Vector2(0, 160), new Vector2(1040, 36), font, 20);
            menu.groupPovLabel = Text(page.transform, "GroupPov", new Vector2(0, 120), new Vector2(1040, 30), font, 20);
            menu.groupPovLabel.alignment = TextAlignmentOptions.Left; menu.groupPovLabel.enableWordWrapping = false;
            menu.groupTrainerLabel = Text(page.transform, "GroupTrainer", new Vector2(0, 2), new Vector2(1040, 30), font, 20);
            menu.groupTrainerLabel.alignment = TextAlignmentOptions.Left; menu.groupTrainerLabel.enableWordWrapping = false;
            menu.introLabel = Text(page.transform, "Intro", new Vector2(0, -60), new Vector2(1040, 84), font, 17);
            menu.introLabel.alignment = TextAlignmentOptions.TopLeft;
            menu.rows = new Button[6]; menu.rowLabels = new TMP_Text[6];
            for (int i = 0; i < 6; i++)
            {
                var row = Button(page.transform, "ActivityRow" + i, new Vector2((i % 3 - 1) * 355, i < 3 ? 60 : -150), new Vector2(330, 88), "", "_Row" + i);
                menu.rows[i] = row.GetComponent<Button>(); menu.rowLabels[i] = row.GetComponentInChildren<TextMeshProUGUI>();
                menu.rowLabels[i].richText = true;
                ((RectTransform)menu.rowLabels[i].transform).sizeDelta = new Vector2(310, 80);
            }
            menu.status = Text(page.transform, "Status", new Vector2(0, -225), new Vector2(1040, 62), font, 20);
            var back = Button(page.transform, "Back", new Vector2(-430, -285), new Vector2(200, 48), "戻る", "_Back");
            menu.back = back; menu.backLabel = back.GetComponentInChildren<TextMeshProUGUI>();
            var action = Button(page.transform, "Action", new Vector2(280, -285), new Vector2(500, 48), "", "_Action");
            menu.action = action.GetComponent<Button>(); menu.actionLabel = action.GetComponentInChildren<TextMeshProUGUI>();
            menu.placementMarkers = new GameObject[6];
            var previewMat = Material("M_KD_FormationPreview", "Unlit/Color", new Color(.25f, .8f, .85f));
            var previewHost = Host("KD_FormationPreview", KD_DogDefs.PlacesPath);
            for (int i = 0; i < 6; i++)
            {
                var t = previewHost.transform.Find("Slot" + i);
                var marker = t != null ? t.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                if (t == null) { marker.name = "Slot" + i; marker.transform.SetParent(previewHost.transform, false); marker.transform.localScale = new Vector3(.65f, .005f, .65f); UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>()); }
                marker.GetComponent<Renderer>().sharedMaterial = previewMat; marker.SetActive(false); menu.placementMarkers[i] = marker;
            }
            BuildPov(pov, items[2], language, photos, dogKeys, font);
            page.SetActive(false);
            foreach (var c in new UdonSharpBehaviour[] { activities, live, menu, pov, pouch, language }) KD_ProductsEditor.Copy(c);
            EditorSceneManager.MarkSceneDirty(live.gameObject.scene);
            File.WriteAllText("Logs/activities-build.txt", log.AppendLine("BUILD complete (not saved)").ToString());
        }
        static TMP_Text Text(Transform parent, string name, Vector2 pos, Vector2 size, TMP_FontAsset font, float points)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var text = go.GetComponent<TextMeshProUGUI>(); if (text == null) text = go.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = points; text.color = Color.white; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; text.enableWordWrapping = true;
            return text;
        }
        static Material Material(string name, string shader, Color color)
        {
            var path = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(material, path); }
            material.color = color; EditorUtility.SetDirty(material); return material;
        }
        static RenderTexture Texture(string name, int size)
        {
            var path = "Assets/KominkaHillside/Imported/KominkaDogRun/Data/" + name + ".renderTexture";
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (rt == null) { rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32); rt.name = name; rt.antiAliasing = 1; rt.useMipMap = false; AssetDatabase.CreateAsset(rt, path); }
            return rt;
        }
        static void BuildPov(KD_DogPovCamera pov, KD_ShopItem item, KD_Language language, KD_PhotoDirector photos, string[] keys, TMP_FontAsset font)
        {
            var cameraGo = Host("KD_DogPovCamera", KD_DogDefs.SystemsPath);
            var camera = cameraGo.GetComponent<Camera>(); if (camera == null) camera = cameraGo.AddComponent<Camera>();
            camera.enabled = false; camera.nearClipPlane = .01f; camera.farClipPlane = 60f; camera.fieldOfView = 80; // IMP-ENV-021: Near は視点と同じ 0.01
            camera.allowHDR = false; camera.allowMSAA = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
            // BUG-CAM-006: 撮影カメラと同じ許可リスト。自分のアバターは MirrorReflection(18) で描く (PlayerLocal(10) は顔が欠ける)。
            // 外すのは UI(5) = モニター自身と、InternalUI(19) だけ。
            camera.cullingMask = photos.photoCamera.cullingMask & ~(1 << 5) & ~(1 << 19);
            var monitor = Host("KD_DogPovMonitor", KD_DogDefs.PropsPath);
            if (monitor.GetComponent<Rigidbody>() == null)
            {
                var rb = monitor.AddComponent<Rigidbody>(); rb.useGravity = false; rb.isKinematic = true;
                monitor.AddComponent<BoxCollider>().size = new Vector3(.48f, .42f, .03f);
            }
            var pickup = monitor.GetComponent<VRC.SDK3.Components.VRCPickup>(); if (pickup == null) pickup = monitor.AddComponent<VRC.SDK3.Components.VRCPickup>();
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes; pickup.allowManipulationWhenEquipped = true;
            var screen = monitor.transform.Find("Screen");
            if (screen == null)
            { var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Screen"; screen = go.transform; screen.SetParent(monitor.transform, false); screen.localScale = new Vector3(.4f, .4f, 1); UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); }
            screen.gameObject.layer = 5;
            var frame = monitor.transform.Find("Frame");
            if (frame == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Frame"; frame = go.transform;
                frame.SetParent(monitor.transform, false); UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            }
            // IMP-CAM-008: 上にも帯を取り (0.54 → 0.60)、[×] を画面と Pickup の Collider (0.48×0.42) の外へ置く。
            frame.localScale = new Vector3(.46f, .60f, 1); frame.localPosition = new Vector3(0, -.005f, .005f);
            frame.gameObject.layer = 5;
            frame.GetComponent<Renderer>().sharedMaterial = Material("M_KD_DogPovFrame", "Unlit/Color", new Color(.055f, .065f, .07f));
            var mat = Material("M_KD_DogPovScreen", "Unlit/Texture", Color.white);
            pov.desktopTexture = Texture("RT_KD_DogPov_PC", 512); pov.mobileTexture = Texture("RT_KD_DogPov_Mobile", 256);
            mat.mainTexture = pov.desktopTexture; screen.GetComponent<Renderer>().sharedMaterial = mat; camera.targetTexture = pov.desktopTexture;
            var caption = monitor.transform.Find("Caption");
            if (caption == null)
            {
                var go = new GameObject("Caption", typeof(RectTransform), typeof(Canvas)); go.layer = 5; go.transform.SetParent(monitor.transform, false); caption = go.transform;
                go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; caption.localScale = Vector3.one * .001f;
                caption.localPosition = new Vector3(0, -.24f, -.02f);
            }
            pov.dogLabel = Text(caption, "Dog", Vector2.zero, new Vector2(440, 60), font, 32);
            BuildPovClose(pov, monitor, font);
            pov.item = item; pov.language = language; pov.povCamera = camera; pov.heads = photos.dogHeadAnchors.ToArray(); pov.roots = photos.dogRoots.ToArray();
            pov.headCorrections = pov.heads.Select((head, i) => Quaternion.Inverse(head.rotation) * pov.roots[i].rotation).ToArray();
            pov.forwardOffsets = pov.roots.Select((root, i) => Mathf.Clamp(Find<KD_DogPack>().dogRadius[i] * .65f, .12f, .35f)).ToArray();
            pov.screen = screen.GetComponent<Renderer>(); pov.monitor = monitor; pov.pickup = pickup; pov.dogNameKeys = keys; monitor.SetActive(false);
        }
        // IMP-CAM-008: モニター右上の [×]。見える部品 (layer 5 = UI、両カメラの許可リスト外) と
        // 押す部品 (layer 17 = Walkthrough。UI/UiMenu は VRChat メニューを閉じている間ポインタの対象外) を分ける。
        // 17 は撮影カメラの許可リストに入っているので、押す部品には見える Graphic を置かない (透明 Image だけ)。
        public const string CloseVisualName = "CloseVisual", CloseInputName = "CloseInput";
        static readonly Vector3 ClosePos = new Vector3(.19f, .26f, 0f);
        static readonly Vector2 CloseSize = new Vector2(50, 50);
        static void BuildPovClose(KD_DogPovCamera pov, GameObject monitor, TMP_FontAsset font)
        {
            var visual = WorldCanvas(monitor.transform, CloseVisualName, 5, ClosePos + new Vector3(0, 0, -.002f));
            var bg = KD_ShopSetupEditor.EnsureUiChild(visual.transform, "Bg", Vector2.zero, CloseSize);
            var bgImage = bg.GetComponent<Image>(); if (bgImage == null) bgImage = bg.AddComponent<Image>();
            bgImage.color = KD_UiSpriteEditor.ActionClose; bgImage.raycastTarget = false;
            KD_UiSpriteEditor.ApplyRoundSkin(bgImage, KD_UiSpriteEditor.LoadRound());
            KD_ShopSetupEditor.AddGlyphs("×", log);
            var label = Text(visual.transform, "Label", Vector2.zero, CloseSize, font, 34); label.text = "×"; label.enableWordWrapping = false;
            var input = WorldCanvas(monitor.transform, CloseInputName, KD_HandMenuCollisionEditor.InputLayer, ClosePos + new Vector3(0, 0, -.008f));
            if (input.GetComponent<GraphicRaycaster>() == null) input.AddComponent<GraphicRaycaster>();
            if (input.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null) input.AddComponent<VRC.SDK3.Components.VRCUiShape>();
            var hit = KD_ShopSetupEditor.EnsureUiChild(input.transform, "Hit", Vector2.zero, CloseSize);
            var hitImage = hit.GetComponent<Image>(); if (hitImage == null) hitImage = hit.AddComponent<Image>();
            hitImage.color = Color.clear; hitImage.sprite = null; hitImage.raycastTarget = true;
            var button = hit.GetComponent<Button>(); if (button == null) button = hit.AddComponent<Button>();
            button.targetGraphic = hitImage; button.transition = Selectable.Transition.None;
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, i);
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pov);
            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(button.onClick, new UnityEngine.Events.UnityAction<string>(udon.SendCustomEvent), "_Close");
            pov.closeButton = button;
        }
        static GameObject WorldCanvas(Transform parent, string name, int layer, Vector3 localPos)
        {
            var t = parent.Find(name);
            var go = t != null ? t.gameObject : new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false); go.layer = layer;
            var canvas = go.GetComponent<Canvas>(); if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform; rect.sizeDelta = CloseSize; rect.localScale = Vector3.one * .001f;
            rect.localPosition = localPos; rect.localRotation = Quaternion.identity;
            return go;
        }
        [MenuItem("Tools/KominkaHillside/Menu/23 Verify Activities")]
        public static void Verify()
        {
            var sb = new StringBuilder(); int fail = 0;
            void Check(bool ok, string what) { sb.AppendLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fail++; }
            var a = Find<KD_GroupActivities>(); var m = Find<KD_ActivitiesMenu>(); var p = Find<KD_DogPovCamera>();
            Check(a.gameObject == a.live.gameObject && a.live.activities == a, "one ownership domain for live and hosting");
            // IMP-NET-007: 「開催をおわる」の管理者判定 (UI と受信側) は live → access → KD_SleepBoard.CanControl に届くこと
            Check(a.live.access != null && a.live.access.sleepBoard != null, "end-activity manager check reaches KD_SleepBoard.CanControl (owner first, Master while absent)");
            Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(a).SyncMethod == Networking.SyncType.Manual, "actual hosting Manual sync");
            Check(a.balls.Length == 6 && a.pack.dogFetch.All(f => f.activities == a) && a.pack.dogWander.All(w => w.activities == a), "six dogs connected");
            // 2026-09-15: 集合・みんなで遊ぶはトレーナー (CE Subscription。同じ Product / Listing)、犬目線は Point のまま
            Check(m.items.Length == 3 && m.items.All(i => i.IsConfigured())
                && m.items[0].IsCE() && m.items[1].IsCE() && m.items[0].ceProduct != null && m.items[0].ceProduct == m.items[1].ceProduct
                && m.items[0].ceListingId == m.items[1].ceListingId && !m.items[2].IsCE() && m.items[2].price == 60, "trainer CE pair shares one product; dog pov stays 60pt (DES-CAM-001, 2026-09-16)");
            // P2: あそぶは 7 枚目のタブ。設定の面と上段の [あそぶ] は無い
            Check(m.pouch.tabPages.Length == KD_TreatPouch.TabCount && m.pouch.tabPages[KD_TreatPouch.TabPlay] == m.page && m.page.transform.parent == m.pouch.panelRoot.transform, "activities page is the 7th tab page (P2)");
            Check(!m.pouch.extraSettingsPages.Contains(m.page) && m.pouch.settingsPage.transform.Find("ActivitiesPage") == null && m.pouch.panelRoot.transform.Find("ActivitiesButton") == null, "legacy settings page and header entry removed (P2)");
            Check(m.pouch.activitiesMenu == m && m.groupPovLabel != null && m.groupTrainerLabel != null && m.introLabel != null && m.back != null, "group labels, intro and back wired (P2)");
            Check(m.rows.Length == 6 && m.rowLabels.All(l => l != null), "menu connected");
            foreach (var button in m.page.GetComponentsInChildren<Button>(true)) Check(button.onClick.GetPersistentEventCount() == 1 && button.onClick.GetPersistentTarget(0) != null, "button " + button.name);
            Check(p.heads.Length == 6 && p.heads.All(h => h != null) && p.headCorrections.Length == 6, "six camera targets");
            Check(p.desktopTexture.width == 512 && p.mobileTexture.width == 256 && !p.povCamera.enabled && !p.monitor.activeSelf, "camera defaults off and both platform resolutions");
            Check(p.povCamera.nearClipPlane <= .01f + 1e-4f && Mathf.Approximately(p.povCamera.farClipPlane, 60f), "pov camera near 0.01 / far 60 (IMP-ENV-021, actual " + p.povCamera.nearClipPlane + " / " + p.povCamera.farClipPlane + ")");
            Check(p.monitor.GetComponent<VRC.SDK3.Components.VRCObjectSync>() == null && p.pickup != null, "local pickup monitor");
            Check((p.povCamera.cullingMask & (1 << p.screen.gameObject.layer)) == 0, "monitor excluded from its camera");
            // BUG-CAM-006: 自分は MirrorReflection(18) で映す。UI(5) / InternalUI(19) は外したまま
            Check((p.povCamera.cullingMask & (1 << 18)) != 0 && (p.povCamera.cullingMask & (1 << 5)) == 0 && (p.povCamera.cullingMask & (1 << 19)) == 0, "pov camera renders local avatar (18) and excludes UI (5, 19)");
            // IMP-CAM-008: モニターの見える部品 (非アクティブ込み) は犬目線・撮影 (縁側・寝室) の両カメラに写らない。
            // 押す部品 (17) は透明 Image だけで Renderer を持たない。[×] は _Close を 1 本だけ指す
            var cameras = KD_ProductsEditor.SceneComponents<KD_PhotoCapture>().Select(c => c.photoCamera).Where(c => c != null).Append(p.povCamera).Distinct().ToArray();
            Check(cameras.Length >= 2, "photo and pov cameras found for monitor exclusion (" + cameras.Length + ")");
            var input = p.monitor.transform.Find(CloseInputName);
            Check(input != null && input.gameObject.layer == KD_HandMenuCollisionEditor.InputLayer && input.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null && input.GetComponent<GraphicRaycaster>() != null, "close input canvas on Walkthrough with UiShape");
            if (input != null)
            {
                Check(input.GetComponentsInChildren<Renderer>(true).Length == 0 && input.GetComponentsInChildren<Graphic>(true).All(g => g.color.a == 0f), "close input has no visible part");
                Check(input.GetComponentsInChildren<Graphic>(true).Any(g => g.raycastTarget), "close input is a raycast target");
            }
            foreach (var r in p.monitor.GetComponentsInChildren<Renderer>(true)) foreach (var c in cameras) Check((c.cullingMask & (1 << r.gameObject.layer)) == 0, "monitor renderer hidden from " + c.name + ": " + r.name);
            foreach (var g in p.monitor.GetComponentsInChildren<Graphic>(true))
            {
                if (input != null && g.transform.IsChildOf(input)) continue;
                foreach (var c in cameras) Check((c.cullingMask & (1 << g.gameObject.layer)) == 0, "monitor graphic hidden from " + c.name + ": " + g.name);
            }
            var povUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            bool oneListener = p.closeButton != null && p.closeButton.onClick.GetPersistentEventCount() == 1;
            var closeCall = oneListener ? new SerializedObject(p.closeButton).FindProperty("m_OnClick.m_PersistentCalls.m_Calls").GetArrayElementAtIndex(0) : null;
            string closeEvent = closeCall == null ? "" : closeCall.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
            bool closeEnabled = closeCall != null && closeCall.FindPropertyRelative("m_CallState").intValue == (int)UnityEngine.Events.UnityEventCallState.RuntimeOnly;
            Check(oneListener && p.closeButton.transform.IsChildOf(p.monitor.transform)
                && p.closeButton.onClick.GetPersistentTarget(0) == povUdon && p.closeButton.onClick.GetPersistentMethodName(0) == "SendCustomEvent" && closeEvent == "_Close" && closeEnabled,
                "monitor close button calls pov SendCustomEvent(_Close) once, runtime-enabled (" + closeEvent + ")");
            var closeVisual = p.monitor.transform.Find(CloseVisualName);
            Check(closeVisual != null && closeVisual.gameObject.layer == 5 && closeVisual.GetComponentInChildren<TMP_Text>(true) != null && closeVisual.GetComponentInChildren<TMP_Text>(true).text == "×", "close visual on UI layer shows x");
            var pickupBox = p.monitor.GetComponent<BoxCollider>();
            Check(pickupBox != null && input != null && Mathf.Abs(input.localPosition.y) - CloseSize.y * .0005f > pickupBox.center.y + pickupBox.size.y * .5f, "close button sits outside the pickup collider");
            // 隊形は体格ベース (2026-09-13): 全ペアが「幅の和 × 比率」を下回らず (輪は弦なので 0.9 掛け)、足跡が部屋に収まる
            var radius = a.pack.dogRadius;
            Check(radius != null && radius.Length == 6 && radius.All(r => r > .05f), "pack radii feed the formation geometry");
            for (int shape = 0; shape < 6; shape++)
            {
                float ratio = shape == 2 ? a.huddleRatio : a.gapRatio;
                for (int i = 0; i < 6; i++)
                {
                    Vector3 o = a.Offset(shape, i);
                    Check(Mathf.Abs(o.x) <= 2f && Mathf.Abs(o.z) <= 2f, "formation footprint fits a room " + shape + "/" + i + " " + o);
                    for (int j = i + 1; j < 6; j++)
                        Check(Vector3.Distance(o, a.Offset(shape, j)) >= (radius[i] + radius[j]) * a.widthRatio * ratio * .9f, "formation spacing " + shape + "/" + i + "/" + j);
                }
            }
            KD_LocalizationEditor.ValidateScene(Find<KD_Language>(), KD_LocalizationEditor.Load());
            Check(m.placementMarkers.Length == 6 && m.placementMarkers.All(g => g.GetComponent<Collider>() == null), "six noncolliding placement markers");
            sb.AppendLine("RESULT " + (fail == 0 ? "PASS" : "FAIL") + " failures=" + fail);
            File.WriteAllText("Logs/activities-verify.txt", sb.ToString());
            if (fail > 0) throw new Exception("Activity verification failed: " + fail);
        }

        [MenuItem("Tools/KominkaHillside/Menu/23a Preview Activities UI")]
        public static void Preview()
        {
            var p = Find<KD_TreatPouch>(); var menu = Find<KD_ActivitiesMenu>();
            var scene = EditorSceneManager.NewPreviewScene(); var previous = RenderTexture.active;
            var rt = new RenderTexture(1140, 700, 24); var image = new Texture2D(1140, 700, TextureFormat.RGB24, false);
            var fit = new StringBuilder(); int overflow = 0;
            try
            {
                foreach (var language in new[] { "ja", "en" }) for (int view = 0; view < 5; view++)
                {
                    string L(string key) { return p.language._TextForLanguage(key, language); }
                    var clone = UnityEngine.Object.Instantiate(p.panelRoot); clone.SetActive(false);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone, scene);
                    foreach (var b in clone.GetComponentsInChildren<UdonSharpBehaviour>(true)) UnityEngine.Object.DestroyImmediate(b);
                    foreach (var b in clone.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true)) UnityEngine.Object.DestroyImmediate(b);
                    foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                    clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.transform.localScale = Vector3.one * .001f;
                    clone.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                    // P2: view 0 = ホーム (未加入) / 1 隊形 / 2 犬 2 匹 / 3 犬目線 / 4 ホーム (加入中)
                    bool home = view == 0 || view == 4; bool trainer = view == 4;
                    foreach (var page in p.tabPages) clone.transform.Find(AnimationUtility.CalculateTransformPath(page.transform, p.panelRoot.transform)).gameObject.SetActive(false);
                    clone.transform.Find(AnimationUtility.CalculateTransformPath(p.settingsPage.transform, p.panelRoot.transform)).gameObject.SetActive(false);
                    var root = clone.transform.Find(AnimationUtility.CalculateTransformPath(p.tabPages[KD_TreatPouch.TabPlay].transform, p.panelRoot.transform)); root.gameObject.SetActive(true);
                    string[] tabKeys = { "KD_TreatPouch.WordTabTreat", "KD_TreatPouch.WordTabToy", "KD_TreatPouch.WordTabPetFx", "KD_TreatPouch.WordTabSleep", "KD_TreatPouch.WordLiveVolume", "KD_TreatPouch.WordTabAcc", "KD_TreatPouch.WordTabPlay" }; // ライブのタブ語は WordLiveVolume に配線されている (bindings)
                    for (int t = 0; t < p.tabImages.Length; t++)
                    {
                        var tab = clone.transform.Find(AnimationUtility.CalculateTransformPath(p.tabImages[t].transform, p.panelRoot.transform));
                        tab.GetComponent<Image>().color = t == KD_TreatPouch.TabPlay ? p.tabOnColor : p.tabOffColor;
                        var underline = clone.transform.Find(AnimationUtility.CalculateTransformPath(p.tabUnderlines[t].transform, p.panelRoot.transform));
                        underline.gameObject.SetActive(t == KD_TreatPouch.TabPlay);
                        tab.Find("Label").GetComponent<TMP_Text>().text = L(tabKeys[t]);
                    }
                    clone.transform.Find("SectionTitle").GetComponent<TMP_Text>().text = L("activities.title");
                    clone.transform.Find("Balance").GetComponent<TMP_Text>().text = "100 pt";
                    root.Find("Heading").GetComponent<TMP_Text>().text = L(view == 1 ? "activities.formationHelp" : view == 2 ? "activities.pairHelp" : view == 3 ? "activities.povHelp" : trainer ? "activities.helpTrainer" : "activities.help");
                    root.Find("GroupPov").gameObject.SetActive(home); root.Find("GroupPov").GetComponent<TMP_Text>().text = L("activities.group.pov");
                    root.Find("GroupTrainer").gameObject.SetActive(home); root.Find("GroupTrainer").GetComponent<TMP_Text>().text = L(trainer ? "activities.group.trainer.on" : "activities.group.trainer.off");
                    root.Find("Intro").gameObject.SetActive(home); root.Find("Intro").GetComponent<TMP_Text>().text = L(trainer ? "activities.trainerActive" : "activities.trainerIntro");
                    root.Find("Status").GetComponent<TMP_Text>().text = L(home && !trainer ? "activities.storeNote" : "activities.rejected");
                    root.Find("Back/Label").GetComponent<TMP_Text>().text = L("activities.back"); root.Find("Back").gameObject.SetActive(!home);
                    root.Find("Action/Label").GetComponent<TMP_Text>().text = L(view == 1 ? "activities.gather" : view == 2 ? "activities.startPair" : view == 3 ? "activities.follow" : "activities.openStore");
                    root.Find("Action").gameObject.SetActive(!home || !trainer);
                    string[] homeKeys = { "activities.home.2", "activities.home.4", "activities.home.5", "activities.home.0", "activities.home.1", "activities.home.3" };
                    string[] subKeys = { "activities.povDesc", "activities.recallDesc", "activities.closeDesc", "activities.perk.formation", "activities.perk.hosting", "activities.endDesc" };
                    for (int i = 0; i < 6; i++)
                    {
                        var row = (RectTransform)root.Find("ActivityRow" + i);
                        row.anchoredPosition = new Vector2((i % 3 - 1) * 355, home ? (i < 3 ? 60 : -150) : (i < 3 ? 68 : -32));
                        string sub = L(subKeys[i]);
                        if (i == 0) sub = trainer ? L("activities.povOwnedDesc") : "60 pt · " + sub;
                        if ((i == 3 || i == 4) && !trainer) sub = L("activities.needTrainer");
                        row.Find("Label").GetComponent<TMP_Text>().text = home ? L(homeKeys[i]) + "\n<size=17>" + sub + "</size>" : view == 1 ? L("activities.shape." + i) : L(menu.dogKeys[i]);
                    }
                    clone.SetActive(true); Canvas.ForceUpdateCanvases();
                    foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>())
                    {
                        label.isOrthographic = true;
                        var size = label.GetPreferredValues(label.text, label.rectTransform.rect.width, Mathf.Infinity);
                        if (size.y > label.rectTransform.rect.height + 2) { overflow++; fit.AppendLine(language + "/" + view + " " + label.name + " " + size + " > " + label.rectTransform.rect.size); }
                        label.ForceMeshUpdate(true);
                    }
                    var go = new GameObject("ActivityPreviewCamera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                    go.transform.position = new Vector3(0, 0, -2);
                    var camera = go.AddComponent<Camera>(); camera.scene = scene; camera.orthographic = true; camera.orthographicSize = .35f; camera.aspect = 1140f / 700;
                    camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f, .1f, .1f); camera.targetTexture = rt;
                    camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1140, 700), 0, 0); image.Apply();
                    File.WriteAllBytes("Logs/activities-" + language + "-" + view + ".png", image.EncodeToPNG());
                    RenderTexture.active = previous; camera.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(clone); UnityEngine.Object.DestroyImmediate(go);
                }
                fit.AppendLine("OVERFLOW=" + overflow); File.WriteAllText("Logs/activities-ui-fit.txt", fit.ToString());
            }
            finally
            {
                RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
