using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinPreviewEditor
    {
        public static string InspectSource002()
        {
            GuardStage();
            const string source = "C:/work/unity/vrc-live-generator/shows/kd-pumpkinparade/versions/002";
            var data = JsonConvert.DeserializeObject<VirtualCampers.LiveGenerator.ShowplanData>(File.ReadAllText(source + "/kd-pumpkinparade-002.showplan.json"));
            var result = VirtualCampers.LiveGenerator.ShowplanValidator.Validate(data);
            if (!result.IsValid) throw new InvalidOperationException(string.Join("\n", result.Errors));
            var p = One<KD_CandyDogPerformance>();
            if (Vector3.Distance(p.director.transform.position, KD_DogDefs.LiveStagePosition) > .001f
                || Quaternion.Angle(p.director.transform.rotation, Quaternion.Euler(0, KD_DogDefs.LiveStageEulerY, 0)) > .01f)
                throw new InvalidOperationException("Current stage cannot be used as the proposed source ground frame.");
            var survey = KD_CandyDogSetupEditor.InspectChoreographySource(p, source);
            Directory.CreateDirectory(Evidence);
            File.WriteAllText(Evidence + "/source002-ground.txt", survey);
            var manifest = JObject.Parse(File.ReadAllText(source + "/music-handoff.json"));
            string expected = (string)manifest["audio"]["sha256"];
            if (Hash(source + "/audio.wav") != expected) throw new InvalidOperationException("Source audio changed.");
            File.WriteAllText(Evidence + "/source002-inspection.json", JsonConvert.SerializeObject(new
            {
                checkedAt = DateTime.UtcNow.ToString("o"), assemblyUtc = File.GetLastWriteTimeUtc(typeof(KD_PumpkinPreviewEditor).Assembly.Location).ToString("o"),
                showplanSha256 = Hash(source + "/kd-pumpkinparade-002.showplan.json"), choreographySha256 = Hash(source + "/choreography.json"), audioSha256 = expected,
                scene = SceneManager.GetActiveScene().path, dirty = SceneManager.GetActiveScene().isDirty,
                result.Errors, result.Warnings, survey, scope = "Read-only source validation and ground raycasts; no scene integration or actual locomotion."
            }, Formatting.Indented));
            return "Source002 validator PASS. " + survey;
        }

        public static string InspectUpgrade()
        {
            GuardProject();
            var names = new[] { "kd_witch_hat_001", "kd_bat_body_001", "kd_bat_wing_left_001", "kd_bat_wing_right_001" };
            var assets = names.Select(name =>
            {
                string path = "Packages/com.virtualcampers.live-generator/Presets/Meshes/" + name + ".fbx";
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) throw new InvalidOperationException("Prop not imported: " + path);
                var mesh = root.GetComponentsInChildren<MeshFilter>(true).Single().sharedMesh;
                if (mesh == null || mesh.subMeshCount != 1 || mesh.uv.Length != mesh.vertexCount || mesh.normals.Length != mesh.vertexCount
                    || root.GetComponentsInChildren<Camera>(true).Length != 0 || root.GetComponentsInChildren<Light>(true).Length != 0)
                    throw new InvalidOperationException("Prop mesh/UV/normals/export scope differs: " + name);
                return new { name, path, mesh = mesh.name, vertices = mesh.vertexCount, triangles = mesh.triangles.Length / 3,
                    size = new[] { mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z },
                    clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Count() };
            }).ToArray();
            var scene = SceneManager.GetActiveScene();
            Directory.CreateDirectory(Evidence);
            File.WriteAllText(Evidence + "/upgrade-readiness.json", JsonConvert.SerializeObject(new
            {
                checkedAt = DateTime.UtcNow.ToString("o"), implementation = "stable-bodies-002-review-fixed",
                assemblyUtc = File.GetLastWriteTimeUtc(typeof(KD_PumpkinPreviewEditor).Assembly.Location).ToString("o"),
                scene = scene.path, dirty = scene.isDirty, sourceSha256 = Hash(SourcePath), assets,
                scope = "Imported asset structure only; scene upgrade and visual integration are not verified"
            }, Formatting.Indented));
            return "Four props imported; stable body code loaded. Scene dirty=" + scene.isDirty;
        }
    }
}
