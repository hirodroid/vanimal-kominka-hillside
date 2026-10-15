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
    /// <summary>IMP-UI-008 C: 裏山版の「アイテムの説明と手元メニューを開く看板」。
    ///
    /// 参照元は古民家ドッグランの写真の板「犬とのあそびかた」(KD_PlayGuideEditor、IMP-PRG-013 / 014、play-guide-corner.md)。札を押すと
    /// KD_TreatPouch._OpenFromBoard* が手元メニューをそのタブで開く。裏山版は、案内板 (Asobikata / Map / Kominka) と同じ屋根つきの立て看板に、
    /// 裏山の手元メニューに合わせた札 8 枚: さんぽ・おやつ・かざり・呼ぶ・おもちゃ・ばにまるトレーナー (あそぶ)・写真と撮影カメラ (押せる 7 枚、
    /// KD_TreatPouch.Board.cs の _OpenFromBoard*)、洗い場・おさんぽカート (押せない 1 枚)。ライブ (準備中)・CE 商品 (IMP-PRG-002)・
    /// 札の写真・区分の札・公式サイト欄は入れない (決定: 2026-10-14 ユーザー「不要」)。
    ///
    /// 置き場所 (決定: 2026-10-14 ユーザー「洗い場 4 か所の選択ボードの隣」。地図の向かいの 1 枚 KH_ItemGuideBoard はなくす。カートの乗り場には置かない):
    /// 洗い場ごと (KH_WashSelectionBoard = PremiumSelectionBoard のある主洗い場 + KH_WashStations の 3 か所) に 1 枚、KH_ItemGuideBoards/KH_ItemGuideBoard_&lt;洗い場&gt;
    /// (主洗い場は _Main)。初回だけ探す: 洗い場の向き (選択ボードと同じ yaw) のまま、選択ボードのある側 (洗い場ローカル +X / -X) を外へ 2.0 → 7.0 m、
    /// 前後 0 / +0.25 / -0.25 / +0.5 m、yaw ±0 / ±15°。無ければ反対側を同じように。各点を案内板の置き場所の規則 (Guide/2 の Placement。入口の外の洗い場では
    /// 入口だけの規則 (スポーンからの距離・入口の円・門の線・振り返りの視界) を外す) + 看板どうし + 洗い場の規則 (選択ボードの足元 0.3 m、選択ボードの読む場所、
    /// 作業域 + 1 m、個人の床の外 0.5 m、呼び出しの円 (犬よけ)) で判定し、全部を満たす最初の点を採る。無ければ体積が空く最初の点に置いて WARN
    /// (Guide/23 は FAIL にしない。ユーザーが Play で見て決める)。以後 x / z / yaw は人のもの、y だけ地面へ据え直す。
    ///
    /// 押せる面: 描く Canvas (読むだけ) とは別に、同じ大きさの透明な入力の Canvas "Input" (GraphicRaycaster + VRCUiShape + trigger BoxCollider、Default)。
    /// 札の Button は KD_TreatPouch の backing UdonBehaviour へ SendCustomEvent (古民家と同じ。4 枚とも同じ backing を呼ぶ。新しい Udon は無い)。
    /// Guide/22 Build Item Guide Boards / Guide/23 Verify Item Guide Boards。</summary>
    public static partial class KH_GuideBoardsEditor
    {
        public const string ItemGuideRootName = "KH_ItemGuideBoards", ItemGuidePrefix = "KH_ItemGuideBoard_";
        /// <summary>2026-10-14 の最初の版 (地図の向かいの 1 枚)。Guide/22 が消し、Guide/23 が無いことを見る。</summary>
        const string OldItemGuideName = "KH_ItemGuideBoard";
        const string MainWashStationName = "KH_WashStation", SelectionBoardName = "PremiumSelectionBoard";
        const string ItemInputName = "Input", ItemPaperName = "Paper";
        const string ItemBuildReportPath = OutDir + "/item-guide-build.json", ItemVerifyReportPath = OutDir + "/item-guide-verify.json";
        static Board ItemBoard(string name) { return new Board { name = name, createXZ = Vector2.zero, createYaw = 0f, canvasMm = new Vector2(1300f, 1180f), canvasBottom = 0.80f }; }

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

        // ---------------------------------------------------------------- placement search beside a wash station's selection board (station-local metres)
        const float ItemSideFrom = 2.0f, ItemSideTo = 7.0f, ItemSideStep = 0.25f;
        static readonly float[] ItemDepthOffsets = { 0f, 0.25f, -0.25f, 0.5f };
        static readonly float[] ItemYawOffsets = { 0f, 15f, -15f };
        /// <summary>The main wash station's working area (KominkaRules' WorkHalfX / WorkMinZ / WorkMaxZ) + 1 m, in station-local x / z.</summary>
        static Rect StationWorkArea { get { return Rect.MinMaxRect(-WorkHalfX - 1f, WorkMinZ - 1f, WorkHalfX + 1f, WorkMaxZ + 1f); } }
        const float SelectionBoardGap = 0.3f, StationFloorClear = 0.5f, ItemSelectionMax = 7.5f;

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

        /// <summary>The labels of one board (paths below its root) the scene and UILocalizationBindings.json must agree on (one set per board root).</summary>
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

        // ================================================================ the wash stations and the boards

        class WashSpot
        {
            public string key, boardName; public Transform station, selection, floor, callAnchor; public float floorHalf, keepOut; public bool main;
        }

        /// <summary>Every wash station with a selection board (KH_WashSelectionBoard on PremiumSelectionBoard), main first, then by name.</summary>
        static List<WashSpot> WashSpots()
        {
            var list = new List<WashSpot>();
            foreach (var sel in UnityEngine.Object.FindObjectsOfType<KH_WashSelectionBoard>(true))
            {
                var st = sel.transform.parent;
                if (st == null) continue;
                bool main = st.name == MainWashStationName;
                string key = main ? "Main" : st.name;
                var spot = new WashSpot { key = key, boardName = ItemGuidePrefix + key, station = st, selection = sel.transform, main = main };
                var pf = st.GetComponentInChildren<KH_WashPersonalFloor>(true);
                if (pf != null) { spot.floor = pf.transform; spot.floorHalf = Mathf.Max(pf.outerHalf.x, pf.outerHalf.y); }
                var call = st.GetComponentInChildren<KH_WashStationCall>(true);
                if (call == null) call = UnityEngine.Object.FindObjectsOfType<KH_WashStationCall>(true).FirstOrDefault(x => x.anchor != null && x.anchor.IsChildOf(st));
                if (call != null) { spot.callAnchor = call.anchor != null ? call.anchor : call.transform; spot.keepOut = call.keepOutRadius; }
                list.Add(spot);
            }
            return list.OrderBy(s => s.main ? 0 : 1).ThenBy(s => s.key, StringComparer.Ordinal).ToList();
        }

        /// <summary>The item guide board roots the bindings manifest names (KH_ItemGuideBoard_*).</summary>
        static List<string> ManifestItemRoots()
        {
            var manifest = JsonUtility.FromJson<KD_LocalizationEditor.Manifest>(File.ReadAllText(KD_LocalizationEditor.BindingsPath));
            return manifest.labels.Where(b => b.root.StartsWith(ItemGuidePrefix)).Select(b => b.root).Distinct().ToList();
        }

        static bool ItemBoardsBuilt() { return ManifestItemRoots().All(r => KD_SceneObjects.Find(r) != null); }

        static IEnumerable<Transform> ItemBoardTransforms()
        {
            var g = KD_SceneObjects.Find(ItemGuideRootName);
            if (g == null) yield break;
            foreach (Transform t in g.transform) if (t.name.StartsWith(ItemGuidePrefix)) yield return t;
        }

        // ================================================================ context

        static Ctx ResolveItem(out List<WashSpot> spots)
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
            if (guide.Length != 1) throw new InvalidOperationException(RootName + " must exist once (Guide/1): the placement rules measure against its boards; nothing changed");
            c.root = guide[0].transform;
            spots = WashSpots();
            if (spots.Count == 0) throw new InvalidOperationException("no KH_WashSelectionBoard in the scene (the boards stand beside the wash stations' selection boards); nothing changed");
            if (spots.Select(s => s.boardName).Distinct().Count() != spots.Count) throw new InvalidOperationException("two wash stations share a name (" + string.Join(", ", spots.Select(s => s.key)) + "); nothing changed");
            var missing = new List<string>();
            var fallback = c.catalog.languages.First(l => l.code == c.catalog.fallbackLanguage);
            foreach (var l in ItemLabels())
            {
                var e = fallback.texts.FirstOrDefault(x => x.key == l.key);
                if (e == null || string.IsNullOrWhiteSpace(e.text)) missing.Add(l.key);
            }
            if (missing.Count > 0) throw new InvalidOperationException("item guide texts missing in " + KD_MenuFinishEditor.CatalogPath + ": " + string.Join(", ", missing.Distinct().Take(10)) + "; nothing changed");
            var manifest = JsonUtility.FromJson<KD_LocalizationEditor.Manifest>(File.ReadAllText(KD_LocalizationEditor.BindingsPath));
            foreach (var s in spots)
                foreach (var l in ItemLabels())
                    if (!manifest.labels.Any(b => b.root == s.boardName && b.path == l.path && b.key == l.key))
                        throw new InvalidOperationException("binding missing in " + KD_LocalizationEditor.BindingsPath + ": " + s.boardName + "/" + l.path + " = " + l.key + "; nothing changed");
            var boardNames = new HashSet<string>(spots.Select(s => s.boardName));
            var foreign = manifest.labels.Where(b => (b.root == OldItemGuideName || b.root.StartsWith(ItemGuidePrefix)) && !(boardNames.Contains(b.root) && ItemLabels().Any(l => l.path == b.path)))
                .Select(b => b.root + "/" + b.path).ToArray();
            if (foreign.Length > 0) throw new InvalidOperationException("item guide bindings that this builder does not make (no such wash station / path): " + string.Join(", ", foreign.Take(6)) + "; nothing changed");
            foreach (var card in ItemCards)
                if (card.evt != null && typeof(KD_TreatPouch).GetMethod(card.evt, BindingFlags.Public | BindingFlags.Instance) == null)
                    throw new InvalidOperationException("KD_TreatPouch." + card.evt + " missing (KD_TreatPouch.Board.cs; compile first); nothing changed");
            var groups = c.scene.GetRootGameObjects().Where(g => g.name == ItemGuideRootName).ToArray();
            if (groups.Length > 1) throw new InvalidOperationException(groups.Length + " " + ItemGuideRootName + " at the scene root (keep one); nothing changed");
            KD_SceneObjects.Find(ItemGuideRootName, out int anywhere);
            if (anywhere != groups.Length) throw new InvalidOperationException(ItemGuideRootName + " exists below another object (move it to the scene root); nothing changed");
            foreach (var s in spots)
            {
                KD_SceneObjects.Find(s.boardName, out int n);
                if (n > 1) throw new InvalidOperationException(n + " objects named " + s.boardName + " (keep one); nothing changed");
            }
            return c;
        }

        // ================================================================ where: beside the selection board

        /// <summary>Rules the guide boards apply around the entrance only (they fail by distance at a station far up the course).</summary>
        static bool EntranceOnlyRule(string why)
        {
            return why.Contains("within the entrance") || why.Contains("outside the gate") || why.Contains("turn-around view") || why.Contains("from the spawn") || why.Contains("spawn ");
        }

        /// <summary>hard / soft as DogBookStand's StandRules (Placement + the guide boards + the photo board), the other boards found by their renderers
        /// (footprint gap &lt; 0.3 m = hard, readers' areas = soft), and the wash station's own rules.</summary>
        static void ItemRules(Board b, Transform t, Ctx c, WashSpot spot, out List<string> hard, out List<string> soft)
        {
            StandRules(b, t, c, out hard, out soft);
            if (!spot.main) soft = soft.Where(x => !EntranceOnlyRule(x)).ToList();
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
            // ---- the wash station (2026-10-14 ユーザー決定: beside its selection board) ----
            var sel = spot.selection as RectTransform;
            Vector2 selSize = sel != null ? Vector2.Scale(sel.sizeDelta, new Vector2(sel.lossyScale.x, sel.lossyScale.y)) : new Vector2(0.9f, 0.68f);
            var selFoot = Rect.MinMaxRect(-selSize.x * 0.5f - 0.05f, -0.10f, selSize.x * 0.5f + 0.05f, 0.15f);   // the board and whatever holds it (selection-local)
            var selReaders = Rect.MinMaxRect(-selSize.x * 0.5f, -2.0f, selSize.x * 0.5f, -0.3f);
            float selGap = foot.Min(q => RectGap(selFoot, SelLocal(spot.selection, q)));
            if (selGap < SelectionBoardGap) hard.Add("footprint " + selGap.ToString("F2") + " m from the selection board (< " + SelectionBoardGap + ")");
            if (foot.Any(q => RectGap(selReaders, SelLocal(spot.selection, q)) <= 0f)) soft.Add("stands where the selection board's readers stand");
            if (read.Any(q => RectGap(selReaders, SelLocal(spot.selection, q)) <= 0f)) soft.Add("readers' area shared with the selection board");
            var work = StationWorkArea;
            if (foot.Concat(read).Any(q => { var l = spot.station.InverseTransformPoint(q); return work.Contains(new Vector2(l.x, l.z)); }))
                soft.Add("footprint / readers within 1 m of the wash station's working area");
            if (spot.floor != null)
            {
                float outside = foot.Min(q => OutsideSquare(q, spot.floor.position, spot.station.rotation, spot.floorHalf));
                if (outside < StationFloorClear) soft.Add("on / near the wash station's personal floor (" + outside.ToString("F2") + " m outside, < " + StationFloorClear + ")");
            }
            if (spot.callAnchor != null && spot.keepOut > 0f)
            {
                float d = foot.Min(q => Flat(q - spot.callAnchor.position));
                if (d < spot.keepOut) soft.Add("inside the wash station's dog keep-out circle (" + d.ToString("F2") + " m from the stand, < " + spot.keepOut.ToString("F1") + ")");
            }
            hard = hard.Distinct().ToList(); soft = soft.Distinct().ToList();
        }

        /// <summary>A world point in the selection board's frame in metres (its transform is the canvas, scale 0.001: InverseTransformPoint would give mm).</summary>
        static Vector3 SelLocal(Transform sel, Vector3 world) { return Quaternion.Inverse(sel.rotation) * (world - sel.position); }

        static void PlaceItemBoard(Transform stand, Board b, Ctx c, WashSpot spot, List<string> log)
        {
            var st = spot.station;
            float selSide = st.InverseTransformPoint(spot.selection.position).x >= 0f ? 1f : -1f;   // the selection board's side of the station
            float stYaw = st.eulerAngles.y;
            int tried = 0; Vector3? firstClear = null; float firstClearYaw = 0f; string firstClearWhy = null;
            foreach (float side in new[] { selSide, -selSide })
                for (float x = ItemSideFrom; x <= ItemSideTo + 1e-4f; x += ItemSideStep)
                    foreach (float dz in ItemDepthOffsets)
                        foreach (float dy in ItemYawOffsets)
                        {
                            tried++;
                            var p = st.TransformPoint(new Vector3(side * x, 0f, spot.selection.localPosition.z + dz));
                            float yaw = stYaw + dy;
                            stand.SetPositionAndRotation(new Vector3(p.x, 0f, p.z), Quaternion.Euler(0f, yaw, 0f));
                            float gy = float.NegativeInfinity;
                            try { foreach (var sp in SeatSamples(b)) gy = Mathf.Max(gy, Ground(stand.TransformPoint(sp))); }
                            catch (Exception e) { if (tried <= 3) log.Add("  try " + tried + ": " + e.Message); continue; }
                            stand.position = new Vector3(p.x, gy, p.z);
                            ItemRules(b, stand, c, spot, out List<string> hard, out List<string> soft);
                            string at = (side == selSide ? "selection-board side" : "other side") + " x " + (side * x).ToString("+0.00;-0.00") + " depth " + dz.ToString("+0.00;-0.00") + " yaw " + yaw.ToString("F1") + " " + V(stand.position);
                            if (hard.Count > 0) continue;
                            if (soft.Count == 0) { log.Add("PLACED " + spot.boardName + " at the first point that passes every rule (" + tried + " tried): " + at); return; }
                            if (!firstClear.HasValue) { firstClear = stand.position; firstClearYaw = yaw; firstClearWhy = at + " breaks " + string.Join("; ", soft); }
                        }
            if (!firstClear.HasValue) throw new InvalidOperationException("no point beside the selection board of " + KD_SceneObjects.PathOf(st.gameObject) + " (" + tried + " tried) has a clear volume; nothing saved (reopen the scene without saving)");
            stand.SetPositionAndRotation(firstClear.Value, Quaternion.Euler(0f, firstClearYaw, 0f));
            log.Add("WARN " + spot.boardName + ": no point passes every rule (" + tried + " tried). PLACED at the first point with a clear volume (USER DECISION NEEDED: Play で見て、場所を変えるか規則の例外にするか): " + firstClearWhy);
        }

        // ================================================================ Guide/22

        [MenuItem(Menu + "22 - Build Item Guide Boards")]
        public static void BuildItemGuide()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var c = ResolveItem(out List<WashSpot> spots);
            ProbeOtherBindings(new[] { WallPostersName });
            var r = new WallReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-item-guide-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, false);
            MeshTxBegin();   // BUG-ENV-005: the trays written by this run are undone if it fails before the save
            try
            {
                // the first version (one board across the road from the map) is gone (2026-10-14 ユーザー決定)
                foreach (var old in c.scene.GetRootGameObjects().Where(g => g.name == OldItemGuideName).ToArray())
                {
                    r.log.Add("removed the old " + OldItemGuideName + " at " + V(old.transform.position) + " (the board across the road from the map; replaced by one board per wash station)");
                    UnityEngine.Object.DestroyImmediate(old);
                }
                var groupGo = c.scene.GetRootGameObjects().FirstOrDefault(g => g.name == ItemGuideRootName);
                if (groupGo == null) { groupGo = new GameObject(ItemGuideRootName); SceneManagerMove(groupGo, c.scene); r.log.Add("created " + ItemGuideRootName + " at the scene root"); }
                var group = groupGo.transform;
                group.SetPositionAndRotation(Vector3.zero, Quaternion.identity); group.localScale = Vector3.one; groupGo.layer = 0; groupGo.SetActive(true);
                foreach (Transform t in group)
                    if (!spots.Any(s => s.boardName == t.name)) r.log.Add("WARN " + ItemGuideRootName + "/" + t.name + " is not a board this builder makes (no such wash station): left as is");
                foreach (var spot in spots)
                {
                    var b = ItemBoard(spot.boardName);
                    var stand = group.Find(spot.boardName);
                    if (stand == null)
                    {
                        stand = new GameObject(spot.boardName).transform;
                        stand.SetParent(group, false);
                        PlaceItemBoard(stand, b, c, spot, r.log);
                    }
                    else
                    {
                        var e = stand.eulerAngles; stand.rotation = Quaternion.Euler(0f, e.y, 0f);
                        r.log.Add("kept " + spot.boardName + " x/z/yaw (human-owned after the first build): " + V(stand.position) + " yaw " + e.y.ToString("F1") + " (delete it to search again)");
                    }
                    stand.localScale = Vector3.one; stand.gameObject.layer = 0; stand.gameObject.SetActive(true);
                    float y = float.NegativeInfinity;
                    foreach (var p in SeatSamples(b)) y = Mathf.Max(y, Ground(stand.TransformPoint(p)));
                    stand.position = new Vector3(stand.position.x, y, stand.position.z);
                    BuildItemBoard(stand, b, c, r.log);
                }
                var log = new StringBuilder();
                KD_ShopSetupEditor.AddGlyphs(ItemGlyphText(c), log);
                BindIfComplete(c, log);
                foreach (var line in log.ToString().Split('\n')) if (line.Trim().Length > 0 && !line.StartsWith("  AUTOSIZE")) r.log.Add(line.Trim());
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("item guide scene save failed");
                MeshTxCommit();
            }
            catch
            {
                MeshTxRollback(r.log);
                Debug.LogError("[KH Item Guide] Build failed; the scene before this run is at " + r.backup + ". Reopen the scene without saving to discard partial changes (the tray meshes this run wrote were rolled back).");
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
            var canvas = EnsureCanvas(stand, b, keep);   // BUG-ENV-005: the frame is built after the canvas (BuildStructure below)
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
            // BUG-ENV-005: the frame, its Back with a hole under the header plate and the paper (the cards are on the paper; the input
            // canvas draws nothing: its graphics are clear and culled)
            var notes = new List<string>();
            var holes = CanvasHoles(stand, TrayFront, new[] { canvas }, null, keep, notes);
            BuildStructure(stand, b, c, keep, holes, b.name, log);
            foreach (var n in notes) log.Add("NOTE " + stand.name + " " + n);

            foreach (Transform t in stand)
                if (t.name != StructureName && t.name != CanvasName && t.name != ItemInputName) log.Add("WARN " + stand.name + "/" + t.name + " is not made by this builder: left as is");
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
            log.Add(stand.name + " at " + V(stand.position) + " yaw " + stand.eulerAngles.y.ToString("F1") + ": " + ItemCards.Count(x => x.evt != null) + " cards open the hand menu, " + ItemCards.Count(x => x.evt == null) + " card is read-only");
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

        [MenuItem(Menu + "23 - Verify Item Guide Boards")]
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
            v.limitations = "Static checks of the saved scene (IMP-UI-008 C, one board per wash station): the stands, the read-only canvases and the input canvases, every card's "
                + "button against KD_TreatPouch's _OpenFromBoard* entries, the bindings, the glyphs and the fit of the 13 languages (ja / en measured with the bundled font; the "
                + "11 client-glyph languages are drawn by VRChat's fallback fonts, so their fit is a WARN estimate), and the placement rules. Not judged here: "
                + "whether pressing a card opens the menu on that page in ClientSim / the client, readability at the station, how people walk around it (Play).";
            Action<bool, string> req = (ok, what) => { v.lines.Add((ok ? "OK   " : "FAIL ") + what); if (!ok) v.failed++; };
            Action<bool, string> warn = (ok, what) => { v.lines.Add((ok ? "OK   " : "WARN ") + what); if (!ok) v.warned++; };
            Ctx c; List<WashSpot> spots;
            try { c = ResolveItem(out spots); }
            catch (Exception e) { req(false, "resolve: " + e.Message); v.status = "ITEM_GUIDE_FAIL"; return v; }
            req(KD_SceneObjects.Find(OldItemGuideName) == null, "the old single board " + OldItemGuideName + " (across the road from the map) is gone (2026-10-14 ユーザー決定)");
            var groupGo = c.scene.GetRootGameObjects().FirstOrDefault(g => g.name == ItemGuideRootName);
            req(groupGo != null && groupGo.transform.position == Vector3.zero && groupGo.transform.rotation == Quaternion.identity && groupGo.transform.localScale == Vector3.one,
                ItemGuideRootName + " once at the scene root, at the origin");
            if (groupGo == null) { v.status = "ITEM_GUIDE_FAIL"; return v; }
            var boards = groupGo.transform.Cast<Transform>().ToList();
            req(boards.Count == spots.Count && spots.All(s => boards.Any(t => t.name == s.boardName)),
                boards.Count + " boards = " + spots.Count + " wash stations with a selection board (" + string.Join(", ", spots.Select(s => s.key)) + ")");
            req(spots.Count == 4, "4 wash stations with a selection board (main + 3 on the course; actual " + spots.Count + ")");
            var manifestRoots = ManifestItemRoots();
            req(manifestRoots.Count == spots.Count && spots.All(s => manifestRoots.Contains(s.boardName)), "the bindings name one label set per board (" + manifestRoots.Count + " roots x " + ItemLabels().Count + " labels)");
            // Guide/22 before Guide/20: the bind waits for the wall posters (BindIfComplete), so the binding lines are WARN until then
            bool bindPending = KD_SceneObjects.Find(WallPostersName) == null;
            Action<bool, string> bound = bindPending ? (ok, what) => warn(ok, what + " (bind deferred: run Guide/20, which binds everything)") : req;
            var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(c.pouch);
            foreach (var spot in spots)
            {
                var stand = groupGo.transform.Find(spot.boardName);
                if (stand == null) continue;
                CheckOneItemBoard(c, spot, stand, pouchUdon, req, warn, bound, v);
            }
            warn(c.pouch.photoPage != null, "the photo page exists (Photo/1): the photo cards open it");

            // ---- fit (the same labels on every board: measured once on the first board) ----
            var first = spots.Select(s => groupGo.transform.Find(s.boardName)).FirstOrDefault(t => t != null);
            if (first != null)
            {
                var measure = new GameObject("ItemGuideMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var sample = measure.AddComponent<TextMeshProUGUI>();
                    foreach (var l in ItemLabels())
                    {
                        var tr = first.Find(l.path);
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
            }
            var missing = KD_ShopSetupEditor.MissingGlyphs(ItemGlyphText(c));
            req(missing.Length == 0, "every ja / en character of the boards is in the shared font" + (missing.Length > 0 ? " (missing " + missing + "; Guide/22 adds them)" : ""));
            foreach (var lg in c.catalog.languages)
            {
                int empty = ItemLabels().Select(l => l.key).Distinct().Count(k => { var e = lg.texts.FirstOrDefault(x => x.key == k); return e == null || string.IsNullOrWhiteSpace(e.text); });
                warn(empty == 0, lg.code + ": every item guide text present (" + empty + " missing; AI translation, not proofread)");
            }
            try { KD_LocalizationEditor.ValidateScene(c.language, c.catalog); bound(true, "localization tables, bindings and glyphs match the catalogs (KD_LocalizationEditor.ValidateScene)"); }
            catch (Exception e) { bound(false, "localization: " + e.Message + " (Guide/20 / Guide/22 re-bind; the later of the two binds everything)"); }
            v.budget = "per board: frame 6 renderers (5 cubes + the tray Back, " + BoardMeshDir + "; " + WoodMatPath + ", Batching Static, shared with the guide boards), a drawing canvas (header plate, paper, 8 cards (7 rounded "
                + KD_UiSpriteEditor.RoundSpritePath + "), " + ItemLabels().Count + " labels) and an input canvas (7 clear hit areas, culled); " + spots.Count + " boards = " + spots.Count
                + " x 2 canvases; Udon 0 (the buttons call KD_TreatPouch), trigger colliders " + spots.Count + ", network objects 0, no new texture";
            v.status = v.failed == 0 ? "ITEM_GUIDE_OK" : "ITEM_GUIDE_FAIL";
            return v;
        }

        static void CheckOneItemBoard(Ctx c, WashSpot spot, Transform stand, VRC.Udon.UdonBehaviour pouchUdon, Action<bool, string> req, Action<bool, string> warn, Action<bool, string> bound, VerifyReport v)
        {
            var b = ItemBoard(spot.boardName); string n = spot.boardName;
            var e3 = stand.eulerAngles;
            req(Mathf.Abs(Mathf.DeltaAngle(e3.x, 0f)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(e3.z, 0f)) < 0.01f && stand.localScale == Vector3.one, n + " yaw only, scale 1");
            req(stand.gameObject.activeInHierarchy && stand.gameObject.layer == 0, n + " active, Default layer");
            try { float seat = SeatSamples(b).Max(p => Ground(stand.TransformPoint(p))); req(Mathf.Abs(stand.position.y - seat) <= 0.02f, n + " seated on the terrain (" + (stand.position.y - seat).ToString("+0.000;-0.000") + " m; moved by hand? run Guide/22)"); }
            catch (Exception ex) { req(false, n + " terrain under the posts: " + ex.Message); }
            var s = stand.Find(StructureName);
            req(s != null, n + "/" + StructureName);
            if (s != null)
                foreach (var name in WoodParts)
                {
                    var p = s.Find(name); var mr = p != null ? p.GetComponent<MeshRenderer>() : null;
                    req(mr != null && mr.sharedMaterial == c.wood && p.GetComponent<Collider>() == null, n + "/" + name + " wood, no collider (as the guide boards)");
                }
            var cv = stand.Find(CanvasName) as RectTransform;
            req(cv != null && cv.GetComponent<Canvas>() != null && cv.sizeDelta == b.canvasMm && Mathf.Abs(cv.localPosition.y - CanvasCenterY(b)) < 0.001f && Mathf.Abs(cv.localScale.x - Mm) < 1e-6f
                && Mathf.Abs(cv.localPosition.z - TrayFront) < 1e-4f && cv.localRotation == Quaternion.identity
                && cv.GetComponent<GraphicRaycaster>() == null && cv.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null, n + " drawing canvas " + b.canvasMm + " mm, read-only, in the Back's front plane z " + TrayFront);
            // BUG-ENV-005 (Codex review 2026-10-15 中1): Back = the tray the canvas needs now (pose, asset, mesh regenerated from the paper's rects)
            {
                Vector3 tc, ts; BoardTrayBox(b, out tc, out ts);
                string why = "no canvas";
                bool trayOk = cv != null && TrayMatches(stand, new[] { cv }, null, TrayFront, tc, ts, true, BoardTrayAsset(b.name), out why);
                req(trayOk, n + "/Back is the tray with the paper's holes (BUG-ENV-005; run Guide/22): " + why);
            }
            var inRt = stand.Find(ItemInputName) as RectTransform;
            var inBox = inRt != null ? inRt.GetComponent<BoxCollider>() : null;
            req(inRt != null && inRt.GetComponent<Canvas>() != null && inRt.GetComponent<GraphicRaycaster>() != null && inRt.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null
                && inBox != null && inBox.isTrigger && inRt.gameObject.layer == 0 && inRt.sizeDelta == b.canvasMm && inRt.localPosition.z < 0f && Mathf.Abs(inRt.localScale.x - Mm) < 1e-6f,
                n + " input canvas: GraphicRaycaster + VRCUiShape + trigger BoxCollider, Default layer, in front of the drawing canvas (vrc-world-ui §2)");
            req(stand.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length == 0, n + ": no UdonBehaviour (the cards call KD_TreatPouch)");
            req(stand.GetComponentsInChildren<Collider>(true).All(x => x.isTrigger), n + ": no solid Collider");

            // ---- cards ----
            var paper = cv != null ? cv.Find(ItemPaperName) as RectTransform : null;
            req(paper != null, n + "/" + CanvasName + "/" + ItemPaperName);
            var rects = new List<Rect>();
            var inside = Rect.MinMaxRect(-ItemPaperSize.x * 0.5f, -ItemPaperSize.y * 0.5f, ItemPaperSize.x * 0.5f, ItemSubtitlePos.y - ItemSubtitleSize.y * 0.5f);
            for (int i = 0; paper != null && i < ItemCards.Length; i++)
            {
                var card = ItemCards[i];
                var face = paper.Find(card.name) as RectTransform;
                var img = face != null ? face.GetComponent<Image>() : null;
                req(img != null && face.anchoredPosition == ItemCardPos(i) && face.sizeDelta == ItemCardSize, n + " " + card.name + " at " + ItemCardPos(i));
                if (img == null) continue;
                var rc = new Rect(ItemCardPos(i) - ItemCardSize * 0.5f, ItemCardSize);
                req(!rects.Any(o => o.Overlaps(rc)) && inside.Contains(rc.min) && inside.Contains(rc.max), n + " " + card.name + " inside the paper, below the subtitle, not overlapping another card");
                rects.Add(rc);
                bool press = card.evt != null;
                req(press ? (img.sprite == c.round && img.type == Image.Type.Sliced) : img.sprite == null, n + " " + card.name + (press ? " rounded (pressable)" : " flat (read-only: no menu entry)"));
                var hit = inRt != null ? inRt.Find(card.name) : null;
                if (!press) { req(hit == null, n + " " + card.name + " has no hit area"); continue; }
                var button = hit != null ? hit.GetComponent<Button>() : null;
                req(button != null && button.targetGraphic == img && button.interactable && hit.GetComponent<Image>() != null && hit.GetComponent<Image>().raycastTarget
                    && ((RectTransform)hit).anchoredPosition == ItemPaperPos + ItemCardPos(i) && ((RectTransform)hit).sizeDelta == ItemCardSize, n + " " + card.name + " hit area over the card, tints the card");
                req(button != null && OnClickIs(button, pouchUdon, card.evt), n + " " + card.name + " -> KD_TreatPouch." + card.evt + " (SendCustomEvent on the backing UdonBehaviour)");
                req(typeof(KD_TreatPouch).GetMethod(card.evt, BindingFlags.Public | BindingFlags.Instance) != null && card.evt.StartsWith("_"), "KD_TreatPouch." + card.evt + " exists, public, local (`_`)");
            }

            // ---- labels: bound, ja text, font ----
            var lang = c.language;
            foreach (var l in ItemLabels())
            {
                var tr = stand.Find(l.path);
                var tmp = tr != null ? tr.GetComponent<TextMeshProUGUI>() : null;
                req(tmp != null, n + " label " + l.path);
                if (tmp == null) continue;
                req(tmp.font == c.font && !tmp.raycastTarget && tmp.overflowMode == TextOverflowModes.Overflow, n + " label " + l.path + ": menu font, not a raycast target, Overflow");
                bound(tmp.text == KD_LocalizationEditor.Text(c.catalog, c.catalog.fallbackLanguage, l.key), n + " label " + l.path + " holds the ja text of " + l.key + " (Bind)");
                int i = lang.fixedLabels != null ? Array.IndexOf(lang.fixedLabels, tmp) : -1;
                bound(i >= 0 && lang.fixedKeys[i] == l.key, n + " label " + l.path + " bound to " + l.key);
            }

            // ---- placement: the guide boards' rules and the wash station's (the pose is the user's: rules broken are WARN) ----
            ItemRules(b, stand, c, spot, out List<string> hard, out List<string> soft);
            req(hard.Count == 0, n + " volume / readers' area clear, footprint apart from the other boards and the selection board, ground range" + (hard.Count > 0 ? ": " + string.Join("; ", hard) : ""));
            foreach (var x in soft) warn(false, n + " placement rule (USER DECISION: 洗い場の選択ボードの隣との兼ね合い): " + x);
            if (soft.Count == 0) req(true, n + " passes the guide boards' and the wash station's placement rules");
            float ds = Flat(stand.position - spot.selection.position);
            warn(ds <= ItemSelectionMax, n + " beside the selection board: " + ds.ToString("F2") + " m from it (<= " + ItemSelectionMax + ")");
            var l2 = spot.station.InverseTransformPoint(stand.position);
            float selX = spot.station.InverseTransformPoint(spot.selection.position).x;
            v.notes.Add(n + " at " + V(stand.position) + " yaw " + stand.eulerAngles.y.ToString("F1") + " (station yaw " + spot.station.eulerAngles.y.ToString("F1") + "); station-local x " + l2.x.ToString("+0.00;-0.00")
                + " z " + l2.z.ToString("+0.00;-0.00") + " (" + (Mathf.Sign(l2.x) == Mathf.Sign(selX) ? "the selection board's side" : "the other side") + "), " + ds.ToString("F2") + " m from the selection board");
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
