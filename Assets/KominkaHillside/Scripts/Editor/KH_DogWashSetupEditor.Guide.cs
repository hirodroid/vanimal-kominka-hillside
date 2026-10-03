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
    // IMP-DOG-039 builder (docs/plans/2026-10-03-hand-wash.md §7), made 4 steps by IMP-UI-003 (§13.11): the picture guide above
    // each station's board.
    // - `Guide`: a World Space Canvas, a SIBLING of `Board` under the station root (the board builder deletes the board's unknown
    //   children), 800 x 260 px at 1 px = 1 mm (0.80 x 0.26 m, centred over the 0.60 m board), on the board's face, its bottom
    //   GuideGap above the board's top. Read only: no GraphicRaycaster / VRCUiShape / Collider, every graphic raycastTarget false.
    //   Rebuilt by every Wash/11.
    // - Children: Bg, Step1..4 / Pic1..4 (RawImage cells of one texture) / Sub1..4, Arrow1..3 ("▶"), Help. Wired into the
    //   station's KH_WashStationBoard (guideLabels, guidePics, guideUvVr2 / guideUvFlat2 / guideUvVr3 / guideUvFlat3), which swaps
    //   steps ② ③ per device.
    // - The picture: KH_WashGuide.png (1024 x 512, a 4 x 2 grid of 256² cells) drawn here from signed-distance shapes (AI-made by
    //   code, no external image). GuideArtVersion names the drawing; the PNG is rewritten only when its bytes change. Wash/19
    //   redraws it alone.
    public static partial class KH_DogWashSetupEditor
    {
        const string GuideName = "Guide";
        public const string GuideTexturePath = "Assets/KominkaHillside/Textures/Wash/KH_WashGuide.png";
        /// <summary>The drawing's version (KH_WashGuideArt). Bump it when the drawing changes (the build report records it).
        /// v1 IMP-DOG-039 (3 steps, 1024 x 256); v2 IMP-UI-003 (4 steps, 1024 x 512).</summary>
        public const int GuideArtVersion = 2;
        static readonly Vector2 GuidePixels = new Vector2(800f, 260f);
        const float GuideScale = 0.001f;   // 1 px = 1 mm: 0.80 x 0.26 m
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
        // ---- layout (px from the canvas centre; §13.11) ----
        static readonly float[] GuidePanelX = { -300f, -100f, 100f, 300f };
        const float GuideStepY = 106f, GuidePicY = 24f, GuideSubY = -56f, GuideArrowY = 24f;
        static readonly Vector2 GuideStepSize = new Vector2(178f, 40f), GuidePicSize = new Vector2(170f, 120f), GuideSubSize = new Vector2(178f, 30f);   // 178: 22 mm between neighbouring step labels (at 190 the long ③ and ④ ran together, 2026-10-03 shot)
        static readonly float[] GuideArrowX = { -200f, 0f, 200f };
        static readonly Vector2 GuideArrowSize = new Vector2(24f, 24f);
        static readonly Vector2 GuideHelpPos = new Vector2(0f, -110f), GuideHelpSize = new Vector2(760f, 28f);
        const float GuideStepPt = 22f, GuideSubPt = 17f, GuideHelpPt = 17f, GuideArrowPt = 20f;   // autosize down to MinPt (14)
        const string GuideArrowText = "▶";
        static readonly Color GuideInkStep = Color.white;
        // ---- the texture's cells (KH_WashGuideArt): a 4 x 2 grid of 256²; Unity's UV origin is the bottom-left, so the top row
        // (row 0: A..D) is v 0.5..1 and the bottom row (row 1: E..H) is v 0..0.5 ----
        // A ① bottle + foam anywhere, B ② let go of the bottle (VR), C ③ both hands (VR), D ④ add more;
        // E ② hold the sponge (Desktop / phone), F ③ scrub with the sponge (Desktop / phone); G, H empty.
        static readonly Rect GuideUvFoam = new Rect(0f, 0.5f, 0.25f, 0.5f), GuideUvRelease = new Rect(0.25f, 0.5f, 0.25f, 0.5f),
            GuideUvBothHands = new Rect(0.5f, 0.5f, 0.25f, 0.5f), GuideUvAddMore = new Rect(0.75f, 0.5f, 0.25f, 0.5f),
            GuideUvHoldSponge = new Rect(0f, 0f, 0.25f, 0.5f), GuideUvSponge = new Rect(0.25f, 0f, 0.25f, 0.5f);
        /// <summary>The pictures as built (② ③ start as the VR cells; the board switches them per device).</summary>
        static readonly Rect[] GuidePicUvBuilt = { GuideUvFoam, GuideUvRelease, GuideUvBothHands, GuideUvAddMore };
        /// <summary>guideLabels order on the board: Step1, Sub1, Step2, Sub2, Step3, Sub3, Step4, Sub4, Help.</summary>
        static readonly string[] GuideLabelNames = { "Step1", "Sub1", "Step2", "Sub2", "Step3", "Sub3", "Step4", "Sub4", "Help" };
        static readonly string[] GuidePicNames = { "Pic1", "Pic2", "Pic3", "Pic4" };
        static readonly string[] GuideArrowNames = { "Arrow1", "Arrow2", "Arrow3" };
        /// <summary>The steps' panels (= GuidePanelX.Length = GuidePicNames.Length); the Help label is guideLabels[2 * GuideSteps].</summary>
        const int GuideSteps = 4;
        /// <summary>The words the 9 labels show on each device (guideLabels order), as KH_WashStationBoard._ApplyGuide writes them.</summary>
        static int[] GuideLabelWords(bool vr)
        {
            return new[]
            {
                KH_WashStationBoard.WGuideStep1, KH_WashStationBoard.WGuideStep1Sub,
                vr ? KH_WashStationBoard.WGuideStep2 : KH_WashStationBoard.WGuideStep2Flat, vr ? KH_WashStationBoard.WGuideStep2Sub : KH_WashStationBoard.WGuideStep2FlatSub,
                vr ? KH_WashStationBoard.WGuideStep3 : KH_WashStationBoard.WGuideStep3Flat, vr ? KH_WashStationBoard.WGuideStep3Sub : KH_WashStationBoard.WGuideStep3FlatSub,
                KH_WashStationBoard.WGuideStep4, KH_WashStationBoard.WGuideStep4Sub,
                vr ? KH_WashStationBoard.WGuideHelp : KH_WashStationBoard.WGuideHelpFlat,
            };
        }

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
            var texts = GuideLabelWords(true).Select(i => w[i]).ToArray();   // the ja VR words (the board rewrites them per language and device)
            var labels = new TextMeshProUGUI[GuideLabelNames.Length];
            for (int i = 0; i < GuideSteps; i++)
            {
                float x = GuidePanelX[i];
                labels[2 * i] = EnsureBoardLabel(go.transform, GuideLabelNames[2 * i], new Vector2(x, GuideStepY), GuideStepSize, font, GuideStepPt, GuideInkStep, texts[2 * i]);
                labels[2 * i + 1] = EnsureBoardLabel(go.transform, GuideLabelNames[2 * i + 1], new Vector2(x, GuideSubY), GuideSubSize, font, GuideSubPt, BoardInkRule, texts[2 * i + 1]);
            }
            labels[2 * GuideSteps] = EnsureBoardLabel(go.transform, GuideLabelNames[2 * GuideSteps], GuideHelpPos, GuideHelpSize, font, GuideHelpPt, BoardInkDim, texts[2 * GuideSteps]);
            for (int i = 0; i < GuideArrowNames.Length; i++)
                EnsureBoardLabel(go.transform, GuideArrowNames[i], new Vector2(GuideArrowX[i], GuideArrowY), GuideArrowSize, font, GuideArrowPt, BoardInkDim, GuideArrowText);

            var pics = new RawImage[GuidePicNames.Length];
            for (int i = 0; i < pics.Length; i++)
            {
                var pg = KD_ShopSetupEditor.EnsureUiChild(go.transform, GuidePicNames[i], new Vector2(GuidePanelX[i], GuidePicY), GuidePicSize);
                foreach (var img in pg.GetComponents<Image>()) UnityEngine.Object.DestroyImmediate(img);
                var raw = pg.GetComponent<RawImage>(); if (raw == null) raw = pg.AddComponent<RawImage>();
                raw.texture = tex; raw.uvRect = GuidePicUvBuilt[i]; raw.color = Color.white; raw.raycastTarget = false;
                EditorUtility.SetDirty(raw);
                pics[i] = raw;
            }
            var known = new[] { "Bg" }.Concat(GuideLabelNames).Concat(GuidePicNames).Concat(GuideArrowNames).ToArray();
            foreach (Transform c in go.transform.Cast<Transform>().ToArray())
                if (!known.Contains(c.name)) { r.log.Add("removed stale " + st.name + "/" + GuideName + "/" + c.name); UnityEngine.Object.DestroyImmediate(c.gameObject); }
            foreach (var g in go.GetComponentsInChildren<Graphic>(true)) if (g.raycastTarget) { g.raycastTarget = false; EditorUtility.SetDirty(g); }

            board.guideLabels = labels;
            board.guidePics = pics;
            board.guideUvVr2 = GuideUvRelease;
            board.guideUvFlat2 = GuideUvHoldSponge;
            board.guideUvVr3 = GuideUvBothHands;
            board.guideUvFlat3 = GuideUvSponge;
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
    /// <summary>IMP-DOG-039 §7.4 / IMP-UI-003 §13.11: the guide picture, drawn from signed distances. 1024 x 512 RGBA (straight
    /// alpha, row 0 = bottom), a 4 x 2 grid of 256² cells. Cell index = row * 4 + column, row 0 = the TOP row (texels 256..511):
    /// 0 A the bottle squirting, foam on the head, the back and the hip (① "anywhere"); 1 B an open hand letting go of the bottle
    /// (VR ②); 2 C two hands, left and right, rubbing the foamy dog (VR ③); 3 D the bottle again with "+" and a return arrow (④);
    /// 4 E a hand holding the sponge (Desktop / phone ②); 5 F the sponge rubbing the dog (Desktop / phone ③); 6, 7 empty.
    /// Each cell is drawn in display units W x H = the Pic RawImage (170 x 120 px), so the square cell is not squashed on the board.
    /// Line art white, foam bluish white, motion marks amber; the tools keep the colours of the real ones on the shelf.</summary>
    internal static class KH_WashGuideArt
    {
        public const int CellPx = 256, Columns = 4, Rows = 2, Width = CellPx * Columns, Height = CellPx * Rows;
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
        static void Hand(Canvas c, V tips, float fingerDeg, float s, float thumbSide, Action held = null)
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
            if (held == null) c.FillStroke(thumb, Skin, 1.0f, Line);   // under the palm: its own outline shows the gap to the fingers
            c.FillStroke(palmAndFingers, Skin, 1.0f, Line);
            if (held == null) c.Fill(p => SdCapsule(L(p), new V(7f, -3f), new V(9.5f, 0.5f), 3.2f) * s, Skin);   // the thumb's root joins the palm (no line there)
            for (int k = -1; k <= 1; k++)                      // finger creases
            {
                float x = k * 4.8f;
                c.Stroke(p => SdCapsule(L(p), new V(x, 15.5f), new V(x, 23.5f), 0f) * s, 0.5f, SkinCrease);
            }
            c.FillStroke(sleeve, Sleeve, 1.0f, Line);
            if (held != null) { held(); c.FillStroke(thumb, Skin, 1.0f, Line); }   // gripping: the object over the palm, the thumb over it
        }

        /// <summary>An open hand letting go: the palm toward the viewer, four fingers spread apart and the thumb out, with its forearm
        /// and sleeve. Local as Hand: palm centre at the origin, fingers toward +y (the middle fingertip at y 27), the thumb toward
        /// thumbSide. Placed with the palm centre at `palm`, the fingers pointing along fingerDeg, scaled by s.</summary>
        static void OpenHand(Canvas c, V palm, float fingerDeg, float s, float thumbSide)
        {
            float deg = fingerDeg - 90f;
            Func<V, V> L = p => { V q = (p - palm).Rot(-deg) / s; q.x *= thumbSide; return q; };
            // index (next to the thumb) .. little finger, each a capsule from its knuckle; spread like a fan
            float[] f = { 5.6f, 7f, 9.6f, 24f, 2.7f,   1.8f, 8.5f, 2.4f, 27f, 2.8f,   -2.2f, 8f, -4.6f, 25f, 2.7f,   -6f, 6f, -10.8f, 20.5f, 2.4f };
            Sdf hand = p =>
            {
                V q = L(p);
                float d = SdRoundBox(q, new V(0f, 0f), 9.8f, 9.5f, 4.6f);                 // palm
                for (int k = 0; k < f.Length; k += 5) d = Smin(d, SdCapsule(q, new V(f[k], f[k + 1]), new V(f[k + 2], f[k + 3]), f[k + 4]), 0.8f);
                d = Smin(d, SdCapsule(q, new V(0f, -6f), new V(0f, -60f), 7.2f), 2f);   // wrist and forearm
                return d * s;
            };
            Sdf thumb = p => SdCapsule(L(p), new V(8f, -3f), new V(19f, 6f), 3.6f) * s;
            Sdf sleeve = p => { V q = L(p); return SdRoundBox(q, new V(0f, -58f), 10.5f, 24f, 3f) * s; };
            c.FillStroke(thumb, Skin, 1.0f, Line);
            c.FillStroke(hand, Skin, 1.0f, Line);
            c.Fill(p => SdCapsule(L(p), new V(7f, -4f), new V(10f, -1f), 3.1f) * s, Skin);   // the thumb's root joins the palm
            c.FillStroke(sleeve, Sleeve, 1.0f, Line);
        }

        /// <summary>Short motion ticks: n strokes fanned around the direction `deg`, from radius r0 to r1 around `at`.</summary>
        static void Ticks(Canvas c, V at, float deg, float spread, int n, float r0, float r1, Rgba col)
        {
            for (int k = 0; k < n; k++)
            {
                float a = n == 1 ? deg : deg - spread * 0.5f + spread * k / (n - 1);
                V d = V.Dir(a), p0 = at + d * r0, p1 = at + d * r1;
                c.Fill(p => SdCapsule(p, p0, p1, 1.25f), col);
            }
        }

        /// <summary>A plus sign.</summary>
        static void Plus(Canvas c, V at, float size, float halfWidth, Rgba col)
        {
            c.Fill(p => Math.Min(SdCapsule(p, at - new V(size, 0f), at + new V(size, 0f), halfWidth), SdCapsule(p, at - new V(0f, size), at + new V(0f, size), halfWidth)), col);
        }

        /// <summary>A circular arrow ("again"): an arc around `at` of radius r from a0 to a1 degrees (counter-clockwise), the head at a1.</summary>
        static void ArcArrow(Canvas c, V at, float r, float a0, float a1, Rgba col)
        {
            const int segs = 18; const float shaft = 1.35f, head = 6f, half = 4.4f;
            var pts = new V[segs + 1];
            float end = a1 - head / r * 57.3f * 0.7f;          // the shaft stops under the head
            for (int k = 0; k <= segs; k++) pts[k] = at + V.Dir(a0 + (end - a0) * k / segs) * r;
            V tip = at + V.Dir(a1) * r, tangent = V.Dir(a1 + 90f), radial = V.Dir(a1);
            V b = tip - tangent * head;
            Sdf f = p =>
            {
                float d = 1e9f;
                for (int k = 0; k < segs; k++) d = Math.Min(d, SdCapsule(p, pts[k], pts[k + 1], shaft));
                return Math.Min(d, SdTriangle(p, tip, b + radial * half, b - radial * half) - 0.4f);
            };
            c.Fill(f, col);
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

        // ---------------------------------------------------------------- the cells

        /// <summary>The bottle squirting down onto the dog's back (drops in the air), the spout's aim `target`.</summary>
        static void Squirt(Canvas c, V target, float deg, float s)
        {
            V spoutDir = V.Dir(60f + deg);
            V tip = target - spoutDir * 21f * s / 0.9f;
            for (int k = 1; k <= 3; k++)
            {
                V drop = tip + spoutDir * (4.5f * k + 1f) * s / 0.9f;
                float r = (1.4f + 0.25f * k) * s / 0.9f;
                c.FillStroke(p => SdCircle(p, drop, r), Foam, 0.4f, FoamEdge);
            }
            Bottle(c, tip, deg, s);
        }

        /// <summary>A: ① the bottle squirting onto the back, and foam already on the head and the hip too (not only the back).</summary>
        static void CellFoamAnywhere(Canvas c)
        {
            c.Cell(0);
            V dog = new V(56f, 38f); const float s = 0.9f;
            Dog(c, dog, s);
            FoamHeap(c, 26.5f, 45f, 4.6f, 32f, 49f, 5f, 35.5f, 43.5f, 3.6f);          // hip
            FoamHeap(c, 85.5f, 62.5f, 4.4f, 90.5f, 66.5f, 4.2f, 84f, 57.5f, 3.2f);   // head (behind the ear)
            FoamHeap(c, 52f, 53f, 5f, 59f, 55.5f, 5.6f, 65.5f, 53f, 4.4f);          // back (the squirt lands here)
            Bubbles(c, 22f, 56f, 1.3f, 98f, 76f, 1.2f, 70f, 62f, 1.1f);
            Squirt(c, new V(59f, 60f), 160f, 0.9f);
        }

        /// <summary>B: ② (VR) an open hand with spread fingers above, the bottle let go beside it (upright, staying there),
        /// motion ticks between them; the foamy dog below on the right.</summary>
        static void CellRelease(Canvas c)
        {
            c.Cell(1);
            V dog = new V(108f, 31f); const float s = 0.66f;
            Dog(c, dog, s);
            FoamHeap(c, 98f, 41.5f, 3.8f, 104f, 43f, 4.2f, 110f, 41.5f, 3.4f, 87f, 39f, 3f);
            OpenHand(c, new V(44f, 74f), 80f, 1.0f, 1f);     // the arm from the lower left, fingers up, thumb toward the bottle
            Bottle(c, new V(94f, 104f), 8f, 0.9f);           // released: upright, a little to the right of the open hand
            Ticks(c, new V(92.5f, 82.5f), 180f, 64f, 3, 13.5f, 19.5f, Motion);   // "let go" ticks between the thumb and the bottle
        }

        /// <summary>C: ③ (VR) two hands, the left one from the upper left and the right one from the upper right, rubbing the dog's
        /// body all over with foam; back-and-forth arrows.</summary>
        static void CellBothHands(Canvas c)
        {
            c.Cell(2);
            V dog = new V(72f, 34f); const float s = 0.84f;
            Dog(c, dog, s);
            FoamHeap(c, 50f, 42f, 4.6f, 57f, 45f, 5.2f, 64f, 42f, 4.6f, 76f, 43f, 4.8f, 83f, 41f, 4.6f, 70f, 38.5f, 3.8f, 44f, 39f, 3.4f, 89f, 38f, 3.2f);
            Bubbles(c, 40f, 50f, 1.3f, 68f, 53f, 1.2f, 96f, 48f, 1.2f);
            Hand(c, new V(57f, 42f), -62f, 0.82f, -1f);      // left hand: from the upper left, onto the hip / back
            Hand(c, new V(83f, 41f), -112f, 0.82f, 1f);      // right hand: from the upper right, onto the shoulder
            FoamHeap(c, 54.5f, 41f, 2.3f, 59.5f, 40f, 2.1f, 81f, 40f, 2.2f, 86f, 40.5f, 2.1f);   // lather over the fingertips
            DoubleArrow(c, new V(14f, 58f), new V(34f, 58f), Motion);
            DoubleArrow(c, new V(136f, 58f), new V(156f, 58f), Motion);
        }

        /// <summary>D: ④ the bottle again, a thin foam left on the back, a "+" and a return arrow ("again from ①").</summary>
        static void CellAddMore(Canvas c)
        {
            c.Cell(3);
            V dog = new V(56f, 38f); const float s = 0.9f;
            Dog(c, dog, s);
            FoamHeap(c, 52f, 52.5f, 3.6f, 59f, 54f, 4.2f, 66f, 52.5f, 3.2f);          // a little foam left
            Squirt(c, new V(59f, 58f), 160f, 0.9f);
            ArcArrow(c, new V(130f, 86f), 15f, 200f, 470f, Motion);                     // "again"
            Plus(c, new V(130f, 86f), 6.5f, 1.9f, Motion);                               // "+ more"
        }

        /// <summary>E: ② (Desktop / phone) a hand holding the sponge up: the fingers behind it, the thumb across its front; motion
        /// ticks above ("picked up").</summary>
        static void CellHoldSponge(Canvas c)
        {
            c.Cell(4);
            const float s = 1.1f, fingerDeg = 78f;
            V tips = new V(88f, 82f);
            // the sponge over the fingers: centred at the hand's local (0, 15)
            V palm = tips - (new V(0f, 25f) * s).Rot(fingerDeg - 90f);
            V at = palm + (new V(0f, 15f) * s).Rot(fingerDeg - 90f);
            Hand(c, tips, fingerDeg, s, -1f, () => Sponge(c, at, fingerDeg - 90f, 1.55f));
            Ticks(c, at, 90f, 0f, 1, 25f, 32f, Motion);
            Ticks(c, at, 122f, 0f, 1, 29f, 36f, Motion);
            Ticks(c, at, 58f, 0f, 1, 29f, 36f, Motion);
        }

        /// <summary>F: ③ (Desktop / phone) the sponge on the dog's side, the back-and-forth arrow and foam (the v1 drawing).</summary>
        static void CellSponge(Canvas c)
        {
            c.Cell(5);
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
            CellFoamAnywhere(c);
            CellRelease(c);
            CellBothHands(c);
            CellAddMore(c);
            CellHoldSponge(c);
            CellSponge(c);
            return c.px;   // cells 6, 7 stay empty (transparent)
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
