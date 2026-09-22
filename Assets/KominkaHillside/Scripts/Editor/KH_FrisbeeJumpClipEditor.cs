using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    // IMP-DOG-005: authors the four-stage jump-catch clip for one dog from its idle pose, the way
    // KominkaDogRun's KD_CompanionMotionEditor authors Curl/BellyUp/Sit. The clip carries pose only
    // (legs, trunk pitch, neck, jaw, and the pelvis sink on ground frames); KH_FrisbeeCatch moves the
    // root along the arc at runtime, so no root motion is baked. Angles are authoring values for this
    // model, not measured canine joint angles (vrc-animator-reactions section 9).
    // IMP-DOG-008 wave 2: one generator, several clips. A ClipVariant is a name + key table + durations +
    // the Animator Bool that plays it; Build writes every variant (KH_Jump_Lab, KH_Twist_Lab, ...) and
    // Render sheets every variant. The Jump variant is byte-for-byte the table that shipped in stage 2.
    public static class KH_FrisbeeJumpClipEditor
    {
        public const string SourceScene = "Assets/KominkaHillside/Imported/KH_DogPortSource.unity";
        public const string ClipDir = "Assets/KominkaHillside/Animations";
        public const string DataDir = "Assets/KominkaHillside/Data/Frisbee";
        public const string Output = "Temp/KH_Frisbee";
        public const string MouthAnchorName = "KD_FetchAnchor_Mouth";
        public const string Breed = "Lab";
        // Asset names are KH_{variant}_{Breed}; ClipPath / FactsPath stay the Jump variant for every caller.
        public static string ClipPathFor(string name) { return ClipDir + "/KH_" + name + "_" + Breed + ".anim"; }
        public static string FactsPathFor(string name) { return DataDir + "/KH_" + name + "_" + Breed + ".json"; }
        public static string ClipPath { get { return ClipPathFor("Jump"); } }
        public static string FactsPath { get { return FactsPathFor("Jump"); } }
        public static string ControllerPath { get { return "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_KD_Dog_" + Breed + ".controller"; } }

        // Jump clip timing (seconds). Rise == land so the symmetric arc touches down on the touchdown key.
        // Every variant carries its own copy in ClipVariant.crouch/rise/recover; CatchAt/TouchdownAt/EndAt
        // below stay the Jump numbers (KH_FrisbeePrototypeEditor.Verify reads them).
        public const float Crouch = 0.25f, Rise = 0.35f, Recover = 0.40f;
        public const float CatchAt = Crouch + Rise, TouchdownAt = Crouch + Rise + Rise, EndAt = Crouch + Rise + Rise + Recover;

        // One key of the clip: time and additive angles (deg) about the dog's pitch axis (+ = nose/limb down-back).
        public struct Key
        {
            public string name; public float time;
            public float hip, knee, hock, shoulder, elbow, wrist, root, lumbar, chest, neck, head, jaw;
            public float twist;  // whole-body yaw (deg) about world up, + = the dog turns to its own right (clockwise from above)
            public bool ground; // feet on the floor: sink/raise the pelvis so the lowest point touches y=0
            public bool plant;  // all four paws down: pitch the body about the fore paws until the hind paws touch
        }

        static Key K(string name, float time, float hip, float knee, float hock, float shoulder, float elbow, float wrist,
            float root, float lumbar, float chest, float neck, float head, float jaw, bool ground)
        {
            return new Key { name = name, time = time, hip = hip, knee = knee, hock = hock, shoulder = shoulder, elbow = elbow, wrist = wrist,
                root = root, lumbar = lumbar, chest = chest, neck = neck, head = head, jaw = jaw, ground = ground };
        }

        static Key Plant(Key k) { k.plant = true; return k; }

        // Yaw for one key. Kept out of K() so the jump table below stays exactly the table of stage 2.
        static Key Twist(Key k, float deg) { k.twist = deg; return k; }

        static Key Scale(Key k, float s)
        {
            k.hip *= s; k.knee *= s; k.hock *= s; k.shoulder *= s; k.elbow *= s; k.wrist *= s; k.root *= s; k.lumbar *= s; k.chest *= s; k.neck *= s; k.head *= s; k.jaw *= s;
            return k;
        }

        static Key[] Keys()
        {
            var crouch = K("crouch", 0.15f, -16, 26, -20, 8, 22, -14, 0, 6, 2, -14, -8, 0, true); crouch.plant = true;
            var catchK = K("catch", CatchAt, 18, 4, 8, 12, 70, -30, -8, -4, -2, -20, -14, 34, false);
            var bite = catchK; bite.name = "bite"; bite.time = CatchAt + 0.08f; bite.jaw = 0; bite.head = -8;
            var start = Scale(crouch, 0.4f); start.name = "start"; start.time = 0f;
            return new[]
            {
                start, crouch,
                K("takeoff", Crouch, 35, -20, 25, -15, 50, -20, 0, -14, -8, -8, -4, 6, true),
                K("rise", Crouch + 0.17f, 30, -10, 15, 5, 70, -30, -12, -6, -4, -14, -10, 22, false),
                catchK, bite,
                K("descend", TouchdownAt - 0.13f, -10, 20, -12, -30, 20, -10, 4, 5, 3, 2, -8, 0, false),
                K("touchdown", TouchdownAt, -24, 32, -18, -24, 6, -4, 6, 7, 3, 2, -8, 0, true),
                Plant(K("absorb", TouchdownAt + 0.15f, -12, 20, -16, 6, 26, -16, 0, 5, 2, -4, -4, 0, true)),
                K("recover", EndAt, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, true),
            };
        }

        // IMP-DOG-008 v4 "twist": the same ten poses as the jump, only yawed. The dog leaves along its run,
        // turns 90 deg in the air to bite the disc coming across it, and unwinds so it lands facing the run
        // again. Ground keys stay at 0 so the feet land square; the catch mouth offset gains a lateral (x)
        // component, which is what KH_FrisbeeCatch already places with right * mouthOffsetCatch.x.
        static readonly string[] TwistOrder = { "start", "crouch", "takeoff", "rise", "catch", "bite", "descend", "touchdown", "absorb", "recover" };
        static readonly float[] TwistDeg = { 0f, 0f, 0f, 40f, 90f, 90f, 45f, 0f, 0f, 0f };

        static Key[] TwistKeys()
        {
            var keys = Keys(); // same durations as the jump, so the jump times carry over unchanged
            for (int i = 0; i < keys.Length; i++)
            {
                int at = Array.IndexOf(TwistOrder, keys[i].name);
                if (at < 0) throw new InvalidOperationException("No twist authored for key " + keys[i].name);
                keys[i] = Twist(keys[i], TwistDeg[at]);
            }
            return keys;
        }

        // One clip of the family: which keys, how long each phase lasts, and the Animator Bool / State that plays it.
        public struct ClipVariant
        {
            public string name;      // asset name part: KH_{name}_{Breed}.anim / .json
            public Key[] keys;
            public float crouch, rise, recover;
            public string boolParam; // Animator Bool and State name
            public float CatchAt { get { return crouch + rise; } }
            public float TouchdownAt { get { return crouch + rise + rise; } }
            public float EndAt { get { return crouch + rise + rise + recover; } }
            public string ClipPath { get { return ClipPathFor(name); } }
            public string FactsPath { get { return FactsPathFor(name); } }
        }

        public static ClipVariant[] Variants()
        {
            return new[]
            {
                new ClipVariant { name = "Jump", keys = Keys(), crouch = Crouch, rise = Rise, recover = Recover, boolParam = "Jump" },
                new ClipVariant { name = "Twist", keys = TwistKeys(), crouch = Crouch, rise = Rise, recover = Recover, boolParam = "Twist" },
            };
        }

        // ---------------------------------------------------------------- source dog
        // The port source scene is opened additively only if it is not already open, and closed only if it was
        // opened here (review M1: never close a scene someone else has open, dirty or not).
        public static GameObject CloneSourceDog(Scene into)
        {
            var source = SceneManager.GetSceneByPath(SourceScene);
            bool openedHere = !(source.IsValid() && source.isLoaded);
            if (openedHere) source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
            try
            {
                Transform found = null;
                foreach (var root in source.GetRootGameObjects())
                {
                    found = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "KD_Dog_" + Breed);
                    if (found != null) break;
                }
                if (found == null) throw new InvalidOperationException("KD_Dog_" + Breed + " not found in " + SourceScene);
                var clone = Object.Instantiate(found.gameObject);
                clone.name = "KD_Dog_" + Breed;
                SceneManager.MoveGameObjectToScene(clone, into);
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clone.SetActive(true);
                MakeBodyRigid(clone);
                return clone;
            }
            finally { if (openedHere) EditorSceneManager.CloseScene(source, true); }
        }

        // The purchased dog hangs its whole visible body (inu: both skeletons and every mesh) off a
        // ParentConstraint fed by the asset's follow gimmick (追従GIMMICK -> VRC constraint chain), not off the
        // dog root's hierarchy. At rest the two agree exactly, but the chain trails the root with a time constant
        // of seconds: measured in Play (2026-09-21), after the dog turned 32 degrees the body still sat 30 degrees
        // behind it half a second later, and during a jump it trailed 0.3-0.5 m below/behind the root. Every pose
        // fact this generator writes - and the whole catch plan, which places the root so that
        // root + rotation * mouthOffsetCatch lands on the disc - assumes the body is rigidly attached to the root.
        // So the body is made rigid: the constraint is switched off (not destroyed, so the gimmick can be restored),
        // and the transform keeps its hierarchy values, which is where the constraint was holding it anyway.
        // Verified: with this off, the runtime mouth follows the authored clip to ~2 cm through the whole jump;
        // with it on, the twist catch missed by 1.3 m (IMP-DOG-008 wave 2 follow-up).
        // Idempotent: a second call finds nothing left to switch off and returns 0.
        //
        // Codex D2: scoped to ONE known constraint. "Every ParentConstraint under the dog" would also switch off
        // anything a future prop, accessory or rig fix adds, silently and with no way to tell from the scene which
        // of them this tool turned off. The one constraint the follow gimmick drives is identified by all three of
        // its facts - the object name, its path under the dog, and a source that lives under 追従GIMMICK - and
        // anything else is logged and left alone.
        public const string BodyFollowPath = "hojo_born/Bone/Bone_end/PB3/inu";
        public const string BodyFollowObject = "inu";
        public const string BodyFollowSourceRoot = "追従GIMMICK";

        // The one ParentConstraint the purchased follow gimmick drives, or null. Shared by MakeBodyRigid and by
        // both Verifies, so "which constraint" is decided in exactly one place.
        public static UnityEngine.Animations.ParentConstraint FindBodyFollow(GameObject dog)
        {
            foreach (var pc in dog.GetComponentsInChildren<UnityEngine.Animations.ParentConstraint>(true))
                if (IsBodyFollow(dog, pc)) return pc;
            return null;
        }

        static bool IsBodyFollow(GameObject dog, UnityEngine.Animations.ParentConstraint pc)
        {
            if (pc.transform.name != BodyFollowObject) return false;
            if (AnimationUtility.CalculateTransformPath(pc.transform, dog.transform) != BodyFollowPath) return false;
            for (int i = 0; i < pc.sourceCount; i++)
            {
                var src = pc.GetSource(i).sourceTransform;
                for (var a = src; a != null; a = a.parent)
                    if (a.name == BodyFollowSourceRoot) return true;
            }
            return false;
        }

        public static int MakeBodyRigid(GameObject dog)
        {
            int n = 0;
            foreach (var pc in dog.GetComponentsInChildren<UnityEngine.Animations.ParentConstraint>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(pc.transform, dog.transform);
                string src = pc.sourceCount > 0 && pc.GetSource(0).sourceTransform != null
                    ? AnimationUtility.CalculateTransformPath(pc.GetSource(0).sourceTransform, dog.transform) : "-";
                if (!IsBodyFollow(dog, pc))
                {
                    // Not ours: say so out loud rather than switching off something nobody asked about.
                    Debug.Log("[KH Frisbee] body-follow skipped (not the known follow constraint): " + dog.name + "/" + path
                        + " ParentConstraint (source " + src + ")");
                    continue;
                }
                if (!pc.constraintActive && !pc.enabled) continue;
                Debug.Log("[KH Frisbee] body-follow off: " + dog.name + "/" + path + " ParentConstraint (source " + src + ")");
                pc.constraintActive = false; pc.enabled = false; n++;
                EditorUtility.SetDirty(pc);
            }
            return n;
        }

        // Verify helper for both scene builders: true when the follow gimmick's own constraint is switched off.
        // IsBodyRigid(dog) == false is the state that made the twist catch miss by 1.3 m. A dog with no such
        // constraint at all is NOT rigid: it means the path above no longer matches the purchased rig.
        public static bool IsBodyRigid(GameObject dog)
        {
            var pc = FindBodyFollow(dog);
            return pc != null && !pc.constraintActive && !pc.enabled;
        }

        // Codex D2: the other half of the scope. Every ParentConstraint under the dog that is NOT the follow
        // gimmick's must still be running, so Verify can prove the change stayed where it was aimed.
        public static bool OtherConstraintsUntouched(GameObject dog)
        {
            foreach (var pc in dog.GetComponentsInChildren<UnityEngine.Animations.ParentConstraint>(true))
                if (!IsBodyFollow(dog, pc) && !(pc.constraintActive && pc.enabled)) return false;
            return true;
        }

        // Existing scenes: 4 Build Scene makes the dogs it wires rigid itself (both the prototype builder and the
        // walking builder call MakeBodyRigid), so this menu is only for a scene whose frisbee dog predates that -
        // or for putting it back after someone re-imported the purchased dog. Nothing has to be run after a Build.
        // Codex D2: only dogs that actually have a KH_FrisbeeCatch wired to them. Sweeping every KD_Dog_* would
        // stiffen the other five, whose bodies are meant to keep the purchased follow gimmick.
        [MenuItem("Tools/KominkaHillside/Frisbee/6 Make Dog Bodies Rigid")]
        public static void MakeDogBodiesRigid()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            int dogs = 0, off = 0, skipped = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                bool touched = false;
                var seen = new List<GameObject>();
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var c in root.GetComponentsInChildren<KH_FrisbeeCatch>(true))
                    {
                        if (c.dogRoot == null) { skipped++; continue; }
                        var go = c.dogRoot.gameObject;
                        if (seen.Contains(go)) continue;   // two catchers on one dog: switch it off once
                        seen.Add(go);
                        dogs++;
                        int n = MakeBodyRigid(go);
                        off += n; touched |= n > 0;
                    }
                if (touched) EditorSceneManager.MarkSceneDirty(scene);
            }
            Debug.Log("[KH Frisbee] body-follow constraints switched off: " + off + " on " + dogs + " frisbee dog(s)"
                + (skipped > 0 ? ", " + skipped + " catcher(s) with no dogRoot wired" : "")
                + ". Dogs without a KH_FrisbeeCatch are untouched. Save the scene to keep it.");
        }

        public static AnimationClip IdleClip()
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ac == null) throw new InvalidOperationException("Missing " + ControllerPath);
            var idle = ac.layers[0].stateMachine.states.FirstOrDefault(s => s.state.name == "Idle").state;
            var clip = idle == null ? null : idle.motion as AnimationClip;
            if (clip == null) throw new InvalidOperationException("Idle state has no clip in " + ControllerPath);
            return clip;
        }

        // ---------------------------------------------------------------- posing
        // Rotate the segment joint->child by deg about the axis, measured on the mesh side. A plain world
        // rotation written to the driver reached the mesh segment at only ~40-75% (constraint offsets and
        // the second tier), so the residual is solved iteratively like KD_CompanionMotionEditor.Aim.
        static void Rot(KH_DogRig rig, string joint, string child, Vector3 axis, float deg)
        {
            if (Mathf.Abs(deg) < 0.001f) return;
            var j = rig.Bone(joint); var c = child == null ? null : rig.Bone(child);
            if (c == null) c = rig.mouthAnchor != null && KH_DogRig.IsDescendantOf(rig.mouthAnchor, j) ? rig.mouthAnchor : null;
            var driver = rig.Driver(joint);
            if (c == null) { driver.rotation = Quaternion.AngleAxis(deg, axis) * driver.rotation; KH_DogRig.ApplyConstraints(rig.dog); return; }
            KH_DogRig.ApplyConstraints(rig.dog);
            var target = Quaternion.AngleAxis(deg, axis) * (c.position - j.position);
            for (int i = 0; i < 3; i++)
            {
                var current = c.position - j.position;
                var delta = Quaternion.FromToRotation(current, target);
                if (Quaternion.Angle(Quaternion.identity, delta) < 0.2f) break;
                driver.rotation = delta * driver.rotation;
                KH_DogRig.ApplyConstraints(rig.dog);
            }
        }

        // Body forward from the trunk (lumbar -> neck), not from the head: the idle head is turned a little,
        // and a pitch axis taken from it yaws every limb sideways (first render, 2026-09-20).
        public static Vector3 Forward(KH_DogRig rig)
        {
            var a = rig.Bone("Bone.001"); var b = rig.Bone("Bone.003");
            var fwd = (a != null && b != null) ? b.position - a.position : rig.nose.position - rig.head.position; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            return fwd.normalized;
        }

        static Vector3 PitchAxis(KH_DogRig rig)
        {
            return Vector3.Cross(Vector3.up, Forward(rig)).normalized; // + rotation = nose / hanging limb goes down-back
        }

        // Whole-body rotation about the pelvis, written to the root driver (the armature root that carries
        // every other driver, so the rotation reaches all mesh bones through the constraints). The sibling
        // tail root is not under it, so it follows the same pivot by hand.
        static void RootRotate(KH_DogRig rig, Quaternion q)
        {
            var root = rig.AnimationRoot; var tail = rig.SiblingTail;
            if (tail != null) { tail.position = root.position + q * (tail.position - root.position); tail.rotation = q * tail.rotation; }
            root.rotation = q * root.rotation;
        }

        // Yaw the whole dog about world up. Written to the same root driver as the k.root pitch, but the
        // amount that arrives is measured on the displayed body (Forward reads the mesh-side trunk bones)
        // and the residual is solved up to three times, like Rot does for a joint -- a world rotation written
        // to a driver reached the mesh at only 40-75% in the 2026-09-20 run, and a pitch-only solver would
        // leave the same shortfall in yaw. Applied before the pitch axis is taken so every joint angle below
        // is measured in the dog's own (turned) frame: the twist is a rigid yaw of the jump pose.
        static void TwistBody(GameObject dog, KH_DogRig rig, float deg)
        {
            KH_DogRig.ApplyConstraints(dog);
            var target = Quaternion.AngleAxis(deg, Vector3.up) * Forward(rig);
            RootRotate(rig, Quaternion.AngleAxis(deg, Vector3.up));
            KH_DogRig.ApplyConstraints(dog);
            for (int i = 0; i < 3; i++)
            {
                float residual = Vector3.SignedAngle(Forward(rig), target, Vector3.up);
                if (Mathf.Abs(residual) < 0.2f) break;
                RootRotate(rig, Quaternion.AngleAxis(residual, Vector3.up));
                KH_DogRig.ApplyConstraints(dog);
            }
        }

        static void Pose(GameObject dog, KH_DogRig rig, Key k)
        {
            if (rig.AnimationRoot == null) throw new InvalidOperationException("No animation root 'Bone'");
            if (Mathf.Abs(k.twist) > 0.001f) TwistBody(dog, rig, k.twist);
            var axis = PitchAxis(rig);
            var root = rig.AnimationRoot;
            var tail = rig.SiblingTail;
            // Whole body pitch about the pelvis; the sibling tail root follows the same pivot.
            if (Mathf.Abs(k.root) > 0.001f)
            {
                var q = Quaternion.AngleAxis(k.root, axis);
                if (tail != null) { tail.position = root.position + q * (tail.position - root.position); tail.rotation = q * tail.rotation; }
                root.rotation = q * root.rotation;
            }
            Rot(rig, "Bone.001", "Bone.002", axis, k.lumbar);
            Rot(rig, "Bone.002", "Bone.003", axis, k.chest);
            Rot(rig, "Bone.003", "Bone.004", axis, k.neck);
            Rot(rig, "Bone.004", "Bone.007", axis, k.head);
            Rot(rig, "Bone.006", null, axis, k.jaw);
            foreach (var side in new[] { "L", "R" })
            {
                Rot(rig, "Bone.005_" + side, "Bone.010_" + side, axis, k.hip);
                Rot(rig, "Bone.010_" + side, "Bone.015_" + side, axis, k.knee);
                Rot(rig, "Bone.015_" + side, "Bone.016_" + side, axis, k.hock);
                Rot(rig, "Bone.011_" + side, "Bone.012_" + side, axis, k.shoulder);
                Rot(rig, "Bone.012_" + side, "Bone.013_" + side, axis, k.elbow);
                Rot(rig, "Bone.013_" + side, "Bone.014_" + side, axis, k.wrist);
            }
            KH_DogRig.ApplyConstraints(dog);
            if (k.ground) Ground(dog, rig);
            if (k.plant) PlantHind(dog, rig, axis);
        }

        // Paw bones sit ~0.015 above the mesh bottom in the idle pose. Seating on the paws (not the body
        // bounds) keeps a drooping tail from lifting the dog off the floor (first PlantHind run reared the dog).
        const float PawPad = 0.015f;
        static float PawFloor(KH_DogRig rig)
        {
            float y = float.MaxValue;
            foreach (var n in new[] { "Bone.014_L", "Bone.014_R", "Bone.016_L", "Bone.016_R" }) { var b = rig.Bone(n); if (b != null && b.position.y < y) y = b.position.y; }
            return y - PawPad;
        }

        // Lowest body vertex in front of the tail base (the tail may droop through the floor; PhysBones own it at runtime).
        public static float BodyMinYNoTail(GameObject dog, KH_DogRig rig)
        {
            var tailBase = rig.Bone("Bone.018"); var fwd = Forward(rig);
            float limit = tailBase == null ? float.NegativeInfinity : Vector3.Dot(tailBase.position, fwd) - 0.02f;
            float min = float.MaxValue;
            foreach (var r in Bodies(dog))
            {
                var mesh = new Mesh();
                try
                {
                    r.BakeMesh(mesh, true);
                    foreach (var v in mesh.vertices) { var w = r.transform.TransformPoint(v); if (Vector3.Dot(w, fwd) >= limit && w.y < min) min = w.y; }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
            return min;
        }

        static void Ground(GameObject dog, KH_DogRig rig)
        {
            var root = rig.AnimationRoot; var tail = rig.SiblingTail;
            var shift = Vector3.up * (0.003f - Mathf.Min(PawFloor(rig), BodyMinYNoTail(dog, rig)));
            root.position += shift;
            if (tail != null) tail.position += shift;
            KH_DogRig.ApplyConstraints(dog);
        }

        // Folded hind legs are shorter than the fore legs, so a vertical sink leaves the hind paws in the air.
        // Pitch the whole body nose-up about the fore paws until the hind paws reach the floor (the loaded
        // crouch: rear low, chest up), then re-seat the lowest point.
        static void PlantHind(GameObject dog, KH_DogRig rig, Vector3 axis)
        {
            var root = rig.AnimationRoot; var tail = rig.SiblingTail;
            for (int iter = 0; iter < 2; iter++)
            {
                var fore = (rig.Bone("Bone.014_L").position + rig.Bone("Bone.014_R").position) * .5f;
                var hind = (rig.Bone("Bone.016_L").position + rig.Bone("Bone.016_R").position) * .5f;
                float h = hind.y - fore.y; var flat = hind - fore; flat.y = 0f;
                if (h < 0.01f || flat.magnitude < 0.05f) break;
                float deg = Mathf.Min(30f, Mathf.Asin(Mathf.Clamp(h / flat.magnitude, -1f, 1f)) * Mathf.Rad2Deg);
                var q = Quaternion.AngleAxis(-deg, axis); // nose up = rear down
                root.position = fore + q * (root.position - fore); root.rotation = q * root.rotation;
                if (tail != null) { tail.position = fore + q * (tail.position - fore); tail.rotation = q * tail.rotation; }
                KH_DogRig.ApplyConstraints(dog);
            }
            Ground(dog, rig);
        }

        // ---------------------------------------------------------------- bake helpers (KD_CompanionMotionEditor)
        public static IEnumerable<SkinnedMeshRenderer> Bodies(GameObject dog)
        {
            return dog.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy
                && !Ancestors(r.transform, dog.transform).Any(t => t.name.StartsWith("KD_Acc_") || t.name.StartsWith("KD_CandyCostume_") || t.name.StartsWith("KD_PumpkinCostume_") || t.name.StartsWith("KH_Collar_")));
        }
        static IEnumerable<Transform> Ancestors(Transform t, Transform root) { for (var p = t; p != null && p != root; p = p.parent) yield return p; }

        public static Bounds BodyBounds(GameObject dog)
        {
            bool first = true; var bounds = new Bounds();
            foreach (var r in Bodies(dog))
            {
                var mesh = new Mesh();
                try
                {
                    r.BakeMesh(mesh, true);
                    foreach (var v in mesh.vertices)
                    {
                        var p = r.transform.TransformPoint(v);
                        if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
            if (first) throw new InvalidOperationException("No visible body");
            return bounds;
        }

        static void SetKey(AnimationClip clip, EditorCurveBinding binding, float time, float value)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding) ?? new AnimationCurve();
            curve.AddKey(new Keyframe(time, value, 0, 0)); AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        static void Capture(AnimationClip clip, GameObject dog, KH_DogRig rig, float time)
        {
            foreach (var t in rig.srcSkelRoot.GetComponentsInChildren<Transform>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(t, dog.transform);
                var q = t.localRotation; var p = t.localPosition;
                for (int i = 0; i < 4; i++) SetKey(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + "xyzw"[i]), time, q[i]);
                for (int i = 0; i < 3; i++) SetKey(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition." + "xyz"[i]), time, p[i]);
            }
        }

        // ---------------------------------------------------------------- build
        [MenuItem("Tools/KominkaHillside/Frisbee/2 Build Jump Clip")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            Directory.CreateDirectory(ClipDir); Directory.CreateDirectory(DataDir); Directory.CreateDirectory(Output);
            var scene = EditorSceneManager.NewPreviewScene(); var log = new StringBuilder();
            try
            {
                var variants = Variants();
                for (int i = 0; i < variants.Length; i++) BuildOne(scene, variants[i], i, log);
                File.WriteAllText(Output + "/build.txt", log.ToString());
                Debug.Log("[KH Frisbee] clips built: " + string.Join(", ", variants.Select(v => v.ClipPath)) + "\n" + log);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void BuildOne(Scene scene, ClipVariant variant, int index, StringBuilder log)
        {
            AnimationClip result = new AnimationClip();
            try
            {
                var baseClip = IdleClip();
                log.AppendLine("== " + variant.name + " ==");
                result.name = "KH_" + variant.name + "_" + Breed; result.frameRate = 30;
                foreach (var key in variant.keys)
                {
                    var dog = CloneSourceDog(scene);
                    try
                    {
                        baseClip.SampleAnimation(dog, 0f); KH_DogRig.ApplyConstraints(dog);
                        var rig = KH_DogRig.Resolve(dog, MouthAnchorName);
                        if (!rig.Ok) throw new InvalidOperationException(string.Join("; ", rig.problems));
                        Pose(dog, rig, key);
                        string skeletonPath = AnimationUtility.CalculateTransformPath(rig.srcSkelRoot, dog.transform);
                        foreach (var b in AnimationUtility.GetCurveBindings(baseClip))
                        {
                            if (b.type == typeof(SkinnedMeshRenderer)) continue;
                            if (b.type == typeof(Transform) && (b.path == skeletonPath || b.path.StartsWith(skeletonPath + "/", StringComparison.Ordinal))) continue;
                            if (b.path.Length == 0) throw new InvalidOperationException("Base clip animates the network root: " + baseClip.name);
                            SetKey(result, b, key.time, AnimationUtility.GetEditorCurve(baseClip, b).Evaluate(0f));
                        }
                        Capture(result, dog, rig, key.time);
                        var mouth = rig.mouthAnchor.position - dog.transform.position;
                        log.AppendLine(key.name + " t=" + key.time.ToString("F2") + " mouth=" + mouth.ToString("F3") + " minY=" + BodyBounds(dog).min.y.ToString("F3") + " " + Limbs(rig));
                    }
                    finally { Object.DestroyImmediate(dog); }
                }
                result.EnsureQuaternionContinuity();
                foreach (var binding in AnimationUtility.GetCurveBindings(result))
                {
                    var curve = AnimationUtility.GetEditorCurve(result, binding);
                    for (int k = 1; k < curve.length - 1; k++) curve.SmoothTangents(k, 0);
                    AnimationUtility.SetEditorCurve(result, binding, curve);
                }
                var settings = AnimationUtility.GetAnimationClipSettings(result);
                settings.loopTime = false; AnimationUtility.SetAnimationClipSettings(result, settings);
                string clipPath = variant.ClipPath;
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (existing == null) { AssetDatabase.CreateAsset(result, clipPath); result = null; }
                else { EditorUtility.CopySerialized(result, existing); EditorUtility.SetDirty(existing); }
                AssetDatabase.SaveAssets();

                // Pose facts for the controller: mouth anchor in the dog frame at the catch key and at idle.
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                Vector3 mouthCatch = MouthAt(scene, clip, variant.CatchAt), mouthStand = MouthAt(scene, baseClip, 0f);
                File.WriteAllText(variant.FactsPath, "{\n  \"name\": \"" + variant.name + "\",\n  \"breed\": \"" + Breed + "\",\n  \"mouthOffsetCatch\": " + V(mouthCatch) + ",\n  \"mouthOffsetStand\": " + V(mouthStand)
                    + ",\n  \"crouch\": " + F(variant.crouch) + ",\n  \"rise\": " + F(variant.rise) + ",\n  \"recover\": " + F(variant.recover) + ",\n  \"clipLength\": " + F(clip.length) + "\n}\n");
                AssetDatabase.ImportAsset(variant.FactsPath);
                AddClipState(clip, variant, index);
                log.AppendLine("clip length=" + clip.length + " curves=" + AnimationUtility.GetCurveBindings(clip).Length);
                log.AppendLine("mouthOffsetCatch=" + mouthCatch.ToString("F3") + " mouthOffsetStand=" + mouthStand.ToString("F3"));
            }
            finally { if (result != null) Object.DestroyImmediate(result); }
        }

        // Segment directions (world, dog at identity) for reading the pose numerically: fore 011->012->014, hind 005->010->015->016.
        static string Limbs(KH_DogRig rig)
        {
            string D(string a, string b) { var pa = rig.Bone(a); var pb = rig.Bone(b); return pa == null || pb == null ? "?" : (pb.position - pa.position).ToString("F2"); }
            return "upperArm=" + D("Bone.011_R", "Bone.012_R") + " forearm=" + D("Bone.012_R", "Bone.014_R") + " thigh=" + D("Bone.005_R", "Bone.010_R") + " shank=" + D("Bone.010_R", "Bone.015_R") + " meta=" + D("Bone.015_R", "Bone.016_R") + " root=" + rig.AnimationRoot.position.ToString("F2");
        }

        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        static string F(float v) { return v.ToString("F4", Inv); }
        static string V(Vector3 v) { return "[" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + "]"; }

        static Vector3 MouthAt(Scene scene, AnimationClip clip, float time)
        {
            var dog = CloneSourceDog(scene);
            try
            {
                clip.SampleAnimation(dog, time); KH_DogRig.ApplyConstraints(dog);
                var rig = KH_DogRig.Resolve(dog, MouthAnchorName);
                if (!rig.Ok) throw new InvalidOperationException(string.Join("; ", rig.problems));
                return Quaternion.Inverse(dog.transform.rotation) * (rig.mouthAnchor.position - dog.transform.position);
            }
            finally { Object.DestroyImmediate(dog); }
        }

        // Adds Bool "Jump" + state "Jump" to the breed's controller (idempotent), i.e. the first variant.
        public static void AddJumpState(AnimationClip clip) { AddClipState(clip, Variants()[0], 0); }

        // Adds the variant's Bool + state to the breed's controller (idempotent). Any State -> state while
        // the Bool is up (no self transition); state -> Run / Walk / Idle once it drops, like the Call states.
        // New states are stacked below the Jump node so the graph stays readable; an existing node keeps its place.
        public static void AddClipState(AnimationClip clip, ClipVariant variant, int index)
        {
            string param = variant.boolParam;
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ac == null) throw new InvalidOperationException("Missing " + ControllerPath);
            if (!ac.parameters.Any(p => p.name == param)) ac.AddParameter(param, AnimatorControllerParameterType.Bool);
            var sm = ac.layers[0].stateMachine;
            var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == param);
            if (state == null) state = sm.AddState(param, new Vector3(950, 640 + 80 * index, 0));
            state.motion = clip; state.writeDefaultValues = true;
            AnimatorState Find(string n) { var s = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == n); if (s == null) throw new InvalidOperationException("State " + n + " missing"); return s; }
            foreach (var t in sm.anyStateTransitions.Where(t => t.destinationState == state).ToArray()) sm.RemoveAnyStateTransition(t);
            var any = sm.AddAnyStateTransition(state);
            any.hasExitTime = false; any.duration = 0.1f; any.canTransitionToSelf = false; any.AddCondition(AnimatorConditionMode.If, 0, param);
            foreach (var t in state.transitions.ToArray()) state.RemoveTransition(t);
            var toRun = state.AddTransition(Find("Run")); toRun.hasExitTime = false; toRun.duration = 0.1f; toRun.AddCondition(AnimatorConditionMode.IfNot, 0, param); toRun.AddCondition(AnimatorConditionMode.If, 0, "Run");
            var toWalk = state.AddTransition(Find("Walk")); toWalk.hasExitTime = false; toWalk.duration = 0.1f; toWalk.AddCondition(AnimatorConditionMode.IfNot, 0, param); toWalk.AddCondition(AnimatorConditionMode.If, 0, "Walk");
            var toIdle = state.AddTransition(Find("Idle")); toIdle.hasExitTime = false; toIdle.duration = 0.2f; toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, param);
            EditorUtility.SetDirty(ac); AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/KominkaHillside/Frisbee/9 Inspect Rig")]
        public static void InspectRig()
        {
            var scene = EditorSceneManager.NewPreviewScene(); var log = new StringBuilder();
            try
            {
                var dog = CloneSourceDog(scene);
                try
                {
                    IdleClip().SampleAnimation(dog, 0f); KH_DogRig.ApplyConstraints(dog);
                    var rig = KH_DogRig.Resolve(dog, MouthAnchorName);
                    log.AppendLine("twoTier=" + rig.TwoTier + " mesh=" + (rig.meshSkelRoot == null ? "-" : rig.meshSkelRoot.name) + " problems=" + string.Join(";", rig.problems));
                    foreach (var n in new[] { "Bone", "Bone.001", "Bone.002", "Bone.003", "Bone.004", "Bone.006", "Bone.005_R", "Bone.010_R", "Bone.015_R", "Bone.016_R", "Bone.011_R", "Bone.012_R", "Bone.013_R", "Bone.014_R", "Bone.018" })
                    {
                        var m = rig.Bone(n); string d = "?";
                        try { var dr = rig.Driver(n); d = AnimationUtility.CalculateTransformPath(dr, dog.transform); } catch (Exception e) { d = "ERR " + e.Message; }
                        log.AppendLine(n + " meshPath=" + (m == null ? "-" : AnimationUtility.CalculateTransformPath(m, dog.transform)) + " pos=" + (m == null ? "-" : m.position.ToString("F3")) + " driver=" + d);
                    }
                    var mouth = rig.mouthAnchor; log.AppendLine("mouth path=" + (mouth == null ? "-" : AnimationUtility.CalculateTransformPath(mouth, dog.transform)));
                    // Experiment: does a 42-degree world rotation on the forearm driver reach the mesh paw?
                    {
                        var drv = rig.Driver("Bone.012_R"); var paw = rig.Bone("Bone.014_R"); var m12 = rig.Bone("Bone.012_R");
                        var before = drv.rotation; var pawBefore = paw.position; var axis = PitchAxis(rig);
                        log.AppendLine("scales: drvParentLossy=" + drv.parent.lossyScale.ToString("F3") + " meshParentLossy=" + m12.parent.lossyScale.ToString("F3") + " inu=" + rig.inu.lossyScale.ToString("F3"));
                        drv.rotation = Quaternion.AngleAxis(42f, axis) * drv.rotation;
                        log.AppendLine("driver world delta=" + Quaternion.Angle(before, drv.rotation).ToString("F1") + " local=" + drv.localRotation.eulerAngles.ToString("F1"));
                        KH_DogRig.ApplyConstraints(dog);
                        log.AppendLine("mesh012 delta vs driver=" + Quaternion.Angle(m12.rotation, drv.rotation).ToString("F1") + " paw moved=" + (paw.position - pawBefore).ToString("F3") + " forearm=" + (paw.position - m12.position).ToString("F3"));
                        var rc = m12.GetComponent<UnityEngine.Animations.RotationConstraint>(); log.AppendLine("rc offset=" + rc.rotationOffset.ToString("F2") + " weight=" + rc.weight + " srcWeight=" + rc.GetSource(0).weight + " axis=" + rc.rotationAxis);
                    }
                    foreach (var b in AnimationUtility.GetCurveBindings(IdleClip()).Take(400)) log.AppendLine("BIND " + b.path + " " + b.propertyName);
                }
                finally { Object.DestroyImmediate(dog); }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/rig.txt", log.ToString());
        }

        // ---------------------------------------------------------------- render (side + three-quarter sheets)
        [MenuItem("Tools/KominkaHillside/Frisbee/3 Render Stages")]
        public static void Render()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            Directory.CreateDirectory(Output);
            var variants = Variants();
            foreach (var v in variants)
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(v.ClipPath) == null) throw new InvalidOperationException("Build the clips first: " + v.ClipPath);
            var scene = EditorSceneManager.NewPreviewScene();
            var log = new StringBuilder();
            try
            {
                Bounds frame;
                {
                    var idle = CloneSourceDog(scene);
                    try { IdleClip().SampleAnimation(idle, 0f); KH_DogRig.ApplyConstraints(idle); frame = BodyBounds(idle); }
                    finally { Object.DestroyImmediate(idle); }
                }
                var center = frame.center; center.y = frame.size.y * 0.55f;
                float ortho = Mathf.Max(frame.size.x, frame.size.y, frame.size.z) * 0.72f;
                foreach (var v in variants) RenderVariant(scene, v, center, ortho, log);
                Debug.Log("[KH Frisbee] rendered " + variants.Length + " variants to " + Output + "\n" + log);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        // The jump keeps the file names the stage-2 record links (sheet-side.png / sheet-quarter.png);
        // every other variant gets its name appended.
        static string Suffix(ClipVariant v) { return v.name == "Jump" ? "" : "-" + v.name; }

        static void RenderVariant(Scene scene, ClipVariant variant, Vector3 center, float ortho, StringBuilder log)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(variant.ClipPath);
            var keys = variant.keys;
            string sfx = Suffix(variant);
            const int W = 420, H = 360;
            var side = new Texture2D(W * keys.Length, H, TextureFormat.RGB24, false);
            var quarter = new Texture2D(W * keys.Length, H, TextureFormat.RGB24, false);
            try
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    var (s, q) = RenderOne(scene, clip, keys[i].time, keys[i].name, center, ortho, W, H, keys[i].twist);
                    side.SetPixels(i * W, 0, W, H, s.GetPixels()); quarter.SetPixels(i * W, 0, W, H, q.GetPixels());
                    File.WriteAllBytes(Output + "/" + i + "-" + keys[i].name + "-side" + sfx + ".png", s.EncodeToPNG());
                    Object.DestroyImmediate(s); Object.DestroyImmediate(q);
                }
                side.Apply(); quarter.Apply();
                File.WriteAllBytes(Output + "/sheet-side" + sfx + ".png", side.EncodeToPNG());
                File.WriteAllBytes(Output + "/sheet-quarter" + sfx + ".png", quarter.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(side); Object.DestroyImmediate(quarter); }
            var mouthCatch = MouthAt(scene, clip, variant.CatchAt);
            log.AppendLine(variant.name + ": " + keys.Length + " stages, sheet-side" + sfx + ".png / sheet-quarter" + sfx + ".png"
                + " mouthOffsetCatch=" + mouthCatch.ToString("F3") + " twist=" + string.Join("/", keys.Select(k => k.twist.ToString("F0"))));
        }

        // untwist = the key's yaw: the cameras stay in the run frame instead of turning with the dog, so a
        // twist key shows the body turned toward the lens. Zero for every jump key, so the jump sheets are
        // the same views as stage 2.
        static (Texture2D side, Texture2D quarter) RenderOne(Scene scene, AnimationClip clip, float time, string label, Vector3 center, float ortho, int w, int h, float untwist = 0f)
        {
            var owned = new List<Object>();
            T Own<T>(T v) where T : Object { owned.Add(v); return v; }
            try
            {
                var dog = Own(CloneSourceDog(scene));
                clip.SampleAnimation(dog, time); KH_DogRig.ApplyConstraints(dog);
                var rig = KH_DogRig.Resolve(dog, MouthAnchorName);
                var fwd = Forward(rig);
                if (Mathf.Abs(untwist) > 0.001f) fwd = Quaternion.AngleAxis(-untwist, Vector3.up) * fwd;
                var right = Vector3.Cross(Vector3.up, fwd);
                var display = Own(new GameObject("Render")); SceneManager.MoveGameObjectToScene(display, scene);
                var mat = Own(new Material(Shader.Find("Standard"))); mat.color = new Color(.70f, .55f, .30f); mat.SetFloat("_Glossiness", .1f);
                foreach (var r in Bodies(dog))
                {
                    var mesh = Own(new Mesh()); r.BakeMesh(mesh, true);
                    var v = mesh.vertices; var n = mesh.normals; var nm = r.transform.localToWorldMatrix.inverse.transpose;
                    for (int i = 0; i < v.Length; i++) v[i] = r.transform.TransformPoint(v[i]);
                    for (int i = 0; i < n.Length; i++) n[i] = nm.MultiplyVector(n[i]).normalized;
                    mesh.vertices = v; mesh.normals = n; mesh.RecalculateBounds();
                    var obj = new GameObject("Body"); obj.transform.SetParent(display.transform, false);
                    obj.AddComponent<MeshFilter>().sharedMesh = mesh; obj.AddComponent<MeshRenderer>().sharedMaterials = Enumerable.Repeat(mat, mesh.subMeshCount).ToArray();
                }
                // Mouth anchor marker (red) and pelvis marker (blue): the two points the controller reasons about.
                var red = Own(new Material(Shader.Find("Standard"))); red.color = Color.red;
                var blue = Own(new Material(Shader.Find("Standard"))); blue.color = new Color(.2f, .4f, 1f);
                var m = GameObject.CreatePrimitive(PrimitiveType.Sphere); m.transform.SetParent(display.transform, false); m.transform.position = rig.mouthAnchor.position; m.transform.localScale = Vector3.one * .035f; m.GetComponent<Renderer>().sharedMaterial = red;
                var pv = GameObject.CreatePrimitive(PrimitiveType.Sphere); pv.transform.SetParent(display.transform, false); pv.transform.position = rig.AnimationRoot.position; pv.transform.localScale = Vector3.one * .03f; pv.GetComponent<Renderer>().sharedMaterial = blue;
                dog.SetActive(false);
                var camGo = Own(new GameObject("Camera")); SceneManager.MoveGameObjectToScene(camGo, scene); var cam = camGo.AddComponent<Camera>(); cam.enabled = false; cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .20f, .22f); cam.orthographic = true; cam.useOcclusionCulling = false;
                cam.orthographicSize = ortho; cam.nearClipPlane = .01f; cam.farClipPlane = 20; cam.aspect = (float)w / h;
                var lightGo = Own(new GameObject("Light")); SceneManager.MoveGameObjectToScene(lightGo, scene); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; lightGo.transform.rotation = Quaternion.Euler(40, -30, 0);
                var floor = Own(GameObject.CreatePrimitive(PrimitiveType.Plane)); SceneManager.MoveGameObjectToScene(floor, scene); floor.transform.localScale = Vector3.one * .3f;
                var floorMat = Own(new Material(Shader.Find("Standard"))); floorMat.color = new Color(.30f, .32f, .33f); floor.GetComponent<Renderer>().sharedMaterial = floorMat;
                // A thin slab reads as the ground line in the edge-on side view.
                var line = Own(GameObject.CreatePrimitive(PrimitiveType.Cube)); SceneManager.MoveGameObjectToScene(line, scene); line.transform.position = new Vector3(0, -.006f, 0); line.transform.localScale = new Vector3(3f, .012f, 3f);
                var lineMat = Own(new Material(Shader.Find("Standard"))); lineMat.color = new Color(.85f, .85f, .8f); line.GetComponent<Renderer>().sharedMaterial = lineMat;
                var rt = Own(new RenderTexture(w, h, 24)); var previous = RenderTexture.active;
                Texture2D Shot(Vector3 offset)
                {
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    cam.transform.position = center + offset; cam.transform.LookAt(center);
                    cam.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1, 1, -1)) * cam.transform.worldToLocalMatrix;
                    float hh = cam.orthographicSize, hw = hh * cam.aspect;
                    cam.projectionMatrix = Matrix4x4.Ortho(-hw, hw, -hh, hh, cam.nearClipPlane, cam.farClipPlane);
                    cam.cullingMatrix = cam.projectionMatrix * cam.worldToCameraMatrix;
                    cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                    return tex;
                }
                try
                {
                    var s = Shot(right * 3f);
                    var q = Shot((right * 2.2f + fwd * 2.2f + Vector3.up * 1.4f));
                    return (s, q);
                }
                finally { RenderTexture.active = previous; cam.targetTexture = null; }
            }
            finally { for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); }
        }
    }
}
