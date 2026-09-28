using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // IMP-DOG-021 (DES-DOG-004 §E-1): the river-wide fetch data, baked into the existing KH_Dogs/KH_RiverData (no new U# class, no
    // new wiring: the 6 dogs, the 6 Fetch behaviours and KH_ToyRecall already hold it). Production scene only, idle Edit mode, saved.
    //   90 Build : riverbed grid along the whole river (0.5 m, rows in z; each row centre +- (half width + BankExtra 2 m), the
    //              saved Terrain's SampleHeight, NoBed where there is no terrain) and the bank slices every 1.5 m in z, both sides:
    //              bank point (first NavMesh point 1-4 m beyond the band edge, outside the band), return point (2.5 m further
    //              inland on the NavMesh), ok = exit slope <= 25 deg + complete NavMesh path from the nearest route node to the
    //              return point + no stone collider (layer 11, not Terrain) in the water between the centre and that bank +
    //              outside the bridge zones (bridge z +- 8 m, + half a slice). The play spot's water-slope limit is not applied
    //              (a ball only floats there). Also writes the tuning (fetchWadeRatio 0.85, bankReach 30 m, bridge zone 8 m).
    //              Arrays are written on the proxy and copied to the Udon heap (UdonSharpEditorUtility.CopyProxyToUdon, vrc-udonsharp
    //              §4); the arrays are ~33 k values, so SerializedProperty per element is avoided. Saves the scene.
    //   91 Verify: never writes the scene. The slice list with each check, the grid against the saved Terrain (<= 0.01 m) and
    //              against the play spot's grid, missing cells inside the band (0 = PASS), ok banks on the NavMesh outside the band
    //              with a complete path, no ok bank in a bridge zone, the stretches where a ball would not float for want of a bank
    //              (recorded), the capacity of every array in bytes, the tuning values and the wiring.
    // Results: Temp/imp-dog-021/river-fetch-build.json, river-fetch-verify.json.
    public static class KH_RiverFetchEditor
    {
        const string OutDir = "Temp/imp-dog-021";
        const string DogsRoot = "KH_Dogs", NodeName = "KH_RiverData";
        const float GridZ0 = 0f, GridZ1 = 720f, GridStep = .5f;
        const float SliceZ0 = 0f, SliceStep = 1.5f;
        const float ReturnOut = 2.5f, MaxExitSlopeDeg = 25f, TolBed = .01f;
        const int GroundMask = (1 << 0) | (1 << 11);
        // Rough raw budgets of design §E-1 (the scene serialisation and the bundle add their own overhead; the build size itself is
        // Course/17 / 18 against the +0.2 MiB of IMP-DOG-021 step 5)
        const int GridBudgetBytes = 130 * 1024, SliceBudgetBytes = 30 * 1024;

        [Serializable] public class Slice
        {
            public float z; public int side;
            public Vector3 bank, ret;
            public float exitSlopeDeg, pathFrom = -1f;
            public bool bridgeOk, exitOk, returnOk, pathOk, stonesOk, ok;
            public string stone;
        }
        [Serializable] public class Gap { public float z0, z1; public int section; }
        [Serializable] public class BuildReport
        {
            public string status, utc, scene, reason;
            public int rows, cells, missingCells, slices, okMinus, okPlus;
            public int gridBytes, sliceBytes;
            public float[] bridgeZ;
            public List<Gap> noBank = new List<Gap>();
        }
        [Serializable] public class VerifyReport
        {
            public string status, utc, scene;
            public int errors, rows, cells, missingInBand, slices, okMinus, okPlus, sliceMismatches, okInBridgeZone, okNotOnNav, okNoPath;
            public float maxBedError, maxSpotDiff, floatCoverage, bandLength;
            public Vector3 worstBedAt;
            public int gridBytes, rowBytes, sliceBytes, totalBytes;
            public float fetchWadeRatio, bankReach, bridgeZoneHalfZ;
            public int wanderWired, fetchWired, toyRecallWired;
            public List<Gap> noBank = new List<Gap>();
            public List<Slice> slices_ = new List<Slice>();
            public List<string> findings = new List<string>(), notes = new List<string>();
            public string tolerances = "grid vs saved Terrain <= 0.01 m; missing riverbed cells inside the band (centre +- (half width + 1 m)) = 0; every ok bank on the NavMesh (0.3 m) outside the band with a complete path from the nearest route node; no ok bank in a bridge zone; stored slices = recomputed slices.";
            public string capacity = "Raw bytes of the baked arrays (float / int 4, Vector3 12, bool 1). The scene and bundle serialisation add overhead; the build-size budget (+0.2 MiB) is measured with Course/17 / 18.";
            public string limitations = "Static data and saved navigation only. Runtime floating and retrieval: ClientSim harness Course/88 (88b = the IMP-DOG-021 cases) and PCVR.";
        }

        [MenuItem("Tools/KominkaHillside/Course/90 - Build River Fetch Data")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var data = Data();
            if (!data._Ready()) throw new InvalidOperationException("KH_RiverData not built: run Course/73 first");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(data);
            if (backing == null) throw new InvalidOperationException("KH_RiverData backing UdonBehaviour missing (compile U# first)");
            var route = Route();
            var terrains = UnityEngine.Object.FindObjectsOfType<Terrain>();

            // ---- riverbed grid ----
            int rows = Mathf.RoundToInt((GridZ1 - GridZ0) / GridStep) + 1;
            var rowX0 = new float[rows]; var rowStart = new int[rows + 1]; var bed = new List<float>(rows * 24);
            for (int r = 0; r < rows; r++)
            {
                float z = GridZ0 + r * GridStep, cx = data._CenterX(z), half = data._HalfWidth(z) + KH_RiverData.BankExtra;
                int n = Mathf.CeilToInt(2f * half / GridStep) + 1;
                rowX0[r] = cx - half; rowStart[r] = bed.Count;
                for (int i = 0; i < n; i++)
                {
                    float g = Ground(terrains, rowX0[r] + i * GridStep, z);
                    if (g <= KH_RiverData.NoBed + 1f) report.missingCells++;
                    bed.Add(g);
                }
            }
            rowStart[rows] = bed.Count;

            // ---- bank slices ----
            var slices = ComputeSlices(data, route);
            int count = slices.Count / 2;
            var bank = new Vector3[slices.Count]; var ret = new Vector3[slices.Count]; var ok = new bool[slices.Count];
            for (int k = 0; k < slices.Count; k++) { bank[k] = slices[k].bank; ret[k] = slices[k].ret; ok[k] = slices[k].ok; }

            // ---- write (proxy, then the Udon heap) ----
            // Undo on both sides, as 73 / 76 (Codex review 2026-09-28 中)
            Undo.RecordObjects(new UnityEngine.Object[] { data, backing }, "Build River Fetch Data");
            data.fetchZ0 = GridZ0; data.fetchStep = GridStep; data.fetchRows = rows;
            data.fetchRowX0 = rowX0; data.fetchRowStart = rowStart; data.fetchBed = bed.ToArray();
            data.sliceZ0 = SliceZ0; data.sliceStep = SliceStep;
            data.sliceBank = bank; data.sliceReturn = ret; data.sliceOk = ok;
            data.fetchWadeRatio = KH_RiverData.DefaultFetchWadeRatio;
            data.bankReach = KH_RiverData.DefaultBankReach;
            data.bridgeZoneHalfZ = KH_RiverData.DefaultBridgeZoneHalfZ;
            EditorUtility.SetDirty(data);
            UdonSharpEditorUtility.CopyProxyToUdon(data);
            if (backing.SyncMethod != Networking.SyncType.None) { backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing); }
            EditorUtility.SetDirty(backing);

            report.rows = rows; report.cells = bed.Count; report.slices = count;
            report.okMinus = slices.Count(s => s.ok && s.side < 0); report.okPlus = slices.Count(s => s.ok && s.side > 0);
            report.gridBytes = bed.Count * 4 + rows * 4 + (rows + 1) * 4; report.sliceBytes = slices.Count * (12 + 12 + 1);
            report.bridgeZ = data.bridgeZ; report.noBank = NoBankGaps(data);
            EditorSceneManager.MarkSceneDirty(data.gameObject.scene);
            if (!EditorSceneManager.SaveScene(data.gameObject.scene)) throw new IOException("River fetch data scene save failed");
            report.status = "BUILT"; report.reason = "Saved. Run 91 - Verify River Fetch Data (saved state) separately.";
            Write("river-fetch-build.json", JsonUtility.ToJson(report, true));
            Debug.Log("[KH River Fetch] built rows=" + rows + " cells=" + bed.Count + " missing=" + report.missingCells + " slices=" + count
                + " ok -x=" + report.okMinus + " +x=" + report.okPlus + " grid=" + report.gridBytes + " B slices=" + report.sliceBytes + " B noBankGaps=" + report.noBank.Count);
        }

        static KH_RiverData Data()
        {
            var node = GameObject.Find(DogsRoot + "/" + NodeName);
            var data = node == null ? null : node.GetComponent<KH_RiverData>();
            if (data == null) throw new InvalidOperationException("KH_Dogs/KH_RiverData missing; run Course/73 first");
            return data;
        }

        static KH_WalkRoute Route()
        {
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (pouches.Length != 1 || pouches[0].manager == null || pouches[0].manager.route == null) throw new InvalidOperationException("Exactly one wired menu (with the route) required");
            var route = pouches[0].manager.route;
            if (route.nodePositions == null || route.nodePositions.Length < 10) throw new InvalidOperationException("Route nodes missing");
            return route;
        }

        /// <summary>Slices every 1.5 m, two per z (index 2k = -x side, 2k + 1 = +x side), each checked on its own (Course/76 rules).</summary>
        static List<Slice> ComputeSlices(KH_RiverData data, KH_WalkRoute route)
        {
            var list = new List<Slice>(); var path = new NavMeshPath();
            int count = Mathf.FloorToInt((GridZ1 - SliceZ0) / SliceStep) + 1;
            float zoneHalf = KH_RiverData.DefaultBridgeZoneHalfZ;
            for (int k = 0; k < count; k++)
            {
                float z = SliceZ0 + k * SliceStep, cx = data._CenterX(z), edge = data._HalfWidth(z) + KH_RiverData.BandMargin, surface = data._SurfaceY(z);
                foreach (int side in new[] { -1, 1 })
                {
                    var sl = new Slice { z = z, side = side };
                    sl.bridgeOk = data.bridgeZ == null || data.bridgeZ.All(b => Mathf.Abs(z - b) > zoneHalf + SliceStep * .5f);
                    for (float o = 1f; o <= 4.001f && !sl.exitOk; o += .25f)
                    {
                        Vector3 e;
                        if (OnBank(data, new Vector3(cx + side * (edge + o), 0, z), .3f, out e)) { sl.bank = e; sl.exitOk = true; }
                    }
                    if (sl.exitOk)
                    {
                        RaycastHit g;
                        if (Physics.Raycast(sl.bank + Vector3.up, Vector3.down, out g, 3, GroundMask, QueryTriggerInteraction.Ignore)) sl.exitSlopeDeg = Vector3.Angle(g.normal, Vector3.up);
                        if (sl.exitSlopeDeg > MaxExitSlopeDeg) sl.exitOk = false;
                    }
                    if (sl.exitOk)
                    {
                        Vector3 r; sl.returnOk = OnBank(data, sl.bank + new Vector3(side * ReturnOut, 0, 0), 1f, out r); sl.ret = r;
                        if (sl.returnOk)
                        {
                            var node = NearestNode(route, sl.ret); NavMeshHit from, to;
                            sl.pathOk = NavMesh.SamplePosition(node, out from, 1.5f, NavMesh.AllAreas) && NavMesh.SamplePosition(sl.ret, out to, .3f, NavMesh.AllAreas)
                                && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                            if (sl.pathOk) sl.pathFrom = Flat(node - sl.ret);
                        }
                    }
                    // Stones in the water between the centre and this bank (the dog wades straight to / from the bank point)
                    float width = sl.exitOk ? Mathf.Abs(sl.bank.x - cx) : edge;
                    var box = Physics.OverlapBox(new Vector3(cx + side * width * .5f, surface, z), new Vector3(width * .5f, 1.5f, SliceStep * .5f), Quaternion.identity, 1 << 11, QueryTriggerInteraction.Ignore);
                    var stone = box.FirstOrDefault(c => !(c is TerrainCollider));
                    sl.stonesOk = stone == null; if (stone != null) sl.stone = stone.transform.root.name + "/" + stone.name;
                    sl.ok = sl.bridgeOk && sl.exitOk && sl.returnOk && sl.pathOk && sl.stonesOk;
                    if (!sl.ok) { if (!sl.exitOk) sl.bank = Vector3.zero; if (!sl.returnOk) sl.ret = Vector3.zero; }
                    list.Add(sl);
                }
            }
            return list;
        }

        static Vector3 NearestNode(KH_WalkRoute route, Vector3 p)
        {
            Vector3 best = route.nodePositions[0]; float bestD = float.MaxValue;
            foreach (var n in route.nodePositions) { float d = Flat(n - p); if (d < bestD) { bestD = d; best = n; } }
            return best;
        }

        /// <summary>NavMesh point near (x, z) at ground height, outside the river band (Course/76's rule, on the baked data).</summary>
        static bool OnBank(KH_RiverData data, Vector3 xz, float radius, out Vector3 nav)
        {
            nav = Vector3.zero; RaycastHit g; NavMeshHit h;
            float s = data._SurfaceY(xz.z);
            if (!Physics.Raycast(new Vector3(xz.x, s + 10, xz.z), Vector3.down, out g, 30, GroundMask, QueryTriggerInteraction.Ignore)) return false;
            if (!NavMesh.SamplePosition(g.point, out h, radius, NavMesh.AllAreas)) return false;
            if (data._InBand(h.position, KH_RiverData.BandMargin)) return false;
            nav = h.position; return true;
        }

        /// <summary>Stretches (outside the bridge zones) where the river centre has no usable bank within reach: a ball there is
        /// returned to the bank instead of floating (E-1).</summary>
        static List<Gap> NoBankGaps(KH_RiverData data)
        {
            var gaps = new List<Gap>(); Gap open = null;
            for (float z = GridZ0; z <= GridZ1 + 1e-3f; z += 1f)
            {
                bool bridge = data._InBridgeZone(z, 0f);
                var c = new Vector3(data._CenterX(z), data._SurfaceY(z) - .05f, z);
                bool none = !bridge && data._NearestSlice(c, 0, data._BankReach(), -1) < 0;
                if (none) { if (open == null) { open = new Gap { z0 = z, z1 = z, section = data._Section(z) }; gaps.Add(open); } else open.z1 = z; }
                else open = null;
            }
            return gaps;
        }

        static float Ground(Terrain[] terrains, float x, float z)
        {
            foreach (var t in terrains)
            {
                var p = t.transform.position; var s = t.terrainData.size;
                if (x >= p.x && x <= p.x + s.x && z >= p.z && z <= p.z + s.z) return t.SampleHeight(new Vector3(x, 0, z)) + p.y;
            }
            return KH_RiverData.NoBed;
        }

        [MenuItem("Tools/KominkaHillside/Course/91 - Verify River Fetch Data")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var data = Data();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(data);
            if (backing == null || backing.SyncMethod != Networking.SyncType.None) r.findings.Add("KH_RiverData backing missing or not sync None");
            if (!data._FetchReady()) throw new InvalidOperationException("River fetch data not built (run Course/90): arrays missing or inconsistent");
            var terrains = UnityEngine.Object.FindObjectsOfType<Terrain>();
            r.rows = data.fetchRows; r.cells = data.fetchBed.Length;
            for (int k = 0; k < data.fetchRows; k++) if (data.fetchRowStart[k + 1] < data.fetchRowStart[k] + 2) { r.findings.Add("Row " + k + " has fewer than 2 cells"); break; }

            // grid vs saved Terrain, and missing cells inside the band
            for (int row = 0; row < data.fetchRows; row++)
            {
                float z = data.fetchZ0 + row * data.fetchStep;
                for (int i = data.fetchRowStart[row]; i < data.fetchRowStart[row + 1]; i++)
                {
                    float x = data.fetchRowX0[row] + (i - data.fetchRowStart[row]) * data.fetchStep, b = data.fetchBed[i];
                    bool inBand = Mathf.Abs(x - data._CenterX(z)) <= data._HalfWidth(z) + KH_RiverData.BandMargin;
                    if (b <= KH_RiverData.NoBed + 1f) { if (inBand) r.missingInBand++; continue; }
                    float g = Ground(terrains, x, z);
                    if (g <= KH_RiverData.NoBed + 1f) continue;
                    float e = Mathf.Abs(b - g);
                    if (e > r.maxBedError) { r.maxBedError = e; r.worstBedAt = new Vector3(x, b, z); }
                }
            }
            if (r.maxBedError > TolBed) r.findings.Add("Grid differs from the saved Terrain by " + r.maxBedError.ToString("F4") + " m at " + r.worstBedAt + " (rebuild with 90 after a Terrain change)");
            if (r.missingInBand > 0) r.findings.Add(r.missingInBand + " riverbed cells missing inside the band");

            // against the play spot's grid where both have a value (the dogs switch between them at the spot's box edge)
            var spotNode = GameObject.Find(DogsRoot + "/KH_WaterSpot"); var spot = spotNode == null ? null : spotNode.GetComponent<KH_WaterSpot>();
            if (spot != null && spot._Ready())
                for (int i = 0; i < spot.bed.Length; i++)
                {
                    if (spot.bed[i] <= KH_WaterSpot.NoBed + 1f) continue;
                    var c = spot._CellPoint(i); float b = data._BedAt(c);
                    if (b > KH_RiverData.NoBed + 1f) r.maxSpotDiff = Mathf.Max(r.maxSpotDiff, Mathf.Abs(b - c.y));
                }
            if (r.maxSpotDiff > .02f) r.findings.Add("River grid and play-spot grid differ by " + r.maxSpotDiff.ToString("F3") + " m (> 0.02; rebuild 76 / 90 from the same Terrain)");

            // slices: stored = recomputed, each ok bank checked again
            var route = Route();
            var recomputed = ComputeSlices(data, route);
            r.slices = recomputed.Count / 2; r.slices_ = recomputed;
            if (recomputed.Count != data.sliceOk.Length) r.findings.Add("Slice count " + data.sliceOk.Length / 2 + " stored vs " + r.slices + " now (rebuild with 90)");
            NavMeshHit h; var path = new NavMeshPath();
            for (int k = 0; k < Mathf.Min(recomputed.Count, data.sliceOk.Length); k++)
            {
                var s = recomputed[k];
                if (s.ok != data.sliceOk[k] || (s.ok && (Flat(s.bank - data.sliceBank[k]) > .05f || Flat(s.ret - data.sliceReturn[k]) > .05f))) r.sliceMismatches++;
                if (!data.sliceOk[k]) continue;
                if (s.side < 0) r.okMinus++; else r.okPlus++;
                if (data._InBridgeZone(data.sliceBank[k].z, 0f)) r.okInBridgeZone++;
                bool onNav = NavMesh.SamplePosition(data.sliceBank[k], out h, .3f, NavMesh.AllAreas) && !data._InBand(data.sliceBank[k], KH_RiverData.BandMargin)
                    && NavMesh.SamplePosition(data.sliceReturn[k], out h, .3f, NavMesh.AllAreas) && !data._InBand(data.sliceReturn[k], KH_RiverData.BandMargin);
                if (!onNav) r.okNotOnNav++;
                NavMeshHit from, to; var node = NearestNode(route, data.sliceReturn[k]);
                bool pathOk = NavMesh.SamplePosition(node, out from, 1.5f, NavMesh.AllAreas) && NavMesh.SamplePosition(data.sliceReturn[k], out to, .3f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                if (!pathOk) r.okNoPath++;
            }
            if (r.sliceMismatches > 0) r.findings.Add(r.sliceMismatches + " stored slices differ from the recomputed ones (NavMesh / Terrain / stones changed: rebuild with 90)");
            if (r.okInBridgeZone > 0) r.findings.Add(r.okInBridgeZone + " ok banks inside a bridge zone");
            if (r.okNotOnNav > 0) r.findings.Add(r.okNotOnNav + " ok bank / return points not on the NavMesh outside the band");
            if (r.okNoPath > 0) r.findings.Add(r.okNoPath + " ok return points without a complete path from the nearest route node");
            if (r.okMinus + r.okPlus == 0) r.findings.Add("No ok bank slice at all");

            // where a ball floats (river centre, outside the bridge zones), and the stretches without a bank (recorded)
            r.noBank = NoBankGaps(data);
            int floats = 0, samples = 0;
            for (float z = GridZ0; z <= GridZ1 + 1e-3f; z += 1f)
            {
                if (data._InBridgeZone(z, 0f)) continue;
                samples++;
                if (data._FloatsAt(new Vector3(data._CenterX(z), data._SurfaceY(z) - .05f, z))) floats++;
            }
            r.bandLength = samples; r.floatCoverage = samples > 0 ? (float)floats / samples : 0f;
            foreach (var g in r.noBank) r.notes.Add("No usable bank within " + data._BankReach() + " m: z " + g.z0 + "-" + g.z1 + " (section " + g.section + "): a ball there goes back to the bank");

            // capacity
            r.gridBytes = data.fetchBed.Length * 4; r.rowBytes = data.fetchRowX0.Length * 4 + data.fetchRowStart.Length * 4;
            r.sliceBytes = data.sliceBank.Length * 12 + data.sliceReturn.Length * 12 + data.sliceOk.Length;
            r.totalBytes = r.gridBytes + r.rowBytes + r.sliceBytes;
            if (r.gridBytes + r.rowBytes > GridBudgetBytes) r.notes.Add("Grid " + (r.gridBytes + r.rowBytes) + " B is above the design estimate " + GridBudgetBytes + " B (§E-1)");
            if (r.sliceBytes > SliceBudgetBytes) r.notes.Add("Slices " + r.sliceBytes + " B are above the design estimate " + SliceBudgetBytes + " B (§E-1)");

            // tuning and wiring
            r.fetchWadeRatio = data.fetchWadeRatio; r.bankReach = data.bankReach; r.bridgeZoneHalfZ = data.bridgeZoneHalfZ;
            if (Mathf.Abs(data.fetchWadeRatio - KH_RiverData.DefaultFetchWadeRatio) > 1e-4f || Mathf.Abs(data.bankReach - KH_RiverData.DefaultBankReach) > 1e-4f
                || Mathf.Abs(data.bridgeZoneHalfZ - KH_RiverData.DefaultBridgeZoneHalfZ) > 1e-4f) r.findings.Add("Tuning values differ from the constants (rebuild with 90)");
            foreach (var w in UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true)) if (w.riverData == data) r.wanderWired++;
            foreach (var f in UnityEngine.Object.FindObjectsOfType<KD_DogFetchController>(true)) if (f.riverData == data) r.fetchWired++;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<KH_ToyRecall>(true)) if (t.riverData == data) r.toyRecallWired++;
            if (r.wanderWired != 6 || r.fetchWired != 6 || r.toyRecallWired != 1)
                r.findings.Add("Wiring incomplete wander=" + r.wanderWired + "/6 fetch=" + r.fetchWired + "/6 toyRecall=" + r.toyRecallWired + "/1 (Course/73 wires KH_RiverData)");

            r.errors = r.findings.Count; r.status = r.errors == 0 ? "PASS" : "FAIL";
            Write("river-fetch-verify.json", JsonUtility.ToJson(r, true));
            Debug.Log("[KH River Fetch] verify " + r.status + " errors=" + r.errors + " rows=" + r.rows + " cells=" + r.cells + " bed=" + r.maxBedError.ToString("F4")
                + " missingInBand=" + r.missingInBand + " ok -x=" + r.okMinus + " +x=" + r.okPlus + " floatCoverage=" + (r.floatCoverage * 100f).ToString("F1") + "% noBankGaps=" + r.noBank.Count
                + " bytes grid=" + (r.gridBytes + r.rowBytes) + " slices=" + r.sliceBytes + " total=" + r.totalBytes);
        }

        static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }
        static void Write(string name, string json) { Directory.CreateDirectory(OutDir); File.WriteAllText(OutDir + "/" + name, json); }
    }
}
