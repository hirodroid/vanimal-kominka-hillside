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

        public int _DestinationNode(int destination)
        {
            if (destinationNodes == null || destination < 0 || destination >= destinationNodes.Length) return -1;
            return destinationNodes[destination];
        }

        public int _Next(int from, int to)
        {
            if (nextHop == null || from < 0 || to < 0 || from >= count || to >= count) return -1;
            return nextHop[from * count + to];
        }

        public float _Cost(int from, int to)
        {
            if (routeCost == null || from < 0 || to < 0 || from >= count || to >= count) return 1e6f;
            return routeCost[from * count + to];
        }

        public Vector3 _Position(int node)
        {
            return nodes[node].position;
        }

        public float _Radius(int node)
        {
            return arriveRadius[node];
        }

        // Horizontal distance from a point to the nearest link segment.
        public float _DistanceToTrail(Vector3 p)
        {
            float best = 1e6f;
            if (linkA == null) return best;
            for (int i = 0; i < linkA.Length; i++)
            {
                Vector3 a = nodes[linkA[i]].position, b = nodes[linkB[i]].position;
                a.y = 0f; b.y = 0f;
                Vector3 q = p; q.y = 0f;
                Vector3 ab = b - a;
                float len = ab.sqrMagnitude;
                float t = len < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(q - a, ab) / len);
                float d = Vector3.Distance(q, a + ab * t);
                if (d < best) best = d;
            }
            return best;
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
            Vector3 a = nodes[linkA[i]].position, b = nodes[linkB[i]].position;
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
            Vector3 d = nodes[linkB[i]].position - nodes[linkA[i]].position; d.y = 0f;
            return d.sqrMagnitude < 0.0001f ? Vector3.forward : d.normalized;
        }

        private int NearestLink(Vector3 p)
        {
            if (linkA == null) return -1;
            int best = -1; float bestDist = 1e6f;
            Vector3 q = p; q.y = 0f;
            for (int i = 0; i < linkA.Length; i++)
            {
                Vector3 a = nodes[linkA[i]].position, b = nodes[linkB[i]].position;
                a.y = 0f; b.y = 0f;
                Vector3 ab = b - a;
                float len = ab.sqrMagnitude;
                float t = len < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(q - a, ab) / len);
                float d = Vector3.Distance(q, a + ab * t);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }
}
