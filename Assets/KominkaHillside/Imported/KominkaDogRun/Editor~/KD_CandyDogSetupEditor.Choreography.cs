using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using UdonSharpEditor;

namespace KominkaDogRun.Editor
{
    public static partial class KD_CandyDogSetupEditor
    {
        [Serializable] private class MotionPlan
        {
            public string[] dogKeys;
            public string audioSha256;
            public float duration;
            public float[] motionTimes;
            public Vector3[] motionPositions;
            public float[] motionYaws;
            public int[] motionPoses;
            public int[] motionPartners;
            public float[] motionSpeedLimits;
            public MountPlan mount;
        }
        [Serializable] private class LightBinding
        {
            public string actorName;
            public int dogIndex;
            public Vector3 referenceDogOrigin;
            public string[] layerNames;
        }
        [Serializable] private class LightBindingFile { public string choreographySha256, audioSha256; public LightBinding[] bindings; }

        public static void SetupChoreography(KD_CandyDogPerformance p, string versionDirectory)
        {
            GuardPreview(p.gameObject);
            if (p.gameObject.scene.path != KD_CandyPreviewEditor.ScenePath002
                && p.gameObject.scene.path != KD_PumpkinPreviewEditor.ScenePath)
                throw new InvalidOperationException("Choreography setup is revision 002 only; keep 001 untouched.");
            var plan = JsonConvert.DeserializeObject<MotionPlan>(File.ReadAllText(versionDirectory + "/choreography.json"));
            var bindings = JsonConvert.DeserializeObject<LightBindingFile>(File.ReadAllText(versionDirectory + "/dog-light-bindings.json"));
            if (plan == null || plan.dogKeys == null || !plan.dogKeys.SequenceEqual(DogKeys)
                || Math.Abs(plan.duration - p.duration) > .001f) throw new InvalidOperationException("Choreography identity/duration differs");
            ValidateMotion(plan, p);
            if (bindings?.bindings == null || bindings.bindings.Length == 0) throw new InvalidOperationException("No dog-light bindings");
            var layers = new List<Transform>(); var identities = new List<int>();
            var origins = new List<Vector3>(); var baselines = new List<Vector3>();
            var stage = p.director.transform;
            var timeline = p.director.playableAsset as TimelineAsset;
            if (timeline == null) throw new InvalidOperationException("Timeline missing");
            var audio = timeline.GetOutputTracks().OfType<AudioTrack>().SelectMany(t => t.GetClips())
                .Select(c => (c.asset as AudioPlayableAsset)?.clip).Where(c => c != null).ToArray();
            if (bindings.choreographySha256 != MotionHash(versionDirectory + "/choreography.json")
                || string.IsNullOrEmpty(plan.audioSha256) || bindings.audioSha256 != plan.audioSha256
                || audio.Length != 1 || MotionHash(AssetDatabase.GetAssetPath(audio[0])) != plan.audioSha256)
                throw new InvalidOperationException("Choreography/bindings/actual Timeline audio hashes differ");
            var animatedChildren = new HashSet<Transform>();
            foreach (var track in timeline.GetOutputTracks().OfType<AnimationTrack>())
            {
                var clips = track.GetClips().Select(c => (c.asset as AnimationPlayableAsset)?.clip).Where(c => c != null).ToList();
                if (track.infiniteClip != null) clips.Add(track.infiniteClip);
                UnityEngine.Object bound = p.director.GetGenericBinding(track);
                for (var parent = track.parent as TrackAsset; bound == null && parent != null; parent = parent.parent as TrackAsset)
                    bound = p.director.GetGenericBinding(parent);
                Transform animationRoot = bound is GameObject go ? go.transform : (bound as Component)?.transform;
                foreach (var clip in clips)
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        if (binding.type == typeof(Transform))
                        {
                            if (animationRoot == null) throw new InvalidOperationException("Cannot resolve Transform animation root: " + track.name);
                            var animated = string.IsNullOrEmpty(binding.path) ? animationRoot : animationRoot.Find(binding.path);
                            if (animated != null) animatedChildren.Add(animated);
                        }
            }
            foreach (var b in bindings.bindings)
            {
                if (b.dogIndex < 0 || b.dogIndex >= 6 || string.IsNullOrEmpty(b.actorName)
                    || b.actorName.Contains("/") || b.layerNames == null || b.layerNames.Length == 0)
                    throw new InvalidOperationException("Invalid dog-light identity or actor name");
                var actor = stage.Find(b.actorName);
                if (actor == null || actor.parent != stage || Quaternion.Angle(actor.localRotation, Quaternion.identity) > .01f
                    || Vector3.Distance(actor.localScale, Vector3.one) > .001f)
                    throw new InvalidOperationException("Light actor must remain direct stage child with identity rotation/scale: " + b.actorName);
                if (Vector3.Distance(b.referenceDogOrigin, plan.motionPositions[b.dogIndex]) > .001f)
                    throw new InvalidOperationException("Light reference origin differs from initial dog position");
                foreach (var layerName in b.layerNames)
                {
                    if (string.IsNullOrEmpty(layerName) || layerName.Contains("/")) throw new InvalidOperationException("Expected direct PS child name");
                    var layer = actor.Find(layerName);
                    if (layer == null || layer.parent != actor || layer.GetComponent<ParticleSystem>() == null || layers.Contains(layer)
                        || animatedChildren.Contains(layer))
                        throw new InvalidOperationException("Missing, duplicate, or Transform-animated dog PS layer: " + b.actorName + "/" + layerName);
                    int old = p.dogLightLayers == null ? -1 : Array.IndexOf(p.dogLightLayers, layer);
                    layers.Add(layer); identities.Add(b.dogIndex); origins.Add(b.referenceDogOrigin);
                    baselines.Add(old >= 0 && p.dogLightBaselines != null && old < p.dogLightBaselines.Length ? p.dogLightBaselines[old] : layer.localPosition);
                }
            }
            // All identities, numeric inputs, clearances and layers validate before the first mutation.
            p.choreographySpace = stage;
            p.motionTimes = plan.motionTimes; p.motionPositions = plan.motionPositions; p.motionYaws = plan.motionYaws;
            p.motionPoses = plan.motionPoses; p.motionPartners = plan.motionPartners; p.motionSpeedLimits = plan.motionSpeedLimits;
            p.dogLightLayers = layers.ToArray(); p.dogLightDogs = identities.ToArray();
            p.dogLightOrigins = origins.ToArray(); p.dogLightBaselines = baselines.ToArray();
            p.useChoreography = true;
            SetupMount(p, plan.mount);
            UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
        }

