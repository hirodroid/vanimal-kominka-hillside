using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    public static class KD_CatAnimatorBuilderEditor
    {
        public const string ControllerPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_Cat.controller";
        const string Seated2Path = "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AN_CatSuwari2.anim";
        const string BonePath = "hojo_born/Bone/Bone_end/PB3/neko/アーマチュア/ボーン";
        const string ContactPath = "hojo_born/Bone/Bone_end/PB3/neko/contact2";
        static readonly string[] Guids = { "b2d773ea05c3d21468cd01f6bdb64140", "879c991916e724c409ed083085aac93e", "e9b2d8c2f86a97440904ae2b03624991", "fa0314bbcfc2cee409a8704b6ee51f76" };
        static readonly float[] Lengths = { 25.116667f, 6f, 8.5f, 32.216667f };
        static readonly string[] Names = { "Rest", "PetA", "PetB", "RestB" };

        static AnimationClip Source(int index)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(Guids[index]));
            if (clip == null || Mathf.Abs(clip.length - Lengths[index]) > .05f) throw new InvalidOperationException("Seated source mismatch: " + Guids[index]);
            return clip;
        }
        internal static AnimationClip LoadClip(int index)
        {
            var clip = index == 3 ? AssetDatabase.LoadAssetAtPath<AnimationClip>(Seated2Path) : Source(index);
            if (clip == null || Mathf.Abs(clip.length - Lengths[index]) > .05f) throw new InvalidOperationException("Seated clip mismatch: " + Names[index]);
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type != typeof(Transform) || !b.propertyName.StartsWith("m_LocalPosition")) continue;
                if (b.path != BonePath) throw new InvalidOperationException("Unexpected position curve: " + b.path);
                float expected = b.propertyName.EndsWith(".y") ? .0743f : b.propertyName.EndsWith(".z") ? .0502f : 0f;
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                for (int k = 0; k <= 240; k++)
                    if (Mathf.Abs(curve.Evaluate(clip.length * k / 240f) - expected) > .001f) throw new InvalidOperationException("Seated root drift: " + clip.name);
            }
            return clip;
        }
        static bool IsContact(EditorCurveBinding b) { return b.path == ContactPath && b.type == typeof(GameObject) && b.propertyName == "m_IsActive"; }
        static void CopySeated2()
        {
            // World v3 has no avatar contact2. Preserve every body, paw and face curve.
            var clip = UnityEngine.Object.Instantiate(Source(3)); clip.name = "AN_CatSuwari2";
            var contact = AnimationUtility.GetCurveBindings(clip).Where(IsContact).ToArray();
            if (contact.Length != 1) throw new InvalidOperationException("Expected one avatar contact2 curve");
            AnimationUtility.SetEditorCurve(clip, contact[0], null);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(Seated2Path);
            if (existing == null) AssetDatabase.CreateAsset(clip, Seated2Path);
            else { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); UnityEngine.Object.DestroyImmediate(clip); }
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Cat/2 - Build AC_Cat")]
        public static void Build()
        {
            var v = new KD_VerifyLog("[KD_CatAnimatorBuilder] Build");
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Build requires idle Edit mode.");
                CopySeated2();
                var clips = Enumerable.Range(0, 4).Select(LoadClip).ToArray();
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                else
                {
                    while (ctrl.layers.Length > 0) ctrl.RemoveLayer(0);
                    foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(ControllerPath)) if (obj != ctrl) UnityEngine.Object.DestroyImmediate(obj, true);
                    ctrl.parameters = new AnimatorControllerParameter[0]; ctrl.AddLayer("Base Layer");
                }
                ctrl.AddParameter("Pet", AnimatorControllerParameterType.Bool); ctrl.AddParameter("PetIndex", AnimatorControllerParameterType.Int);
                var sm = ctrl.layers[0].stateMachine; var states = new AnimatorState[4];
                for (int i = 0; i < 4; i++) { states[i] = sm.AddState(Names[i], new Vector3(i * 240, 0)); states[i].motion = clips[i]; states[i].writeDefaultValues = true; }
                sm.defaultState = states[0];
                foreach (int rest in new[] { 0, 3 })
                {
                    for (int pet = 1; pet <= 2; pet++)
                    {
                        var t = Transition(states[rest], states[pet], .5f, false);
                        t.AddCondition(AnimatorConditionMode.If, 0, "Pet"); t.AddCondition(AnimatorConditionMode.Equals, pet - 1, "PetIndex");
                    }
                    Transition(states[rest], states[3 - rest], .8f, true).AddCondition(AnimatorConditionMode.IfNot, 0, "Pet");
                }
                for (int pet = 1; pet <= 2; pet++)
                {
                    Transition(states[pet], states[0], .8f, false).AddCondition(AnimatorConditionMode.IfNot, 0, "Pet");
                    Transition(states[pet], states[3 - pet], .5f, true).AddCondition(AnimatorConditionMode.If, 0, "Pet");
                }
                EditorUtility.SetDirty(ctrl); AssetDatabase.SaveAssets();
                v.Check(true, "suwari1 / suwari2 idle; suwarinade1 / 2 pet; GUID retained");
            }
            catch (Exception e) { v.Check(false, e.ToString()); }
            v.Finish("Logs/imp-cat-001-animator-build.txt", "BUILD RESULT");
        }
        static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float seconds, bool exit)
        {
            var t = from.AddTransition(to); t.hasFixedDuration = true; t.duration = seconds; t.hasExitTime = exit; t.exitTime = .98f;
            t.canTransitionToSelf = false; t.interruptionSource = TransitionInterruptionSource.SourceThenDestination; t.orderedInterruption = true; return t;
        }
        internal static void Verify(KD_VerifyLog v, GameObject cat)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!v.Check(ctrl != null, "AC_Cat exists")) return;
            v.Check(ctrl.parameters.Length == 2 && ctrl.parameters.Any(p => p.name == "Pet" && p.type == AnimatorControllerParameterType.Bool) && ctrl.parameters.Any(p => p.name == "PetIndex" && p.type == AnimatorControllerParameterType.Int), "Pet bool / PetIndex int only");
            if (!v.Check(ctrl.layers.Length == 1, "single layer")) return;
            var sm = ctrl.layers[0].stateMachine;
            v.Check(sm.states.Length == 4 && sm.stateMachines.Length == 0 && sm.anyStateTransitions.Length == 0, "four seated states only");
            for (int i = 0; i < 4; i++)
            {
                var state = sm.states.FirstOrDefault(s => s.state.name == Names[i]).state;
                if (!v.Check(state != null, Names[i] + " exists")) continue;
                var clip = LoadClip(i);
                v.Check(state.motion == clip && state.writeDefaultValues, Names[i] + " authored clip / fixed root / write defaults");
                if (i == 0) v.Check(sm.defaultState == state, "suwari1 default");
                int unresolved = cat == null ? -1 : AnimationUtility.GetCurveBindings(clip).Count(b => AnimationUtility.GetAnimatedObject(cat, b) == null);
                v.Check(unresolved == 0, clip.name + " unresolved bindings=" + unresolved);
                bool rest = i == 0 || i == 3;
                v.Check(state.transitions.Length == (rest ? 3 : 2), Names[i] + " transitions");
                if (rest)
                {
                    v.Check(state.transitions.Any(t => t.destinationState.name == Names[3 - i] && t.hasExitTime && t.conditions.Any(c => c.parameter == "Pet" && c.mode == AnimatorConditionMode.IfNot)), Names[i] + " alternates seated idle");
                    for (int p = 0; p < 2; p++) v.Check(state.transitions.Any(t => t.destinationState.name == Names[p + 1] && !t.hasExitTime && t.conditions.Any(c => c.parameter == "Pet" && c.mode == AnimatorConditionMode.If) && t.conditions.Any(c => c.parameter == "PetIndex" && c.mode == AnimatorConditionMode.Equals && c.threshold == p)), Names[i] + " pet zone=" + p);
                }
                else
                {
                    v.Check(state.transitions.Any(t => t.destinationState.name == "Rest" && !t.hasExitTime && t.conditions.Any(c => c.parameter == "Pet" && c.mode == AnimatorConditionMode.IfNot)), Names[i] + " releases to seated idle");
                    v.Check(state.transitions.Any(t => t.destinationState.name == Names[3 - i] && t.hasExitTime && t.conditions.Any(c => c.parameter == "Pet" && c.mode == AnimatorConditionMode.If)), Names[i] + " alternates petting");
                }
            }
            var source = Source(3); var copy = LoadClip(3);
            var authored = AnimationUtility.GetCurveBindings(source).Where(b => !IsContact(b)).ToArray();
            v.Check(AnimationUtility.GetCurveBindings(copy).Length == authored.Length && authored.All(b => {
                var a = AnimationUtility.GetEditorCurve(source, b); var c = AnimationUtility.GetEditorCurve(copy, b);
                return c != null && a.keys.SequenceEqual(c.keys) && a.preWrapMode == c.preWrapMode && a.postWrapMode == c.postWrapMode;
            }), "suwari2 body / paw / face curves identical to source; only contact2 omitted");
        }
    }
}
