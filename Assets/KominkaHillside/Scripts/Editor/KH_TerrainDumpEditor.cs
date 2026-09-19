// 現在のシーンの Terrain (高さ / splat / Detail 合計) と KH_World の主要 Transform を JSON に書き出す (解析用)。
// 手で編集した地形を数値で把握し、Build の設定へ写すために使う。出力: Temp/KH_TerrainPrototype/terrain-dump.json
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class KH_TerrainDumpEditor
{
    const string OutPath = "Temp/KH_TerrainPrototype/terrain-dump.json";

    [MenuItem("Tools/KominkaHillside/Terrain Prototype/Dump Terrain To JSON")]
    public static void Dump()
    {
        var root = GameObject.Find("KH_World");
        var terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogError("KH_TerrainDumpEditor: KH_World/terrain not found"); return; }
        var td = terrain.terrainData;
        var sb = new StringBuilder();
        sb.Append("{\"size\":[").Append(td.size.x).Append(',').Append(td.size.y).Append(',').Append(td.size.z).Append("],");
        int R = td.heightmapResolution;
        var h = td.GetHeights(0, 0, R, R);
        sb.Append("\"hm\":").Append(R).Append(",\"heights\":[");
        for (int z = 0; z < R; z++) for (int x = 0; x < R; x++) { if (z + x > 0) sb.Append(','); sb.Append((h[z, x] * td.size.y).ToString("0.00")); }
        sb.Append("],");
        int A = td.alphamapResolution; int L = td.terrainLayers.Length;
        var a = td.GetAlphamaps(0, 0, A, A);
        sb.Append("\"alpha\":").Append(A).Append(",\"layers\":[");
        for (int l = 0; l < L; l++) { if (l > 0) sb.Append(','); sb.Append('"').Append(td.terrainLayers[l] != null && td.terrainLayers[l].diffuseTexture != null ? td.terrainLayers[l].diffuseTexture.name : "null").Append('"'); }
        sb.Append("],\"splat\":[");
        bool first = true;
        for (int z = 0; z < A; z++) for (int x = 0; x < A; x++) for (int l = 0; l < L; l++) { if (!first) sb.Append(','); first = false; sb.Append(a[z, x, l].ToString("0.00")); }
        sb.Append("],");
        // Detail 合計 (全枠)
        int D = td.detailWidth; var sum = new int[td.detailHeight, D];
        for (int l = 0; l < td.detailPrototypes.Length; l++) { var m = td.GetDetailLayer(0, 0, D, td.detailHeight, l); for (int z = 0; z < td.detailHeight; z++) for (int x = 0; x < D; x++) sum[z, x] += m[z, x]; }
        sb.Append("\"detail\":").Append(D).Append(",\"detailSum\":[");
        for (int z = 0; z < td.detailHeight; z++) for (int x = 0; x < D; x++) { if (z + x > 0) sb.Append(','); sb.Append(sum[z, x]); }
        sb.Append("],\"objects\":[");
        first = true;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root.transform) continue;
            int depth = 0; var p = t; while (p.parent != null && p.parent != root.transform) { p = p.parent; depth++; }
            if (depth > 1) continue; // 主要階層だけ
            if (!first) sb.Append(','); first = false;
            sb.Append("{\"path\":\"").Append(Path(t, root.transform)).Append("\",\"pos\":[").Append(t.position.x.ToString("0.00")).Append(',').Append(t.position.y.ToString("0.00")).Append(',').Append(t.position.z.ToString("0.00"))
              .Append("],\"yaw\":").Append(t.eulerAngles.y.ToString("0.0")).Append(",\"scale\":").Append(t.lossyScale.x.ToString("0.00")).Append('}');
        }
        sb.Append("],\"kominkaParts\":[");
        var kom = root.transform.Find("KH_Places/KH_FarKominka");
        first = true;
        if (kom != null)
            foreach (var r in kom.GetComponentsInChildren<Renderer>(false))
            {
                if (!first) sb.Append(','); first = false;
                var b = r.bounds;
                sb.Append("{\"name\":\"").Append(Escape(r.name)).Append("\",\"parent\":\"").Append(Escape(r.transform.parent != null ? r.transform.parent.name : "")).Append("\",\"center\":[").Append(b.center.x.ToString("0.00")).Append(',').Append(b.center.y.ToString("0.00")).Append(',').Append(b.center.z.ToString("0.00"))
                  .Append("],\"size\":[").Append(b.size.x.ToString("0.00")).Append(',').Append(b.size.y.ToString("0.00")).Append(',').Append(b.size.z.ToString("0.00")).Append("]}");
            }
        sb.Append("]}");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OutPath));
        File.WriteAllText(OutPath, sb.ToString());
        Debug.Log("KH_TerrainDumpEditor -> " + OutPath + " (" + sb.Length + " chars)");
    }

    static string Path(Transform t, Transform root)
    {
        var parts = new List<string>();
        while (t != null && t != root) { parts.Insert(0, t.name); t = t.parent; }
        return Escape(string.Join("/", parts.ToArray()));
    }

    static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) { if (c == '"' || c == '\\') sb.Append('\\').Append(c); else if (c < 128) sb.Append(c); else sb.Append("\\u").Append(((int)c).ToString("x4")); }
        return sb.ToString();
    }
}
