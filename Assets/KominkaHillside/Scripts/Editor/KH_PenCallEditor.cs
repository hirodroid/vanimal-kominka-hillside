using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using QvPen.UdonScript;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-017: 筆ペン (モサンゴ屋「筆ペン差し替えショートwith筆掛け」、QvPen 3.3.15、6 色) を手元メニューの設定から色を選んで呼ぶ。
    /// 設計: docs/development/issues/IMP-WLD-017-pen-call-from-menu.md §3 (決定・3-a) / §4。雛形は KH_VideoPlayerEditor (IMP-UI-011)。
    ///
    /// 実行順 (統括が直列に。サブエージェントは実行しない):
    ///   Pen/0 - Create Program Asset   KH_PenCall.asset を作る → U# をコンパイル (unity-compile.sh --udon)
    ///   Pen/1 - Build Pen Call Page    冪等。筆掛けの 6 本を読む (購入物のインスタンスは読むだけ。書かない) → KH_PenCall (KH_ToyRecall の隣、面の外) に配線、
    ///                                  面 Page_Settings/PenPage (状態・[もどる]・6 色のボタン)、設定ホームの [筆ペン] (群「読む・書く」の行の右端)、
    ///                                  pouch.penPage / penPageBehaviour、文言表と字形。保存前に Pen/2 と同じ検査 (FAIL なら保存しない)
    ///   Pen/2 - Verify                 読むだけ (Temp/imp-wld-017/pen-verify.json)
    ///   Pen/9 - Remove                 Pen/1 が作った物 (KH_PenCall・PenPage・PenCallButton・extraSettingsPages の 1 件・pouch の参照・それらの NetworkIDs) だけを外す
    ///
    /// 配線の出どころ (Codex 相談 2026-10-16): QvPen_Settings の serialized `pensParent` の配下の PenManager (0)〜(5) → 各 Manager の serialized `pen`。
    /// QvPen_Settings.penManagers は NonSerialized (Start で埋まる) なので Editor では使わない。名前探索で `pen` を黙って補完しない (足りなければ FAIL)。
    /// KH_PenCall はプレハブの外 (素のシーンオブジェクト) からプレハブの中を指す (外 → 内。vrc-udonsharp §3 の向き)。
    /// 文言: 設定ホームの [筆ペン] は任意の部品 (Pen/9 で消える) なので UILocalizationBindings.json に入れない (Bind / ValidateScene は束縛先が無いと投げる)。
    /// 動画の [動画] (KH_VideoPage.headerLabel) と同じく KH_PenCall が _RefreshLanguage で描き直す (`entryLabel`)。
    /// </summary>
    public static partial class KH_PenCallEditor
    {
        const string Menu = "Tools/KominkaHillside/Pen/";
        public const string OutDir = "Temp/imp-wld-017";
        public const string RackName = "筆ペン差し替えショートwith筆掛け";
        public const string RackPrefabGuid = "9212744b825cd7544a01b29e932fdc6d";
        public const int PenCount = 6;
        public const string BehaviourName = "KH_PenCall";
        public const string PageName = "PenPage";
        public const string ProgramPath = "Assets/KominkaHillside/Scripts/KH_PenCall";

        // ---------------------------------------------------------------- 文言 (UITranslations.json)
        public const string OpenKey = "pen.call.open", HeadingKey = "settings.group.read", InUseKey = "KD_ShopShelf.WordInUse", BackKey = "KD_TreatPouch.WordAccBack";
        public static readonly string[] StateKeys = { "KD_TreatPouch.WordToyCalling", "KD_TreatPouch.WordToyPlaced", "KD_TreatPouch.WordToyFailed" };
        public static readonly string[] NeededKeys = { OpenKey, HeadingKey, InUseKey, BackKey, StateKeys[0], StateKeys[1], StateKeys[2] };

        // ---------------------------------------------------------------- 面の配置 (1140 x 700、本文の帯 Y 148〜-232。Issue §3-2 / §4)
        public const float BandTop = 148f, BandBottom = -232f;
        public static readonly Vector2 StatePos = new Vector2(-170f, 170f), StateSize = new Vector2(700f, 44f);
        public static readonly Vector2 BackPos = new Vector2(450f, 170f), BackSize = new Vector2(220f, 44f);
        public static readonly Vector2 ColorSize = new Vector2(330f, 88f);
        public const float ColorPitchX = 355f, ColorRow0Y = 50f, ColorRow1Y = -60f, ColorFontMax = 22f;
        /// <summary>色のボタン i の中心 (3 列 x 2 段: x -355 / 0 / 355、y 50 / -60。各 330 x 88 で x -520〜520、y 94〜-104)。</summary>
        public static Vector2 ColorPos(int i) { return new Vector2((i % 3 - 1) * ColorPitchX, i < 3 ? ColorRow0Y : ColorRow1Y); }
        public static string ColorName(int i) { return "PenColor" + i; }

        /// <summary>面の子と期待する矩形 (Verify が見る)。</summary>
        public static IEnumerable<(string name, Vector2 pos, Vector2 size)> PageLayout()
        {
            yield return ("State", StatePos, StateSize);
            yield return ("Back", BackPos, BackSize);
            for (int i = 0; i < PenCount; i++) yield return (ColorName(i), ColorPos(i), ColorSize);
        }

        static readonly Color Ink = new Color(0.97f, 0.95f, 0.90f);
        static readonly Color DarkInk = new Color(0.12f, 0.11f, 0.10f);
        /// <summary>明るいインク (黄・黄緑) の上は暗い字、暗いインクの上は白。輝度 (Rec.601 の重み) 0.5 で分ける。</summary>
        public static float Luminance(Color c) { return 0.299f * c.r + 0.587f * c.g + 0.114f * c.b; }
        public static Color LabelColorFor(Color ink) { return Luminance(ink) > 0.5f ? DarkInk : Color.white; }
        /// <summary>ボタンの地 = Manager の colorGradient の先頭 (Evaluate(0))、不透明。</summary>
        public static Color InkOf(QvPen_PenManager m) { var c = m.colorGradient != null ? m.colorGradient.Evaluate(0f) : Color.magenta; c.a = 1f; return c; }

        // ================================================================ context

        class Ctx
        {
            public UnityEngine.SceneManagement.Scene scene;
            public KD_TreatPouch pouch; public KD_Language language; public TMP_FontAsset font; public UdonBehaviour pouchUdon;
            public KD_LocalizationEditor.Catalog catalog;
            public List<string> missingKeys = new List<string>();
            public List<string> notes = new List<string>();
        }

        static Ctx Resolve()
        {
            var c = new Ctx { scene = EditorSceneManager.GetActiveScene() };
            var pouches = Object.FindObjectsOfType<KD_TreatPouch>(true).Where(p => p.gameObject.scene == c.scene).ToArray();
            if (pouches.Length != 1) throw new InvalidOperationException("exactly one KD_TreatPouch required (" + pouches.Length + "); nothing changed");
            c.pouch = pouches[0];
            c.language = c.pouch.language;
            if (c.language == null) throw new InvalidOperationException("KD_TreatPouch.language not wired (Menu/11); nothing changed");
            c.font = c.pouch.sectionTitleLabel != null ? c.pouch.sectionTitleLabel.font : null;
            if (c.font == null) throw new InvalidOperationException("the menu's shared font (sectionTitleLabel.font) is missing; nothing changed");
            if (c.pouch.settingsPage == null || c.pouch.settingsHome == null || c.pouch.panelRoot == null) throw new InvalidOperationException("menu panel / settings page / settings home missing; nothing changed");
            c.pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(c.pouch);
            if (c.pouchUdon == null) throw new InvalidOperationException("KD_TreatPouch backing missing; nothing changed");
            c.catalog = KD_LocalizationEditor.Load();
            var fallback = c.catalog.languages.First(l => l.code == c.catalog.fallbackLanguage);
            foreach (var key in NeededKeys)
            {
                var e = fallback.texts.FirstOrDefault(x => x.key == key);
                if (e == null || string.IsNullOrWhiteSpace(e.text)) c.missingKeys.Add(key);
            }
            return c;
        }

        static string ProgramMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") == null)
                return ProgramPath + ".asset missing: run Pen/0, compile U# (unity-compile.sh --udon), then this menu";
            return null;
        }

        /// <summary>文言表の文 (無いキーは null。KD_LocalizationEditor.Text は未知のキーで投げる)。</summary>
        static string TextOrNull(KD_LocalizationEditor.Catalog catalog, string code, string key)
        {
            try { return KD_LocalizationEditor.Text(catalog, code, key); } catch (InvalidOperationException) { return null; }
        }

        static string Ja(Ctx c, string key) { return TextOrNull(c.catalog, c.catalog.fallbackLanguage, key) ?? key; }

        static VRCSceneDescriptor Descriptor() { return Object.FindObjectOfType<VRCSceneDescriptor>(); }

        /// <summary>今 GameObject を失っている NetworkIDs の ID (Pen/1 の保存前検査で、元からあった残骸を区別する)。</summary>
        static HashSet<int> NullNetworkIds(VRCSceneDescriptor d)
        {
            var s = new HashSet<int>();
            if (d == null || d.NetworkIDCollection == null) return s;
            foreach (var p in d.NetworkIDCollection) if (p != null && p.gameObject == null) s.Add(p.ID);
            return s;
        }

        // ================================================================ the rack (購入物のインスタンス。読むだけ)

        class Rack
        {
            public GameObject root; public QvPen_Settings settings; public Transform pensParent;
            public QvPen_PenManager[] managers = new QvPen_PenManager[PenCount];
            public QvPen_Pen[] pens = new QvPen_Pen[PenCount];
            public Color[] inks = new Color[PenCount];
        }

        /// <summary>筆掛けを読む。SerializedObject は読むだけで Apply しない (購入物のインスタンスへは何も書かない)。
        /// problems が空でなければ null (呼び手が FAIL にする)。warnings は止めない注意。</summary>
        static Rack ReadRack(UnityEngine.SceneManagement.Scene scene, List<string> problems, List<string> warnings)
        {
            var roots = scene.GetRootGameObjects().Where(g => g.name == RackName).ToArray();
            KD_SceneObjects.Find(RackName, out int anywhere);
            if (roots.Length != 1 || anywhere != 1) { problems.Add("exactly one '" + RackName + "' at the scene root required (root " + roots.Length + ", anywhere " + anywhere + ")"); return null; }
            var r = new Rack { root = roots[0] };
            string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.root);
            if (string.IsNullOrEmpty(src) || AssetDatabase.AssetPathToGUID(src) != RackPrefabGuid)
                warnings?.Add("'" + RackName + "' is not an instance of the purchased prefab " + RackPrefabGuid + " (source " + (string.IsNullOrEmpty(src) ? "none" : src) + ")");
            var settings = r.root.GetComponentsInChildren<QvPen_Settings>(true);
            if (settings.Length != 1) { problems.Add(RackName + " must hold exactly one QvPen_Settings (" + settings.Length + ")"); return null; }
            r.settings = settings[0];
            using (var so = new SerializedObject(r.settings))
            {
                var pp = so.FindProperty("pensParent");
                if (pp == null) { problems.Add("QvPen_Settings.pensParent not found (QvPen version changed?)"); return null; }
                r.pensParent = pp.objectReferenceValue as Transform;
            }
            if (r.pensParent == null) { problems.Add("QvPen_Settings.pensParent is empty"); return null; }
            if (!r.pensParent.IsChildOf(r.root.transform)) { problems.Add("QvPen_Settings.pensParent (" + r.pensParent.name + ") is outside " + RackName); return null; }
            var all = r.pensParent.GetComponentsInChildren<QvPen_PenManager>(true);
            if (all.Length != PenCount) problems.Add("pensParent " + r.pensParent.name + " holds " + all.Length + " QvPen_PenManager (want " + PenCount + "): " + string.Join(", ", all.Select(m => m.name)));
            for (int i = 0; i < PenCount; i++)
            {
                string want = "PenManager (" + i + ")";
                var hits = all.Where(m => m.name == want).ToArray();
                if (hits.Length != 1) { problems.Add(want + ": " + hits.Length + " under pensParent (want 1)"); continue; }
                r.managers[i] = hits[0];
            }
            if (problems.Count > 0) return null;
            for (int i = 0; i < PenCount; i++)
            {
                var m = r.managers[i];
                QvPen_Pen pen;
                using (var so = new SerializedObject(m))
                {
                    var p = so.FindProperty("pen");
                    if (p == null) { problems.Add(m.name + ".pen not found (QvPen version changed?)"); continue; }
                    pen = p.objectReferenceValue as QvPen_Pen;
                }
                if (pen == null) { problems.Add(m.name + ".pen is empty (not filled by a name search)"); continue; }
                if (!pen.transform.IsChildOf(r.root.transform)) { problems.Add(m.name + ".pen (" + pen.name + ") is outside " + RackName); continue; }
                r.pens[i] = pen;
                r.inks[i] = InkOf(m);
                var pickup = pen.GetComponent<VRCPickup>();
                var sync = pen.GetComponent<VRCObjectSync>();
                var body = pen.GetComponent<Rigidbody>();
                if (pickup == null) problems.Add(m.name + "/" + pen.name + ": no VRCPickup");
                if (sync == null) problems.Add(m.name + "/" + pen.name + ": no VRCObjectSync");
                if (body == null || !body.isKinematic) problems.Add(m.name + "/" + pen.name + ": Rigidbody " + (body == null ? "missing" : "not kinematic") + " (KH_PenCall places it without physics)");
            }
            if (problems.Count > 0) return null;
            if (r.pens.Distinct().Count() != PenCount) { problems.Add("the 6 managers do not point at 6 distinct pens: " + string.Join(", ", r.pens.Select(p => p.name + "#" + p.GetInstanceID()))); return null; }
            return r;
        }

        /// <summary>KH_ToyRecall の親 (KH_PenCall の置き場。面の外で、常に有効)。null = シーン直下。</summary>
        static Transform PenCallParent(Ctx c, List<string> problems, out KH_ToyRecall toy)
        {
            toy = null;
            var toys = Object.FindObjectsOfType<KH_ToyRecall>(true).Where(t => t.gameObject.scene == c.scene).ToArray();
            if (toys.Length != 1) { problems.Add("exactly one KH_ToyRecall required (" + toys.Length + ") to place " + BehaviourName + " beside it"); return null; }
            toy = toys[0];
            var parent = toy.transform.parent;
            if (parent != null && (parent.IsChildOf(c.pouch.panelRoot.transform) || parent.IsChildOf(c.pouch.transform)))
                problems.Add("KH_ToyRecall's parent " + parent.name + " is inside the hand menu (KH_PenCall must stay outside the page that is hidden)");
            if (parent != null && !parent.gameObject.activeInHierarchy) problems.Add("KH_ToyRecall's parent " + parent.name + " is inactive in the hierarchy");
            return parent;
        }

        // ================================================================ Pen/0

        [MenuItem(Menu + "0 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") != null)
            { Debug.Log("[KH Pen] PEN_PROGRAM_OK already " + ProgramPath + ".asset"); return; }
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ProgramPath + ".cs");
            if (script == null) { Debug.LogError("[KH Pen] PEN_PROGRAM_FAIL " + ProgramPath + ".cs not imported yet (refresh first)"); return; }
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, ProgramPath + ".asset");
            AssetDatabase.SaveAssets();
            Debug.Log("[KH Pen] PEN_PROGRAM_OK created " + ProgramPath + ".asset. Compile U# (unity-compile.sh --udon) before Pen/1.");
        }

        // ================================================================ Pen/1

        [Serializable] class BuildReport
        {
            public string utc, scene, status, backup, menu;
            public List<string> log = new List<string>();
        }

        class PageUi
        {
            public GameObject page, back, entry; public TextMeshProUGUI state;
            public GameObject[] colors = new GameObject[PenCount];
        }

        [MenuItem(Menu + "1 - Build Pen Call Page")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, menu = "Pen/1" };
            string missing = ProgramMissing();
            if (missing != null)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + missing); WriteJson(OutDir + "/pen-build.json", r);
                Debug.LogError("[KH Pen] PEN_BUILD_FAIL " + missing + "; nothing changed"); return;
            }
            Ctx c; Rack rack; Transform parent; KH_ToyRecall toy;
            try
            {
                c = Resolve();
                if (c.missingKeys.Count > 0) throw new InvalidOperationException("texts missing in " + KD_MenuFinishEditor.CatalogPath + " (" + c.catalog.fallbackLanguage + "): " + string.Join(", ", c.missingKeys) + "; add them first; nothing changed");
                var problems = new List<string>(); var warnings = new List<string>();
                rack = ReadRack(c.scene, problems, warnings);
                parent = PenCallParent(c, problems, out toy);
                if (problems.Count > 0) throw new InvalidOperationException(string.Join("; ", problems) + "; nothing changed");
                foreach (var w in warnings) r.log.Add("WARN " + w);
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message); WriteJson(OutDir + "/pen-build.json", r);
                Debug.LogError("[KH Pen] PEN_BUILD_FAIL " + e.Message); return;
            }
            r.log.AddRange(c.notes);
            r.log.Add("rack: " + RackName + " -> pensParent " + rack.pensParent.name + " -> "
                + string.Join(", ", Enumerable.Range(0, PenCount).Select(i => rack.managers[i].name + "/" + rack.pens[i].name + " #" + ColorUtility.ToHtmlStringRGB(rack.inks[i]))) + " (read only)");
            var descriptor = Descriptor();
            var baselineNull = NullNetworkIds(descriptor);
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-build.unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, true);
            try
            {
                var pc = EnsurePenCall(c, parent, toy, r.log);
                var pcU = UdonSharpEditorUtility.GetBackingUdonBehaviour(pc);
                var ui = BuildPage(c, rack, pcU, descriptor, r.log);
                ui.entry = PlaceEntryButton(c, r.log);
                RetitleHeading(c, r.log);
                Wire(c, rack, pc, ui, r.log);
                RefreshLanguage(c, r.log);
                foreach (UdonSharpBehaviour b in new UdonSharpBehaviour[] { pc, c.pouch, c.language }) { UdonSharpEditorUtility.CopyProxyToUdon(b); EditorUtility.SetDirty(b); }
                // ---- the same checks as Pen/2 before saving (a failure leaves the scene unsaved) ----
                var v = RunVerify(baselineNull);
                WriteJson(OutDir + "/pen-verify.json", v);
                if (v.fails > 0)
                {
                    r.status = "VERIFY_FAILED"; r.log.Add("NOT SAVED: " + v.fails + " checks failed (" + OutDir + "/pen-verify.json)");
                    r.log.AddRange(v.lines.Where(l => l.StartsWith("FAIL")).Take(20));
                    WriteJson(OutDir + "/pen-build.json", r);
                    Debug.LogError("[KH Pen] PEN_BUILD_FAIL verify fails=" + v.fails + " (not saved). First: " + string.Join(" | ", v.lines.Where(l => l.StartsWith("FAIL")).Take(4))
                        + ". The scene before this run is at " + r.backup + "; reopen the scene without saving to discard the changes.");
                    return;
                }
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("scene save failed");
                AssetDatabase.SaveAssets();
                r.status = "BUILT";
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message);
                WriteJson(OutDir + "/pen-build.json", r);
                Debug.LogError("[KH Pen] PEN_BUILD_FAIL " + e.Message + ". The scene before this run is at " + r.backup + "; reopen the scene without saving to discard partial changes.");
                throw;
            }
            WriteJson(OutDir + "/pen-build.json", r);
            Debug.Log("[KH Pen] PEN_BUILD_OK " + BehaviourName + " wired to 6 pens, page " + PageName + " / settings [筆ペン] built, saved. " + r.log.Count + " log lines in " + OutDir
                + "/pen-build.json. Next: Pen/2 (after the save the backing heaps are populated), Menu/22.");
        }

        // ---------------------------------------------------------------- KH_PenCall (KH_ToyRecall の隣。面の外、常に有効、sync None)

        static KH_PenCall EnsurePenCall(Ctx c, Transform parent, KH_ToyRecall toy, List<string> log)
        {
            var existing = Object.FindObjectsOfType<KH_PenCall>(true).Where(x => x.gameObject.scene == c.scene).ToArray();
            if (existing.Length > 1) throw new InvalidOperationException(existing.Length + " KH_PenCall in the scene (" + string.Join(", ", existing.Select(x => x.name)) + "); delete the extras by hand (or Pen/9) first");
            GameObject go;
            if (existing.Length == 1)
            {
                go = existing[0].gameObject;
                if (!OwnObject(go.transform))
                    throw new InvalidOperationException("the existing KH_PenCall sits on " + go.name + " together with other parts or children; move it to its own object by hand");
            }
            else
            {
                Transform t = parent != null ? parent.Find(BehaviourName) : null;
                if (t == null && parent == null) { var rootGo = c.scene.GetRootGameObjects().FirstOrDefault(g => g.name == BehaviourName); if (rootGo != null) t = rootGo.transform; }
                if (t != null && (t.GetComponents<Component>().Length > 1 || t.childCount > 0))
                    throw new InvalidOperationException("an object named " + BehaviourName + " with other parts exists beside KH_ToyRecall; rename or delete it by hand");
                go = t != null ? t.gameObject : new GameObject(BehaviourName);
            }
            go.name = BehaviourName;
            if (go.transform.parent != parent) go.transform.SetParent(parent, false);
            go.layer = 0; go.SetActive(true);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            // KH_ToyRecall の直後に並べる (同じ親のとき)
            if (toy.transform.parent == parent && toy.transform != go.transform)
            {
                int a = toy.transform.GetSiblingIndex();
                go.transform.SetSiblingIndex(go.transform.GetSiblingIndex() > a ? a + 1 : a);
            }
            var pc = go.GetComponent<KH_PenCall>();
            if (pc == null) pc = go.AddUdonSharpComponent<KH_PenCall>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(pc);
            if (backing == null) throw new InvalidOperationException("KH_PenCall backing missing (run Pen/0, compile U#, run again)");
            if (backing.SyncMethod != Networking.SyncType.None) { backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing); log.Add("KH_PenCall sync -> None"); }
            pc.enabled = true;
            EditorUtility.SetDirty(go);
            log.Add("KH_PenCall on " + ScenePath(go.transform) + " (beside KH_ToyRecall, outside the menu, always active, sync None)");
            return pc;
        }

        /// <summary>Pen/1 が作る形 (名前 KH_PenCall、子なし、Transform + KH_PenCall + その backing だけ)。</summary>
        static bool OwnObject(Transform t)
        {
            return t != null && t.childCount == 0
                && t.GetComponents<Component>().All(x => x == null || x is Transform || x is KH_PenCall || x is UdonBehaviour);
        }

        // ---------------------------------------------------------------- the page (Page_Settings/PenPage)

        static PageUi BuildPage(Ctx c, Rack rack, UdonBehaviour pcU, VRCSceneDescriptor descriptor, List<string> log)
        {
            var p = c.pouch;
            var parent = p.settingsPage.transform;
            // rebuilt from scratch every run (no stale children; everything on it is the builder's)
            var old = parent.Find(PageName);
            if (old != null)
            {
                p.extraSettingsPages = (p.extraSettingsPages ?? new GameObject[0]).Where(e => e != null && e != old.gameObject).ToArray();
                DropNetworkIds(descriptor, new[] { old }, log);
                Object.DestroyImmediate(old.gameObject);
                log.Add("removed old Page_Settings/" + PageName);
            }
            var ui = new PageUi();
            var page = KD_ShopSetupEditor.EnsureUiChild(parent, PageName, Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            ui.page = page;
            ui.state = Label(page.transform, "State", StatePos, StateSize, "", c.font, 22f, 14f, TextAlignmentOptions.MidlineLeft, false);
            ui.back = Btn(page.transform, "Back", BackPos, BackSize, Ja(c, BackKey), KD_UiSpriteEditor.ActionNeutral, c, c.pouchUdon, "_PenBack", 20f, log);
            for (int i = 0; i < PenCount; i++)
            {
                var ink = rack.inks[i];
                ui.colors[i] = Btn(page.transform, ColorName(i), ColorPos(i), ColorSize, Ja(c, OpenKey), ink, c, pcU, "_Call" + i, ColorFontMax, log);
                var l = LabelOf(ui.colors[i]);
                if (l != null) { l.color = LabelColorFor(ink); EditorUtility.SetDirty(l); }   // EnsureButton writes white; set after it
            }
            page.SetActive(false);

            var pages = new List<GameObject>();
            foreach (var e in p.extraSettingsPages ?? new GameObject[0]) if (e != null && !pages.Contains(e)) pages.Add(e);
            if (!pages.Contains(page)) pages.Add(page);
            p.extraSettingsPages = pages.ToArray();
            EditorUtility.SetDirty(p);
            log.Add("page: Page_Settings/" + PageName + " (hidden, registered in extraSettingsPages: " + pages.Count + " pages), state " + StateSize + " at " + StatePos + ", [back] " + BackSize + " at " + BackPos
                + " -> pouch _PenBack, 6 colour buttons " + ColorSize + " (3 x 2 at x +-" + ColorPitchX + ", y " + ColorRow0Y + " / " + ColorRow1Y + ") -> KH_PenCall _Call0.._Call5, ink "
                + string.Join(" ", rack.inks.Select(k => "#" + ColorUtility.ToHtmlStringRGB(k) + (Luminance(k) > 0.5f ? "/dark" : "/white"))) + ". Layer " + LayerMask.LayerToName(page.layer));
            return ui;
        }

        // ---------------------------------------------------------------- settings home [筆ペン] (群「読む・書く」の行の右端。PhotoCaptureToggle と同じ置き方)

        static GameObject PlaceEntryButton(Ctx c, List<string> log)
        {
            var go = Btn(c.pouch.settingsHome.transform, KD_MenuFinishEditor.PenCallButtonName, KD_MenuFinishEditor.HillsidePenCallPos, KD_MenuFinishEditor.HillsidePenCallSize,
                Ja(c, OpenKey), KD_UiSpriteEditor.ActionNeutral, c, c.pouchUdon, "_PenOpen", 22f, log);
            go.SetActive(true);
            EditorUtility.SetDirty(go);
            log.Add("settings [筆ペン] " + KD_MenuFinishEditor.PenCallButtonName + " at " + KD_MenuFinishEditor.HillsidePenCallPos + " " + KD_MenuFinishEditor.HillsidePenCallSize
                + " -> pouch _PenOpen (group read/write, right end of the read row)");
            // 2026-10-16: the read row (読む・書く) is 4 x 260 (Credit / Information / WalkIntro / PenCall). WalkIntroButton (IMP-UI-006) sat at the old (455,-214) 190 x 60,
            // so lay out the whole row from the constants (positions, sizes, Label = size - (24, 4)); objects that are absent are skipped, nothing is created
            var row = KD_MenuFinishEditor.LayoutHillsideReadRow(c.pouch.settingsHome.transform);
            log.Add("read row laid out from KD_MenuFinishEditor.HillsideReadRow (" + KD_MenuFinishEditor.HillsideReadEntrySize + " each): " + string.Join(", ", row));
            return go;
        }

        /// <summary>群の見出し GroupRead は束縛 (UILocalizationBindings.json) で実行時に描かれる。Editor の見た目も今の文言 (「読む・書く」) にそろえる。</summary>
        static void RetitleHeading(Ctx c, List<string> log)
        {
            var t = c.pouch.settingsHome.transform.Find("GroupRead");
            var label = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
            if (label == null) { log.Add("WARN settings heading GroupRead not found (Menu/22 builds it)"); return; }
            string want = Ja(c, HeadingKey);
            if (label.text != want) { label.text = want; EditorUtility.SetDirty(label); log.Add("heading GroupRead -> " + want + " (" + HeadingKey + ")"); }
        }

        // ---------------------------------------------------------------- wiring

        static void Wire(Ctx c, Rack rack, KH_PenCall pc, PageUi ui, List<string> log)
        {
            // vrc-udonsharp §4: 非同期の参照配列は宣言で確保しない。ここで毎回新しく確保して配る
            pc.penManagers = new QvPen_PenManager[PenCount];
            pc.pens = new QvPen_Pen[PenCount];
            pc.colorButtons = new Button[PenCount];
            pc.colorLabels = new TextMeshProUGUI[PenCount];
            for (int i = 0; i < PenCount; i++)
            {
                pc.penManagers[i] = rack.managers[i];
                pc.pens[i] = rack.pens[i];
                pc.colorButtons[i] = ui.colors[i].GetComponent<Button>();
                pc.colorLabels[i] = LabelOf(ui.colors[i]);
            }
            pc.language = c.language;
            pc.page = ui.page;
            pc.stateLabel = ui.state;
            pc.backLabel = LabelOf(ui.back);
            // 設定ホームの [筆ペン] の文字。束縛 (UILocalizationBindings.json) ではなく KH_PenCall._RefreshLanguage が描き直す (KH_VideoPage.headerLabel と同じ型)
            pc.entryLabel = LabelOf(ui.entry);
            EditorUtility.SetDirty(pc);

            c.pouch.penPage = ui.page; c.pouch.penPageBehaviour = pc;
            EditorUtility.SetDirty(c.pouch);
            log.Add("wired: KH_PenCall (penManagers / pens x6 new arrays, language, page, colorButtons / colorLabels x6, stateLabel, backLabel, entryLabel), pouch.penPage / penPageBehaviour");
        }

        /// <summary>文言表 (UITranslations.json) を KD_Language へ入れ直す (Menu/11 の Configure と同じ。束縛 Bind は触らない) と、筆ペンの文字の字形を共通フォントへ焼く。</summary>
        static void RefreshLanguage(Ctx c, List<string> log)
        {
            var sb = new StringBuilder();
            KD_LocalizationEditor.Configure(c.language, c.catalog, sb);
            string missing = KD_ShopSetupEditor.MissingGlyphs(BundledGlyphs(c));
            if (missing.Length > 0) { KD_ShopSetupEditor.AddGlyphs(missing, sb); log.Add("glyphs added: " + missing); }
            log.Add("language tables refreshed (Configure; bindings untouched)");
        }

        /// <summary>自前で焼く言語 (glyphs=self) の筆ペンの文言 (変えた見出し settings.group.read を含む)。</summary>
        static string BundledGlyphs(Ctx c)
        {
            var glyphs = new StringBuilder();
            foreach (var lang in c.catalog.languages)
                if (KD_LocalizationEditor.Bundled(lang))
                    foreach (var key in NeededKeys)
                    {
                        var t = TextOrNull(c.catalog, lang.code, key);
                        if (t != null) glyphs.Append(System.Text.RegularExpressions.Regex.Replace(t, "<[^<>]+>", ""));
                    }
            return glyphs.ToString();
        }

        // ================================================================ Pen/9

        [MenuItem(Menu + "9 - Remove")]
        public static void Remove()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, menu = "Pen/9" };
            Ctx c;
            try { c = Resolve(); } catch (Exception e) { Debug.LogError("[KH Pen] PEN_REMOVE_FAIL " + e.Message); return; }
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-remove.unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, true);
            try
            {
                var p = c.pouch;
                var descriptor = Descriptor();
                // references first (they would turn into {fileID: 0} silently)
                p.penPage = null; p.penPageBehaviour = null;
                var page = p.settingsPage.transform.Find(PageName);
                var entry = p.settingsHome.transform.Find(KD_MenuFinishEditor.PenCallButtonName);
                var calls = Object.FindObjectsOfType<KH_PenCall>(true).Where(x => x.gameObject.scene == c.scene).Select(x => x.transform).ToArray();
                // 自作物の木を指す NetworkIDs だけを外す (元からある残骸 = gameObject null の行には触れない)
                var mine = new List<Transform>();
                foreach (var t in calls) if (OwnObject(t)) mine.Add(t);
                if (page != null) mine.Add(page);
                if (entry != null) mine.Add(entry);
                DropNetworkIds(descriptor, mine, r.log);
                foreach (var t in calls)
                {
                    if (OwnObject(t)) { r.log.Add("removed " + ScenePath(t)); Object.DestroyImmediate(t.gameObject); continue; }
                    // 他の部品と同居している (手で動かされた) ときは KH_PenCall だけを外す。proxy → backing の順 (vrc-udonsharp §4)
                    var b = t.GetComponent<KH_PenCall>(); var u = b != null ? UdonSharpEditorUtility.GetBackingUdonBehaviour(b) : null;
                    if (b != null) Object.DestroyImmediate(b);
                    if (u != null) Object.DestroyImmediate(u);
                    r.log.Add("removed the KH_PenCall component from " + ScenePath(t) + " (the object holds other parts; kept. Its NetworkIDs entry, if any, is left for the SDK)");
                }
                p.extraSettingsPages = (p.extraSettingsPages ?? new GameObject[0]).Where(e => e != null && (page == null || e != page.gameObject)).ToArray();
                if (page != null) { Object.DestroyImmediate(page.gameObject); r.log.Add("removed Page_Settings/" + PageName + " and its extraSettingsPages entry"); }
                if (entry != null) { Object.DestroyImmediate(entry.gameObject); r.log.Add("removed settings [筆ペン] " + KD_MenuFinishEditor.PenCallButtonName); }
                r.log.Add("read row kept at the 4 x " + KD_MenuFinishEditor.HillsideReadEntrySize.x + " layout (Credit / Information / WalkIntro stay in their seats; the PenCall seat x "
                    + KD_MenuFinishEditor.HillsidePenCallPos.x + " is left empty)");
                UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
                r.log.Add("cleared KD_TreatPouch.penPage / penPageBehaviour (the heading text " + HeadingKey + " stays: it comes from the catalog)");
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("scene save failed");
                r.status = "REMOVED";
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message); WriteJson(OutDir + "/pen-remove.json", r);
                Debug.LogError("[KH Pen] PEN_REMOVE_FAIL " + e.Message + ". Backup: " + r.backup + " (reopen the scene without saving to discard partial changes)");
                throw;
            }
            WriteJson(OutDir + "/pen-remove.json", r);
            Debug.Log("[KH Pen] PEN_REMOVE_OK " + string.Join(" | ", r.log));
        }

        /// <summary>trees の中の GameObject を指す NetworkIDs の行だけを外す (消す前に呼ぶ。vrc-scene-edit-safety)。gameObject null の既存の残骸は触らない。</summary>
        static void DropNetworkIds(VRCSceneDescriptor descriptor, IEnumerable<Transform> trees, List<string> log)
        {
            if (descriptor == null || descriptor.NetworkIDCollection == null) return;
            var roots = trees.Where(t => t != null).ToArray();
            if (roots.Length == 0) return;
            bool Mine(VRC.SDKBase.Network.NetworkIDPair x) { return x != null && x.gameObject != null && roots.Any(o => x.gameObject.transform.IsChildOf(o)); }
            var ids = descriptor.NetworkIDCollection.Where(Mine).Select(x => x.ID).OrderBy(i => i).ToList();
            if (ids.Count == 0) return;
            int dropped = descriptor.NetworkIDCollection.RemoveAll(Mine);
            EditorUtility.SetDirty(descriptor);
            log.Add("NetworkIDs: dropped " + dropped + " entries of " + string.Join(", ", roots.Select(t => t.name)) + " (IDs " + string.Join(",", ids) + ")");
        }

        // ================================================================ small helpers

        /// <summary>Unity の偽 null (エディタの GetComponent) に ?? を使わない。</summary>
        static T Comp<T>(GameObject go) where T : Component { var x = go.GetComponent<T>(); if (x == null) x = go.AddComponent<T>(); return x; }

        static TextMeshProUGUI LabelOf(GameObject button) { if (button == null) return null; var t = button.transform.Find("Label"); return t != null ? t.GetComponent<TextMeshProUGUI>() : null; }

        static string ScenePath(Transform t) { string s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }

        /// <summary>EnsureButton (角丸・押した感じ・onClick → target.SendCustomEvent(evt)) + 文字の自動縮小 points → ButtonFontSizeMin。</summary>
        static GameObject Btn(Transform parent, string name, Vector2 pos, Vector2 size, string text, Color color, Ctx c, UdonBehaviour target, string evt, float points, List<string> log)
        {
            var sb = new StringBuilder();
            var go = KD_ShopSetupEditor.EnsureButton(parent, name, pos, size, color, c.font, text, target, evt, sb);
            if (sb.Length > 0) log.Add(sb.ToString().Trim());
            var label = LabelOf(go);
            if (label != null)
            {
                label.fontSize = points; label.enableAutoSizing = true; label.fontSizeMax = points; label.fontSizeMin = KD_LocalizationEditor.ButtonFontSizeMin;
                label.overflowMode = TextOverflowModes.Overflow; EditorUtility.SetDirty(label);
            }
            EditorUtility.SetDirty(go);
            return go;
        }

        static TextMeshProUGUI Label(Transform parent, string name, Vector2 pos, Vector2 size, string text, TMP_FontAsset font, float max, float min, TextAlignmentOptions align, bool wrap)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            foreach (var g in go.GetComponents<Graphic>()) if (!(g is TextMeshProUGUI)) Object.DestroyImmediate(g);
            var t = Comp<TextMeshProUGUI>(go);
            t.font = font; t.text = text; t.color = Ink; t.alignment = align; t.raycastTarget = false;
            t.enableWordWrapping = wrap; t.overflowMode = TextOverflowModes.Overflow;   // never Truncate / Ellipsis (vrc-world-ui §10.1)
            t.enableAutoSizing = true; t.fontSizeMax = max; t.fontSizeMin = min; t.fontSize = max; t.richText = true;
            EditorUtility.SetDirty(t);
            return t;
        }

        static void WriteJson(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(o, true));
        }
    }
}
