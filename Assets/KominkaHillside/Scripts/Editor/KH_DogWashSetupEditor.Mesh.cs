using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KominkaHillside.Editor
{
    // IMP-DOG-025 builder: the mesh side (read, bone -> part, capsule fits, UV cells). Pure data, no scene or asset writes.
    // KH_DogWashBakeEditor's reader and rasteriser are private, so the parts needed here are written again (same rules:
    // texel centres inside UV0 triangles, the larger UV triangle wins an overlap, dominant bone by blended weights).
    public static partial class KH_DogWashSetupEditor
    {
        /// <summary>メッシュ 1 つ分。座標はバインドポーズのメッシュ座標 (生の値)。</summary>
        class Geo
        {
            public Vector3[] v; public Vector2[] uv; public int[] tri; public Matrix4x4[] bindposes;
            public byte[] wCount; public int[] wStart; public BoneWeight1[] w;
            public int[] dom;   // per vertex: the max-weight bone (-1 = none)

            public float Weight(int vi, int bone)
            {
                for (int k = 0; k < wCount[vi]; k++) { var b = w[wStart[vi] + k]; if (b.boneIndex == bone) return b.weight; }
                return 0f;
            }
        }

        /// <summary>骨 → 部位と、頂点をどの Collider の骨へまとめるか。</summary>
        class BoneMap
        {
            public Transform[] bones;
            public int[] part;             // per renderer bone
            public int[] owner;            // per renderer bone: the collider bone its vertices go to (-1 = none)
            public List<int> colliderBones = new List<int>();   // renderer bone indices that carry a collider, in build order
            public Dictionary<int, int> colliderPart = new Dictionary<int, int>();
            public int[] domCount;
            public List<string> notes = new List<string>();
        }

        /// <summary>1 つのカプセル (メッシュ座標)。</summary>
        class Fit
        {
            public int bone, part, verts;
            public Vector3 center, axis;   // mesh space; axis unit
            public float radius, height, radius0;
            public string axisFrom, note = "";
            public float worstSwallow; public int worstSwallowPart = -1;
        }

        class Cells
        {
            public bool[] valid = new bool[CellCount];
            public Vector3[] center = new Vector3[CellCount];
            public int[] part = new int[CellCount];
            public int[] texels = new int[CellCount];
            public int validCells, texelsTotal;
        }

        const int MinExtraColliderVerts = 20, MinFitVerts = 6;

        // ================================================================ 読み取り

        /// <summary>sharedMesh から読む (Wash/1 の記録では 6 匹とも sharedMesh で読める)。**Import 設定は変えない**。読めなければ止まる。</summary>
        static Geo ReadGeo(Mesh mesh)
        {
            if (!mesh.isReadable) throw new InvalidOperationException(mesh.name + " is not readable (the builder does not change import settings)");
            var g = new Geo { v = mesh.vertices, uv = mesh.uv, bindposes = mesh.bindposes };
            int n = g.v.Length;
            if (n == 0 || g.uv == null || g.uv.Length != n) throw new InvalidOperationException(mesh.name + ": no vertices or no UV0");
            if (g.bindposes == null || g.bindposes.Length == 0) throw new InvalidOperationException(mesh.name + ": no bindposes");
            var tri = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++) if (mesh.GetTopology(s) == MeshTopology.Triangles) tri.AddRange(mesh.GetTriangles(s));
            g.tri = tri.ToArray();
            var per = mesh.GetBonesPerVertex();       // owned by the mesh: not disposed
            var all = mesh.GetAllBoneWeights();
            if (per.Length != n) throw new InvalidOperationException(mesh.name + ": bone weights not readable");
            g.wCount = per.ToArray(); g.w = all.ToArray();
            g.wStart = new int[n];
            for (int i = 1; i < n; i++) g.wStart[i] = g.wStart[i - 1] + g.wCount[i - 1];
            g.dom = new int[n];
            for (int i = 0; i < n; i++)
            {
                int best = -1; float bw = 0f;
                for (int k = 0; k < g.wCount[i]; k++) { var b = g.w[g.wStart[i] + k]; if (b.weight > bw) { bw = b.weight; best = b.boneIndex; } }
                g.dom[i] = best;
            }
            return g;
        }

        // ================================================================ 骨 → 部位

        /// <summary>表 (約束 §3) の骨はその部位。表に無い骨は親をたどって最初に見つかった部位。祖先に表の骨が無い骨 (根の `Bone`) は
        /// <see cref="FallbackPart"/>、そのうち頂点を <see cref="MinExtraColliderVerts"/> 以上持つ骨には Collider を 1 つ足す (胴は骨ごと)。</summary>
        static BoneMap MapBones(SkinnedMeshRenderer smr, Geo g)
        {
            var m = new BoneMap { bones = smr.bones };
            int n = m.bones.Length;
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < n; i++) if (m.bones[i] != null && !index.ContainsKey(m.bones[i])) index[m.bones[i]] = i;
            var tablePart = Enumerable.Repeat(-1, n).ToArray();
            var ordered = new List<int>();
            // IMP-DOG-030: a table bone missing from this dog (the Pome has 31 bones: no Bone.022 / .023, which are not in the
            // table; the table's own bones are in all 6 dogs per the Wash/1 survey) is skipped with a note: its vertices, if a
            // child bone has any, go to the nearest collider bone above (the owner rule below). A part with none of its
            // table bones stops the build (nothing to hang the part's collider on).
            for (int p = 0; p < PartCount; p++)
            {
                int found = 0;
                foreach (var name in PartBoneNames[p])
                {
                    int b = Array.FindIndex(m.bones, x => x != null && x.name == name);
                    if (b < 0) { m.notes.Add("table bone " + name + " (part " + PartNames[p] + ") not in the renderer's bones: skipped (its part keeps the other bones' colliders)"); continue; }
                    if (tablePart[b] >= 0) throw new InvalidOperationException("bone " + name + " is listed for two parts");
                    tablePart[b] = p; ordered.Add(b); found++;
                }
                if (found == 0) throw new InvalidOperationException("part " + PartNames[p] + ": none of its bones (" + string.Join(", ", PartBoneNames[p]) + ") in the renderer's bones");
            }
            m.domCount = new int[n];
            foreach (int d in g.dom) if (d >= 0 && d < n) m.domCount[d]++;

            Func<int, Func<int, bool>, int> ancestor = (b, pred) =>
            {
                for (var t = m.bones[b] != null ? m.bones[b].parent : null; t != null; t = t.parent)
                    if (index.TryGetValue(t, out int k) && pred(k)) return k;
                return -1;
            };
            var colliderSet = new HashSet<int>(ordered);
            var extras = new List<int>();
            for (int b = 0; b < n; b++)
            {
                if (tablePart[b] >= 0 || m.bones[b] == null) continue;
                if (ancestor(b, k => tablePart[k] >= 0) >= 0) continue;
                if (m.domCount[b] >= MinExtraColliderVerts) { extras.Add(b); colliderSet.Add(b); m.notes.Add(m.bones[b].name + ": no part bone above it, " + m.domCount[b] + " vertices -> extra collider in part " + PartNames[FallbackPart]); }
            }
            // build order: part by part, the table order, the extras of a part right after its table bones
            for (int p = 0; p < PartCount; p++)
            {
                foreach (int b in ordered) if (tablePart[b] == p) m.colliderBones.Add(b);
                if (p == FallbackPart) m.colliderBones.AddRange(extras);
            }
            foreach (int b in m.colliderBones) m.colliderPart[b] = tablePart[b] >= 0 ? tablePart[b] : FallbackPart;

            m.owner = new int[n]; m.part = new int[n];
            for (int b = 0; b < n; b++)
            {
                m.owner[b] = colliderSet.Contains(b) ? b : ancestor(b, k => colliderSet.Contains(k));
                if (m.owner[b] >= 0) m.part[b] = m.colliderPart[m.owner[b]];
                else
                {
                    int ta = ancestor(b, k => tablePart[k] >= 0);
                    m.part[b] = ta >= 0 ? tablePart[ta] : FallbackPart;
                    if (m.domCount[b] > 0) m.notes.Add((m.bones[b] != null ? m.bones[b].name : "#" + b) + ": " + m.domCount[b] + " vertices belong to no collider (part " + PartNames[m.part[b]] + " by table)");
                }
            }
            return m;
        }

        // ================================================================ カプセル

        static Vector3 BoneOrigin(Geo g, int b) { return g.bindposes[b].inverse.MultiplyPoint3x4(Vector3.zero); }

        /// <summary>Collider ごとに、その骨へまとめた頂点 (最大の重みの骨) へカプセルを当てはめる (メッシュ座標)。軸は骨から子の Collider の骨への向き
        /// (無ければ頂点の主成分)、半径は軸からの距離の 85 パーセンタイル、長さは軸方向の 2〜98 パーセンタイル。胴 (部位 0・1) は、ほかの部位の頂点を
        /// 20% より多く呑む間は半径を 0.9 倍ずつ (元の 0.6 倍まで) 小さくする (vrc-character-rig §14)。</summary>
        static List<Fit> FitColliders(Geo g, BoneMap m)
        {
            var fits = new List<Fit>();
            var byCollider = new Dictionary<int, List<Vector3>>();
            foreach (int b in m.colliderBones) byCollider[b] = new List<Vector3>();
            var byPart = new List<Vector3>[PartCount];
            for (int p = 0; p < PartCount; p++) byPart[p] = new List<Vector3>();
            for (int i = 0; i < g.v.Length; i++)
            {
                int d = g.dom[i];
                if (d < 0 || d >= m.owner.Length) continue;
                byPart[m.part[d]].Add(g.v[i]);
                if (m.owner[d] >= 0) byCollider[m.owner[d]].Add(g.v[i]);
            }
            var colliderSet = new HashSet<int>(m.colliderBones);
            foreach (int b in m.colliderBones)
            {
                var f = new Fit { bone = b, part = m.colliderPart[b] };
                var pts = byCollider[b]; f.verts = pts.Count;
                Vector3 o = BoneOrigin(g, b);
                // the child collider bone (nearest collider ancestor == b): the same part first (legs, tail, the root
                // bone -> hip), else the next part along the spine (hip -> chest -> neck -> head), never a leg root
                int child = FindChildCollider(m, colliderSet, b, f.part);
                if (child < 0 && SpineNext[f.part] >= 0) child = FindChildCollider(m, colliderSet, b, SpineNext[f.part]);
                Vector3 centroid = pts.Count > 0 ? pts.Aggregate(Vector3.zero, (a, x) => a + x) / pts.Count : o;
                Vector3 axis;
                if (child >= 0 && (BoneOrigin(g, child) - o).sqrMagnitude > 1e-10f) { axis = (BoneOrigin(g, child) - o).normalized; f.axisFrom = "child " + m.bones[child].name; }
                else if (pts.Count >= 3)
                {
                    axis = Principal(pts, centroid);
                    if (Vector3.Dot(axis, centroid - o) < 0f) axis = -axis;
                    f.axisFrom = "principal";
                }
                else
                {
                    var parent = m.bones[b].parent; int pk = parent != null ? Array.IndexOf(m.bones, parent) : -1;
                    axis = pk >= 0 && (o - BoneOrigin(g, pk)).sqrMagnitude > 1e-10f ? (o - BoneOrigin(g, pk)).normalized : Vector3.up;
                    f.axisFrom = "parent";
                }
                f.axis = axis;
                if (pts.Count < MinFitVerts)
                {
                    // Too few vertices: a small capsule from the bone along the axis, and a note.
                    f.radius = 0.015f; f.height = 0.06f; f.center = o + axis * 0.03f;   // mesh units (about 2.4 cm / 9.7 cm on the Shiba)
                    f.note = "only " + pts.Count + " vertices: default capsule";
                }
                else
                {
                    var ts = new List<float>(pts.Count); var ds = new List<float>(pts.Count);
                    foreach (var p in pts)
                    {
                        var q = p - centroid; float t = Vector3.Dot(q, axis);
                        ts.Add(t); ds.Add((q - axis * t).magnitude);
                    }
                    ts.Sort(); ds.Sort();
                    float tmin = pts.Count >= 20 ? Pct(ts, 0.02f) : ts[0], tmax = pts.Count >= 20 ? Pct(ts, 0.98f) : ts[ts.Count - 1];
                    f.radius = Mathf.Max(Pct(ds, 0.85f), 1e-4f);
                    f.height = Mathf.Max(tmax - tmin, 2f * f.radius);
                    f.center = centroid + axis * ((tmin + tmax) * 0.5f);
                }
                f.radius0 = f.radius;
                fits.Add(f);
            }
            // torso: do not swallow the legs, the tail root, the neck (vrc-character-rig §14)
            foreach (var f in fits)
            {
                if (f.part != 0 && f.part != 1) continue;
                for (int iter = 0; iter < 8; iter++)
                {
                    f.worstSwallow = 0f; f.worstSwallowPart = -1;
                    for (int q = 2; q < PartCount; q++)
                    {
                        if (byPart[q].Count == 0) continue;
                        int inside = byPart[q].Count(p => InCapsule(p, f));
                        float frac = inside / (float)byPart[q].Count;
                        if (frac > f.worstSwallow) { f.worstSwallow = frac; f.worstSwallowPart = q; }
                    }
                    if (f.worstSwallow <= 0.2f || f.radius <= f.radius0 * 0.6f + 1e-6f) break;
                    f.radius = Mathf.Max(f.radius * 0.9f, f.radius0 * 0.6f);
                    f.height = Mathf.Max(f.height, 2f * f.radius);
                }
                if (f.radius < f.radius0) f.note += (f.note.Length > 0 ? "; " : "") + "radius capped " + (f.radius / f.radius0).ToString("F2") + " x (swallowed " + PartNames[Math.Max(0, f.worstSwallowPart)] + " " + f.worstSwallow.ToString("P0") + ")";
            }
            return fits;
        }

        /// <summary>背骨に沿った次の部位 (胴の骨の軸を子の骨へ向けるため)。-1 = なし。</summary>
        static readonly int[] SpineNext = { 1, 2, 7, -1, -1, -1, -1, -1, -1 };

        static int FindChildCollider(BoneMap m, HashSet<int> colliderSet, int b, int part)
        {
            foreach (int c in m.colliderBones)
            {
                if (c == b || m.colliderPart[c] != part || m.bones[c] == null) continue;
                for (var t = m.bones[c].parent; t != null; t = t.parent)
                {
                    int k = Array.IndexOf(m.bones, t);
                    if (k < 0 || !colliderSet.Contains(k)) continue;
                    if (k == b) return c;
                    break;
                }
            }
            return -1;
        }

        static bool InCapsule(Vector3 p, Fit f)
        {
            float half = Mathf.Max(0f, f.height * 0.5f - f.radius);
            Vector3 a = f.center - f.axis * half, b = f.center + f.axis * half, ab = b - a;
            float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).sqrMagnitude < f.radius * f.radius;
        }

        /// <summary>カプセルの表面からの符号付き距離 (メッシュ座標、外が正)。</summary>
        static float CapsuleSignedDistance(Vector3 p, Fit f)
        {
            float half = Mathf.Max(0f, f.height * 0.5f - f.radius);
            Vector3 a = f.center - f.axis * half, b = f.center + f.axis * half, ab = b - a;
            float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).magnitude - f.radius;
        }

        static float Pct(List<float> sorted, float q)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[i];
        }

        /// <summary>主成分 (共分散の最大固有ベクトル、べき乗法)。</summary>
        static Vector3 Principal(List<Vector3> pts, Vector3 c)
        {
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pts) { var q = p - c; xx += q.x * q.x; xy += q.x * q.y; xz += q.x * q.z; yy += q.y * q.y; yz += q.y * q.z; zz += q.z * q.z; }
            var v = new Vector3(0.577f, 0.577f, 0.577f);
            for (int i = 0; i < 48; i++)
            {
                var nv = new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z);
                if (nv.sqrMagnitude < 1e-20f) return Vector3.up;
                v = nv.normalized;
            }
            return v;
        }

        // ================================================================ セル (UV 16x16)

        /// <summary>自分で UV0 をラスタライズする (位置マップは継ぎ目を広げてあり、広げたテクセルと区別できないため)。テクセルの中心が三角形の中なら有効、
        /// 重なりは UV 面積の大きい三角形。セル = (y / 16) * 16 + x / 16 (y は v、下から)。中心 = セルの有効テクセルの平均、部位 = 最も多い部位。</summary>
        static Cells BuildCells(Geo g, BoneMap m)
        {
            int size = KH_DogWashBakeEditor.Size;
            var bestArea = new float[size * size]; var pos = new Vector3[size * size]; var bone = Enumerable.Repeat(-1, size * size).ToArray();
            var hit = new bool[size * size];
            var acc = new Dictionary<int, float>();
            for (int t = 0; t + 2 < g.tri.Length; t += 3)
            {
                int ia = g.tri[t], ib = g.tri[t + 1], ic = g.tri[t + 2];
                Vector2 a = g.uv[ia] * size, b = g.uv[ib] * size, c = g.uv[ic] * size;
                float area2 = Cross(a, b, c);
                if (Mathf.Abs(area2) < 1e-9f) continue;
                int x0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - .5f)), x1 = Mathf.Min(size - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - .5f));
                int y0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - .5f)), y1 = Mathf.Min(size - 1, Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - .5f));
                float uvArea = Mathf.Abs(area2) * .5f;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + .5f, y + .5f);
                        float wa = Cross(p, b, c) / area2, wb = Cross(a, p, c) / area2, wc = 1f - wa - wb;
                        if (wa < 0f || wb < 0f || wc < 0f) continue;
                        int i = y * size + x;
                        if (hit[i] && bestArea[i] >= uvArea) continue;
                        acc.Clear();
                        Accumulate(g, ia, wa, acc); Accumulate(g, ib, wb, acc); Accumulate(g, ic, wc, acc);
                        int bb = -1; float bw = 0f;
                        foreach (var kv in acc) if (kv.Value > bw) { bw = kv.Value; bb = kv.Key; }
                        hit[i] = true; bestArea[i] = uvArea; bone[i] = bb;
                        pos[i] = g.v[ia] * wa + g.v[ib] * wb + g.v[ic] * wc;
                    }
            }
            var cells = new Cells();
            var sum = new Vector3[CellCount]; var partCount = new int[CellCount, PartCount];
            int per = size / CellGrid;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    if (!hit[i] || bone[i] < 0 || bone[i] >= m.part.Length) continue;
                    int cell = (y / per) * CellGrid + x / per;
                    sum[cell] += pos[i]; cells.texels[cell]++; partCount[cell, m.part[bone[i]]]++; cells.texelsTotal++;
                }
            for (int c = 0; c < CellCount; c++)
            {
                cells.valid[c] = cells.texels[c] > 0;
                if (!cells.valid[c]) continue;
                cells.validCells++;
                cells.center[c] = sum[c] / cells.texels[c];
                int best = 0;
                for (int p = 1; p < PartCount; p++) if (partCount[c, p] > partCount[c, best]) best = p;
                cells.part[c] = best;
            }
            return cells;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) { return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x); }

        static void Accumulate(Geo g, int v, float f, Dictionary<int, float> acc)
        {
            for (int k = 0; k < g.wCount[v]; k++)
            {
                var b = g.w[g.wStart[v] + k];
                acc.TryGetValue(b.boneIndex, out float cur);
                acc[b.boneIndex] = cur + b.weight * f;
            }
        }

        static float[] BoneToPartArray(BoneMap m)
        {
            var a = new float[BoneSlots];
            for (int b = 0; b < m.part.Length && b < BoneSlots; b++) a[b] = m.part[b];
            return a;
        }
    }
}
