using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UdonSharpEditor;

namespace KominkaDogRun.Editor
{
    public static class KD_VrInteractionChecksEditor
    {
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/VR Interaction/4 - Check Reentry")]
        public static void CheckReentry()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Edit mode only");
            var log = new StringBuilder();
            var box = KD_SceneObjects.Find(KD_DogDefs.AccessoryBoxName).GetComponent<KD_AccessoryBox>();
            var nodes = (GameObject[])box.nodes.Clone();
            var poses = new Matrix4x4[nodes.Length];
            for (int i = 0; i < nodes.Length; i++) poses[i] = nodes[i].transform.localToWorldMatrix;
            KD_AccessorySetupEditor.SetupNodes();
            int unchanged = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (box.nodes[i] != nodes[i]) throw new System.InvalidOperationException("Node changed " + i);
                for (int n = 0; n < 16; n++)
                    if (Mathf.Abs(poses[i][n] - nodes[i].transform.localToWorldMatrix[n]) > .00001f)
                        throw new System.InvalidOperationException("Pose changed " + i);
                unchanged++;
            }
            log.AppendLine("Setup retained node identity and world matrix=" + unchanged + "/" + nodes.Length);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("PouchFixture");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                var pouch = go.AddComponent<KD_TreatPouch>();
                var panel = new GameObject("Panel"); panel.transform.SetParent(go.transform);
                pouch.panelRoot = panel;
                var enter = typeof(KD_TreatPouch).GetMethod("EnterState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                for (int i = 0; i < 2; i++)
                {
                    panel.SetActive(true);
                    enter.Invoke(pouch, new object[] { KD_TreatBox.DispOut });
                    if (panel.activeSelf || !pouch.TreatInputBlocked()) throw new System.InvalidOperationException("Dispense did not close/block UI");
                }
                log.AppendLine("PASS repeated successful dispense closes menu and blocks closing-frame USE");
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
            log.AppendLine("RESULT failures=0");
            File.WriteAllText("Temp/vr-interaction-reentry.txt", log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/VR Interaction/2 - Repair")]
        public static void Repair()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Edit mode only");
            var log = new StringBuilder();
            int moved = 0;
            foreach (var def in KD_DogDefs.All)
            {
                var dog = KD_SceneObjects.Find(def.DogName);
                var rig = KD_DogRig.Resolve(dog);
                if (!rig.Ok) throw new System.InvalidOperationException(rig.Summary());
                foreach (var t in dog.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("KD_Acc_")) continue;
                    var slot = SlotOf(t.name);
                    var target = KD_AccessorySetupEditor.AnchorFor(rig, slot);
                    if (target == null) throw new System.InvalidOperationException("No anchor " + t.name);
                    if (t.parent == target) continue;
                    var world = t.localToWorldMatrix;
                    Undo.SetTransformParent(t, target, "Fix accessory bone");
                    for (int i = 0; i < 16; i++)
                        if (Mathf.Abs(world[i] - t.localToWorldMatrix[i]) > .00001f)
                            throw new System.InvalidOperationException("Fit changed " + t.name);
                    EditorUtility.SetDirty(t); moved++;
                    log.AppendLine(t.name + " -> " + target.name + " (world pose preserved)");
                }
            }
            var placement = KD_SceneObjects.Find(KD_SleepPlacementEditor.SystemName).GetComponent<KD_SleepPlacement>();
            KD_SleepPlacementEditor.ConfigureVrInputUI(placement, log);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(placement.gameObject.scene);
            log.AppendLine("reparented=" + moved);
            File.WriteAllText("Temp/vr-interaction-repair.txt", log.ToString());
        }

