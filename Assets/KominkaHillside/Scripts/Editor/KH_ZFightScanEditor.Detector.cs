using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KominkaHillside.Editor
{
    // BUG-ENV-003 / BUG-DOG-013: shared, read-only coplanar-overlap detector and capture helpers for the ZFight menus.
    // Nothing here edits a scene, prefab or asset. Every failure is logged and skipped (callers never throw).
    //
    // Detector: world-space triangles from MeshFilter.sharedMesh x matrix; a uniform 3D grid over the triangles' AABBs
    // (expanded by the "near" gap tolerance), each candidate pair tested once (in the cell holding the min corner of the
    // two AABBs' intersection). A pair is kept when |dot(nA, nB)| > NormalDot, the overlap of B projected onto A's plane
    // (Sutherland-Hodgman, shoelace) exceeds minAreaM2, and the gap along nA between the planes, measured at the overlap
    // polygon's vertices (max), is <= NearGap. Tier "coplanar" = max gap <= CoplanarGap, "near" = up to NearGap.
    // Same-renderer pairs that share exactly two vertex positions (edge neighbours) are skipped; duplicates (3 shared)
    // and disconnected regions are kept. Opposite-facing pairs are "back-to-back" (culled away unless a material is Cull Off).
    public static class KH_ZFightDetector
    {
        public const float NormalDot = 0.999f, CoplanarGap = 0.003f, NearGap = 0.020f;
        public const int MaxCellsPerTri = 20000, AcceptCapPerPair = 50000;
        public const long CandidateCapPerPair = 4000000;

        // ---------------------------------------------------------------- input / output types

        public class Input
        {
            public string path; public Mesh mesh; public Matrix4x4 toWorld; public Material[] materials;
            public bool textUi, stepLike, inactive; public Bounds? clip;   // clip: keep only triangles whose AABB meets it
        }

        [Serializable] public class MaterialInfo
        { public string name, shader; public int renderQueue; public bool transparent, doubleSided; public string zWrite; }

        [Serializable] public class PartInfo
        { public int index; public string path; public int triangles, submeshes; public bool readable, textUi, stepLike, inactive; public string note; public List<MaterialInfo> materials = new List<MaterialInfo>(); }

        [Serializable] public class Group
        {
            public int rank; public string tier, facing, partA, partB, materialA, materialB, relation, flags = "", notes = "";
            public int subA, subB, partIndexA, partIndexB, pairs;
            public float areaCm2, gapMaxMm, gapMeanMm, distance, score;
            public Vector3 centre, boundsMin, boundsMax, normal;
            public bool capped, transparentInvolved, textUiInvolved;
        }

        public class Result
        {
            public List<PartInfo> parts = new List<PartInfo>();
            public List<Group> groups = new List<Group>();        // same-facing (or double-sided) coplanar + near
            public List<Group> backToBack = new List<Group>();    // opposite-facing, both single-sided
            public List<string> notes = new List<string>();
            public int triangles, oversize; public long candidates, accepted; public double seconds; public bool truncated;
            public bool incomplete;   // truncated, or a candidate / accepted cap was hit (some overlaps were not examined)
        }

        struct Tri
        {
            public int part, sub, region, wa, wb, wc;
            public Vector3 a, b, c, n, min, max;
            public float area;
        }

        class Contribution { public Vector3 centre, min, max, normal; public float area, gapMax, gapSum; public int pairs; }

        class PairAcc
        {
            public int partA, subA, partB, subB; public string tier, facing, relation;
            public List<Contribution> items = new List<Contribution>();
            public long candidates; public bool capped;
        }

        // ---------------------------------------------------------------- mesh reading

        /// <summary>Reads positions and per-submesh triangle indices; tries the managed API, then AcquireReadOnlyMeshData. Never throws.</summary>
        public static bool ReadMesh(Mesh mesh, out Vector3[] vertices, out List<int[]> subs, out string note)
        {
            vertices = null; subs = new List<int[]>(); note = "";
            if (mesh == null) { note = "no mesh"; return false; }
            try
            {
                vertices = mesh.vertices;
                if (vertices == null || (vertices.Length == 0 && mesh.vertexCount > 0)) throw new InvalidOperationException("empty vertices");
                for (int s = 0; s < mesh.subMeshCount; s++)
                    subs.Add(mesh.GetTopology(s) == MeshTopology.Triangles ? mesh.GetTriangles(s) : new int[0]);
                return true;
            }
            catch (Exception e) { note = "managed read failed (" + e.Message + ")"; }
            try
            {
                using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    var d = data[0]; subs.Clear();
                    using (var v = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp)) { d.GetVertices(v); vertices = v.ToArray(); }
                    for (int s = 0; s < d.subMeshCount; s++)
                    {
                        var sm = d.GetSubMesh(s);
                        if (sm.topology != MeshTopology.Triangles) { subs.Add(new int[0]); continue; }
                        using (var idx = new NativeArray<int>(sm.indexCount, Allocator.Temp)) { d.GetIndices(idx, s); subs.Add(idx.ToArray()); }
                    }
                }
                note += "; read via AcquireReadOnlyMeshData";
                return vertices != null && vertices.Length > 0;
            }
            catch (Exception e) { note += "; AcquireReadOnlyMeshData failed (" + e.Message + ")"; vertices = null; subs.Clear(); return false; }
        }

        public static MaterialInfo Describe(Material m)
        {
            var info = new MaterialInfo { name = m != null ? m.name : "(none)", shader = m != null && m.shader != null ? m.shader.name : "(none)", zWrite = "n/a" };
            if (m == null) return info;
            info.renderQueue = m.renderQueue;
            info.transparent = m.renderQueue > 2500 || string.Equals(m.GetTag("RenderType", false), "Transparent", StringComparison.OrdinalIgnoreCase);
            foreach (var p in new[] { "_Cull", "_CullMode" })
                if (m.HasProperty(p)) { info.doubleSided = Mathf.RoundToInt(m.GetFloat(p)) == 0; break; }
            if (m.HasProperty("_ZWrite")) info.zWrite = m.GetFloat("_ZWrite").ToString("0");
            return info;
        }

        public static string LeafPath(Transform t, Transform stopAt)
        {
            var names = new List<string>();
            for (var x = t; x != null; x = x.parent) { names.Add(x.name); if (x == stopAt) break; }
            names.Reverse(); return string.Join("/", names);
        }

        // ---------------------------------------------------------------- detection

        static float sCoplanar = CoplanarGap, sNear = NearGap, sMinWidth = 0f;

        /// <summary>coplanarGap / nearGap default to the farmhouse tiers (3 / 20 mm); the tools pass tighter ones. minWidth (BUG-ENV-005, ZFight/7):
        /// an overlap piece whose mean width 2 x area / perimeter is below it is a float sliver along a shared edge (two faces meeting edge to
        /// edge through different transforms), not an overlap; 0 = off (the other menus).</summary>
        public static Result Run(List<Input> inputs, float cellSize, float minAreaM2, float clusterLink, double budgetSeconds, string progressTitle, float coplanarGap = CoplanarGap, float nearGap = NearGap, float minWidth = 0f)
        {
            sCoplanar = coplanarGap; sNear = nearGap; sMinWidth = minWidth;
            var r = new Result(); var watch = Stopwatch.StartNew();
            var tris = new List<Tri>(); var partBounds = new List<Bounds>(); var partMats = new List<List<MaterialInfo>>();
            try
            {
                // 1. world triangles, welded ids and regions per part
                for (int p = 0; p < inputs.Count; p++)
                {
                    var inp = inputs[p];
                    var info = new PartInfo { index = p, path = inp.path, textUi = inp.textUi, stepLike = inp.stepLike, inactive = inp.inactive };
                    r.parts.Add(info); partMats.Add(info.materials);
                    Vector3[] v; List<int[]> subs; string note;
                    info.readable = ReadMesh(inp.mesh, out v, out subs, out note); info.note = note;
                    info.submeshes = subs.Count;
                    for (int s = 0; s < Math.Max(subs.Count, 1); s++)
                        info.materials.Add(Describe(inp.materials != null && inp.materials.Length > 0 ? inp.materials[Math.Min(s, inp.materials.Length - 1)] : null));
                    var pb = new Bounds(); bool hasBounds = false;
                    if (!info.readable) { partBounds.Add(pb); r.notes.Add("unreadable mesh skipped: " + inp.path + " " + note); Debug.LogWarning("[KH ZFight] unreadable " + inp.path + " " + note); continue; }
                    bool flip = inp.toWorld.determinant < 0f;
                    var wv = new Vector3[v.Length]; for (int i = 0; i < v.Length; i++) wv[i] = inp.toWorld.MultiplyPoint3x4(v[i]);
                    var weld = new int[v.Length]; var weldMap = new Dictionary<Vector3Int, int>();
                    for (int i = 0; i < v.Length; i++)
                    {
                        var key = new Vector3Int(Mathf.RoundToInt(v[i].x * 1e5f), Mathf.RoundToInt(v[i].y * 1e5f), Mathf.RoundToInt(v[i].z * 1e5f));
                        int id; if (!weldMap.TryGetValue(key, out id)) { id = weldMap.Count; weldMap[key] = id; } weld[i] = id;
                    }
                    var uf = new int[weldMap.Count]; for (int i = 0; i < uf.Length; i++) uf[i] = i;
                    foreach (var idx in subs) for (int t = 0; t + 2 < idx.Length; t += 3) { Union(uf, weld[idx[t]], weld[idx[t + 1]]); Union(uf, weld[idx[t]], weld[idx[t + 2]]); }
                    for (int s = 0; s < subs.Count; s++)
                    {
                        var idx = subs[s];
                        for (int t = 0; t + 2 < idx.Length; t += 3)
                        {
                            int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                            if (i0 >= wv.Length || i1 >= wv.Length || i2 >= wv.Length) continue;
                            Vector3 a = wv[i0], b = wv[i1], c = wv[i2];
                            var cr = Vector3.Cross(b - a, c - a); float len = cr.magnitude; if (len < 1e-10f) continue;
                            var tr = new Tri { part = p, sub = s, a = a, b = b, c = c, n = (flip ? -cr : cr) / len, area = len * 0.5f, wa = weld[i0], wb = weld[i1], wc = weld[i2], region = Find(uf, weld[i0]) };
                            tr.min = Vector3.Min(a, Vector3.Min(b, c)); tr.max = Vector3.Max(a, Vector3.Max(b, c));
                            if (inp.clip.HasValue && !inp.clip.Value.Intersects(new Bounds((tr.min + tr.max) * .5f, tr.max - tr.min))) continue;
                            tris.Add(tr); info.triangles++;
                            if (!hasBounds) { pb = new Bounds(tr.min, Vector3.zero); hasBounds = true; }
                            pb.Encapsulate(tr.min); pb.Encapsulate(tr.max);
                        }
                    }
                    pb.Expand(Mathf.Max(0.01f, 2f * sNear)); partBounds.Add(pb);   // so cross-renderer near-tier pairs are not dropped by the bounds prefilter
                }
                r.triangles = tris.Count;
                bool usePartMatrix = inputs.Count <= 4000;
                var partMeet = usePartMatrix ? new bool[inputs.Count, inputs.Count] : null;
                if (usePartMatrix)
                    for (int i = 0; i < inputs.Count; i++) for (int j = i; j < inputs.Count; j++)
                        { bool m = r.parts[i].readable && r.parts[j].readable && partBounds[i].Intersects(partBounds[j]); partMeet[i, j] = m; partMeet[j, i] = m; }

                // 2. grid
                float inv = 1f / Mathf.Max(cellSize, 1e-4f);
                var grid = new Dictionary<long, List<int>>(); var oversize = new List<int>();
                for (int i = 0; i < tris.Count; i++)
                {
                    Vector3Int lo = Cell(tris[i].min - Vector3.one * sNear, inv), hi = Cell(tris[i].max + Vector3.one * sNear, inv);
                    long count = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1) * (hi.z - lo.z + 1);
                    if (count > MaxCellsPerTri) { oversize.Add(i); continue; }
                    for (int x = lo.x; x <= hi.x; x++) for (int y = lo.y; y <= hi.y; y++) for (int z = lo.z; z <= hi.z; z++)
                    { long k = Pack(x, y, z); List<int> l; if (!grid.TryGetValue(k, out l)) grid[k] = l = new List<int>(); l.Add(i); }
                }
                r.oversize = oversize.Count;
                if (oversize.Count > 0) r.notes.Add(oversize.Count + " triangles spanned > " + MaxCellsPerTri + " cells; tested against every triangle by brute force");

                var acc = new Dictionary<string, PairAcc>(); var pairCounts = new Dictionary<long, long>(); var cappedPairs = new HashSet<long>();
                var isOver = new bool[tris.Count]; foreach (int o in oversize) isOver[o] = true;
                var ctx = new Clip();
                int done = 0, total = grid.Count;
                foreach (var kv in grid)
                {
                    // time is checked on every cell (and every 16 rows inside a dense cell); the cancelable bar every 256 cells
                    if (watch.Elapsed.TotalSeconds > budgetSeconds) { r.truncated = true; r.notes.Add("time budget " + budgetSeconds + " s reached at cell " + (done + 1) + "/" + total + "; partial result"); break; }
                    if ((++done & 255) == 0 && EditorUtility.DisplayCancelableProgressBar(progressTitle, "cells " + done + "/" + total + ", accepted " + r.accepted, (float)done / total))
                    { r.truncated = true; r.notes.Add("cancelled at cell " + done + "/" + total + "; partial result"); break; }
                    var list = kv.Value; long cellKey = kv.Key;
                    for (int x = 0; x < list.Count && !r.truncated; x++)
                    {
                        if ((x & 15) == 15 && watch.Elapsed.TotalSeconds > budgetSeconds)
                        { r.truncated = true; r.notes.Add("time budget " + budgetSeconds + " s reached inside cell " + done + "/" + total + " (" + list.Count + " triangles); partial result"); break; }
                        for (int y = x + 1; y < list.Count; y++)
                            TestPair(tris, list[x], list[y], cellKey, inv, partMeet, minAreaM2, acc, pairCounts, cappedPairs, ctx, r);
                    }
                    if (r.truncated) break;
                }
                if (!r.truncated)
                    for (int o = 0; o < oversize.Count; o++)
                    {
                        if (watch.Elapsed.TotalSeconds > budgetSeconds) { r.truncated = true; r.notes.Add("time budget reached in oversize pass"); break; }
                        if (EditorUtility.DisplayCancelableProgressBar(progressTitle, "large triangles " + o + "/" + oversize.Count, (float)o / Math.Max(oversize.Count, 1)))
                        { r.truncated = true; r.notes.Add("cancelled in oversize pass; partial result"); break; }
                        for (int j = 0; j < tris.Count; j++)
                        {
                            if (j == oversize[o]) continue;
                            if (isOver[j] && j < oversize[o]) continue;
                            TestPair(tris, oversize[o], j, long.MinValue, inv, partMeet, minAreaM2, acc, pairCounts, cappedPairs, ctx, r);
                        }
                    }

                // 3. cluster each renderer/submesh pair into spatial groups
                foreach (var pa in acc.Values) if (cappedPairs.Contains(PairKey(pa.partA, pa.partB))) pa.capped = true;
                if (cappedPairs.Count > 0) r.notes.Add(cappedPairs.Count + " renderer pairs hit the candidate cap (" + CandidateCapPerPair + "); their groups are marked capped");
                int acceptCapped = 0; bool anyCapped = false;
                foreach (var pa in acc.Values) { if (pa.items.Count >= AcceptCapPerPair) acceptCapped++; if (pa.capped) anyCapped = true; }
                if (acceptCapped > 0) r.notes.Add(acceptCapped + " submesh pairs hit the accepted-overlap cap (" + AcceptCapPerPair + "); their groups are marked capped");
                // capped = some overlaps were not examined: the result is incomplete even though it was not truncated by time
                r.incomplete = cappedPairs.Count > 0 || anyCapped;
                foreach (var pa in acc.Values) foreach (var g in ClusterGroups(pa, clusterLink, r.parts, partMats)) (g.facing == "opposite" && !DoubleSided(partMats, g) ? r.backToBack : r.groups).Add(g);
            }
            catch (Exception e) { r.truncated = true; r.notes.Add("detector exception: " + e.GetType().Name + " " + e.Message); Debug.LogException(e); }
            finally { EditorUtility.ClearProgressBar(); }
            r.seconds = watch.Elapsed.TotalSeconds; r.incomplete |= r.truncated;
            return r;
        }

        static bool DoubleSided(List<List<MaterialInfo>> mats, Group g)
        { return Mat(mats, g.partIndexA, g.subA).doubleSided || Mat(mats, g.partIndexB, g.subB).doubleSided; }

        static MaterialInfo Mat(List<List<MaterialInfo>> mats, int part, int sub)
        {
            if (part < 0 || part >= mats.Count || mats[part].Count == 0) return new MaterialInfo { name = "(none)" };
            return mats[part][Math.Min(sub, mats[part].Count - 1)];
        }

        static void TestPair(List<Tri> tris, int i, int j, long cellKey, float inv, bool[,] partMeet, float minArea,
            Dictionary<string, PairAcc> acc, Dictionary<long, long> pairCounts, HashSet<long> cappedPairs, Clip ctx, Result r)
        {
            Tri A = tris[i], B = tris[j];
            // AABB (expanded) and dedupe by reference cell
            float e = sNear;
            if (A.min.x > B.max.x + e || B.min.x > A.max.x + e || A.min.y > B.max.y + e || B.min.y > A.max.y + e || A.min.z > B.max.z + e || B.min.z > A.max.z + e) return;
            if (cellKey != long.MinValue)
            {
                var m = Vector3.Max(A.min, B.min) - Vector3.one * e; var c = Cell(m, inv);
                if (Pack(c.x, c.y, c.z) != cellKey) return;
            }
            float dot = Vector3.Dot(A.n, B.n); if (Mathf.Abs(dot) < NormalDot) return;
            if (partMeet != null && !partMeet[A.part, B.part]) return;
            // B entirely beyond the near tolerance on one side of A's plane
            float d0 = Vector3.Dot(A.n, B.a - A.a), d1 = Vector3.Dot(A.n, B.b - A.a), d2 = Vector3.Dot(A.n, B.c - A.a);
            if ((d0 > e && d1 > e && d2 > e) || (d0 < -e && d1 < -e && d2 < -e)) return;
            if (A.part == B.part)
            {
                int shared = (Same(A.wa, B) ? 1 : 0) + (Same(A.wb, B) ? 1 : 0) + (Same(A.wc, B) ? 1 : 0);
                if (shared == 2) return;   // edge neighbours
            }
            long pk = PairKey(A.part, B.part);
            long seen; pairCounts.TryGetValue(pk, out seen); pairCounts[pk] = seen + 1; r.candidates++;
            if (seen + 1 > CandidateCapPerPair) { cappedPairs.Add(pk); return; }

            // overlap of B projected onto A's plane
            Vector3 u = (A.b - A.a).normalized, v = Vector3.Cross(A.n, u);
            ctx.Set(A.a, u, v);
            float area = ctx.Overlap(A.a, A.b, A.c, B.a, B.b, B.c);
            if (area < minArea) return;
            if (sMinWidth > 0f)
            {
                float per = 0f; for (int k = 0; k < ctx.count; k++) per += Vector2.Distance(ctx.poly[k], ctx.poly[(k + 1) % ctx.count]);
                if (per <= 0f || 2f * area / per < sMinWidth) return;
            }
            float gMax = 0f, gSum = 0f; float nn = Vector3.Dot(B.n, A.n); if (Mathf.Abs(nn) < 1e-4f) return;
            Vector3 centre = Vector3.zero;
            for (int k = 0; k < ctx.count; k++)
            {
                Vector3 p = A.a + u * ctx.poly[k].x + v * ctx.poly[k].y; centre += p;
                float gap = Mathf.Abs(Vector3.Dot(B.n, B.a - p) / nn); gMax = Mathf.Max(gMax, gap);
            }
            centre /= ctx.count;
            float gMid = Mathf.Abs(Vector3.Dot(B.n, B.a - centre) / nn); gSum = gMid;
            if (gMax > sNear) return;
            string tier = gMax <= sCoplanar ? "coplanar" : "near";
            string facing = dot > 0 ? "same" : "opposite";
            bool aFirst = A.part < B.part || (A.part == B.part && A.sub <= B.sub);
            Tri P = aFirst ? A : B, Q = aFirst ? B : A;
            string relation = P.part != Q.part ? "renderers" : P.sub != Q.sub ? "submeshes" : P.region != Q.region ? "regions" : "same-region";
            if (relation == "same-region" && (Same(P.wa, Q) && Same(P.wb, Q) && Same(P.wc, Q))) relation = "duplicate-face";
            string key = P.part + ":" + P.sub + "|" + Q.part + ":" + Q.sub + "|" + tier + "|" + facing + "|" + relation;
            PairAcc pa; if (!acc.TryGetValue(key, out pa)) acc[key] = pa = new PairAcc { partA = P.part, subA = P.sub, partB = Q.part, subB = Q.sub, tier = tier, facing = facing, relation = relation };
            if (pa.items.Count >= AcceptCapPerPair) { pa.capped = true; return; }
            pa.items.Add(new Contribution { centre = centre, min = Vector3.Min(A.min, B.min), max = Vector3.Max(A.max, B.max), area = area, gapMax = gMax, gapSum = gSum, pairs = 1, normal = A.n * area });
            r.accepted++;
        }

        static long PairKey(int a, int b) { return ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b); }

        static bool Same(int w, Tri t) { return w == t.wa || w == t.wb || w == t.wc; }

        static List<Group> ClusterGroups(PairAcc pa, float link, List<PartInfo> parts, List<List<MaterialInfo>> mats)
        {
            var items = pa.items; var uf = new int[items.Count]; for (int i = 0; i < uf.Length; i++) uf[i] = i;
            float inv = 1f / Mathf.Max(link, 1e-4f); var cells = new Dictionary<long, int>();
            for (int i = 0; i < items.Count; i++)
            {
                var c = Cell(items[i].centre, inv); long k = Pack(c.x, c.y, c.z); int rep;
                if (cells.TryGetValue(k, out rep)) Union(uf, rep, i); else cells[k] = i;
            }
            foreach (var kv in cells)
            {
                var c = Cell(items[kv.Value].centre, inv);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                { int other; if (cells.TryGetValue(Pack(c.x + dx, c.y + dy, c.z + dz), out other)) Union(uf, kv.Value, other); }
            }
            var byRoot = new Dictionary<int, Group>(); var weighted = new Dictionary<int, Vector3>();
            var ma = Mat(mats, pa.partA, pa.subA); var mb = Mat(mats, pa.partB, pa.subB);
            for (int i = 0; i < items.Count; i++)
            {
                int root = Find(uf, i); Group g; var it = items[i];
                if (!byRoot.TryGetValue(root, out g))
                {
                    g = new Group { tier = pa.tier, facing = pa.facing, relation = pa.relation, partIndexA = pa.partA, partIndexB = pa.partB, subA = pa.subA, subB = pa.subB,
                        partA = parts[pa.partA].path, partB = parts[pa.partB].path, materialA = ma.name, materialB = mb.name, boundsMin = it.min, boundsMax = it.max, capped = pa.capped,
                        transparentInvolved = ma.transparent || mb.transparent, textUiInvolved = parts[pa.partA].textUi || parts[pa.partB].textUi };
                    byRoot[root] = g; weighted[root] = Vector3.zero;
                }
                g.areaCm2 += it.area * 1e4f; g.pairs += it.pairs; g.gapMaxMm = Mathf.Max(g.gapMaxMm, it.gapMax * 1000f); g.gapMeanMm += it.gapSum * 1000f * it.area * 1e4f;
                weighted[root] += it.centre * it.area; g.normal += it.normal;
                g.boundsMin = Vector3.Min(g.boundsMin, it.min); g.boundsMax = Vector3.Max(g.boundsMax, it.max);
            }
            var outList = new List<Group>();
            foreach (var kv in byRoot)
            {
                var g = kv.Value; float areaM2 = g.areaCm2 * 1e-4f;
                g.centre = areaM2 > 0 ? weighted[kv.Key] / areaM2 : (g.boundsMin + g.boundsMax) * .5f;
                g.gapMeanMm = g.areaCm2 > 0 ? g.gapMeanMm / g.areaCm2 : 0f; g.normal = g.normal.normalized;
                var notes = new List<string>();
                if (g.capped) notes.Add("capped");
                if (g.transparentInvolved) notes.Add(ma.transparent && mb.transparent ? "both transparent" : "transparent vs opaque");
                if (g.textUiInvolved) notes.Add("text/UI (likely intended offset)");
                if (ma.doubleSided || mb.doubleSided) notes.Add("Cull Off material");
                if (parts[pa.partA].inactive || parts[pa.partB].inactive) notes.Add("inactive renderer");
                g.notes = string.Join("; ", notes);
                outList.Add(g);
            }
            return outList;
        }

        // ---------------------------------------------------------------- terrain complement

        [Serializable] public class TerrainRow
        { public string part, terrain; public float upAreaCm2, coplanarCm2, nearCm2, buriedCm2, minGapMm, maxGapMm; public Vector3 centre; }

        /// <summary>Upward faces (n.y > 0.9) of each part vs the Terrain under them: (face height - terrain height) at centroid and vertices.</summary>
        public static List<TerrainRow> TerrainContact(List<Input> inputs, Terrain[] terrains)
        {
            var rows = new List<TerrainRow>();
            if (terrains == null || terrains.Length == 0) return rows;
            foreach (var inp in inputs)
            {
                try
                {
                    Vector3[] v; List<int[]> subs; string note;
                    if (!ReadMesh(inp.mesh, out v, out subs, out note)) continue;
                    bool flip = inp.toWorld.determinant < 0f;
                    var row = new TerrainRow { part = inp.path, minGapMm = float.MaxValue, maxGapMm = float.MinValue }; Vector3 wc = Vector3.zero; float wsum = 0;
                    foreach (var idx in subs)
                        for (int t = 0; t + 2 < idx.Length; t += 3)
                        {
                            if (idx[t] >= v.Length || idx[t + 1] >= v.Length || idx[t + 2] >= v.Length) continue;
                            Vector3 a = inp.toWorld.MultiplyPoint3x4(v[idx[t]]), b = inp.toWorld.MultiplyPoint3x4(v[idx[t + 1]]), c = inp.toWorld.MultiplyPoint3x4(v[idx[t + 2]]);
                            var cr = Vector3.Cross(b - a, c - a); float len = cr.magnitude; if (len < 1e-10f) continue;
                            var n = (flip ? -cr : cr) / len; if (n.y < 0.9f) continue;
                            if (inp.clip.HasValue && !inp.clip.Value.Contains((a + b + c) / 3f)) continue;
                            float area = len * .5f; float gMin = float.MaxValue, gMax = float.MinValue; Terrain hit = null;
                            foreach (var p in new[] { (a + b + c) / 3f, a, b, c })
                            {
                                var ter = TerrainAt(terrains, p); if (ter == null) { gMin = float.MaxValue; break; }
                                hit = ter; float g = p.y - (ter.SampleHeight(p) + ter.transform.position.y);
                                gMin = Mathf.Min(gMin, g); gMax = Mathf.Max(gMax, g);
                            }
                            if (hit == null || gMin == float.MaxValue) continue;
                            row.terrain = hit.name; row.upAreaCm2 += area * 1e4f;
                            if (gMax < -CoplanarGap) row.buriedCm2 += area * 1e4f;
                            else if (Mathf.Abs(gMin) <= CoplanarGap && Mathf.Abs(gMax) <= CoplanarGap) row.coplanarCm2 += area * 1e4f;
                            else if (Mathf.Abs(gMin) <= NearGap || Mathf.Abs(gMax) <= NearGap || (gMin < 0 && gMax > 0)) row.nearCm2 += area * 1e4f;
                            row.minGapMm = Mathf.Min(row.minGapMm, gMin * 1000f); row.maxGapMm = Mathf.Max(row.maxGapMm, gMax * 1000f);
                            if (Mathf.Abs(gMin) <= NearGap || (gMin < 0 && gMax > 0)) { wc += (a + b + c) / 3f * area; wsum += area; }
                        }
                    if (row.upAreaCm2 <= 0) continue;
                    row.centre = wsum > 0 ? wc / wsum : Vector3.zero;
                    rows.Add(row);
                }
                catch (Exception e) { Debug.LogWarning("[KH ZFight] terrain contact failed for " + inp.path + ": " + e.Message); }
            }
            return rows;
        }

        public static Terrain TerrainAt(Terrain[] terrains, Vector3 p)
        {
            foreach (var t in terrains)
            {
                if (t == null || t.terrainData == null) continue;
                var o = t.transform.position; var s = t.terrainData.size;
                if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return t;
            }
            return null;
        }

        // ---------------------------------------------------------------- 2D clipping

        class Clip
        {
            public Vector2[] poly = new Vector2[16], tmp = new Vector2[16]; public int count;
            Vector3 o, u, v;
            public void Set(Vector3 origin, Vector3 uu, Vector3 vv) { o = origin; u = uu; v = vv; }
            Vector2 P(Vector3 p) { var d = p - o; return new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v)); }

            /// <summary>Area of (B projected on A's plane) clipped by A. Leaves the polygon in poly[0..count).</summary>
            public float Overlap(Vector3 a, Vector3 b, Vector3 c, Vector3 pa, Vector3 pb, Vector3 pc)
            {
                Vector2 A0 = P(a), A1 = P(b), A2 = P(c);
                if (Cross(A1 - A0, A2 - A0) < 0) { var t = A1; A1 = A2; A2 = t; }
                poly[0] = P(pa); poly[1] = P(pb); poly[2] = P(pc); count = 3;
                ClipEdge(A0, A1); if (count < 3) return 0f;
                ClipEdge(A1, A2); if (count < 3) return 0f;
                ClipEdge(A2, A0); if (count < 3) return 0f;
                float s = 0f; for (int i = 0; i < count; i++) { var p = poly[i]; var q = poly[(i + 1) % count]; s += p.x * q.y - q.x * p.y; }
                return Mathf.Abs(s) * .5f;
            }
            void ClipEdge(Vector2 e0, Vector2 e1)
            {
                int n = 0; var edge = e1 - e0;
                for (int i = 0; i < count && n < 14; i++)
                {
                    var p = poly[i]; var q = poly[(i + 1) % count];
                    float sp = Cross(edge, p - e0), sq = Cross(edge, q - e0);
                    bool pin = sp >= 0f, qin = sq >= 0f;
                    if (pin) tmp[n++] = p;
                    if (pin != qin) { float t = sp / (sp - sq); tmp[n++] = p + (q - p) * t; }
                }
                var sw = poly; poly = tmp; tmp = sw; count = n;
            }
            static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }
        }

        // ---------------------------------------------------------------- grid / union-find helpers

        static Vector3Int Cell(Vector3 p, float inv) { return new Vector3Int(Mathf.FloorToInt(p.x * inv), Mathf.FloorToInt(p.y * inv), Mathf.FloorToInt(p.z * inv)); }
        static long Pack(int x, int y, int z) { return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF); }
        static int Find(int[] uf, int i) { while (uf[i] != i) { uf[i] = uf[uf[i]]; i = uf[i]; } return i; }
        static void Union(int[] uf, int a, int b) { a = Find(uf, a); b = Find(uf, b); if (a != b) uf[b] = a; }

        // ---------------------------------------------------------------- capture helpers

        public const int ShotW = 1600, ShotH = 900, SaveW = 800, SaveH = 450;

        public class Shot { public string file, diffFile; public float changedFraction; public string error; }

        /// <summary>
        /// Renders cam (already placed) at 1600x900 into a 24-bit depth RT, saves an 800-px JPEG; when diffPath is set also
        /// renders a second frame with the camera moved 1 mm along its view axis and writes a red change map (pixels whose
        /// colour differs between the two frames; z-fighting surfaces swap, solid surfaces do not). Never throws.
        /// </summary>
        public static Shot Render(Camera cam, string jpgPath, string diffPath)
        {
            var shot = new Shot { file = jpgPath };
            RenderTexture big = null, small = null; Texture2D t1 = null, t2 = null, outTex = null; var prior = RenderTexture.active; var prevTarget = cam.targetTexture;
            try
            {
                big = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 }; big.Create();
                small = new RenderTexture(SaveW, SaveH, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave }; small.Create();
                cam.targetTexture = big; cam.aspect = (float)ShotW / ShotH;
                cam.Render();
                Graphics.Blit(big, small); RenderTexture.active = small;
                outTex = new Texture2D(SaveW, SaveH, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                outTex.ReadPixels(new Rect(0, 0, SaveW, SaveH), 0, 0); outTex.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(jpgPath)); File.WriteAllBytes(jpgPath, outTex.EncodeToJPG(85));
                if (!string.IsNullOrEmpty(diffPath))
                {
                    RenderTexture.active = big; t1 = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                    t1.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0); t1.Apply();
                    var pos = cam.transform.position; cam.transform.position = pos + cam.transform.forward * 0.001f;
                    cam.Render(); cam.transform.position = pos;
                    RenderTexture.active = big; t2 = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                    t2.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0); t2.Apply();
                    var p1 = t1.GetPixels32(); var p2 = t2.GetPixels32(); int changed = 0;
                    for (int i = 0; i < p1.Length; i++)
                    {
                        int d = Math.Max(Math.Abs(p1[i].r - p2[i].r), Math.Max(Math.Abs(p1[i].g - p2[i].g), Math.Abs(p1[i].b - p2[i].b)));
                        byte grey = (byte)((p1[i].r + p1[i].g + p1[i].b) / 6);
                        if (d > 24) { changed++; p1[i] = new Color32(255, 0, 0, 255); } else p1[i] = new Color32(grey, grey, grey, 255);
                    }
                    shot.changedFraction = (float)changed / p1.Length;
                    t1.SetPixels32(p1); t1.Apply();
                    var tmpRt = RenderTexture.GetTemporary(SaveW, SaveH, 0, RenderTextureFormat.ARGB32); Graphics.Blit(t1, tmpRt); RenderTexture.active = tmpRt;
                    outTex.ReadPixels(new Rect(0, 0, SaveW, SaveH), 0, 0); outTex.Apply(); RenderTexture.active = prior; RenderTexture.ReleaseTemporary(tmpRt);
                    Directory.CreateDirectory(Path.GetDirectoryName(diffPath)); File.WriteAllBytes(diffPath, outTex.EncodeToJPG(85)); shot.diffFile = diffPath;
                }
            }
            catch (Exception e) { shot.error = e.GetType().Name + ": " + e.Message; Debug.LogWarning("[KH ZFight] render failed " + jpgPath + ": " + e.Message); }
            finally
            {
                RenderTexture.active = prior; cam.targetTexture = prevTarget;
                if (big != null) { big.Release(); UnityEngine.Object.DestroyImmediate(big); }
                if (small != null) { small.Release(); UnityEngine.Object.DestroyImmediate(small); }
                if (t1 != null) UnityEngine.Object.DestroyImmediate(t1); if (t2 != null) UnityEngine.Object.DestroyImmediate(t2); if (outTex != null) UnityEngine.Object.DestroyImmediate(outTex);
            }
            return shot;
        }

        public static string Ascii(string s, int max)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s ?? "") { if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_') sb.Append(ch); if (sb.Length >= max) break; }
            return sb.ToString();
        }

        public static string V(Vector3 v) { return v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2"); }
        public static string Md(string s) { return (s ?? "").Replace("|", "\\|"); }
    }
}
