using System;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;

namespace KominkaDogRun.Editor
{
    // Runs the actual C# contact state machine in a disposable preview scene.
    // Does not simulate the Udon VM, networking, or visible head motion.
    public static class KD_PetSamplingVerifyEditor
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static object Invoke(KD_DogPetController pet, string method, params object[] args)
            => typeof(KD_DogPetController).GetMethod(method, Private).Invoke(pet, args);
        static T Read<T>(KD_DogPetController pet, string name)
            => (T)typeof(KD_DogPetController).GetField(name, Private).GetValue(pet);
        static void Set(KD_DogPetController pet, string name, object value)
            => typeof(KD_DogPetController).GetField(name, Private).SetValue(pet, value);

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet Sampling/Verify Contact Sampling")]
        public static void Verify()
        {
            KD_PlushShelfEditor.Guard();
            var log = new KD_VerifyLog("Pet sampling C# regression (Edit preview scene)");
            var live = KD_ProductsEditor.SceneComponents<KD_DogPetController>();
            log.Check(live.Length == 6, "Six scene dogs use the pet controller");
            foreach (var controller in live)
            {
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
                log.Check(controller.enabled && controller.headAnchor != null && controller.sourceBone != null
                    && controller.dogAnimator != null && controller.dogRoot != null,
                    controller.name + " active controller and required anchors");
                log.Check(backing != null && backing.enabled && AssetDatabase.GetAssetPath(backing.programSource)
                    == "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogPetController.asset",
                    controller.name + " Udon program binding");
                var compiled = backing == null ? null : new SerializedObject(backing)
                    .FindProperty("serializedProgramAsset").objectReferenceValue as VRC.Udon.AbstractSerializedUdonProgramAsset;
                var program = compiled == null ? null : compiled.RetrieveProgram();
                log.Check(program != null && program.SymbolTable.GetSymbols().Contains("_nextContactSampleTime")
                    && program.SymbolTable.GetSymbols().Contains("_previousContactSampleTime")
                    && compiled == backing.programSource.SerializedProgramAsset,
                    controller.name + " serialized Udon program contains sampling scheduler (not only C# proxy)");
            }
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("KD_PetSamplingTest") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                var pet = root.AddUdonSharpComponent<KD_DogPetController>();
                var anchor = new GameObject("KD_TestHead");
                anchor.transform.SetParent(root.transform, false);
                anchor.transform.position = new Vector3(10f, 1f, 10f);
                pet.headAnchor = anchor.transform;
                Set(pet, "_lastStrokeTime", -1000f);
                Vector3 hand = anchor.transform.position;
                Vector3 head = hand + Vector3.up * .8f;
                Vector3 absent = Vector3.zero;

                var constants = BindingFlags.Static | BindingFlags.NonPublic;
                log.Check((float)typeof(KD_DogPetController).GetField("ContactSampleInterval", constants).GetRawConstantValue() == .1f,
                    "Near sampling: 100 ms on every platform");
                log.Check((float)typeof(KD_DogPetController).GetField("FarContactSampleInterval", constants).GetRawConstantValue() == .25f,
                    "Far sampling: 250 ms");
                log.Check(!(bool)Invoke(pet, "IsFarFromContact", head), "Nearby head is not culled");
                log.Check(!(bool)Invoke(pet, "IsFarFromContact", hand + Vector3.right * 4f), "4 m boundary stays near");
                log.Check((bool)Invoke(pet, "IsFarFromContact", hand + Vector3.right * 5f), "5 m head uses far path");
                log.Check(!(bool)Invoke(pet, "IsFarFromContact", Vector3.zero), "Missing head does not enable far culling");
                pet.handValidMaxDistance = 6f;
                log.Check(!(bool)Invoke(pet, "IsFarFromContact", hand + Vector3.right * 5f), "Long valid arm reach stays detectable");
                pet.handValidMaxDistance = 3f;
                var cheek = new GameObject("KD_TestCheek");
                cheek.transform.SetParent(root.transform, false);
                cheek.transform.position = hand + Vector3.right * 6f;
                pet.cheekAnchorR = cheek.transform;
                log.Check(!(bool)Invoke(pet, "IsFarFromContact", cheek.transform.position), "Every contact anchor protects nearby interaction");
                pet.cheekAnchorR = null;

                Invoke(pet, "SampleLocalContact", 10f, head, hand, absent);
                log.Check(Read<bool>(pet, "_handHeld") && !pet.IsPetting, "First stationary contact does not count as a stroke");
                Vector3 slow = hand + Vector3.right * .001f;
                Invoke(pet, "SampleLocalContact", 10.1f, head, slow, absent);
                log.Check(!pet.IsPetting, "1 mm / 100 ms stays below stroke speed (not divided by frame delta)");
                Vector3 stroke = hand + Vector3.right * .011f;
                Invoke(pet, "SampleLocalContact", 10.2f, head, stroke, absent);
                log.Check(pet.IsPetting && !pet.IsBigJoy, "10 mm / 100 ms gives a one-hand stroke");
                Invoke(pet, "RefreshFollowHand", head, hand + Vector3.right * .05f);
                log.Check(Read<Vector3>(pet, "_followHandPos") == hand + Vector3.right * .05f,
                    "Follow target updates between contact samples");
                log.Check(Read<Vector3>(pet, "_prevHandPos") == stroke && Read<float>(pet, "_previousContactSampleTime") == 10.2f,
                    "Visual follow does not overwrite velocity sample history");
                Invoke(pet, "SampleLocalContact", 10.3f, head, stroke, absent);
                log.Check(Read<float>(pet, "_lastStrokeTime") == 10.2f, "Unsampled follow movement does not invent a stroke");
                Invoke(pet, "SampleLocalContact", 10.91f, head, stroke, absent);
                log.Check(!pet.IsPetting && Read<bool>(pet, "_handHeld"), "Stopped hand expires stroke grace but remains held");

                Invoke(pet, "SampleLocalContact", 11f, head, absent, hand);
                log.Check(Read<bool>(pet, "_handIsRight") && !pet.IsPetting, "Changing hands does not create a velocity spike");
                Invoke(pet, "SampleLocalContact", 11.1f, head, hand, stroke);
                log.Check(pet.IsBigJoy, "Second valid hand joins a moving held hand");
                Invoke(pet, "SampleLocalContact", 11.2f, head, absent, hand);
                log.Check(pet.IsBigJoy, "Two-hand release retains its existing short grace");
                Invoke(pet, "SampleLocalContact", 11.2f + pet.twoHandGrace + .1f, head, absent, hand + Vector3.right * .08f);
                log.Check(pet.IsPetting && !pet.IsBigJoy, "Two-hand grace expires back to one-hand petting");
                Invoke(pet, "RefreshFollowHand", head, absent);
                log.Check(!Read<bool>(pet, "_followHandValid") && !Read<bool>(pet, "_prevSampleValid"),
                    "Tracking loss immediately invalidates follow and stroke history");
                Invoke(pet, "SampleLocalContact", 12.5f, head, absent, hand);
                log.Check(!pet.IsPetting, "Tracking reacquisition does not invent a stroke");
                Invoke(pet, "SampleLocalContact", 12.6f, head, absent, stroke);
                log.Check(pet.IsPetting, "A new valid stroke works after tracking loss");
                Invoke(pet, "SampleLocalContact", 12.7f, head, absent, absent);
                log.Check(!pet.IsPetting && !Read<bool>(pet, "_handHeld") && !Read<bool>(pet, "_followHandValid"),
                    "Release clears petting, held hand, and follow at the next sample");

                Invoke(pet, "SampleLocalContact", 13f, head, hand, absent);
                Invoke(pet, "SampleLocalContact", 13.1f, head, stroke, absent);
                Set(pet, "_nextContactSampleTime", 99f);
                pet._SuppressPetting();
                log.Check(!pet.IsPetting && !Read<bool>(pet, "_handHeld") && !Read<bool>(pet, "_prevSampleValid"),
                    "Fetch suppression clears contact immediately");
                pet._ResumePetting();
                log.Check(!Read<bool>(pet, "_suppressed") && Read<float>(pet, "_nextContactSampleTime") == 0f,
                    "Resume is eligible for sampling immediately");
                Set(pet, "_nextContactSampleTime", 99f);
                Set(pet, "_prevSampleValid", true);
                pet._ResumePetting();
                log.Check(Read<float>(pet, "_nextContactSampleTime") == 99f && Read<bool>(pet, "_prevSampleValid"),
                    "Repeated resume does not defeat rate limiting or clear stroke history");
            }
            catch (Exception e)
            {
                log.Check(false, e.ToString());
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            log.Info("Scope: real C# state transitions only; Udon VM, tracking input, networking, visible motion and FPS require separate validation.");
            log.Finish("Temp/pet-sampling-verify.txt");
        }
    }
}
