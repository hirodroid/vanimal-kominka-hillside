using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-ENV-013 (2026-10-12): the wash stand's size per dog (docs/plans/2026-10-01-wash-call-and-height.md §8). A targeted
    /// update (Wash/11 refuses once the premium selection is installed): Wash/22 measures the six dogs' paws and writes the sizes to
    /// each station's KH_WashStationCall (and the pool's "keep the size" flag to KH_WashSelection), Wash/23 checks them. Neither saves.
    ///
    /// Rule (Q1, the Cove's): the rectangle of the four paw capsules (the lowest KH_WashPart_{FrontL,FrontR,HindL,HindR}_N of each leg,
    /// radius included, in the dog's frame) -> length / width = max(paws x 1.5, paws + 2 x 0.05, minimum 0.30 / 0.22), never more than
    /// the table's own 1.3 x 0.7. The table moves along the dog's length (Lift X) so the paws' centre is its centre; it does not move
    /// sideways, so the width also gets 2 x |the paws' sideways centre| (both sides keep the 5 cm; the Cove took that centre as 0).
    /// No dog (Q4): the smallest rectangle holding all six dogs' stands (= the largest dog, the Lab, plus their offsets), so a stand
    /// going back to idle only ever grows. Switch (Q3): the accepted call (the synced heldDog), eased at 1.5 m/s on every client.
    /// The pool (IMP-DOG-044, 2026-10-11 decision): never scaled, the stand keeps 1.3 x 0.7 while it is selected.</summary>
    public static partial class KH_DogWashSetupEditor
    {
        const float StandFactor = 1.5f, StandMargin = 0.05f, StandMinLength = 0.30f, StandMinWidth = 0.22f, StandEaseSpeed = 1.5f;
        const float StandCentreTol = 0.005f, StandValueTol = 0.001f;
        static readonly string[] StandLegs = { "FrontL", "FrontR", "HindL", "HindR" };
        const string StandApplyReport = "Temp/KH_WashStandSize.txt", StandVerifyReport = "Temp/KH_WashStandSizeVerify.txt";

        /// <summary>One paw's horizontal footprint in the dog's frame (x right, z forward, m from the dog root).</summary>
        class StandPaw { public string leg, name, lastName; public float minX, maxX, minZ, maxZ; }
        class StandDog
        {
            public int dog; public string name; public Vector3 rootScale; public float rootTilt;
            public List<StandPaw> paws = new List<StandPaw>();
            public float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            public float Length { get { return maxZ - minZ; } }
            public float Width { get { return maxX - minX; } }
            public float CentreZ { get { return (maxZ + minZ) * 0.5f; } }
            public float CentreX { get { return (maxX + minX) * 0.5f; } }
        }
        /// <summary>One station: its parts and the sizes for it (Lift-local: x along the dog, z across).</summary>
        class StandStation
        {
            public KH_WashStationCall call; public KH_WashSelection sel; public Transform st, lift, table, anchor; public BoxCollider box;
            public float sign; public Vector3[] sizes = new Vector3[0]; public Vector3 idle; public List<string> notes = new List<string>();
        }

        static void StandGuard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorSceneManager.GetActiveScene().path != KH_CourseGroundEditor.ScenePath)
                throw new InvalidOperationException("[KH Wash StandSize] the production scene in idle Edit mode is required (nothing changed)");
            if (typeof(KH_WashStationCall).GetField("standSize") == null || typeof(KH_WashSelection).GetField("bathKeepsSize") == null)
                throw new InvalidOperationException("[KH Wash StandSize] KH_WashStationCall.standSize / KH_WashSelection.bathKeepsSize missing: compile first (nothing changed)");
        }

        // ================================================================ 22

        [MenuItem(Menu + "22 - Apply Stand Sizes")]
        public static void ApplyStandSizes()
        {
            StandGuard();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("[KH Wash StandSize] save or discard unrelated scene edits first (nothing changed)");
            var dogs = MeasureStandDogs();
            var stations = ResolveStandStations(scene, dogs);
            var log = new StringBuilder();
            var problems = stations.SelectMany(x => x.notes.Where(n => n.StartsWith("FAIL")).Select(n => x.st.name + ": " + n)).ToList();
            if (problems.Count > 0) throw new InvalidOperationException("[KH Wash StandSize] preconditions failed, nothing changed:\n" + string.Join("\n", problems));
            int changed = 0;
            foreach (var s in stations)
            {
                bool diff = false;
                var so = new SerializedObject(s.call);
                var pt = Prop(so, "standTable"); if (pt.objectReferenceValue != s.table) { pt.objectReferenceValue = s.table; diff = true; }
                var ps = Prop(so, "standSize");
                if (ps.arraySize != s.sizes.Length) { ps.arraySize = s.sizes.Length; diff = true; }
                for (int i = 0; i < s.sizes.Length; i++)
                {
                    var e = ps.GetArrayElementAtIndex(i);
                    if ((e.vector3Value - s.sizes[i]).sqrMagnitude > 1e-12f) { e.vector3Value = s.sizes[i]; diff = true; }
                }
                diff |= SetV3(so, "standIdle", s.idle);
                diff |= SetV3(so, "standBaseSize", s.box.size);
                diff |= SetV3(so, "standBasePos", s.table.localPosition);
                diff |= SetV3(so, "standBaseScale", s.table.localScale);
                var sp = Prop(so, "standSpeed"); if (Mathf.Abs(sp.floatValue - StandEaseSpeed) > 1e-6f) { sp.floatValue = StandEaseSpeed; diff = true; }
                so.ApplyModifiedProperties();
                Copy(s.call);   // proxy -> Udon every run (idempotent; vrc-udonsharp §4)
                if (s.sel != null)
                {
                    var ss = new SerializedObject(s.sel);
                    var pc = Prop(ss, "call"); if (pc.objectReferenceValue != s.call) { pc.objectReferenceValue = s.call; diff = true; s.notes.Add("selection.call -> " + s.st.name + "/" + CallName); }
                    var want = KeepsSizeArray(s.sel);
                    var pk = Prop(ss, "bathKeepsSize");
                    if (pk.arraySize != want.Length) { pk.arraySize = want.Length; diff = true; }
                    for (int i = 0; i < want.Length; i++) { var e = pk.GetArrayElementAtIndex(i); if (e.boolValue != want[i]) { e.boolValue = want[i]; diff = true; } }
                    ss.ApplyModifiedProperties();
                    Copy(s.sel);
                }
                if (diff) changed++;
                log.AppendLine(StandStationText(s, dogs) + (diff ? "  (written)" : "  (unchanged)"));
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Temp");
            File.WriteAllText(StandApplyReport, DateTime.UtcNow.ToString("o") + "\nIMP-ENV-013 Wash/22 Apply Stand Sizes: stations=" + stations.Count + " changed=" + changed + " (0 on a re-run). NOT saved: run Wash/23, then save, then Wash/12 / Wash/32 / WashPremium/21.\n"
                + StandRuleText() + "\n" + StandDogsText(dogs) + log);
            Debug.Log("[KH Wash StandSize] applied to " + stations.Count + " stations (" + changed + " changed, not saved). " + StandApplyReport + ". Next: Wash/23 Verify, then save.");
        }

        static bool SetV3(SerializedObject so, string field, Vector3 v)
        {
            var p = Prop(so, field);
            if ((p.vector3Value - v).sqrMagnitude <= 1e-12f) return false;
            p.vector3Value = v; return true;
        }

        /// <summary>By item id: true only for the pool (KH_WashSelectionEditor.PoolBathId).</summary>
        static bool[] KeepsSizeArray(KH_WashSelection sel)
        {
            int n = sel.models != null ? sel.models.Length : 0;
            var a = new bool[n];
            if (KH_WashSelectionEditor.PoolBathId < n) a[KH_WashSelectionEditor.PoolBathId] = true;
            return a;
        }

        // ================================================================ 23

        [MenuItem(Menu + "23 - Verify Stand Sizes")]
        public static void VerifyStandSizes()
        {
            StandGuard();
            var scene = EditorSceneManager.GetActiveScene();
            var lines = new List<string>(); int failed = 0;
            Action<bool, string> req = (ok, what) => { lines.Add((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; };
            var dogs = MeasureStandDogs();
            var stations = ResolveStandStations(scene, dogs);
            req(stations.Count == 4, "four stations (" + stations.Count + ")");
            foreach (var s in stations)
            {
                string label = s.st.name;
                foreach (var n in s.notes) if (n.StartsWith("FAIL")) req(false, label + ": " + n.Substring(5));
                VerifyStandStation(s, dogs, req, lines);
            }
            Directory.CreateDirectory("Temp");
            File.WriteAllText(StandVerifyReport, DateTime.UtcNow.ToString("o") + "\nIMP-ENV-013 Wash/23 Verify Stand Sizes: " + (failed == 0 ? "PASS" : "FAIL " + failed) + " (" + lines.Count + " checks; Edit mode, the runtime _StandSize / _StandPose on the proxy, every pose put back)\n"
                + StandRuleText() + "\n" + StandDogsText(dogs) + string.Join("\n", lines)
                + "\nNot covered (Play / PCVR): the easing on screen, a dog's washing pose against the Edit-mode paws (the 5 cm margin absorbs it), the hinoki grain and edges at a small size (Pome X 0.23), tools put on a small stand, two players seeing the same size, Quest.\n");
            if (failed > 0) throw new InvalidOperationException("[KH Wash StandSize] Verify failed (" + failed + "), see " + StandVerifyReport + ":\n" + string.Join("\n", lines.Where(l => l.StartsWith("FAIL")).Take(30)));
            Debug.Log("[KH Wash StandSize] VERIFY_OK (" + lines.Count + " checks) -> " + StandVerifyReport);
        }

        static void VerifyStandStation(StandStation s, StandDog[] dogs, Action<bool, string> req, List<string> lines)
        {
            string label = s.st.name;
            var c = s.call;
            if (s.table == null || s.box == null || s.lift == null || s.anchor == null) return;   // the precondition FAIL is already recorded
            // ---- proxy = a fresh measure ----
            req(c.standTable == s.table, label + ": Call.standTable = " + LiftName + "/" + TableName);
            req(c.standSize != null && c.standSize.Length == dogs.Length, label + ": Call.standSize has " + dogs.Length + " sizes (" + (c.standSize == null ? "null: run Wash/22" : c.standSize.Length.ToString()) + ")");
            bool sizesOk = c.standSize != null && c.standSize.Length == dogs.Length;
            for (int d = 0; sizesOk && d < dogs.Length; d++)
                req(Vector3.Distance(c.standSize[d], s.sizes[d]) <= StandValueTol, label + ": standSize[" + d + " " + dogs[d].name + "] " + V(c.standSize[d]) + " = fresh measure " + V(s.sizes[d]) + " (1 mm)");
            req(Vector3.Distance(c.standIdle, s.idle) <= StandValueTol, label + ": standIdle " + V(c.standIdle) + " = the rectangle of all six " + V(s.idle));
            req((c.standBaseSize - s.box.size).sqrMagnitude < 1e-10f && (c.standBaseSize - TableSize).sqrMagnitude < 1e-8f, label + ": standBaseSize " + c.standBaseSize.ToString("F3") + " = the table collider " + TableSize.ToString("F3"));
            req((c.standBasePos - s.table.localPosition).sqrMagnitude < 1e-10f, label + ": standBasePos " + c.standBasePos.ToString("F4") + " = the table's local position " + s.table.localPosition.ToString("F4") + " (re-run Wash/22 after moving the table)");
            req((c.standBaseScale - s.table.localScale).sqrMagnitude < 1e-10f && (s.table.localScale - Vector3.one).sqrMagnitude < 1e-10f, label + ": standBaseScale = the table's scale = 1 in Edit mode (" + s.table.localScale.ToString("F3") + ")");
            req(Mathf.Abs(c.standSpeed - StandEaseSpeed) < 1e-6f, label + ": standSpeed " + c.standSpeed + " = " + StandEaseSpeed + " m/s");
            // ---- backing (what the runtime reads) ----
            var cb = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            Backing(req, cb, label + ": Call", "standTable", c.standTable);
            Backing(req, cb, label + ": Call", "standSize", c.standSize);
            Backing(req, cb, label + ": Call", "standIdle", c.standIdle);
            Backing(req, cb, label + ": Call", "standBaseSize", c.standBaseSize);
            Backing(req, cb, label + ": Call", "standBasePos", c.standBasePos);
            Backing(req, cb, label + ": Call", "standBaseScale", c.standBaseScale);
            Backing(req, cb, label + ": Call", "standSpeed", c.standSpeed);
            if (s.sel != null)
            {
                var want = KeepsSizeArray(s.sel);
                req(s.sel.call == c, label + ": selection.call = " + label + "/" + CallName + " (it writes standKeep)");
                req(s.sel.bathKeepsSize != null && s.sel.bathKeepsSize.SequenceEqual(want), label + ": selection.bathKeepsSize true only for the pool (item " + KH_WashSelectionEditor.PoolBathId + ")");
                var sb = UdonSharpEditorUtility.GetBackingUdonBehaviour(s.sel);
                Backing(req, sb, label + ": selection", "bathKeepsSize", s.sel.bathKeepsSize);
                Backing(req, sb, label + ": selection", "call", s.sel.call);
                bool keepRule = s.sel._KeepsSize(KH_WashSelectionEditor.PoolBathId) && !s.sel._KeepsSize(-1);
                for (int i = 0; s.sel.models != null && i < s.sel.models.Length; i++) if (i != KH_WashSelectionEditor.PoolBathId && s.sel._KeepsSize(i)) keepRule = false;
                req(keepRule, label + ": selection._KeepsSize: the pool yes, every other item and no platform (-1) no");
            }
            else lines.Add("note " + label + ": no premium selection (no pool here): every platform scales with the dog");

            // ---- the runtime bodies in Edit mode: _StandSize / _StandPose, every dog + idle + the pool ----
            var fHeld = typeof(KH_WashStationCall).GetField("heldDog", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fHeld == null) { req(false, label + ": KH_WashStationCall.heldDog field (reflection)"); return; }
            int h0 = (int)fHeld.GetValue(c); bool k0 = c.standKeep;
            Vector3 p0 = s.table.localPosition, sc0 = s.table.localScale, lift0 = s.lift.localPosition, an0 = s.anchor.position;
            float top0 = TableTopY(s);
            var baseRect = StandRect(s);   // the original 1.3 x 0.7 in Lift local (x0, x1, z0, z1)
            try
            {
                for (int d = -1; d < dogs.Length; d++)
                {
                    fHeld.SetValue(c, d); c.standKeep = false;
                    Vector3 got = c._StandSize();
                    Vector3 want = d < 0 ? c.standIdle : (sizesOk ? c.standSize[d] : Vector3.zero);
                    string who = d < 0 ? "no dog (idle)" : d + " " + dogs[d].name;
                    req((got - want).sqrMagnitude < 1e-12f, label + ": _StandSize() with " + who + " = " + V(got));
                    c._StandPose(got);
                    var r = StandRect(s);
                    req(Mathf.Abs(s.table.localScale.y - sc0.y) < 1e-6f && Mathf.Abs(s.table.localPosition.y - p0.y) < 1e-6f && Mathf.Abs(s.table.localPosition.z - p0.z) < 1e-6f
                        && Mathf.Abs(TableTopY(s) - top0) < 1e-5f && (s.lift.localPosition - lift0).sqrMagnitude < 1e-12f && (s.anchor.position - an0).sqrMagnitude < 1e-12f,
                        label + " " + who + ": height unchanged (top y " + TableTopY(s).ToString("F4") + " = " + top0.ToString("F4") + ", y scale " + s.table.localScale.y + ", the lift and the anchor untouched)");
                    req(r.x >= baseRect.x - 1e-4f && r.y <= baseRect.y + 1e-4f && r.z >= baseRect.z - 1e-4f && r.w <= baseRect.w + 1e-4f,
                        label + " " + who + ": inside the original 1.3 x 0.7 (x " + r.x.ToString("F3") + ".." + r.y.ToString("F3") + ", z " + r.z.ToString("F3") + ".." + r.w.ToString("F3") + ")");
                    // the paws of this dog (idle: of every dog, the "only grows back" guarantee) at the anchor, facing standYaw
                    for (int e = 0; e < dogs.Length; e++)
                    {
                        if (d >= 0 && e != d) continue;
                        float margin, cx, cz; PawsOnStand(s, dogs[e], r, out margin, out cx, out cz);
                        req(margin >= StandMargin - 1e-4f, label + " " + who + ": " + dogs[e].name + "'s paws " + (margin * 100f).ToString("F1") + " cm inside the edge (>= 5)");
                        if (d >= 0) req(Mathf.Abs(cx) <= StandCentreTol, label + " " + who + ": the paws' centre " + (cx * 1000f).ToString("F1") + " mm from the stand's centre along the dog (<= 5; across " + (cz * 1000f).ToString("F1") + " mm, the width covers it)");
                    }
                }
                // the pool keeps 1.3 x 0.7 (with a dog held, too)
                fHeld.SetValue(c, 1); c.standKeep = true;
                Vector3 keep = c._StandSize();
                c._StandPose(keep);
                req(Mathf.Abs(keep.x - s.box.size.x) < 1e-6f && Mathf.Abs(keep.y - s.box.size.z) < 1e-6f && keep.z == 0f && (s.table.localScale - sc0).sqrMagnitude < 1e-12f && (s.table.localPosition - p0).sqrMagnitude < 1e-12f,
                    label + ": standKeep (the pool selected) -> the base size " + V(keep) + " with a dog held, the table at scale 1 and its own place");
            }
            finally
            {
                fHeld.SetValue(c, h0); c.standKeep = k0;
                s.table.localPosition = p0; s.table.localScale = sc0;
            }
            req((s.table.localPosition - p0).sqrMagnitude < 1e-12f && (s.table.localScale - sc0).sqrMagnitude < 1e-12f, label + ": the table put back as it was");
        }

        // ================================================================ measure (nothing changes)

        /// <summary>The six dogs' paws in their own frame (rotation of the dog root only: positions in world metres relative to the root,
        /// so a root scale is kept as the stand sees it). Per leg the capsule reaching lowest is the paw (logged with the highest-N one).</summary>
        static StandDog[] MeasureStandDogs()
        {
            KD_DogWanderController[] wanders; KH_DogWash[] washes;
            DogsByIndex(out wanders, out washes);
            var r = new StandDog[wanders.Length];
            for (int i = 0; i < wanders.Length; i++)
            {
                var w = wanders[i];
                if (w == null || w.dogRoot == null) throw new InvalidOperationException("[KH Wash StandSize] dog " + i + ": no KD_DogWanderController / dogRoot (nothing changed)");
                var root = w.dogRoot;
                var d = new StandDog { dog = i, name = KD_DogDefs.All[i].key, rootScale = root.lossyScale, rootTilt = Vector3.Angle(root.up, Vector3.up) };
                var inv = Quaternion.Inverse(root.rotation);
                var caps = root.GetComponentsInChildren<CapsuleCollider>(true);
                foreach (var leg in StandLegs)
                {
                    string prefix = PartPrefix + leg + "_";
                    var mine = caps.Where(x => x.name.StartsWith(prefix) && int.TryParse(x.name.Substring(prefix.Length), out int _)).ToArray();
                    if (mine.Length == 0) throw new InvalidOperationException("[KH Wash StandSize] " + root.name + ": no " + prefix + "N capsule (Wash/11's part colliders; nothing changed)");
                    CapsuleCollider paw = null; float low = float.MaxValue;
                    foreach (var cap in mine) { Vector3 a, b; float rad; CapsuleEnds(cap, out a, out b, out rad); float y = Mathf.Min(a.y, b.y) - rad; if (y < low) { low = y; paw = cap; } }
                    var last = mine.OrderBy(x => int.Parse(x.name.Substring(prefix.Length))).Last();
                    Vector3 e1, e2; float rr; CapsuleEnds(paw, out e1, out e2, out rr);
                    Vector3 q1 = inv * (e1 - root.position), q2 = inv * (e2 - root.position);
                    var p = new StandPaw { leg = leg, name = paw.name, lastName = last.name,
                        minX = Mathf.Min(q1.x, q2.x) - rr, maxX = Mathf.Max(q1.x, q2.x) + rr, minZ = Mathf.Min(q1.z, q2.z) - rr, maxZ = Mathf.Max(q1.z, q2.z) + rr };
                    d.paws.Add(p);
                    d.minX = Mathf.Min(d.minX, p.minX); d.maxX = Mathf.Max(d.maxX, p.maxX); d.minZ = Mathf.Min(d.minZ, p.minZ); d.maxZ = Mathf.Max(d.maxZ, p.maxZ);
                }
                r[i] = d;
            }
            return r;
        }

        /// <summary>A capsule's two sphere centres and radius in world space (Unity's scaling: the axis by its own scale, the radius by
        /// the larger of the other two).</summary>
        static void CapsuleEnds(CapsuleCollider c, out Vector3 a, out Vector3 b, out float r)
        {
            var t = c.transform; var s = t.lossyScale;
            float ax = Mathf.Abs(s.x), ay = Mathf.Abs(s.y), az = Mathf.Abs(s.z);
            Vector3 axisLocal = c.direction == 0 ? Vector3.right : c.direction == 1 ? Vector3.up : Vector3.forward;
            float axisScale = c.direction == 0 ? ax : c.direction == 1 ? ay : az;
            float radScale = c.direction == 0 ? Mathf.Max(ay, az) : c.direction == 1 ? Mathf.Max(ax, az) : Mathf.Max(ax, ay);
            r = c.radius * radScale;
            float half = Mathf.Max(0f, c.height * axisScale * 0.5f - r);
            Vector3 centre = t.TransformPoint(c.center), axis = t.TransformDirection(axisLocal).normalized;
            a = centre + axis * half; b = centre - axis * half;
        }

        /// <summary>The rule in the dog's frame: (length, width, the paws' centre along the dog). Width includes 2 x |sideways centre|.</summary>
        static Vector3 StandRule(StandDog d)
        {
            float l = Mathf.Max(d.Length * StandFactor, d.Length + 2f * StandMargin, StandMinLength);
            float w = Mathf.Max(d.Width * StandFactor, d.Width + 2f * StandMargin, StandMinWidth) + 2f * Mathf.Abs(d.CentreX);
            return new Vector3(l, w, d.CentreZ);
        }

        /// <summary>Every station's parts, the preconditions (FAIL notes) and the sizes for it. Changes nothing.</summary>
        static List<StandStation> ResolveStandStations(UnityEngine.SceneManagement.Scene scene, StandDog[] dogs)
        {
            var list = new List<StandStation>();
            foreach (var call in AllStationCalls(scene))
            {
                var s = new StandStation { call = call, st = call.transform.parent };
                list.Add(s);
                Action<bool, string> need = (ok, what) => { if (!ok) s.notes.Add("FAIL " + what); };
                s.lift = s.st.Find(LiftName);
                s.table = s.lift != null ? s.lift.Find(TableName) : null;
                s.anchor = s.lift != null ? s.lift.Find(AnchorName) : null;
                var pt = s.st.Find("PremiumTools");
                s.sel = pt != null ? pt.GetComponent<KH_WashSelection>() : null;
                need(s.lift != null && s.table != null && s.anchor != null, LiftName + "/" + TableName + " and " + LiftName + "/" + AnchorName + " present");
                need(s.sel == null || (s.sel.models != null && s.sel.models.Length > KH_WashSelectionEditor.PoolBathId),
                    "selection.models holds the pool (item " + KH_WashSelectionEditor.PoolBathId + "): run WashPremium/49 / 48 first (else bathKeepsSize would come out empty and the pool would scale)");
                if (s.lift == null || s.table == null || s.anchor == null) continue;
                need(call.liftRoot == s.lift && call.anchor == s.anchor, "Call.liftRoot / anchor = " + LiftName + " / " + AnchorName);
                need((s.lift.lossyScale - Vector3.one).sqrMagnitude < 1e-8f && (s.lift.localScale - Vector3.one).sqrMagnitude < 1e-10f, LiftName + " scale 1 (lossy " + s.lift.lossyScale.ToString("F4") + ": sizes are in metres)");
                need((s.table.localScale - Vector3.one).sqrMagnitude < 1e-10f && Quaternion.Angle(s.table.localRotation, Quaternion.identity) < 0.01f, TableName + " scale 1, no rotation in Edit mode (" + s.table.localScale.ToString("F3") + ")");
                var cols = s.table.GetComponentsInChildren<Collider>(true);
                s.box = s.table.GetComponent<BoxCollider>();
                need(s.box != null && cols.Length == 1 && cols[0] == s.box && s.box.enabled && !s.box.isTrigger && s.table.gameObject.layer == StandLayer,
                    TableName + ": one solid BoxCollider on layer " + StandLayer + " and no other collider under it (" + cols.Length + ")");
                if (s.box != null) need(Near(s.box.size, TableSize) && Near(s.box.center, new Vector3(0f, TableSize.y * 0.5f, 0f)), TableName + " collider " + s.box.size.ToString("F3") + " = " + TableSize.ToString("F3") + ", centre on its bottom face");
                // An Animator without a controller does nothing (the premium platforms are FBX instances with the Generic rig's empty
                // Animator); anything else that runs (Udon, constraints, a controller) would act on scaled children
                var all = s.table.GetComponentsInChildren<Behaviour>(true);
                var behaviours = all.Where(b => !(b is Animator an && an.runtimeAnimatorController == null)).ToArray();
                need(behaviours.Length == 0, "no running Behaviour under " + TableName + " (they would act on the scaled children): " + string.Join(", ", behaviours.Take(5).Select(b => b.GetType().Name + " on " + b.name)));
                if (all.Length > behaviours.Length) s.notes.Add("note: " + (all.Length - behaviours.Length) + " Animator(s) without a controller under " + TableName + " (inert; FBX instances)");
                var allowed = new HashSet<Transform>();
                for (int i = 0; i < TableSlats; i++) { var t = s.table.Find("Slat_" + i); if (t != null) allowed.Add(t); }
                var ip = s.table.Find("ImportedPlatform"); if (ip != null) allowed.Add(ip);
                if (s.sel != null && s.sel.models != null) foreach (var m in s.sel.models) if (m != null && m.transform.parent == s.table) allowed.Add(m.transform);
                var strays = s.table.Cast<Transform>().Where(t => !allowed.Contains(t)).Select(t => t.name).ToArray();
                need(strays.Length == 0, TableName + "'s children are the slats, ImportedPlatform and the selection's platforms only (" + string.Join(", ", strays) + ")");
                need(Mathf.Abs(s.anchor.localPosition.x - s.table.localPosition.x) < 1e-3f && Mathf.Abs(s.anchor.localPosition.z - s.table.localPosition.z) < 1e-3f
                    && Mathf.Abs(s.anchor.localPosition.y - (s.table.localPosition.y + TableSize.y)) < 1e-3f, AnchorName + " over the table's top centre (" + s.anchor.localPosition.ToString("F3") + ")");
                float yaw = Mathf.Repeat(s.st.eulerAngles.y + StandYawOffset, 360f);
                need(Mathf.Abs(Mathf.DeltaAngle(call.standYaw, yaw)) < 0.05f, "Call.standYaw " + call.standYaw.ToString("F1") + " = station yaw + " + StandYawOffset + " (Wash/13)");
                Vector3 fwd = Quaternion.Inverse(s.lift.rotation) * Quaternion.Euler(0f, call.standYaw, 0f) * Vector3.forward;
                need(Mathf.Abs(fwd.x) >= 0.999f, "the dog (standYaw) faces along " + LiftName + " X (" + fwd.ToString("F3") + ")");
                s.sign = fwd.x >= 0f ? 1f : -1f;
                // lighting: nothing under the table is static or lightmapped (a scaled lightmapped mesh would keep its baked light); Light Probe lit
                foreach (var t in s.table.GetComponentsInChildren<Transform>(true))
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                    if (flags != 0) s.notes.Add("FAIL static flags " + flags + " on " + t.name + " (must be 0: batching / GI would not follow the scale)");
                }
                foreach (var rend in s.table.GetComponentsInChildren<Renderer>(true))
                {
                    bool baked = rend.lightmapIndex >= 0 && rend.lightmapIndex < 0xFFFE, realtime = rend.realtimeLightmapIndex >= 0 && rend.realtimeLightmapIndex < 0xFFFE;
                    if (baked || realtime) s.notes.Add("FAIL lightmap on " + rend.name + " (index " + rend.lightmapIndex + " / realtime " + rend.realtimeLightmapIndex + ")");
                    if (rend.lightProbeUsage == LightProbeUsage.Off) s.notes.Add("FAIL " + rend.name + " lightProbeUsage Off (the stand is lit by Light Probes)");
                }
                StandBakeryCheck(s);
                // the sizes for this station (Lift local: x = along the dog, sign by standYaw; z = across)
                s.sizes = new Vector3[dogs.Length];
                float x0 = float.MaxValue, x1 = float.MinValue, half = 0f;
                for (int d = 0; d < dogs.Length; d++)
                {
                    var rule = StandRule(dogs[d]);
                    float off = s.sign * rule.z;
                    float w = Mathf.Min(rule.y, TableSize.z), l = Mathf.Min(rule.x, TableSize.x - 2f * Mathf.Abs(off));
                    if (w < rule.y || l < rule.x) s.notes.Add("capped " + dogs[d].name + " to the table (" + rule.x.ToString("F3") + " x " + rule.y.ToString("F3") + " -> " + l.ToString("F3") + " x " + w.ToString("F3") + ")");
                    s.sizes[d] = new Vector3(l, w, off);
                    x0 = Mathf.Min(x0, off - l * 0.5f); x1 = Mathf.Max(x1, off + l * 0.5f); half = Mathf.Max(half, w * 0.5f);
                }
                s.idle = new Vector3(x1 - x0, 2f * half, (x1 + x0) * 0.5f);
            }
            return list;
        }

        /// <summary>Bakery is not in this project (no Assets/Bakery, 0 ftLightmapsStorage in the scene, 2026-10-12). If a storage appears,
        /// any reference from it to the table or below fails (its baked lightmaps would not follow the scale).</summary>
        static void StandBakeryCheck(StandStation s)
        {
            var under = new HashSet<UnityEngine.Object>();
            foreach (var t in s.table.GetComponentsInChildren<Transform>(true)) { under.Add(t.gameObject); foreach (var r in t.GetComponents<Renderer>()) under.Add(r); }
            foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "ftLightmapsStorage") continue;
                var it = new SerializedObject(mb).GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null && under.Contains(it.objectReferenceValue))
                    { s.notes.Add("FAIL Bakery storage " + mb.name + " lists " + it.objectReferenceValue.name + " (" + it.propertyPath + ")"); break; }
            }
        }

        // ================================================================ geometry

        /// <summary>The table's top face in Lift local as (x min, x max, z min, z max), from its transform and collider (no rotation).</summary>
        static Vector4 StandRect(StandStation s)
        {
            Vector3 c = s.table.localPosition + Vector3.Scale(s.table.localScale, s.box.center);
            Vector3 h = Vector3.Scale(s.table.localScale, s.box.size) * 0.5f;
            return new Vector4(c.x - h.x, c.x + h.x, c.z - h.z, c.z + h.z);
        }

        static float TableTopY(StandStation s) { return s.table.TransformPoint(s.box.center + Vector3.up * s.box.size.y * 0.5f).y; }

        /// <summary>The dog standing on the anchor facing standYaw (KD_DogWanderController.UpdateWashStation): the smallest distance from
        /// any paw corner to the stand's edge (m), and the paws' centre minus the stand's centre along the dog (cx) and across (cz).</summary>
        static void PawsOnStand(StandStation s, StandDog d, Vector4 r, out float margin, out float cx, out float cz)
        {
            var rot = Quaternion.Euler(0f, s.call.standYaw, 0f);
            Vector3 a = s.anchor.position;
            float px0 = float.MaxValue, px1 = float.MinValue, pz0 = float.MaxValue, pz1 = float.MinValue;
            foreach (var p in d.paws)
                foreach (var q in new[] { new Vector3(p.minX, 0f, p.minZ), new Vector3(p.maxX, 0f, p.minZ), new Vector3(p.minX, 0f, p.maxZ), new Vector3(p.maxX, 0f, p.maxZ) })
                {
                    Vector3 l = s.lift.InverseTransformPoint(a + rot * q);
                    px0 = Mathf.Min(px0, l.x); px1 = Mathf.Max(px1, l.x); pz0 = Mathf.Min(pz0, l.z); pz1 = Mathf.Max(pz1, l.z);
                }
            margin = Mathf.Min(Mathf.Min(px0 - r.x, r.y - px1), Mathf.Min(pz0 - r.z, r.w - pz1));
            cx = (px0 + px1) * 0.5f - (r.x + r.y) * 0.5f;
            cz = (pz0 + pz1) * 0.5f - (r.z + r.w) * 0.5f;
        }

        // ================================================================ text

        static string V(Vector3 v) { return v.x.ToString("F4") + " x " + v.y.ToString("F4") + " (offset " + v.z.ToString("+0.0000;-0.0000") + ")"; }

        static string StandRuleText()
        {
            return "rule: paws (the lowest " + PartPrefix + "{" + string.Join(",", StandLegs) + "}_N per leg, radius included, dog frame) -> length = max(x " + StandFactor + ", + 2 x " + StandMargin + ", " + StandMinLength
                + "), width = max(x " + StandFactor + ", + 2 x " + StandMargin + ", " + StandMinWidth + ") + 2 x |sideways centre|, capped to " + TableSize.x + " x " + TableSize.z
                + "; the table moves along the dog by the paws' centre; idle = the rectangle of all six; the pool keeps " + TableSize.x + " x " + TableSize.z + "; ease " + StandEaseSpeed + " m/s";
        }

        static string StandDogsText(StandDog[] dogs)
        {
            var b = new StringBuilder("paws (dog frame, m: length along the dog x width across, centre along / across):\n");
            foreach (var d in dogs)
            {
                var rule = StandRule(d);
                b.AppendLine("  " + d.dog + " " + d.name + ": " + d.Length.ToString("F3") + " x " + d.Width.ToString("F3") + ", centre " + d.CentreZ.ToString("+0.0000;-0.0000") + " / " + d.CentreX.ToString("+0.0000;-0.0000")
                    + " -> stand " + rule.x.ToString("F4") + " x " + rule.y.ToString("F4") + " (root scale " + d.rootScale.ToString("F3") + ", tilt " + d.rootTilt.ToString("F1") + " deg)");
                foreach (var p in d.paws) b.AppendLine("      " + p.leg + ": " + p.name + (p.name != p.lastName ? " (lowest; the highest N is " + p.lastName + ")" : "") + " x " + p.minX.ToString("F3") + ".." + p.maxX.ToString("F3") + " z " + p.minZ.ToString("F3") + ".." + p.maxZ.ToString("F3"));
            }
            return b.ToString();
        }

        static string StandStationText(StandStation s, StandDog[] dogs)
        {
            var b = new StringBuilder(s.st.name + " (station " + s.call.stationIndex + ", standYaw " + s.call.standYaw.ToString("F1") + ", dog forward = Lift " + (s.sign > 0 ? "+" : "-") + "X):\n");
            for (int d = 0; d < s.sizes.Length; d++) b.AppendLine("  " + d + " " + dogs[d].name + ": " + V(s.sizes[d]) + " = " + (s.sizes[d].x * s.sizes[d].y / (TableSize.x * TableSize.z) * 100f).ToString("F0") + "% of the table");
            b.AppendLine("  idle: " + V(s.idle));
            b.AppendLine("  pool keeps the size: " + (s.sel != null ? "selection wired (item " + KH_WashSelectionEditor.PoolBathId + ")" : "no selection here"));
            foreach (var n in s.notes) b.AppendLine("  " + n);
            return b.ToString();
        }
    }
}
