using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-020 段階 7: フリスビーの直線移動と川の帯 (Codex レビュー 2026-09-27 の積み残し)。
    ///
    /// Course/89 は本番シーンの KH_FrisbeeCatch (6) と KH_Frisbee (6) の `riverData` / `waterSpot` を配線して保存し、そのあと
    /// Editor で決まる検査を行う (結果は Temp/imp-dog-020/frisbee-river.json):
    /// - 直線移動: 帯の外 (岸から 6 m) から対岸へ向けて 0.1 m ずつ進め、KH_FrisbeeCatch と同じ規則 (_BlocksStep、水遊び場の中は例外) で
    ///   帯へ入る前に止まるか (上流・中流・下流の 3 か所。橋と水遊び場は除く)。水遊び場の中の一歩は止めないか。
    /// - ディスク: 帯の中の点を、KH_Frisbee と同じ式で近い方の岸 (中心 ± (半幅 + 2 m)) へ移したとき、帯の外で、足元に NavMesh があるか。
    /// ClientSim のフリスビーハーネス (KH_FrisbeeHarnessEditor) は川の無い試作シーン専用なので、川へ投げる動的なケースは足していない
    /// (代わりにこの静的な検査。実際の挙動は PCVR で見る)。試作シーンでは配線が null のままで、挙動は変わらない。</summary>
    public static class KH_CourseFrisbeeRiverEditor
    {
        const string OutDir = "Temp/imp-dog-020";
        static readonly float[] ProbeZ = { 120f, 360f, 620f };   // 橋 (190 / 490) と水遊び場 (279〜291) から離した 3 か所

        [Serializable] class Report
        {
            public string utc, scene, status;
            public int catchersWired, discsWired, errors;
            public List<string> probes = new List<string>();
            public List<string> findings = new List<string>();
            public string limitations = "Static Editor geometry with the saved river data (the same rule as runtime). No ClientSim throw: the frisbee harness runs in the prototype scene, which has no river. Dog animation / give-up feel is PCVR.";
        }

        [MenuItem("Tools/KominkaHillside/Course/89 - Wire and Check Frisbee River Band")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new Report { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var river = UnityEngine.Object.FindObjectsOfType<KH_RiverData>(true).FirstOrDefault();
            var spot = UnityEngine.Object.FindObjectsOfType<KH_WaterSpot>(true).FirstOrDefault();
            if (river == null || !river._Ready()) throw new InvalidOperationException("KH_RiverData missing or empty: run Course/73 first");
            var catchers = UnityEngine.Object.FindObjectsOfType<KH_FrisbeeCatch>(true);
            var discs = UnityEngine.Object.FindObjectsOfType<KH_Frisbee>(true);
            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-frisbee-river.unity", true);
            foreach (var c in catchers) { Wire(c, river, spot); r.catchersWired++; }
            foreach (var d in discs) { Wire(d, river, spot); r.discsWired++; }
            EditorSceneManager.MarkSceneDirty(river.gameObject.scene);
            if (!EditorSceneManager.SaveScene(river.gameObject.scene)) throw new IOException("Frisbee river save failed");
            if (catchers.Length != 6 || discs.Length != 6) r.findings.Add("expected 6 catchers / 6 discs, found " + catchers.Length + " / " + discs.Length);
            foreach (var c in catchers) if (c.riverData != river || c.waterSpot != spot) r.findings.Add("catcher not wired " + c.name);
            foreach (var d in discs) if (d.riverData != river || d.waterSpot != spot) r.findings.Add("disc not wired " + d.name);
            Probe(r, river, spot);
            r.errors = r.findings.Count; r.status = r.errors == 0 ? "FRISBEE_RIVER_OK" : "FAIL";
            File.WriteAllText(OutDir + "/frisbee-river.json", JsonUtility.ToJson(r, true));
            if (r.errors > 0) throw new InvalidOperationException("[KH Frisbee River] " + string.Join("\n", r.findings));
            Debug.Log("[KH Frisbee River] FRISBEE_RIVER_OK catchers=" + r.catchersWired + " discs=" + r.discsWired);
        }

        static void Wire(UdonSharpBehaviour target, KH_RiverData river, KH_WaterSpot spot)
        {
            var so = new SerializedObject(target);
            var a = so.FindProperty("riverData"); var b = so.FindProperty("waterSpot");
            if (a == null || b == null) throw new InvalidOperationException(target.GetType().Name + ".riverData / waterSpot missing (compile first)");
            a.objectReferenceValue = river; b.objectReferenceValue = spot; so.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(target); EditorUtility.SetDirty(target);
        }

        // KH_FrisbeeCatch.RiverBlocks と同じ規則
        static bool Blocks(KH_RiverData river, KH_WaterSpot spot, Vector3 from, Vector3 to)
        {
            if (spot != null && spot._Contains(to)) return false;
            return river._BlocksStep(from, to, KH_RiverData.BandMargin);
        }

        static void Probe(Report r, KH_RiverData river, KH_WaterSpot spot)
        {
            foreach (float z in ProbeZ)
            {
                float cx = river._CenterX(z), hw = river._HalfWidth(z);
                foreach (float side in new[] { -1f, 1f })
                {
                    var start = new Vector3(cx + side * (hw + KH_RiverData.BandMargin + 6f), river._SurfaceY(z) + 1f, z);
                    var dir = new Vector3(-side, 0f, 0f);
                    Vector3 p = start; bool stopped = false; float stopLateral = 0f;
                    for (int i = 0; i < 400; i++)
                    {
                        var next = p + dir * 0.1f;
                        if (Blocks(river, spot, p, next)) { stopped = true; stopLateral = river._Lateral(p, KH_RiverData.BandMargin); break; }
                        p = next;
                    }
                    string name = "z=" + z + " side=" + side;
                    r.probes.Add(name + ": " + (stopped ? "stopped " + stopLateral.ToString("F2") + " m outside the band" : "NOT stopped"));
                    if (!stopped || stopLateral < -0.001f) r.findings.Add("straight move into the band was not refused at " + name);
                    // ディスク: 帯の中の点 → 近い方の岸
                    var inBand = new Vector3(cx + side * hw * 0.5f, river._SurfaceY(z) - 0.2f, z);
                    float bs = inBand.x >= cx ? 1f : -1f;
                    var bank = river._BankPoint(inBand, bs, KH_RiverData.BankExtra);
                    if (Physics.Raycast(bank + Vector3.up * 5f, Vector3.down, out RaycastHit g, 12f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore)) bank = g.point;
                    bool onNav = NavMesh.SamplePosition(bank, out NavMeshHit nav, 2f, NavMesh.AllAreas);
                    bool outside = !river._InBand(bank, KH_RiverData.BandMargin);
                    r.probes.Add(name + " disc → bank " + bank + " outside=" + outside + " navmesh=" + onNav);
                    if (!outside || !onNav) r.findings.Add("disc bank point not usable at " + name);
                }
            }
            // 水遊び場の中の一歩は止めない (例外)
            if (spot != null)
            {
                var e = spot.transform.position;
                bool anyInside = false, anyBlocked = false;
                for (float dz = -5f; dz <= 5f; dz += 1f)
                {
                    float z = 285f + dz, cx = river._CenterX(z);
                    for (float lat = 0.5f; lat <= 6f; lat += 0.5f)
                    {
                        var a = new Vector3(cx + lat + 0.1f, river._SurfaceY(z), z); var b = new Vector3(cx + lat, river._SurfaceY(z), z);
                        if (!spot._Contains(b)) continue;
                        anyInside = true;
                        if (Blocks(river, spot, a, b)) anyBlocked = true;
                    }
                }
                r.probes.Add("water spot: inside samples=" + anyInside + " blocked=" + anyBlocked);
                if (anyInside && anyBlocked) r.findings.Add("a step inside the water spot was refused (the spot is the exception)");
            }
        }
    }
}
