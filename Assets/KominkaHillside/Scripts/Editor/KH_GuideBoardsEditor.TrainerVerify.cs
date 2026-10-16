using System;
using System.Collections.Generic;
using System.Linq;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    // Guide/2, IMP-UI-013: the Trainer board, its operation canvases, KH_TrainerBoard and its wiring, the cove portal, the photo's import and the
    // dynamic words (rendered in Edit mode through KH_TrainerBoard._Render, the scene values restored afterwards). Never saves.
    public static partial class KH_GuideBoardsEditor
    {
        static readonly string[] TrainerTree =
        {
            "Canvas/HeaderPlate", "Canvas/Header", "Canvas/Subtitle", "Canvas/Paper", "Canvas/Paper/Cove", "Canvas/Paper/Support", "Canvas/Paper/Price", "Canvas/Photo",
            "Actions/Plate", "Actions/Status", "Actions/GiftNote", "Actions/StoreButton", "Actions/StoreButton/Label",
            "Actions/UrlCaption", "Actions/UrlField",
        };

        /// <summary>Earlier builds' nodes that must be gone (2026-10-16: the portal button, the perks on the face, the details popup and its button).</summary>
        static readonly string[] TrainerGone = { "Actions/" + StalePortalButtonName, "Actions/" + StaleDetailsButtonName, StaleDetailsName, "Canvas/Paper/Perks" };

        static void CheckTrainer(Ctx c, Anch a, Action<bool, string> req, Action<bool, string> warn, VerifyReport v)
        {
            var t = c.root.Find(Trainer.name);
            if (t == null) return;   // CheckBoard already failed "Trainer exists"
            string tag = Trainer.name + ": ";
            var k = c.root.Find(Kominka.name);

            // ---- placement: the same rules Guide/1 decides with ----
            if (k != null)
            {
                List<string> broken;
                try { broken = TrainerRules(c, a, t.position, t.eulerAngles.y, k.position, k.eulerAngles.y, t, true); }
                catch (Exception e) { broken = new List<string> { "could not be measured: " + e.Message }; }
                req(broken.Count == 0, tag + "placement rules (beside the Kominka board, clear of its portal, gate line, spawn, fixtures, other boards / posters and their readers, physics, ground)"
                    + (broken.Count > 0 ? ": " + string.Join("; ", broken) : ""));
                req(Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, k.eulerAngles.y)) < 1f, tag + "faces like the Kominka board (yaw " + t.eulerAngles.y.ToString("F1") + " / " + k.eulerAngles.y.ToString("F1") + ")");
                var d = Flat3(t.position - k.position);
                v.notes.Add(tag + d.magnitude.ToString("F2") + " m " + Compass(d) + " of the Kominka board; centre at " + V(t.position) + (a.spawn != null ? ", " + Flat(t.position - a.spawn.position).ToString("F1") + " m from the spawn" : ""));
            }
            else req(false, tag + "the Kominka board is missing (the Trainer board is placed beside it)");
            float buttonsUp = CanvasCenterY(Trainer) + TrActionsY * Mm;
            v.notes.Add(tag + "the buttons' centre is " + buttonsUp.ToString("F2") + " m above the board's seat; the photo / perks centre " + (CanvasCenterY(Trainer) + TrPhotoPos.y * Mm).ToString("F2") + " m");

            // ---- the tree ----
            foreach (var p in TrainerTree) req(t.Find(p) != null, tag + p);
            foreach (var p in TrainerGone) req(t.Find(p) == null, tag + "no " + p + " (removed 2026-10-16: no portal to the private cove, no details popup, one button; Guide/1 removes it)");
            var sub = Tmp(t, "Canvas/Subtitle");
            req(sub != null && (sub.fontStyle & FontStyles.Bold) != 0 && !sub.enableWordWrapping, tag + "the catch copy (Canvas/Subtitle) is bold and one line");
            // the face: Support and Price under the photo and the cove lead, full width
            var ph = t.Find("Canvas/Photo") as RectTransform; var cv2 = Tmp(t, "Canvas/Paper/Cove"); var sp = Tmp(t, "Canvas/Paper/Support"); var pr = Tmp(t, "Canvas/Paper/Price");
            if (ph != null && cv2 != null && sp != null && pr != null)
            {
                Rect rph = FrameRect(t, ph), rcv = FrameRect(t, cv2.rectTransform), rsp = FrameRect(t, sp.rectTransform), rpr = FrameRect(t, pr.rectTransform);
                req(rcv.xMin >= rph.xMax && Mathf.Min(rph.yMin, rcv.yMin) >= rsp.yMax && rsp.yMin >= rpr.yMax,
                    tag + "face: Cove beside the photo, Support under both, Price under Support (no overlap)");
            }
            var face = t.Find(CanvasName);
            var actions = t.Find(ActionsName) as RectTransform;
            if (face != null)
                req(face.GetComponent<GraphicRaycaster>() == null && face.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null && face.GetComponent<Collider>() == null,
                    tag + "the face canvas is read-only (no raycaster / VRCUiShape / collider)");
            var photoT = t.Find("Canvas/Photo");
            var raw = photoT != null ? photoT.GetComponent<RawImage>() : null;
            // Codex review 2026-10-16 低: a missing / hidden photo fails (it no longer only skips the checks below)
            req(raw != null && raw.enabled && raw.texture != null && raw.color.a >= 0.999f && photoT.gameObject.activeInHierarchy,
                tag + "Canvas/Photo is a drawn RawImage (present, enabled, a texture, opaque, active)");
            if (raw != null)
            {
                var rr = raw.rectTransform.rect;
                req(c.covePhoto != null && raw.texture == c.covePhoto && !raw.raycastTarget && raw.uvRect == new Rect(0f, 0f, 1f, 1f), tag + "the photo is " + CovePhotoPath + " (whole image, not a raycast target)");
                req(Mathf.Abs(rr.width / Mathf.Max(1f, rr.height) - TrPhotoAspect) < 0.01f, tag + "the photo keeps the source's 14:11 (" + rr.width.ToString("F0") + " x " + rr.height.ToString("F0") + " mm)");
            }
            var imp = AssetImporter.GetAtPath(CovePhotoPath) as TextureImporter;
            var ip = CovePhotoImportProblems(imp);
            req(ip.Count == 0, tag + "photo import (sRGB, no alpha, mipmaps, POT, no Read/Write, 1024, PC DXT1 / Android + iOS ASTC 6x6)" + (ip.Count > 0 ? ": " + string.Join(", ", ip) + " (Guide/1 sets it)" : ""));
            if (c.covePhoto != null) v.notes.Add(tag + "photo texture " + c.covePhoto.width + " x " + c.covePhoto.height + " " + c.covePhoto.format + (c.covePhoto.mipmapCount > 1 ? " mip" : " nomip"));

            // ---- the operation canvases ----
            foreach (var op in new[] { actions })
            {
                if (op == null) continue;
                string n = tag + op.name;
                var cvs = op.GetComponent<Canvas>(); var box = op.GetComponent<BoxCollider>();
                req(cvs != null && cvs.renderMode == RenderMode.WorldSpace && op.GetComponent<GraphicRaycaster>() != null && op.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null,
                    n + " is an operation canvas (World Space + GraphicRaycaster + VRCUiShape)");
                req(box != null && box.isTrigger && box.enabled && box.size.x >= op.sizeDelta.x - 0.5f && box.size.y >= op.sizeDelta.y - 0.5f && op.gameObject.layer == 0,
                    n + " has a trigger BoxCollider over its whole rect, Default layer (the wash board's UI shape)");
                req(Mathf.Abs(op.localScale.x - Mm) < 1e-6f && op.localRotation == Quaternion.identity && Mathf.Abs(op.localPosition.z - TrayFront) < 1e-4f,
                    n + " in the tray's front plane, 1 unit = 1 mm, facing like the face");
                var ui = op.GetComponents<Component>().Where(x => x is CanvasScaler).ToArray();
                req(ui.Length == 0, n + " has no CanvasScaler");
            }
            if (actions != null && face != null && t.Find("Canvas/Paper") is RectTransform fpp)
                req(FrameRect(t, fpp).yMin >= FrameRect(t, actions).yMax, tag + "the paper ends above the Actions strip");

            // ---- no cove portal (2026-10-16: a VRCPortalMarker only opens a public instance; the cove is a Private world) ----
            int anchorsLeft; KD_SceneObjects.Find(CovePortalName, out anchorsLeft);
            req(anchorsLeft == 0, "no " + CovePortalName + " in the scene (" + anchorsLeft + "; the first build's cove portal, Guide/1 removes it)");
            req(c.staleCoveMarkers.Count == 0, "no VRCPortalMarker with the cove's world id " + CoveRoomId + " (" + string.Join(", ", c.staleCoveMarkers.Select(m => KD_SceneObjects.PathOf(m.gameObject))) + ")");

            // ---- KH_TrainerBoard and its wiring ----
            var tb = actions != null ? actions.GetComponent<KH_TrainerBoard>() : null;
            req(tb != null, tag + "Actions has KH_TrainerBoard");
            if (tb == null) return;
            var bk = UdonSharpEditorUtility.GetBackingUdonBehaviour(tb);
            req(bk != null && bk.SyncMethod == VRC.SDKBase.Networking.SyncType.None, tag + "KH_TrainerBoard sync None (local only, no network objects)");
            var statusT = Tmp(actions, "Status"); var giftT = Tmp(actions, "GiftNote"); var storeL = Tmp(actions, "StoreButton/Label");
            var storeB = actions.Find(StoreButtonName) != null ? actions.Find(StoreButtonName).GetComponent<Button>() : null;
            req(tb.pass == c.trainerPass && tb.language == c.language, tag + "KH_TrainerBoard.pass / language (proxy)");
            req(tb.status == statusT && statusT != null && tb.storeLabel == storeL && storeL != null && tb.giftNote == giftT && giftT != null && tb.storeButton == storeB && storeB != null,
                tag + "KH_TrainerBoard.status / storeLabel / giftNote / storeButton (proxy)");
            if (bk != null)
            {
                Func<string, object, bool> same = (name, want) => { object x; return bk.publicVariables.TryGetVariableValue(name, out x) && want != null && ReferenceEquals(x, want); };
                var passBk = c.trainerPass != null ? (object)UdonSharpEditorUtility.GetBackingUdonBehaviour(c.trainerPass) : null;
                var langBk = c.language != null ? (object)UdonSharpEditorUtility.GetBackingUdonBehaviour(c.language) : null;
                req(same("pass", passBk) && same("language", langBk), tag + "KH_TrainerBoard.pass / language (backing = their backing UdonBehaviours)");
                req(same("status", statusT) && same("storeLabel", storeL) && same("giftNote", giftT) && same("storeButton", storeB),
                    tag + "KH_TrainerBoard.status / storeLabel / giftNote / storeButton (backing)");
            }
            foreach (var w in new[] { new[] { "Actions/" + StoreButtonName, StoreEvent } })
            {
                var bt = t.Find(w[0]);
                var btn = bt != null ? bt.GetComponent<Button>() : null;
                string why;
                req(ClickWired(btn, bk, w[1], out why), tag + w[0] + " -> " + w[1] + " (one String listener on KH_TrainerBoard's backing SendCustomEvent)" + (why != null ? ": " + why : ""));
                var img = bt != null ? bt.GetComponent<Image>() : null;
                req(btn != null && img != null && btn.targetGraphic == img && img.raycastTarget && btn.interactable && img.type == Image.Type.Sliced, tag + w[0] + " is a rounded (Sliced) raycast target, interactable in the saved scene");
            }
            var graphicsOff = new List<string>();
            foreach (var op in new[] { actions })
                if (op != null)
                    foreach (var g in op.GetComponentsInChildren<Graphic>(true))
                        if (g.raycastTarget && g.GetComponent<Button>() == null && g.GetComponent<TMP_InputField>() == null) graphicsOff.Add(KD_SceneObjects.PathOf(g.gameObject));
            req(graphicsOff.Count == 0, tag + "only the button and the URL field take the pointer" + (graphicsOff.Count > 0 ? " (" + string.Join(", ", graphicsOff.Take(5)) + ")" : ""));

            // ---- the URL field ----
            var uf = actions != null ? actions.Find(UrlFieldName) : null;
            var field = uf != null ? uf.GetComponent<TMP_InputField>() : null;
            req(field != null && field.text == CoveUrl && field.readOnly && field.onFocusSelectAll && !field.richText && field.lineType == TMP_InputField.LineType.SingleLine
                && field.textViewport != null && field.textViewport.GetComponent<RectMask2D>() != null && field.textComponent != null && field.textComponent.font == c.font,
                tag + "Actions/UrlField = " + CoveUrl + " (read-only copy field, select-all on focus, single line, masked, the menu font, inside the raycaster canvas)");
            if (uf != null)
                req(Mathf.Abs(uf.localScale.x - TrUrlScale) < 1e-5f && Mathf.Abs(uf.localScale.y - TrUrlScale) < 1e-5f && ((RectTransform)uf).sizeDelta == TrUrlPx && uf.gameObject.activeInHierarchy,
                    tag + "Actions/UrlField at the wall poster's 0.8 mm per unit, " + TrUrlPx.x + " x " + TrUrlPx.y + " (" + (TrUrlPx.x * TrUrlScale).ToString("F0") + " x " + (TrUrlPx.y * TrUrlScale).ToString("F0") + " mm), shown");
            if (field != null && field.textComponent != null && field.textViewport != null)
            {
                var go = new GameObject("TrainerUrlMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var sample = go.AddComponent<TextMeshProUGUI>();
                    sample.isOrthographic = true; sample.enableAutoSizing = false; sample.font = c.font; sample.fontSize = field.pointSize; sample.enableWordWrapping = false; sample.richText = false;
                    float w = sample.GetPreferredValues(CoveUrl, Mathf.Infinity, Mathf.Infinity).x, room = field.textViewport.rect.width;
                    req(w <= room, tag + "the whole URL fits its field on one line (" + w.ToString("F0") + " / " + room.ToString("F0") + " units at " + field.pointSize + " pt = " + (field.pointSize * TrUrlScale).ToString("F1") + " mm)");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }

            // ---- the dynamic words (Edit mode through _Render; the scene values come back afterwards) ----
            if (statusT != null && storeL != null && giftT != null && storeB != null) CheckTrainerStates(c, tb, statusT, storeL, giftT, storeB, req, warn, v);
            // 2026-10-16: the button's label on one line in both states (ja / en FAIL, the others WARN)
            if (storeL != null) { CheckOneLine(c, storeL, "Actions/StoreButton/Label", KH_TrainerBoard.KeyJoin, req, warn); CheckOneLine(c, storeL, "Actions/StoreButton/Label", KH_TrainerBoard.KeyGift, req, warn); }
        }

        static TextMeshProUGUI Tmp(Transform parent, string path)
        {
            var x = parent != null ? parent.Find(path) : null;
            return x != null ? x.GetComponent<TextMeshProUGUI>() : null;
        }

        /// <summary>The label drawn with each language's text of <paramref name="key"/>: one line (textInfo.lineCount after ForceMeshUpdate), no
        /// wrapping, within the box's width. ja / en FAIL, the client-glyph languages WARN (their glyphs are VRChat's). The text comes back after.</summary>
        static void CheckOneLine(Ctx c, TextMeshProUGUI label, string path, string key, Action<bool, string> req, Action<bool, string> warn)
        {
            string t0 = label.text;
            var off = new List<string>();
            try
            {
                req(!label.enableWordWrapping, path + " does not wrap (one line, auto-size shrinks it)");
                foreach (var lang in c.catalog.languages)
                {
                    string text = KD_LocalizationEditor.Text(c.catalog, lang.code, key);
                    label.text = text;
                    label.ForceMeshUpdate(true, true);
                    int lines = label.textInfo.lineCount;
                    float w = label.textBounds.size.x, room = label.rectTransform.rect.width;
                    bool ok = lines == 1 && w <= room + 2f;
                    string what = path + " " + lang.code + " 「" + text + "」 " + lines + " line(s), " + w.ToString("F0") + " / " + room.ToString("F0") + " mm at " + label.fontSize.ToString("F1") + " mm";
                    if (KD_LocalizationEditor.Bundled(lang)) req(ok, what + " (one line, inside the button)");
                    else if (!ok) off.Add(lang.code);
                }
                warn(off.Count == 0, path + " " + key + " one line in the client-glyph languages (estimate)" + (off.Count > 0 ? ": not in " + string.Join(", ", off) : ""));
            }
            catch (Exception e) { req(false, path + " one-line check failed: " + e.Message); }
            finally { label.text = t0; label.ForceMeshUpdate(true, true); }
        }

        static void CheckTrainerStates(Ctx c, KH_TrainerBoard tb, TextMeshProUGUI status, TextMeshProUGUI storeLabel, TextMeshProUGUI giftNote, Button storeButton,
            Action<bool, string> req, Action<bool, string> warn, VerifyReport v)
        {
            string tag = "Trainer states: ";
            req(tb._StateFrom(false, false, false) == KH_TrainerBoard.StateUnconfigured && tb._StateFrom(false, true, true) == KH_TrainerBoard.StateUnconfigured
                && tb._StateFrom(true, false, false) == KH_TrainerBoard.StateLoading && tb._StateFrom(true, false, true) == KH_TrainerBoard.StateLoading
                && tb._StateFrom(true, true, false) == KH_TrainerBoard.StateOff && tb._StateFrom(true, true, true) == KH_TrainerBoard.StateOn,
                tag + "KH_TrainerPass -> state (no product / listing = unconfigured; not loaded = checking; loaded = owned or not)");
            string s0 = status.text, l0 = storeLabel.text, g0 = giftNote.text; bool a0 = giftNote.gameObject.activeSelf, i0 = storeButton.interactable;
            req(s0 == KH_TrainerBoard.JaLoading && !a0 && i0, tag + "the saved scene shows the checking state (「" + s0 + "」, gift note hidden, button interactable); Start re-renders");
            int[] states = { KH_TrainerBoard.StateUnconfigured, KH_TrainerBoard.StateLoading, KH_TrainerBoard.StateOff, KH_TrainerBoard.StateOn };
            string[] statusKeys = { KH_TrainerBoard.KeyUnconfigured, KH_TrainerBoard.KeyLoading, KH_TrainerBoard.KeyOff, KH_TrainerBoard.KeyOn };
            string[] names = { "unconfigured", "checking", "not a member", "member" };
            var go = new GameObject("TrainerStatesMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                foreach (var code in new[] { "ja", "en" })
                    for (int i = 0; i < states.Length; i++)
                    {
                        bool on = states[i] == KH_TrainerBoard.StateOn;
                        tb._Render(states[i], code);
                        string ws = KD_LocalizationEditor.Text(c.catalog, code, statusKeys[i]);
                        string wl = KD_LocalizationEditor.Text(c.catalog, code, on ? KH_TrainerBoard.KeyGift : KH_TrainerBoard.KeyJoin);
                        string wg = KD_LocalizationEditor.Text(c.catalog, code, KH_TrainerBoard.KeyGiftNote);
                        bool ok = status.text == ws && storeLabel.text == wl && storeButton.interactable == (states[i] != KH_TrainerBoard.StateUnconfigured)
                            && giftNote.gameObject.activeSelf == on && (!on || giftNote.text == wg);
                        req(ok, tag + names[i] + " (" + code + "): 「" + status.text + "」 / [" + storeLabel.text + "]" + (giftNote.gameObject.activeSelf ? " / 「" + giftNote.text + "」" : "") + ", button "
                            + (storeButton.interactable ? "on" : "off") + (ok ? "" : " (expected 「" + ws + "」 / [" + wl + "]" + (on ? " / 「" + wg + "」" : "") + ")"));
                    }
                // fit of every dynamic word in its box (ja / en with the bundled font; the others are a WARN estimate, as CheckFit)
                var sample = go.AddComponent<TextMeshProUGUI>();
                var pairs = statusKeys.Select(x => new KeyValuePair<TMP_Text, string>(status, x)).Concat(new[]
                {
                    new KeyValuePair<TMP_Text, string>(storeLabel, KH_TrainerBoard.KeyJoin), new KeyValuePair<TMP_Text, string>(storeLabel, KH_TrainerBoard.KeyGift),
                    new KeyValuePair<TMP_Text, string>(giftNote, KH_TrainerBoard.KeyGiftNote),
                }).ToList();
                foreach (var pr in pairs)
                {
                    int worstSelf = 0; var over = new List<string>();
                    sample.lineSpacing = pr.Key.lineSpacing;
                    foreach (var lang in c.catalog.languages)
                    {
                        Vector2 pref;
                        int r = KD_LocalizationEditor.MeasureLabelFit(sample, pr.Key, KD_LocalizationEditor.Text(c.catalog, lang.code, pr.Value), out pref);
                        bool self = KD_LocalizationEditor.Bundled(lang);
                        if (self) { worstSelf = Mathf.Max(worstSelf, r); v.fit.Add(lang.code + " " + pr.Value + " preferred=" + pref.ToString("F0") + " rect=" + pr.Key.rectTransform.rect.size.ToString("F0") + " fit=" + r); }
                        else if (r == 2) over.Add(lang.code);
                    }
                    req(worstSelf < 2, pr.Value + " fits " + pr.Key.name + " in ja / en (worst " + worstSelf + ")");
                    warn(over.Count == 0, pr.Value + " fits " + pr.Key.name + " in the client-glyph languages (estimate)" + (over.Count > 0 ? ": overflows in " + string.Join(", ", over) : ""));
                }
            }
            catch (Exception e) { req(false, tag + "Edit-mode render failed: " + e.Message); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                status.text = s0; storeLabel.text = l0; giftNote.text = g0; storeButton.interactable = i0;
                if (giftNote.gameObject.activeSelf != a0) giftNote.gameObject.SetActive(a0);
            }
            v.notes.Add(tag + "rendered in Edit mode for ja / en; KH_TrainerPass's live answers (purchases loaded, owned) and the 0.5 / 2 s tick need Play / the device");
        }
    }
}
