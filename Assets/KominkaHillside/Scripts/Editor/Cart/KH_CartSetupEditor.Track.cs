using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>M1.1 Editor 1 (implementation.md「M1.1」): カート専用の軌道。
    /// KH_WalkRoute の周回 (436 節) から節 0 (門へ入る枝) を除き、節 435 → 1 を直結した閉路 (435 節) を作る。
    /// 区間 k = trackPositions[k] → [k+1] (最後は 435 → 1) で、横ずれ可否は linkAllowsDetour[k + 1] (節 k+1 から始まる区間。直結区間は節 435 の値)。
    /// 距離は route.loopDistances の差 (NavMesh の経路長) をそのまま使い、直結区間だけ直線距離。
    /// **Course/21 で経路を作り直したら Cart/1 を再実行する** (軌道は Setup が焼いた複製なので追従しない)。</summary>
    public static partial class KH_CartSetupEditor
    {
        public const string GateFrameName = "門枠";   // KH_TerrainPrototypeConfig.KominkaGateFrameChild (外壁.prefab の門)
        public const string BenchPath = "KH_World/KH_Places/KH_Viewpoint/KH_Bench";
        public const float SweepStep = 1f, SweepHalfWidth = .6f, SweepLow = .3f, SweepHigh = 1.6f, SweepMargin = 4f, SweepMaxObjectSize = 10f;
        static readonly string[] SweepExclude = { "Terrain", "Grass", "Tree", "Stone", "Shrub", "Riverbed", "Dog", "KD_", "Cart" };
        public const string RerunNote = "Course/21 で経路を作り直したら Cart/1 を再実行する (軌道 trackPositions は Setup が焼いた複製)";

        public class Track
        {
            public Vector3[] positions; public float[] distances; public bool[] detour; public float length, closing;
            public float[] spot, dest, noFrom, noTo;
            public List<string> sweepHits = new List<string>();
            public int sweepCandidates, sweepSkippedLarge;
        }

        public static Track BuildTrack(KH_WalkRoute route)
        {
            int n = route.count; var np = route.nodePositions; var ld = route.loopDistances;
            if (!route.closedLoop || np == null || np.Length != n || ld == null || ld.Length != n || n < 4) throw new InvalidOperationException("Closed-loop route with nodePositions / loopDistances required");
            int m = n - 1;
            var t = new Track { positions = new Vector3[m], distances = new float[m], detour = new bool[m] };
            bool has = route.linkAllowsDetour != null && route.linkAllowsDetour.Length >= n;
            for (int k = 0; k < m; k++)
            {
                t.positions[k] = np[k + 1];
                t.distances[k] = ld[k + 1] - ld[1];
                t.detour[k] = !has || route.linkAllowsDetour[k + 1];   // k = m-1 は節 435 の区間 (直結)
            }
            t.closing = Vector3.Distance(np[n - 1], np[1]);
            t.length = t.distances[m - 1] + t.closing;
            return t;
        }

        public static float TrackWrap(Track t, float d) { d %= t.length; return d < 0f ? d + t.length : d; }

        public static int TrackSegment(Track t, float d)
        {
            d = TrackWrap(t, d);
            int lo = 0, hi = t.distances.Length - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (t.distances[mid] <= d) lo = mid; else hi = mid - 1; }
            return lo;
        }

        public static Vector3 TrackPointRef(Track t, float d)
        {
            d = TrackWrap(t, d);
            int i = TrackSegment(t, d), m = t.distances.Length;
            float a = t.distances[i], b = i + 1 < m ? t.distances[i + 1] : t.length;
            float u = b - a > 1e-6f ? Mathf.Clamp01((d - a) / (b - a)) : 0f;
            return Vector3.Lerp(t.positions[i], t.positions[(i + 1) % m], u);
        }

        public static Vector3 TrackDirectionRef(Track t, float d)
        {
            int i = TrackSegment(t, d), m = t.distances.Length;
            var v = t.positions[(i + 1) % m] - t.positions[i]; v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }

        /// <summary>点を軌道へ水平に投影した距離 (最も近い区間)。</summary>
        public static float ProjectToTrack(Track t, Vector3 p)
        {
            int m = t.positions.Length; float best = float.MaxValue, bestD = 0f;
            for (int i = 0; i < m; i++)
            {
                Vector3 a = t.positions[i], b = t.positions[(i + 1) % m];
                Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ap = new Vector2(p.x - a.x, p.z - a.z);
                float len2 = ab.sqrMagnitude, u = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / len2) : 0f;
                float dist2 = (ap - ab * u).sqrMagnitude;
                if (dist2 < best)
                {
                    best = dist2;
                    float segEnd = i + 1 < m ? t.distances[i + 1] : t.length;
                    bestD = t.distances[i] + (segEnd - t.distances[i]) * u;
                }
            }
            return TrackWrap(t, bestD);
        }

        /// <summary>乗り場 0..4 (spotNodes の位置) と行き先 0..5 (destinationNodes の位置。2/3 の周回は -1) を軌道へ投影する。</summary>
        public static void ProjectStops(Track t, KH_WalkRoute route, KH_LeashManager leash)
        {
            t.spot = new float[StationCount];
            for (int s = 0; s < StationCount; s++)
            {
                if (leash.spotNodes == null || leash.spotNodes.Length <= s || leash.spotNodes[s] < 0 || leash.spotNodes[s] >= route.count) throw new InvalidOperationException("spotNodes[" + s + "] invalid (run Course/80)");
                t.spot[s] = ProjectToTrack(t, route.nodePositions[leash.spotNodes[s]]);
            }
            t.dest = new float[6];
            for (int d = 0; d < 6; d++)
            {
                if (route._LoopDirection(d) != 0) { t.dest[d] = -1f; continue; }
                int node = route._DestinationNode(d);
                if (node < 0 || node >= route.count) throw new InvalidOperationException("destination " + d + " has no node");
                t.dest[d] = ProjectToTrack(t, route.nodePositions[node]);
            }
        }

        /// <summary>障害物の掃引: 軌道沿い 1 m ごとに、中心 ±0.6 m・車高 0.3〜1.6 m の箱へ Renderer.bounds が入る物を探し、その距離 ±4 m を横ずれ 0 の範囲にする。
        /// 除外: Terrain、階層の名前に Terrain/Grass/Tree/Stone/Shrub/Riverbed/Dog/KD_/Cart を含む物、MeshRenderer 以外、水平 10 m を超える物 (地面・水面の AABB が全体を覆うため)。</summary>
        public static void SweepObstacles(Track t)
        {
            var cands = new List<KeyValuePair<Renderer, string>>();
            foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>(false))
            {
                if (!r.enabled || r.GetComponentInParent<Terrain>() != null) continue;
                var b = r.bounds;
                string path = HierarchyPath(r.transform);
                if (SweepExclude.Any(x => path.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                if (b.size.x > SweepMaxObjectSize || b.size.z > SweepMaxObjectSize) { t.sweepSkippedLarge++; continue; }
                cands.Add(new KeyValuePair<Renderer, string>(r, path));
            }
            t.sweepCandidates = cands.Count;
            var hitD = new List<float>(); var seen = new Dictionary<string, float>();
            // 掃引箱: 1 m の区間 (前後 ±0.5 m) × 中心 ±0.6 m × 車高 0.3〜1.6 m の向き付き箱を、ワールド座標の AABB で包む (斜めの区間では広めになる。保守的でよい)。
            // Renderer.bounds との Bounds.Intersects で判定する (細い柱・看板の脚を標本点の間で取りこぼさない。Codex M1.1 レビュー 1)
            for (float d = 0f; d < t.length; d += SweepStep)
            {
                var p = TrackPointRef(t, d); var f = TrackDirectionRef(t, d); var right = new Vector3(f.z, 0f, -f.x);
                var box = new Bounds(p + Vector3.up * SweepLow, Vector3.zero);
                foreach (float a in new[] { -SweepHalfWidth, SweepHalfWidth })
                    foreach (float h in new[] { SweepLow, SweepHigh })
                        foreach (float fo in new[] { -SweepStep * .5f, SweepStep * .5f })
                            box.Encapsulate(p + right * a + Vector3.up * h + f * fo);
                foreach (var c in cands)
                {
                    if (!c.Key.bounds.Intersects(box)) continue;
                    hitD.Add(d);
                    if (!seen.ContainsKey(c.Value)) { seen[c.Value] = d; t.sweepHits.Add("d=" + d.ToString("F0") + " " + c.Value); }
                }
            }
            // 範囲 [d-4, d+4] を併合し、0 / trackLength を跨ぐものは 2 つに分ける
            var ranges = new List<Vector2>();
            foreach (var d in hitD.OrderBy(x => x))
            {
                var rg = new Vector2(d - SweepMargin, d + SweepMargin);
                if (ranges.Count > 0 && rg.x <= ranges[ranges.Count - 1].y) ranges[ranges.Count - 1] = new Vector2(ranges[ranges.Count - 1].x, Mathf.Max(ranges[ranges.Count - 1].y, rg.y));
                else ranges.Add(rg);
            }
            var from = new List<float>(); var to = new List<float>();
            foreach (var rg in ranges)
            {
                float a = rg.x, b = rg.y;
                if (a < 0f) { from.Add(a + t.length); to.Add(t.length); a = 0f; }
                if (b > t.length) { from.Add(0f); to.Add(b - t.length); b = t.length; }
                from.Add(a); to.Add(b);
            }
            t.noFrom = from.ToArray(); t.noTo = to.ToArray();
        }

        public static bool InNoLateral(float[] from, float[] to, float d)
        {
            if (from == null || to == null) return false;
            for (int i = 0; i < from.Length && i < to.Length; i++) if (d >= from[i] && d <= to[i]) return true;
            return false;
        }

        /// <summary>門枠 (外壁.prefab の子) の Renderer を全部包む bounds。見つからなければ false。</summary>
        public static bool GateBounds(out Bounds bounds, out string path)
        {
            bounds = default; path = null;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(false)).Where(x => x.name == GateFrameName).ToArray();
            var gate = all.FirstOrDefault(x => HierarchyPath(x).StartsWith("KH_World/")) ?? all.FirstOrDefault();
            if (gate == null) return false;
            var rs = gate.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0) return false;
            bounds = rs[0].bounds; foreach (var r in rs) bounds.Encapsulate(r.bounds);
            path = HierarchyPath(gate);
            return true;
        }

        public static string HierarchyPath(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        static void WriteTrack(UnityEngine.Object manager, Track t)
        {
            var so = new SerializedObject(manager);
            SetVectors(so, "trackPositions", t.positions);
            SetFloats(so, "trackDistances", t.distances);
            var len = Prop(so, "trackLength"); if (len.propertyType != SerializedPropertyType.Float) throw new InvalidOperationException("trackLength is not a float"); len.floatValue = t.length;
            SetBools(so, "trackDetour", t.detour);
            SetFloats(so, "noLateralFrom", t.noFrom);
            SetFloats(so, "noLateralTo", t.noTo);
            SetFloats(so, "spotTrackDistance", t.spot);
            SetFloats(so, "destTrackDistance", t.dest);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static SerializedProperty ArrayProp(SerializedObject so, string name, int length)
        {
            var p = Prop(so, name);
            if (!p.isArray) throw new InvalidOperationException(so.targetObject.GetType().Name + "." + name + " is not an array");
            p.arraySize = length; return p;
        }
        static void SetFloats(SerializedObject so, string name, float[] v) { var a = ArrayProp(so, name, v.Length); for (int i = 0; i < v.Length; i++) a.GetArrayElementAtIndex(i).floatValue = v[i]; }
        static void SetBools(SerializedObject so, string name, bool[] v) { var a = ArrayProp(so, name, v.Length); for (int i = 0; i < v.Length; i++) a.GetArrayElementAtIndex(i).boolValue = v[i]; }
        static void SetVectors(SerializedObject so, string name, Vector3[] v) { var a = ArrayProp(so, name, v.Length); for (int i = 0; i < v.Length; i++) a.GetArrayElementAtIndex(i).vector3Value = v[i]; }
    }
}
