using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    public static partial class KH_DogWashSetupEditor
    {
        // ================================================================ 13
        // 2026-10-11 (IMP-ENV-007 / 008): Wash/11 refuses once the premium selection (IMP-DOG-043) is installed, but 5dfbff7 turned
        // the main station (~183.5 deg) and Hillside (~2.7 deg) without rewriting Call.standYaw, so Wash/12 failed "standYaw = station
        // yaw + 90". This writes only that value (the same formula as BuildStation) and copies it to the backing. It marks the scene
        // dirty and does not save: run Wash/12, then save.
        [MenuItem(Menu + "13 - Sync Stand Yaw (premium-safe)")]
        public static void SyncStandYaw()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Save or discard unrelated scene edits first");
            var calls = AllStationCalls(scene);
            var log = new System.Text.StringBuilder();
            int changed = 0;
            foreach (var call in calls)
            {
                var st = call.transform.parent;
                float want = Mathf.Repeat(st.eulerAngles.y + StandYawOffset, 360f);
                var so = new SerializedObject(call);
                var p = Prop(so, "standYaw");
                float before = p.floatValue;
                bool differs = Mathf.Abs(Mathf.DeltaAngle(before, want)) >= 0.05f;
                if (differs) { p.floatValue = want; so.ApplyModifiedProperties(); Copy(call); changed++; }
                log.AppendLine(st.name + " standYaw " + before.ToString("F1") + (differs ? " -> " + want.ToString("F1") : " (unchanged)"));
            }
            if (changed > 0) EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/KH_WashStandYaw.txt", "calls=" + calls.Count + " changed=" + changed + " (not saved)\n" + log);
            Debug.Log("[KH Wash Setup] Stand yaw: " + changed + " of " + calls.Count + " changed. Temp/KH_WashStandYaw.txt");
        }
    }
}
