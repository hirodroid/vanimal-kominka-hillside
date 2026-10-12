using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // IMP-DOG-053 §3 (2026-10-12): Edit-mode probe of the leashed river follow's planners. Read only: never writes the scene or an asset,
    // no Play, idle Edit mode on the production scene (saved, not dirty; dirtiness re-checked at the end). It calls the runtime's own
    // methods on the U# proxies (the same code the dogs run: KD_DogWanderController._LeashRiverClamp / _LeashRiverPlanEntry /
    // _LeashRiverPlanExit, KH_LeashManager._ReleaseHome), on the saved Terrain, the baked NavMesh and the river data of Course/73 / 90.
    //   1 clamp, per breed (the six dogs: M, Ms, swims; walk limit = KH_RiverData.fetchWadeRatio x M): the hand on the far bank
    //     (known success: reached), the deepest cell of the river outside the bridge zones and the spot (reached: max depth is below every
    //     non-swimmer's limit in this scene, recorded as a fact), the same with an explicit walk limit of 0.25 m and swimming off (the
    //     branch the scene cannot produce: clamped short of the deep water), a goal 4 m from a bridge (the no-entry zone: clamped at
    //     its edge, never inside), a band point without riverbed data outside the bridge zones (none expected in this scene).
    //   2 the way in, for every non-bridge band-edge sample of Course/130 (Temp/kh-leash-river/survey.json): the dog at the NavMesh
    //     start, the target 1 m inside the water's edge. The runtime's choice (ok slice / direct / none) against Course/130's "direct"
    //     (connected, slope <= 25 deg, no stone): direct where 130 says not direct is the honest risk count, listed with its reason.
    //     Since the Codex review 2026-10-12 (F1) the runtime enters through ok slices only: any direct choice is a finding (expect 0).
    //     A sample with no NavMesh within 8 m (the river's ends) is a fact, not a finding.
    //   3 the way out, every 10 m along the centre line (outside the bridge zones): a holder on each bank -> a plan on the holder's side.
    //   4 the release home: a point in the water -> an ok slice's return point on the NavMesh; a land point -> itself.
    // Result: Temp/kh-leash-river/probe.json and the Console ([KH Leash River Probe]).
    public static class KH_CourseLeashRiverProbeEditor
    {
        const string OutDir = "Temp/kh-leash-river";
        static readonly string[] Breeds = { "Corgi", "Pome", "Lab", "Dachs", "Shiba", "Samoyed" };
        const float ExplicitLimit = .25f;

        [Serializable] public class Clamp
        {
            public string name; public Vector3 from, goal, got; public float goalDepth, gotDepth, shortBy; public bool reached;
            public string expect, verdict;
        }
        [Serializable] public class BreedRow
        {
            public string name; public int dogIndex; public float M, Ms, walkLimit; public bool swims;
            public List<Clamp> clamps = new List<Clamp>();
        }
        [Serializable] public class EntryStats
        {
            public int samples, slice, direct, none, sliceWhereDirect, sliceWhereNotDirect, directWhereDirect, directWhereNotDirect, sliceOtherSide;
            public List<string> otherSideAt = new List<string>();   // the slice is on the other bank (first 10)
            public float sliceDistMedian, sliceDistP90, sliceDistMax;
            public List<string> risk = new List<string>();   // direct at runtime where Course/130 says not direct (z, side, reason)
            public List<string> noneAt = new List<string>();
        }
        [Serializable] public class ExitStats { public int points, planned, holderSide, otherSide, spot, fallback, none; public List<string> missing = new List<string>(); }
        [Serializable] public class HomeStats { public int water, waterOnNav, waterSlice, land, landSame; public List<string> bad = new List<string>(); }
        [Serializable] public class Report
        {
            public string status = "DONE", utc, scene;
            public bool dirtyAfter;
            public float maxDepth, deepZ; public Vector3 deepAt;
            public List<BreedRow> breeds = new List<BreedRow>();
            public EntryStats entry = new EntryStats();
            public ExitStats exit = new ExitStats();
            public HomeStats home = new HomeStats();
            public int bandNoBedOutsideBridges;
            public List<string> summary = new List<string>(), findings = new List<string>(), facts = new List<string>();
        }

        [MenuItem("Tools/KominkaHillside/Course/131 - Probe Leash River")]
        public static void Probe()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new Report { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var wanders = UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true).Where(w => w.dogIndex >= 0 && w.dogIndex < 6).OrderBy(w => w.dogIndex).ToArray();
            if (wanders.Length != 6) throw new InvalidOperationException("Six KD_DogWanderController with dogIndex 0-5 required (" + wanders.Length + ")");
            var data = wanders[0].riverData;
            if (data == null || !data._FetchReady()) throw new InvalidOperationException("riverData not wired or the river-wide data not built (Course/73 / 90)");
            foreach (var w in wanders)
                if (w.riverData != data || w.waterSpot == null) r.findings.Add(Breeds[w.dogIndex] + ": riverData / waterSpot not wired like dog 0 (Course/76 / 90)");
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            KH_LeashManager manager = pouches.Length == 1 ? pouches[0].manager : null;
            if (manager == null) r.findings.Add("the leash manager (through the one menu) not found: the release-home section is skipped");

            DeepestCell(r, data);
            foreach (var w in wanders) ClampRows(r, data, w);
            NoBedBand(r, data);
            EntryTable(r, data, wanders[0]);
            ExitTable(r, data, wanders[0]);
            if (manager != null) HomeTable(r, data, manager);
            Summarise(r);

            r.dirtyAfter = SceneManager.GetActiveScene().isDirty;
            if (r.dirtyAfter) r.findings.Add("Scene became dirty during the probe (it writes nothing: investigate before saving)");
            if (r.findings.Count > 0) r.status = "DONE_WITH_FINDINGS";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/probe.json", JsonUtility.ToJson(r, true));
            foreach (var line in r.summary) Debug.Log("[KH Leash River Probe] " + line);
            foreach (var f in r.findings) Debug.LogWarning("[KH Leash River Probe] FINDING " + f);
            Debug.Log("[KH Leash River Probe] " + r.status + " findings=" + r.findings.Count + " -> " + OutDir + "/probe.json");
        }

        static bool Excluded(KH_RiverData d, float z) { return d._InBridgeZone(z, 10f) || (z > 265f && z < 315f); }

        static float Depth(KH_RiverData d, Vector3 p) { return d._DepthAt(p); }

        static Vector3 OnBed(KH_RiverData d, Vector3 p) { float b = d._BedAt(p); p.y = b > KH_RiverData.NoBed + 1f ? b : d._SurfaceY(p.z); return p; }

        static void DeepestCell(Report r, KH_RiverData d)
        {
            for (int row = 0; row < d.fetchRows; row++)
            {
                float z = d.fetchZ0 + row * d.fetchStep;
                if (!d._Covers(z) || Excluded(d, z)) continue;
                for (int i = d.fetchRowStart[row]; i < d.fetchRowStart[row + 1]; i++)
                {
                    float bed = d.fetchBed[i];
                    if (bed <= KH_RiverData.NoBed + 1f) continue;
                    var p = new Vector3(d.fetchRowX0[row] + (i - d.fetchRowStart[row]) * d.fetchStep, bed, z);
                    if (!d._FetchWater(p)) continue;
                    float dep = d._SurfaceY(z) - bed;
                    if (dep > r.maxDepth) { r.maxDepth = dep; r.deepAt = p; r.deepZ = z; }
                }
            }
        }

        /// <summary>A point in the water dx metres inside the water's edge on the given side at z (on the riverbed).</summary>
        static Vector3 Inside(KH_RiverData d, float z, int side, float dx)
        {
            return OnBed(d, new Vector3(d._CenterX(z) + side * (d._HalfWidth(z) - dx), 0f, z));
        }

        static void ClampRows(Report r, KH_RiverData d, KD_DogWanderController w)
        {
            float limit = d._FetchWadeRatio() * w.waterMouthHeight;
            var row = new BreedRow { name = Breeds[w.dogIndex], dogIndex = w.dogIndex, M = w.waterMouthHeight, Ms = w.waterSwimMouth, swims = w.waterSwimAllowed, walkLimit = limit };
            float z = r.deepZ;
            int deepSide = r.deepAt.x >= d._CenterX(z) ? 1 : -1;
            // the hand on the far bank: from just inside one edge to just inside the other (known success: every breed reaches it here)
            Add(row, d, w, "far-bank", Inside(d, z, -deepSide, .3f), Inside(d, z, deepSide, .3f), limit, w.waterSwimAllowed, "reached");
            // the deepest cell, the real limit (the scene's max depth is below every non-swimmer's limit: reached, recorded as a fact)
            Add(row, d, w, "deepest", Inside(d, z, -deepSide, .3f), r.deepAt, limit, w.waterSwimAllowed, r.maxDepth <= limit || w.waterSwimAllowed ? "reached" : "clamped");
            // the same with an explicit limit and swimming off: the non-swimmer's depth branch (clamped short, the stop no deeper than it)
            Add(row, d, w, "deepest-limit-" + ExplicitLimit.ToString("F2"), Inside(d, z, -deepSide, .3f), r.deepAt, ExplicitLimit, false, r.maxDepth > ExplicitLimit ? "clamped" : "reached");
            // a bridge zone: from 12 m below a bridge to 4 m below it (inside its zone): clamped at the zone's edge
            foreach (float b in d.bridgeZ ?? new float[0])
            {
                var from = OnBed(d, new Vector3(d._CenterX(b - 12f), 0f, b - 12f));
                var goal = OnBed(d, new Vector3(d._CenterX(b - 4f), 0f, b - 4f));
                var c = Add(row, d, w, "bridge-" + b.ToString("F0"), from, goal, limit, w.waterSwimAllowed, "clamped");
                if (d._InBridgeZone(c.got.z, 0f)) { c.verdict = "FAIL"; r.findings.Add(row.name + " bridge " + b + ": the clamp ends inside the zone at z " + c.got.z.ToString("F2")); }
                else if (Mathf.Abs(c.got.z - (b - d._BridgeZoneHalfZ())) > .6f) { c.verdict = "FAIL"; r.findings.Add(row.name + " bridge " + b + ": the clamp stops " + (b - d._BridgeZoneHalfZ() - c.got.z).ToString("F2") + " m before the zone's edge (expected within 0.6 m)"); }
            }
            foreach (var c in row.clamps) if (c.verdict == "FAIL" && !r.findings.Any(f => f.StartsWith(row.name + " " + c.name))) r.findings.Add(row.name + " " + c.name + ": expected " + c.expect + ", got " + (c.reached ? "reached" : "clamped (short by " + c.shortBy.ToString("F2") + " m, depth there " + c.gotDepth.ToString("F3") + ")"));
            r.breeds.Add(row);
        }

        static Clamp Add(BreedRow row, KH_RiverData d, KD_DogWanderController w, string name, Vector3 from, Vector3 goal, float limit, bool swim, string expect)
        {
            var got = w._LeashRiverClamp(from, goal, limit, swim);
            var c = new Clamp { name = name, from = from, goal = goal, got = got, goalDepth = Depth(d, goal), gotDepth = Depth(d, got), expect = expect };
            var dd = goal - got; dd.y = 0f; c.shortBy = dd.magnitude; c.reached = c.shortBy < .01f;
            bool ok = expect == "reached" ? c.reached : !c.reached;
            if (ok && expect == "clamped" && name.StartsWith("deepest-limit") && c.gotDepth > limit + .02f) ok = false;   // the stop is no deeper than the limit (+ one 0.25 m step's slope)
            c.verdict = ok ? "PASS" : "FAIL";
            row.clamps.Add(c);
            return c;
        }

        static void NoBedBand(Report r, KH_RiverData d)
        {
            int n = 0;
            for (float z = 0f; z <= 720f; z += 1f)
            {
                if (!d._Covers(z) || d._InBridgeZone(z, 0f)) continue;
                float cx = d._CenterX(z), hw = d._HalfWidth(z);
                for (float x = -hw - KH_RiverData.BandMargin + .25f; x < hw + KH_RiverData.BandMargin; x += .5f)
                {
                    var p = new Vector3(cx + x, d._SurfaceY(z), z);
                    if (d._InBand(p, KH_RiverData.BandMargin) && d._BedAt(p) <= KH_RiverData.NoBed + 1f) n++;
                }
            }
            r.bandNoBedOutsideBridges = n;
            r.facts.Add("band points without riverbed data outside the bridge zones (1 m x 0.5 m): " + n + (n == 0 ? " (the only band without river-wide water is the bridge zones: the bridge clamp is the scene's real 'no water' refusal)" : ""));
        }

        static void EntryTable(Report r, KH_RiverData d, KD_DogWanderController w)
        {
            var path = OutDir + "/survey.json";
            if (!File.Exists(path)) { r.findings.Add(path + " missing: run Course/130 first (the entry table is skipped)"); return; }
            var s = JsonUtility.FromJson<KH_CourseLeashRiverSurveyEditor.Report>(File.ReadAllText(path));
            var e = r.entry; var dist = new List<float>();
            foreach (var smp in s.edges)
            {
                if (smp.bridgeZone) continue;
                Vector3 dog = smp.scanLateral < 999f ? new Vector3(d._CenterX(smp.z) + smp.side * (d._HalfWidth(smp.z) + KH_RiverData.BandMargin + smp.scanLateral), 0f, smp.z) : smp.navDist >= 0f ? smp.nav : Vector3.zero;
                if (smp.scanLateral >= 999f && smp.navDist < 0f) { e.samples++; e.none++; e.noneAt.Add("z " + smp.z + " side " + smp.side + " (no NavMesh within 8 m)"); continue; }
                NavMeshHit h; if (NavMesh.SamplePosition(dog + Vector3.up * 2f, out h, 3f, NavMesh.AllAreas)) dog = h.position;
                var target = Inside(d, smp.z, smp.side, 1f);
                int code = w._LeashRiverPlanEntry(dog, target);
                var at = w._LeashRiverPlanPoint();
                e.samples++;
                string why130 = smp.directOk ? "direct" : !smp.connected ? "unconnected" : smp.profileMaxSlopeDeg > 25f ? "steep " + smp.profileMaxSlopeDeg.ToString("F0") + " deg" : !string.IsNullOrEmpty(smp.stone) ? "stone " + smp.stone : smp.scanLateral > KH_RiverData.BandMargin ? "NavMesh " + smp.scanLateral.ToString("F2") + " m out" : "other";
                if (code >= 0)
                {
                    e.slice++; if (smp.directOk) e.sliceWhereDirect++; else e.sliceWhereNotDirect++;
                    // the last fallback picks a slice on the other bank: at runtime the dog walks round by a bridge (or times out the
                    // 15 s way in and retries). Counted so the controller can decide whether that fallback should be "none" instead
                    int got = at.x >= d._CenterX(at.z) ? 1 : -1;
                    if (got != smp.side) { e.sliceOtherSide++; if (e.otherSideAt.Count < 10) e.otherSideAt.Add("z " + smp.z + " side " + smp.side + " (" + why130 + ")"); }
                    var cross = d._BankPoint(target, smp.side, KH_RiverData.BandMargin); var dd = at - cross; dd.y = 0f; dist.Add(dd.magnitude);
                }
                else if (code == -1)
                {
                    e.direct++;
                    if (smp.directOk) e.directWhereDirect++;
                    else { e.directWhereNotDirect++; if (e.risk.Count < 60) e.risk.Add("z " + smp.z + " side " + smp.side + ": " + why130); }
                }
                else { e.none++; if (e.noneAt.Count < 40) e.noneAt.Add("z " + smp.z + " side " + smp.side + " (" + why130 + ")"); }
            }
            dist.Sort();
            if (dist.Count > 0) { e.sliceDistMedian = dist[dist.Count / 2]; e.sliceDistP90 = dist[Mathf.Min(dist.Count - 1, (int)(dist.Count * .9f))]; e.sliceDistMax = dist[dist.Count - 1]; }
            // IMP-DOG-053 F1 (Codex review 2026-10-12): the runtime enters through verified ok slices only; a direct entry is a finding
            if (e.direct > 0) r.findings.Add(e.direct + " band-edge samples chose a direct entry (expected 0: ok slices only since F1): " + string.Join("; ", e.risk.Take(8)));
            // a sample without any NavMesh within 8 m (the river's ends, z 720: run 2026-10-12) has nowhere to stand: a fact, not a defect
            int noNav = e.noneAt.Count(a => a.Contains("no NavMesh within 8 m"));
            if (noNav > 0) r.facts.Add(noNav + " band-edge samples have no NavMesh within 8 m (nothing to enter from): " + string.Join("; ", e.noneAt.Where(a => a.Contains("no NavMesh")).Take(6)));
            if (e.none - noNav > 0) r.findings.Add((e.none - noNav) + " band-edge samples with NavMesh get no way in at all (no ok slice within the bank reach; the dog stays on the bank there): " + string.Join("; ", e.noneAt.Where(a => !a.Contains("no NavMesh")).Take(8)));
        }

        static void ExitTable(Report r, KH_RiverData d, KD_DogWanderController w)
        {
            var x = r.exit;
            for (float z = 5f; z <= 715f; z += 10f)
            {
                if (!d._Covers(z) || d._InBridgeZone(z, 0f)) continue;
                var p = OnBed(d, new Vector3(d._CenterX(z), 0f, z));
                if (!d._FetchWater(p)) continue;
                foreach (int side in new[] { -1, 1 })
                {
                    x.points++;
                    var holder = new Vector3(d._CenterX(z) + side * (d._HalfWidth(z) + 4f), p.y, z);
                    if (!w._LeashRiverPlanExit(p, holder, -1)) { x.none++; if (x.missing.Count < 40) x.missing.Add("z " + z + " holder side " + side); continue; }
                    x.planned++;
                    var bank = w._LeashRiverExitPoint();
                    int got = bank.x >= d._CenterX(bank.z) ? 1 : -1;
                    if (got == side) x.holderSide++; else x.otherSide++;
                }
            }
            if (x.none > 0) r.findings.Add(x.none + " of " + x.points + " water points have no way out at all: " + string.Join("; ", x.missing.Take(8)));
        }

        static void HomeTable(Report r, KH_RiverData d, KH_LeashManager m)
        {
            var hs = r.home;
            for (float z = 5f; z <= 715f; z += 15f)
            {
                if (!d._Covers(z) || d._InBridgeZone(z, 0f)) continue;
                var p = OnBed(d, new Vector3(d._CenterX(z), 0f, z));
                if (!d._FetchWater(p)) continue;
                hs.water++;
                var home = m._ReleaseHome(0, p);
                NavMeshHit h; bool onNav = NavMesh.SamplePosition(home, out h, .5f, NavMesh.AllAreas);
                bool inBand = d._InBand(home, KH_RiverData.BandMargin);
                if (onNav && !inBand) hs.waterOnNav++; else if (hs.bad.Count < 20) hs.bad.Add("z " + z + ": home " + home.ToString("F2") + (onNav ? "" : " off the NavMesh") + (inBand ? " in the band" : ""));
                if ((home - p).sqrMagnitude > .01f) hs.waterSlice++;
                // a land point on the route beside it: itself
                var land = new Vector3(d._CenterX(z) + d._HalfWidth(z) + 6f, p.y, z);
                if (NavMesh.SamplePosition(land, out h, 3f, NavMesh.AllAreas) && !d._InBand(h.position, KH_RiverData.BandMargin))
                { hs.land++; if ((m._ReleaseHome(0, h.position) - h.position).sqrMagnitude < 1e-6f) hs.landSame++; }
            }
            if (hs.waterOnNav < hs.water) r.findings.Add((hs.water - hs.waterOnNav) + " release homes in the water are not on the land / NavMesh: " + string.Join("; ", hs.bad.Take(6)));
            if (hs.landSame < hs.land) r.findings.Add((hs.land - hs.landSame) + " release homes on the land were moved (expected the point itself)");
        }

        static void Summarise(Report r)
        {
            var S = r.summary;
            S.Add("deepest cell outside the bridge zones and the spot: " + r.maxDepth.ToString("F3") + " m at " + r.deepAt.ToString("F2"));
            foreach (var b in r.breeds)
                S.Add(b.name + " (M " + b.M.ToString("F3") + ", Ms " + b.Ms.ToString("F3") + (b.swims ? ", swims" : ", no swim") + ", walk limit " + b.walkLimit.ToString("F3") + "): "
                    + string.Join(", ", b.clamps.Select(c => c.name + " " + (c.reached ? "reached" : "clamped " + c.shortBy.ToString("F2") + " m short (depth " + c.gotDepth.ToString("F3") + ", z " + c.got.z.ToString("F2") + ")") + " [" + c.verdict + "]")));
            var e = r.entry;
            S.Add("way in (" + e.samples + " band-edge samples of Course/130): slice " + e.slice + " (where 130 says direct " + e.sliceWhereDirect + ", not direct " + e.sliceWhereNotDirect + "), direct " + e.direct
                + " (agrees " + e.directWhereDirect + ", RISK where 130 says not direct " + e.directWhereNotDirect + "), none " + e.none + "; slice bank from the crossing point median " + e.sliceDistMedian.ToString("F2")
                + " / p90 " + e.sliceDistP90.ToString("F2") + " / max " + e.sliceDistMax.ToString("F2") + " m");
            if (e.risk.Count > 0) S.Add("way in, direct where 130 says not direct: " + string.Join("; ", e.risk.Take(12)) + (e.risk.Count > 12 ? " ..." : ""));
            S.Add("way in, a slice on the other bank (the dog would go round by a bridge): " + e.sliceOtherSide + (e.otherSideAt.Count > 0 ? " - " + string.Join("; ", e.otherSideAt) : ""));
            var x = r.exit;
            S.Add("way out (" + x.points + " water points x holder side): planned " + x.planned + " (holder's side " + x.holderSide + ", other side " + x.otherSide + "), none " + x.none);
            var h = r.home;
            S.Add("release home: in the water " + h.water + " -> on the land / NavMesh " + h.waterOnNav + " (moved to a slice return point " + h.waterSlice + "); on the land " + h.land + " -> unchanged " + h.landSame);
            foreach (var f in r.facts) S.Add("fact: " + f);
            if (r.maxDepth <= r.breeds.Where(b => !b.swims).Select(b => b.walkLimit).DefaultIfEmpty(0f).Min())
                S.Add("fact: no non-swimmer meets its depth limit in this scene (max " + r.maxDepth.ToString("F3") + " m); the branch is exercised with the explicit limit " + ExplicitLimit.ToString("F2") + " m");
        }
    }
}
