using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

namespace KominkaHillside.Editor
{
    // Trimmed port of KominkaDogRun KD_DogRig / KD_DogPreviewEditor.ApplyConstraints (IMP-DOG-005 clip
    // authoring). Resolves the purchased two-tier skeleton by measurement: clips animate the driver
    // bones under inu/アーマチュア; RotationConstraints copy them onto the mesh-side bones. Bone names are
    // the same across the six breeds (Bone = pelvis root, 001 lumbar, 002 chest, 003 neck, 004 head,
    // 006 jaw, 007 nose, 005/010/015/016 hind hip/knee/hock/paw, 011/012/013/014 fore shoulder/elbow/wrist/paw).
    public class KH_DogRig
    {
        public GameObject dog;
        public Transform inu, meshSkelRoot, srcSkelRoot, head, nose, mouthAnchor;
        public readonly List<string> problems = new List<string>();
        public bool Ok { get { return problems.Count == 0; } }
        public bool TwoTier { get { return meshSkelRoot != null; } }
        public Transform BoneRoot { get { return meshSkelRoot != null ? meshSkelRoot : srcSkelRoot; } }

        public static KH_DogRig Resolve(GameObject dog, string mouthAnchorName)
        {
            var rig = new KH_DogRig { dog = dog };
            rig.inu = FindDescendant(dog.transform, "inu");
            if (rig.inu == null) { rig.problems.Add("'inu' not found"); return rig; }
            foreach (Transform c in rig.inu) if (c.name == "アーマチュア") { rig.srcSkelRoot = c; break; }
            if (rig.srcSkelRoot == null) { rig.problems.Add("inu/アーマチュア not found"); return rig; }
            int candidates = 0;
            foreach (Transform c in rig.inu)
            {
                if (c == rig.srcSkelRoot || c.Find("Bone") == null) continue;
                candidates++; if (rig.meshSkelRoot == null) rig.meshSkelRoot = c;
            }
            if (candidates > 1) rig.problems.Add("more than one mesh skeleton under inu");
            rig.head = rig.Bone("Bone.004"); rig.nose = rig.Bone("Bone.007");
            if (rig.head == null) rig.problems.Add("head Bone.004 missing");
            if (rig.nose == null) rig.problems.Add("nose Bone.007 missing");
            rig.mouthAnchor = FindDescendant(dog.transform, mouthAnchorName);
            if (rig.mouthAnchor == null) rig.problems.Add(mouthAnchorName + " missing");
            return rig;
        }

        public Transform Bone(string name)
        {
            if (BoneRoot == null) return null;
            foreach (var t in BoneRoot.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        // Where additive rotation is written: the constraint source for a two-tier rig, the bone itself otherwise.
        public Transform Driver(string name)
        {
            var mesh = Bone(name);
            if (mesh == null) throw new System.InvalidOperationException("Missing bone " + name);
            if (!TwoTier) return mesh;
            var rc = mesh.GetComponent<RotationConstraint>();
            if (rc == null || rc.sourceCount != 1) throw new System.InvalidOperationException(name + ": no single-source RotationConstraint");
            var src = rc.GetSource(0).sourceTransform;
            if (src == null || !IsDescendantOf(src, srcSkelRoot)) throw new System.InvalidOperationException(name + ": constraint source is not under the armature");
            return src;
        }

        public Transform AnimationRoot { get { return srcSkelRoot.Find("Bone"); } }
        public Transform SiblingTail { get { return srcSkelRoot.Find("Bone.018"); } }

        // Same full-weight single-source constraints the two-tier rigs use; the Editor does not evaluate them.
        public static void ApplyConstraints(GameObject clone)
        {
            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
            {
                var pc = t.GetComponent<PositionConstraint>();
                if (pc != null && pc.constraintActive && pc.weight > .99f && pc.sourceCount > 0 && pc.GetSource(0).weight > .99f)
                {
                    if (pc.sourceCount != 1 || pc.translationAxis != (Axis.X | Axis.Y | Axis.Z)) throw new System.InvalidOperationException("Unsupported position constraint");
                    t.position = pc.GetSource(0).sourceTransform.position + (t.parent == null ? pc.translationOffset : t.parent.TransformVector(pc.translationOffset));
                }
                var rc = t.GetComponent<RotationConstraint>();
                if (rc != null && rc.constraintActive && rc.weight > .99f && rc.sourceCount > 0 && rc.GetSource(0).weight > .99f)
                {
                    if (rc.sourceCount != 1 || rc.rotationAxis != (Axis.X | Axis.Y | Axis.Z)) throw new System.InvalidOperationException("Unsupported rotation constraint");
                    t.rotation = rc.GetSource(0).sourceTransform.rotation * Quaternion.Euler(rc.rotationOffset);
                }
            }
        }

        public static Transform FindDescendant(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        public static bool IsDescendantOf(Transform t, Transform ancestor)
        {
            for (var u = t == null ? null : t.parent; u != null; u = u.parent) if (u == ancestor) return true;
            return false;
        }
    }
}
