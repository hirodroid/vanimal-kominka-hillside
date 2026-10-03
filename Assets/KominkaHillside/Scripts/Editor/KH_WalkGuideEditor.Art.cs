using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    // IMP-UI-002 (docs/plans/2026-10-03-onsite-guides.md §2.5): the walk strip's picture, KH_WalkGuide.png (1024 x 512, a 4 x 2 grid of
    // 256² cells), drawn from signed-distance shapes (AI-made by code, no external image) in the wash strip's style
    // (KH_DogWashSetupEditor.Guide.cs: white line art on a transparent background, amber marks). Guide/10 redraws it alone; Guide/11
    // calls the same EnsureArt. WalkArtVersion names the drawing; the PNG is rewritten only when its bytes change.
    public static partial class KH_WalkGuideEditor
    {
        public const string ArtPath = "Assets/KominkaHillside/Textures/Guide/KH_WalkGuide.png";
        /// <summary>The drawing's version (KH_WalkGuideArt). Bump it when the drawing changes (the build report records it).</summary>
        public const int WalkArtVersion = 1;
        public const int ArtMaxSize = 1024;

        // ---- the cells (Unity's UV origin is the bottom-left: the grid's top row (cells 0..3) is v 0.5..1) ----
        // 0 VR ① take the tip from the rack, 1 VR ② the tip at the collar (green), 2 ③ the walk tab's destination tiles (both),
        // 3 VR ④ press the collar; 4 Flat ① the menu near the dog, 5 Flat ② the row's left / right buttons, 6 Flat ④ the row's
        // wide [unleash] button; 7 empty.
        public static Rect CellUv(int cell) { return new Rect((cell % 4) * 0.25f, cell < 4 ? 0.5f : 0f, 0.25f, 0.5f); }
        static readonly int[] VrCells = { 0, 1, 2, 3 }, FlatCells = { 4, 5, 2, 6 };

        [MenuItem(Menu + "10 - Draw Walk Guide Art")]
        public static void DrawArt()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Walk Guide] Edit mode only (not compiling)");
            var log = new System.Collections.Generic.List<string>();
            bool written;
            EnsureArt(log, out written);
            Debug.Log("[KH Walk Guide] art v" + WalkArtVersion + " " + ArtPath + (written ? " written" : " unchanged (same bytes)") + "; " + string.Join(" | ", log));
        }

        /// <summary>Draws the picture, writes the PNG when its bytes differ from the file, imports it with the wash guide's settings
        /// (re-imported only when one differs) and returns the imported texture.</summary>
        static Texture2D EnsureArt(System.Collections.Generic.List<string> log, out bool written)
        {
            string dir = Path.GetDirectoryName(ArtPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder(Path.GetDirectoryName(dir).Replace('\\', '/'), Path.GetFileName(dir));
                log.Add("created folder " + dir);
            }
            var rgba = KH_WalkGuideArt.RenderBytes();
            var tex = new Texture2D(KH_WalkGuideArt.Width, KH_WalkGuideArt.Height, TextureFormat.RGBA32, false, false);
            byte[] png;
            try
            {
                var px = new Color32[KH_WalkGuideArt.Width * KH_WalkGuideArt.Height];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(rgba[4 * i], rgba[4 * i + 1], rgba[4 * i + 2], rgba[4 * i + 3]);
                tex.SetPixels32(px);   // row 0 = the bottom (the drawing's y is up)
                tex.Apply(false, false);
                png = tex.EncodeToPNG();
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
            written = !File.Exists(ArtPath) || !File.ReadAllBytes(ArtPath).SequenceEqual(png);
            if (written)
            {
                File.WriteAllBytes(ArtPath, png);
                AssetDatabase.ImportAsset(ArtPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                log.Add("art v" + WalkArtVersion + " written to " + ArtPath + " (" + png.Length + " bytes)");
            }
            var ti = AssetImporter.GetAtPath(ArtPath) as TextureImporter;
            if (ti == null) { AssetDatabase.ImportAsset(ArtPath, ImportAssetOptions.ForceSynchronousImport); ti = AssetImporter.GetAtPath(ArtPath) as TextureImporter; }
            if (ti == null) throw new InvalidOperationException("TextureImporter missing: " + ArtPath);
            if (ConfigureImporter(ti)) { ti.SaveAndReimport(); log.Add("art import settings applied (" + ArtPath + ")"); }
            var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath);
            if (loaded == null) throw new InvalidOperationException("Texture not imported: " + ArtPath);
            return loaded;
        }

        /// <summary>The wash guide's import settings (KH_DogWashSetupEditor.ConfigureGuideImporter): sRGB, alpha is transparency,
        /// Read/Write off, mipmaps on, clamp, bilinear, 1024 max; PC DXT5, Android / iOS ASTC 6x6. Returns whether something changed.</summary>
        static bool ConfigureImporter(TextureImporter ti)
        {
            bool changed = false;
            void Set<T>(T now, T want, Action<T> apply) { if (!Equals(now, want)) { apply(want); changed = true; } }
            Set(ti.textureType, TextureImporterType.Default, v => ti.textureType = v);
            Set(ti.textureShape, TextureImporterShape.Texture2D, v => ti.textureShape = v);
            Set(ti.sRGBTexture, true, v => ti.sRGBTexture = v);
            Set(ti.alphaSource, TextureImporterAlphaSource.FromInput, v => ti.alphaSource = v);
            Set(ti.alphaIsTransparency, true, v => ti.alphaIsTransparency = v);
            Set(ti.isReadable, false, v => ti.isReadable = v);
            Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            Set(ti.wrapMode, TextureWrapMode.Clamp, v => ti.wrapMode = v);
            Set(ti.filterMode, FilterMode.Bilinear, v => ti.filterMode = v);
            Set(ti.npotScale, TextureImporterNPOTScale.None, v => ti.npotScale = v);
            Set(ti.maxTextureSize, ArtMaxSize, v => ti.maxTextureSize = v);
            Set(ti.textureCompression, TextureImporterCompression.Compressed, v => ti.textureCompression = v);
            foreach (var (platform, format) in PlatformFormats)
            {
                var ps = ti.GetPlatformTextureSettings(platform);
                if (!ps.overridden || ps.format != format || ps.maxTextureSize != ArtMaxSize)
                {
                    ps.name = platform; ps.overridden = true; ps.format = format; ps.maxTextureSize = ArtMaxSize; ps.compressionQuality = 50;
                    ti.SetPlatformTextureSettings(ps);
                    changed = true;
                }
            }
            return changed;
        }

        static readonly (string, TextureImporterFormat)[] PlatformFormats =
            { ("Standalone", TextureImporterFormat.DXT5), ("Android", TextureImporterFormat.ASTC_6x6), ("iPhone", TextureImporterFormat.ASTC_6x6) };
    }

    // ---- ART CORE BEGIN (plain C#: no UnityEngine, so the drawing can be rendered and looked at outside Unity) ----
    /// <summary>IMP-UI-002 (onsite-guides §2.5): the walk strip's picture. 1024 x 512 RGBA (straight alpha, row 0 = bottom), a 4 x 2
    /// grid of 256² cells. Cell index = row * 4 + column, row 0 = the TOP row. 0 VR ①: the rack's crossbar with two hanging leash
    /// tips (rings), a hand lifting one; 1 VR ②: close-up of a dog's neck and collar, a hand bringing the tip to the collar's ring,
    /// the tip green (KH_ClipReady) with a glow; 2 ③: the hand menu's walk tab drawn as shapes (tabs, dog rows, 2 x 3 destination
    /// tiles), one tile marked and touched; 3 VR ④: a pointing hand's ray on the dog's collar, the leash tip coming off;
    /// 4 Flat ①: the menu open next to a dog, a dashed circle around the dog (the 4 m reach); 5 Flat ②: three dog rows of the menu,
    /// the middle row's two leash buttons marked, the pointer on one; 6 Flat ④: the middle row's single wide button marked; 7 empty.
    /// No words, no digits (13 languages: the words are the strip's TMP labels). Each cell is drawn in display units W x H = the Pic
    /// RawImage (170 x 120 px). The shapes (dog, hand, palette, line widths) are copied from KH_WashGuideArt so the two strips
    /// match; a shared kit is the optional wave 4 of the plan.</summary>
    internal static class KH_WalkGuideArt
    {
        public const int CellPx = 256, Columns = 4, Rows = 2, Width = CellPx * Columns, Height = CellPx * Rows;
        public const float W = 170f, H = 120f;
        const float Aa = 0.55f;
        const float EdgeFade = 4f;

        // ---------------------------------------------------------------- tiny vector / colour types (as KH_WashGuideArt)

        public struct V
        {
            public float x, y;
            public V(float x, float y) { this.x = x; this.y = y; }
            public static V operator +(V a, V b) { return new V(a.x + b.x, a.y + b.y); }
            public static V operator -(V a, V b) { return new V(a.x - b.x, a.y - b.y); }
            public static V operator -(V a) { return new V(-a.x, -a.y); }
            public static V operator *(V a, float s) { return new V(a.x * s, a.y * s); }
            public static V operator /(V a, float s) { return new V(a.x / s, a.y / s); }
            public float Len { get { return (float)Math.Sqrt(x * x + y * y); } }
            public static float Dot(V a, V b) { return a.x * b.x + a.y * b.y; }
            public V Rot(float deg) { double r = deg * Math.PI / 180.0; float c = (float)Math.Cos(r), s = (float)Math.Sin(r); return new V(c * x - s * y, s * x + c * y); }
            public static V Dir(float deg) { return new V(1f, 0f).Rot(deg); }
        }

        public struct Rgba
        {
            public float r, g, b, a;
            public Rgba(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        }

        delegate float Sdf(V p);

        // ---------------------------------------------------------------- palette

        static readonly Rgba Line = new Rgba(1f, 1f, 1f);
        static readonly Rgba DogFill = new Rgba(1f, 1f, 1f, 0.13f);
        static readonly Rgba Motion = new Rgba(1f, 0.82f, 0.35f);                  // the wash strip's amber marks
        static readonly Rgba MarkFill = new Rgba(1f, 0.82f, 0.35f, 0.30f);
        static readonly Rgba Skin = new Rgba(1f, 0.86f, 0.74f);
        static readonly Rgba SkinCrease = new Rgba(0.82f, 0.60f, 0.50f);
        static readonly Rgba Sleeve = new Rgba(0.60f, 0.72f, 0.90f);
        static readonly Rgba Wood = new Rgba(0.58f, 0.40f, 0.24f);                  // KH_RackWood (0.32 / 0.19 / 0.10), lifted for the dark strip
        static readonly Rgba TipNeutral = new Rgba(0.80f, 0.63f, 0.27f);            // KH_ClipNeutral
        static readonly Rgba TipReady = new Rgba(0.20f, 0.80f, 0.45f);              // KH_ClipReady (the tip in range of a free dog's collar)
        static readonly Rgba Collar = new Rgba(0.86f, 0.32f, 0.26f);
        static readonly Rgba Metal = new Rgba(0.86f, 0.87f, 0.90f);
        static readonly Rgba Panel = new Rgba(0.22f, 0.25f, 0.31f, 0.96f);
        static readonly Rgba Faint = new Rgba(1f, 1f, 1f, 0.22f);
        static readonly Rgba Soft = new Rgba(1f, 1f, 1f, 0.55f);
        static readonly Rgba Ink = new Rgba(0.10f, 0.10f, 0.12f);

        // ---------------------------------------------------------------- signed distances (negative inside)

        static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
        static float Smoothstep(float a, float b, float x) { float t = Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        static float SdCircle(V p, V c, float r) { return (p - c).Len - r; }
        static float SdEllipse(V p, V c, float rx, float ry)
        {
            V q = p - c;
            float k0 = new V(q.x / rx, q.y / ry).Len, k1 = new V(q.x / (rx * rx), q.y / (ry * ry)).Len;
            if (k1 < 1e-6f) return -Math.Min(rx, ry);
            return k0 * (k0 - 1f) / k1;
        }
        static float SdCapsule(V p, V a, V b, float r)
        {
            V pa = p - a, ba = b - a;
            float h = Clamp01(V.Dot(pa, ba) / Math.Max(1e-6f, V.Dot(ba, ba)));
            return (pa - ba * h).Len - r;
        }
        static float SdRoundBox(V p, V c, float hx, float hy, float r, float deg = 0f)
        {
            V q = (p - c).Rot(-deg);
            float dx = Math.Abs(q.x) - (hx - r), dy = Math.Abs(q.y) - (hy - r);
            float outside = new V(Math.Max(dx, 0f), Math.Max(dy, 0f)).Len;
            return outside + Math.Min(Math.Max(dx, dy), 0f) - r;
        }
        static float SdTriangle(V p, V p0, V p1, V p2)
        {
            V e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
            V v0 = p - p0, v1 = p - p1, v2 = p - p2;
            V pq0 = v0 - e0 * Clamp01(V.Dot(v0, e0) / V.Dot(e0, e0));
            V pq1 = v1 - e1 * Clamp01(V.Dot(v1, e1) / V.Dot(e1, e1));
            V pq2 = v2 - e2 * Clamp01(V.Dot(v2, e2) / V.Dot(e2, e2));
            float s = Math.Sign(e0.x * e2.y - e0.y * e2.x);
            float dA = V.Dot(pq0, pq0), sA = s * (v0.x * e0.y - v0.y * e0.x);
            float dB = V.Dot(pq1, pq1), sB = s * (v1.x * e1.y - v1.y * e1.x);
            float dC = V.Dot(pq2, pq2), sC = s * (v2.x * e2.y - v2.y * e2.x);
            float d = Math.Min(dA, Math.Min(dB, dC)), sg = Math.Min(sA, Math.Min(sB, sC));
            return -(float)Math.Sqrt(d) * Math.Sign(sg);
        }
        static float SdRing(V p, V c, float radius, float halfWidth) { return Math.Abs((p - c).Len - radius) - halfWidth; }
        static float Smin(float a, float b, float k)
        {
            float h = Clamp01(0.5f + 0.5f * (b - a) / k);
            return Lerp(b, a, h) - k * h * (1f - h);
        }

        // ---------------------------------------------------------------- the canvas (as KH_WashGuideArt)

        sealed class Canvas
        {
            public readonly float[] px = new float[Width * Height * 4];
            int cell;
            public void Cell(int c) { cell = c; }
            public void Fill(Sdf f, Rgba c) { Paint(f, c, -1f); }
            public void Stroke(Sdf f, float halfWidth, Rgba c) { Paint(f, c, halfWidth); }
            public void FillStroke(Sdf f, Rgba fill, float halfWidth, Rgba line) { Fill(f, fill); Stroke(f, halfWidth, line); }

            void Paint(Sdf f, Rgba c, float stroke)
            {
                int x0 = (cell % Columns) * CellPx;
                int y0 = (Rows - 1 - cell / Columns) * CellPx;   // row 0 of the grid is the top of the texture
                for (int j = 0; j < CellPx; j++)
                {
                    float y = (j + 0.5f) / CellPx * H;
                    for (int i = 0; i < CellPx; i++)
                    {
                        float x = (i + 0.5f) / CellPx * W;
                        float edge = Math.Min(Math.Min(x, W - x), Math.Min(y, H - y));
                        if (edge <= 0f) continue;
                        float d = f(new V(x, y));
                        if (stroke >= 0f) d = Math.Abs(d) - stroke;
                        float cover = 1f - Smoothstep(-Aa, Aa, d);
                        if (cover <= 0f) continue;
                        Blend(((y0 + j) * Width + x0 + i) * 4, c, cover * Smoothstep(0f, EdgeFade, edge));
                    }
                }
            }

            void Blend(int k, Rgba c, float cover)
            {
                float a = c.a * cover;
                if (a <= 0f) return;
                float da = px[k + 3], oa = a + da * (1f - a);
                px[k] = (c.r * a + px[k] * da * (1f - a)) / oa;
                px[k + 1] = (c.g * a + px[k + 1] * da * (1f - a)) / oa;
                px[k + 2] = (c.b * a + px[k + 2] * da * (1f - a)) / oa;
                px[k + 3] = oa;
            }
        }

        // ---------------------------------------------------------------- the dog (KH_WashGuideArt's outline) and its collar

        static float DogLocal(V p)
        {
            float d = SdEllipse(p, new V(0f, 0f), 34f, 15f);
            d = Smin(d, SdCircle(p, new V(22f, -1f), 14f), 4f);
            d = Smin(d, SdCircle(p, new V(-22f, -1f), 13f), 4f);
            d = Smin(d, SdCapsule(p, new V(26f, 6f), new V(36f, 18f), 9f), 4f);
            d = Smin(d, SdCircle(p, new V(42f, 22f), 12f), 3f);
            d = Smin(d, SdCapsule(p, new V(46f, 18f), new V(57f, 15f), 5.5f), 2f);
            float legs = SdCapsule(p, new V(24f, -8f), new V(26f, -31f), 4.5f);
            legs = Math.Min(legs, SdCapsule(p, new V(14f, -8f), new V(12f, -31f), 4.3f));
            legs = Math.Min(legs, SdCapsule(p, new V(-24f, -9f), new V(-28f, -31f), 4.8f));
            legs = Math.Min(legs, SdCapsule(p, new V(-14f, -9f), new V(-11f, -31f), 4.4f));
            float paws = Math.Min(Math.Min(SdEllipse(p, new V(28.5f, -32.5f), 5.2f, 3f), SdEllipse(p, new V(14.5f, -32.5f), 5f, 3f)),
                                  Math.Min(SdEllipse(p, new V(-25.5f, -32.5f), 5.4f, 3f), SdEllipse(p, new V(-8.5f, -32.5f), 5f, 3f)));
            d = Smin(d, Math.Min(legs, paws), 3f);
            float ear = SdTriangle(p, new V(34f, 30f), new V(39.5f, 42f), new V(46f, 31.5f)) - 1.3f;
            float tail = Math.Min(SdRing(p, new V(-36f, 17f), 7f, 2.6f), SdCapsule(p, new V(-31f, 7f), new V(-37f, 11f), 2.8f));
            return Math.Min(d, Math.Min(ear, tail));
        }

        static void Dog(Canvas c, V at, float s)
        {
            Sdf dog = p => DogLocal((p - at) / s) * s;
            c.FillStroke(dog, DogFill, 1.15f, Line);
            c.Fill(p => SdCircle(p, at + new V(47f, 25f) * s, 1.7f), Line);
            c.Fill(p => SdCircle(p, at + new V(58.5f, 17f) * s, 2.2f), Line);
        }

        // The collar in the dog's local units: a band across the neck (perpendicular to the neck's axis (26,6)->(36,18)) and the
        // metal ring at the throat, where the tip clips on.
        static readonly V CollarMid = new V(33f, 14.2f), NeckPerp = new V(-0.77f, 0.64f);
        /// <summary>The collar's ring (the clip point) in canvas units for a dog at `at`, scale s.</summary>
        static V CollarRing(V at, float s) { return at + (CollarMid - NeckPerp * 12.6f) * s; }

        static void DogCollar(Canvas c, V at, float s, bool glow)
        {
            V a = at + (CollarMid + NeckPerp * 10.2f) * s, b = at + (CollarMid - NeckPerp * 10.2f) * s;
            Sdf band = p => SdCapsule(p, a, b, 2.5f * s);
            c.FillStroke(band, Collar, 0.9f, Line);
            V ring = CollarRing(at, s);
            if (glow) c.Stroke(p => SdCircle(p, ring, 6.2f * s), 1.2f, Motion);
            c.Stroke(p => SdCircle(p, ring, 2.6f * s), 0.75f * s, Metal);
        }

        // ---------------------------------------------------------------- hands (KH_WashGuideArt.Hand) and a pointing hand

        static void Hand(Canvas c, V tips, float fingerDeg, float s, float thumbSide, Action held = null)
        {
            float deg = fingerDeg - 90f;
            V origin = tips - (new V(0f, 25f) * s).Rot(deg);
            Func<V, V> L = p => { V q = (p - origin).Rot(-deg) / s; q.x *= thumbSide; return q; };
            Sdf palmAndFingers = p =>
            {
                V q = L(p);
                float d = SdRoundBox(q, new V(0f, 0f), 10f, 9.5f, 5f);
                d = Smin(d, SdRoundBox(q, new V(0f, 14f), 9.6f, 11f, 7.5f), 2f);
                d = Smin(d, SdCapsule(q, new V(0f, -6f), new V(0f, -60f), 7.2f), 2f);
                return d * s;
            };
            Sdf thumb = p => SdCapsule(L(p), new V(8f, -2f), new V(17.5f, 9.5f), 3.8f) * s;
            Sdf sleeve = p => { V q = L(p); return SdRoundBox(q, new V(0f, -58f), 10.5f, 24f, 3f) * s; };
            if (held == null) c.FillStroke(thumb, Skin, 1.0f, Line);
            c.FillStroke(palmAndFingers, Skin, 1.0f, Line);
            if (held == null) c.Fill(p => SdCapsule(L(p), new V(7f, -3f), new V(9.5f, 0.5f), 3.2f) * s, Skin);
            for (int k = -1; k <= 1; k++)
            {
                float x = k * 4.8f;
                c.Stroke(p => SdCapsule(L(p), new V(x, 15.5f), new V(x, 23.5f), 0f) * s, 0.5f, SkinCrease);
            }
            c.FillStroke(sleeve, Sleeve, 1.0f, Line);
            if (held != null) { held(); c.FillStroke(thumb, Skin, 1.0f, Line); }
        }

        /// <summary>A hand pointing with the index finger: a fist (local palm centre at the origin), the index finger along +y to
        /// y 33, the thumb folded over the fist, forearm and sleeve. Placed with the fingertip at `tip`, pointing along fingerDeg.</summary>
        static void PointingHand(Canvas c, V tip, float fingerDeg, float s, float thumbSide)
        {
            float deg = fingerDeg - 90f;
            V origin = tip - (new V(-3.5f, 33f) * s).Rot(deg);
            Func<V, V> L = p => { V q = (p - origin).Rot(-deg) / s; q.x *= thumbSide; return q; };
            Sdf fist = p =>
            {
                V q = L(p);
                float d = SdRoundBox(q, new V(0f, 2f), 10.2f, 11f, 5.5f);
                d = Smin(d, SdCapsule(q, new V(-3.5f, 9f), new V(-3.5f, 33f), 3.1f), 1.2f);   // the index finger
                d = Smin(d, SdCapsule(q, new V(0f, -6f), new V(0f, -60f), 7.2f), 2f);
                return d * s;
            };
            Sdf thumb = p => SdCapsule(L(p), new V(8.5f, -1f), new V(1.5f, 7.5f), 3.4f) * s;   // folded across the fist
            Sdf sleeve = p => { V q = L(p); return SdRoundBox(q, new V(0f, -58f), 10.5f, 24f, 3f) * s; };
            c.FillStroke(fist, Skin, 1.0f, Line);
            for (int k = 0; k < 2; k++)                         // the folded fingers' creases
            {
                float x = 1.5f + k * 4.2f;
                c.Stroke(p => SdCapsule(L(p), new V(x, 8f), new V(x, 12.5f), 0f) * s, 0.5f, SkinCrease);
            }
            c.FillStroke(thumb, Skin, 0.9f, Line);
            c.FillStroke(sleeve, Sleeve, 1.0f, Line);
        }

        // ---------------------------------------------------------------- the leash tip and marks

        /// <summary>The leash tip as the world has it (a metal ring, KH_LeashTip): drawn large and thick, with a small snap gate.</summary>
        static void Tip(Canvas c, V at, float r, Rgba col)
        {
            c.Fill(p => SdRing(p, at, r, r * 0.30f), col);
            c.Stroke(p => SdRing(p, at, r, r * 0.30f), 0.7f, Line);
            c.Fill(p => SdCapsule(p, at + new V(-r * 0.55f, r * 0.84f), at + new V(r * 0.55f, r * 0.84f), r * 0.16f), Line);   // the gate
        }

        static void Ticks(Canvas c, V at, float deg, float spread, int n, float r0, float r1, Rgba col)
        {
            for (int k = 0; k < n; k++)
            {
                float a = n == 1 ? deg : deg - spread * 0.5f + spread * k / (n - 1);
                V d = V.Dir(a), p0 = at + d * r0, p1 = at + d * r1;
                c.Fill(p => SdCapsule(p, p0, p1, 1.25f), col);
            }
        }

        static void Arrow(Canvas c, V a, V b, Rgba col)
        {
            V dir = (b - a) / (b - a).Len, n = new V(-dir.y, dir.x);
            const float head = 6.5f, half = 4.4f, shaft = 1.4f;
            V b1 = b - dir * (head * 0.8f);
            c.Fill(p => Math.Min(SdCapsule(p, a, b1, shaft), SdTriangle(p, b, b - dir * head - n * half, b - dir * head + n * half) - 0.4f), col);
        }

        static void DashedLine(Canvas c, V a, V b, float dash, float halfWidth, Rgba col)
        {
            float len = (b - a).Len; V dir = (b - a) / len;
            for (float t = 0f; t < len; t += dash * 2f)
            {
                V p0 = a + dir * t, p1 = a + dir * Math.Min(len, t + dash);
                c.Fill(p => SdCapsule(p, p0, p1, halfWidth), col);
            }
        }

        static void DashedEllipse(Canvas c, V at, float rx, float ry, int dashes, float halfWidth, Rgba col)
        {
            int n = dashes * 2;
            for (int k = 0; k < n; k += 2)
            {
                double a0 = 2 * Math.PI * k / n, a1 = 2 * Math.PI * (k + 1) / n;
                V p0 = at + new V((float)Math.Cos(a0) * rx, (float)Math.Sin(a0) * ry), p1 = at + new V((float)Math.Cos(a1) * rx, (float)Math.Sin(a1) * ry);
                c.Fill(p => SdCapsule(p, p0, p1, halfWidth), col);
            }
        }

        /// <summary>A mouse pointer (tip at `tip`, pointing up-left), white with a dark rim. On a phone it stands for the tap.</summary>
        static void Pointer(Canvas c, V tip, float s)
        {
            Sdf f = p =>
            {
                V q = (p - tip) / s;
                float d = SdTriangle(q, new V(0f, 0f), new V(0f, -17f), new V(12f, -12.5f));
                d = Math.Min(d, SdCapsule(q, new V(5f, -11f), new V(8.6f, -19f), 2.1f));
                return d * s;
            };
            c.FillStroke(f, Line, 0.9f, Ink);
        }

        /// <summary>A marked control: amber fill and a thick amber outline (the wash strip's motion colour).</summary>
        static void Marked(Canvas c, Sdf f)
        {
            c.Fill(f, MarkFill);
            c.Stroke(f, 1.5f, Motion);
        }

        // ---------------------------------------------------------------- the menu drawn as shapes (no words)

        /// <summary>The walk tab, small: the panel, 7 tab pills (the first lit), 6 dog rows on the left, 2 x 3 destination tiles and the
        /// map button on the right. Returns the centre of tile (col, row) for the marks.</summary>
        static void MiniMenu(Canvas c, V at, float s, int markCol, int markRow)
        {
            Func<float, float, V> P = (x, y) => at + new V(x, y) * s;
            Sdf panel = p => SdRoundBox(p, at, 72f * s, 50f * s, 6f * s);
            c.FillStroke(panel, Panel, 0.9f, Line);
            for (int k = 0; k < 7; k++)
            {
                V t = P(-57f + k * 19f, 40f);
                Sdf pill = p => SdRoundBox(p, t, 8f * s, 3.4f * s, 3f * s);
                if (k == 0) c.Fill(pill, Motion); else c.Fill(pill, Faint);
            }
            c.Fill(p => SdCapsule(p, P(-66f, 33f), P(66f, 33f), 0.35f * s), Faint);
            for (int k = 0; k < 6; k++)
            {
                float y = 25f - k * 11.5f;
                V dot = P(-62f, y);
                c.Fill(p => SdCircle(p, dot, 2.3f * s), Soft);
                V n0 = P(-56f, y), n1 = P(-40f, y);
                c.Fill(p => SdCapsule(p, n0, n1, 1.5f * s), Soft);
                V b0 = P(-23f, y), b1 = P(-8f, y);
                c.Fill(p => SdRoundBox(p, b0, 6.5f * s, 4.2f * s, 1.6f * s), Faint);
                c.Fill(p => SdRoundBox(p, b1, 6.5f * s, 4.2f * s, 1.6f * s), Faint);
            }
            c.Fill(p => SdCapsule(p, P(0f, 30f), P(0f, -42f), 0.35f * s), Faint);   // the column rule
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 2; col++)
                {
                    V t = P(21f + col * 30f, 18f - row * 17f);
                    Sdf tile = p => SdRoundBox(p, t, 13.5f * s, 6.8f * s, 2.2f * s);
                    if (col == markCol && row == markRow) Marked(c, tile); else c.Fill(tile, Faint);
                }
            V m = P(36f, -38f);
            c.Fill(p => SdRoundBox(p, m, 28.5f * s, 5.2f * s, 2.2f * s), Faint);   // [map and travel]
        }

        /// <summary>Three dog rows of the menu, large. Middle row: `wide` = one wide button (own dog: [unleash]), otherwise the two
        /// leash buttons; marked. The rows: a colour dot, the name bar, the state bar under it.</summary>
        static void MenuRows(Canvas c, bool wide)
        {
            Sdf panel = p => SdRoundBox(p, new V(85f, 60f), 78f, 50f, 6f);
            c.FillStroke(panel, Panel, 0.9f, Line);
            for (int k = 0; k < 3; k++)
            {
                float y = 91f - k * 31f;
                bool mid = k == 1;
                Sdf row = p => SdRoundBox(p, new V(85f, y), 72f, 13f, 3f);
                c.Fill(row, mid && wide ? MarkFill : new Rgba(1f, 1f, 1f, 0.07f));
                c.Fill(p => SdCircle(p, new V(24f, y + 2f), 4f), Soft);
                c.Fill(p => SdCapsule(p, new V(33f, y + 4f), new V(62f, y + 4f), 2.4f), Soft);
                c.Fill(p => SdCapsule(p, new V(33f, y - 4.5f), new V(54f, y - 4.5f), 1.4f), Faint);
                if (mid && wide)
                {
                    Marked(c, p => SdRoundBox(p, new V(124f, y), 30f, 8.5f, 2.6f));
                }
                else
                {
                    Sdf l = p => SdRoundBox(p, new V(108f, y), 14f, 8.5f, 2.6f), r = p => SdRoundBox(p, new V(140f, y), 14f, 8.5f, 2.6f);
                    if (mid) { Marked(c, l); Marked(c, r); }
                    else { c.Fill(l, Faint); c.Fill(r, Faint); }
                }
            }
        }

        // ---------------------------------------------------------------- the cells

        /// <summary>0: VR ① the rack's crossbar on two posts, a tip hanging on the left peg, a hand lifting the right one (ticks up).</summary>
        static void CellTake(Canvas c)
        {
            c.Cell(0);
            c.FillStroke(p => SdRoundBox(p, new V(85f, 96f), 76f, 5f, 1.5f), Wood, 0.9f, Line);       // crossbar
            c.FillStroke(p => SdRoundBox(p, new V(15f, 50f), 3.2f, 46f, 1f), Wood, 0.9f, Line);       // posts
            c.FillStroke(p => SdRoundBox(p, new V(155f, 50f), 3.2f, 46f, 1f), Wood, 0.9f, Line);
            foreach (var x in new[] { 55f, 112f })                                                    // pegs
                c.FillStroke(p => SdCapsule(p, new V(x, 91f), new V(x, 86f), 1.6f), Metal, 0.5f, Line);
            Tip(c, new V(55f, 76f), 9f, TipNeutral);                                                   // still hanging
            DashedLine(c, new V(112f, 84f), new V(112f, 72f), 2.2f, 0.8f, Soft);                       // where the lifted one hung
            V ring = new V(112f, 60f);
            Hand(c, ring + new V(2f, -6f), 96f, 0.95f, -1f, () => Tip(c, ring, 9f, TipNeutral));       // the hand from below holding it
            Ticks(c, ring + new V(0f, 2f), 90f, 0f, 1, 13f, 19f, Motion);
            Ticks(c, ring + new V(0f, 2f), 60f, 0f, 1, 14f, 20f, Motion);
            Ticks(c, ring + new V(0f, 2f), 120f, 0f, 1, 14f, 20f, Motion);
        }

        /// <summary>1: VR ② the dog's head and neck close up (collar band, metal ring at the throat), a hand bringing the tip to that
        /// ring; the tip is green (KH_ClipReady) with a glow: in range of a free dog's collar it turns green, then clips on.</summary>
        static void CellCollar(Canvas c)
        {
            c.Cell(1);
            V dog = new V(26f, 18f); const float s = 2.0f;
            Dog(c, dog, s);
            DogCollar(c, dog, s, false);
            V ring = CollarRing(dog, s);
            V tipAt = ring + new V(-12f, -3f);                                                           // right next to the collar's ring
            Hand(c, tipAt + new V(-6f, -2f), 14f, 0.95f, 1f, () => Tip(c, tipAt, 8.5f, TipReady));    // from the left
            c.Stroke(p => SdCircle(p, tipAt, 14f), 1.0f, TipReady);                                      // the glow
            Ticks(c, tipAt, 95f, 80f, 3, 17f, 23f, TipReady);
        }

        /// <summary>2: ③ (VR and flat) the walk tab: a destination tile on the right marked and touched by a fingertip.</summary>
        static void CellDestination(Canvas c)
        {
            c.Cell(2);
            MiniMenu(c, new V(85f, 62f), 1.0f, 1, 1);
            PointingHand(c, new V(139f, 57f), 118f, 0.85f, 1f);
        }

        /// <summary>3: VR ④ a pointing hand's ray on the dog's collar (the collar glows: its Interact), the tip coming off it.</summary>
        static void CellUnleash(Canvas c)
        {
            c.Cell(3);
            V dog = new V(48f, 34f); const float s = 1.2f;
            Dog(c, dog, s);
            DogCollar(c, dog, s, true);
            V ring = CollarRing(dog, s);
            V tipAt = ring + new V(20f, -16f);                                                          // off the collar, dropping away
            Tip(c, tipAt, 5.5f, TipNeutral);
            Ticks(c, ring + new V(9f, -7f), -40f, 70f, 3, 2f, 6.5f, Line);
            V finger = new V(116f, 72f);
            DashedLine(c, finger + (ring - finger) / (ring - finger).Len * 4f, ring + (finger - ring) / (finger - ring).Len * 9f, 3f, 0.9f, Motion);   // the ray
            PointingHand(c, finger, 218f, 0.8f, -1f);
        }

        /// <summary>4: Flat ① the menu open beside a dog, a dashed circle on the ground around the dog (the 4 m reach).</summary>
        static void CellMenuNear(Canvas c)
        {
            c.Cell(4);
            DashedEllipse(c, new V(96f, 16f), 68f, 11f, 18, 0.9f, Motion);
            V dog = new V(114f, 47f); const float s = 0.78f;
            Dog(c, dog, s);
            MiniMenu(c, new V(46f, 64f), 0.48f, -1, -1);
            Ticks(c, new V(46f, 64f), 90f, 0f, 1, 28f, 34f, Motion);
            Ticks(c, new V(46f, 64f), 140f, 0f, 1, 32f, 38f, Motion);
            Ticks(c, new V(46f, 64f), 40f, 0f, 1, 32f, 38f, Motion);
        }

        /// <summary>5: Flat ② three dog rows, the middle row's two leash buttons marked, the pointer on the left one.</summary>
        static void CellLeashButtons(Canvas c)
        {
            c.Cell(5);
            MenuRows(c, false);
            Pointer(c, new V(110f, 57f), 1.1f);
        }

        /// <summary>6: Flat ④ three dog rows, the middle row (own dog, lit) has one wide button, marked, the pointer on it.</summary>
        static void CellUnleashButton(Canvas c)
        {
            c.Cell(6);
            MenuRows(c, true);
            Pointer(c, new V(128f, 57f), 1.1f);
        }

        // ---------------------------------------------------------------- output

        public static float[] Render()
        {
            var c = new Canvas();
            CellTake(c);
            CellCollar(c);
            CellDestination(c);
            CellUnleash(c);
            CellMenuNear(c);
            CellLeashButtons(c);
            CellUnleashButton(c);
            return c.px;   // cell 7 stays empty (transparent)
        }

        /// <summary>RGBA32 bytes (row 0 = bottom). Fully transparent texels are written as 0 (stable bytes).</summary>
        public static byte[] RenderBytes()
        {
            var f = Render();
            var b = new byte[f.Length];
            for (int i = 0; i < f.Length; i += 4)
            {
                byte a = (byte)Math.Round(Clamp01(f[i + 3]) * 255f);
                if (a == 0) continue;
                b[i] = (byte)Math.Round(Clamp01(f[i]) * 255f);
                b[i + 1] = (byte)Math.Round(Clamp01(f[i + 1]) * 255f);
                b[i + 2] = (byte)Math.Round(Clamp01(f[i + 2]) * 255f);
                b[i + 3] = a;
            }
            return b;
        }
    }
    // ---- ART CORE END ----
}
