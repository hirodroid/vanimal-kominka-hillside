using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinPreviewEditor
    {
        // Sample fresh clones in an unsaved Editor preview scene. The actual world,
        // its dogs, controllers and purchased clips are never sampled or modified.
        public static string InspectMountGeometry()
        {
            GuardStage();
            var sourceScene = SceneManager.GetActiveScene(); bool wasDirty = sourceScene.isDirty;
            var sourceRoots = sourceScene.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(i => i).ToArray();
            var descriptor = sourceScene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<VRC.SDK3.Components.VRCSceneDescriptor>(true)).Single();
            var networkBefore = descriptor.NetworkIDCollection.Select(n => n?.gameObject == null ? 0 : n.gameObject.GetInstanceID()).ToArray();
            var p = One<KD_CandyDogPerformance>();
            var before = p.dogs.Select(d => d.dogRoot.localToWorldMatrix).ToArray();
            var measurements = new List<object>();
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (string key in new[] { "Lab", "Pome" })
                {
                    var def = KD_DogDefs.All.Single(d => d.key == key);
                    var dog = p.dogs.Single(d => d.dogRoot.name == "KD_Dog_" + key).dogRoot;
                    var lie = KD_CallPoseEditor.PoseClip(def, 1);
                    var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(def.idle.guid));
                    if (lie == null || idle == null) throw new InvalidOperationException("Mount pose source missing.");
                    foreach (var clip in new[] { idle, lie }) foreach (float time in new[] { 0f, 1f, 4f, 8f })
                    {
                        var clone = UnityEngine.Object.Instantiate(dog.gameObject);
                        try
                        {
                            SceneManager.MoveGameObjectToScene(clone, preview);
                            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                            clone.transform.localScale = dog.lossyScale; clone.SetActive(true);
                            clip.SampleAnimation(clone, Mathf.Min(time, clip.length));
                            KD_DogPreviewEditor.ApplyConstraints(clone);
                            var vertices = new List<Vector3>(); var skins = new List<object>();
                            foreach (var skin in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            {
                                bool accessory = false;
                                for (var t = skin.transform; t != clone.transform; t = t.parent)
                                    if (t.name.StartsWith("KD_Acc_", StringComparison.Ordinal) || t.name.StartsWith("KD_CandyCostume_", StringComparison.Ordinal)) accessory = true;
                                if (accessory || !skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                                var mesh = new Mesh();
                                try
                                {
                                    skin.BakeMesh(mesh, true);
                                    var world = mesh.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
                                    vertices.AddRange(world);
                                    skins.Add(new { name = skin.name, vertices = world.Length, bounds = MountBounds(world) });
                                }
                                finally { UnityEngine.Object.DestroyImmediate(mesh); }
                            }
                            if (vertices.Count == 0) throw new InvalidOperationException("No posed dog vertices.");
                            var center = new List<object>();
                            for (int cell = -20; cell <= 20; cell++)
                            {
                                float z = cell * .04f;
                                var strip = vertices.Where(v => Math.Abs(v.x) < .055f && Math.Abs(v.z - z) < .025f).ToArray();
                                if (strip.Length != 0) center.Add(new { z, minY = strip.Min(v => v.y), maxY = strip.Max(v => v.y), points = strip.Length });
                            }
                            float minY = vertices.Min(v => v.y);
                            var feet = vertices.Where(v => v.y <= minY + .018f).ToArray();
                            measurements.Add(new { dog = key, pose = clip == lie ? "lie" : "idle", time,
                                clip = AssetDatabase.GetAssetPath(clip), clipHash = Hash(AssetDatabase.GetAssetPath(clip)), clipLength = clip.length,
                                rootScale = new[] { dog.lossyScale.x, dog.lossyScale.y, dog.lossyScale.z },
                                bounds = MountBounds(vertices), lowerSurface = MountBounds(feet), centerProfile = center, skins,
                                bones = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s => s.bones).Where(b => b != null).Distinct()
                                    .Select(b => new { path = AnimationUtility.CalculateTransformPath(b, clone.transform), position = new[] { b.position.x, b.position.y, b.position.z } }).ToArray(),
                                scope = "Static sampled geometry after existing two-tier constraints; lower surface is not a semantic foot-contact measurement."
                            });
                        }
                        finally { UnityEngine.Object.DestroyImmediate(clone); }
                    }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                CheckMountStudySource(sourceScene, wasDirty, sourceRoots, descriptor, networkBefore, p, before);
            }
            File.WriteAllText(Evidence + "/mount-geometry.json", JsonConvert.SerializeObject(new
            { checkedAt = DateTime.UtcNow.ToString("o"), sourceScene = sourceScene.path, dirty = wasDirty,
                sourcePreserved = true, previewClosed = !preview.IsValid(), measurements }, Formatting.Indented));
            return "Mount geometry sampled on 16 fresh preview clones; world and dog transforms preserved.";
        }

        private static object MountBounds(IEnumerable<Vector3> vertices)
        {
            var all = vertices.ToArray();
            if (all.Length == 0) throw new InvalidOperationException("Empty geometry slice.");
            return new { min = new[] { all.Min(v => v.x), all.Min(v => v.y), all.Min(v => v.z) },
                max = new[] { all.Max(v => v.x), all.Max(v => v.y), all.Max(v => v.z) } };
        }

        // A pose/contact study, not a live movement test. Cameras and colliders are
        // confined to the disposable preview scene; no scene save is performed.
        public static string RenderMountPoseStudy() { return RenderMountStudy(false); }
        public static string RenderMountFlightStudy() { return RenderMountStudy(true); }
        public static string RenderMountAnimatedFlightStudy() { return RenderMountStudy(true, true); }
        public static string RenderMountAbortStudy() { return RenderMountStudy(true, true, true); }

        private static string RenderMountStudy(bool flight, bool animated = false, bool abort = false)
        {
            GuardStage();
            var source = SceneManager.GetActiveScene(); bool dirty = source.isDirty;
            var roots = source.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(i => i).ToArray();
            var descriptor = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<VRC.SDK3.Components.VRCSceneDescriptor>(true)).Single();
            var network = descriptor.NetworkIDCollection.Select(n => n?.gameObject == null ? 0 : n.gameObject.GetInstanceID()).ToArray();
            var p = One<KD_CandyDogPerformance>();
            var matrices = p.dogs.Select(d => d.dogRoot.localToWorldMatrix).ToArray();
            var baked = new List<Mesh>(); var results = new List<object>();
            var viewObjects = new List<GameObject>();
            Vector3 attachmentPosition = Vector3.zero; Quaternion attachmentRotation = Quaternion.identity; string attachmentPath = null;
            string directory = Evidence + (abort ? "/mount-abort-study" : animated ? "/mount-flight-animated-study" : flight ? "/mount-flight-study" : "/mount-pose-study"); Directory.CreateDirectory(directory);
            Newtonsoft.Json.Linq.JObject flightPlan = flight ? Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Logs/pumpkin-setup-staging/mount-candidate.json")) : null;
            if (flight)
            {
                var attachment = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Evidence + "/mount-pose-study/contacts.json"));
                var pos = attachment["attachmentPosition"].ToObject<float[]>(); var rot = attachment["attachmentRotation"].ToObject<float[]>();
                attachmentPosition = new Vector3(pos[0], pos[1], pos[2]); attachmentRotation = new Quaternion(rot[0], rot[1], rot[2], rot[3]);
                attachmentPath = (string)attachment["attachmentPath"];
            }
            var hopClip = animated ? AssetDatabase.LoadAssetAtPath<AnimationClip>(MountHopPath) : null;
            if (animated && hopClip == null) throw new InvalidOperationException("Build the candidate hop clip first.");
            var scene = EditorSceneManager.NewPreviewScene(); var previous = RenderTexture.active;
            RenderTexture rt = null; Texture2D image = null;
            try
            {
                rt = new RenderTexture(1200, 800, 24); image = new Texture2D(1200, 800, TextureFormat.RGB24, false);
                var cameraObject = new GameObject("MountStudyCamera"); viewObjects.Add(cameraObject); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene; camera.targetTexture = rt;
                camera.orthographic = true; camera.orthographicSize = flight ? .9f : .65f; camera.aspect = 1.5f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f, .18f, .22f);
                var lightObject = new GameObject("MountStudyLight"); viewObjects.Add(lightObject); SceneManager.MoveGameObjectToScene(lightObject, scene);
                var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
                light.transform.rotation = Quaternion.Euler(40, -30, 0);
                var frames = abort ? MountAbortFrames(flightPlan) : flight ? flightPlan["frames"].Where((f, i) => i % 2 == 0).ToArray() : null;
                var times = flight ? frames.Select(f => (float)f["time"]).ToArray() : new[] { 0f, 1f, 4f, 8f };
                for (int frame = 0; frame < times.Length; frame++)
                {
                    float time = times[frame];
                    int samplePhase = flight ? (int)frames[frame]["phase"] : 2;
                    bool airborne = animated && (samplePhase == 1 || samplePhase == 3 || samplePhase == 4);
                    float sampleTime = !flight ? time : samplePhase == 1 && animated ? time - (float)flightPlan["start"]
                        : samplePhase == 3 && animated ? time - (float)flightPlan["leave"] : Mathf.Max(0, time - (float)flightPlan["touch"]);
                    if (animated && samplePhase == 0) sampleTime = 0;
                    if (abort) sampleTime = (float)frames[frame]["hopTime"];
                    var lab = MountStudyClone(p, "Lab", true, flight ? time - (float)flightPlan["supportLieStart"] : time, scene);
                    var pome = MountStudyClone(p, "Pome", false, sampleTime, scene, airborne ? hopClip : null);
                    try
                    {
                        var supportBone = lab.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(s => s.bones).Where(b => b != null).Distinct().Single(b => b.name == "Bone.002");
                        if (flight && lab.transform.Find(attachmentPath) != supportBone)
                            throw new InvalidOperationException("Measured mount attachment path differs from the actual support bone.");
                        if (!flight && time == 0)
                        {
                            attachmentPath = AnimationUtility.CalculateTransformPath(supportBone, lab.transform);
                            attachmentPosition = supportBone.InverseTransformPoint(new Vector3(0, .366f, -.05f));
                            attachmentRotation = Quaternion.Inverse(supportBone.rotation) * Quaternion.Euler(0, 180, 0) * Quaternion.Euler(-9, 0, 0);
                        }
                        pome.transform.SetPositionAndRotation(supportBone.TransformPoint(attachmentPosition), supportBone.rotation * attachmentRotation);
                        int mountPhase = flight ? (int)frames[frame]["phase"] : 2;
                        if (abort)
                        {
                            var xyz = frames[frame]["position"].ToObject<float[]>();
                            var xyzw = frames[frame]["rotation"].ToObject<float[]>();
                            pome.transform.SetPositionAndRotation(new Vector3(xyz[0], xyz[1], xyz[2]), new Quaternion(xyzw[0], xyzw[1], xyzw[2], xyzw[3]));
                        }
                        else if (flight && mountPhase != 2)
                        {
                            var position = frames[frame]["position"].ToObject<float[]>(); var origin = flightPlan["supportOrigin"].ToObject<float[]>();
                            var nominal = new Vector3(-(position[0] - origin[0]), position[1], -(position[2] - origin[2]));
                            float blend = mountPhase == 1 ? Mathf.InverseLerp((float)flightPlan["start"], (float)flightPlan["touch"], time)
                                : mountPhase == 3 ? 1 - Mathf.InverseLerp((float)flightPlan["leave"], (float)flightPlan["land"], time) : 0;
                            blend = Mathf.SmoothStep(0, 1, blend);
                            var correction = pome.transform.position - new Vector3(0, .366f, -.05f);
                            pome.transform.position = nominal + correction * blend;
                            pome.transform.rotation = Quaternion.Slerp(Quaternion.Euler(0, 180, 0), pome.transform.rotation, blend);
                        }
                        var surfaces = new List<MeshCollider>();
                        foreach (var skin in lab.GetComponentsInChildren<SkinnedMeshRenderer>())
                        {
                            if (!skin.enabled) continue;
                            var mesh = new Mesh(); baked.Add(mesh); skin.BakeMesh(mesh, true);
                            var collider = skin.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; surfaces.Add(collider);
                        }
                        Physics.SyncTransforms();
                        var feet = pome.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(s => s.bones).Where(b => b != null).Distinct()
                            .Where(b => b.name == "Bone.014_L" || b.name == "Bone.014_R" || b.name == "Bone.016_L" || b.name == "Bone.016_R").ToArray();
                        if (feet.Length != 4) throw new InvalidOperationException("Expected four measured Pome foot bones.");
                        var pawVertices = new List<Vector3>();
                        foreach (var skin in pome.GetComponentsInChildren<SkinnedMeshRenderer>())
                        {
                            if (!skin.enabled) continue;
                            var mesh = new Mesh(); baked.Add(mesh); skin.BakeMesh(mesh, true);
                            pawVertices.AddRange(mesh.vertices.Select(v => skin.transform.TransformPoint(v)));
                        }
                        var contacts = new List<object>();
                        int insideVertices = 0; float deepestVertex = 0;
                        if (flight) foreach (var vertex in pawVertices)
                        {
                            float top = float.NegativeInfinity;
                            foreach (var surface in surfaces)
                                if (surface.Raycast(new Ray(new Vector3(vertex.x, 1.8f, vertex.z), Vector3.down), out var hit, 2f)) top = Mathf.Max(top, hit.point.y);
                            if (!float.IsNegativeInfinity(top) && vertex.y < top - .006f)
                            { insideVertices++; deepestVertex = Mathf.Max(deepestVertex, top - vertex.y); }
                        }
                        foreach (var foot in feet)
                        {
                            float top = float.NegativeInfinity;
                            foreach (var surface in surfaces)
                                if (surface.Raycast(new Ray(new Vector3(foot.position.x, 1.5f, foot.position.z), Vector3.down), out var hit, 2f))
                                    top = Mathf.Max(top, hit.point.y);
                            var sole = pawVertices.Where(v => Math.Abs(v.x - foot.position.x) < .02f && Math.Abs(v.z - foot.position.z) < .02f
                                && Math.Abs(v.y - foot.position.y) < .04f).OrderBy(v => v.y).Take(1).ToArray();
                            float soleTop = float.NegativeInfinity;
                            if (sole.Length == 1) foreach (var surface in surfaces)
                                if (surface.Raycast(new Ray(new Vector3(sole[0].x, 1.5f, sole[0].z), Vector3.down), out var hit, 2f))
                                    soleTop = Mathf.Max(soleTop, hit.point.y);
                            contacts.Add(new { foot = foot.name, position = new[] { foot.position.x, foot.position.y, foot.position.z },
                                surfaceHit = !float.IsNegativeInfinity(top), surfaceY = float.IsNegativeInfinity(top) ? (float?)null : top,
                                boneGap = float.IsNegativeInfinity(top) ? (float?)null : foot.position.y - top,
                                lowestNearbyVertexGap = sole.Length != 1 || float.IsNegativeInfinity(soleTop) ? (float?)null : sole[0].y - soleTop });
                        }
                        var views = new[] { new Vector3(2, .6f, 0), new Vector3(1.6f, 1.1f, 1.8f), new Vector3(0, .7f, -2) };
                        bool capture = abort ? frame % 15 == 7 : !flight || frame % 4 == 0 || Math.Abs(time - (float)flightPlan["touch"]) < .01f || frame == times.Length - 1;
                        for (int i = 0; capture && i < (flight ? 1 : views.Length); i++)
                        {
                            camera.transform.position = views[i]; camera.transform.LookAt(new Vector3(0, flight ? .5f : .3f, -.08f));
                            camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); image.Apply();
                            File.WriteAllBytes(directory + "/" + (abort ? "stop" + (float)frames[frame]["abortAt"] + "-" : "") + "t" + time + "-view" + i + ".png", image.EncodeToPNG());
                        }
                        results.Add(new { time, abortAt = abort ? (float?)frames[frame]["abortAt"] : null, mountPhase, insideVertices, deepestVertex, position = new[] { pome.transform.position.x, pome.transform.position.y, pome.transform.position.z },
                            rotation = new[] { pome.transform.rotation.x, pome.transform.rotation.y, pome.transform.rotation.z, pome.transform.rotation.w }, contacts });
                    }
                    finally { UnityEngine.Object.DestroyImmediate(lab); UnityEngine.Object.DestroyImmediate(pome); }
                }
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (var viewObject in viewObjects) if (viewObject != null) UnityEngine.Object.DestroyImmediate(viewObject);
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var mesh in baked) UnityEngine.Object.DestroyImmediate(mesh);
                if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                CheckMountStudySource(source, dirty, roots, descriptor, network, p, matrices);
            }
            File.WriteAllText(directory + "/contacts.json", JsonConvert.SerializeObject(new { checkedAt = DateTime.UtcNow.ToString("o"),
                sourcePreserved = true, previewClosed = !scene.IsValid(), position = new[] { 0f, .366f, -.05f }, yaw = 180, pitch = -9,
                inputSha256 = flight ? Hash("Logs/pumpkin-setup-staging/mount-candidate.json") : null,
                attachmentSha256 = flight ? Hash(Evidence + "/mount-pose-study/contacts.json") : null,
                inspectionSourceSha256 = Hash("Assets/KominkaHillside/Imported/KominkaDogRun/Editor/KD_PumpkinPreviewEditor.MountInspection.cs"),
                hopClipSha256 = animated ? Hash(MountHopPath) : null,
                abortSourceSha256 = abort ? Hash(Evidence + "/mount-flight-animated-study/contacts.json") : null,
                runtimeSourceSha256 = abort ? Hash("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.CandyMount.cs") : null,
                attachmentPath, attachmentPosition = new[] { attachmentPosition.x, attachmentPosition.y, attachmentPosition.z },
                attachmentRotation = new[] { attachmentRotation.x, attachmentRotation.y, attachmentRotation.z, attachmentRotation.w },
                scope = abort ? "Six cancellation times, 15 discrete poses per 1.4s return arc. Geometry with continued hop time only; no actual Udon cancellation, pause or Animator transition acceptance."
                    : animated ? "55 discrete samples with authored hop clip and back attachment. Actual Animator/motor, continuous collisions and interruption recovery remain unverified."
                    : flight ? "55 discrete samples of candidate root flight with standing pose and back attachment. No airborne leg animation or actual motor validation. Vertical skin surface probes are conservative, not triangle intersection."
                    : "Four sampled standing poses. Nearby lowest vertices supplement foot bone probes; not a full sole manifold or continuous mount validation.", results }, Formatting.Indented));
            return abort ? "Mount abort study: 6 stops, 90 candidate frames measured; inspect contacts; source preserved."
                : flight ? "Mount flight study: 55 candidate frames measured; source preserved." : "Mount pose study: 12 images and 16 foot surface probes; source preserved.";
        }

        private static Newtonsoft.Json.Linq.JToken[] MountAbortFrames(Newtonsoft.Json.Linq.JObject plan)
        {
            var source = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Evidence + "/mount-flight-animated-study/contacts.json"));
            if ((string)source["hopClipSha256"] != Hash(MountHopPath)
                || (string)source["inputSha256"] != Hash("Logs/pumpkin-setup-staging/mount-candidate.json") || !(bool)source["sourcePreserved"])
                throw new InvalidOperationException("Abort study needs matching completed flight evidence.");
            var frames = new List<Newtonsoft.Json.Linq.JToken>();
            // Local coordinates of the measured Lab, whose positive Z faces the
            // takeoff point. Runtime uses the corresponding world-space endpoints.
            var origin = plan["supportOrigin"].ToObject<float[]>();
            var takeoff = plan["takeoff"].ToObject<float[]>(); var landing = plan["landing"].ToObject<float[]>();
            Vector3 front = new Vector3(-(takeoff[0] - origin[0]), takeoff[1], -(takeoff[2] - origin[2]));
            Vector3 rear = new Vector3(-(landing[0] - origin[0]), landing[1], -(landing[2] - origin[2]));
            foreach (float stop in new[] { 104.3f, 104.8f, 105.4f, 106.6f, 108.2f, 108.8f })
            {
                var start = source["results"].Single(r => Mathf.Abs((float)r["time"] - stop) < .001f);
                var p = start["position"].ToObject<float[]>(); var q = start["rotation"].ToObject<float[]>();
                var from = new Vector3(p[0], p[1], p[2]); var rotation = new Quaternion(q[0], q[1], q[2], q[3]);
                var to = Vector3.Distance(from, front) < Vector3.Distance(from, rear) ? front : rear;
                int phase = (int)start["mountPhase"];
                float pose = phase == 1 ? Mathf.InverseLerp((float)plan["start"], (float)plan["touch"], stop)
                    : phase == 3 ? Mathf.InverseLerp((float)plan["leave"], (float)plan["land"], stop) : 0;
                for (int i = 0; i <= 14; i++)
                {
                    float u = i / 14f, e = Mathf.SmoothStep(0, 1, u);
                    var position = Vector3.Lerp(from, to, e);
                    position.y += Mathf.Max((float)plan["ascentArc"], (float)plan["descentArc"]) * Mathf.Sin(Mathf.PI * u);
                    var rot = Quaternion.Slerp(rotation, Quaternion.Euler(0, 180, 0), e);
                    frames.Add(Newtonsoft.Json.Linq.JObject.FromObject(new { time = stop + i * .1f, abortAt = stop, phase = 4,
                        hopTime = Mathf.Lerp(pose, 1, u) * 1.4f, position = new[] { position.x, position.y, position.z }, rotation = new[] { rot.x, rot.y, rot.z, rot.w } }));
                }
            }
            return frames.ToArray();
        }

        private static void CheckMountStudySource(Scene source, bool dirty, int[] roots,
            VRC.SDK3.Components.VRCSceneDescriptor descriptor, int[] network, KD_CandyDogPerformance p, Matrix4x4[] matrices)
        {
            if (source.isDirty != dirty || !roots.SequenceEqual(source.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(i => i))
                || !network.SequenceEqual(descriptor.NetworkIDCollection.Select(n => n?.gameObject == null ? 0 : n.gameObject.GetInstanceID()))
                || p.dogs.Where((d, i) => d.dogRoot.localToWorldMatrix != matrices[i]).Any())
                throw new InvalidOperationException("Mount study changed source scene state.");
            VerifySource();
        }

        private static GameObject MountStudyClone(KD_CandyDogPerformance p, string key, bool lying, float time, Scene scene, AnimationClip animationOverride = null)
        {
            var def = KD_DogDefs.All.Single(d => d.key == key);
            var source = p.dogs.Single(d => d.dogRoot.name == "KD_Dog_" + key).dogRoot;
            var clone = UnityEngine.Object.Instantiate(source.gameObject);
            try
            {
                SceneManager.MoveGameObjectToScene(clone, scene);
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.transform.localScale = source.lossyScale; clone.SetActive(true);
                foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("KD_Acc_", StringComparison.Ordinal) || t.name.StartsWith("KD_CandyCostume_", StringComparison.Ordinal)) t.gameObject.SetActive(false);
                var clip = animationOverride != null ? animationOverride : lying ? KD_CallPoseEditor.PoseClip(def, 1) : AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(def.idle.guid));
                if (clip == null) throw new InvalidOperationException("Mount study clip missing.");
                clip.SampleAnimation(clone, Mathf.Min(time, clip.length)); KD_DogPreviewEditor.ApplyConstraints(clone);
                return clone;
            }
            catch { UnityEngine.Object.DestroyImmediate(clone); throw; }
        }
    }
}
