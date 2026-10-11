using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    // Static trail data written by Build: nodes along the path, links between them and a routing table.
    // Dogs enter at a nearby reachable node and follow nextHop toward their destination.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WalkRoute : UdonSharpBehaviour
    {
        // Allocated by Build. See KH_LeashManager for why reference arrays have no same-length defaults.
        public Transform[] nodes;
        public float[] arriveRadius;
        public int[] linkA;
        public int[] linkB;
        public int[] nextHop;          // nextHop[from * count + to]; -1 when unreachable
        public float[] routeCost;      // metres along links; large when unreachable
        public int[] destinationNodes; // 0 entrance, 1 plaza
        public int count;
        // Full-course copies opt into an ordered ring. The original 17-node route keeps its table.
        // Static positions avoid hundreds of Transform reads in Udon. Build owns these arrays.
        public bool closedLoop;
        public Vector3[] nodePositions;
        public float[] loopDistances;
        public float loopLength;
        public int loopForwardDestination = 2, loopReverseDestination = 3;
        public bool[] linkAllowsDetour;
        public Vector2 cellOrigin;
        public float cellSize = 32f;
        public int cellColumns, cellRows;
        public int[] linkCellStarts, linkCellIndices;
        public int[] entryCellStarts, entryCellNodes;

        public int _DestinationNode(int destination)
        {
            if (destinationNodes == null || destination < 0 || destination >= destinationNodes.Length) return -1;
            return destinationNodes[destination];
        }

        public int _Next(int from, int to)
        {
            if (from < 0 || to < 0 || from >= count || to >= count) return -1;
            if (closedLoop)
            {
                if (from == to) return to;
                float forward = loopDistances[to] - loopDistances[from];
                if (forward < 0f) forward += loopLength;
                return forward <= loopLength * .5f ? (from + 1) % count : (from + count - 1) % count;
            }
            if (nextHop == null) return -1;
            return nextHop[from * count + to];
        }

        public float _Cost(int from, int to)
        {
            if (from < 0 || to < 0 || from >= count || to >= count) return 1e6f;
            if (closedLoop)
            {
                float distance = Mathf.Abs(loopDistances[to] - loopDistances[from]);
                return Mathf.Min(distance, loopLength - distance);
            }
            if (routeCost == null) return 1e6f;
            return routeCost[from * count + to];
        }

        public int _LoopDirection(int destination)
        {
            if (!closedLoop) return 0;
            if (destination == loopForwardDestination) return 1;
            if (destination == loopReverseDestination) return -1;
            return 0;
        }

        public int _Advance(int from, int destination)
        {
            if (from < 0 || from >= count) return -1;
            int direction = _LoopDirection(destination);
            return direction == 0 ? _Next(from, _DestinationNode(destination)) : (from + direction + count) % count;
        }

        // Starting midway around the loop aims at the forward end of the nearest segment,
        // not the node with the lowest remaining cost to the gate. Caller still validates NavMesh.
        public int _LoopEntry(Vector3 p, int destination)
        {
            int direction = _LoopDirection(destination);
            int link = NearestLink(p);
            if (direction == 0 || link < 0) return -1;
            return direction > 0 ? linkB[link] : linkA[link];
        }

        // Rejoin after catching up to the holder. Prefer the current and following segments,
        // retaining which pass of the shared gate stem this dog is on. A distant relocation
        // falls back to a fresh directional entry instead of dragging it toward a stale node.
        public int _LoopReentry(Vector3 p, int destination, int previousNext)
        {
            int direction = _LoopDirection(destination);
            if (direction == 0 || previousNext < 0 || previousNext >= count) return _LoopEntry(p, destination);
            int first = direction > 0 ? (previousNext + count - 1) % count : previousNext;
            int best = -1;float distance = float.MaxValue;
            for (int step = 0; step < 9 && step < count; step++)
            {
                int link = (first + direction * step + count) % count;
                float d = LinkDistanceSquared(p, link);
                if (d < distance) { distance = d; best = link; }
            }
            if (best < 0 || distance > 144f) return _LoopEntry(p, destination);
            return direction > 0 ? linkB[best] : linkA[best];
        }

        public bool _AllowsDetour(Vector3 p)
        {
            if (linkAllowsDetour == null) return true;
            int link = NearestLink(p);
            return link >= 0 && link < linkAllowsDetour.Length && linkAllowsDetour[link];
        }

        public int _EntryCell(Vector3 p)
        {
            if (entryCellStarts == null || entryCellNodes == null) return -1;
            return Cell(p);
        }

        public Vector3 _Position(int node)
        {
            return nodePositions != null && nodePositions.Length == count ? nodePositions[node] : nodes[node].position;
        }

        public float _Radius(int node)
        {
            return arriveRadius[node];
        }

        // Horizontal distance from a point to the nearest link segment.
        public float _DistanceToTrail(Vector3 p)
        {
            int link = NearestLink(p);
            return link < 0 ? 1e6f : Mathf.Sqrt(LinkDistanceSquared(p, link));
        }

        // IMP-DOG-010: the same nearest segment as _DistanceToTrail, as a foot point and as a direction,
        // so a dog can pick a spot measured from the trail centre instead of from wherever it happens to
        // stand. Two plain returns rather than one call with out parameters (cheaper to trust in Udon);
        // both rescan, which costs nothing at the rate these are called (once per 4 s roll).

        // The nearest point on the trail centreline, at the trail's own height.
        public Vector3 _ProjectToTrail(Vector3 p)
        {
            int i = NearestLink(p);
            if (i < 0) return p;
            Vector3 a = _Position(linkA[i]), b = _Position(linkB[i]);
            Vector3 ab = b - a; ab.y = 0f;
            Vector3 qa = p - a; qa.y = 0f;
            float len = ab.sqrMagnitude;
            float t = len < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(qa, ab) / len);
            return a + (b - a) * t;
        }

        // Horizontal unit direction of that segment, linkA -> linkB. The caller decides the sign it wants.
        public Vector3 _TrailDirection(Vector3 p)
        {
            int i = NearestLink(p);
            if (i < 0) return Vector3.forward;
            Vector3 d = _Position(linkB[i]) - _Position(linkA[i]); d.y = 0f;
            return d.sqrMagnitude < 0.0001f ? Vector3.forward : d.normalized;
        }

        private int NearestLink(Vector3 p)
        {
            if (linkA == null) return -1;
            int best = -1; float bestDist = float.MaxValue;
            int cell = Cell(p);
            if (cell >= 0 && linkCellStarts != null && linkCellIndices != null)
            {
                for (int j = linkCellStarts[cell]; j < linkCellStarts[cell + 1]; j++)
                {
                    int i = linkCellIndices[j];float d = LinkDistanceSquared(p, i);
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                // Cells contain every segment within cellSize metres. Outside that radius,
                // fall back to a global search so off-trail queries retain their exact meaning.
                if (best >= 0 && bestDist <= cellSize * cellSize) return best;
            }
            for (int i = 0; i < linkA.Length; i++)
            {
                float d = LinkDistanceSquared(p, i);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private int Cell(Vector3 p)
        {
            if (cellSize <= 0f || cellColumns <= 0 || cellRows <= 0) return -1;
            int x = Mathf.FloorToInt((p.x - cellOrigin.x) / cellSize);
            int z = Mathf.FloorToInt((p.z - cellOrigin.y) / cellSize);
            return x < 0 || z < 0 || x >= cellColumns || z >= cellRows ? -1 : z * cellColumns + x;
        }

        private float LinkDistanceSquared(Vector3 p, int link)
        {
            Vector3 a = _Position(linkA[link]), b = _Position(linkB[link]);
            a.y = 0f;b.y = 0f;p.y = 0f;
            Vector3 ab = b - a;float length = ab.sqrMagnitude;
            float t = length < .0001f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / length);
            return (p - a - ab * t).sqrMagnitude;
        }

        // ---- DES-WLD-004 おさんぽカート: 周回の距離 → 位置 (docs/plans/auto-walk-cart/implementation.md) ----
        // loopDistances[i] はノード i までの累積距離 (NavMesh の経路長。[0] = 0、昇順)。区間 i はノード i → (i + 1) % count
        // (KH_CourseRouteEditor が linkA[i] = i, linkB[i] = i + 1 で書く)。最後の区間の終端は loopLength。
        // 距離は loopLength で正規化するので、負値や 1 周を超える値も受け付ける。周回データが無ければノード 0 を返す。

        /// <summary>周回上の距離 d の点 (区間の直線補間。高さはノードの高さの補間)。</summary>
        public Vector3 _PointAtDistance(float d)
        {
            if (!LoopReady()) return count > 0 ? _Position(0) : transform.position;
            float x = LoopWrap(d);
            int i = LoopSegment(x);
            int j = (i + 1) % count;
            float a = loopDistances[i];
            float b = j == 0 ? loopLength : loopDistances[j];
            float t = b - a > 0.0001f ? Mathf.Clamp01((x - a) / (b - a)) : 0f;
            return Vector3.Lerp(_Position(i), _Position(j), t);
        }

        /// <summary>距離 d を含む区間の水平単位ベクトル (距離が増える向き)。呼び出し側が進行方向の符号を掛ける。</summary>
        public Vector3 _DirectionAtDistance(float d)
        {
            if (!LoopReady()) return Vector3.forward;
            int i = LoopSegment(LoopWrap(d));
            Vector3 v = _Position((i + 1) % count) - _Position(i); v.y = 0f;
            return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
        }

        /// <summary>距離 d を含む区間の linkAllowsDetour (橋などは false)。配列が無ければ true。</summary>
        public bool _DetourAtDistance(float d)
        {
            if (linkAllowsDetour == null) return true;
            if (!LoopReady()) return true;
            int i = LoopSegment(LoopWrap(d));
            return i >= linkAllowsDetour.Length || linkAllowsDetour[i];
        }

        private bool LoopReady()
        {
            return loopDistances != null && count > 1 && loopDistances.Length >= count && loopLength > 0f;
        }

        private float LoopWrap(float d)
        {
            float x = d - Mathf.Floor(d / loopLength) * loopLength;
            if (x >= loopLength || x < 0f) x = 0f;   // 浮動小数の丸めで端に乗ったとき
            return x;
        }

        // loopDistances[i] <= x を満たす最大の i (二分探索。loopDistances[0] = 0 なので必ず 0 以上)
        private int LoopSegment(float x)
        {
            int lo = 0, hi = count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (loopDistances[mid] <= x) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
    }
}
