using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    // IMP-DOG-010 B: static stopover data written by Build (Walking/4d). A stopover is a fixed point beside the
    // trail the dog likes to drop in on; the walk destination never changes. The spot children under KH_Stopovers
    // are human-owned (see KH_WalkStopoverEditor for the naming), Build only re-derives these arrays from them.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WalkStopovers : UdonSharpBehaviour
    {
        public const int KindSniff = 0;   // nose down: Pickup pose
        public const int KindSit = 1;     // sit and gaze: Sit pose

        // Allocated by Build. See KH_LeashManager for why reference arrays have no same-length defaults.
        public Transform[] spots;        // where the dog stands
        public Vector3[] lookTargets;    // world point the dog faces while it pauses
        public int[] kinds;              // KindSniff / KindSit
        public int[] nearestNode;        // index into KH_WalkRoute.nodes, for the approach
        public int count;

        public Vector3 _Position(int i)
        {
            if (spots == null || i < 0 || i >= spots.Length || spots[i] == null) return Vector3.zero;
            return spots[i].position;
        }

        public Vector3 _LookTarget(int i)
        {
            if (lookTargets == null || i < 0 || i >= lookTargets.Length) return Vector3.zero;
            return lookTargets[i];
        }

        public int _Kind(int i)
        {
            if (kinds == null || i < 0 || i >= kinds.Length) return KindSniff;
            return kinds[i];
        }

        public int _NearestNode(int i)
        {
            if (nearestNode == null || i < 0 || i >= nearestNode.Length) return -1;
            return nearestNode[i];
        }
    }
}
