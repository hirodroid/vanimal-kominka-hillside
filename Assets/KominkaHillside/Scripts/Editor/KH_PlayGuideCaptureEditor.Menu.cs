using System;
using System.Collections.Generic;
using System.Linq;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    // Guide/13, the menu cuts (plan §3.3 F1 / N2 / B1, §4.1 step 2). Built like KD_MenuFinishEditor.Preview (Menu/22c): the
    // hand-menu panel (KD_TreatPouch.panelRoot) is instantiated straight into a preview scene (so the copy never exists in the
    // production scene), its Udon is removed, only Page_Toy is shown in the state the runtime draws for "おもちゃ" (tab lit,
    // buttons enabled, throw style "まっすぐ"), and EVERY TMP_Text / Text is switched off: the cut shows where to press, the
    // words come back on the board as TMP callouts bound to the same keys (listed in the manifest).
    public static partial class KH_PlayGuideCaptureEditor
    {
        const float PxPerUnit = 2f;          // plan §4.1 step 2: orthographic, 2 px per canvas unit
        const int PanelLayer = 31;
        const float InsetMaxMag = 3f;        // plan §3.3 F1: the row magnified 3x when it fits
        const float MinGoodMag = 2.5f;       // second pass (coordinator): the inset at >= 2.5x whenever it fits
        const float MenuDim = 0.6f;          // second pass: the unframed area x0.6
        const float RowPadUnits = 8f;
        // KD_TreatPouch.Hillside.cs:47 (_hillsideDogKeys, private): the dog name keys used when toyNameKeys is not wired
        static readonly string[] DefaultDogKeys = { "activities.dog.Corgi", "activities.dog.Pome", "activities.dog.Lab", "activities.dog.Dachs", "activities.dog.Shiba", "activities.dog.Samoyed" };

        sealed class PanelShot
        {
            public Img hi;
            public Camera cam;
            public int W, H;
            public Transform srcRoot, cloneRoot;
            public Rect Hi(RectTransform rt)
            {
                var c = new Vector3[4]; rt.GetWorldCorners(c);
                var pts = c.Select(p => { var v = cam.WorldToViewportPoint(p); return new Vector2(v.x * W, v.y * H); }).ToArray();
                return BoundsOf(pts);
            }
            /// <summary>The clone's twin of an object under the original panel (same relative path).</summary>
            public Transform Twin(Transform original, string what)
            {
                if (original == null) throw new InvalidOperationException(what + " is not wired on KD_TreatPouch (run the menu builders / Course/122 / Course/124 first)");
                if (original != srcRoot && !original.IsChildOf(srcRoot)) throw new InvalidOperationException(what + " (" + PathOf(original) + ") is not under the menu panel " + srcRoot.name);
                string path = Relative(srcRoot, original);
                var t = path.Length == 0 ? cloneRoot : cloneRoot.Find(path);
                if (t == null) throw new InvalidOperationException(what + ": '" + path + "' not found in the panel copy");
                return t;
            }
        }

        static string Relative(Transform parent, Transform child)
        {
            if (child == parent) return "";
            string path = child.name;
            while (child.parent != null && child.parent != parent) { child = child.parent; path = child.name + "/" + path; }
            return path;
        }

        static void GateMenu(KD_TreatPouch p)
        {
            if (p.panelRoot == null) throw new InvalidOperationException("KD_TreatPouch.panelRoot is not wired (Menu builders)");
            if (p.tabPages == null || p.tabPages.Length <= KD_TreatPouch.TabToy || p.tabPages[KD_TreatPouch.TabToy] == null) throw new InvalidOperationException("Page_Toy (tabPages[" + KD_TreatPouch.TabToy + "]) missing");
            if (p.throwStraightImage == null || p.throwCurveImage == null || p.throwStyleLabel == null || p.throwStraightLabel == null || p.throwCurveLabel == null)
                throw new InvalidOperationException("the toy tab's throw-style row is missing: run Course/122 - Build Toy Tab Throw Style Row first (plan §4.1 step 1)");
            if (p.patternLabel == null || p.patternActImage == null)
                throw new InvalidOperationException("the toy tab's ball-pattern row is missing: run Course/124 first (plan §4.1 step 1)");
            if (p.toyButtons == null || p.toyButtons.Length <= GuideDog || p.toyButtons[GuideDog] == null
                || p.discButtons == null || p.discButtons.Length <= GuideDog || p.discButtons[GuideDog] == null
                || p.toyLabels == null || p.toyLabels.Length <= GuideDog || p.toyLabels[GuideDog] == null)
                throw new InvalidOperationException("ToyRow" + GuideDog + " ([ボール] toyButtons / [ディスク] discButtons / name toyLabels) is not wired: run the menu builders first");
            if (p.tabImages == null || p.tabImages.Length <= KD_TreatPouch.TabToy || p.tabImages[KD_TreatPouch.TabToy] == null) throw new InvalidOperationException("tabImages[" + KD_TreatPouch.TabToy + "] (the おもちゃ tab) missing");
        }

        /// <summary>The menu's translation key for an original label: KD_Language.fixedLabels first (what the runtime binds),
        /// else the given key (what the runtime writes in Redraw, cited at the caller).</summary>
        static string KeyFor(KD_TreatPouch p, TMP_Text original, string fallback, string fallbackSource, out string source)
        {
            var lang = p.language;
            if (original != null && lang != null && lang.fixedLabels != null && lang.fixedKeys != null)
                for (int i = 0; i < lang.fixedLabels.Length && i < lang.fixedKeys.Length; i++)
                    if (lang.fixedLabels[i] == original && !string.IsNullOrEmpty(lang.fixedKeys[i])) { source = "KD_Language.fixedLabels"; return lang.fixedKeys[i]; }
            source = fallbackSource;
            return fallback;
        }

        static string DogKey(KD_TreatPouch p, int row, out string source)
        {
            if (p.toyNameKeys != null && row < p.toyNameKeys.Length && !string.IsNullOrEmpty(p.toyNameKeys[row])) { source = "KD_TreatPouch.toyNameKeys"; return p.toyNameKeys[row]; }
            source = "KD_TreatPouch._hillsideDogKeys (default)";
            return DefaultDogKeys[row];
        }

        static PanelShot RenderPanel(KD_TreatPouch p, Scene preview)
        {
            var holder = new GameObject(TmpPrefix + "PanelHolder");
            SceneManager.MoveGameObjectToScene(holder, preview);
            var clone = Object.Instantiate(p.panelRoot, holder.transform);
            clone.SetActive(false);
            foreach (var b in clone.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true)) Object.DestroyImmediate(b);
            foreach (var b in clone.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true)) Object.DestroyImmediate(b);
            foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PanelLayer;
            clone.transform.localPosition = Vector3.zero; clone.transform.localRotation = Quaternion.identity; clone.transform.localScale = Vector3.one * 0.001f;
            var canvas = clone.GetComponent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("KD_TreatPouch.panelRoot (" + p.panelRoot.name + ") has no Canvas");
            canvas.renderMode = RenderMode.WorldSpace;
            var shot = new PanelShot { srcRoot = p.panelRoot.transform, cloneRoot = clone.transform };

            // ---- the state KD_TreatPouch.ApplyTab / RedrawHillsideToy / RedrawToyTopRow draw for the toy tab
            void Off(GameObject original, string what) { if (original == null || !original.transform.IsChildOf(shot.srcRoot)) return; shot.Twin(original.transform, what).gameObject.SetActive(false); }
            Off(p.welcomePage, "welcomePage"); Off(p.settingsPage, "settingsPage"); Off(p.confirmRoot, "confirmRoot");
            Off(p.photoPage, "photoPage"); Off(p.hillsidePointsPage, "hillsidePointsPage");
            for (int i = 0; i < p.tabPages.Length; i++)
                if (p.tabPages[i] != null && p.tabPages[i].transform.IsChildOf(shot.srcRoot)) shot.Twin(p.tabPages[i].transform, "tabPages[" + i + "]").gameObject.SetActive(i == KD_TreatPouch.TabToy);
            Transform In(Component original) { return original != null && original.transform.IsChildOf(shot.srcRoot) ? shot.Twin(original.transform, original.name) : null; }
            void Tint(Component original, Color c) { var t = In(original); var img = t != null ? t.GetComponent<Image>() : null; if (img != null) img.color = c; }
            void Show(Component original, bool on) { var t = In(original); if (t != null) t.gameObject.SetActive(on); }
            if (p.tabImages != null) for (int i = 0; i < p.tabImages.Length; i++) Tint(p.tabImages[i], i == KD_TreatPouch.TabToy ? p.tabOnColor : p.tabOffColor);
            if (p.tabUnderlines != null) for (int i = 0; i < p.tabUnderlines.Length; i++) if (p.tabUnderlines[i] != null) Show(p.tabUnderlines[i].transform, i == KD_TreatPouch.TabToy);
            if (p.toyButtonImages != null) foreach (var img in p.toyButtonImages) Tint(img, p.recallOnColor);
            if (p.discButtonImages != null) foreach (var img in p.discButtonImages) Tint(img, p.recallOnColor);
            if (p.toyButtons != null) foreach (var b in p.toyButtons) if (b != null) Show(b.transform, true);
            if (p.discButtons != null) foreach (var b in p.discButtons) if (b != null) Show(b.transform, true);
            Tint(p.throwStraightImage, p.equipOnColor);   // "まっすぐ" chosen (the runtime default)
            Tint(p.throwCurveImage, p.hillsideNeutralColor);
            if (p.throwStraightUnderline != null) Show(p.throwStraightUnderline.transform, true);
            if (p.throwCurveUnderline != null) Show(p.throwCurveUnderline.transform, false);
            // no words in the image
            foreach (var t in clone.GetComponentsInChildren<TMP_Text>(true)) t.enabled = false;
            foreach (var t in clone.GetComponentsInChildren<Text>(true)) t.enabled = false;

            clone.SetActive(true);
            Canvas.ForceUpdateCanvases();
            foreach (var s in clone.GetComponentsInChildren<ScrollRect>(true))
            {
                if (s.content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(s.content);
                s.verticalNormalizedPosition = 1f;
            }
            Canvas.ForceUpdateCanvases();
            foreach (var mask in clone.GetComponentsInChildren<RectMask2D>()) mask.PerformClipping();

            var rootRt = clone.GetComponent<RectTransform>();
            var corners = new Vector3[4]; rootRt.GetWorldCorners(corners);
            var centre = (corners[0] + corners[2]) * 0.5f;
            float worldW = Vector3.Distance(corners[0], corners[3]), worldH = Vector3.Distance(corners[0], corners[1]);
            shot.W = Mathf.RoundToInt(rootRt.rect.width * PxPerUnit); shot.H = Mathf.RoundToInt(rootRt.rect.height * PxPerUnit);
            if (shot.W < 64 || shot.H < 64 || worldW <= 0f || worldH <= 0f) throw new InvalidOperationException("menu panel size " + rootRt.rect.size + " is not usable");

            var camGo = new GameObject(TmpPrefix + "PanelCamera");
            SceneManager.MoveGameObjectToScene(camGo, preview);
            camGo.transform.SetPositionAndRotation(centre - Vector3.forward * 2f, Quaternion.identity);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.scene = preview; cam.orthographic = true; cam.orthographicSize = worldH * 0.5f;
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 10f; cam.cullingMask = 1 << PanelLayer;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = MenuBackdrop; cam.allowHDR = false;
            shot.cam = cam;
            var rt = new RenderTexture(shot.W, shot.H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 4 };
            var tex = new Texture2D(shot.W, shot.H, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            var prev = RenderTexture.active;
            try
            {
                cam.targetTexture = rt; cam.aspect = (float)shot.W / shot.H;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, shot.W, shot.H), 0, 0); tex.Apply(false);
                shot.hi = Img.FromTexture(tex);
            }
            finally
            {
                cam.targetTexture = null; RenderTexture.active = prev;
                Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            }
            return shot;
        }

        /// <summary>F1, B1 and N2 from one panel render. Each cut is guarded on its own.</summary>
        static void CaptureMenuCuts(KD_TreatPouch p, Dictionary<string, CutWork> works, Report report)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                PanelShot shot;
                try { shot = RenderPanel(p, preview); }
                catch (Exception ex)
                {
                    foreach (var id in new[] { "F1", "B1", "N2" }) works[id].error = "menu panel render: " + ex.Message;
                    return;
                }
                works["F1"].raw = shot.hi; works["B1"].raw = shot.hi; works["N2"].raw = shot.hi;
                Guard(works["F1"], () => RowCut(p, shot, works["F1"], true));
                Guard(works["B1"], () => RowCut(p, shot, works["B1"], false));
                Guard(works["N2"], () => ThrowRowCut(p, shot, works["N2"]));
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        /// <summary>F1 (disc) / B1 (ball): the whole panel, a frame on the おもちゃ tab and on the row's button, and the row
        /// magnified in an inset with the button framed again. Callouts: the tab word above its frame; in the inset the dog's
        /// name and both button words on their buttons.</summary>
        static void RowCut(KD_TreatPouch p, PanelShot s, CutWork w, bool disc)
        {
            int row = GuideDog;
            var tabT = s.Twin(p.tabImages[KD_TreatPouch.TabToy].transform, "tabImages[TabToy]");
            var ballT = s.Twin(p.toyButtons[row].transform, "toyButtons[" + row + "]");
            var discT = s.Twin(p.discButtons[row].transform, "discButtons[" + row + "]");
            var rowT = s.Twin(p.toyButtons[row].transform.parent, "ToyRow" + row);
            var nameT = s.Twin(p.toyLabels[row].transform, "toyLabels[" + row + "]");
            var ballLabelOrig = p.toyButtons[row].transform.Find("Label");
            if (ballLabelOrig == null) throw new InvalidOperationException("toyButtons[" + row + "] has no Label child");
            var discLabelOrig = p.discButtonLabels != null && row < p.discButtonLabels.Length && p.discButtonLabels[row] != null ? p.discButtonLabels[row].transform : p.discButtons[row].transform.Find("Label");
            if (discLabelOrig == null) throw new InvalidOperationException("discButtons[" + row + "] has no Label");
            var tabLabelOrig = p.tabImages[KD_TreatPouch.TabToy].transform.Find("Label");

            // panel to cut: full width, a band at the top for the tab callout
            float k = (float)CutW / s.W;
            int ph = Mathf.RoundToInt(s.H * k);
            float band = CutH - ph;
            float yb = Mathf.Round(band * 0.3f);
            w.final = new Img(CutW, CutH, MenuBackdrop);
            w.final.Blit(s.hi.Resample(new Rect(0, 0, s.W, s.H), CutW, ph), 0, (int)yb);
            Rect Map(Rect hi) { return new Rect(hi.x * k, yb + hi.y * k, hi.width * k, hi.height * k); }

            var tabHi = s.Hi((RectTransform)tabT); var targetHi = s.Hi((RectTransform)(disc ? discT : ballT));
            var rowHi = Grow(s.Hi((RectTransform)rowT), RowPadUnits * PxPerUnit);
            var viewport = rowT.GetComponentInParent<ScrollRect>();
            if (viewport != null && viewport.viewport != null)
            {
                var vp = s.Hi(viewport.viewport);
                if (!vp.Contains(targetHi.center)) throw new InvalidOperationException("ToyRow" + row + "'s button is scrolled out of the toy page's viewport");
            }
            // second pass: everything but the two targets is dimmed so the eye goes to the frames
            w.final.Dim(MenuDim, Grow(Map(tabHi), FramePad), Grow(Map(targetHi), FramePad));
            var tabFrame = DrawFrame(w.final, Map(tabHi), w.marks, "tab おもちゃ");
            var btnFrame = DrawFrame(w.final, Map(targetHi), w.marks, disc ? "[ディスク] of ToyRow" + row : "[ボール] of ToyRow" + row);

            // the inset shows the part of the row that matters: both buttons and, above them, the strip where the dog's name is
            // (the whole 480-unit row would never fit at >= 2.5x)
            var ballHi = s.Hi((RectTransform)ballT); var discHi = s.Hi((RectTransform)discT); var nameHi = s.Hi((RectTransform)nameT);
            float pad = RowPadUnits * PxPerUnit;
            var insetHi = Rect.MinMaxRect(Mathf.Min(ballHi.xMin, discHi.xMin) - pad, Mathf.Min(ballHi.yMin, discHi.yMin) - pad,
                Mathf.Max(ballHi.xMax, discHi.xMax) + pad, Mathf.Max(Mathf.Max(ballHi.yMax, discHi.yMax) + pad, Mathf.Min(nameHi.yMax, rowHi.yMax) + pad * 0.5f));
            var srcCut = Map(insetHi);
            var avoid = new[] { tabFrame, btnFrame, Grow(srcCut, 4f), BadgeZone() };
            Rect inset = Rect.zero; float mag = 0f;
            for (float m = InsetMaxMag; m >= 1.5f - 1e-3f && mag == 0f; m -= 0.1f)
            {
                float iw = Mathf.Round(srcCut.width * m), ih = Mathf.Round(srcCut.height * m);
                if (iw > CutW - 16f || ih > CutH - 16f) continue;
                // scan from the bottom-right corner: rows upward, columns leftward, 4 px steps
                for (float y = 10f; y + ih <= CutH - 10f && mag == 0f; y += 4f)
                    for (float x = CutW - 10f - iw; x >= 10f; x -= 4f)
                    {
                        var r = new Rect(x, y, iw, ih);
                        if (avoid.Any(a => a.Overlaps(Grow(r, 6f)))) continue;
                        inset = r; mag = m; break;
                    }
            }
            if (mag == 0f) throw new InvalidOperationException(w.id + ": no place for the inset at >= 1.5x that covers neither frame nor the source");
            if (mag < MinGoodMag) w.warnings.Add(w.id + ": inset only x" + mag.ToString("F1") + " (wanted >= " + MinGoodMag + ")");
            float ik = inset.width / insetHi.width;
            Rect MapInset(Rect hi) { return new Rect(inset.x + (hi.x - insetHi.x) * ik, inset.y + (hi.y - insetHi.y) * ik, hi.width * ik, hi.height * ik); }
            DrawInset(w.final, s.hi, insetHi, inset, srcCut, w.marks, "ToyRow" + row + " buttons x" + mag.ToString("F1"), Grow(MapInset(targetHi), FramePad), MenuDim);
            w.notes.Add("panel " + s.W + "x" + s.H + " px (" + PxPerUnit + " px/unit) -> " + CutW + "x" + ph + " at y " + yb + "; inset x" + mag.ToString("F1") + " of the panel scale at " + inset + "; unframed area x" + MenuDim);
            DrawFrame(w.final, MapInset(targetHi), w.marks, "the same button in the inset");
            nameHi = Rect.MinMaxRect(Mathf.Max(nameHi.xMin, insetHi.xMin), Mathf.Max(nameHi.yMin, Mathf.Max(ballHi.yMax, discHi.yMax)), Mathf.Min(nameHi.xMax, insetHi.xMax), Mathf.Min(nameHi.yMax, insetHi.yMax));
            if (nameHi.width < 40f || nameHi.height < 8f)   // the name label lies outside the inset: use the strip above the buttons
                nameHi = Rect.MinMaxRect(insetHi.xMin + pad, Mathf.Max(ballHi.yMax, discHi.yMax), insetHi.xMax - pad, insetHi.yMax);

            // callouts
            string src;
            string tabKey = KeyFor(p, tabLabelOrig != null ? tabLabelOrig.GetComponent<TMP_Text>() : null, "KD_TreatPouch.WordTabToy", "binding Tab1/Label (UILocalizationBindings.json)", out src);
            var tabSize = new Vector2(Mathf.Max(tabFrame.width, 120f), 34f);
            float ty = tabFrame.yMax + 4f + tabSize.y * 0.5f;
            if (ty + tabSize.y * 0.5f > CutH - 2f) ty = tabFrame.yMin - 4f - tabSize.y * 0.5f;
            w.callouts.Add(new CalloutPx { key = tabKey, keySource = src, role = "tab", rect = new Rect(tabFrame.center.x - tabSize.x * 0.5f, ty - tabSize.y * 0.5f, tabSize.x, tabSize.y), framed = true, inInset = false });
            string dogKey = DogKey(p, row, out src);
            w.callouts.Add(new CalloutPx { key = dogKey, keySource = src, role = "dogName", rect = MapInset(nameHi), framed = false, inInset = true });
            string ballKey = KeyFor(p, ballLabelOrig.GetComponent<TMP_Text>(), "KD_TreatPouch.WordToyBall", "KD_TreatSetupEditor.Hillside (wBall)", out src);
            w.callouts.Add(new CalloutPx { key = ballKey, keySource = src, role = "button", rect = MapInset(s.Hi((RectTransform)s.Twin(ballLabelOrig, "ball label"))), framed = !disc, inInset = true });
            string discKey = KeyFor(p, discLabelOrig.GetComponent<TMP_Text>(), "KD_TreatPouch.WordToyDisc", "KD_TreatPouch.Toy.cs RedrawHillsideToy", out src);
            w.callouts.Add(new CalloutPx { key = discKey, keySource = src, role = "button", rect = MapInset(s.Hi((RectTransform)s.Twin(discLabelOrig, "disc label"))), framed = disc, inInset = true });
        }

        /// <summary>N2: the top row's left half cropped to 4:3, both throw-style buttons framed. Callouts: 投げ方 (no frame),
        /// まっすぐ / 曲げる on their buttons. The runtime writes these labels in RedrawToyTopRow (KD_TreatPouch.ToyTop.cs:45-47).</summary>
        static void ThrowRowCut(KD_TreatPouch p, PanelShot s, CutWork w)
        {
            var labelHi = s.Hi((RectTransform)s.Twin(p.throwStyleLabel.transform, "throwStyleLabel"));
            var straightHi = s.Hi((RectTransform)s.Twin(p.throwStraightImage.transform, "throwStraightImage"));
            var curveHi = s.Hi((RectTransform)s.Twin(p.throwCurveImage.transform, "throwCurveImage"));
            var union = Rect.MinMaxRect(Mathf.Min(labelHi.xMin, straightHi.xMin, curveHi.xMin), Mathf.Min(labelHi.yMin, straightHi.yMin, curveHi.yMin),
                Mathf.Max(labelHi.xMax, straightHi.xMax, curveHi.xMax), Mathf.Max(labelHi.yMax, straightHi.yMax, curveHi.yMax));
            float cw = union.width * 1.15f, ch = cw * CutH / CutW;
            if (ch > s.H) { ch = s.H; cw = ch * CutW / CutH; }
            if (cw > s.W) { cw = s.W; ch = cw * CutH / CutW; }
            var crop = new Rect(Mathf.Clamp(union.center.x - cw * 0.5f, 0f, s.W - cw), Mathf.Clamp(union.center.y - ch * 0.5f, 0f, s.H - ch), cw, ch);
            w.final = s.hi.Resample(crop, CutW, CutH);
            float k = CutW / cw;
            Rect Map(Rect hi) { return new Rect((hi.x - crop.x) * k, (hi.y - crop.y) * k, hi.width * k, hi.height * k); }
            w.notes.Add("crop " + crop + " of the " + s.W + "x" + s.H + " panel render (" + (k / PxPerUnit).ToString("F2") + " px per canvas unit)");
            w.final.Dim(MenuDim, Grow(Map(straightHi), FramePad), Grow(Map(curveHi), FramePad));
            DrawFrame(w.final, Map(straightHi), w.marks, "[まっすぐ]");
            DrawFrame(w.final, Map(curveHi), w.marks, "[曲げる]");
            string src;
            w.callouts.Add(new CalloutPx { key = KeyFor(p, p.throwStyleLabel, "course.toy.throw.label", "KD_TreatPouch.ToyTop.cs RedrawToyTopRow", out src), keySource = src, role = "label", rect = Map(labelHi), framed = false, inInset = false });
            w.callouts.Add(new CalloutPx { key = KeyFor(p, p.throwStraightLabel, "course.toy.throw.straight", "KD_TreatPouch.ToyTop.cs RedrawToyTopRow", out src), keySource = src, role = "button", rect = Map(s.Hi((RectTransform)s.Twin(p.throwStraightLabel.transform, "throwStraightLabel"))), framed = true, inInset = false });
            w.callouts.Add(new CalloutPx { key = KeyFor(p, p.throwCurveLabel, "course.toy.throw.curve", "KD_TreatPouch.ToyTop.cs RedrawToyTopRow", out src), keySource = src, role = "button", rect = Map(s.Hi((RectTransform)s.Twin(p.throwCurveLabel.transform, "throwCurveLabel"))), framed = true, inInset = false });
        }

        /// <summary>The top-left corner the board keeps for the step number (plan §3.2: Ø56 mm circle at each cut's top-left).</summary>
        static Rect BadgeZone() { return new Rect(0f, CutH - BadgeZonePx, BadgeZonePx, BadgeZonePx); }
        const float BadgeZonePx = 76f;
    }
}
