using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-UI-011 §7 (2026-10-16 ユーザー依頼「記号化」): 動画の面の操作ボタンの記号を、フォントにも翻訳にも依存しない絵 (PNG → Sprite) として手続き的に描く。
    /// VizVid 同梱の記号は MSDF の文字なので使わない。
    ///
    /// 形は Material Design のアイコン (24 x 24 の格子) に合わせた: play / pause / stop / sync (↻) / volume / muted / fullscreen / load (投入) / close、
    /// §8 で skip_next (次へ) / repeat (ループ) / repeat_one (ループ + 1) / list (一覧) / add (+)。
    /// 128 x 128、各画素 4 x 4 の標本 (スーパーサンプリング) の被覆率を α にする白い絵。角は小さく丸める。
    /// 凸多角形は「内側へ r 寄せた多角形 + 距離 r」で角を丸めて塗り (標本ごとの内外判定 = 走査線の塗りと同じ結果)、弧と線は距離場で描く。
    /// 既にある PNG は描き直さない (force で描き直す)。KH_VideoPlayerEditor (Player/1) が呼び、Player/2 が Exists / ImporterOk で確かめる。
    /// </summary>
    public static class KH_IconSpriteEditor
    {
        public const string Dir = "Assets/KominkaHillside/Textures/Icons";
        public const int Size = 128, Super = 4;
        const float Grid = 24f;
        public static readonly string[] Names = { "play", "pause", "stop", "sync", "volume", "muted", "fullscreen", "load", "close",
            "skip_next", "repeat", "repeat_one", "list", "add" };   // §8 (2026-10-16): history / queue / repeat

        public static string PathOf(string name) { return Dir + "/KH_Icon_" + name + ".png"; }
        public static Sprite Load(string name) { return AssetDatabase.LoadAssetAtPath<Sprite>(PathOf(name)); }
        public static bool Exists(string name) { return Load(name) != null; }

        /// <summary>Sprite (Single)・Alpha Is Transparency・Mip 無し・無圧縮 (RGBA32)・Clamp か。why に食い違いを書く。</summary>
        public static bool ImporterOk(string name, out string why)
        {
            var imp = AssetImporter.GetAtPath(PathOf(name)) as TextureImporter;
            if (imp == null) { why = "no TextureImporter at " + PathOf(name); return false; }
            var bad = new List<string>();
            if (imp.textureType != TextureImporterType.Sprite) bad.Add("type " + imp.textureType);
            if (imp.spriteImportMode != SpriteImportMode.Single) bad.Add("sprite mode " + imp.spriteImportMode);
            if (!imp.alphaIsTransparency) bad.Add("alpha is transparency off");
            if (imp.mipmapEnabled) bad.Add("mipmaps on");
            if (imp.textureCompression != TextureImporterCompression.Uncompressed) bad.Add("compression " + imp.textureCompression);
            if (imp.wrapMode != TextureWrapMode.Clamp) bad.Add("wrap " + imp.wrapMode);
            // Codex 独立レビュー 2026-10-16 (低): Ensure が書く残りの設定も同じ条件で見る (alphaSource が None だと白い矩形になるのに通っていた)
            if (imp.alphaSource != TextureImporterAlphaSource.FromInput) bad.Add("alpha source " + imp.alphaSource);
            if (imp.spritePixelsPerUnit != 100f) bad.Add("ppu " + imp.spritePixelsPerUnit);
            if (imp.maxTextureSize != Size) bad.Add("max size " + imp.maxTextureSize);
            if (imp.filterMode != FilterMode.Bilinear) bad.Add("filter " + imp.filterMode);
            if (!imp.sRGBTexture) bad.Add("sRGB off");
            why = string.Join(", ", bad);
            return bad.Count == 0;
        }

        public static Sprite[] EnsureAll(bool force, List<string> log)
        {
            var r = new Sprite[Names.Length];
            for (int i = 0; i < Names.Length; i++) r[i] = Ensure(Names[i], force, log);
            return r;
        }

        [MenuItem("Tools/KominkaHillside/Player/8 - Redraw Icons (force)")]
        static void RedrawAll()
        {
            var log = new List<string>();
            EnsureAll(true, log);
            Debug.Log("[KH Player] PLAYER_ICONS_OK " + string.Join(" | ", log));
        }

        public static Sprite Ensure(string name, bool force, List<string> log)
        {
            if (Array.IndexOf(Names, name) < 0) throw new ArgumentException("unknown icon " + name);
            string path = PathOf(name);
            bool exists = File.Exists(path);
            if (!exists || force)
            {
                EnsureFolder(Dir);
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.SetPixels32(Render(Shape(name)));
                tex.Apply(false, false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                log?.Add((exists ? "redrew " : "drew ") + path);
            }
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) throw new InvalidOperationException("icon not imported: " + path);
            if (!ImporterOk(name, out _))
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.filterMode = FilterMode.Bilinear;
                imp.sRGBTexture = true;
                imp.spritePixelsPerUnit = 100f;
                imp.maxTextureSize = Size;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.SaveAndReimport();
                log?.Add("import settings: " + path + " Sprite / alpha is transparency / no mips / RGBA32 uncompressed / clamp");
            }
            var sprite = Load(name);
            if (sprite == null) throw new InvalidOperationException("icon sprite missing after import: " + path);
            return sprite;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'), leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ================================================================ rendering

        /// <summary>被覆率 = 各画素の Super x Super 標本のうち inside が真の割合。座標は 24 の格子で y は下向き (Material の path と同じ)。テクスチャの行 0 は下。</summary>
        static Color32[] Render(Func<Vector2, bool> inside)
        {
            var px = new Color32[Size * Size];
            float step = Grid / Size;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < Super; sy++)
                        for (int sx = 0; sx < Super; sx++)
                        {
                            float gx = (x + (sx + 0.5f) / Super) * step;
                            float gy = Grid - (y + (sy + 0.5f) / Super) * step;
                            if (inside(new Vector2(gx, gy))) hit++;
                        }
                    byte a = (byte)Mathf.RoundToInt(255f * hit / (Super * Super));
                    px[y * Size + x] = new Color32(255, 255, 255, a);
                }
            return px;
        }

        static Func<Vector2, bool> Shape(string name)
        {
            switch (name)
            {
                case "play": { var t = Poly(0.8f, V(8, 5), V(8, 19), V(19, 12)); return t; }
                case "pause": { var a = Rect(6, 5, 10, 19, 1f); var b = Rect(14, 5, 18, 19, 1f); return p => a(p) || b(p); }
                case "stop": return Rect(6, 6, 18, 18, 1.5f);
                case "sync":
                {
                    // ↻: a ring of radius 7 (stroke 2.4) from the right (0°) clockwise on screen through the bottom and the left to the top (270°),
                    // a round cap at the right end, an arrowhead at the top end pointing right (clockwise)
                    var arc = Arc(12, 12, 7f, 1.2f, 0f, 270f, true, false);
                    var head = Poly(0.4f, V(12, 1.4f), V(12, 8.6f), V(17, 5));
                    return p => arc(p) || head(p);
                }
                case "volume":
                {
                    var spk = Speaker();
                    var w1 = Arc(12.5f, 12, 3.5f, 1f, -50f, 50f, true, true);
                    var w2 = Arc(12.5f, 12, 7.5f, 1f, -55f, 55f, true, true);
                    return p => spk(p) || w1(p) || w2(p);
                }
                case "muted":
                {
                    var spk = Speaker();
                    var w1 = Arc(12.5f, 12, 3.5f, 1f, -50f, 50f, true, true);
                    var w2 = Arc(12.5f, 12, 7.5f, 1f, -55f, 55f, true, true);
                    Vector2 a = V(3.5f, 3.5f), b = V(20.5f, 20.5f);
                    // the slash with a clear gap around it (legible at small sizes)
                    return p => { float d = SegDist(p, a, b); return d <= 1.1f || (d > 2.3f && (spk(p) || w1(p) || w2(p))); };
                }
                case "fullscreen":
                {
                    var parts = new List<Func<Vector2, bool>>
                    {
                        Rect(5, 5, 10, 7, 0.5f), Rect(5, 5, 7, 10, 0.5f),        // top left
                        Rect(14, 5, 19, 7, 0.5f), Rect(17, 5, 19, 10, 0.5f),     // top right
                        Rect(5, 17, 10, 19, 0.5f), Rect(5, 14, 7, 19, 0.5f),     // bottom left
                        Rect(14, 17, 19, 19, 0.5f), Rect(17, 14, 19, 19, 0.5f),  // bottom right
                    };
                    return p => { foreach (var f in parts) if (f(p)) return true; return false; };
                }
                case "load":
                {
                    // download / put in: the stem, the head (Material file_download) and the tray under it
                    var stem = Rect(9, 3, 15, 10.5f, 0.6f);
                    var head = Poly(0.6f, V(5, 9), V(19, 9), V(12, 16));
                    var tray = Rect(5, 18, 19, 20.2f, 0.6f);
                    return p => stem(p) || head(p) || tray(p);
                }
                case "skip_next":
                {
                    // Material skip_next: a right triangle and a bar at its tip
                    var tri = Poly(0.6f, V(6, 6), V(6, 18), V(14.5f, 12));
                    var bar = Rect(16, 6, 18, 18, 0.5f);
                    return p => tri(p) || bar(p);
                }
                case "repeat": return Repeat();
                case "repeat_one":
                {
                    // Material repeat_one: the loop and a small "1" in the middle (a stem and its flag)
                    var loop = Repeat();
                    var stem = Rect(11.5f, 9, 13, 15, 0.2f);
                    var flag = Poly(0.2f, V(10, 10), V(12, 9), V(12.2f, 11), V(10, 11));
                    return p => loop(p) || stem(p) || flag(p);
                }
                case "list":
                {
                    // Material list: three dots on the left and three lines
                    var parts = new List<Func<Vector2, bool>>();
                    foreach (float y in new[] { 7f, 11f, 15f }) { parts.Add(Rect(3, y, 5, y + 2, 0.4f)); parts.Add(Rect(7, y, 21, y + 2, 0.4f)); }
                    return p => { foreach (var f in parts) if (f(p)) return true; return false; };
                }
                case "add":
                {
                    var h = Rect(5, 11, 19, 13, 0.4f); var v = Rect(11, 5, 13, 19, 0.4f);
                    return p => h(p) || v(p);
                }
                case "close":
                {
                    Vector2 a = V(5.5f, 5.5f), b = V(18.5f, 18.5f), c = V(18.5f, 5.5f), d = V(5.5f, 18.5f);
                    return p => SegDist(p, a, b) <= 1.15f || SegDist(p, c, d) <= 1.15f;
                }
            }
            throw new ArgumentException("unknown icon " + name);
        }

        /// <summary>Material repeat: the top bar with its left leg and the arrowhead to the right, the bottom bar with its right leg and the arrowhead to the left
        /// (the bars run into the heads so the rounded ends do not notch the joints).</summary>
        static Func<Vector2, bool> Repeat()
        {
            var topBar = Rect(5, 5, 17.5f, 7, 0.4f); var topLeg = Rect(5, 5, 7, 11, 0.4f); var topHead = Poly(0.4f, V(17, 2), V(21, 6), V(17, 10));
            var botBar = Rect(6.5f, 17, 19, 19, 0.4f); var botLeg = Rect(17, 13, 19, 19, 0.4f); var botHead = Poly(0.4f, V(7, 14), V(7, 22), V(3, 18));
            return p => topBar(p) || topLeg(p) || topHead(p) || botBar(p) || botLeg(p) || botHead(p);
        }

        /// <summary>Material volume_up の箱 + 台形 (凹なので 2 つの凸に分ける。箱は台形の中まで伸ばして継ぎ目の丸めを隠す)。</summary>
        static Func<Vector2, bool> Speaker()
        {
            var box = Rect(3, 9, 8, 15, 0.6f);
            var cone = Poly(0.6f, V(7, 9), V(12, 4), V(12, 20), V(7, 15));
            return p => box(p) || cone(p);
        }

        static Vector2 V(float x, float y) { return new Vector2(x, y); }

        static Func<Vector2, bool> Rect(float x0, float y0, float x1, float y1, float r)
        {
            return Poly(r, V(x0, y0), V(x1, y0), V(x1, y1), V(x0, y1));
        }

        /// <summary>凸多角形を角の半径 r で丸めて塗る: 各辺を内側へ r 寄せた多角形 W の内側か、W の辺から r 以内。</summary>
        static Func<Vector2, bool> Poly(float r, params Vector2[] v)
        {
            var w = r > 0f ? Inset(v, r) : v;
            return p => InPoly(w, p) || (r > 0f && EdgeDist(w, p) <= r);
        }

        static Vector2[] Inset(Vector2[] v, float r)
        {
            int n = v.Length;
            var c = Vector2.zero; foreach (var q in v) c += q; c /= n;
            var pts = new Vector2[n]; var dirs = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 a = v[i], b = v[(i + 1) % n], d = (b - a).normalized;
                var nrm = new Vector2(-d.y, d.x);
                if (Vector2.Dot(nrm, c - a) < 0f) nrm = -nrm;
                pts[i] = a + nrm * r; dirs[i] = d;   // edge i (v[i] -> v[i+1]) moved inward
            }
            var w = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;   // vertex i joins edge prev and edge i
                w[i] = Intersect(pts[prev], dirs[prev], pts[i], dirs[i]);
            }
            return w;
        }

        static Vector2 Intersect(Vector2 p, Vector2 d, Vector2 q, Vector2 e)
        {
            float den = d.x * e.y - d.y * e.x;
            if (Mathf.Abs(den) < 1e-6f) return q;
            float t = ((q.x - p.x) * e.y - (q.y - p.y) * e.x) / den;
            return p + d * t;
        }

        static bool InPoly(Vector2[] v, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                if ((v[i].y > p.y) != (v[j].y > p.y) && p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x) inside = !inside;
            return inside;
        }

        static float EdgeDist(Vector2[] v, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < v.Length; i++) best = Mathf.Min(best, SegDist(p, v[i], v[(i + 1) % v.Length]));
            return best;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>中心 (cx, cy)・半径 radius・太さの半分 half の弧。角度は画面の向き (y 下向き、0° = 右、90° = 下) で from → to (from &lt; to)。capFrom / capTo で端を丸める。</summary>
        static Func<Vector2, bool> Arc(float cx, float cy, float radius, float half, float from, float to, bool capFrom, bool capTo)
        {
            var c = V(cx, cy);
            Vector2 Ends(float deg) { float a = deg * Mathf.Deg2Rad; return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius; }
            Vector2 e0 = Ends(from), e1 = Ends(to);
            return p =>
            {
                var d = p - c;
                if (Mathf.Abs(d.magnitude - radius) <= half)
                {
                    float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    // bring the angle into [from, from + 360)
                    while (ang < from) ang += 360f;
                    while (ang >= from + 360f) ang -= 360f;
                    if (ang <= to) return true;
                }
                return (capFrom && Vector2.Distance(p, e0) <= half) || (capTo && Vector2.Distance(p, e1) <= half);
            };
        }
    }
}
