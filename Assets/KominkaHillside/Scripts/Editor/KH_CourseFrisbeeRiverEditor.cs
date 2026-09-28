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
    /// (代わりにこの静的な検査。実際の挙動は PCVR で見る)。試作シーンでは配線が null のままで、挙動は変わらない。
    /// DES-DOG-005 M2 (§D-6): 浮きと岸戻しの分かれ目。KH_Frisbee.FloatKind / InRiverBand と同じ式 (水遊び場の _FloatsAt || 川の _FloatsAt
    /// = 浮く、それ以外で帯の中 (水遊び場を除く) = 岸戻し) を、水の中の点 (浮く)・橋の帯 / 岸の無い区間 / 帯の縁の坂 (岸戻し)・水遊び場の水
    /// (浮く) にかける。岸の無い区間がデータに無ければ「無し」と記録するだけ (失敗にしない)。動的な確認は Course/94 (ClientSim)。</summary>
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
            public List<string> floatProbes = new List<string>();   // DES-DOG-005 M2 §D-6
            public int waterFloats, waterSkippedNoBank, bridgeBankReturns, edgeBankReturns, noBankBankReturns;
            public bool spotFloats;
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
            ProbeFloat(r, river, spot);
            r.errors = r.findings.Count; r.status = r.errors == 0 ? "FRISBEE_RIVER_OK" : "FAIL";
            File.WriteAllText(OutDir + "/frisbee-river.json", JsonUtility.ToJson(r, true));
            if (r.errors > 0) throw new InvalidOperationException("[KH Frisbee River] " + string.Join("\n", r.findings));
            Debug.Log("[KH Frisbee River] FRISBEE_RIVER_OK catchers=" + r.catchersWired + " discs=" + r.discsWired
                + " float: water=" + r.waterFloats + " (skipped no-bank " + r.waterSkippedNoBank + ") bridge=" + r.bridgeBankReturns + " edge=" + r.edgeBankReturns
                + " noBank=" + r.noBankBankReturns + " spot=" + r.spotFloats);
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

        // ---- DES-DOG-005 M2 §D-6: where a disc floats and where it still goes to the bank ----
        // KH_Frisbee.FloatKind (spot water first, then the river) and InRiverBand (the band minus the spot), called on the saved data.
        static bool DiscFloats(KH_RiverData river, KH_WaterSpot spot, Vector3 p)
        { return (spot != null && spot._FloatsAt(p)) || river._FloatsAt(p); }
        static bool DiscBankReturn(KH_RiverData river, KH_WaterSpot spot, Vector3 p)
        { return !DiscFloats(river, spot, p) && river._InBand(p, KH_RiverData.BandMargin) && (spot == null || !spot._Contains(p)); }

        static void ProbeFloat(Report r, KH_RiverData river, KH_WaterSpot spot)
        {
            if (!river._FetchReady()) { r.findings.Add("river fetch data missing: run Course/90 first (the disc floats only with it)"); return; }
            float reach = river._BankReach();
            // 1. In the water (centre and +-0.5 half width) away from the bridge zones and the spot: floats wherever the data has a
            //    usable bank within reach (otherwise skipped here; 4 below covers that case).
            foreach (float z in new[] { 60f, 120f, 150f, 250f, 360f, 400f, 560f, 620f })
            {
                if (!river._Covers(z) || river._InBridgeZone(z, 1f)) continue;
                float cx = river._CenterX(z), hw = river._HalfWidth(z), sy = river._SurfaceY(z);
                foreach (float f in new[] { -0.5f, 0f, 0.5f })
                {
                    var p = new Vector3(cx + f * hw, sy - 0.1f, z);
                    if (spot != null && spot._Contains(p)) continue;
                    if (river._BedAt(p) <= KH_RiverData.NoBed + 1f || river._NearestSlice(p, 0, reach, -1) < 0) { r.waterSkippedNoBank++; continue; }
                    bool fl = DiscFloats(river, spot, p), bank = DiscBankReturn(river, spot, p);
                    r.floatProbes.Add("water " + V(p) + " floats=" + fl + " bank=" + bank);
                    if (fl && !bank) r.waterFloats++; else r.findings.Add("a disc in the water does not float at " + V(p));
                }
            }
            if (r.waterFloats < 6) r.findings.Add("too few water points float (" + r.waterFloats + "); expected >= 6");
            // 2. Bridge zones: the water 4 m and 7 m from each bridge (inside its +-8 m zone, off the deck) goes to the bank
            if (river.bridgeZ != null)
                foreach (float b in river.bridgeZ)
                    foreach (float dz in new[] { -7f, -4f, 4f, 7f })
                    {
                        float z = b + dz; if (!river._Covers(z)) continue;
                        var p = new Vector3(river._CenterX(z), river._SurfaceY(z) - 0.1f, z);
                        bool fl = DiscFloats(river, spot, p), bank = DiscBankReturn(river, spot, p);
                        r.floatProbes.Add("bridge-zone " + V(p) + " floats=" + fl + " bank=" + bank);
                        if (!fl && bank) r.bridgeBankReturns++; else r.findings.Add("a disc in a bridge zone does not go to the bank at " + V(p));
                    }
            // 3. The band-edge slope (half width + 0.5 m, both sides, just above the water): the bank
            foreach (float z in ProbeZ)
                foreach (float side in new[] { -1f, 1f })
                {
                    float cx = river._CenterX(z), hw = river._HalfWidth(z);
                    var p = new Vector3(cx + side * (hw + 0.5f), river._SurfaceY(z) + 0.1f, z);
                    if (spot != null && spot._Contains(p)) continue;
                    bool fl = DiscFloats(river, spot, p), bank = DiscBankReturn(river, spot, p);
                    r.floatProbes.Add("band-edge " + V(p) + " floats=" + fl + " bank=" + bank);
                    if (!fl && bank) r.edgeBankReturns++; else r.findings.Add("a disc on the band edge does not go to the bank at " + V(p));
                }
            // 4. A stretch without a usable bank within reach (from the data; none = recorded, not a failure)
            int found = 0;
            for (float z = 2f; z <= 718f && found < 3; z += 1f)
            {
                if (!river._Covers(z) || river._InBridgeZone(z, 1f)) continue;
                var p = new Vector3(river._CenterX(z), river._SurfaceY(z) - 0.1f, z);
                if (spot != null && spot._Contains(p)) continue;
                if (river._BedAt(p) <= KH_RiverData.NoBed + 1f || river._NearestSlice(p, 0, reach, -1) >= 0) continue;
                found++; z += 10f;
                bool fl = DiscFloats(river, spot, p), bank = DiscBankReturn(river, spot, p);
                r.floatProbes.Add("no-bank " + V(p) + " floats=" + fl + " bank=" + bank);
                if (!fl && bank) r.noBankBankReturns++; else r.findings.Add("a disc where no bank is within reach does not go to the bank at " + V(p));
            }
            if (found == 0) r.floatProbes.Add("no-bank: the data has no stretch without a usable bank within " + reach + " m (nothing to check here; Course/94 F7 uses a heap stand-in)");
            // 5. The water-play spot's water floats, and the shared drift keeps the spot's rule (KH_RiverData._DriftVelocity)
            if (spot != null && spot._Ready())
            {
                Vector3 best = Vector3.zero; float bestDepth = -1f;
                for (int i = 0; i < spot._CellCount(); i++)
                {
                    var c = spot._CellPoint(i);
                    if (c.y <= KH_WaterSpot.NoBed + 1f || !spot._Contains(c)) continue;
                    float d = river._SurfaceY(c.z) - c.y;
                    if (d > bestDepth) { bestDepth = d; best = c; }
                }
                if (bestDepth > 0.05f)
                {
                    var p = new Vector3(best.x, river._SurfaceY(best.z) - 0.1f, best.z);
                    r.spotFloats = spot._FloatsAt(p) && DiscFloats(river, spot, p) && !DiscBankReturn(river, spot, p);
                    r.floatProbes.Add("spot " + V(p) + " depth=" + bestDepth.ToString("F2") + " floats=" + r.spotFloats + " downstreamZ=" + spot._DownstreamZ().ToString("F2"));
                    if (!r.spotFloats) r.findings.Add("a disc in the water-play spot does not float at " + V(p));
                    float end = spot._DownstreamZ();
                    var above = river._DriftVelocity(new Vector3(p.x, p.y, end + 1f), KH_WaterSpot.DefaultFloatDrift, true, end);
                    var below = river._DriftVelocity(new Vector3(p.x, p.y, end - 0.1f), KH_WaterSpot.DefaultFloatDrift, true, end);
                    if (Mathf.Abs(above.z + KH_WaterSpot.DefaultFloatDrift) > 1e-4f || above.x != 0f || below.sqrMagnitude > 1e-8f)
                        r.findings.Add("_DriftVelocity lost the spot rule: above=" + V(above) + " below=" + V(below));
                }
                else r.floatProbes.Add("spot: no wet cell found");
            }
        }

        static string V(Vector3 v) { return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")"; }
    }
}
