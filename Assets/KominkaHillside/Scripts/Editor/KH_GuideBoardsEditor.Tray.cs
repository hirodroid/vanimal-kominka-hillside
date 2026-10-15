using System;
using System.Collections.Generic;
using System.Linq;
using KominkaDogRun.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    // BUG-ENV-005 (2026-10-15): the wooden backs of the boards and posters as "trays" with holes where the papers are.
    //
    // Quest only flickered where a paper (World Space Canvas, z 0) floated 10 mm in front of the board's wood (the old Back cube's
    // front face at z 0.01), the posters' images 2 / 4 mm in front of their planks. PC (DX11, reversed-Z float depth) never shows it;
    // Quest's depth precision is unconfirmed and Near is 0.01 since BUG-WLD-002 (2026-10-14), so no millimetre gap is safe at distance.
    // The fix removes the parallel overlap instead of tuning the gap:
    //   - the wood's front face (the rim) lies in the canvas plane and has a hole exactly under every top-level paper / plate (the
    //     canvas's direct opaque Images / RawImages), so wood and paper only meet edge to edge, never overlap;
    //   - every drawn Graphic of the canvas (texts, photos, badges, at any depth) must lie inside those holes (CanvasHoles throws
    //     otherwise: UI over the wood in the same plane would fight again). UI over UI never fights (UI/Default has ZWrite Off);
    //   - boards (pockets = true): each hole is a closed pocket (walls + a floor at the tray's back plane, 50 mm behind the paper),
    //     so the shell stays closed (shadows, KD_DogBook's index page that hides while its detail page shows 40 mm in front, and
    //     sub-pixel cracks along the paper edge all show dark wood, never the scenery behind the board);
    //   - posters (pockets = false): plain holes; the farmhouse wall 30 mm behind is the backdrop (a 20 mm plank cannot hold a
    //     floor far enough behind the image), and the plank casts shadows two-sided so the holes do not let light through.
    // UVs copy the built-in Cube's per-face mapping (0..1 across each whole face of the box), so the rim shows exactly the texels the
    // cube showed there and the look of the wood does not change. The mesh is an asset per board (Meshes/Boards/<name>.asset),
    // rewritten in place (same GUID) only when its data changed, so re-running a builder neither duplicates nor dirties it.
    public static partial class KH_GuideBoardsEditor
    {
        public const string BoardMeshDir = "Assets/KominkaHillside/Meshes/Boards";
        const string BoardMeshParent = "Assets/KominkaHillside/Meshes";
        /// <summary>Board-local z of the tray's front (the rim) = the drawing canvas's plane (EnsureCanvas puts the canvas at z 0).</summary>
        const float TrayFront = 0f;
        /// <summary>Board-local z of the tray's back = the old Back cube's back face (BackZ + BackDepth / 2 = 0.05): the board grows 10 mm
        /// toward the readers (its rim is flush with the papers now); seen from behind it is unchanged.</summary>
        static float TrayBack { get { return BackZ + BackDepth * 0.5f; } }
        /// <summary>Hole edges are snapped to 0.01 mm (deterministic mesh data across runs); the cover test allows 0.05 mm; a canvas must sit
        /// within 0.1 mm of the tray's front plane; a hole must keep 0.5 mm of rim to the tray's outer edge.</summary>
        const float HoleSnap = 1e-5f, HoleCoverTol = 5e-5f, CanvasPlaneTol = 1e-4f, HoleMarginMin = 5e-4f;

        // ================================================================ holes from the canvases

        static float Snap(float v) { return Mathf.Round(v / HoleSnap) * HoleSnap; }
        static Rect SnapRect(Rect r) { return Rect.MinMaxRect(Snap(r.xMin), Snap(r.yMin), Snap(r.xMax), Snap(r.yMax)); }

        /// <summary>A RectTransform's rect in <paramref name="frame"/>-local x / y (m), snapped.</summary>
        static Rect FrameRect(Transform frame, RectTransform rt)
        {
            var w = new Vector3[4]; rt.GetWorldCorners(w);
            float x0 = float.PositiveInfinity, y0 = float.PositiveInfinity, x1 = float.NegativeInfinity, y1 = float.NegativeInfinity;
            foreach (var p in w) { var l = frame.InverseTransformPoint(p); x0 = Mathf.Min(x0, l.x); y0 = Mathf.Min(y0, l.y); x1 = Mathf.Max(x1, l.x); y1 = Mathf.Max(y1, l.y); }
            return SnapRect(Rect.MinMaxRect(x0, y0, x1, y1));
        }

        static bool HasGraphicAncestor(Transform t, Transform canvas)
        {
            for (var p = t.parent; p != null && p != canvas; p = p.parent) if (p.GetComponent<Graphic>() != null) return true;
            return false;
        }

        /// <summary>Why a hole's graphic may not hide the hole completely (null = opaque, flat). Reported, not refused: the pocket / the wall shows through.</summary>
        static string NotOpaque(Graphic g)
        {
            if (g.color.a < 0.999f) return "alpha " + g.color.a.ToString("F2") + " (what is behind the hole shows through faintly)";
            var img = g as Image;
            if (img != null && img.sprite != null) return "sprite " + img.sprite.name + " (transparent corners would show what is behind the hole)";
            var raw = g as RawImage;
            if (raw != null)
            {
                if (raw.texture == null) return "RawImage without a texture";
                var ti = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(raw.texture)) as TextureImporter;
                if (ti != null && ti.alphaSource != TextureImporterAlphaSource.None && ti.DoesSourceTextureHaveAlpha())
                    return "texture " + raw.texture.name + " has an alpha channel (fine only if every pixel is opaque; KH_TorinosukePoster.png measured all 255 on 2026-10-15)";
            }
            return null;
        }

        /// <summary>True when the union of <paramref name="holes"/> covers <paramref name="r"/> (shrunk by HoleCoverTol). Exact: the rect is cut on
        /// every hole edge and each cell's centre is tested.</summary>
        static bool Covered(Rect r, List<Rect> holes)
        {
            var s = Rect.MinMaxRect(r.xMin + HoleCoverTol, r.yMin + HoleCoverTol, r.xMax - HoleCoverTol, r.yMax - HoleCoverTol);
            if (s.width <= 0f || s.height <= 0f) return true;   // a zero-size rect draws nothing
            if (holes.Any(h => h.xMin <= s.xMin && h.xMax >= s.xMax && h.yMin <= s.yMin && h.yMax >= s.yMax)) return true;
            var xs = new List<float> { s.xMin, s.xMax }; var ys = new List<float> { s.yMin, s.yMax };
            foreach (var h in holes)
            {
                foreach (var x in new[] { h.xMin, h.xMax }) if (x > s.xMin && x < s.xMax) xs.Add(x);
                foreach (var y in new[] { h.yMin, h.yMax }) if (y > s.yMin && y < s.yMax) ys.Add(y);
            }
            xs = xs.Distinct().OrderBy(x => x).ToList(); ys = ys.Distinct().OrderBy(y => y).ToList();
            for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < ys.Count; j++)
                {
                    var c = new Vector2((xs[i] + xs[i + 1]) * 0.5f, (ys[j] + ys[j + 1]) * 0.5f);
                    if (!holes.Any(h => h.Contains(c))) return false;
                }
            return true;
        }

        /// <summary>BUG-ENV-005: the holes of a tray in <paramref name="frame"/>-local x / y (m): one per top-level Image / RawImage of each canvas
        /// (no Graphic between it and the canvas; enabled, alpha > 0, active or not) plus <paramref name="extra"/> (DogBook: the KD_DogBook panel,
        /// which is not on the stand's canvas). Throws (nothing is saved by the callers) when a canvas is not in the tray's front plane
        /// (<paramref name="frontZ"/>, frame-local) or when any drawn Graphic of the canvases lies (partly) outside the holes, i.e. on the wood
        /// in the same plane. Top-level texts (the headers) are not holes: they must lie on a plate. <paramref name="keep"/> = what this run built
        /// (the callers' keep set): stale graphics of an earlier layout, removed right after, neither cut holes nor are checked.</summary>
        static List<Rect> CanvasHoles(Transform frame, float frontZ, IEnumerable<RectTransform> canvases, IEnumerable<Rect> extra, HashSet<Transform> keep, List<string> notes)
        {
            var holes = new List<Rect>(); var drawn = new List<KeyValuePair<string, Rect>>();
            foreach (var cv in canvases)
            {
                float z = frame.InverseTransformPoint(cv.position).z;
                if (Mathf.Abs(z - frontZ) > CanvasPlaneTol || Vector3.Angle(cv.forward, frame.forward) > 0.01f)
                    throw new InvalidOperationException("BUG-ENV-005: " + KD_SceneObjects.PathOf(cv.gameObject) + " is not in the tray's front plane (z " + z.ToString("F4") + ", expected " + frontZ.ToString("F4") + ", facing like the board); reopen the scene without saving");
                foreach (var g in cv.GetComponentsInChildren<Graphic>(true))
                {
                    if (!g.enabled || g.color.a <= 0f || g is TMPro.TMP_SubMeshUI) continue;
                    if (keep != null && !keep.Contains(g.transform)) continue;
                    string path = KD_SceneObjects.PathOf(g.gameObject);
                    if (g.transform == cv) { notes.Add("the canvas itself carries " + g.GetType().Name + " (" + path + "): not a hole, checked as a drawn part"); }
                    var r = FrameRect(frame, g.rectTransform);
                    drawn.Add(new KeyValuePair<string, Rect>(path, r));
                    if (g.transform == cv || !(g is Image || g is RawImage) || HasGraphicAncestor(g.transform, cv)) continue;
                    holes.Add(r);
                    var why = NotOpaque(g);
                    if (why != null) notes.Add("hole under " + path + ": " + why);
                    if (!g.gameObject.activeInHierarchy) notes.Add("hole under the inactive " + path + " (the pocket shows while it is hidden)");
                }
            }
            if (extra != null) holes.AddRange(extra.Select(SnapRect));
            var outside = drawn.Where(d => !Covered(d.Value, holes)).Select(d => d.Key + " " + d.Value.ToString("F4")).ToList();
            if (outside.Count > 0)
                throw new InvalidOperationException("BUG-ENV-005: drawn UI outside the papers' holes would lie on the wood in the same plane (Quest z-fighting): "
                    + string.Join("; ", outside.Take(8)) + (outside.Count > 8 ? " (+" + (outside.Count - 8) + ")" : "") + ". Keep every label / image on a paper or plate; reopen the scene without saving");
            return holes;
        }

        // ================================================================ the tray mesh

        /// <summary>The built-in Cube's UV mapping of one face: uv = c + ai * p[i] + aj * p[j] for p in the unit cube (-0.5..0.5).</summary>
        class FaceUv { public int i, j; public Vector2 c, ai, aj; }
        static Dictionary<int, FaceUv> cubeUv;

        static int FaceKey(Vector3 n)
        {
            int axis = Mathf.Abs(n.x) >= Mathf.Abs(n.y) && Mathf.Abs(n.x) >= Mathf.Abs(n.z) ? 0 : Mathf.Abs(n.y) >= Mathf.Abs(n.z) ? 1 : 2;
            return axis * 2 + (n[axis] > 0f ? 1 : 0);
        }

        /// <summary>Reads the six face mappings from Resources' Cube.fbx (the mesh GameObject.CreatePrimitive(Cube) uses, as the old Back did).</summary>
        static FaceUv CubeFace(Vector3 n)
        {
            if (cubeUv == null)
            {
                var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                if (cube == null) throw new InvalidOperationException("the built-in Cube mesh is not available (its UVs are copied to the board trays)");
                var v = cube.vertices; var nn = cube.normals; var uv = cube.uv;
                var map = new Dictionary<int, FaceUv>();
                foreach (var grp in Enumerable.Range(0, v.Length).GroupBy(k => FaceKey(nn[k])))
                {
                    int axis = grp.Key / 2, i = (axis + 1) % 3, j = (axis + 2) % 3;
                    var ids = grp.ToList();
                    FaceUv f = null;
                    for (int a = 0; a < ids.Count && f == null; a++)
                        for (int b = a + 1; b < ids.Count && f == null; b++)
                            for (int c = b + 1; c < ids.Count && f == null; c++)
                            {
                                Vector3 p0 = v[ids[a]], p1 = v[ids[b]], p2 = v[ids[c]];
                                float x1 = p1[i] - p0[i], y1 = p1[j] - p0[j], x2 = p2[i] - p0[i], y2 = p2[j] - p0[j], det = x1 * y2 - x2 * y1;
                                if (Mathf.Abs(det) < 1e-6f) continue;
                                Vector2 f1 = uv[ids[b]] - uv[ids[a]], f2 = uv[ids[c]] - uv[ids[a]];
                                var ai = (f1 * y2 - f2 * y1) / det; var aj = (f2 * x1 - f1 * x2) / det;
                                f = new FaceUv { i = i, j = j, ai = ai, aj = aj, c = uv[ids[a]] - ai * p0[i] - aj * p0[j] };
                            }
                    if (f == null) throw new InvalidOperationException("the built-in Cube's face " + grp.Key + " has no UV triangle");
                    foreach (var k in ids)
                        if (Vector2.Distance(f.c + f.ai * v[k][f.i] + f.aj * v[k][f.j], uv[k]) > 1e-4f) throw new InvalidOperationException("the built-in Cube's face " + grp.Key + " UVs are not affine");
                    map[grp.Key] = f;
                }
                if (map.Count != 6) throw new InvalidOperationException("the built-in Cube has " + map.Count + " face directions (expected 6)");
                cubeUv = map;
            }
            return cubeUv[FaceKey(n)];
        }

        class TrayBuild
        {
            public Vector3 size;
            public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public List<Vector2> uv = new List<Vector2>();
            public List<int> t = new List<int>();
            readonly Dictionary<string, int> index = new Dictionary<string, int>();

            int Vert(Vector3 p, Vector3 nn)
            {
                string key = Mathf.RoundToInt(p.x / HoleSnap) + "," + Mathf.RoundToInt(p.y / HoleSnap) + "," + Mathf.RoundToInt(p.z / HoleSnap) + "|" + FaceKey(nn);
                int k;
                if (index.TryGetValue(key, out k)) return k;
                var f = CubeFace(nn);
                var pn = new Vector3(p.x / size.x, p.y / size.y, p.z / size.z);   // the unit cube's coordinates: the cube's UVs over the whole box
                v.Add(p); n.Add(nn); uv.Add(f.c + f.ai * pn[f.i] + f.aj * pn[f.j]);
                index[key] = v.Count - 1;
                return v.Count - 1;
            }

            /// <summary>A quad a-b-c-d (in order around it, either winding) facing <paramref name="normal"/> (Unity: front = Cross(b - a, c - a)).</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f) { var s = b; b = d; d = s; }
                int ia = Vert(a, normal), ib = Vert(b, normal), ic = Vert(c, normal), id = Vert(d, normal);
                t.AddRange(new[] { ia, ib, ic, ia, ic, id });
            }
        }

        /// <summary>A box of <paramref name="size"/> centred on the origin (front = -Z half, the readers' side) whose front face has the
        /// <paramref name="holes"/> (tray-local x / y, inside the front face). The front, back and sides are cut on one grid (every hole edge
        /// runs across the whole box), so faces meet vertex to vertex (no T-junction, no crack). pockets: each hole gets four walls and a floor
        /// at the back plane (facing the readers; the back face behind it faces away), otherwise the hole is open. Flat normals (Scenery Lite
        /// lights per vertex, like the cube), no vertex colours (like the cube).</summary>
        static Mesh TrayMesh(string name, Vector3 size, List<Rect> holes, bool pockets)
        {
            float hx = Snap(size.x * 0.5f), hy = Snap(size.y * 0.5f), zf = -size.z * 0.5f, zb = size.z * 0.5f;
            foreach (var h in holes)
                if (h.xMin < -hx + HoleMarginMin || h.xMax > hx - HoleMarginMin || h.yMin < -hy + HoleMarginMin || h.yMax > hy - HoleMarginMin || h.width <= 0f || h.height <= 0f)
                    throw new InvalidOperationException("BUG-ENV-005: hole " + h.ToString("F4") + " of " + name + " is not inside the tray's front (" + (2f * hx).ToString("F3") + " x " + (2f * hy).ToString("F3") + " m, rim >= " + HoleMarginMin * 1000f + " mm)");
            var xs = new List<float> { -hx, hx }; var ys = new List<float> { -hy, hy };
            foreach (var h in holes) { xs.Add(h.xMin); xs.Add(h.xMax); ys.Add(h.yMin); ys.Add(h.yMax); }
            xs = xs.Distinct().OrderBy(x => x).ToList(); ys = ys.Distinct().OrderBy(y => y).ToList();
            int nx = xs.Count - 1, ny = ys.Count - 1;
            var hole = new bool[nx, ny];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    var c = new Vector2((xs[i] + xs[i + 1]) * 0.5f, (ys[j] + ys[j + 1]) * 0.5f);
                    hole[i, j] = holes.Any(h => h.Contains(c));
                }
            var b = new TrayBuild { size = new Vector3(2f * hx, 2f * hy, size.z) };
            Vector3 back = Vector3.forward, front = Vector3.back;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    float x0 = xs[i], x1 = xs[i + 1], y0 = ys[j], y1 = ys[j + 1];
                    b.Quad(new Vector3(x0, y0, zb), new Vector3(x1, y0, zb), new Vector3(x1, y1, zb), new Vector3(x0, y1, zb), back);
                    if (!hole[i, j]) { b.Quad(new Vector3(x0, y0, zf), new Vector3(x1, y0, zf), new Vector3(x1, y1, zf), new Vector3(x0, y1, zf), front); continue; }
                    if (!pockets) continue;
                    b.Quad(new Vector3(x0, y0, zb), new Vector3(x1, y0, zb), new Vector3(x1, y1, zb), new Vector3(x0, y1, zb), front);   // the pocket's floor
                    // walls on the edges to rim cells, facing into the hole
                    if (i == 0 || !hole[i - 1, j]) b.Quad(new Vector3(x0, y0, zf), new Vector3(x0, y1, zf), new Vector3(x0, y1, zb), new Vector3(x0, y0, zb), Vector3.right);
                    if (i == nx - 1 || !hole[i + 1, j]) b.Quad(new Vector3(x1, y0, zf), new Vector3(x1, y1, zf), new Vector3(x1, y1, zb), new Vector3(x1, y0, zb), Vector3.left);
                    if (j == 0 || !hole[i, j - 1]) b.Quad(new Vector3(x0, y0, zf), new Vector3(x1, y0, zf), new Vector3(x1, y0, zb), new Vector3(x0, y0, zb), Vector3.up);
                    if (j == ny - 1 || !hole[i, j + 1]) b.Quad(new Vector3(x0, y1, zf), new Vector3(x1, y1, zf), new Vector3(x1, y1, zb), new Vector3(x0, y1, zb), Vector3.down);
                }
            // outer sides, cut at the grid lines like the front and back
            for (int i = 0; i < nx; i++)
            {
                float x0 = xs[i], x1 = xs[i + 1];
                b.Quad(new Vector3(x0, -hy, zf), new Vector3(x1, -hy, zf), new Vector3(x1, -hy, zb), new Vector3(x0, -hy, zb), Vector3.down);
                b.Quad(new Vector3(x0, hy, zf), new Vector3(x1, hy, zf), new Vector3(x1, hy, zb), new Vector3(x0, hy, zb), Vector3.up);
            }
            for (int j = 0; j < ny; j++)
            {
                float y0 = ys[j], y1 = ys[j + 1];
                b.Quad(new Vector3(-hx, y0, zf), new Vector3(-hx, y1, zf), new Vector3(-hx, y1, zb), new Vector3(-hx, y0, zb), Vector3.left);
                b.Quad(new Vector3(hx, y0, zf), new Vector3(hx, y1, zf), new Vector3(hx, y1, zb), new Vector3(hx, y0, zb), Vector3.right);
            }
            var m = new Mesh { name = name };
            m.SetVertices(b.v); m.SetNormals(b.n); m.SetUVs(0, b.uv); m.SetTriangles(b.t, 0);
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        /// <summary>Same mesh data within float noise: equal triangles, positions within <paramref name="tol"/> m, normals / UVs within 1e-4.
        /// A board moved by hand re-derives its hole edges through other float paths; the 0.01 mm snap can then flip by one step.</summary>
        static bool MeshClose(Mesh a, Mesh b, float tol)
        {
            if (a == null || b == null || a.subMeshCount != 1 || b.subMeshCount != 1) return false;
            var av = a.vertices; var bv = b.vertices; var an = a.normals; var bn = b.normals; var au = a.uv; var bu = b.uv;
            if (av.Length != bv.Length || an.Length != bn.Length || au.Length != bu.Length || !a.triangles.SequenceEqual(b.triangles)) return false;
            for (int i = 0; i < av.Length; i++) if ((av[i] - bv[i]).sqrMagnitude > tol * tol) return false;
            for (int i = 0; i < an.Length; i++) if ((an[i] - bn[i]).sqrMagnitude > 1e-8f) return false;
            for (int i = 0; i < au.Length; i++) if ((au[i] - bu[i]).sqrMagnitude > 1e-8f) return false;
            return true;
        }
        const float MeshSameTol = 2e-5f, MeshVerifyTol = 5e-5f;

        static string AssetSafe(string s)
        {
            var chars = (s ?? "").Select(ch => (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-' ? ch : '_').ToArray();
            return new string(chars);
        }

        static string BoardMeshPath(string assetName) { return BoardMeshDir + "/" + AssetSafe(assetName) + ".asset"; }

        // ---------------------------------------------------------------- one menu run = one mesh transaction (Codex review 2026-10-15 中2)

        /// <summary>What a run did to a mesh asset, to undo it: created (delete it) or rewritten (put the old data back).</summary>
        class MeshUndo { public string path; public bool created; public Vector3[] v, n; public Vector2[] uv; public int[] t; }
        static List<MeshUndo> meshTx;

        /// <summary>Guide/1 / 20 / 22 and DogBook/2's stand build open one before their first tray. A builder that fails after writing some
        /// trays rolls them all back (MeshTxRollback), so the assets stay in step with the unsaved scene; MeshTxCommit once the scene is saved
        /// (DogBook/2: once the stand is built; that menu never saves).</summary>
        static void MeshTxBegin() { meshTx = new List<MeshUndo>(); }
        static void MeshTxCommit() { meshTx = null; }

        static void MeshTxRollback(List<string> log)
        {
            var tx = meshTx; meshTx = null;
            if (tx == null) return;
            for (int i = tx.Count - 1; i >= 0; i--)
            {
                var u = tx[i];
                try
                {
                    if (u.created) { AssetDatabase.DeleteAsset(u.path); if (log != null) log.Add("rolled back: deleted " + u.path); continue; }
                    var m = AssetDatabase.LoadAssetAtPath<Mesh>(u.path);
                    if (m == null) continue;
                    m.Clear(); m.SetVertices(u.v); m.SetNormals(u.n); m.SetUVs(0, u.uv); m.SetTriangles(u.t, 0); m.RecalculateBounds(); m.RecalculateTangents();
                    EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
                    if (log != null) log.Add("rolled back: restored " + u.path);
                }
                catch (Exception e) { Debug.LogError("[KH Guide Boards] mesh rollback failed for " + u.path + ": " + e.Message); }
            }
        }

        /// <summary>Writes <paramref name="built"/> to Meshes/Boards/&lt;assetName&gt;.asset: created the first time; afterwards the existing asset is
        /// rewritten in place (same GUID, the scene's reference stays) and only when its data differ beyond float noise. Recorded in the open
        /// mesh transaction (MeshTxBegin) so a failed run can undo it. Returns the asset.</summary>
        static Mesh SaveBoardMesh(Mesh built, string assetName, List<string> log)
        {
            if (!AssetDatabase.IsValidFolder(BoardMeshParent)) AssetDatabase.CreateFolder("Assets/KominkaHillside", "Meshes");
            if (!AssetDatabase.IsValidFolder(BoardMeshDir)) AssetDatabase.CreateFolder(BoardMeshParent, "Boards");
            string path = BoardMeshPath(assetName);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new InvalidOperationException(path + " exists and is not a Mesh; nothing written (reopen the scene without saving)");
                built.name = assetName;
                AssetDatabase.CreateAsset(built, path);
                if (meshTx != null) meshTx.Add(new MeshUndo { path = path, created = true });
                if (log != null) log.Add("created " + path + " (" + built.vertexCount + " vertices, " + built.triangles.Length / 3 + " triangles)");
                return built;
            }
            if (MeshClose(existing, built, MeshSameTol)) { UnityEngine.Object.DestroyImmediate(built); return existing; }
            if (meshTx != null) meshTx.Add(new MeshUndo { path = path, v = existing.vertices, n = existing.normals, uv = existing.uv, t = existing.triangles });
            existing.Clear();
            existing.SetVertices(built.vertices); existing.SetNormals(built.normals); existing.SetUVs(0, built.uv); existing.SetTriangles(built.triangles, 0);
            existing.RecalculateBounds(); existing.RecalculateTangents();
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
            if (log != null) log.Add("rewrote " + path + " (" + existing.vertexCount + " vertices, " + existing.triangles.Length / 3 + " triangles)");
            UnityEngine.Object.DestroyImmediate(built);
            return existing;
        }

        static List<Rect> TrayLocalHoles(List<Rect> holes, Vector3 centre)
        {
            return holes.Select(h => Rect.MinMaxRect(Snap(h.xMin - centre.x), Snap(h.yMin - centre.y), Snap(h.xMax - centre.x), Snap(h.yMax - centre.y))).ToList();
        }

        // ---------------------------------------------------------------- the trays' boxes (one source for the builders and the Verify menus)

        /// <summary>The Back's mesh asset name of a board built by BuildStructure (meshName: KH_GuideBoards_&lt;board&gt;, the item board's name, KH_DogBookStand).</summary>
        static string BoardTrayAsset(string meshName) { return meshName + "_Back"; }

        /// <summary>A board's Back: from the canvas plane (TrayFront) to the old back face (TrayBack), BackW x BackH, centred on the canvas.</summary>
        static void BoardTrayBox(Board b, out Vector3 centre, out Vector3 size)
        {
            centre = new Vector3(0f, CanvasCenterY(b), (TrayFront + TrayBack) * 0.5f);
            size = new Vector3(BackW(b), BackH(b), TrayBack - TrayFront);
        }

        /// <summary>Verify (Codex review 2026-10-15 中1): <paramref name="frame"/>'s Structure/Back is exactly the tray the builder makes now:
        /// Structure at identity, Back at <paramref name="centre"/> / identity / scale 1, two-sided shadows, its mesh the asset
        /// Meshes/Boards/&lt;assetName&gt;.asset, and that mesh equal (within float noise) to one regenerated in memory from the canvases' current
        /// graphics by the build's own code (CanvasHoles: every canvas in the front plane, every drawn Graphic inside the holes; TrayMesh).
        /// Never writes anything; why = the first difference (or what matched).</summary>
        static bool TrayMatches(Transform frame, IEnumerable<RectTransform> canvases, IEnumerable<Rect> extra, float frontZ, Vector3 centre, Vector3 size, bool pockets, string assetName, out string why)
        {
            var s = frame != null ? frame.Find(StructureName) : null;
            var back = s != null ? s.Find("Back") : null;
            if (back == null) { why = "no " + StructureName + "/Back"; return false; }
            if (s.localPosition != Vector3.zero || s.localRotation != Quaternion.identity || s.localScale != Vector3.one) { why = StructureName + " is not at the board's origin / identity / scale 1"; return false; }
            if ((back.localPosition - centre).sqrMagnitude > 1e-10f || Quaternion.Angle(back.localRotation, Quaternion.identity) > 1e-3f || back.localScale != Vector3.one)
            { why = "Back at " + back.localPosition.ToString("F4") + " rot " + back.localEulerAngles.ToString("F2") + " scale " + back.localScale.ToString("F3") + " (expected " + centre.ToString("F4") + ", identity, 1)"; return false; }
            var mf = back.GetComponent<MeshFilter>(); var mr = back.GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null || mr == null) { why = "Back has no mesh / renderer"; return false; }
            if (mr.shadowCastingMode != ShadowCastingMode.TwoSided) { why = "Back shadows " + mr.shadowCastingMode + " (expected TwoSided)"; return false; }
            string want = BoardMeshPath(assetName), have = AssetDatabase.GetAssetPath(mf.sharedMesh);
            if (have != want) { why = "Back's mesh is " + (string.IsNullOrEmpty(have) ? mf.sharedMesh.name + " (not an asset)" : have) + " (expected " + want + ")"; return false; }
            Mesh expected = null;
            try
            {
                var holes = CanvasHoles(frame, frontZ, canvases, extra, null, new List<string>());
                expected = TrayMesh(AssetSafe(assetName), size, TrayLocalHoles(holes, centre), pockets);
                if (!MeshClose(mf.sharedMesh, expected, MeshVerifyTol))
                { why = "Back's mesh (" + mf.sharedMesh.vertexCount + " vertices) differs from the tray the canvas needs now (" + expected.vertexCount + " vertices): the papers moved or changed"; return false; }
            }
            catch (Exception e) { why = e.Message; return false; }
            finally { if (expected != null) UnityEngine.Object.DestroyImmediate(expected); }
            why = want + ", " + mf.sharedMesh.vertexCount + " vertices, matches the canvas's holes";
            return true;
        }

        /// <summary>The wood back as a tray: <paramref name="centre"/> / <paramref name="size"/> in the parent's space (the parent is the board's
        /// identity Structure, so parent-local = board-local), <paramref name="holes"/> in the same space. Same renderer settings as Wood()
        /// (KH_RiverWood, no collider, Batching Static, light probes) except shadows: two-sided, so an open hole never lets the light through.</summary>
        static void Tray(Transform parent, string name, Vector3 centre, Vector3 size, List<Rect> holes, bool pockets, string assetName, Ctx c, HashSet<Transform> keep, List<string> log)
        {
            assetName = AssetSafe(assetName);
            var mesh = SaveBoardMesh(TrayMesh(assetName, size, TrayLocalHoles(holes, centre), pockets), assetName, log);
            var t = parent.Find(name);
            GameObject go;
            if (t == null) { go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false); }
            else go = t.gameObject;
            foreach (var col in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);   // no walking / interact / dog obstacle
            go.layer = 0;
            go.transform.localPosition = centre; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var mf = go.GetComponent<MeshFilter>(); if (mf == null) mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh; EditorUtility.SetDirty(mf);
            var mr = go.GetComponent<MeshRenderer>(); if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = c.wood;
            mr.shadowCastingMode = ShadowCastingMode.TwoSided; mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.BlendProbes; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            EditorUtility.SetDirty(mr);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            keep.Add(go.transform);
        }
    }
}
