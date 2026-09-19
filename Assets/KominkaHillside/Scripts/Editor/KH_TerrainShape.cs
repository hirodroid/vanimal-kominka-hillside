// Terrain 試作の形状計算 (高さ / 道 / splat)。計画 §2.1〜2.2。設定は KH_TerrainPrototypeConfig。
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using C = KH_TerrainPrototypeConfig;

public static class KH_TerrainShape
{
    public const int LayerGrassHill = 0, LayerGrassRocky = 1, LayerSand = 2, LayerMudRocky = 3;

    public struct PathSample
    {
        public Vector2 xz;   // 位置
        public float h;      // 道の高さ (m)
        public float s;      // 起点からの弧長
        public bool anchored; // 制御点 / 平坦部 (勾配調整で動かさない)
    }

    // ---------------------------------------------------------------- 基本面
    static float SmoothStep(float a, float b, float t) { t = Mathf.Clamp01((t - a) / (b - a)); return t * t * (3f - 2f * t); }

    public static float BaseHeight(float x, float z)
    {
        float h;
        if (z < C.RidgeZ1) h = Mathf.Lerp(C.RidgeTop, C.ValleyH, SmoothStep(C.RidgeZ0, C.RidgeZ1, z));
        else if (z < C.SlopeZ0) h = C.ValleyH + (Mathf.PerlinNoise(x * 0.07f + 3f, z * 0.07f + 9f) - 0.5f) * 2f * C.ValleyNoise;
        else if (z < C.SlopeZ1) h = Mathf.Lerp(C.ValleyH + 0.5f, C.HillBase, Mathf.Clamp01((z - C.SlopeZ0) / (C.SlopeZ1 - C.SlopeZ0))); // 線形 (一様な勾配)
        else if (z < C.BankZ0) h = C.HillBase + C.HillRise * SmoothStep(C.SlopeZ1, C.BankZ0, z);
        else h = C.HillBase + C.HillRise + C.BankHeight * SmoothStep(C.BankZ0, C.BankZ1, z);

        if (z >= C.BankFromZ)
        {
            if (x < C.BankX0) h += C.BankHeight * SmoothStep(C.BankX0, 0f, x);
            else if (x > C.BankX1) h += C.BankHeight * SmoothStep(C.BankX1, C.TerrainSize.x, x);
        }
        for (int i = 0; i < C.Bumps.GetLength(0); i++)
        {
            float dx = x - C.Bumps[i, 0], dz = z - C.Bumps[i, 1], sg = C.Bumps[i, 3];
            h += C.Bumps[i, 2] * Mathf.Exp(-(dx * dx + dz * dz) / (2f * sg * sg));
        }
        return h;
    }

    // 平坦部を適用した面
    public static float FlattenedHeight(float x, float z, float baseH)
    {
        float h = baseH;
        for (int i = 0; i < C.Flats.GetLength(0); i++)
        {
            float d = Vector2.Distance(new Vector2(x, z), new Vector2(C.Flats[i, 0], C.Flats[i, 1]));
            float r = C.Flats[i, 3], band = C.Flats[i, 4];
            h = Mathf.Lerp(C.Flats[i, 2], h, SmoothStep(r, r + band, d));
        }
        return h;
    }

    // ---------------------------------------------------------------- 道
    static Vector2 CtrlXZ(int i) { int n = C.Path.GetLength(0); i = ((i % n) + n) % n; return new Vector2(C.Path[i, 0], C.Path[i, 1]); }

