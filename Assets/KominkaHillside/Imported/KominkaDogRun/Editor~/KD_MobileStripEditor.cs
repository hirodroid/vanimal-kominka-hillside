using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;

namespace KominkaDogRun.Editor
{
    // IMP-ENV-019: HoboRadio is PC-only (publisher's answer, 2026-09-18). Android / iOS builds drop the
    // scene-root `HoboRadio` (the whole prefab instance: controller, AVPro player, knob, programme board)
    // from the build's scene copy. The authoring scene and PC builds keep it. Same shape as
    // KD_AndroidToonMaterialsEditor, but the target scene is matched by path: the Toon probe scene shares
    // the `====KominkaDogRun====` root and has no radio, so a root-name match would fail that Verify.
    //
    // Order -1000: UdonSharp's [PostProcessScene] (order 0) copies proxy -> Udon and then destroys every
    // UdonSharpBehaviour proxy in the build copy. Stripping first means the radio is already gone when
    // KD_MenuSettings / KD_Language are serialized (their references become null), and the proxy-based
    // detection below still sees HoboRadio_Controller. At order 1000 the proxy was gone and the guard
    // failed the build (measured 2026-09-18).
    public sealed class KD_MobileStripEditor : IProcessSceneWithReport
    {
        internal const string ScenePath = "Assets/Scenes/KominkaDogRun.unity";
        internal const string RadioRoot = "HoboRadio";
        internal const string RadioProgram = "HoboRadio_Controller";
        internal const string BuildLog = "Temp/kd-mobile-strip-build.txt";
        public int callbackOrder => -1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // AssetBundle builds may have a null report; null is not a Play-mode test.
            var target = report != null ? report.summary.platform : EditorUserBuildSettings.activeBuildTarget;
            if (!ShouldProcess(scene, target, BuildPipeline.isBuildingPlayer, EditorApplication.isPlayingOrWillChangePlaymode)) return;
            int removed = StripForBuild(scene, out int droppedMaterials);
            Directory.CreateDirectory("Temp");
            File.AppendAllText(BuildLog, System.DateTime.Now.ToString("s") + " target=" + target
                + " scene=" + scene.path + " removed=" + removed + " droppedDynamicMaterials=" + droppedMaterials
                + " remainingRadios=" + CountRadios(scene) + "\n");
        }

        internal static bool IsMobile(BuildTarget target) => target == BuildTarget.Android || target == BuildTarget.iOS;

        internal static bool ShouldProcess(Scene scene, BuildTarget target, bool building, bool playing)
            => building && !playing && IsMobile(target) && scene.IsValid() && scene.path == ScenePath;

        internal static GameObject[] RadioRoots(Scene scene)
            => scene.GetRootGameObjects().Where(g => g.name == RadioRoot).ToArray();

        // A radio is the U# proxy (authoring scene, fixtures) or, once proxies are stripped, the backing
        // UdonBehaviour whose program is HoboRadio_Controller. Counted per GameObject so both never double up.
        internal static bool IsRadio(GameObject go)
            => go.GetComponent<HoboRadio_Controller>() != null
            || go.GetComponents<VRC.Udon.UdonBehaviour>().Any(u => u.programSource != null && u.programSource.name == RadioProgram);

        internal static int CountRadios(GameObject root)
            => root.GetComponentsInChildren<Transform>(true).Count(t => IsRadio(t.gameObject));

        internal static int CountRadios(Scene scene)
            => scene.GetRootGameObjects().Sum(g => CountRadios(g));

