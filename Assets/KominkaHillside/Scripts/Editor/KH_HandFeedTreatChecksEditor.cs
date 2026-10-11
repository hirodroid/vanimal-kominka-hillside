using System;
using System.IO;
using System.Reflection;
using System.Text;
using KominkaDogRun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // IMP-DOG-041: real receipt/query/display methods in a disposable preview scene.
    // No Play, no mutation or save of the production scene, no mocked copy of the rules.
    public static class KH_HandFeedTreatChecksEditor
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Set(object o, string name, object value) { o.GetType().GetField(name, Private).SetValue(o, value); }
        static void Apply(KD_TreatController body) { typeof(KD_TreatController).GetMethod("ApplySlot", Private).Invoke(body, new object[] { false }); }

        [MenuItem("Tools/KominkaHillside/Menu/6e Verify Hand Feeding")]
        public static void Verify()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode required");
            Scene production = SceneManager.GetActiveScene();
            bool wasDirty = production.isDirty;
            var log = new StringBuilder("IMP-DOG-041 hand feeding Edit checks\n");
            int passed = 0, failed = 0;
            Action<bool, string> check = (ok, name) => { if (ok) passed++; else failed++; log.AppendLine((ok ? "PASS " : "FAIL ") + name); };
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("HandFeedProbe");
                SceneManager.MoveGameObjectToScene(go, preview);
                var box = go.AddComponent<KD_TreatBox>();
                var slot = go.AddComponent<KD_TreatSlot>();
                var body = go.AddComponent<KD_TreatController>();
                var collider = go.AddComponent<SphereCollider>();
                var mesh = new GameObject("TreatMesh");
                SceneManager.MoveGameObjectToScene(mesh, preview);
                mesh.transform.SetParent(go.transform);
                slot.body = body; slot.box = box; body.slot = slot; body.slotIndex = 0;
                body.productKeys = new[] { "20" }; body.meshNodes = new[] { mesh };
                Set(body, "_collider", collider);
                Set(slot, "giverId", 7); Set(slot, "product", "20"); Set(slot, "outMs", 100);
                Set(box, "dogSlot", new[] { 0 }); Set(box, "dogSlotOutMs", new[] { 100 });
                Set(box, "dogEatMs", new[] { 0 }); Set(box, "dogHandFed", new[] { false });
                Set(box, "dogBitePos", new[] { new Vector3(1, 2, 3) });

                check(box.HandFeedReachWith(Vector3.zero, new Vector3(.219f, 0, 0)), "near mouth accepted");
                check(!box.HandFeedReachWith(Vector3.zero, new Vector3(.221f, 0, 0)), "far mouth rejected");
                check(!box.HandFeedReachWith(Vector3.zero, new Vector3(0, .221f, 0)), "high hand rejected despite same horizontal point");
                check(!box.HandFeedReachWith(Vector3.zero, new Vector3(.16f, .16f, 0)), "diagonal outside sphere rejected");
                check(box.HandFeedReachWith(new Vector3(30, 6, 50), new Vector3(30, 6.2f, 50)), "reach independent of world origin and slope height");

                Apply(body);
                check(mesh.activeSelf && collider.enabled, "unconsumed offer visible");
                check(!box.HandFedFor(0, 100), "no receipt before eating");
                Set(box, "dogHandFed", new[] { true });
                check(!box.HandFedFor(0, 100), "kind without eat start is not consumed");
                Set(box, "dogEatMs", new[] { 1 });
                check(box.HandFedFor(0, 100), "receipt remains independent of elapsed eat duration");
                check(!box.HandFedFor(0, 101) && !box.HandFedFor(1, 100), "wrong slot or generation cannot consume");
                check(box.BitePositionOf(0) == new Vector3(1, 2, 3), "bite point preserved independently of hand/body position");
                Apply(body);
                check(!mesh.activeSelf && !collider.enabled, "receipt hides mesh and collider within same Out state");
                Apply(body);
                check(!mesh.activeSelf, "repeated application keeps receipt hidden");
                var allow = new int[1];
                box.BuildAllowWith(new[] { 9 }, new[] { 7 }, new[] { KD_TreatBox.SlotOut }, new[] { 1 },
                    new[] { -1 }, new[] { 0 }, 1, 1, allow);
                check(allow[0] == KD_TreatBox.AllowNone, "other player leash excludes an unreceived offer");
                box.KeepHandReceipts(new[] { KD_TreatBox.SlotOut }, new[] { 100 }, allow, 1, 1);
                var assigned = new int[1]; var assignedGen = new int[1];
                box.ResolveClaimsAllowWith(new[] { 0 }, new[] { 100 }, new[] { KD_TreatBox.SlotOut }, new[] { 100 },
                    new[] { 1 }, new[] { 500 }, allow, 1, 1, assigned, assignedGen);
                check(assigned[0] == 0 && assignedGen[0] == 100 && box.HandFedFor(0, 100),
                    "received hand treat survives another player's leash until slot completion");
                allow[0] = KD_TreatBox.AllowNone;
                box.KeepHandReceipts(new[] { KD_TreatBox.SlotOut }, new[] { 101 }, allow, 1, 1);
                check(allow[0] == KD_TreatBox.AllowNone, "receipt never protects a reused generation");
                // Same giver/product, no intervening Free observed. An old Box snapshot must not hide a new slot.
                Set(slot, "outMs", 101);
                Apply(body);
                check(mesh.activeSelf && collider.enabled, "next generation restores same giver/product without observing Free");
                Set(box, "dogSlotOutMs", new[] { 101 }); Set(box, "dogHandFed", new[] { false });
                Apply(body);
                check(mesh.activeSelf, "floor eating keeps existing visible treat");
                Set(slot, "giverId", -1); Set(slot, "outMs", 0);
                Apply(body);
                check(!mesh.activeSelf, "put away hides treat");
                check(!box.HandFedOf(-1) && !box.HandFedOf(6), "missing dog rejected");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }

            int dogs = 0;
            foreach (var w in UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>())
            {
                if (w.gameObject.scene != production) continue;
                dogs++;
                Transform mouth = w.fetchController != null ? w.fetchController.mouthAnchor : null;
                check(w.treatBox != null && mouth != null, w.name + " treatBox and mouth wired");
                if (mouth != null && w.dogRoot != null)
                    log.AppendLine("  mouth offset world m: " + (mouth.position - w.dogRoot.position).ToString("F3"));
            }
            check(dogs == 6, "six production dogs");
            check(production.isDirty == wasDirty, "production dirty unchanged");
            log.AppendLine("RESULT " + passed + " passed / " + failed + " failed");
            Directory.CreateDirectory("Logs/KominkaHillside");
            File.WriteAllText("Logs/KominkaHillside/verify-hand-feeding.txt", log.ToString());
            if (failed > 0) throw new InvalidOperationException(log.ToString());
            Debug.Log(log.ToString());
        }
    }
}
