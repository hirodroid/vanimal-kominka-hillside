using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace KominkaHillside.Editor
{
    // DES-DOG-006 の位置マップ: メッシュの読み取りと UV 空間のラスタライズ (Wash/1 と Wash/2 が共有する)。
    // メニューと JSON は KH_DogWashBakeEditor.cs。
    public static partial class KH_DogWashBakeEditor
    {
        /// <summary>位置マップとマスクの一辺 (設計書 §1 のマスク 256² と同じ格子)。</summary>
        public const int Size = 256;

        /// <summary>読めたメッシュ 1 つ分。座標はメッシュのローカル (= バインドポーズ) の生の値。</summary>
        class Geo
        {
            public Vector3[] v; public Vector2[] uv; public int[] tri; public int subMeshCount; public bool hasUv1;
            // 頂点ごとの重み。GetAllBoneWeights と同じ並び (頂点 i の重みは w[wStart[i]] から wCount[i] 個)
            public byte[] wCount; public int[] wStart; public BoneWeight1[] w;
            public Matrix4x4[] bindposes; public string readVia;
            public int[] domBone;   // 頂点ごとの最大の重みの骨 (重みが無ければ -1)
        }

        /// <summary>テクセルの中心を覆った三角形 1 枚。p は重心座標で補間したバインドポーズ座標。</summary>
        struct Hit { public int tri; public Vector3 p; public int bone; public float uvArea; }

        // ================================================================ 読み取り

        /// <summary>sharedMesh → Instantiate の複製 → AcquireReadOnlyMeshData の順に試す。
        /// **Import 設定 (isReadable) は書き換えない** (購入アセットの設定は変えない)。読めなければ null と理由。</summary>
        static Geo ReadGeo(Mesh mesh, List<string> errors)
        {
            int n = mesh.vertexCount;
            if (n == 0) { errors.Add("mesh has no vertices"); return null; }
            var g = TryRead(mesh, "sharedMesh", n);
            if (g == null)
            {
                var copy = UnityEngine.Object.Instantiate(mesh);
                try { g = TryRead(copy, "Instantiate", n); }
                finally { UnityEngine.Object.DestroyImmediate(copy); }
            }
            if (g == null) g = TryReadMeshData(mesh, n, errors);
            if (g == null)
            {
                errors.Add("mesh data not readable (isReadable=" + mesh.isReadable + "; sharedMesh, Instantiate and AcquireReadOnlyMeshData all failed). Import settings were NOT changed");
                return null;
            }
            g.bindposes = mesh.bindposes;
            if (g.bindposes == null || g.bindposes.Length == 0) { errors.Add("mesh has no bindposes (not skinned?)"); return null; }
            g.wStart = new int[n];
            for (int i = 1; i < n; i++) g.wStart[i] = g.wStart[i - 1] + g.wCount[i - 1];
            g.domBone = new int[n];
            for (int i = 0; i < n; i++)
            {
                int best = -1; float bw = 0f;
                for (int k = 0; k < g.wCount[i]; k++)
                {
                    var bwt = g.w[g.wStart[i] + k];
                    if (bwt.weight > bw) { bw = bwt.weight; best = bwt.boneIndex; }
                }
                g.domBone[i] = best;
            }
            return g;
        }

        static Geo TryRead(Mesh m, string via, int n)
        {
            try
            {
                var v = m.vertices;
                if (v == null || v.Length != n) return null;   // 読めない時は Unity がエラーを出して空の配列を返す
                var uv = m.uv;
                var g = new Geo { v = v, uv = uv != null && uv.Length == n ? uv : null, subMeshCount = m.subMeshCount, readVia = via };
                g.hasUv1 = m.HasVertexAttribute(VertexAttribute.TexCoord1);
                var tri = new List<int>();
                for (int s = 0; s < m.subMeshCount; s++)
                    if (m.GetTopology(s) == MeshTopology.Triangles) tri.AddRange(m.GetTriangles(s));
                g.tri = tri.ToArray();
                var per = m.GetBonesPerVertex();   // mesh が持つ配列。Dispose しない
                if (per.Length != n)
                {
                    if (m.HasVertexAttribute(VertexAttribute.BlendIndices)) return null;   // 重みはあるのに読めない = 読めない扱い
                    g.wCount = new byte[n]; g.w = new BoneWeight1[0];
                }
                else { g.wCount = per.ToArray(); g.w = m.GetAllBoneWeights().ToArray(); }
                return g;
            }
            catch (Exception) { return null; }
        }

        /// <summary>最後の手段。位置・UV・添字は MeshData の API、重みは頂点ストリームの生のバイトから読む
        /// (MeshData には重みの読み出しが無い。同じストリームの属性は VertexAttribute の順に並ぶ)。</summary>
        static Geo TryReadMeshData(Mesh mesh, int n, List<string> errors)
        {
            try
            {
                using (var arr = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    var d = arr[0];
                    if (d.vertexCount != n) return null;
                    var g = new Geo { readVia = "AcquireReadOnlyMeshData", subMeshCount = d.subMeshCount };
                    using (var v = new NativeArray<Vector3>(n, Allocator.Temp)) { d.GetVertices(v); g.v = v.ToArray(); }
                    if (d.HasVertexAttribute(VertexAttribute.TexCoord0))
                    {
                        using (var uv = new NativeArray<Vector2>(n, Allocator.Temp)) { d.GetUVs(0, uv); g.uv = uv.ToArray(); }
                    }
                    g.hasUv1 = d.HasVertexAttribute(VertexAttribute.TexCoord1);
                    var tri = new List<int>();
                    for (int s = 0; s < d.subMeshCount; s++)
                    {
                        var sm = d.GetSubMesh(s);
                        if (sm.topology != MeshTopology.Triangles) continue;
                        using (var idx = new NativeArray<int>(sm.indexCount, Allocator.Temp)) { d.GetIndices(idx, s); tri.AddRange(idx.ToArray()); }
                    }
                    g.tri = tri.ToArray();
                    ReadSkinStreams(mesh, d, n, g);
                    return g;
                }
            }
            catch (Exception e) { errors.Add("AcquireReadOnlyMeshData failed: " + e.Message); return null; }
        }

        static void ReadSkinStreams(Mesh mesh, Mesh.MeshData d, int n, Geo g)
        {
            g.wCount = new byte[n];
            if (!d.HasVertexAttribute(VertexAttribute.BlendIndices)) { g.w = new BoneWeight1[0]; return; }
            var ai = VertexAttribute.BlendIndices; var aw = VertexAttribute.BlendWeight;
            int si = d.GetVertexAttributeStream(ai), di = d.GetVertexAttributeDimension(ai), oi = mesh.GetVertexAttributeOffset(ai);
            var fi = d.GetVertexAttributeFormat(ai); int stI = mesh.GetVertexBufferStride(si);
            var bi = d.GetVertexData<byte>(si);
            bool hasW = d.HasVertexAttribute(aw);
            int sw = hasW ? d.GetVertexAttributeStream(aw) : 0, dw = hasW ? d.GetVertexAttributeDimension(aw) : 0, ow = hasW ? mesh.GetVertexAttributeOffset(aw) : 0;
            var fw = hasW ? d.GetVertexAttributeFormat(aw) : VertexAttributeFormat.Float32; int stW = hasW ? mesh.GetVertexBufferStride(sw) : 0;
            var bw = hasW ? d.GetVertexData<byte>(sw) : bi;
            var list = new List<BoneWeight1>(n * 4); var tmp = new List<BoneWeight1>(8);
            for (int i = 0; i < n; i++)
            {
                tmp.Clear();
                for (int k = 0; k < di; k++)
                {
                    int bone = (int)ReadComp(bi, i * stI + oi + k * FormatSize(fi), fi);
                    float wt = hasW ? (k < dw ? ReadComp(bw, i * stW + ow + k * FormatSize(fw), fw) : 0f) : (k == 0 ? 1f : 0f);
                    if (wt > 0f) tmp.Add(new BoneWeight1 { boneIndex = bone, weight = wt });
                }
                tmp.Sort((a, b) => b.weight.CompareTo(a.weight));
                g.wCount[i] = (byte)tmp.Count; list.AddRange(tmp);
            }
            g.w = list.ToArray();
        }

        static int FormatSize(VertexAttributeFormat f)
        {
            switch (f)
            {
                case VertexAttributeFormat.Float32: case VertexAttributeFormat.UInt32: case VertexAttributeFormat.SInt32: return 4;
                case VertexAttributeFormat.Float16: case VertexAttributeFormat.UNorm16: case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16: case VertexAttributeFormat.SInt16: return 2;
                default: return 1;
            }
        }

        static float ReadComp(NativeArray<byte> b, int at, VertexAttributeFormat f)
        {
            switch (f)
            {
                case VertexAttributeFormat.Float32: return BitConverter.ToSingle(new[] { b[at], b[at + 1], b[at + 2], b[at + 3] }, 0);
                case VertexAttributeFormat.Float16: return Mathf.HalfToFloat((ushort)(b[at] | b[at + 1] << 8));
                case VertexAttributeFormat.UNorm8: return b[at] / 255f;
                case VertexAttributeFormat.UNorm16: return (b[at] | b[at + 1] << 8) / 65535f;
                case VertexAttributeFormat.UInt8: return b[at];
                case VertexAttributeFormat.SInt8: return (sbyte)b[at];
                case VertexAttributeFormat.UInt16: return b[at] | b[at + 1] << 8;
                case VertexAttributeFormat.SInt16: return (short)(b[at] | b[at + 1] << 8);
                case VertexAttributeFormat.UInt32: case VertexAttributeFormat.SInt32: return b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24;
                default: throw new NotSupportedException("vertex format " + f);
            }
        }

        // ================================================================ ラスタライズ

        static float Cross(Vector2 o, Vector2 a, Vector2 b) { return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x); }

        /// <summary>全サブメッシュの三角形を UV0 の 256² 格子へ描く。テクセルの中心 ((x+0.5)/256, (y+0.5)/256) が三角形の中
        /// (辺の上を含む) なら覆ったとする。0〜1 の外の UV はラップせず捨てる (実行時のマスクも Clamp で読むので)。
        /// 返り値の添字は y * Size + x (y = 0 が v = 0。Texture2D の行の並びと同じ)。</summary>
        static List<Hit>[] Rasterize(Geo g, out int negativeWinding, out int degenerate)
        {
            var cells = new List<Hit>[Size * Size];
            negativeWinding = 0; degenerate = 0;
            for (int t = 0; t + 2 < g.tri.Length; t += 3)
            {
                int ia = g.tri[t], ib = g.tri[t + 1], ic = g.tri[t + 2];
                Vector2 a = g.uv[ia] * Size, b = g.uv[ib] * Size, c = g.uv[ic] * Size;
                float area2 = Cross(a, b, c);
                if (Mathf.Abs(area2) < 1e-9f) { degenerate++; continue; }
                if (area2 < 0f) negativeWinding++;   // UV の巻きが逆 = ミラーした島の目安 (3D 側の向きとは比べていない)
                int x0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - .5f));
                int x1 = Mathf.Min(Size - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - .5f));
                int y0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - .5f));
                int y1 = Mathf.Min(Size - 1, Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - .5f));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + .5f, y + .5f);
                        float wa = Cross(p, b, c) / area2, wb = Cross(a, p, c) / area2, wc = 1f - wa - wb;
                        if (wa < 0f || wb < 0f || wc < 0f) continue;
                        var hit = new Hit
                        {
                            tri = t / 3, uvArea = Mathf.Abs(area2) * .5f,
                            p = g.v[ia] * wa + g.v[ib] * wb + g.v[ic] * wc,
                            bone = DominantAt(g, ia, ib, ic, wa, wb, wc),
                        };
                        int i = y * Size + x;
                        if (cells[i] == null) cells[i] = new List<Hit>(1);
                        cells[i].Add(hit);
                    }
            }
            return cells;
        }

        static readonly int[] accBone = new int[64];
        static readonly float[] accW = new float[64];

        /// <summary>3 頂点の重みを重心座標で混ぜた時に最大になる骨 (bones 配列の番号)。重みが無ければ -1。</summary>
        static int DominantAt(Geo g, int ia, int ib, int ic, float wa, float wb, float wc)
        {
            int count = 0;
            Accumulate(g, ia, wa, ref count); Accumulate(g, ib, wb, ref count); Accumulate(g, ic, wc, ref count);
            int best = -1; float bw = 0f;
            for (int k = 0; k < count; k++) if (accW[k] > bw) { bw = accW[k]; best = accBone[k]; }
            return best;
        }

        static void Accumulate(Geo g, int v, float f, ref int count)
        {
            for (int k = 0; k < g.wCount[v]; k++)
            {
                var bwt = g.w[g.wStart[v] + k];
                int j = 0;
                while (j < count && accBone[j] != bwt.boneIndex) j++;
                if (j == count) { if (count == accBone.Length) continue; accBone[j] = bwt.boneIndex; accW[j] = 0f; count++; }
                accW[j] += bwt.weight * f;
            }
        }

        /// <summary>覆った三角形どうしのバインドポーズ座標の差の最大 (メッシュの単位)。</summary>
        static float Spread(List<Hit> h)
        {
            float m = 0f;
            for (int i = 0; i < h.Count; i++)
                for (int j = i + 1; j < h.Count; j++) m = Mathf.Max(m, (h[i].p - h[j].p).sqrMagnitude);
            return Mathf.Sqrt(m);
        }

        /// <summary>メッシュの単位 → m の倍率。骨ごとに (bone.localToWorldMatrix × bindpose) の軸の長さの平均を取り、
        /// 最大の重みの頂点を持つ骨の中央値を返す。前提: 骨のスケールがアニメーションで変わらない
        /// (回転と移動だけなら姿勢によらず、バインド時の Renderer の localToWorld の倍率と一致する)。
        /// lossyScale 100 の Renderer でも、実際に皮を張っている骨の側から測るので単位の取り違えに強い。</summary>
        static float MeshToWorldScale(SkinnedMeshRenderer smr, Geo g, out float min, out float max)
        {
            var bones = smr.bones; var list = new List<float>();
            var used = new bool[g.bindposes.Length];
            foreach (int b in g.domBone) if (b >= 0 && b < used.Length) used[b] = true;
            for (int i = 0; i < Mathf.Min(bones.Length, g.bindposes.Length); i++)
            {
                if (bones[i] == null || !used[i]) continue;
                list.Add(AxisScale(bones[i].localToWorldMatrix * g.bindposes[i]));
            }
            if (list.Count == 0) { min = max = AxisScale(smr.transform.localToWorldMatrix); return min; }
            list.Sort(); min = list[0]; max = list[list.Count - 1];
            return list[list.Count / 2];
        }

        static float AxisScale(Matrix4x4 m)
        {
            return (((Vector3)m.GetColumn(0)).magnitude + ((Vector3)m.GetColumn(1)).magnitude + ((Vector3)m.GetColumn(2)).magnitude) / 3f;
        }
    }
}
