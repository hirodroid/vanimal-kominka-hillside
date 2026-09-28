using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    // Course/95 - Frisbee Plan Probe (2026-09-28). The Course/94 (M2 float) and Course/94b (M3 water) cases, placed by the harness's
    // own FbPlaceGeometry / F3PlaceGeometry in Edit mode (Get<T> reads the U# proxies there), and the dog's FIRST plan for each throw
    // from KH_FrisbeeCatch._ProbePlan (the runtime Plan). 94b's cases are tried with every aim the harness would try (attempt 0-5):
    // a case the planner can never give its shape (a ドボン, a float, deep-disc) is visible here before a 40-minute Play.
    // Nothing in the scene is written: no Teleport, no dog move, no heap write (F7-nobank's heap stand-in cannot be made in Edit mode).
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/95 - Frisbee Plan Probe")]
        public static void FrisbeePlanProbe()
        {
            // Synchronous: a delayCall never ran when the menu came through the HTTP API with the Editor unfocused (2026-09-28). The
            // CLI tolerates the API timing out and waits for the JSON (94b casts a few million aim rays: tens of seconds).
            f3AimSet = null;   // the 94b ドボン aims are searched afresh (F3SearchAims) and written to Temp/frisbee-m3-aims.json
            KH_FrisbeePlanProbeEditor.RunSafe("Course/95 - Frisbee Plan Probe", () => PlanProbeCourse(true, true));
        }

        /// <summary>m2 = the Course/94 cases, m3 = the Course/94b cases (every aim). Also runs Temp/frisbee-plan-probe-cases.json.</summary>
        public static KH_FrisbeePlanProbeEditor.Report PlanProbeCourse(bool m2, bool m3)
        {
            if (SessionState.GetBool(Key, false) || report != null) throw new InvalidOperationException("A Course walk harness is active; probe in idle Edit mode");
            var rep = new KH_FrisbeePlanProbeEditor.Report { menu = "Course/95 - Frisbee Plan Probe", utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            string previous = KH_FrisbeePlanProbeEditor.EnsureScene(KH_CourseGroundEditor.ScenePath);
            KH_FrisbeePlanProbeEditor.Stage(rep, "scene ready: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            try
            {
                Bind();
                KH_FrisbeePlanProbeEditor.Stage(rep, "bound: dogs=" + roots.Length + " wander=" + wander.Length);
                var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
                var river = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_RiverData");
                var spot = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_WaterSpot");
                if (river == null || spot == null) throw new InvalidOperationException("KH_RiverData / KH_WaterSpot UdonBehaviour missing in " + KH_CourseGroundEditor.ScenePath);
                KH_FrisbeePlanProbeEditor.Stage(rep, "river=" + river.gameObject.name + " spot=" + spot.gameObject.name + " sliceOk=" + Get<bool[]>(river, "sliceOk").Length);
                if (m2) { KH_FrisbeePlanProbeEditor.Stage(rep, "94 cases"); ProbeM2(rep, all, river, spot); }
                if (m3) { KH_FrisbeePlanProbeEditor.Stage(rep, "94b cases"); ProbeM3(rep, all, river, spot); }
                KH_FrisbeePlanProbeEditor.ProbeAdHoc(rep);
            }
            finally
            {
                manager = null; pouch = null; route = null; wander = null; roots = null; nodes = null;
                fbRiver = fbSpot = fbCatcher = fbDisc = null; fbSliceOkBackup = null;
                f3River = f3Spot = f3Recall = f3Catcher = f3Disc = f3Fetch = null;
                KH_FrisbeePlanProbeEditor.RestoreScene(previous);
            }
            return KH_FrisbeePlanProbeEditor.Finish(rep);
        }

        /// <summary>Course/94 / 94b: the probe on the same cases before Begin enters Play. Warnings only (PROBE_MISMATCH lines); it never
        /// switches scenes here (Begin needs the main scene alone, as it is).</summary>
        static void CoursePlanPreflight(bool m2, bool m3)
        {
            if (UnityEngine.SceneManagement.SceneManager.sceneCount != 1 || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != KH_CourseGroundEditor.ScenePath)
            { Debug.Log("[KH Course Walk] PROBE_SKIPPED the main scene is not open alone (" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path + ")"); return; }
            var lines = KH_FrisbeePlanProbeEditor.Preflight("Course/94 preflight", () => PlanProbeCourse(m2, m3));
            foreach (var l in lines) { if (l.StartsWith("PLAN_PROBE_OK")) Debug.Log("[KH Course Walk] " + l); else Debug.LogWarning("[KH Course Walk] " + l); }
        }

        static KH_FrisbeeCatch ProbeCatcherOf(int dog)
        {
            return UnityEngine.Object.FindObjectsOfType<KH_FrisbeeCatch>(true).FirstOrDefault(c => c.dogRoot == roots[dog]);
        }

        // Course/94 (M2 rules: the catcher's water states off, as the harness's _debugWaterFetchOff). The harness wants the dog never
        // in the river band while it plays, and F5d a ground retrieve; the float itself is the disc's (not a planner decision).
        static void ProbeM2(KH_FrisbeePlanProbeEditor.Report rep, UdonBehaviour[] all, UdonBehaviour river, UdonBehaviour spot)
        {
            fbRiver = river; fbSpot = spot;
            fbCatcher = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_FrisbeeCatch" && Get<Transform>(u, "dogRoot") == roots[FbDog]);
            if (fbCatcher == null) { rep.notes.Add("94: no KH_FrisbeeCatch for dog 0"); return; }
            fbDisc = Get<UdonBehaviour>(fbCatcher, "frisbee");
            var catcher = ProbeCatcherOf(FbDog);
            if (catcher == null) { rep.notes.Add("94: no KH_FrisbeeCatch proxy whose dogRoot is dog 0"); return; }
            foreach (string name in FbCaseNames)
            {
                var row = new KH_FrisbeePlanProbeEditor.Row { suite = "94", name = name, catcher = catcher != null ? catcher.gameObject.name : "-", expect = name == "F5d-roll" ? "ground" : "noband" };
                rep.rows.Add(row);
                if (!FbSetupCase(name)) { row.counted = false; row.verdict = "no throw of its own (uses F5a's floating disc)"; continue; }
                var c = new FbCase { name = name };
                fbSliceOkBackup = null;
                try { FbPlaceGeometry(c); }
                catch (Exception e) { row.verdict = "BLOCKED placement: " + e.Message; continue; }
                if (c.standIn) { row.counted = false; row.note = "F7 heap stand-in (sliceOk cleared) is Play-only; probed on the real data"; }
                row.r = catcher._ProbePlan(c.p0, c.v0, c.dogAt, new Vector3(-c.side, 0f, 0f), -1, -99f, -1f, 0f, true);
                if (name == "F5d-roll")
                {
                    KH_FrisbeePlanProbeEditor.JudgeExpect(row);
                    // Land control (fix-a consult item 3): the retrieve decision 0.4 s on, the dog run toward the landing
                    var ctl = new KH_FrisbeePlanProbeEditor.Row { suite = "landctl", name = "94/F5d-roll", catcher = row.catcher, expect = "same as baseline" };
                    rep.rows.Add(ctl);
                    if (row.r != null && row.r.ok)
                    {
                        Vector3 to = row.r.landing - c.dogAt; to.y = 0f;
                        Vector3 p1 = to.sqrMagnitude > 1e-6f ? c.dogAt + to.normalized * Mathf.Min(to.magnitude, catcher.chaseRunSpeed * 0.4f) : c.dogAt;
                        var r2 = catcher._ProbePlan(c.p0, c.v0, c.dogAt, new Vector3(-c.side, 0f, 0f), -1, -99f, row.r.tNow, 0f, true,
                            new[] { new KH_FrisbeePlanProbeStep { tNow = row.r.tNow + 0.4f, dogPos = p1, speed = catcher.chaseRunSpeed, dogFwd = to } });
                        var run = new KH_FrisbeePlanProbeEditor.ContRun { label = "planned", last = r2 != null && r2.ok && r2.steps.Count > 0 ? r2.steps[0] : null };
                        run.text = run.last != null ? "planned: " + KH_FrisbeePlanProbeEditor.StepText(run.last) : "planned: ERROR";
                        KH_FrisbeePlanProbeEditor.JudgeLandControl(ctl, new List<KH_FrisbeePlanProbeEditor.ContRun> { run });
                    }
                    else ctl.verdict = "ERROR first plan";
                    continue;
                }
                var r = row.r;
                if (r == null || !r.ok) { row.verdict = "ERROR " + (r == null ? "no result" : r.error); continue; }
                // The dog must not be sent into the band: a committed spot, crouch start or landing in it, or a ground plan to it.
                var bad = new List<string>();
                if (r.kind != "none" && r.kind != "ground")
                {
                    if (RiverInBand(r.standRoot)) bad.Add("stand/crouch spot in the band " + r.standRoot.ToString("F1"));
                    if (RiverInBand(r.landPt)) bad.Add("landing in the band " + r.landPt.ToString("F1"));
                }
                if (r.kind == "ground" && RiverInBand(r.landing)) bad.Add("ground retrieve into the band " + r.landing.ToString("F1"));
                row.pass = bad.Count == 0;
                row.verdict = row.pass ? "OK (" + r.runtimeNext + ")" : "MISMATCH " + string.Join("; ", bad.ToArray());
            }
        }

        // Course/94b (M3 rules). Per case, every aim the harness would try (attempt 0 .. F3MaxAttempts-1): an attempt row is information,
        // the case row passes when at least one attempt gives the case's shape.
        static void ProbeM3(KH_FrisbeePlanProbeEditor.Report rep, UdonBehaviour[] all, UdonBehaviour river, UdonBehaviour spot)
        {
            f3River = river; f3Spot = spot;
            f3Recall = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_ToyRecall");
            foreach (var d in F3Defs)
            {
                string name = (string)d[0]; int dog = (int)d[1];
                var c = new F3Case { name = name, dog = dog };
                string expect = name.StartsWith("F2") || name.StartsWith("F3") || name == "F14b-claim-dobon" ? "dobon"
                    : name == "F4-deep-Lab" ? "deep" : name == "F13-inwater" ? "any" : "float";
                var caseRow = new KH_FrisbeePlanProbeEditor.Row { suite = "94b", name = name, expect = expect };
                rep.rows.Add(caseRow);
                // F3Bind without its heap writes (and through Get, which reads the proxies in Edit mode)
                f3Catcher = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_FrisbeeCatch" && Get<Transform>(u, "dogRoot") == roots[dog]);
                try { f3Disc = f3Catcher != null ? Get<UdonBehaviour>(f3Catcher, "frisbee") : null; } catch (Exception) { f3Disc = null; }
                if (f3Catcher == null || f3Disc == null) { caseRow.verdict = "BLOCKED no KH_FrisbeeCatch / disc for dog " + dog; continue; }
                var catcher = ProbeCatcherOf(dog);
                caseRow.catcher = catcher != null ? catcher.gameObject.name : "-";
                if (catcher == null) { caseRow.verdict = "BLOCKED no KH_FrisbeeCatch proxy for dog " + dog; continue; }
                int attempts = name == "F13-inwater" ? 1 : F3MaxAttempts;
                var hits = new List<int>(); var seen = new List<string>(); string skip = null;
                for (int k = 0; k < attempts; k++)
                {
                    c.attempts = k;
                    var row = new KH_FrisbeePlanProbeEditor.Row { suite = "94b", name = name + "#" + (k + 1), catcher = caseRow.catcher, expect = expect, counted = false };
                    rep.rows.Add(row);
                    string err;
                    try { err = F3PlaceGeometry(c); }
                    catch (Exception e) { err = e.Message; }
                    if (err != null && err.StartsWith("SKIP ")) { skip = err.Substring(5); break; }   // N/A in this world's data (F4)
                    if (err != null) { row.verdict = (err.StartsWith("RETRY ") ? "aim: " + err.Substring(6) : "BLOCKED " + err); seen.Add("#" + (k + 1) + " no aim"); continue; }
                    row.r = catcher._ProbePlan(c.p0, c.v0, c.dogAt, new Vector3(-c.side, 0f, 0f), -1, -99f, -1f, 0f, false);
                    KH_FrisbeePlanProbeEditor.JudgeExpect(row);
                    // 94c run 20260928-112611: the ドボン aims must also hold under the live differences (a late first frame, the dog
                    // turned 25 deg): F3LiveOk, the same check the aim search applies
                    if (row.pass && expect == "dobon")
                    {
                        string live = F3LiveOk(catcher, c.p0, row.r.v0, c.dogAt, c.side);
                        if (live != null) { row.pass = false; row.verdict = "MISMATCH live: " + live; }
                    }
                    // 2026-09-28 (Codex consult): the first plan is not the catch. Keep it and tick again 0.4 s later with the dog run
                    // toward the crouch start at the planned pace and at +15 %: only a jump that stays a ドボン through KeepPlan counts.
                    // Hold check (Codex fix-a consult item 4): planned / ahead +0.57 / behind -0.3 / lateral +-0.3 must keep the same
                    // ドボン with T0 still; past T0 must not be rescued; T0 + 1 m toward the water must stay a refused take-off.
                    if (row.pass && expect == "dobon")
                    {
                        var runs = KH_FrisbeePlanProbeEditor.ContinueSet(catcher, c.p0, c.v0, c.dogAt, new Vector3(-c.side, 0f, 0f), -1, false, row.r, true);
                        string bad = KH_FrisbeePlanProbeEditor.JudgeDobonContinuation(catcher, runs, row.r, new Vector3(-c.side, 0f, 0f));
                        row.note += " || " + string.Join(" || ", runs.Select(x => x.text).ToArray());
                        if (bad != null) { row.pass = false; row.verdict = "MISMATCH continuation: " + bad; }
                        else row.verdict += " | continuation held (" + runs.Count + " inputs)";
                    }
                    row.note = (row.note ?? "") + "target " + c.target.ToString("F2") + " el=[" + c.elMin + "," + c.elMax + "] v0 " + c.v0.ToString("F2") + " err " + c.landError.ToString("F2");
                    if (row.pass) hits.Add(k + 1);
                    seen.Add("#" + (k + 1) + " " + (row.r != null && row.r.ok ? row.r.kind + (row.r.kind == "jump" ? "/" + row.r.mode + (row.r.landWater ? "/water" : "/land") : "") : "error"));
                }
                if (skip != null) { caseRow.counted = false; caseRow.verdict = "N/A " + skip; rep.rows.RemoveAll(x => x.suite == "94b" && x.name.StartsWith(name + "#")); continue; }
                if (name == "F13-inwater") { caseRow.counted = false; caseRow.verdict = "site OK (z=" + c.z.ToString("F1") + " side=" + c.side + " wade=" + c.wadeAt.ToString("F2") + "); the in-water reject depends on the dog's waterPose (live state): not probed; " + string.Join(", ", seen.ToArray()); continue; }
                caseRow.pass = hits.Count > 0;
                caseRow.verdict = (caseRow.pass ? "OK attempts " + string.Join(",", hits.Select(h => "#" + h).ToArray()) : "MISMATCH no attempt gives " + expect) + " | " + string.Join(", ", seen.ToArray());
            }
        }

        // Edit mode: the U# proxy's field (serialized values; private heap-only fields read their C# default). Course/95 only.
        static bool ProxyTryGet(UdonBehaviour u, string key, out object value)
        {
            value = null;
            UdonSharpBehaviour proxy;
            try { proxy = UdonSharpEditorUtility.GetProxyBehaviour(u); } catch (Exception) { return false; }
            if (proxy == null) return false;
            for (Type t = proxy.GetType(); t != null && t != typeof(UdonSharpBehaviour); t = t.BaseType)
            {
                var f = t.GetField(key, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f == null) continue;
                value = f.GetValue(proxy);
                // A U# reference to another behaviour reads as its proxy here; the harness expects the UdonBehaviour.
                var ub = value as UdonSharpBehaviour;
                if (ub != null) value = UdonSharpEditorUtility.GetBackingUdonBehaviour(ub);
                return true;
            }
            return false;
        }
    }
}
