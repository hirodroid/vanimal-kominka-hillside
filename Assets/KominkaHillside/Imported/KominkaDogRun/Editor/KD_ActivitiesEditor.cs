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
    /// <summary>あそぶ (Page_Play) の構築と検査。
    ///
    /// 裏山改変 (KominkaHillside 段階 0-b、計画書 D-5): **集合のみ。** 古民家の犬目線カメラ (KD_DogPovCamera)・
    /// みんなで遊ぶセット・KD_ShopItem (CE 商品) は作らない。トレーナーの権利は KH_TrainerPass、集合の権限者は
    /// KH_LeashManager と同じ GameObject の KD_GroupActivities (Walking/3 Build が作る)。その配線は現行シーンの値を壊さない
    /// (null の参照だけ埋める)。面の座標は古民家を踏襲し、犬目線の群が抜けたぶんトレーナーの群を上の段へ置く。</summary>
    public static class KD_ActivitiesEditor
    {
        // 裏山改変: KD_DogPovCamera を作らない
        static readonly string[] Programs = { "KD_GroupActivities", "KD_ActivitiesMenu" };
        // 裏山改変: シーンガードは裏山の作業シーン
        const string HillsideScene = KD_Hillside.ScenePath;
        const string DogsRoot = KD_Hillside.DogsRootName;
        const string MarkersName = "KH_GatherMarkers";
        const string MarkerMaterialPath = "Assets/KominkaHillside/Data/Walking/KH_GatherMarker.mat";
        const int MarkerLayer = 14;
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
        static Transform DogsTransform()
        {
            var dogs = KD_SceneObjects.Find(DogsRoot, out int count);
            if (dogs == null || count != 1) throw new Exception(DogsRoot + " missing or duplicated (" + count + "): run Walking/3 Build first");
            return dogs.transform;
        }
        static GameObject Host(string name, Transform parent)
        {
            var t = parent.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
            return go;
        }

        [MenuItem("Tools/KominkaHillside/Menu/9 Setup Activities")]
        public static void Build()
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != HillsideScene) throw new Exception("Target Edit scene required: " + HillsideScene);
            log = new StringBuilder();
            var dogs = DogsTransform();
            var pouch = Find<KD_TreatPouch>(); var language = Find<KD_Language>();
            var activities = dogs.GetComponentsInChildren<KD_GroupActivities>(true);
            if (activities.Length != 1) throw new Exception("KD_GroupActivities under " + DogsRoot + ": " + activities.Length + " (run Walking/3 Build)");
            var act = activities[0];
            var pass = dogs.GetComponentsInChildren<KominkaHillside.KH_TrainerPass>(true).SingleOrDefault();
            var manager = dogs.GetComponentsInChildren<KominkaHillside.KH_LeashManager>(true).SingleOrDefault();
            if (pass == null || manager == null) throw new Exception("KH_TrainerPass / KH_LeashManager missing under " + DogsRoot + " (run Walking/3 Build)");

            // KD_GroupActivities は Walking/3 Build の持ち物。**既に配線済みの値は触らない** (null の口だけ埋める)
            bool actChanged = false;
            if (act.hillsideLeashes == null) { act.hillsideLeashes = manager; actChanged = true; log.AppendLine("wired activities.hillsideLeashes"); }
            if (act.hillsidePass == null) { act.hillsidePass = pass; actChanged = true; log.AppendLine("wired activities.hillsidePass"); }
            if (act.pack == null) { act.pack = KD_ProductsEditor.SceneComponents<KD_DogPack>().SingleOrDefault(); if (act.pack != null) { actChanged = true; log.AppendLine("wired activities.pack"); } }
            if (UdonSharpEditorUtility.GetBackingUdonBehaviour(act).SyncMethod != Networking.SyncType.Manual) log.AppendLine("WARN activities backing is not Manual (Walking/3 Build owns it; not changed here)");

            // 集合の要求と隊形の印を描く板 (ローカル専用、同期 0 本)
            var host = Host("KD_Activities", dogs);
            var menu = Add<KD_ActivitiesMenu>(host, Networking.SyncType.None);

            if (pouch.tabPages == null || pouch.tabPages.Length <= KD_TreatPouch.TabPlay || pouch.tabPages[KD_TreatPouch.TabPlay] == null)
                throw new Exception("Page_Play missing: run Tools/KominkaHillside/Menu/7 Build Menu first (7 tabs)");
            var page = pouch.tabPages[KD_TreatPouch.TabPlay];
            if (pouch.settingsPage != null)
            {
                var legacyPage = pouch.settingsPage.transform.Find("ActivitiesPage");
                if (legacyPage != null) { UnityEngine.Object.DestroyImmediate(legacyPage.gameObject); log.AppendLine("removed legacy Page_Settings/ActivitiesPage"); }
            }
            pouch.extraSettingsPages = (pouch.extraSettingsPages ?? new GameObject[0]).Where(p => p != null).ToArray();
            var legacyEntry = pouch.panelRoot.transform.Find("ActivitiesButton");
            if (legacyEntry != null) { UnityEngine.Object.DestroyImmediate(legacyEntry.gameObject); log.AppendLine("removed legacy ActivitiesButton"); }
            // 裏山改変: 犬目線の群は作らない。古民家の Build で作られた残骸があれば消す
            var legacyPov = page.transform.Find("GroupPov");
            if (legacyPov != null) { UnityEngine.Object.DestroyImmediate(legacyPov.gameObject); log.AppendLine("removed GroupPov (hillside: gathering only)"); }

            menu.page = page; menu.pouch = pouch; menu.activities = act; menu.hillsidePass = pass; menu.language = language;
            menu.items = null; menu.groundMask = act.groundMask;
            // 裏山改変: フリスビーの取り方の行 (DES-UI-002)。犬ごとの KH_FrisbeeCatch は Walking/3 Build が KD_GroupActivities へ配線したものを写す (null を含む 6 要素。act 側は触らない)
            menu.frisbeeCatchers = new KominkaHillside.KH_FrisbeeCatch[6];
            for (int i = 0; i < 6; i++) menu.frisbeeCatchers[i] = act.frisbeeCatchers != null && i < act.frisbeeCatchers.Length ? act.frisbeeCatchers[i] : null;
            log.AppendLine("frisbee catchers: " + menu.frisbeeCatchers.Count(c => c != null) + " of 6");
            pouch.activitiesMenu = menu;

            // 文言は UITranslations.json が正本 (activities.hillside.* を含む)。KD_Language の配列を作り直し、字形を焼く
            var translations = KD_LocalizationEditor.Load();
            KD_LocalizationEditor.Configure(language, translations, log);
            KD_ShopSetupEditor.AddGlyphs(KD_LocalizationEditor.BundledCharacters(translations), log);

            var font = pouch.sectionTitleLabel.font;
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(menu);
            var color = pouch.tabOffColor;
            GameObject Button(Transform parent, string name, Vector2 pos, Vector2 size, string text, string ev)
            {
                var button = KD_ShopSetupEditor.EnsureButton(parent, name, pos, size, color, font, text, udon, ev, log);
                var label = button.GetComponentInChildren<TextMeshProUGUI>(); label.fontSize = 22; label.enableWordWrapping = true;
                return button;
            }
            // 面 (古民家の座標を踏襲): 見出し 160 → 群「ばにまるトレーナー」120 → 一覧の行 60 (3 列の左 2 つ) → 案内 -60 → 状態 -225 → [戻る] [購入画面をひらく]/[ここに集合]
            // 隊形の面は行を 3 列 × 2 段 (+68 / -32) へ動かす (KD_ActivitiesMenu.Draw)
            menu.heading = Text(page.transform, "Heading", new Vector2(0, 160), new Vector2(1040, 36), font, 20);
            menu.groupTrainerLabel = Text(page.transform, "GroupTrainer", new Vector2(0, 120), new Vector2(1040, 30), font, 20);
            menu.groupTrainerLabel.alignment = TextAlignmentOptions.Left; menu.groupTrainerLabel.enableWordWrapping = false;
            menu.introLabel = Text(page.transform, "Intro", new Vector2(0, -60), new Vector2(1040, 84), font, 17);
            menu.introLabel.alignment = TextAlignmentOptions.TopLeft;
            menu.rows = new Button[6]; menu.rowLabels = new TMP_Text[6];
            for (int i = 0; i < 6; i++)
            {
                var row = Button(page.transform, "ActivityRow" + i, new Vector2((i % 3 - 1) * 355, i < 3 ? 68 : -32), new Vector2(330, 88), "", "_Row" + i);
                menu.rows[i] = row.GetComponent<Button>(); menu.rowLabels[i] = row.GetComponentInChildren<TextMeshProUGUI>();
                menu.rowLabels[i].richText = true;
                ((RectTransform)menu.rowLabels[i].transform).sizeDelta = new Vector2(310, 80);
            }
            menu.status = Text(page.transform, "Status", new Vector2(0, -225), new Vector2(1040, 62), font, 20);
            var back = Button(page.transform, "Back", new Vector2(-430, -285), new Vector2(200, 48), "戻る", "_Back");
            menu.back = back; menu.backLabel = back.GetComponentInChildren<TextMeshProUGUI>();
            var action = Button(page.transform, "Action", new Vector2(280, -285), new Vector2(500, 48), "", "_Action");
            menu.action = action.GetComponent<Button>(); menu.actionLabel = action.GetComponentInChildren<TextMeshProUGUI>();

            // 隊形の印 (KH_HandMenu の Markers を移す。退役する KH_HandMenu の子ではなく KH_Dogs 直下に置く)
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MarkerMaterialPath);
            if (mat == null) throw new Exception("marker material missing: " + MarkerMaterialPath);
            var markers = Host(MarkersName, dogs);
            menu.placementMarkers = new GameObject[6];
            for (int i = 0; i < 6; i++)
            {
                var t = markers.transform.Find("Marker" + i);
                GameObject marker = t != null ? t.gameObject : null;
                if (marker == null)
                {
                    marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder); marker.name = "Marker" + i;
                    Undo.RegisterCreatedObjectUndo(marker, "Create marker");
                    marker.transform.SetParent(markers.transform, false);
                }
                var col = marker.GetComponent<Collider>(); if (col != null) UnityEngine.Object.DestroyImmediate(col);
                marker.layer = MarkerLayer; marker.GetComponent<Renderer>().sharedMaterial = mat; marker.SetActive(false);
                menu.placementMarkers[i] = marker;
            }
            page.SetActive(false);
            if (actChanged) KD_ProductsEditor.Copy(act);
            foreach (var c in new UdonSharpBehaviour[] { menu, pouch, language }) KD_ProductsEditor.Copy(c);
            EditorSceneManager.MarkSceneDirty(pouch.gameObject.scene);
            File.WriteAllText("Logs/activities-build.txt", log.AppendLine("BUILD complete (not saved)").ToString());
        }
        static TMP_Text Text(Transform parent, string name, Vector2 pos, Vector2 size, TMP_FontAsset font, float points)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var text = go.GetComponent<TextMeshProUGUI>(); if (text == null) text = go.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = points; text.color = Color.white; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; text.enableWordWrapping = true;
            return text;
        }

        [MenuItem("Tools/KominkaHillside/Menu/23 Verify Activities")]
        public static void Verify()
        {
            var sb = new StringBuilder(); int fail = 0;
            void Check(bool ok, string what) { sb.AppendLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fail++; }
            void Skip(string what) { sb.AppendLine("SKIP 裏山では対象外: " + what); }
            var dogs = DogsTransform();
            var acts = dogs.GetComponentsInChildren<KD_GroupActivities>(true);
            Check(acts.Length == 1 && KD_ProductsEditor.SceneComponents<KD_GroupActivities>().Length == 1, "one KD_GroupActivities, under " + DogsRoot);
            var menus = KD_ProductsEditor.SceneComponents<KD_ActivitiesMenu>();
            Check(menus.Length == 1, "one KD_ActivitiesMenu (" + menus.Length + ")");
            if (acts.Length != 1 || menus.Length != 1) { Finish(sb, fail); return; }
            var a = acts[0]; var m = menus[0];
            var manager = dogs.GetComponentsInChildren<KominkaHillside.KH_LeashManager>(true).SingleOrDefault();
            Check(manager != null && a.gameObject == manager.gameObject && manager.activities == a, "gathering authority shares the leash manager's GameObject (one Owner for attach and gather)");
            Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(a).SyncMethod == Networking.SyncType.Manual, "actual hosting Manual sync");
            Check(a.hillsideLeashes == manager && a.hillsidePass != null && a.pack != null, "hillsideLeashes / hillsidePass / pack wired");
            Check(a.pack != null && a.pack.dogFetch != null && a.pack.dogFetch.Length == 6, "six dogs in the pack");
            // DES-LIV-001: シーンに KD_LiveSystem があれば a.live はそれ (Menu/26a が書く)、無ければ null
            { var lives = KD_ProductsEditor.SceneComponents<KD_LiveSystem>(); var want = lives.Length == 1 ? lives[0] : null;
              Check(lives.Length <= 1 && a.live == want, "KD_GroupActivities.live = シーンの KD_LiveSystem があればそれ、無ければ null (KD_LiveSystem " + lives.Length + " 個 / 配線 " + (a.live != null) + ")"); }
            Skip("開催の所有の共有・管理者の判定 (集合を終われるのは開催者本人。ライブの開催認可は Menu/26b Verify Live)");
            Skip("トレーナー / みんなで遊ぶ / 犬目線の商品 (KD_ShopItem)。権利は KH_TrainerPass");

            // 集合のみ (犬目線カメラ・2 匹の遊びが無い)
            Check(KD_ProductsEditor.SceneComponents<KD_DogPovCamera>().Length == 0, "no KD_DogPovCamera in the scene (gathering only)");
            Skip("犬目線カメラ (KD_DogPovCamera) のモニター・カメラ・[×] の検査");
            Check(m.hillsidePass != null && m.hillsidePass == a.hillsidePass && m.activities == a, "menu reads the same trainer pass and authority");
            Check(m.items == null || m.items.Length == 0, "menu has no KD_ShopItem products");
            Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(m).SyncMethod == Networking.SyncType.None
                && !typeof(KD_ActivitiesMenu).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    .Any(f => f.GetCustomAttributes(typeof(UdonSyncedAttribute), true).Length > 0), "menu SyncMode None, 0 synced variables");

            // あそぶは 7 枚目のタブ
            Check(m.pouch != null && m.pouch.tabPages != null && m.pouch.tabPages.Length == KD_TreatPouch.TabCount && m.pouch.tabPages[KD_TreatPouch.TabPlay] == m.page && m.page.transform.parent == m.pouch.panelRoot.transform, "activities page is the 7th tab page (P2)");
            if (m.pouch == null || m.page == null) { Finish(sb, fail); return; }
            Check(m.pouch.activitiesMenu == m, "pouch.activitiesMenu wired");
            Check(m.pouch.settingsPage == null || m.pouch.settingsPage.transform.Find("ActivitiesPage") == null, "legacy settings page removed (P2)");
            Check(m.pouch.panelRoot.transform.Find("ActivitiesButton") == null, "legacy header entry removed (P2)");
            Check(m.page.transform.Find("GroupPov") == null, "no dog-camera group on the page");
            Check(m.groupTrainerLabel != null && m.introLabel != null && m.back != null && m.heading != null && m.status != null && m.action != null && m.actionLabel != null && m.backLabel != null, "labels, back and action wired");
            Check(m.rows != null && m.rows.Length == 6 && m.rows.All(r => r != null) && m.rowLabels != null && m.rowLabels.Length == 6 && m.rowLabels.All(l => l != null), "six rows connected");
            // 裏山改変: フリスビーの取り方 (DES-UI-002)。トレーナー群の一覧の行 (row2 = _Row2)、犬ごとの配列は Gather の写し
            Check(m.frisbeeCatchers != null && m.frisbeeCatchers.Length == 6, "frisbeeCatchers has 6 entries (" + (m.frisbeeCatchers == null ? "null" : m.frisbeeCatchers.Length.ToString()) + ")");
            Check(m.frisbeeCatchers != null && a.frisbeeCatchers != null && m.frisbeeCatchers.Length == 6
                && Enumerable.Range(0, 6).All(i => m.frisbeeCatchers[i] == (i < a.frisbeeCatchers.Length ? a.frisbeeCatchers[i] : null)), "frisbeeCatchers match KD_GroupActivities.frisbeeCatchers (" + (m.frisbeeCatchers == null ? 0 : m.frisbeeCatchers.Count(c => c != null)) + " dogs with a disc)");
            Check(KD_ActivitiesMenu.RowFrisbee == 2 && m.rows != null && m.rows.Length > KD_ActivitiesMenu.RowFrisbee && m.rows[KD_ActivitiesMenu.RowFrisbee] != null
                && m.rows[KD_ActivitiesMenu.RowFrisbee].name == "ActivityRow" + KD_ActivitiesMenu.RowFrisbee && m.rows[KD_ActivitiesMenu.RowFrisbee].transform.parent == m.page.transform,
                "frisbee row is ActivityRow" + KD_ActivitiesMenu.RowFrisbee + " in the trainer group (home row, beside gather / end)");
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(m);
            foreach (var button in m.page.GetComponentsInChildren<Button>(true))
            {
                string want = button.name.StartsWith("ActivityRow") ? "_Row" + button.name.Substring("ActivityRow".Length) : button.name == "Back" ? "_Back" : button.name == "Action" ? "_Action" : "?";
                bool one = button.onClick.GetPersistentEventCount() == 1;
                string ev = one ? new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls").GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue : "";
                Check(one && button.onClick.GetPersistentTarget(0) == udon && button.onClick.GetPersistentMethodName(0) == "SendCustomEvent" && ev == want, "button " + button.name + " -> " + want + " (" + ev + ")");
            }

            // 隊形は体格ベース (2026-09-13): 全ペアが「幅の和 × 比率」を下回らず (輪は弦なので 0.9 掛け)、足跡が部屋に収まる
            var radius = a.pack.dogRadius;
            Check(radius != null && radius.Length == 6 && radius.All(r => r > .05f), "pack radii feed the formation geometry");
            if (radius != null && radius.Length == 6)
                for (int shape = 0; shape < 6; shape++)
                {
                    float ratio = shape == 2 ? a.huddleRatio : a.gapRatio;
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 o = a.Offset(shape, i);
                        Check(Mathf.Abs(o.x) <= 2f && Mathf.Abs(o.z) <= 2f, "formation footprint fits " + shape + "/" + i + " " + o);
                        for (int j = i + 1; j < 6; j++)
                            Check(Vector3.Distance(o, a.Offset(shape, j)) >= (radius[i] + radius[j]) * a.widthRatio * ratio * .9f, "formation spacing " + shape + "/" + i + "/" + j);
                    }
                }
            Check(m.placementMarkers != null && m.placementMarkers.Length == 6 && m.placementMarkers.All(g => g != null && g.GetComponent<Collider>() == null && !g.activeSelf), "six noncolliding inactive placement markers");
            Check(m.placementMarkers != null && m.placementMarkers.All(g => g != null && !g.transform.IsChildOf(m.page.transform)), "markers live outside the menu board");
            try { KD_LocalizationEditor.ValidateScene(Find<KD_Language>(), KD_LocalizationEditor.Load()); Check(true, "localization bindings current"); }
            catch (Exception e) { Check(false, "localization bindings current: " + e.Message); }
            var catalog = KD_LocalizationEditor.Load();
            var ja = catalog.languages.First(l => l.code == "ja");
            foreach (string key in new[] { "activities.hillside.help", "activities.hillside.formationHelp", "activities.hillside.started", "activities.hillside.unknown", "activities.hillside.available",
                "activities.hillside.frisbee", "activities.hillside.frisbeeNow", "activities.hillside.frisbeePending", "activities.hillside.frisbeeRequested", "activities.hillside.frisbeeAfterReturn",
                "activities.hillside.frisbeeVariant.0", "activities.hillside.frisbeeVariant.1", "activities.hillside.frisbeeVariant.2", "activities.hillside.frisbeeVariant.3", "activities.hillside.frisbeeVariant.4", "activities.hillside.frisbeeVariant.5" })
                Check(ja.texts.Any(e => e.key == key && !string.IsNullOrEmpty(e.text)), "text " + key);
            Finish(sb, fail);
        }
        static void Finish(StringBuilder sb, int fail)
        {
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
                foreach (var language in new[] { "ja", "en" }) for (int view = 0; view < 3; view++)
                {
                    string L(string key) { return p.language._TextForLanguage(key, language); }
                    var clone = UnityEngine.Object.Instantiate(p.panelRoot); clone.SetActive(false);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone, scene);
                    foreach (var b in clone.GetComponentsInChildren<UdonSharpBehaviour>(true)) UnityEngine.Object.DestroyImmediate(b);
                    foreach (var b in clone.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true)) UnityEngine.Object.DestroyImmediate(b);
                    foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                    clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.transform.localScale = Vector3.one * .001f;
                    clone.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                    // 裏山: view 0 = 一覧 (未加入) / 1 = 隊形 / 2 = 一覧 (加入中)
                    bool home = view != 1; bool trainer = view == 2;
                    foreach (var page in p.tabPages) clone.transform.Find(AnimationUtility.CalculateTransformPath(page.transform, p.panelRoot.transform)).gameObject.SetActive(false);
                    if (p.settingsPage != null) clone.transform.Find(AnimationUtility.CalculateTransformPath(p.settingsPage.transform, p.panelRoot.transform)).gameObject.SetActive(false);
                    var root = clone.transform.Find(AnimationUtility.CalculateTransformPath(p.tabPages[KD_TreatPouch.TabPlay].transform, p.panelRoot.transform)); root.gameObject.SetActive(true);
                    string[] tabKeys = { "KD_TreatPouch.WordTabTreat", "KD_TreatPouch.WordTabToy", "KD_TreatPouch.WordWalkTab", "KD_TreatPouch.WordTabSleep", "KD_TreatPouch.WordLiveVolume", "KD_TreatPouch.WordTabAcc", "KD_TreatPouch.WordTabPlay" }; // ライブのタブ語は WordLiveVolume に配線されている (bindings)
                    for (int t = 0; t < p.tabImages.Length; t++)
                    {
                        var tab = clone.transform.Find(AnimationUtility.CalculateTransformPath(p.tabImages[t].transform, p.panelRoot.transform));
                        tab.GetComponent<Image>().color = t == KD_TreatPouch.TabPlay ? p.tabOnColor : p.tabOffColor;
                        var underline = clone.transform.Find(AnimationUtility.CalculateTransformPath(p.tabUnderlines[t].transform, p.panelRoot.transform));
                        underline.gameObject.SetActive(t == KD_TreatPouch.TabPlay);
                        var tabLabel = tab.Find("Label"); if (tabLabel != null) tabLabel.GetComponent<TMP_Text>().text = L(tabKeys[t]);
                    }
                    clone.transform.Find("SectionTitle").GetComponent<TMP_Text>().text = L("activities.title");
                    root.Find("Heading").GetComponent<TMP_Text>().text = L(home ? (trainer ? "activities.helpTrainer" : "activities.hillside.help") : "activities.hillside.formationHelp");
                    root.Find("GroupTrainer").gameObject.SetActive(home); root.Find("GroupTrainer").GetComponent<TMP_Text>().text = L(trainer ? "activities.group.trainer.on" : "activities.group.trainer.off");
                    root.Find("Intro").gameObject.SetActive(home); root.Find("Intro").GetComponent<TMP_Text>().text = L(trainer ? "activities.hillside.trainerActive" : "activities.hillside.trainerIntro");
                    root.Find("Status").GetComponent<TMP_Text>().text = home && !trainer ? L("activities.storeNote") : L("activities.hillside.rejectStart");
                    root.Find("Back/Label").GetComponent<TMP_Text>().text = L("activities.back"); root.Find("Back").gameObject.SetActive(!home);
                    root.Find("Action/Label").GetComponent<TMP_Text>().text = L(home ? "activities.openStore" : "activities.gather");
                    root.Find("Action").gameObject.SetActive(!home || !trainer);
                    for (int i = 0; i < 6; i++)
                    {
                        var row = (RectTransform)root.Find("ActivityRow" + i);
                        // 裏山改変: フリスビー行 (row2、DES-UI-002) は一覧で出す (ディスクを持つ犬がいる前提)。未加入は中央の枠へ詰める。加入中は長めの副文 (予約中) で幅を確かめる
                        bool frisbee = home && i == KD_ActivitiesMenu.RowFrisbee;
                        bool show = home ? (i == 0 || (i == 1 && trainer) || frisbee) : true;
                        row.gameObject.SetActive(show);
                        int column = frisbee && !trainer ? 1 : i % 3;
                        row.anchoredPosition = new Vector2((column - 1) * 355, home ? 60 : (i < 3 ? 68 : -32));
                        string text = home
                            ? (i == 0 ? L("activities.home.0") + "\n<size=17>" + (trainer ? L("activities.hillside.perk") : L("activities.needTrainer")) + "</size>"
                              : frisbee ? L("activities.hillside.frisbee") + "\n<size=17>" + (trainer ? L("activities.hillside.frisbeePending").Replace("{0}", L("activities.hillside.frisbeeVariant.3")) : L("activities.needTrainer")) + "</size>"
                                      : L("activities.home.3") + "\n<size=17>" + L("activities.endDesc") + "</size>")
                            : (i == 0 ? "● " : "") + L("activities.shape." + i);
                        row.Find("Label").GetComponent<TMP_Text>().text = text;
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
