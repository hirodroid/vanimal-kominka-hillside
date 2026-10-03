using System;
using System.IO;
using System.Linq;
using KominkaDogRun.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace KominkaHillside.Editor
{
    // IMP-DOG-039 builder (docs/plans/2026-10-03-hand-wash.md §7): the 3-step picture guide above each station's board.
    // - `Guide`: a World Space Canvas, a SIBLING of `Board` under the station root (the board builder deletes the board's unknown
    //   children), 600 x 260 px at 1 px = 1 mm, on the board's face, its bottom GuideGap above the board's top. Read only: no
    //   GraphicRaycaster / VRCUiShape / Collider, every graphic raycastTarget false. Rebuilt by every Wash/11.
    // - Children: Bg, Step1..3 / Pic1..3 (RawImage cells of one texture) / Sub1..3, Arrow1..2 ("▶"), Help. Wired into the
    //   station's KH_WashStationBoard (guideLabels, guidePics, guideUvVr / guideUvFlat), which swaps step ② per device.
    // - The picture: KH_WashGuide.png (1024 x 256, 4 cells of 256²) drawn here from signed-distance shapes (AI-made by code, no
    //   external image). GuideArtVersion names the drawing; the PNG is rewritten only when its bytes change. Wash/19 redraws it alone.
    public static partial class KH_DogWashSetupEditor
    {
        const string GuideName = "Guide";
        public const string GuideTexturePath = "Assets/KominkaHillside/Textures/Wash/KH_WashGuide.png";
        /// <summary>The drawing's version (KH_WashGuideArt). Bump it when the drawing changes (the build report records it).</summary>
        public const int GuideArtVersion = 1;
        static readonly Vector2 GuidePixels = new Vector2(600f, 260f);
        const float GuideScale = 0.001f;   // 1 px = 1 mm: 0.60 x 0.26 m
        /// <summary>Gap between the board's top edge and the guide's bottom edge (m): bottom 1.38 m, top 1.64 m above the station origin.</summary>
        const float GuideGap = 0.02f;
        /// <summary>Station local: the board's face (same x / z as BoardLocal), the bottom GuideGap above the board's top.</summary>
        static Vector3 GuideLocal
        {
            get
            {
                float boardTop = BoardLocal.y + BoardPixels.y * BoardScale * 0.5f;
                return new Vector3(BoardLocal.x, boardTop + GuideGap + GuidePixels.y * GuideScale * 0.5f, BoardLocal.z);
            }
        }
        // ---- layout (px from the canvas centre; §7.2) ----
        static readonly float[] GuidePanelX = { -200f, 0f, 200f };
        const float GuideStepY = 106f, GuidePicY = 24f, GuideSubY = -56f, GuideArrowY = 24f;
        static readonly Vector2 GuideStepSize = new Vector2(180f, 40f), GuidePicSize = new Vector2(170f, 120f), GuideSubSize = new Vector2(180f, 30f);
        static readonly float[] GuideArrowX = { -100f, 100f };
        static readonly Vector2 GuideArrowSize = new Vector2(24f, 24f);
        static readonly Vector2 GuideHelpPos = new Vector2(0f, -110f), GuideHelpSize = new Vector2(560f, 28f);
        const float GuideStepPt = 22f, GuideSubPt = 17f, GuideHelpPt = 17f, GuideArrowPt = 20f;   // autosize down to MinPt (14)
        const string GuideArrowText = "▶";
        static readonly Color GuideInkStep = Color.white;
        // ---- the texture's cells (KH_WashGuideArt): 0 bottle, 1 hand (VR ②), 2 shower, 3 sponge (Desktop / phone ②) ----
        static readonly Rect GuideUvBottle = new Rect(0f, 0f, 0.25f, 1f), GuideUvHand = new Rect(0.25f, 0f, 0.25f, 1f),
            GuideUvShower = new Rect(0.5f, 0f, 0.25f, 1f), GuideUvSponge = new Rect(0.75f, 0f, 0.25f, 1f);
        /// <summary>guideLabels order on the board: Step1, Sub1, Step2, Sub2, Step3, Sub3, Help.</summary>
        static readonly string[] GuideLabelNames = { "Step1", "Sub1", "Step2", "Sub2", "Step3", "Sub3", "Help" };
        static readonly string[] GuidePicNames = { "Pic1", "Pic2", "Pic3" };
        static readonly string[] GuideArrowNames = { "Arrow1", "Arrow2" };

        /// <summary>The guide of one station: the Canvas, its children (code-owned, rewritten every run) and the board's guide fields.
        /// The labels start as the ja VR texts (the board rewrites them per language and device at run time).</summary>
        static void BuildGuide(Transform st, KH_WashStationBoard board, Texture2D tex, BuildReport r)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (font == null) throw new InvalidOperationException("Shared UI font missing: " + KD_ShopSetupEditor.SharedFontPath);
            if (board.words == null || board.words.Length != BoardWordKeys.Length) throw new InvalidOperationException(st.name + ": the board's words are not written (BuildBoard runs first)");
            var t = st.Find(GuideName);
            GameObject go;
            if (t == null) { go = new GameObject(GuideName, typeof(RectTransform)); go.transform.SetParent(st, false); }
            else go = t.gameObject;
            go.layer = 0;
            var canvas = go.GetComponent<Canvas>(); if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            // read only: nothing that takes the pointer (the board's buttons below keep the ray)
            foreach (var gr in go.GetComponents<GraphicRaycaster>()) UnityEngine.Object.DestroyImmediate(gr);
            foreach (var shape in go.GetComponents<VRCUiShape>()) UnityEngine.Object.DestroyImmediate(shape);
            foreach (var col in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = GuidePixels;
            rect.localScale = Vector3.one * GuideScale;
            rect.localPosition = GuideLocal;
            rect.localRotation = Quaternion.identity;   // the Canvas's front is -Z = the station's front (as the board)
            EditorUtility.SetDirty(canvas);

            var bg = KD_ShopSetupEditor.EnsureUiChild(go.transform, "Bg", Vector2.zero, GuidePixels);
            bg.transform.SetAsFirstSibling();
            var bgImg = bg.GetComponent<Image>(); if (bgImg == null) bgImg = bg.AddComponent<Image>();
            bgImg.color = KD_UiSpriteEditor.SurfacePanel;   // the board's dark surface, no button-like frame (§7.1)
            bgImg.raycastTarget = false;
            EditorUtility.SetDirty(bgImg);

            var w = board.words;
            string[] texts =
            {
                w[KH_WashStationBoard.WGuideStep1], w[KH_WashStationBoard.WGuideStep1Sub],
                w[KH_WashStationBoard.WGuideStep2], w[KH_WashStationBoard.WGuideStep2Sub],
                w[KH_WashStationBoard.WGuideStep3], w[KH_WashStationBoard.WGuideStep3Sub],
                w[KH_WashStationBoard.WGuideHelp],
            };
            var labels = new TextMeshProUGUI[GuideLabelNames.Length];
            for (int i = 0; i < 3; i++)
            {
                float x = GuidePanelX[i];
                labels[2 * i] = EnsureBoardLabel(go.transform, GuideLabelNames[2 * i], new Vector2(x, GuideStepY), GuideStepSize, font, GuideStepPt, GuideInkStep, texts[2 * i]);
                labels[2 * i + 1] = EnsureBoardLabel(go.transform, GuideLabelNames[2 * i + 1], new Vector2(x, GuideSubY), GuideSubSize, font, GuideSubPt, BoardInkRule, texts[2 * i + 1]);
            }
            labels[6] = EnsureBoardLabel(go.transform, GuideLabelNames[6], GuideHelpPos, GuideHelpSize, font, GuideHelpPt, BoardInkDim, texts[6]);
            for (int i = 0; i < GuideArrowNames.Length; i++)
                EnsureBoardLabel(go.transform, GuideArrowNames[i], new Vector2(GuideArrowX[i], GuideArrowY), GuideArrowSize, font, GuideArrowPt, BoardInkDim, GuideArrowText);

            var uv = new[] { GuideUvBottle, GuideUvHand, GuideUvShower };   // ② starts as the VR cell (the board switches it per device)
            var pics = new RawImage[GuidePicNames.Length];
            for (int i = 0; i < pics.Length; i++)
            {
                var pg = KD_ShopSetupEditor.EnsureUiChild(go.transform, GuidePicNames[i], new Vector2(GuidePanelX[i], GuidePicY), GuidePicSize);
                foreach (var img in pg.GetComponents<Image>()) UnityEngine.Object.DestroyImmediate(img);
                var raw = pg.GetComponent<RawImage>(); if (raw == null) raw = pg.AddComponent<RawImage>();
                raw.texture = tex; raw.uvRect = uv[i]; raw.color = Color.white; raw.raycastTarget = false;
                EditorUtility.SetDirty(raw);
                pics[i] = raw;
            }
            var known = new[] { "Bg" }.Concat(GuideLabelNames).Concat(GuidePicNames).Concat(GuideArrowNames).ToArray();
            foreach (Transform c in go.transform.Cast<Transform>().ToArray())
                if (!known.Contains(c.name)) { r.log.Add("removed stale " + st.name + "/" + GuideName + "/" + c.name); UnityEngine.Object.DestroyImmediate(c.gameObject); }
            foreach (var g in go.GetComponentsInChildren<Graphic>(true)) if (g.raycastTarget) { g.raycastTarget = false; EditorUtility.SetDirty(g); }

            board.guideLabels = labels;
            board.guidePics = pics;
            board.guideUvVr = GuideUvHand;
            board.guideUvFlat = GuideUvSponge;
            board.debugVr = false;   // harness only
            EditorUtility.SetDirty(board);
            r.guides.Add(st.name + "/" + GuideName + " local " + GuideLocal.ToString("F3") + ", " + GuidePixels.x + " x " + GuidePixels.y + " px at " + GuideScale + " m/px, labels " + labels.Length + ", pictures " + pics.Length);
        }

        // ================================================================ the picture

        [MenuItem(Menu + "19 - Rebuild Guide Art")]
        public static void RebuildGuideArt()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Wash Setup] Edit mode only (not compiling)");
            var r = new BuildReport();
            EnsureGuideTexture(r);
            Debug.Log("[KH Wash Setup] guide art v" + GuideArtVersion + " " + GuideTexturePath + (r.guideTextureWritten ? " written" : " unchanged (same bytes)") + "; " + string.Join(" | ", r.log));
        }

        /// <summary>Draws the guide picture (KH_WashGuideArt), writes the PNG when its bytes differ from the file, imports it with the
        /// settings below (re-imported only when one differs) and returns the imported texture.</summary>
        static Texture2D EnsureGuideTexture(BuildReport r)
        {
            EnsureFolder(Path.GetDirectoryName(GuideTexturePath).Replace('\\', '/'));
            var rgba = KH_WashGuideArt.RenderBytes();
            var tex = new Texture2D(KH_WashGuideArt.Width, KH_WashGuideArt.Height, TextureFormat.RGBA32, false, false);
            byte[] png;
            try
            {
                var px = new Color32[KH_WashGuideArt.Width * KH_WashGuideArt.Height];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(rgba[4 * i], rgba[4 * i + 1], rgba[4 * i + 2], rgba[4 * i + 3]);
                tex.SetPixels32(px);   // row 0 = the bottom (the drawing's y is up)
                tex.Apply(false, false);
                png = tex.EncodeToPNG();
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
            bool write = !File.Exists(GuideTexturePath) || !File.ReadAllBytes(GuideTexturePath).SequenceEqual(png);
            if (write)
            {
                File.WriteAllBytes(GuideTexturePath, png);
                AssetDatabase.ImportAsset(GuideTexturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                r.log.Add("guide art v" + GuideArtVersion + " written to " + GuideTexturePath + " (" + png.Length + " bytes)");
            }
            r.guideTexture = GuideTexturePath; r.guideArtVersion = GuideArtVersion; r.guideTextureWritten = write;
            var ti = AssetImporter.GetAtPath(GuideTexturePath) as TextureImporter;
            if (ti == null) { AssetDatabase.ImportAsset(GuideTexturePath, ImportAssetOptions.ForceSynchronousImport); ti = AssetImporter.GetAtPath(GuideTexturePath) as TextureImporter; }
            if (ti == null) throw new InvalidOperationException("TextureImporter missing: " + GuideTexturePath);
            if (ConfigureGuideImporter(ti)) { ti.SaveAndReimport(); r.log.Add("guide art import settings applied (" + GuideTexturePath + ")"); }
            var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(GuideTexturePath);
            if (loaded == null) throw new InvalidOperationException("Texture not imported: " + GuideTexturePath);
            return loaded;
        }

        public const int GuideMaxSize = 1024;
        /// <summary>Import settings (§7.4): sRGB, alpha is transparency, Read/Write off, mipmaps on, clamp, bilinear, 1024 max;
        /// PC (Standalone) DXT5; Android / iOS ASTC 6x6 at most 1024. Returns whether something changed (then the caller re-imports).</summary>
        static bool ConfigureGuideImporter(TextureImporter ti)
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
            Set(ti.maxTextureSize, GuideMaxSize, v => ti.maxTextureSize = v);
            Set(ti.textureCompression, TextureImporterCompression.Compressed, v => ti.textureCompression = v);
            foreach (var (platform, format) in new[] { ("Standalone", TextureImporterFormat.DXT5), ("Android", TextureImporterFormat.ASTC_6x6), ("iPhone", TextureImporterFormat.ASTC_6x6) })
            {
                var ps = ti.GetPlatformTextureSettings(platform);
                if (!ps.overridden || ps.format != format || ps.maxTextureSize != GuideMaxSize)
                {
                    ps.name = platform; ps.overridden = true; ps.format = format; ps.maxTextureSize = GuideMaxSize; ps.compressionQuality = 50;
                    ti.SetPlatformTextureSettings(ps);
                    changed = true;
                }
            }
            return changed;
        }
    }

    // ---- ART CORE BEGIN (plain C#: no UnityEngine, so the drawing can be rendered and looked at outside Unity) ----
    /// <summary>IMP-DOG-039 §7.4: the guide picture, drawn from signed distances. 1024 x 256 RGBA (straight alpha, row 0 = bottom),
    /// 4 cells of 256²: 0 the bottle squeezing foam onto a dog's back, 1 an empty hand rubbing the dog's side (back-and-forth arrow,
    /// foam growing around the fingers), 2 the shower rinsing (water lines, foam running off, sparkles), 3 the sponge (Desktop / phone ②).
    /// Each cell is drawn in display units W x H = the Pic RawImage (170 x 120 px), so the square cell is not squashed on the board.
    /// Line art white, foam bluish white, water light blue; the tools keep the colours of the real ones on the shelf.</summary>
    internal static class KH_WashGuideArt
    {
        public const int CellPx = 256, Cells = 4, Width = CellPx * Cells, Height = CellPx;
        public const float W = 170f, H = 120f;   // a cell in display units (1 unit = 1 mm on the board)
        const float Aa = 0.55f;                  // anti-alias half width (one texel is 0.66 x 0.47 units)
        const float EdgeFade = 4f;               // everything fades out this close to a cell's edge (no bleeding between cells in the mips)

        // ---------------------------------------------------------------- tiny vector / colour types

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
            /// <summary>Rotated counter-clockwise by deg.</summary>
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
        static readonly Rgba Foam = new Rgba(0.90f, 0.95f, 1f, 0.97f);
        static readonly Rgba FoamEdge = new Rgba(0.68f, 0.82f, 1f);
        static readonly Rgba Water = new Rgba(0.55f, 0.80f, 1f, 0.95f);
        static readonly Rgba Motion = new Rgba(1f, 0.82f, 0.35f);                 // the back-and-forth arrows
        static readonly Rgba Skin = new Rgba(1f, 0.86f, 0.74f);
        static readonly Rgba SkinCrease = new Rgba(0.82f, 0.60f, 0.50f);
        static readonly Rgba Sleeve = new Rgba(0.60f, 0.72f, 0.90f);
        static readonly Rgba BottlePink = new Rgba(0.98f, 0.70f, 0.80f);           // the shelf's KH_WashTool_Shampoo, a little deeper
        static readonly Rgba CapWhite = new Rgba(0.94f, 0.94f, 0.96f);
        static readonly Rgba SpongeYellow = new Rgba(0.98f, 0.82f, 0.20f);         // KH_WashTool_Sponge
        static readonly Rgba SpongePore = new Rgba(0.84f, 0.60f, 0.10f);
        static readonly Rgba ShowerBlue = new Rgba(0.30f, 0.52f, 0.78f);           // KH_WashTool_Shower
        static readonly Rgba ShowerHead = new Rgba(0.80f, 0.82f, 0.84f);           // KH_WashTool_ShowerHead
        static readonly Rgba ShowerHole = new Rgba(0.45f, 0.50f, 0.56f);

        // ---------------------------------------------------------------- signed distances (negative inside)

        static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
        static float Smoothstep(float a, float b, float x) { float t = Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        static float SdCircle(V p, V c, float r) { return (p - c).Len - r; }

        /// <summary>Ellipse (radii rx, ry): the usual k0 (k0 - 1) / k1 approximation (exact on the rim, good near it).</summary>
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

        /// <summary>Rounded box: centre c, half size (hx, hy) including the corner radius r, turned deg counter-clockwise.</summary>
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

        /// <summary>Smooth union (k = the blend width in units).</summary>
        static float Smin(float a, float b, float k)
        {
            float h = Clamp01(0.5f + 0.5f * (b - a) / k);
            return Lerp(b, a, h) - k * h * (1f - h);
        }

        // ---------------------------------------------------------------- the canvas

        sealed class Canvas
        {
            public readonly float[] px = new float[Width * Height * 4];
            int cell;
            public void Cell(int c) { cell = c; }

            public void Fill(Sdf f, Rgba c) { Paint(f, c, -1f); }
            /// <summary>A line along the shape's rim, halfWidth to each side.</summary>
            public void Stroke(Sdf f, float halfWidth, Rgba c) { Paint(f, c, halfWidth); }
            public void FillStroke(Sdf f, Rgba fill, float halfWidth, Rgba line) { Fill(f, fill); Stroke(f, halfWidth, line); }

            void Paint(Sdf f, Rgba c, float stroke)
            {
                int x0 = cell * CellPx;
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
                        Blend((j * Width + x0 + i) * 4, c, cover * Smoothstep(0f, EdgeFade, edge));
                    }
                }
            }

            /// <summary>"Over" with straight alpha.</summary>
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

        // ---------------------------------------------------------------- shared pieces

        /// <summary>The one side-view dog of every cell (facing right), local units: body centre at the origin, paws at y -32.
        /// Body ellipse + chest + haunch, neck, head circle, snout, an upright ear, capsule legs with paws, a curled tail.</summary>
        static float DogLocal(V p)
        {
            float d = SdEllipse(p, new V(0f, 0f), 34f, 15f);
            d = Smin(d, SdCircle(p, new V(22f, -1f), 14f), 4f);      // chest
            d = Smin(d, SdCircle(p, new V(-22f, -1f), 13f), 4f);     // haunch
            d = Smin(d, SdCapsule(p, new V(26f, 6f), new V(36f, 18f), 9f), 4f);   // neck
            d = Smin(d, SdCircle(p, new V(42f, 22f), 12f), 3f);      // head
            d = Smin(d, SdCapsule(p, new V(46f, 18f), new V(57f, 15f), 5.5f), 2f);   // snout
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

        /// <summary>The dog at `at` (its body centre), scaled by s: faint fill, white outline, eye and nose.</summary>
        static void Dog(Canvas c, V at, float s)
        {
            Sdf dog = p => DogLocal((p - at) / s) * s;
            c.FillStroke(dog, DogFill, 1.15f, Line);
            c.Fill(p => SdCircle(p, at + new V(47f, 25f) * s, 1.7f), Line);      // eye
            c.Fill(p => SdCircle(p, at + new V(58.5f, 17f) * s, 2.2f), Line);    // nose
        }

        /// <summary>A heap of foam: the circles merged, bluish white with a light blue rim.</summary>
        static void FoamHeap(Canvas c, params float[] xyr)
        {
            Sdf f = p =>
            {
                float d = 1e9f;
                for (int i = 0; i + 2 < xyr.Length; i += 3) d = Smin(d, SdCircle(p, new V(xyr[i], xyr[i + 1]), xyr[i + 2]), 1.6f);
                return d;
            };
            c.FillStroke(f, Foam, 0.6f, FoamEdge);
        }

        /// <summary>Small floating bubbles (rings).</summary>
        static void Bubbles(Canvas c, params float[] xyr)
        {
            for (int i = 0; i + 2 < xyr.Length; i += 3)
            {
                V at = new V(xyr[i], xyr[i + 1]); float r = xyr[i + 2];
                c.Stroke(p => SdCircle(p, at, r), 0.45f, Foam);
            }
        }

        /// <summary>A double-headed arrow a <-> b (the back-and-forth of rubbing).</summary>
        static void DoubleArrow(Canvas c, V a, V b, Rgba col)
        {
            V dir = (b - a) / (b - a).Len, n = new V(-dir.y, dir.x);
            const float head = 6.5f, half = 4.2f, shaft = 1.3f;
            V a1 = a + dir * (head * 0.8f), b1 = b - dir * (head * 0.8f);
            Sdf f = p => Math.Min(SdCapsule(p, a1, b1, shaft),
                Math.Min(SdTriangle(p, a, a + dir * head + n * half, a + dir * head - n * half) - 0.4f,
                         SdTriangle(p, b, b - dir * head - n * half, b - dir * head + n * half) - 0.4f));
            c.Fill(f, col);
        }

        /// <summary>A four-pointed sparkle ("clean").</summary>
        static void Sparkle(Canvas c, V at, float size)
        {
            Sdf f = p =>
            {
                V q = p - at;
                float ax = Math.Abs(q.x), ay = Math.Abs(q.y);
                // a thin diamond along each axis: |x| / size + |y| / (size * 0.22) <= 1, and the same turned
                float d1 = (ax / size + ay / (size * 0.22f) - 1f) * size * 0.22f;
                float d2 = (ay / size + ax / (size * 0.22f) - 1f) * size * 0.22f;
                return Math.Min(d1, d2);
            };
            c.Fill(f, Line);
        }

        // ---------------------------------------------------------------- the tools

        /// <summary>The shampoo bottle, local: body centre at the origin, the cap up (+y), the spout tilted 30° toward +x.
        /// Placed so the spout's tip is at `tip`, the whole bottle turned deg (counter-clockwise), scaled by s.</summary>
        static void Bottle(Canvas c, V tip, float deg, float s)
        {
            V tipLocal = new V(0f, 22.5f) + V.Dir(60f) * 9f;   // spout from the cap's top, 30° off vertical
            V origin = tip - (tipLocal * s).Rot(deg);
            Func<V, V> L = p => (p - origin).Rot(-deg) / s;
            Sdf body = p => { V q = L(p); return Smin(SdRoundBox(q, new V(0f, -1f), 9.5f, 16f, 4.5f), SdEllipse(q, new V(0f, 14f), 9.5f, 4.5f), 2f) * s; };
            Sdf cap = p => { V q = L(p); return SdRoundBox(q, new V(0f, 20f), 4.8f, 2.8f, 1f) * s; };
            Sdf spout = p => { V q = L(p); return SdCapsule(q, new V(0f, 22f), tipLocal, 1.7f) * s; };
            Sdf label = p => { V q = L(p); return SdRoundBox(q, new V(0f, -2f), 6f, 6.5f, 2f) * s; };
            Sdf all = p => Math.Min(body(p), Math.Min(cap(p), spout(p)));
            c.Fill(body, BottlePink);
            c.Fill(cap, CapWhite);
            c.Fill(spout, CapWhite);
            c.Stroke(label, 0.5f, Line);                       // the label (reads as a bottle, not a can)
            c.Fill(p => { V q = L(p); return SdCircle(q, new V(0f, -0.5f), 2.4f) * s; }, Line);   // a drop mark on the label
            c.Fill(p => { V q = L(p); return SdTriangle(q, new V(-2.1f, 0.6f), new V(2.1f, 0.6f), new V(0f, 4.5f)) * s; }, Line);
            c.Stroke(all, 1.0f, Line);
        }

        /// <summary>An open hand (mitten with the thumb apart and three finger creases) with its forearm and a sleeve. Local: palm
        /// centre at the origin, the fingers toward +y, the thumb toward thumbSide (+1 = +x). Placed with the fingertips at `tips`,
        /// the fingers pointing along `fingerDeg` (degrees, counter-clockwise from +x), scaled by s.</summary>
        static void Hand(Canvas c, V tips, float fingerDeg, float s, float thumbSide)
        {
            float deg = fingerDeg - 90f;                       // local +y -> fingerDeg
            V origin = tips - (new V(0f, 25f) * s).Rot(deg);
            Func<V, V> L = p => { V q = (p - origin).Rot(-deg) / s; q.x *= thumbSide; return q; };
            Sdf palmAndFingers = p =>
            {
                V q = L(p);
                float d = SdRoundBox(q, new V(0f, 0f), 10f, 9.5f, 5f);                    // palm
                d = Smin(d, SdRoundBox(q, new V(0f, 14f), 9.6f, 11f, 7.5f), 2f);        // the fingers together (mitten), tips at y 25
                d = Smin(d, SdCapsule(q, new V(0f, -6f), new V(0f, -60f), 7.2f), 2f);  // wrist and forearm
                return d * s;
            };
            Sdf thumb = p => SdCapsule(L(p), new V(8f, -2f), new V(17.5f, 9.5f), 3.8f) * s;   // apart from the fingers
            Sdf sleeve = p => { V q = L(p); return SdRoundBox(q, new V(0f, -58f), 10.5f, 24f, 3f) * s; };
            c.FillStroke(thumb, Skin, 1.0f, Line);             // under the palm: its own outline shows the gap to the fingers
            c.FillStroke(palmAndFingers, Skin, 1.0f, Line);
            c.Fill(p => SdCapsule(L(p), new V(7f, -3f), new V(9.5f, 0.5f), 3.2f) * s, Skin);   // the thumb's root joins the palm (no line there)
            for (int k = -1; k <= 1; k++)                      // finger creases
            {
                float x = k * 4.8f;
                c.Stroke(p => SdCapsule(L(p), new V(x, 15.5f), new V(x, 23.5f), 0f) * s, 0.5f, SkinCrease);
            }
            c.FillStroke(sleeve, Sleeve, 1.0f, Line);
        }

        static void Sponge(Canvas c, V at, float deg, float s)
        {
            Func<V, V> L = p => (p - at).Rot(-deg) / s;
            Sdf body = p => SdRoundBox(L(p), new V(0f, 0f), 14f, 8.5f, 3.5f) * s;
            c.Fill(body, SpongeYellow);
            float[] pores = { -9f, 3f, -4f, -3f, 1f, 4f, 6f, -2f, 10f, 3.5f, -10f, -4f, 3f, -5.5f };
            for (int i = 0; i + 1 < pores.Length; i += 2)
            {
                V q0 = new V(pores[i], pores[i + 1]);
                c.Fill(p => SdCircle(L(p), q0, 1.25f) * s, SpongePore);
            }
            c.Stroke(body, 1.0f, Line);
        }

        /// <summary>The hand shower: the head (face toward faceDeg) at `head`, the blue handle going back from it.</summary>
        static void Shower(Canvas c, V head, float faceDeg, float s)
        {
            float deg = faceDeg + 90f;                         // local -y = the face
            Func<V, V> L = p => (p - head).Rot(-deg) / s;
            Sdf headS = p => SdRoundBox(L(p), new V(0f, 0f), 12.5f, 4.2f, 3.6f) * s;
            Sdf handle = p => SdCapsule(L(p), new V(-6f, 3f), new V(-24f, 26f), 3.6f) * s;
            c.FillStroke(handle, ShowerBlue, 1.0f, Line);
            c.FillStroke(headS, ShowerHead, 1.0f, Line);
            for (int k = -3; k <= 3; k++)
            {
                V hole = new V(k * 3f, -2.2f);
                c.Fill(p => SdCircle(L(p), hole, 0.8f) * s, ShowerHole);
            }
        }

        // ---------------------------------------------------------------- the four cells

        /// <summary>0: ① the bottle, spout down toward the dog's back, a few drops in the air and a heap of foam on the back.</summary>
        static void CellBottle(Canvas c)
        {
            c.Cell(0);
            V dog = new V(58f, 40f); const float s = 0.9f;
            Dog(c, dog, s);
            V target = new V(56f, 58f);
            FoamHeap(c, 49f, 57f, 5.5f, 56f, 59.5f, 6.5f, 63.5f, 57f, 5f, 55f, 53.5f, 4f);
            Bubbles(c, 45f, 65f, 1.4f, 64f, 66f, 1.1f);
            const float deg = 160f;                            // the bottle upside down, the spout toward the lower left
            V spoutDir = V.Dir(60f + deg);
            V tip = target - spoutDir * 21f;
            for (int k = 1; k <= 3; k++)
            {
                V drop = tip + spoutDir * (4.5f * k + 1f);
                float r = 1.4f + 0.25f * k;
                c.FillStroke(p => SdCircle(p, drop, r), Foam, 0.4f, FoamEdge);
            }
            Bottle(c, tip, deg, 0.9f);
        }

        /// <summary>1: ② an empty hand on the dog's side, rubbing (back-and-forth arrow), foam growing around the fingers.</summary>
        static void CellHand(Canvas c)
        {
            c.Cell(1);
            V dog = new V(60f, 40f); const float s = 0.9f;
            Dog(c, dog, s);
            // the foam already spread: bigger near the fingers, smaller where it reaches out (it grows outward)
            FoamHeap(c, 58f, 45f, 6f, 66f, 41f, 6.5f, 74f, 44f, 5f, 50f, 42.5f, 4.3f, 43.5f, 44f, 3.2f, 80f, 40f, 3f);
            Bubbles(c, 54f, 54f, 1.6f, 47f, 51f, 1.2f, 79f, 50f, 1.3f, 70f, 55f, 1.0f);
            Hand(c, new V(64f, 41f), -66f, 1f, -1f);           // from the upper left, fingers down-right onto the side, thumb up
            FoamHeap(c, 61f, 40f, 2.6f, 67.5f, 38.5f, 2.3f);   // lather over the fingertips
            DoubleArrow(c, new V(73f, 69f), new V(92f, 69f), Motion);
        }

        /// <summary>2: ③ the shower: water lines onto the back, the foam running off under the belly, sparkles.</summary>
        static void CellShower(Canvas c)
        {
            c.Cell(2);
            V dog = new V(58f, 40f); const float s = 0.9f;
            Dog(c, dog, s);
            const float faceDeg = -78f;
            V headAt = new V(52f, 100f);
            // water first (under the head), from the face to the back
            {
                V fc = headAt + (new V(0f, -4.5f) * 0.95f).Rot(faceDeg + 90f);
                V fd = V.Dir(faceDeg), fa = V.Dir(faceDeg + 90f);
                for (int k = -2; k <= 2; k++)
                {
                    V a = fc + fa * (k * 4.2f), d = (fd + fa * (k * 0.07f)) / (fd + fa * (k * 0.07f)).Len;
                    for (int seg = 0; seg < 5; seg++)
                    {
                        V s0 = a + d * (3f + seg * 8.5f), s1 = s0 + d * 5.5f;
                        c.Fill(p => SdCapsule(p, s0, s1, 1.05f), Water);
                    }
                }
            }
            Shower(c, headAt, faceDeg, 0.95f);
            // the foam sliding down and dripping off the belly
            FoamHeap(c, 38f, 30f, 3f, 52f, 27.5f, 3.6f, 66f, 28.5f, 2.6f);
            foreach (var x in new[] { 42f, 55f, 68f })
            {
                V top = new V(x, 21f);
                c.Fill(p => Math.Min(SdCircle(p, top + new V(0f, -2.2f), 2f), SdTriangle(p, top + new V(-1.9f, -1.6f), top + new V(1.9f, -1.6f), top + new V(0f, 2.2f))), Water);
            }
            Sparkle(c, new V(82f, 64f), 5f);
            Sparkle(c, new V(30f, 66f), 3.6f);
        }

        /// <summary>3: ② on Desktop / phone: the sponge on the dog's side, the back-and-forth arrow and foam.</summary>
        static void CellSponge(Canvas c)
        {
            c.Cell(3);
            V dog = new V(60f, 40f); const float s = 0.9f;
            Dog(c, dog, s);
            FoamHeap(c, 49f, 41f, 5f, 57f, 36f, 4.5f, 73f, 37f, 5.5f, 80f, 42f, 3.6f, 43f, 44f, 3f);
            Bubbles(c, 50f, 54f, 1.5f, 78f, 53f, 1.3f, 44f, 50f, 1.1f);
            Sponge(c, new V(64f, 45f), -10f, 1f);
            DoubleArrow(c, new V(52f, 64f), new V(76f, 64f), Motion);
        }

        // ---------------------------------------------------------------- output

        /// <summary>The picture as straight-alpha floats (row 0 = bottom).</summary>
        public static float[] Render()
        {
            var c = new Canvas();
            CellBottle(c);
            CellHand(c);
            CellShower(c);
            CellSponge(c);
            return c.px;
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
