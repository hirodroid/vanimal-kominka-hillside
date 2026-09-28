using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>DES-DOG-005 stage 3 M1 (docs/plans/2026-09-28-frisbee-anywhere.md §G): the world-scene side of "frisbee anywhere".
    ///
    /// Course/92 writes the four reachability values (jumpMaxSlopeDeg 15 / jumpMaxRise 0.5 / runMaxSlopeDeg 25 / runMaxStep 0.5,
    /// the code defaults) and playAreaRadius 0 (M4: the circle is gone; the take-out gate is KH_FrisbeeCatch._TakeoutOkAt at the
    /// player's feet) to the six KH_FrisbeeCatch, deletes each disc's old area ring GameObject (`<disc>_Aim/AreaRing`; M4 removed
    /// KH_Frisbee.areaRing - a ring inside a prefab instance is only deactivated) and rewrites the aim hint to
    /// KH_FrisbeePrototypeEditor.AimHint, then saves. playAreaCenter is kept (the harness's plaza anchor; nothing at runtime reads it).
    ///
    /// Course/93 re-checks that configuration and measures the loop with the same static rules the runtime uses
    /// (KH_FrisbeeCatch.PlayableGround / PathGround / SlopeOk + the river band): route nodes every 2 m, points at -3 / 0 / +3 m
    /// across it. Result: Temp/des-dog-005/anywhere.json and one FRISBEE_ANYWHERE_OK | FAIL line. The ratios are observations
    /// for the PCVR round (37-39), not pass criteria; FAIL is a configuration problem or a measurement that could not run.</summary>
    public static class KH_CourseFrisbeeAnywhereEditor
    {
        const string OutDir = "Temp/des-dog-005";
        const float RouteStep = 2f;
        static readonly float[] Across = { -3f, 0f, 3f };
        const int MaxListed = 50;

        [Serializable] class Report
        {
            public string utc, scene, status;
            public int catchers, discs, errors;
            public List<string> findings = new List<string>();
            public int routeSamples, points, noGround, playable, reachable, jumpable;
            public float reachableRatio, jumpableRatio;
            public int pathRefusedSegments;
            public List<string> pathRefusedAt = new List<string>();
            public int bandPoints, bandFloats, bandBankReturn;
            public float bandFloatRatio;
            public int treeInstancesNoCollider, treeObjectsNoCollider;
            public List<string> treePrototypesNoCollider = new List<string>();
            public string values;
            public string limitations = "Static Editor geometry with the runtime's own static rules (PlayableGround / PathGround / SlopeOk / river band). The dog's actual chase, the solve's lanes (LaneClear) and the airborne arc (not checked at all, design I-2) are the harness terrain lane and PCVR 37-39. Collider-less trees: terrain tree prototypes without a Collider in their prefab, plus scene LODGroups named *tree* without any Collider.";
        }

        [MenuItem("Tools/KominkaHillside/Course/92 - Build Frisbee Anywhere")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var catchers = UnityEngine.Object.FindObjectsOfType<KH_FrisbeeCatch>(true);
            var discs = UnityEngine.Object.FindObjectsOfType<KH_Frisbee>(true);
            if (catchers.Length != 6 || discs.Length != 6) throw new InvalidOperationException("expected 6 catchers / 6 discs, found " + catchers.Length + " / " + discs.Length + " (nothing changed)");
            // Everything that could refuse is checked BEFORE anything changes.
            foreach (var d in discs)
            {
                if (d.aimHint == null || d.aimHint.GetComponent<TMPro.TextMeshPro>() == null) throw new InvalidOperationException(d.name + ": aimHint has no TextMeshPro (nothing changed)");
                string glyphs = MissingGlyphs(d.aimHint.GetComponent<TMPro.TextMeshPro>());
                if (glyphs.Length > 0) throw new InvalidOperationException(d.name + ": the hint font lacks " + glyphs + " for \"" + KH_FrisbeePrototypeEditor.AimHint + "\" (add them to the atlas first; nothing changed)");
            }
            foreach (var c in catchers)
                if (new SerializedObject(c).FindProperty("jumpMaxSlopeDeg") == null) throw new InvalidOperationException("KH_FrisbeeCatch.jumpMaxSlopeDeg missing (compile first; nothing changed)");
            var scene = catchers[0].gameObject.scene;
            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-frisbee-anywhere.unity", true);
            foreach (var c in catchers)
            {
                var so = new SerializedObject(c);
                so.FindProperty("jumpMaxSlopeDeg").floatValue = KH_FrisbeeCatch.DefaultJumpMaxSlopeDeg;
                so.FindProperty("jumpMaxRise").floatValue = KH_FrisbeeCatch.DefaultJumpMaxRise;
                so.FindProperty("runMaxSlopeDeg").floatValue = KH_FrisbeeCatch.DefaultRunMaxSlopeDeg;
                so.FindProperty("runMaxStep").floatValue = KH_FrisbeeCatch.DefaultRunMaxStep;
                so.FindProperty("playAreaRadius").floatValue = 0f;   // M4 (design F)
                so.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(c); EditorUtility.SetDirty(c);
            }
            foreach (var d in discs)
            {
                var ring = OldRing(d);
                if (ring != null)
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(ring)) { if (ring.activeSelf) { ring.SetActive(false); EditorUtility.SetDirty(ring); } }
                    else UnityEngine.Object.DestroyImmediate(ring);
                }
                var tmp = d.aimHint.GetComponent<TMPro.TextMeshPro>();
                if (tmp.text != KH_FrisbeePrototypeEditor.AimHint) { tmp.text = KH_FrisbeePrototypeEditor.AimHint; EditorUtility.SetDirty(tmp); }
            }
            var recalls = UnityEngine.Object.FindObjectsOfType<KH_ToyRecall>(true);
            int purged = PurgeRemovedM4Vars(catchers.Cast<UdonSharpBehaviour>().Concat(discs).Concat(recalls), false, null);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Frisbee anywhere save failed");
            var findings = new List<string>();
            CheckConfig(catchers, discs, findings);
            if (findings.Count > 0) throw new InvalidOperationException("[KH Frisbee Anywhere] FRISBEE_ANYWHERE_BUILD_FAIL " + string.Join(" | ", findings));
            Debug.Log("[KH Frisbee Anywhere] FRISBEE_ANYWHERE_BUILD_OK catchers=6 discs=6 values=" + Values() + " playAreaRadius 0, old rings removed, stale M4 variables purged=" + purged + ", hint \"" + KH_FrisbeePrototypeEditor.AimHint + "\"");
        }

        [MenuItem("Tools/KominkaHillside/Course/93 - Verify Frisbee Anywhere")]
        public static void Verify()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Edit mode only");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != KH_CourseGroundEditor.ScenePath) throw new InvalidOperationException("Open " + KH_CourseGroundEditor.ScenePath + " first");
            var r = new Report { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, values = Values() };
            var catchers = UnityEngine.Object.FindObjectsOfType<KH_FrisbeeCatch>(true);
            var discs = UnityEngine.Object.FindObjectsOfType<KH_Frisbee>(true);
            r.catchers = catchers.Length; r.discs = discs.Length;
            CheckConfig(catchers, discs, r.findings);
            var river = UnityEngine.Object.FindObjectsOfType<KH_RiverData>(true).FirstOrDefault();
            var spot = UnityEngine.Object.FindObjectsOfType<KH_WaterSpot>(true).FirstOrDefault();
            if (river == null || !river._Ready()) r.findings.Add("KH_RiverData missing or empty (Course/73)");
            var route = UnityEngine.Object.FindObjectsOfType<KH_WalkRoute>(true).FirstOrDefault(w => w.closedLoop && w.nodePositions != null && w.nodePositions.Length > 2);
            if (route == null) r.findings.Add("no closed-loop KH_WalkRoute with nodePositions (Course route build)");
            LayerMask mask = catchers.Length > 0 ? catchers[0].groundLayerMask : (LayerMask)((1 << 0) | (1 << 11));
            if (route != null && river != null) Measure(r, route, river, spot, mask);
            CountTrees(r);
            if (r.routeSamples == 0 && route != null) r.findings.Add("the route produced no samples");
            r.errors = r.findings.Count; r.status = r.errors == 0 ? "FRISBEE_ANYWHERE_OK" : "FRISBEE_ANYWHERE_FAIL";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/anywhere.json", JsonUtility.ToJson(r, true));
            string line = "[KH Frisbee Anywhere] " + r.status + " reachable=" + r.reachable + "/" + r.points + " (" + (r.reachableRatio * 100f).ToString("F1") + "%)"
                + " jumpable=" + r.jumpable + " (" + (r.jumpableRatio * 100f).ToString("F1") + "%) noGround=" + r.noGround
                + " pathRefusedSegments=" + r.pathRefusedSegments + "/" + Mathf.Max(0, r.routeSamples - 1)
                + " band=" + r.bandPoints + " floats=" + r.bandFloats + " bank=" + r.bandBankReturn
                + " treesNoCollider=" + r.treeInstancesNoCollider + "+" + r.treeObjectsNoCollider
                + " errors=" + r.errors + " -> " + OutDir + "/anywhere.json";
            if (r.errors > 0) Debug.LogError(line + " | " + string.Join(" | ", r.findings));
            else Debug.Log(line);
        }

        static string Values()
        {
            return "jumpMaxSlopeDeg " + KH_FrisbeeCatch.DefaultJumpMaxSlopeDeg + " / jumpMaxRise " + KH_FrisbeeCatch.DefaultJumpMaxRise
                + " / runMaxSlopeDeg " + KH_FrisbeeCatch.DefaultRunMaxSlopeDeg + " / runMaxStep " + KH_FrisbeeCatch.DefaultRunMaxStep
                + " (RunMaxDrop " + KH_FrisbeeCatch.RunMaxDrop + ", SolveRayCap " + KH_FrisbeeCatch.SolveRayCap + ")";
        }

        static string MissingGlyphs(TMPro.TextMeshPro tmp)
        {
            if (tmp.font == null) return "(no font)";
            List<char> missing;
            if (tmp.font.HasCharacters(KH_FrisbeePrototypeEditor.AimHint, out missing) || missing == null) return "";
            return string.Join("", missing.Distinct().Select(ch => ch.ToString()).ToArray());
        }

        // The configuration Course/92 promises (also the first half of Course/93).
        static void CheckConfig(KH_FrisbeeCatch[] catchers, KH_Frisbee[] discs, List<string> findings)
        {
            if (catchers.Length != 6 || discs.Length != 6) findings.Add("expected 6 catchers / 6 discs, found " + catchers.Length + " / " + discs.Length);
            foreach (var c in catchers)
            {
                string p = KH_FrisbeePrototypeEditor.AnywhereProblems(c);
                if (p.Length > 0) findings.Add(c.name + ": " + p + " (run Course/92)");
                // M4 (design F): no circle. Proxy and backing, since Course/92 writes it through the proxy.
                object rb; var cb = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
                if (c.playAreaRadius != 0f || cb == null || !cb.publicVariables.TryGetVariableValue("playAreaRadius", out rb) || !(rb is float) || (float)rb != 0f)
                    findings.Add(c.name + ": playAreaRadius is not 0 on the proxy and the backing (run Course/92; M4 removed the circle)");
            }
            var stale = new List<string>();
            if (PurgeRemovedM4Vars(catchers.Cast<UdonSharpBehaviour>().Concat(discs).Concat(UnityEngine.Object.FindObjectsOfType<KH_ToyRecall>(true)), true, stale) > 0)
                findings.Add("removed M4 fields still in the backing publicVariables (run Course/92): " + string.Join(", ", stale.Take(6).ToArray()));
            foreach (var d in discs)
            {
                var ring = OldRing(d);
                if (ring != null && ring.activeSelf) findings.Add(d.name + ": the old area ring GameObject is still there and active (Course/92 removes it)");
                var tmp = d.aimHint == null ? null : d.aimHint.GetComponent<TMPro.TextMeshPro>();
                if (tmp == null || tmp.text != KH_FrisbeePrototypeEditor.AimHint) findings.Add(d.name + ": aim hint is \"" + (tmp == null ? "(none)" : tmp.text) + "\", want \"" + KH_FrisbeePrototypeEditor.AimHint + "\"");
                else { string g = MissingGlyphs(tmp); if (g.Length > 0) findings.Add(d.name + ": hint font lacks " + g); }
                if (d.catcher == null) findings.Add(d.name + ": no catcher");
            }
        }

        // M4 removed public fields; their values stay in each backing UdonBehaviour's publicVariables until something rewrites
        // it. UdonSharp's scene hooks do that for EVERY loaded scene when another scene is opened (Frisbee/4 / 5 open the prototype
        // additively), which left the saved world scene dirty after Frisbee/4 / 5 (2026-09-28). Course/92 purges them and saves,
        // Frisbee/4 purges the prototype's, Course/93 reports any left. Returns how many were removed (dry = count only).
        static readonly string[][] RemovedM4Vars = {
            new[] { "KH_Frisbee", "areaRing", "areaRingPoints", "areaRingLift" },
            new[] { "KH_FrisbeeCatch", "playAreaMargin" },
            new[] { "KH_ToyRecall", "discTakeoutMargin" } };
        public static int PurgeRemovedM4Vars(IEnumerable<UdonSharpBehaviour> behaviours, bool dry, List<string> where)
        {
            int n = 0;
            foreach (var u in behaviours)
            {
                if (u == null) continue;
                var row = RemovedM4Vars.FirstOrDefault(r => r[0] == u.GetType().Name);
                if (row == null) continue;
                var b = UdonSharpEditorUtility.GetBackingUdonBehaviour(u);
                if (b == null || b.publicVariables == null) continue;
                for (int i = 1; i < row.Length; i++)
                {
                    Type t;
                    if (!b.publicVariables.TryGetVariableType(row[i], out t)) continue;
                    n++; if (where != null) where.Add(u.name + "." + row[i]);
                    if (!dry) { b.publicVariables.RemoveVariable(row[i]); EditorUtility.SetDirty(b); }
                }
            }
            return n;
        }

        // The old play-area ring (removed in M4) under the disc's aim root, the hint's parent (KH_FrisbeePrototypeEditor.BuildDisc).
        static GameObject OldRing(KH_Frisbee d)
        {
            var root = d.aimHint != null ? d.aimHint.parent : null;
            var t = root != null ? root.Find("AreaRing") : null;
            return t != null ? t.gameObject : null;
        }

        // The runtime's two water rules (KH_FrisbeeCatch.InRunRiver / InJumpWater, M1 decision 2026-09-28): the dog may run
        // in the water-play spot (the pre-M1 exception) but never take off / land a jump in the band or the spot.
        static bool InRunRiver(KH_RiverData river, KH_WaterSpot spot, Vector3 p)
        {
            if (river == null || !river._InBand(p, KH_RiverData.BandMargin)) return false;
            return spot == null || !spot._Contains(p);
        }
        static bool InJumpWater(KH_RiverData river, KH_WaterSpot spot, Vector3 p)
        {
            if (river != null && river._InBand(p, KH_RiverData.BandMargin)) return true;
            return spot != null && spot._Contains(p);
        }

        static bool Ground(Vector3 p, LayerMask mask, out RaycastHit hit)
        {
            return Physics.Raycast(new Vector3(p.x, p.y + 3f, p.z), Vector3.down, out hit, 8f, mask, QueryTriggerInteraction.Ignore);
        }

        static void Measure(Report r, KH_WalkRoute route, KH_RiverData river, KH_WaterSpot spot, LayerMask mask)
        {
            var nodes = route.nodePositions;
            bool havePrev = false; Vector3 prev = Vector3.zero;
            for (int i = 0; i < nodes.Length; i++)
            {
                Vector3 a = nodes[i], b = nodes[(i + 1) % nodes.Length];
                Vector3 seg = b - a; seg.y = 0f;
                float len = seg.magnitude;
                if (len < 0.01f) continue;
                Vector3 dir = seg / len, side = Vector3.Cross(Vector3.up, dir);
                for (float s = 0f; s < len; s += RouteStep)
                {
                    Vector3 c = Vector3.Lerp(a, b, s / len);
                    RaycastHit hc;
                    if (!Ground(c, mask, out hc)) { r.noGround++; havePrev = false; continue; }
                    Vector3 centre = hc.point;
                    r.routeSamples++;
                    // The route itself, 2 m at a time: the walk the dog would do along the road.
                    if (havePrev)
                    {
                        bool ok = KH_FrisbeeCatch.PathGround(prev, centre, mask, KH_FrisbeeCatch.DefaultRunMaxStep, KH_FrisbeeCatch.RunMaxDrop);
                        if (!ok)
                        {
                            r.pathRefusedSegments++;
                            if (r.pathRefusedAt.Count < MaxListed) r.pathRefusedAt.Add(prev.ToString("F1") + " -> " + centre.ToString("F1"));
                        }
                    }
                    prev = centre; havePrev = true;
                    foreach (float off in Across)
                    {
                        Vector3 q = c + side * off;
                        r.points++;
                        RaycastHit hq;
                        if (!Ground(q, mask, out hq)) { r.noGround++; continue; }
                        Vector3 g = hq.point;
                        bool band = river._InBand(g, KH_RiverData.BandMargin);
                        if (band)
                        {
                            r.bandPoints++;
                            bool floats = river._FloatsAt(g) || (spot != null && spot._FloatsAt(g));
                            if (floats) r.bandFloats++; else r.bandBankReturn++;
                        }
                        bool playable = !InRunRiver(river, spot, g) && KH_FrisbeeCatch.PlayableGround(g, mask, 0.5f, 4f, KH_FrisbeeCatch.DefaultRunMaxSlopeDeg);
                        if (!playable) continue;
                        r.playable++;
                        bool crosses = false;
                        Vector3 d = g - centre; d.y = 0f; float dl = d.magnitude;
                        for (float t = 1f; t < dl && !crosses; t += KH_FrisbeeCatch.PathStep) crosses = InRunRiver(river, spot, centre + d / dl * t);
                        if (crosses || !KH_FrisbeeCatch.PathGround(centre, g, mask, KH_FrisbeeCatch.DefaultRunMaxStep, KH_FrisbeeCatch.RunMaxDrop)) continue;
                        r.reachable++;
                        bool deck = river._OnBridgeDeck(g);
                        if (!deck && !InJumpWater(river, spot, g) && KH_FrisbeeCatch.SlopeOk(hq.normal.y, KH_FrisbeeCatch.DefaultJumpMaxSlopeDeg)) r.jumpable++;
                    }
                }
            }
            r.reachableRatio = r.points > 0 ? (float)r.reachable / r.points : 0f;
            r.jumpableRatio = r.points > 0 ? (float)r.jumpable / r.points : 0f;
            r.bandFloatRatio = r.bandPoints > 0 ? (float)r.bandFloats / r.bandPoints : 0f;
        }

        // Design I-3: trees the dog would run through. Terrain trees whose prototype prefab has no Collider, and scene LODGroups
        // named like a tree with no Collider anywhere under them.
        static void CountTrees(Report r)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                var data = t.terrainData;
                if (data == null) continue;
                var protos = data.treePrototypes;
                var noCol = new bool[protos.Length];
                for (int i = 0; i < protos.Length; i++)
                {
                    noCol[i] = protos[i].prefab == null || protos[i].prefab.GetComponentsInChildren<Collider>(true).Length == 0;
                    if (noCol[i] && protos[i].prefab != null && !r.treePrototypesNoCollider.Contains(protos[i].prefab.name)) r.treePrototypesNoCollider.Add(protos[i].prefab.name);
                }
                foreach (var inst in data.treeInstances)
                    if (inst.prototypeIndex >= 0 && inst.prototypeIndex < noCol.Length && noCol[inst.prototypeIndex]) r.treeInstancesNoCollider++;
            }
            foreach (var lod in UnityEngine.Object.FindObjectsOfType<LODGroup>(true))
            {
                if (lod.name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (lod.GetComponentsInChildren<Collider>(true).Length == 0) r.treeObjectsNoCollider++;
            }
        }
    }
}
