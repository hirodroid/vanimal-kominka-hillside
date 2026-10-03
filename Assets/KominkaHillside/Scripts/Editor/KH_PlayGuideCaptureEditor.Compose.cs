using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    // Guide/13 (IMP-UI-002, docs/plans/2026-10-03-onsite-guides.md §3.3 / §4.1 step 5): the pixel side. A small float RGBA image
    // (bottom-left origin, like Texture2D.GetPixels), resampling, and the overlay marks drawn with signed distances so every cut
    // carries the same frame / ring / arrow / inset / hand in the same colours and widths. Nothing here touches the scene; the
    // drawing version is PlayGuideCutsVersion (bump it when a mark's look changes so the manifest says the atlas was redrawn).
    // Pixel coordinates: pixel (i, j) covers [i, i+1] x [j, j+1]; its centre is (i + 0.5, j + 0.5). Colours are sRGB values
    // written straight into the PNG (the renders are read back from sRGB render textures, as Guide/3 does).
    public static partial class KH_PlayGuideCaptureEditor
    {
        // ---------------------------------------------------------------- style (plan §3.3)
        public static readonly Color Vermilion = new Color(0xB5 / 255f, 0x43 / 255f, 0x2A / 255f, 1f);   // the boards' accent #B5432A
        static readonly Color White = Color.white;
        // board-scale pass (a 512 px cut is printed 320-380 mm wide): the vermilion strokes get a thin DARK edge instead of the
        // white one, so they stay fully vermilion instead of blending to pink. #2A1A12 at 80 %.
        static readonly Color EdgeDark = new Color(0x2A / 255f, 0x1A / 255f, 0x12 / 255f, 0.8f);
        static readonly Color ShadowCol = new Color(0f, 0f, 0f, 0.38f);
        static readonly Color LeaderUnder = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color HandFill = new Color(1f, 1f, 1f, 0.16f);
        static readonly Color HandUnder = new Color(0f, 0f, 0f, 0.40f);
        public static readonly Color PaleSky = new Color(0xCF / 255f, 0xE3 / 255f, 0xF0 / 255f, 1f);         // #CFE3F0 (plan §4.1 step 3)
        static readonly Color MenuBackdrop = new Color(0.16f, 0.15f, 0.14f, 1f);                             // letterbox around the menu panel
        // widths in px of a 512 x 384 cut (plan §3.3: frame 6 px, arrow 8 px, inset border 4 px, white edging 2 px)
        // widths in px of a 512 x 384 cut. Board-scale pass: strokes 12 px (was 6, read pink at 340 mm), dark edge 1.75 px,
        // arrow heads scaled with the shaft, inset border 5 px white between dark hairlines.
        const float FrameWidth = 12f, Edge = 1.75f, FrameRadius = 12f;
        /// <summary>Gap between the framed target and the frame's inner (dark) edge: the frame grows OUTWARD and never covers the
        /// target's face. The menu cuts keep this area undimmed.</summary>
        const float FramePad = 1f + Edge;
        const float ArrowWidth = 12f, ArrowHeadLength = 38f, ArrowHeadHalf = 22f;
        const float InsetBorder = 5f, LeaderWidth = 3f;
        const float DashOn = 24f, DashOff = 14f;
        const float HandLine = 5f;

        // ================================================================ image
        public sealed class Img
        {
            public readonly int w, h;
            public readonly Color[] px;

            public Img(int width, int height, Color fill)
            {
                if (width <= 0 || height <= 0) throw new ArgumentException("image size " + width + "x" + height);
                w = width; h = height; px = new Color[w * h];
                for (int i = 0; i < px.Length; i++) px[i] = fill;
            }

            public static Img FromTexture(Texture2D t)
            {
                var img = new Img(t.width, t.height, Color.black);
                var c = t.GetPixels();
                Array.Copy(c, img.px, c.Length);
                for (int i = 0; i < img.px.Length; i++) img.px[i].a = 1f;
                return img;
            }

            public Color Get(int x, int y)
            {
                x = x < 0 ? 0 : x >= w ? w - 1 : x;
                y = y < 0 ? 0 : y >= h ? h - 1 : y;
                return px[y * w + x];
            }

            /// <summary>Bilinear sample at a continuous pixel coordinate (pixel centres at i + 0.5), clamped at the edges.</summary>
            public Color Bilinear(float x, float y)
            {
                x -= 0.5f; y -= 0.5f;
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                var a = Color.Lerp(Get(x0, y0), Get(x0 + 1, y0), fx);
                var b = Color.Lerp(Get(x0, y0 + 1), Get(x0 + 1, y0 + 1), fx);
                return Color.Lerp(a, b, fy);
            }

            /// <summary>The source rectangle (pixel coordinates of this image) resampled to ow x oh with ss x ss bilinear taps per
            /// output pixel (an area average for reductions; deterministic).</summary>
            public Img Resample(Rect src, int ow, int oh, int ss = 4)
            {
                var o = new Img(ow, oh, Color.black);
                float sx = src.width / ow, sy = src.height / oh, inv = 1f / (ss * ss);
                for (int j = 0; j < oh; j++)
                    for (int i = 0; i < ow; i++)
                    {
                        Color acc = new Color(0f, 0f, 0f, 0f);
                        for (int b = 0; b < ss; b++)
                            for (int a = 0; a < ss; a++)
                                acc += Bilinear(src.x + (i + (a + 0.5f) / ss) * sx, src.y + (j + (b + 0.5f) / ss) * sy);
                        acc *= inv; acc.a = 1f;
                        o.px[j * ow + i] = acc;
                    }
                return o;
            }

            /// <summary>Exact 2 x 2 box reduction (the world shots are rendered at twice the cut size).</summary>
            public Img Half()
            {
                var o = new Img(w / 2, h / 2, Color.black);
                for (int j = 0; j < o.h; j++)
                    for (int i = 0; i < o.w; i++)
                    {
                        var c = (Get(2 * i, 2 * j) + Get(2 * i + 1, 2 * j) + Get(2 * i, 2 * j + 1) + Get(2 * i + 1, 2 * j + 1)) * 0.25f;
                        c.a = 1f; o.px[j * o.w + i] = c;
                    }
                return o;
            }

            public void Blit(Img src, int ox, int oy)
            {
                for (int j = 0; j < src.h; j++)
                {
                    int y = oy + j; if (y < 0 || y >= h) continue;
                    for (int i = 0; i < src.w; i++)
                    {
                        int x = ox + i; if (x < 0 || x >= w) continue;
                        px[y * w + x] = src.px[j * src.w + i];
                    }
                }
            }

            /// <summary>Blends colour c over the pixels whose centre has signed distance d (negative inside) to a shape.
            /// soft 0 = one-pixel anti-aliased edge; soft &gt; 0 = a blurred edge (shadows).</summary>
            public void Paint(Rect box, Func<Vector2, float> sd, Color c, float soft = 0f)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(box.xMin) - 2), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(box.xMax) + 2);
                int y0 = Mathf.Max(0, Mathf.FloorToInt(box.yMin) - 2), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(box.yMax) + 2);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = sd(new Vector2(x + 0.5f, y + 0.5f));
                        float cov = soft > 0f ? Mathf.Clamp01(0.5f - d / soft) : Mathf.Clamp01(0.5f - d);
                        if (cov <= 0f) continue;
                        int k = y * w + x;
                        var dst = px[k];
                        float a = c.a * cov;
                        dst.r += (c.r - dst.r) * a; dst.g += (c.g - dst.g) * a; dst.b += (c.b - dst.b) * a; dst.a = 1f;
                        px[k] = dst;
                    }
            }

            /// <summary>Multiplies every pixel outside the keep rectangles by f (the menu cuts: the eye goes to the framed places).</summary>
            public void Dim(float f, params Rect[] keep)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        bool kept = false;
                        foreach (var r in keep) if (r.Contains(p)) { kept = true; break; }
                        if (kept) continue;
                        int k = y * w + x; var c = px[k];
                        px[k] = new Color(c.r * f, c.g * f, c.b * f, 1f);
                    }
            }

            public Texture2D ToTexture()
            {
                var t = new Texture2D(w, h, TextureFormat.RGB24, false, false) { hideFlags = HideFlags.HideAndDontSave };
                var c32 = new Color32[px.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    c32[i] = new Color32(B(c.r), B(c.g), B(c.b), 255);
                }
                t.SetPixels32(c32); t.Apply(false);
                return t;
            }

            static byte B(float v) { return (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f); }

            public byte[] Png()
            {
                var t = ToTexture();
                try { return t.EncodeToPNG(); }
                finally { Object.DestroyImmediate(t); }
            }
        }

        // ================================================================ signed distances (px)
        static float SdRoundRect(Vector2 p, Rect r, float radius)
        {
            var c = r.center; var half = r.size * 0.5f;
            radius = Mathf.Min(radius, Mathf.Min(half.x, half.y));
            float qx = Mathf.Abs(p.x - c.x) - half.x + radius, qy = Mathf.Abs(p.y - c.y) - half.y + radius;
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        static float SdSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var pa = p - a; var ba = b - a;
            float len2 = Vector2.Dot(ba, ba);
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(pa, ba) / len2) : 0f;
            return (pa - ba * t).magnitude;
        }

        /// <summary>Approximate distance to an axis-aligned ellipse (exact for circles; good enough for a ring of a few px).</summary>
        static float SdEllipse(Vector2 p, Vector2 c, Vector2 rad)
        {
            var q = new Vector2((p.x - c.x) / Mathf.Max(rad.x, 1e-3f), (p.y - c.y) / Mathf.Max(rad.y, 1e-3f));
            return (q.magnitude - 1f) * Mathf.Min(rad.x, rad.y);
        }

        static float Cross2(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

        static float SdTriangle(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            Vector2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
            Vector2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
            Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(Cross2(e0, e2));   // Inigo Quilez's sdTriangle (winding-independent)
            float d0 = pq0.sqrMagnitude, d1 = pq1.sqrMagnitude, d2 = pq2.sqrMagnitude;
            float c0 = s * Cross2(v0, e0), c1 = s * Cross2(v1, e1), c2 = s * Cross2(v2, e2);
            float d = Mathf.Min(d0, Mathf.Min(d1, d2));
            float inside = Mathf.Min(c0, Mathf.Min(c1, c2));
            return -Mathf.Sqrt(d) * Mathf.Sign(inside);
        }

        static Rect Grow(Rect r, float by) { return new Rect(r.x - by, r.y - by, r.width + 2f * by, r.height + 2f * by); }

        static Rect BoundsOf(params Vector2[] pts)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in pts) { x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        // ================================================================ marks (each records itself for the manifest / report)
        public sealed class MarkPx { public string kind; public Rect rect; public string note; }

        /// <summary>Rounded frame around a target rectangle: white edging under a vermilion stroke. Returns the outer rectangle.</summary>
        static Rect DrawFrame(Img img, Rect target, List<MarkPx> marks, string note)
        {
            // centre line: target + gap + dark edge + half the stroke, so the whole mark lies outside the target
            var r = Grow(target, FramePad + Edge + FrameWidth * 0.5f);
            Func<Vector2, float> ring = p => Mathf.Abs(SdRoundRect(p, r, FrameRadius));
            img.Paint(Grow(r, FrameWidth), p => ring(p) - (FrameWidth * 0.5f + Edge), EdgeDark);
            img.Paint(Grow(r, FrameWidth), p => ring(p) - FrameWidth * 0.5f, Vermilion);
            var outer = Grow(r, FrameWidth * 0.5f + Edge);
            marks.Add(new MarkPx { kind = "frame", rect = outer, note = note });
            return outer;
        }

        /// <summary>Elliptic ring around a target rectangle (padded), same edging. Returns the ring's outer rectangle.</summary>
        static Rect DrawRing(Img img, Rect target, float pad, List<MarkPx> marks, string note)
        {
            var c = target.center;
            // an ellipse through the padded rectangle's corners (x/a)^2 + (y/b)^2 = 1 with the aspect kept: scale by sqrt(2)
            var rad = new Vector2(target.width * 0.5f + pad, target.height * 0.5f + pad) * 1.18f;
            rad += Vector2.one * (Edge + FrameWidth * 0.5f);   // the inner edge stays where the old ring was: outward growth only
            rad.x = Mathf.Max(rad.x, 24f); rad.y = Mathf.Max(rad.y, 24f);
            var box = new Rect(c.x - rad.x, c.y - rad.y, rad.x * 2f, rad.y * 2f);
            img.Paint(Grow(box, FrameWidth), p => Mathf.Abs(SdEllipse(p, c, rad)) - (FrameWidth * 0.5f + Edge), EdgeDark);
            img.Paint(Grow(box, FrameWidth), p => Mathf.Abs(SdEllipse(p, c, rad)) - FrameWidth * 0.5f, Vermilion);
            var outer = Grow(box, FrameWidth * 0.5f + Edge);
            marks.Add(new MarkPx { kind = "ring", rect = outer, note = note });
            return outer;
        }

        /// <summary>Straight arrow from 'from' to the tip 'to' (shaft + triangular head), white edged.</summary>
        static void DrawArrow(Img img, Vector2 from, Vector2 to, List<MarkPx> marks, string note)
        {
            var d = to - from; float len = d.magnitude;
            if (len < ArrowHeadLength + 4f) { from = to - (len > 1e-3f ? d / len : Vector2.up) * (ArrowHeadLength + 24f); d = to - from; len = d.magnitude; }
            var u = d / len; var n = new Vector2(-u.y, u.x);
            var neck = to - u * ArrowHeadLength;
            var shaftEnd = to - u * (ArrowHeadLength * 0.6f);
            Vector2 h0 = to, h1 = neck + n * ArrowHeadHalf, h2 = neck - n * ArrowHeadHalf;
            var box = Grow(BoundsOf(from, to, h1, h2), ArrowWidth * 0.5f + Edge + 2f);
            img.Paint(box, p => SdSegment(p, from, shaftEnd) - (ArrowWidth * 0.5f + Edge), EdgeDark);
            img.Paint(box, p => SdTriangle(p, h0, h1, h2) - Edge, EdgeDark);
            img.Paint(box, p => SdSegment(p, from, shaftEnd) - ArrowWidth * 0.5f, Vermilion);
            img.Paint(box, p => SdTriangle(p, h0, h1, h2), Vermilion);
            marks.Add(new MarkPx { kind = "arrow", rect = box, note = note });
        }

        /// <summary>Dashed curve along the points with an arrow head at the last point (the ball's throwing arc, B2).</summary>
        static void DrawDashedArrow(Img img, List<Vector2> pts, List<MarkPx> marks, string note)
        {
            if (pts == null || pts.Count < 2) return;
            // cut the last head length off the curve so the dashes stop at the head's neck
            var tip = pts[pts.Count - 1];
            var before = pts[pts.Count - 2];
            var u = (tip - before).normalized; var nrm = new Vector2(-u.y, u.x);
            var neck = tip - u * ArrowHeadLength;
            var dashes = new List<KeyValuePair<Vector2, Vector2>>();
            bool on = true; float left = DashOn;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = i == pts.Count - 1 ? neck : pts[i];
                float seg = (b - a).magnitude; if (seg < 1e-3f) continue;
                var dir = (b - a) / seg; float t = 0f;
                while (t < seg)
                {
                    float step = Mathf.Min(left, seg - t);
                    if (on) dashes.Add(new KeyValuePair<Vector2, Vector2>(a + dir * t, a + dir * (t + step)));
                    t += step; left -= step;
                    if (left <= 1e-4f) { on = !on; left = on ? DashOn : DashOff; }
                }
            }
            var all = new List<Vector2>(pts); all.Add(neck + nrm * ArrowHeadHalf); all.Add(neck - nrm * ArrowHeadHalf);
            var box = Grow(BoundsOf(all.ToArray()), ArrowWidth * 0.5f + Edge + 2f);
            Vector2 h1 = neck + nrm * ArrowHeadHalf, h2 = neck - nrm * ArrowHeadHalf;
            foreach (var s in dashes) { var a = s.Key; var b = s.Value; img.Paint(Grow(BoundsOf(a, b), ArrowWidth), p => SdSegment(p, a, b) - (ArrowWidth * 0.5f + Edge), EdgeDark); }
            img.Paint(box, p => SdTriangle(p, tip, h1, h2) - Edge, EdgeDark);
            foreach (var s in dashes) { var a = s.Key; var b = s.Value; img.Paint(Grow(BoundsOf(a, b), ArrowWidth), p => SdSegment(p, a, b) - ArrowWidth * 0.5f, Vermilion); }
            img.Paint(box, p => SdTriangle(p, tip, h1, h2), Vermilion);
            marks.Add(new MarkPx { kind = "dashed-arrow", rect = box, note = note });
        }

        /// <summary>Zoom inset: the source rectangle of the hi-res render shown magnified in dstRect (white border, drop shadow,
        /// thin leader line back to where it came from). sourceInCut = the same rectangle in cut pixels.</summary>
        static void DrawInset(Img img, Img hi, Rect hiSrc, Rect dstRect, Rect sourceInCut, List<MarkPx> marks, string note, Rect? keepInDst = null, float dim = 1f)
        {
            // leader first (under the inset): nearest edge midpoints
            Vector2 a = Nearest(sourceInCut, dstRect.center), b = Nearest(dstRect, sourceInCut.center);
            var lbox = Grow(BoundsOf(a, b), 6f);
            img.Paint(lbox, p => SdSegment(p, a, b) - (LeaderWidth * 0.5f + 1.5f), LeaderUnder);
            img.Paint(lbox, p => SdSegment(p, a, b) - LeaderWidth * 0.5f, White);
            // shadow
            var sh = new Rect(dstRect.x + 5f, dstRect.y - 5f, dstRect.width, dstRect.height);
            img.Paint(Grow(sh, 10f), p => SdRoundRect(p, sh, 4f), ShadowCol, 6f);
            // content
            int w = Mathf.RoundToInt(dstRect.width), h = Mathf.RoundToInt(dstRect.height);
            var content = hi.Resample(hiSrc, w, h);
            if (dim < 1f)
            {
                var keep = keepInDst.HasValue ? new Rect(keepInDst.Value.x - dstRect.x, keepInDst.Value.y - dstRect.y, keepInDst.Value.width, keepInDst.Value.height) : Rect.zero;
                content.Dim(dim, keep);
            }
            img.Blit(content, Mathf.RoundToInt(dstRect.x), Mathf.RoundToInt(dstRect.y));
            // border (on the inset's edge, drawn half outside so the content stays whole)
            var border = Grow(dstRect, InsetBorder * 0.5f);
            img.Paint(Grow(border, InsetBorder + Edge + 2f), p => Mathf.Abs(SdRoundRect(p, border, 3f)) - (InsetBorder * 0.5f + Edge), EdgeDark);
            img.Paint(Grow(border, InsetBorder + Edge + 2f), p => Mathf.Abs(SdRoundRect(p, border, 3f)) - InsetBorder * 0.5f, White);
            marks.Add(new MarkPx { kind = "inset", rect = Grow(border, InsetBorder * 0.5f + Edge), note = note });
        }

        static Vector2 Nearest(Rect r, Vector2 toward)
        {
            var c = r.center; var d = toward - c;
            if (Mathf.Abs(d.x) * r.height > Mathf.Abs(d.y) * r.width) return new Vector2(d.x > 0 ? r.xMax : r.xMin, c.y);
            return new Vector2(c.x, d.y > 0 ? r.yMax : r.yMin);
        }

        /// <summary>A hand line drawing (palm, four fingers over the disc's near rim, thumb) whose finger tips sit at 'grip'.
        /// s = px per unit (a unit is about one palm width). One outline of the union, so no inner lines.</summary>
        static void DrawHand(Img img, Vector2 grip, float s, List<MarkPx> marks, string note)
        {
            Vector2 P(float x, float y) { return grip + new Vector2(x, y) * s; }
            var palm = new Rect(P(-0.30f, -1.05f), new Vector2(0.78f, 0.62f) * s);
            var fingers = new List<KeyValuePair<Vector2, Vector2>>();
            for (int i = 0; i < 4; i++) fingers.Add(new KeyValuePair<Vector2, Vector2>(P(-0.22f + 0.19f * i, -0.55f), P(-0.20f + 0.18f * i, 0.02f)));
            var thumbA = P(-0.28f, -0.85f); var thumbB = P(-0.62f, -0.30f);
            float fr = 0.085f * s, tr = 0.095f * s;
            Func<Vector2, float> sd = p =>
            {
                float d = SdRoundRect(p, palm, 0.18f * s);
                foreach (var f in fingers) d = Mathf.Min(d, SdSegment(p, f.Key, f.Value) - fr);
                d = Mathf.Min(d, SdSegment(p, thumbA, thumbB) - tr);
                // wrist
                d = Mathf.Min(d, SdRoundRect(p, new Rect(P(-0.18f, -1.55f), new Vector2(0.56f, 0.6f) * s), 0.12f * s));
                return d;
            };
            var box = Grow(new Rect(P(-0.8f, -1.6f), new Vector2(1.5f, 1.8f) * s), 8f);
            img.Paint(box, sd, HandFill);
            img.Paint(box, p => Mathf.Abs(sd(p)) - (HandLine * 0.5f + Edge), HandUnder);
            img.Paint(box, p => Mathf.Abs(sd(p)) - HandLine * 0.5f, White);
            marks.Add(new MarkPx { kind = "hand", rect = box, note = note });
        }

        /// <summary>Optional step badge (BakeStepBadge): a vermilion disc with a white digit 1-4 drawn as strokes (no font).</summary>
        static void DrawBadge(Img img, Vector2 c, float r, int n, List<MarkPx> marks)
        {
            var box = Grow(new Rect(c.x - r, c.y - r, 2f * r, 2f * r), 4f);
            img.Paint(box, p => (p - c).magnitude - (r + Edge), EdgeDark);
            img.Paint(box, p => (p - c).magnitude - r, Vermilion);
            float u = r * 0.42f, sw = r * 0.13f;
            Vector2 Q(float x, float y) { return c + new Vector2(x, y) * u; }
            var segs = new List<KeyValuePair<Vector2, Vector2>>();
            void S(Vector2 a, Vector2 b) { segs.Add(new KeyValuePair<Vector2, Vector2>(a, b)); }
            switch (n)
            {
                case 1: S(Q(0.1f, 1f), Q(0.1f, -1f)); S(Q(0.1f, 1f), Q(-0.45f, 0.55f)); break;
                case 2: S(Q(-0.6f, 0.6f), Q(-0.2f, 1f)); S(Q(-0.2f, 1f), Q(0.35f, 1f)); S(Q(0.35f, 1f), Q(0.65f, 0.6f)); S(Q(0.65f, 0.6f), Q(-0.6f, -1f)); S(Q(-0.6f, -1f), Q(0.7f, -1f)); break;
                case 3: S(Q(-0.6f, 1f), Q(0.6f, 1f)); S(Q(0.6f, 1f), Q(0f, 0.1f)); S(Q(0f, 0.1f), Q(0.6f, -0.35f)); S(Q(0.6f, -0.35f), Q(0.2f, -1f)); S(Q(0.2f, -1f), Q(-0.6f, -0.85f)); break;
                default: S(Q(0.35f, -1f), Q(0.35f, 1f)); S(Q(0.35f, 1f), Q(-0.65f, -0.35f)); S(Q(-0.65f, -0.35f), Q(0.75f, -0.35f)); break;
            }
            foreach (var sg in segs) { var a = sg.Key; var b = sg.Value; img.Paint(box, p => SdSegment(p, a, b) - sw, White); }
            marks.Add(new MarkPx { kind = "badge", rect = box, note = n.ToString() });
        }

        // ================================================================ atlas
        /// <summary>Cells in reading order: row 0 (top) F1 F2 F3 F4, row 1 N1 N2 B1 B2 (plan §3.2 / §3.3).</summary>
        public static readonly string[] CutOrder = { "F1", "F2", "F3", "F4", "N1", "N2", "B1", "B2" };
        public const int CutW = 512, CutH = 384, AtlasCols = 4, AtlasRows = 2;
        public const int AtlasW = CutW * AtlasCols, AtlasH = CutH * AtlasRows;   // 2048 x 768 (plan §4.4)
        /// <summary>The uv rect of each cut leaves this many texels out at every edge, so mipmaps / ASTC 6x6 blocks that straddle
        /// two cells never show the neighbour's edge on the board.</summary>
        public const float UvInsetPx = 2f;

        static RectInt CellOf(int index)
        {
            int col = index % AtlasCols, row = index / AtlasCols;
            return new RectInt(col * CutW, AtlasH - (row + 1) * CutH, CutW, CutH);
        }

        static Img PackAtlas(Dictionary<string, Img> cuts)
        {
            var atlas = new Img(AtlasW, AtlasH, MenuBackdrop);
            for (int i = 0; i < CutOrder.Length; i++)
            {
                Img c;
                if (!cuts.TryGetValue(CutOrder[i], out c) || c == null) continue;
                var cell = CellOf(i);
                atlas.Blit(c, cell.x, cell.y);
            }
            return atlas;
        }

        /// <summary>A cut pixel point (0..512, 0..384, bottom-left) to the normalised coordinates of the visible (uv) area.</summary>
        static Vector2 Norm(Vector2 cutPx)
        {
            float vw = CutW - 2f * UvInsetPx, vh = CutH - 2f * UvInsetPx;
            return new Vector2((cutPx.x - UvInsetPx) / vw, (cutPx.y - UvInsetPx) / vh);
        }

        static Vector2 NormSize(Vector2 sizePx)
        {
            return new Vector2(sizePx.x / (CutW - 2f * UvInsetPx), sizePx.y / (CutH - 2f * UvInsetPx));
        }

        /// <summary>A grey cell with a vermilion cross: a cut that failed (only ever reaches Temp; the atlas is not written then).</summary>
        static Img Placeholder()
        {
            var img = new Img(CutW, CutH, new Color(0.35f, 0.35f, 0.35f, 1f));
            img.Paint(new Rect(0, 0, CutW, CutH), p => SdSegment(p, new Vector2(60, 60), new Vector2(CutW - 60, CutH - 60)) - 6f, Vermilion);
            img.Paint(new Rect(0, 0, CutW, CutH), p => SdSegment(p, new Vector2(60, CutH - 60), new Vector2(CutW - 60, 60)) - 6f, Vermilion);
            return img;
        }
    }
}
