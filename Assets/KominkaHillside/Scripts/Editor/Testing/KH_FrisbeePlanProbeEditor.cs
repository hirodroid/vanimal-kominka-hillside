using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// フリスビーの計画プローブ (Edit モード、Play なし。2026-09-28)。
    ///
    /// 捕り手の最初の Plan (投げの 0.1 s 後に 1 回走る Solve → 予備の D / D' / E → 落下点の Reachable) を、Play に入らずに
    /// ケースの配置で走らせ、どの形 (jump / stand / over / ground / none) を選ぶか・選ばない理由 (why=) を表にする。
    /// 判定は KH_FrisbeeCatch._ProbePlan (KH_FrisbeeCatch.Probe.cs、Udon には入らない) が実行時と同じコードで行う。
    /// ケースの配置と期待はハーネスの表をそのまま使う (KH_FrisbeeHarnessEditor.TerrainCaseTable / Shots / TerrainPlace /
    /// PlaceForGeometry、Course/94 / 94b の FbPlaceGeometry / F3PlaceGeometry)。数字をここに複製しない。
    ///
    /// 結果: Temp/frisbee-plan-probe.json と Console の表、最終行 PLAN_PROBE_OK n/m か PLAN_PROBE_FAIL n/m。
    /// 臨時のケース: Temp/frisbee-plan-probe-cases.json ({"cases":[{name, catcher, p0, v0, curve, dogPos, dogFwd, variant, tNow,
    /// dogSpeed, waterFetchOff, expect}]}) があれば、開いているシーンでそれも走らせる。
    ///
    /// 見るのは最初の計画と、それを保持したままの継続 tick (KeepPlan / KeepFixedT0、しゃがみ開始 FixedCrouchBegin、離陸の門
    /// FixedCrouchEnd → JumpGround / AirLanding) を、与えた位置・時刻・速さで回した結果。犬の実際の動き・加速・フレーム時刻・
    /// 所有権・アニメーションは見ない (Play の役目)。
    /// シーンは変えない (犬は一時の隠し Transform で代わりに立たせ、投げ台は一時のコライダーの写しを置いて片付ける)。
    /// </summary>
    public static class KH_FrisbeePlanProbeEditor
    {
        public const string OutPath = "Temp/frisbee-plan-probe.json";
        public const string CasesPath = "Temp/frisbee-plan-probe-cases.json";
        const int GroundMask = (1 << 0) | (1 << 11);

        [Serializable] public class AdHocCase
        {
            public string name = "adhoc", catcher = "", expect = "any";
            public Vector3 p0, v0, dogPos, dogFwd = Vector3.forward;
            public float curve = -99f, tNow = -1f, dogSpeed;
            public int variant = -1;
            public bool waterFetchOff;
            // Continuation ticks after the first plan (JSON key "continue"): [{tNow, dogPos, speed, dogFwd?}] - the committed plan is
            // kept and Plan runs again (KeepPlan / ReshootJumpGround / DropMode). expect is then judged on the LAST tick.
            public List<KH_FrisbeePlanProbeStep> @continue = new List<KH_FrisbeePlanProbeStep>();
        }
        [Serializable] public class AdHocFile { public List<AdHocCase> cases = new List<AdHocCase>(); }

        [Serializable] public class Row
        {
            public string suite, name, catcher, expect, verdict, note = "";
            public bool pass, counted = true;
            public string sig = "";   // land controls: the decision signature compared with the baseline file
            public KH_FrisbeePlanProbeResult r;
        }
        [Serializable] public class Report
        {
            public string menu, scene, utc, status = "RUNNING", failure = "";
            public int pass, total;
            public List<Row> rows = new List<Row>();
            public List<string> notes = new List<string>();
            public string limitations = "The first Plan tick (tNow = noticeDelay, dog standing, facing as placed) and, where asked, continuation ticks on the kept plan at GIVEN positions / times / speeds (KeepPlan, the crouch start and the liftoff gate on copies). Not probed: the dog's real motion, acceleration and frame timing, ownership / network, Animator, CanPlay's live terms (leash, Fetch, treat, rest, waterPose). Terrain / Course placements are the harness's own functions; the main-scene dog is placed at the harness's spot but in Play it idles 3 s there first.";
        }

        // ------------------------------------------------------------------ menus

        [MenuItem("Tools/KominkaHillside/Frisbee/10 Plan Probe")]
        public static void MenuPrototype()
        {
            // Synchronous (2026-09-28): a delayCall / update callback never ran when the menu came through the HTTP API with the
            // Editor unfocused (no JSON, no log). The CLI tolerates the API timing out on a long run and waits for the JSON instead.
            RunSafe("Frisbee/10 Plan Probe", () => RunPrototype(true, true, null));
        }

        [MenuItem("Tools/KominkaHillside/Frisbee/11 Plan Probe (ad-hoc cases, open scene)")]
        public static void MenuAdHoc()
        {
            RunSafe("Frisbee/11 Plan Probe (ad-hoc)", () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode only (the probe reads the U# proxies)");
                if (!File.Exists(CasesPath)) throw new InvalidOperationException(CasesPath + " is missing (node scripts/kh-plan-probe.cjs adhoc --cases=<file>)");
                var rep = new Report { menu = "Frisbee/11 Plan Probe (ad-hoc)", utc = DateTime.UtcNow.ToString("o"), scene = SceneManager.GetActiveScene().path };
                Physics.SyncTransforms();
                ProbeAdHoc(rep);
                return Finish(rep);
            });
        }

        /// <summary>Run a suite and write the report; any exception becomes status ERROR in the JSON (never a silent stop).</summary>
        public static Report RunSafe(string menu, Func<Report> run)
        {
            Report rep = null;
            // A RUNNING marker first: the CLI waits for a newer file whose status is no longer RUNNING, and a run that dies without
            // writing its result leaves this behind (with the stage it reached) instead of nothing.
            try { Write(new Report { menu = menu, utc = DateTime.UtcNow.ToString("o"), status = "RUNNING", failure = "started; if this stays, see Editor.log [KH Plan Probe] lines" }); }
            catch (Exception e) { Debug.LogError("[KH Plan Probe] cannot write " + OutPath + ": " + e.Message); }
            Debug.Log("[KH Plan Probe] start " + menu + " scene=" + SceneManager.GetActiveScene().path + " loaded=" + SceneManager.sceneCount
                + " dirty=" + SceneManager.GetActiveScene().isDirty + " playing=" + EditorApplication.isPlayingOrWillChangePlaymode);
            try { rep = run(); }
            catch (Exception e)
            {
                rep = new Report { menu = menu, utc = DateTime.UtcNow.ToString("o"), status = "ERROR", failure = e.GetType().Name + ": " + e.Message };
                Debug.LogError("[KH Plan Probe] " + menu + " ERROR " + e);
            }
            try { Write(rep); }
            catch (Exception e) { Debug.LogError("[KH Plan Probe] cannot write " + OutPath + ": " + e.Message); }
            Debug.Log("[KH Plan Probe] end " + menu + " " + rep.status + " " + rep.pass + "/" + rep.total + (rep.failure.Length > 0 ? " failure=" + rep.failure : "") + " -> " + OutPath);
            return rep;
        }

        /// <summary>One row of a suite: any exception becomes that row's ERROR verdict (the run goes on).</summary>
        static void Guard(Row row, Action body)
        {
            try { body(); }
            catch (Exception e) { row.pass = false; row.verdict = "ERROR " + e.GetType().Name + ": " + e.Message; Debug.LogWarning("[KH Plan Probe] " + row.suite + " " + row.name + " " + e); }
        }

        public static void Stage(Report rep, string what)
        {
            rep.notes.Add("stage: " + what);
            Debug.Log("[KH Plan Probe] " + what);
        }

        // ------------------------------------------------------------------ prototype scene (Frisbee/10)

        /// <summary>terrain / shots: which suites. breeds: null = every lane for the shots table.</summary>
        public static Report RunPrototype(bool terrain, bool shots, string[] breeds)
        {
            var rep = new Report { menu = "Frisbee/10 Plan Probe", utc = DateTime.UtcNow.ToString("o"), scene = KH_FrisbeePrototypeEditor.ScenePath };
            string previous = EnsureScene(KH_FrisbeePrototypeEditor.ScenePath);
            Stage(rep, "scene ready: " + SceneManager.GetActiveScene().path + (previous != null ? " (opened; will reopen " + previous + ")" : ""));
            try
            {
                if (terrain) ProbeTerrain(rep);
                if (shots) ProbeShots(rep, breeds ?? KH_FrisbeePrototypeEditor.LaneBreeds);
                ProbeAdHoc(rep);
            }
            finally { RestoreScene(previous); }
            return Finish(rep);
        }

        static void ProbeTerrain(Report rep)
        {
            string tag = KH_FrisbeePrototypeEditor.TerrainTag;
            var catcher = CatcherByName("KH_FrisbeeCatch_" + tag);
            var stand = GameObject.Find("KH_FrisbeeStand_" + tag);
            var recall = GameObject.Find("KH_FrisbeeRecall_" + tag);
            var cycle = GameObject.Find("KH_FrisbeeVariant_" + tag);
            Stage(rep, "terrain: catcher=" + (catcher != null ? catcher.gameObject.name : "MISSING") + " stand=" + (stand != null ? stand.name : "MISSING"));
            if (catcher == null || stand == null) { rep.notes.Add("terrain lane missing (KH_FrisbeeCatch_/Stand_" + tag + "): run Frisbee/4 Build Scene"); return; }
            var launcher = Launcher(stand);
            var shot = KH_FrisbeeHarnessEditor.Shots.First(s => s.name == KH_FrisbeeHarnessEditor.TerrainShot);
            var skip = new[] { stand.transform, recall != null ? recall.transform : null, cycle != null ? cycle.transform : null };
            foreach (var tc in KH_FrisbeeHarnessEditor.TerrainCaseTable)
            {
                var row = new Row { suite = "terrain", name = tc.name, catcher = catcher.gameObject.name, expect = TerrainExpect(tc) };
                rep.rows.Add(row);
                var tcc = tc;
                Guard(row, () => ProbeTerrainCase(row, tcc, catcher, stand, launcher, shot, skip));
            }
        }

        static void ProbeTerrainCase(Row row, KH_FrisbeeHarnessEditor.TerrainCaseDef tc, KH_FrisbeeCatch catcher, GameObject stand, KH_FrisbeeLauncher launcher, KH_FrisbeeHarnessEditor.Shot shot, Transform[] skip)
        {
            {
                Vector3 standPos, dogPos, playerPos; Quaternion dogRot;
                if (!KH_FrisbeeHarnessEditor.TerrainPlace(tc, skip, out standPos, out dogPos, out dogRot, out playerPos))
                { row.verdict = "BLOCKED no ground under the pad (Frisbee/4 Build Scene)"; return; }
                float speed = tc.speed > 0f ? tc.speed : shot.speed, elevation = tc.speed > 0f ? tc.elevation : shot.elevation;
                Vector3 p0, v0;
                // In Play the stand is moved onto the pad (TerrainPlace), so its colliders stand there too: copy them for the call.
                var temps = CopyColliders(stand.transform, standPos);
                try
                {
                    LaunchFrom(stand.transform, launcher.muzzle, standPos, speed, elevation, shot.side, out p0, out v0);
                    row.r = catcher._ProbePlan(p0, v0, dogPos, dogRot * Vector3.forward, tc.variant, -99f, -1f, 0f, false);
                }
                finally { foreach (var t in temps) UnityEngine.Object.DestroyImmediate(t); Physics.SyncTransforms(); }
                JudgeTerrain(row, tc, catcher);
            }
        }

        static string TerrainExpect(KH_FrisbeeHarnessEditor.TerrainCaseDef tc)
        {
            var parts = new List<string> { "kind " + tc.expectKind };
            if (tc.allowed != null && tc.allowed.Length == 1 && tc.allowed[0] == "none") parts.Add("none + cannot-play");
            if (tc.crossSign != 0) parts.Add("step jump " + tc.fromY.ToString("F1", CultureInfo.InvariantCulture) + "->" + tc.toY.ToString("F1", CultureInfo.InvariantCulture));
            if (tc.needLaneRefused) parts.Add("lane refusal");
            return string.Join(", ", parts.ToArray());
        }

        static void JudgeTerrain(Row row, KH_FrisbeeHarnessEditor.TerrainCaseDef tc, KH_FrisbeeCatch catcher)
        {
            var r = row.r;
            if (r == null || !r.ok) { row.verdict = "ERROR " + (r == null ? "no result" : r.error); return; }
            var bad = new List<string>();
            if (!KindOk(tc.expectKind, r.kind)) bad.Add("kind=" + r.kind + " want " + tc.expectKind);
            if (tc.allowed != null && tc.allowed.Length == 1 && tc.allowed[0] == "none")
            {
                if (r.kind != "none") bad.Add("kind=" + r.kind + " want none");
                else if (!r.runtimeNext.Contains("cannot-play")) bad.Add("no cannot-play (" + r.runtimeNext + ")");
            }
            if (tc.allowed != null && !tc.allowed.Contains("caught") && tc.allowed.Contains("ground") && r.kind != "ground") bad.Add("kind=" + r.kind + " want ground");
            if (tc.crossSign != 0)
            {
                bool cross = r.kind == "jump" && Mathf.Abs(r.dY) >= 0.3f && Mathf.Sign(r.dY) == tc.crossSign
                    && (tc.crossSign < 0 ? r.tDown > r.tUp + 0.01f : r.tDown < r.tUp - 0.01f)
                    && Mathf.Abs(r.takeoffY - tc.fromY) <= 0.1f && Mathf.Abs(r.landY - tc.toY) <= 0.1f;
                if (!cross) bad.Add("no step-crossing jump (takeoffY " + F(r.takeoffY) + " landY " + F(r.landY) + " tUp " + F(r.tUp) + " tDown " + F(r.tDown) + ")");
            }
            if (tc.needLaneRefused && r.laneRefused < 1 && !(r.whyA + r.whyB + r.whyC).Contains("lane")) bad.Add("no lane refusal (laneRefused 0, why A:" + r.whyA + " B:" + r.whyB + " C:" + r.whyC + ")");
            if (r.kind == "jump" && !r.landWater && Mathf.Abs(r.dY) > catcher.jumpMaxRise + 0.02f) bad.Add("|dY| " + F(Mathf.Abs(r.dY)) + " > jumpMaxRise");
            row.pass = bad.Count == 0;
            row.verdict = row.pass ? "OK" : "MISMATCH " + string.Join("; ", bad.ToArray());
        }

        static void ProbeShots(Report rep, string[] breeds)
        {
            var variants = Enumerable.Range(0, KH_FrisbeeCatch.VariantCount).Where(v => !KH_FrisbeeHarnessEditor.RetiredVariants.Contains(v)).ToArray();
            foreach (string breed in breeds)
            {
                var catcher = CatcherByName("KH_FrisbeeCatch_" + breed);
                var stand = GameObject.Find(KH_FrisbeePrototypeEditor.LaneName("KH_FrisbeeStand", breed));
                Stage(rep, "shots: lane " + breed + " catcher=" + (catcher != null ? catcher.gameObject.name : "MISSING") + " stand=" + (stand != null ? stand.name : "MISSING"));
                if (catcher == null || stand == null || catcher.dogRoot == null) { rep.notes.Add("lane " + breed + " missing (Frisbee/4 Build Scene)"); continue; }
                var launcher = Launcher(stand);
                Vector3 home = catcher.dogRoot.position; Quaternion homeRot = catcher.dogRoot.rotation;
                bool lab = breed == "Lab";   // KH_FrisbeeHarnessEditor.DefaultBreed: Lab has its own expectations (lab* columns)
                foreach (var shot in KH_FrisbeeHarnessEditor.Shots)
                    foreach (int v in shot.variant < 0 ? variants : new[] { shot.variant })
                    {
                        string expectKind = lab && shot.labExpectKind != null ? shot.labExpectKind : shot.expectKind;
                        string expectMode = lab && shot.labExpectMode != null ? shot.labExpectMode : shot.expectMode;
                        var row = new Row { suite = "shots", name = breed + "/v" + v + "/" + shot.name, catcher = catcher.gameObject.name, expect = "kind " + expectKind + (string.IsNullOrEmpty(expectMode) ? "" : " (jump mode " + expectMode + ")") };
                        rep.rows.Add(row);
                        var sh = shot; int vv = v;
                        Guard(row, () => ProbeShot(row, sh, vv, catcher, stand, launcher, home, homeRot, expectKind, expectMode));
                        // Land controls (fix-a consult item 3): the Lab lane's v0 jump shots keep the decision they had before the change
                        if (lab && v == 0 && row.r != null && row.r.ok && row.r.kind == "jump")
                        {
                            var ctl = new Row { suite = "landctl", name = "proto/" + row.name, catcher = row.catcher, expect = "same as baseline" };
                            rep.rows.Add(ctl);
                            var first = row.r;
                            Guard(ctl, () => JudgeLandControl(ctl, ContinueSet(catcher, first.p0, first.v0, first.dogPos, first.forward, vv, false, first, false)));
                        }
                    }
            }
        }

        static void ProbeShot(Row row, KH_FrisbeeHarnessEditor.Shot shot, int v, KH_FrisbeeCatch catcher, GameObject stand, KH_FrisbeeLauncher launcher, Vector3 home, Quaternion homeRot, string expectKind, string expectMode)
        {
                    {
                        Vector3 at = home; Quaternion rot = homeRot;
                        if (!string.IsNullOrEmpty(shot.place)) KH_FrisbeeHarnessEditor.PlaceForGeometry(shot.place, stand.transform.position, home, out at, out rot);
                        Vector3 p0, v0;
                        LaunchFrom(stand.transform, launcher.muzzle, stand.transform.position, shot.speed, shot.elevation, shot.side, out p0, out v0, false);
                        row.r = catcher._ProbePlan(p0, v0, at, rot * Vector3.forward, v, -99f, -1f, 0f, false);
                        var r = row.r;
                        if (r == null || !r.ok) { row.verdict = "ERROR " + (r == null ? "no result" : r.error); return; }
                        var bad = new List<string>();
                        if (!KindOk(expectKind, r.kind)) bad.Add("kind=" + r.kind + " want " + expectKind);
                        else if (!string.IsNullOrEmpty(expectMode) && r.kind == "jump" && r.mode != expectMode) bad.Add("mode=" + r.mode + " want " + expectMode);
                        row.pass = bad.Count == 0;
                        row.verdict = row.pass ? "OK" : "MISMATCH " + string.Join("; ", bad.ToArray());
                    }
        }

        // ------------------------------------------------------------------ ad-hoc cases (any scene)

        public static void ProbeAdHoc(Report rep)
        {
            if (!File.Exists(CasesPath)) { Debug.Log("[KH Plan Probe] no ad-hoc cases (" + CasesPath + " absent)"); return; }
            AdHocFile file;
            try { file = JsonUtility.FromJson<AdHocFile>(File.ReadAllText(CasesPath)); }
            catch (Exception e) { rep.notes.Add(CasesPath + " unreadable: " + e.Message); return; }
            if (file == null || file.cases == null) return;
            var all = UnityEngine.Object.FindObjectsOfType<KH_FrisbeeCatch>(true);
            foreach (var c in file.cases)
            {
                var catcher = string.IsNullOrEmpty(c.catcher) ? all.FirstOrDefault()
                    : all.FirstOrDefault(k => k.gameObject.name == c.catcher || (k.dogRoot != null && k.dogRoot.name == c.catcher));
                var row = new Row { suite = "adhoc", name = c.name, catcher = catcher != null ? catcher.gameObject.name : c.catcher, expect = c.expect };
                rep.rows.Add(row);
                if (catcher == null) { row.verdict = "ERROR no KH_FrisbeeCatch '" + c.catcher + "' in the open scene"; continue; }
                var cc = c; var kc = catcher;
                Guard(row, () =>
                {
                    var cont = cc.@continue != null && cc.@continue.Count > 0 ? cc.@continue.ToArray() : null;
                    row.r = kc._ProbePlan(cc.p0, cc.v0, cc.dogPos, cc.dogFwd, cc.variant, cc.curve, cc.tNow, cc.dogSpeed, cc.waterFetchOff, cont);
                    JudgeExpect(row);
                    if (row.r != null) foreach (var st in row.r.steps) row.note += (row.note.Length > 0 ? " || " : "") + StepText(st);
                });
            }
        }

        /// <summary>expect: "a|b|..." any of jump / stand / over / ground / none / any / dobon / nodobon / float / deep / cannot-play / reach.</summary>
        public static void JudgeExpect(Row row)
        {
            var r = row.r;
            if (r == null || !r.ok) { row.verdict = "ERROR " + (r == null ? "no result" : r.error); return; }
            string e = string.IsNullOrEmpty(row.expect) ? "any" : row.expect;
            var lastStep = r.steps != null && r.steps.Count > 0 ? r.steps[r.steps.Count - 1] : null;
            row.pass = e.Split('|').Select(s => s.Trim()).Any(t => lastStep != null ? StepToken(t, lastStep, r) : ExpectToken(t, r));
            row.verdict = row.pass ? "OK" : lastStep != null
                ? "MISMATCH want " + e + " after " + r.steps.Count + " continuation tick(s): " + StepText(lastStep)
                : "MISMATCH want " + e + " got " + r.kind + (r.kind == "jump" ? "/" + r.mode : "") + " -> " + r.runtimeNext;
        }

        public static bool ExpectToken(string t, KH_FrisbeePlanProbeResult r)
        {
            switch (t)
            {
                case "any": return true;
                case "dobon": return r.p0Playable && r.kind == "jump" && r.landWater;   // a throw ReqCatch refuses is no ドボン
                case "nodobon": return !(r.kind == "jump" && r.landWater);
                case "float": return (r.kind == "none" || r.kind == "ground") && r.landingValid && r.landingFloat && r.landingReachable;
                case "deep": return r.runtimeNext.StartsWith("deep-disc");   // the runtime would cancel deep-disc (WaterIntercept), not cannot-play first
                case "cannot-play": return r.runtimeNext.Contains("cannot-play");
                case "reach": return r.landingValid && r.landingReachable;
                default: return r.kind == t;
            }
        }

        /// <summary>A kind token judged on a continuation tick (float / deep / cannot-play / reach still read the first plan's landing).</summary>
        public static bool StepToken(string t, KH_FrisbeePlanProbeStep st, KH_FrisbeePlanProbeResult r)
        {
            switch (t)
            {
                case "any": return true;
                case "dobon": return r.p0Playable && st.kind == "jump" && st.landWater && (!st.liftoffCalled || st.liftoffWater);
                case "nodobon": return !(st.kind == "jump" && st.landWater);
                case "jump": case "stand": case "over": case "ground": case "none": return st.kind == t;
                default: return ExpectToken(t, r);
            }
        }

        /// <summary>One continuation tick in one line: the input, KeepPlan's numbers and verdict, and what the real tick committed.</summary>
        public static string StepText(KH_FrisbeePlanProbeStep st)
        {
            var sb = new StringBuilder();
            sb.Append("t=" + F(st.tNow) + " v=" + F(st.speed) + " pos=" + st.dogPos.ToString("F2") + ": ");
            if (st.expired) sb.Append("expired; ");
            if (st.keepCalled)
            {
                if (st.fit > 0f) sb.Append("vTake " + F(st.vTake) + " fit " + F(st.fit) + " slid " + F(st.slid) + (st.reshoot ? " (re-shoot T0/L0)" : "") + " t0 " + st.t0.ToString("F2") + " availR " + F(st.availR) + " pathR " + F(st.pathR) + "; ");
                sb.Append("keep=" + (st.kept ? "yes" : "NO drop=" + st.keepDrop + (st.keepWhy.Length > 0 ? " jgWhy=" + st.keepWhy : "") + " banned " + st.bannedBefore + "->" + st.bannedAfterKeep) + "; ");
            }
            sb.Append("-> " + st.kind + (st.kind == "jump" ? "/" + st.mode + (st.landWater ? "/water" : "/land") : "") + " banned=" + st.bannedAfter);
            if (st.kind != "jump") sb.Append(" why=A:" + st.whyA + ",B:" + st.whyB + ",C:" + st.whyC);
            sb.Append("; hold=" + (st.holdOk ? "OK" : "NO(" + st.holdWhy + ")") + " t0Move " + F(st.t0Move) + " groundChecked=" + (st.groundChecked ? 1 : 0));
            if (st.fxOnBefore || st.fxOnAfter) sb.Append(" fx=" + (st.fxOnBefore ? 1 : 0) + "->" + (st.fxOnAfter ? 1 : 0) + " fxT0 " + st.fxT0Before.ToString("F3") + "->" + st.fxT0After.ToString("F3")
                + " fxAhead " + F(st.fxAhead) + " fxLat " + F(st.fxLat) + " fxCrouchV " + F(st.fxCrouchV) + " fxDv " + F(st.fxDv) + " fxEndErr " + F(st.fxEndErr) + " C0 " + st.standRootAfter.ToString("F2"));
            if (st.liftoffCalled) sb.Append("; liftoff " + (st.liftoffOk ? (st.liftoffWater ? "OK water" : "OK LAND") : "REFUSED " + st.liftoffWhy) + " vh " + F(st.liftoffVh) + " vy " + F(st.liftoffVy) + (st.liftoffOk ? " landY " + F(st.liftoffLandY) : ""));
            if (st.crouchCalled) sb.Append("; crouch@" + F(st.crouchNow) + " " + (st.crouchOk ? "OK" : "refused=" + st.crouchRefused) + " v " + F(st.crouchVBegin) + " dv " + F(st.crouchDvBegin) + " ahead " + F(st.crouchAhead) + " lat " + F(st.crouchLat));
            if (st.planBefore == 1) sb.Append(" crouchV " + F(st.crouchV) + " dv " + F(st.dv) + " aheadRemain " + F(st.aheadRemain) + " lateral " + F(st.lateral) + " endErr " + F(st.endErr) + " airCap " + (st.airCap ? 1 : 0) + " (vhAir " + F(st.vhAir) + ")");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ continuation set (Codex fix-a consult 2026-09-28 item 4)

        public class ContRun { public string label; public bool negative; public KH_FrisbeePlanProbeStep last; public List<KH_FrisbeePlanProbeStep> steps = new List<KH_FrisbeePlanProbeStep>(); public string text; }

        public const float ContNegDv = 4.0f;   // the "NEG dv" input runs at take-off speed + this (m3's FixedCrouchMaxDv is 3.5)
        public const float ContFirstDt = 0.4f, ContAhead = 0.57f, ContBehind = 0.3f, ContLateral = 0.3f, ContPast = 0.3f, ContBeforeCrouch = 0.05f;

        /// <summary>From a committed momentum jump (first): tick 1 at first.tNow + 0.4 s on the paced way to C0, tick 2 just before the
        /// planned crouch (catchT - tUp - crouch - 0.05 s) with the dog at C0 + an offset along f / right, running at the take-off speed:
        /// planned, ahead +0.57 m (measured 0.50-0.64), behind -0.3 m, lateral +-0.3 m, and the negative "past T0" (T0 + 0.3 m: the pace
        /// it needs is over any cap - it must be refused / re-planned, never rescued). Each input is its own _ProbePlan sequence.</summary>
        public static List<ContRun> ContinueSet(KH_FrisbeeCatch catcher, Vector3 p0, Vector3 v0, Vector3 dogAt, Vector3 dogFwd, int variant, bool waterOff, KH_FrisbeePlanProbeResult first, bool full)
        {
            var runs = new List<ContRun>();
            if (first == null || !first.ok || first.kind != "jump") return runs;
            Vector3 f = first.forward; f.y = 0f; f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 c0 = first.standRoot;
            Vector3 t0 = c0 + f * (first.jumpSpeed * first.crouch);
            float tCrouch = first.catchT - first.tUp - first.crouch;
            float v = Mathf.Max(first.jumpSpeed, 0.5f);
            var inputs = new List<KeyValuePair<string, Vector3>> { new KeyValuePair<string, Vector3>("planned", c0), new KeyValuePair<string, Vector3>("ahead+" + F(ContAhead), c0 + f * ContAhead) };
            if (full)
            {
                inputs.Add(new KeyValuePair<string, Vector3>("behind-" + F(ContBehind), c0 - f * ContBehind));
                inputs.Add(new KeyValuePair<string, Vector3>("lateral+" + F(ContLateral), c0 + right * ContLateral));
                inputs.Add(new KeyValuePair<string, Vector3>("lateral-" + F(ContLateral), c0 - right * ContLateral));
                inputs.Add(new KeyValuePair<string, Vector3>("NEG past-T0+" + F(ContPast), t0 + f * ContPast));
                inputs.Add(new KeyValuePair<string, Vector3>("NEG dv>" + F(ContNegDv), c0));
            }
            foreach (var kv in inputs)
            {
                var steps = new List<KH_FrisbeePlanProbeStep>();
                float t1 = first.tNow + ContFirstDt, t2 = tCrouch - ContBeforeCrouch;
                if (t1 < t2 - 0.1f)
                {
                    Vector3 to = c0 - dogAt; to.y = 0f;
                    float pace = to.magnitude / Mathf.Max(0.05f, tCrouch - first.tNow);
                    Vector3 p1 = to.sqrMagnitude > 1e-6f ? dogAt + to.normalized * Mathf.Min(to.magnitude, pace * ContFirstDt) : dogAt;
                    steps.Add(new KH_FrisbeePlanProbeStep { tNow = t1, dogPos = Grounded(p1), speed = Mathf.Max(pace, 0.5f), dogFwd = to });
                }
                float v2 = kv.Key.StartsWith("NEG dv") ? v + ContNegDv : v;
                if (t2 > first.tNow) steps.Add(new KH_FrisbeePlanProbeStep { tNow = t2, dogPos = Grounded(kv.Value), speed = v2, dogFwd = f, beginCrouch = true });
                var run = new ContRun { label = kv.Key, negative = kv.Key.StartsWith("NEG") };
                if (steps.Count == 0) { run.text = kv.Key + ": no time left before the crouch"; runs.Add(run); continue; }
                var r = catcher._ProbePlan(p0, v0, dogAt, dogFwd, variant, -99f, first.tNow, 0f, waterOff, steps.ToArray());
                if (r == null || !r.ok || r.steps.Count == 0) { run.text = kv.Key + ": ERROR " + (r == null ? "no result" : r.error); runs.Add(run); continue; }
                run.last = r.steps[r.steps.Count - 1]; run.steps = r.steps;
                run.text = kv.Key + ": " + string.Join(" >> ", r.steps.Select(s => StepText(s)).ToArray());
                runs.Add(run);
            }
            return runs;
        }

        /// <summary>A continuation set on a ドボン aim: every non-negative input holds (same plan / mode / catchT, T0 still, water, kept,
        /// no new drop / ban), the negative is not rescued (no longer the same kept jump), and T0 moved 1 m toward the water is refused
        /// as a take-off (the ground rule is intact). null = OK, else the reasons.</summary>
        public static string JudgeDobonContinuation(KH_FrisbeeCatch catcher, List<ContRun> runs, KH_FrisbeePlanProbeResult first, Vector3 towardWater)
        {
            var bad = new List<string>();
            foreach (var run in runs)
            {
                if (run.last == null) { bad.Add(run.text); continue; }
                var l = run.last;
                if (!run.negative)
                {
                    // every tick must hold (Codex M3b review: not only the last one); the failing tick is named
                    for (int i = 0; i < run.steps.Count; i++)
                        if (!run.steps[i].holdOk) { bad.Add(run.label + " tick " + (i + 1) + " (t=" + F(run.steps[i].tNow) + ") " + run.steps[i].holdWhy); break; }
                    if (run.steps.All(x => x.holdOk))
                    {
                        if (l.crouchCalled && !l.crouchOk) bad.Add(run.label + " crouch refused=" + l.crouchRefused);
                        else if (!l.crouchCalled && l.fxOnAfter) bad.Add(run.label + " crouch start not checked");
                        else if (l.crouchCalled && !l.liftoffOk) bad.Add(run.label + " liftoff " + l.liftoffWhy);
                        else if (l.crouchCalled && !l.liftoffWater) bad.Add(run.label + " liftoff lands on LAND (water lost at the gate)");
                    }
                }
                else
                {
                    bool refusedAtTick = run.steps.Any(x => !(x.kept && x.sameCatch && x.kind == "jump"));
                    bool refusedAtCrouch = l.crouchCalled && !l.crouchOk;
                    bool refusedAtLiftoff = l.liftoffCalled && !l.liftoffOk;
                    if (!refusedAtTick && !refusedAtCrouch && !refusedAtLiftoff) bad.Add(run.label + " RESCUED (the same jump kept, its crouch and liftoff accepted)");
                }
            }
            Vector3 f = first.forward; f.y = 0f; f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            Vector3 t0 = first.standRoot + f * (first.jumpSpeed * first.crouch);
            if (towardWater.sqrMagnitude > 1e-6f)
            {
                string g = catcher._ProbeJumpGround(t0 + towardWater.normalized * 1f, first.takeoffY);
                if (g.Length == 0) bad.Add("NEG T0+1 m toward the water is a valid take-off (should be river / slope / noground)");
            }
            return bad.Count == 0 ? null : string.Join(" | ", bad.ToArray());
        }

        public const string LandBaselinePath = "Temp/frisbee-plan-probe-landctl-baseline.json";
        [Serializable] public class BaselineRow { public string name, sig; }
        [Serializable] public class Baseline { public List<BaselineRow> rows = new List<BaselineRow>(); }

        /// <summary>Land control: the decision signature of each continuation input; compared with the baseline file when it exists
        /// (node scripts/kh-plan-probe.cjs ... --save-baseline writes it from a run of the code BEFORE the change).</summary>
        public static void JudgeLandControl(Row row, List<ContRun> runs)
        {
            row.sig = string.Join(",", runs.Select(x => x.label + ":" + (x.last == null ? "none" : x.last.kind + "/" + x.last.mode + "/" + (x.last.kept ? "kept" : "drop") + (x.last.t0Move > KH_FrisbeeCatch.HoldT0Tolerance ? "/t0moved" : ""))).ToArray());
            row.note = string.Join(" || ", runs.Select(x => x.text).ToArray());
            Baseline b = null;
            try { if (File.Exists(LandBaselinePath)) b = JsonUtility.FromJson<Baseline>(File.ReadAllText(LandBaselinePath)); } catch (Exception) { b = null; }
            var hit = b == null ? null : b.rows.FirstOrDefault(x => x.name == row.name);
            if (hit == null) { row.counted = false; row.verdict = "no baseline (" + LandBaselinePath + "): " + row.sig; return; }
            row.pass = hit.sig == row.sig;
            row.verdict = row.pass ? "OK same as baseline" : "MISMATCH baseline " + hit.sig + " now " + row.sig;
        }

        static Vector3 Grounded(Vector3 p)
        {
            RaycastHit g;
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out g, 6f, GroundMask, QueryTriggerInteraction.Ignore)) p.y = g.point.y;
            return p;
        }

        static bool KindOk(string expectKind, string kind)
        {
            if (string.IsNullOrEmpty(expectKind) || expectKind == "any") return true;
            return expectKind.Split('|').Contains(kind);
        }

        // ------------------------------------------------------------------ helpers

        public static KH_FrisbeeCatch CatcherByName(string name)
        {
            return UnityEngine.Object.FindObjectsOfType<KH_FrisbeeCatch>(true).FirstOrDefault(c => c.gameObject.name == name);
        }

        static KH_FrisbeeLauncher Launcher(GameObject stand)
        {
            var l = stand.GetComponents<KH_FrisbeeLauncher>().FirstOrDefault(x => !x.recallOnly && !x.cycleVariant && x.muzzle != null);
            if (l == null) throw new InvalidOperationException(stand.name + " has no throwing KH_FrisbeeLauncher with a muzzle");
            return l;
        }

        /// <summary>KH_FrisbeeLauncher.Update's launch from a stand whose root is at standPos with rotation identity (moved = true, the
        /// harness's terrain placement) or where it is now (moved = false).</summary>
        static void LaunchFrom(Transform stand, Transform muzzle, Vector3 standPos, float speed, float elevation, float side, out Vector3 p0, out Vector3 v0, bool moved = true)
        {
            Vector3 pos = muzzle.position; Quaternion rot = muzzle.rotation;
            if (moved)
            {
                pos = standPos + Vector3.Scale(stand.lossyScale, stand.InverseTransformPoint(muzzle.position));
                rot = Quaternion.Inverse(stand.rotation) * muzzle.rotation;
            }
            Vector3 right = rot * Vector3.right, fwd = rot * Vector3.forward;
            Vector3 dir = Quaternion.AngleAxis(side, Vector3.up) * Quaternion.AngleAxis(-elevation, right) * fwd;
            p0 = pos; v0 = dir.normalized * speed;
        }

        /// <summary>Hidden, unsaved copies of the stand's solid colliders (Box / Sphere / Capsule) as they stand after the move.</summary>
        static List<GameObject> CopyColliders(Transform stand, Vector3 standPos)
        {
            var list = new List<GameObject>();
            foreach (var c in stand.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled || ((1 << c.gameObject.layer) & GroundMask) == 0) continue;
                var go = new GameObject("PlanProbe_" + c.name) { hideFlags = HideFlags.HideAndDontSave, layer = c.gameObject.layer };
                go.transform.SetPositionAndRotation(standPos + Vector3.Scale(stand.lossyScale, stand.InverseTransformPoint(c.transform.position)), Quaternion.Inverse(stand.rotation) * c.transform.rotation);
                go.transform.localScale = c.transform.lossyScale;
                var b = c as BoxCollider; var s = c as SphereCollider; var k = c as CapsuleCollider;
                if (b != null) { var n = go.AddComponent<BoxCollider>(); n.center = b.center; n.size = b.size; }
                else if (s != null) { var n = go.AddComponent<SphereCollider>(); n.center = s.center; n.radius = s.radius; }
                else if (k != null) { var n = go.AddComponent<CapsuleCollider>(); n.center = k.center; n.radius = k.radius; n.height = k.height; n.direction = k.direction; }
                list.Add(go);
            }
            Physics.SyncTransforms();
            return list;
        }

        /// <summary>The scene the probe needs, alone. Already so = null (nothing to restore). Otherwise opens it (only when nothing is
        /// unsaved) and returns the path to go back to.</summary>
        public static string EnsureScene(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode only (the probe reads the U# proxies)");
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) throw new InvalidOperationException("Compile first (compiling or compile errors present)");
            if (SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().path == path) { Physics.SyncTransforms(); return null; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene " + SceneManager.GetSceneAt(i).path + "; open " + path + " alone yourself (the probe does not save or discard)");
            string previous = SceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Physics.SyncTransforms();
            return string.IsNullOrEmpty(previous) ? null : previous;
        }

        public static void RestoreScene(string previous)
        {
            if (string.IsNullOrEmpty(previous)) return;
            if (SceneManager.GetActiveScene().isDirty) { Debug.LogWarning("[KH Plan Probe] the probed scene reads dirty; not switching back to " + previous); return; }
            EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        }

        public static Report Finish(Report rep)
        {
            var counted = rep.rows.Where(r => r.counted).ToList();
            rep.total = counted.Count; rep.pass = counted.Count(r => r.pass);
            rep.status = rep.total > 0 && rep.pass == rep.total ? "PLAN_PROBE_OK" : "PLAN_PROBE_FAIL";
            var sb = new StringBuilder();
            sb.AppendLine("[KH Plan Probe] " + rep.menu + " scene=" + rep.scene);
            sb.AppendLine("suite   case                         kind/mode      dY     land   next                               verdict");
            foreach (var r in rep.rows)
            {
                var x = r.r;
                string km = x == null || !x.ok ? "-" : x.kind + (x.kind == "jump" || x.kind == "stand" || x.kind == "over" ? "/" + x.mode : "");
                sb.AppendLine(Pad(r.suite, 7) + " " + Pad(r.name, 28) + " " + Pad(km, 14) + " " + Pad(x != null && x.kind == "jump" ? F(x.dY) : "", 6) + " "
                    + Pad(x != null && x.landingValid ? (x.landingFloat ? "float" : "land") + (x.landingReachable ? "+" : "x") : "", 6) + " "
                    + Pad(x != null ? x.runtimeNext : "", 34) + " " + (r.counted ? "" : "(info) ") + r.verdict
                    + (x != null && x.ok && x.kind != "jump" && x.bandIn >= 0f ? "  why=A:" + x.whyA + ",B:" + x.whyB + ",C:" + x.whyC : ""));
            }
            foreach (var n in rep.notes) sb.AppendLine("NOTE " + n);
            sb.Append(rep.status + " " + rep.pass + "/" + rep.total);
            Debug.Log(sb.ToString());
            foreach (var r in rep.rows.Where(r => r.counted && !r.pass))
                Debug.LogWarning("PROBE_MISMATCH " + r.suite + " " + r.name + ": " + r.verdict + (r.r != null && r.r.ok ? " | " + r.r.tail.Trim() + " drop=" + r.r.drop : ""));
            return rep;
        }

        static void Write(Report rep)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, JsonUtility.ToJson(rep, true));
        }

        /// <summary>For the Play harnesses: run a suite and return the PROBE_MISMATCH lines (never throws; a probe error is one line).</summary>
        public static List<string> Preflight(string menu, Func<Report> run)
        {
            var lines = new List<string>();
            var rep = RunSafe(menu, run);
            if (rep.status == "ERROR") { lines.Add("PROBE_SKIPPED " + rep.failure); return lines; }
            foreach (var r in rep.rows.Where(r => r.counted && !r.pass)) lines.Add("PROBE_MISMATCH " + r.suite + " " + r.name + ": " + r.verdict);
            lines.Add(rep.status + " " + rep.pass + "/" + rep.total + " (" + OutPath + ")");
            return lines;
        }

        static string F(float v) { return v.ToString("F2", CultureInfo.InvariantCulture); }
        static string Pad(string s, int n) { s = s ?? ""; return s.Length >= n ? s : s + new string(' ', n - s.Length); }
    }
}
