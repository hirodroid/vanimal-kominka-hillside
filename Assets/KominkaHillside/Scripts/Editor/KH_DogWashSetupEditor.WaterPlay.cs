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
                Transform truth = null, chin = null, nose = null; string why = "";
                try
                {
                    var rig = KD_DogRig.Resolve(w.dogRoot.gameObject);
                    chin = rig.chin;
                    nose = rig.nose;
                    truth = chin != null ? rig.WriteBone(chin, "顎") : null;
                    if (truth == null) why = " (" + string.Join("; ", rig.problems) + ")";
                }
                catch (Exception e) { why = " (" + e.Message + ")"; }
                var rooted = PhysBones(w.dogRoot.gameObject).Select(PbRoot).Where(t => t != null && (t == runtimeJaw || t == chin)).ToList();
                req(runtimeJaw != null && runtimeJaw == truth && rooted.Count == 0,
                    who + ": jaw = " + TPath(runtimeJaw, w.dogRoot) + " (head write bone " + TPath(pet != null ? pet.sourceBone : null, w.dogRoot) + " / child " + WaterJawBoneName
                    + "), editor write bone " + TPath(truth, w.dogRoot) + why + ", PhysBone roots on it " + rooted.Count);

                // ---- the jaw's opening direction (2026-10-11; the 段 B risk "顎が開く向き (+ = 下) は未確認") ----
                JawOpensDown(req, who, w, runtimeJaw, chin, nose);
            }
        }

        // The runtime's write (KD_DogWanderController.WaterPlayJawLate): jaw.localRotation = AngleAxis(open, Inverse(jaw.parent.rotation) *
        // dogRoot.right) * pose, open = 0..WaterPlayJawDegrees, i.e. the world pre-rotation D = AngleAxis(open, dogRoot.right) about the jaw's
        // own pivot. On a two-tier rig the bone written is the constraint source and a RotationConstraint copies its world rotation to the skin
        // bone (KD_DogRig chin, mesh side) in the frame loop, which does not run inside this method: so the probe writes the runtime formula on
        // the source and D on the skin bone (the same bone on a one-tier rig) and requires the two world deltas to agree. Measured: the chin's
        // skin (vertices weighted >= 0.5 to the chin bone, BakeMesh(useScale) x localToWorldMatrix, vrc-character-rig §13) must go down along
        // -dogRoot.up and away from the nose's skin (KD_DogRig nose, Bone.007 under the head: the jaw does not move it). No skin on the chin
        // = the farthest descendant of the chin as the tip. Both bones go back exactly in finally (checked), and the scene's dirty flag
        // must not change (nothing is recorded or saved).
        const float JawDownMin = 0.001f;   // m: at least 1 mm down at the full +WaterPlayJawDegrees
        static void JawOpensDown(Action<bool, string> req, string who, KD_DogWanderController w, Transform jaw, Transform chin, Transform nose)
        {
            float deg = KH_DogWash.WaterPlayJawDegrees;
            string label = who + ": jaw opens down at +" + deg.ToString("0.#") + " deg (the runtime's write)";
            if (jaw == null || chin == null || jaw.parent == null || w.dogRoot == null) { req(false, label + ": jaw / chin bone missing"); return; }
            var scene = w.gameObject.scene;
            bool dirty0 = scene.isDirty;
            Quaternion jaw0 = jaw.localRotation, chin0 = chin.localRotation;
            var smrs = w.dogRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => x.sharedMesh != null && x.bones != null).ToArray();
            Vector3 up = w.dogRoot.up.normalized, fwd = w.dogRoot.forward.normalized;
            Vector3 c0 = Vector3.zero, c1 = Vector3.zero, noseAt = Vector3.zero;
            int chinVerts = 0, noseVerts = 0;
            string method = "", err = null;
            float agree = -1f;
            Transform tip = null;
            try
            {
                c0 = SkinCentroid(smrs, chin, out chinVerts);
                if (chinVerts == 0)
                {
                    float far = 0.01f;
                    foreach (var d in chin.GetComponentsInChildren<Transform>(true)) { float dd = (d.position - chin.position).magnitude; if (d != chin && dd > far) { far = dd; tip = d; } }
                    if (tip != null) c0 = tip.position;
                }
                method = chinVerts > 0 ? chinVerts + " chin vertices" : tip != null ? "tip " + tip.name + " (no skin weighted to the chin)" : "nothing to measure";
                if (nose != null) { noseAt = SkinCentroid(smrs, nose, out noseVerts); if (noseVerts == 0) noseAt = nose.position; }
                // the runtime formula on the written bone, D on the skin bone
                Quaternion jawW0 = jaw.rotation, chinW0 = chin.rotation;
                jaw.localRotation = Quaternion.AngleAxis(deg, Quaternion.Inverse(jaw.parent.rotation) * w.dogRoot.right) * jaw0;
                Quaternion dJaw = jaw.rotation * Quaternion.Inverse(jawW0);
                if (chin != jaw) chin.rotation = Quaternion.AngleAxis(deg, w.dogRoot.right) * chinW0;
                Quaternion dChin = chin.rotation * Quaternion.Inverse(chinW0);
                agree = Quaternion.Angle(dJaw, dChin);
                int n1;
                if (chinVerts > 0) c1 = SkinCentroid(smrs, chin, out n1);
                else if (tip != null) c1 = tip.position;
            }
            catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
            finally
            {
                jaw.localRotation = jaw0;
                chin.localRotation = chin0;
            }
            bool restored = SameQ(jaw.localRotation, jaw0) && SameQ(chin.localRotation, chin0);
            bool dirtyKept = scene.isDirty == dirty0;
            bool measured = err == null && (chinVerts > 0 || tip != null);
            float down = Vector3.Dot(c0 - c1, up), ahead = Vector3.Dot(c1 - c0, fwd);
            float gap0 = (c0 - noseAt).magnitude, gap1 = (c1 - noseAt).magnitude;
            bool noseOk = nose != null;
            req(measured && down > JawDownMin && (!noseOk || gap1 > gap0) && agree >= 0f && agree < 0.01f && restored && dirtyKept,
                label + ": " + method + " move " + (down * 1000f).ToString("F1") + " mm down (-dogRoot.up), " + (ahead * 1000f).ToString("F1") + " mm forward; gap to the nose skin ("
                + (noseOk ? noseVerts + " vertices" : "no nose bone") + ") " + (gap0 * 1000f).ToString("F1") + " -> " + (gap1 * 1000f).ToString("F1") + " mm"
                + "; written " + TPath(jaw, w.dogRoot) + (chin != jaw ? " + skin " + TPath(chin, w.dogRoot) + " (world deltas differ " + agree.ToString("F4") + " deg)" : " (one-tier)")
                + "; restored exactly " + restored + ", scene dirty " + dirty0 + " -> " + scene.isDirty + (err != null ? "; " + err : ""));
        }

        static bool SameQ(Quaternion a, Quaternion b) { return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; }

        // World centroid of the vertices weighted >= 0.5 to bone, as drawn now (BakeMesh(useScale) x the renderer's localToWorldMatrix).
        static Vector3 SkinCentroid(SkinnedMeshRenderer[] smrs, Transform bone, out int count)
        {
            Vector3 sum = Vector3.zero;
            count = 0;
            var baked = new Mesh();
            try
            {
                foreach (var smr in smrs)
                {
                    int bi = Array.IndexOf(smr.bones, bone);
                    if (bi < 0) continue;
                    var bw = smr.sharedMesh.boneWeights;
                    smr.BakeMesh(baked, true);
                    var vs = baked.vertices;
                    var m = smr.transform.localToWorldMatrix;
                    for (int v = 0; v < bw.Length && v < vs.Length; v++)
                    {
                        var b = bw[v];
                        float wt = (b.boneIndex0 == bi ? b.weight0 : 0f) + (b.boneIndex1 == bi ? b.weight1 : 0f) + (b.boneIndex2 == bi ? b.weight2 : 0f) + (b.boneIndex3 == bi ? b.weight3 : 0f);
                        if (wt < 0.5f) continue;
                        sum += m.MultiplyPoint3x4(vs[v]);
                        count++;
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
            return count > 0 ? sum / count : Vector3.zero;
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
