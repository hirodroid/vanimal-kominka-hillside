using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-029 汚れの発生: 泥の地点の配置と、KH_DogWash (全部の犬) への配線。
    ///
    /// - Wash/20 Build Dirt Zones: 本番シーン (Idle 必須) を Temp へ退避してから、シーンの直下に KH_DirtZones と子の地点 2 つ
    ///   (奥広場の縁 / 川辺の岸の土) を置き (既にある地点は位置と大きさを保つ = 人が動かした位置を上書きしない。置き直すときは子を消してから流す)、
    ///   シーンの全 KH_DogWash へ dirtZones / dirtZoneBox / washStation (KH_WashStation) / dirtFetcher / dirtLeashes / dirtFrisbee
    ///   (同じ dogIndex の KD_DogWanderController の fetchController / hillsideLeashes / frisbeeCatch) を書き、proxy → Udon へ写して保存し、
    ///   続けて 21 と同じ検査をする。何度流しても同じ結果。
    /// - Wash/21 Verify Dirt Zones: proxy と backing (publicVariables)、地点の置き場所 (NavMesh・地形の高さ・川の帯の外・洗い場から十分遠い) を見る。
    ///   シーンを書かない。Temp/imp-dog-029/dirt-zones-verify.json。失敗があれば最後に例外。
    ///
    /// IMP-DOG-030: KH_DogWash は 6 匹分 (Wash/11 が作る)。20 はシーンの全 KH_DogWash へ書くので犬の数によらず同じ手順、21 は 6 匹そろっていることも見る。
    ///
    /// 地点は Transform だけ (Udon も Collider も無い)。球 = 中心から lossyScale.x / 2 以内 (KH_DogWash.InDirtZone)。見た目は今は無し
    /// (IMP-ENV-008 で地面の見た目を足す)。initialDirt (試作の初期値) は触らない: 本番では 0 にする (Wash/11 のビルダー側の値)。</summary>
    public static class KH_DirtZoneEditor
    {
        const string Menu = "Tools/KominkaHillside/Wash/";
        const string OutDir = "Temp/imp-dog-029";
        const string BuildReportPath = OutDir + "/dirt-zones-build.json";
        const string VerifyReportPath = OutDir + "/dirt-zones-verify.json";
        const string RootName = "KH_DirtZones", StationName = "KH_WashStation";

        /// <summary>地点の表 (試作値)。destination = KH_WalkRoute の行き先の番号 (4 奥の広場 / 5 川辺、KH_CourseRouteEditor)。</summary>
        class ZoneSpec
        {
            public string name, label; public int destination; public float radius; public bool riverbank;
            public ZoneSpec(string name, string label, int destination, float radius, bool riverbank) { this.name = name; this.label = label; this.destination = destination; this.radius = radius; this.riverbank = riverbank; }
        }
        static readonly ZoneSpec[] Zones =
        {
            new ZoneSpec("KH_DirtZone_Upper", "奥広場の縁", 4, 2.5f, false),
            new ZoneSpec("KH_DirtZone_Riverbank", "川辺の岸の土", 5, 2.5f, true),
        };
        /// <summary>奥広場: 行き先のノードから道に直角へこの距離 (m) を順に試す (広場の縁)。</summary>
        static readonly float[] UpperOffsets = { 5f, 4f, 6f, 3f };
        /// <summary>川辺: 地点の縁と川の帯 (水面の幅) の間に残す土の幅 (m)。</summary>
        const float BankGap = 0.3f;
        /// <summary>地点の中心が守ること: 行き先 / 水遊びの入口からの距離の上限 (m)、地形の高さとの差 (m)、NavMesh までの距離 (m)。</summary>
        const float MaxFromAnchor = 12f, MaxTerrainError = 0.3f, MaxNavDistance = 1.0f;

        [Serializable] class BuildReport
        {
            public string utc, scene, status, backup;
            public List<ZoneOut> zones = new List<ZoneOut>();
            public List<string> wired = new List<string>();
            public List<string> log = new List<string>();
        }
        [Serializable] class ZoneOut
        {
            public string name, label; public Vector3 position; public float radius; public bool kept; public string how;
        }
        [Serializable] class VerifyReport
        {
            public string utc, scene, status;
            public int failed;
            public List<string> lines = new List<string>();
            public string limitations = "Static placement and wiring only. The dirt itself (walk distance, zone time, river, grace, station) is the ClientSim harness "
                + "Course/100 W-dirt-* (KH_CourseWalkHarnessEditor.WashDirt.cs). No ground look yet (IMP-ENV-008). initialDirt is Wash/11's value (0 for release).";
        }

        // ================================================================ 20

        [MenuItem(Menu + "20 - Build Dirt Zones")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            RequireCompiled();
            var ctx = Resolve();   // everything is looked up before anything changes
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var placed = new List<KeyValuePair<ZoneSpec, Vector3>>();
            foreach (var z in Zones)
            {
                var existing = ctx.root != null ? ctx.root.Find(z.name) : null;
                if (existing != null) continue;
                string how;
                placed.Add(new KeyValuePair<ZoneSpec, Vector3>(z, Place(ctx, z, out how)));
                r.log.Add(z.name + ": " + how);
            }
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-dirt-zones-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, false);
            try
            {
                var root = ctx.root;
                if (root == null)
                {
                    var go = new GameObject(RootName);
                    root = go.transform;
                    r.log.Add("created " + RootName + " at the scene root");
                }
                var zoneTs = new List<Transform>();
                foreach (var z in Zones)
                {
                    var t = root.Find(z.name);
                    bool kept = t != null;
                    if (t == null)
                    {
                        t = new GameObject(z.name).transform;
                        t.SetParent(root, false);
                        t.position = placed.First(kv => kv.Key == z).Value;
                        t.rotation = Quaternion.identity;
                        t.localScale = new Vector3(z.radius * 2f, z.radius * 2f, z.radius * 2f);
                    }
                    zoneTs.Add(t);
                    r.zones.Add(new ZoneOut { name = z.name, label = z.label, position = t.position, radius = Mathf.Abs(t.lossyScale.x) * 0.5f, kept = kept, how = kept ? "kept (existing position and scale)" : r.log.LastOrDefault(l => l.StartsWith(z.name)) });
                }

                foreach (var w in ctx.washes)
                {
                    var wander = ctx.wanders.FirstOrDefault(x => x.dogIndex == w.dogIndex);
                    var so = new SerializedObject(w);
                    var zp = so.FindProperty("dirtZones");
                    var bp = so.FindProperty("dirtZoneBox");
                    zp.arraySize = zoneTs.Count;
                    bp.arraySize = zoneTs.Count;
                    for (int i = 0; i < zoneTs.Count; i++)
                    {
                        zp.GetArrayElementAtIndex(i).objectReferenceValue = zoneTs[i];
                        bp.GetArrayElementAtIndex(i).boolValue = false;
                    }
                    so.FindProperty("washStation").objectReferenceValue = ctx.station;
                    so.FindProperty("dirtFetcher").objectReferenceValue = wander != null ? wander.fetchController : null;
                    so.FindProperty("dirtLeashes").objectReferenceValue = wander != null ? wander.hillsideLeashes : null;
                    so.FindProperty("dirtFrisbee").objectReferenceValue = wander != null ? wander.frisbeeCatch : null;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Copy(w);
                    r.wired.Add(w.name + " (dog " + w.dogIndex + "): zones " + zoneTs.Count + ", station " + (ctx.station != null ? ctx.station.name : "null")
                        + ", fetcher " + Name(wander != null ? wander.fetchController : null) + ", leashes " + Name(wander != null ? wander.hillsideLeashes : null)
                        + ", frisbee " + Name(wander != null ? wander.frisbeeCatch : null));
                }

                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
                if (!EditorSceneManager.SaveScene(root.gameObject.scene)) throw new IOException("Dirt zones scene save failed");
            }
            catch
            {
                Debug.LogError("[KH Dirt Zones] Build failed; the scene before this run is at " + r.backup + ". Reopen the scene without saving to discard partial changes.");
                r.status = "FAIL (exception, not saved)";
                WriteJson(BuildReportPath, r);
                throw;
            }
            r.status = "BUILT";
            WriteJson(BuildReportPath, r);
            var v = Check();
            WriteJson(VerifyReportPath, v);
            if (v.failed > 0) throw new InvalidOperationException("[KH Dirt Zones] Build saved but " + v.failed + " check(s) failed (" + VerifyReportPath + "):\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL"))));
            Debug.Log("[KH Dirt Zones] BUILT and verified (" + v.lines.Count + " checks, " + ctx.washes.Length + " KH_DogWash). " + BuildReportPath + " / " + VerifyReportPath);
        }

        // ================================================================ 21

        [MenuItem(Menu + "21 - Verify Dirt Zones")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var v = Check();
            WriteJson(VerifyReportPath, v);
            if (v.failed > 0) throw new InvalidOperationException("[KH Dirt Zones] Verify failed (" + v.failed + "), see " + VerifyReportPath + ":\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL"))));
            Debug.Log("[KH Dirt Zones] VERIFY_OK (" + v.lines.Count + " checks) -> " + VerifyReportPath);
        }

        // ================================================================ 解決 (何も変えない)

        class Ctx
        {
            public Transform root, station;
            public KH_DogWash[] washes;
            public KD_DogWanderController[] wanders;
            public KH_WalkRoute route;
            public KH_WaterSpot spot;
            public KH_RiverData river;
        }

        static void RequireCompiled()
        {
            foreach (var f in new[] { "dirtZones", "dirtZoneBox", "washStation", "dirtFetcher", "dirtLeashes", "dirtFrisbee" })
                if (typeof(KH_DogWash).GetField(f) == null) throw new InvalidOperationException("KH_DogWash." + f + " missing: compile first (nothing changed)");
        }

        static Ctx Resolve()
        {
            var c = new Ctx();
            var roots = UnityEngine.Object.FindObjectsOfType<Transform>(true).Where(t => t.parent == null && t.name == RootName).ToArray();
            if (roots.Length > 1) throw new InvalidOperationException(RootName + " found " + roots.Length + " times at the scene root (expected 0 or 1)");
            c.root = roots.Length == 1 ? roots[0] : null;
            var stations = UnityEngine.Object.FindObjectsOfType<Transform>(true).Where(t => t.parent == null && t.name == StationName).ToArray();
            if (stations.Length != 1) throw new InvalidOperationException(StationName + " found " + stations.Length + " times at the scene root (run Wash/11 first; nothing changed)");
            c.station = stations[0];
            c.washes = UnityEngine.Object.FindObjectsOfType<KH_DogWash>(true).OrderBy(w => w.dogIndex).ToArray();
            if (c.washes.Length == 0) throw new InvalidOperationException("no KH_DogWash in the scene (run Wash/11 first; nothing changed)");
            if (c.washes.Select(w => w.dogIndex).Distinct().Count() != c.washes.Length || c.washes.Any(w => w.dogIndex < 0))
                throw new InvalidOperationException("KH_DogWash dogIndex values are not distinct / valid (" + string.Join(",", c.washes.Select(w => w.dogIndex)) + ")");
            c.wanders = UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true).OrderBy(w => w.dogIndex).ToArray();
            foreach (var w in c.washes)
                if (c.wanders.Count(x => x.dogIndex == w.dogIndex) != 1) throw new InvalidOperationException(w.name + ": no single KD_DogWanderController with dogIndex " + w.dogIndex);
            var managers = UnityEngine.Object.FindObjectsOfType<KH_LeashManager>(true);
            if (managers.Length != 1 || managers[0].route == null) throw new InvalidOperationException("exactly one KH_LeashManager with a route required (" + managers.Length + ")");
            c.route = managers[0].route;
            if (c.route.nodePositions == null || c.route.nodePositions.Length < 2) throw new InvalidOperationException("KH_WalkRoute.nodePositions not built");
            c.spot = UnityEngine.Object.FindObjectsOfType<KH_WaterSpot>(true).FirstOrDefault();
            c.river = UnityEngine.Object.FindObjectsOfType<KH_RiverData>(true).FirstOrDefault();
            if (c.river == null || !c.river._Ready()) throw new InvalidOperationException("KH_RiverData missing / not built (Course/73)");
            return c;
        }

        static Vector3 DestinationPoint(Ctx c, int destination)
        {
            int node = c.route._DestinationNode(destination);
            if (node < 0 || node >= c.route.nodePositions.Length) throw new InvalidOperationException("destination " + destination + " has no node");
            return c.route.nodePositions[node];
        }

        /// <summary>地点の中心を決める (決まらなければ例外 = 何も変えない)。</summary>
        static Vector3 Place(Ctx c, ZoneSpec z, out string how)
        {
            Vector3 anchor = DestinationPoint(c, z.destination);
            if (!z.riverbank)
            {
                int node = c.route._DestinationNode(z.destination);
                var np = c.route.nodePositions;
                Vector3 dir = np[Mathf.Min(node + 1, np.Length - 1)] - np[Mathf.Max(node - 1, 0)];
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
                Vector3 perp = new Vector3(-dir.z, 0f, dir.x).normalized;
                var tried = new List<string>();
                foreach (float d in UpperOffsets)
                    foreach (float side in new[] { 1f, -1f })
                    {
                        Vector3 p = anchor + perp * (d * side);
                        string why;
                        if (TryGround(c, p, anchor, z.radius, out Vector3 g, out why)) { how = "destination " + z.destination + " node " + node + " + " + d + " m across the path (side " + side + ")"; return g; }
                        tried.Add(d + "/" + side + ": " + why);
                    }
                throw new InvalidOperationException(z.name + ": no place at the edge of the upper plaza (" + string.Join("; ", tried) + "); nothing changed");
            }
            // Riverbank: the bank soil on the water-play spot's side, the zone's edge BankGap from the water band.
            Vector3 entry = c.spot != null && c.spot.entry != Vector3.zero ? c.spot.entry : anchor;
            float sideX = Mathf.Sign(entry.x - c.river._CenterX(entry.z));
            if (sideX == 0f) sideX = 1f;
            var tries = new List<string>();
            foreach (float dz in new[] { 0f, 1.5f, -1.5f, 3f, -3f, 4.5f, -4.5f, 6f, -6f })
            {
                float zz = entry.z + dz;
                if (!c.river._Covers(zz)) { tries.Add(dz + ": outside the river data"); continue; }
                float x = c.river._CenterX(zz) + sideX * (c.river._HalfWidth(zz) + z.radius + BankGap);
                Vector3 p = new Vector3(x, entry.y, zz);
                string why;
                if (TryGround(c, p, entry, z.radius, out Vector3 g, out why)) { how = "bank of the water-play spot" + (c.spot != null ? " (entry " + V(entry) + ")" : " (no KH_WaterSpot: the 川辺 node)") + ", dz " + dz + " m, side " + sideX; return g; }
                tries.Add(dz + ": " + why);
            }
            throw new InvalidOperationException(z.name + ": no bank place near " + V(entry) + " (" + string.Join("; ", tries) + "); nothing changed");
        }

        /// <summary>地形の高さへ下ろし、NavMesh・川・洗い場・基準点からの距離を確かめる。</summary>
        static bool TryGround(Ctx c, Vector3 p, Vector3 anchor, float radius, out Vector3 g, out string why)
        {
            g = p;
            var t = TerrainAt(p.x, p.z);
            if (t == null) { why = "no terrain"; return false; }
            g.y = t.SampleHeight(new Vector3(p.x, 0f, p.z)) + t.transform.position.y;
            return PlaceOk(c, g, anchor, radius, out why);
        }

        static bool PlaceOk(Ctx c, Vector3 g, Vector3 anchor, float radius, out string why)
        {
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(g, out hit, MaxNavDistance, NavMesh.AllAreas)) { why = "not on the NavMesh (> " + MaxNavDistance + " m)"; return false; }
            if (c.river._InBand(g, radius)) { why = "the zone touches the river band (lateral " + c.river._Lateral(g, 0f).ToString("F2") + " m, radius " + radius + ")"; return false; }
            float fs = Flat(g - c.station.position);
            if (fs < 20f) { why = "too close to the wash station (" + fs.ToString("F1") + " m)"; return false; }
            float fa = Flat(g - anchor);
            if (fa > MaxFromAnchor) { why = fa.ToString("F1") + " m from the anchor (> " + MaxFromAnchor + ")"; return false; }
            if (Mathf.Abs(g.y - anchor.y) > 2.5f) { why = "height " + (g.y - anchor.y).ToString("F2") + " m from the anchor"; return false; }
            why = "ok";
            return true;
        }

        static Terrain TerrainAt(float x, float z)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null || t.terrainData == null) continue;
                var o = t.transform.position; var s = t.terrainData.size;
                if (x >= o.x && x <= o.x + s.x && z >= o.z && z <= o.z + s.z) return t;
            }
            return null;
        }

        // ================================================================ 検査

        static VerifyReport Check()
        {
            var v = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            Action<bool, string> req = (ok, what) => { v.lines.Add((ok ? "OK   " : "FAIL ") + what); if (!ok) v.failed++; };
            Ctx c;
            try { c = Resolve(); }
            catch (Exception e) { req(false, "resolve: " + e.Message); v.status = "FAIL"; return v; }
            req(c.root != null, RootName + " at the scene root");
            // IMP-DOG-030: every dog the wash builder targets has its KH_DogWash (Wash/11 before Wash/20), so all of them get the zones
            var keys = KH_DogWashSetupEditor.TargetKeys;
            var names = new HashSet<string>(c.washes.Select(w => w.name));
            var missing = keys.Where(k => !names.Contains("KH_Wash_" + k)).ToArray();
            req(missing.Length == 0 && c.washes.Length == keys.Length, "KH_DogWash for every wash target (" + c.washes.Length + " / " + keys.Length + (missing.Length > 0 ? ", missing " + string.Join(", ", missing) + ": run Wash/11, then Wash/20" : "") + ")");
            var zoneTs = new List<Transform>();
            foreach (var z in Zones)
            {
                var t = c.root != null ? c.root.Find(z.name) : null;
                req(t != null, z.name + " (" + z.label + ") exists");
                if (t == null) continue;
                zoneTs.Add(t);
                req(t.GetComponents<Component>().Length == 1, z.name + " is a bare Transform (no collider / renderer / Udon)");
                float radius = Mathf.Abs(t.lossyScale.x) * 0.5f;
                req(radius >= 1f && radius <= 6f, z.name + " radius " + radius.ToString("F2") + " m within 1..6");
                var terrain = TerrainAt(t.position.x, t.position.z);
                float ty = terrain != null ? terrain.SampleHeight(t.position) + terrain.transform.position.y : float.NaN;
                req(terrain != null && Mathf.Abs(ty - t.position.y) <= MaxTerrainError, z.name + " on the terrain height (" + (terrain == null ? "no terrain" : (t.position.y - ty).ToString("+0.00;-0.00") + " m") + ")");
                Vector3 anchor;
                if (z.riverbank) anchor = c.spot != null && c.spot.entry != Vector3.zero ? c.spot.entry : DestinationPoint(c, z.destination);
                else anchor = DestinationPoint(c, z.destination);
                string why;
                req(PlaceOk(c, t.position, anchor, radius, out why), z.name + " placement at " + V(t.position) + ": " + why);
            }
            for (int i = 0; i < zoneTs.Count; i++)
                for (int j = i + 1; j < zoneTs.Count; j++)
                    req(Flat(zoneTs[i].position - zoneTs[j].position) > zoneTs[i].lossyScale.x * 0.5f + zoneTs[j].lossyScale.x * 0.5f, zoneTs[i].name + " and " + zoneTs[j].name + " do not overlap");

            foreach (var w in c.washes)
            {
                string o = w.name + " (dog " + w.dogIndex + ")";
                var wander = c.wanders.First(x => x.dogIndex == w.dogIndex);
                req(w.dirtZones != null && w.dirtZones.Length == zoneTs.Count && zoneTs.All(t => w.dirtZones.Contains(t)), o + " proxy dirtZones = the " + zoneTs.Count + " zones");
                req(w.dirtZoneBox != null && w.dirtZones != null && w.dirtZoneBox.Length == w.dirtZones.Length, o + " proxy dirtZoneBox length = dirtZones");
                req(w.washStation == c.station, o + " proxy washStation = " + StationName);
                req(w.dirtFetcher == wander.fetchController && w.dirtFetcher != null, o + " proxy dirtFetcher = the wander's fetchController");
                req(w.dirtLeashes == wander.hillsideLeashes && w.dirtLeashes != null, o + " proxy dirtLeashes = the wander's hillsideLeashes");
                req(w.dirtFrisbee == wander.frisbeeCatch, o + " proxy dirtFrisbee = the wander's frisbeeCatch (" + Name(wander.frisbeeCatch) + ")");
                var b = UdonSharpEditorUtility.GetBackingUdonBehaviour(w);
                Backing(req, b, o, "dirtZones", w.dirtZones);
                Backing(req, b, o, "dirtZoneBox", w.dirtZoneBox);
                Backing(req, b, o, "washStation", w.washStation);
                Backing(req, b, o, "dirtFetcher", w.dirtFetcher);
                Backing(req, b, o, "dirtLeashes", w.dirtLeashes);
                Backing(req, b, o, "dirtFrisbee", w.dirtFrisbee);
                req(w.dirtEnabled, o + " dirtEnabled");
                req(w.dirtStationRadius > 0f && w.dirtGraceSeconds > 0f && w.walkDirtMeters > 0f && w.zoneDirtSeconds > 0f && w.riverDropSeconds > 0f,
                    o + " tuning positive (station " + w.dirtStationRadius + " m, grace " + w.dirtGraceSeconds + " s, walk " + w.walkDirtMeters + " m, zone " + w.zoneDirtSeconds + " s, river " + w.riverDropSeconds + " s)");
                req(w.riverDirtFloor > 0f && w.riverDirtFloor >= w.cleanDirtThreshold, o + " riverDirtFloor " + w.riverDirtFloor + " >= cleanDirtThreshold " + w.cleanDirtThreshold + " (the river alone never makes a cell clean)");
                v.lines.Add("NOTE " + o + " initialDirt " + w.initialDirt + (w.initialDirt > 0f ? " (prototype seed; release = 0, Wash/11's value)" : ""));
            }
            v.status = v.failed == 0 ? "OK" : "FAIL";
            return v;
        }

        static void Backing(Action<bool, string> req, UdonBehaviour b, string owner, string field, object want)
        {
            object got = null; bool found = false;
            if (b != null) found = b.publicVariables.TryGetVariableValue(field, out got);
            bool ok = b != null && (IsNull(want) ? (!found || IsNull(got)) : found && Same(got, want));
            req(ok, owner + " backing " + field + (b == null ? " (no backing)" : !found ? " (no variable)" : ok ? "" : " (backing differs from the proxy)"));
        }

        static bool IsNull(object o) { return o == null || (o is UnityEngine.Object uo && uo == null); }

        static bool Same(object got, object want)
        {
            if (IsNull(want)) return IsNull(got);
            if (want is UnityEngine.Object wo)
            {
                var go = got as UnityEngine.Object;
                if (go == null) return false;
                if (go == wo) return true;
                if (wo is UdonSharpBehaviour usb) { var bk = UdonSharpEditorUtility.GetBackingUdonBehaviour(usb); return bk != null && go == bk; }
                return false;
            }
            if (want is Array wa)
            {
                if (!(got is Array ga) || ga.Length != wa.Length) return false;
                for (int i = 0; i < wa.Length; i++) if (!Same(ga.GetValue(i), wa.GetValue(i))) return false;
                return true;
            }
            return Equals(got, want);
        }

        // ================================================================ 部品

        static void Copy(UdonSharpBehaviour b)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(b);
            EditorUtility.SetDirty(b);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (backing == null) return;
            // The same hide flag KH_DogWashSetupEditor.Copy sets before the save (a second Build then finds the scene clean).
            if ((backing.hideFlags & HideFlags.HideInInspector) == 0) backing.hideFlags |= HideFlags.HideInInspector;
            EditorUtility.SetDirty(backing);
        }

        static string Name(UnityEngine.Object o) { return o == null ? "null" : o.name; }
        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
        static string V(Vector3 p) { return "(" + p.x.ToString("F2") + "," + p.y.ToString("F2") + "," + p.z.ToString("F2") + ")"; }

        static void WriteJson(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(o, true));
        }
    }
}
