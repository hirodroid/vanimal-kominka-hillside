using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    public static partial class KD_CandyDogSetupEditor
    {
        [Serializable] private class MountPlan
        {
            public bool enabled;
            public int rider, support;
            public float supportLieStart, start, touch, leave, land, supportRise;
            public Vector3 takeoff, seat, landing, supportOrigin;
            public float ascentArc, descentArc, seatYaw, seatPitch, supportYaw;
            public string attachmentPath, hopSha256;
            public Vector3 attachmentPosition;
            public Quaternion attachmentRotation;
        }

        private const string MountBone = "hojo_born/Bone/Bone_end/PB3/inu/ラブラドール/Bone/Bone.001/Bone.002";
        private const string MountHopHash = "66cd2065fa936c8ddd14b0e0f2a207eee9ee533fe688c7116e8c051bcb8107b8";
        private const string PomeOriginalController = "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_KD_Dog_Pome.controller";
        private static Vector3 MountAttachmentPosition => new Vector3(.000000372598777f, -.053902965f, -.0549221821f);
        private static Quaternion MountAttachmentRotation => new Quaternion(.00000212379837f, -.7400526f, .672549546f, -.00000176208107f);

        // The exemption is deliberately tied to this measured scene, pair, path,
        // support pose and hop asset. It is not a general-purpose collision bypass.
        private static void ValidateMount(MotionPlan q, KD_CandyDogPerformance p)
        {
            var m = q.mount;
            if (m == null || !m.enabled) return;
            if (p.gameObject.scene.path != KD_PumpkinPreviewEditor.ScenePath || !p.previewSceneOnly
                || m.rider != 3 || m.support != 0 || p.dogs[3].dogRoot.name != "KD_Dog_Pome" || p.dogs[0].dogRoot.name != "KD_Dog_Lab"
                || m.attachmentPath != MountBone || m.hopSha256 != MountHopHash
                || p.dogs[0].dogRoot.Find(MountBone) == null)
                throw new InvalidOperationException("Mount requires the measured Pumpkin Lab/Pome pair and back bone.");
            CheckMountValue(m.supportLieStart, 101.6f); CheckMountValue(m.start, 104);
            CheckMountValue(m.touch, 105.4f); CheckMountValue(m.leave, 108); CheckMountValue(m.land, 109.4f);
            CheckMountValue(m.supportRise, 110.4f); CheckMountValue(m.ascentArc, .55f); CheckMountValue(m.descentArc, .5f);
            CheckMountValue(m.supportYaw, 180); CheckMountValue(m.seatYaw, 0); CheckMountValue(m.seatPitch, -9);
            CheckMountVector(m.takeoff, new Vector3(-2.464f, 0, 1.3f));
            CheckMountVector(m.seat, new Vector3(-2.464f, .366f, 2.25f));
            CheckMountVector(m.landing, new Vector3(-2.464f, 0, 3.5f));
            CheckMountVector(m.supportOrigin, new Vector3(-2.464f, 0, 2.2f));
            CheckMountVector(m.attachmentPosition, MountAttachmentPosition);
            if (!Finite(m.attachmentRotation.x) || !Finite(m.attachmentRotation.y) || !Finite(m.attachmentRotation.z)
                || !Finite(m.attachmentRotation.w) || Quaternion.Angle(m.attachmentRotation, MountAttachmentRotation) > .01f)
                throw new InvalidOperationException("Mount attachment rotation differs from the measured pose.");
            var controller = LoadMountController();
            string existing = AssetDatabase.GetAssetPath(p.dogs[3].dogAnimator.runtimeAnimatorController);
            if (existing != PomeOriginalController && existing != KD_PumpkinPreviewEditor.MountControllerPath)
                throw new InvalidOperationException("Unexpected rider controller; do not replace custom scene changes.");
            if (Vector3.Distance(p.dogs[3].dogRoot.lossyScale, Vector3.one) > .001f
                || Vector3.Distance(p.dogs[0].dogRoot.lossyScale, Vector3.one) > .001f)
                throw new InvalidOperationException("Mounted dog root scale differs from the geometry study.");
            foreach (float boundary in new[] { m.supportLieStart, m.start, m.touch, m.leave, m.land, m.supportRise })
                if (!q.motionTimes.Any(t => Mathf.Abs(t - boundary) < .001f)) throw new InvalidOperationException("Mount boundary is not a motion key.");
            for (int k = 0; k < q.motionTimes.Length; k++)
            {
                float t = q.motionTimes[k]; int r = k * 6 + m.rider, s = k * 6 + m.support;
                if (t >= m.supportLieStart && t <= m.supportRise + 1.2f)
                {
                    CheckMountVector(q.motionPositions[s], m.supportOrigin); CheckMountValue(q.motionYaws[s], m.supportYaw);
                    if (q.motionPoses[s] != (t < m.supportRise ? 3 : 0)) throw new InvalidOperationException("Support must lie through dismount, then finish rising in place.");
                }
                if (t >= m.start - 1.6f && t <= m.land + .4f)
                {
                    Vector3 expected = t < m.start ? m.takeoff : t > m.land ? m.landing : MountPosition(m, t);
                    CheckMountVector(q.motionPositions[r], expected); CheckMountValue(q.motionYaws[r], m.seatYaw);
                    if (q.motionPoses[r] != 0) throw new InvalidOperationException("Rider mount/settle may not receive a competing pose.");
                }
            }
            ValidateMountFloor(p, m);
        }

        private static void CheckMountValue(float actual, float expected)
        { if (!Finite(actual) || Mathf.Abs(actual - expected) > .0005f) throw new InvalidOperationException("Mount value differs from measured route: " + actual + " expected " + expected); }
        private static void CheckMountVector(Vector3 actual, Vector3 expected)
        { CheckMountValue(actual.x, expected.x); CheckMountValue(actual.y, expected.y); CheckMountValue(actual.z, expected.z); }

        private static Vector3 MountPosition(MountPlan m, float t)
        {
            if (t < m.touch)
            {
                float u = Mathf.InverseLerp(m.start, m.touch, t);
                var position = Vector3.Lerp(m.takeoff, m.seat, Mathf.SmoothStep(0, 1, u));
                position.y += m.ascentArc * Mathf.Sin(Mathf.PI * u); return position;
            }
            if (t < m.leave) return m.seat;
            float v = Mathf.InverseLerp(m.leave, m.land, t);
            var result = Vector3.Lerp(m.seat, m.landing, Mathf.SmoothStep(0, 1, v));
            result.y += m.descentArc * Mathf.Sin(Mathf.PI * v); return result;
        }

        private static bool MountPairSegment(MountPlan m, int a, int b, float from, float to)
        { return m != null && m.enabled && a == m.support && b == m.rider && from >= m.start && to <= m.land; }

        private static void ValidateMountFloor(KD_CandyDogPerformance p, MountPlan m)
        {
            // Include both abort endpoints explicitly, even when the general path
            // survey strides past their exact key. Dog colliders are never ground.
            foreach (var point in new[] { m.takeoff, m.landing, m.supportOrigin })
            {
                var world = p.director.transform.TransformPoint(point);
                var hits = Physics.RaycastAll(world + Vector3.up * 2, Vector3.down, 4,
                    p.dogs[m.rider].groundLayerMask, QueryTriggerInteraction.Ignore)
                    .Where(h => !p.dogs.Any(d => h.collider.transform.IsChildOf(d.dogRoot))).OrderBy(h => h.distance).ToArray();
                if (hits.Length == 0 || Mathf.Abs(hits[0].point.y - world.y) > .02f)
                    throw new InvalidOperationException("Mount support/takeoff/landing floor is missing or changed: " + world);
            }
        }

        private static AnimatorController LoadMountController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(KD_PumpkinPreviewEditor.MountControllerPath);
            var hop = AssetDatabase.LoadAssetAtPath<AnimationClip>(KD_PumpkinPreviewEditor.MountHopPath);
            if (controller == null || hop == null || MotionHash(KD_PumpkinPreviewEditor.MountHopPath) != MountHopHash
                || controller.layers.Length != 2 || controller.layers[1].name != KD_PumpkinPreviewEditor.MountLayerName
                || controller.layers[1].defaultWeight != 0 || controller.layers[1].blendingMode != AnimatorLayerBlendingMode.Override
                || AnimationUtility.GetCurveBindings(hop).Any(b => b.type == typeof(Transform) && string.IsNullOrEmpty(b.path)))
                throw new InvalidOperationException("Build and verify opt-in mount assets before wiring.");
            var layer = controller.layers[1]; var states = layer.stateMachine.states.Select(s => s.state).ToArray();
            var air = states.SingleOrDefault(s => s.name == "MountHop"); var hold = states.SingleOrDefault(s => s.name == "MountHold");
            if (states.Length != 2 || air == null || hold == null || air.motion != hop || hold.motion == null
                || !air.timeParameterActive || air.timeParameter != "PumpkinMountTime"
                || !hold.timeParameterActive || hold.timeParameter != "PumpkinMountHoldTime")
                throw new InvalidOperationException("Mount states/clocks differ from authored animation.");
            return controller;
        }

        private static void SetupMount(KD_CandyDogPerformance p, MountPlan m)
        {
            p.useMount = m != null && m.enabled;
            if (!p.useMount) return;
            p.mountRider = m.rider; p.mountSupport = m.support;
            p.mountAnchor = p.dogs[m.support].dogRoot.Find(m.attachmentPath);
            p.mountAttachmentPosition = m.attachmentPosition; p.mountAttachmentRotation = m.attachmentRotation;
            p.mountTakeoff = m.takeoff; p.mountSeat = m.seat; p.mountLanding = m.landing;
            p.mountSupportPosition = m.supportOrigin; p.mountSupportYaw = m.supportYaw; p.mountRiderYaw = m.seatYaw;
            p.mountStart = m.start; p.mountTouch = m.touch; p.mountLeave = m.leave; p.mountLand = m.land;
            p.mountAscentArc = m.ascentArc; p.mountDescentArc = m.descentArc; p.mountAnimatorLayer = 1;
            p.dogs[m.rider].dogAnimator.runtimeAnimatorController = LoadMountController();
            if (PrefabUtility.IsPartOfPrefabInstance(p.dogs[m.rider].dogAnimator))
                PrefabUtility.RecordPrefabInstancePropertyModifications(p.dogs[m.rider].dogAnimator);
            EditorUtility.SetDirty(p.dogs[m.rider].dogAnimator);
        }

        private static MountPlan MountFromRuntime(KD_CandyDogPerformance p)
        {
            if (!p.useMount) return null;
            return new MountPlan { enabled = true, rider = p.mountRider, support = p.mountSupport,
                supportLieStart = 101.6f, supportRise = 110.4f, start = p.mountStart, touch = p.mountTouch, leave = p.mountLeave, land = p.mountLand,
                takeoff = p.mountTakeoff, seat = p.mountSeat, landing = p.mountLanding, supportOrigin = p.mountSupportPosition,
                ascentArc = p.mountAscentArc, descentArc = p.mountDescentArc, seatYaw = p.mountRiderYaw, seatPitch = -9, supportYaw = p.mountSupportYaw,
                attachmentPath = MountBone, attachmentPosition = p.mountAttachmentPosition, attachmentRotation = p.mountAttachmentRotation, hopSha256 = MountHopHash };
        }

        private static void VerifyMountWiring(KD_CandyDogPerformance p)
        {
            if (!p.useMount) return;
            if (p.mountAnchor != p.dogs[p.mountSupport].dogRoot.Find(MountBone) || p.mountAnimatorLayer != 1
                || p.dogs[p.mountRider].dogAnimator.runtimeAnimatorController != LoadMountController())
                throw new InvalidOperationException("Mount backing bone or rider Animator is not wired.");
        }
    }
}
