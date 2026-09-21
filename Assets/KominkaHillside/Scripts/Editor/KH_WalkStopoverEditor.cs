using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // IMP-DOG-010 B: stopovers beside the trail. Build (4d) creates the spots once from the scene anchors and then
    // never touches them again - position, rotation and kind are human-owned, like the sign roots in 4b. Every run it
    // re-derives KH_WalkStopovers from whatever children are there and wires KH_LeashManager.stopovers.
    //
    // Encoding in the scene (no MonoBehaviour: VRChat strips unknown scripts on upload):
    //   KH_Dogs/KH_Stopovers/Stop{NN}_{Kind}   position = where the dog stands
    //                                          rotation = forward is the look direction
    //                                          {Kind}   = Sniff (nose down, Pickup pose) | Sit (sit and gaze)
    //   .../Stop{NN}_{Kind}/Look               optional child; its world position IS the look target
    //                                          (used for the viewpoint spot, which looks at KH_FarKominka).
    //                                          Without it the look target is position + forward * LookFallback.
    // Move a spot, turn it, rename the suffix to change the kind, or delete one: 4d follows. It only re-creates an
    // index that has no child at all, so a deleted spot comes back with its generated placement. The log names the
    // rule each generated spot came from (rule=bench / rule=trunk / rule=node fallback / rule=gate).
    //
    // The array index is the COMPACTED order of the children by name, not the NN in the name: delete Stop01 and
    // Stop02 becomes index 1. NN only decides the order and which default placement 4d re-creates.
    public static class KH_WalkStopoverEditor
    {
        const string WorldScene = "Assets/Scenes/VRCDefaultWorldScene.unity";
        const string ProgramPath = "Assets/KominkaHillside/Scripts/KH_WalkStopovers";
        public const string DogsRoot = "KH_Dogs", RootName = "KH_Stopovers", SpotPrefix = "Stop", LookName = "Look";
        public static readonly string[] KindNames = { "Sniff", "Sit" };
        // Prototype values (docs/development/issues/IMP-DOG-010-walk-stopovers.md).
        public const float StandOffset = 1.8f;              // metres from the trail centerline: off the path, still reachable
        public const float BandMin = 1.2f, BandMax = 2.4f;  // what 4e accepts around it
        public const float MinSpotSpacing = 6f;             // dogs must not read two stopovers as one
        public const float MaxApproach = 8f;                // NavMesh path from the nearest node
        public const float MinLookDistance = 1.5f, LookFallback = 5f;
        public const float BenchStep = 1.5f, TreeStep = 1.2f;
        public const float TreeTrailMin = 1.5f, TreeTrailMax = 6f;
        public const int TreeNodeFirst = 5, TreeNodeLast = 8, GateNode = 2, MidNode = 7;
        public const float MidTrailLook = 10f;              // fallback spot looks at a trunk within this range
        public const float SnapRadius = 2f;

        static void Report(string name, string text) { Directory.CreateDirectory("Temp/KH_DogPort"); File.WriteAllText("Temp/KH_DogPort/" + name + ".txt", text); Debug.Log("[KH Stopover] " + text); }
        static void Guard()
        {
            if (Application.isPlaying || EditorSceneManager.GetActiveScene().path != WorldScene)
                throw new InvalidOperationException("Open the hillside scene in Edit mode.");
        }
        static Transform Child(Transform parent, string name)
        {
            var t = parent.Find(name); if (t != null) return t;
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        static float Ground(float x, float z)
        {
            var terrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            if (terrain == null) return 0f;
            return terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;
        }
        static Transform Anchor(string path, string leaf)
        {
            var world = GameObject.Find("KH_World");
            if (world != null) { var t = world.transform.Find(path); if (t != null) return t; }
            return UnityEngine.Object.FindObjectsOfType<Transform>(true).FirstOrDefault(x => x.name == leaf);
        }

        // ---------------------------------------------------------------- 4d Build

        [MenuItem("Tools/KominkaHillside/Walking/4d Build Stopovers")]
        public static void BuildStopovers()
        {
            Guard(); var scene = EditorSceneManager.GetActiveScene();
            // U# program asset first: the component cannot be added (nor copied to Udon) before it compiles.
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") == null)
            {
                var created = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                created.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ProgramPath + ".cs");
                AssetDatabase.CreateAsset(created, ProgramPath + ".asset"); AssetDatabase.SaveAssets();
                UdonSharpProgramAsset.CompileAllCsPrograms(true);
                Report("stopovers", "created the KH_WalkStopovers program asset; wait for the U# compile, then run 4d again.");
                return;
            }
            var dogs = GameObject.Find(DogsRoot); if (dogs == null) throw new InvalidOperationException("Run Walking/3 Build first.");
            var route = dogs.GetComponentInChildren<KH_WalkRoute>(true);
            if (route == null || route.nodes == null || route.count < 2) throw new InvalidOperationException("Run Walking/4b Build Walk Route first.");
            if (NavMesh.CalculateTriangulation().vertices.Length == 0) throw new InvalidOperationException("Bake Navigation first.");
            var log = new StringBuilder();

            var root = Child(dogs.transform, RootName);
            root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;
            CreateMissing(root, route, log);

            var children = root.Cast<Transform>().Where(c => c.name.StartsWith(SpotPrefix)).OrderBy(c => c.name, StringComparer.Ordinal).ToList();
            int n = children.Count;
            var spots = new Transform[n]; var looks = new Vector3[n]; var kinds = new int[n]; var near = new int[n];
            for (int i = 0; i < n; i++)
            {
                var c = children[i];
                spots[i] = c;
                int kind = KindOf(c.name);
                if (kind < 0) { log.AppendLine("WARN " + c.name + " has no Sniff/Sit suffix; read as Sniff"); kind = KH_WalkStopovers.KindSniff; }
                kinds[i] = kind;
                looks[i] = LookTargetOf(c);
                near[i] = NearestNode(route, c.position);
            }

            var data = root.GetComponent<KH_WalkStopovers>();
            if (data == null) data = root.gameObject.AddUdonSharpComponent<KH_WalkStopovers>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(data);
            if (backing.SyncMethod != Networking.SyncType.None) { backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing); }
            data.spots = spots; data.lookTargets = looks; data.kinds = kinds; data.nearestNode = near; data.count = n;
            UdonSharpEditorUtility.CopyProxyToUdon(data); EditorUtility.SetDirty(data);

            var leashes = dogs.transform.Find("KH_Leashes");
            var manager = leashes != null ? leashes.GetComponent<KH_LeashManager>() : dogs.GetComponentInChildren<KH_LeashManager>(true);
            if (manager == null) log.AppendLine("WARN no KH_LeashManager on KH_Leashes; stopovers left unwired");
            else { manager.stopovers = data; UdonSharpEditorUtility.CopyProxyToUdon(manager); EditorUtility.SetDirty(manager); }

            // Adding an Udon object leaves NetworkID entries; drop the dangling, typeless ones (vrc-scene-edit-safety).
            var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            if (descriptor != null)
            {
                int dropped = descriptor.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0));
                if (dropped > 0) { EditorUtility.SetDirty(descriptor); log.AppendLine("dropped dangling NetworkIDs " + dropped); }
            }

            for (int i = 0; i < n; i++)
                log.AppendLine("stop " + i + " " + children[i].name + " kind=" + KindNames[kinds[i]] + " pos=" + spots[i].position.ToString("F2")
                    + " trail=" + DistanceToTrail(route, spots[i].position).ToString("F2") + "m node=" + near[i]
                    + " look=" + looks[i].ToString("F2") + " (" + Vector3.Distance(spots[i].position, looks[i]).ToString("F2") + "m)");
            log.AppendLine("stopovers=" + n + " wiredToManager=" + (manager != null));
            // Save this scene only: Guard vouches for the active scene, not for anything else the user has open.
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            log.AppendLine("saved " + scene.path + "=" + saved);
            Report("stopovers", log.ToString());
        }

        // First run only, per index: a viewpoint seat, a mid-trail tree root and a shrub by the gate.
        static void CreateMissing(Transform root, KH_WalkRoute route, StringBuilder log)
        {
            var placed = root.Cast<Transform>().Where(c => c.name.StartsWith(SpotPrefix)).Select(c => c.position).ToList();
            for (int index = 0; index < 3; index++)
            {
                if (root.Cast<Transform>().Any(c => c.name.StartsWith(SpotPrefix + index.ToString("00")))) continue;
                Vector3 pos, look; int kind; string why;
                bool ok = index == 0 ? Viewpoint(route, out pos, out look, out kind, out why)
                    : index == 1 ? MidTrailTree(route, out pos, out look, out kind, out why)
                    : GateShrub(route, out pos, out look, out kind, out why);
                if (!ok) { log.AppendLine("SKIP spot " + index + ": " + why); continue; }
                if (placed.Any(p => Vector3.Distance(p, pos) < MinSpotSpacing)) { log.AppendLine("SKIP spot " + index + ": within " + MinSpotSpacing + "m of another stopover"); continue; }
                var t = CreateSpot(root, index, kind, pos, look);
                placed.Add(t.position);
                log.AppendLine("created " + t.name + " (" + why + ")");
            }
        }

        static Transform CreateSpot(Transform root, int index, int kind, Vector3 pos, Vector3 look)
        {
            var go = new GameObject(SpotPrefix + index.ToString("00") + "_" + KindNames[kind]);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            Vector3 dir = look - pos; dir.y = 0f;
            go.transform.rotation = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;
            var lookGo = new GameObject(LookName); lookGo.transform.SetParent(go.transform, false); lookGo.transform.position = look;
            return go.transform;
        }

        // 1) Beside KH_Bench at the viewpoint, on the trail side, sitting and looking at the far kominka.
        static bool Viewpoint(KH_WalkRoute route, out Vector3 pos, out Vector3 look, out int kind, out string why)
        {
            pos = Vector3.zero; look = Vector3.zero; kind = KH_WalkStopovers.KindSit; why = "";
            var bench = Anchor("KH_Places/KH_Viewpoint/KH_Bench", "KH_Bench");
            var kominka = Anchor("KH_Places/KH_FarKominka", "KH_FarKominka");
            if (bench == null) { why = "KH_Bench not found (KH_World/KH_Places/KH_Viewpoint/KH_Bench)"; return false; }
            if (kominka == null) { why = "KH_FarKominka not found (KH_World/KH_Places/KH_FarKominka)"; return false; }
            int node = NearestNode(route, bench.position);
            Vector3 toTrail = route.nodes[node].position - bench.position; toTrail.y = 0f;
            if (toTrail.sqrMagnitude < 1e-4f) { why = "bench sits on the trail centerline"; return false; }
            Vector3 p = bench.position + toTrail.normalized * BenchStep;
            if (!Settle(route, ref p, bench.position - route.nodes[node].position, out why)) return false;
            pos = p; look = EnsureLook(pos, kominka.position, route.nodes[node].position - pos);
            why = "rule=bench: beside KH_Bench toward node " + node + ", looking at KH_FarKominka";
            return true;
        }

        // 2) The trunk closest to the middle of the trail, standing far enough off it to read as "off the path".
        static bool MidTrailTree(KH_WalkRoute route, out Vector3 pos, out Vector3 look, out int kind, out string why)
        {
            pos = Vector3.zero; look = Vector3.zero; kind = KH_WalkStopovers.KindSniff; why = "";
            int last = Mathf.Min(TreeNodeLast, route.count - 1);
            if (TreeNodeFirst > last) { why = "route too short for nodes " + TreeNodeFirst + ".." + TreeNodeLast; return false; }
            Transform best = null; float bestDist = float.MaxValue;
            foreach (var trunk in Trunks())
            {
                float d = DistanceToTrail(route, trunk.position);
                if (d < TreeTrailMin || d > TreeTrailMax) continue;
                float toMid = float.MaxValue;
                for (int i = TreeNodeFirst; i <= last; i++) toMid = Mathf.Min(toMid, Horizontal(trunk.position, route.nodes[i].position));
                if (toMid < bestDist) { bestDist = toMid; best = trunk; }
            }
            if (best == null)
            {
                string missing = "no KH_Tree_* / KH_BoardTree_* trunk " + TreeTrailMin + "-" + TreeTrailMax + "m off the trail near nodes " + TreeNodeFirst + ".." + last;
                return MidTrailNode(route, missing, out pos, out look, out why);
            }
            int node = NearestNode(route, best.position);
            Vector3 toTrail = route.nodes[node].position - best.position; toTrail.y = 0f;
            if (toTrail.sqrMagnitude < 1e-4f) { why = "trunk on the trail centerline"; return false; }
            Vector3 p = best.position + toTrail.normalized * TreeStep;
            if (!Settle(route, ref p, best.position - route.nodes[node].position, out why)) return false;
            pos = p; look = EnsureLook(pos, new Vector3(best.position.x, pos.y, best.position.z), best.position - route.nodes[node].position);
            why = "rule=trunk: " + best.name + " " + bestDist.ToString("F1") + "m from nodes " + TreeNodeFirst + ".." + last + ", nose to the roots";
            return true;
        }

        // 2b) Fallback when no trunk qualifies: stand off Node07 like the gate spot, nose toward the nearest
        // trunk within MidTrailLook if there is one, otherwise away from the trail.
        static bool MidTrailNode(KH_WalkRoute route, string missing, out Vector3 pos, out Vector3 look, out string why)
        {
            pos = Vector3.zero; look = Vector3.zero;
            Vector3 side;
            if (!Beside(route, MidNode, out pos, out side, out why)) { why = missing + "; fallback failed: " + why; return false; }
            Transform near = null; float nearDist = MidTrailLook;
            foreach (var trunk in Trunks())
            {
                float d = Horizontal(pos, trunk.position);
                if (d < nearDist) { nearDist = d; near = trunk; }
            }
            look = near != null
                ? EnsureLook(pos, new Vector3(near.position.x, pos.y, near.position.z), side)
                : EnsureLook(pos, pos + side * 3f, side);
            why = "rule=node fallback (" + missing + "): Node" + MidNode.ToString("00") + " "
                + (near != null ? "nose to " + near.name + " " + nearDist.ToString("F1") + "m" : "nose away from the trail");
            return true;
        }

        // 3) Shrub side of the trail near the gate: straight out from Node02, on whichever side has NavMesh.
        static bool GateShrub(KH_WalkRoute route, out Vector3 pos, out Vector3 look, out int kind, out string why)
        {
            pos = Vector3.zero; look = Vector3.zero; kind = KH_WalkStopovers.KindSniff; why = "";
            Vector3 side;
            if (!Beside(route, GateNode, out pos, out side, out why)) return false;
            look = EnsureLook(pos, pos + side * 3f, side);
            why = "rule=gate: " + why + ", nose away from the trail";
            return true;
        }

        // StandOffset straight out from a node, on whichever side has NavMesh. Hands back the outward direction.
        static bool Beside(KH_WalkRoute route, int nodeIndex, out Vector3 pos, out Vector3 side, out string why)
        {
            pos = Vector3.zero; side = Vector3.zero; why = "";
            if (nodeIndex >= route.count) { why = "route has no Node" + nodeIndex.ToString("00"); return false; }
            Vector3 node = route.nodes[nodeIndex].position;
            Vector3 ahead = route.nodes[Mathf.Min(nodeIndex + 1, route.count - 1)].position - route.nodes[Mathf.Max(nodeIndex - 1, 0)].position;
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 1e-4f) { why = "cannot read the trail direction at Node" + nodeIndex.ToString("00"); return false; }
            Vector3 perp = Vector3.Cross(Vector3.up, ahead.normalized);
            foreach (float sign in new[] { 1f, -1f })
            {
                Vector3 p = node + perp * sign * StandOffset;
                string reason;
                if (!Settle(route, ref p, perp * sign, out reason)) continue;
                pos = p; side = perp * sign;
                why = "Node" + nodeIndex.ToString("00") + " " + (sign > 0f ? "left" : "right") + " side";
                return true;
            }
            why = "neither side of Node" + nodeIndex.ToString("00") + " has NavMesh at " + StandOffset + "m";
            return false;
        }

        // Push the point to StandOffset from the trail centerline (keeping its side), drop it on the terrain and
        // snap it to NavMesh. The anchor picks where along the trail and which side; the offset is code-owned.
        static bool Settle(KH_WalkRoute route, ref Vector3 p, Vector3 fallbackSide, out string why)
        {
            why = "";
            Vector3 c = TrailClosest(route, p);
            Vector3 off = p - c; off.y = 0f;
            if (off.sqrMagnitude < 1e-4f) { off = fallbackSide; off.y = 0f; }
            if (off.sqrMagnitude < 1e-4f) { why = "no side to step to"; return false; }
            p = c + off.normalized * StandOffset;
            p.y = Ground(p.x, p.z);
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(p, out hit, SnapRadius, NavMesh.AllAreas)) { why = "off NavMesh at " + p.ToString("F2"); return false; }
            p = hit.position;
            float d = DistanceToTrail(route, p);
            if (d < BandMin || d > BandMax) { why = "NavMesh snap left it " + d.ToString("F2") + "m from the trail (band " + BandMin + "-" + BandMax + ")"; return false; }
            return true;
        }

        static Vector3 EnsureLook(Vector3 spot, Vector3 look, Vector3 fallbackDir)
        {
            Vector3 d = look - spot; d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) { d = fallbackDir; d.y = 0f; }
            if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            if (d.magnitude < MinLookDistance) d = d.normalized * MinLookDistance;
            return new Vector3(spot.x + d.x, look.y, spot.z + d.z);
        }

        static IEnumerable<Transform> Trunks()
        {
            var world = GameObject.Find("KH_World");
            var result = new List<Transform>();
            foreach (string group in new[] { "KH_Trees", "KH_BoardTrees" })
            {
                var g = world != null ? world.transform.Find(group) : null;
                if (g == null) g = UnityEngine.Object.FindObjectsOfType<Transform>(true).FirstOrDefault(x => x.name == group);
                if (g != null) result.AddRange(g.Cast<Transform>());
            }
            return result;
        }

        // ---------------------------------------------------------------- shared reads

        public static int KindOf(string name)
        {
            int u = name.IndexOf('_');
            if (u < 0) return -1;
            string suffix = name.Substring(u + 1);
            for (int k = 0; k < KindNames.Length; k++) if (string.Equals(suffix, KindNames[k], StringComparison.OrdinalIgnoreCase)) return k;
            return -1;
        }

        public static Vector3 LookTargetOf(Transform spot)
        {
            var look = spot.Find(LookName);
            if (look != null) return look.position;
            return spot.position + spot.forward * LookFallback;
        }

        static float Horizontal(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        public static int NearestNode(KH_WalkRoute route, Vector3 p)
        {
            int best = -1; float bestDist = float.MaxValue;
            for (int i = 0; i < route.count; i++)
            {
                float d = Horizontal(p, route.nodes[i].position);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // Same geometry as KH_WalkRoute._DistanceToTrail, but it also hands back the point on the centerline.
        public static Vector3 TrailClosest(KH_WalkRoute route, Vector3 p)
        {
            Vector3 best = p; float bestDist = float.MaxValue;
            for (int i = 0; route.linkA != null && i < route.linkA.Length; i++)
            {
                Vector3 a = route.nodes[route.linkA[i]].position, b = route.nodes[route.linkB[i]].position;
                a.y = 0f; b.y = 0f;
                Vector3 q = p; q.y = 0f;
                Vector3 ab = b - a;
                float len = ab.sqrMagnitude;
                float t = len < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(q - a, ab) / len);
                Vector3 c = a + ab * t;
                float d = Vector3.Distance(q, c);
                if (d < bestDist) { bestDist = d; best = new Vector3(c.x, p.y, c.z); }
            }
            return best;
        }

        public static float DistanceToTrail(KH_WalkRoute route, Vector3 p) { return Horizontal(p, TrailClosest(route, p)); }

        static string Len(Array a) { return a == null ? "null" : a.Length.ToString(); }

        static float PathLength(NavMeshPath path)
        {
            float l = 0f;
            for (int i = 1; i < path.corners.Length; i++) l += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return l;
        }

        // ---------------------------------------------------------------- 4e Verify

        [MenuItem("Tools/KominkaHillside/Walking/4e Verify Stopovers")]
        public static void VerifyStopovers()
        {
            Guard(); var log = new StringBuilder(); int total = 0, failed = 0;
            void Check(bool ok, string what) { total++; if (!ok) failed++; log.AppendLine((ok ? "PASS " : "FAIL ") + what); }
            var dogs = GameObject.Find(DogsRoot);
            Check(dogs != null, DogsRoot + " exists");
            var root = dogs == null ? null : dogs.transform.Find(RootName);
            Check(root != null, RootName + " exists next to KH_WalkRoute");
            var data = root == null ? null : root.GetComponent<KH_WalkStopovers>();
            Check(data != null, "KH_WalkStopovers on " + RootName);
            var route = dogs == null ? null : dogs.GetComponentInChildren<KH_WalkRoute>(true);
            Check(route != null && route.nodes != null && route.count >= 2, "KH_WalkRoute built (run 4b first)");
            if (data == null || route == null || route.nodes == null) { Report("stopovers-verify", log + "TOTAL " + total + " FAILED " + failed); return; }

            int n = data.count;
            Check(n >= 1, "count >= 1 (" + n + ")");
            // Every per-spot check reads all four arrays, so enter the loop only when they all have the expected
            // shape; a short array must not throw and take the accumulated result and the TOTAL line with it.
            bool shaped = data.spots != null && data.spots.Length == n && data.lookTargets != null && data.lookTargets.Length == n
                && data.kinds != null && data.kinds.Length == n && data.nearestNode != null && data.nearestNode.Length == n;
            Check(shaped, "arrays sized n=" + n + " (spots=" + Len(data.spots) + " lookTargets=" + Len(data.lookTargets)
                + " kinds=" + Len(data.kinds) + " nearestNode=" + Len(data.nearestNode) + ")");
            if (!shaped) Check(false, "per-spot checks skipped: all four arrays must be n=" + n + " first (run 4d)");
            var path = new NavMeshPath();
            for (int i = 0; shaped && i < n; i++)
            {
                var spot = data.spots[i];
                Check(spot != null, "spots[" + i + "] assigned");
                if (spot == null) continue;
                string tag = i + " " + spot.name;
                NavMeshHit hit;
                Check(NavMesh.SamplePosition(spot.position, out hit, .3f, NavMesh.AllAreas), "spot " + tag + " on NavMesh");
                float trail = DistanceToTrail(route, spot.position);
                Check(trail >= BandMin && trail <= BandMax, "spot " + tag + " " + trail.ToString("F2") + "m from the trail centerline, in [" + BandMin + ", " + BandMax + "]");
                int node = data.nearestNode[i];
                Check(node >= 0 && node < route.count, "spot " + tag + " nearestNode " + node + " in range");
                if (node >= 0 && node < route.count)
                {
                    bool complete = NavMesh.CalculatePath(route.nodes[node].position, spot.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    float len = complete ? PathLength(path) : -1f;
                    Check(complete && len <= MaxApproach, "spot " + tag + " approach from Node" + node.ToString("00") + " complete and " + len.ToString("F2") + "m <= " + MaxApproach);
                }
                int kind = data.kinds[i];
                Check(kind == KH_WalkStopovers.KindSniff || kind == KH_WalkStopovers.KindSit, "spot " + tag + " kind " + kind + " in {0,1}");
                Check(kind >= 0 && kind < KindNames.Length && KindOf(spot.name) == kind, "spot " + tag + " name suffix matches kind " + (kind >= 0 && kind < KindNames.Length ? KindNames[kind] : "?"));
                float lookDist = Vector3.Distance(spot.position, data.lookTargets[i]);
                Check(lookDist >= 1f, "spot " + tag + " look target " + lookDist.ToString("F2") + "m away (>= 1)");
                Check(data.lookTargets[i] == LookTargetOf(spot), "spot " + tag + " look target matches the Look child / forward");
                for (int j = i + 1; j < n; j++)
                {
                    if (data.spots[j] == null) continue;
                    float gap = Vector3.Distance(spot.position, data.spots[j].position);
                    Check(gap >= MinSpotSpacing, "spots " + i + "/" + j + " " + gap.ToString("F2") + "m apart (>= " + MinSpotSpacing + ")");
                }
            }
            var leashes = dogs.transform.Find("KH_Leashes");
            var manager = leashes != null ? leashes.GetComponent<KH_LeashManager>() : dogs.GetComponentInChildren<KH_LeashManager>(true);
            Check(manager != null, "KH_LeashManager on KH_Leashes");
            Check(manager != null && manager.stopovers == data, "manager.stopovers references " + RootName);
            Check(manager != null && Mathf.Approximately(manager.stopoverChance, 0.5f), "manager.stopoverChance == 0.50 (actual " + (manager == null ? "-" : manager.stopoverChance.ToString("F2")) + ")");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(data);
            Check(backing != null && backing.SyncMethod == Networking.SyncType.None, "stopovers backing sync None");
            object v; var vars = backing == null ? null : backing.publicVariables;
            Array BackingArray(string name) { object o; return vars != null && vars.TryGetVariableValue(name, out o) ? o as Array : null; }
            var bSpots = BackingArray("spots"); var bLooks = BackingArray("lookTargets");
            var bKinds = BackingArray("kinds"); var bNodes = BackingArray("nearestNode");
            Check(bSpots != null && bSpots.Length == n && bLooks != null && bLooks.Length == n && bKinds != null && bKinds.Length == n && bNodes != null && bNodes.Length == n,
                "backing arrays sized n=" + n + " (spots=" + Len(bSpots) + " lookTargets=" + Len(bLooks) + " kinds=" + Len(bKinds) + " nearestNode=" + Len(bNodes) + ")");
            Check(vars != null && vars.TryGetVariableValue("count", out v) && v is int && (int)v == n, "backing count == " + n);
            if (shaped)
            {
                // The heap is what Udon actually reads (vrc-udonsharp §4): compare every element, not just the first.
                bool same = bSpots != null && bSpots.Length == n && bLooks != null && bLooks.Length == n && bKinds != null && bKinds.Length == n && bNodes != null && bNodes.Length == n;
                int firstBad = -1;
                for (int i = 0; same && i < n; i++)
                {
                    object s = bSpots.GetValue(i), l = bLooks.GetValue(i), k = bKinds.GetValue(i), nd = bNodes.GetValue(i);
                    bool ok = (s as Transform) == data.spots[i]
                        && l is Vector3 && (Vector3)l == data.lookTargets[i]
                        && k is int && (int)k == data.kinds[i]
                        && nd is int && (int)nd == data.nearestNode[i];
                    if (!ok) { same = false; firstBad = i; }
                }
                Check(same, "backing arrays match the proxy element by element" + (firstBad >= 0 ? " (first mismatch at " + firstBad + ")" : " (n=" + n + ")"));
            }
            if (manager != null)
            {
                var mb = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
                object mv;
                Check(mb.publicVariables.TryGetVariableValue("stopovers", out mv) && (mv as UnityEngine.Object) == backing, "manager backing stopovers reference");
            }
            var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset");
            Check(program != null && program.sourceCsScript != null && AssetDatabase.GetAssetPath(program.sourceCsScript) == ProgramPath + ".cs" && backing != null && backing.programSource == program, "program asset points at KH_WalkStopovers.cs and the backing uses it");
            Report("stopovers-verify", log + "TOTAL " + total + " FAILED " + failed);
        }
    }
}
