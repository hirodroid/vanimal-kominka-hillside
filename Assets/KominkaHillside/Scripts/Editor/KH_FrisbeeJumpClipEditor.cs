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
    // IMP-DOG-012: six breeds. Every path / clone / controller takes a breed; the Lab table is the base and
    // Specs (breed table) scales it per breed. 2 / 3 run all six, 2a / 3a one breed (EditorPrefs KH_Frisbee_Breed),
    // 2b verifies the 12 clips + 12 facts. The breed-less members stay as the Lab-compatible entry points.
    public static class KH_FrisbeeJumpClipEditor
    {
        public const string SourceScene = "Assets/KominkaHillside/Imported/KH_DogPortSource.unity";
        public const string ClipDir = "Assets/KominkaHillside/Animations";
        public const string DataDir = "Assets/KominkaHillside/Data/Frisbee";
        public const string Output = "Temp/KH_Frisbee";
        public const string MouthAnchorName = "KD_FetchAnchor_Mouth";
        // IMP-DOG-012: every path is per breed. The breed-less members below are the Lab-compatible entry points
        // the stage-2/3 callers (prototype scene, Walking Build before the six-dog wiring) still use.
        public const string Breed = "Lab";
        // Dog key order of the scene (KH_DogWalkingEditor.Keys): Corgi, Pome, Lab, Dachs, Shiba, Samoyed.
        public static readonly string[] Breeds = { "Corgi", "Pome", "Lab", "Dachs", "Shiba", "Samoyed" };
        // Asset names are KH_{variant}_{breed}.
        public static string ClipPathFor(string name, string breed) { return ClipDir + "/KH_" + name + "_" + breed + ".anim"; }
        public static string FactsPathFor(string name, string breed) { return DataDir + "/KH_" + name + "_" + breed + ".json"; }
        public static string ControllerPathFor(string breed) { return "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_KD_Dog_" + breed + ".controller"; }
        public static string OutputFor(string breed) { return Output + "/" + breed; }
        // Lab compatible.
        public static string ClipPathFor(string name) { return ClipPathFor(name, Breed); }
        public static string FactsPathFor(string name) { return FactsPathFor(name, Breed); }
        public static string ClipPath { get { return ClipPathFor("Jump", Breed); } }
        public static string FactsPath { get { return FactsPathFor("Jump", Breed); } }
        public static string ControllerPath { get { return ControllerPathFor(Breed); } }

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
            public float pawPad; // paw bone height above the mesh sole for this breed (BreedSpec.pawPad; set by ForBreed)
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

        // The Lab table of stage 2 (IMP-DOG-005), unchanged. Every other breed is this table run through its BreedSpec.
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

        // ---------------------------------------------------------------- breed table (IMP-DOG-012)
        // THE one place the per-breed pose values live. Each breed = the Lab table above with its joint angles
        // multiplied per group and its phases re-timed. Ground keys (feet on the floor: start, crouch, takeoff,
        // touchdown, absorb, recover) and air keys (rise, catch, bite, descend) get separate leg factors because a
        // short leg folded as far as the Lab's drops the belly onto the floor (Ground then seats the dog on its belly
        // and the paws hang in the air), while in the air a short leg can tuck as much as it likes.
        // rise stays the Lab's 0.35 s for every breed: the runtime stretches the rise to the planned tUp anyway
        // (animSpeed = clipRise / tUp), and the planner's lower root rise 0.19 m is derived from 0.35 s x 1.8.
        // Crouch / recover are re-timed (shorter legs load and settle faster; ~sqrt of the leg-length ratio).
        // "仮" = first guess from the leg / height ratio, to be confirmed on the side sheets (Frisbee/3).
        public struct BreedSpec
        {
            public string breed;
            public float groundLegs; // hip/knee/hock/shoulder/elbow/wrist on ground keys
            public float airLegs;    // the same on air keys
            public float trunk;      // root/lumbar/chest
            public float head;       // neck/head/jaw
            public float twist;      // x TwistDeg (the twist variant's yaw)
            public float crouch, rise, recover; // seconds
            // Standing mouth height (m) of this breed's idle, measured by Frisbee/2 (facts mouthOffsetStand.y, 2026-09-24).
            // 2b Verify checks the built facts still agree, so a changed model cannot leave maxRootRise stale.
            public float standMouthY;
            // Planner upper root rise (IMP-DOG-012, main's decision after Codex D1): the Lab keeps the 0.9 m approved in
            // PCVR; every other breed is scaled by its standing mouth height, clamped to [0.30, 0.9]. Written to the facts;
            // F2's ConfigureCatcher carries it to the catcher. 仮 (PCVR で高い投げを確認).
            public float maxRootRise { get { return Mathf.Clamp(LabMaxRootRise * (standMouthY / LabStandMouthY), MinMaxRootRise, LabMaxRootRise); } }
            // Paw bone -> mesh sole. The Lab's 0.015 left the small dogs floating 0.5-1 cm on the round-1 sheets
            // (ground keys minYNoTail 0.008-0.013 with pawGap -0.005..-0.010): pad = 0.015 - the worst resting gap.
            public float pawPad;
            public string basis;
        }

        // maxRootRise = 0.9 x standMouthY / 0.5559 in [0.30, 0.9]: Corgi 0.370, Pome 0.30 (0.254 floored), Lab 0.90,
        // Dachs 0.372, Shiba 0.581, Samoyed 0.815.
        public const float LabMaxRootRise = 0.9f, LabStandMouthY = 0.5559f, MinMaxRootRise = 0.45f; // 司令塔 2026-09-24: 下限 0.30 では Pome が flat を跳んで取れず stand 捕獲になった (ハーネス expectFailed 2)。小型犬でも 0.45 m (体高の約 2.9 倍) は跳ぶ。PCVR で見え方を確認

        static readonly BreedSpec[] Specs =
        {
            // Idle measurements (MeasureBreeds, 2026-09-24; avg leg = (fore + hind) / 2 of the mesh bone chains,
            // trunk = Bone.001-003):   leg/Lab  leg/trunk (Lab 1.40)
            //   Corgi   leg 0.207  trunk 0.195   0.48   1.06   short-legged
            //   Pome    leg 0.140  trunk 0.114   0.32   1.23   small, near-proportional
            //   Lab     leg 0.434  trunk 0.309   1      1.40
            //   Dachs   leg 0.182  trunk 0.199   0.42   0.91   shortest relative to trunk
            //   Shiba   leg 0.347  trunk 0.212   0.80   1.64   long-legged for its trunk
            //   Samoyed leg 0.414  trunk 0.323   0.95   1.28   Lab-sized, thick belly coat
            // groundLegs follows leg/trunk against the Lab's (short-legged = less fold before the belly meets the floor);
            // crouch / recover ~ sqrt(leg/Lab), floored at 0.8 x so the small dogs' load stays readable.
            // 仮: Corgi. leg/trunk 0.76 x Lab. Trunk pitch softened: its long low trunk lifts the chest a lot when pitched.
            new BreedSpec { breed = "Corgi", standMouthY = 0.2284f, pawPad = 0.010f, groundLegs = 0.55f, airLegs = 1f, trunk = 0.8f, head = 1f, twist = 1f, crouch = 0.20f, rise = Rise, recover = 0.32f,
                basis = "仮: leg/trunk 0.76 x Lab (short legs) -> ground folds 0.55, air 1 (round 2: 0.8 read as too little change; air clearance at min rise 0.144), trunk 0.8; sqrt(leg 0.48)=0.69 -> timing floor 0.8 x" },
            // 仮: Pome. leg/trunk 0.88 x Lab: nearly Lab angles; the smallest dog, so the fastest phases (floored).
            new BreedSpec { breed = "Pome", standMouthY = 0.1566f, pawPad = 0.005f, groundLegs = 0.85f, airLegs = 1f, trunk = 1f, head = 1f, twist = 1f, crouch = 0.20f, rise = Rise, recover = 0.32f,
                basis = "仮: leg/trunk 0.88 x Lab -> ground folds 0.85; sqrt(leg 0.32)=0.57 -> timing floor 0.8 x" },
            new BreedSpec { breed = "Lab", standMouthY = LabStandMouthY, pawPad = 0.015f, groundLegs = 1f, airLegs = 1f, trunk = 1f, head = 1f, twist = 1f, crouch = Crouch, rise = Rise, recover = Recover,
                basis = "stage-2 table (IMP-DOG-005, user OK in PCVR)" },
            // 仮: Dachs. leg/trunk 0.65 x Lab, the longest trunk for its legs: the Corgi's reasoning one step further.
            new BreedSpec { breed = "Dachs", standMouthY = 0.2296f, pawPad = 0.007f, groundLegs = 0.5f, airLegs = 0.9f, trunk = 0.7f, head = 1f, twist = 1f, crouch = 0.20f, rise = Rise, recover = 0.32f,
                basis = "仮: leg/trunk 0.65 x Lab (shortest) -> ground folds 0.5, air 0.9 (round 2: was 0.75; clearance 0.160), trunk 0.7; sqrt(leg 0.42)=0.65 -> timing floor 0.8 x" },
            // 仮: Shiba. leg/trunk 1.17 x Lab: Lab angles; a little smaller, so a little faster.
            new BreedSpec { breed = "Shiba", standMouthY = 0.3587f, pawPad = 0.006f, groundLegs = 1f, airLegs = 1f, trunk = 1f, head = 1f, twist = 1f, crouch = 0.22f, rise = Rise, recover = 0.36f,
                basis = "仮: leg/trunk 1.17 x Lab -> Lab angles; sqrt(leg 0.80)=0.89 -> crouch 0.22 / recover 0.36" },
            // 仮: Samoyed. Lab-sized (leg 0.95 x); leg/trunk 0.91 x and a thick belly coat -> slightly less ground fold.
            new BreedSpec { breed = "Samoyed", standMouthY = 0.5034f, pawPad = 0.010f, groundLegs = 0.9f, airLegs = 1f, trunk = 1f, head = 1f, twist = 1f, crouch = Crouch, rise = Rise, recover = Recover,
                basis = "仮: leg 0.95 x Lab, leg/trunk 0.91 x + belly coat -> ground folds 0.9, Lab timing" },
        };

        public static BreedSpec SpecFor(string breed)
        {
            foreach (var s in Specs) if (s.breed == breed) return s;
            throw new InvalidOperationException("No frisbee breed spec for " + breed + " (KH_FrisbeeJumpClipEditor.Specs)");
        }

        static bool IsLabTiming(BreedSpec s) { return s.crouch == Crouch && s.rise == Rise && s.recover == Recover; }

        // Maps a time on the Lab clip onto this breed's phases (piecewise linear per phase; phase boundaries exact).
        static float Retime(float t, BreedSpec s)
        {
            if (IsLabTiming(s)) return t;
            if (t <= 0f) return 0f;
            if (t == Crouch) return s.crouch;
            if (t == CatchAt) return s.crouch + s.rise;
            if (t == TouchdownAt) return s.crouch + s.rise + s.rise;
            if (t == EndAt) return s.crouch + s.rise + s.rise + s.recover;
            if (t < Crouch) return t * (s.crouch / Crouch);
            if (t < CatchAt) return s.crouch + (t - Crouch) * (s.rise / Rise);
            if (t < TouchdownAt) return s.crouch + s.rise + (t - CatchAt) * (s.rise / Rise);
            return s.crouch + s.rise + s.rise + (t - TouchdownAt) * (s.recover / Recover);
        }

        static Key ForBreed(Key k, BreedSpec s)
        {
            float legs = k.ground ? s.groundLegs : s.airLegs;
            k.hip *= legs; k.knee *= legs; k.hock *= legs; k.shoulder *= legs; k.elbow *= legs; k.wrist *= legs;
            k.root *= s.trunk; k.lumbar *= s.trunk; k.chest *= s.trunk;
            k.neck *= s.head; k.head *= s.head; k.jaw *= s.head;
            k.twist *= s.twist;
            k.pawPad = s.pawPad;
            k.time = Retime(k.time, s);
            return k;
        }

        static Key[] Keys(BreedSpec s) { return Keys().Select(k => ForBreed(k, s)).ToArray(); }
        static Key[] TwistKeys(BreedSpec s) { return TwistKeys().Select(k => ForBreed(k, s)).ToArray(); }

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
            public string name;      // asset name part: KH_{name}_{breed}.anim / .json
            public string breed;     // null = Lab (the stage-2/3 callers build variants without a breed)
            public Key[] keys;
            public float crouch, rise, recover;
            public string boolParam; // Animator Bool and State name
            public string Breed { get { return string.IsNullOrEmpty(breed) ? KH_FrisbeeJumpClipEditor.Breed : breed; } }
            public float CatchAt { get { return crouch + rise; } }
            public float TouchdownAt { get { return crouch + rise + rise; } }
            public float EndAt { get { return crouch + rise + rise + recover; } }
            public string ClipPath { get { return ClipPathFor(name, Breed); } }
            public string FactsPath { get { return FactsPathFor(name, Breed); } }
            public string ControllerPath { get { return ControllerPathFor(Breed); } }
        }

        // Lab compatible (KH_FrisbeePrototypeEditor / KH_DogWalkingEditor before the six-dog wiring).
        public static ClipVariant[] Variants() { return Variants(Breed); }

        public static ClipVariant[] Variants(string breed)
        {
            var s = SpecFor(breed);
            return new[]
            {
                new ClipVariant { name = "Jump", breed = breed, keys = Keys(s), crouch = s.crouch, rise = s.rise, recover = s.recover, boolParam = "Jump" },
                new ClipVariant { name = "Twist", breed = breed, keys = TwistKeys(s), crouch = s.crouch, rise = s.rise, recover = s.recover, boolParam = "Twist" },
            };
        }

        // ---------------------------------------------------------------- source dog
        // The port source scene is opened additively only if it is not already open, and closed only if it was
        // opened here (review M1: never close a scene someone else has open, dirty or not).
        public static GameObject CloneSourceDog(Scene into) { return CloneSourceDog(into, Breed); }

        public static GameObject CloneSourceDog(Scene into, string breed)
        {
            var source = SceneManager.GetSceneByPath(SourceScene);
            bool openedHere = !(source.IsValid() && source.isLoaded);
            if (openedHere) source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
            try
            {
                Transform found = null;
                foreach (var root in source.GetRootGameObjects())
                {
                    found = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "KD_Dog_" + breed);
                    if (found != null) break;
                }
                if (found == null) throw new InvalidOperationException("KD_Dog_" + breed + " not found in " + SourceScene);
                var clone = Object.Instantiate(found.gameObject);
                clone.name = "KD_Dog_" + breed;
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
            int dogs = 0, off = 0, skipped = 0; var state = new StringBuilder();
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
                // IMP-DOG-012: say per breed where each of the six stands, so a dog still waiting for its catcher
                // (Walking/3 wires it and makes it rigid itself) is visible instead of silently skipped.
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (!t.name.StartsWith("KD_Dog_", StringComparison.Ordinal) || !Breeds.Contains(t.name.Substring(7))) continue;
                        bool wired = seen.Contains(t.gameObject);
                        state.AppendLine("  " + scene.name + "/" + t.name + ": " + (wired ? "frisbee dog, " : "no KH_FrisbeeCatch yet (left alone), ")
                            + (FindBodyFollow(t.gameObject) == null ? "follow constraint NOT FOUND" : IsBodyRigid(t.gameObject) ? "rigid" : "follow gimmick on"));
                    }
            }
            Debug.Log("[KH Frisbee] body-follow constraints switched off: " + off + " on " + dogs + " frisbee dog(s)"
                + (skipped > 0 ? ", " + skipped + " catcher(s) with no dogRoot wired" : "")
                + ". Dogs without a KH_FrisbeeCatch are untouched. Save the scene to keep it.\n" + state);
        }

        public static AnimationClip IdleClip() { return IdleClip(Breed); }

        public static AnimationClip IdleClip(string breed)
        {
            string path = ControllerPathFor(breed);
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) throw new InvalidOperationException("Missing " + path);
            var idle = ac.layers[0].stateMachine.states.FirstOrDefault(s => s.state.name == "Idle").state;
            var clip = idle == null ? null : idle.motion as AnimationClip;
            if (clip == null) throw new InvalidOperationException("Idle state has no clip in " + path);
            return clip;
        }

        // IMP-DOG-012: idle proportions read off the posed clone, for choosing / checking the breed table.
        // Leg lengths are the mesh-side bone chains (fore 011-012-013-014, hind 005-010-015-016).
        public static string Proportions(GameObject dog, KH_DogRig rig)
        {
            float Chain(params string[] n)
            {
                float sum = 0f;
                for (int i = 1; i < n.Length; i++) { var a = rig.Bone(n[i - 1]); var b = rig.Bone(n[i]); if (a == null || b == null) return float.NaN; sum += Vector3.Distance(a.position, b.position); }
                return sum;
            }
            var bounds = BodyBounds(dog); var fwd = Forward(rig);
            float fore = Chain("Bone.011_R", "Bone.012_R", "Bone.013_R", "Bone.014_R");
            float hind = Chain("Bone.005_R", "Bone.010_R", "Bone.015_R", "Bone.016_R");
            float trunk = Chain("Bone.001", "Bone.002", "Bone.003");
            var shoulder = rig.Bone("Bone.011_R"); var pelvis = rig.AnimationRoot;
            return "height=" + bounds.max.y.ToString("F3") + " length=" + Vector3.Scale(bounds.size, new Vector3(Mathf.Abs(fwd.x), 0, Mathf.Abs(fwd.z))).magnitude.ToString("F3")
                + " withers=" + (shoulder == null ? "?" : shoulder.position.y.ToString("F3")) + " pelvis=" + pelvis.position.y.ToString("F3")
                + " foreLeg=" + fore.ToString("F3") + " hindLeg=" + hind.ToString("F3") + " trunk(001-003)=" + trunk.ToString("F3")
                + " leg/height=" + ((fore + hind) * 0.5f / Mathf.Max(0.001f, bounds.max.y)).ToString("F2")
                + " mouth=" + (rig.mouthAnchor.position - dog.transform.position).ToString("F3");
        }

        // Read-only: clones each breed into a preview scene, samples its idle and returns the proportions (no menu;
        // called by reflection when choosing the table, and by Build for the log).
        public static string MeasureBreeds()
        {
            var scene = EditorSceneManager.NewPreviewScene(); var sb = new StringBuilder();
            try
            {
                foreach (var breed in Breeds)
                {
                    var dog = CloneSourceDog(scene, breed);
                    try
                    {
                        IdleClip(breed).SampleAnimation(dog, 0f); KH_DogRig.ApplyConstraints(dog);
                        var rig = KH_DogRig.Resolve(dog, MouthAnchorName);
                        sb.AppendLine(breed + ": " + (rig.Ok ? Proportions(dog, rig) : "rig problems " + string.Join("; ", rig.problems)));
                    }
                    finally { Object.DestroyImmediate(dog); }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            return sb.ToString();
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
            if (k.ground) Ground(dog, rig, k.pawPad);
            if (k.plant) PlantHind(dog, rig, axis, k.pawPad);
        }

        // Paw bones sit ~pawPad above the mesh bottom in the idle pose (Lab 0.015; per breed in BreedSpec.pawPad). Seating on the paws (not the body
        // bounds) keeps a drooping tail from lifting the dog off the floor (first PlantHind run reared the dog).
        static float PawFloor(KH_DogRig rig, float pawPad)
        {
            float y = float.MaxValue;
            foreach (var n in new[] { "Bone.014_L", "Bone.014_R", "Bone.016_L", "Bone.016_R" }) { var b = rig.Bone(n); if (b != null && b.position.y < y) y = b.position.y; }
            return y - pawPad;
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

        static void Ground(GameObject dog, KH_DogRig rig, float pawPad)
        {
            var root = rig.AnimationRoot; var tail = rig.SiblingTail;
            var shift = Vector3.up * (0.003f - Mathf.Min(PawFloor(rig, pawPad), BodyMinYNoTail(dog, rig)));
            root.position += shift;
            if (tail != null) tail.position += shift;
            KH_DogRig.ApplyConstraints(dog);
        }

        // Folded hind legs are shorter than the fore legs, so a vertical sink leaves the hind paws in the air.
        // Pitch the whole body nose-up about the fore paws until the hind paws reach the floor (the loaded
        // crouch: rear low, chest up), then re-seat the lowest point.
        static void PlantHind(GameObject dog, KH_DogRig rig, Vector3 axis, float pawPad)
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
            Ground(dog, rig, pawPad);
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
        // IMP-DOG-012: 2 builds all six breeds; 2a builds the one breed in EditorPrefs "KH_Frisbee_Breed" (default Lab).
        public const string OneBreedPref = "KH_Frisbee_Breed";
        public static string OneBreed
        {
            get { var b = EditorPrefs.GetString(OneBreedPref, Breed); return Breeds.Contains(b) ? b : Breed; }
            set { if (!Breeds.Contains(value)) throw new ArgumentException("Unknown breed " + value); EditorPrefs.SetString(OneBreedPref, value); }
        }

        [MenuItem("Tools/KominkaHillside/Frisbee/2 Build Jump Clip")]
        public static void Build() { BuildBreeds(Breeds); }

        [MenuItem("Tools/KominkaHillside/Frisbee/2a Build Jump Clip (One Breed)")]
        public static void BuildOneBreed() { BuildBreeds(new[] { OneBreed }); }

        public static void BuildBreeds(string[] breeds)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            Directory.CreateDirectory(ClipDir); Directory.CreateDirectory(DataDir); Directory.CreateDirectory(Output);
            var scene = EditorSceneManager.NewPreviewScene(); var all = new StringBuilder(); var built = new List<string>();
            try
            {
                foreach (var breed in breeds)
                {
                    var log = new StringBuilder();
                    var spec = SpecFor(breed);
                    {
                        var idle = CloneSourceDog(scene, breed);
                        try
                        {
                            IdleClip(breed).SampleAnimation(idle, 0f); KH_DogRig.ApplyConstraints(idle);
                            var rig = KH_DogRig.Resolve(idle, MouthAnchorName);
                            if (!rig.Ok) throw new InvalidOperationException(breed + ": " + string.Join("; ", rig.problems));
                            log.AppendLine("##### " + breed + " idle " + Proportions(idle, rig));
                        }
                        finally { Object.DestroyImmediate(idle); }
                    }
                    log.AppendLine("spec groundLegs=" + spec.groundLegs + " airLegs=" + spec.airLegs + " trunk=" + spec.trunk + " head=" + spec.head + " twist=" + spec.twist
                        + " crouch=" + spec.crouch + " rise=" + spec.rise + " recover=" + spec.recover + " maxRootRise=" + spec.maxRootRise + " (" + spec.basis + ")");
                    var variants = Variants(breed);
                    for (int i = 0; i < variants.Length; i++) { BuildOne(scene, variants[i], i, log); built.Add(variants[i].ClipPath); }
                    Directory.CreateDirectory(OutputFor(breed));
                    File.WriteAllText(OutputFor(breed) + "/build.txt", log.ToString());
                    all.Append(log);
                }
                File.WriteAllText(Output + "/build.txt", all.ToString());
                Debug.Log("[KH Frisbee] clips built (" + breeds.Length + " breed(s)): " + string.Join(", ", built) + "\n" + all);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void BuildOne(Scene scene, ClipVariant variant, int index, StringBuilder log)
        {
            AnimationClip result = new AnimationClip();
            string breed = variant.Breed;
            try
            {
                var baseClip = IdleClip(breed);
                log.AppendLine("== " + variant.name + " " + breed + " ==");
                result.name = "KH_" + variant.name + "_" + breed; result.frameRate = 30;
                foreach (var key in variant.keys)
                {
                    var dog = CloneSourceDog(scene, breed);
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
                        // IMP-DOG-012: pawGap = paw floor - lowest non-tail vertex. On a ground key > ~0.01 means the dog sits on
                        // its belly / chest with the paws in the air (the legs are folded too far for this breed).
                        float noTail = BodyMinYNoTail(dog, rig), paws = PawFloor(rig, key.pawPad);
                        log.AppendLine(key.name + " t=" + key.time.ToString("F2") + " mouth=" + mouth.ToString("F3") + " minY=" + BodyBounds(dog).min.y.ToString("F3")
                            + " minYNoTail=" + noTail.ToString("F3") + " pawFloor=" + paws.ToString("F3") + (key.ground ? " pawGap=" + (paws - noTail).ToString("F3") : "") + " " + Limbs(rig));
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
                Vector3 mouthCatch = MouthAt(scene, clip, variant.CatchAt, breed), mouthStand = MouthAt(scene, baseClip, 0f, breed);
                File.WriteAllText(variant.FactsPath, "{\n  \"name\": \"" + variant.name + "\",\n  \"breed\": \"" + breed + "\",\n  \"mouthOffsetCatch\": " + V(mouthCatch) + ",\n  \"mouthOffsetStand\": " + V(mouthStand)
                    + ",\n  \"crouch\": " + F(variant.crouch) + ",\n  \"rise\": " + F(variant.rise) + ",\n  \"recover\": " + F(variant.recover) + ",\n  \"maxRootRise\": " + F(SpecFor(breed).maxRootRise) + ",\n  \"clipLength\": " + F(clip.length) + "\n}\n");
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

        static Vector3 MouthAt(Scene scene, AnimationClip clip, float time, string breed)
        {
            var dog = CloneSourceDog(scene, breed);
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
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(variant.ControllerPath);
            if (ac == null) throw new InvalidOperationException("Missing " + variant.ControllerPath);
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

        // ---------------------------------------------------------------- verify (IMP-DOG-012)
        [Serializable]
        class FactsFile
        {
            public string name, breed;
            public float[] mouthOffsetCatch, mouthOffsetStand;
            public float crouch, rise, recover, clipLength, maxRootRise;
        }

        // Read-only: 6 breeds x every variant = 12 clips + 12 facts, each facts file naming the breed and variant of its
        // own file name, timings equal to the breed table, and the breed's controller playing that clip.
        [MenuItem("Tools/KominkaHillside/Frisbee/2b Verify Clips")]
        public static void VerifyClips()
        {
            var log = new StringBuilder(); int fails = 0, checks = 0, clips = 0, facts = 0;
            void Check(bool ok, string what) { checks++; if (!ok) fails++; log.AppendLine((ok ? "PASS " : "FAIL ") + what); }
            foreach (var breed in Breeds)
            {
                var spec = SpecFor(breed);
                var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPathFor(breed));
                Check(ac != null, breed + ": controller " + ControllerPathFor(breed));
                foreach (var v in Variants(breed))
                {
                    string tag = breed + "/" + v.name;
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(v.ClipPath);
                    Check(clip != null, tag + ": clip exists " + v.ClipPath);
                    if (clip != null)
                    {
                        clips++;
                        Check(clip.name == "KH_" + v.name + "_" + breed, tag + ": clip name " + clip.name);
                        Check(!AnimationUtility.GetAnimationClipSettings(clip).loopTime, tag + ": clip does not loop");
                        Check(Mathf.Abs(clip.length - v.EndAt) < 0.02f, tag + ": clip length " + clip.length.ToString("F3") + " == " + v.EndAt.ToString("F3"));
                        Check(!AnimationUtility.GetCurveBindings(clip).Any(b => b.path.Length == 0), tag + ": no root curves (root motion stays with KH_FrisbeeCatch)");
                    }
                    FactsFile f = null;
                    if (File.Exists(v.FactsPath)) { try { f = JsonUtility.FromJson<FactsFile>(File.ReadAllText(v.FactsPath)); } catch (Exception e) { log.AppendLine("  parse error " + e.Message); } }
                    Check(f != null, tag + ": facts exist and parse " + v.FactsPath);
                    if (f != null)
                    {
                        facts++;
                        Check(f.breed == breed, tag + ": facts breed '" + f.breed + "' == file name breed");
                        Check(f.name == v.name, tag + ": facts name '" + f.name + "' == file name variant");
                        Check(Mathf.Abs(f.crouch - spec.crouch) < 1e-3f && Mathf.Abs(f.rise - spec.rise) < 1e-3f && Mathf.Abs(f.recover - spec.recover) < 1e-3f,
                            tag + ": facts timing " + f.crouch + "/" + f.rise + "/" + f.recover + " == table " + spec.crouch + "/" + spec.rise + "/" + spec.recover);
                        Check(clip != null && Mathf.Abs(f.clipLength - clip.length) < 1e-3f, tag + ": facts clipLength " + f.clipLength + " == clip");
                        Check(Mathf.Abs(f.maxRootRise - spec.maxRootRise) < 1e-3f && f.maxRootRise >= MinMaxRootRise && f.maxRootRise <= LabMaxRootRise,
                            tag + ": facts maxRootRise " + f.maxRootRise + " == 0.9 x standMouthY / " + LabStandMouthY + " in [" + MinMaxRootRise + ", " + LabMaxRootRise + "] = " + spec.maxRootRise.ToString("F4"));
                        Check(breed != Breed || f.maxRootRise == LabMaxRootRise, tag + ": Lab keeps the PCVR-approved maxRootRise " + LabMaxRootRise);
                        Check(f.mouthOffsetStand != null && f.mouthOffsetStand.Length == 3 && Mathf.Abs(f.mouthOffsetStand[1] - spec.standMouthY) < 0.005f,
                            tag + ": facts mouthOffsetStand.y == table standMouthY " + spec.standMouthY + " (update the table if the model changed)");
                        Check(f.mouthOffsetCatch != null && f.mouthOffsetCatch.Length == 3 && f.mouthOffsetCatch[1] > 0.05f, tag + ": mouthOffsetCatch set");
                        Check(f.mouthOffsetStand != null && f.mouthOffsetStand.Length == 3 && f.mouthOffsetStand[1] > 0.05f, tag + ": mouthOffsetStand set");
                        if (f.mouthOffsetCatch != null && f.mouthOffsetCatch.Length == 3 && f.mouthOffsetStand != null && f.mouthOffsetStand.Length == 3)
                            log.AppendLine("  " + tag + " mouthOffsetCatch=(" + string.Join(", ", f.mouthOffsetCatch.Select(x => x.ToString("F3"))) + ") stand=(" + string.Join(", ", f.mouthOffsetStand.Select(x => x.ToString("F3"))) + ")");
                    }
                    if (ac != null)
                    {
                        Check(ac.parameters.Any(p => p.name == v.boolParam && p.type == AnimatorControllerParameterType.Bool), tag + ": controller Bool " + v.boolParam);
                        var st = ac.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == v.boolParam);
                        Check(st != null && clip != null && st.motion == clip, tag + ": controller state " + v.boolParam + " plays " + v.ClipPath);
                    }
                }
            }
            int expected = Breeds.Length * Variants(Breed).Length;
            Check(clips == expected, "clips " + clips + " / " + expected);
            Check(facts == expected, "facts " + facts + " / " + expected);
            string text = "[KH Frisbee] Verify Clips: " + (checks - fails) + "/" + checks + " PASS, " + fails + " FAIL\n" + log;
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/verify-clips.txt", text);
            if (fails > 0) Debug.LogError(text); else Debug.Log(text);
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
        // IMP-DOG-012: 3 renders all six breeds into Temp/KH_Frisbee/<Breed>/ (sheet-side.png, sheet-quarter.png,
        // sheet-side-Twist.png, ...); 3a renders the one breed of 2a. Each breed is framed on its own idle bounds.
        [MenuItem("Tools/KominkaHillside/Frisbee/3 Render Stages")]
        public static void Render() { RenderBreeds(Breeds); }

        [MenuItem("Tools/KominkaHillside/Frisbee/3a Render Stages (One Breed)")]
        public static void RenderOneBreed() { RenderBreeds(new[] { OneBreed }); }

        public static void RenderBreeds(string[] breeds)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            foreach (var breed in breeds)
                foreach (var v in Variants(breed))
                    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(v.ClipPath) == null) throw new InvalidOperationException("Build the clips first: " + v.ClipPath);
            var scene = EditorSceneManager.NewPreviewScene();
            var all = new StringBuilder();
            try
            {
                foreach (var breed in breeds)
                {
                    var log = new StringBuilder();
                    string dir = OutputFor(breed); Directory.CreateDirectory(dir);
                    Bounds frame;
                    {
                        var idle = CloneSourceDog(scene, breed);
                        try { IdleClip(breed).SampleAnimation(idle, 0f); KH_DogRig.ApplyConstraints(idle); frame = BodyBounds(idle); }
                        finally { Object.DestroyImmediate(idle); }
                    }
                    var center = frame.center; center.y = frame.size.y * 0.55f;
                    float ortho = Mathf.Max(frame.size.x, frame.size.y, frame.size.z) * 0.72f;
                    log.AppendLine("##### " + breed + " idle height=" + frame.max.y.ToString("F3") + " -> " + dir);
                    foreach (var v in Variants(breed)) RenderVariant(scene, v, center, ortho, dir, log);
                    File.WriteAllText(dir + "/render.txt", log.ToString());
                    all.Append(log);
                }
                File.WriteAllText(Output + "/render.txt", all.ToString());
                Debug.Log("[KH Frisbee] rendered " + breeds.Length + " breed(s) to " + Output + "/<Breed>/\n" + all);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        // The jump keeps the file names the stage-2 record links (sheet-side.png / sheet-quarter.png);
        // every other variant gets its name appended.
        static string Suffix(ClipVariant v) { return v.name == "Jump" ? "" : "-" + v.name; }

        // Planner root-rise range (docs/plans/2026-09-20-frisbee-prototype.md): the disc heights one clip reaches are
        // mouthOffsetCatch.y + [MinRootRise, BreedSpec.maxRootRise]. Only for the log; KH_FrisbeeCatch owns the real values.
        const float MinRootRise = 0.19f;

        static void RenderVariant(Scene scene, ClipVariant variant, Vector3 center, float ortho, string dir, StringBuilder log)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(variant.ClipPath);
            string breed = variant.Breed;
            var keys = variant.keys;
            string sfx = Suffix(variant);
            const int W = 420, H = 360;
            var side = new Texture2D(W * keys.Length, H, TextureFormat.RGB24, false);
            var quarter = new Texture2D(W * keys.Length, H, TextureFormat.RGB24, false);
            try
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    var (s, q) = RenderOne(scene, breed, clip, keys[i].time, keys[i].name, center, ortho, W, H, keys[i].twist);
                    side.SetPixels(i * W, 0, W, H, s.GetPixels()); quarter.SetPixels(i * W, 0, W, H, q.GetPixels());
                    File.WriteAllBytes(dir + "/" + i + "-" + keys[i].name + "-side" + sfx + ".png", s.EncodeToPNG());
                    Object.DestroyImmediate(s); Object.DestroyImmediate(q);
                }
                side.Apply(); quarter.Apply();
                File.WriteAllBytes(dir + "/sheet-side" + sfx + ".png", side.EncodeToPNG());
                File.WriteAllBytes(dir + "/sheet-quarter" + sfx + ".png", quarter.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(side); Object.DestroyImmediate(quarter); }
            var mouthCatch = MouthAt(scene, clip, variant.CatchAt, breed);
            // Numbers for judging the sheets: ground keys must sit on the paws (pawGap ~0, minYNoTail ~0.003); air keys
            // are drawn with the root at idle height, so the lowest point at the smallest planned rise is airMin + 0.19.
            var stages = new StringBuilder(); float airMin = float.MaxValue, groundMin = float.MaxValue, worstGap = 0f;
            foreach (var k in keys)
            {
                var dog = CloneSourceDog(scene, breed);
                try
                {
                    clip.SampleAnimation(dog, k.time); KH_DogRig.ApplyConstraints(dog);
                    var rig = KH_DogRig.Resolve(dog, MouthAnchorName);
                    float noTail = BodyMinYNoTail(dog, rig), paws = PawFloor(rig, k.pawPad);
                    if (k.ground) { groundMin = Mathf.Min(groundMin, noTail); worstGap = Mathf.Max(worstGap, paws - noTail); }
                    else airMin = Mathf.Min(airMin, noTail);
                    stages.Append(" " + k.name + "=" + noTail.ToString("F3") + (k.ground ? "/gap" + (paws - noTail).ToString("F3") : ""));
                }
                finally { Object.DestroyImmediate(dog); }
            }
            log.AppendLine(breed + " " + variant.name + ": " + keys.Length + " stages, sheet-side" + sfx + ".png / sheet-quarter" + sfx + ".png"
                + " mouthOffsetCatch=" + mouthCatch.ToString("F3")
                + " discReach=" + (mouthCatch.y + MinRootRise).ToString("F2") + ".." + (mouthCatch.y + SpecFor(breed).maxRootRise).ToString("F2") + "m (maxRootRise " + SpecFor(breed).maxRootRise.ToString("F2") + " 仮)"
                + " groundMinY=" + groundMin.ToString("F3") + " worstPawGap=" + worstGap.ToString("F3")
                + " airMinY=" + airMin.ToString("F3") + " airClearanceAtMinRise=" + (airMin + MinRootRise).ToString("F3")
                + " twist=" + string.Join("/", keys.Select(k => k.twist.ToString("F0"))));
            log.AppendLine("  minYNoTail per key:" + stages);
        }

        // untwist = the key's yaw: the cameras stay in the run frame instead of turning with the dog, so a
        // twist key shows the body turned toward the lens. Zero for every jump key, so the jump sheets are
        // the same views as stage 2.
        static (Texture2D side, Texture2D quarter) RenderOne(Scene scene, string breed, AnimationClip clip, float time, string label, Vector3 center, float ortho, int w, int h, float untwist = 0f)
        {
            var owned = new List<Object>();
            T Own<T>(T v) where T : Object { owned.Add(v); return v; }
            try
            {
                var dog = Own(CloneSourceDog(scene, breed));
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
