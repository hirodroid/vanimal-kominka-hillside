using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 §17 (2026-10-08): the pattern board and the checks of the pattern table.
    ///   Video/1 (BuildMonitor) builds the board: a second world-space canvas "PatternPanel" (500 × 880 px, 1 px = 1 mm) to the right of the
    ///   monitor's canvas under Monitor (layer 17 like the rest, hidden with it), its back plate, one row per pattern (the name button, the
    ///   underline shown while it runs, the note), [とめる] and the state line. KH_VideoCamera.patternImages / patternLines / patternLabel.
    ///   Video/2 (VerifyPatterns) checks the table (ranges inside the table and contiguous, modes &lt; ModeCountAll, arguments, shortest ≤ longest,
    ///   the conditions and the modes that raise them), the board's wiring, that おかえり (ModeWelcome) has no button, and the source rules.
    /// </summary>
    public static partial class KH_VideoCameraEditor
    {
        public const string PatternPanelName = "PatternPanel", PatternBackName = "PatternBack", PatternStopName = "PatternStop", PatternStateName = "PatternState";
        public static readonly Vector2 PatCanvasPx = new Vector2(500f, 880f);
        /// <summary>The board's centre: right of the monitor canvas (±420 px) with a 30 px gap.</summary>
        public const float PatOffsetPxX = 700f;
        public static readonly Vector2 PatBackM = new Vector2(0.52f, 0.90f);
        public static readonly Vector2 PatTitlePos = new Vector2(-20f, 412f), PatTitleSize = new Vector2(420f, 44f);
        public static readonly Vector2 PatButtonSize = new Vector2(200f, 64f), PatNoteSize = new Vector2(262f, 84f), PatLineSize = new Vector2(200f, 6f);
        public const float PatButtonX = -135f, PatNoteX = 110f, PatRow0Y = 330f, PatRowPitch = 100f;
        public static readonly Vector2 PatStopPos = new Vector2(-135f, -275f), PatStopSize = new Vector2(200f, 56f);
        public static readonly Vector2 PatStatePos = new Vector2(0f, -372f), PatStateSize = new Vector2(470f, 100f);
        public static float PatRowY(int p) { return PatRow0Y - p * PatRowPitch; }
        public static string PatternButtonName(int p) { return "Pattern" + p; }
        public static string PatternEvent(int p) { return "_Pattern" + p; }

        // §17 trial values (Video/1 writes them through ModeTuning)
        public const float WelcomeFov = 55f, WelcomeDist = 1.6f, WelcomeSide = 0.8f, WelcomeHeight = 0.45f, WelcomeLookTau = 0.1f;
        public const float PatWashNear = 4f, PatWashSpotMax = 6f, PatDogNear = 15f;

        /// <summary>§17 every string of the board (the glyphs are baked from these).</summary>
        public static string PatternWords()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(KH_VideoCamera.WordPatternTitle).Append(KH_VideoCamera.WordPatternStop).Append(KH_VideoCamera.WordPatternIdle).Append(KH_VideoCamera.WordPatternRetry)
              .Append(KH_VideoCamera.WordModeWelcome).Append(KH_VideoCamera.WordSceneFrisbee).Append(KH_VideoCamera.WordSceneWash).Append(KH_VideoCamera.WordSceneWalk)
              .Append(KH_VideoCamera.WordSceneIdle).Append("/…()");
            for (int p = 0; p < KH_VideoCamera.PatternCount; p++) sb.Append(KH_VideoCamera.PatternName(p)).Append(KH_VideoCamera.PatternNote(p));
            return sb.ToString();
        }

        /// <summary>Video/1: the pattern board beside the monitor canvas (built in place; names are the contract with Verify).</summary>
        static void BuildPatternBoard(Transform mon, Ctx c, UdonBehaviour target, Ui u, List<string> log)
        {
            var pt = mon.Find(PatternPanelName);
            GameObject go;
            if (pt == null) { go = new GameObject(PatternPanelName, typeof(RectTransform)); go.transform.SetParent(mon, false); } else go = pt.gameObject;
            go.layer = MonitorLayer;
            var canvas = go.GetComponent<Canvas>(); if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = false;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            foreach (var cs in go.GetComponents<CanvasScaler>()) UnityEngine.Object.DestroyImmediate(cs);
            if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
            if (go.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null) go.AddComponent<VRC.SDK3.Components.VRCUiShape>();
            var box = go.GetComponent<BoxCollider>(); if (box == null) box = go.AddComponent<BoxCollider>();
            box.isTrigger = true; box.center = Vector3.zero; box.size = new Vector3(PatCanvasPx.x, PatCanvasPx.y, 2f);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = PatCanvasPx;
            rt.localPosition = new Vector3(PatOffsetPxX * Px, 0f, 0f); rt.localRotation = Quaternion.identity; rt.localScale = Vector3.one * Px;

            var frame = KD_ShopSetupEditor.EnsureUiChild(go.transform, "Frame", Vector2.zero, PatCanvasPx);
            foreach (var g in frame.GetComponents<Graphic>()) if (!(g is Image)) UnityEngine.Object.DestroyImmediate(g);
            var fimg = frame.GetComponent<Image>(); if (fimg == null) fimg = frame.AddComponent<Image>();
            fimg.color = FrameColor; fimg.raycastTarget = false; KD_UiSpriteEditor.ApplyFlatSkin(fimg);
            frame.transform.SetAsFirstSibling();

            Label(go.transform, "Title", PatTitlePos, PatTitleSize, Ja(KH_VideoCamera.KeyPatternTitle), c.font, 28f, 18f, TextAlignmentOptions.MidlineLeft, false).color = KD_UiSpriteEditor.AccentGold;
            int n = KH_VideoCamera.PatternCount;
            u.patternImages = new Image[n]; u.patternLines = new GameObject[n];
            for (int p = 0; p < n; p++)
            {
                float y = PatRowY(p);
                var b = Btn(go.transform, PatternButtonName(p), new Vector2(PatButtonX, y), PatButtonSize, Ja(KH_VideoCamera.PatternNameKey(p)), c.font, target, PatternEvent(p), log);
                u.patternImages[p] = b.GetComponent<Image>();
                var line = KD_ShopSetupEditor.EnsureUiChild(go.transform, "PatternLine" + p, new Vector2(PatButtonX, y - PatButtonSize.y / 2f - 5f), PatLineSize);
                foreach (var g in line.GetComponents<Graphic>()) if (!(g is Image)) UnityEngine.Object.DestroyImmediate(g);
                var li = line.GetComponent<Image>(); if (li == null) li = line.AddComponent<Image>();
                li.color = KD_UiSpriteEditor.AccentGold; li.raycastTarget = false;
                line.SetActive(false);
                u.patternLines[p] = line;
                Label(go.transform, "PatternNote" + p, new Vector2(PatNoteX, y), PatNoteSize, Ja(KH_VideoCamera.PatternNoteKey(p)), c.font, 16f, 11f, TextAlignmentOptions.MidlineLeft, true);
            }
            Btn(go.transform, PatternStopName, PatStopPos, PatStopSize, Ja(KH_VideoCamera.KeyPatternStop), c.font, target, nameof(KH_VideoCamera._PatternStop), log);
            u.patternLabel = Label(go.transform, PatternStateName, PatStatePos, PatStateSize, Ja(KH_VideoCamera.KeyPatternIdle), c.font, 18f, 11f, TextAlignmentOptions.TopLeft, true);

            // the back plate (the monitor's covers only its own canvas)
            var mat = EnsureMat(BackMatPath, BackShaderName, log);
            var bt = mon.Find(PatternBackName);
            GameObject back;
            if (bt == null) { back = GameObject.CreatePrimitive(PrimitiveType.Quad); back.name = PatternBackName; back.transform.SetParent(mon, false); } else back = bt.gameObject;
            foreach (var col in back.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            back.layer = MonitorLayer;
            back.transform.localPosition = new Vector3(PatOffsetPxX * Px, 0f, BackBehindM);
            back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            back.transform.localScale = new Vector3(PatBackM.x, PatBackM.y, 1f);
            var mr = back.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            EditorUtility.SetDirty(go); EditorUtility.SetDirty(back);
            log.Add("pattern board: " + PatternPanelName + " " + PatCanvasPx + " px at x " + PatOffsetPxX * Px + " m, " + n + " patterns + " + PatternStopName + " + state, back plate " + PatBackM + " m");
        }

        // ================================================================ Video/2

        /// <summary>§17: the table, the board, おかえり without a button, the source rules.</summary>
        static void VerifyPatterns(KH_VideoCamera v, Action<bool, string> Check, Action<string> Warn)
        {
            // ---- the table ----
            float[] t = KH_VideoCamera.CutTable();
            int[] st = KH_VideoCamera.PatternSeqStarts(), ln = KH_VideoCamera.PatternSeqLengths();
            int cols = KH_VideoCamera.PatCols;
            Check(t != null && t.Length > 0 && t.Length % cols == 0, "§17 cut table: " + (t != null ? t.Length : 0) + " floats = rows × " + cols);
            int rows = t != null ? t.Length / cols : 0;
            Check(st != null && ln != null && st.Length == KH_VideoCamera.SeqCount && ln.Length == KH_VideoCamera.SeqCount, "§17 " + KH_VideoCamera.SeqCount + " flows (starts / lengths)");
            if (t == null || st == null || ln == null || st.Length != ln.Length) return;
            int expect = 0;
            for (int s = 0; s < st.Length; s++)
            {
                Check(st[s] == expect && ln[s] >= 1 && st[s] + ln[s] <= rows, "§17 flow " + s + ": rows " + st[s] + ".." + (st[s] + ln[s] - 1) + " inside the table, right after the previous flow");
                expect = st[s] + ln[s];
            }
            Check(expect == rows, "§17 the flows cover the " + rows + " rows exactly (" + expect + ")");
            int bad = 0;
            for (int r = 0; r < rows; r++)
            {
                float fm = t[r * cols + KH_VideoCamera.ColMode], fa = t[r * cols + KH_VideoCamera.ColArg], mn = t[r * cols + KH_VideoCamera.ColMin], mx = t[r * cols + KH_VideoCamera.ColMax];
                float fd = t[r * cols + KH_VideoCamera.ColAdv], af = t[r * cols + KH_VideoCamera.ColAfter];
                int m = Mathf.RoundToInt(fm), a = Mathf.RoundToInt(fa), adv = Mathf.RoundToInt(fd);
                var why = new List<string>();
                if (Mathf.Abs(fm - m) > 1e-4f || Mathf.Abs(fa - a) > 1e-4f || Mathf.Abs(fd - adv) > 1e-4f) why.Add("an integer column is not an integer");
                if (m < 0 || m >= KH_VideoCamera.ModeCountAll) why.Add("mode " + m + " not < ModeCountAll " + KH_VideoCamera.ModeCountAll);
                if (m == KH_VideoCamera.ModePath) { if (a < 0 || a >= KH_VideoCamera.PathMoveCount) why.Add("みち needs a move 0.." + (KH_VideoCamera.PathMoveCount - 1)); }
                else if (m == KH_VideoCamera.ModeOrbit) { if (a < 0 || a >= KH_VideoCamera.OrbitKindCount) why.Add("一周 needs a way 0.." + (KH_VideoCamera.OrbitKindCount - 1)); }
                else if (a == KH_VideoCamera.ArgSpotWash) { if (m != KH_VideoCamera.ModeSpot) why.Add("ArgSpotWash only on 定点"); }
                else if (a == KH_VideoCamera.ArgDogNear) { if (KH_VideoCamera.PickerKind(m) != 1) why.Add("ArgDogNear only on a dog mode"); }
                else if (a != KH_VideoCamera.ArgAuto) why.Add("argument " + a + " unknown for mode " + m);
                if (!(mn >= 0f) || !(mx > 0f) || mn > mx) why.Add("shortest " + mn + " / longest " + mx);
                if (adv < 0 || adv >= KH_VideoCamera.AdvCount) why.Add("condition " + adv + " not < AdvCount " + KH_VideoCamera.AdvCount);
                if (!(af >= 0f) || af > mx) why.Add("delay " + af);
                // an event condition only on the mode that raises it (otherwise the row could only end by its longest time)
                if (adv == KH_VideoCamera.AdvCatch && m != KH_VideoCamera.ModeCatchWait) why.Add("ShotCatch comes only from キャッチ待ち");
                if (adv == KH_VideoCamera.AdvPassed && m != KH_VideoCamera.ModePassBy) why.Add("ShotPassed comes only from 通り過ぎ");
                if (adv == KH_VideoCamera.AdvMoveDone && !(m == KH_VideoCamera.ModePath && (a == KH_VideoCamera.PathWaitApproach || a == KH_VideoCamera.PathOvertake || a == KH_VideoCamera.PathPullAway)))
                    why.Add("ShotMoveDone comes only from みち 待って寄る / 追いこし / 寄って引く");
                if (adv == KH_VideoCamera.AdvOrbitEnd && !(m == KH_VideoCamera.ModeOrbit && a != KH_VideoCamera.OrbitSteady)) why.Add("ShotOrbitEnd comes only from 一周 近づく / 離れる");
                if (why.Count > 0) { bad++; Check(false, "§17 row " + r + " (" + KH_VideoCamera.ModeWord(m) + "): " + string.Join("; ", why)); }
            }
            if (bad == 0) Check(true, "§17 every one of the " + rows + " rows: mode < ModeCountAll, its argument, shortest ≤ longest, a known condition raised by that mode, delay ≤ longest");
            // ---- the board's patterns and おまかせ's scenes ----
            var names = Enumerable.Range(0, KH_VideoCamera.PatternCount).Select(KH_VideoCamera.PatternName).ToArray();
            Check(names.All(x => !string.IsNullOrEmpty(x) && x != "?") && names.Distinct().Count() == names.Length
                && Enumerable.Range(0, KH_VideoCamera.PatternCount).All(p => KH_VideoCamera.PatternNote(p).Length > 0), "§17 " + names.Length + " patterns with their own names and notes: " + string.Join(" / ", names));
            Check(Enumerable.Range(0, KH_VideoCamera.PatternCount).All(p => p == KH_VideoCamera.PatternOmakase ? KH_VideoCamera.PatternSeq(p) == -1 : KH_VideoCamera.PatternSeq(p) >= 0 && KH_VideoCamera.PatternSeq(p) < KH_VideoCamera.SeqCount),
                "§17 every pattern has a flow (おまかせ = by scene)");
            Check(Enumerable.Range(0, KH_VideoCamera.SceneCount).All(s => KH_VideoCamera.SceneSeq(s) >= 0 && KH_VideoCamera.SceneSeq(s) < KH_VideoCamera.SeqCount && KH_VideoCamera.SceneWord(s).Length > 0),
                "§17 every おまかせ scene has a flow and a word");
            Check(KH_VideoCamera.ShotOfAdv(KH_VideoCamera.AdvCatch) == KH_VideoCamera.ShotCatch && KH_VideoCamera.ShotOfAdv(KH_VideoCamera.AdvPassed) == KH_VideoCamera.ShotPassed
                && KH_VideoCamera.ShotOfAdv(KH_VideoCamera.AdvMoveDone) == KH_VideoCamera.ShotMoveDone && KH_VideoCamera.ShotOfAdv(KH_VideoCamera.AdvOrbitEnd) == KH_VideoCamera.ShotOrbitEnd
                && KH_VideoCamera.ShotOfAdv(KH_VideoCamera.AdvTime) == KH_VideoCamera.ShotNone, "§17 the event conditions map to their shot events");
            // ---- おかえり: internal only ----
            Check(KH_VideoCamera.ModeWelcome == KH_VideoCamera.ModeCount && KH_VideoCamera.ModeCountAll == KH_VideoCamera.ModeCount + 1 && KH_VideoCamera.ModeWord(KH_VideoCamera.ModeWelcome) == "おかえり",
                "§17 おかえり is the internal mode " + KH_VideoCamera.ModeWelcome + " (ModeCount " + KH_VideoCamera.ModeCount + " buttons, ModeCountAll " + KH_VideoCamera.ModeCountAll + ")");
            var buttons = v.monitorRoot != null ? v.monitorRoot.GetComponentsInChildren<Button>(true) : new Button[0];
            var sent = new List<string>();
            foreach (var b in buttons)
            {
                var so = new SerializedObject(b);
                var arg = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_StringArgument");
                if (arg != null) sent.Add(arg.stringValue);
            }
            Check(!sent.Contains(ModeEvent(KH_VideoCamera.ModeWelcome)) && buttons.All(b => { var l = LabelOf(b.gameObject); return l == null || l.text != KH_VideoCamera.WordModeWelcome; }),
                "§17 no button on the monitor sends " + ModeEvent(KH_VideoCamera.ModeWelcome) + " or reads おかえり (" + buttons.Length + " buttons)");
            // ---- the board ----
            var vb = UdonSharpEditorUtility.GetBackingUdonBehaviour(v);
            var board = v.monitorRoot != null ? v.monitorRoot.transform.Find(PatternPanelName) : null;
            var canvas = board != null ? board.GetComponent<Canvas>() : null;
            Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace && board.GetComponent<GraphicRaycaster>() != null && board.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null
                && board.GetComponent<BoxCollider>() != null && board.GetComponent<BoxCollider>().isTrigger, "§17 " + PatternPanelName + ": world-space canvas + GraphicRaycaster + VRCUiShape + trigger collider under the monitor");
            if (board == null) return;
            var brt = (RectTransform)board;
            Check(brt.sizeDelta == PatCanvasPx && Mathf.Abs(brt.localPosition.x - PatOffsetPxX * Px) < 1e-4f && Mathf.Abs(brt.localScale.x - Px) < 1e-6f
                && PatOffsetPxX - PatCanvasPx.x / 2f >= CanvasPx.x / 2f, "§17 the board (" + PatCanvasPx + " px) sits right of the monitor canvas without overlapping it");
            var back = v.monitorRoot.transform.Find(PatternBackName);
            Check(back != null && back.GetComponent<Collider>() == null && back.localPosition.z > 0.001f && Vector3.Dot(back.forward, v.monitorRoot.transform.forward) < -0.99f,
                "§17 the board's back plate sits behind it and faces away");
            int n = KH_VideoCamera.PatternCount;
            Check(v.patternImages != null && v.patternImages.Length == n && v.patternLines != null && v.patternLines.Length == n && v.patternLabel != null,
                "§17 patternImages / patternLines (" + n + ") and patternLabel wired");
            for (int p = 0; p < n; p++)
            {
                var b = board.Find(PatternButtonName(p));
                CheckEvt(Check, b != null ? b.gameObject : null, vb, PatternEvent(p), "§17 pattern " + p);
                var lbl = b != null ? LabelOf(b.gameObject) : null;
                var rt = b as RectTransform;
                Check(lbl != null && lbl.text == Ja(KH_VideoCamera.PatternNameKey(p)) && rt != null && rt.sizeDelta.x >= 120f && rt.sizeDelta.y >= 50f
                    && v.patternImages != null && p < v.patternImages.Length && v.patternImages[p] == b.GetComponent<Image>(), "§17 pattern " + p + " '" + (lbl != null ? lbl.text : "") + "' ≥ 120 × 50 px, image wired");
                var line = board.Find("PatternLine" + p);
                Check(line != null && !line.gameObject.activeSelf && v.patternLines != null && p < v.patternLines.Length && v.patternLines[p] == line.gameObject, "§17 pattern " + p + " underline wired, hidden in the scene");
                var note = board.Find("PatternNote" + p);
                var nt = note != null ? note.GetComponent<TextMeshProUGUI>() : null;
                Check(nt != null && nt.text == Ja(KH_VideoCamera.PatternNoteKey(p)), "§17 pattern " + p + " note (the catalog's ja; W5 rewrites it at runtime)");
            }
            var stop = board.Find(PatternStopName);
            CheckEvt(Check, stop != null ? stop.gameObject : null, vb, "_PatternStop", "§17 [とめる]");
            var state = board.Find(PatternStateName);
            Check(state != null && v.patternLabel == state.GetComponent<TextMeshProUGUI>(), "§17 the state line is patternLabel");
            if (vb != null && !vb.publicVariables.TryGetVariableValue("patternLabel", out object _)) Warn("§17 'patternLabel' not in the backing heap yet (a saved / built scene carries it; rerun Video/2 after a reload)");
            // ---- source rules ----
            string pat = File.Exists(ProgramPath + ".Patterns.cs") ? File.ReadAllText(ProgramPath + ".Patterns.cs") : "";
            Check(pat.Length > 0, "§17 KH_VideoCamera.Patterns.cs is there");
            Check(!pat.Contains("TryPersonPose") && !pat.Contains("PersonFollow") && !pat.Contains("_FilmRequest") && !pat.Contains("PersonSet(") && !pat.Contains("GetPlayerById"),
                "§17 the pattern player never reads / asks another person");
            var onShot = Regex.Match(pat, @"void PatternOnShot\(int kind\)\s*\{[^}]*\}");
            Check(onShot.Success && !onShot.Value.Contains("ShotApply") && !onShot.Value.Contains("PatternStep") && onShot.Value.Contains("_patStepNow = true"),
                "§17 a shot event only latches (no switch inside the mode function); the step runs on the next frame");
            string all = string.Concat(Directory.GetFiles("Assets/KominkaHillside/Scripts", "KH_VideoCamera*.cs").Where(f => !f.EndsWith(".meta")).Select(File.ReadAllText));
            Check(Regex.IsMatch(all, @"shotListener\.SendCustomEvent\(""_OnVideoShot""\);\s*PatternOnShot\(kind\);"), "§17 RaiseShot still tells shotListener, then the pattern");
            Check(Regex.IsMatch(all, @"case ModeSelfFollow: return _person < 0 \|\| _patRun \? SelfFollow\(dt\) : PersonFollow\(dt\);"), "§17 a pattern's じぶん is always me");
            Check(Regex.IsMatch(all, @"case ModeWelcome: return Welcome\(dt\);"), "§17 the switch runs おかえり");
            Check(Regex.IsMatch(all, @"void ShotApply\(int mode, int dog, int spot\)\s*\{\s*if \(!_open \|\| mode < 0 \|\| mode >= ModeCountAll\) return;"), "§17 the shot entry takes modes < ModeCountAll");
            Check(Regex.IsMatch(all, @"void SetMode\(int mode\)\s*\{\s*if \(_patRun\) PatternStop\(\);") && Regex.IsMatch(all, @"void StepPick\(int step\)\s*\{\s*if \(!_open\) return;\s*if \(_patRun\) PatternStop\(\);")
                && Regex.IsMatch(all, @"if \(_patRun\) PatternStop\(\);   // §17: × stops"), "§17 a mode button, ◀ ▶ and × stop the pattern");
        }
    }
}
