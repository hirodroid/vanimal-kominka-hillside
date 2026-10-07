using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using KominkaDogRun;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    /// <summary>破壊監査 2026-10-07 Phase A (docs/audits/2026-10-07/triage.md §1 RC-5 / RC-15, §8): the checks Edit mode can decide,
    /// so they need no Play. Reads the saved scene only (proxies, backing publicVariables, the baked KH_RiverData); never writes the
    /// scene (the one write is the runtime-only, non-serialized _packAreaCenter of a proxy, restored at once). Course/127.
    ///
    /// RC-5 (じゃれ合い・あいさつのホーム間の距離):
    /// - scene: playMaxHomeDistance = 25 on the 6 KD_DogWanderController proxies (and their backing UdonBehaviour when it holds the
    ///   variable), PlayMaxDistance() = 25, areaCenter / riverData wired, KH_LeashManager.roamCenters[i] = the dog's areaCenter (the
    ///   initial home = that Transform's position: ApplyHomes writes releasedHomes into roamCenters, PlayHomesApart reads areaCenter).
    /// - the rule on synthetic homes (the same expression as PlayHomesApart: Horizontal(a, b) > PlayMaxDistance() || riverData._OtherBank(a, b),
    ///   with the dog's own Horizontal / PlayMaxDistance through reflection and the scene's KH_RiverData): 20 m -> false, 25 m exactly -> false
    ///   (the boundary, ">"), 25.01 m and 30 m -> true (dog 0's home rounded to whole metres, moved along an axis whose points are not
    ///   on the other bank, so distance alone decides), 20 m across the centre line -> true, 20 m on one bank at the same z -> false.
    /// - the real PlayHomesApart (reflection, _packAreaCenter filled the way Start fills it) on every ordered pair of the six initial homes
    ///   -> false (the gathering at the gate is not refused), and equal to the expression above. When the scene has them, two more
    ///   real-method probes against existing scene Transforms: one 30-80 m away on the same bank -> true, one < 20 m on the other bank -> true.
    /// RC-15 (W-fixed-kept): source text only (the Play case is W-reroll-clock in Course/78 / 78r): WaterRollInterval = 45, WaterEnd writes
    /// _waterNextRoll = Mathf.Max(_waterNextRoll, Time.time + WaterRollInterval) (a later value, the harness's +600 s hold, survives), no
    /// other writer of the clock than that and the roll, and the harness still holds it (+600 s) outside W-reroll-clock.
    /// Result: Temp/audit-2026-10-07/phase-a-edit.json and one AUDIT_EDIT_OK | AUDIT_EDIT_FAIL line (first), then one warning per FAIL.
    public static class KH_AuditPhaseAEditor
    {
        const string OutDir = "Temp/audit-2026-10-07";
        const string OutPath = OutDir + "/phase-a-edit.json";
        const string WaterSource = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideWater.cs";
        const string WanderSourceDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts";
        const string HarnessSource = "Assets/KominkaHillside/Scripts/Editor/Testing/KH_CourseWalkHarnessEditor.Water.cs";
        const float WantHomeDistance = 25f;
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Serializable] class Report
        {
            public string utc, scene, status;
            public int checks, failed;
            public List<string> lines = new List<string>();
            public List<string> notes = new List<string>();
            public List<string> homes = new List<string>();
            public string limitations = "Edit mode on the saved scene. RC-5: the distance / bank rule of PlayHomesApart and the scene values; not the pairing in Play, the greeting's player distance (GreetTooFar), the busy rules (water / wash / leash: DbgOtherBusy) or homes moved at runtime (gatherings, releases). RC-15: source text and the constant; the clock's behaviour is the Play case W-reroll-clock (Course/78 / 78r).";
        }

        [MenuItem("Tools/KominkaHillside/Course/127 - Audit Phase A Edit Checks")]
        public static void Run()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new Report { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            Action<bool, string> req = (ok, what) => { r.checks++; r.lines.Add((ok ? "PASS " : "FAIL ") + what); if (!ok) r.failed++; };
            try { CheckPlayHomes(r, req); }
            catch (Exception e) { req(false, "RC-5 probe stopped: " + e.GetType().Name + ": " + e.Message); }
            try { CheckWaterClock(req); }
            catch (Exception e) { req(false, "RC-15 source check stopped: " + e.GetType().Name + ": " + e.Message); }
            req(!SceneManager.GetActiveScene().isDirty, "the scene is not dirty after the checks");
            r.status = r.failed == 0 ? "AUDIT_EDIT_OK" : "AUDIT_EDIT_FAIL";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutPath, JsonUtility.ToJson(r, true));
            string summary = "[KH Audit Phase A] " + r.status + " checks=" + r.checks + " failed=" + r.failed + " -> " + OutPath;
            if (r.failed > 0) Debug.LogError(summary); else Debug.Log(summary);
            foreach (var l in r.lines.Where(l => l.StartsWith("FAIL"))) Debug.LogWarning("[KH Audit Phase A] " + l);
        }

        // ================================================================ RC-5

        static void CheckPlayHomes(Report r, Action<bool, string> req)
        {
            var wanders = UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true).OrderBy(w => w.dogIndex).ToArray();
            req(wanders.Length == 6 && wanders.Select(w => w.dogIndex).SequenceEqual(Enumerable.Range(0, 6)), "6 KD_DogWanderController with dogIndex 0-5 (" + string.Join(",", wanders.Select(w => w.dogIndex)) + ")");
            var river = UnityEngine.Object.FindObjectsOfType<KH_RiverData>(true).FirstOrDefault();
            req(river != null && river._Ready(), "KH_RiverData in the scene and baked (_Ready)");
            var t = typeof(KD_DogWanderController);
            var fMax = t.GetField("playMaxHomeDistance", Inst);
            var fPack = t.GetField("_packAreaCenter", Inst);
            var mApart = t.GetMethod("PlayHomesApart", Inst, null, new[] { typeof(int) }, null);
            var mHoriz = t.GetMethod("Horizontal", Inst, null, new[] { typeof(Vector3), typeof(Vector3) }, null);
            var mLimit = t.GetMethod("PlayMaxDistance", Inst, null, Type.EmptyTypes, null);
            req(fMax != null && fPack != null && mApart != null && mHoriz != null && mLimit != null, "reflection: playMaxHomeDistance / _packAreaCenter / PlayHomesApart(int) / Horizontal / PlayMaxDistance found");
            if (wanders.Length != 6 || river == null || !river._Ready() || fMax == null || fPack == null || mApart == null || mHoriz == null || mLimit == null) return;

            // ---- scene values
            var leash = UnityEngine.Object.FindObjectsOfType<KH_LeashManager>(true).FirstOrDefault();
            foreach (var w in wanders)
            {
                string who = "dog " + w.dogIndex + " (" + w.name + ")";
                float v = (float)fMax.GetValue(w);
                req(Mathf.Abs(v - WantHomeDistance) < 1e-4f, who + " playMaxHomeDistance (proxy) = 25 (got " + v.ToString("F3") + ")");
                var b = UdonSharpEditorUtility.GetBackingUdonBehaviour(w);
                if (b != null && b.publicVariables.TryGetVariableValue("playMaxHomeDistance", out object bv))
                    req(bv is float bf && Mathf.Abs(bf - WantHomeDistance) < 1e-4f, who + " playMaxHomeDistance (backing) = 25 (got " + (bv ?? "null") + ")");
                else r.notes.Add(who + ": the backing UdonBehaviour holds no playMaxHomeDistance variable (the proxy value is the one checked)");
                float lim = (float)mLimit.Invoke(w, null);
                req(Mathf.Abs(lim - WantHomeDistance) < 1e-4f, who + " PlayMaxDistance() = 25 (got " + lim.ToString("F3") + ")");
                req(w.areaCenter != null, who + " areaCenter wired");
                req(w.riverData == river, who + " riverData = the scene KH_RiverData (without it the other-bank rule never applies)");
                if (leash != null && leash.roamCenters != null && w.dogIndex < leash.roamCenters.Length)
                    req(leash.roamCenters[w.dogIndex] == w.areaCenter, who + " KH_LeashManager.roamCenters[" + w.dogIndex + "] is its areaCenter (the initial home is that Transform's position)");
                else r.notes.Add(who + ": KH_LeashManager / roamCenters not found, the initial home is read from areaCenter only");
                if (w.areaCenter != null) r.homes.Add(who + " home " + w.areaCenter.position.ToString("F2") + " side " + Side(river, w.areaCenter.position));
            }
            if (wanders.Any(w => w.areaCenter == null)) return;

            Func<KD_DogWanderController, Vector3, Vector3, float> horiz = (w, a, b) => (float)mHoriz.Invoke(w, new object[] { a, b });
            // The same expression as PlayHomesApart (KD_DogWanderController.Play.cs), with the dog's own Horizontal / PlayMaxDistance
            Func<KD_DogWanderController, Vector3, Vector3, bool> apart = (w, a, b) =>
                horiz(w, a, b) > (float)mLimit.Invoke(w, null) || (w.riverData != null && w.riverData._OtherBank(a, b));

            // ---- synthetic homes (the expression)
            var w0 = wanders[0];
            var h0 = w0.areaCenter.position;
            var p0 = new Vector3(Mathf.Round(h0.x), h0.y, Mathf.Round(h0.z));   // whole metres: p0 + 25 m along an axis is exactly 25 m
            var dists = new[] { 20f, 25f, 25.01f, 30f };
            Vector3 axis = Vector3.zero;
            foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
                if (dists.All(m => !river._OtherBank(p0, p0 + d * m))) { axis = d; break; }
            req(axis != Vector3.zero, "synthetic: an axis from dog 0's home " + p0.ToString("F1") + " (side " + Side(river, p0) + ") whose points up to 30 m are not on the other bank (distance alone decides)");
            if (axis != Vector3.zero)
            {
                foreach (var m in dists)
                {
                    var q = p0 + axis * m; float hd = horiz(w0, p0, q);
                    bool want = m > WantHomeDistance, got = apart(w0, p0, q);
                    req(got == want, "synthetic same bank " + m.ToString("F2") + " m (Horizontal " + hd.ToString("F4") + ", axis " + axis + ") -> apart " + got + " (want " + want + (m == WantHomeDistance ? ", the boundary: > not >=" : "") + ")");
                }
            }
            // across the centre line at the same distance (20 m) vs. the same 20 m on one bank, at the home's z (or the data's middle)
            float zc = river._Covers(p0.z) ? p0.z : river.zStart + (river.centerX.Length - 1) * river.zStep * .5f;
            float cx = river._CenterX(zc);
            var acrossA = new Vector3(cx - 10f, p0.y, zc); var acrossB = new Vector3(cx + 10f, p0.y, zc);
            bool across = apart(w0, acrossA, acrossB);
            req(river._OtherBank(acrossA, acrossB) && across, "synthetic other bank 20 m (z " + zc.ToString("F1") + ", centre x " + cx.ToString("F2") + ", Horizontal " + horiz(w0, acrossA, acrossB).ToString("F3") + ") -> apart " + across + " (want True)");
            var sameA = new Vector3(cx + 2f, p0.y, zc); var sameB = new Vector3(cx + 22f, p0.y, zc);
            bool same = apart(w0, sameA, sameB);
            req(!river._OtherBank(sameA, sameB) && !same, "synthetic same bank 20 m at the same z -> apart " + same + " (want False)");

            // ---- the real PlayHomesApart on the six initial homes (and probes against existing Transforms)
            var homes = wanders.Select(w => w.areaCenter).ToArray();
            int pairs = 0, apartPairs = 0, mismatches = 0; float maxPair = 0f; string far = "", diff = "";
            foreach (var w in wanders)
            {
                object before = fPack.GetValue(w);
                try
                {
                    fPack.SetValue(w, (Transform[])homes.Clone());   // what Start builds (w.areaCenter of each pack dog)
                    for (int j = 0; j < 6; j++)
                    {
                        if (j == w.dogIndex) continue;
                        pairs++;
                        bool real = (bool)mApart.Invoke(w, new object[] { j });
                        bool expr = apart(w, w.areaCenter.position, homes[j].position);
                        float hd = horiz(w, w.areaCenter.position, homes[j].position);
                        if (hd > maxPair) { maxPair = hd; far = w.dogIndex + "-" + j; }
                        if (real) apartPairs++;
                        if (real != expr) { mismatches++; diff += " " + w.dogIndex + "-" + j; }
                        if (real) r.notes.Add("initial homes " + w.dogIndex + "-" + j + " apart: Horizontal " + hd.ToString("F2") + " m, other bank " + river._OtherBank(w.areaCenter.position, homes[j].position));
                    }
                }
                finally { fPack.SetValue(w, before); }
            }
            req(pairs == 30 && apartPairs == 0, "PlayHomesApart on the six initial homes: " + apartPairs + " of " + pairs + " ordered pairs apart (want 0; farthest pair " + far + " " + maxPair.ToString("F2") + " m)");
            req(mismatches == 0, "PlayHomesApart = the expression on every initial pair" + (mismatches > 0 ? " (differs:" + diff + ")" : ""));

            // real-method probes with existing scene Transforms as the other home (read only; null when the scene has none)
            var all = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Where(x => (x.hideFlags & HideFlags.HideInHierarchy) == 0 && !homes.Contains(x)).ToArray();
            ProbeReal(r, req, wanders, homes, fPack, mApart, apart, river, all, "same bank 30-80 m", (w, x) =>
            { float hd = Flat(w.areaCenter.position, x.position); return hd >= 30f && hd <= 80f && !river._OtherBank(w.areaCenter.position, x.position); });
            ProbeReal(r, req, wanders, homes, fPack, mApart, apart, river, all, "other bank < 20 m", (w, x) =>
            { float hd = Flat(w.areaCenter.position, x.position); return hd < 20f && river._OtherBank(w.areaCenter.position, x.position); });
        }

        /// <summary>The real PlayHomesApart with one pack slot pointed at an existing scene Transform (the nearest match to dog 0, then
        /// the other dogs): want true, and equal to the expression. A note (not a failure) when the scene has no such Transform.</summary>
        static void ProbeReal(Report r, Action<bool, string> req, KD_DogWanderController[] wanders, Transform[] homes, FieldInfo fPack, MethodInfo mApart,
            Func<KD_DogWanderController, Vector3, Vector3, bool> apart, KH_RiverData river, Transform[] all, string what, Func<KD_DogWanderController, Transform, bool> match)
        {
            foreach (var w in wanders)
            {
                var hit = all.Where(x => match(w, x)).OrderBy(x => Flat(w.areaCenter.position, x.position)).FirstOrDefault();
                if (hit == null) continue;
                int slot = (w.dogIndex + 1) % 6;
                object before = fPack.GetValue(w);
                try
                {
                    var pack = (Transform[])homes.Clone(); pack[slot] = hit;
                    fPack.SetValue(w, pack);
                    bool real = (bool)mApart.Invoke(w, new object[] { slot });
                    bool expr = apart(w, w.areaCenter.position, hit.position);
                    req(real && expr, "PlayHomesApart real probe " + what + ": dog " + w.dogIndex + " home vs. '" + hit.name + "' " + hit.position.ToString("F1")
                        + " (" + Flat(w.areaCenter.position, hit.position).ToString("F2") + " m, other bank " + river._OtherBank(w.areaCenter.position, hit.position) + ") -> real " + real + " / expression " + expr + " (want True)");
                }
                finally { fPack.SetValue(w, before); }
                return;
            }
            r.notes.Add("real probe " + what + ": no scene Transform matched for any dog (the synthetic case covers the rule)");
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
        static string Side(KH_RiverData river, Vector3 p) { return !river._Covers(p.z) ? "outside the data" : p.x >= river._CenterX(p.z) ? "+x" : "-x"; }

        // ================================================================ RC-15 (W-fixed-kept)

        static void CheckWaterClock(Action<bool, string> req)
        {
            var f = typeof(KD_DogWanderController).GetField("WaterRollInterval", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            object v = f != null && f.IsLiteral ? f.GetRawConstantValue() : null;
            req(v is float fv && Mathf.Abs(fv - 45f) < 1e-4f, "WaterRollInterval = 45 (got " + (v ?? "missing") + ")");

            string src = File.ReadAllText(WaterSource);
            int at = src.IndexOf("void WaterEnd(string reason)", StringComparison.Ordinal);
            int end = at >= 0 ? src.IndexOf("\n        }", at, StringComparison.Ordinal) : -1;
            string body = at >= 0 && end > at ? src.Substring(at, end - at) : "";
            req(body.Contains("_waterNextRoll = Mathf.Max(_waterNextRoll, Time.time + WaterRollInterval)"),
                "WaterEnd writes _waterNextRoll = Mathf.Max(_waterNextRoll, Time.time + WaterRollInterval) (a later clock, the harness's +600 s hold, is kept)");

            var writer = new Regex(@"_waterNextRoll\s*=(?!=)");
            var writes = new List<string>();
            foreach (var path in Directory.GetFiles(WanderSourceDir, "KD_DogWanderController*.cs"))
            {
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    if (writer.IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith("//")) writes.Add(Path.GetFileName(path) + ":" + (i + 1) + " " + lines[i].Trim());
            }
            bool onlyTwo = writes.Count == 2 && writes.Count(x => x.Contains("Mathf.Max(_waterNextRoll, Time.time + WaterRollInterval)")) == 1
                && writes.Count(x => x.Contains("_waterNextRoll = Time.time + WaterRollInterval;")) == 1;
            req(onlyTwo, "the clock's writers are the roll and WaterEnd only (" + writes.Count + ": " + string.Join(" | ", writes) + ")");

            string h = File.ReadAllText(HarnessSource);
            int holds = Regex.Matches(h, @"SetProgramVariable\(""_waterNextRoll"", Time\.time \+ 600f\)").Count;
            req(holds >= 4, "the harness still holds the clock at +600 s (" + holds + " writes: 78 phase 0, swim quiet / W2, fetch, the end of W-reroll-clock)");
            int unheld = Regex.Matches(h, @"if \(c\.name == ""W-reroll-clock""\) wander\[WhDog\]\.SetProgramVariable\(""_waterNextRoll"", 0f\)").Count;
            req(unheld == 1, "only W-reroll-clock runs with the clock not held (" + unheld + " guarded write of 0)");
        }
    }
}
