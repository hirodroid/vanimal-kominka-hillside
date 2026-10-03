using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace KominkaHillside.Editor
{
    // Guide/12: the walk strips as saved (docs/plans/2026-10-03-onsite-guides.md §7.1). Reads only; the one temporary object (a
    // hidden TMP label for measuring the 13 languages) is HideAndDontSave and destroyed. Log line WALK_GUIDE_OK / WALK_GUIDE_FAIL,
    // report Temp/kh-walk-guide/verify.json.
    public static partial class KH_WalkGuideEditor
    {
        [Serializable] class VerifyReport
        {
            public string utc; public int failed, warned;
            public List<string> lines = new List<string>();
            public List<string> notes = new List<string>();
        }

        [MenuItem(Menu + "12 - Verify Walk Guide Strips")]
        public static void Verify()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Walk Guide] Edit mode only (not compiling)");
            if (EditorSceneManager.GetActiveScene().path != KH_CourseGroundEditor.ScenePath) throw new InvalidOperationException("[KH Walk Guide] open " + KH_CourseGroundEditor.ScenePath + " first");
            var v = Check();
            WriteJson(VerifyReportPath, v);
            if (v.failed > 0) Debug.LogError("[KH Walk Guide] WALK_GUIDE_FAIL (" + v.failed + " failed, " + v.warned + " WARN) -> " + VerifyReportPath + "\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL")).Take(30)));
            else Debug.Log("[KH Walk Guide] WALK_GUIDE_OK (" + v.lines.Count + " checks, " + v.warned + " WARN) -> " + VerifyReportPath);
        }

        const float Eps = 0.002f;

        static VerifyReport Check()
        {
            var v = new VerifyReport { utc = DateTime.UtcNow.ToString("o") };
            void Ok(bool ok, string what) { v.lines.Add((ok ? "PASS " : "FAIL ") + what); if (!ok) v.failed++; }
            void Warn(bool ok, string what) { v.lines.Add((ok ? "PASS " : "WARN ") + what); if (!ok) v.warned++; }

            Ctx c;
            try { c = Resolve(false); }
            catch (Exception e) { Ok(false, "resolve: " + e.Message); return v; }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath);
            Ok(tex != null, "art texture " + ArtPath);
            CheckImporter(Ok);
            CheckArtCurrent(Warn);

            // the localization: the scene's tables and bindings match the manifest (the whole manifest: a stale binding anywhere fails here)
            try { KD_LocalizationEditor.ValidateScene(c.language, c.catalog); Ok(true, "KD_Language tables and bindings match the catalog and the manifest"); }
            catch (Exception e) { Ok(false, "KD_Language ValidateScene (whole manifest, re-run Guide/11 or the builder named): " + e.Message); }

            var measure = new GameObject("KH_WalkGuideMeasure") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var probe = measure.AddComponent<TextMeshProUGUI>();
                for (int i = 0; i < Stations.Length; i++) CheckStation(c, i, tex, probe, Ok, Warn, v);
            }
            finally { UnityEngine.Object.DestroyImmediate(measure); }
            Ok(UnityEngine.Object.FindObjectsOfType<KH_MenuOpener>(true).Length == Stations.Length, "KH_MenuOpener on the 3 station plates (" + UnityEngine.Object.FindObjectsOfType<KH_MenuOpener>(true).Length + ")");
            return v;
        }

        static void CheckStation(Ctx c, int index, Texture2D tex, TextMeshProUGUI probe, Action<bool, string> Ok, Action<bool, string> Warn, VerifyReport v)
        {
            var spec = Stations[index]; var st = c.stations[index]; string n = spec.name;
            var strips = st.Cast<Transform>().Where(t => t.name == StripName).ToArray();
            Ok(strips.Length == 1, n + ": one " + StripName + " (" + strips.Length + ")");
            if (strips.Length != 1) return;
            var strip = strips[0];
            Ok((strip.localPosition - new Vector3(0f, 0f, CrossbarZ)).magnitude < Eps && Quaternion.Angle(strip.localRotation, Quaternion.Euler(0f, spec.readsFromPlusZ ? 180f : 0f, 0f)) < 0.1f && strip.localScale == Vector3.one,
                n + ": " + StripName + " at the crossbar plane, yaw " + (spec.readsFromPlusZ ? 180 : 0));
            Ok(strip.gameObject.activeSelf, n + ": " + StripName + " active");
            foreach (var (name, pos, scale) in new[] { (PostLName, new Vector3(-PostX, PostY, 0f), PostScale), (PostRName, new Vector3(PostX, PostY, 0f), PostScale), (BackName, new Vector3(0f, BackY, 0f), BackScale) })
            {
                var t = strip.Find(name);
                Ok(t != null && (t.localPosition - pos).magnitude < Eps && (t.localScale - scale).magnitude < Eps, n + ": " + name + " placed " + V(pos) + " size " + V(scale));
                if (t == null) continue;
                Ok(t.GetComponentsInChildren<Collider>(true).Length == 0, n + ": " + name + " has no collider");
                var mr = t.GetComponent<MeshRenderer>();
                Ok(mr != null && mr.sharedMaterial == c.wood, n + ": " + name + " uses KH_RackWood");
            }
            var canvasT = strip.Find(CanvasName);
            Ok(canvasT != null, n + ": Canvas");
            if (canvasT == null) return;
            var canvas = canvasT.GetComponent<Canvas>(); var rect = canvasT as RectTransform;
            Ok(canvas != null && canvas.renderMode == RenderMode.WorldSpace, n + ": World Space canvas");
            Ok(rect != null && rect.sizeDelta == CanvasPixels && Mathf.Abs(rect.localScale.x - CanvasScale) < 1e-6f && (rect.localPosition - new Vector3(0f, CanvasY, CanvasZ)).magnitude < Eps && Quaternion.Angle(rect.localRotation, Quaternion.identity) < 0.1f,
                n + ": canvas 800 x 260 px at 1 mm/px, local " + V(new Vector3(0f, CanvasY, CanvasZ)));
            Ok(canvasT.GetComponentsInChildren<GraphicRaycaster>(true).Length == 0 && canvasT.GetComponentsInChildren<VRCUiShape>(true).Length == 0 && canvasT.GetComponentsInChildren<Collider>(true).Length == 0,
                n + ": read only (no GraphicRaycaster / VRCUiShape / Collider)");
            Ok(canvasT.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), n + ": every graphic raycastTarget false");
            float ground = st.position.y - StationGroundBelow;
            float bottom = canvasT.position.y - CanvasPixels.y * CanvasScale * 0.5f;
            Ok(Mathf.Abs(bottom - ground - (StationGroundBelow + StripBottom)) < 0.01f, n + ": strip bottom ground + " + (bottom - ground).ToString("F2") + " m (1.30)");
            var bar = st.Find(CrossbarName);
            if (bar != null) Ok(bottom - bar.position.y > 0.3f, n + ": strip bottom " + (bottom - bar.position.y).ToString("F2") + " m above the crossbar's centre (> 0.3: clear of the tips' pickup sphere)");

            var vrT = canvasT.Find(VrName); var flatT = canvasT.Find(FlatName);
            Ok(vrT != null && vrT.gameObject.activeSelf, n + ": Vr shown in the saved scene");
            Ok(flatT != null && !flatT.gameObject.activeSelf, n + ": Flat hidden in the saved scene");
            for (int a = 0; a < ArrowNames.Length; a++) Ok(canvasT.Find(ArrowNames[a])?.GetComponent<TextMeshProUGUI>() != null, n + ": " + ArrowNames[a]);
            foreach (var (gt, keys, cells, gname) in new[] { (vrT, VrKeys, VrCells, VrName), (flatT, FlatKeys, FlatCells, FlatName) })
            {
                if (gt == null) continue;
                for (int i = 0; i < LabelNames.Length; i++)
                {
                    var label = gt.Find(LabelNames[i])?.GetComponent<TextMeshProUGUI>();
                    Ok(label != null && label.font == c.font, n + "/" + gname + "/" + LabelNames[i] + ": TMP with the shared font");
                    if (label == null) continue;
                    int bi = Array.IndexOf(c.language.fixedLabels ?? new TMP_Text[0], label);
                    Ok(bi >= 0 && c.language.fixedKeys != null && bi < c.language.fixedKeys.Length && c.language.fixedKeys[bi] == keys[i], n + "/" + gname + "/" + LabelNames[i] + " bound to " + keys[i]);
                    if (index == 0) CheckFit(c, probe, label, keys[i], gname + "/" + LabelNames[i], Ok, Warn, v);
                }
                for (int i = 0; i < PicNames.Length; i++)
                {
                    var raw = gt.Find(PicNames[i])?.GetComponent<RawImage>();
                    Ok(raw != null && raw.texture == tex && raw.uvRect == CellUv(cells[i]), n + "/" + gname + "/" + PicNames[i] + ": cell " + cells[i]);
                }
            }
            // the plate's device switch, proxy and backing
            var opener = c.openers[index];
            bool proxyOk = opener.vrOnly != null && opener.vrOnly.Length == 1 && vrT != null && opener.vrOnly[0] == vrT.gameObject
                && opener.flatOnly != null && opener.flatOnly.Length == 1 && flatT != null && opener.flatOnly[0] == flatT.gameObject;
            Ok(proxyOk, n + "/" + PlateName + ": KH_MenuOpener.vrOnly = [Vr], flatOnly = [Flat]");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(opener);
            bool backOk = backing != null
                && backing.publicVariables.TryGetVariableValue("vrOnly", out object bv) && bv is GameObject[] bva && bva.Length == 1 && vrT != null && bva[0] == vrT.gameObject
                && backing.publicVariables.TryGetVariableValue("flatOnly", out object bf) && bf is GameObject[] bfa && bfa.Length == 1 && flatT != null && bfa[0] == flatT.gameObject;
            Ok(backOk, n + "/" + PlateName + ": backing Udon holds the same arrays (CopyProxyToUdon)");
            Ok(backing != null && backing.SyncMethod == VRC.SDKBase.Networking.SyncType.None, n + "/" + PlateName + ": KH_MenuOpener sync None");

            // facing and the reading position (WARN: the user may have turned the station)
            Vector3 front = -canvasT.forward; front.y = 0f; front.Normalize();
            var target = TargetOf(spec);
            if (target.HasValue)
            {
                Vector3 to = target.Value - canvasT.position; to.y = 0f;
                float dot = to.sqrMagnitude > 1e-4f ? Vector3.Dot(front, to.normalized) : 0f;
                Warn(dot >= 0.5f, n + ": strip faces its readers (" + spec.target + " " + V(target.Value) + "): dot " + dot.ToString("F2") + " >= 0.5");
            }
            else Warn(false, n + ": the " + spec.target + " anchor was not found (facing not checked)");
            Vector3 read = canvasT.position + front * 1.2f;
            float g = Ground(read);
            Warn(!float.IsNaN(g) && Mathf.Abs(g - ground) <= 0.4f, n + ": reading position 1.2 m in front " + V(read) + ", ground " + (float.IsNaN(g) ? "none" : (g - ground).ToString("+0.00;-0.00") + " m") + " vs the station's (<= 0.4)");
            if (!float.IsNaN(g))
            {
                var hits = Physics.OverlapCapsule(new Vector3(read.x, g + 0.5f, read.z), new Vector3(read.x, g + 1.5f, read.z), 0.25f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => !(h is TerrainCollider) && !h.transform.IsChildOf(st)).Select(h => KD_SceneObjects.PathOf(h.gameObject)).ToArray();
                Warn(hits.Length == 0, n + ": nothing solid at the reading position" + (hits.Length > 0 ? " (" + string.Join(", ", hits.Take(4)) + ")" : ""));
            }
            v.notes.Add(n + ": canvas centre " + V(canvasT.position) + ", faces yaw " + Quaternion.LookRotation(front).eulerAngles.y.ToString("F1") + ", reading position " + V(read));
        }

        /// <summary>Each language's text at the floor size (14) inside the label's width: ja / en (glyphs baked) FAIL, the other 11 WARN
        /// (drawn by VRChat's fallback fonts; measured with the shared font, an estimate).</summary>
        static void CheckFit(Ctx c, TextMeshProUGUI probe, TextMeshProUGUI label, string key, string what, Action<bool, string> Ok, Action<bool, string> Warn, VerifyReport v)
        {
            float width = ((RectTransform)label.transform).sizeDelta.x;
            probe.font = c.font; probe.enableAutoSizing = false; probe.fontSize = MinPt; probe.enableWordWrapping = false;
            var over = new List<string>(); var overBaked = new List<string>();
            float jaFill = 0f;
            foreach (var lang in c.catalog.languages)
            {
                string text = KD_LocalizationEditor.Text(c.catalog, lang.code, key);
                float w = probe.GetPreferredValues(text).x;
                if (lang.code == "ja")
                {
                    probe.fontSize = label.fontSizeMax;
                    jaFill = probe.GetPreferredValues(text).x / width;
                    probe.fontSize = MinPt;
                }
                if (w <= width) continue;
                (KD_LocalizationEditor.Bundled(lang) ? overBaked : over).Add(lang.code + " " + w.ToString("F0"));
            }
            Ok(overBaked.Count == 0, what + " (" + key + "): ja / en fit " + width + " px at 14 pt" + (overBaked.Count > 0 ? " — over: " + string.Join(", ", overBaked) : ""));
            Warn(over.Count == 0, what + " (" + key + "): 11 client languages fit at 14 pt (estimate)" + (over.Count > 0 ? " — over: " + string.Join(", ", over) : ""));
            v.notes.Add(what + ": ja at " + label.fontSizeMax + " pt fills " + (jaFill * 100f).ToString("F0") + " % of " + width + " px" + (jaFill > 1f ? " (autosizes down)" : ""));
        }

        static void CheckImporter(Action<bool, string> Ok)
        {
            var ti = AssetImporter.GetAtPath(ArtPath) as TextureImporter;
            Ok(ti != null, "art importer");
            if (ti == null) return;
            Ok(ti.sRGBTexture && ti.alphaIsTransparency && !ti.isReadable && ti.mipmapEnabled && ti.wrapMode == TextureWrapMode.Clamp && ti.npotScale == TextureImporterNPOTScale.None && ti.maxTextureSize == ArtMaxSize,
                "art import: sRGB, alpha transparency, Read/Write off, mips, clamp, NPOT none, max " + ArtMaxSize);
            foreach (var (platform, format) in PlatformFormats)
            {
                var ps = ti.GetPlatformTextureSettings(platform);
                Ok(ps.overridden && ps.format == format && ps.maxTextureSize == ArtMaxSize, "art import " + platform + ": " + format);
            }
        }

        /// <summary>The PNG on disk is the code's current drawing (else Guide/10 was not run after a change).</summary>
        static void CheckArtCurrent(Action<bool, string> Warn)
        {
            if (!File.Exists(ArtPath)) { Warn(false, "art file missing"); return; }
            var rgba = KH_WalkGuideArt.RenderBytes();
            var tex = new Texture2D(KH_WalkGuideArt.Width, KH_WalkGuideArt.Height, TextureFormat.RGBA32, false, false);
            try
            {
                var px = new Color32[KH_WalkGuideArt.Width * KH_WalkGuideArt.Height];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(rgba[4 * i], rgba[4 * i + 1], rgba[4 * i + 2], rgba[4 * i + 3]);
                tex.SetPixels32(px); tex.Apply(false, false);
                Warn(File.ReadAllBytes(ArtPath).SequenceEqual(tex.EncodeToPNG()), "art file is the code's drawing v" + WalkArtVersion + " (else run Guide/10)");
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }
    }
}
