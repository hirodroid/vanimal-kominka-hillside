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
            "Canvas/HeaderPlate", "Canvas/Header", "Canvas/Subtitle", "Canvas/Paper", "Canvas/Paper/Perks", "Canvas/Paper/Cove", "Canvas/Photo",
            "Actions/Plate", "Actions/Status", "Actions/GiftNote", "Actions/StoreButton", "Actions/StoreButton/Label", "Actions/PortalButton", "Actions/PortalButton/Label",
            "Actions/DetailsButton", "Actions/DetailsButton/Label",
            "Details/Paper", "Details/Body", "Details/UrlCaption", "Details/UrlField", "Details/CloseButton", "Details/CloseButton/Label",
        };

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
                req(broken.Count == 0, tag + "placement rules (beside the Kominka board, both portals clear, gate line, spawn, fixtures, other boards / posters and their readers, physics, ground)"
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
            var face = t.Find(CanvasName);
            var actions = t.Find(ActionsName) as RectTransform;
            var details = t.Find(DetailsName) as RectTransform;
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
            foreach (var op in new[] { actions, details })
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
            if (details != null && face != null && actions != null)
            {
                req(!details.gameObject.activeSelf, tag + "Details inactive in the saved scene (KH_TrainerBoard opens it per player)");
                var dc = details.GetComponent<Canvas>(); var ac = actions.GetComponent<Canvas>(); var fc = face.GetComponent<Canvas>();
                req(dc != null && fc != null && ac != null && dc.sortingOrder > fc.sortingOrder && dc.sortingOrder > ac.sortingOrder, tag + "Details draws over the face (sortingOrder " + (dc != null ? dc.sortingOrder : 0) + " > " + (fc != null ? fc.sortingOrder : 0) + ")");
                var fp = face.Find("Paper") as RectTransform; var dp = details.Find("Paper") as RectTransform;
                if (fp != null && dp != null)
                {
                    Rect fr = FrameRect(t, fp), dr = FrameRect(t, dp), ar = FrameRect(t, actions);
                    req(Mathf.Abs(fr.xMin - dr.xMin) < 1e-3f && Mathf.Abs(fr.xMax - dr.xMax) < 1e-3f && Mathf.Abs(fr.yMin - dr.yMin) < 1e-3f && Mathf.Abs(fr.yMax - dr.yMax) < 1e-3f,
                        tag + "Details covers exactly the face's paper (its pocket is the paper's)");
                    req(dr.yMin >= ar.yMax, tag + "Details leaves the Actions strip free (" + ((dr.yMin - ar.yMax) * 1000f).ToString("F0") + " mm between)");
                }
            }

            // ---- the cove portal (before the runtime checks, so a missing KH_TrainerBoard does not hide it) ----
            CheckCovePortal(c, a, t, k, req, warn, v);

            // ---- KH_TrainerBoard and its wiring ----
            var tb = actions != null ? actions.GetComponent<KH_TrainerBoard>() : null;
            req(tb != null, tag + "Actions has KH_TrainerBoard");
            if (tb == null) return;
            var bk = UdonSharpEditorUtility.GetBackingUdonBehaviour(tb);
            req(bk != null && bk.SyncMethod == VRC.SDKBase.Networking.SyncType.None, tag + "KH_TrainerBoard sync None (local only, no network objects)");
            var statusT = Tmp(actions, "Status"); var giftT = Tmp(actions, "GiftNote"); var storeL = Tmp(actions, "StoreButton/Label");
            var storeB = actions.Find(StoreButtonName) != null ? actions.Find(StoreButtonName).GetComponent<Button>() : null;
            var anchorGo = c.coveAnchor != null ? c.coveAnchor.gameObject : null;
            var detailsGo = details != null ? details.gameObject : null;
            req(tb.pass == c.trainerPass && tb.language == c.language && tb.portal == anchorGo && anchorGo != null && tb.details == detailsGo && detailsGo != null,
                tag + "KH_TrainerBoard.pass / language / portal (" + CovePortalName + ") / details (proxy)");
            req(tb.status == statusT && statusT != null && tb.storeLabel == storeL && storeL != null && tb.giftNote == giftT && giftT != null && tb.storeButton == storeB && storeB != null,
                tag + "KH_TrainerBoard.status / storeLabel / giftNote / storeButton (proxy)");
            req(Mathf.Approximately(tb.showSeconds, PortalShowSeconds), tag + "showSeconds " + tb.showSeconds + " (= the Kominka portal's " + PortalShowSeconds + ")");
            if (bk != null)
            {
                Func<string, object, bool> same = (name, want) => { object x; return bk.publicVariables.TryGetVariableValue(name, out x) && want != null && ReferenceEquals(x, want); };
                var passBk = c.trainerPass != null ? (object)UdonSharpEditorUtility.GetBackingUdonBehaviour(c.trainerPass) : null;
                var langBk = c.language != null ? (object)UdonSharpEditorUtility.GetBackingUdonBehaviour(c.language) : null;
                req(same("pass", passBk) && same("language", langBk), tag + "KH_TrainerBoard.pass / language (backing = their backing UdonBehaviours)");
                req(same("portal", anchorGo) && same("details", detailsGo) && same("status", statusT) && same("storeLabel", storeL) && same("giftNote", giftT) && same("storeButton", storeB),
                    tag + "KH_TrainerBoard.portal / details / status / storeLabel / giftNote / storeButton (backing)");
            }
            foreach (var w in new[] { new[] { "Actions/" + StoreButtonName, StoreEvent }, new[] { "Actions/" + PortalButtonName, PortalEvent },
                new[] { "Actions/" + DetailsButtonName, DetailsOpenEvent }, new[] { "Details/" + CloseButtonName, DetailsCloseEvent } })
            {
                var bt = t.Find(w[0]);
                var btn = bt != null ? bt.GetComponent<Button>() : null;
                string why;
                req(ClickWired(btn, bk, w[1], out why), tag + w[0] + " -> " + w[1] + " (one String listener on KH_TrainerBoard's backing SendCustomEvent)" + (why != null ? ": " + why : ""));
                var img = bt != null ? bt.GetComponent<Image>() : null;
                req(btn != null && img != null && btn.targetGraphic == img && img.raycastTarget && btn.interactable && img.type == Image.Type.Sliced, tag + w[0] + " is a rounded (Sliced) raycast target, interactable in the saved scene");
            }
            var graphicsOff = new List<string>();
            foreach (var op in new[] { actions, details })
                if (op != null)
                    foreach (var g in op.GetComponentsInChildren<Graphic>(true))
                        if (g.raycastTarget && g.GetComponent<Button>() == null && g.GetComponent<TMP_InputField>() == null) graphicsOff.Add(KD_SceneObjects.PathOf(g.gameObject));
            req(graphicsOff.Count == 0, tag + "only the buttons and the URL field take the pointer" + (graphicsOff.Count > 0 ? " (" + string.Join(", ", graphicsOff.Take(5)) + ")" : ""));

            // ---- the URL field ----
            var uf = details != null ? details.Find("UrlField") : null;
            var field = uf != null ? uf.GetComponent<TMP_InputField>() : null;
            req(field != null && field.text == CoveUrl && field.readOnly && field.onFocusSelectAll && !field.richText && field.lineType == TMP_InputField.LineType.SingleLine
                && field.textViewport != null && field.textViewport.GetComponent<RectMask2D>() != null && field.textComponent != null && field.textComponent.font == c.font,
                tag + "Details/UrlField = " + CoveUrl + " (read-only copy field, select-all on focus, single line, masked, the menu font)");
            if (field != null && field.textComponent != null && field.textViewport != null)
            {
                var go = new GameObject("TrainerUrlMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var sample = go.AddComponent<TextMeshProUGUI>();
                    sample.isOrthographic = true; sample.enableAutoSizing = false; sample.font = c.font; sample.fontSize = field.pointSize; sample.enableWordWrapping = false; sample.richText = false;
                    float w = sample.GetPreferredValues(CoveUrl, Mathf.Infinity, Mathf.Infinity).x, room = field.textViewport.rect.width;
                    warn(w <= room, tag + "the whole URL shows in its field (" + w.ToString("F0") + " / " + room.ToString("F0") + " mm at " + field.pointSize + " mm)");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }

            // ---- the dynamic words (Edit mode through _Render; the scene values come back afterwards) ----
            if (statusT != null && storeL != null && giftT != null && storeB != null) CheckTrainerStates(c, tb, statusT, storeL, giftT, storeB, req, warn, v);
        }

        static TextMeshProUGUI Tmp(Transform parent, string path)
        {
            var x = parent != null ? parent.Find(path) : null;
            return x != null ? x.GetComponent<TextMeshProUGUI>() : null;
        }

        static void CheckCovePortal(Ctx c, Anch a, Transform t, Transform k, Action<bool, string> req, Action<bool, string> warn, VerifyReport v)
        {
            string tag = "cove portal: ";
            var m = c.covePortal; var anchor = c.coveAnchor;
            req(m != null, tag + "one VRCPortalMarker with roomId " + CoveRoomId + " (Guide/1 creates it)");
            req(anchor != null && anchor.parent == null, tag + CovePortalName + " at the scene root");
            if (m == null || anchor == null) return;
            req(!anchor.gameObject.activeSelf && m.gameObject.activeSelf, tag + CovePortalName + " inactive in the saved scene (hidden for everyone at load, late joiners too), the marker itself active");
            req(m.transform.parent == anchor && m.transform.localPosition == Vector3.zero && m.transform.localRotation == Quaternion.identity && m.transform.localScale == Vector3.one,
                tag + "the marker " + KD_SceneObjects.PathOf(m.gameObject) + " sits at the anchor (local pose zero)");
            req(c.portal != null && c.portal != m && c.portalRoomId == ExpectedRoomId, tag + "the Kominka portal is a different marker with its own world id (" + (c.portalRoomId ?? "none") + ")");
            if (c.portal != null)
            {
                // Codex review 2026-10-16 中2: always checked (no skip): the cove marker is an instance of the portal prefab (SdkPortalPrefabPath)
                req(c.portalPrefab != null && PrefabUtility.IsOutermostPrefabInstanceRoot(m.gameObject) && PrefabUtility.GetCorrespondingObjectFromSource(m.gameObject) == c.portalPrefab,
                    tag + "an instance of the portal prefab " + (c.portalPrefab != null ? AssetDatabase.GetAssetPath(c.portalPrefab) : SdkPortalPrefabPath + " (not found)"));
                var diffs = PortalFieldDiffs(c.portal, m);
                req(diffs.Count == 0, tag + "the same presentation as the Kominka portal (every VRCPortalMarker field but roomId)" + (diffs.Count > 0 ? ": differs in " + string.Join(", ", diffs) : ""));
            }
            var cols = anchor.GetComponentsInChildren<Collider>(true);
            req(cols.All(x => x.isTrigger), tag + "only trigger colliders (players walk into it)");
            if (t == null || k == null) return;
            float side = TrainerPortalSide(t.position, t.eulerAngles.y, k.position);
            var spot = t.TransformPoint(new Vector3(side * PortalSideX, 0f, BackZ));
            float g = Ground(spot);
            req(Flat(anchor.position - spot) <= 0.01f && Mathf.Abs(anchor.position.y - g) <= 0.05f, tag + "beside the Trainer board on the side away from the Kominka board, on the terrain ("
                + Flat(anchor.position - spot).ToString("F3") + " m off, " + (anchor.position.y - g).ToString("+0.00;-0.00") + " m; Guide/1 places it)");
            req(Mathf.Abs(Mathf.DeltaAngle(anchor.eulerAngles.y, t.eulerAngles.y + PortalYaw)) < 0.5f && Mathf.Abs(Mathf.DeltaAngle(anchor.eulerAngles.x, 0f)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(anchor.eulerAngles.z, 0f)) < 0.01f,
                tag + "faces the readers like the Kominka portal (yaw " + anchor.eulerAngles.y.ToString("F1") + ")");
            var kp = k.Find(PortalAnchorName);
            v.notes.Add(tag + "at " + V(anchor.position) + (kp != null ? ", " + Flat(anchor.position - kp.position).ToString("F2") + " m from the Kominka portal" : "")
                + (a.spawn != null ? ", " + Flat(anchor.position - a.spawn.position).ToString("F1") + " m from the spawn" : "") + ". Whether a portal to a private (URL-only) world lets other players through is not judged here (device)");
        }

        /// <summary>Every visible serialized field of two VRCPortalMarkers that differs, roomId excepted.</summary>
        static List<string> PortalFieldDiffs(VRC.SDK3.Components.VRCPortalMarker x, VRC.SDK3.Components.VRCPortalMarker y)
        {
            var diffs = new List<string>();
            var sx = new SerializedObject(x); var sy = new SerializedObject(y);
            var it = sx.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "roomId" || it.name == "m_Script") continue;
                var other = sy.FindProperty(it.propertyPath);
                if (other == null || !SerializedProperty.DataEquals(it, other)) diffs.Add(it.propertyPath);
            }
            return diffs;
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
