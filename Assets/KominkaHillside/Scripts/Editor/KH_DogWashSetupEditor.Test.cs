using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // IMP-DOG-025 / 030 builder: Edit-mode looks at the shell (contract §8), on the dog chosen with Wash/18 (default the Shiba;
    // images are Temp/KH_WashShot_{dog}_{scene}_{view}.png, masks Temp/KH_WashMask_{dog}_{scene}.png). Nothing is saved: the Blit material is a
    // temporary copy, the cell texture is temporary, the shell gets its arrays through a MaterialPropertyBlock, and the
    // shell's `enabled` is set without Undo / SetDirty (the scene is not marked dirty; if the scene is saved before 14,
    // Wash/12 reports the shell as enabled). Capture cameras are HideAndDontSave and destroyed after each shot.
    //
    // Strokes use the runtime amount: every Blit of a capsule a -> b (radius r, contact time sec) adds
    // ToolDelta(tool, Dwell(sec, a, b, r)) with Dwell = sec * 2r / (|b - a| + 2r) and at most MaxBlitSeconds per Blit,
    // the rates / radii / brushSoft / edgeNoise / rinse values read from the KH_DogWash proxy (KH_DogWash.DrawBrush).
    public static partial class KH_DogWashSetupEditor
    {
        const string TestMenu = Menu + "13 - Paint Test/";
        const string TestJsonPath = "Temp/KH_DogWashTestStroke.json";
        const float TestMaxBlitSeconds = 0.25f;   // KH_DogWash.MaxBlitSeconds
        static string lastScene = "none";

        [Serializable] class TestReport
        {
            public string utc, dog, scene, png, status;
            public int blits;
            public List<string> strokes = new List<string>();
            public List<string> notes = new List<string>();
        }

        static readonly string[] SceneNames = { "FoamThenRinse", "DirtOnly", "FoamLight", "FoamHeavy", "RinseBandOnFoam", "WetOnly", "CleanDogFoam" };

        [MenuItem(TestMenu + "1 Foam then Rinse")] public static void Test1() { PaintScene(1); }
        [MenuItem(TestMenu + "2 Dirt Only")] public static void Test2() { PaintScene(2); }
        [MenuItem(TestMenu + "3 Foam Light")] public static void Test3() { PaintScene(3); }
        [MenuItem(TestMenu + "4 Foam Heavy")] public static void Test4() { PaintScene(4); }
        [MenuItem(TestMenu + "5 Rinse Band on Foam")] public static void Test5() { PaintScene(5); }
        [MenuItem(TestMenu + "6 Wet Only")] public static void Test6() { PaintScene(6); }
        [MenuItem(TestMenu + "7 Clean Dog Foam")] public static void Test7() { PaintScene(7); }

        // ---- 18: 試し描きと撮影の対象の犬 (IMP-DOG-030)。SessionState に持つ (Editor を閉じるまで。既定は柴 = Targets[0]) ----
        const string SelectedDogKey = "KH_WashTest.SelectedDog";
        const string SelectMenu = Menu + "18 - Select Dog/";
        [MenuItem(SelectMenu + "Shiba")] public static void SelectShiba() { SelectDog("Shiba"); }
        [MenuItem(SelectMenu + "Corgi")] public static void SelectCorgi() { SelectDog("Corgi"); }
        [MenuItem(SelectMenu + "Pome")] public static void SelectPome() { SelectDog("Pome"); }
        [MenuItem(SelectMenu + "Lab")] public static void SelectLab() { SelectDog("Lab"); }
        [MenuItem(SelectMenu + "Dachs")] public static void SelectDachs() { SelectDog("Dachs"); }
        [MenuItem(SelectMenu + "Samoyed")] public static void SelectSamoyed() { SelectDog("Samoyed"); }
        [MenuItem(SelectMenu + "Shiba", true)] static bool SelectShibaCheck() { return SelectCheck("Shiba"); }
        [MenuItem(SelectMenu + "Corgi", true)] static bool SelectCorgiCheck() { return SelectCheck("Corgi"); }
        [MenuItem(SelectMenu + "Pome", true)] static bool SelectPomeCheck() { return SelectCheck("Pome"); }
        [MenuItem(SelectMenu + "Lab", true)] static bool SelectLabCheck() { return SelectCheck("Lab"); }
        [MenuItem(SelectMenu + "Dachs", true)] static bool SelectDachsCheck() { return SelectCheck("Dachs"); }
        [MenuItem(SelectMenu + "Samoyed", true)] static bool SelectSamoyedCheck() { return SelectCheck("Samoyed"); }

        static bool SelectCheck(string key) { UnityEditor.Menu.SetChecked(SelectMenu + key, SelectedTarget().key == key); return true; }

        /// <summary>試し描き・撮影の対象を選ぶ。前の犬の試し描きは消す (殻を切り、マスクを黒へ)。</summary>
        public static void SelectDog(string key)
        {
            if (!Targets.Any(t => t.key == key)) throw new ArgumentException("[KH Wash Test] unknown dog " + key + " (" + string.Join(", ", Targets.Select(t => t.key)) + ")");
            var prev = SelectedTarget().key;
            SessionState.SetString(SelectedDogKey, key);
            if (prev != key && !EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.GetActiveScene().path == KH_CourseGroundEditor.ScenePath) ClearTestStroke();
            Debug.Log("[KH Wash Test] selected dog " + key + " (13 paints, 15 / 16 capture this dog)");
        }

        static DogTarget SelectedTarget()
        {
            string key = SessionState.GetString(SelectedDogKey, Targets[0].key);
            return Targets.FirstOrDefault(t => t.key == key) ?? Targets[0];
        }

        [MenuItem(Menu + "14 - Clear Test Stroke (Edit)")]
        public static void ClearTestStroke()
        {
            GuardTest();
            int cleared = 0;
            foreach (var t in Targets)
            {
                var wash = FindWashOrNull(t.key);
                if (wash == null) continue;   // not built yet: nothing painted on it
                if (wash.shellRenderer != null) { wash.shellRenderer.SetPropertyBlock(null); wash.shellRenderer.enabled = false; }
                ClearRt(wash.maskA); ClearRt(wash.maskB);
                cleared++;
            }
            lastScene = "none";
            SceneView.RepaintAll();
            Debug.Log("[KH Wash Test] cleared " + cleared + " dogs (shell off, masks black). scene dirty = " + SceneManager.GetActiveScene().isDirty);
        }

        [MenuItem(Menu + "15 - Capture Selected Dog")]
        public static void CaptureSelected()
        {
            GuardTest();
            var t = SelectedTarget();
            var files = Capture(t, lastScene);
            Debug.Log("[KH Wash Shot] " + t.key + ": " + files.Count + " images (scene " + lastScene + "): " + string.Join(", ", files) + ". scene dirty = " + SceneManager.GetActiveScene().isDirty);
        }

        [MenuItem(Menu + "16 - Capture All Scenes")]
        public static void CaptureAll()
        {
            GuardTest();
            var t = SelectedTarget();
            var files = new List<string>();
            try
            {
                for (int s = 1; s <= SceneNames.Length; s++)
                {
                    PaintScene(s);
                    files.AddRange(Capture(t, lastScene));
                }
            }
            finally { ClearTestStroke(); }
            Debug.Log("[KH Wash Shot] " + t.key + ", all scenes: " + files.Count + " images in Temp/ (KH_WashShot_" + t.key + "_{scene}_{view}.png). scene dirty = " + SceneManager.GetActiveScene().isDirty);
        }

        // ================================================================ 場面

        class Painter
        {
            public KH_DogWash wash; public Material mat; public RenderTexture cur, next; public int blits;
            public Vector3 torso, fwd, up, side; public List<Vector3> flank;
            public TestReport r;
        }

        static void PaintScene(int scene)
        {
            GuardTest();
            var t = SelectedTarget();
            var wash = FindWash(t.key);
            if (wash.maskA == null || wash.maskB == null || wash.maskMaterial == null || wash.posMap == null || wash.shellRenderer == null || wash.cellValid == null || wash.cellValid.Length != CellCount)
                throw new InvalidOperationException(WashName(t.key) + " not built (run 11)");
            string name = SceneNames[scene - 1];
            var r = new TestReport { utc = DateTime.UtcNow.ToString("o"), dog = t.key, scene = name, png = "Temp/KH_WashMask_" + t.key + "_" + name + ".png" };
            var p = new Painter { wash = wash, r = r };
            p.mat = new Material(wash.maskMaterial) { hideFlags = HideFlags.HideAndDontSave };
            var cells = new Texture2D(CellGrid, CellGrid, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            try
            {
                bool dirty = scene != 6 && scene != 7;
                Restore(p, cells, dirty);
                Frame(p);
                float heavy = FoamDwell(p, 0.95f), light = FoamDwell(p, 0.3f);
                switch (scene)
                {
                    case 1:
                        foreach (float q in new[] { 0.3f, 0.55f, 0.8f }) FlankBand(p, q, KH_DogWash.ToolSponge, FoamDwell(p, 0.6f));
                        Diagonal(p, KH_DogWash.ToolShower, 0.5f);
                        break;
                    case 2: break;
                    case 3:
                        FlankBand(p, 0.35f, KH_DogWash.ToolSponge, FoamDwell(p, 0.2f));
                        FlankBand(p, 0.65f, KH_DogWash.ToolSponge, FoamDwell(p, 0.35f));
                        Back(p, KH_DogWash.ToolSponge, FoamDwell(p, 0.4f));
                        break;
                    case 4: case 5: case 7:
                        HeavyFoam(p, heavy);
                        if (scene == 5) RinseAcross(p, 0.9f);
                        break;
                    case 6:
                        float wet = 0.8f / Mathf.Max(0.01f, wash.showerWetRate);
                        foreach (float q in new[] { 0.3f, 0.6f, 0.85f }) FlankBand(p, q, KH_DogWash.ToolShower, wet);
                        Back(p, KH_DogWash.ToolShower, wet);
                        break;
                }
                if (scene == 3) r.notes.Add("light foam dwell " + light.ToString("F2") + " s at 0.3");
                if (p.cur != wash.maskA) Graphics.Blit(p.cur, wash.maskA);   // the shell reads A
                ShowShell(wash);
                SavePng(wash.maskA, r.png);
                r.blits = p.blits;
                r.status = "PAINTED";
                lastScene = name;
                SceneView.RepaintAll();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(p.mat);
                UnityEngine.Object.DestroyImmediate(cells);
            }
            WriteJson(TestJsonPath, r);
            Debug.Log("[KH Wash Test] " + name + ": " + r.strokes.Count + " strokes, " + r.blits + " blits -> " + r.png + " (scene dirty = " + SceneManager.GetActiveScene().isDirty + "). 15 captures it, 14 clears.");
        }

        /// <summary>Restore (パス 1): 実行時の初期値 (initialDirt × 高さの重み = KH_DogWash.InitialCells、値は proxy から) か、汚れ 0。</summary>
        static void Restore(Painter p, Texture2D cells, bool dirty)
        {
            var w = p.wash;
            var weights = RuntimeDirtWeights(w);
            var seed = InitialCellColors(w.cellValid, weights, dirty ? w.initialDirt : 0f);
            cells.SetPixels(seed); cells.Apply(false, false);
            p.mat.SetTexture("_PosMap", w.posMap);
            p.mat.SetTexture("_CellTex", cells);
            p.mat.SetFloatArray("_BoneToPart", Pad(w.boneToPart, BoneSlots));
            p.cur = w.maskA; p.next = w.maskB;
            Graphics.Blit(p.cur, p.next, p.mat, 1); p.blits++;
            Swap(ref p.cur, ref p.next);
            p.r.strokes.Add("restore " + (dirty ? "initialDirt " + w.initialDirt + " x height weight (full " + w.dirtHeightFull + " / mid " + w.dirtHeightMid + " / top " + w.dirtHeightTop + " m)" : "clean"));
        }

        /// <summary>体の向き (メッシュ座標): 前 = 腰 → 頭、上 = 足 → 胴、横 = その外積。横腹 = 胴のセルのうち横側の 1/4 より外。</summary>
        static void Frame(Painter p)
        {
            var w = p.wash;
            p.torso = CellMean(w, q => q == 0 || q == 1);
            Vector3 head = CellMean(w, q => q == 7), hip = CellMean(w, q => q == 0), legs = CellMean(w, q => q >= 3 && q <= 6);
            p.fwd = (head - hip).normalized;
            Vector3 up0 = p.torso - legs; p.up = (up0 - p.fwd * Vector3.Dot(up0, p.fwd)).normalized;
            p.side = Vector3.Cross(p.up, p.fwd).normalized;
            var torsoCells = PartCells(w, q => q == 0 || q == 1);
            float maxSide = torsoCells.Max(c => Vector3.Dot(c - p.torso, p.side));
            p.flank = torsoCells.Where(c => Vector3.Dot(c - p.torso, p.side) > 0.25f * maxSide).ToList();
            if (p.flank.Count < 4) throw new InvalidOperationException("too few flank cells (" + p.flank.Count + ")");
        }

        static float Along(Painter p, Vector3 c) { return Vector3.Dot(c - p.torso, p.fwd); }
        static float Height(Painter p, Vector3 c) { return Vector3.Dot(c - p.torso, p.up); }

        /// <summary>泡がこの量に届く接触時間 (1 点あたり)。シェーダーの伸びの鈍り (_FoamGrowEase) を含めて解く: f(t) = (1 - e^(-R E t)) / E。</summary>
        static float FoamDwell(Painter p, float target)
        {
            float rate = Mathf.Max(0.01f, p.wash.spongeFoamRate), ease = p.mat.HasProperty("_FoamGrowEase") ? p.mat.GetFloat("_FoamGrowEase") : 0f;
            if (ease < 1e-3f) return target / rate;
            return -Mathf.Log(Mathf.Max(1e-4f, 1f - ease * Mathf.Min(target, 0.99f / ease))) / (rate * ease);
        }

        /// <summary>1 本のストローク: a → b を半径ごとの区間に切り、各区間は 1 点あたり dwell 秒になる接触時間を持つ (実行時の Dwell の逆)。
        /// 1 回の Blit は <see cref="TestMaxBlitSeconds"/> まで (実行時と同じ)。部位は当たった部位 = part、許可はその隣接 (partNeighbors)。</summary>
        static void Stroke(Painter p, int tool, int part, Vector3 a, Vector3 b, float dwell, string label)
        {
            var w = p.wash;
            float radMeters = tool == KH_DogWash.ToolShower ? w.showerRadius : tool == KH_DogWash.ToolTowel ? w.towelRadius : w.spongeRadius;
            float r = radMeters / Mathf.Max(1e-4f, w.meshToWorldScale);
            var allow = new float[AllowSlots];
            int mask = w.partNeighbors != null && part < w.partNeighbors.Length ? w.partNeighbors[part] : 0;
            for (int i = 0; i < AllowSlots; i++) allow[i] = (i == part || (i < PartCount && ((mask >> i) & 1) != 0)) ? 1f : 0f;
            p.mat.SetFloatArray("_AllowPart", allow);
            p.mat.SetFloat("_BrushSoft", w.brushSoft);
            p.mat.SetFloat("_EdgeNoise", w.edgeNoise);
            p.mat.SetFloat("_RinseFoamBoost", w.rinseFoamBoost);
            p.mat.SetFloat("_FoamRinseGate", tool == KH_DogWash.ToolShower ? w.foamRinseGate : 0f);
            p.mat.SetFloat("_BrushRadius", r);
            int segs = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / r));
            for (int s = 0; s < segs; s++)
            {
                Vector3 sa = Vector3.Lerp(a, b, s / (float)segs), sb = Vector3.Lerp(a, b, (s + 1) / (float)segs);
                // contact time that gives `dwell` per point: sec = dwell * (|b - a| + 2r) / 2r
                float sec = dwell * ((sb - sa).magnitude + 2f * r) / (2f * r);
                while (sec > 1e-5f)
                {
                    float take = Mathf.Min(sec, TestMaxBlitSeconds);
                    sec -= take;
                    float d = take * 2f * r / ((sb - sa).magnitude + 2f * r);   // Dwell()
                    p.mat.SetVector("_BrushA", new Vector4(sa.x, sa.y, sa.z, 1f));
                    p.mat.SetVector("_BrushB", new Vector4(sb.x, sb.y, sb.z, 1f));
                    p.mat.SetVector("_Delta", ToolDeltaOf(w, tool, d));
                    Graphics.Blit(p.cur, p.next, p.mat, 0); p.blits++;
                    Swap(ref p.cur, ref p.next);
                }
            }
            p.r.strokes.Add(label + ": " + ToolLabels[tool] + " part " + PartNames[part] + " " + a.ToString("F4") + " -> " + b.ToString("F4") + " r " + radMeters.ToString("F3") + " m, dwell " + dwell.ToString("F2") + " s per point");
        }

        /// <summary>KH_DogWash.ToolDelta の写し (値は proxy から読む)。</summary>
        static Vector4 ToolDeltaOf(KH_DogWash w, int tool, float sec)
        {
            if (tool == KH_DogWash.ToolShower) return new Vector4(w.showerDirtRate * sec, w.showerFoamRate * sec, w.showerWetRate * sec, 0f);
            if (tool == KH_DogWash.ToolTowel) return new Vector4(w.towelDirtRate * sec, w.towelFoamRate * sec, w.towelWetRate * sec, 0f);
            return new Vector4(0f, w.spongeFoamRate * sec, w.spongeWetRate * sec, 0f);
        }

        /// <summary>横腹の帯 (高さの分位 q): 胸 → 腰。</summary>
        static void FlankBand(Painter p, float q, int tool, float dwell)
        {
            var hs = p.flank.Select(c => Height(p, c)).OrderBy(x => x).ToList();
            float h0 = hs[Mathf.Clamp(Mathf.RoundToInt(q * (hs.Count - 1)), 0, hs.Count - 1)];
            float band = (hs[hs.Count - 1] - hs[0]) * 0.18f + 1e-4f;
            var inBand = p.flank.Where(c => Mathf.Abs(Height(p, c) - h0) <= band).ToList();
            if (inBand.Count < 2) { p.r.notes.Add("flank band " + q + ": too few cells"); return; }
            Vector3 a = inBand.OrderByDescending(c => Along(p, c)).First(), b = inBand.OrderBy(c => Along(p, c)).First();
            Stroke(p, tool, 1, a, b, dwell, "flank " + q.ToString("F2"));
        }

        /// <summary>背中: 胴のセルのうち最も高い帯を胸 → 腰。</summary>
        static void Back(Painter p, int tool, float dwell)
        {
            var torso = PartCells(p.wash, q => q == 0 || q == 1);
            float top = torso.Max(c => Height(p, c)), bottom = torso.Min(c => Height(p, c));
            var high = torso.Where(c => Height(p, c) >= top - (top - bottom) * 0.2f).ToList();
            if (high.Count < 2) { p.r.notes.Add("back: too few cells"); return; }
            Stroke(p, tool, 1, high.OrderByDescending(c => Along(p, c)).First(), high.OrderBy(c => Along(p, c)).First(), dwell, "back");
        }

        /// <summary>部位の中で最も離れた 2 つのセルを結ぶストローク (首・足・尻尾)。</summary>
        static void PartLine(Painter p, int part, int tool, float dwell)
        {
            var cs = PartCells(p.wash, q => q == part);
            if (cs.Count < 2) { p.r.notes.Add(PartNames[part] + ": too few cells"); return; }
            Vector3 a = cs[0], b = cs[1]; float best = -1f;
            for (int i = 0; i < cs.Count; i++) for (int j = i + 1; j < cs.Count; j++) { float d = (cs[i] - cs[j]).sqrMagnitude; if (d > best) { best = d; a = cs[i]; b = cs[j]; } }
            Stroke(p, tool, part, a, b, dwell, PartNames[part]);
        }

        static void HeavyFoam(Painter p, float dwell)
        {
            foreach (float q in new[] { 0.2f, 0.45f, 0.7f, 0.9f }) FlankBand(p, q, KH_DogWash.ToolSponge, dwell);
            Back(p, KH_DogWash.ToolSponge, dwell);
            PartLine(p, 2, KH_DogWash.ToolSponge, dwell * 0.85f);
            PartLine(p, 3, KH_DogWash.ToolSponge, dwell);
            PartLine(p, 8, KH_DogWash.ToolSponge, dwell);
        }

        /// <summary>斜め (前上 → 後ろ下) の水。</summary>
        static void Diagonal(Painter p, int tool, float dwell)
        {
            Vector3 a = p.flank.OrderByDescending(c => Along(p, c) + Height(p, c)).First(), b = p.flank.OrderBy(c => Along(p, c) + Height(p, c)).First();
            Stroke(p, tool, 1, a, b, dwell, "diagonal");
        }

        /// <summary>横腹の中ほどを上から下へ横切る水 1 本 (ゆっくり: 1 点あたりの接触時間が長い)。</summary>
        static void RinseAcross(Painter p, float dwell)
        {
            var near = p.flank.Where(c => Mathf.Abs(Along(p, c)) <= p.flank.Max(x => Mathf.Abs(Along(p, x))) * 0.25f).ToList();
            if (near.Count < 2) near = p.flank;
            Stroke(p, KH_DogWash.ToolShower, 1, near.OrderByDescending(c => Height(p, c)).First(), near.OrderBy(c => Height(p, c)).First(), dwell, "rinse across");
        }

        static List<Vector3> PartCells(KH_DogWash w, Func<int, bool> sel)
        {
            return Enumerable.Range(0, CellCount).Where(c => w.cellValid[c] && sel(w.cellPart[c])).Select(c => w.cellCenter[c]).ToList();
        }

        static Vector3 CellMean(KH_DogWash w, Func<int, bool> sel)
        {
            var list = PartCells(w, sel);
            if (list.Count == 0) throw new InvalidOperationException("no valid cells for the test");
            return list.Aggregate(Vector3.zero, (a, b) => a + b) / list.Count;
        }

        /// <summary>殻: 配列とマスクを MaterialPropertyBlock で渡し (保存されない)、Undo / SetDirty なしで有効にする。</summary>
        static void ShowShell(KH_DogWash wash)
        {
            var shell = wash.shellRenderer;
            var mpb = new MaterialPropertyBlock();
            shell.GetPropertyBlock(mpb);
            mpb.SetTexture("_Mask", wash.maskA);
            mpb.SetFloatArray("_BoneToPart", Pad(wash.boneToPart, BoneSlots));
            mpb.SetFloatArray("_PartFoamLimit", Pad(wash.partFoamLimit, AllowSlots));
            shell.SetPropertyBlock(mpb);
            shell.enabled = true;
        }

        // ================================================================ 撮影

        /// <summary>胸と腰の骨の中点を中心に 6 構図 (横・斜め前・斜め後ろ・近接・上・顔の正面)、1024x768、Temp/KH_WashShot_{犬}_{場面}_{構図}.png。
        /// 距離は柴 (横 1.3 m、近接 0.7 m、顔 0.7 m) を犬の高さの比 (0.55〜1.3 倍) で伸び縮みさせる (小型犬が画面の中で小さくなりすぎないように)。</summary>
        static List<string> Capture(DogTarget target, string scene)
        {
            string key = target.key;
            float k = Mathf.Clamp(target.heightM / ShibaHeightM, 0.55f, 1.3f);
            int idx = Array.FindIndex(KD_DogDefs.All, x => x.key == key);
            var dog = KD_SceneObjects.Find(KD_DogDefs.All[idx].DogName);
            if (dog == null) throw new InvalidOperationException(KD_DogDefs.All[idx].DogName + " not found");
            var body = dog.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == KH_DogWashBakeEditor.BodyName);
            if (body == null) throw new InvalidOperationException("karada not found");
            Transform hipB = body.bones.FirstOrDefault(b => b != null && b.name == "Bone.001"), chestB = body.bones.FirstOrDefault(b => b != null && b.name == "Bone.002"), headB = body.bones.FirstOrDefault(b => b != null && b.name == "Bone.004");
            if (hipB == null || chestB == null || headB == null) throw new InvalidOperationException("Bone.001 / .002 / .004 not found");
            Vector3 c = (hipB.position + chestB.position) * 0.5f;
            Vector3 right = dog.transform.right, fwd = dog.transform.forward;
            float d = 1.3f * k;
            var files = new List<string>();
            void Shot(string view, Vector3 pos, Vector3 look)
            {
                string path = "Temp/KH_WashShot_" + key + "_" + scene + "_" + view + ".png";
                ShotOne(path, pos, look);
                files.Add(path);
            }
            Shot("side", c + right * d + Vector3.up * 0.15f * k, c);
            Shot("frontq", c + (right + fwd).normalized * d + Vector3.up * 0.25f * k, c);
            Shot("backq", c + (-right - fwd).normalized * d + Vector3.up * 0.35f * k, c);
            Shot("close", c + right * 0.7f * k + Vector3.up * 0.1f * k, c);
            Shot("top", c + Vector3.up * 1.2f * k + right * 0.15f * k, c);
            Vector3 face = headB.position;
            Shot("face", face + fwd * 0.7f * k + Vector3.up * 0.05f * k, face);
            return files;
        }

        static void ShotOne(string path, Vector3 pos, Vector3 look)
        {
            const int W = 1024, H = 768;
            var go = new GameObject("KH_WashShotCam") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.HideAndDontSave };
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            var prev = RenderTexture.active;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.position = pos;
                cam.transform.LookAt(look);
                cam.fieldOfView = 40f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 500f; cam.allowHDR = false;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply(false);
                cam.targetTexture = null;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = prev;
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ================================================================ 17

        [MenuItem(Menu + "17 - Reset Shell Material to Shader Defaults")]
        public static void ResetMaterials()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Wash Setup] Edit mode only");
            var log = new List<string>();
            foreach (var t in Targets)
            {
                string k = t.key;
                var maskMat = AssetDatabase.LoadAssetAtPath<Material>(MaskMatPath(k));
                var shellMat = AssetDatabase.LoadAssetAtPath<Material>(ShellMatPath(k));
                var maskA = AssetDatabase.LoadAssetAtPath<RenderTexture>(MaskRtPath(k, "A"));
                var cellTex = AssetDatabase.LoadAssetAtPath<Texture2D>(CellTexPath(k));
                var posMap = AssetDatabase.LoadAssetAtPath<Texture2D>(KH_DogWashBakeEditor.TexturePath(k));
                var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(KH_DogWashBakeEditor.BonesPath(k));
                if (maskMat == null || shellMat == null || maskA == null || cellTex == null || posMap == null || ta == null) throw new InvalidOperationException(k + ": wash assets missing (run 11)");
                float mts = JsonUtility.FromJson<KH_DogWashBakeEditor.PosMapBones>(ta.text).meshToWorldScale;
                log.Add(k + " mask: " + ResetToDefaults(maskMat, MaskBuilderProps));
                log.Add(k + " shell: " + ResetToDefaults(shellMat, ShellBuilderProps));
                ApplyBuilderMaterialProps(maskMat, shellMat, maskA, posMap, cellTex, mts, t.baseOffset);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[KH Wash Setup] materials reset to shader defaults (builder values re-applied):\n" + string.Join("\n", log));
        }

        /// <summary>シェーダーの Properties の既定値へ戻す (keep の名前は除く)。テクスチャは null (= シェーダーの既定のテクスチャ)。</summary>
        static string ResetToDefaults(Material m, string[] keep)
        {
            var sh = m.shader; var changed = new List<string>();
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                string n = sh.GetPropertyName(i);
                if (keep.Contains(n)) continue;
                switch (sh.GetPropertyType(i))
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                    {
                        float v = sh.GetPropertyDefaultFloatValue(i);
                        if (m.GetFloat(n) != v) { changed.Add(n + " " + m.GetFloat(n) + " -> " + v); m.SetFloat(n, v); }
                        break;
                    }
                    case ShaderPropertyType.Color:
                    {
                        Vector4 v = sh.GetPropertyDefaultVectorValue(i); Color c = new Color(v.x, v.y, v.z, v.w);
                        if (m.GetColor(n) != c) { changed.Add(n + " -> " + c); m.SetColor(n, c); }
                        break;
                    }
                    case ShaderPropertyType.Vector:
                    {
                        Vector4 v = sh.GetPropertyDefaultVectorValue(i);
                        if (m.GetVector(n) != v) { changed.Add(n + " -> " + v); m.SetVector(n, v); }
                        break;
                    }
                    case ShaderPropertyType.Texture:
                        if (m.GetTexture(n) != null) { changed.Add(n + " -> default"); m.SetTexture(n, null); }
                        break;
                }
            }
            EditorUtility.SetDirty(m);
            return changed.Count == 0 ? "no change" : string.Join(", ", changed);
        }

        // ================================================================ 部品

        static void GuardTest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Wash Test] Edit mode only");
            if (SceneManager.GetActiveScene().path != KH_CourseGroundEditor.ScenePath) throw new InvalidOperationException("[KH Wash Test] open " + KH_CourseGroundEditor.ScenePath);
        }

        static KH_DogWash FindWash(string key)
        {
            var w = FindWashOrNull(key);
            if (w == null) throw new InvalidOperationException(KD_Hillside.DogsRootName + "/" + WashName(key) + " missing (run 11)");
            return w;
        }

        static KH_DogWash FindWashOrNull(string key)
        {
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName);
            var node = dogs != null ? dogs.transform.Find(WashName(key)) : null;
            return node != null ? node.GetComponent<KH_DogWash>() : null;
        }

        static void Swap(ref RenderTexture a, ref RenderTexture b) { var t = a; a = b; b = t; }

        static float[] Pad(float[] src, int n)
        {
            var a = new float[n];
            if (src != null) for (int i = 0; i < n && i < src.Length; i++) a[i] = src[i];
            return a;
        }

        static void SavePng(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply(false);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally { RenderTexture.active = prev; UnityEngine.Object.DestroyImmediate(tex); }
        }

        static void ClearRt(RenderTexture rt)
        {
            if (rt == null) return;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
        }
    }
}
