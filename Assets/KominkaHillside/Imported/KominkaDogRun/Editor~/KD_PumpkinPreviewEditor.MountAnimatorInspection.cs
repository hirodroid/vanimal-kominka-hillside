using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinPreviewEditor
    {
        // Evaluates the real derived controller, including its .12-second state
        // transitions, on a disposable clone. This is not the live Udon motor.
        public static string InspectMountAnimator()
        {
            GuardStage();
            var source = SceneManager.GetActiveScene(); bool dirty = source.isDirty;
            var roots = source.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(i => i).ToArray();
            var descriptor = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<VRC.SDK3.Components.VRCSceneDescriptor>(true)).Single();
            var network = descriptor.NetworkIDCollection.Select(n => n?.gameObject == null ? 0 : n.gameObject.GetInstanceID()).ToArray();
            var p = One<KD_CandyDogPerformance>(); var matrices = p.dogs.Select(d => d.dogRoot.localToWorldMatrix).ToArray();
            var scene = EditorSceneManager.NewPreviewScene(); GameObject clone = null;
            PlayableGraph graph = default; var records = new List<object>(); float maximumStep = 0;
            try
            {
                clone = MountStudyClone(p, "Pome", false, 0, scene);
                var original = p.dogs.Single(d => d.dogRoot.name == "KD_Dog_Pome");
                string animatorPath = AnimationUtility.CalculateTransformPath(original.dogAnimator.transform, original.dogRoot);
                var animator = (animatorPath.Length == 0 ? clone.transform : clone.transform.Find(animatorPath)).GetComponent<Animator>();
                animator.applyRootMotion = false; animator.fireEvents = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var controllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(MountControllerPath);
                if (controllerAsset == null) throw new InvalidOperationException("Mount controller missing.");
                animator.runtimeAnimatorController = controllerAsset;
                graph = PlayableGraph.Create("PumpkinMountAnimatorInspection"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var controller = AnimatorControllerPlayable.Create(graph, controllerAsset);
                var output = AnimationPlayableOutput.Create(graph, "Pome", animator); output.SetSourcePlayable(controller);
                controller.SetLayerWeight(1, 1); controller.Play("PumpkinMount.MountHold", 1, 0); graph.Play();
                var bones = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s => s.bones).Where(b => b != null).Distinct().ToArray();
                Vector3[] previous = null; bool sawHop = false, sawHoldAfterHop = false;
                for (int frame = 0; frame <= 348; frame++)
                {
                    float t = 103.8f + frame / 60f;
                    bool air = t >= 104 && t < 105.4f || t >= 108 && t < 109.4f;
                    float progress = t < 105.4f ? Mathf.InverseLerp(104, 105.4f, t) : Mathf.InverseLerp(108, 109.4f, t);
                    controller.SetBool("PumpkinMountAir", air);
                    if (air) controller.SetFloat("PumpkinMountTime", progress);
                    else controller.SetFloat("PumpkinMountHoldTime", t < 105.4f || t >= 109.4f ? 0 : (t - 105.4f) / 13.75f);
                    graph.Evaluate(1f / 60f); KD_DogPreviewEditor.ApplyConstraints(clone);
                    var positions = bones.Select(b => clone.transform.InverseTransformPoint(b.position)).ToArray();
                    if (previous != null)
                        for (int i = 0; i < positions.Length; i++) maximumStep = Mathf.Max(maximumStep, Vector3.Distance(positions[i], previous[i]));
                    if (positions.Any(v => float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z)))
                        throw new InvalidOperationException("Non-finite mount bone.");
                    if (clone.transform.position.sqrMagnitude > .000001f || Quaternion.Angle(clone.transform.rotation, Quaternion.identity) > .01f)
                        throw new InvalidOperationException("Mount Animator moved its root.");
                    var state = controller.GetCurrentAnimatorStateInfo(1);
                    if (state.IsName("PumpkinMount.MountHop")) sawHop = true;
                    if (sawHop && t > 105.6f && t < 107.9f && state.IsName("PumpkinMount.MountHold")) sawHoldAfterHop = true;
                    if (frame % 6 == 0) records.Add(new { t, air, transition = controller.IsInTransition(1), state = state.fullPathHash,
                        feet = bones.Where(b => b.name == "Bone.014_L" || b.name == "Bone.014_R" || b.name == "Bone.016_L" || b.name == "Bone.016_R")
                            .Select(b => new { b.name, position = new[] { b.position.x, b.position.y, b.position.z } }).ToArray() });
                    previous = positions;
                }
                if (!sawHop || !sawHoldAfterHop) throw new InvalidOperationException("Mount Animator did not enter both authored states.");
                if (maximumStep > .075f) throw new InvalidOperationException("Mount Animator has an abrupt bone displacement: " + maximumStep);
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                EditorSceneManager.ClosePreviewScene(scene);
                CheckMountStudySource(source, dirty, roots, descriptor, network, p, matrices);
            }
            File.WriteAllText(Evidence + "/mount-animator-inspection.json", JsonConvert.SerializeObject(new
            { checkedAt = DateTime.UtcNow.ToString("o"), sourcePreserved = true, previewClosed = !scene.IsValid(),
                hopSha256 = Hash(MountHopPath), controllerSha256 = Hash(MountControllerPath), samples = 349, maximumBoneStep = maximumStep,
                scope = "Real AnimatorController transitions at 60 Hz on an Editor clone. No Udon motor, actual flight/contact or runtime gaze acceptance.", records }, Formatting.Indented));
            return "Mount Animator evaluated 349 frames; both states entered; maximum bone step " + maximumStep + "; world preserved.";
        }
    }
}
