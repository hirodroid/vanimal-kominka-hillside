using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    // Guide/2: the checks (also run at the end of Guide/1). Writes Temp/kh-guide-boards/verify.json; never changes the scene.
    public static partial class KH_GuideBoardsEditor
    {
        // ---- placement rules (docs/plans/2026-10-02-guide-boards.md §3-2). Distances are horizontal, from the footprint (posts + roof). ----
        const float RouteClear = 1.8f;          // the loop's centre line (KH_WalkRoute.nodePositions)
        const float RoamClear = 3.3f;           // the dogs' free-roam centres KH_Dogs/KH_Roam_* (radius 3)
        const float SpawnClear = 3.0f;          // KH_Entrance: arrivals and dropped portals
        const float ReaderSpawnClear = 1.5f;    // the readers' area from the spawn point
        const float FixtureClear = 2.0f;        // shopkeeper, leash rack, welcome spot, treats
        const float WashFloorClear = 0.5f;      // outside the main wash station's 5 x 5 m floor (KH_WashStation/PersonalFloor)
        const float WashFloorHalf = 2.5f;
        const float EntranceRadius = 18f;       // the board's centre from the spawn
        const float GateSideMin = 1.5f;         // in front of the gate line (spawn forward) by at least this
        const float FarmhouseSectorMin = 45f;   // degrees between spawn->board and spawn->farmhouse / gate (the turn-around view)
        const float SlopeWarn = 0.6f, SlopeFail = 1.2f;   // terrain height range over the footprint + readers' area (m)
        const float ObstacleTop = 2.8f;
        static readonly string[] WoodParts = { "Back", "PostL", "PostR", "Ridge", "RoofFront", "RoofBack" };

        [Serializable] class VerifyReport
        {
            public string utc, scene, status;
            public int failed, warned;
            public List<string> lines = new List<string>();
            public List<string> notes = new List<string>();
            public List<string> fit = new List<string>();
            public string budget;
            public string limitations = "Static checks of the saved scene: placement against the terrain, the colliders and the scene's anchors; the words "
                + "against the catalogs and the scene's wash-board labels; text fit measured with the bundled font (the 11 client-glyph languages are "
                + "drawn by VRChat's own fallback fonts, so their fit is a WARN estimate only). Not judged here: readability in a headset (Guide/3 "
                + "captures + PCVR / Quest / iOS), night mode, how people actually walk around the boards, where dropped portals land.";
        }

        [MenuItem(Menu + "2 - Verify Guide Boards")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var v = Check();
            WriteJson(VerifyReportPath, v);
            if (v.failed > 0)
            {
                Debug.LogError("[KH Guide Boards] GUIDE_BOARDS_FAIL (" + v.failed + " failed, " + v.warned + " WARN) -> " + VerifyReportPath);
                throw new InvalidOperationException("[KH Guide Boards] Verify failed:\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL")).Take(30)));
            }
            Debug.Log("[KH Guide Boards] GUIDE_BOARDS_OK (" + v.lines.Count + " checks, " + v.warned + " WARN) -> " + VerifyReportPath);
        }

        class Anch
        {
            public Transform spawn, gate, kominka, keeper, rack, welcome, washMain, washFloor;
            public List<Transform> treats = new List<Transform>(), roams = new List<Transform>();
            public Vector3[] route; public bool closed;
        }

        static Transform ScenePath(UnityEngine.SceneManagement.Scene scene, string path)
        {
            var parts = path.Split(new[] { '/' }, 2);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == parts[0]);
            if (root == null) return null;
            return parts.Length == 1 ? root.transform : root.transform.Find(parts[1]);
        }

        static Anch FindAnchors(Ctx c)
        {
            var a = new Anch();
            a.spawn = ScenePath(c.scene, "KH_World/KH_Places/KH_Entrance");
            a.gate = ScenePath(c.scene, "KH_World/KH_Places/KH_GateBlock");
            a.kominka = ScenePath(c.scene, "KH_World/KH_Places/KH_FarKominka");
            a.keeper = ScenePath(c.scene, "KH_Dogs/KD_ShopKeeper");
            a.rack = ScenePath(c.scene, "KH_Dogs/KH_LeashRack");
            a.welcome = ScenePath(c.scene, "KH_Dogs/KD_WelcomeSpot");
            a.washMain = ScenePath(c.scene, "KH_WashStation");
            a.washFloor = a.washMain != null ? a.washMain.Find("PersonalFloor") : null;
            var dogs = ScenePath(c.scene, "KH_Dogs");
            if (dogs != null)
                foreach (Transform t in dogs)
                {
                    if (t.name.StartsWith("KD_Treat_")) a.treats.Add(t);
                    if (t.name.StartsWith("KH_Roam_")) a.roams.Add(t);
                }
            if (c.manager != null && c.manager.route != null) { a.route = c.manager.route.nodePositions; a.closed = c.manager.route.closedLoop; }
            return a;
        }

        static VerifyReport Check()
        {
            var v = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            Action<bool, string> req = (ok, what) => { v.lines.Add((ok ? "OK   " : "FAIL ") + what); if (!ok) v.failed++; };
            Action<bool, string> warn = (ok, what) => { v.lines.Add((ok ? "OK   " : "WARN ") + what); if (!ok) v.warned++; };
            Ctx c;
            try { c = Resolve(false); }
            catch (Exception e) { req(false, "resolve: " + e.Message); v.status = "GUIDE_BOARDS_FAIL"; return v; }
            req(c.root != null, RootName + " once at the scene root");
            if (c.root == null) { v.status = "GUIDE_BOARDS_FAIL"; return v; }
            req(c.root.position == Vector3.zero && c.root.rotation == Quaternion.identity && c.root.localScale == Vector3.one, RootName + " at the origin, no rotation, scale 1");
            foreach (Transform t in c.root) warn(Boards.Any(b => b.name == t.name), RootName + "/" + t.name + " is a board this builder makes");

            // ---- read-only: nothing to collide with, nothing to run, nothing to point at ----
            Func<Component[], string> names = list => string.Join(", ", list.Select(x => KD_SceneObjects.PathOf(x.gameObject)).Take(6));
            var cols = c.root.GetComponentsInChildren<Collider>(true);
            req(cols.Length == 0, "no Collider under " + RootName + (cols.Length > 0 ? " (" + names(cols) + ")" : " (players, dogs, discs and the interact ray pass)"));
            var udons = c.root.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true);
            req(udons.Length == 0, "no UdonBehaviour under " + RootName + (udons.Length > 0 ? " (" + names(udons) + ")" : ""));
            var bodies = c.root.GetComponentsInChildren<Rigidbody>(true);
            req(bodies.Length == 0, "no Rigidbody under " + RootName);
            var rays = c.root.GetComponentsInChildren<GraphicRaycaster>(true);
            var shapes = c.root.GetComponentsInChildren<VRC.SDK3.Components.VRCUiShape>(true);
            req(rays.Length == 0 && shapes.Length == 0, "read-only canvases: no GraphicRaycaster / VRCUiShape (vrc-world-ui §2)");
            var occluders = c.root.GetComponentsInChildren<Transform>(true).Where(t => (GameObjectUtility.GetStaticEditorFlags(t.gameObject) & StaticEditorFlags.OccluderStatic) != 0).ToArray();
            req(occluders.Length == 0, "nothing Occluder Static (Course/37: only the terrain occludes)");

            var a = FindAnchors(c);
            foreach (var b in Boards) CheckBoard(b, c, a, req, warn, v);
            CheckLabels(c, req, v);
            CheckWords(c, req, warn, v);
            CheckGlyphs(c, req);
            CheckFit(c, req, warn, v);
            try { KD_LocalizationEditor.ValidateScene(c.language, c.catalog); req(true, "localization tables, bindings and glyphs match the catalogs (KD_LocalizationEditor.ValidateScene)"); }
            catch (Exception e) { req(false, "localization: " + e.Message + " (Guide/1 re-binds; a missing object belongs to another builder)"); }
            v.budget = Budget(c);
            v.status = v.failed == 0 ? "GUIDE_BOARDS_OK" : "GUIDE_BOARDS_FAIL";
            return v;
        }

        // ================================================================ one board: pose, frame, canvas, placement

        static void CheckBoard(Board b, Ctx c, Anch a, Action<bool, string> req, Action<bool, string> warn, VerifyReport v)
        {
            var t = c.root.Find(b.name);
            req(t != null, b.name + " exists");
            if (t == null) return;
            var e = t.eulerAngles;
            req(Mathf.Abs(Mathf.DeltaAngle(e.x, 0f)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(e.z, 0f)) < 0.01f && t.localScale == Vector3.one, b.name + " yaw only, scale 1 (euler " + e.ToString("F2") + ")");
            req(t.gameObject.activeInHierarchy, b.name + " active");
            float seat;
            try { seat = SeatSamples(b).Max(p => Ground(t.TransformPoint(p))); }
            catch (Exception ex) { req(false, b.name + " terrain under the posts: " + ex.Message); return; }
            req(Mathf.Abs(t.position.y - seat) <= 0.02f, b.name + " seated on the terrain (" + (t.position.y - seat).ToString("+0.000;-0.000") + " m; moved by hand? run Guide/1)");

            // ---- the frame ----
            var s = t.Find(StructureName);
            req(s != null, b.name + "/" + StructureName);
            if (s != null)
                foreach (var name in WoodParts)
                {
                    var p = s.Find(name);
                    var mr = p != null ? p.GetComponent<MeshRenderer>() : null;
                    req(mr != null && mr.sharedMaterial == c.wood, b.name + "/" + name + " wood (" + WoodMatPath + ")");
                    if (p != null) req((GameObjectUtility.GetStaticEditorFlags(p.gameObject) & StaticEditorFlags.BatchingStatic) != 0, b.name + "/" + name + " Batching Static");
                    if (p != null && name.StartsWith("Post"))
                    {
                        float bottom = p.position.y - p.lossyScale.y * 0.5f, g = Ground(p.position);
                        req(bottom <= g - 0.05f, b.name + "/" + name + " reaches into the ground (bottom " + (bottom - g).ToString("+0.00;-0.00") + " m)");
                    }
                }

            // ---- the canvas ----
            var cv = t.Find(CanvasName) as RectTransform;
            var canvas = cv != null ? cv.GetComponent<Canvas>() : null;
            req(canvas != null && canvas.renderMode == RenderMode.WorldSpace, b.name + "/" + CanvasName + " World Space Canvas");
            if (cv != null)
            {
                req(Mathf.Abs(cv.localScale.x - Mm) < 1e-6f && Mathf.Abs(cv.localScale.y - Mm) < 1e-6f && Mathf.Abs(cv.localScale.z - Mm) < 1e-6f, b.name + " canvas scale 0.001 (1 unit = 1 mm)");
                req(cv.sizeDelta == b.canvasMm && cv.localRotation == Quaternion.identity && Mathf.Abs(cv.localPosition.y - CanvasCenterY(b)) < 0.001f,
                    b.name + " canvas " + cv.sizeDelta + " mm at " + cv.localPosition.y.ToString("F2") + " m, front -Z");
                req(cv.gameObject.layer == 0, b.name + " canvas on Default");
            }
            if (b == MapBoard && cv != null) CheckMap(cv, t, c, req);

            // ---- placement ----
            try { Placement(b, t, a, c, req, warn, v); }
            catch (Exception ex) { req(false, b.name + " placement could not be measured: " + ex.Message); }
        }

        static void CheckMap(RectTransform cv, Transform board, Ctx c, Action<bool, string> req)
        {
            var paper = cv.Find("Paper");
            req(paper != null, "Map/Canvas/Paper");
            if (paper == null) return;
            var raw = paper.Find("MapImage") != null ? paper.Find("MapImage").GetComponent<RawImage>() : null;
            req(raw != null && raw.texture == c.mapTexture, "the map is the hand map's texture " + KH_CourseTravelMapEditor.TexturePath + " (Course/84)");
            for (int s = 0; s < 6; s++)
            {
                var n = paper.Find("Num" + (s + 1));
                var tmp = n != null ? n.GetComponent<TextMeshProUGUI>() : null;
                var want = MapLocal(c.manager.spotPositions[s]) + NumOffset;
                req(tmp != null && tmp.text == (s + 1).ToString() && Vector2.Distance(((RectTransform)n).anchoredPosition, want) < 1f,
                    "map number " + (s + 1) + " at spot " + s + " (" + (c.manager.spotWarpEnabled[s] ? "warp" : "landmark") + ")");
            }
            var here = paper.Find("Here") as RectTransform;
            req(here != null && Vector2.Distance(here.anchoredPosition, MapLocal(board.position)) < 1f, "\"you are here\" at the board's position");
            int want2 = c.washStations.Count(st => st != null && Flat(st.position - board.position) >= WashMarkSkipMeters);
            int have = paper.Cast<Transform>().Count(x => x.name.StartsWith("Wash_"));
            req(have == want2, "wash station marks " + have + " = stations farther than " + WashMarkSkipMeters + " m (" + want2 + ")");
        }

        static void Placement(Board b, Transform t, Anch a, Ctx c, Action<bool, string> req, Action<bool, string> warn, VerifyReport v)
        {
            var foot = Samples(t, Footprint(b), 0.1f);
            var read = Samples(t, ReaderArea(b), 0.2f);
            string tag = b.name + " placement: ";
            Func<List<Vector3>, Vector3, float> minTo = (list, p) => list.Min(q => Flat(q - p));

            if (a.route != null && a.route.Length > 1)
            {
                int n = a.route.Length, segs = a.closed ? n : n - 1;
                float best = float.PositiveInfinity;
                foreach (var p in foot)
                    for (int i = 0; i < segs; i++) best = Mathf.Min(best, SegDist(p, a.route[i], a.route[(i + 1) % n]));
                req(best >= RouteClear, tag + "the loop's centre line " + best.ToString("F2") + " m (>= " + RouteClear + ")");
            }
            else req(false, tag + "KH_WalkRoute.nodePositions missing");
            if (a.roams.Count > 0)
            {
                float best = a.roams.Min(r => minTo(foot, r.position));
                req(best >= RoamClear, tag + "dog roam centres " + best.ToString("F2") + " m (>= " + RoamClear + ", roam radius 3)");
            }
            else warn(false, tag + "no KH_Dogs/KH_Roam_* found");
            if (a.spawn != null)
            {
                float f = minTo(foot, a.spawn.position), r = minTo(read, a.spawn.position);
                req(f >= SpawnClear, tag + "spawn " + f.ToString("F2") + " m (>= " + SpawnClear + ")");
                req(r >= ReaderSpawnClear, tag + "readers' area from the spawn " + r.ToString("F2") + " m (>= " + ReaderSpawnClear + ")");
                float centre = Flat(t.position - a.spawn.position);
                req(centre <= EntranceRadius, tag + "within the entrance (" + centre.ToString("F1") + " m from the spawn, <= " + EntranceRadius + ")");
                Vector3 fwd = a.spawn.forward; fwd.y = 0f; fwd.Normalize();
                float bearingBoard = Bearing(a.spawn.position, t.position);
                if (a.gate != null)
                {
                    float side = foot.Min(p => Vector3.Dot(Flat3(p - a.gate.position), fwd));
                    req(side >= GateSideMin, tag + "outside the gate, " + side.ToString("F2") + " m in front of the gate line (>= " + GateSideMin + ")");
                    float d = Mathf.Abs(Mathf.DeltaAngle(bearingBoard, Bearing(a.spawn.position, a.gate.position)));
                    req(d >= FarmhouseSectorMin, tag + "not in the turn-around view to the gate (" + d.ToString("F0") + " deg apart, >= " + FarmhouseSectorMin + ")");
                }
                if (a.kominka != null)
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(bearingBoard, Bearing(a.spawn.position, a.kominka.position)));
                    req(d >= FarmhouseSectorMin, tag + "not in the turn-around view to the farmhouse (" + d.ToString("F0") + " deg apart, >= " + FarmhouseSectorMin + ")");
                }
                Vector3 front = -t.forward; front.y = 0f;
                float facing = Vector3.Angle(front, Flat3(a.spawn.position - t.position));
                v.notes.Add(b.name + ": " + centre.ToString("F1") + " m from the spawn at bearing " + bearingBoard.ToString("F0") + " deg (" + Mathf.DeltaAngle(Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, bearingBoard).ToString("+0;-0")
                    + " deg from the spawn's facing); its front is " + facing.ToString("F0") + " deg off the direction to the spawn");
            }
            else req(false, tag + "KH_Entrance missing");
            foreach (var kv in new[] { new KeyValuePair<string, Transform>("shopkeeper", a.keeper), new KeyValuePair<string, Transform>("leash rack", a.rack), new KeyValuePair<string, Transform>("welcome spot", a.welcome) })
            {
                if (kv.Value == null) { warn(false, tag + kv.Key + " not found"); continue; }
                float d = minTo(foot, kv.Value.position);
                req(d >= FixtureClear, tag + kv.Key + " " + d.ToString("F2") + " m (>= " + FixtureClear + ")");
            }
            if (a.treats.Count > 0) { float d = a.treats.Min(x => minTo(foot, x.position)); req(d >= FixtureClear, tag + "treats " + d.ToString("F2") + " m (>= " + FixtureClear + ")"); }
            if (a.washFloor != null && a.washMain != null)
            {
                float out1 = foot.Min(p => OutsideSquare(p, a.washFloor.position, a.washMain.rotation, WashFloorHalf));
                bool readOverlap = read.Any(p => OutsideSquare(p, a.washFloor.position, a.washMain.rotation, WashFloorHalf) <= 0f);
                req(out1 >= WashFloorClear, tag + "outside the main wash station's floor by " + out1.ToString("F2") + " m (>= " + WashFloorClear + ")");
                req(!readOverlap, tag + "the readers' area is not on the main wash station's floor");
            }
            else warn(false, tag + "KH_WashStation/PersonalFloor not found (wash station clearance not checked)");

            // ---- solid things that players bump into, over the board and where the readers stand ----
            Physics.SyncTransforms();
            Rect fr = Footprint(b), rr = ReaderArea(b);
            float gy = t.position.y;
            var hitsFoot = Overlap(t, fr, gy + 0.05f, gy + ObstacleTop, c);
            float ry = Ground(t.TransformPoint(new Vector3(0f, 0f, rr.center.y)));
            var hitsRead = Overlap(t, rr, ry + 0.3f, ry + 1.9f, c);
            req(hitsFoot.Count == 0, tag + "no solid collider in the board's volume" + (hitsFoot.Count > 0 ? " (" + string.Join(", ", hitsFoot.Take(5)) + ")" : ""));
            req(hitsRead.Count == 0, tag + "no solid collider where the readers stand" + (hitsRead.Count > 0 ? " (" + string.Join(", ", hitsRead.Take(5)) + ")" : ""));

            // ---- ground ----
            var heights = foot.Concat(read).Select(p => Ground(p)).ToList();
            float range = heights.Max() - heights.Min();
            req(range <= SlopeFail, tag + "terrain range over the board + readers' area " + range.ToString("F2") + " m (<= " + SlopeFail + ")");
            warn(range <= SlopeWarn, tag + "terrain range " + range.ToString("F2") + " m (<= " + SlopeWarn + " for an easy stand)");
            var readerPoint = t.TransformPoint(new Vector3(0f, 0f, -ReadDistance));
            float above = t.TransformPoint(new Vector3(0f, CanvasCenterY(b), 0f)).y - Ground(readerPoint);
            warn(above >= 1.0f && above <= 2.0f, tag + "the papers' centre " + above.ToString("F2") + " m above the ground where a reader stands 1.8 m away (1.0 .. 2.0)");

            // ---- the view of the farmhouse from the last stretch of the loop (note only) ----
            if (a.route != null && a.kominka != null && a.spawn != null)
            {
                int n = a.route.Length, seen = 0, blocked = 0;
                var hit = new List<int>();
                for (int i = Mathf.Max(0, n - 12); i < n; i++)
                {
                    if (Flat(a.route[i] - a.spawn.position) > 25f) continue;
                    seen++;
                    if (Crosses(t, fr, a.route[i], a.kominka.position)) { blocked++; hit.Add(i); }
                }
                v.notes.Add(b.name + ": crosses the sight line to KH_FarKominka from " + blocked + " of " + seen + " route nodes on the last stretch within 25 m of the spawn" + (hit.Count > 0 ? " (nodes " + string.Join(", ", hit) + ")" : ""));
            }
        }

        static List<Vector3> Samples(Transform t, Rect r, float step)
        {
            var list = new List<Vector3>();
            int nx = Mathf.Max(1, Mathf.CeilToInt(r.width / step)), nz = Mathf.Max(1, Mathf.CeilToInt(r.height / step));
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                    list.Add(t.TransformPoint(new Vector3(r.xMin + r.width * i / nx, 0f, r.yMin + r.height * j / nz)));
            return list;
        }

        static List<string> Overlap(Transform t, Rect r, float bottom, float top, Ctx c)
        {
            var centre = t.TransformPoint(new Vector3(r.center.x, 0f, r.center.y)); centre.y = (bottom + top) * 0.5f;
            var half = new Vector3(r.width * 0.5f, (top - bottom) * 0.5f, r.height * 0.5f);
            return Physics.OverlapBox(centre, half, t.rotation, ~0, QueryTriggerInteraction.Ignore)
                .Where(x => !(x is TerrainCollider) && !x.transform.IsChildOf(c.root) && !Physics.GetIgnoreLayerCollision(x.gameObject.layer, 10))
                .Select(x => KD_SceneObjects.PathOf(x.gameObject)).Distinct().ToList();
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z), ab = B - A;
            float l2 = ab.sqrMagnitude, u = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(P - A, ab) / l2) : 0f;
            return Vector2.Distance(P, A + ab * u);
        }

        /// <summary>Signed distance outside a square (centre, yaw of rot, half size); &lt;= 0 inside.</summary>
        static float OutsideSquare(Vector3 p, Vector3 centre, Quaternion rot, float half)
        {
            var l = Quaternion.Inverse(Quaternion.Euler(0f, rot.eulerAngles.y, 0f)) * (p - centre);
            float dx = Mathf.Abs(l.x) - half, dz = Mathf.Abs(l.z) - half;
            if (dx <= 0f && dz <= 0f) return Mathf.Max(dx, dz);
            return new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dz, 0f)).magnitude;
        }

        static bool Crosses(Transform t, Rect r, Vector3 from, Vector3 to)
        {
            int steps = Mathf.CeilToInt(Flat(to - from) / 0.25f);
            for (int i = 0; i <= steps; i++)
            {
                var l = t.InverseTransformPoint(Vector3.Lerp(from, to, i / (float)steps));
                if (r.Contains(new Vector2(l.x, l.z))) return true;
            }
            return false;
        }

        static float Bearing(Vector3 from, Vector3 to) { return Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg; }
        static Vector3 Flat3(Vector3 v) { v.y = 0f; return v; }

        // ================================================================ labels and their bindings

        static void CheckLabels(Ctx c, Action<bool, string> req, VerifyReport v)
        {
            var lang = c.language;
            var lb = UdonSharpEditorUtility.GetBackingUdonBehaviour(lang);
            object bkObj = null, blObj = null;
            bool backing = lb != null && lb.publicVariables.TryGetVariableValue("fixedKeys", out bkObj) && lb.publicVariables.TryGetVariableValue("fixedLabels", out blObj);
            req(backing, "KD_Language backing has fixedKeys / fixedLabels");
            var bk = bkObj as string[]; var bl = blObj as Array;
            string ja = c.catalog.fallbackLanguage;
            foreach (var l in Labels)
            {
                var tr = c.root.Find(l.path);
                var tmp = tr != null ? tr.GetComponent<TextMeshProUGUI>() : null;
                req(tmp != null, "label " + l.path);
                if (tmp == null) continue;
                req(!string.IsNullOrWhiteSpace(tmp.text) && tmp.text == KD_LocalizationEditor.Text(c.catalog, ja, l.key), "label " + l.path + " holds the " + ja + " text of " + l.key + " (Bind)");
                req(tmp.font == c.font, "label " + l.path + " uses the menu font");
                req(!tmp.raycastTarget && tmp.overflowMode == TextOverflowModes.Overflow && tmp.richText, "label " + l.path + " not a raycast target, Overflow, rich text");
                int i = lang.fixedLabels != null ? Array.IndexOf(lang.fixedLabels, tmp) : -1;
                req(i >= 0 && lang.fixedKeys != null && i < lang.fixedKeys.Length && lang.fixedKeys[i] == l.key, "label " + l.path + " bound to " + l.key + " (proxy)");
                bool inBacking = false;
                if (bk != null && bl != null)
                    for (int k = 0; k < bl.Length && k < bk.Length; k++)
                        if (ReferenceEquals(bl.GetValue(k), tmp) && bk[k] == l.key) { inBacking = true; break; }
                req(inBacking, "label " + l.path + " bound to " + l.key + " (backing)");
            }
        }

        // ================================================================ the words say what the world does

        static void CheckWords(Ctx c, Action<bool, string> req, Action<bool, string> warn, VerifyReport v)
        {
            string ja = c.catalog.fallbackLanguage;
            var course = JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath));
            var known = new HashSet<string>();
            foreach (var cat in new[] { c.catalog, course })
            {
                var l = cat.languages.FirstOrDefault(x => x.code == ja);
                if (l != null) foreach (var e in l.texts) if (!string.IsNullOrEmpty(e.text) && !e.key.StartsWith("guide.")) known.Add(e.text);
            }
            var sceneWords = new List<string>();
            foreach (var st in c.washStations) if (st != null) foreach (var t in st.GetComponentsInChildren<TMP_Text>(true)) if (!string.IsNullOrEmpty(t.text)) sceneWords.Add(t.text);
            foreach (var l in Labels)
            {
                string text = Visible(KD_LocalizationEditor.Text(c.catalog, ja, l.key));
                foreach (Match m in Regex.Matches(text, "「([^」]+)」"))
                    req(known.Contains(m.Groups[1].Value), l.key + ": 「" + m.Groups[1].Value + "」 is a menu label (catalog text)");
                foreach (Match m in Regex.Matches(text, "［([^］]+)］"))
                {
                    string w = m.Groups[1].Value;
                    req(known.Contains(w) || sceneWords.Any(s => s.Contains(w)), l.key + ": ［" + w + "］ is a button in the menu or on a wash-station board");
                }
            }
            // the map legend uses the hand menu's names
            var names = new[] { new[] { "base", "KD_TreatPouch.WordWalkGate" }, new[] { "base", "KD_TreatPouch.WordWalkPlaza" }, new[] { "course", "course.walk.upper" },
                new[] { "course", "course.travel.join.spot.bridgeUp" }, new[] { "course", "course.walk.river" }, new[] { "course", "course.travel.join.spot.bridgeDown" } };
            foreach (var code in new[] { "ja", "en" })
            {
                var lines = KD_LocalizationEditor.Text(c.catalog, code, "guide.map.legend").Split('\n');
                bool ok = lines.Length == 6;
                for (int i = 0; ok && i < 6; i++)
                {
                    var src = names[i][0] == "base" ? c.catalog : course;
                    ok = lines[i] == (i + 1) + " " + KD_LocalizationEditor.Text(src, code, names[i][1]);
                }
                req(ok, "guide.map.legend (" + code + ") = the spots' names in the menu, in spot order 1..6");
            }
            // numbers the boards state
            var keepers = UnityEngine.Object.FindObjectsOfType<KD_ShopKeeper>(true);
            var ledger = keepers.Length == 1 ? keepers[0].ledger : null;
            req(ledger != null && ledger.keeperPoints == 200, "the shopkeeper gives 200 pt once (KD_ProgressLedger.keeperPoints " + (ledger != null ? ledger.keeperPoints.ToString() : "?") + "; guide.shop.body says 200)");
            var stations = c.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "KH_WashStations");
            int along = stations != null ? stations.transform.childCount : 0;
            req(along == 3, "3 wash stations along the course (KH_WashStations: " + along + "; guide.wash.body says 3)");
            var passes = UnityEngine.Object.FindObjectsOfType<KH_TrainerPass>(true);
            warn(passes.Length == 1 && passes[0]._Configured(), "KH_TrainerPass has a product and a listing (the board sends people to [購入画面をひらく])");
        }

        static void CheckGlyphs(Ctx c, Action<bool, string> req)
        {
            var missing = new HashSet<char>();
            foreach (var ch in GlyphText(c.catalog))
                if (KD_LocalizationEditor.Drawn(ch) && ch != ' ' && !KD_LocalizationEditor.HasGlyph(c.font, ch)) missing.Add(ch);
            req(missing.Count == 0, "every ja / en character of the boards is in " + c.font.name + (missing.Count > 0 ? " (missing: " + string.Join(" ", missing.Select(ch => ch + " U+" + ((int)ch).ToString("X4"))) + "; Guide/1 adds them)" : ""));
        }

        // ================================================================ fit (vrc-world-ui §10)

        static void CheckFit(Ctx c, Action<bool, string> req, Action<bool, string> warn, VerifyReport v)
        {
            var go = new GameObject("GuideBoardsMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                foreach (var l in Labels)
                {
                    var tr = c.root.Find(l.path);
                    var tmp = tr != null ? tr.GetComponent<TextMeshProUGUI>() : null;
                    if (tmp == null) continue;
                    int worstSelf = 0, worstClient = 0; var over = new List<string>();
                    foreach (var lang in c.catalog.languages)
                    {
                        string text = KD_LocalizationEditor.Text(c.catalog, lang.code, l.key);
                        int r = KD_LocalizationEditor.MeasureLabelFit(sample, tmp, text, out Vector2 pref);
                        bool self = KD_LocalizationEditor.Bundled(lang);
                        if (self) worstSelf = Mathf.Max(worstSelf, r); else worstClient = Mathf.Max(worstClient, r);
                        if (r == 2) over.Add(lang.code);
                        if (self || r == 2) v.fit.Add(lang.code + " " + l.key + " preferred=" + pref.ToString("F0") + " rect=" + tmp.rectTransform.rect.size.ToString("F0") + " fit=" + r + (r == 1 ? " (shrinks toward " + tmp.fontSizeMin + ")" : ""));
                    }
                    req(worstSelf < 2, l.key + " fits its box in ja / en (worst " + worstSelf + ": 0 at full size, 1 shrinks, 2 overflows at the minimum)");
                    warn(worstClient < 2, l.key + " fits in the client-glyph languages (estimate)" + (over.Count > 0 ? ": overflows in " + string.Join(", ", over) : ""));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ================================================================ budget (report only)

        static string Budget(Ctx c)
        {
            var mrs = c.root.GetComponentsInChildren<MeshRenderer>(true);
            long tris = 0;
            foreach (var mr in mrs) { var mf = mr.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += mf.sharedMesh.GetIndexCount(s) / 3; }
            var parts = new List<string>();
            int tmpCount = 0, chars = 0;
            foreach (var cv in c.root.GetComponentsInChildren<Canvas>(true))
            {
                var graphics = cv.GetComponentsInChildren<Graphic>(true);
                var groups = new HashSet<string>();
                foreach (var g in graphics)
                {
                    var mat = g.materialForRendering; var tex = g.mainTexture;
                    groups.Add((mat != null ? mat.name : "null") + "|" + (tex != null ? tex.name : "null"));
                }
                var tmps = cv.GetComponentsInChildren<TextMeshProUGUI>(true);
                tmpCount += tmps.Length;
                int cc = tmps.Sum(x => Visible(x.text).Count(ch => !char.IsWhiteSpace(ch)));
                chars += cc;
                parts.Add(cv.transform.parent.name + ": " + graphics.Length + " graphics in " + groups.Count + " material/texture groups (≈ batches when nothing interleaves), " + tmps.Length + " labels, " + cc + " ja characters");
            }
            return "frame: " + mrs.Length + " cube renderers, " + tris + " triangles, one material (" + WoodMatPath + "), Batching Static (≈ 1 draw call + shadow when visible); "
                + string.Join("; ", parts) + "; text quads ≈ " + (chars * 2) + " triangles; new textures 0 (map = " + KH_CourseTravelMapEditor.TexturePath + ", already in the hand menu; the dot uses KD_UiCircle); "
                + "font: the shared 2048² atlas (no new texture memory), glyphs added only for missing ja / en characters; Udon 0, colliders 0, network objects 0";
        }
    }
}
