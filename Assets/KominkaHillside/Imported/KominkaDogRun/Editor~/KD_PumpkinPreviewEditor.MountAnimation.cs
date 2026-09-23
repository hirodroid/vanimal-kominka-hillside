using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinPreviewEditor
    {
        public const string MountAnimationDirectory = "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/PumpkinPreview";
        public const string MountHopPath = MountAnimationDirectory + "/KD_Pumpkin_PomeHop.anim";
        public const string MountControllerPath = MountAnimationDirectory + "/KD_Pumpkin_Pome.controller";
        public const string MountLayerName = "PumpkinMount";

        // Generate opt-in assets only. Wiring the copied scene is a separate operation.
        // The hop blends one native Pome run cycle with its measured standing pose;
        // the world-space ascent/descent remains owned by the movement controller.
        public static string BuildMountAnimationAssets()
        {
            GuardStage();
            var p = One<KD_CandyDogPerformance>();
            var dog = p.dogs.Single(d => d.dogRoot.name == "KD_Dog_Pome");
            var definition = KD_DogDefs.All.Single(d => d.key == "Pome");
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(definition.idle.guid));
            var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(definition.run.guid));
            string originalPath = AssetDatabase.GetAssetPath(dog.dogAnimator.runtimeAnimatorController);
            if (!originalPath.StartsWith("Assets/KominkaHillside/Imported/KominkaDogRun/", StringComparison.Ordinal) || originalPath == MountControllerPath
                || idle == null || run == null || Math.Abs(run.length - 1.4f) > .002f)
                throw new InvalidOperationException("Native Pome clips and an original self-owned controller are required.");
            string controllerHash = Hash(originalPath);
            var sourceScene = SceneManager.GetActiveScene(); bool dirty = sourceScene.isDirty;
            var roots = sourceScene.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(i => i).ToArray();
            var descriptor = sourceScene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<VRC.SDK3.Components.VRCSceneDescriptor>(true)).Single();
            var network = descriptor.NetworkIDCollection.Select(n => n?.gameObject == null ? 0 : n.gameObject.GetInstanceID()).ToArray();
            var matrices = p.dogs.Select(d => d.dogRoot.localToWorldMatrix).ToArray();
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var hop = new AnimationClip { name = "KD_Pumpkin_PomeHop", frameRate = 30 };
            GameObject clone = null;
            try
            {
                clone = MountStudyClone(p, "Pome", false, 0, preview);
                var bindings = AnimationUtility.GetCurveBindings(idle).Concat(AnimationUtility.GetCurveBindings(run)).Distinct().ToArray();
                if (bindings.Any(b => b.type == typeof(Transform) && string.IsNullOrEmpty(b.path)))
                    throw new InvalidOperationException("The jump may not animate the network root transform.");
                var rotations = bindings.Where(IsMountRotation)
                    .GroupBy(b => b.path).ToArray();
                var rotationPaths = new HashSet<string>(rotations.Select(g => g.Key));
                foreach (var binding in bindings)
                {
                    if (IsMountRotation(binding)) continue;
                    var idleCurve = AnimationUtility.GetEditorCurve(idle, binding);
                    var runCurve = AnimationUtility.GetEditorCurve(run, binding);
                    if (idleCurve == null && runCurve == null) continue;
                    // Missing channels use the actual sampled standing transform, not zero.
                    float baseline = idleCurve != null ? idleCurve.Evaluate(0) : MountBaseline(clone, binding);
                    var curve = new AnimationCurve();
                    for (int k = 0; k <= 42; k++)
                    {
                        float time = k / 30f, u = time / 1.4f;
                        float weight = MountHopWeight(u);
                        float moving = runCurve != null ? runCurve.Evaluate(time) : baseline;
                        curve.AddKey(time, Mathf.Lerp(baseline, moving, weight));
                    }
                    LinearMountCurve(curve); AnimationUtility.SetEditorCurve(hop, binding, curve);
                }
                foreach (var group in rotations)
                {
                    var bone = clone.transform.Find(group.Key);
                    if (bone == null) throw new InvalidOperationException("Pome rotation path missing: " + group.Key);
                    var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                    var channels = "xyzw";
                    var sourceBinding = AnimationUtility.GetCurveBindings(run).FirstOrDefault(b => b.path == group.Key && IsMountRotation(b));
                    bool euler = sourceBinding.propertyName != null && sourceBinding.propertyName.Contains("Euler");
                    string prefix = sourceBinding.propertyName == null ? "m_LocalRotation" : sourceBinding.propertyName.Substring(0, sourceBinding.propertyName.LastIndexOf('.'));
                    var sourceCurves = channels.Select(c => AnimationUtility.GetEditorCurve(run,
                        EditorCurveBinding.FloatCurve(group.Key, typeof(Transform), prefix + "." + c))).ToArray();
                    Quaternion baseline = bone.localRotation, previous = baseline;
                    for (int k = 0; k <= 42; k++)
                    {
                        float time = k / 30f; var moving = baseline;
                        if (euler)
                        {
                            var angles = baseline.eulerAngles;
                            for (int axis = 0; axis < 3; axis++) if (sourceCurves[axis] != null) angles[axis] = sourceCurves[axis].Evaluate(time);
                            moving = Quaternion.Euler(angles);
                        }
                        else
                        {
                            for (int axis = 0; axis < 4; axis++) if (sourceCurves[axis] != null) moving[axis] = sourceCurves[axis].Evaluate(time);
                            moving = Quaternion.Normalize(moving);
                        }
                        var q = Quaternion.Slerp(baseline, moving, MountHopWeight(time / 1.4f));
                        if (Quaternion.Dot(previous, q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        for (int axis = 0; axis < 4; axis++) curves[axis].AddKey(time, q[axis]);
                        previous = q;
                    }
                    for (int axis = 0; axis < 4; axis++)
                    { LinearMountCurve(curves[axis]); AnimationUtility.SetEditorCurve(hop, EditorCurveBinding.FloatCurve(group.Key, typeof(Transform), "m_LocalRotation." + channels[axis]), curves[axis]); }
                }
                hop.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(hop); settings.loopTime = false; AnimationUtility.SetAnimationClipSettings(hop, settings);
                Directory.CreateDirectory(MountAnimationDirectory); AssetDatabase.Refresh();
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(MountHopPath);
                if (existing == null) { AssetDatabase.CreateAsset(hop, MountHopPath); hop = null; }
                else { EditorUtility.CopySerialized(hop, existing); EditorUtility.SetDirty(existing); }
                if (!File.Exists(MountControllerPath) && !AssetDatabase.CopyAsset(originalPath, MountControllerPath))
                    throw new IOException("Could not copy the Pome controller.");
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(MountControllerPath);
                if (controller == null) throw new InvalidOperationException("Mount controller copy missing.");
                int index = ConfigureMountLayer(controller, idle);
                EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
                if (Hash(originalPath) != controllerHash) throw new InvalidOperationException("Original controller changed.");
                File.WriteAllText(Evidence + "/mount-animation.json", JsonConvert.SerializeObject(new
                { generatedAt = DateTime.UtcNow.ToString("o"), originalPath, controllerHash, hop = MountHopPath, controller = MountControllerPath,
                    hopHash = Hash(MountHopPath), mountLayer = index, channels = bindings.Length, rotations = rotationPaths.Count,
                    idle = AssetDatabase.GetAssetPath(idle), run = AssetDatabase.GetAssetPath(run), idleHash = Hash(AssetDatabase.GetAssetPath(idle)), runHash = Hash(AssetDatabase.GetAssetPath(run)),
                    sceneWired = false, scope = "Opt-in animation assets only. Airborne geometry and movement integration pending." }, Formatting.Indented));
                return "Mount animation assets built; scene controller unchanged; layer " + index;
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                if (hop != null) UnityEngine.Object.DestroyImmediate(hop);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
                CheckMountStudySource(sourceScene, dirty, roots, descriptor, network, p, matrices);
            }
        }

        // Shared with production: add only the mount layer to the latest self-owned controller.
        public static int ConfigureMountLayer(AnimatorController controller, AnimationClip idle)
        {
            var parameters = controller.parameters;
            foreach (string parameter in new[] { "PumpkinMountTime", "PumpkinMountHoldTime" })
                if (!parameters.Any(v => v.name == parameter)) controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            if (!parameters.Any(v => v.name == "PumpkinMountAir")) controller.AddParameter("PumpkinMountAir", AnimatorControllerParameterType.Bool);
            if (parameters.Any(v => v.name == "PumpkinMountHop")) controller.RemoveParameter(Array.FindIndex(controller.parameters, v => v.name == "PumpkinMountHop"));
            int index = Array.FindIndex(controller.layers, l => l.name == MountLayerName);
            if (index < 0) { controller.AddLayer(MountLayerName); index = controller.layers.Length - 1; }
            var layers = controller.layers; var layer = layers[index]; layer.defaultWeight = 0; layer.blendingMode = AnimatorLayerBlendingMode.Override; layers[index] = layer; controller.layers = layers;
            var sm = layer.stateMachine;
            var state = sm.states.FirstOrDefault(s => s.state.name == "Mount" || s.state.name == "MountHold").state;
            if (state == null) state = sm.AddState("MountHold");
            state.name = "MountHold";
            var tree = state.motion as BlendTree;
            state.motion = idle; state.timeParameterActive = true; state.timeParameter = "PumpkinMountHoldTime"; state.writeDefaultValues = false;
            if (tree != null && tree.name == "PumpkinMountPose" && AssetDatabase.GetAssetPath(tree) == AssetDatabase.GetAssetPath(controller)) UnityEngine.Object.DestroyImmediate(tree, true);
            var air = sm.states.FirstOrDefault(s => s.state.name == "MountHop").state;
            if (air == null) air = sm.AddState("MountHop");
            air.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(MountHopPath); air.timeParameterActive = true; air.timeParameter = "PumpkinMountTime"; air.writeDefaultValues = false;
            foreach (var transition in state.transitions) state.RemoveTransition(transition);
            foreach (var transition in air.transitions) air.RemoveTransition(transition);
            var intoAir = state.AddTransition(air); intoAir.hasExitTime = false; intoAir.hasFixedDuration = true; intoAir.duration = .12f;
            intoAir.AddCondition(AnimatorConditionMode.If, 0, "PumpkinMountAir");
            var intoHold = air.AddTransition(state); intoHold.hasExitTime = false; intoHold.hasFixedDuration = true; intoHold.duration = .12f;
            intoHold.AddCondition(AnimatorConditionMode.IfNot, 0, "PumpkinMountAir");
            sm.defaultState = state;
            return index;
        }

        private static float MountHopWeight(float u)
        { return Mathf.SmoothStep(0, 1, Mathf.Clamp01(u / .18f)) * Mathf.SmoothStep(0, 1, Mathf.Clamp01((1 - u) / .18f)); }

        public static string VerifyMountRuntimeCompilation()
        {
            GuardStage();
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
            // Read the compiler's current diagnostics, not old Console entries.
            // These exact members are present in the installed SDK source.
            var cacheType = typeof(UdonSharp.UdonSharpProgramAsset).Assembly.GetType("UdonSharp.UdonSharpEditorCache", true);
            var cache = cacheType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).GetValue(null);
            bool failed = (bool)cacheType.GetMethod("HasUdonSharpCompileError", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(cache, null);
            if (failed) throw new InvalidOperationException("Current UdonSharp compile diagnostics contain errors.");
            foreach (var pair in new[] { new[] { "KD_DogWanderController", "candyMountArmed" }, new[] { "KD_CandyDogPerformance", "useMount" }, new[] { "KD_CandyDogPerformance", "costumeGlowRenderers" } })
            {
                var asset = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/" + pair[0] + ".asset");
                if (asset == null || asset.SerializedProgramAsset == null) throw new InvalidOperationException("Compiled asset missing: " + pair[0]);
                var program = asset.SerializedProgramAsset.RetrieveProgram();
                if (program == null || !program.SymbolTable.GetSymbols().Contains(pair[1])) throw new InvalidOperationException("Compiled mount field missing: " + pair[1]);
            }
            VerifySource();
            File.WriteAllText(Evidence + "/mount-runtime-compilation.json", JsonConvert.SerializeObject(new
            { checkedAt = DateTime.UtcNow.ToString("o"), compilerHasErrors = failed,
                files = new[] { "KD_DogWanderController.CandyMount.cs", "KD_DogWanderController.Candy.cs", "KD_CandyDogPerformance.Mount.cs",
                    "KD_CandyDogPerformance.cs", "KD_CandyDogPerformance.Choreography.cs", "KD_CandyDogPerformance.CostumeGlow.cs", "KD_DogFetchController.cs" }
                    .Select(f => new { file = f, sha256 = Hash("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/" + f) }),
                scope = "Current UdonSharp compile diagnostics and new serialized program symbols only; runtime/scene wiring unverified." }, Formatting.Indented));
            return "Current UdonSharp diagnostics: no errors; mount and costume glow symbols are in compiled programs.";
        }
        private static bool IsMountRotation(EditorCurveBinding binding)
        { return binding.type == typeof(Transform) && (binding.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal) || binding.propertyName.Contains("Euler")); }
        private static void LinearMountCurve(AnimationCurve curve)
        {
            for (int k = 0; k < curve.length; k++)
            { AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear); }
        }
        private static float MountBaseline(GameObject clone, EditorCurveBinding binding)
        {
            var target = clone.transform.Find(binding.path);
            if (target == null || binding.type != typeof(Transform)) throw new InvalidOperationException("Missing idle baseline: " + binding.path + " " + binding.propertyName);
            int axis = "xyz".IndexOf(binding.propertyName[binding.propertyName.Length - 1]);
            if (axis < 0) throw new InvalidOperationException("Unsupported mount baseline channel.");
            if (binding.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal)) return target.localPosition[axis];
            if (binding.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal)) return target.localScale[axis];
            throw new InvalidOperationException("Unsupported mount baseline property: " + binding.propertyName);
        }
    }
}
