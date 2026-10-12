using System;
using System.IO;
using System.Linq;
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
            var log = new StringBuilder("IMP-DOG-041 hand feeding + IMP-DOG-042 called dog Edit checks\n");
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

                CalledDogChecks(box, slot, preview, check, log);
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
                // IMP-DOG-042: the called-dog beat reads the call (sleepBoard slot), the gaze writer and the treat box
                check(w.sleepBoard != null && w.sleepBoard.SlotOf(w.dogIndex) != null && w.petController != null,
                    w.name + " called-dog treat wiring (sleepBoard slot, petController)");
            }
            // IMP-DOG-042: the pose panel drops the chin rest / paw while CalledTreatActive; it reads its own dogs[] by index
            var poses = UnityEngine.Object.FindObjectsOfType<KD_CallPose>(true).Where(p => p.gameObject.scene == production).ToArray();
            check(poses.Length == 1 && poses[0].dogs != null && poses[0].dogs.Length == 6 && poses[0].dogs.All(x => x != null),
                "KD_CallPose wired to six dogs (chin rest / paw yield to the called-dog treat)");
            check(dogs == 6, "six production dogs");
            check(production.isDirty == wasDirty, "production dirty unchanged");
            log.AppendLine("RESULT " + passed + " passed / " + failed + " failed");
            Directory.CreateDirectory("Logs/KominkaHillside");
            File.WriteAllText("Logs/KominkaHillside/verify-hand-feeding.txt", log.ToString());
            if (failed > 0) throw new InvalidOperationException(log.ToString());
            Debug.Log(log.ToString());
        }

        static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, Private).GetValue(o); }

        // IMP-DOG-042: the called dog's acceptance rule (KD_TreatBox.CalledFeedAllowWith, the real method) and the dog-side gates,
        // on the same disposable preview scene. Inputs are the table the master builds in Reconcile; nothing is mocked inside the rule.
        static void CalledDogChecks(KD_TreatBox box, KD_TreatSlot slot, Scene preview, Action<bool, string> check, StringBuilder log)
        {
            const int Out = KD_TreatBox.SlotOut, None = KD_TreatBox.AllowNone, Keep = KD_TreatBox.AllowKeep, New = KD_TreatBox.AllowNew;
            // dogs: 0 = called by 7 and waiting (ふせ / おすわり), 1 = free dog that claimed slot 0 first, 2 = called by 7 but not waiting
            // (walking there / lying pose / stretching), 3 = called by 8 and waiting. slots: 0 = 7's treat held at dog 0's mouth,
            // 1 = 8's treat held at dog 0's and dog 3's mouth, 2 = 7's treat on the ground at dog 0's mouth
            const int D = 4, S = 3;
            int[] caller = { 7, -1, 7, 8 }, ready = { 1, 0, 0, 1 }, giver = { 7, 8, 7 }, state = { Out, Out, Out }, held = { 1, 1, 0 };
            int[] gens = { 100, 200, 300 }, prev = { -1, 0, -1, -1 }, prevGen = { 0, 100, 0, 0 };
            var reach = new int[D * S];
            reach[0 * S + 0] = 2; reach[0 * S + 1] = 2; reach[0 * S + 2] = 2; reach[2 * S + 0] = 2; reach[3 * S + 1] = 2;
            Func<int[]> fresh = () => Enumerable.Repeat(New, D * S).ToArray();   // unleashed dogs, as BuildAllowWith leaves them
            var allow = fresh();
            box.CalledFeedAllowWith(caller, ready, new int[D], giver, state, held, reach, prev, prevGen, gens, D, S, allow);
            check(allow[0 * S + 0] == New, "042 caller's held treat at the called dog's mouth is accepted");
            check(allow[0 * S + 1] == None, "042 another player's hand at the called dog's mouth is ignored");
            check(allow[0 * S + 2] == None, "042 ground treat at the called dog's mouth is ignored (no walking to food)");
            check(allow[2 * S + 0] == None && allow[2 * S + 1] == None && allow[2 * S + 2] == None,
                "042 a called dog not waiting in an awake pose takes nothing (walking there / lying / stretching)");
            check(allow[1 * S + 0] == None && allow[1 * S + 2] == New, "042 offered slot taken from the free dog that had not started eating; its other slots stay");
            check(allow[3 * S + 1] == New && allow[1 * S + 1] == None, "042 each caller's offer goes to that caller's dog only");
            var outSlot = new int[D]; var outGen = new int[D];
            box.ResolveClaimsAllowWith(prev, prevGen, state, gens, new[] { 1, 1, 0, 1 }, Enumerable.Repeat(500, D * S).ToArray(), allow, D, S, outSlot, outGen);
            check(outSlot[0] == 0 && outGen[0] == 100 && outSlot[3] == 1 && outSlot[1] == 2 && outSlot[2] == -1,
                "042 resolve: called dogs get the offers at their mouths, the free dog moves to the remaining slot (" + string.Join(",", outSlot) + ")");

            // keep band (hand wobbling between HandFeedReach and CalledFeedKeep) and release beyond it
            reach = new int[D * S]; reach[0 * S + 0] = 1;
            allow = fresh();
            box.CalledFeedAllowWith(caller, ready, new int[D], giver, state, held, reach, new[] { 0, -1, -1, -1 }, new[] { 100, 0, 0, 0 }, gens, D, S, allow);
            check(allow[0 * S + 0] == Keep && allow[1 * S + 0] == None, "042 own claim kept inside the keep band, still not given to others");
            allow = fresh();
            box.CalledFeedAllowWith(caller, ready, new int[D], giver, state, held, reach, new[] { -1, -1, -1, -1 }, new int[D], gens, D, S, allow);
            check(allow[0 * S + 0] == None && allow[1 * S + 0] == New, "042 keep band never starts a claim; the slot stays open to free dogs");
            reach = new int[D * S];
            allow = fresh();
            box.CalledFeedAllowWith(caller, ready, new int[D], giver, state, held, reach, new[] { 0, -1, -1, -1 }, new[] { 100, 0, 0, 0 }, gens, D, S, allow);
            check(allow[0 * S + 0] == None, "042 hand pulled beyond the keep band releases the claim (far / high treat)");
            check(KD_TreatBox.CalledFeedKeep > KD_TreatBox.HandFeedReach, "042 keep band is wider than the bite reach");

            // a dog that has started eating keeps its row (RC-6: no un-eating), even when its slot is now offered to a called dog
            reach = new int[D * S]; reach[0 * S + 0] = 2;
            allow = fresh();
            box.CalledFeedAllowWith(caller, ready, new[] { 0, 1, 0, 0 }, giver, state, held, reach, prev, prevGen, gens, D, S, allow);
            check(allow[1 * S + 0] == New, "042 a free dog already eating keeps its row");
            allow = fresh();
            box.CalledFeedAllowWith(caller, ready, new[] { 1, 0, 0, 0 }, giver, state, new[] { 0, 0, 0 }, new int[D * S], prev, prevGen, gens, D, S, allow);
            check(allow[0 * S + 0] == New && allow[0 * S + 2] == New, "042 a called dog already eating keeps its row (receipt kept by KeepHandReceipts)");
            allow = fresh();
            box.CalledFeedAllowWith(new[] { -1, -1, -1, -1 }, new int[D], new int[D], giver, state, held, reach, prev, prevGen, gens, D, S, allow);
            check(allow.All(a => a == New), "042 nobody called: the table is untouched (same answer as before 042)");

            // ---- dog side: which poses wait for a treat, and the beat never moves the dog ----
            var dogGo = new GameObject("CalledDogProbe");
            SceneManager.MoveGameObjectToScene(dogGo, preview);
            var dog = dogGo.AddComponent<KD_DogWanderController>();
            var board = dogGo.AddComponent<KD_SleepBoard>();
            var seat = dogGo.AddComponent<KD_SleepSlot>();
            board.slots = new[] { seat };
            seat.dogIndex = 0;
            dog.dogIndex = 0; dog.sleepBoard = board; dog.treatBox = box; dog.dogRoot = dogGo.transform;
            Set(dog, "_sleepCalled", true); Set(dog, "_wantSleep", false);
            Set(seat, "poseId", 2);
            check(!dog.CalledFeedReady, "042 called but not arrived (still walking) does not wait for a treat");
            Set(dog, "_wantSleep", true);
            foreach (int pose in new[] { 0, 3, 4 })
            {
                Set(seat, "poseId", pose);
                check(!dog.CalledFeedReady, "042 lying pose " + pose + " (IsAsleep: ごろん / うずくまる / へそ天) takes no treat");
            }
            foreach (int pose in new[] { 1, 2 })
            {
                Set(seat, "poseId", pose);
                // IsStretching reads the server clock; in Edit it may be unavailable. Then the awake case is left to the harness (97f)
                try { check(dog.CalledFeedReady, "042 awake pose " + pose + " (ふせ / おすわり) waits for a treat at the mouth"); }
                catch (Exception e) { log.AppendLine("NOTE 042 awake pose " + pose + " not evaluable in Edit (" + e.GetType().Name + "); Course/97f covers it"); }
            }
            // placed spot (場所を決めて呼ぶ): IsCompanionReady is raised before the lift, so a dog rising / turning onto the spot takes nothing
            // (review 2026-10-12). Every refusal below returns before the stretch check (no server clock needed)
            var spot = new Vector3(3f, 1.5f, 4f);
            Set(seat, "poseId", 2); Set(seat, "hasSpot", true); Set(seat, "spot", spot); Set(seat, "spotYaw", 37f);
            Set(dog, "_placedSleepActive", true);
            dogGo.transform.SetPositionAndRotation(spot - Vector3.up * .5f, Quaternion.Euler(0, 37, 0));
            check(!dog.CalledFeedReady, "042 placed spot: still lifting (0.5 m below the spot) takes no treat");
            dogGo.transform.SetPositionAndRotation(spot - Vector3.up * .09f, Quaternion.Euler(0, 37, 0));
            check(!dog.CalledFeedReady, "042 placed spot: just outside the 3D arrival tolerance (0.09 m) takes no treat");
            dogGo.transform.SetPositionAndRotation(spot, Quaternion.Euler(0, 37 + 30, 0));
            check(!dog.CalledFeedReady, "042 placed spot: still turning (30 deg off the spot yaw) takes no treat");
            Set(dog, "_dogOwner", true); Set(dog, "_placedSettling", true);
            dogGo.transform.SetPositionAndRotation(spot - Vector3.up * .02f, Quaternion.Euler(0, 37, 0));
            check(!dog.CalledFeedReady, "042 placed spot: the Owner's lift not finished (_placedSettling) takes no treat");
            Set(dog, "_placedSettling", false);
            Set(seat, "hasSpot", false);
            check(!dog.CalledFeedReady, "042 placed flag without a synced spot (release in flight) takes no treat");
            Set(seat, "hasSpot", true);
            dogGo.transform.SetPositionAndRotation(spot, Quaternion.Euler(0, 37, 0));
            try { check(dog.CalledFeedReady, "042 placed spot: settled on the spot and yaw waits for a treat"); }
            catch (Exception e) { log.AppendLine("NOTE 042 settled placed spot not evaluable in Edit (" + e.GetType().Name + "); Course/97f covers the fan spot only"); }
            Set(dog, "_placedSleepActive", false); Set(dog, "_dogOwner", false); Set(seat, "hasSpot", false);

            Set(dog, "_ctActive", true); Set(dog, "_sleepCalled", false);
            check(!dog.CalledTreatActive, "042 CalledTreatActive drops with the call (KD_CallPose resumes chin rest / paw)");
            Set(dog, "_sleepCalled", true);
            check(dog.CalledTreatActive, "042 CalledTreatActive while called and in the beat");

            // the beat itself on a lying pose (no server clock): the caller's receipt holds the dog busy, another giver's does not;
            // neither moves nor turns the root (no Coast-turn / FaceTowards / StepToward / _look / _eatPose)
            Set(seat, "poseId", 0);
            box.slots = new[] { slot };   // LiveSlotOf reads the box's slot table (the 041 rows above did not need it)
            Set(slot, "giverId", 7); Set(slot, "product", "20"); Set(slot, "outMs", 100);
            Set(box, "dogSlot", new[] { 0 }); Set(box, "dogSlotOutMs", new[] { 100 });
            Set(box, "dogEatMs", new[] { 1 }); Set(box, "dogHandFed", new[] { true });
            dogGo.transform.SetPositionAndRotation(new Vector3(3, 1, 4), Quaternion.Euler(0, 37, 0));
            MethodInfo beat = typeof(KD_DogWanderController).GetMethod("UpdateCalledTreat", Private);
            Set(dog, "_treatBusy", false); Set(dog, "_ctActive", false);
            beat.Invoke(dog, new object[] { 8 });
            check(!Field<bool>(dog, "_treatBusy") && !Field<bool>(dog, "_ctActive"), "042 another player's treat claim is not this called dog's beat");
            beat.Invoke(dog, new object[] { 7 });
            check(Field<bool>(dog, "_treatBusy") && Field<bool>(dog, "_ctActive"), "042 the caller's received treat holds the called dog in the beat");
            check(dogGo.transform.position == new Vector3(3, 1, 4) && Quaternion.Angle(dogGo.transform.rotation, Quaternion.Euler(0, 37, 0)) < .001f
                && !Field<bool>(dog, "_eatPose") && !Field<bool>(dog, "_look"), "042 the beat never moves / turns the dog or raises the floor / LookAt pose");
        }
    }
}
