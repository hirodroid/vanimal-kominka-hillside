using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // IMP-DOG-025 builder: an Edit-mode look at the shell (contract §8 "Editor での描画の確認"). Nothing is saved:
    // the Blit material is a temporary copy, the cell texture is temporary, the shell gets its arrays through a
    // MaterialPropertyBlock, and the shell's `enabled` is set without Undo / SetDirty (the scene is not marked dirty; if
    // the scene is saved before 14, Wash/12 reports the shell as enabled).
    public static partial class KH_DogWashSetupEditor
    {
        const string TestPngPath = "Temp/KH_WashMask_Shiba_test.png", TestJsonPath = "Temp/KH_DogWashTestStroke.json";

        [Serializable] class TestReport
        {
            public string utc, dog, png, status;
            public List<string> strokes = new List<string>();
            public List<string> notes = new List<string>();
        }

        [MenuItem(Menu + "13 - Paint Test Stroke (Edit)")]
        public static void PaintTestStroke()
        {
            GuardTest();
            var t = Targets[0];
            var wash = FindWash(t.key);
            if (wash.maskA == null || wash.maskB == null || wash.maskMaterial == null || wash.posMap == null || wash.shellRenderer == null || wash.cellValid == null || wash.cellValid.Length != CellCount)
                throw new InvalidOperationException(WashName(t.key) + " not built (run 11)");
            var r = new TestReport { utc = DateTime.UtcNow.ToString("o"), dog = t.key, png = TestPngPath };
            float mts = wash.meshToWorldScale;

            var mat = new Material(wash.maskMaterial) { hideFlags = HideFlags.HideAndDontSave };
            var cells = new Texture2D(CellGrid, CellGrid, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            try
            {
                // ---- Restore: the runtime seed (initialDirt x PartDirtFactor on the valid cells) ----
                var seed = new Color[CellCount];
                for (int c = 0; c < CellCount; c++)
                    seed[c] = new Color(wash.cellValid[c] ? Mathf.Clamp01(wash.initialDirt * PartDirtFactor(wash.cellPart[c])) : 0f, 0f, 0f, 1f);
                cells.SetPixels(seed); cells.Apply(false, false);
                mat.SetTexture("_PosMap", wash.posMap);
                mat.SetTexture("_CellTex", cells);
                mat.SetFloatArray("_BoneToPart", Pad(wash.boneToPart, BoneSlots));
                RenderTexture cur = wash.maskA, next = wash.maskB;
                Graphics.Blit(cur, next, mat, 1);
                Swap(ref cur, ref next);

                // ---- brush positions from the torso cells (mesh space) ----
                Vector3 Mean(Func<int, bool> sel)
                {
                    var list = Enumerable.Range(0, CellCount).Where(c => wash.cellValid[c] && sel(wash.cellPart[c])).Select(c => wash.cellCenter[c]).ToList();
                    if (list.Count == 0) throw new InvalidOperationException("no valid cells for the test");
                    return list.Aggregate(Vector3.zero, (a, b) => a + b) / list.Count;
                }
                Vector3 torso = Mean(p => p == 0 || p == 1), head = Mean(p => p == 7), hip = Mean(p => p == 0), legs = Mean(p => p >= 3 && p <= 6);
                Vector3 fwd = (head - hip).normalized;
                Vector3 up0 = torso - legs; Vector3 up = (up0 - fwd * Vector3.Dot(up0, fwd)).normalized;
                Vector3 side = Vector3.Cross(up, fwd).normalized;
                var torsoCells = Enumerable.Range(0, CellCount).Where(c => wash.cellValid[c] && (wash.cellPart[c] == 0 || wash.cellPart[c] == 1)).ToList();
                float maxSide = torsoCells.Max(c => Vector3.Dot(wash.cellCenter[c] - torso, side));
                var flank = torsoCells.Where(c => Vector3.Dot(wash.cellCenter[c] - torso, side) > 0.25f * maxSide).Select(c => wash.cellCenter[c]).ToList();
                if (flank.Count < 4) throw new InvalidOperationException("too few flank cells (" + flank.Count + ")");
                Func<Vector3, float> along = p => Vector3.Dot(p - torso, fwd), height = p => Vector3.Dot(p - torso, up);
                var hs = flank.Select(height).OrderBy(x => x).ToList();

                // allowed parts: chest + hip and their neighbours
                var allow = new float[AllowSlots];
                int mask = NeighborMask(0) | NeighborMask(1);
                for (int p = 0; p < PartCount; p++) allow[p] = ((mask >> p) & 1) != 0 ? 1f : 0f;
                mat.SetFloatArray("_AllowPart", allow);
                mat.SetFloat("_BrushSoft", 0.35f);
                mat.SetFloat("_EdgeNoise", 0.3f);

                // ---- foam: chest -> hip along the flank, three bands ----
                foreach (float q in new[] { 0.3f, 0.55f, 0.8f })
                {
                    float h0 = hs[Mathf.Clamp(Mathf.RoundToInt(q * (hs.Count - 1)), 0, hs.Count - 1)];
                    float band = (hs[hs.Count - 1] - hs[0]) * 0.18f + 1e-4f;
                    var inBand = flank.Where(p => Mathf.Abs(height(p) - h0) <= band).ToList();
                    if (inBand.Count < 2) { r.notes.Add("band " + q + ": too few cells"); continue; }
                    Vector3 a = inBand.OrderByDescending(along).First(), b = inBand.OrderBy(along).First();
                    Stroke(mat, ref cur, ref next, a, b, 0.09f / mts, new Vector4(0f, 0.75f, 0.15f, 0f), 0f, 0f);
                    r.strokes.Add("foam " + a.ToString("F4") + " -> " + b.ToString("F4") + " r " + (0.09f / mts).ToString("F4") + " (mesh)");
                }
                // ---- water: one diagonal (front-top -> back-bottom) ----
                {
                    Vector3 a = flank.OrderByDescending(p => along(p) + height(p)).First(), b = flank.OrderBy(p => along(p) + height(p)).First();
                    Stroke(mat, ref cur, ref next, a, b, 0.08f / mts, new Vector4(-0.3f, -0.96f, 0.72f, 0f), 2.2f, 1f);
                    r.strokes.Add("water " + a.ToString("F4") + " -> " + b.ToString("F4") + " r " + (0.08f / mts).ToString("F4") + " (mesh)");
                }
                if (cur != wash.maskA) Graphics.Blit(cur, wash.maskA);   // the shell reads A

                // ---- the shell: arrays and the mask through a property block (not saved), then shown ----
                var shell = wash.shellRenderer;
                var mpb = new MaterialPropertyBlock();
                shell.GetPropertyBlock(mpb);
                mpb.SetTexture("_Mask", wash.maskA);
                mpb.SetFloatArray("_BoneToPart", Pad(wash.boneToPart, BoneSlots));
                mpb.SetFloatArray("_PartFoamLimit", Pad(wash.partFoamLimit, AllowSlots));
                shell.SetPropertyBlock(mpb);
                shell.enabled = true;   // deliberately no Undo / SetDirty (see the class comment)
                r.notes.Add("shell enabled without marking the scene dirty; run 14 to turn it off. Do not save the scene in between");

                SavePng(wash.maskA, TestPngPath);
                r.status = "PAINTED";
                SceneView.RepaintAll();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mat);
                UnityEngine.Object.DestroyImmediate(cells);
            }
            WriteJson(TestJsonPath, r);
            Debug.Log("[KH Wash Test] painted " + r.strokes.Count + " strokes on " + t.key + " -> " + TestPngPath + " (scene dirty = " + SceneManager.GetActiveScene().isDirty + "). Run 14 to clear.");
        }

        [MenuItem(Menu + "14 - Clear Test Stroke (Edit)")]
        public static void ClearTestStroke()
        {
            GuardTest();
            foreach (var t in Targets)
            {
                var wash = FindWash(t.key);
                if (wash.shellRenderer != null) { wash.shellRenderer.SetPropertyBlock(null); wash.shellRenderer.enabled = false; }
                ClearRt(wash.maskA); ClearRt(wash.maskB);
            }
            SceneView.RepaintAll();
            Debug.Log("[KH Wash Test] cleared (shell off, masks black). scene dirty = " + SceneManager.GetActiveScene().isDirty);
        }

        static void GuardTest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Wash Test] Edit mode only");
            if (SceneManager.GetActiveScene().path != KH_CourseGroundEditor.ScenePath) throw new InvalidOperationException("[KH Wash Test] open " + KH_CourseGroundEditor.ScenePath);
        }

        static KH_DogWash FindWash(string key)
        {
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName);
            var node = dogs != null ? dogs.transform.Find(WashName(key)) : null;
            var w = node != null ? node.GetComponent<KH_DogWash>() : null;
            if (w == null) throw new InvalidOperationException(KD_Hillside.DogsRootName + "/" + WashName(key) + " missing (run 11)");
            return w;
        }

        static void Stroke(Material mat, ref RenderTexture cur, ref RenderTexture next, Vector3 a, Vector3 b, float radius, Vector4 delta, float boost, float gate)
        {
            mat.SetVector("_BrushA", new Vector4(a.x, a.y, a.z, 1f));
            mat.SetVector("_BrushB", new Vector4(b.x, b.y, b.z, 1f));
            mat.SetFloat("_BrushRadius", radius);
            mat.SetVector("_Delta", delta);
            mat.SetFloat("_RinseFoamBoost", boost);
            mat.SetFloat("_FoamRinseGate", gate);
            Graphics.Blit(cur, next, mat, 0);
            Swap(ref cur, ref next);
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
