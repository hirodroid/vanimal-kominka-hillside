using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using KominkaDogRun.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    // IMP-UI-002 wave 2 (docs/plans/2026-10-03-onsite-guides.md §3): board A's frisbee and ball papers built from Guide/13's cut atlas.
    //
    // Guide/13 (KH_PlayGuideCaptureEditor, not edited here) writes KH_PlayGuideCuts.png (2048 x 768, 4 x 2 cells of 512 x 384) and
    // KH_PlayGuideCuts.json: per cut its uvRect, the step-badge place and the callouts (the menu's own text keys with their place in
    // the cut). This file only reads that manifest: each cut is a RawImage with the cut's uvRect, the step number is UI (a vermilion
    // circle + TMP digit at the manifest's badge place), every word over a cut is a TMP label bound through UILocalizationBindings.json
    // (KD_Language follows the viewer's language), nothing is baked into the image.
    //
    // Callout placement: a callout the manifest puts inside a frame (a button in the menu) is written on that button's face; any other
    // callout (the tab, the dog's name, the throw-style label) gets a dark rounded backing and is trimmed so that neither cover a frame.
    public static partial class KH_GuideBoardsEditor
    {
        // ---------------------------------------------------------------- papers (canvas mm; the strip's bottom is at 377, the canvas bottom at -720)
        class PaperSpec { public string name, titleKey; public float x, width; }
        static readonly PaperSpec PFrisbee = new PaperSpec { name = "Paper_Frisbee", titleKey = "guide.frisbee.title", x = -770f, width = 1510f };
        static readonly PaperSpec PBall = new PaperSpec { name = "Paper_Ball", titleKey = "guide.ball.title", x = 385f, width = 740f };
        static readonly PaperSpec PShop = new PaperSpec { name = "Paper_Shop", titleKey = "guide.shop.title", x = 1155f, width = 740f };
        static readonly PaperSpec[] Papers = { PFrisbee, PBall, PShop };
        const float PaperH = 1010f, PaperY = -195f;   // top 310, bottom -700 (the old 1060 mm papers' bottom)

        /// <summary>Removed by wave 2: their objects are stale (RemoveStale), their keys / bindings are gone from the catalogs.</summary>
        static readonly string[] RemovedPapers = { "Paper_Walk", "Paper_Wash" };
        static readonly string[] RemovedKeys = { "guide.walk.title", "guide.walk.body", "guide.wash.title", "guide.wash.body", "guide.frisbee.body" };

        // ---------------------------------------------------------------- cut slots (paper-local mm, centre origin; paper 1010 tall: top +505)
        class CutSlot
        {
            public string id; public PaperSpec paper; public Vector2 pos, size, capPos, capSize; public TextAlignmentOptions capAlign;
        }
        const float StepW = 340f, StepH = 255f, StepY = 247.5f, StepCapY = 59f;
        static readonly Vector2 StepCapSize = new Vector2(350f, 110f);
        const float NoteY = -132f;
        static readonly Vector2 NoteCutSize = new Vector2(320f, 240f), NoteCapSize = new Vector2(386f, 240f);
        static readonly Vector2 BallSize = new Vector2(380f, 285f), BallCapSize = new Vector2(680f, 80f);
        static readonly CutSlot[] Slots =
        {
            Step("F1", -555f), Step("F2", -185f), Step("F3", 185f), Step("F4", 555f),
            new CutSlot { id = "N1", paper = PFrisbee, pos = new Vector2(-570f, NoteY), size = NoteCutSize, capPos = new Vector2(-203f, NoteY), capSize = NoteCapSize, capAlign = TextAlignmentOptions.MidlineLeft },
            new CutSlot { id = "N2", paper = PFrisbee, pos = new Vector2(170f, NoteY), size = NoteCutSize, capPos = new Vector2(537f, NoteY), capSize = NoteCapSize, capAlign = TextAlignmentOptions.MidlineLeft },
            new CutSlot { id = "B1", paper = PBall, pos = new Vector2(0f, 232.5f), size = BallSize, capPos = new Vector2(0f, 44f), capSize = BallCapSize, capAlign = TextAlignmentOptions.Top },
            new CutSlot { id = "B2", paper = PBall, pos = new Vector2(0f, -148.5f), size = BallSize, capPos = new Vector2(0f, -337f), capSize = BallCapSize, capAlign = TextAlignmentOptions.Top },
        };
        static CutSlot Step(string id, float x)
        {
            return new CutSlot { id = id, paper = PFrisbee, pos = new Vector2(x, StepY), size = new Vector2(StepW, StepH), capPos = new Vector2(x, StepCapY), capSize = StepCapSize, capAlign = TextAlignmentOptions.Top };
        }
        // the closing note under the cuts: frisbee 4 lines, ball 2 lines (top-left, like the old bodies)
        static readonly Vector2 FrisbeeMorePos = new Vector2(0f, -376.5f), FrisbeeMoreSize = new Vector2(1460f, 217f);
        static readonly Vector2 BallMorePos = new Vector2(0f, -437.5f), BallMoreSize = new Vector2(680f, 105f);
        const float CapMax = 38f, CapMin = 24f, CapWarnMm = 30f, MoreMax = 34f, MoreMin = 22f, MoreLineSpacing = 12f;
        const float CutEdgeMm = 4f;                 // a sumi border around each photo (the washi is light; the photos' edges are not)
        const float BadgeMinMm = 48f, BadgeRingMm = 4f, BadgeDigitShare = 0.70f;
        const float CalloutMinMm = 9f, CalloutPadMm = 2f, CalloutGapPx = 1f;
        static readonly Color CalloutBack = new Color(0.12f, 0.10f, 0.08f, 0.88f);   // KD_UiSpriteEditor.SurfacePanel, a little denser
        /// <summary>A frame's drawn band, inward from the manifest's (outer) frame rect: FrameWidth 6 + 2 x Edge 2 px
        /// (KH_PlayGuideCaptureEditor.Compose DrawFrame: outer = target + FramePad + 3 + 2).</summary>
        const float FrameBandPx = 10f;

        /// <summary>Callout keys the manifest takes from the course catalog (CourseUITranslations.json), which KD_Language holds as
        /// extension keys. UILocalizationBindings.json binds base-catalog keys only (KD_LocalizationEditor.Bind / Text resolve keys in
        /// UITranslations.json), so the board binds a guide.cut.* mirror whose 13 texts Guide/2 requires to equal the course text.</summary>
        static readonly Dictionary<string, string> CalloutMirrors = new Dictionary<string, string>
        {
            { "course.toy.throw.label", "guide.cut.throw.label" },
            { "course.toy.throw.straight", "guide.cut.throw.straight" },
            { "course.toy.throw.curve", "guide.cut.throw.curve" },
        };
        static string BoardKey(string calloutKey) { string m; return CalloutMirrors.TryGetValue(calloutKey, out m) ? m : calloutKey; }

        static string PaperPath(PaperSpec p) { return Asob.name + "/" + CanvasName + "/" + p.name; }
        static string CutName(string id) { return "Cut_" + id; }

        // ================================================================ the manifest

        static KH_PlayGuideCaptureEditor.Manifest LoadCuts(out Texture2D atlas)
        {
            atlas = null;
            string path = KH_PlayGuideCaptureEditor.ManifestPath;
            if (!File.Exists(path)) throw new InvalidOperationException(path + " missing: run Guide/13 - Capture Play Guide Cuts first; nothing changed");
            KH_PlayGuideCaptureEditor.Manifest m;
            try { m = JsonUtility.FromJson<KH_PlayGuideCaptureEditor.Manifest>(File.ReadAllText(path)); }
            catch (Exception e) { throw new InvalidOperationException(path + " unreadable (" + e.Message + "): run Guide/13 again; nothing changed"); }
            if (m == null || m.cuts == null) throw new InvalidOperationException(path + " has no cuts: run Guide/13 again; nothing changed");
            atlas = string.IsNullOrEmpty(m.atlas) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(m.atlas);
            if (atlas == null) throw new InvalidOperationException("the cut atlas " + (m.atlas ?? KH_PlayGuideCaptureEditor.AtlasPath) + " is missing or not imported: run Guide/13 - Capture Play Guide Cuts first; nothing changed");
            return m;
        }

        /// <summary>Anything that makes the manifest unusable for this layout (Guide/1 refuses, Guide/2 fails).</summary>
        static List<string> CutsProblems(KH_PlayGuideCaptureEditor.Manifest m, Texture2D atlas)
        {
            var p = new List<string>();
            if (m.format != KH_PlayGuideCaptureEditor.ManifestFormat) p.Add("format '" + m.format + "' (expected " + KH_PlayGuideCaptureEditor.ManifestFormat + ")");
            if (m.drawVersion != KH_PlayGuideCaptureEditor.PlayGuideCutsVersion) p.Add("drawVersion " + m.drawVersion + " (the capture code is at " + KH_PlayGuideCaptureEditor.PlayGuideCutsVersion + ")");
            if (m.atlas != KH_PlayGuideCaptureEditor.AtlasPath) p.Add("atlas path " + m.atlas);
            if (atlas != null && (atlas.width != m.atlasWidth || atlas.height != m.atlasHeight)) p.Add("atlas " + atlas.width + "x" + atlas.height + " vs manifest " + m.atlasWidth + "x" + m.atlasHeight);
            if (m.atlasWidth <= 0 || m.atlasHeight <= 0 || m.cutWidth <= 0 || m.cutHeight <= 0) p.Add("sizes missing");
            var ids = m.cuts.Select(x => x.id).ToList();
            foreach (var s in Slots) if (ids.Count(x => x == s.id) != 1) p.Add("cut " + s.id + " x" + ids.Count(x => x == s.id));
            foreach (var x in ids) if (!Slots.Any(s => s.id == x)) p.Add("unknown cut " + x);
            foreach (var e in m.cuts)
            {
                if (e.uvW <= 0f || e.uvH <= 0f || e.uvX < 0f || e.uvY < 0f || e.uvX + e.uvW > 1.0001f || e.uvY + e.uvH > 1.0001f) p.Add(e.id + " uvRect outside the atlas");
                if (string.IsNullOrEmpty(e.captionKey)) p.Add(e.id + " has no captionKey");
                if (e.callouts == null) e.callouts = new List<KH_PlayGuideCaptureEditor.Callout>();
                if (e.marks == null) e.marks = new List<KH_PlayGuideCaptureEditor.MarkEntry>();
            }
            if (File.Exists(KH_PlayGuideCaptureEditor.AtlasPath) && !string.IsNullOrEmpty(m.atlasSha256))
            {
                string sha;
                using (var h = SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(KH_PlayGuideCaptureEditor.AtlasPath))).Replace("-", "").ToLowerInvariant();
                if (sha != m.atlasSha256) p.Add("the atlas bytes differ from the manifest's sha256 (one was rewritten without the other)");
            }
            else p.Add("no atlas sha256 / file to compare");
            return p;
        }

        static KH_PlayGuideCaptureEditor.CutEntry CutOf(Ctx c, string id) { return c.cuts.cuts.First(x => x.id == id); }

        /// <summary>The cut captions and callouts in a fixed order (slots, then each cut's callouts in manifest order: Call0, Call1, ...).
        /// UILocalizationBindings.json must hold exactly these (Resolve compares both ways).</summary>
        static IEnumerable<LabelSpec> CutLabels(KH_PlayGuideCaptureEditor.Manifest m)
        {
            foreach (var s in Slots)
            {
                var e = m.cuts.First(x => x.id == s.id);
                string paper = PaperPath(s.paper);
                yield return new LabelSpec(paper + "/Cap_" + s.id, e.captionKey);
                for (int i = 0; i < e.callouts.Count; i++)
                    yield return new LabelSpec(paper + "/" + CutName(s.id) + "/Call" + i, BoardKey(e.callouts[i].key));
            }
        }

        static bool IsCalloutPath(string path) { return path.Contains("/Cut_") && path.Contains("/Call"); }

        // ================================================================ geometry in the cut (0..1 of its visible area, origin bottom-left)

        static Rect ShareRect(float cx, float cy, float w, float h) { return new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h); }
        static Rect Shrink(Rect r, float dx, float dy) { return new Rect(r.x + dx, r.y + dy, r.width - 2f * dx, r.height - 2f * dy); }
        static bool Inside(Rect inner, Rect outer, float tol) { return inner.xMin >= outer.xMin - tol && inner.xMax <= outer.xMax + tol && inner.yMin >= outer.yMin - tol && inner.yMax <= outer.yMax + tol; }
        static bool Overlaps(Rect a, Rect b, float tol) { return a.xMin < b.xMax - tol && b.xMin < a.xMax - tol && a.yMin < b.yMax - tol && b.yMin < a.yMax - tol; }

        static float VisibleW(KH_PlayGuideCaptureEditor.Manifest m, KH_PlayGuideCaptureEditor.CutEntry e) { return e.uvW * m.atlasWidth; }
        static float VisibleH(KH_PlayGuideCaptureEditor.Manifest m, KH_PlayGuideCaptureEditor.CutEntry e) { return e.uvH * m.atlasHeight; }

        static List<Rect> Frames(KH_PlayGuideCaptureEditor.CutEntry e)
        {
            return e.marks.Where(k => k.kind == "frame").Select(k => ShareRect(k.x, k.y, k.w, k.h)).ToList();
        }

        /// <summary>Where a callout goes (share rect) and whether it sits on a framed button's face. A callout inside a frame's inner
        /// edge stays as it is; any other callout is trimmed away from every frame it touches (the side that loses the least).</summary>
        static Rect PlaceCallout(KH_PlayGuideCaptureEditor.Manifest m, KH_PlayGuideCaptureEditor.CutEntry e, KH_PlayGuideCaptureEditor.Callout k, out bool onFace, List<string> log)
        {
            var r = ShareRect(k.x, k.y, k.w, k.h);
            float vw = VisibleW(m, e), vh = VisibleH(m, e), bx = FrameBandPx / vw, by = FrameBandPx / vh, gx = CalloutGapPx / vw, gy = CalloutGapPx / vh;
            var frames = Frames(e);
            onFace = k.role == "button" && frames.Any(f => Inside(r, Shrink(f, bx, by), 0.5f / vw));
            if (onFace) return r;
            if (k.role == "button" && !k.framed) onFace = true;   // an unframed button (the inset's other button): its own face, no backing
            foreach (var f in frames)
            {
                if (Inside(r, Shrink(f, bx, by), 0.5f / vw) || !Overlaps(r, f, 0f)) continue;
                var options = new[]
                {
                    Rect.MinMaxRect(r.xMin, r.yMin, Mathf.Min(r.xMax, f.xMin - gx), r.yMax),
                    Rect.MinMaxRect(Mathf.Max(r.xMin, f.xMax + gx), r.yMin, r.xMax, r.yMax),
                    Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, Mathf.Min(r.yMax, f.yMin - gy)),
                    Rect.MinMaxRect(r.xMin, Mathf.Max(r.yMin, f.yMax + gy), r.xMax, r.yMax),
                };
                var best = options.Where(o => o.width > 0f && o.height > 0f).OrderByDescending(o => o.width * o.height).FirstOrDefault();
                if (best.width <= 0f) continue;   // cannot be trimmed: Guide/2 fails on it
                if (log != null) log.Add(e.id + " callout " + k.key + " trimmed off a frame: " + r + " -> " + best);
                r = best;
            }
            return r;
        }

        // ================================================================ build

        static void BuildCutPaper(RectTransform paper, PaperSpec spec, Ctx c, HashSet<Transform> keep)
        {
            var log = new List<string>();
            foreach (var s in Slots.Where(x => x.paper == spec))
            {
                var e = CutOf(c, s.id);
                // a thin sumi edge under the photo
                Img(paper, "Edge_" + s.id, s.pos, s.size + Vector2.one * (2f * CutEdgeMm), Sumi, null, keep).transform.SetAsLastSibling();
                var go = KD_ShopSetupEditor.EnsureUiChild(paper, CutName(s.id), s.pos, s.size);
                go.layer = 0;
                foreach (var g in go.GetComponents<Graphic>()) if (!(g is RawImage)) UnityEngine.Object.DestroyImmediate(g);
                var raw = go.GetComponent<RawImage>(); if (raw == null) raw = go.AddComponent<RawImage>();
                raw.texture = c.cutAtlas; raw.color = Color.white; raw.raycastTarget = false;
                raw.uvRect = new Rect(e.uvX, e.uvY, e.uvW, e.uvH);
                EditorUtility.SetDirty(raw);
                keep.Add(go.transform);
                go.transform.SetAsLastSibling();
                var cut = (RectTransform)go.transform;

                // callouts: backings first, then the words, then the badge (draw order = sibling order)
                var placed = new List<KeyValuePair<Rect, bool>>();
                foreach (var k in e.callouts) { bool face; placed.Add(new KeyValuePair<Rect, bool>(PlaceCallout(c.cuts, e, k, out face, log), face)); }
                for (int i = 0; i < e.callouts.Count; i++)
                {
                    if (placed[i].Value) continue;
                    var back = Img(cut, "CallBack" + i, Vector2.zero, Vector2.zero, CalloutBack, c.round, keep);
                    back.type = Image.Type.Sliced; back.pixelsPerUnitMultiplier = 4f;   // ~7 mm corners on the mm canvas
                    Anchor(back.rectTransform, placed[i].Key, s.size);
                    back.transform.SetAsLastSibling();
                    EditorUtility.SetDirty(back);
                }
                for (int i = 0; i < e.callouts.Count; i++)
                {
                    var r = placed[i].Key;
                    float hMm = r.height * s.size.y;
                    var t = Txt(cut, "Call" + i, Vector2.zero, Vector2.zero, Mathf.Max(CalloutMinMm, hMm * 0.68f), CalloutMinMm, TextAlignmentOptions.Center, SignInk, false, c, keep);
                    Anchor(t.rectTransform, r, s.size);
                    t.rectTransform.sizeDelta -= new Vector2(2f * CalloutPadMm, CalloutPadMm);
                    t.transform.SetAsLastSibling();
                    EditorUtility.SetDirty(t);
                }
                if (e.step > 0 && !e.badgeBaked) BuildBadge(cut, e, s, c, keep);
                // the caption under / beside the cut
                var cap = Txt(paper, "Cap_" + s.id, s.capPos, s.capSize, CapMax, CapMin, s.capAlign, Sumi, true, c, keep);
                cap.transform.SetAsLastSibling();
            }
            var more = spec == PFrisbee
                ? Txt(paper, "More", FrisbeeMorePos, FrisbeeMoreSize, MoreMax, MoreMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep)
                : Txt(paper, "More", BallMorePos, BallMoreSize, MoreMax, MoreMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            more.lineSpacing = MoreLineSpacing; EditorUtility.SetDirty(more);
            more.transform.SetAsLastSibling();
            foreach (var line in log) Debug.Log("[KH Guide Boards] " + line);
        }

        /// <summary>RectTransform at a share rect of its parent cut: anchors at the centre share, size in mm.</summary>
        static void Anchor(RectTransform rt, Rect share, Vector2 cutMm)
        {
            rt.anchorMin = rt.anchorMax = share.center;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(share.width * cutMm.x, share.height * cutMm.y);
        }

        static float BadgeMm(KH_PlayGuideCaptureEditor.CutEntry e, CutSlot s) { return Mathf.Max(BadgeMinMm, 2f * e.badgeR * s.size.x); }
        const float BadgeMarginMm = 4f;

        /// <summary>The badge centre (share of the cut): the manifest's place, moved in just enough that the whole badge (ring included)
        /// stays BadgeMarginMm inside the cut. 2026-10-03 Guide/2: the manifest's place (≈ 24 mm from the corner) let the Ø56 badge poke out.</summary>
        static Vector2 BadgeCentre(KH_PlayGuideCaptureEditor.CutEntry e, CutSlot s)
        {
            float half = BadgeMm(e, s) * 0.5f + BadgeRingMm + BadgeMarginMm;
            float mx = Mathf.Min(0.5f, half / s.size.x), my = Mathf.Min(0.5f, half / s.size.y);
            return new Vector2(Mathf.Clamp(e.badgeX, mx, 1f - mx), Mathf.Clamp(e.badgeY, my, 1f - my));
        }

        static void BuildBadge(RectTransform cut, KH_PlayGuideCaptureEditor.CutEntry e, CutSlot s, Ctx c, HashSet<Transform> keep)
        {
            float d = BadgeMm(e, s);
            var ring = Img(cut, "Badge", Vector2.zero, Vector2.zero, Color.white, c.circle, keep);
            ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = BadgeCentre(e, s);
            ring.rectTransform.anchoredPosition = Vector2.zero;
            ring.rectTransform.sizeDelta = Vector2.one * (d + 2f * BadgeRingMm);
            ring.transform.SetAsLastSibling();
            var dot = Img(ring.transform, "Dot", Vector2.zero, Vector2.one * d, Vermilion, c.circle, keep);
            var num = Txt(dot.transform, "Num", Vector2.zero, Vector2.one * d, d * BadgeDigitShare, d * BadgeDigitShare, TextAlignmentOptions.Center, Color.white, false, c, keep);
            num.enableAutoSizing = false; num.fontSize = d * BadgeDigitShare; num.fontStyle = FontStyles.Bold; num.text = e.step.ToString();
            EditorUtility.SetDirty(num);
        }

        // ================================================================ verify (Guide/2)

        static void CheckCuts(Ctx c, Action<bool, string> req, Action<bool, string> warn, VerifyReport v)
        {
            var m = c.cuts;
            req(true, "cut manifest " + KH_PlayGuideCaptureEditor.ManifestPath + ": format " + m.format + ", drawVersion " + m.drawVersion + ", captured " + m.capturedUtc + ", " + m.cuts.Count + " cuts");
            CheckAtlasImporter(req);
            var canvas = c.root.Find(Asob.name + "/" + CanvasName);
            if (canvas == null) { req(false, "Asobikata/Canvas"); return; }
            foreach (var name in RemovedPapers) req(canvas.Find(name) == null, "the old paper " + name + " is gone (walk / wash are explained at the leash racks / wash stations)");
            foreach (var s in Slots)
            {
                var e = CutOf(c, s.id);
                string tag = "cut " + s.id + ": ";
                var paper = canvas.Find(s.paper.name);
                var cut = paper != null ? paper.Find(CutName(s.id)) as RectTransform : null;
                var raw = cut != null ? cut.GetComponent<RawImage>() : null;
                req(raw != null, tag + PaperPath(s.paper) + "/" + CutName(s.id) + " is a RawImage");
                if (raw == null) continue;
                req(raw.texture == c.cutAtlas, tag + "shows the atlas " + KH_PlayGuideCaptureEditor.AtlasPath);
                var want = new Rect(e.uvX, e.uvY, e.uvW, e.uvH);
                bool uvOk = Mathf.Abs(raw.uvRect.x - want.x) < 1e-5f && Mathf.Abs(raw.uvRect.y - want.y) < 1e-5f && Mathf.Abs(raw.uvRect.width - want.width) < 1e-5f && Mathf.Abs(raw.uvRect.height - want.height) < 1e-5f;
                req(uvOk, tag + "uvRect " + raw.uvRect.ToString("F6") + " = the manifest's " + want.ToString("F6"));
                req(Vector2.Distance(cut.anchoredPosition, s.pos) < 0.5f && Vector2.Distance(cut.sizeDelta, s.size) < 0.5f, tag + "at " + s.pos + " size " + s.size + " mm");
                float imgAspect = VisibleW(m, e) / VisibleH(m, e), slotAspect = s.size.x / s.size.y;
                req(Mathf.Abs(imgAspect / slotAspect - 1f) < 0.015f, tag + "not stretched (image " + imgAspect.ToString("F3") + " vs slot " + slotAspect.ToString("F3") + ")");
                req(!raw.raycastTarget, tag + "not a raycast target");
                var edge = paper.Find("Edge_" + s.id);
                req(edge != null && edge.GetSiblingIndex() < cut.GetSiblingIndex(), tag + "its edge is drawn under it");

                // badge
                var badge = cut.Find("Badge") as RectTransform;
                if (e.step > 0 && !e.badgeBaked)
                {
                    var num = badge != null ? badge.Find("Dot/Num") : null;
                    var tmp = num != null ? num.GetComponent<TextMeshProUGUI>() : null;
                    var bc = BadgeCentre(e, s);
                    req(badge != null && Vector2.Distance(badge.anchorMin, bc) < 1e-4f && tmp != null && tmp.text == e.step.ToString(),
                        tag + "number badge " + e.step + " at the manifest's corner (" + e.badgeX + ", " + e.badgeY + ") kept " + BadgeMarginMm + " mm inside -> (" + bc.x.ToString("F4") + ", " + bc.y.ToString("F4") + "), Ø" + BadgeMm(e, s).ToString("F0") + " mm");
                    if (badge != null)
                    {
                        // measured where the badge actually is (the scene), ring included
                        var br = ShareRect(badge.anchorMin.x, badge.anchorMin.y, badge.sizeDelta.x / s.size.x, badge.sizeDelta.y / s.size.y);
                        req(Inside(br, new Rect(0f, 0f, 1f, 1f), 0.002f), tag + "the badge stays inside the cut");
                    }
                }
                else req(badge == null, tag + "no UI badge (" + (e.badgeBaked ? "baked into the image" : "a note cut") + ")");

                // callouts
                var frames = Frames(e);
                float vw = VisibleW(m, e), vh = VisibleH(m, e), bx = FrameBandPx / vw, by = FrameBandPx / vh, tol = 0.5f / vw;
                var insets = e.marks.Where(k => k.kind == "inset").Select(k => ShareRect(k.x, k.y, k.w, k.h)).ToList();
                for (int i = 0; i < e.callouts.Count; i++)
                {
                    var k = e.callouts[i];
                    var t = cut.Find("Call" + i) as RectTransform;
                    var tmp = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
                    string ct = tag + "callout " + i + " " + k.role + " " + k.key + ": ";
                    req(tmp != null, ct + "label exists");
                    if (tmp == null) continue;
                    var backT = cut.Find("CallBack" + i) as RectTransform;
                    var box = backT != null ? backT : t;   // the backing is the callout's full box; on a button face the label is
                    var r = ShareRect(box.anchorMin.x, box.anchorMin.y, box.sizeDelta.x / s.size.x, box.sizeDelta.y / s.size.y);
                    if (backT == null) r = Shrink(r, -CalloutPadMm / s.size.x, -CalloutPadMm * 0.5f / s.size.y);
                    req(Inside(r, new Rect(0f, 0f, 1f, 1f), 0.001f), ct + "inside its cut " + r.ToString("F3"));
                    bool onFace = frames.Any(f => Inside(r, Shrink(f, bx, by), tol));
                    var hit = frames.Where(f => !Inside(r, Shrink(f, bx, by), tol) && Overlaps(r, f, tol)).ToList();
                    req(hit.Count == 0, ct + (onFace ? "on the framed button's face, inside the frame's band" : "clear of every frame") + (hit.Count > 0 ? " (covers " + string.Join(", ", hit.Select(f => f.ToString("F3"))) + ")" : ""));
                    if (k.inInset) req(insets.Any(f => Inside(r, f, tol)), ct + "inside the zoom inset");
                    // a button's label is written on its face (the button is the backing); every other callout has the dark rounded backing
                    bool needsBack = k.role != "button";
                    if (needsBack || backT != null)
                        req(backT != null && backT.GetComponent<Image>() != null && backT.GetSiblingIndex() < t.GetSiblingIndex(), ct + "dark rounded backing under the words");
                    req(Mathf.Abs(t.anchorMin.x - r.center.x) < 1e-3f && Mathf.Abs(t.anchorMin.y - r.center.y) < 1e-3f, ct + "label centred on its box");
                }
                var capT = paper.Find("Cap_" + s.id);
                req(capT != null && capT.GetComponent<TextMeshProUGUI>() != null, tag + "caption " + PaperPath(s.paper) + "/Cap_" + s.id + " (" + e.captionKey + ")");
            }
            // removed keys: neither bound nor in the catalog
            var manifest = JsonUtility.FromJson<KD_LocalizationEditor.Manifest>(File.ReadAllText(KD_LocalizationEditor.BindingsPath));
            var stillBound = manifest.labels.Where(b => RemovedKeys.Contains(b.key) || RemovedPapers.Any(p => b.path.Contains("/" + p + "/"))).Select(b => b.root + "/" + b.path).ToList();
            req(stillBound.Count == 0, "the 5 removed keys (" + string.Join(", ", RemovedKeys) + ") and the old papers are not bound" + (stillBound.Count > 0 ? ": " + string.Join(", ", stillBound) : ""));
            var stillThere = c.catalog.languages.SelectMany(l => l.texts.Where(x => RemovedKeys.Contains(x.key)).Select(x => l.code + ":" + x.key)).ToList();
            req(stillThere.Count == 0, "the 5 removed keys are gone from " + KD_MenuFinishEditor.CatalogPath + (stillThere.Count > 0 ? " (" + string.Join(", ", stillThere.Take(6)) + ")" : ""));
            var fixedKeys = c.language.fixedKeys ?? new string[0];
            req(!fixedKeys.Any(k => RemovedKeys.Contains(k)), "KD_Language binds none of the removed keys (Guide/1 re-binds)");
            // the mirrored callout texts = the course catalog's, in every language
            var course = JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath));
            foreach (var kv in CalloutMirrors)
            {
                var bad = new List<string>();
                foreach (var lang in c.catalog.languages)
                {
                    string a, b;
                    try { a = KD_LocalizationEditor.Text(c.catalog, lang.code, kv.Value); b = KD_LocalizationEditor.Text(course, lang.code, kv.Key); }
                    catch (Exception ex) { bad.Add(lang.code + " (" + ex.Message + ")"); continue; }
                    if (a != b) bad.Add(lang.code + " '" + a + "' vs '" + b + "'");
                }
                req(bad.Count == 0, kv.Value + " = the menu's " + kv.Key + " in all " + c.catalog.languages.Length + " languages" + (bad.Count > 0 ? ": " + string.Join("; ", bad.Take(4)) : ""));
            }
        }

        static void CheckAtlasImporter(Action<bool, string> req)
        {
            var ti = AssetImporter.GetAtPath(KH_PlayGuideCaptureEditor.AtlasPath) as TextureImporter;
            req(ti != null, "the atlas has a TextureImporter");
            if (ti == null) return;
            bool common = ti.textureType == TextureImporterType.Default && ti.sRGBTexture && !ti.isReadable && ti.mipmapEnabled && ti.wrapMode == TextureWrapMode.Clamp
                && ti.filterMode == FilterMode.Bilinear && ti.npotScale == TextureImporterNPOTScale.None && ti.alphaSource == TextureImporterAlphaSource.None && ti.maxTextureSize >= 2048;
            req(common, "atlas importer: Default, sRGB, Read/Write off, mipmaps, Clamp, Bilinear, NPOT kept, no alpha, max >= 2048 (plan §4.4)");
            foreach (var pf in new[] { new KeyValuePair<string, TextureImporterFormat>("Standalone", TextureImporterFormat.DXT1), new KeyValuePair<string, TextureImporterFormat>("Android", TextureImporterFormat.ASTC_6x6), new KeyValuePair<string, TextureImporterFormat>("iPhone", TextureImporterFormat.ASTC_6x6) })
            {
                var s = ti.GetPlatformTextureSettings(pf.Key);
                req(s.overridden && s.format == pf.Value && s.maxTextureSize >= 2048, "atlas " + pf.Key + " " + pf.Value + " (" + (s.overridden ? s.format.ToString() : "not overridden") + ", max " + s.maxTextureSize + ")");
            }
        }

        // ---------------------------------------------------------------- board A layout: nothing overlaps, the papers are filled (plan §3.5)

        static Rect LocalRect(RectTransform rt)
        {
            // every part of board A is centre-anchored in its parent (EnsureUiChild)
            return ShareRect(rt.anchoredPosition.x, rt.anchoredPosition.y, rt.sizeDelta.x, rt.sizeDelta.y);
        }

        static void CheckAsobLayout(Ctx c, Action<bool, string> req, Action<bool, string> warn, VerifyReport v)
        {
            var cv = c.root.Find(Asob.name + "/" + CanvasName) as RectTransform;
            if (cv == null) return;
            var canvasRect = ShareRect(0f, 0f, Asob.canvasMm.x, Asob.canvasMm.y);
            // canvas level: header plate, strip, papers
            var parts = new List<RectTransform>();
            foreach (var n in new[] { "HeaderPlate", "MenuStrip" }.Concat(Papers.Select(p => p.name))) { var t = cv.Find(n) as RectTransform; req(t != null, "Asobikata/Canvas/" + n); if (t != null) parts.Add(t); }
            foreach (Transform t in cv) if (t.name.StartsWith("Paper_") && !Papers.Any(p => p.name == t.name)) req(false, "Asobikata/Canvas/" + t.name + " is not one of the 3 papers");
            OverlapCheck(parts, canvasRect, "Asobikata canvas", req);
            foreach (var p in Papers)
            {
                var pt = cv.Find(p.name) as RectTransform;
                if (pt == null) continue;
                req(Mathf.Abs(pt.sizeDelta.y - PaperH) < 0.5f && Mathf.Abs(pt.sizeDelta.x - p.width) < 0.5f, p.name + " " + pt.sizeDelta + " mm (" + p.width + " x " + PaperH + ")");
            }
            var strip = cv.Find("MenuStrip") as RectTransform;
            if (strip != null)
            {
                var lines = new[] { "Text", "Pointer" }.Select(n => strip.Find(n) as RectTransform).Where(x => x != null).ToList();
                req(lines.Count == 2, "the strip has its two lines (Text, Pointer)");
                OverlapCheck(lines, ShareRect(0f, 0f, strip.sizeDelta.x, strip.sizeDelta.y), "MenuStrip", req);
            }

            // paper level: direct children don't overlap (an edge holds its cut), stay on the paper; the empty strip under the content
            var go = new GameObject("GuideBoardsLayoutMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                string ja = c.catalog.fallbackLanguage;
                foreach (var p in Papers)
                {
                    var pt = cv.Find(p.name) as RectTransform;
                    if (pt == null) continue;
                    var kids = pt.Cast<Transform>().OfType<RectTransform>().Where(t => !t.name.StartsWith("Edge_")).ToList();
                    var paperRect = ShareRect(0f, 0f, pt.sizeDelta.x, pt.sizeDelta.y);
                    OverlapCheck(kids, paperRect, p.name, req);
                    float bottom = float.PositiveInfinity; string lowest = "-";
                    foreach (var k in kids)
                    {
                        var r = LocalRect(k);
                        float b = r.yMin;
                        var tmp = k.GetComponent<TextMeshProUGUI>();
                        if (tmp != null)
                        {
                            var label = c.labels.FirstOrDefault(l => l.path == PaperPath(p) + "/" + k.name);
                            string text = label != null ? KD_LocalizationEditor.Text(c.catalog, ja, label.key) : tmp.text;
                            float size = FittedSize(sample, tmp, text);
                            float h = PreferredAt(sample, tmp, text, size < 0f ? tmp.fontSizeMin : size).y;
                            var va = tmp.alignment;
                            bool top = va == TextAlignmentOptions.Top || va == TextAlignmentOptions.TopLeft || va == TextAlignmentOptions.TopRight || va == TextAlignmentOptions.TopJustified;
                            b = top ? r.yMax - h : r.center.y - h * 0.5f;
                        }
                        if (b < bottom) { bottom = b; lowest = k.name; }
                    }
                    float gap = bottom - (-pt.sizeDelta.y * 0.5f);
                    float fill = (pt.sizeDelta.y * 0.5f - Mathf.Max(bottom, -pt.sizeDelta.y * 0.5f)) / pt.sizeDelta.y;
                    v.notes.Add(p.name + ": the lowest content (" + lowest + ", ja) ends " + gap.ToString("F0") + " mm above the paper's bottom; content spans " + (fill * 100f).ToString("F0") + " % of the paper's height");
                    warn(gap <= 60f, p.name + " empty space under the content " + gap.ToString("F0") + " mm (<= 60, plan §3.5)");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static void OverlapCheck(List<RectTransform> parts, Rect bounds, string where, Action<bool, string> req)
        {
            var bad = new List<string>();
            for (int i = 0; i < parts.Count; i++)
            {
                var a = LocalRect(parts[i]);
                if (!Inside(a, bounds, 0.5f)) bad.Add(parts[i].name + " leaves " + where);
                for (int j = i + 1; j < parts.Count; j++)
                    if (Overlaps(a, LocalRect(parts[j]), 0.5f)) bad.Add(parts[i].name + " x " + parts[j].name);
            }
            req(bad.Count == 0, where + ": " + parts.Count + " parts inside it, none overlapping" + (bad.Count > 0 ? " (" + string.Join("; ", bad.Take(8)) + ")" : ""));
        }

        // ---------------------------------------------------------------- the size autosize ends at (ja), for the 30 mm caption floor

        static Vector2 PreferredAt(TextMeshProUGUI sample, TMP_Text t, string text, float size)
        {
            sample.isOrthographic = true; sample.enableAutoSizing = false; sample.font = t.font; sample.fontSize = size;
            sample.enableWordWrapping = t.enableWordWrapping; sample.margin = t.margin; sample.richText = t.richText; sample.lineSpacing = t.lineSpacing;
            sample.fontStyle = t.fontStyle;
            sample.rectTransform.sizeDelta = t.rectTransform.rect.size;
            return sample.GetPreferredValues(text, t.rectTransform.rect.width, Mathf.Infinity);
        }

        static bool FitsAt(TextMeshProUGUI sample, TMP_Text t, string text, float size)
        {
            var p = PreferredAt(sample, t, text, size);
            var r = t.rectTransform.rect;
            return !(p.y > r.height + 2f || (!t.enableWordWrapping && p.x > r.width + 2f));
        }

        /// <summary>The largest size in [min, max] at which the text fits (TMP's autosize ends near it); -1 = not even at min.</summary>
        static float FittedSize(TextMeshProUGUI sample, TMP_Text t, string text)
        {
            if (!t.enableAutoSizing) return FitsAt(sample, t, text, t.fontSize) ? t.fontSize : -1f;
            float lo = t.fontSizeMin, hi = t.fontSizeMax;
            if (FitsAt(sample, t, text, hi)) return hi;
            if (!FitsAt(sample, t, text, lo)) return -1f;
            for (int i = 0; i < 14; i++) { float mid = (lo + hi) * 0.5f; if (FitsAt(sample, t, text, mid)) lo = mid; else hi = mid; }
            return lo;
        }
    }
}
