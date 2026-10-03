using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-005 自動写真とギャラリー (小さい版。docs/plans/2026-10-03-auto-photo-gallery.md §4・§6)。
    ///
    /// 前作から移した写真の仕組み (KD_PhotoCapture / KD_PhotoDirector / KD_GalleryController / KD_PhotoSettings / KD_PhotoViewer /
    /// KD_PhotoBoard) を本番シーンへ配線する。古民家の KD_PhotoSetupEditor (Imported-Unused) は実行しない (前作専用の参照と寝室があるため)。
    ///
    /// 実行順 (統括が直列に。サブエージェントは実行しない):
    ///   Photo/0 - Create Program Asset   KH_PhotoAlbum.asset を作る → U# をコンパイル (unity-compile.sh --udon)
    ///   Photo/1 - Build Photo and Gallery  中核 (撮影カメラ・枠 8 枚・犬の受け口・メニューの写真の面)。**見える物は置かない・撮らない**
    ///   Photo/4 - Place Board, Menu Button and Settings Row (optional)  門の看板 A の隣のボード、ヘッダの [写真]、設定の [写真: ON/OFF]。ここで撮り始める
    ///   Photo/2 - Verify                   読むだけ (Photo/1 の後、Photo/4 の後の両方で通る)
    ///   Photo/3 - Harness                  ClientSim の小さいケース群 (Photo/4 の後)
    ///   Photo/5 - Remove Board, Menu Button and Settings Row  Photo/4 の取り消し (10-07 の切り戻し。撮影も止まる)
    ///   Photo/9 - Remove Photo System     Photo/1 ごと外す (犬の受け口を null へ戻す)
    ///
    /// 名前は KH_PhotoSystem (前作の KD_PhotoSystem にしない: KD_LocalizationEditor.ValidateScene 等が前作のギャラリーの文字を検査しに来るため)。
    /// 文言は UILocalizationBindings.json に **登録しない** (Photo/4 を置かない構成で Bind / Guide/1 の解決が落ちないように)。
    /// 表示中の文は KH_PhotoAlbum が言語に合わせて書く。
    /// </summary>
    public static partial class KH_PhotoGalleryEditor
    {
        const string Menu = "Tools/KominkaHillside/Photo/";
        const string OutDir = "Temp/kh-photo";
        public const string SystemName = "KH_PhotoSystem";
        public const string BoardRootName = "KH_PhotoBoard";
        public const string PageName = "PhotoPage";
        const string CameraName = "PhotoCamera", ViewerName = "KD_PhotoViewer";

        public const string AlbumProgramPath = "Assets/KominkaHillside/Scripts/KH_PhotoAlbum";
        const string TexDir = "Assets/KominkaHillside/Textures/Photo";
        const string MatDir = "Assets/KominkaHillside/Materials/Photo";
        public const string CaptureRTPath = TexDir + "/KH_PhotoCaptureRT.renderTexture";
        public static string SlotRTPath(int i) { return TexDir + "/KH_PhotoSlot" + i + ".renderTexture"; }
        public static string PrintMatPath(int i) { return MatDir + "/M_KH_PhotoPrint" + i + ".mat"; }
        const string ViewerBackMatPath = MatDir + "/M_KH_PhotoViewerBack.mat", ViewerPhotoMatPath = MatDir + "/M_KH_PhotoViewerPhoto.mat";
        const string PrintShaderName = "KominkaDogRun/KD_PhotoPrint", ViewerShaderName = "KominkaDogRun/KD_PhotoViewer";

        // ---------------------------------------------------------------- 枚数・解像度・メモリ (設計 §4.3)
        public const int SlotCount = KH_PhotoAlbum.Slots;   // 8
        /// <summary>RT アセット (雛形) の寸法。実行時は KD_PhotoSettings が端末の寸法で 1 組だけ作り直し、雛形は描かれないまま解放される。</summary>
        public const int TemplateWidth = 640, TemplateHeight = 360;
        public const int PcWidth = 1280, PcHeight = 720, MobileWidth = 640, MobileHeight = 360;
        const int CaptureDepthBits = 24;
        /// <summary>計算値の上限 (MiB)。撮影用 RT の深度は最悪の D32_SFloat_S8 (8 byte/px。前作 2026-08-14 の PC 実測) で数える。</summary>
        public const float PcBudgetMiB = 48f, MobileBudgetMiB = 12f;
        public static float BudgetMiB(int w, int h)
        {
            float px = w * (float)h;
            return (SlotCount * px * 4f + px * (4f + 8f)) / (1024f * 1024f);
        }

        // ---------------------------------------------------------------- 板の吸着 (前作 KD_PhotoSetupEditor と同じ値。隙間 40 mm の半分より小さい余白)
        public const float AttachDistance = 0.35f, AttachAimMargin = 0.012f;

        // ---------------------------------------------------------------- 撮影カメラの cullingMask (許可リスト。裏山のレイヤーで作り直した 2026-10-03)
        // シーンの実測 (2026-10-03、Canvas の layer): 手元メニュー KD_TreatPouchPanel / KD_TouchLauncher / 眠りの VrHint = 17 Walkthrough、
        // 手元メニューの一部と眠りの印 = 2 Ignore Raycast、案内の吹き出し KD_HintDirector = 5 UI、洗い場の自分の床 = 22。
        // 看板 (KH_GuideBoards)・洗い場の板・おじいさんの売り場は 0 Default (世界の物として写ってよい)。犬と首輪 = 14、おやつ・道具・ディスク = 13、地形・木 = 11。
        public static readonly int[] PhotoLayers = { 0, 8, 9, 11, 13, 14, 18 };
        /// <summary>写ってはいけないもののレイヤー (Verify が名前ごと見る)。17 には洗い場の棚・台も居るので、背景の棚は写らない (小さい版では洗い場を撮らない)。</summary>
        public static readonly int[] MustExcludeLayers = { 2, 4, 5, 10, 12, 17, 19, 22 };
        public static readonly string[] PhotoLayerNames = { "Default", "Interactive", "Player", "Environment", "Pickup", "PickupNoEnvironment", "MirrorReflection" };
        public static int PhotoCullingMask { get { int m = 0; foreach (var l in PhotoLayers) m |= 1 << l; return m; } }
        /// <summary>カメラを壁・地面の手前へ引くときに当てる地形 (Default + Environment)。犬・人は入れない (前作と同じ)。</summary>
        public const int CameraBlockMask = (1 << 0) | (1 << 11);

        // ---------------------------------------------------------------- 犬 (名簿の順 = KD_DogPack.dogIndex = KH_FrisbeeCatch.dogIndex)
        public static readonly string[] Keys = { "Corgi", "Pome", "Lab", "Dachs", "Shiba", "Samoyed" };

        // ---------------------------------------------------------------- メニューの写真の面 (Page_Settings/PhotoPage、1140 x 700、上端はタブの下 201)
        public static readonly Vector2 PageStatePos = new Vector2(-170f, 170f), PageStateSize = new Vector2(700f, 44f);
        public static readonly Vector2 PageHelpPos = new Vector2(300f, 170f), PageHelpSize = new Vector2(60f, 44f);
        public static readonly Vector2 PageBackPos = new Vector2(450f, 170f), PageBackSize = new Vector2(220f, 44f);
        public static readonly Vector2 ThumbSize = new Vector2(240f, 135f);
        const float ThumbPitchX = 252f, ThumbRow0Y = 66f, ThumbRow1Y = -81f, ThumbBorder = 4f;
        public static Vector2 ThumbPos(int k) { int col = k % 4, row = k / 4; return new Vector2((col - 1.5f) * ThumbPitchX, row == 0 ? ThumbRow0Y : ThumbRow1Y); }
        public static readonly Vector2 NewMarkSize = new Vector2(100f, 30f);
        public static readonly Vector2 PageSettingsPos = new Vector2(0f, -205f), PageSettingsSize = new Vector2(280f, 50f);
        public static readonly Vector2 LargePhotoPos = new Vector2(0f, -30f), LargePhotoSize = new Vector2(760f, 427.5f), LargeBackdropSize = new Vector2(1110f, 470f);
        public static readonly Vector2 HelpBodyPos = new Vector2(0f, -40f), HelpBodySize = new Vector2(1020f, 370f), HelpTitlePos = new Vector2(-170f, 170f);

        // ---------------------------------------------------------------- ヘッダの [写真] (Photo/4)
        public static readonly Vector2 HeaderButtonPos = new Vector2(-65f, 314f), HeaderButtonSize = new Vector2(120f, 44f);
        /// <summary>[写真] があるときの見出しの箱 (KD_HandMenuScrollEditor.LayoutFrame と同じ値。-550〜-150)。</summary>
        public const float TitleWithButtonX = -350f, TitleWithButtonWidth = 400f;

        static readonly Color ThumbFrameColor = new Color(0.30f, 0.22f, 0.15f);
        static readonly Color Ink = new Color(0.97f, 0.95f, 0.90f);

        // ================================================================ context

        class Ctx
        {
            public UnityEngine.SceneManagement.Scene scene;
            public KD_TreatPouch pouch; public KD_Language language; public TMP_FontAsset font; public UdonBehaviour pouchUdon;
            public KD_LocalizationEditor.Catalog catalog;
            public Transform[] dogs = new Transform[6]; public Transform[] heads = new Transform[6];
            public KD_DogFetchController[] fetch = new KD_DogFetchController[6];
            public KD_DogWanderController[] wander = new KD_DogWanderController[6];
            public KD_DogPetController[] pet = new KD_DogPetController[6];
            public KH_FrisbeeCatch[] frisbee = new KH_FrisbeeCatch[6];
            public KD_GroupActivities activities;
            public List<string> notes = new List<string>();
        }

        static Ctx Resolve()
        {
            var c = new Ctx { scene = EditorSceneManager.GetActiveScene() };
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (pouches.Length != 1) throw new InvalidOperationException("exactly one KD_TreatPouch required (" + pouches.Length + "); nothing changed");
            c.pouch = pouches[0];
            c.language = c.pouch.language;
            if (c.language == null) throw new InvalidOperationException("KD_TreatPouch.language not wired (Menu/11); nothing changed");
            c.font = c.pouch.sectionTitleLabel != null ? c.pouch.sectionTitleLabel.font : null;
            if (c.font == null) throw new InvalidOperationException("the menu's shared font (sectionTitleLabel.font) is missing; nothing changed");
            if (c.pouch.settingsPage == null || c.pouch.settingsHome == null || c.pouch.panelRoot == null) throw new InvalidOperationException("menu panel / settings page missing; nothing changed");
            c.pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(c.pouch);
            if (c.pouchUdon == null) throw new InvalidOperationException("KD_TreatPouch backing missing; nothing changed");
            c.catalog = KD_LocalizationEditor.Load();
            foreach (var key in NeededKeys)
                foreach (var lang in c.catalog.languages)
                {
                    var e = lang.texts.FirstOrDefault(x => x.key == key);
                    if (e == null || string.IsNullOrWhiteSpace(e.text)) throw new InvalidOperationException("text missing in " + KD_MenuFinishEditor.CatalogPath + ": " + lang.code + ":" + key + "; nothing changed");
                }
            if (c.language.keys == null || NeededKeys.Any(k => Array.IndexOf(c.language.keys, k) < 0))
                c.notes.Add("NOTE KD_Language does not hold every photo key yet: Photo/1 runs KD_LocalizationEditor.Configure (the same table refresh as Menu/11)");
            for (int i = 0; i < Keys.Length; i++)
            {
                string k = Keys[i];
                c.dogs[i] = One<Transform>("KD_Dog_" + k);
                c.fetch[i] = One<KD_DogFetchController>("KD_FetchSystem_" + k);
                c.wander[i] = One<KD_DogWanderController>("KD_WanderSystem_" + k);
                c.pet[i] = One<KD_DogPetController>("KD_PetSystem_" + k);
                c.frisbee[i] = One<KH_FrisbeeCatch>("KH_FrisbeeCatch_" + k);
                if (c.fetch[i].dogIndex != i || c.wander[i].dogIndex != i || c.frisbee[i].dogIndex != i)
                    throw new InvalidOperationException("dogIndex of " + k + " is not " + i + " (fetch " + c.fetch[i].dogIndex + ", wander " + c.wander[i].dogIndex + ", frisbee " + c.frisbee[i].dogIndex + "); nothing changed");
                if (c.frisbee[i].wander != c.wander[i]) throw new InvalidOperationException("KH_FrisbeeCatch_" + k + ".wander is not KD_WanderSystem_" + k + " (the frisbee photo hook reads wander.photoDirector); nothing changed");
                c.heads[i] = c.pet[i].headAnchor != null ? c.pet[i].headAnchor : FindChild(c.dogs[i], "KD_PetAnchor_Head");
                if (c.heads[i] == null) c.notes.Add("WARN no head anchor for " + k + ": framing falls back to the root (+0.3 m)");
            }
            c.activities = c.pouch.manager != null ? c.pouch.manager.activities : null;
            if (c.activities == null) c.notes.Add("WARN KH_LeashManager.activities missing: the trainer group photo is not wired");
            return c;
        }

        /// <summary>このメニューが要る文言 (13 言語とも本文がある)。新しいキーは photo.empty / photo.new / photo.menu の 3 つ。</summary>
        public static readonly string[] NeededKeys =
        {
            "photo.menu", "photo.empty", "photo.new", "photo.settings.off", "photo.settings.loading", "photo.settings.failed",
            "photo.help.mark", "photo.help.title", "photo.help.body", "photo.help.close", "gallery.caption",
            "KD_PhotoBoard.InteractWord", "KD_PhotoViewer.InteractWord", "KD_TreatPouch.WordSettings", "gift.back",
        };

        static T One<T>(string name) where T : Component
        {
            var go = KD_SceneObjects.Find(name, out int count);
            if (go == null || count != 1) throw new InvalidOperationException(name + " x" + count + " in the scene (exactly 1 required); nothing changed");
            var comp = go.GetComponent<T>();
            if (comp == null) throw new InvalidOperationException(name + " has no " + typeof(T).Name + "; nothing changed");
            return comp;
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static GameObject SceneRoot(UnityEngine.SceneManagement.Scene scene, string name, out int count)
        {
            var all = scene.GetRootGameObjects().Where(g => g.name == name).ToArray();
            count = all.Length;
            return all.Length == 1 ? all[0] : null;
        }

        static string ProgramMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AlbumProgramPath + ".asset") == null) return AlbumProgramPath + ".asset missing: run Photo/0, compile U# (unity-compile.sh --udon), then this menu";
            foreach (var n in new[] { "KD_PhotoCapture", "KD_PhotoDirector", "KD_GalleryController", "KD_PhotoSettings", "KD_PhotoViewer", "KD_PhotoBoard" })
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/" + n + ".asset") == null) return n + ".asset missing (the copied photo scripts need their program assets)";
            return null;
        }

        // ================================================================ Photo/0

        [MenuItem(Menu + "0 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AlbumProgramPath + ".asset") != null) { Debug.Log("[KH Photo] PHOTO_PROGRAM_OK already exists: " + AlbumProgramPath + ".asset"); return; }
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AlbumProgramPath + ".cs");
            if (script == null) { Debug.LogError("[KH Photo] PHOTO_PROGRAM_FAIL " + AlbumProgramPath + ".cs not imported yet (refresh first)"); return; }
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, AlbumProgramPath + ".asset");
            AssetDatabase.SaveAssets();
            Debug.Log("[KH Photo] PHOTO_PROGRAM_OK created " + AlbumProgramPath + ".asset. Compile U# (unity-compile.sh --udon) before Photo/1.");
        }

        // ================================================================ Photo/1

        [Serializable] class BuildReport
        {
            public string utc, scene, status, backup, menu;
            public List<string> log = new List<string>();
        }

        [MenuItem(Menu + "1 - Build Photo and Gallery")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            string missing = ProgramMissing();
            if (missing != null) { Debug.LogError("[KH Photo] PHOTO_BUILD_FAIL " + missing + "; nothing changed"); return; }
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, menu = "Photo/1" };
            var c = Resolve();
            r.log.AddRange(c.notes);
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-photo-build.unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, true);
            try
            {
                var assets = EnsureAssets(r.log);
                var sys = BuildSystem(c, assets, r.log);
                BuildMenuPage(c, sys, r.log);
                WireHooks(c, sys.director, r.log);
                // Photo/4 の部品が既にあれば配線し直す (Photo/1 の再実行で外さない)
                RewireSurfaces(c, sys, r.log);
                RefreshLanguage(c, r.log);
                CopyAll(c, sys);
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("scene save failed");
                AssetDatabase.SaveAssets();
                r.status = "BUILT";
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message);
                WriteJson(OutDir + "/build.json", r);
                Debug.LogError("[KH Photo] PHOTO_BUILD_FAIL " + e.Message + ". The scene before this run is at " + r.backup + "; reopen the scene without saving to discard partial changes.");
                throw;
            }
            WriteJson(OutDir + "/build.json", r);
            Debug.Log("[KH Photo] PHOTO_BUILD_OK core built (nothing visible, no capture until Photo/4). " + r.log.Count + " log lines in " + OutDir + "/build.json. Next: Course/27 (glyphs), Photo/2, then the optional Photo/4.");
        }

        // ---------------------------------------------------------------- assets

        class Assets { public RenderTexture capture; public RenderTexture[] slots = new RenderTexture[SlotCount]; public Material[] prints = new Material[SlotCount]; public Material viewerBack, viewerPhoto; }

        static Assets EnsureAssets(List<string> log)
        {
            var a = new Assets();
            var print = Shader.Find(PrintShaderName); if (print == null) throw new InvalidOperationException("shader " + PrintShaderName + " not found");
            var viewer = Shader.Find(ViewerShaderName); if (viewer == null) throw new InvalidOperationException("shader " + ViewerShaderName + " not found");
            EnsureFolder(TexDir); EnsureFolder(MatDir);
            a.capture = EnsureRT(CaptureRTPath, CaptureDepthBits, log);
            for (int i = 0; i < SlotCount; i++)
            {
                a.slots[i] = EnsureRT(SlotRTPath(i), 0, log);
                var m = EnsureMat(PrintMatPath(i), print, log);
                m.mainTexture = a.slots[i];
                m.SetFloat("_AttachDistance", AttachDistance); m.SetFloat("_AttachMargin", AttachAimMargin);
                if (m.HasProperty("_DebugProbe")) m.SetFloat("_DebugProbe", 0f);
                if (m.HasProperty("_HideFromEyes")) m.SetFloat("_HideFromEyes", 0f);
                EditorUtility.SetDirty(m);
                a.prints[i] = m;
            }
            a.viewerBack = EnsureMat(ViewerBackMatPath, viewer, log); a.viewerBack.SetColor("_Color", Color.black); a.viewerBack.renderQueue = 4000; EditorUtility.SetDirty(a.viewerBack);
            a.viewerPhoto = EnsureMat(ViewerPhotoMatPath, viewer, log); a.viewerPhoto.SetColor("_Color", Color.white); a.viewerPhoto.renderQueue = 4001; EditorUtility.SetDirty(a.viewerPhoto);
            return a;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'), leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>雛形の RT。**作り直さない** (材質が GUID で参照する)。書式が違えば Release してその場で直す。</summary>
        static RenderTexture EnsureRT(string path, int depth, List<string> log)
        {
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (rt == null)
            {
                rt = new RenderTexture(TemplateWidth, TemplateHeight, depth, RenderTextureFormat.ARGB32) { useMipMap = false, antiAliasing = 1 };
                AssetDatabase.CreateAsset(rt, path);
                log.Add("created " + path + " " + TemplateWidth + "x" + TemplateHeight + " depth " + depth);
                return rt;
            }
            bool ok = rt.width == TemplateWidth && rt.height == TemplateHeight && (depth == 0 ? rt.depth == 0 : rt.depth >= depth) && !rt.useMipMap && rt.antiAliasing == 1 && rt.format == RenderTextureFormat.ARGB32;
            if (!ok)
            {
                rt.Release(); rt.width = TemplateWidth; rt.height = TemplateHeight; rt.depth = depth; rt.useMipMap = false; rt.antiAliasing = 1; rt.format = RenderTextureFormat.ARGB32;
                EditorUtility.SetDirty(rt);
                log.Add("retuned " + path + " in place (GUID kept)");
            }
            return rt;
        }

        static Material EnsureMat(string path, Shader shader, List<string> log)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); log.Add("created " + path); }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        // ---------------------------------------------------------------- system

        class Sys
        {
            public GameObject root; public Camera camera;
            public KD_PhotoCapture capture; public KD_PhotoDirector director; public KD_GalleryController gallery; public KD_PhotoSettings settings;
            public KD_PhotoViewer viewer; public KH_PhotoAlbum album;
        }

        static T EnsureUsc<T>(GameObject go, List<string> log) where T : UdonSharpBehaviour
        {
            var comp = go.GetComponent<T>();
            if (comp == null) { comp = go.AddUdonSharpComponent<T>(); log.Add("added " + typeof(T).Name + " on " + go.name); }
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(comp);
            if (backing == null) throw new InvalidOperationException(typeof(T).Name + " backing missing on " + go.name + " (compile U# and run again)");
            if (backing.SyncMethod != Networking.SyncType.None) { backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing); log.Add(typeof(T).Name + " sync -> None"); }
            return comp;
        }

        static Sys FindSystem(Ctx c)
        {
            var root = SceneRoot(c.scene, SystemName, out int n);
            if (n > 1) throw new InvalidOperationException(n + " " + SystemName + " at the scene root (keep one); nothing changed");
            KD_SceneObjects.Find(SystemName, out int anywhere);
            if (anywhere != n) throw new InvalidOperationException(SystemName + " exists below another object; nothing changed");
            if (root == null) return null;
            var s = new Sys { root = root };
            s.capture = root.GetComponent<KD_PhotoCapture>(); s.director = root.GetComponent<KD_PhotoDirector>(); s.gallery = root.GetComponent<KD_GalleryController>();
            s.settings = root.GetComponent<KD_PhotoSettings>(); s.album = root.GetComponent<KH_PhotoAlbum>();
            var camT = root.transform.Find(CameraName); s.camera = camT != null ? camT.GetComponent<Camera>() : null;
            var vT = root.transform.Find(ViewerName); s.viewer = vT != null ? vT.GetComponent<KD_PhotoViewer>() : null;
            return s;
        }

        static Sys BuildSystem(Ctx c, Assets a, List<string> log)
        {
            var root = SceneRoot(c.scene, SystemName, out int n);
            if (n > 1) throw new InvalidOperationException(n + " " + SystemName + " at the scene root (keep one)");
            if (root == null) { root = new GameObject(SystemName); SceneManagerMove(root, c.scene); log.Add("created " + SystemName); }
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
            root.layer = 0;
            var s = new Sys { root = root };

            // ---- camera: disabled except for the single frame KD_PhotoCapture renders ----
            var camT = root.transform.Find(CameraName);
            if (camT == null) { camT = new GameObject(CameraName).transform; camT.SetParent(root.transform, false); log.Add("created " + CameraName); }
            var cam = camT.GetComponent<Camera>(); if (cam == null) cam = camT.gameObject.AddComponent<Camera>();
            foreach (var listener in camT.GetComponents<AudioListener>()) UnityEngine.Object.DestroyImmediate(listener);
            cam.enabled = false;
            cam.fieldOfView = 50f; cam.nearClipPlane = 0.01f; cam.farClipPlane = 60f;
            cam.allowHDR = false; cam.allowMSAA = false; cam.useOcclusionCulling = true;
            cam.clearFlags = CameraClearFlags.Skybox; cam.depth = -10f;
            cam.targetTexture = a.capture;
            cam.cullingMask = PhotoCullingMask;   // 許可リスト。写ってはいけない物を決める値なので毎回書く (前作 BUG-CAM-001)
            s.camera = cam;
            log.Add("camera: FOV 50 / near 0.01 / far 60 / HDR off / MSAA off / disabled / cullingMask 0x" + PhotoCullingMask.ToString("X8") + " (" + DescribeMask(PhotoCullingMask) + ")");

            // ---- viewer (phones: tap a photo -> in front of the eyes; renderers on UI(5) so photos never show it) ----
            var vT = root.transform.Find(ViewerName);
            if (vT == null) { vT = new GameObject(ViewerName).transform; vT.SetParent(root.transform, false); log.Add("created " + ViewerName); }
            vT.gameObject.layer = 0;
            var box = vT.GetComponent<BoxCollider>(); if (box == null) box = vT.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 3f, 0.05f); box.center = Vector3.zero; box.isTrigger = true; box.enabled = false;
            var panelT = vT.Find("Panel"); if (panelT == null) { panelT = new GameObject("Panel").transform; panelT.SetParent(vT, false); }
            panelT.gameObject.layer = 5;
            Transform Quad(string name, float w, float h, float z, Material m)
            {
                var q = panelT.Find(name);
                if (q == null) { var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name; go.transform.SetParent(panelT, false); q = go.transform; }
                foreach (var col in q.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
                q.localPosition = new Vector3(0f, 0f, z); q.localRotation = Quaternion.identity; q.localScale = new Vector3(w, h, 1f);
                q.gameObject.layer = 5;
                var mr = q.GetComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                return q;
            }
            Quad("Backdrop", 4f, 3f, 0.02f, a.viewerBack);
            var photoQ = Quad("Photo", 0.78f * 16f / 9f, 0.78f, 0f, a.viewerPhoto);
            panelT.gameObject.SetActive(false);

            // ---- U# ----
            s.capture = EnsureUsc<KD_PhotoCapture>(root, log);
            s.gallery = EnsureUsc<KD_GalleryController>(root, log);
            s.director = EnsureUsc<KD_PhotoDirector>(root, log);
            s.settings = EnsureUsc<KD_PhotoSettings>(root, log);
            s.album = EnsureUsc<KH_PhotoAlbum>(root, log);
            s.viewer = EnsureUsc<KD_PhotoViewer>(vT.gameObject, log);

            s.capture.photoCamera = cam; s.capture.captureTexture = a.capture; s.capture.settings = s.settings; s.capture.capturedListener = s.album;

            s.gallery.slotTextures = (RenderTexture[])a.slots.Clone();
            s.gallery.boards = null;                // the album owns the board (newest first), the gallery only hands out slots
            s.gallery.slotGroupCount = 1;           // one category: the oldest photo is replaced (設計 §4.1)
            s.gallery.slotGroups = null;

            var d = s.director;
            d.photoCapture = s.capture; d.photoCamera = cam; d.gallery = s.gallery; d.bedroom = null;
            d.dogRoots = (Transform[])c.dogs.Clone(); d.dogHeadAnchors = (Transform[])c.heads.Clone();
            d.fetchControllers = (KD_DogFetchController[])c.fetch.Clone(); d.petControllers = (KD_DogPetController[])c.pet.Clone(); d.wanderControllers = (KD_DogWanderController[])c.wander.Clone();
            d.cameraBlockMask = CameraBlockMask;
            d.chaseShotEnabled = true; d.playShotEnabled = true;
            d.tickInterval = 0.1f;
            d.frisbeeShotEnabled = true; d.frisbeeShotDelay = 0.1f; d.frisbeeShotCooldown = 20f;

            var st = s.settings;
            st.capture = s.capture; st.slots = (RenderTexture[])a.slots.Clone(); st.boards = null; st.galleries = new[] { s.gallery }; st.viewer = s.viewer; st.language = c.language;
            st.stateLabel = null; st.highButton = null; st.standardButton = null; st.offButton = null; st.boardButtons = null; st.boardStateLabels = null;
            st.highEnabled = false; st.standardWidth = PcWidth; st.standardHeight = PcHeight; st.mobileWidth = MobileWidth; st.mobileHeight = MobileHeight;
            st.stateListener = s.album;

            var v = s.viewer;
            v.slotTextures = (RenderTexture[])a.slots.Clone(); v.photoRenderer = photoQ.GetComponent<MeshRenderer>(); v.panelRoot = panelT.gameObject; v.closeCollider = box; v.pouch = c.pouch; v.distance = 0.8f;
            var vUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(v);
            vUdon.interactText = KD_LocalizationEditor.Text(c.catalog, c.catalog.fallbackLanguage, "KD_PhotoViewer.InteractWord"); vUdon.proximity = 2f; EditorUtility.SetDirty(vUdon);

            var al = s.album;
            al.gallery = s.gallery; al.settings = s.settings; al.capture = s.capture; al.language = c.language;
            al.directorUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(s.director);
            al.viewerUdon = vUdon;
            al.onColor = c.pouch.equipOnColor; al.offColor = c.pouch.tabOffColor;
            log.Add("system: 8 slots (template " + TemplateWidth + "x" + TemplateHeight + "; runtime PC " + PcWidth + "x" + PcHeight + " / Android・iOS " + MobileWidth + "x" + MobileHeight + "), 1 category, 2K off, director tick 0.1 s, frisbee 0.1 s / 20 s");
            log.Add("memory (computed): PC " + BudgetMiB(PcWidth, PcHeight).ToString("F1") + " MiB / Quest・iOS " + BudgetMiB(MobileWidth, MobileHeight).ToString("F1") + " MiB");
            return s;
        }

        static void SceneManagerMove(GameObject go, UnityEngine.SceneManagement.Scene scene)
        {
            if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }

        // ---------------------------------------------------------------- dog hooks

        static void WireHooks(Ctx c, KD_PhotoDirector director, List<string> log)
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                c.fetch[i].photoDirector = director; EditorUtility.SetDirty(c.fetch[i]);
                c.wander[i].photoDirector = director; EditorUtility.SetDirty(c.wander[i]);
            }
            if (c.activities != null) { c.activities.photos = director; EditorUtility.SetDirty(c.activities); }
            log.Add("hooks: fetch.photoDirector x6, wander.photoDirector x6 (the frisbee hook reads it), KD_GroupActivities.photos" + (c.activities != null ? "" : " (missing)"));
        }

        static void UnwireHooks(Ctx c, List<string> log)
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                c.fetch[i].photoDirector = null; EditorUtility.SetDirty(c.fetch[i]); UdonSharpEditorUtility.CopyProxyToUdon(c.fetch[i]);
                c.wander[i].photoDirector = null; EditorUtility.SetDirty(c.wander[i]); UdonSharpEditorUtility.CopyProxyToUdon(c.wander[i]);
            }
            if (c.activities != null) { c.activities.photos = null; EditorUtility.SetDirty(c.activities); UdonSharpEditorUtility.CopyProxyToUdon(c.activities); }
            log.Add("hooks cleared (null = no photos, the pre-photo state)");
        }

        static void CopyAll(Ctx c, Sys s)
        {
            foreach (UdonSharpBehaviour b in new UdonSharpBehaviour[] { s.capture, s.gallery, s.director, s.settings, s.viewer, s.album, c.pouch, c.language })
            { UdonSharpEditorUtility.CopyProxyToUdon(b); EditorUtility.SetDirty(b); }
            for (int i = 0; i < Keys.Length; i++) { UdonSharpEditorUtility.CopyProxyToUdon(c.fetch[i]); UdonSharpEditorUtility.CopyProxyToUdon(c.wander[i]); }
            if (c.activities != null) UdonSharpEditorUtility.CopyProxyToUdon(c.activities);
            var board = SceneRoot(c.scene, BoardRootName, out _);
            if (board != null) foreach (var tap in board.GetComponentsInChildren<KD_PhotoBoard>(true)) { UdonSharpEditorUtility.CopyProxyToUdon(tap); EditorUtility.SetDirty(tap); }
        }

        /// <summary>文言表 (UITranslations.json) を KD_Language へ入れ直す (Menu/11 の Configure と同じ。束縛 Bind は触らない) と、写真の文字の字形を共通フォントへ焼く。</summary>
        static void RefreshLanguage(Ctx c, List<string> log)
        {
            var sb = new StringBuilder();
            KD_LocalizationEditor.Configure(c.language, c.catalog, sb);
            var glyphs = new StringBuilder();
            foreach (var lang in c.catalog.languages)
                if (KD_LocalizationEditor.Bundled(lang))
                    foreach (var key in NeededKeys) glyphs.Append(System.Text.RegularExpressions.Regex.Replace(KD_LocalizationEditor.Text(c.catalog, lang.code, key), "<[^<>]+>", ""));
            glyphs.Append("…: ONOFF");
            string missing = KD_ShopSetupEditor.MissingGlyphs(glyphs.ToString());
            if (missing.Length > 0) { KD_ShopSetupEditor.AddGlyphs(missing, sb); log.Add("glyphs added: " + missing); }
            log.Add("language tables refreshed (Configure; bindings untouched)");
        }

        // ---------------------------------------------------------------- menu page (Page_Settings/PhotoPage)

        static void BuildMenuPage(Ctx c, Sys s, List<string> log)
        {
            var p = c.pouch;
            var albumUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(s.album);
            string T(string key) { return KD_LocalizationEditor.Text(c.catalog, c.catalog.fallbackLanguage, key); }
            var page = KD_ShopSetupEditor.EnsureUiChild(p.settingsPage.transform, PageName, Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            var state = Label(page.transform, "State", PageStatePos, PageStateSize, T("photo.empty"), c.font, 22f, 14f, TextAlignmentOptions.MidlineLeft, false);
            var help = Btn(page.transform, "Help", PageHelpPos, PageHelpSize, T("photo.help.mark"), c, albumUdon, "_MenuHelpOpen", log);
            var back = Btn(page.transform, "Back", PageBackPos, PageBackSize, T("gift.back"), c, c.pouchUdon, "_PhotoBack", log);
            var thumbs = new RawImage[SlotCount]; var thumbButtons = new GameObject[SlotCount];
            for (int k = 0; k < SlotCount; k++)
            {
                var b = PlainButton(page.transform, "Thumb" + k, ThumbPos(k), ThumbSize + Vector2.one * (ThumbBorder * 2f), ThumbFrameColor, albumUdon, "_MenuPick" + k, log);
                var raw = RawChild(b.transform, "Photo", Vector2.zero, ThumbSize);
                thumbs[k] = raw; thumbButtons[k] = b; b.SetActive(false);
            }
            // 「新しい」は左上 (位置 0) の縮小表示の左上の角。縮小表示より後の兄弟 = 上に描く
            var t0 = ThumbPos(0);
            var mark = KD_ShopSetupEditor.EnsureUiChild(page.transform, "NewMark", new Vector2(t0.x - ThumbSize.x * 0.5f + NewMarkSize.x * 0.5f - 6f, t0.y + ThumbSize.y * 0.5f - NewMarkSize.y * 0.5f + 8f), NewMarkSize);
            var markImg = Comp<Image>(mark); markImg.color = KD_UiSpriteEditor.AccentGold; markImg.raycastTarget = false; KD_UiSpriteEditor.ApplyRoundSkin(markImg, KD_UiSpriteEditor.LoadRound());
            var markLabel = Label(mark.transform, "Label", Vector2.zero, NewMarkSize - new Vector2(8f, 2f), T("photo.new"), c.font, 18f, 12f, TextAlignmentOptions.Center, false);
            markLabel.color = new Color(0.17f, 0.13f, 0.10f);
            mark.transform.SetAsLastSibling(); mark.SetActive(false);
            var toSettings = Btn(page.transform, "SettingsButton", PageSettingsPos, PageSettingsSize, T("KD_TreatPouch.WordSettings"), c, c.pouchUdon, "_PhotoToSettings", log);
            toSettings.SetActive(false);

            // ---- 大きく (縮小表示を押す)。背板は面全体の押せる面を塞ぐ (裏の縮小表示へ通さない) ----
            var large = KD_ShopSetupEditor.EnsureUiChild(page.transform, "Large", Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            var backdrop = KD_ShopSetupEditor.EnsureUiChild(large.transform, "Backdrop", new Vector2(0f, -20f), LargeBackdropSize);
            var bdImg = Comp<Image>(backdrop); bdImg.color = new Color(0.08f, 0.07f, 0.06f, 0.96f); bdImg.raycastTarget = true; KD_UiSpriteEditor.ApplyFlatSkin(bdImg);
            var largeRaw = RawChild(large.transform, "Photo", LargePhotoPos, LargePhotoSize);
            var largeClose = Btn(large.transform, "Close", PageBackPos, PageBackSize, T("photo.help.close"), c, albumUdon, "_MenuLargeClose", log);
            large.transform.SetAsLastSibling(); large.SetActive(false);

            // ---- 写真のしくみ ([?]) ----
            var helpRoot = KD_ShopSetupEditor.EnsureUiChild(page.transform, "HelpPanel", Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            var hb = KD_ShopSetupEditor.EnsureUiChild(helpRoot.transform, "Backdrop", new Vector2(0f, -20f), LargeBackdropSize);
            var hbImg = Comp<Image>(hb); hbImg.color = new Color(0.12f, 0.10f, 0.08f, 0.98f); hbImg.raycastTarget = true; KD_UiSpriteEditor.ApplyFlatSkin(hbImg);
            var helpTitle = Label(helpRoot.transform, "Title", HelpTitlePos, PageStateSize, T("photo.help.title"), c.font, 24f, 16f, TextAlignmentOptions.MidlineLeft, false);
            helpTitle.color = KD_UiSpriteEditor.AccentGold;
            var helpBody = Label(helpRoot.transform, "Body", HelpBodyPos, HelpBodySize, T("photo.help.body"), c.font, 22f, 14f, TextAlignmentOptions.TopLeft, true);
            var helpClose = Btn(helpRoot.transform, "Close", PageBackPos, PageBackSize, T("photo.help.close"), c, albumUdon, "_MenuHelpClose", log);
            helpRoot.transform.SetAsLastSibling(); helpRoot.SetActive(false);
            page.SetActive(false);

            var pages = new List<GameObject>();
            foreach (var e in p.extraSettingsPages ?? new GameObject[0]) if (e != null && !pages.Contains(e)) pages.Add(e);
            if (!pages.Contains(page)) pages.Add(page);
            p.extraSettingsPages = pages.ToArray();
            p.photoPage = page; p.photoAlbum = s.album;
            EditorUtility.SetDirty(p);

            var al = s.album;
            al.menuPage = page; al.menuThumbs = thumbs; al.menuThumbButtons = thumbButtons;
            al.menuNewMark = mark; al.menuNewLabel = markLabel; al.menuState = state;
            al.menuSettingsButton = toSettings; al.menuSettingsLabel = LabelOf(toSettings);
            al.menuBackLabel = LabelOf(back); al.menuHelpMarkLabel = LabelOf(help);
            al.menuHelpRoot = helpRoot; al.menuHelpTitle = helpTitle; al.menuHelpBody = helpBody; al.menuHelpCloseLabel = LabelOf(helpClose);
            al.menuLargeRoot = large; al.menuLarge = largeRaw; al.menuLargeCloseLabel = LabelOf(largeClose);
            log.Add("menu page: Page_Settings/" + PageName + " (registered in extraSettingsPages, hidden): 4 x 2 thumbnails " + ThumbSize.x + "x" + ThumbSize.y + ", large " + LargePhotoSize.x + "x" + LargePhotoSize.y + ", [?] help, [back] -> _PhotoBack. Layer " + LayerMask.LayerToName(page.layer) + " (excluded from photos)");
        }

        /// <summary>Unity の偽 null (エディタの GetComponent) に ?? を使わない。</summary>
        static T Comp<T>(GameObject go) where T : Component { var x = go.GetComponent<T>(); if (x == null) x = go.AddComponent<T>(); return x; }

        static TextMeshProUGUI LabelOf(GameObject button) { var t = button.transform.Find("Label"); return t != null ? t.GetComponent<TextMeshProUGUI>() : null; }

        static GameObject Btn(Transform parent, string name, Vector2 pos, Vector2 size, string text, Ctx c, UdonBehaviour target, string evt, List<string> log)
        {
            var sb = new StringBuilder();
            var go = KD_ShopSetupEditor.EnsureButton(parent, name, pos, size, KD_UiSpriteEditor.ActionNeutral, c.font, text, target, evt, sb);
            if (sb.Length > 0) log.Add(sb.ToString().Trim());
            var label = LabelOf(go);
            if (label != null) { label.fontSize = 20f; label.enableAutoSizing = true; label.fontSizeMax = 20f; label.fontSizeMin = KD_LocalizationEditor.ButtonFontSizeMin; EditorUtility.SetDirty(label); }
            return go;
        }

        /// <summary>文字の無い押せる面 (縮小表示の枠)。onClick は SendCustomEvent 1 本だけ (毎回張り直す)。</summary>
        static GameObject PlainButton(Transform parent, string name, Vector2 pos, Vector2 size, Color color, UdonBehaviour target, string evt, List<string> log)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var img = Comp<Image>(go); img.color = color; img.raycastTarget = true;
            KD_UiSpriteEditor.ApplyRoundSkin(img, KD_UiSpriteEditor.LoadRound());
            var btn = Comp<Button>(go); btn.targetGraphic = img; KD_UiSpriteEditor.ApplyButtonFeedback(btn);
            var stale = go.transform.Find("Label"); if (stale != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);
            for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(btn.onClick, i);
            UnityEventTools.AddStringPersistentListener(btn.onClick, new UnityEngine.Events.UnityAction<string>(target.SendCustomEvent), evt);
            return go;
        }

        static RawImage RawChild(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            foreach (var g in go.GetComponents<Graphic>()) if (!(g is RawImage)) UnityEngine.Object.DestroyImmediate(g);
            var raw = Comp<RawImage>(go);
            raw.texture = null; raw.color = Color.white; raw.raycastTarget = false; raw.uvRect = new Rect(0f, 0f, 1f, 1f);
            return raw;
        }

        static TextMeshProUGUI Label(Transform parent, string name, Vector2 pos, Vector2 size, string text, TMP_FontAsset font, float max, float min, TextAlignmentOptions align, bool wrap)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            foreach (var g in go.GetComponents<Graphic>()) if (!(g is TextMeshProUGUI)) UnityEngine.Object.DestroyImmediate(g);
            var t = Comp<TextMeshProUGUI>(go);
            t.font = font; t.text = text; t.color = Ink; t.alignment = align; t.raycastTarget = false;
            t.enableWordWrapping = wrap; t.overflowMode = TextOverflowModes.Overflow;   // never Truncate / Ellipsis (vrc-world-ui §10.1)
            t.enableAutoSizing = true; t.fontSizeMax = max; t.fontSizeMin = min; t.fontSize = max; t.richText = true;
            EditorUtility.SetDirty(t);
            return t;
        }

        // ================================================================ Photo/9 (the whole system out)

        [MenuItem(Menu + "9 - Remove Photo System")]
        public static void RemoveSystem()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, menu = "Photo/9" };
            var c = Resolve();
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-photo-remove-system.unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, true);
            try
            {
                var s = FindSystem(c);
                RemoveSurfaces(c, s, r.log);
                UnwireHooks(c, r.log);
                var p = c.pouch;
                var page = p.settingsPage.transform.Find(PageName);
                p.extraSettingsPages = (p.extraSettingsPages ?? new GameObject[0]).Where(e => e != null && (page == null || e != page.gameObject)).ToArray();
                p.photoPage = null; p.photoAlbum = null;
                if (page != null) { UnityEngine.Object.DestroyImmediate(page.gameObject); r.log.Add("removed Page_Settings/" + PageName); }
                UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
                if (s != null) { UnityEngine.Object.DestroyImmediate(s.root); r.log.Add("removed " + SystemName); }
                var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
                if (descriptor != null)
                {
                    int dropped = descriptor.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0));
                    if (dropped > 0) { EditorUtility.SetDirty(descriptor); r.log.Add("NetworkIDs: dropped " + dropped + " empty entries"); }
                }
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("scene save failed");
                r.status = "REMOVED";
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message); WriteJson(OutDir + "/remove-system.json", r);
                Debug.LogError("[KH Photo] PHOTO_REMOVE_FAIL " + e.Message + ". Backup: " + r.backup);
                throw;
            }
            WriteJson(OutDir + "/remove-system.json", r);
            Debug.Log("[KH Photo] PHOTO_REMOVE_OK photo system removed (assets kept). " + string.Join(" | ", r.log));
        }

        // ================================================================ small helpers

        public static string DescribeMask(int mask)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                string n = LayerMask.LayerToName(i);
                sb.Append(i).Append(':').Append(string.IsNullOrEmpty(n) ? "?" : n);
            }
            return sb.ToString();
        }

        static void WriteJson(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(o, true));
        }
    }
}
