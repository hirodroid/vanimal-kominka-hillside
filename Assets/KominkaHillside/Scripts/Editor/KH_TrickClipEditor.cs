using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    // IMP-DOG-054 wave 1-A: authors the two one-shot tricks of the called (sitting) dog for the six breeds:
    //   KH_Bang_<breed>.anim      (6.0 s) sit -> "shot" flinch -> fall to the dog's RIGHT through an authored side-lying key
    //                                     -> belly-up (KD_BellyUp_<breed>) held 3 s -> back through the side key -> sit
    //   KH_TrickJump_<breed>.anim (2.0 s) sit -> crouch -> KH_Jump_<breed> airborne posture with the pelvis (AnimationRoot) and the
    //                                     sibling tail (Bone.018) baked LIFTED -> touchdown / absorb (hip dip) -> sit
    // Both start and end exactly on KD_Sit_<breed> (t = 0 of that clip), so CallSit <-> CallBang / CallJump do not jump.
    //
    // How a frame is made (the same pattern as KD_CompanionMotionEditor / KH_FrisbeeJumpClipEditor, bones = the driver
    // skeleton inu/アーマチュア whose 30 transforms every KD_Sit / KD_BellyUp / KH_Jump clip keys):
    //   1. reference poses are read off a clone of the scene dog after SampleAnimation (KD_Sit, KD_BellyUp, the purchased idle,
    //      KH_Jump at its named keys), as local position / rotation of each bound transform;
    //   2. a frame = slerp / lerp of reference poses, plus world-space operations on the AnimationRoot and SiblingTail only
    //      (roll about the pelvis for the fall, lateral shift, lift for the jump) and small additive driver rotations (flinch);
    //   3. KH_DogRig.ApplyConstraints, then "seat": AnimationRoot and SiblingTail are moved by the same world delta so the lowest
    //      vertex of the baked body meshes sits at SeatY (KD_CompanionMotionEditor does the same at 0.003);
    //   4. a floor pass samples the finished clip every FloorPassStep s on the clone (SampleAnimation + ApplyConstraints +
    //      BakeMesh) and inserts a full key wherever the interpolation between keys sinks below the ground (or, on grounded
    //      frames, floats), up to FloorPassIterations times. 2b repeats that sampling as the check.
    // The network root (DogRoot, binding path "") is never animated: the called dog is frozen on its placement point.
    // Angles and timings are authoring values for these models, not measured canine motion (仮, PCVR で確認).
    public static partial class KH_TrickClipEditor
    {
        // ---------------------------------------------------------------- paths / names
        public const string ClipDir = "Assets/KominkaHillside/Animations";
        public const string Output = "Temp/kh-tricks";
        public const string EvidenceDir = "docs/plans/evidence/2026-10-13-tricks";
        public const string OneBreedPref = "KH_Tricks_Breed";
        public const string MouthAnchorName = "KD_FetchAnchor_Mouth";
        public const string BangName = "Bang", JumpName = "TrickJump";
        public static string ClipPathFor(string trick, string breed) { return ClipDir + "/KH_" + trick + "_" + breed + ".anim"; }

        // ---------------------------------------------------------------- shared tolerances
        public const float BangLength = 6.0f, JumpLength = 2.0f;
        public const float LengthTolerance = 0.01f;
        // First / last frame of a trick vs KD_Sit_<breed> at t = 0, per curve value (m for positions, quaternion component, blend weight).
        public const float EndpointTolerance = 1e-4f;
        // Size (Codex review, 2026-10-13): a property group (rotation xyzw / position xyz of one transform, or one blend shape) whose
        // values stay within this of its first key at every key is written as 2 keys (start, end) with the first key's value.
        // The binding stays (KD_Sit superset). Varying curves keep every key: there is no lossy key reduction.
        public const float ConstantCurveEpsilon = 1e-6f;
        public const float SeatY = 0.003f;                 // lowest body vertex of a grounded key (KD_CompanionMotionEditor's value)
        public const float GroundTolerance = 0.015f;       // 2b: lowest vertex >= -this at every sample (KD_CompanionMotionEditor.Verify's value)
        public const float FloorPassSink = 0.002f;         // build: insert a key where the sampled clip goes below SeatY - this
        public const float FloorPassFloat = 0.02f;         // build: ... or, on a grounded frame, above SeatY + this
        public const float GroundedMaxFloat = 0.04f;       // 2b: on grounded frames the lowest vertex stays <= max(SeatY, KD_Sit lowest) + this
        public const float FloorPassStep = 1f / 30f;   // build-time sampling (keys are ~0.1-0.15 s apart)
        public const float VerifyStep = 1f / 60f;
        public const int FloorPassIterations = 3;

        // ---------------------------------------------------------------- Bang (6.0 s)
        public const float BangFlinchPeak = 0.12f, BangFlinchEnd = 0.60f;       // "shot": head / neck / chest jerk back, decays into the fall
        public const float FlinchRoot = -3f, FlinchChest = -4f, FlinchNeck = -8f, FlinchHead = -15f; // deg about the dog's right axis (- = nose up)
        public const float BangFallStart = 0.30f, BangSideAt = 0.90f, BangBellyAt = 1.50f;   // fall 1.2 s (accelerating to the side key)
        public const float BangRiseStart = 4.50f, BangRiseSideAt = 5.10f, BangSitAt = 5.70f; // belly-up hold 3.0 s, rise 1.2 s
        // KD_CompanionMotionEditor.Shape (pose 1 = BellyUp) rolls the idle root by +170 deg about world forward. The trick rolls the
        // other way round to the same orientation (0 -> -90 side -> -190 == +170), so the dog goes over its RIGHT side.
        public const float BellyRollDeg = 170f;
        // Side-lying key: limbs half way from sit to belly-up, then pulled toward the standing (idle) legs = legs out to the side.
        public const float SideBellyBlend = 0.5f, SideIdleWeight = 0.4f;
        // Lateral shift toward the dog's right while it goes over (x sit pelvis height); stays during the hold, returns on the rise.
        public const float FallShiftScale = 0.5f;
        static readonly float[] BangKeyTimes =
        {
            0f, BangFlinchPeak, BangFallStart, 0.45f, 0.60f, 0.75f, BangSideAt, 1.05f, 1.20f, 1.35f, BangBellyAt,
            BangRiseStart, 4.65f, 4.80f, 4.95f, BangRiseSideAt, 5.25f, 5.40f, 5.55f, BangSitAt, BangLength,
        };

        // ---------------------------------------------------------------- Jump (2.0 s)
        public const float JumpCrouchAt = 0.30f, JumpTakeoffAt = 0.42f, JumpTouchdownAt = 1.22f, JumpAbsorbAt = 1.36f, JumpSitAt = 1.85f;
        public const float JumpCrouchBlend = 0.4f;   // crouch from sit = 40 % toward KH_Jump "crouch"
        public const float JumpAirRise = 0.3f, JumpAirDescend = 0.8f; // airborne phase split: takeoff->rise | rise->descend | descend->touchdown
        // Pelvis (AnimationRoot) and sibling tail are lifted by 4 * JumpLift * s * (1 - s) over the grounded airborne posture, so the
        // lowest body vertex clears the ground by SeatY + JumpLift at the apex (s = 0.5). 2b checks the apex clearance is in range.
        public const float JumpLift = 0.25f, JumpClearanceMin = 0.20f, JumpClearanceMax = 0.30f;
        static readonly float[] JumpKeyTimes =
        {
            0f, 0.15f, JumpCrouchAt, JumpTakeoffAt, 0.52f, 0.62f, 0.72f, 0.82f, 0.92f, 1.02f, 1.12f, JumpTouchdownAt,
            JumpAbsorbAt, 1.52f, 1.68f, JumpSitAt, JumpLength,
        };

        public static bool IsBang(string trick) { return trick == BangName; }
        public static float LengthOf(string trick) { return IsBang(trick) ? BangLength : JumpLength; }
        static readonly string[] Tricks = { BangName, JumpName };

        // ---------------------------------------------------------------- breeds
        public static KD_DogDef[] Defs { get { return KD_DogDefs.All; } }
        public static KD_DogDef DefFor(string breed)
        {
            foreach (var d in Defs) if (d.key == breed) return d;
            throw new InvalidOperationException("Unknown breed " + breed);
        }

        // 1a: the selected dog (KD_Dog_<breed> or anything under it), else EditorPrefs KH_Tricks_Breed (default Corgi).
        public static string OneBreed
        {
            get
            {
                for (var t = Selection.activeTransform; t != null; t = t.parent)
                    if (t.name.StartsWith("KD_Dog_", StringComparison.Ordinal) && Defs.Any(d => d.DogName == t.name)) return t.name.Substring(7);
                var b = EditorPrefs.GetString(OneBreedPref, "Corgi");
                return Defs.Any(d => d.key == b) ? b : "Corgi";
            }
            set { if (!Defs.Any(d => d.key == value)) throw new ArgumentException("Unknown breed " + value); EditorPrefs.SetString(OneBreedPref, value); }
        }

        // ---------------------------------------------------------------- menus
        [MenuItem("Tools/KominkaHillside/Tricks/1 Build Trick Clips")]
        public static void BuildAll() { BuildBreeds(Defs.Select(d => d.key).ToArray()); }

        [MenuItem("Tools/KominkaHillside/Tricks/1a Build Trick Clips (One Breed)")]
        public static void BuildOne() { BuildBreeds(new[] { OneBreed }); }

        // ---------------------------------------------------------------- pose engine
        class Pose
        {
            public Vector3[] p; public Quaternion[] q;
            public Pose(int n) { p = new Vector3[n]; q = new Quaternion[n]; }
        }

        // One breed's working clone and its reference poses.
        class Ctx : IDisposable
        {
            public KD_DogDef def;
            public GameObject dog;
            public KH_DogRig rig;
            public string[] paths;           // transform binding paths of KD_Sit_<breed> (the driver skeleton)
            public Transform[] T;
            public Transform root, tail;
            public EditorCurveBinding[] floatBindings; // KD_Sit's non-transform bindings (face blend shapes)
            public float[] sitFloats, bellyFloats;
            public EditorCurveBinding[] bindings;      // output order: per transform rot xyzw + pos xyz, then floatBindings
            public Pose sit, belly, unrolled, idle, side;
            public Pose jCrouch, jTakeoff, jRise, jDescend, jTouch, jAbsorb;
            public float rollSign, rollEnd, sitPelvisY, sitMinY; // sitMinY: KD_Sit's own lowest vertex (raw end frames are not re-seated)
            public Vector3 sitPelvis, right;
            public AnimationClip sitClip;
            public StringBuilder log = new StringBuilder();
            public void Dispose() { if (dog != null) Object.DestroyImmediate(dog); dog = null; }
        }

        static GameObject CloneSceneDog(KD_DogDef def, Scene scene)
        {
            var source = KD_SceneObjects.Find(def.DogName);
            if (source == null) throw new InvalidOperationException(def.DogName + " not found in the active scene (open Assets/Scenes/VRCDefaultWorldScene.unity)");
            var clone = Object.Instantiate(source); clone.name = def.DogName;
            SceneManager.MoveGameObjectToScene(clone, scene);
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clone.SetActive(true);
            return clone;
        }

        static Ctx Prepare(KD_DogDef def, Scene scene)
        {
            var c = new Ctx { def = def };
            try
            {
                c.dog = CloneSceneDog(def, scene);
                c.rig = KH_DogRig.Resolve(c.dog, MouthAnchorName);
                if (c.rig.srcSkelRoot == null || c.rig.head == null || c.rig.nose == null)
                    throw new InvalidOperationException(def.key + ": rig " + string.Join("; ", c.rig.problems));
                c.root = c.rig.AnimationRoot; c.tail = c.rig.SiblingTail;
                if (c.root == null) throw new InvalidOperationException(def.key + ": no AnimationRoot 'Bone' under the armature");

                c.sitClip = KD_CallPoseEditor.SitClip(def);
                var bellyClip = KD_CallPoseEditor.PoseClip(def, 4);
                var sitBindings = AnimationUtility.GetCurveBindings(c.sitClip);
                c.paths = sitBindings.Where(b => b.type == typeof(Transform)).Select(b => b.path).Distinct().ToArray();
                c.T = c.paths.Select(p => c.dog.transform.Find(p)).ToArray();
                for (int i = 0; i < c.T.Length; i++) if (c.T[i] == null) throw new InvalidOperationException(def.key + ": KD_Sit path not on the dog: " + c.paths[i]);
                if (Array.IndexOf(c.T, c.root) < 0) throw new InvalidOperationException(def.key + ": KD_Sit does not key the AnimationRoot");
                if (c.tail != null && Array.IndexOf(c.T, c.tail) < 0) throw new InvalidOperationException(def.key + ": KD_Sit does not key the sibling tail");
                c.floatBindings = sitBindings.Where(b => b.type != typeof(Transform)).ToArray();
                c.sitFloats = c.floatBindings.Select(b => AnimationUtility.GetEditorCurve(c.sitClip, b).Evaluate(0f)).ToArray();
                c.bellyFloats = c.floatBindings.Select((b, i) => { var cv = AnimationUtility.GetEditorCurve(bellyClip, b); return cv == null ? c.sitFloats[i] : cv.Evaluate(0f); }).ToArray();
                var list = new List<EditorCurveBinding>();
                foreach (var p in c.paths)
                {
                    for (int i = 0; i < 4; i++) list.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalRotation." + "xyzw"[i]));
                    for (int i = 0; i < 3; i++) list.Add(EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalPosition." + "xyz"[i]));
                }
                list.AddRange(c.floatBindings);
                c.bindings = list.ToArray();

                c.sit = Sample(c, c.sitClip, 0f);
                c.belly = Sample(c, bellyClip, 0f);

                // Idle (the purchased clip the belly-up was authored from) on a fresh clone: it may not key every bound transform.
                var idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(def.idle.guid));
                if (idleClip == null) throw new InvalidOperationException(def.key + ": idle clip missing (" + def.idle.label + ")");
                var fresh = CloneSceneDog(def, scene);
                try
                {
                    idleClip.SampleAnimation(fresh, 0f);
                    c.idle = new Pose(c.T.Length);
                    for (int i = 0; i < c.T.Length; i++) { var t = fresh.transform.Find(c.paths[i]); c.idle.p[i] = t.localPosition; c.idle.q[i] = t.localRotation; }
                }
                finally { Object.DestroyImmediate(fresh); }

                // Facing: every roll / pitch below assumes the dog at identity faces +Z (KD_CompanionMotionEditor rolls about Vector3.forward).
                SetPose(c, c.sit); KH_DogRig.ApplyConstraints(c.dog);
                var fwd = KH_FrisbeeJumpClipEditor.Forward(c.rig);
                if (Vector3.Dot(fwd, Vector3.forward) < 0.9f) throw new InvalidOperationException(def.key + ": sitting dog does not face +Z (trunk forward " + fwd.ToString("F3") + ")");
                c.right = Vector3.Cross(Vector3.up, Vector3.forward).normalized;
                c.sitPelvis = c.root.position; c.sitPelvisY = c.root.position.y; c.sitMinY = MinY(c);
                // Roll sign by test, not by handedness: the roll whose quarter turn brings the back (up) onto the dog's right.
                c.rollSign = Vector3.Dot(Quaternion.AngleAxis(-90f, Vector3.forward) * Vector3.up, c.right) > 0f ? -1f : 1f;
                c.rollEnd = c.rollSign < 0f ? BellyRollDeg - 360f : BellyRollDeg;

                // Belly-up with its roll taken out (about the pelvis): the belly-up limbs in the upright body frame.
                SetPose(c, c.belly);
                RotateRootTail(c, Quaternion.AngleAxis(-BellyRollDeg, Vector3.forward), c.root.position);
                c.unrolled = Read(c);
                var unrolledRoot = c.root.rotation;
                SetPose(c, c.idle);
                float residual = Quaternion.Angle(unrolledRoot, c.root.rotation);
                c.log.AppendLine(def.key + ": facing " + fwd.ToString("F3") + " right=" + c.right.ToString("F2") + " rollSign=" + c.rollSign + " rollEnd=" + c.rollEnd
                    + " sitPelvisY=" + c.sitPelvisY.ToString("F3") + " sitMinY=" + c.sitMinY.ToString("F4") + " unrolledBellyRoot-vs-idleRoot=" + residual.ToString("F1") + " deg" + (residual > 5f ? " (WARN: belly-up was not authored as a pure 170 deg roll of idle)" : ""));
                c.side = Blend(Blend(c.sit, c.unrolled, SideBellyBlend), c.idle, SideIdleWeight);

                // KH_Jump keys by name (the frisbee breed table owns their times).
                var jumpClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(KH_FrisbeeJumpClipEditor.ClipPathFor("Jump", def.key));
                if (jumpClip == null) throw new InvalidOperationException(def.key + ": missing " + KH_FrisbeeJumpClipEditor.ClipPathFor("Jump", def.key) + " (Frisbee/2)");
                var keys = KH_FrisbeeJumpClipEditor.Variants(def.key)[0].keys;
                float KeyTime(string n)
                {
                    foreach (var k in keys) if (k.name == n) return Mathf.Min(k.time, jumpClip.length);
                    throw new InvalidOperationException("KH_Jump key " + n + " missing");
                }
                c.jCrouch = Sample(c, jumpClip, KeyTime("crouch"));
                c.jTakeoff = Sample(c, jumpClip, KeyTime("takeoff"));
                c.jRise = Sample(c, jumpClip, KeyTime("rise"));
                c.jDescend = Sample(c, jumpClip, KeyTime("descend"));
                c.jTouch = Sample(c, jumpClip, KeyTime("touchdown"));
                c.jAbsorb = Sample(c, jumpClip, KeyTime("absorb"));
                SetPose(c, c.sit); KH_DogRig.ApplyConstraints(c.dog);
                return c;
            }
            catch { c.Dispose(); throw; }
        }

        static Pose Sample(Ctx c, AnimationClip clip, float time) { clip.SampleAnimation(c.dog, time); return Read(c); }

        static Pose Read(Ctx c)
        {
            var r = new Pose(c.T.Length);
            for (int i = 0; i < c.T.Length; i++) { r.p[i] = c.T[i].localPosition; r.q[i] = c.T[i].localRotation; }
            return r;
        }

        static void SetPose(Ctx c, Pose pose)
        {
            for (int i = 0; i < c.T.Length; i++) { c.T[i].localPosition = pose.p[i]; c.T[i].localRotation = pose.q[i]; }
        }

        static Pose Blend(Pose a, Pose b, float w)
        {
            if (w <= 0f) return a; if (w >= 1f) return b;
            var r = new Pose(a.p.Length);
            for (int i = 0; i < a.p.Length; i++) { r.p[i] = Vector3.Lerp(a.p[i], b.p[i], w); r.q[i] = Quaternion.Slerp(a.q[i], b.q[i], w); }
            return r;
        }

        // Whole-body rotation about a pivot, written to the root driver; the sibling tail root follows the same pivot.
        static void RotateRootTail(Ctx c, Quaternion q, Vector3 pivot)
        {
            c.root.position = pivot + q * (c.root.position - pivot); c.root.rotation = q * c.root.rotation;
            if (c.tail != null) { c.tail.position = pivot + q * (c.tail.position - pivot); c.tail.rotation = q * c.tail.rotation; }
        }

        static void Translate(Ctx c, Vector3 delta)
        {
            c.root.position += delta;
            if (c.tail != null) c.tail.position += delta;
        }

        static float MinY(Ctx c)
        {
            KH_DogRig.ApplyConstraints(c.dog);
            return KH_FrisbeeJumpClipEditor.BodyBounds(c.dog).min.y;
        }

        // Lowest baked vertex onto SeatY by moving the pelvis root and the sibling tail together (the whole skeleton translates).
        static void Seat(Ctx c)
        {
            Translate(c, Vector3.up * (SeatY - MinY(c)));
            KH_DogRig.ApplyConstraints(c.dog);
        }

        // Top of the ground band for grounded frames: SeatY, or KD_Sit's own lowest vertex if that sits higher (raw end frames).
        static float GroundBase(Ctx c) { return Mathf.Max(SeatY, c.sitMinY); }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        // ---------------------------------------------------------------- Bang frames
        // u: 0 sit, 1 side-lying (right side down), 2 belly-up. The fall accelerates into the side key (x^2) and rolls on to the back;
        // the rise retraces it (back -> side -> sit) with eased phases.
        static float BangU(float t)
        {
            if (t <= BangFallStart) return 0f;
            if (t < BangSideAt) { float x = (t - BangFallStart) / (BangSideAt - BangFallStart); return x * x; }
            if (t < BangBellyAt) return 1f + Smooth((t - BangSideAt) / (BangBellyAt - BangSideAt));
            if (t <= BangRiseStart) return 2f;
            if (t < BangRiseSideAt) return 2f - Smooth((t - BangRiseStart) / (BangRiseSideAt - BangRiseStart));
            if (t < BangSitAt) return 1f - Smooth((t - BangRiseSideAt) / (BangSitAt - BangRiseSideAt));
            return 0f;
        }

        static float FlinchAmp(float t)
        {
            if (t <= 0f || t >= BangFlinchEnd) return 0f;
            if (t < BangFlinchPeak) return Smooth(t / BangFlinchPeak);
            return 1f - Smooth((t - BangFlinchPeak) / (BangFlinchEnd - BangFlinchPeak));
        }

        static void Flinch(Ctx c, float a)
        {
            RotateRootTail(c, Quaternion.AngleAxis(FlinchRoot * a, c.right), c.root.position);
            foreach (var pair in new[] { ("Bone.002", FlinchChest), ("Bone.003", FlinchNeck), ("Bone.004", FlinchHead) })
            {
                var d = c.rig.Driver(pair.Item1);
                d.rotation = Quaternion.AngleAxis(pair.Item2 * a, c.right) * d.rotation;
            }
        }

        // Poses the clone for Bang at time t; returns the float-binding values. raw = the frame is KD_Sit itself (no seat).
        static float[] BangFrame(Ctx c, float t, out bool raw)
        {
            float u = BangU(t), a = FlinchAmp(t);
            raw = u <= 0f && a <= 0f;
            if (raw) { SetPose(c, c.sit); KH_DogRig.ApplyConstraints(c.dog); return (float[])c.sitFloats.Clone(); }
            SetPose(c, u <= 1f ? Blend(c.sit, c.side, u) : Blend(c.side, c.unrolled, u - 1f));
            if (a > 0f) Flinch(c, a);
            float theta = u <= 1f ? c.rollSign * 90f * u : c.rollSign * 90f + (c.rollEnd - c.rollSign * 90f) * (u - 1f);
            if (Mathf.Abs(theta) > 1e-4f) RotateRootTail(c, Quaternion.AngleAxis(theta, Vector3.forward), c.root.position);
            Translate(c, c.right * (FallShiftScale * c.sitPelvisY * Mathf.Min(u, 1f)));
            Seat(c);
            float w = Mathf.Min(u, 1f);
            return c.sitFloats.Select((s, i) => Mathf.Lerp(s, c.bellyFloats[i], w)).ToArray();
        }

        // ---------------------------------------------------------------- Jump frames
        static bool JumpAirborne(float t) { return t > JumpTakeoffAt && t < JumpTouchdownAt; }

        static float[] JumpFrame(Ctx c, float t, out bool raw)
        {
            raw = t <= 0f || t >= JumpSitAt;
            if (raw) { SetPose(c, c.sit); KH_DogRig.ApplyConstraints(c.dog); return (float[])c.sitFloats.Clone(); }
            var crouch = Blend(c.sit, c.jCrouch, JumpCrouchBlend);
            float lift = 0f;
            if (t < JumpCrouchAt) SetPose(c, Blend(c.sit, crouch, Smooth(t / JumpCrouchAt)));
            else if (t <= JumpTakeoffAt) SetPose(c, Blend(crouch, c.jTakeoff, Smooth((t - JumpCrouchAt) / (JumpTakeoffAt - JumpCrouchAt))));
            else if (t < JumpTouchdownAt)
            {
                float s = (t - JumpTakeoffAt) / (JumpTouchdownAt - JumpTakeoffAt);
                if (s < JumpAirRise) SetPose(c, Blend(c.jTakeoff, c.jRise, Smooth(s / JumpAirRise)));
                else if (s < JumpAirDescend) SetPose(c, Blend(c.jRise, c.jDescend, Smooth((s - JumpAirRise) / (JumpAirDescend - JumpAirRise))));
                else SetPose(c, Blend(c.jDescend, c.jTouch, Smooth((s - JumpAirDescend) / (1f - JumpAirDescend))));
                lift = 4f * JumpLift * s * (1f - s);
            }
            else if (t < JumpAbsorbAt) SetPose(c, Blend(c.jTouch, c.jAbsorb, Smooth((t - JumpTouchdownAt) / (JumpAbsorbAt - JumpTouchdownAt))));
            else SetPose(c, Blend(c.jAbsorb, c.sit, Smooth((t - JumpAbsorbAt) / (JumpSitAt - JumpAbsorbAt))));
            Seat(c);
            if (lift > 0f) { Translate(c, Vector3.up * lift); KH_DogRig.ApplyConstraints(c.dog); }
            return (float[])c.sitFloats.Clone();
        }

        static float[] Frame(Ctx c, string trick, float t, out bool raw)
        {
            return IsBang(trick) ? BangFrame(c, t, out raw) : JumpFrame(c, t, out raw);
        }

        static bool Grounded(string trick, float t) { return IsBang(trick) || !JumpAirborne(t); }

        // Values in c.bindings order, read off the posed clone (raw frames are KD_Sit's own values, so first / last keys are exact).
        static float[] Values(Ctx c, float[] floats, bool raw)
        {
            var pose = raw ? c.sit : Read(c);
            var v = new float[c.bindings.Length]; int j = 0;
            for (int i = 0; i < c.T.Length; i++)
            {
                var q = pose.q[i]; var p = pose.p[i];
                v[j++] = q.x; v[j++] = q.y; v[j++] = q.z; v[j++] = q.w; v[j++] = p.x; v[j++] = p.y; v[j++] = p.z;
            }
            for (int i = 0; i < floats.Length; i++) v[j++] = floats[i];
            return v;
        }

        // ---------------------------------------------------------------- clip writing
        static void WriteCurves(Ctx c, AnimationClip clip, List<float> times, List<float[]> values)
        {
            int n = times.Count;
            // Quaternion sign continuity from the first (raw KD_Sit) key forward.
            for (int i = 0; i < c.T.Length; i++)
                for (int k = 1; k < n; k++)
                {
                    int o = i * 7; var a = values[k - 1]; var b = values[k];
                    float dot = a[o] * b[o] + a[o + 1] * b[o + 1] + a[o + 2] * b[o + 2] + a[o + 3] * b[o + 3];
                    if (dot < 0f) for (int m = 0; m < 4; m++) b[o + m] = -b[o + m];
                }
            clip.ClearCurves();
            // Group = the 4 rotation or 3 position components of one transform (Unity stores them as one quaternion / vector curve,
            // so a group is reduced only when all its components are constant), or one float binding.
            int transformCurves = c.T.Length * 7;
            var constant = new bool[c.bindings.Length];
            for (int g = 0; g < c.bindings.Length;)
            {
                int size = g < transformCurves ? ((g % 7) == 0 ? 4 : 3) : 1;
                bool same = true;
                for (int j = g; j < g + size && same; j++)
                    for (int k = 1; k < n && same; k++) same = Mathf.Abs(values[k][j] - values[0][j]) <= ConstantCurveEpsilon;
                for (int j = g; j < g + size; j++) constant[j] = same;
                g += size;
            }
            for (int j = 0; j < c.bindings.Length; j++)
            {
                if (constant[j])
                {
                    float v0 = values[0][j];
                    AnimationUtility.SetEditorCurve(clip, c.bindings[j], new AnimationCurve(new Keyframe(times[0], v0, 0f, 0f), new Keyframe(times[n - 1], v0, 0f, 0f)));
                    continue;
                }
                var keys = new Keyframe[n];
                for (int k = 0; k < n; k++)
                {
                    float v = values[k][j], slope = 0f;
                    if (k > 0 && k < n - 1)
                    {
                        float prev = values[k - 1][j], next = values[k + 1][j];
                        // Flat at holds and turning points (no overshoot: a held pose stays held, a seated key is not pushed below).
                        bool flat = Mathf.Abs(v - prev) < 1e-7f || Mathf.Abs(next - v) < 1e-7f || (v - prev) * (next - v) <= 0f;
                        if (!flat) slope = (next - prev) / (times[k + 1] - times[k - 1]);
                    }
                    keys[k] = new Keyframe(times[k], v, slope, slope);
                }
                AnimationUtility.SetEditorCurve(clip, c.bindings[j], new AnimationCurve(keys));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false; settings.stopTime = times[n - 1];
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        // Lowest vertex of the clip sampled at t (posing the clone).
        static float SampleMinY(Ctx c, AnimationClip clip, float t) { clip.SampleAnimation(c.dog, t); return MinY(c); }

        static AnimationClip BuildTrick(Ctx c, string trick, StringBuilder log)
        {
            string breed = c.def.key; float length = LengthOf(trick);
            var times = new List<float>(IsBang(trick) ? BangKeyTimes : JumpKeyTimes);
            var values = new List<float[]>();
            foreach (var t in times) { bool raw; var f = Frame(c, trick, t, out raw); values.Add(Values(c, f, raw)); }
            var clip = new AnimationClip { name = "KH_" + trick + "_" + breed, frameRate = 30, legacy = false };
            WriteCurves(c, clip, times, values);
            int inserted = 0;
            for (int pass = 0; pass < FloorPassIterations; pass++)
            {
                // Worst violation per key interval.
                var add = new List<float>();
                for (int k = 0; k + 1 < times.Count; k++)
                {
                    float worst = 0f, at = -1f;
                    for (float t = times[k] + FloorPassStep; t < times[k + 1] - FloorPassStep * 0.5f; t += FloorPassStep)
                    {
                        float y = SampleMinY(c, clip, t);
                        float bad = Mathf.Max(SeatY - FloorPassSink - y, Grounded(trick, t) ? y - (GroundBase(c) + FloorPassFloat) : 0f);
                        if (bad > worst) { worst = bad; at = t; }
                    }
                    if (at >= 0f) add.Add(at);
                }
                if (add.Count == 0) break;
                add.RemoveAll(t => times.Any(x => Mathf.Abs(x - t) < 1e-3f)); // a violation the pose function cannot fix is only logged
                if (add.Count == 0) { log.AppendLine("  floor pass " + (pass + 1) + ": remaining violations sit on existing keys (see the analysis line)"); break; }
                foreach (var t in add)
                {
                    bool raw; var f = Frame(c, trick, t, out raw);
                    int index = times.FindIndex(x => x > t);
                    times.Insert(index, t); values.Insert(index, Values(c, f, raw));
                    inserted++;
                }
                WriteCurves(c, clip, times, values);
                log.AppendLine("  floor pass " + (pass + 1) + ": +" + add.Count + " keys at " + string.Join(", ", add.Select(x => x.ToString("F3"))));
            }
            if (Mathf.Abs(clip.length - length) > LengthTolerance) throw new InvalidOperationException(clip.name + " length " + clip.length + " != " + length);
            log.AppendLine("  " + clip.name + ": keys=" + times.Count + " (inserted " + inserted + ") curves=" + c.bindings.Length + " length=" + clip.length.ToString("F3"));
            return clip;
        }

        // ---------------------------------------------------------------- sampling report (Build log and 2b)
        public struct Analysis
        {
            public float sitMinY, minY, minYAt, groundedMaxY, groundedMaxAt, apexClearance, apexAt, pelvisRise, pelvisDriftXZ, pawMin, trunkMin;
        }

        static Analysis Analyze(Ctx c, AnimationClip clip, string trick)
        {
            var r = new Analysis { sitMinY = c.sitMinY, minY = float.MaxValue, groundedMaxY = float.MinValue, apexClearance = float.MinValue, pawMin = float.MaxValue, trunkMin = float.MaxValue };
            float length = clip.length;
            int steps = Mathf.CeilToInt(length / VerifyStep);
            float pelvisTop = float.MinValue;
            for (int i = 0; i <= steps; i++)
            {
                float t = Mathf.Min(length, i * VerifyStep);
                float y = SampleMinY(c, clip, t);
                if (y < r.minY) { r.minY = y; r.minYAt = t; }
                if (Grounded(trick, t)) { if (y > r.groundedMaxY) { r.groundedMaxY = y; r.groundedMaxAt = t; } }
                else if (y > r.apexClearance) { r.apexClearance = y; r.apexAt = t; }
                foreach (var n in new[] { "Bone.014_L", "Bone.014_R", "Bone.016_L", "Bone.016_R" }) { var b = c.rig.Bone(n); if (b != null) r.pawMin = Mathf.Min(r.pawMin, b.position.y); }
                foreach (var n in new[] { "Bone", "Bone.001", "Bone.002", "Bone.003" }) { var b = c.rig.Bone(n); if (b != null) r.trunkMin = Mathf.Min(r.trunkMin, b.position.y); }
                pelvisTop = Mathf.Max(pelvisTop, c.root.position.y);
                var d = c.root.position - c.sitPelvis; d.y = 0f;
                r.pelvisDriftXZ = Mathf.Max(r.pelvisDriftXZ, d.magnitude);
            }
            r.pelvisRise = pelvisTop - c.sitPelvisY;
            return r;
        }

        // Back direction of the body at time t: from the mean of the four paw bones to the chest bone (Bone.002), mesh side.
        // Upright sit: ~+Y. After the bang's fall to the right it points to the dog's right (+X), belly-up ~-Y.
        public static Vector3 DorsalAt(AnimationClip clip, GameObject dog, KH_DogRig rig, float t)
        {
            clip.SampleAnimation(dog, t); KH_DogRig.ApplyConstraints(dog);
            var paws = Vector3.zero; int n = 0;
            foreach (var name in new[] { "Bone.014_L", "Bone.014_R", "Bone.016_L", "Bone.016_R" }) { var b = rig.Bone(name); if (b != null) { paws += b.position; n++; } }
            var chest = rig.Bone("Bone.002");
            if (n == 0 || chest == null) return Vector3.zero;
            return (chest.position - paws / n).normalized;
        }

        static string Describe(Analysis a, string trick)
        {
            var s = "sitMinY=" + a.sitMinY.ToString("F4") + " minY=" + a.minY.ToString("F4") + "@" + a.minYAt.ToString("F2") + " groundedMaxY=" + a.groundedMaxY.ToString("F4") + "@" + a.groundedMaxAt.ToString("F2")
                + " pawBoneMin=" + a.pawMin.ToString("F3") + " trunkBoneMin=" + a.trunkMin.ToString("F3") + " pelvisDriftXZ=" + a.pelvisDriftXZ.ToString("F3");
            if (!IsBang(trick)) s += " apexClearance=" + a.apexClearance.ToString("F3") + "@" + a.apexAt.ToString("F2") + " pelvisRiseOverSit=" + a.pelvisRise.ToString("F3");
            return s;
        }

        // ---------------------------------------------------------------- build
        public static void BuildBreeds(string[] breeds)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            Directory.CreateDirectory(ClipDir); Directory.CreateDirectory(Output);
            var log = new StringBuilder(); var built = new List<string>();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var breed in breeds)
                {
                    var def = DefFor(breed);
                    using (var c = Prepare(def, scene))
                    {
                        log.Append(c.log);
                        foreach (var trick in Tricks)
                        {
                            var clip = BuildTrick(c, trick, log);
                            try
                            {
                                log.AppendLine("  " + clip.name + " " + Describe(Analyze(c, clip, trick), trick));
                                string path = ClipPathFor(trick, breed);
                                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                                if (existing == null) { AssetDatabase.CreateAsset(clip, path); clip = null; }
                                else { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); }
                                built.Add(path);
                            }
                            finally { if (clip != null) Object.DestroyImmediate(clip); }
                        }
                    }
                }
                AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            File.WriteAllText(Output + "/build.txt", log.ToString());
            Debug.Log("[KH Tricks] built " + built.Count + " clips: " + string.Join(", ", built) + "\n" + log);
        }
    }
}