        private static void ValidateMotion(MotionPlan q, KD_CandyDogPerformance p)
        {
            if (q.motionTimes == null || q.motionTimes.Length < 2 || q.motionTimes.Length > 2000) throw new InvalidOperationException("Motion key count invalid");
            int n = q.motionTimes.Length * 6;
            if (q.motionPositions == null || q.motionPositions.Length != n || q.motionYaws == null || q.motionYaws.Length != n
                || q.motionPoses == null || q.motionPoses.Length != n || q.motionPartners == null || q.motionPartners.Length != n
                || q.motionSpeedLimits == null || q.motionSpeedLimits.Length != n) throw new InvalidOperationException("Motion arrays must all be K*6");
            if (Math.Abs(q.motionTimes[0]) > .001 || Math.Abs(q.motionTimes[q.motionTimes.Length - 1] - p.duration) > .001)
                throw new InvalidOperationException("Motion must cover audio 0 through duration");
            ValidateMount(q, p);
            for (int k = 0; k < q.motionTimes.Length; k++)
            {
                if (!Finite(q.motionTimes[k]) || k > 0 && q.motionTimes[k] <= q.motionTimes[k - 1]) throw new InvalidOperationException("Motion times not finite/strictly ascending");
                for (int d = 0; d < 6; d++)
                {
                    int i = k * 6 + d; var v = q.motionPositions[i];
                    if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z) || !Finite(q.motionYaws[i])
                        || !Finite(q.motionSpeedLimits[i]) || q.motionSpeedLimits[i] <= 0 || q.motionSpeedLimits[i] > p.dogs[d].runSpeed
                        || q.motionPoses[i] < 0 || q.motionPoses[i] > 3 || q.motionPartners[i] < -1 || q.motionPartners[i] >= 6 || q.motionPartners[i] == d)
                        throw new InvalidOperationException("Invalid motion value at key " + k + " dog " + DogKeys[d]);
                    if (k > 0 && q.motionPoses[i - 6] == 3)
                    {
                        if (Vector3.Distance(v, q.motionPositions[i - 6]) > .001f
                            || Math.Abs(Mathf.DeltaAngle(q.motionYaws[i], q.motionYaws[i - 6])) > .01f)
                            throw new InvalidOperationException("A lying dog must keep its target and yaw fixed: " + DogKeys[d]);
                        if (q.motionPoses[i] != 3)
                        {
                            // Include the motor's look-ahead so it cannot anticipate movement
                            // or rotation while the LieDown exit animation is still running.
                            float holdUntil = q.motionTimes[k] + .8f + p.motionLookAhead;
                            if (q.motionTimes[q.motionTimes.Length - 1] < holdUntil)
                                throw new InvalidOperationException("LieDown exit needs a full rise interval before the end.");
                            for (int f = k + 1; f < q.motionTimes.Length && q.motionTimes[f - 1] < holdUntil; f++)
                                if (Vector3.Distance(q.motionPositions[f * 6 + d], v) > .001f
                                    || Math.Abs(Mathf.DeltaAngle(q.motionYaws[f * 6 + d], q.motionYaws[i])) > .01f
                                    || q.motionPoses[(f - 1) * 6 + d] != 0)
                                    throw new InvalidOperationException("LieDown exit must hold standing position and yaw before the next action: " + DogKeys[d]);
                        }
                    }
                    if (k == 0 && Vector3.Distance(p.director.transform.TransformPoint(v), p.slots[d].position) > .08f)
                        throw new InvalidOperationException("Initial choreography does not match preserved slot: " + DogKeys[d]);
                    if (k > 0 && Vector3.Distance(v, q.motionPositions[i - 6]) / (q.motionTimes[k] - q.motionTimes[k - 1]) > q.motionSpeedLimits[i - 6] + .02f)
                        throw new InvalidOperationException("Authored route exceeds physical speed cap at key " + k + " dog " + DogKeys[d]);
                }
            }
            // Exact minimum distance on every simultaneous linear segment, not just frame endpoints.
            for (int k = 0; k < q.motionTimes.Length - 1; k++)
            for (int d = 0; d < 6; d++) for (int e = d + 1; e < 6; e++)
            {
                // This exact pair/interval passed the dedicated support/flight gate.
                // Every other dog pair retains the ordinary ground clearance test.
                if (MountPairSegment(q.mount, d, e, q.motionTimes[k], q.motionTimes[k + 1])) continue;
                Vector3 a = q.motionPositions[k * 6 + d] - q.motionPositions[k * 6 + e]; a.y = 0;
                Vector3 b = q.motionPositions[(k + 1) * 6 + d] - q.motionPositions[(k + 1) * 6 + e]; b.y = 0;
                Vector3 change = b - a;
                float u = change.sqrMagnitude < .000001f ? 0 : Mathf.Clamp01(-Vector3.Dot(a, change) / change.sqrMagnitude);
                float radius = Radius(p.dogs[d]) + Radius(p.dogs[e]) + .22f;
                if ((a + change * u).magnitude < radius)
                    throw new InvalidOperationException("Choreography crosses dog clearance at " + q.motionTimes[k] + " " + DogKeys[d] + "/" + DogKeys[e]);
            }
        }
        private static float Radius(KD_DogWanderController dog)
        {
            if (dog.pack == null || dog.pack.dogRadius == null || dog.dogIndex < 0 || dog.dogIndex >= dog.pack.dogRadius.Length)
                throw new InvalidOperationException("Missing measured dog radius");
            return dog.pack.dogRadius[dog.dogIndex];
        }
        private static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }
        private static string MotionHash(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }

        public static string VerifyChoreography(KD_CandyDogPerformance p)
        {
            GuardPreview(p.gameObject);
            if (!p.useChoreography || p.choreographySpace != p.director.transform) throw new InvalidOperationException("002 choreography is not enabled/bound");
            ValidateMotion(new MotionPlan { duration = p.duration, motionTimes = p.motionTimes, motionPositions = p.motionPositions,
                motionYaws = p.motionYaws, motionPoses = p.motionPoses, motionPartners = p.motionPartners, motionSpeedLimits = p.motionSpeedLimits,
                mount = MountFromRuntime(p) }, p);
            VerifyMountWiring(p);
            if (p.dogLightLayers.Length == 0 || p.dogLightLayers.Length != p.dogLightDogs.Length || p.dogLightLayers.Length != p.dogLightOrigins.Length
                || p.dogLightLayers.Length != p.dogLightBaselines.Length) throw new InvalidOperationException("Dog light layer arrays differ");
            for (int i = 0; i < p.dogLightLayers.Length; i++)
                if (p.dogLightLayers[i] == null || p.dogLightDogs[i] < 0 || p.dogLightDogs[i] >= 6
                    || p.dogLightLayers[i].parent == null || p.dogLightLayers[i].parent.parent != p.choreographySpace)
                    throw new InvalidOperationException("Dog light layer hierarchy/identity mismatch");
            return "Choreography PASS: " + p.motionTimes.Length + " time keys, 6 physical dog routes, exact linear-segment pair clearance, "
                + p.dogLightLayers.Length + " identity-bound PS child layers. Ground/obstacles and actual locomotion require separate checks.";
        }

        public static string SurveyChoreographyGround(KD_CandyDogPerformance p)
        {
            GuardPreview(p.gameObject);
            if (!p.useChoreography) throw new InvalidOperationException("Wire choreography first");
            return SurveyMotionGround(p, p.motionTimes, p.motionPositions, MountFromRuntime(p));
        }

        public static string InspectChoreographySource(KD_CandyDogPerformance p, string versionDirectory)
        {
            GuardPreview(p.gameObject);
            var q = JsonConvert.DeserializeObject<MotionPlan>(File.ReadAllText(versionDirectory + "/choreography.json"));
            if (q == null || q.dogKeys == null || !q.dogKeys.SequenceEqual(DogKeys) || Math.Abs(q.duration - p.duration) > .001f)
                throw new InvalidOperationException("Source choreography identity differs.");
            ValidateMotion(q, p);
            return "Source motion numeric/pose/clearance checks PASS; current scene was not modified.\n"
                + SurveyMotionGround(p, q.motionTimes, q.motionPositions, q.mount);
        }

        private static string SurveyMotionGround(KD_CandyDogPerformance p, float[] times, Vector3[] positions, MountPlan mount)
        {
            var report = new StringBuilder("Choreography ground survey; finite planned path only, not foot-contact acceptance\n");
            int miss = 0, high = 0, count = 0;
            for (int k = 0; k < times.Length; k += 3) for (int d = 0; d < 6; d++)
            {
                if (mount != null && mount.enabled && d == mount.rider && times[k] > mount.start && times[k] < mount.land) continue;
                var world = p.director.transform.TransformPoint(positions[k * 6 + d]);
                var hits = Physics.RaycastAll(world + Vector3.up * 2f, Vector3.down, 4f, p.dogs[d].groundLayerMask, QueryTriggerInteraction.Ignore)
                    .Where(h => !p.dogs.Any(dog => h.collider.transform.IsChildOf(dog.dogRoot))).OrderBy(h => h.distance).ToArray();
                count++;
                if (hits.Length == 0) { miss++; report.AppendLine("MISSING " + times[k] + " " + DogKeys[d] + " " + world); }
                else if (Math.Abs(hits[0].point.y - world.y) > .18f)
                { high++; report.AppendLine("HEIGHT " + times[k] + " " + DogKeys[d] + " y=" + hits[0].point.y + " " + hits[0].collider.name); }
            }
            report.AppendLine("Samples=" + count + " no-ground=" + miss + " height-mismatch=" + high + ". Obstacle side clearance still needs world inspection.");
            if (mount != null && mount.enabled) ValidateMountFloor(p, mount);
            if (miss != 0 || high != 0) throw new InvalidOperationException(report.ToString());
            return report.ToString();
        }
    }
}
