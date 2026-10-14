using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-UI-008 C: 裏山版の「アイテムの説明と手元メニューを開く看板」(KH_ItemGuideBoard)。
    ///
    /// 参照元は古民家ドッグランの写真の板「犬とのあそびかた」(KD_PlayGuideEditor、IMP-PRG-013 / 014、play-guide-corner.md)。犬紹介板「庭の犬たち」の右に
    /// 札 6 枚 (おもちゃ・呼ぶ・ライブ・かざり・パーティクル・トレーナー) が並び、札を押すと KD_TreatPouch._OpenFromBoard* が手元メニューをそのタブで開く。
    /// 裏山版は、案内板 (Asobikata / Map / Kominka) と同じ屋根つきの立て看板 (BuildStructure / EnsureCanvas / 見出しの板) に、裏山の手元メニューに合わせた札 8 枚:
    ///   さんぽ・おやつ・かざり・呼ぶ・おもちゃ・ばにまるトレーナー (あそぶ)・写真と撮影カメラ (押せる 7 枚、KD_TreatPouch.Board.cs の _OpenFromBoard*)、
    ///   洗い場・おさんぽカート (押せない 1 枚。手元メニューに入口が無く、洗い場の板・乗り場で選ぶ)。
    /// ライブは裏山で準備中 (DES-LIV-001)、古民家の CE 商品は今回の公開版から外した (IMP-PRG-002) ので札にしない。写真は古民家と違い載せない
    /// (札の写真は撮影が要る。文字の札で始め、写真を足すかはユーザー判断)。
    ///
    /// 押せる面: 描く Canvas (読むだけ) とは別に、同じ大きさの透明な入力の Canvas "Input" (GraphicRaycaster + VRCUiShape + trigger BoxCollider、Default)。
    /// 札の Button は KD_TreatPouch の backing UdonBehaviour へ SendCustomEvent (古民家と同じ。新しい Udon は持たない)。
    /// KH_GuideBoards の下には置かない (Guide/2 の「案内板は読むだけ」に掛かるため。DogBookStand と同じ理由)。置き場はシーン直下。
    ///
    /// 置き場所 (ユーザーの「地図の隣、道の反対側かなぁ」、未確定。初回だけ探す。以後 x / z / yaw は人のもの、y だけ地面へ据え直す):
    /// 地図の看板 (KH_GuideBoards/Map) にいちばん近い周回の道の線分を取り、地図を道に映した側 (反対側) へ、地図の真向かいから道に沿って 0 → ±3 m、
    /// 道の中心線から 2.4〜4.0 m、表を道へ向けて (yaw ±0 / ±10°) 試す。各点を案内板の置き場所の規則 (Guide/2 の Placement) と、看板どうし・写真ボード・
    /// 犬紹介の看板・塀のポスターとの足元・読む場所で判定し (DogBookStand の StandRules と同じ分け方)、全部を満たす最初の点を採る。無ければ、体積の空いた
    /// 最初の点に置き、破った規則を WARN で出す (Guide/23 は FAIL にしない。ユーザーが Play で見て決める)。
    /// Guide/22 Build Item Guide Board / Guide/23 Verify Item Guide Board。</summary>
    public static partial class KH_GuideBoardsEditor
    {
        public const string ItemGuideName = "KH_ItemGuideBoard";
        const string ItemInputName = "Input", ItemPaperName = "Paper";
        const string ItemBuildReportPath = OutDir + "/item-guide-build.json", ItemVerifyReportPath = OutDir + "/item-guide-verify.json";
        static readonly Board ItemBoard = new Board { name = ItemGuideName, createXZ = new Vector2(43.5f, 52.5f), createYaw = 40f, canvasMm = new Vector2(1300f, 1180f), canvasBottom = 0.80f };

        // ---------------------------------------------------------------- layout (canvas mm, centre origin; 1300 x 1180)
        static readonly Vector2 ItemHeaderPos = new Vector2(0f, 495f), ItemHeaderPlate = new Vector2(1240f, 130f), ItemHeaderText = new Vector2(1200f, 120f);
        static readonly Vector2 ItemPaperPos = new Vector2(0f, -71f), ItemPaperSize = new Vector2(1240f, 978f);
        static readonly Vector2 ItemSubtitlePos = new Vector2(0f, 449f), ItemSubtitleSize = new Vector2(1180f, 50f);   // paper-local
        static readonly Vector2 ItemCardSize = new Vector2(580f, 200f);
        const float ItemCardX = 300f, ItemCardTopY = 309f, ItemCardPitch = 220f;                                      // paper-local
        // card-local. Name 450 mm: the longest ja name 「洗い場・おさんぽカート」 (11 full-width) fits at ~41 mm (max 46, min 30); the chip 100 mm
        // holds 「ひらく ▶」 at ~22-24 mm. Name -280..170, chip 170..270 (the card's inner edge 290 - 20)
        static readonly Vector2 ItemNamePos = new Vector2(-55f, 55f), ItemNameSize = new Vector2(450f, 60f);
        static readonly Vector2 ItemOpenPos = new Vector2(220f, 55f), ItemOpenSize = new Vector2(100f, 50f);
        static readonly Vector2 ItemLinePos = new Vector2(0f, -35f), ItemLineSize = new Vector2(540f, 110f);
        const float ItemSubMax = 40f, ItemSubMin = 24f, ItemNameMax = 46f, ItemNameMin = 30f, ItemOpenMax = 32f, ItemOpenMin = 22f, ItemLineMax = 34f, ItemLineMin = 24f;
        static readonly Color ItemCardPress = new Color(0.30f, 0.22f, 0.15f);   // = 写真ボードのセルの色 (押せる札は暗い板 + 明るい字、角丸)
        const string ItemOpenKey = "guide.items.open";

        class ItemCard { public string name, nameKey, lineKey, evt; }
        /// <summary>裏山の手元メニューのタブの並び (world-ui-spec §4 案 A: さんぽ / おやつ / かざり / 呼ぶ / おもちゃ / あそぶ) + 写真 + 現地で選ぶもの。
        /// 名前はタブの文言キーをそのまま使う (札の名前 = タブの名前。古民家の play-guide-corner の規則)。</summary>
        static readonly ItemCard[] ItemCards =
        {
            new ItemCard { name = "CardWalk", nameKey = "KD_TreatPouch.WordWalkTab", lineKey = "guide.items.walk.line", evt = "_OpenFromBoardWalk" },
            new ItemCard { name = "CardTreat", nameKey = "KD_TreatPouch.WordTabTreat", lineKey = "guide.items.treat.line", evt = "_OpenFromBoardTreat" },
            new ItemCard { name = "CardAccessory", nameKey = "KD_TreatPouch.WordTabAcc", lineKey = "guide.items.acc.line", evt = "_OpenFromBoardAccessory" },
            new ItemCard { name = "CardCall", nameKey = "KD_TreatPouch.WordTabSleep", lineKey = "guide.items.call.line", evt = "_OpenFromBoardCall" },
            new ItemCard { name = "CardToy", nameKey = "KD_TreatPouch.WordTabToy", lineKey = "guide.items.toy.line", evt = "_OpenFromBoardToy" },
            new ItemCard { name = "CardTrainer", nameKey = "guide.items.trainer.name", lineKey = "guide.items.trainer.line", evt = "_OpenFromBoardPlay" },
            new ItemCard { name = "CardPhoto", nameKey = "guide.items.photo.name", lineKey = "guide.items.photo.line", evt = "_OpenFromBoardPhoto" },
            new ItemCard { name = "CardWash", nameKey = "guide.items.wash.name", lineKey = "guide.items.wash.line", evt = null },
        };
        static Vector2 ItemCardPos(int i) { return new Vector2((i % 2 == 0 ? -1f : 1f) * ItemCardX, ItemCardTopY - (i / 2) * ItemCardPitch); }

        /// <summary>The labels (root KH_ItemGuideBoard) the scene and UILocalizationBindings.json must agree on.</summary>
        static List<LabelSpec> ItemLabels()
        {
            var list = new List<LabelSpec> { new LabelSpec(CanvasName + "/Header", "guide.items.title"), new LabelSpec(CanvasName + "/" + ItemPaperName + "/Subtitle", "guide.items.subtitle") };
            foreach (var card in ItemCards)
            {
                string p = CanvasName + "/" + ItemPaperName + "/" + card.name + "/";
                list.Add(new LabelSpec(p + "Name", card.nameKey));
                list.Add(new LabelSpec(p + "Line", card.lineKey));
                if (card.evt != null) list.Add(new LabelSpec(p + "Open", ItemOpenKey));
            }
            return list;
        }

        // ================================================================ context

        static Ctx ResolveItem(bool forBuild)
        {
            var c = new Ctx { scene = EditorSceneManager.GetActiveScene() };
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (pouches.Length != 1) throw new InvalidOperationException("exactly one KD_TreatPouch required (" + pouches.Length + "); nothing changed");
            c.pouch = pouches[0]; c.language = c.pouch.language;
            if (c.language == null) throw new InvalidOperationException("KD_TreatPouch.language not wired (Menu/11); nothing changed");
            c.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (c.font == null || (c.pouch.sectionTitleLabel != null && c.pouch.sectionTitleLabel.font != c.font)) throw new InvalidOperationException("the menu font is not " + KD_ShopSetupEditor.SharedFontPath + "; nothing changed");
            c.catalog = KD_LocalizationEditor.Load();
            c.wood = AssetDatabase.LoadAssetAtPath<Material>(WoodMatPath);
            if (c.wood == null) throw new InvalidOperationException(WoodMatPath + " missing; nothing changed");
            c.round = KD_UiSpriteEditor.LoadRound();
            if (c.round == null) throw new InvalidOperationException(KD_UiSpriteEditor.RoundSpritePath + " missing; nothing changed");
            var managers = UnityEngine.Object.FindObjectsOfType<KH_LeashManager>(true);
            if (managers.Length != 1 || managers[0].route == null || managers[0].route.nodePositions == null || managers[0].route.nodePositions.Length < 2)
                throw new InvalidOperationException("one KH_LeashManager with its KH_WalkRoute nodes required (Course/80); nothing changed");
            c.manager = managers[0];
            var guide = c.scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray();
            if (guide.Length != 1 || guide[0].transform.Find(MapBoard.name) == null) throw new InvalidOperationException(RootName + "/" + MapBoard.name + " must exist (Guide/1): the board stands across the road from the map; nothing changed");
            c.root = guide[0].transform;
            var missing = new List<string>();
            foreach (var lang in c.catalog.languages)
                foreach (var l in ItemLabels())
                {
                    var e = lang.texts.FirstOrDefault(x => x.key == l.key);
                    if (lang.code == c.catalog.fallbackLanguage && (e == null || string.IsNullOrWhiteSpace(e.text))) missing.Add(lang.code + ":" + l.key);
                }
            if (missing.Count > 0) throw new InvalidOperationException("item guide texts missing in " + KD_MenuFinishEditor.CatalogPath + ": " + string.Join(", ", missing.Take(10)) + "; nothing changed");
            var manifest = JsonUtility.FromJson<KD_LocalizationEditor.Manifest>(File.ReadAllText(KD_LocalizationEditor.BindingsPath));
            foreach (var l in ItemLabels())
                if (!manifest.labels.Any(b => b.root == ItemGuideName && b.path == l.path && b.key == l.key))
                    throw new InvalidOperationException("binding missing in " + KD_LocalizationEditor.BindingsPath + ": " + ItemGuideName + "/" + l.path + " = " + l.key + "; nothing changed");
            var foreign = manifest.labels.Where(b => b.root == ItemGuideName && !ItemLabels().Any(l => l.path == b.path)).Select(b => b.path).ToArray();
            if (foreign.Length > 0) throw new InvalidOperationException("bindings under " + ItemGuideName + " that this builder does not make: " + string.Join(", ", foreign) + "; nothing changed");
            foreach (var card in ItemCards)
                if (card.evt != null && typeof(KD_TreatPouch).GetMethod(card.evt, BindingFlags.Public | BindingFlags.Instance) == null)
                    throw new InvalidOperationException("KD_TreatPouch." + card.evt + " missing (KD_TreatPouch.Board.cs; compile first); nothing changed");
            var roots = c.scene.GetRootGameObjects().Where(g => g.name == ItemGuideName).ToArray();
            if (roots.Length > 1) throw new InvalidOperationException(roots.Length + " " + ItemGuideName + " at the scene root (keep one); nothing changed");
            KD_SceneObjects.Find(ItemGuideName, out int anywhere);
            if (anywhere != roots.Length) throw new InvalidOperationException(ItemGuideName + " exists below another object (move it to the scene root); nothing changed");
            return c;
        }

        // ================================================================ where: across the road from the map

        class RoadSide { public Vector3 a, dir, opp; public float t0, mapDist; public int seg; }

        /// <summary>The loop segment nearest to the map board, the map's foot on it, and the unit normal pointing to the other side of the road.</summary>
        static RoadSide RoadAcrossMap(Ctx c)
        {
            var map = c.root.Find(MapBoard.name).position;
            var nodes = c.manager.route.nodePositions; int n = nodes.Length, segs = c.manager.route.closedLoop ? n : n - 1;
            int best = 0; float bd = float.PositiveInfinity;
            for (int i = 0; i < segs; i++) { float d = SegDist(map, nodes[i], nodes[(i + 1) % n]); if (d < bd) { bd = d; best = i; } }
            var a = Flat3(nodes[best]); var b = Flat3(nodes[(best + 1) % n]);
            var dir = (b - a).normalized;
            var left = new Vector3(-dir.z, 0f, dir.x);
            float mapSide = Vector3.Dot(left, Flat3(map) - a) >= 0f ? 1f : -1f;
            return new RoadSide { a = a, dir = dir, opp = -mapSide * left, t0 = Vector3.Dot(Flat3(map) - a, dir), mapDist = bd, seg = best };
        }

        static readonly float[] ItemAlongOffsets = { 0f, 0.5f, -0.5f, 1f, -1f, 1.5f, -1.5f, 2f, -2f, 2.5f, -2.5f, 3f, -3f };
        static readonly float[] ItemRoadDistances = { 2.4f, 2.8f, 3.2f, 3.6f, 4.0f };
        static readonly float[] ItemYawOffsets = { 0f, 10f, -10f };

        /// <summary>hard / soft as DogBookStand's StandRules (Placement + the guide boards + the photo board), plus the other boards found by their
        /// renderers (the dog book stand, the wall posters): footprint gap &lt; 0.3 m = hard, readers' areas = soft.</summary>
        static void ItemRules(Board b, Transform t, Ctx c, out List<string> hard, out List<string> soft)
        {
            StandRules(b, t, c, out hard, out soft);
            var foot = Samples(t, Footprint(b), 0.1f);
            var read = Samples(t, ReaderArea(b), 0.2f);
            foreach (var o in OtherBoards(c, t))
            {
                if (Boards.Any(x => x.name == o.t.name && o.t.parent == c.root)) continue;   // StandRules judged the guide boards' footprints
                float gap = foot.Min(q => RectGap(o.foot, o.t.InverseTransformPoint(q)));
                if (gap < StandBoardGap) hard.Add("footprint " + gap.ToString("F2") + " m from " + o.name + "'s footprint (< " + StandBoardGap + ")");
                // StandRules looks at the photo board's readers' area only (its frame has no solid collider, so physics does not see an overlap):
                // the footprint gap above is the hard rule for it (Codex review 2026-10-14 中 2); its readers' rules stay StandRules'
                if (o.t.name == "KH_PhotoBoard") continue;
                if (foot.Any(q => RectGap(o.readers, o.t.InverseTransformPoint(q)) <= 0f)) soft.Add("stands in " + o.name + "'s readers' area");
                if (read.Any(q => RectGap(o.foot, o.t.InverseTransformPoint(q)) <= 0f)) soft.Add(o.name + " stands in this board's readers' area");
            }
        }

        static void PlaceItemBoard(Transform stand, Board b, Ctx c, List<string> log)
        {
            var road = RoadAcrossMap(c);
            log.Add("the map board is " + road.mapDist.ToString("F2") + " m from the loop segment " + road.seg + "-" + (road.seg + 1) + "; the other side of the road = normal " + road.opp.ToString("F2"));
            float baseYaw = Mathf.Atan2(road.opp.x, road.opp.z) * Mathf.Rad2Deg;   // +Z away from the readers (they stand on the road side)
            int tried = 0; Vector3? firstClear = null; float firstClearYaw = 0f; string firstClearWhy = null;
            foreach (float s in ItemAlongOffsets)
                foreach (float d in ItemRoadDistances)
                    foreach (float dy in ItemYawOffsets)
                    {
                        tried++;
                        var p = road.a + road.dir * (road.t0 + s) + road.opp * d;
                        float yaw = baseYaw + dy;
                        stand.SetPositionAndRotation(new Vector3(p.x, 0f, p.z), Quaternion.Euler(0f, yaw, 0f));
                        float gy = float.NegativeInfinity;
                        try { foreach (var sp in SeatSamples(b)) gy = Mathf.Max(gy, Ground(stand.TransformPoint(sp))); }
                        catch (Exception e) { log.Add("  try " + tried + ": " + e.Message); continue; }
                        stand.position = new Vector3(p.x, gy, p.z);
                        ItemRules(b, stand, c, out List<string> hard, out List<string> soft);
                        string at = "along " + s.ToString("+0.0;-0.0") + " road " + d.ToString("F1") + " yaw " + yaw.ToString("F1") + " " + V(stand.position);
                        if (hard.Count > 0) continue;
                        if (soft.Count == 0) { log.Add("PLACED " + ItemGuideName + " at the first point that passes every rule (" + tried + " tried): " + at); return; }
                        if (!firstClear.HasValue) { firstClear = stand.position; firstClearYaw = yaw; firstClearWhy = at + " breaks " + string.Join("; ", soft); }
                    }
            if (!firstClear.HasValue) throw new InvalidOperationException("no point across the road from the map (" + tried + " tried) has a clear volume; nothing changed");
            stand.SetPositionAndRotation(firstClear.Value, Quaternion.Euler(0f, firstClearYaw, 0f));
            log.Add("WARN no point passes every rule (" + tried + " tried). PLACED at the first point with a clear volume (USER DECISION NEEDED: Play で見て、場所を変えるか規則の例外にするか): " + firstClearWhy);
        }

        // ================================================================ Guide/22

        [MenuItem(Menu + "22 - Build Item Guide Board")]
        public static void BuildItemGuide()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var c = ResolveItem(true);
            ProbeOtherBindings(new[] { WallPostersName, ItemGuideName });
            var r = new WallReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-item-guide-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, false);
            var b = ItemBoard;
            try
            {
                var existing = c.scene.GetRootGameObjects().FirstOrDefault(g => g.name == ItemGuideName);
                Transform stand;
                if (existing == null)
                {
                    stand = new GameObject(ItemGuideName).transform;
                    SceneManagerMove(stand.gameObject, c.scene);
                    try { PlaceItemBoard(stand, b, c, r.log); }
                    catch { UnityEngine.Object.DestroyImmediate(stand.gameObject); throw; }
                }
                else
                {
                    stand = existing.transform;
                    var e = stand.eulerAngles; stand.rotation = Quaternion.Euler(0f, e.y, 0f);
                    r.log.Add("kept " + ItemGuideName + " x/z/yaw (human-owned after the first build): " + V(stand.position) + " yaw " + e.y.ToString("F1") + " (delete it to search again)");
                }
                stand.localScale = Vector3.one; stand.gameObject.layer = 0; stand.gameObject.SetActive(true);
                float y = float.NegativeInfinity;
                foreach (var p in SeatSamples(b)) y = Mathf.Max(y, Ground(stand.TransformPoint(p)));
                stand.position = new Vector3(stand.position.x, y, stand.position.z);
                BuildItemBoard(stand, b, c, r.log);
                var log = new StringBuilder();
                KD_ShopSetupEditor.AddGlyphs(ItemGlyphText(c), log);
                BindIfComplete(c, log, new[] { WallPostersName, ItemGuideName });
                foreach (var line in log.ToString().Split('\n')) if (line.Trim().Length > 0 && !line.StartsWith("  AUTOSIZE")) r.log.Add(line.Trim());
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("item guide scene save failed");
            }
            catch
            {
                Debug.LogError("[KH Item Guide] Build failed; the scene before this run is at " + r.backup + ". Reopen the scene without saving to discard partial changes.");
                r.status = "FAIL (exception, not saved)";
                WriteJson(ItemBuildReportPath, r);
                throw;
            }
            r.status = "BUILT";
            WriteJson(ItemBuildReportPath, r);
            var v = CheckItemGuide();
            WriteJson(ItemVerifyReportPath, v);
            if (v.failed > 0)
            {
                Debug.LogError("[KH Item Guide] ITEM_GUIDE_FAIL after build (" + v.failed + " failed) -> " + ItemVerifyReportPath);
                throw new InvalidOperationException("[KH Item Guide] Build saved but " + v.failed + " check(s) failed:\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL")).Take(30)));
            }
            Debug.Log("[KH Item Guide] BUILT and ITEM_GUIDE_OK (" + v.lines.Count + " checks, " + v.warned + " WARN) -> " + ItemBuildReportPath + " / " + ItemVerifyReportPath);
        }

        static void BuildItemBoard(Transform stand, Board b, Ctx c, List<string> log)
        {
            var keep = new HashSet<Transform>();
            BuildStructure(stand, b, c, keep);
            var canvas = EnsureCanvas(stand, b, keep);
            Img(canvas, "HeaderPlate", ItemHeaderPos, ItemHeaderPlate, HeaderPlateColor, null, keep);
            Txt(canvas, "Header", ItemHeaderPos, ItemHeaderText, MapTitleMax, MapTitleMin, TextAlignmentOptions.Center, SignInk, false, c, keep);
            var paper = Img(canvas, ItemPaperName, ItemPaperPos, ItemPaperSize, Washi, null, keep).transform;
            Txt(paper, "Subtitle", ItemSubtitlePos, ItemSubtitleSize, ItemSubMax, ItemSubMin, TextAlignmentOptions.Center, Sumi, false, c, keep);

            // ---- the input canvas (transparent; the cards' hit areas) ----
            var inT = stand.Find(ItemInputName);
            GameObject inGo;
            if (inT == null) { inGo = new GameObject(ItemInputName, typeof(RectTransform)); inGo.transform.SetParent(stand, false); }
            else inGo = inT.gameObject;
            inGo.layer = 0;
            var inCanvas = inGo.GetComponent<Canvas>(); if (inCanvas == null) inCanvas = inGo.AddComponent<Canvas>();
            inCanvas.renderMode = RenderMode.WorldSpace; inCanvas.overrideSorting = false;
            if (inGo.GetComponent<GraphicRaycaster>() == null) inGo.AddComponent<GraphicRaycaster>();
            var box = inGo.GetComponent<BoxCollider>(); if (box == null) box = inGo.AddComponent<BoxCollider>();
            box.isTrigger = true; box.center = Vector3.zero; box.size = new Vector3(b.canvasMm.x, b.canvasMm.y, 10f);
            if (inGo.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null) inGo.AddComponent<VRC.SDK3.Components.VRCUiShape>();
            var inRt = (RectTransform)inGo.transform;
            inRt.anchorMin = inRt.anchorMax = inRt.pivot = new Vector2(0.5f, 0.5f);
            inRt.sizeDelta = b.canvasMm; inRt.localPosition = new Vector3(0f, CanvasCenterY(b), -0.004f); inRt.localRotation = Quaternion.identity; inRt.localScale = Vector3.one * Mm;
            var blocker = inGo.GetComponent<Image>(); if (blocker == null) blocker = inGo.AddComponent<Image>();
            blocker.color = Color.clear; blocker.raycastTarget = true; blocker.sprite = null;
            keep.Add(inRt);
            var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(c.pouch);
            if (pouchUdon == null) throw new InvalidOperationException("KD_TreatPouch backing UdonBehaviour missing; reopen the scene without saving");

            for (int i = 0; i < ItemCards.Length; i++)
            {
                var card = ItemCards[i];
                bool press = card.evt != null;
                if (card.evt == "_OpenFromBoardPhoto" && c.pouch.photoPage == null) log.Add("WARN the photo page (Photo/1) is not built: the photo card opens the menu but not the page");
                var pos = ItemCardPos(i);
                var face = Img(paper, card.name, pos, ItemCardSize, press ? ItemCardPress : StripWashi, press ? c.round : null, keep);
                if (press) { face.type = Image.Type.Sliced; face.pixelsPerUnitMultiplier = 4f; EditorUtility.SetDirty(face); }   // ~7 mm corners on the mm canvas (as the cut callouts)
                var ink = press ? SignInk : Sumi;
                Txt(face.transform, "Name", ItemNamePos, ItemNameSize, ItemNameMax, ItemNameMin, TextAlignmentOptions.MidlineLeft, ink, false, c, keep);
                var line = Txt(face.transform, "Line", ItemLinePos, ItemLineSize, ItemLineMax, ItemLineMin, TextAlignmentOptions.TopLeft, ink, true, c, keep);
                line.lineSpacing = 6f; EditorUtility.SetDirty(line);
                if (press) Txt(face.transform, "Open", ItemOpenPos, ItemOpenSize, ItemOpenMax, ItemOpenMin, TextAlignmentOptions.MidlineRight, KD_UiSpriteEditor.AccentGold, false, c, keep);

                var hitT = inRt.Find(card.name);
                if (!press) { if (hitT != null) UnityEngine.Object.DestroyImmediate(hitT.gameObject); continue; }
                var hit = KD_ShopSetupEditor.EnsureUiChild(inRt, card.name, ItemPaperPos + pos, ItemCardSize);
                hit.layer = 0;
                var hitImage = hit.GetComponent<Image>(); if (hitImage == null) hitImage = hit.AddComponent<Image>();
                hitImage.color = Color.clear; hitImage.raycastTarget = true; hitImage.sprite = null;
                var button = hit.GetComponent<Button>(); if (button == null) button = hit.AddComponent<Button>();
                button.enabled = true; button.interactable = true; button.targetGraphic = face; button.navigation = new Navigation { mode = Navigation.Mode.None };
                KD_UiSpriteEditor.ApplyButtonFeedback(button);
                while (button.onClick.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(button.onClick, 0);
                UnityEventTools.AddStringPersistentListener(button.onClick, pouchUdon.SendCustomEvent, card.evt);
                EditorUtility.SetDirty(button);
                keep.Add(hit.transform);
            }
            foreach (var g in inRt.GetComponentsInChildren<Graphic>(true)) if (g.color.a == 0f) g.canvasRenderer.cullTransparentMesh = true;

            foreach (Transform t in stand)
                if (t.name != StructureName && t.name != CanvasName && t.name != ItemInputName) log.Add("WARN " + ItemGuideName + "/" + t.name + " is not made by this builder: left as is");
            foreach (var name in new[] { StructureName, CanvasName, ItemInputName })
            {
                var top = stand.Find(name);
                if (top == null) continue;
                var stale = top.GetComponentsInChildren<Transform>(true).Where(t => !keep.Contains(t)).ToList();
                foreach (var t in stale)
                {
                    if (t == null || (t.parent != null && stale.Contains(t.parent))) continue;
                    log.Add("removed stale " + KD_SceneObjects.PathOf(t.gameObject));
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
                }
            }
            log.Add(ItemGuideName + " at " + V(stand.position) + " yaw " + stand.eulerAngles.y.ToString("F1") + ": " + ItemCards.Count(x => x.evt != null) + " cards open the hand menu, " + ItemCards.Count(x => x.evt == null) + " card is read-only");
        }

        static string ItemGlyphText(Ctx c)
        {
            var s = new StringBuilder();
            foreach (var lang in c.catalog.languages)
            {
                if (!KD_LocalizationEditor.Bundled(lang)) continue;
                foreach (var l in ItemLabels()) s.Append(Visible(KD_LocalizationEditor.Text(c.catalog, lang.code, l.key)));
            }
            return s.ToString();
        }

        // ================================================================ Guide/23

        [MenuItem(Menu + "23 - Verify Item Guide Board")]
        public static void VerifyItemGuide()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var v = CheckItemGuide();
            WriteJson(ItemVerifyReportPath, v);
            if (v.failed > 0)
            {
                Debug.LogError("[KH Item Guide] ITEM_GUIDE_FAIL (" + v.failed + " failed, " + v.warned + " WARN) -> " + ItemVerifyReportPath);
                throw new InvalidOperationException("[KH Item Guide] Verify failed:\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL")).Take(30)));
            }
            Debug.Log("[KH Item Guide] ITEM_GUIDE_OK (" + v.lines.Count + " checks, " + v.warned + " WARN) -> " + ItemVerifyReportPath);
        }

        static VerifyReport CheckItemGuide()
        {
            var v = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            v.limitations = "Static checks of the saved scene (IMP-UI-008 C): the stand, the read-only canvas and the input canvas, every card's button against "
                + "KD_TreatPouch's _OpenFromBoard* entries, the bindings, the glyphs and the fit of the 13 languages (ja / en measured with the bundled font; the "
                + "11 client-glyph languages are drawn by VRChat's fallback fonts, so their fit is a WARN estimate), and the placement rules. Not judged here: "
                + "whether pressing a card opens the menu on that page in ClientSim / the client, readability from the road, how people walk around it (Play).";
            Action<bool, string> req = (ok, what) => { v.lines.Add((ok ? "OK   " : "FAIL ") + what); if (!ok) v.failed++; };
            Action<bool, string> warn = (ok, what) => { v.lines.Add((ok ? "OK   " : "WARN ") + what); if (!ok) v.warned++; };
            Ctx c;
            try { c = ResolveItem(false); }
            catch (Exception e) { req(false, "resolve: " + e.Message); v.status = "ITEM_GUIDE_FAIL"; return v; }
            var go = c.scene.GetRootGameObjects().FirstOrDefault(g => g.name == ItemGuideName);
            req(go != null, ItemGuideName + " once at the scene root");
            if (go == null) { v.status = "ITEM_GUIDE_FAIL"; return v; }
            var stand = go.transform; var b = ItemBoard;
            var e3 = stand.eulerAngles;
            req(Mathf.Abs(Mathf.DeltaAngle(e3.x, 0f)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(e3.z, 0f)) < 0.01f && stand.localScale == Vector3.one, ItemGuideName + " yaw only, scale 1");
            req(go.activeInHierarchy && go.layer == 0, ItemGuideName + " active, Default layer");
            try { float seat = SeatSamples(b).Max(p => Ground(stand.TransformPoint(p))); req(Mathf.Abs(stand.position.y - seat) <= 0.02f, ItemGuideName + " seated on the terrain (" + (stand.position.y - seat).ToString("+0.000;-0.000") + " m; moved by hand? run Guide/22)"); }
            catch (Exception ex) { req(false, ItemGuideName + " terrain under the posts: " + ex.Message); }
            var s = stand.Find(StructureName);
            req(s != null, ItemGuideName + "/" + StructureName);
            if (s != null)
                foreach (var name in WoodParts)
                {
                    var p = s.Find(name); var mr = p != null ? p.GetComponent<MeshRenderer>() : null;
                    req(mr != null && mr.sharedMaterial == c.wood && p.GetComponent<Collider>() == null, ItemGuideName + "/" + name + " wood, no collider (as the guide boards)");
                }
            var cv = stand.Find(CanvasName) as RectTransform;
            req(cv != null && cv.GetComponent<Canvas>() != null && cv.sizeDelta == b.canvasMm && Mathf.Abs(cv.localPosition.y - CanvasCenterY(b)) < 0.001f && Mathf.Abs(cv.localScale.x - Mm) < 1e-6f
                && cv.GetComponent<GraphicRaycaster>() == null && cv.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null, ItemGuideName + " drawing canvas " + b.canvasMm + " mm, read-only");
            var inRt = stand.Find(ItemInputName) as RectTransform;
            var inBox = inRt != null ? inRt.GetComponent<BoxCollider>() : null;
            req(inRt != null && inRt.GetComponent<Canvas>() != null && inRt.GetComponent<GraphicRaycaster>() != null && inRt.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null
                && inBox != null && inBox.isTrigger && inRt.gameObject.layer == 0 && inRt.sizeDelta == b.canvasMm && inRt.localPosition.z < 0f && Mathf.Abs(inRt.localScale.x - Mm) < 1e-6f,
                "input canvas: GraphicRaycaster + VRCUiShape + trigger BoxCollider, Default layer, in front of the drawing canvas (vrc-world-ui §2)");
            req(stand.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length == 0, "no UdonBehaviour on the board (the cards call KD_TreatPouch)");
            var solid = stand.GetComponentsInChildren<Collider>(true).Where(x => !x.isTrigger).ToArray();
            req(solid.Length == 0, "no solid Collider under " + ItemGuideName);

            // ---- cards ----
            var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(c.pouch);
            var paper = cv != null ? cv.Find(ItemPaperName) as RectTransform : null;
            req(paper != null, ItemGuideName + "/" + CanvasName + "/" + ItemPaperName);
            var rects = new List<Rect>();
            for (int i = 0; paper != null && i < ItemCards.Length; i++)
            {
                var card = ItemCards[i];
                var face = paper.Find(card.name) as RectTransform;
                var img = face != null ? face.GetComponent<Image>() : null;
                req(img != null && face.anchoredPosition == ItemCardPos(i) && face.sizeDelta == ItemCardSize, card.name + " at " + ItemCardPos(i));
                if (img == null) continue;
                var rc = new Rect(ItemCardPos(i) - ItemCardSize * 0.5f, ItemCardSize);
                req(!rects.Any(o => o.Overlaps(rc)) && Rect.MinMaxRect(-ItemPaperSize.x * 0.5f, -ItemPaperSize.y * 0.5f, ItemPaperSize.x * 0.5f, ItemSubtitlePos.y - ItemSubtitleSize.y * 0.5f).Contains(rc.min)
                    && Rect.MinMaxRect(-ItemPaperSize.x * 0.5f, -ItemPaperSize.y * 0.5f, ItemPaperSize.x * 0.5f, ItemSubtitlePos.y - ItemSubtitleSize.y * 0.5f).Contains(rc.max), card.name + " inside the paper, below the subtitle, not overlapping another card");
                rects.Add(rc);
                bool press = card.evt != null;
                req(press ? (img.sprite == c.round && img.type == Image.Type.Sliced) : img.sprite == null, card.name + (press ? " rounded (pressable)" : " flat (read-only: no menu entry)"));
                var hit = inRt != null ? inRt.Find(card.name) : null;
                if (!press) { req(hit == null, card.name + " has no hit area"); continue; }
                var button = hit != null ? hit.GetComponent<Button>() : null;
                req(button != null && button.targetGraphic == img && button.interactable && hit.GetComponent<Image>() != null && hit.GetComponent<Image>().raycastTarget
                    && ((RectTransform)hit).anchoredPosition == ItemPaperPos + ItemCardPos(i) && ((RectTransform)hit).sizeDelta == ItemCardSize, card.name + " hit area over the card, tints the card");
                req(button != null && OnClickIs(button, pouchUdon, card.evt), card.name + " -> KD_TreatPouch." + card.evt + " (SendCustomEvent on the backing UdonBehaviour)");
                req(typeof(KD_TreatPouch).GetMethod(card.evt, BindingFlags.Public | BindingFlags.Instance) != null && card.evt.StartsWith("_"), "KD_TreatPouch." + card.evt + " exists, public, local (`_`)");
            }
            warn(c.pouch.photoPage != null, "the photo page exists (Photo/1): the photo card opens it");

            // ---- labels: bound, ja text, font, fit ----
            // Guide/22 before Guide/20: the bind waits for the wall posters (BindIfComplete), so the binding lines are WARN until then
            bool bindPending = KD_SceneObjects.Find(WallPostersName) == null;
            Action<bool, string> bound = bindPending ? (ok, what) => warn(ok, what + " (bind deferred: run Guide/20, which binds everything)") : req;
            var lang = c.language;
            foreach (var l in ItemLabels())
            {
                var tr = stand.Find(l.path);
                var tmp = tr != null ? tr.GetComponent<TextMeshProUGUI>() : null;
                req(tmp != null, "label " + l.path);
                if (tmp == null) continue;
                req(tmp.font == c.font && !tmp.raycastTarget && tmp.overflowMode == TextOverflowModes.Overflow, "label " + l.path + ": menu font, not a raycast target, Overflow");
                bound(tmp.text == KD_LocalizationEditor.Text(c.catalog, c.catalog.fallbackLanguage, l.key), "label " + l.path + " holds the ja text of " + l.key + " (Bind)");
                int i = lang.fixedLabels != null ? Array.IndexOf(lang.fixedLabels, tmp) : -1;
                bound(i >= 0 && lang.fixedKeys[i] == l.key, "label " + l.path + " bound to " + l.key);
            }
            var measure = new GameObject("ItemGuideMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var sample = measure.AddComponent<TextMeshProUGUI>();
                foreach (var l in ItemLabels())
                {
                    var tr = stand.Find(l.path);
                    var tmp = tr != null ? tr.GetComponent<TextMeshProUGUI>() : null;
                    if (tmp == null) continue;
                    sample.lineSpacing = tmp.lineSpacing;
                    int worstSelf = 0; var over = new List<string>();
                    foreach (var lg in c.catalog.languages)
                    {
                        int r = KD_LocalizationEditor.MeasureLabelFit(sample, tmp, KD_LocalizationEditor.Text(c.catalog, lg.code, l.key), out Vector2 pref);
                        if (KD_LocalizationEditor.Bundled(lg)) worstSelf = Mathf.Max(worstSelf, r);
                        else if (r == 2) over.Add(lg.code);
                        if (r > 0) v.fit.Add(lg.code + " " + l.key + " preferred=" + pref.ToString("F0") + " rect=" + tmp.rectTransform.rect.size.ToString("F0") + " fit=" + r);
                    }
                    req(worstSelf < 2, l.key + " (" + l.path + ") fits in ja / en (0 full size, 1 shrinks, 2 overflows at " + tmp.fontSizeMin + " mm)");
                    warn(over.Count == 0, l.key + " fits in the client-glyph languages (estimate)" + (over.Count > 0 ? ": overflows in " + string.Join(", ", over) : ""));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(measure); }
            var missing = KD_ShopSetupEditor.MissingGlyphs(ItemGlyphText(c));
            req(missing.Length == 0, "every ja / en character of the board is in the shared font" + (missing.Length > 0 ? " (missing " + missing + "; Guide/22 adds them)" : ""));
            foreach (var lg in c.catalog.languages)
            {
                int empty = ItemLabels().Select(l => l.key).Distinct().Count(k => { var e = lg.texts.FirstOrDefault(x => x.key == k); return e == null || string.IsNullOrWhiteSpace(e.text); });
                warn(empty == 0, lg.code + ": every item guide text present (" + empty + " missing; AI translation, not proofread)");
            }
            try { KD_LocalizationEditor.ValidateScene(c.language, c.catalog); bound(true, "localization tables, bindings and glyphs match the catalogs (KD_LocalizationEditor.ValidateScene)"); }
            catch (Exception e) { bound(false, "localization: " + e.Message + " (Guide/20 / Guide/22 re-bind; the later of the two binds everything)"); }

            // ---- placement: the guide boards' rules, next to the map, across the road ----
            ItemRules(b, stand, c, out List<string> hard, out List<string> soft);
            req(hard.Count == 0, "board volume / readers' area clear, footprint apart from the other boards, ground range" + (hard.Count > 0 ? ": " + string.Join("; ", hard) : ""));
            foreach (var x in soft) warn(false, "placement rule (USER DECISION: 「地図の隣・道の反対側」との兼ね合い): " + x);
            if (soft.Count == 0) req(true, "the board passes the guide boards' placement rules (route, roam, spawn, fixtures, gate, wash, other boards)");
            var road = RoadAcrossMap(c);
            var map = c.root.Find(MapBoard.name);
            float side = Vector3.Dot(Flat3(stand.position) - road.a, road.opp);
            warn(side > 0f, "across the road from the map (" + side.ToString("F2") + " m on the far side of loop segment " + road.seg + ")");
            float dm = Flat(stand.position - map.position);
            warn(dm <= 8f, "near the map: " + dm.ToString("F2") + " m from the map board (<= 8, \"地図の隣\")");
            Vector3 front = -stand.forward; front.y = 0f;
            v.notes.Add(ItemGuideName + " at " + V(stand.position) + " yaw " + stand.eulerAngles.y.ToString("F1") + "; " + dm.ToString("F2") + " m from the map, " + side.ToString("F2") + " m from the road's centre line on the other side, the front "
                + Vector3.Angle(front, -road.opp).ToString("F0") + " deg off facing the road; along the road " + (Vector3.Dot(Flat3(stand.position) - road.a, road.dir) - road.t0).ToString("+0.00;-0.00") + " m from the map's opposite point");
            v.budget = "frame: 6 cube renderers (" + WoodMatPath + ", Batching Static); canvas: header plate, paper, 8 cards (7 rounded), " + ItemLabels().Count + " labels; input canvas: 7 clear hit areas; "
                + "no new texture (KD_UiRound shared), shared font atlas; Udon 0 (the buttons call KD_TreatPouch), trigger colliders 1, network objects 0";
            v.status = v.failed == 0 ? "ITEM_GUIDE_OK" : "ITEM_GUIDE_FAIL";
            return v;
        }

        static bool OnClickIs(Button button, VRC.Udon.UdonBehaviour target, string evt)
        {
            var calls = new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            return calls.arraySize == 1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue == target
                && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_MethodName").stringValue == "SendCustomEvent"
                && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == evt;
        }
    }
}
