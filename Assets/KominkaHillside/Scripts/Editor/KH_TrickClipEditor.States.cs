using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KominkaDogRun.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KominkaHillside.Editor
{
    // IMP-DOG-054 wave 1-A: the CallBang (CallPose 8) / CallJump (CallPose 9) one-shot states on the six companion controllers.
    //
    // Why a separate array instead of extending KD_CallPoseEditor.States: the 7 existing call states stay the responsibility of
    // KD_CallPoseEditor (it creates them in ConfigureAnimator and checks them in Verify / AnimatorConfigured). Those count only
    // the names in its own States array, so two more states with other names cannot make AnimatorConfigured() report
    // "not configured" and cannot make the next Menu/6j rebuild the 7 states (which would change their fileIDs).
    //
    // Idempotent by repair, not by rebuild: an existing CallBang / CallJump whose entry and exits already have the right shape is
    // kept as it is (no new fileIDs, no .controller diff on a re-run); only a missing or mismatching part is replaced.
    // If Menu/6j ever rebuilds the 7 call states, run Tricks/2b; run Tricks/2 only when 2b reports NG for the trick states.
    public static partial class KH_TrickClipEditor
    {
        public static readonly string[] TrickStates = { "CallBang", "CallJump" };
        public static readonly int[] TrickCallPoses = { 8, 9 };          // KD_SleepSlot trickKind 2 / 3 -> CallPose 8 / 9 (IMP-DOG-054 §2.1)
        public const float EntryDuration = 0.2f, ExitDefaultDuration = 0.2f, ExitMoveDuration = 0f;
        static readonly string[] MoveStates = { "Run", "Walk" };
        // Below the call column (950, 0..540) and clear of the frisbee states (950, 640 / 720).
        static Vector3 StatePosition(int i) { return new Vector3(1250, 630 + 90 * i, 0); }

        [MenuItem("Tools/KominkaHillside/Tricks/2 Add Trick States")]
        public static void AddTrickStates()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var log = new StringBuilder(); int changedControllers = 0;
            foreach (var def in Defs)
            {
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
                if (ctrl == null) throw new InvalidOperationException("Missing controller " + def.ControllerPath);
                var changes = new List<string>();
                RepairParameters(ctrl, changes);
                for (int i = 0; i < TrickStates.Length; i++)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPathFor(i == 0 ? BangName : JumpName, def.key));
                    if (clip == null) throw new InvalidOperationException("Build the trick clips first (Tricks/1): " + ClipPathFor(i == 0 ? BangName : JumpName, def.key));
                    RepairState(ctrl, TrickStates[i], TrickCallPoses[i], clip, i, changes);
                }
                if (changes.Count > 0) { EditorUtility.SetDirty(ctrl); changedControllers++; }
                log.AppendLine(def.key + ": " + (changes.Count == 0 ? "unchanged" : string.Join("; ", changes)));
            }
            AssetDatabase.SaveAssets();
            System.IO.Directory.CreateDirectory(Output);
            System.IO.File.WriteAllText(Output + "/states.txt", log.ToString());
            Debug.Log("[KH Tricks] trick states: " + changedControllers + " controller(s) changed\n" + log);
        }

        static void RepairParameters(AnimatorController ctrl, List<string> changes)
        {
            var wanted = new[] { ("CallReady", AnimatorControllerParameterType.Bool), ("CallPose", AnimatorControllerParameterType.Int), ("CallTime", AnimatorControllerParameterType.Float) };
            foreach (var (name, type) in wanted)
            {
                var existing = ctrl.parameters.FirstOrDefault(p => p.name == name);
                if (existing == null) { ctrl.AddParameter(name, type); changes.Add("added parameter " + name); }
                else if (existing.type != type) throw new InvalidOperationException(ctrl.name + ": parameter " + name + " is " + existing.type + ", expected " + type + " (not changed; fix by hand)");
            }
        }

        static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            var s = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == name);
            if (s == null) throw new InvalidOperationException("State " + name + " missing");
            return s;
        }

        static bool HasCondition(AnimatorTransitionBase t, AnimatorConditionMode mode, string parameter, float threshold = 0f)
        {
            return t.conditions.Any(c => c.mode == mode && c.parameter == parameter && (mode != AnimatorConditionMode.Equals || Mathf.Approximately(c.threshold, threshold)));
        }

        // AnyState -> state, the shape KD_CallPoseEditor.ConfigureAnimator gives CallStretch.
        internal static bool EntryOk(AnimatorStateTransition t, int pose)
        {
            return t != null && !t.hasExitTime && t.hasFixedDuration && Mathf.Abs(t.duration - EntryDuration) < 1e-4f && !t.canTransitionToSelf
                && !t.mute && !t.solo && t.conditions.Length == 2
                && HasCondition(t, AnimatorConditionMode.If, "CallReady") && HasCondition(t, AnimatorConditionMode.Equals, "CallPose", pose);
        }

        // state -> Run / Walk (!CallReady && move, 0 s) and -> default state (!CallReady, 0.2 s); exactly these three.
        internal static bool ExitsOk(AnimatorStateMachine sm, AnimatorState state, out string why)
        {
            why = null;
            var ts = state.transitions;
            if (ts.Length != 3) { why = "exits " + ts.Length + " != 3"; return false; }
            bool Exit(AnimatorStateTransition t, AnimatorState target, float duration, string move)
            {
                return t.destinationState == target && !t.isExit && !t.hasExitTime && t.hasFixedDuration && Mathf.Abs(t.duration - duration) < 1e-4f
                    && !t.mute && !t.solo && t.conditions.Length == (move == null ? 1 : 2)
                    && HasCondition(t, AnimatorConditionMode.IfNot, "CallReady") && (move == null || HasCondition(t, AnimatorConditionMode.If, move));
            }
            if (sm.defaultState == null) { why = "no default state"; return false; }
            foreach (var move in MoveStates)
            {
                var target = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == move);
                if (target == null) { why = "state " + move + " missing"; return false; }
                if (ts.Count(t => Exit(t, target, ExitMoveDuration, move)) != 1) { why = "exit to " + move + " missing or wrong"; return false; }
            }
            if (ts.Count(t => Exit(t, sm.defaultState, ExitDefaultDuration, null)) != 1) { why = "exit to default " + sm.defaultState.name + " missing or wrong"; return false; }
            return true;
        }

        static void RepairState(AnimatorController ctrl, string name, int pose, AnimationClip clip, int index, List<string> changes)
        {
            var sm = ctrl.layers[0].stateMachine;
            var named = sm.states.Select(x => x.state).Where(x => x.name == name).ToArray();
            AnimatorState state = named.Length == 1 ? named[0] : null;
            if (state == null)
            {
                foreach (var s in named) RemoveStateAndTransitions(sm, s);
                state = sm.AddState(name, StatePosition(index));
                changes.Add(named.Length == 0 ? "added state " + name : "recreated state " + name + " (had " + named.Length + ")");
            }
            if (state.motion != clip) { state.motion = clip; changes.Add(name + " motion"); }
            if (!state.timeParameterActive || state.timeParameter != "CallTime")
            {
                state.timeParameterActive = true; state.timeParameter = "CallTime"; changes.Add(name + " time parameter CallTime");
            }
            // Transitions from ordinary states into a trick state are not part of the shape (only AnyState enters it).
            foreach (var other in sm.states.Select(x => x.state))
                foreach (var t in other.transitions.Where(t => t.destinationState == state).ToArray()) { other.RemoveTransition(t); changes.Add("removed " + other.name + " -> " + name); }
            var entries = sm.anyStateTransitions.Where(t => t.destinationState == state).ToArray();
            if (entries.Length != 1 || !EntryOk(entries[0], pose))
            {
                foreach (var t in entries) sm.RemoveAnyStateTransition(t);
                var enter = sm.AddAnyStateTransition(state);
                enter.hasExitTime = false; enter.hasFixedDuration = true; enter.duration = EntryDuration; enter.canTransitionToSelf = false;
                enter.AddCondition(AnimatorConditionMode.If, 0, "CallReady");
                enter.AddCondition(AnimatorConditionMode.Equals, pose, "CallPose");
                changes.Add(name + " entry (CallReady && CallPose == " + pose + ")");
            }
            string why;
            if (!ExitsOk(sm, state, out why))
            {
                foreach (var t in state.transitions.ToArray()) state.RemoveTransition(t);
                foreach (var move in MoveStates)
                {
                    var t = state.AddTransition(FindState(sm, move)); t.hasExitTime = false; t.hasFixedDuration = true; t.duration = ExitMoveDuration;
                    t.AddCondition(AnimatorConditionMode.IfNot, 0, "CallReady");
                    t.AddCondition(AnimatorConditionMode.If, 0, move);
                }
                var leave = state.AddTransition(sm.defaultState); leave.hasExitTime = false; leave.hasFixedDuration = true; leave.duration = ExitDefaultDuration;
                leave.AddCondition(AnimatorConditionMode.IfNot, 0, "CallReady");
                changes.Add(name + " exits (" + why + ")");
            }
        }

        static void RemoveStateAndTransitions(AnimatorStateMachine sm, AnimatorState state)
        {
            foreach (var t in sm.anyStateTransitions.Where(t => t.destinationState == state).ToArray()) sm.RemoveAnyStateTransition(t);
            foreach (var other in sm.states.Select(x => x.state))
                foreach (var t in other.transitions.Where(t => t.destinationState == state).ToArray()) other.RemoveTransition(t);
            sm.RemoveState(state);
        }
    }
}