        // 枠は品目表 (KD_AccessorySetupEditor.Accessories) から引く。ここに写しを持つと品目追加のたびに落ちる (2026-09-14)。
        private static KD_AccessorySetupEditor.Slot SlotOf(string name) { return KD_AccessorySetupEditor.SlotOfObject(name); }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/VR Interaction/3 - Verify")]
        public static void Verify()
        {
            var log = new StringBuilder();
            int count = 0, fail = 0;
            var box = KD_SceneObjects.Find(KD_DogDefs.AccessoryBoxName).GetComponent<KD_AccessoryBox>();
            foreach (var def in KD_DogDefs.All)
            {
                var dog = KD_SceneObjects.Find(def.DogName);
                var rig = KD_DogRig.Resolve(dog);
                int perDog = 0;
                // 台帳 (box.nodes / productSlots) を正本に、犬ごとのノードを照合する。名前の接頭辞は
                // KD_Acc_ (飾り 18) と KD_CandyCostume_ (キャンディ 3。KD_AccessorySetsEditor.BindCandyNodes) の 2 系統。
                foreach (var t in dog.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("KD_Acc_") && !t.name.StartsWith("KD_CandyCostume_")) continue;
                    count++; perDog++;
                    int index = System.Array.IndexOf(box.nodes, t.gameObject);
                    // Verify は Setup の AnchorFor を再利用せず、表示骨・PB・台帳の実物を照合する。
                    var slot = index >= 0 ? (KD_AccessorySetupEditor.Slot)box.productSlots[index / box.dogCount] : SlotOf(t.name);
                    var target = slot == KD_AccessorySetupEditor.Slot.Head || slot == KD_AccessorySetupEditor.Slot.Face ? rig.head : rig.head.parent;
                    if (slot == KD_AccessorySetupEditor.Slot.Tail)
                    {
                        var pb = rig.tailPhysBone;
                        target = pb == null ? null : (pb.rootTransform != null ? pb.rootTransform : pb.transform).parent;
                    }
                    else if (slot == KD_AccessorySetupEditor.Slot.Body) target = rig.Bone("Bone.001", null); // リュック。AnchorFor と同じ骨
                    bool skinned = false;
                    foreach (var skin in dog.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        foreach (var bone in skin.bones) if (bone == t.parent) skinned = true;
                    int refs = 0;
                    foreach (var node in box.nodes) if (node == t.gameObject) refs++;
                    bool ok = target != null && t.parent == target && skinned && refs == 1;
                    foreach (var renderer in t.GetComponentsInChildren<Renderer>(true))
                        if ((GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.BatchingStatic) != 0) ok = false;
                    if (!ok) fail++;
                    log.AppendLine((ok ? "PASS " : "FAIL ") + t.name + " parent=" + t.parent.name + " skin=" + skinned + " refs=" + refs);
                }
                if (perDog != box.productIds.Length) fail++;
            }
            var p = KD_SceneObjects.Find(KD_SleepPlacementEditor.SystemName).GetComponent<KD_SleepPlacement>();
            bool ui = p.pouch != null && p.pouch.sleepPlacement == p && !p.transform.IsChildOf(p.pouch.panelRoot.transform)
                && p.confirmProgress != null && p.confirmProgress.sprite != null
                && p.confirmProgress.type == UnityEngine.UI.Image.Type.Filled && !p.confirmProgress.raycastTarget;
            if (!ui) fail++;
            log.AppendLine("placement independent of menu / circular progress=" + ui);
            int expected = box.productIds.Length * KD_DogDefs.All.Length;
            if (count != expected || box.nodes.Length != expected) fail++;
            log.AppendLine("RESULT count=" + count + "/" + expected + " failures=" + fail);
            File.WriteAllText("Temp/vr-interaction-verify.txt", log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/VR Interaction/1 - Survey")]
        public static void Survey()
        {
            var log = new StringBuilder();
            foreach (var def in KD_DogDefs.All)
            {
                var dog = KD_SceneObjects.Find(def.DogName);
                if (dog == null) { log.AppendLine("MISSING " + def.DogName); continue; }
                var rig = KD_DogRig.Resolve(dog);
                log.AppendLine(rig.Summary());
                foreach (var t in dog.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("KD_Acc_")) continue;
                    log.AppendLine(t.name + " parent=" + AnimationUtility.CalculateTransformPath(t.parent, dog.transform)
                        + " local=" + t.localPosition.ToString("F5") + " scale=" + t.localScale.ToString("F5"));
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                        log.AppendLine("  renderer=" + r.name + " static=" + GameObjectUtility.GetStaticEditorFlags(r.gameObject));
                }
                foreach (var r in dog.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    log.AppendLine("  skin=" + r.name);
                    foreach (var b in r.bones)
                        if (b != null && (b.name == "Bone.004" || b.name == "Bone.003" || b.name == "Bone.018"))
                            log.AppendLine("    bone=" + AnimationUtility.CalculateTransformPath(b, dog.transform));
                }
            }
            foreach (var b in Object.FindObjectsOfType<KD_TreatController>(true))
                log.AppendLine("treat=" + b.name + " slot=" + (b.slot != null) + " pouch=" + (b.slot != null && b.slot.pouch != null));
            foreach (var box in Object.FindObjectsOfType<KD_AccessoryBox>(true))
                foreach (var node in box.nodes)
                    log.AppendLine("boxNode=" + (node == null ? "NULL" : AnimationUtility.CalculateTransformPath(node.transform, null)));
            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (desc != null)
                foreach (var entry in desc.NetworkIDCollection)
                    if (entry.gameObject == null) log.AppendLine("missingNetworkID=" + entry.ID + " types=" + string.Join(",", entry.SerializedTypeNames));
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/vr-interaction-survey.txt", log.ToString());
        }
    }
}
