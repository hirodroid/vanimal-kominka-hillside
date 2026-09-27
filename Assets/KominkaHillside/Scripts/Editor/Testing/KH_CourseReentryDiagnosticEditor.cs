using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace KominkaHillside.Editor
{
    // Replay recorded positions against the saved NavMesh; never move a runtime actor.
    public static class KH_CourseReentryDiagnosticEditor
    {
        [Serializable] class Sample
        {
            public int dog, observedNext, mode, ahead;
            public double seconds;
            public Vector3 position;
            public bool sampled, complete;
            public float pathLength;
        }
        [Serializable] class Report
        {
            public string source, limitation = "Recorded half-second positions approximate the transition; this is saved-NavMesh replay, not exact runtime branch instrumentation.";
            public List<Sample> samples = new List<Sample>();
        }
        [MenuItem("Tools/KominkaHillside/Course/47 - Replay Gate Reentry NavMesh")]
        public static void Replay()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var route = UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            if (route == null || !route.closedLoop) throw new InvalidOperationException("Closed course required");
            var output = new Report { source = "Logs/kh-course-walk/20260926-185647/report.json" };
            var run = JsonUtility.FromJson<KH_CourseWalkHarnessEditor.Report>(File.ReadAllText(output.source));
            var test = run.cases.Find(c => c.name == "gate-wrap-choice-2-dogs-3");
            foreach (var trace in test.trace)
            {
                if (trace.dog != 1 || trace.seconds < 13 || trace.seconds > 17) continue;
                var sample = new Sample { dog = trace.dog, observedNext = trace.next, mode = trace.mode,
                    seconds = trace.seconds, position = trace.position,
                    ahead = route._LoopReentry(trace.position, 2, 2) };
                NavMeshHit hit;
                sample.sampled = NavMesh.SamplePosition(trace.position, out hit, 2, NavMesh.AllAreas);
                var path = new NavMeshPath();
                sample.complete = sample.sampled && sample.ahead >= 0 &&
                    NavMesh.CalculatePath(hit.position, route._Position(sample.ahead), NavMesh.AllAreas, path) &&
                    path.status == NavMeshPathStatus.PathComplete;
                var corners = path.corners;
                for (int i = 1; i < corners.Length; i++) sample.pathLength += Vector3.Distance(corners[i-1], corners[i]);
                output.samples.Add(sample);
            }
            File.WriteAllText("Temp/des-env-002/gate-reentry-navmesh.json", JsonUtility.ToJson(output, true));
            Debug.Log("[KH Course] Gate reentry NavMesh replay: " + output.samples.Count);
        }
    }
}