        static HashSet<Material> RendererMaterials(IEnumerable<GameObject> roots)
            => new HashSet<Material>(roots.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m != null));

        // Only the native build callback and disposable verification fixtures call this.
        // Also drops the radio's materials from VRCSceneDescriptor.DynamicMaterials: the SDK lists every scene
        // material there, so the materials (and their textures) stayed in the Android bundle after the root was
        // gone (measured 2026-09-18). A material still used by another renderer is kept.
        internal static int StripForBuild(Scene scene, out int droppedMaterials)
        {
            droppedMaterials = 0;
            var roots = RadioRoots(scene);
            if (roots.Length != 1)
                throw new BuildFailedException("Mobile strip expects exactly one scene-root '" + RadioRoot + "', found " + roots.Length);
            if (CountRadios(roots[0]) == 0)
                throw new BuildFailedException("Scene-root '" + RadioRoot + "' no longer holds a HoboRadio_Controller; review IMP-ENV-019");
            var radioMaterials = RendererMaterials(roots);
            Object.DestroyImmediate(roots[0]);
            if (CountRadios(scene) != 0)
                throw new BuildFailedException("A HoboRadio_Controller survives outside '" + RadioRoot + "'; review IMP-ENV-019");
            var stillUsed = RendererMaterials(scene.GetRootGameObjects());
            foreach (var descriptor in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<VRC.SDK3.Components.VRCSceneDescriptor>(true)))
            {
                if (descriptor.DynamicMaterials == null) continue;
                int before = descriptor.DynamicMaterials.Count;
                descriptor.DynamicMaterials = descriptor.DynamicMaterials.Where(m => m == null || !radioMaterials.Contains(m) || stillUsed.Contains(m)).ToList();
                droppedMaterials += before - descriptor.DynamicMaterials.Count;
            }
            return 1;
        }

        // Unique per run so a leftover from an interrupted run is never overwritten or deleted by mistake.
        static string ProbePath => "Assets/KominkaHillside/Imported/KominkaDogRun/KD_MobileStripProbe-" + GUID.Generate() + ".unity";

        static bool Rejects(Scene scene)
        {
            try { StripForBuild(scene, out _); return false; }
            catch (BuildFailedException) { return true; }
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Mobile Strip/1 - Verify")]
        public static void Verify()
        {
            KD_PlushShelfEditor.Guard();
            var authoring = SceneManager.GetActiveScene();
            var log = new KD_VerifyLog("Mobile (Android / iOS) build strip verification — IMP-ENV-019");
            int authoringRoots = authoring.GetRootGameObjects().Length;
            var radioRoots = RadioRoots(authoring);
            log.Check(radioRoots.Length == 1, "authoring scene has exactly one scene-root " + RadioRoot);
            log.Check(radioRoots.Length == 1 && CountRadios(radioRoots[0]) == 1, RadioRoot + " holds the HoboRadio_Controller");
            log.Check(CountRadios(authoring) == 1, "the only radio lives under " + RadioRoot);
            log.Check(radioRoots.Length == 1 && radioRoots[0].GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true)
                .Any(u => u.programSource != null && u.programSource.name == RadioProgram), "backing UdonBehaviour program is " + RadioProgram + " (detection survives proxy stripping)");
            log.Check(new KD_MobileStripEditor().callbackOrder < 0, "strip runs before UdonSharp's [PostProcessScene] (order 0) so references serialize as null");
            var settings = KD_SceneObjects.Find("KD_MenuSettings")?.GetComponent<KD_MenuSettings>();
            log.Check(settings != null && settings.radioButton != null, "KD_MenuSettings.radioButton wired (Menu Finish/2 - Setup)");
            log.Check(settings != null && settings.radio != null && radioRoots.Length == 1 && settings.radio.transform.IsChildOf(radioRoots[0].transform), "settings radio reference points into " + RadioRoot);

            log.Check(!ShouldProcess(authoring, BuildTarget.StandaloneWindows64, true, false), "PC builds excluded");
            log.Check(ShouldProcess(authoring, BuildTarget.Android, true, false), "Android world build accepted");
            log.Check(ShouldProcess(authoring, BuildTarget.iOS, true, false), "iOS world build accepted");
            log.Check(!ShouldProcess(authoring, BuildTarget.Android, false, false), "Editing excluded");
            log.Check(!ShouldProcess(authoring, BuildTarget.Android, true, true), "Play excluded");
            // Native callback called outside a build must leave the authoring scene alone.
            new KD_MobileStripEditor().OnProcessScene(authoring, null);
            log.Check(authoring.GetRootGameObjects().Length == authoringRoots && RadioRoots(authoring).Length == 1, "callback outside a build leaves the authoring scene alone");

            // Disposable fixture: a settings behaviour pointing at a radio root, stripped, saved, reloaded.
            Material radioOnly = null, shared = null, unrelated = null;
            string probePath = ProbePath;
            bool probeSaved = false;
            var testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(testScene);
            try
            {
                log.Check(!ShouldProcess(testScene, BuildTarget.Android, true, false), "Other scenes excluded (Toon probe scene is not matched by root name)");
                var radio = new GameObject(RadioRoot);
                SceneManager.MoveGameObjectToScene(radio, testScene);
                var body = new GameObject("Radio_Local");
                body.transform.SetParent(radio.transform, false);
                var controller = body.AddUdonSharpComponent<HoboRadio_Controller>();
                log.Check(CountRadios(radio) == 1, "fixture counts proxy + backing as one radio");
                Object.DestroyImmediate(controller);
                log.Check(CountRadios(radio) == 1, "fixture still counts the backing UdonBehaviour after its proxy is stripped");
                var radioBacking = body.GetComponent<VRC.Udon.UdonBehaviour>();
                var knob = new GameObject("Knob").transform;
                knob.SetParent(body.transform, false);
                var holder = new GameObject("KD_MenuSettings");
                SceneManager.MoveGameObjectToScene(holder, testScene);
                var fixture = holder.AddUdonSharpComponent<KD_MenuSettings>();
                fixture.radio = radioBacking;
                fixture.radioKnob = knob;
                UdonSharpEditorUtility.CopyProxyToUdon(fixture);
                var extra = new GameObject("KD_Unrelated");
                SceneManager.MoveGameObjectToScene(extra, testScene);
                // Materials: one only on the radio (dropped), one shared with an unrelated renderer (kept), one unrelated (kept).
                radioOnly = new Material(Shader.Find("Standard")) { name = "KD_ProbeRadioOnly" };
                shared = new Material(Shader.Find("Standard")) { name = "KD_ProbeShared" };
                unrelated = new Material(Shader.Find("Standard")) { name = "KD_ProbeUnrelated" };
                var radioMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                radioMesh.transform.SetParent(body.transform, false);
                radioMesh.GetComponent<Renderer>().sharedMaterials = new[] { radioOnly, shared };
                var extraMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                extraMesh.transform.SetParent(extra.transform, false);
                extraMesh.GetComponent<Renderer>().sharedMaterials = new[] { shared, unrelated };
                var descriptorGo = new GameObject("KD_ProbeDescriptor");
                SceneManager.MoveGameObjectToScene(descriptorGo, testScene);
                var descriptor = descriptorGo.AddComponent<VRC.SDK3.Components.VRCSceneDescriptor>();
                descriptor.DynamicMaterials = new List<Material> { radioOnly, shared, unrelated, null };

                int removed = 0, dropped = 0;
                try { removed = StripForBuild(testScene, out dropped); } catch (BuildFailedException e) { log.Check(false, e.Message); }
                log.Check(removed == 1 && RadioRoots(testScene).Length == 0 && CountRadios(testScene) == 0, "fixture radio root removed");
                log.Check(extra != null && testScene.GetRootGameObjects().Contains(extra) && testScene.GetRootGameObjects().Contains(holder), "unrelated roots untouched");
                log.Check(dropped == 1 && descriptor.DynamicMaterials.SequenceEqual(new Material[] { shared, unrelated, null }),
                    "descriptor DynamicMaterials drops the radio-only material, keeps shared / unrelated / null entries in order");
                log.Check(Rejects(testScene), "second strip fails loudly (no radio root left)");
                // Each guard must fail the build on its own: two roots / root without a radio / a radio outside the root.
                var rootA = new GameObject(RadioRoot); SceneManager.MoveGameObjectToScene(rootA, testScene);
                var rootB = new GameObject(RadioRoot); SceneManager.MoveGameObjectToScene(rootB, testScene);
                log.Check(Rejects(testScene), "two " + RadioRoot + " roots rejected");
                Object.DestroyImmediate(rootB);
                log.Check(Rejects(testScene) && rootA != null, RadioRoot + " without a radio rejected and left in place");
                var inner = new GameObject("Radio_Local"); inner.transform.SetParent(rootA.transform, false);
                inner.AddUdonSharpComponent<HoboRadio_Controller>();
                var stray = new GameObject("KD_StrayRadio"); stray.transform.SetParent(extra.transform, false);
                stray.AddUdonSharpComponent<HoboRadio_Controller>();
                log.Check(Rejects(testScene) && rootA == null && CountRadios(testScene) == 1, "a radio surviving outside " + RadioRoot + " rejected after the root is removed");
                Object.DestroyImmediate(stray);

                // Serialize + reload. UdonSharp removes null GameObject / Transform / UdonBehaviour variables from the
                // backing table (UdonVariableStorageInterface.SetVariable), and Udon resolves a missing one to the
                // behaviour itself at run time (UdonBehaviour.cs:319-325). So "absent" and "null" are both the stripped
                // state; what must never remain is a live reference into the removed radio.
                object controlValue;
                bool controlPresent = UdonSharpEditorUtility.GetBackingUdonBehaviour(fixture).publicVariables.TryGetVariableValue("radioKnob", out controlValue);
                log.Check(controlPresent && controlValue as Object == knob, "control: before saving, backing radioKnob still points at the fixture knob");
                if (!EditorSceneManager.SaveScene(testScene, probePath)) throw new System.InvalidOperationException("Could not save disposable fixture");
                probeSaved = true;
                EditorSceneManager.CloseScene(testScene, true);
                testScene = EditorSceneManager.OpenScene(probePath, OpenSceneMode.Additive);
                var reloaded = testScene.GetRootGameObjects().First(g => g.name == "KD_MenuSettings").GetComponent<KD_MenuSettings>();
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(reloaded);
                object radioValue, knobValue;
                bool radioPresent = backing.publicVariables.TryGetVariableValue("radio", out radioValue);
                bool knobPresent = backing.publicVariables.TryGetVariableValue("radioKnob", out knobValue);
                log.Info("reloaded backing: radio " + (radioPresent ? (radioValue as Object == null ? "null" : "LIVE") : "absent")
                    + ", radioKnob " + (knobPresent ? (knobValue as Object == null ? "null" : "LIVE") : "absent"));
                log.Check(reloaded.radio == null && reloaded.radioKnob == null, "reloaded proxy radio / radioKnob are null");
                log.Check(radioValue as Object == null && knobValue as Object == null, "reloaded backing radio / radioKnob are absent or null, never a live reference (Udon resolves them to self at run time; entry points gated by RadioAvailable)");
            }
            catch (System.Exception e) { log.Check(false, e.ToString()); }
            finally
            {
                SceneManager.SetActiveScene(authoring);
                if (testScene.IsValid()) EditorSceneManager.CloseScene(testScene, true);
                if (probeSaved) AssetDatabase.DeleteAsset(probePath);
                foreach (var m in new[] { radioOnly, shared, unrelated }) if (m != null) Object.DestroyImmediate(m);
            }
            // Root and radio counts only: opening scenes lets SleepKit2's PenManagerButtonGenerator regenerate its buttons
            // in the authoring scene (dirty, same content), which this Verify neither causes deliberately nor checks.
            log.Check(authoring.GetRootGameObjects().Length == authoringRoots && RadioRoots(authoring).Length == 1 && CountRadios(authoring) == 1, "authoring scene root count and radio count unchanged after fixture");
            var authoringDescriptor = authoring.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<VRC.SDK3.Components.VRCSceneDescriptor>(true)).FirstOrDefault();
            int radioMaterialsListed = authoringDescriptor == null || authoringDescriptor.DynamicMaterials == null ? -1
                : authoringDescriptor.DynamicMaterials.Count(m => m != null && RendererMaterials(radioRoots).Contains(m));
            log.Info("authoring descriptor lists " + radioMaterialsListed + " radio material(s) in DynamicMaterials (dropped in mobile builds)");
            log.Finish("Temp/kd-mobile-strip-verify.txt");
        }
    }
}
