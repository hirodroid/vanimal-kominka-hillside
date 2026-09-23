using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;

namespace KominkaDogRun.Editor
{
    // Regression oracle uses the pre-optimization equations, independent of the cached fields.
    public static class KD_DogPerformanceVerifyEditor
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Flags).Invoke(obj, args);
        static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Flags).GetValue(obj);
        static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
        static float Roll(int cycle, int stream, int salt = 0)
        {
            long hash = (long)cycle * 73856093L ^ (long)stream * 19349663L ^ (long)salt * 83492791L;
            hash = (hash * 1664525L + 1013904223L) & 0x7FFFFFFF;
            return (hash >> 7) / 16777216f;
        }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static void Near(float actual, float expected, string label) => Require(Mathf.Abs(actual - expected) < 0.00001f, label);

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Performance/Verify Plans and Outputs")]
        public static void Verify()
        {
            KD_PlushShelfEditor.Guard();
            var log = new KD_VerifyLog("Dog plan/output regression; C# preview and compiled Udon binding");
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            AnimatorController controller = null;
            try
            {
                root = new GameObject("KD_DogPerformanceFixture") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                var wander = root.AddUdonSharpComponent<KD_DogWanderController>();
                var fetch = root.AddUdonSharpComponent<KD_DogFetchController>();
                var pet = root.AddUdonSharpComponent<KD_DogPetController>();
                var center = new GameObject("KD_TestCenter"); center.transform.SetParent(root.transform);
                wander.areaCenter = center.transform;
                int cases = 0;
                foreach (int n in new[] { 2, 3, 6, 9, 6 })
                foreach (int cycle in new[] { 0, 1, 1, 20000, -1, -29, 42, 0 })
                foreach (int variant in new[] { 0, 1, 2 })
                {
                    wander.playCyclePeriod = 60f + variant * 10;
                    wander.playDurationMin = 18f + variant; wander.playDurationMax = 28f + variant * 2;
                    wander.playBreakOff = 2f + variant; wander.playFaceChance = variant * 0.5f;
                    wander.playMaxDogs = variant == 0 ? 2 : n;
                    Call(wander, "EnsurePlaySchedule", cycle, n);
                    float length = Mathf.Lerp(wander.playDurationMin, wander.playDurationMax, Roll(cycle, 1));
                    float total = length + wander.playBreakOff;
                    Near(Read<float>(wander, "_playCoreLength"), length, "play length");
                    Near(Read<float>(wander, "_playWindowTotal"), total, "play total");
                    Near(Read<float>(wander, "_playWindowStart"), Roll(cycle, 2) * Mathf.Max(wander.playCyclePeriod - total, 0f), "play start");
                    bool face = Roll(cycle, 4) < wander.playFaceChance;
                    int cap = Mathf.Clamp(wander.playMaxDogs, 2, n);
                    int want = face ? 2 : 2 + (int)(Roll(cycle, 6) * (cap - 1)); want = Mathf.Min(want, cap);
                    Require(Read<bool>(wander, "_playIsFace") == face && Read<int>(wander, "_playPartySize") == want, "play participants");
                    for (int dog = 0; dog < n; dog++)
                    {
                        float r = Roll(cycle, 100 + dog); int rank = 0;
                        for (int k = 0; k < n; k++)
                            if (Roll(cycle, 100 + k) < r || (Roll(cycle, 100 + k) == r && k < dog)) rank++;
                        Require((int)Call(wander, "PlayRankOf", dog, n) == rank, "rank preserved");
                        Require((int)Call(wander, "PlayDogOfRank", rank, n) == dog, "inverse rank preserved");
                    }
                    Require((int)Call(wander, "PlayDogOfRank", -1, n) == -1 && (int)Call(wander, "PlayDogOfRank", n, n) == -1, "invalid rank");
                    wander.seedSalt = variant * 812; wander.cyclePeriod = 65f + variant;
                    wander.wanderDurationMin = 10f + variant; wander.wanderDurationMax = 25f + variant;
                    Call(wander, "EnsureWanderSchedule", cycle);
                    float wl = Mathf.Lerp(wander.wanderDurationMin, wander.wanderDurationMax, Roll(cycle, 1, wander.seedSalt));
                    float latest = Mathf.Max(wander.cyclePeriod - wl, 0f);
                    Near(Read<float>(wander, "_wanderWindowLength"), wl, "wander length");
                    Near(Read<float>(wander, "_wanderWindowStart"), Mathf.Lerp(latest * .25f, latest, Roll(cycle, 2, wander.seedSalt)), "wander start");
                    wander.areaRadius = 2f + variant;
                    foreach (int hop in new[] { 0, 1, -1, 1300, 1300 })
                    {
                        center.transform.position = new Vector3(variant, 0.3f, hop % 7);
                        float radius = Mathf.Sqrt(Roll(hop, 3, wander.seedSalt)) * wander.areaRadius;
                        float angle = Roll(hop, 4, wander.seedSalt) * Mathf.PI * 2f;
                        Vector3 expected = center.transform.position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                        Require(Vector3.Distance((Vector3)Call(wander, "WanderTarget", hop), expected) < .00001f, "wander target");
                        center.transform.position += Vector3.right * 4f;
                        Require(Vector3.Distance((Vector3)Call(wander, "WanderTarget", hop), expected + Vector3.right * 4f) < .00001f, "moving center with hot cache");
                    }
                    cases++;
                }
                log.Check(true, cases + " plan cases preserve original equations, settings changes, count changes and time rewind");

                controller = new AnimatorController(); controller.AddLayer("Base Layer");
                foreach (string name in new[] { "Sleep", "CallReady", "Run", "Walk", "Pickup", "Pet" }) controller.AddParameter(name, AnimatorControllerParameterType.Bool);
                controller.AddParameter("CallPose", AnimatorControllerParameterType.Int);
                controller.AddParameter("CallTime", AnimatorControllerParameterType.Float);
                var animator = root.AddComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.Rebind(); animator.Update(0f);
                wander.dogAnimator = animator; fetch.dogAnimator = animator;
                Call(wander, "ApplyCompanionAnimation", true, false, 4, 0f);
                Require(animator.GetBool("Sleep") && animator.GetInteger("CallPose") == 4, "initial companion output");
                Call(wander, "ApplyCompanionAnimation", false, true, 7, .45f);
                Require(!animator.GetBool("Sleep") && animator.GetBool("CallReady") && animator.GetInteger("CallPose") == 7, "companion transition");
                Near(animator.GetFloat("CallTime"), .45f, "stretch time");
                Call(wander, "ApplyCompanionAnimation", false, true, 7, .6f);
                Near(animator.GetFloat("CallTime"), .6f, "stretch remains smooth");
                Call(wander, "ApplyCompanionAnimation", false, false, 0, 0f);
                Require(!animator.GetBool("CallReady") && animator.GetInteger("CallPose") == 0, "companion exit");
                Near(animator.GetFloat("CallTime"), 0f, "stretch reset");
                // Unity event wrappers are gated by UdonSharp outside Play. Test their reset
                // body here; the ClientSim scenario verifies the real _onEnable entry point.
                animator.SetBool("Sleep", true); Call(wander, "ResetPerformanceOutputs");
                Call(wander, "ApplyCompanionAnimation", false, false, 0, 0f);
                Require(!animator.GetBool("Sleep"), "companion resume resets output");
                log.Check(true, "Companion entry, pose changes, continuous stretch, exit and re-enable");

                Call(fetch, "WriteLocomotionAnimation", true, false, false);
                Require(animator.GetBool("Run") && !animator.GetBool("Walk"), "fetch run");
                Set(fetch, "dogState", 0); Call(fetch, "ApplyState");
                Require(!animator.GetBool("Run") && !animator.GetBool("Pickup"), "idle clears locomotion");
                animator.SetBool("Walk", true); // Wander's output while Fetch is idle.
                Set(fetch, "dogState", 1); Call(fetch, "ApplyState");
                Call(fetch, "WriteLocomotionAnimation", true, false, false);
                Require(animator.GetBool("Run") && !animator.GetBool("Walk"), "fetch reclaims shared animator");
                Call(fetch, "WriteLocomotionAnimation", false, false, true);
                Require(!animator.GetBool("Run") && animator.GetBool("Pickup"), "pickup at rest");
                animator.SetBool("Pickup", false); Call(fetch, "ResetLocomotionOutput");
                Call(fetch, "WriteLocomotionAnimation", false, false, true);
                Require(animator.GetBool("Pickup"), "fetch re-enable");
                log.Check(true, "Fetch/Wander handoff, pickup, re-enable output");

                wander.breathSource = root.AddComponent<AudioSource>();
                Set(wander, "_breathLevel", .4f); wander.localVoiceGain = .5f; wander._ApplyVoiceVolume();
                Near(wander.breathSource.volume, .2f, "initial breath volume");
                Set(wander, "_breathLevel", .3f); wander._ApplyVoiceVolume(); Near(wander.breathSource.volume, .15f, "fade");
                wander.localVoiceGain = 0f; wander._ApplyVoiceVolume(); Near(wander.breathSource.volume, 0f, "mute immediately");
                wander.localVoiceGain = 1f; wander._ApplyVoiceVolume(); Near(wander.breathSource.volume, .3f, "unmute immediately");
                log.Check(true, "Breath fade and settings changes remain immediate");

                var fxObject = new GameObject("KD_TestHeart"); fxObject.transform.SetParent(root.transform);
                pet.heartFx = fxObject.AddComponent<ParticleSystem>(); pet.headAnchor = center.transform;
                var main = pet.heartFx.main; main.simulationSpace = ParticleSystemSimulationSpace.World;
                pet.heartFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                center.transform.position = new Vector3(4, 1, 5); pet._PlayPetHearts();
                Require(pet.heartFx.transform.position == center.transform.position + Vector3.up * pet.heartOffsetY, "Play starts at current head");
                pet._StopPetHearts(); center.transform.position += Vector3.right * 5;
                Set(pet, "_lastJoyBurstTime", -100f); pet._PlayJoyBurst();
                Require(pet.heartFx.transform.position == center.transform.position + Vector3.up * pet.heartOffsetY, "Emit starts at current head after idle");
                log.Check(true, "Heart Play and joy Emit reposition after idle");

                var sceneDogs = KD_ProductsEditor.SceneComponents<KD_DogWanderController>();
                sceneDogs = sceneDogs.Where(d => d.gameObject.scene != scene).ToArray();
                log.Check(sceneDogs.Length == 6, "Six authoring dogs");
                foreach (var dog in sceneDogs)
                {
                    var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(dog);
                    var serialized = new SerializedObject(backing).FindProperty("serializedProgramAsset").objectReferenceValue as VRC.Udon.AbstractSerializedUdonProgramAsset;
                    var program = serialized == null ? null : serialized.RetrieveProgram();
                    log.Check(program != null && program.SymbolTable.GetSymbols().Contains("_playScheduleValid")
                        && program.SymbolTable.GetSymbols().Contains("_companionAnimationValid"), dog.name + " compiled cache/output symbols");
                }
                foreach (var scenePet in KD_ProductsEditor.SceneComponents<KD_DogPetController>().Where(p => p.gameObject.scene != scene))
                {
                    var systems = scenePet.fxVariants.Concat(scenePet.fxVariants2).Concat(new[] { scenePet.heartFx, scenePet.heartFx2 }).Where(p => p != null).Distinct();
                    log.Check(systems.All(p => p.main.simulationSpace == ParticleSystemSimulationSpace.World), scenePet.name + " all heart variants world-space");
                }
            }
            catch (Exception e) { log.Check(false, e.ToString()); }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (controller != null) UnityEngine.Object.DestroyImmediate(controller);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            log.Info("Scope: C# regression and compiled program binding; real Udon execution is measured separately. VR input, networking and Quest FPS not covered.");
            log.Finish("Temp/dog-performance-verify.txt");
        }
    }
}
