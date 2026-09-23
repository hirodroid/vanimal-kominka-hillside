using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    public static partial class KD_DogAnimatorBuilderEditor
    {
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Controller/8 - Audit Root Axes")]
        public static void AuditRootAxes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before auditing clips");
            var rows = new List<object>(); var log = new StringBuilder(); int failures = 0;
            foreach (var def in KD_DogDefs.All)
            {
                if (!TryResolveRootRest(def, log, out var rest, out var ratio)) throw new InvalidOperationException(log.ToString());
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(def.prefabGuid));
                var temp = Object.Instantiate(prefab); temp.hideFlags = HideFlags.HideAndDontSave;
                Vector3 axis;
                try
                {
                    var rig = KD_DogRig.Resolve(temp); var bone = rig.srcSkelRoot.Find("Bone");
                    if (rig.srcSkelRoot.localPosition != Vector3.zero) throw new InvalidOperationException(def.key + " armature is not neutral zero");
                    axis = bone.parent.TransformVector(Vector3.forward).normalized;
                    if (Vector3.Dot(axis, Vector3.up) < .99f) throw new InvalidOperationException(def.key + " local Z is not vertical");
                }
                finally { Object.DestroyImmediate(temp); }
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
                if (ctrl == null) throw new InvalidOperationException("Missing controller " + def.key);
                foreach (var clip in ctrl.animationClips.Distinct())
                {
                    if (!RootPositionSafe(clip, def.key + "/" + clip.name, rest, ratio, log)) failures++;
                    var min = Vector3.zero; var max = Vector3.zero;
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (!b.path.EndsWith(RootBoneSuffix) || !b.propertyName.StartsWith("m_LocalPosition.")) continue;
                        int i = b.propertyName.EndsWith(".x") ? 0 : b.propertyName.EndsWith(".y") ? 1 : 2;
                        var curve = AnimationUtility.GetEditorCurve(clip, b);
                        foreach (var key in curve.keys) { float v = key.value - rest[i]; min[i] = Mathf.Min(min[i], v); max[i] = Mathf.Max(max[i], v); }
                        for (int n = 0; n <= 512; n++) { float v = curve.Evaluate(clip.length * n / 512f) - rest[i]; min[i] = Mathf.Min(min[i], v); max[i] = Mathf.Max(max[i], v); }
                    }
                    rows.Add(new { dog = def.key, clip = clip.name, path = AssetDatabase.GetAssetPath(clip), rest = new[]{rest.x,rest.y,rest.z}, ratio,
                        verticalWorld = new[]{axis.x,axis.y,axis.z}, min = new[]{min.x,min.y,min.z}, max = new[]{max.x,max.y,max.z} });
                }
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/bug-dog-003-root-audit.json", Newtonsoft.Json.JsonConvert.SerializeObject(rows, Newtonsoft.Json.Formatting.Indented));
            log.AppendLine("Root-axis audit: " + rows.Count + " controller clips; local Z verified upward for all dogs; failures=" + failures);
            KD_VerifyLog.Write("Logs/bug-dog-003-root-audit.txt", log);
            if (failures != 0) throw new InvalidOperationException("Current controller clips failed root audit");
        }
    }
}
