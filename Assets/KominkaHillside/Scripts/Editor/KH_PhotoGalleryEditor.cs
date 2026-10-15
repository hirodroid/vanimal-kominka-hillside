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
    ///   Photo/1 - Build Photo and Gallery  中核 (撮影カメラ・枠 24 枚 (IMP-WLD-007 W1 / IMP-WLD-011。散歩 10 / 撫で 4 / 遊び 4 / 洗う 6)・犬の受け口・メニューの写真の面とページ送り)。**見える物は置かない・撮らない**
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
        public const string WatcherProgramPath = "Assets/KominkaHillside/Scripts/KH_PhotoWalkWatcher";

        // ---------------------------------------------------------------- 散歩の写真 (IMP-WLD-007 W2。設計 §0・§6。試作値 = Photo/1 が書き、Photo/2 が照合する)
        public const float DogEyeFov = 70f, DogEyePitch = 3f, DogEyeAheadScale = 0.25f, DogEyeAheadMin = 0.08f, DogEyeAheadMax = 0.2f, DogEyeLift = 0.04f,
            DogEyeClearAhead = 2.5f, DogEyeGroundProbe = 1f, DogEyeGroundBelow = 1.5f;
        public const float SelfieFov = 60f, SelfieReach = 0.9f, SelfieLift = 0.15f, SelfieLookToDogs = 0.35f, SelfieInner = 0.8f, SelfieDogMax = 6f;
        public const float WalkTick = 0.5f, WalkStopoverDelay = 1f, WalkTimeInterval = 90f, WalkDistanceInterval = 40f, WalkMinGap = 20f, WalkFadeGrace = 2f,
            WalkSpotRepeat = 300f, WalkJumpMeters = 5f, WalkSpotHeight = 6f, WalkSpotRadius = 4f;
        /// <summary>写真の場所 (初めは 3 か所)。viewpoint = 見晴らしのベンチ (KH_Places/KH_Viewpoint、古民家を背に)、
        /// bridgeUp / river = 地図の 4 上流の橋・5 川辺 (Course/80 が KH_LeashManager.spotPositions に書いた着地点)。</summary>
        public static readonly string[] WalkSpotNames = { "viewpoint", "bridgeUp", "river" };
        const string ViewpointPath = "KH_World/KH_Places/KH_Viewpoint", FarKominkaPath = "KH_World/KH_Places/KH_FarKominka";
        const string TexDir = "Assets/KominkaHillside/Textures/Photo";
        const string MatDir = "Assets/KominkaHillside/Materials/Photo";
        public const string CaptureRTPath = TexDir + "/KH_PhotoCaptureRT.renderTexture";
        public static string SlotRTPath(int i) { return TexDir + "/KH_PhotoSlot" + i + ".renderTexture"; }
        public static string PrintMatPath(int i) { return MatDir + "/M_KH_PhotoPrint" + i + ".mat"; }
        const string ViewerBackMatPath = MatDir + "/M_KH_PhotoViewerBack.mat", ViewerPhotoMatPath = MatDir + "/M_KH_PhotoViewerPhoto.mat";
        const string PrintShaderName = "KominkaDogRun/KD_PhotoPrint", ViewerShaderName = "KominkaDogRun/KD_PhotoViewer";

        // ---------------------------------------------------------------- 枚数・解像度・メモリ (設計 §4.3)
        public const int SlotCount = KH_PhotoAlbum.Slots;   // 24 (IMP-WLD-007 W1。前は 8)
        /// <summary>ボードの Quad・材質とメニューの縮小表示の数 (1 ページ)。</summary>
        public const int PageSize = KH_PhotoAlbum.PageSize;   // 8
        /// <summary>出来事ごとの枠の数 (KD_PhotoDirector.HillsideGroupWalk / Pet / Play / Wash の順)。合計 = SlotCount。docs/plans/2026-10-03-photo-expansion.md §4.2。
        /// IMP-WLD-011 (2026-10-04 ユーザー決定): 12 / 6 / 6 → 散歩 10 / 撫で 4 / 遊び 4 / 洗う 6 (合計 24 のまま。メモリの計算値は変わらない)</summary>
        public static readonly int[] GroupSizes = { 10, 4, 4, 6 };
        public static readonly string[] GroupNames = { "walk", "pet", "play", "wash" };
        /// <summary>IMP-WLD-011: 撫でのバーストの枚数 (撫での枠が 4 枚なので 4 → 2。AI の既定、ユーザー未確認)。KD_PhotoDirector.petBurstLength へ書く</summary>
        public const int PetBurstLength = 2;
        /// <summary>IMP-WLD-011: 洗い上がりの 1 枚を合図から何秒後に撮るか (ブルブル 1.2 s の途中)。KD_PhotoDirector.washShotDoneDelay へ書く</summary>
        public const float WashDoneDelay = 0.5f;
        /// <summary>スロット番号ごとのカテゴリ (KD_GalleryController.slotGroups)。0..9 散歩、10..13 撫で、14..17 遊び、18..23 洗う。</summary>
        public static int[] SlotGroups()
        {
            var map = new int[SlotCount]; int i = 0;
            for (int g = 0; g < GroupSizes.Length; g++) for (int n = 0; n < GroupSizes[g]; n++) map[i++] = g;
            return map;
        }
        /// <summary>RT アセット (雛形) の寸法。実行時は KD_PhotoSettings が端末の寸法で 1 組だけ作り直し、雛形は描かれないまま解放される。</summary>
        public const int TemplateWidth = 640, TemplateHeight = 360;
        public const int PcWidth = 1280, PcHeight = 720, MobileWidth = 640, MobileHeight = 360;
        const int CaptureDepthBits = 24;
        /// <summary>1 画素の量 (byte)。枠と撮影用の色は ARGB32 (RGBA8) = 4、撮影用の深度は最悪の D32_SFloat_S8 = 8 (前作 2026-08-14 の PC 実測)。</summary>
        public const float ColorBytes = 4f, DepthBytesWorst = 8f;
        /// <summary>計算値の上限 (MiB)。IMP-WLD-007 W1: 24 枚で PC 約 94.9 / Quest・iOS 約 23.7 (前は 8 枚で 48 / 12)。</summary>
        public const float PcBudgetMiB = 100f, MobileBudgetMiB = 28f;
        public static float BudgetMiB(int w, int h)
        {
            float px = w * (float)h;
            return (SlotCount * px * ColorBytes + px * (ColorBytes + DepthBytesWorst)) / (1024f * 1024f);
        }
        /// <summary>雛形の RT アセットの書式から 1 画素の色の量を出す (Verify が BudgetMiB の前提と照合する)。</summary>
        public static float FormatBytes(RenderTextureFormat f)
        {
            switch (f)
            {
                case RenderTextureFormat.ARGB32: case RenderTextureFormat.BGRA32: case RenderTextureFormat.Default: return 4f;
                case RenderTextureFormat.ARGBHalf: case RenderTextureFormat.DefaultHDR: return 8f;
                case RenderTextureFormat.ARGBFloat: return 16f;
                case RenderTextureFormat.RGB565: case RenderTextureFormat.ARGB4444: return 2f;
                default: return -1f;
            }
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
        /// <summary>縮小表示の写真どうしの隙間 (px)。横 = ピッチ − 幅、縦 = 段の差 − 高さ。吸着の余白はこの半分より小さくする (Photo/2)。</summary>
        public static float ThumbGapX => ThumbPitchX - ThumbSize.x;
        public static float ThumbGapY => ThumbRow0Y - ThumbRow1Y - ThumbSize.y;
        /// <summary>IMP-WLD-010 (2026-10-04): 「新しい」の印は廃止。Photo/1 が残っている子をこの名前で消し、Photo/2 が無いことを見る。</summary>
        public const string RetiredNewMarkName = "NewMark";
        public static readonly Vector2 PageSettingsPos = new Vector2(0f, -205f), PageSettingsSize = new Vector2(280f, 50f);
        // IMP-WLD-007 W3: 760 x 427.5 at (0,-30) overlapped [とじる] (450,170 220x44) by 40 x 35 px at its top-right corner; the snap quad draws in front of
        // the UI and would hide that corner. 720 x 405 at (0,-58): top 144.5 < the button's bottom 148. The backdrop grows to 490 to keep the photo inside it
        public static readonly Vector2 LargePhotoPos = new Vector2(0f, -58f), LargePhotoSize = new Vector2(720f, 405f), LargeBackdropSize = new Vector2(1110f, 490f);

        // ---------------------------------------------------------------- the menu snap (IMP-WLD-007 W3, design §7)
        // One Quad with the unchanged KD_PhotoPrint shader over the enlarged photo (the thumbnails got their own quads in IMP-WLD-009, below). Trial values.
        public const float MenuAttachDistance = 0.6f, MenuAttachMargin = 0.02f;
        /// <summary>The snap quad's distance in front of the enlarged RawImage (m). The quad writes depth, so the UI behind it does not draw over it.</summary>
        public const float MenuSnapFront = 0.002f;
        /// <summary>**The snap quad's layer: 17 Walkthrough = the hand menu's own layer, which our photo camera excludes (PhotoLayers).**
        /// OPEN (user check, IMP-WLD-007): does the VRChat camera render layer 17 at all? If a VRChat camera photo of the open hand menu
        /// does not show the menu, change this to a layer the VRChat camera draws but our photo camera still skips — 4 Water is in MustExcludeLayers
        /// (0 Default would put the quad into our own auto photos when the menu is in view). The snap only works on a layer the VRChat camera draws.
        /// One constant: Photo/1 writes it, Photo/2 checks it (and that our photo camera excludes it).</summary>
        /// 2026-10-04 (user, real client): the menu snap did not work on layer 17. The video camera's RecordSnap quad on layer 4 is
        /// confirmed to reach the VRChat camera (IMP-WLD-008), so the menu snap uses 4 as well.
        public const int MenuSnapLayer = 4;
        public const string MenuSnapName = "SnapPhoto";
        const string MenuSnapMatPath = MatDir + "/M_KH_PhotoPrintMenu.mat";

        // ---------------------------------------------------------------- the thumbnails' snap (IMP-WLD-009, 2026-10-04)
        // One Quad per thumbnail (8), same build as SnapPhoto (KD_PhotoPrint, no collider, no shadow, MenuSnapLayer), 2 mm in front of each RawImage.
        // Trial values. All 8 lie in one plane, so the shader's aim test (the sight line's hit point inside photo + margin) lets one photo snap at a time
        // as long as 2 x margin < the gap between photos: the gap is 12 px = 12 mm at the menu's 0.001 m/px, so the requested trial margin 0.01 m
        // (20 mm > 12 mm) would let two neighbours snap at once; 0.005 m keeps 2 mm clear. Photo/2 checks it with the scene's real scale.
        public const float MenuThumbAttachDistance = 0.3f, MenuThumbAttachMargin = 0.005f;
        /// <summary>The thumbnails' own materials (not the board's M_KH_PhotoPrint0..7: the board and the menu page independently).</summary>
        public static string MenuThumbMatPath(int k) { return MatDir + "/M_KH_PhotoPrintMenuThumb" + k + ".mat"; }
        /// <summary>IMP-WLD-007 W1: 面のページ送り ◀ 1/3 ▶ (縮小表示の下、右寄せ。[設定へ] (中央 280 幅) と重ならない)。</summary>
        public static readonly Vector2 PagerPos = new Vector2(360f, -205f), PagerButtonSize = new Vector2(100f, 50f), PagerLabelSize = new Vector2(120f, 50f);
        public const float PagerButtonOffset = 120f;
        public const string PagerGlyphs = "◀▶/0123456789";
        public static readonly Vector2 HelpBodyPos = new Vector2(0f, -40f), HelpBodySize = new Vector2(1020f, 370f), HelpTitlePos = new Vector2(-170f, 170f);

        // ---------------------------------------------------------------- ヘッダの [写真] (Photo/4)
        public static readonly Vector2 HeaderButtonPos = new Vector2(-65f, 314f), HeaderButtonSize = new Vector2(120f, 44f);
        /// <summary>[写真] だけがあるときの見出しの箱 (-550〜-150)。IMP-UI-011 から正本は KD_HandMenuScrollEditor.HeaderLayout ([動画] もあると -390 / 320、
        /// [写真] は -150)。Place / Verify は HeaderLayout と比べる。HeaderButtonPos は作るときの仮の位置 (直後の LayoutFrame が並べ直す)。</summary>
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

        /// <summary>このメニューが要る文言 (13 言語とも本文がある)。新しいキーは photo.empty / photo.menu。
        /// photo.new は IMP-WLD-010 (2026-10-04) で印ごと使わなくなった (文言表にはキーが残っている。表示には出ない)。</summary>
        public static readonly string[] NeededKeys =
        {
            "photo.menu", "photo.empty", "photo.settings.off", "photo.settings.loading", "photo.settings.failed",
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
            foreach (var path in OwnPrograms)
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") == null) return path + ".asset missing: run Photo/0, compile U# (unity-compile.sh --udon), then this menu";
            foreach (var n in new[] { "KD_PhotoCapture", "KD_PhotoDirector", "KD_GalleryController", "KD_PhotoSettings", "KD_PhotoViewer", "KD_PhotoBoard" })
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/" + n + ".asset") == null) return n + ".asset missing (the copied photo scripts need their program assets)";
            return null;
        }

        // ================================================================ Photo/0

        /// <summary>このメニューが Program Asset を作る自作の U# (IMP-WLD-007 W2 で散歩の見回りを足した)。</summary>
        public static readonly string[] OwnPrograms = { AlbumProgramPath, WatcherProgramPath };

        [MenuItem(Menu + "0 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            var created = new List<string>(); var existing = new List<string>();
            foreach (var path in OwnPrograms)
            {
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") != null) { existing.Add(path + ".asset"); continue; }
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path + ".cs");
                if (script == null) { Debug.LogError("[KH Photo] PHOTO_PROGRAM_FAIL " + path + ".cs not imported yet (refresh first)"); return; }
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = script;
                AssetDatabase.CreateAsset(asset, path + ".asset");
                created.Add(path + ".asset");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[KH Photo] PHOTO_PROGRAM_OK created [" + string.Join(", ", created) + "], already [" + string.Join(", ", existing) + "]." + (created.Count > 0 ? " Compile U# (unity-compile.sh --udon) before Photo/1." : ""));
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
                var spots = ResolveWalkSpots(c.pouch.manager);   // IMP-WLD-007 W2: before any change (throws = nothing changed)
                var assets = EnsureAssets(r.log);
                var sys = BuildSystem(c, assets, r.log);
                BuildWalk(c, sys, spots, r.log);
                BuildMenuPage(c, sys, r.log);
                WireHooks(c, sys.director, r.log);
                if (c.pouch.manager != null) { c.pouch.manager.photoWalk = sys.walk; EditorUtility.SetDirty(c.pouch.manager); r.log.Add("KH_LeashManager.photoWalk -> " + nameof(KH_PhotoWalkWatcher) + " (local own-dog notice)"); }
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

        /// <summary>枠の RT は 24 枚、ボードの材質は 8 枚 (ボードの位置ごと。実行時は KH_PhotoAlbum が表示中のページの RT を張り直す)。</summary>
        class Assets { public RenderTexture capture; public RenderTexture[] slots = new RenderTexture[SlotCount]; public Material[] prints = new Material[PageSize]; public Material viewerBack, viewerPhoto; }

        static Assets EnsureAssets(List<string> log)
        {
            var a = new Assets();
            var print = Shader.Find(PrintShaderName); if (print == null) throw new InvalidOperationException("shader " + PrintShaderName + " not found");
            var viewer = Shader.Find(ViewerShaderName); if (viewer == null) throw new InvalidOperationException("shader " + ViewerShaderName + " not found");
            EnsureFolder(TexDir); EnsureFolder(MatDir);
            a.capture = EnsureRT(CaptureRTPath, CaptureDepthBits, log);
            for (int i = 0; i < SlotCount; i++) a.slots[i] = EnsureRT(SlotRTPath(i), 0, log);
            for (int i = 0; i < PageSize; i++)
            {
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
            public KH_PhotoWalkWatcher walk;   // IMP-WLD-007 W2
        }

        /// <summary>写真の場所 (名前・位置・背景)。Photo/1 が変更の前に解決する (見つからなければ何も変えない)。</summary>
        public class WalkSpots { public string[] names; public Vector3[] positions; public float[] radii; public bool[] hasBackdrop; public Vector3[] backdrops; public List<string> log = new List<string>(); }

        public static WalkSpots ResolveWalkSpots(KH_LeashManager manager)
        {
            int n = WalkSpotNames.Length;
            var s = new WalkSpots { names = (string[])WalkSpotNames.Clone(), positions = new Vector3[n], radii = new float[n], hasBackdrop = new bool[n], backdrops = new Vector3[n] };
            var vp = GameObject.Find(ViewpointPath);
            if (vp == null) throw new InvalidOperationException(ViewpointPath + " not found (the viewpoint bench place); nothing changed");
            var kominka = GameObject.Find(FarKominkaPath);
            if (kominka == null) throw new InvalidOperationException(FarKominkaPath + " not found (the farmhouse backdrop); nothing changed");
            if (manager == null || manager.spotIds == null || manager.spotPositions == null || manager.spotIds.Length != manager.spotPositions.Length)
                throw new InvalidOperationException("KH_LeashManager.spotIds / spotPositions missing (Course/80 writes them); nothing changed");
            Vector3 Travel(string id)
            {
                int i = Array.IndexOf(manager.spotIds, id);
                if (i < 0) throw new InvalidOperationException("travel spot " + id + " not in KH_LeashManager.spotIds; nothing changed");
                return manager.spotPositions[i];
            }
            s.positions[0] = vp.transform.position; s.hasBackdrop[0] = true; s.backdrops[0] = kominka.transform.position;
            s.positions[1] = Travel("spot.bridgeUp");
            s.positions[2] = Travel("spot.river");
            for (int i = 0; i < n; i++) { s.radii[i] = WalkSpotRadius; s.log.Add("walk photo spot " + s.names[i] + " at " + V(s.positions[i]) + " r " + WalkSpotRadius + (s.hasBackdrop[i] ? " backdrop KH_FarKominka " + V(s.backdrops[i]) : "")); }
            return s;
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
            s.settings = root.GetComponent<KD_PhotoSettings>(); s.album = root.GetComponent<KH_PhotoAlbum>(); s.walk = root.GetComponent<KH_PhotoWalkWatcher>();
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
            // IMP-WLD-007 W1 / IMP-WLD-011: 4 event categories (walk 10 / pet 4 / play 4 / wash 6); the oldest photo of the same category is replaced
            s.gallery.slotGroupCount = GroupSizes.Length;
            s.gallery.slotGroups = SlotGroups();

            var d = s.director;
            d.photoCapture = s.capture; d.photoCamera = cam; d.gallery = s.gallery; d.bedroom = null;
            d.dogRoots = (Transform[])c.dogs.Clone(); d.dogHeadAnchors = (Transform[])c.heads.Clone();
            d.fetchControllers = (KD_DogFetchController[])c.fetch.Clone(); d.petControllers = (KD_DogPetController[])c.pet.Clone(); d.wanderControllers = (KD_DogWanderController[])c.wander.Clone();
            d.cameraBlockMask = CameraBlockMask;
            d.chaseShotEnabled = true; d.playShotEnabled = true;
            d.tickInterval = 0.1f;
            d.frisbeeShotEnabled = true; d.frisbeeShotDelay = 0.1f; d.frisbeeShotCooldown = 20f;
            d.hillsideEventGroups = true;           // IMP-WLD-007: the category is the event (pet burst = pet, everything else = play; walk = W2)
            // IMP-WLD-011: the wash photos (the wash category, KH_DogWash's milestones through wander.photoDirector) and the shorter pet burst
            d.petBurstLength = PetBurstLength;
            d.washShotEnabled = true; d.washShotDoneDelay = WashDoneDelay;
            d.washFade = c.pouch.manager != null ? c.pouch.manager.travelFade : null;
            d.washSleepPlacement = c.pouch.sleepPlacement;
            if (d.washFade == null) log.Add("WARN KH_LeashManager.travelFade missing: wash photos do not pause during the travel fade");
            if (d.washSleepPlacement == null) log.Add("WARN KD_TreatPouch.sleepPlacement missing: wash photos do not pause while placing a sleeping spot");

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
            log.Add("system: " + SlotCount + " slots (template " + TemplateWidth + "x" + TemplateHeight + "; runtime PC " + PcWidth + "x" + PcHeight + " / Android・iOS " + MobileWidth + "x" + MobileHeight + "), categories "
                + string.Join(" / ", GroupNames.Select((n, g) => n + " " + GroupSizes[g])) + ", 2K off, director tick 0.1 s, frisbee 0.1 s / 20 s, event groups on, pet burst " + PetBurstLength + ", wash done +" + WashDoneDelay + " s");
            log.Add("memory (computed): PC " + BudgetMiB(PcWidth, PcHeight).ToString("F1") + " MiB / Quest・iOS " + BudgetMiB(MobileWidth, MobileHeight).ToString("F1") + " MiB");
            return s;
        }

        /// <summary>IMP-WLD-007 W2: 散歩の見回り (KH_PhotoWalkWatcher、KH_PhotoSystem の上) と、撮影の試作値。</summary>
        static void BuildWalk(Ctx c, Sys s, WalkSpots spots, List<string> log)
        {
            s.walk = EnsureUsc<KH_PhotoWalkWatcher>(s.root, log);
            var w = s.walk;
            w.director = s.director; w.settings = s.settings; w.album = s.album; w.manager = c.pouch.manager;
            w.wanderControllers = (KD_DogWanderController[])c.wander.Clone();
            w.sleepPlacement = c.pouch.sleepPlacement;
            if (w.sleepPlacement == null) log.Add("WARN KD_TreatPouch.sleepPlacement missing: walk photos do not pause while placing a sleeping spot");
            w.spotNames = spots.names; w.spotPositions = spots.positions; w.spotRadii = spots.radii; w.spotHasBackdrop = spots.hasBackdrop; w.spotBackdrops = spots.backdrops;
            w.tickSeconds = WalkTick; w.stopoverDelay = WalkStopoverDelay; w.timeInterval = WalkTimeInterval; w.distanceInterval = WalkDistanceInterval; w.minGap = WalkMinGap;
            w.fadeGrace = WalkFadeGrace; w.spotRepeat = WalkSpotRepeat; w.jumpMeters = WalkJumpMeters; w.spotHeight = WalkSpotHeight;
            var d = s.director;
            d.walkDogEyeFov = DogEyeFov; d.walkDogEyePitch = DogEyePitch; d.walkDogEyeAheadScale = DogEyeAheadScale; d.walkDogEyeAheadMin = DogEyeAheadMin; d.walkDogEyeAheadMax = DogEyeAheadMax;
            d.walkDogEyeLift = DogEyeLift; d.walkDogEyeClearAhead = DogEyeClearAhead; d.walkDogEyeGroundProbe = DogEyeGroundProbe; d.walkDogEyeGroundBelow = DogEyeGroundBelow;
            d.walkSelfieFov = SelfieFov; d.walkSelfieReach = SelfieReach; d.walkSelfieLift = SelfieLift; d.walkSelfieLookToDogs = SelfieLookToDogs; d.walkSelfieInner = SelfieInner; d.walkSelfieDogMax = SelfieDogMax;
            EditorUtility.SetDirty(w); EditorUtility.SetDirty(d);
            log.AddRange(spots.log);
            log.Add("walk watcher: tick " + WalkTick + " s, stopover +" + WalkStopoverDelay + " s, every " + WalkTimeInterval + " s and " + WalkDistanceInterval + " m, gap " + WalkMinGap + " s, fade +" + WalkFadeGrace
                + " s; dog's-eye FOV " + DogEyeFov + " pitch +" + DogEyePitch + "°, selfie " + SelfieReach + " m / +" + SelfieLift + " m FOV " + SelfieFov);
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
            log.Add("hooks: fetch.photoDirector x6, wander.photoDirector x6 (the frisbee and wash hooks read it), KD_GroupActivities.photos" + (c.activities != null ? "" : " (missing)"));
            // IMP-WLD-011: the wash photos go KH_DogWash.voiceDog (Wash/11 wires it to the dog's own wander) -> wander.photoDirector. Not written here
            var washes = WashesByDog(c);
            int ok = Enumerable.Range(0, Keys.Length).Count(i => washes[i] != null && washes[i].voiceDog == c.wander[i]);
            log.Add("wash photo path: " + ok + " / " + Keys.Length + " KH_DogWash.voiceDog = the dog's own KD_DogWanderController" + (ok < Keys.Length ? " (WARN: the others take no wash photos; run Wash/11)" : ""));
        }

        /// <summary>IMP-WLD-011: the scene's KH_DogWash by dogIndex (null where none or more than one).</summary>
        static KH_DogWash[] WashesByDog(Ctx c)
        {
            var r = new KH_DogWash[Keys.Length];
            var all = UnityEngine.Object.FindObjectsOfType<KH_DogWash>(true).Where(w => w.gameObject.scene == c.scene).ToArray();
            for (int i = 0; i < Keys.Length; i++) { var m = all.Where(w => w.dogIndex == i).ToArray(); r[i] = m.Length == 1 ? m[0] : null; }
            return r;
        }

        static void UnwireHooks(Ctx c, List<string> log)
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                c.fetch[i].photoDirector = null; EditorUtility.SetDirty(c.fetch[i]); UdonSharpEditorUtility.CopyProxyToUdon(c.fetch[i]);
                c.wander[i].photoDirector = null; EditorUtility.SetDirty(c.wander[i]); UdonSharpEditorUtility.CopyProxyToUdon(c.wander[i]);
            }
            if (c.activities != null) { c.activities.photos = null; EditorUtility.SetDirty(c.activities); UdonSharpEditorUtility.CopyProxyToUdon(c.activities); }
            if (c.pouch.manager != null) { c.pouch.manager.photoWalk = null; EditorUtility.SetDirty(c.pouch.manager); UdonSharpEditorUtility.CopyProxyToUdon(c.pouch.manager); }   // IMP-WLD-007 W2
            log.Add("hooks cleared (null = no photos, the pre-photo state; also KH_LeashManager.photoWalk)");
        }

        static void CopyAll(Ctx c, Sys s)
        {
            foreach (UdonSharpBehaviour b in new UdonSharpBehaviour[] { s.capture, s.gallery, s.director, s.settings, s.viewer, s.album, c.pouch, c.language })
            { UdonSharpEditorUtility.CopyProxyToUdon(b); EditorUtility.SetDirty(b); }
            if (s.walk != null) { UdonSharpEditorUtility.CopyProxyToUdon(s.walk); EditorUtility.SetDirty(s.walk); }
            if (c.pouch.manager != null) { UdonSharpEditorUtility.CopyProxyToUdon(c.pouch.manager); EditorUtility.SetDirty(c.pouch.manager); }
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
            glyphs.Append("…: ONOFF").Append(PagerGlyphs);   // IMP-WLD-007: the pager ◀ 1/3 ▶ (menu and board)
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
            var thumbs = new RawImage[PageSize]; var thumbButtons = new GameObject[PageSize]; var thumbSnaps = new MeshRenderer[PageSize];
            for (int k = 0; k < PageSize; k++)
            {
                var b = PlainButton(page.transform, "Thumb" + k, ThumbPos(k), ThumbSize + Vector2.one * (ThumbBorder * 2f), ThumbFrameColor, albumUdon, "_MenuPick" + k, log);
                var raw = RawChild(b.transform, "Photo", Vector2.zero, ThumbSize);
                thumbSnaps[k] = BuildThumbSnap(b.transform, k, log);   // IMP-WLD-009: snap from the list (hidden until the album binds a photo)
                thumbs[k] = raw; thumbButtons[k] = b; b.SetActive(false);
            }
            // IMP-WLD-010 (2026-10-04): the 'new' mark is retired. EnsureUiChild never removes children, so delete the one an earlier Photo/1 made
            var oldMark = page.transform.Find(RetiredNewMarkName);
            if (oldMark != null) { UnityEngine.Object.DestroyImmediate(oldMark.gameObject); log.Add("removed " + PageName + "/" + RetiredNewMarkName + " (IMP-WLD-010)"); }
            var toSettings = Btn(page.transform, "SettingsButton", PageSettingsPos, PageSettingsSize, T("KD_TreatPouch.WordSettings"), c, c.pouchUdon, "_PhotoToSettings", log);
            toSettings.SetActive(false);

            // ---- ページ送り ◀ 1/3 ▶ (IMP-WLD-007 W1。9 枚以上のときだけ KH_PhotoAlbum が出す。文言のキーは使わない) ----
            var pager = KD_ShopSetupEditor.EnsureUiChild(page.transform, "Pager", PagerPos, new Vector2(PagerButtonOffset * 2f + PagerButtonSize.x, PagerButtonSize.y));
            var pagerImg = pager.GetComponent<Image>(); if (pagerImg != null) UnityEngine.Object.DestroyImmediate(pagerImg);
            Btn(pager.transform, "Prev", new Vector2(-PagerButtonOffset, 0f), PagerButtonSize, "◀", c, albumUdon, "_MenuPagePrev", log);
            Btn(pager.transform, "Next", new Vector2(PagerButtonOffset, 0f), PagerButtonSize, "▶", c, albumUdon, "_MenuPageNext", log);
            var pageLabel = Label(pager.transform, "Label", Vector2.zero, PagerLabelSize, "1/3", c.font, 24f, 16f, TextAlignmentOptions.Center, false);
            pager.SetActive(false);

            // ---- 大きく (縮小表示を押す)。背板は面全体の押せる面を塞ぐ (裏の縮小表示へ通さない) ----
            var large = KD_ShopSetupEditor.EnsureUiChild(page.transform, "Large", Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            var backdrop = KD_ShopSetupEditor.EnsureUiChild(large.transform, "Backdrop", new Vector2(0f, -20f), LargeBackdropSize);
            var bdImg = Comp<Image>(backdrop); bdImg.color = new Color(0.08f, 0.07f, 0.06f, 0.96f); bdImg.raycastTarget = true; KD_UiSpriteEditor.ApplyFlatSkin(bdImg);
            var largeRaw = RawChild(large.transform, "Photo", LargePhotoPos, LargePhotoSize);
            var snap = BuildMenuSnap(large.transform, log);   // IMP-WLD-007 W3: the VRChat camera snap on the enlarged photo (kept with IMP-WLD-009's thumbnail snaps)
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
            al.menuThumbSnaps = thumbSnaps.Cast<Renderer>().ToArray();
            al.menuState = state;
            al.menuSettingsButton = toSettings; al.menuSettingsLabel = LabelOf(toSettings);
            al.menuBackLabel = LabelOf(back); al.menuHelpMarkLabel = LabelOf(help);
            al.menuHelpRoot = helpRoot; al.menuHelpTitle = helpTitle; al.menuHelpBody = helpBody; al.menuHelpCloseLabel = LabelOf(helpClose);
            al.menuLargeRoot = large; al.menuLarge = largeRaw; al.menuLargeCloseLabel = LabelOf(largeClose);
            al.menuSnapPhoto = snap;
            al.menuPager = pager; al.menuPageLabel = pageLabel;
            log.Add("menu page: Page_Settings/" + PageName + " (registered in extraSettingsPages, hidden): 4 x 2 thumbnails per page (" + SlotCount + " photos, pager at " + PagerPos + "), " + ThumbSize.x + "x" + ThumbSize.y
                + " each with a snap quad (attach " + MenuThumbAttachDistance + " m / margin " + MenuThumbAttachMargin + " m), large " + LargePhotoSize.x + "x" + LargePhotoSize.y + ", [?] help, [back] -> _PhotoBack. Layer " + LayerMask.LayerToName(page.layer) + " (excluded from photos)");
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

        /// <summary>IMP-WLD-007 W3: 大きく出した写真の手前 2 mm に Quad 1 枚 (KD_PhotoPrint、吸着 0.6 m / 余白 0.02 m)。RawImage はそのまま残す
        /// (Quad の下に同じ写真。スマホで吸着を切っても見え方は同じ)。Collider なし、影なし、レイヤーは MenuSnapLayer。
        /// Quad の頂点 ±0.5 にキャンバスの px を掛けて写真と同じ大きさにする (親の RectTransform の座標は px)。</summary>
        static MeshRenderer BuildMenuSnap(Transform large, List<string> log)
        {
            var print = Shader.Find(PrintShaderName); if (print == null) throw new InvalidOperationException("shader " + PrintShaderName + " not found");
            var m = EnsureMat(MenuSnapMatPath, print, log);
            m.mainTexture = null;
            m.SetFloat("_AttachDistance", MenuAttachDistance); m.SetFloat("_AttachMargin", MenuAttachMargin);
            if (m.HasProperty("_DebugProbe")) m.SetFloat("_DebugProbe", 0f);
            if (m.HasProperty("_HideFromEyes")) m.SetFloat("_HideFromEyes", 0f);
            EditorUtility.SetDirty(m);
            var mr = EnsureSnapQuad(large, LargePhotoPos, LargePhotoSize, m, log);
            log.Add("menu snap: " + MenuSnapName + " " + LargePhotoSize + " px at " + LargePhotoPos + ", " + MenuSnapFront * 1000f + " mm in front, layer " + MenuSnapLayer + " (" + LayerMask.LayerToName(MenuSnapLayer)
                + "), attach " + MenuAttachDistance + " m / margin " + MenuAttachMargin + " m");
            return mr;
        }

        /// <summary>IMP-WLD-009: 縮小表示 k の手前 2 mm に Quad (KD_PhotoPrint、材質は M_KH_PhotoPrintMenuThumb{k}、吸着 0.3 m / 余白 0.005 m)。親は Thumb{k} のボタン
        /// (ボタンが隠れると一緒に隠れる)。Collider なし (UI の当たり判定を塞がない)、影なし、レイヤーは MenuSnapLayer。最初は隠す (KH_PhotoAlbum が写真を張って出す)。</summary>
        static MeshRenderer BuildThumbSnap(Transform thumb, int k, List<string> log)
        {
            var print = Shader.Find(PrintShaderName); if (print == null) throw new InvalidOperationException("shader " + PrintShaderName + " not found");
            var m = EnsureMat(MenuThumbMatPath(k), print, log);
            m.mainTexture = null;
            m.SetFloat("_AttachDistance", MenuThumbAttachDistance); m.SetFloat("_AttachMargin", MenuThumbAttachMargin);
            if (m.HasProperty("_DebugProbe")) m.SetFloat("_DebugProbe", 0f);
            if (m.HasProperty("_HideFromEyes")) m.SetFloat("_HideFromEyes", 0f);
            EditorUtility.SetDirty(m);
            var mr = EnsureSnapQuad(thumb, Vector2.zero, ThumbSize, m, log);
            mr.gameObject.SetActive(false);
            return mr;
        }

        /// <summary>吸着の Quad 1 枚 (SnapPhoto) を parent の下に作るか直す。Quad の頂点 ±0.5 にキャンバスの px を掛けて写真と同じ大きさにし、
        /// 写真の MenuSnapFront (2 mm) 手前に置く (親の RectTransform の座標は px、キャンバスの手前 = −Z)。</summary>
        static MeshRenderer EnsureSnapQuad(Transform parent, Vector2 pos, Vector2 size, Material m, List<string> log)
        {
            var t = parent.Find(MenuSnapName);
            GameObject go;
            if (t == null) { go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = MenuSnapName; go.transform.SetParent(parent, false); log.Add("created " + parent.name + "/" + MenuSnapName); } else go = t.gameObject;
            foreach (var col in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            go.layer = MenuSnapLayer;
            float pxToM = Mathf.Abs(parent.lossyScale.x) > 1e-6f ? Mathf.Abs(parent.lossyScale.x) : 0.001f;
            go.transform.localPosition = new Vector3(pos.x, pos.y, -MenuSnapFront / pxToM);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            EditorUtility.SetDirty(go);
            return mr;
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
