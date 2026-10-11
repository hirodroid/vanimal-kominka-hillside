using System;
using System.Linq;
using System.Reflection;
using KominkaDogRun;
using KominkaDogRun.Editor;
using UdonSharp;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KominkaHillside.Editor
{
    // IMP-DOG-049 段 B (2026-10-11): Wash/12's read-only lines for the dogs' water play on a wash stand (the hose play / the pistol
    // hop, KD_DogWanderController.HillsideWash.cs). Nothing is written to the scene: the values are the code's constant table
    // (KH_DogWash.WaterPlay*, Q12 trial values), the Jump state already exists in every AC_KD_Dog_<breed>.controller (Frisbee/2),
    // and the jaw is found at runtime from the pet controller's head write bone. So there is no setup menu for this stage; these
    // lines print what the runtime will use and fail when the runtime lookup would not find what the editor rig resolver finds.
    public static partial class KH_DogWashSetupEditor
    {
        const string WaterJawBoneName = "Bone.006";   // KD_DogWanderController.WaterJawBone (KD_DogRig: chin, parent Bone.004)

        static void CheckWaterPlay(VerifyReport r, Action<bool, string> req, SceneCtx ctx)
        {
            // ---- the table (one line, the actual compiled values) ----
            string parts = string.Join(",", Enumerable.Range(0, PartNames.Length).Where(p => ((KH_DogWash.WaterPlayParts >> p) & 1) != 0).Select(p => PartNames[p]));
            string table = "parts " + parts
                + " / hose first " + KH_DogWash.WaterPlayHoseFirst.ToString("0.##") + " s, every " + KH_DogWash.WaterPlayHoseSpacing.ToString("0.##") + " s, ends " + KH_DogWash.WaterPlayHoseEnd.ToString("0.##") + " s after the water"
                + " / pistol every " + KH_DogWash.WaterPlayPistolSpacing.ToString("0.##") + " s"
                + " / play " + KH_DogWash.WaterPlaySeconds.ToString("0.##") + " s, hop " + KH_DogWash.WaterPlayHopMinSeconds.ToString("0.##") + ".." + KH_DogWash.WaterPlayHopMaxSeconds.ToString("0.##") + " s"
                + " / hop height " + KH_DogWash.WaterPlayHopRatio.ToString("0.##") + " x head, " + KH_DogWash.WaterPlayHopMin.ToString("0.###") + ".." + KH_DogWash.WaterPlayHopMax.ToString("0.###") + " m"
                + " / jaw " + KH_DogWash.WaterPlayJawDegrees.ToString("0.#") + " deg at " + KH_DogWash.WaterPlayJawHz.ToString("0.#") + " Hz"
                + " / gaze out " + KH_DogWash.WaterPlayGazeOut.ToString("0.##") + " m / voice " + KH_DogWash.WaterPlayVoiceVolume.ToString("0.##");
            bool sane = KH_DogWash.WaterPlayParts != 0 && KH_DogWash.WaterPlayHoseFirst > 0f && KH_DogWash.WaterPlayHoseSpacing > KH_DogWash.WaterPlaySeconds
                && KH_DogWash.WaterPlayHoseEnd > 0.2f   // > 1.5 x the 8 Hz stroke interval: a steady hose never breaks its run
                && KH_DogWash.WaterPlayPistolSpacing > KH_DogWash.WaterPlayHopMaxSeconds
                && KH_DogWash.WaterPlayHopMinSeconds <= KH_DogWash.WaterPlayHopMaxSeconds && KH_DogWash.WaterPlayHopMin > 0f && KH_DogWash.WaterPlayHopMax <= 0.25f;
            req(sane, "water play table (KH_DogWash.WaterPlay*, Q12 trial values): " + table);
            var synced = typeof(KH_DogWash).GetField("waterPlay", BindingFlags.NonPublic | BindingFlags.Instance);
            req(synced != null && synced.FieldType == typeof(int) && synced.GetCustomAttributes(typeof(UdonSyncedAttribute), false).Length == 1,
                "KH_DogWash.waterPlay is a synced int (serial << 6 | part << 2 | kind)");

            foreach (var w in ctx.wanders)
            {
                string who = "water play " + w.dogIndex + " (" + w.name + ")";
                if (w.dogRoot == null) { req(false, who + ": dogRoot missing"); continue; }

                // ---- the Jump state (Frisbee/2 adds it to the Hillside derivative controllers; nothing here changes them) ----
                var ac = w.dogAnimator != null ? w.dogAnimator.runtimeAnimatorController as AnimatorController : null;
                string acPath = ac != null ? AssetDatabase.GetAssetPath(ac) : "(none)";
                var sm = ac != null && ac.layers.Length > 0 ? ac.layers[0].stateMachine : null;
                var jump = sm != null ? sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Jump") : null;
                bool param = ac != null && ac.parameters.Any(p => p.name == "Jump" && p.type == AnimatorControllerParameterType.Bool);
                bool any = sm != null && jump != null && sm.anyStateTransitions.Any(t => t.destinationState == jump && t.conditions.Any(c => c.parameter == "Jump" && c.mode == AnimatorConditionMode.If));
                bool back = jump != null && jump.transitions.Any(t => t.destinationState != null && t.destinationState.name == "Idle" && t.conditions.Any(c => c.parameter == "Jump" && c.mode == AnimatorConditionMode.IfNot));
                req(ac != null && acPath.StartsWith("Assets/KominkaHillside/") && param && jump != null && jump.motion != null && any && back,
                    who + ": Jump Bool + state (" + (jump != null && jump.motion != null ? jump.motion.name : "no motion") + ") + Any State in / Idle out in " + acPath);

                // ---- the hop: the clip's timing (frisbeeCatch, per breed) and the height (head above the root, saved pose) ----
                var fc = w.frisbeeCatch;
                float crouch = fc != null ? fc.clipCrouchDuration : 0.25f, rise = fc != null ? fc.clipRiseDuration : 0.35f, recover = fc != null ? fc.clipRecoverDuration : 0.4f;
                float len = Mathf.Clamp(crouch + 2f * rise + recover, KH_DogWash.WaterPlayHopMinSeconds, KH_DogWash.WaterPlayHopMaxSeconds);
                var pet = w.petController;
                float head = pet != null && pet.headAnchor != null ? pet.headAnchor.position.y - w.dogRoot.position.y : -1f;
                float hop = Mathf.Clamp(head * KH_DogWash.WaterPlayHopRatio, KH_DogWash.WaterPlayHopMin, KH_DogWash.WaterPlayHopMax);
                float clipLen = jump != null && jump.motion is AnimationClip clip ? clip.length : -1f;
                req(fc != null && head > 0.1f && head < 1.2f && rise > 0f && Mathf.Abs(clipLen - (crouch + 2f * rise + recover)) < 0.05f,
                    who + ": hop " + hop.ToString("F3") + " m (head " + head.ToString("F3") + " m x " + KH_DogWash.WaterPlayHopRatio.ToString("0.##")
                    + ") in the air " + (2f * rise).ToString("F2") + " s after the crouch " + crouch.ToString("F2") + " s; play " + len.ToString("F2")
                    + " s; Jump clip " + clipLen.ToString("F2") + " s = crouch + 2 rise + recover " + (crouch + 2f * rise + recover).ToString("F2") + " s (frisbeeCatch " + (fc != null ? fc.name : "null") + ")");

                // ---- the jaw: the runtime lookup = the editor rig's write bone for the chin, and no PhysBone rooted there ----
                Transform runtimeJaw = pet != null && pet.sourceBone != null ? pet.sourceBone.Find(WaterJawBoneName) : null;
                Transform truth = null, chin = null; string why = "";
                try
                {
                    var rig = KD_DogRig.Resolve(w.dogRoot.gameObject);
                    chin = rig.chin;
                    truth = chin != null ? rig.WriteBone(chin, "顎") : null;
                    if (truth == null) why = " (" + string.Join("; ", rig.problems) + ")";
                }
                catch (Exception e) { why = " (" + e.Message + ")"; }
                var rooted = PhysBones(w.dogRoot.gameObject).Select(PbRoot).Where(t => t != null && (t == runtimeJaw || t == chin)).ToList();
                req(runtimeJaw != null && runtimeJaw == truth && rooted.Count == 0,
                    who + ": jaw = " + TPath(runtimeJaw, w.dogRoot) + " (head write bone " + TPath(pet != null ? pet.sourceBone : null, w.dogRoot) + " / child " + WaterJawBoneName
                    + "), editor write bone " + TPath(truth, w.dogRoot) + why + ", PhysBone roots on it " + rooted.Count);
            }
        }

        static string TPath(Transform t, Transform root)
        {
            if (t == null) return "null";
            string s = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
