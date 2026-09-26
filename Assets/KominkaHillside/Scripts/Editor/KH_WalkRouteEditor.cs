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
using C = KH_TerrainPrototypeConfig;

namespace KominkaHillside.Editor
{
    // IMP-DOG-003: trail nodes + routing table. Values here are design-owned (docs/game-design.md §4),
    // so Build Walk Route rewrites them every run. The destination is chosen in the hand menu; the
    // temporary Interact signs (KH_WalkSigns) were retired 2026-09-26 and Build deletes any left over.
    public static class KH_WalkRouteEditor
    {
        const string WorldScene = "Assets/Scenes/VRCDefaultWorldScene.unity";
        const string Data = "Assets/KominkaHillside/Data/Walking";
        public const float NodeSpacing = 7f;
        public const float RadiusEntrance = 3f, RadiusTrail = 2.2f, RadiusPlaza = 5f;
        public const int DestinationEntrance = 0, DestinationPlaza = 1;
        public const string RetiredSignsRoot = "KH_WalkSigns";

        static void Report(string name, string text) { Directory.CreateDirectory("Temp/KH_DogPort"); File.WriteAllText("Temp/KH_DogPort/" + name + ".txt", text); Debug.Log("[KH Route] " + text); }
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
            return terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;
        }
        static T Add<T>(GameObject go) where T : UdonSharpBehaviour
        {
            var b = go.GetComponent<T>(); if (b == null) b = go.AddUdonSharpComponent<T>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            EditorUtility.SetDirty(backing); return b;
        }
        // Trail samples every NodeSpacing metres from the gate to the plaza centre, snapped to terrain and NavMesh.
        public static List<Vector3> NodePositions(StringBuilder log)
        {
            var samples = KH_TerrainShape.SamplePath();
            var result = new List<Vector3>();
            float next = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                bool last = i == samples.Count - 1;
                if (samples[i].s < next && !last) continue;
                next = samples[i].s + NodeSpacing;
                if (last && result.Count > 0 && Vector2.Distance(samples[i].xz, new Vector2(result[result.Count - 1].x, result[result.Count - 1].z)) < NodeSpacing * 0.4f)
                    result.RemoveAt(result.Count - 1);
                var p = new Vector3(samples[i].xz.x, Ground(samples[i].xz.x, samples[i].xz.y), samples[i].xz.y);
                NavMeshHit hit;
                if (NavMesh.SamplePosition(p, out hit, 2f, NavMesh.AllAreas)) p = hit.position;
                else if (log != null) log.AppendLine("WARN node off navmesh at " + p);
                result.Add(p);
            }
            return result;
        }

        [MenuItem("Tools/KominkaHillside/Walking/4b Build Walk Route")]
        public static void BuildRoute()
        {
            Guard(); var scene = EditorSceneManager.GetActiveScene();
            if (NavMesh.CalculateTriangulation().vertices.Length == 0) throw new InvalidOperationException("Bake Navigation first.");
            var root = GameObject.Find("KH_Dogs"); if (root == null) throw new InvalidOperationException("Build first");
            var manager = root.GetComponentInChildren<KH_LeashManager>(true);
            var log = new StringBuilder();
            var positions = NodePositions(log);
            int n = positions.Count;
            var routeT = Child(root.transform, "KH_WalkRoute");
            var route = Add<KH_WalkRoute>(routeT.gameObject);
            // Remove stale nodes so a respaced route leaves no orphans.
            for (int i = routeT.childCount - 1; i >= 0; i--) if (routeT.GetChild(i).name.StartsWith("Node") && int.Parse(routeT.GetChild(i).name.Substring(4)) >= n) UnityEngine.Object.DestroyImmediate(routeT.GetChild(i).gameObject);
            var nodes = new Transform[n]; var radius = new float[n];
            for (int i = 0; i < n; i++)
            {
                nodes[i] = Child(routeT, "Node" + i.ToString("00"));
                nodes[i].position = positions[i];
                radius[i] = i == 0 ? RadiusEntrance : i == n - 1 ? RadiusPlaza : RadiusTrail;
            }
            var linkA = new int[n - 1]; var linkB = new int[n - 1];
            var cost = new float[n * n];
            for (int i = 0; i < n * n; i++) cost[i] = 1e6f;
            for (int i = 0; i < n; i++) cost[i * n + i] = 0f;
            var path = new NavMeshPath();
            for (int i = 0; i < n - 1; i++)
            {
                linkA[i] = i; linkB[i] = i + 1;
                float len = Vector3.Distance(positions[i], positions[i + 1]);
                if (NavMesh.CalculatePath(positions[i], positions[i + 1], NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    float l = 0f; for (int k = 1; k < path.corners.Length; k++) l += Vector3.Distance(path.corners[k - 1], path.corners[k]);
                    len = l;
                }
                else { log.AppendLine("WARN link " + i + "-" + (i + 1) + " has no complete NavMesh path; left unlinked"); continue; }
                cost[i * n + i + 1] = len; cost[(i + 1) * n + i] = len;
            }
            var hop = new int[n * n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) hop[i * n + j] = cost[i * n + j] < 1e5f ? j : -1;
            for (int k = 0; k < n; k++) for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
                if (cost[i * n + k] + cost[k * n + j] < cost[i * n + j]) { cost[i * n + j] = cost[i * n + k] + cost[k * n + j]; hop[i * n + j] = hop[i * n + k]; }
            route.nodes = nodes; route.arriveRadius = radius; route.linkA = linkA; route.linkB = linkB;
            route.nextHop = hop; route.routeCost = cost; route.count = n;
            route.destinationNodes = new[] { 0, n - 1 };
            UdonSharpEditorUtility.CopyProxyToUdon(route); EditorUtility.SetDirty(route);
            if (manager != null) { manager.route = route; UdonSharpEditorUtility.CopyProxyToUdon(manager); EditorUtility.SetDirty(manager); }
            var signs = root.transform.Find(RetiredSignsRoot);
            if (signs != null) { UnityEngine.Object.DestroyImmediate(signs.gameObject); log.AppendLine("removed retired " + RetiredSignsRoot); }
            for (int i = 0; i < n; i++) log.AppendLine("node " + i + " " + positions[i].ToString("F2") + " r=" + radius[i] + " costToPlaza=" + cost[i * n + n - 1].ToString("F1"));
            log.AppendLine("nodes=" + n + " trail=" + cost[n - 1].ToString("F1") + "m");
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            Report("route", log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Walking/4c Inspect Walk Route")]
        public static void Inspect()
        {
            Guard(); var root = GameObject.Find("KH_Dogs");
            var route = root.GetComponentInChildren<KH_WalkRoute>(true);
            var log = new StringBuilder();
            if (route == null) { Report("route-inspect", "no route"); return; }
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(route);
            foreach (string symbol in new[] { "nodes", "arriveRadius", "linkA", "linkB", "nextHop", "routeCost", "destinationNodes", "count" })
            {
                object value; bool ok = backing.publicVariables.TryGetVariableValue(symbol, out value);
                var arr = value as Array;
                log.AppendLine(symbol + " exists=" + ok + " len=" + (arr == null ? value == null ? "null" : value.ToString() : arr.Length.ToString()));
            }
            int n = route.count;
            for (int i = 0; i < n; i++) log.AppendLine("node " + i + " " + route.nodes[i].position.ToString("F2") + " hopToPlaza=" + route.nextHop[i * n + n - 1] + " hopToGate=" + route.nextHop[i * n]);
            var m = root.GetComponentInChildren<KH_LeashManager>(true);
            var mb = UdonSharpEditorUtility.GetBackingUdonBehaviour(m);
            object r; log.AppendLine("manager.route backing=" + (mb.publicVariables.TryGetVariableValue("route", out r) && r != null));
            Report("route-inspect", log.ToString());
        }
    }
}
