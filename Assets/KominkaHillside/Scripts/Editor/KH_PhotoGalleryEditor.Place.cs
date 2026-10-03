using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-005: 見える物 (任意の段)。Photo/4 が置き、Photo/5 が外す。
    /// - 門の看板 A「あそびかた」の隣の写真ボード (4 × 2、1 枚 0.48 × 0.27 m、看板と同じ木の枠)。位置は A の実物の Transform から、
    ///   看板と同じ置き場所の条件 (docs/plans/2026-10-02-guide-boards.md §3-2) と実際の Collider で候補を順に試して決める。既にあれば位置と向きは守る
    /// - 手元メニューのヘッダの [写真] (見出しを 430 → 400 に縮める。KD_HandMenuScrollEditor.LayoutFrame がボタンの有無で切り替える)
    /// - 設定ホームの [写真: ON / OFF] (KD_MenuFinishEditor.PhotoToggleName。Verify 22 は置いたときだけ群「自分」に数える)
    /// これを置かない構成でも何も壊れない: KH_PhotoAlbum.surfacesPlaced が false の間は撮らない (撮影の見回りも無効)。</summary>
    public static partial class KH_PhotoGalleryEditor
    {
        const string GuideRootName = "KH_GuideBoards", GuideAName = "Asobikata";
        const string WoodMatPath = "Assets/KominkaHillside/Data/Environment/River/KH_RiverWood.mat";

        // ---------------------------------------------------------------- board geometry (board-local; root at the ground, +Z away from the readers, Canvas front = -Z)
        const float Mm = 0.001f;
        public static readonly Vector2 BoardCanvasMm = new Vector2(2240f, 1040f);
        const float BoardCanvasBottom = 0.85f;
        static float BoardCanvasCenterY { get { return BoardCanvasBottom + BoardCanvasMm.y * Mm * 0.5f; } }
        const float BackZ = 0.03f, BackDepth = 0.04f, PostSize = 0.10f, PostSink = 0.25f, RoofSlope = 25f, RoofPlankDepth = 0.32f, RoofThick = 0.03f, RoofOverhang = 0.12f, RidgeSize = 0.06f;
        static float BackW { get { return BoardCanvasMm.x * Mm + 0.08f; } }
        static float BackH { get { return BoardCanvasMm.y * Mm + 0.08f; } }
        static float PostX { get { return BackW * 0.5f + 0.06f; } }
        static float PostTop { get { return BoardCanvasCenterY + BackH * 0.5f + 0.18f; } }
        static float RidgeY { get { return PostTop + 0.08f; } }
        static float RoofLength { get { return 2f * PostX + PostSize + 2f * RoofOverhang; } }
        /// <summary>柱と屋根の外形 (board-local x / z)。置き場所の検査に使う。</summary>
        static Rect BoardFootprint { get { float hx = RoofLength * 0.5f, hz = Mathf.Cos(RoofSlope * Mathf.Deg2Rad) * RoofPlankDepth + 0.02f; return Rect.MinMaxRect(-hx, BackZ - hz, hx, BackZ + hz); } }
        /// <summary>読む人が立つ所 (board-local、前 = -Z)。</summary>
        static Rect BoardReaders { get { float hx = BackW * 0.5f; return Rect.MinMaxRect(-hx, -2.0f, hx, -0.3f); } }

        // canvas layout (mm, centre origin)
        static readonly Vector2 HeaderPlatePos = new Vector2(0f, 410f), HeaderPlateSize = new Vector2(2160f, 180f), CaptionSize = new Vector2(2080f, 170f);
        static readonly Vector2 PaperPos = new Vector2(0f, -110f), PaperSize = new Vector2(2160f, 780f);
        static readonly Vector2 StatePos = new Vector2(0f, 235f), StateSize = new Vector2(2080f, 64f);
        public static readonly Vector2 PhotoMm = new Vector2(480f, 270f);
        const float PhotoPitchX = 520f, PhotoRow0Y = 50f, PhotoRow1Y = -265f, CellBorderMm = 12f, PhotoFrontZ = -0.006f;
        public static Vector2 CellPos(int k) { int col = k % 4, row = k / 4; return new Vector2((col - 1.5f) * PhotoPitchX, row == 0 ? PhotoRow0Y : PhotoRow1Y); }
        const float CaptionMax = 44f, CaptionMin = 24f, StateMax = 44f, StateMin = 24f;
        // IMP-WLD-007 W1: the board pager ◀ 1/3 ▶ — its own pressable Canvas (Default + GraphicRaycaster + VRCUiShape + trigger BoxCollider, vrc-world-ui §2)
        // in the strip under the bottom row (the cells end at -412 mm, the canvas at -520 mm). The read-only Canvas stays read-only
        public const string BoardPagerName = "Pager";
        public static readonly Vector2 BoardPagerMm = new Vector2(700f, 84f), BoardPagerButtonMm = new Vector2(170f, 76f), BoardPagerLabelMm = new Vector2(240f, 76f);
        public const float BoardPagerY = -462f, BoardPagerButtonX = 255f, BoardPagerZ = -0.004f;

        static readonly Color Washi = new Color(0.94f, 0.91f, 0.83f);
        static readonly Color Sumi = new Color(0.17f, 0.13f, 0.10f);
        static readonly Color SignInk = new Color(0xF2 / 255f, 0xE8 / 255f, 0xD0 / 255f);
        static readonly Color HeaderPlateColor = new Color(0.20f, 0.14f, 0.09f);
        static readonly Color CellColor = new Color(0.30f, 0.22f, 0.15f);

        // ---------------------------------------------------------------- placement rules (guide boards §3-2, the same numbers)
        const float RouteClear = 1.8f, RoamClear = 3.3f, SpawnClear = 3.0f, SpawnReaderClear = 1.5f, FacilityClear = 2.0f, WashFloorClear = 0.5f;
        // GateLineClear: 看板は 1.5 m。写真ボードは A の右端 (門の線から約 2.1 m) の先に直角に立てるので 1.5 m は幾何的に取れない (オフラインの試算 2026-10-03: 約 1.06 m)。
        // 低く短い板なので 1.0 m に緩め、塀の実物は Collider の検査 (SolidIn) で見る
        const float GateLineClear = 1.0f, BackViewClear = 45f, GuideBoardClear = 0.3f, PortalClear = 2.5f, GroundSpread = 0.6f, ReaderGroundSpread = 1.2f;

        // ================================================================ Photo/4

        [MenuItem(Menu + "4 - Place Board, Menu Button and Settings Row (optional)")]
        public static void Place()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            string missing = ProgramMissing();
            if (missing != null) { Debug.LogError("[KH Photo] PHOTO_PLACE_FAIL " + missing + "; nothing changed"); return; }
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, menu = "Photo/4" };
            var c = Resolve();
            var s = FindSystem(c);
            if (s == null || s.album == null || s.viewer == null || c.pouch.photoPage == null) { Debug.LogError("[KH Photo] PHOTO_PLACE_FAIL run Photo/1 first (" + SystemName + " / album / viewer / PhotoPage missing); nothing changed"); return; }
            var wood = AssetDatabase.LoadAssetAtPath<Material>(WoodMatPath);
            if (wood == null) { Debug.LogError("[KH Photo] PHOTO_PLACE_FAIL " + WoodMatPath + " missing; nothing changed"); return; }
            // ---- where (decided before anything changes) ----
            var existing = SceneRoot(c.scene, BoardRootName, out int n);
            if (n > 1) { Debug.LogError("[KH Photo] PHOTO_PLACE_FAIL " + n + " " + BoardRootName + " at the scene root (keep one); nothing changed"); return; }
            Vector3 pos; float yaw; string how;
            if (existing != null)
            {
                pos = existing.transform.position; yaw = existing.transform.eulerAngles.y;
                var broken = Rules(c, pos, yaw, existing.transform);
                how = "kept at " + V(pos) + " yaw " + yaw.ToString("F1") + (broken.Count == 0 ? " (rules OK)" : " (WARN rules: " + string.Join("; ", broken) + ")");
            }
            else if (!FindBoardPose(c, out pos, out yaw, out how)) { Debug.LogError("[KH Photo] PHOTO_PLACE_FAIL " + how + "; nothing changed"); WriteJson(OutDir + "/place.json", r); return; }
            r.log.Add("board: " + how);
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-photo-place.unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, true);
            try
            {
                var root = existing;
                if (root == null) { root = new GameObject(BoardRootName); SceneManagerMove(root, c.scene); root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f)); r.log.Add("created " + BoardRootName); }
                BuildBoard(c, s, root.transform, wood, r.log);
                PlaceHeaderButton(c, r.log);
                PlaceSettingsToggle(c, s, r.log);
                RewireSurfaces(c, s, r.log);
                CopyAll(c, s);
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("scene save failed");
                AssetDatabase.SaveAssets();
                r.status = "PLACED";
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message); WriteJson(OutDir + "/place.json", r);
                Debug.LogError("[KH Photo] PHOTO_PLACE_FAIL " + e.Message + ". Backup: " + r.backup + " (reopen the scene without saving to discard partial changes)");
                throw;
            }
            WriteJson(OutDir + "/place.json", r);
            Debug.Log("[KH Photo] PHOTO_PLACE_OK board / [写真] / settings row placed; capture starts after the player data loads. " + how + ". Next: Photo/2, Photo/3.");
        }

        // ================================================================ Photo/5

        [MenuItem(Menu + "5 - Remove Board, Menu Button and Settings Row")]
        public static void Unplace()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, menu = "Photo/5" };
            var c = Resolve();
            var s = FindSystem(c);
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-photo-unplace.unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, true);
            try
            {
                RemoveSurfaces(c, s, r.log);
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("scene save failed");
                r.status = "UNPLACED";
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message); WriteJson(OutDir + "/unplace.json", r);
                Debug.LogError("[KH Photo] PHOTO_UNPLACE_FAIL " + e.Message + ". Backup: " + r.backup);
                throw;
            }
            WriteJson(OutDir + "/unplace.json", r);
            Debug.Log("[KH Photo] PHOTO_UNPLACE_OK " + string.Join(" | ", r.log));
        }

        static void RemoveSurfaces(Ctx c, Sys s, List<string> log)
        {
            var board = SceneRoot(c.scene, BoardRootName, out _);
            if (board != null) { UnityEngine.Object.DestroyImmediate(board); log.Add("removed " + BoardRootName); }
            var button = c.pouch.panelRoot.transform.Find(KD_HandMenuScrollEditor.PhotoHeaderButtonName);
            if (button != null) { UnityEngine.Object.DestroyImmediate(button.gameObject); log.Add("removed header [写真]"); }
            var toggle = c.pouch.settingsHome.transform.Find(KD_MenuFinishEditor.PhotoToggleName);
            if (toggle != null) { UnityEngine.Object.DestroyImmediate(toggle.gameObject); log.Add("removed settings [写真: ON/OFF]"); }
            KD_HandMenuScrollEditor.LayoutFrame(c.pouch);   // the title returns to 430 when the button is gone
            EditorUtility.SetDirty(c.pouch);
            if (s != null && s.album != null) { RewireSurfaces(c, s, log); UdonSharpEditorUtility.CopyProxyToUdon(s.album); EditorUtility.SetDirty(s.album); }
            UdonSharpEditorUtility.CopyProxyToUdon(c.pouch);
        }

        /// <summary>今シーンにある見える物を KH_PhotoAlbum へ配線し直す (無いものは null)。surfacesPlaced = ボードかヘッダの [写真] がある。</summary>
        static void RewireSurfaces(Ctx c, Sys s, List<string> log)
        {
            var al = s.album;
            var board = SceneRoot(c.scene, BoardRootName, out _);
            var photos = new Renderer[PageSize]; var taps = new KD_PhotoBoard[PageSize]; var tapUdons = new UdonBehaviour[PageSize];
            bool boardOk = false;
            if (board != null)
            {
                boardOk = true;
                var photosT = board.transform.Find("Photos");
                for (int k = 0; k < PageSize; k++)
                {
                    var q = photosT != null ? photosT.Find("Photo" + k) : null;
                    if (q == null) { boardOk = false; break; }
                    photos[k] = q.GetComponent<MeshRenderer>(); taps[k] = q.GetComponent<KD_PhotoBoard>(); tapUdons[k] = taps[k] != null ? UdonSharpEditorUtility.GetBackingUdonBehaviour(taps[k]) : null;
                    if (photos[k] == null || taps[k] == null || tapUdons[k] == null) { boardOk = false; break; }
                    taps[k].viewer = s.viewer; EditorUtility.SetDirty(taps[k]);
                }
            }
            al.boardPhotos = boardOk ? photos : null; al.boardTaps = boardOk ? taps : null; al.boardTapUdons = boardOk ? tapUdons : null;
            al.boardCaption = boardOk ? board.transform.Find("Canvas/HeaderPlate/Caption")?.GetComponent<TextMeshProUGUI>() : null;
            al.boardState = boardOk ? board.transform.Find("Canvas/Paper/State")?.GetComponent<TextMeshProUGUI>() : null;
            var pagerT = boardOk ? board.transform.Find(BoardPagerName) : null;   // IMP-WLD-007 W1 (a board from before W1 has none until Photo/4 runs again)
            al.boardPager = pagerT != null ? pagerT.gameObject : null;
            al.boardPageLabel = pagerT != null ? pagerT.Find("Label")?.GetComponent<TextMeshProUGUI>() : null;
            if (boardOk && pagerT == null) log.Add("WARN the board has no pager (built before IMP-WLD-007): run Photo/4 again to add ◀ ▶");
            var button = c.pouch.panelRoot.transform.Find(KD_HandMenuScrollEditor.PhotoHeaderButtonName);
            al.headerLabel = button != null ? LabelOf(button.gameObject) : null;
            var toggle = c.pouch.settingsHome.transform.Find(KD_MenuFinishEditor.PhotoToggleName);
            al.settingsToggleButton = toggle != null ? toggle.GetComponent<Button>() : null;
            al.settingsToggleImage = toggle != null ? toggle.GetComponent<Image>() : null;
            al.settingsToggleLabel = toggle != null ? LabelOf(toggle.gameObject) : null;
            al.surfacesPlaced = boardOk || button != null;
            EditorUtility.SetDirty(al);
            if (board != null && !boardOk) log.Add("WARN " + BoardRootName + " is incomplete (Photos/Photo0.." + (PageSize - 1) + " with KD_PhotoBoard): not wired; run Photo/4 again");
            log.Add("surfaces: board " + (boardOk ? "wired" : "none") + ", header button " + (button != null ? "yes" : "none") + ", settings row " + (toggle != null ? "yes" : "none") + " -> surfacesPlaced " + al.surfacesPlaced);
        }

        // ---------------------------------------------------------------- header button and settings row

        static void PlaceHeaderButton(Ctx c, List<string> log)
        {
            string T(string key) { return KD_LocalizationEditor.Text(c.catalog, c.catalog.fallbackLanguage, key); }
            var go = Btn(c.pouch.panelRoot.transform, KD_HandMenuScrollEditor.PhotoHeaderButtonName, HeaderButtonPos, HeaderButtonSize, T("photo.menu"), c, c.pouchUdon, "_PhotoOpen", log);
            KD_HandMenuScrollEditor.LayoutFrame(c.pouch);   // title 430 -> 400 (-550..-150) and the button at (-65, 314) 120x44
            var title = c.pouch.sectionTitleLabel != null ? (RectTransform)c.pouch.sectionTitleLabel.transform : null;
            if (title != null && (Mathf.Abs(title.sizeDelta.x - TitleWithButtonWidth) > 0.5f || Mathf.Abs(title.anchoredPosition.x - TitleWithButtonX) > 0.5f))
                throw new InvalidOperationException("LayoutFrame did not narrow the title for the photo button (" + title.anchoredPosition.x + " / " + title.sizeDelta.x + ")");
            EditorUtility.SetDirty(go);
            log.Add("header [写真] at " + HeaderButtonPos + " " + HeaderButtonSize + " -> _PhotoOpen; title " + TitleWithButtonWidth + " wide at x " + TitleWithButtonX);
        }

        static void PlaceSettingsToggle(Ctx c, Sys s, List<string> log)
        {
            var albumUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(s.album);
            string label = KD_LocalizationEditor.Text(c.catalog, c.catalog.fallbackLanguage, "photo.menu") + ": ON";
            var sb = new StringBuilder();
            var go = KD_ShopSetupEditor.EnsureButton(c.pouch.settingsHome.transform, KD_MenuFinishEditor.PhotoToggleName, KD_MenuFinishEditor.HillsidePhotoTogglePos, KD_MenuFinishEditor.HillsidePhotoToggleSize,
                c.pouch.equipOnColor, c.font, label, albumUdon, "_ToggleCapture", sb);
            if (sb.Length > 0) log.Add(sb.ToString().Trim());
            var l = LabelOf(go);
            if (l != null) { l.fontSize = 22f; l.enableAutoSizing = true; l.fontSizeMax = 22f; l.fontSizeMin = KD_LocalizationEditor.ButtonFontSizeMin; EditorUtility.SetDirty(l); }
            log.Add("settings [写真: ON/OFF] at " + KD_MenuFinishEditor.HillsidePhotoTogglePos + " " + KD_MenuFinishEditor.HillsidePhotoToggleSize + " -> KH_PhotoAlbum._ToggleCapture");
        }

        // ---------------------------------------------------------------- the board

        static void BuildBoard(Ctx c, Sys s, Transform root, Material wood, List<string> log)
        {
            root.gameObject.layer = 0;
            root.localScale = Vector3.one;
            float rootY = root.position.y;
            // ---- frame: back board, 2 posts (to the real ground under each), ridge, 2 roof planks. No collider, Batching Static ----
            var st = Child(root, "Structure");
            WoodPart(st, "Back", new Vector3(0f, BoardCanvasCenterY, BackZ), Quaternion.identity, new Vector3(BackW, BackH, BackDepth), wood);
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -PostX : PostX;
                float ground = Ground(root.TransformPoint(new Vector3(x, 0f, BackZ))) - rootY;
                float bottom = ground - PostSink, top = PostTop;
                WoodPart(st, i == 0 ? "PostL" : "PostR", new Vector3(x, (top + bottom) * 0.5f, BackZ), Quaternion.identity, new Vector3(PostSize, top - bottom, PostSize), wood);
            }
            WoodPart(st, "Ridge", new Vector3(0f, RidgeY, BackZ), Quaternion.identity, new Vector3(RoofLength, RidgeSize, RidgeSize), wood);
            float half = RoofPlankDepth * 0.5f, lift = RidgeSize * 0.5f;
            var front = Quaternion.Euler(-RoofSlope, 0f, 0f); var back = Quaternion.Euler(RoofSlope, 0f, 0f);
            WoodPart(st, "RoofFront", new Vector3(0f, RidgeY + lift, BackZ) + front * new Vector3(0f, 0f, -half), front, new Vector3(RoofLength, RoofThick, RoofPlankDepth), wood);
            WoodPart(st, "RoofBack", new Vector3(0f, RidgeY + lift, BackZ) + back * new Vector3(0f, 0f, half), back, new Vector3(RoofLength, RoofThick, RoofPlankDepth), wood);

            // ---- read-only canvas (vrc-world-ui §2: no raycaster / VRCUiShape / collider) ----
            var ct = root.Find("Canvas");
            GameObject cgo;
            if (ct == null) { cgo = new GameObject("Canvas", typeof(RectTransform)); cgo.transform.SetParent(root, false); } else cgo = ct.gameObject;
            cgo.layer = 0;
            foreach (var comp in cgo.GetComponents<Component>())
                if (comp is GraphicRaycaster || comp is Collider || comp is CanvasScaler || comp is VRC.SDK3.Components.VRCUiShape) UnityEngine.Object.DestroyImmediate(comp);
            var canvas = Comp<Canvas>(cgo);
            canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = false;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            var crt = (RectTransform)cgo.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f); crt.sizeDelta = BoardCanvasMm;
            crt.localPosition = new Vector3(0f, BoardCanvasCenterY, 0f); crt.localRotation = Quaternion.identity; crt.localScale = Vector3.one * Mm;
            string T(string key) { return KD_LocalizationEditor.Text(c.catalog, c.catalog.fallbackLanguage, key); }
            var plate = Img(crt, "HeaderPlate", HeaderPlatePos, HeaderPlateSize, HeaderPlateColor);
            var caption = BoardText(plate.transform, "Caption", Vector2.zero, CaptionSize, CaptionMax, CaptionMin, SignInk, T("gallery.caption"), c.font);
            var paper = Img(crt, "Paper", PaperPos, PaperSize, Washi);
            var state = BoardText(paper.transform, "State", StatePos - PaperPos, StateSize, StateMax, StateMin, Sumi, T("photo.empty"), c.font);
            for (int k = 0; k < PageSize; k++)
                Img(paper.transform, "Cell" + k, CellPos(k) - PaperPos, PhotoMm + Vector2.one * (CellBorderMm * 2f), CellColor);

            // ---- the 8 photos of the shown page: Quads with the KD_PhotoPrint material (the VRChat camera snap), trigger collider + KD_PhotoBoard (phones: tap to view) ----
            var photosT = Child(root, "Photos");
            photosT.localPosition = Vector3.zero; photosT.localRotation = Quaternion.identity; photosT.localScale = Vector3.one;   // uniform: the shader's aim test needs no shear
            for (int k = 0; k < PageSize; k++)
            {
                var q = photosT.Find("Photo" + k);
                GameObject go;
                if (q == null) { go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Photo" + k; go.transform.SetParent(photosT, false); } else go = q.gameObject;
                foreach (var mc in go.GetComponents<MeshCollider>()) UnityEngine.Object.DestroyImmediate(mc);
                go.layer = 0;
                var cp = CellPos(k);
                go.transform.localPosition = new Vector3(cp.x * Mm, BoardCanvasCenterY + cp.y * Mm, PhotoFrontZ);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = new Vector3(PhotoMm.x * Mm, PhotoMm.y * Mm, 1f);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(PrintMatPath(k));
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var box = Comp<BoxCollider>(go); box.size = new Vector3(1f, 1f, 0.02f); box.center = Vector3.zero; box.isTrigger = true;   // never blocks a player
                var tap = EnsureUsc<KD_PhotoBoard>(go, log);
                tap.viewer = s.viewer; tap.slotIndex = k;
                var tapUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(tap);
                tapUdon.interactText = T("KD_PhotoBoard.InteractWord"); tapUdon.proximity = 2f; EditorUtility.SetDirty(tapUdon);
                go.SetActive(false);   // shown by KH_PhotoAlbum when the slot holds a photo
                EditorUtility.SetDirty(go);
            }
            BuildBoardPager(c, s, root, log);
            log.Add("board built: canvas " + BoardCanvasMm + " mm, photos " + PhotoMm + " mm (gap " + (PhotoPitchX - PhotoMm.x) + " mm), frame " + RoofLength.ToString("F2") + " m wide, canvas centre " + BoardCanvasCenterY.ToString("F2") + " m");
            caption.text = T("gallery.caption"); state.text = T("photo.empty");
        }

        /// <summary>IMP-WLD-007 W1: ボードの ◀ 1/3 ▶。押せる Canvas を別に持つ (読むだけの Canvas に Raycaster を付けない)。
        /// 9 枚以上のときだけ KH_PhotoAlbum が出す (最初は隠す)。文言のキーは使わない。</summary>
        static void BuildBoardPager(Ctx c, Sys s, Transform root, List<string> log)
        {
            var albumUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(s.album);
            var t = root.Find(BoardPagerName);
            GameObject go;
            if (t == null) { go = new GameObject(BoardPagerName, typeof(RectTransform)); go.transform.SetParent(root, false); } else go = t.gameObject;
            go.layer = 0;   // "UI" does not take the pointer (vrc-world-ui §2)
            var canvas = Comp<Canvas>(go);
            canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = false;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            foreach (var cs in go.GetComponents<CanvasScaler>()) UnityEngine.Object.DestroyImmediate(cs);
            Comp<GraphicRaycaster>(go);
            Comp<VRC.SDK3.Components.VRCUiShape>(go);
            var box = Comp<BoxCollider>(go); box.isTrigger = true; box.center = Vector3.zero; box.size = new Vector3(BoardPagerMm.x, BoardPagerMm.y, 2f);   // never blocks a player
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = BoardPagerMm;
            rt.localPosition = new Vector3(0f, BoardCanvasCenterY + BoardPagerY * Mm, BoardPagerZ); rt.localRotation = Quaternion.identity; rt.localScale = Vector3.one * Mm;
            GameObject Button(string name, float x, string text, string evt)
            {
                var b = Btn(go.transform, name, new Vector2(x, 0f), BoardPagerButtonMm, text, c, albumUdon, evt, log);
                var l = LabelOf(b); if (l != null) { l.fontSizeMax = 56f; l.fontSize = 56f; l.fontSizeMin = 28f; EditorUtility.SetDirty(l); }
                foreach (var tr in b.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = 0;
                return b;
            }
            Button("Prev", -BoardPagerButtonX, "◀", "_BoardPagePrev");
            Button("Next", BoardPagerButtonX, "▶", "_BoardPageNext");
            var label = BoardText(go.transform, "Label", Vector2.zero, BoardPagerLabelMm, 52f, 28f, Sumi, "1/3", c.font);
            var bg = Img(go.transform, "Plate", Vector2.zero, BoardPagerMm, Washi); bg.transform.SetAsFirstSibling();   // the paper colour behind the buttons
            go.SetActive(false);
            EditorUtility.SetDirty(go);
            log.Add("board pager: " + BoardPagerMm + " mm at canvas y " + BoardPagerY + " mm (z " + BoardPagerZ + "), ◀ -> _BoardPagePrev, ▶ -> _BoardPageNext, back to page 1 after " + s.album.boardPageResetSeconds + " s");
        }

        static Transform Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(parent, false); }
            t.gameObject.layer = 0;
            return t;
        }

        static void WoodPart(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 size, Material wood)
        {
            var t = parent.Find(name);
            GameObject go;
            if (t == null) { go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); } else go = t.gameObject;
            foreach (var col in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            go.layer = 0;
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = wood; mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.BlendProbes; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            EditorUtility.SetDirty(mr);
        }

        static Image Img(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            go.layer = 0;
            foreach (var g in go.GetComponents<Graphic>()) if (!(g is Image)) UnityEngine.Object.DestroyImmediate(g);
            var img = Comp<Image>(go);
            img.sprite = null; img.type = Image.Type.Simple; img.color = color; img.raycastTarget = false;
            EditorUtility.SetDirty(img);
            return img;
        }

        static TextMeshProUGUI BoardText(Transform parent, string name, Vector2 pos, Vector2 size, float max, float min, Color color, string text, TMP_FontAsset font)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            go.layer = 0;
            foreach (var g in go.GetComponents<Graphic>()) if (!(g is TextMeshProUGUI)) UnityEngine.Object.DestroyImmediate(g);
            var t = Comp<TextMeshProUGUI>(go);
            t.font = font; t.fontStyle = FontStyles.Normal;
            t.enableAutoSizing = true; t.fontSizeMax = max; t.fontSizeMin = min; t.fontSize = max;
            t.alignment = TextAlignmentOptions.Center; t.color = color; t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow; t.richText = true; t.margin = Vector4.zero; t.raycastTarget = false;
            t.text = text;
            EditorUtility.SetDirty(t);
            return t;
        }

        // ================================================================ where the board goes

        class Anchors
        {
            public Transform guideA; public float halfA; public List<Transform> guides = new List<Transform>();
            public Vector3 spawn, spawnFwd, gate, kominka; public bool hasSpawn, hasGate, hasKominka;
            public List<Vector3> facilities = new List<Vector3>(), roam = new List<Vector3>(), portals = new List<Vector3>();
            public List<Bounds> washFloors = new List<Bounds>();
            public Vector3[] route; public bool loop;
            public Transform boardRoot;
        }

        static Anchors FindAnchors(Ctx c)
        {
            var a = new Anchors();
            var guides = SceneRoot(c.scene, GuideRootName, out _);
            if (guides != null) foreach (Transform t in guides.transform) a.guides.Add(t);
            a.guideA = guides != null ? guides.transform.Find(GuideAName) : null;
            if (a.guideA != null) a.halfA = HalfAlong(a.guideA, a.guideA.right);
            var spawn = KD_SceneObjects.Find("KH_Entrance");
            if (spawn != null) { a.hasSpawn = true; a.spawn = spawn.transform.position; a.spawnFwd = Flat3(spawn.transform.forward).normalized; }
            var gate = KD_SceneObjects.Find("KH_GateBlock"); if (gate != null) { a.hasGate = true; a.gate = gate.transform.position; }
            var kom = KD_SceneObjects.Find("KH_FarKominka"); if (kom != null) { a.hasKominka = true; a.kominka = kom.transform.position; }
            foreach (var name in new[] { "KD_ShopKeeper", "KH_LeashRack", "KD_WelcomeSpot" }) { var g = KD_SceneObjects.Find(name); if (g != null) a.facilities.Add(g.transform.position); }
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t.gameObject.scene != c.scene) continue;
                if (t.name.StartsWith("KD_TreatHome_") || t.name.StartsWith("KD_TreatSlot_")) a.facilities.Add(t.position);
                else if (t.name.StartsWith("KH_Roam_")) a.roam.Add(t.position);
                else if (t.name == "PersonalFloor")
                {
                    bool has = false; var b = new Bounds();
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true)) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
                    foreach (var col in t.GetComponentsInChildren<Collider>(true)) { if (!has) { b = col.bounds; has = true; } else b.Encapsulate(col.bounds); }
                    if (has) a.washFloors.Add(b);
                }
                else if (t.name == "PortalAnchor") a.portals.Add(t.position);
            }
            foreach (var m in UnityEngine.Object.FindObjectsOfType<VRC.SDK3.Components.VRCPortalMarker>(true)) a.portals.Add(m.transform.position);
            if (c.pouch.manager != null && c.pouch.manager.route != null) { a.route = c.pouch.manager.route.nodePositions; a.loop = c.pouch.manager.route.closedLoop; }
            var board = SceneRoot(c.scene, BoardRootName, out _); a.boardRoot = board != null ? board.transform : null;
            return a;
        }

        /// <summary>看板の外形の半幅 (その板の right 方向)。Structure の Renderer の bounds の角を射影する。</summary>
        static float HalfAlong(Transform board, Vector3 axis)
        {
            axis = Flat3(axis).normalized; float half = 0f;
            var st = board.Find("Structure");
            foreach (var r in (st != null ? st : board).GetComponentsInChildren<Renderer>(true))
                foreach (var corner in WorldCorners(r)) half = Mathf.Max(half, Mathf.Abs(Vector3.Dot(Flat3(corner - board.position), axis)));
            return half > 0.1f ? half : 1.7f;
        }

        /// <summary>看板の外形 (その板の local x / z)。Structure の Renderer の bounds から。</summary>
        static Rect LocalRect(Transform board)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue; bool any = false;
            var st = board.Find("Structure");
            var rot = Quaternion.Inverse(Quaternion.Euler(0f, board.eulerAngles.y, 0f));
            foreach (var r in (st != null ? st : board).GetComponentsInChildren<Renderer>(true))
                foreach (var corner in WorldCorners(r))
                {
                    var l = rot * Flat3(corner - board.position); any = true;
                    x0 = Mathf.Min(x0, l.x); x1 = Mathf.Max(x1, l.x); z0 = Mathf.Min(z0, l.z); z1 = Mathf.Max(z1, l.z);
                }
            return any ? Rect.MinMaxRect(x0, z0, x1, z1) : Rect.MinMaxRect(-1f, -0.3f, 1f, 0.3f);
        }

        /// <summary>Renderer の向きのままの 8 隅 (MeshFilter の局所 bounds を変換)。world AABB (Renderer.bounds) は斜めの看板で大きく膨らむので使わない。</summary>
        static IEnumerable<Vector3> WorldCorners(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { foreach (var c in Corners(r.bounds)) yield return c; yield break; }
            var m = r.transform.localToWorldMatrix;
            foreach (var c in Corners(mf.sharedMesh.bounds)) yield return m.MultiplyPoint3x4(c);
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++) yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        }

        /// <summary>候補を順に試し、最初に全部の条件と実際の Collider を通った場所を採る。A を基準にした A の局所座標 (x = A.right, z = A.forward = 読む人の反対)。
        /// 1) A の右端の先に、A と直角に立てる (L 字。A を読む人が右を向くとこちらが正面。A の読む場所を共有) — 設計の第一候補
        /// 2) 同じ形を左端に 3) A と一直線に右 / 左。どれも通らなければ理由を並べて何も変えない。</summary>
        static bool FindBoardPose(Ctx c, out Vector3 pos, out float yaw, out string how)
        {
            pos = Vector3.zero; yaw = 0f;
            var a = FindAnchors(c);
            if (a.guideA == null) { how = GuideRootName + "/" + GuideAName + " missing (run Guide/1 first)"; return false; }
            float ayaw = a.guideA.eulerAngles.y;
            var arot = Quaternion.Euler(0f, ayaw, 0f);
            Vector3 aRight = arot * Vector3.right, aFwd = arot * Vector3.forward, ap = a.guideA.position;
            float hx = BoardFootprint.width * 0.5f;
            var tried = new List<string>();
            var cands = new List<(string name, float x, float z, float yawOff)>();
            foreach (float zGap in new[] { 0.5f, 0.8f, 1.1f, 0.3f })
                foreach (float xGap in new[] { 0.7f, 0.9f, 1.2f })   // the board is ~0.62 m deep: 0.31 + GuideBoardClear 0.3 from A
                    cands.Add(("L-right", a.halfA + xGap, -(zGap + hx), 90f));
            foreach (float zGap in new[] { 0.5f, 0.8f, 1.1f })
                foreach (float xGap in new[] { 0.7f, 0.9f, 1.2f })   // the board is ~0.62 m deep: 0.31 + GuideBoardClear 0.3 from A
                    cands.Add(("L-left", -(a.halfA + xGap), -(zGap + hx), -90f));
            foreach (float gap in new[] { 0.4f, 0.8f })
            {
                cands.Add(("inline-right", a.halfA + gap + hx, 0f, 0f));
                cands.Add(("inline-left", -(a.halfA + gap + hx), 0f, 0f));
            }
            foreach (var cand in cands)
            {
                var p = ap + aRight * cand.x + aFwd * cand.z;
                float y = ayaw + cand.yawOff;
                p.y = BoardGround(p, y);
                var broken = Rules(c, p, y, null, a);
                if (broken.Count == 0)
                {
                    pos = p; yaw = y;
                    how = cand.name + " at A-local (" + cand.x.ToString("F2") + ", " + cand.z.ToString("F2") + ") = " + V(p) + " yaw " + Mathf.Repeat(y, 360f).ToString("F1")
                        + " (A " + V(ap) + " yaw " + ayaw.ToString("F1") + ", half " + a.halfA.ToString("F2") + ")" + (tried.Count > 0 ? "; rejected before: " + string.Join(" | ", tried.Take(6)) : "");
                    return true;
                }
                tried.Add(cand.name + "(" + cand.x.ToString("F1") + "," + cand.z.ToString("F1") + "): " + string.Join(", ", broken.Take(2)));
            }
            how = "no spot next to guide board A passed the rules (" + cands.Count + " tried): " + string.Join(" | ", tried.Take(10));
            return false;
        }

        /// <summary>板の下の地面の高さ (柱の 2 点と中央の最大)。</summary>
        static float BoardGround(Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float g = float.NegativeInfinity;
            foreach (float x in new[] { -PostX, 0f, PostX }) g = Mathf.Max(g, Ground(p + rot * new Vector3(x, 0f, BackZ)));
            return g;
        }

        /// <summary>置き場所の条件 (破れたものを返す)。board は自分 (既存の板。Collider の検査から外す)。</summary>
        static List<string> Rules(Ctx c, Vector3 pos, float yaw, Transform board, Anchors a = null)
        {
            if (a == null) a = FindAnchors(c);
            var bad = new List<string>();
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var fr = BoardFootprint; var rr = BoardReaders;
            var foot = new List<Vector3>(); var read = new List<Vector3>();
            for (int i = 0; i <= 8; i++) for (int j = 0; j <= 2; j++) foot.Add(pos + rot * new Vector3(fr.xMin + fr.width * i / 8f, 0f, fr.yMin + fr.height * j / 2f));
            for (int i = 0; i <= 4; i++) for (int j = 0; j <= 2; j++) read.Add(pos + rot * new Vector3(rr.xMin + rr.width * i / 4f, 0f, rr.yMin + rr.height * j / 2f));
            // route
            if (a.route != null && a.route.Length > 1)
            {
                float d = foot.Min(q => RouteDistance(q, a.route, a.loop));
                if (d < RouteClear) bad.Add("route " + d.ToString("F2") + " m (< " + RouteClear + ")");
            }
            foreach (var r in a.roam) { float d = foot.Min(q => Flat(q - r)); if (d < RoamClear) { bad.Add("dog roam centre " + d.ToString("F2") + " m (< " + RoamClear + ")"); break; } }
            if (a.hasSpawn)
            {
                float ds = foot.Min(q => Flat(q - a.spawn)), dr = read.Min(q => Flat(q - a.spawn));
                if (ds < SpawnClear) bad.Add("spawn " + ds.ToString("F2") + " m (< " + SpawnClear + ")");
                if (dr < SpawnReaderClear) bad.Add("readers on the spawn " + dr.ToString("F2") + " m (< " + SpawnReaderClear + ")");
                float bb = Bearing(a.spawn, pos);
                if (a.hasGate && Mathf.Abs(Mathf.DeltaAngle(bb, Bearing(a.spawn, a.gate))) < BackViewClear) bad.Add("in the look back to the gate from the spawn");
                if (a.hasKominka && Mathf.Abs(Mathf.DeltaAngle(bb, Bearing(a.spawn, a.kominka))) < BackViewClear) bad.Add("in the look back to the farmhouse from the spawn");
                if (a.hasGate)
                {
                    float gl = foot.Min(q => Vector3.Dot(Flat3(q - a.gate), a.spawnFwd));
                    if (gl < GateLineClear) bad.Add("gate line " + gl.ToString("F2") + " m (< " + GateLineClear + ")");
                }
            }
            foreach (var f in a.facilities) { float d = foot.Min(q => Flat(q - f)); if (d < FacilityClear) { bad.Add("keeper / leash rack / welcome / treats " + d.ToString("F2") + " m (< " + FacilityClear + ")"); break; } }
            foreach (var b in a.washFloors)
            {
                var e = b; e.Expand(new Vector3(WashFloorClear * 2f, 100f, WashFloorClear * 2f));
                if (foot.Any(q => e.Contains(new Vector3(q.x, e.center.y, q.z))) || read.Any(q => b.Contains(new Vector3(q.x, b.center.y, q.z)))) { bad.Add("wash station floor"); break; }
            }
            foreach (var p in a.portals) { float d = foot.Concat(read).Min(q => Flat(q - p)); if (d < PortalClear) { bad.Add("portal " + d.ToString("F2") + " m (< " + PortalClear + ")"); break; } }
            foreach (var g in a.guides)
            {
                if (g == null || (board != null && g == board)) continue;
                var lr = LocalRect(g); var inv = Quaternion.Inverse(Quaternion.Euler(0f, g.eulerAngles.y, 0f));
                float d = foot.Min(q => RectDistance(inv * Flat3(q - g.position), lr));
                if (d < GuideBoardClear) { bad.Add("guide board " + g.name + " " + d.ToString("F2") + " m (< " + GuideBoardClear + ")"); continue; }
                // その看板を読む場所に立てない (読む人の前を塞がない)
                float half = Mathf.Max(Mathf.Abs(lr.xMin), Mathf.Abs(lr.xMax));
                var readers = Rect.MinMaxRect(-half, -2.0f, half, lr.yMin);
                if (foot.Any(q => readers.Contains(V2(inv * Flat3(q - g.position))))) bad.Add("in front of guide board " + g.name + " (its readers)");
            }
            // ground
            var fg = foot.Select(q => Ground(q)).ToList(); var rg = read.Select(q => Ground(q)).ToList();
            if (fg.Max() - fg.Min() > GroundSpread) bad.Add("board ground spread " + (fg.Max() - fg.Min()).ToString("F2") + " m (> " + GroundSpread + ")");
            if (rg.Max() - rg.Min() > ReaderGroundSpread || Mathf.Abs(rg.Average() - pos.y) > ReaderGroundSpread) bad.Add("readers' ground spread > " + ReaderGroundSpread + " m");
            // solid colliders a player bumps into (the actual ones: walls inside purchased prefabs are not guessed)
            float gy = fg.Max();
            var hb = SolidIn(c, pos, rot, fr, gy + 0.05f, gy + RidgeY + 0.1f, board);
            var hr = SolidIn(c, pos, rot, rr, rg.Min() + 0.2f, rg.Max() + 1.8f, board);
            if (hb.Count > 0) bad.Add("solid in the board volume: " + string.Join(", ", hb.Take(2)));
            if (hr.Count > 0) bad.Add("solid where readers stand: " + string.Join(", ", hr.Take(2)));
            return bad;
        }

        static List<string> SolidIn(Ctx c, Vector3 pos, Quaternion rot, Rect r, float bottom, float top, Transform self)
        {
            Physics.SyncTransforms();
            var centre = pos + rot * new Vector3(r.center.x, 0f, r.center.y); centre.y = (bottom + top) * 0.5f;
            return Physics.OverlapBox(centre, new Vector3(r.width * 0.5f, Mathf.Max(0.05f, (top - bottom) * 0.5f), r.height * 0.5f), rot, ~0, QueryTriggerInteraction.Ignore)
                .Where(x => !(x is TerrainCollider) && x.gameObject.scene == c.scene && (self == null || !x.transform.IsChildOf(self))
                    && x.gameObject.layer != 13 && x.gameObject.layer != 14   // pickups / dogs move
                    && !Physics.GetIgnoreLayerCollision(x.gameObject.layer, 10))
                .Select(x => KD_SceneObjects.PathOf(x.gameObject)).Distinct().ToList();
        }

        static float RouteDistance(Vector3 p, Vector3[] nodes, bool loop)
        {
            float best = float.MaxValue; int n = nodes.Length;
            for (int i = 0; i < n - 1 + (loop ? 1 : 0); i++) best = Mathf.Min(best, SegmentDistance(p, nodes[i], nodes[(i + 1) % n]));
            return best;
        }

        static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z), ab = B - A;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude);
            return Vector2.Distance(P, A + ab * t);
        }

        static float RectDistance(Vector3 local, Rect r)
        {
            float dx = Mathf.Max(r.xMin - local.x, 0f, local.x - r.xMax), dz = Mathf.Max(r.yMin - local.z, 0f, local.z - r.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>地形の高さ (TerrainCollider、無ければ覆っている Terrain の SampleHeight)。岩や看板を地面と取り違えない (Guide/1 と同じ)。</summary>
        static float Ground(Vector3 world)
        {
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(new Vector3(world.x, 1000f, world.z), Vector3.down, 2000f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity, y = 0f;
            foreach (var h in hits) if (h.collider is TerrainCollider && h.distance < best) { best = h.distance; y = h.point.y; }
            if (best < float.PositiveInfinity) return y;
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null || t.terrainData == null) continue;
                var p = t.transform.position; var s = t.terrainData.size;
                if (world.x >= p.x && world.x <= p.x + s.x && world.z >= p.z && world.z <= p.z + s.z) return t.SampleHeight(world) + p.y;
            }
            throw new InvalidOperationException("no terrain under " + V(world));
        }

        static Vector3 Flat3(Vector3 v) { v.y = 0f; return v; }
        static Vector2 V2(Vector3 v) { return new Vector2(v.x, v.z); }
        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
        static float Bearing(Vector3 from, Vector3 to) { return Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg; }
        static string V(Vector3 p) { return "(" + p.x.ToString("F2") + ", " + p.y.ToString("F2") + ", " + p.z.ToString("F2") + ")"; }
    }
}
