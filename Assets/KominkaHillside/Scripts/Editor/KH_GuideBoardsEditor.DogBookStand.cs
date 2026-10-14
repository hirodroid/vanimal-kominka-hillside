using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>DES-DOG-009 (2026-10-08 ユーザー決定「看板『遊び方』の隣、広場の方向に、遊び方と同じ作りの立て看板を建て、そこに犬紹介パネルを作る」):
    /// 犬紹介パネル (KD_DogBook) を載せる屋根つきの立て看板。設計 docs/plans/2026-10-08-dog-book-and-fur-color.md §3。
    ///
    /// 作りは案内板 (Asobikata / Map / Kominka) と同じ <see cref="BuildStructure"/> / <see cref="EnsureCanvas"/> / 見出しの板 (Img + Txt) を使う。
    /// ただし **KH_GuideBoards の下には置かない**: パネルは Udon・押す面 (VRCUiShape・GraphicRaycaster・trigger の BoxCollider) を持ち、
    /// Guide/2 の「案内板は読むだけ (Udon 1 個・Collider は Interact だけ)」の検査に掛かるため。置き場は KH_Dogs/KH_DogBookStand (板 KD_DogBook・
    /// 訪問タグ・毛色と同じ親)。Guide/1 は KH_GuideBoards の 3 枚だけを作り直すので、この看板には触らない (逆も同じ: ここは 3 枚を動かさない)。
    ///
    /// 置き場所の探し方 (初回だけ。以後 x / z / yaw は人のもの、y だけ地面へ据え直す。案内板と同じ流儀): Asobikata の横方向のうち広場 (KH_Plaza) のある側へ、
    /// Asobikata の canvas の幅 + 0.4 m (= 3.64 m) から 0.5 m 刻みで 2 m まで。各点で、背面側 (Asobikata の +Z、読む人と反対) へ 0〜1.0 m (0.25 m 刻み)、
    /// yaw を Asobikata と同じ → ±10° → ±20° の順に試す (統括の指示 2026-10-08)。各点を案内板の置き場所の規則 (Guide/2 の Placement) と
    /// 「看板の体積・読む人の場所に固い Collider が無い・地面の段差」で判定し、**全部を満たす最初の点**を採る。無ければ、体積が空いている最初の点
    /// (= 指定どおりの 3.64 m・背面 0・同じ yaw から順に) に置き、破った規則を WARN で出す (DogBook/3 Verify は FAIL にする。ユーザーが Play で見て決める)。
    /// 体積の空いた点が 1 つも無ければ例外 (何も変えない)。
    /// 2026-10-14 ユーザー決定 (DES-DOG-009): 置いた場所 (50.91, 2.39, 47.48) yaw 55 をそのまま採用し、戻り道 0.54 m と待機の輪 2.10 m の 2 規則は例外。
    /// Verify はその場所にある間だけこの 2 規則を例外 (PASS) と数える (StandDecidedPos)。</summary>
    public static partial class KH_GuideBoardsEditor
    {
        public const string DogBookStandName = "KH_DogBookStand";
        const string PlazaName = "KH_Plaza";
        static readonly Vector3 PlazaFallback = new Vector3(72.0f, 19.0f, 127.0f);   // 2026-10-08 統括が渡した KH_Plaza の位置 (シーンに無いときだけ使う)

        // ---- 看板の canvas (mm、中心が原点)。幅 = パネル + 左右 40、高さ = 上 25 + 見出し 130 + 間 30 + パネル + 下 25 ----
        const float StandSide = 40f, StandTop = 25f, StandHeaderH = 130f, StandGap = 30f, StandBottom = 25f;
        /// <summary>canvas の下端の高さ (m)。0.85 でパネルの中心が地面 + 約 1.23 m (前の置き方の 1.25 m とほぼ同じ。案内板は 0.80)。</summary>
        const float StandCanvasBottom = 0.85f;
        /// <summary>パネル (KD_DogBook の Index ページ) を看板の canvas の面より手前 (-Z) へ出す量 (m)。詳細・説明のページはさらに 0.04 m 手前。</summary>
        public const float StandPanelFront = 0.01f;

        // ---- 置き場所の探索 ----
        const float StandGapToAsob = 0.4f, StandLateralStep = 0.5f, StandLateralMax = 2.0f, StandBackStep = 0.25f, StandBackMax = 1.0f;
        static readonly float[] StandYawOffsets = { 0f, 10f, -10f, 20f, -20f };
        /// <summary>DogBook/3 Verify の範囲: Asobikata の中心からの水平距離と、yaw の差。</summary>
        public const float StandAsobMin = 3.4f, StandAsobMax = 6.0f, StandYawMaxDiff = 20.5f;
        /// <summary>DES-DOG-009 決定 2026-10-14: この場所 (x / z と yaw) にある間、戻り道と待機の輪の 2 規則は Verify の例外 (本文の VerifyDogBookStand)。</summary>
        public static readonly Vector3 StandDecidedPos = new Vector3(50.91f, 2.39f, 47.48f);
        public const float StandDecidedYaw = 55f, StandDecidedTol = 0.05f, StandDecidedYawTol = 1f;
        /// <summary>ほかの案内板の足元 (柱 + 屋根の出) とのすき間 (案内板は Collider を持たないので、物理では見えない)。</summary>
        const float StandBoardGap = 0.3f;

        static Board DogBookBoard(Vector2 panelMm)
        {
            var canvas = new Vector2(panelMm.x + 2f * StandSide, StandTop + StandHeaderH + StandGap + panelMm.y + StandBottom);
            return new Board { name = DogBookStandName, createXZ = Asob.createXZ, createYaw = Asob.createYaw, canvasMm = canvas, canvasBottom = StandCanvasBottom };
        }
        static float StandHeaderY(Board b) { return b.canvasMm.y * 0.5f - StandTop - StandHeaderH * 0.5f; }
        static float StandPanelY(Board b, Vector2 panelMm) { return -b.canvasMm.y * 0.5f + StandBottom + panelMm.y * 0.5f; }

        /// <summary>看板の canvas の大きさ (mm)。</summary>
        public static Vector2 DogBookStandCanvasMm(Vector2 panelMm) { return DogBookBoard(panelMm).canvasMm; }

        /// <summary>パネル (KD_DogBook の根、scale 0.001) を置く看板ローカルの位置: canvas の見出しの下、面より <see cref="StandPanelFront"/> 手前。</summary>
        public static Vector3 DogBookPanelLocal(Vector2 panelMm)
        {
            var b = DogBookBoard(panelMm);
            return new Vector3(0f, CanvasCenterY(b) + StandPanelY(b, panelMm) * Mm, -StandPanelFront);
        }

        /// <summary>看板ローカル (m) でのパネルの矩形と見出しの板の矩形 (x, y)。Verify が重なり・はみ出しを見る。</summary>
        public static void DogBookStandRects(Vector2 panelMm, out Rect canvas, out Rect header, out Rect panel)
        {
            var b = DogBookBoard(panelMm);
            float cy = CanvasCenterY(b);
            canvas = new Rect(-b.canvasMm.x * Mm * 0.5f, cy - b.canvasMm.y * Mm * 0.5f, b.canvasMm.x * Mm, b.canvasMm.y * Mm);
            var hp = new Vector2(b.canvasMm.x - 40f, StandHeaderH) * Mm;
            header = new Rect(-hp.x * 0.5f, cy + StandHeaderY(b) * Mm - hp.y * 0.5f, hp.x, hp.y);
            panel = new Rect(-panelMm.x * Mm * 0.5f, cy + StandPanelY(b, panelMm) * Mm - panelMm.y * Mm * 0.5f, panelMm.x * Mm, panelMm.y * Mm);
        }

        static Ctx StandCtx(TMP_FontAsset font)
        {
            var c = new Ctx { scene = EditorSceneManager.GetActiveScene(), font = font };
            var roots = c.scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray();
            if (roots.Length != 1) throw new InvalidOperationException(RootName + " must exist once at the scene root (" + roots.Length + "): the stand stands beside its Asobikata; nothing changed");
            c.root = roots[0].transform;
            c.wood = AssetDatabase.LoadAssetAtPath<Material>(WoodMatPath);
            if (c.wood == null) throw new InvalidOperationException(WoodMatPath + " missing; nothing changed");
            var managers = UnityEngine.Object.FindObjectsOfType<KH_LeashManager>(true);
            c.manager = managers.Length == 1 ? managers[0] : null;   // the loop line for the route rule (Placement says FAIL when it is missing)
            return c;
        }

        static Transform AsobOf(Ctx c)
        {
            var t = c.root.Find(Asob.name);
            if (t == null) throw new InvalidOperationException(RootName + "/" + Asob.name + " missing (Guide/1 builds it); nothing changed");
            return t;
        }

        /// <summary>+1 / -1: 広場のある側 (Asobikata の +X / -X)。</summary>
        static float PlazaSide(Transform asob, out string how)
        {
            var plaza = KD_SceneObjects.Find(PlazaName);
            var p = plaza != null ? plaza.transform.position : PlazaFallback;
            float d = Vector3.Dot(Flat3(p - asob.position), Flat3(asob.right));
            how = (plaza != null ? PlazaName : PlazaName + " not found, fallback") + " " + V(p) + " is on Asobikata's " + (d >= 0f ? "+X" : "-X") + " side";
            return d >= 0f ? 1f : -1f;
        }

        // ================================================================ DogBook/2 (KD_DogBookEditor.Hillside が呼ぶ)

        /// <summary>看板を (無ければ探して) 建て、Structure と Canvas (見出しの板と見出し) を作り直す。パネルは呼ぶ側が載せる。シーンは保存しない。
        /// header = 見出しの TMP (文言は KD_DogBook.staticLabels に入れて dogbook.title.hillside で引く)。</summary>
        public static Transform BuildDogBookStand(Transform parent, Vector2 panelMm, TMP_FontAsset font, StringBuilder log, out TextMeshProUGUI header)
        {
            if (parent == null || parent.lossyScale != Vector3.one || parent.rotation != Quaternion.identity) throw new InvalidOperationException("the stand's parent must be unrotated with scale 1; nothing changed");
            var c = StandCtx(font);
            var asob = AsobOf(c);
            var b = DogBookBoard(panelMm);
            log.AppendLine("stand canvas " + b.canvasMm + " mm (panel " + panelMm + " mm), bottom " + b.canvasBottom + " m");
            var stand = parent.Find(DogBookStandName);
            if (stand == null)
            {
                var go = new GameObject(DogBookStandName);
                Undo.RegisterCreatedObjectUndo(go, "Create " + DogBookStandName);
                go.transform.SetParent(parent, false);
                stand = go.transform;
                try { PlaceStand(stand, b, asob, c, log); }
                catch { Undo.DestroyObjectImmediate(go); throw; }
            }
            else
            {
                var e = stand.eulerAngles;
                stand.rotation = Quaternion.Euler(0f, e.y, 0f);
                log.AppendLine("kept " + DogBookStandName + " x/z/yaw (human-owned after the first build): " + V(stand.position) + " yaw " + e.y.ToString("F1") + " — delete the stand to search again");
            }
            stand.localScale = Vector3.one;
            stand.gameObject.layer = 0;
            stand.gameObject.SetActive(true);
            float y = float.NegativeInfinity;
            foreach (var p in SeatSamples(b)) y = Mathf.Max(y, Ground(stand.TransformPoint(p)));
            stand.position = new Vector3(stand.position.x, y, stand.position.z);

            var keep = new HashSet<Transform>();
            BuildStructure(stand, b, c, keep);
            var canvas = EnsureCanvas(stand, b, keep);
            float hy = StandHeaderY(b);
            Img(canvas, "HeaderPlate", new Vector2(0f, hy), new Vector2(b.canvasMm.x - 40f, StandHeaderH), HeaderPlateColor, null, keep);
            header = Txt(canvas, "Header", new Vector2(0f, hy), new Vector2(b.canvasMm.x - 80f, StandHeaderH - 10f), MapTitleMax, MapTitleMin, TextAlignmentOptions.Center, SignInk, false, c, keep);
            foreach (var name in new[] { StructureName, CanvasName })
            {
                var top = stand.Find(name);
                if (top == null) continue;
                foreach (var t in top.GetComponentsInChildren<Transform>(true).Where(t => !keep.Contains(t)).ToList())
                    if (t != null && (t.parent == null || keep.Contains(t.parent))) { log.AppendLine("removed stale " + KD_SceneObjects.PathOf(t.gameObject)); UnityEngine.Object.DestroyImmediate(t.gameObject); }
            }
            log.AppendLine("stand at " + V(stand.position) + " yaw " + stand.eulerAngles.y.ToString("F1") + ", " + Flat(stand.position - asob.position).ToString("F2") + " m from Asobikata");
            return stand;
        }

        /// <summary>初回の置き場所 (クラスの説明のとおり)。候補ごとの結果をログへ 1 行ずつ。</summary>
        static void PlaceStand(Transform stand, Board b, Transform asob, Ctx c, StringBuilder log)
        {
            string how;
            float side = PlazaSide(asob, out how);
            log.AppendLine("plaza side: " + how);
            float baseLateral = Asob.canvasMm.x * Mm + StandGapToAsob;
            Vector3 right = Flat3(asob.right).normalized, back = Flat3(asob.forward).normalized;
            float asobYaw = asob.eulerAngles.y;
            int tried = 0;
            Vector3? firstClear = null; float firstClearYaw = 0f; string firstClearWhy = null;
            for (float lat = baseLateral; lat <= baseLateral + StandLateralMax + 1e-4f; lat += StandLateralStep)
                for (float bk = 0f; bk <= StandBackMax + 1e-4f; bk += StandBackStep)
                    foreach (float dy in StandYawOffsets)
                    {
                        tried++;
                        var pos = asob.position + right * (side * lat) + back * bk;
                        float yaw = asobYaw + dy;
                        stand.SetPositionAndRotation(new Vector3(pos.x, 0f, pos.z), Quaternion.Euler(0f, yaw, 0f));
                        float gy = float.NegativeInfinity;
                        try { foreach (var sp in SeatSamples(b)) gy = Mathf.Max(gy, Ground(stand.TransformPoint(sp))); }
                        catch (Exception e) { log.AppendLine("  try " + tried + " lateral " + lat.ToString("F2") + " back " + bk.ToString("F2") + " yaw " + yaw.ToString("F1") + ": " + e.Message); continue; }
                        stand.position = new Vector3(pos.x, gy, pos.z);
                        List<string> hard, soft;
                        StandRules(b, stand, c, out hard, out soft);
                        string at = "lateral " + lat.ToString("F2") + " back " + bk.ToString("F2") + " yaw " + yaw.ToString("F1") + " " + V(stand.position);
                        log.AppendLine("  try " + tried + " " + at + ": " + (hard.Count > 0 ? "BLOCKED " + string.Join("; ", hard.Take(3)) : soft.Count > 0 ? "clear, breaks " + soft.Count + ": " + string.Join("; ", soft.Take(4)) : "ALL RULES PASS"));
                        if (hard.Count > 0) continue;
                        if (soft.Count == 0)
                        {
                            log.AppendLine("PLACED " + DogBookStandName + " at the first point that passes every rule: " + at);
                            return;
                        }
                        if (!firstClear.HasValue) { firstClear = stand.position; firstClearYaw = yaw; firstClearWhy = at + " breaks " + string.Join("; ", soft); }
                    }
            if (!firstClear.HasValue) throw new InvalidOperationException("no point beside Asobikata (" + tried + " tried) has a clear volume (see the log lines above in the exception's log); nothing changed\n" + log);
            stand.SetPositionAndRotation(firstClear.Value, Quaternion.Euler(0f, firstClearYaw, 0f));
            log.AppendLine("WARN no point passes every rule (" + tried + " tried). PLACED at the first point with a clear volume (USER DECISION NEEDED: Play で見て、場所を変えるか規則の例外にするか): " + firstClearWhy);
        }

        /// <summary>hard = 体積・読む人の場所の固い Collider、地面の段差、ほかの案内板の足元との重なり。soft = 案内板の置き場所の規則 (Guide/2 の Placement) と
        /// 看板どうし・写真ボードの読む場所。</summary>
        static void StandRules(Board b, Transform t, Ctx c, out List<string> hard, out List<string> soft)
        {
            var h = new List<string>(); var s = new List<string>();
            var report = new VerifyReport();
            Action<bool, string> req = (ok, what) =>
            {
                if (ok) return;
                if (what.Contains("solid collider") || what.Contains("terrain range")) h.Add(what); else s.Add(what);
            };
            Action<bool, string> warn = (ok, what) => { };
            try { Placement(b, t, FindAnchors(c), c, req, warn, report); }
            catch (Exception e) { h.Add("placement could not be measured: " + e.Message); }
            var foot = Samples(t, Footprint(b), 0.1f);
            var read = Samples(t, ReaderArea(b), 0.2f);
            foreach (var other in Boards)
            {
                var ot = c.root.Find(other.name);
                if (ot == null) continue;
                var of = Footprint(other); var orr = ReaderArea(other);
                float gap = foot.Min(q => RectGap(of, ot.InverseTransformPoint(q)));
                if (gap < StandBoardGap) h.Add("footprint " + gap.ToString("F2") + " m from board " + other.name + "'s footprint (< " + StandBoardGap + ")");
                if (foot.Any(q => RectGap(orr, ot.InverseTransformPoint(q)) <= 0f)) s.Add("stands in board " + other.name + "'s readers' area");
                if (Samples(ot, of, 0.2f).Any(q => RectGap(ReaderArea(b), t.InverseTransformPoint(q)) <= 0f)) s.Add("board " + other.name + " stands in this stand's readers' area");
            }
            var photo = KD_SceneObjects.Find("KH_PhotoBoard");
            if (photo != null)
            {
                float half = KH_PhotoGalleryEditor.BoardCanvasMm.x * Mm * 0.5f + 0.04f;
                var pr = Rect.MinMaxRect(-half, -2.0f, half, -0.3f);
                if (foot.Any(q => RectGap(pr, photo.transform.InverseTransformPoint(q)) <= 0f)) s.Add("stands in KH_PhotoBoard's readers' area");
                if (read.Any(q => Flat(q - photo.transform.position) < 0.5f)) s.Add("readers' area on KH_PhotoBoard");
            }
            hard = h; soft = s;
        }

        /// <summary>局所座標 (x, z) の点から矩形までの水平距離 (中なら 0 以下)。</summary>
        static float RectGap(Rect r, Vector3 local)
        {
            float dx = Mathf.Max(r.xMin - local.x, local.x - r.xMax), dz = Mathf.Max(r.yMin - local.z, local.z - r.yMax);
            if (dx <= 0f && dz <= 0f) return Mathf.Max(dx, dz);
            return Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dz, 0f) * Mathf.Max(dz, 0f));
        }

        // ================================================================ DogBook/3 (KD_DogBookEditor.Hillside の VerifyPlacement が呼ぶ。直さない)

        public static void VerifyDogBookStand(Transform stand, Vector2 panelMm, Transform panel, TMP_FontAsset font, Func<bool, string, bool> check, Action<string> note)
        {
            Ctx c;
            try { c = StandCtx(font); }
            catch (Exception e) { check(false, "stand context: " + e.Message); return; }
            var b = DogBookBoard(panelMm);
            var e3 = stand.eulerAngles;
            check(Mathf.Abs(Mathf.DeltaAngle(e3.x, 0f)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(e3.z, 0f)) < 0.01f && stand.lossyScale == Vector3.one, DogBookStandName + " yaw only, scale 1");
            check(stand.gameObject.activeInHierarchy, DogBookStandName + " active");
            try
            {
                float seat = SeatSamples(b).Max(p => Ground(stand.TransformPoint(p)));
                check(Mathf.Abs(stand.position.y - seat) <= 0.02f, DogBookStandName + " seated on the terrain (" + (stand.position.y - seat).ToString("+0.000;-0.000") + " m; moved by hand? run DogBook/2)");
            }
            catch (Exception ex) { check(false, DogBookStandName + " terrain under the posts: " + ex.Message); }
            var s = stand.Find(StructureName);
            check(s != null, DogBookStandName + "/" + StructureName);
            if (s != null)
                foreach (var name in WoodParts)
                {
                    var p = s.Find(name);
                    var mr = p != null ? p.GetComponent<MeshRenderer>() : null;
                    check(mr != null && mr.sharedMaterial == c.wood && p.GetComponent<Collider>() == null, DogBookStandName + "/" + name + " wood (" + WoodMatPath + "), no collider (same as the guide boards)");
                }
            var cv = stand.Find(CanvasName) as RectTransform;
            check(cv != null && cv.GetComponent<Canvas>() != null && cv.sizeDelta == b.canvasMm && Mathf.Abs(cv.localPosition.y - CanvasCenterY(b)) < 0.001f && Mathf.Abs(cv.localScale.x - Mm) < 1e-6f,
                DogBookStandName + " canvas " + b.canvasMm + " mm at " + CanvasCenterY(b).ToString("F2") + " m, scale 0.001");
            check(cv != null && cv.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null && cv.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null, DogBookStandName + " canvas is read-only (the panel has its own input canvases)");
            var header = cv != null ? cv.Find("Header") : null;
            check(header != null && cv.Find("HeaderPlate") != null, DogBookStandName + " header plate + header");

            // ---- 遊び方の隣・広場の側 ----
            Transform asob;
            try { asob = AsobOf(c); } catch (Exception ex) { check(false, ex.Message); return; }
            float da = Flat(stand.position - asob.position);
            check(da >= StandAsobMin && da <= StandAsobMax, "stand beside Asobikata: " + da.ToString("F2") + " m (" + StandAsobMin + "〜" + StandAsobMax + ")");
            float dyaw = Mathf.DeltaAngle(asob.eulerAngles.y, stand.eulerAngles.y);
            check(Mathf.Abs(dyaw) <= StandYawMaxDiff, "stand yaw " + stand.eulerAngles.y.ToString("F1") + " vs Asobikata " + asob.eulerAngles.y.ToString("F1") + " (diff " + dyaw.ToString("+0.0;-0.0") + ", <= 20)");
            string how;
            float side = PlazaSide(asob, out how);
            float lateral = Vector3.Dot(Flat3(stand.position - asob.position), Flat3(asob.right).normalized) * side;
            check(lateral > 0f, "stand on the plaza side of Asobikata (" + how + "; " + lateral.ToString("F2") + " m along it)");

            // ---- 体積・規則 ----
            List<string> hard, soft;
            StandRules(b, stand, c, out hard, out soft);
            check(hard.Count == 0, "stand volume / readers' area clear, footprint apart from the guide boards" + (hard.Count > 0 ? " (" + string.Join("; ", hard) + ")" : ""));
            // DES-DOG-009 決定 2026-10-14 ユーザー「看板の場所も推奨で OK。例外で」: 遊び方の隣・広場側 (50.91, 2.39, 47.48) yaw 55 のまま。周回の戻り道 (0.54 m、基準 1.8) と
            // 犬の待機の輪 (2.10 m、基準 3.3) の 2 規則だけ例外。看板がその場所 (±StandDecidedTol m、±StandDecidedYawTol°) にある間だけ PASS (例外) と数え、
            // 動かしたら (DogBook/2 で探し直したら) 元どおり FAIL にする。ほかの規則は例外にしない
            bool atDecided = Flat(stand.position - StandDecidedPos) <= StandDecidedTol && Mathf.Abs(Mathf.DeltaAngle(stand.eulerAngles.y, StandDecidedYaw)) <= StandDecidedYawTol;
            int excepted = 0;
            foreach (var x in soft)
            {
                bool exception = atDecided && (x.Contains("the loop's centre line") || x.Contains("dog roam centres"));
                if (exception) { excepted++; check(true, "placement rule exception (DES-DOG-009 決定 2026-10-14、遊び方の隣・広場側のまま): " + x); }
                else check(false, "placement rule (USER DECISION: 2026-10-08 指定の場所との兼ね合い。設計書 §3): " + x);
            }
            if (soft.Count == 0) check(true, "stand passes the guide boards' placement rules (route, roam, spawn, fixtures, gate, wash, other boards)");
            else if (soft.Count == excepted) check(true, "stand passes the remaining placement rules (the 2 decided exceptions aside)");

            // ---- パネルが看板の canvas にある ----
            check(panel.parent == stand, "panel KD_DogBook is a child of " + DogBookStandName);
            var want = DogBookPanelLocal(panelMm);
            check(Vector3.Distance(panel.localPosition, want) < 0.001f && Quaternion.Angle(panel.localRotation, Quaternion.identity) < 0.01f && Mathf.Abs(panel.localScale.x - Mm) < 1e-6f,
                "panel on the stand's canvas: local " + panel.localPosition.ToString("F3") + " = " + want.ToString("F3") + " (" + (StandPanelFront * 1000f).ToString("F0") + " mm in front of the canvas), facing like the stand, 1 px = 1 mm");
            Rect rc, rh, rp;
            DogBookStandRects(panelMm, out rc, out rh, out rp);
            check(rc.Contains(rp.min) && rc.Contains(rp.max) && !rp.Overlaps(rh), "panel rect inside the canvas and below the header (panel " + rp + ", header " + rh + ")");
            note(DogBookStandName + " at " + V(stand.position) + " yaw " + stand.eulerAngles.y.ToString("F1") + ", " + da.ToString("F2") + " m from Asobikata (" + lateral.ToString("F2") + " m sideways)");
        }
    }
}