    // centripetal Catmull-Rom (閉曲線) を弧長 PathSampleStep で再サンプルし、高さを制御点間で線形補間 → 勾配上限で緩和
    public static List<PathSample> SamplePath()
    {
        int n = C.Path.GetLength(0);
        var dense = new List<Vector2>();
        var denseSeg = new List<int>();
        var denseT = new List<float>();
        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = CtrlXZ(i - 1), p1 = CtrlXZ(i), p2 = CtrlXZ(i + 1), p3 = CtrlXZ(i + 2);
            float t0 = 0f, t1 = t0 + Mathf.Pow(Vector2.Distance(p0, p1), C.CatmullRomAlpha), t2 = t1 + Mathf.Pow(Vector2.Distance(p1, p2), C.CatmullRomAlpha), t3 = t2 + Mathf.Pow(Vector2.Distance(p2, p3), C.CatmullRomAlpha);
            const int steps = 60;
            for (int k = 0; k < steps; k++)
            {
                float u = k / (float)steps;
                float t = Mathf.Lerp(t1, t2, u);
                Vector2 a1 = Lerp2(p0, p1, (t - t0) / (t1 - t0)), a2 = Lerp2(p1, p2, (t - t1) / (t2 - t1)), a3 = Lerp2(p2, p3, (t - t2) / (t3 - t2));
                Vector2 b1 = Lerp2(a1, a2, (t - t0) / (t2 - t0)), b2 = Lerp2(a2, a3, (t - t1) / (t3 - t1));
                dense.Add(Lerp2(b1, b2, (t - t1) / (t2 - t1)));
                denseSeg.Add(i); denseT.Add(u);
            }
        }
        // 弧長で再サンプル
        var result = new List<PathSample>();
        float acc = 0f, next = 0f;
        for (int i = 0; i < dense.Count; i++)
        {
            int j = (i + 1) % dense.Count;
            float segLen = Vector2.Distance(dense[i], dense[j]);
            while (next <= acc + segLen)
            {
                float u = segLen > 1e-5f ? (next - acc) / segLen : 0f;
                var ps = new PathSample();
                ps.xz = Vector2.Lerp(dense[i], dense[j], u);
                ps.s = next;
                int seg = denseSeg[i];
                float segT = Mathf.Lerp(denseT[i], i + 1 < dense.Count && denseSeg[j] == seg ? denseT[j] : 1f, u);
                ps.h = Mathf.Lerp(C.Path[seg, 2], C.Path[(seg + 1) % n, 2], segT);
                ps.anchored = segT < 1e-3f;
                result.Add(ps);
                next += C.PathSampleStep;
            }
            acc += segLen;
        }
        // 平坦部の中は水平 (固定)
        for (int i = 0; i < result.Count; i++)
        {
            var ps = result[i];
            for (int f = 0; f < C.Flats.GetLength(0); f++)
                if (Vector2.Distance(ps.xz, new Vector2(C.Flats[f, 0], C.Flats[f, 1])) <= C.Flats[f, 3]) { ps.h = C.Flats[f, 2]; ps.anchored = true; }
            result[i] = ps;
        }
        // 勾配上限で緩和 (固定点は動かさない)
        float maxD = Mathf.Tan(C.PathMaxSlopeDeg * Mathf.Deg2Rad) * C.PathSampleStep * 0.98f;
        for (int pass = 0; pass < 200; pass++)
        {
            bool changed = false;
            for (int i = 0; i < result.Count; i++)
            {
                var a = result[i]; var b = result[(i + 1) % result.Count];
                float d = b.h - a.h;
                if (Mathf.Abs(d) <= maxD) continue;
                float excess = Mathf.Abs(d) - maxD;
                if (!a.anchored && !b.anchored) { a.h += Mathf.Sign(d) * excess * 0.5f; b.h -= Mathf.Sign(d) * excess * 0.5f; }
                else if (!a.anchored) a.h += Mathf.Sign(d) * excess;
                else if (!b.anchored) b.h -= Mathf.Sign(d) * excess;
                else continue;
                result[i] = a; result[(i + 1) % result.Count] = b; changed = true;
            }
            if (!changed) break;
        }
        return result;
    }

    static Vector2 Lerp2(Vector2 a, Vector2 b, float t) { return a + (b - a) * t; }

    public static float PathLength(List<PathSample> s) { return s.Count * C.PathSampleStep; }

    // 点から道の中心線までの距離と、そこでの道の高さ (隣接サンプルへ射影して補間)
    public static float DistanceToPath(List<PathSample> s, float x, float z, out float roadH)
    {
        var p = new Vector2(x, z);
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < s.Count; i++)
        {
            float d = (s[i].xz - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        float dist = Mathf.Sqrt(bestD); roadH = s[best].h;
        for (int k = -1; k <= 0; k++)
        {
            int i0 = (best + k + s.Count) % s.Count, i1 = (i0 + 1) % s.Count;
            Vector2 a = s[i0].xz, b = s[i1].xz, ab = b - a;
            float len2 = ab.sqrMagnitude; if (len2 < 1e-6f) continue;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            float d = Vector2.Distance(p, a + ab * t);
            if (d < dist) { dist = d; roadH = Mathf.Lerp(s[i0].h, s[i1].h, t); }
        }
        return dist;
    }

    // 道の進行方向 (正規化) と左向きの法線
    public static void PathFrame(List<PathSample> s, int i, out Vector2 forward, out Vector2 left)
    {
        var a = s[(i - 1 + s.Count) % s.Count].xz; var b = s[(i + 1) % s.Count].xz;
        forward = (b - a).normalized;
        left = new Vector2(-forward.y, forward.x);
    }

    // ---------------------------------------------------------------- heightmap
    public static float[,] BuildHeights(StringBuilder log)
    {
        int R = C.HeightmapResolution;
        float W = C.TerrainSize.x, L = C.TerrainSize.z, H = C.TerrainSize.y;
        var h = new float[R, R];
        for (int iz = 0; iz < R; iz++)
            for (int ix = 0; ix < R; ix++)
                h[iz, ix] = BaseHeight(ix / (float)(R - 1) * W, iz / (float)(R - 1) * L);
        // 3x3 平滑化 (基本面のみ)
        var sm = new float[R, R];
        for (int iz = 0; iz < R; iz++)
            for (int ix = 0; ix < R; ix++)
            {
                float sum = 0f; int cnt = 0;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    int z = iz + dz, x = ix + dx;
                    if (z < 0 || z >= R || x < 0 || x >= R) continue;
                    sum += h[z, x]; cnt++;
                }
                sm[iz, ix] = sum / cnt;
            }
        // 平坦部
        for (int iz = 0; iz < R; iz++)
            for (int ix = 0; ix < R; ix++)
                sm[iz, ix] = FlattenedHeight(ix / (float)(R - 1) * W, iz / (float)(R - 1) * L, sm[iz, ix]);
        // 道
        var path = SamplePath();
        float inner = C.PathHalfWidth + C.PathShoulder, outer = inner + C.PathBlend;
        float minH = float.MaxValue, maxH = float.MinValue;
        for (int iz = 0; iz < R; iz++)
            for (int ix = 0; ix < R; ix++)
            {
                float x = ix / (float)(R - 1) * W, z = iz / (float)(R - 1) * L;
                float roadH;
                float d = DistanceToPath(path, x, z, out roadH);
                if (d <= inner) sm[iz, ix] = roadH;
                else if (d < outer) sm[iz, ix] = Mathf.Lerp(roadH, sm[iz, ix], SmoothStep(inner, outer, d));
                minH = Mathf.Min(minH, sm[iz, ix]); maxH = Mathf.Max(maxH, sm[iz, ix]);
            }
        // 正規化
        var norm = new float[R, R];
        for (int iz = 0; iz < R; iz++) for (int ix = 0; ix < R; ix++) norm[iz, ix] = Mathf.Clamp01(sm[iz, ix] / H);
        if (log != null)
        {
            log.AppendLine("heights min=" + minH.ToString("0.0") + " max=" + maxH.ToString("0.0") + " (range " + H + ")");
            log.AppendLine("path samples=" + path.Count + " length=" + PathLength(path).ToString("0.0") + " m");
            float worst = 0f;
            for (int i = 0; i < path.Count; i++) worst = Mathf.Max(worst, Mathf.Abs(path[(i + 1) % path.Count].h - path[i].h));
            log.AppendLine("path profile worst step=" + worst.ToString("0.000") + " m/" + C.PathSampleStep + " m (" + (Mathf.Atan(worst / C.PathSampleStep) * Mathf.Rad2Deg).ToString("0.0") + " deg)");
        }
        return norm;
    }

    // ---------------------------------------------------------------- alphamap
    public static float[,,] BuildAlphamaps(TerrainData td, StringBuilder log)
    {
        int A = td.alphamapResolution, LN = td.terrainLayers.Length;
        float W = td.size.x, L = td.size.z;
        var map = new float[A, A, LN];
        var path = SamplePath();
        float rockySlope = C.RockySlopeDeg;
        int roadCells = 0;
        for (int iz = 0; iz < A; iz++)
            for (int ix = 0; ix < A; ix++)
            {
                float x = ix / (float)(A - 1) * W, z = iz / (float)(A - 1) * L;
                float steep = td.GetSteepness(x / W, z / L);
                float roadH;
                float d = DistanceToPath(path, x, z, out roadH);
                float edgeNoise = (Mathf.PerlinNoise(x * 0.35f + 7f, z * 0.35f + 3f) - 0.5f) * 2f * C.PathEdgeNoise;
                float road = 1f - SmoothStep(C.PathHalfWidth, C.PathHalfWidth + 1.5f, d + edgeNoise);
                bool bank = x < C.BankX0 || x > C.BankX1 || z > C.BankZ0;
                float rocky = Mathf.Clamp01((steep - rockySlope + 5f) / 10f);
                if (bank) rocky = Mathf.Max(rocky, 0.6f);
                float noise = Mathf.PerlinNoise(x * C.LayerNoiseScale + 11f, z * C.LayerNoiseScale + 5f);
                float grassRocky = noise > C.RockyNoiseThreshold ? Mathf.Clamp01((noise - C.RockyNoiseThreshold) * 6f) : 0f;
                float ground = 1f - road;
                float mud = ground * rocky;
                float grass = ground - mud;
                float gr = grass * grassRocky, gh = grass - gr;
                map[iz, ix, LayerGrassHill] = gh;
                map[iz, ix, LayerGrassRocky] = gr;
                map[iz, ix, LayerSand] = road;
                map[iz, ix, LayerMudRocky] = mud;
                float sum = gh + gr + road + mud;
                for (int l = 0; l < LN; l++) map[iz, ix, l] /= sum;
                if (road > 0.5f) roadCells++;
            }
        if (log != null) log.AppendLine("alphamap " + A + "x" + A + " roadCells=" + roadCells);
        return map;
    }
}
