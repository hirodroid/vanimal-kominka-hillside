using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    // IMP-DOG-005 prototype scene: flat ground, one dog (Lab, copied from the port source without its
    // walk/fetch/pet systems), one disc, a fixed-throw stand and a recall button. Everything here is
    // code-owned and rebuilt on every Build; nothing in the hillside world scene is touched.
    public static class KH_FrisbeePrototypeEditor
    {
        public const string ScenePath = "Assets/Scenes/KH_FrisbeePrototype.unity";
        // Shown near the aim marker when the holder stands or aims outside the dog's flat (font glyphs via Menu/2).
        public const string AimHint = "広場の中で遊ぼう";
        const string Data = "Assets/KominkaHillside/Data/Frisbee";
        static readonly string[] Programs = { "KH_Frisbee", "KH_FrisbeeCatch", "KH_FrisbeeLauncher" };
        const int DogLayer = 14, PickupLayer = 13;

        static void Report(string name, string text) { Directory.CreateDirectory(KH_FrisbeeJumpClipEditor.Output); File.WriteAllText(KH_FrisbeeJumpClipEditor.Output + "/" + name + ".txt", text); Debug.Log("[KH Frisbee] " + text); }

        [MenuItem("Tools/KominkaHillside/Frisbee/1 Create Programs")]
        public static void CreatePrograms()
        {
            foreach (string name in Programs)
            {
                string path = "Assets/KominkaHillside/Scripts/" + name;
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") != null) continue;
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path + ".cs");
                AssetDatabase.CreateAsset(asset, path + ".asset");
            }
            AssetDatabase.SaveAssets(); UdonSharpProgramAsset.CompileAllCsPrograms(true);
            Report("programs", "Requested U# compile; confirm completion and errors before Build Scene.");
        }

        static T Add<T>(GameObject go, bool manual = false) where T : UdonSharpBehaviour
        {
            var b = go.GetComponent<T>(); if (b == null) b = go.AddUdonSharpComponent<T>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            backing.SyncMethod = manual ? Networking.SyncType.Manual : Networking.SyncType.None;
            EditorUtility.SetDirty(backing); return b;
        }

        static Material Mat(string name, Color color)
        {
            Directory.CreateDirectory(Data);
            string path = Data + "/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("VRChat/Mobile/Standard Lite")); m.color = color; AssetDatabase.CreateAsset(m, path); }
            return m;
        }

        static GameObject Obj(string name, Transform parent = null)
        {
            var t = parent == null ? SceneManager.GetActiveScene().GetRootGameObjects().Select(g => g.transform).FirstOrDefault(x => x.name == name) : parent.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name); if (parent != null) go.transform.SetParent(parent, false); return go;
        }

        static GameObject Prim(string name, PrimitiveType type, Transform parent)
        {
            var t = parent == null ? null : parent.Find(name);
            if (t != null) return t.gameObject;
            var go = GameObject.CreatePrimitive(type); go.name = name; if (parent != null) go.transform.SetParent(parent, false); return go;
        }

        // The prototype scene is opened (or created) additively and saved on its own, so an unsaved world
        // scene is never touched. Returns the previously active scene so callers can restore it.
        static bool _openedHere;
        static Scene OpenPrototype(bool create)
        {
            var previous = SceneManager.GetActiveScene();
            Scene proto = SceneManager.GetSceneByPath(ScenePath);
            _openedHere = !(proto.IsValid() && proto.isLoaded);
            if (_openedHere)
            {
                if (File.Exists(ScenePath)) proto = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                else if (create) { proto = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive); EditorSceneManager.SaveScene(proto, ScenePath); }
                else throw new InvalidOperationException("Prototype scene missing; run 4 Build Scene first.");
            }
            SceneManager.SetActiveScene(proto);
            return previous;
        }

        // Only a scene opened by this call is closed again (review M1). A prototype scene the user already had
        // open stays open, active scene restored, dirty state untouched.
        static void ClosePrototype(Scene previous, Scene proto)
        {
            if (previous.IsValid() && previous.isLoaded && previous != proto) SceneManager.SetActiveScene(previous);
            if (_openedHere && previous != proto) EditorSceneManager.CloseScene(proto, true);
            _openedHere = false;
        }

        [MenuItem("Tools/KominkaHillside/Frisbee/4 Build Scene")]
        public static void BuildScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var previous = OpenPrototype(true);
            var scene = SceneManager.GetActiveScene();
            try { BuildInto(scene); }
            finally { ClosePrototype(previous, scene); }
        }

        static void BuildInto(Scene scene)
        {
            var facts = ReadFacts();

            // World: descriptor, spawn, flat ground, one wall for the "hits a wall" case.
            var world = Obj("KH_FrisbeeWorld");
            var desc = world.GetComponent<VRCSceneDescriptor>(); if (desc == null) desc = world.AddComponent<VRCSceneDescriptor>();
            var spawn = Obj("Spawn", world.transform).transform; spawn.position = new Vector3(0f, 0f, -6f); spawn.rotation = Quaternion.identity;
            desc.spawns = new[] { spawn }; desc.RespawnHeightY = -20f; desc.spawnOrder = VRC_SceneDescriptor.SpawnOrder.First;
            var ground = Prim("Ground", PrimitiveType.Plane, world.transform); ground.transform.localScale = Vector3.one * 8f; ground.isStatic = true;
            ground.GetComponent<Renderer>().sharedMaterial = Mat("KH_FrisbeeGround", new Color(.45f, .58f, .32f));
            var wall = Prim("Wall", PrimitiveType.Cube, world.transform); wall.transform.position = new Vector3(0f, 1.25f, 22f); wall.transform.localScale = new Vector3(16f, 2.5f, .3f); wall.isStatic = true;
            wall.GetComponent<Renderer>().sharedMaterial = Mat("KH_FrisbeeWall", new Color(.6f, .55f, .5f));

            // Dog: copy of the port-source Lab, without walk/fetch/pet behaviours (they belong to the world scene).
            var dogs = Obj("KH_FrisbeeDogs");
            var dogT = dogs.transform.Find("KD_Dog_Lab");
            if (dogT == null)
            {
                var dog = KH_FrisbeeJumpClipEditor.CloneSourceDog(scene);
                dog.transform.SetParent(dogs.transform, true); dogT = dog.transform;
                foreach (var u in dog.GetComponentsInChildren<UdonSharpBehaviour>(true)) UnityEngine.Object.DestroyImmediate(u);
                foreach (var u in dog.GetComponentsInChildren<UdonBehaviour>(true)) UnityEngine.Object.DestroyImmediate(u);
                foreach (var t in dog.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
            dogT.SetPositionAndRotation(new Vector3(1.5f, 0f, 6.5f), Quaternion.Euler(0f, 180f, 0f)); // beside where the fixed throw comes down (z 6-8), watching the stand (user, 2026-09-21)
            foreach (var t in dogT.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = DogLayer;
            if (dogT.GetComponent<VRCObjectSync>() == null) dogT.gameObject.AddComponent<VRCObjectSync>();
            var animator = dogT.GetComponentInChildren<Animator>(true);
            if (animator == null) throw new InvalidOperationException("Dog has no Animator");
            var mouth = dogT.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == KH_FrisbeeJumpClipEditor.MouthAnchorName);
            if (mouth == null) throw new InvalidOperationException("Dog has no " + KH_FrisbeeJumpClipEditor.MouthAnchorName);

            // Stand (fixed throw) and recall button.
            var props = Obj("KH_FrisbeeProps");
            var stand = Prim("KH_FrisbeeStand", PrimitiveType.Cube, props.transform);
            stand.transform.position = new Vector3(0f, .5f, -4f); stand.transform.localScale = new Vector3(.4f, 1f, .4f);
            stand.GetComponent<Renderer>().sharedMaterial = Mat("KH_FrisbeeStand", new Color(.35f, .25f, .18f));
            var muzzle = Obj("Muzzle", stand.transform).transform; muzzle.localPosition = new Vector3(0f, .75f, 0f); muzzle.localRotation = Quaternion.identity; muzzle.localScale = new Vector3(2.5f, 1f, 2.5f);
            var recall = Prim("KH_FrisbeeRecall", PrimitiveType.Cube, props.transform);
            recall.transform.position = new Vector3(-.8f, .35f, -4f); recall.transform.localScale = new Vector3(.3f, .7f, .3f);
            recall.GetComponent<Renderer>().sharedMaterial = Mat("KH_FrisbeeRecall", new Color(.25f, .35f, .5f));

            // Disc + catcher: the same helpers place the Lab disc in the world scene (KH_DogWalkingEditor).
            bool discCreated;
            var disc = BuildDisc(props.transform, "KH_Frisbee", "KH_FrisbeeDisc", new Color(1f, .5f, .15f), out discCreated);
            disc.transform.position = muzzle.position; disc.transform.rotation = Quaternion.identity; // prototype: always on the stand
            var catcher = Add<KH_FrisbeeCatch>(Obj("KH_FrisbeeCatch_Lab", dogs.transform), true);
            ConfigureCatcher(catcher, disc, dogT, animator, mouth, facts);
            var launcher = Add<KH_FrisbeeLauncher>(stand); launcher.frisbee = disc; launcher.catcher = catcher; launcher.muzzle = muzzle; launcher.recallOnly = false;
            UdonSharpEditorUtility.GetBackingUdonBehaviour(launcher).InteractionText = "Throw (fixed)";
            var recaller = Add<KH_FrisbeeLauncher>(recall); recaller.frisbee = disc; recaller.catcher = catcher; recaller.muzzle = muzzle; recaller.recallOnly = true;
            UdonSharpEditorUtility.GetBackingUdonBehaviour(recaller).InteractionText = "Recall disc";

            foreach (var u in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<UdonSharpBehaviour>(true))) { UdonSharpEditorUtility.CopyProxyToUdon(u); EditorUtility.SetDirty(u); }
            int dropped = desc.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0));
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Report("build-scene", "scene=" + ScenePath + " dog=KD_Dog_Lab mouthOffsetCatch=" + facts.mouthOffsetCatch.ToString("F3") + " droppedNetworkIds=" + dropped + "; run 5 Verify.");
        }

        // ---------------------------------------------------------------- shared with the world-scene builder
        // Disc GameObject (Rigidbody / VRCPickup / VRCObjectSync / KH_Frisbee) plus its aim preview objects
        // ("<name>_Aim" beside it) and the held (see-through) material. Idempotent by name; 'created' tells the
        // caller whether to place it (placement is human-owned once it exists; review M4).
        public static KH_Frisbee BuildDisc(Transform parent, string name, string materialName, Color color, out bool created)
        {
            var existing = parent.Find(name);
            created = existing == null;
            var discGo = Prim(name, PrimitiveType.Cylinder, parent);
            discGo.layer = PickupLayer; discGo.transform.localScale = new Vector3(.24f, .012f, .24f);
            var cap = discGo.GetComponent<CapsuleCollider>(); if (cap != null) UnityEngine.Object.DestroyImmediate(cap);
            var box = discGo.GetComponent<BoxCollider>(); if (box == null) box = discGo.AddComponent<BoxCollider>(); box.size = new Vector3(1f, 2f, 1f);
            var discMat = Mat(materialName, color);
            discGo.GetComponent<Renderer>().sharedMaterial = discMat;
            var rb = discGo.GetComponent<Rigidbody>(); if (rb == null) rb = discGo.AddComponent<Rigidbody>();
            rb.mass = .15f; rb.drag = .4f; rb.angularDrag = 1f; rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            var pickup = discGo.GetComponent<VRCPickup>(); if (pickup == null) pickup = discGo.AddComponent<VRCPickup>();
            pickup.proximity = 1f; pickup.InteractionText = "Disc"; pickup.orientation = VRC_Pickup.PickupOrientation.Any; pickup.ThrowVelocityBoostScale = 1.5f;
            if (discGo.GetComponent<VRCObjectSync>() == null) discGo.AddComponent<VRCObjectSync>();
            var disc = Add<KH_Frisbee>(discGo);
            // Aim preview: a line along the predicted path and a landing marker, shown to the holder only.
            var aimRoot = Obj(name + "_Aim", parent);
            var lineGo = Obj("AimLine", aimRoot.transform);
            var line = lineGo.GetComponent<LineRenderer>(); if (line == null) line = lineGo.AddComponent<LineRenderer>();
            var aimMat = Mat("KH_FrisbeeAim", new Color(.3f, .9f, 1f, .8f));
            line.sharedMaterial = aimMat; line.useWorldSpace = true; line.startWidth = .03f; line.endWidth = .015f; line.positionCount = 0; line.enabled = false;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            var marker = Prim("AimMarker", PrimitiveType.Cylinder, aimRoot.transform);
            var mc = marker.GetComponent<Collider>(); if (mc != null) UnityEngine.Object.DestroyImmediate(mc);
            marker.transform.localScale = new Vector3(.34f, .004f, .34f); marker.GetComponent<Renderer>().sharedMaterial = aimMat; marker.SetActive(false);
            disc.aimLine = line; disc.aimMarker = marker.transform; disc.aimLaunch = true; disc.aimSpeed = 7.5f; disc.aimSpeedMin = 4f; disc.aimSpeedMax = 11f;
            // Play-area feedback (Codex U/V/P 2026-09-21): grey preview + cross when landing outside, a faint ring
            // of the area while held, a one-line hint. Colours / width / alpha are provisional until Play.
            disc.aimMaterial = aimMat; disc.aimOutsideMaterial = Mat("KH_FrisbeeAimOut", new Color(.45f, .45f, .45f)); disc.aimMarkerRenderer = marker.GetComponent<Renderer>();
            var cross = Obj("Cross", marker.transform); cross.transform.localPosition = Vector3.zero; cross.transform.localScale = Vector3.one;
            foreach (var arm in new[] { "A", "B" })
            {
                var bar = Prim(arm, PrimitiveType.Cube, cross.transform);
                var bc = bar.GetComponent<Collider>(); if (bc != null) UnityEngine.Object.DestroyImmediate(bc);
                // Marker is a 0.34 x 0.004 x 0.34 cylinder; arms are expressed in its local (scaled) space.
                bar.transform.localPosition = new Vector3(0f, 1.5f, 0f); bar.transform.localScale = new Vector3(.8f, 1f, .07f);
                bar.transform.localRotation = Quaternion.Euler(0f, arm == "A" ? 45f : -45f, 0f);
                bar.GetComponent<Renderer>().sharedMaterial = disc.aimOutsideMaterial;
            }
            cross.SetActive(false); disc.aimMarkerCross = cross;
            var ringGo = Obj("AreaRing", aimRoot.transform);
            var ring = ringGo.GetComponent<LineRenderer>(); if (ring == null) ring = ringGo.AddComponent<LineRenderer>();
            string ringPath = Data + "/KH_FrisbeeArea.mat"; var ringMat = AssetDatabase.LoadAssetAtPath<Material>(ringPath);
            if (ringMat == null) { ringMat = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")); ringMat.color = new Color(1f, .95f, .85f, .3f); AssetDatabase.CreateAsset(ringMat, ringPath); }
            ring.sharedMaterial = ringMat; ring.useWorldSpace = true; ring.loop = true; ring.startWidth = .03f; ring.endWidth = .03f; ring.positionCount = 0; ring.enabled = false;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; ring.receiveShadows = false;
            disc.areaRing = ring; disc.areaRingLift = .03f;
            var hintGo = Obj("Hint", aimRoot.transform);
            var hint = hintGo.GetComponent<TMPro.TextMeshPro>(); if (hint == null) hint = hintGo.AddComponent<TMPro.TextMeshPro>();
            hint.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(KH_HandMenuEditor.FontPath); hint.fontSize = 3f; hint.alignment = TMPro.TextAlignmentOptions.Center; hint.color = Color.white;
            hint.enableWordWrapping = false; hint.text = AimHint; hint.rectTransform.sizeDelta = new Vector2(4f, 1f); hintGo.SetActive(false);
            disc.aimHint = hintGo.transform;
            // Held look: the holder sees the disc at 25% so the preview line is not hidden behind it.
            string heldPath = Data + "/" + materialName + "Held.mat"; var held = AssetDatabase.LoadAssetAtPath<Material>(heldPath);
            if (held == null) { held = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")); held.color = new Color(color.r, color.g, color.b, .25f); AssetDatabase.CreateAsset(held, heldPath); }
            disc.discRenderer = discGo.GetComponent<Renderer>(); disc.discMaterial = discMat; disc.discHeldMaterial = held;
            // Flight model: code-owned (the Verify integration check and the fixed throw assume these). Tune in the
            // Inspector during Play, then copy the numbers here; a rebuild restores them.
            disc.gravity = 9.81f; disc.drag = 0.15f; disc.lift = 0.30f; disc.liftCap = 0.95f; disc.maxSpeed = 11f;
            disc.desktopMinThrowSpeed = 6f; disc.desktopMaxThrowSpeed = 11f; disc.desktopThrowRise = 0.25f; // a short press must still fly ~2 s
            var descriptor = UnityEngine.Object.FindObjectOfType<VRCSceneDescriptor>();
            if (descriptor != null) disc.respawnBelowY = descriptor.RespawnHeightY;
            return disc;
        }

        // Pose facts (from the clip JSON) and the code-owned tuning; neighbours / play area are the caller's.
        public static void ConfigureCatcher(KH_FrisbeeCatch catcher, KH_Frisbee disc, Transform dogRoot, Animator animator, Transform mouth, Facts facts)
        {
            catcher.dogRoot = dogRoot; catcher.dogAnimator = animator; catcher.mouthAnchor = mouth; catcher.frisbee = disc;
            catcher.mouthOffsetCatch = facts.mouthOffsetCatch; catcher.mouthOffsetStand = facts.mouthOffsetStand;
            catcher.clipCrouchDuration = facts.crouch; catcher.clipRiseDuration = facts.rise; catcher.clipRecoverDuration = facts.recover;
            catcher.groundLayerMask = (1 << 0) | (1 << 11);
            catcher.takeoffSpeed = 2.5f; catcher.chaseRunSpeed = 4.5f; catcher.standoffDistance = 8f; // code-owned, same rule as the flight model
            catcher.playAreaMargin = 0f; // the ring the holder sees is exactly the acceptance circle (Codex P)
            // Inspector tuning survives a rebuild, except the one value the clip dictates: the smallest rise
            // must keep clip playback at or below 1.8x (review D2), so an older scene value is raised here.
            float riseFloor = 0.5f * disc.gravity * Mathf.Pow(facts.rise / 1.8f, 2f);
            if (catcher.minRootRise < riseFloor) catcher.minRootRise = Mathf.Ceil(riseFloor * 100f) / 100f;
            disc.catcher = catcher;
        }

        public struct Facts { public Vector3 mouthOffsetCatch, mouthOffsetStand; public float crouch, rise, recover, clipLength; }

        public static Facts ReadFacts()
        {
            string path = KH_FrisbeeJumpClipEditor.FactsPath;
            if (!File.Exists(path)) throw new InvalidOperationException("Run 2 Build Jump Clip first (" + path + ")");
            string s = File.ReadAllText(path);
            Vector3 Vec(string key)
            {
                int i = s.IndexOf("\"" + key + "\"", StringComparison.Ordinal); int a = s.IndexOf('[', i), b = s.IndexOf(']', a);
                var parts = s.Substring(a + 1, b - a - 1).Split(',').Select(x => float.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                return new Vector3(parts[0], parts[1], parts[2]);
            }
            float Num(string key)
            {
                int i = s.IndexOf("\"" + key + "\"", StringComparison.Ordinal); int a = s.IndexOf(':', i) + 1; int b = s.IndexOfAny(new[] { ',', '\n', '}' }, a);
                return float.Parse(s.Substring(a, b - a).Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
            return new Facts { mouthOffsetCatch = Vec("mouthOffsetCatch"), mouthOffsetStand = Vec("mouthOffsetStand"), crouch = Num("crouch"), rise = Num("rise"), recover = Num("recover"), clipLength = Num("clipLength") };
        }

        [MenuItem("Tools/KominkaHillside/Frisbee/5 Verify")]
        public static void Verify()
        {
            var log = new StringBuilder(); int fail = 0;
            void Check(bool ok, string msg) { log.AppendLine((ok ? "PASS " : "FAIL ") + msg); if (!ok) fail++; }

            foreach (string name in Programs)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/KominkaHillside/Scripts/" + name + ".asset");
                Check(asset != null && asset.sourceCsScript != null, "program asset " + name);
            }
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(KH_FrisbeeJumpClipEditor.ClipPath);
            Check(clip != null, "clip exists " + KH_FrisbeeJumpClipEditor.ClipPath);
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(KH_FrisbeeJumpClipEditor.ControllerPath);
            Check(ac != null && ac.parameters.Any(p => p.name == "Jump" && p.type == AnimatorControllerParameterType.Bool), "controller has Bool Jump");
            var jump = ac == null ? null : ac.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Jump");
            Check(jump != null && jump.motion == clip, "controller Jump state uses the clip");
            Check(ac != null && ac.layers[0].stateMachine.anyStateTransitions.Any(t => t.destinationState == jump && !t.canTransitionToSelf), "AnyState -> Jump (no self transition)");
            Check(jump != null && jump.transitions.Length == 3, "Jump exits to Run/Walk/Idle");
            if (clip != null)
            {
                Check(Mathf.Abs(clip.length - KH_FrisbeeJumpClipEditor.EndAt) < 0.02f, "clip length " + clip.length.ToString("F3") + " == " + KH_FrisbeeJumpClipEditor.EndAt.ToString("F3"));
                Check(!clip.isLooping, "clip not looping");
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var dog = KH_FrisbeeJumpClipEditor.CloneSourceDog(scene);
                    try
                    {
                        int unresolved = 0, rootCurves = 0;
                        foreach (var b in AnimationUtility.GetCurveBindings(clip))
                        {
                            if (b.path.Length == 0) rootCurves++;
                            else if (dog.transform.Find(b.path) == null) unresolved++;
                            var c = AnimationUtility.GetEditorCurve(clip, b);
                            if (c.keys.Any(k => float.IsNaN(k.value) || float.IsInfinity(k.value))) { unresolved++; }
                        }
                        Check(unresolved == 0, "clip bindings resolve on the dog (unresolved=" + unresolved + ")");
                        Check(rootCurves == 0, "clip has no network-root curves");
                        float minY = 100f, minAir = 100f;
                        var rig0 = KH_DogRig.Resolve(dog, KH_FrisbeeJumpClipEditor.MouthAnchorName);
                        float[] groundTimes = { 0f, 0.15f, KH_FrisbeeJumpClipEditor.Crouch, KH_FrisbeeJumpClipEditor.TouchdownAt, KH_FrisbeeJumpClipEditor.EndAt };
                        foreach (float t in groundTimes)
                        {
                            clip.SampleAnimation(dog, t); KH_DogRig.ApplyConstraints(dog);
                            float y = KH_FrisbeeJumpClipEditor.BodyMinYNoTail(dog, rig0); if (y < minY) minY = y;
                        }
                        Check(minY > -0.02f, "ground keys keep the body (tail excluded) above the floor (min y=" + minY.ToString("F3") + ")");
                        clip.SampleAnimation(dog, KH_FrisbeeJumpClipEditor.CatchAt); KH_DogRig.ApplyConstraints(dog);
                        var rig = KH_DogRig.Resolve(dog, KH_FrisbeeJumpClipEditor.MouthAnchorName);
                        Check(rig.Ok, "rig resolves: " + string.Join("; ", rig.problems));
                        if (rig.Ok)
                        {
                            var facts = ReadFacts();
                            var mouth = rig.mouthAnchor.position - dog.transform.position;
                            Check(Vector3.Distance(mouth, facts.mouthOffsetCatch) < 0.005f, "facts mouthOffsetCatch matches clip (" + mouth.ToString("F3") + ")");
                            Check(facts.mouthOffsetCatch.y > facts.mouthOffsetStand.y - 0.05f, "catch mouth is not lower than standing mouth (catch y=" + facts.mouthOffsetCatch.y.ToString("F3") + " stand y=" + facts.mouthOffsetStand.y.ToString("F3") + ")");
                            minAir = KH_FrisbeeJumpClipEditor.BodyBounds(dog).min.y;
                            log.AppendLine("INFO catch-frame lowest body point y=" + minAir.ToString("F3") + " (airborne; the root arc adds the rise)");
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(dog); }
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }

            // Scene wiring (only when the prototype scene is open; the world scene is never inspected here).
            Scene previousScene = default(Scene); Scene open = default(Scene); bool opened = false;
            if (File.Exists(ScenePath)) { previousScene = OpenPrototype(false); open = SceneManager.GetActiveScene(); opened = true; }
            try
            {
            if (opened)
            {
                var catcher = open.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<KH_FrisbeeCatch>(true)).FirstOrDefault();
                var disc = open.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<KH_Frisbee>(true)).FirstOrDefault();
                var launchers = open.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<KH_FrisbeeLauncher>(true)).ToArray();
                Check(catcher != null && disc != null && launchers.Length == 2, "scene has catcher, disc and two launchers");
                if (catcher != null && disc != null)
                {
                    Check(catcher.dogRoot != null && catcher.dogAnimator != null && catcher.mouthAnchor != null && catcher.frisbee == disc, "catcher wiring");
                    Check(catcher.mouthOffsetCatch.sqrMagnitude > 1e-6f && catcher.clipRiseDuration > 0.05f, "catcher pose facts present");
                    var sceneFacts = ReadFacts(); // JSON == proxy == backing, so a rebuilt clip cannot leave stale facts in the scene (review M3)
                    Check(Vector3.Distance(catcher.mouthOffsetCatch, sceneFacts.mouthOffsetCatch) < 1e-4f && Vector3.Distance(catcher.mouthOffsetStand, sceneFacts.mouthOffsetStand) < 1e-4f
                        && Mathf.Abs(catcher.clipCrouchDuration - sceneFacts.crouch) < 1e-4f && Mathf.Abs(catcher.clipRiseDuration - sceneFacts.rise) < 1e-4f && Mathf.Abs(catcher.clipRecoverDuration - sceneFacts.recover) < 1e-4f,
                        "catcher pose facts equal the clip JSON (rebuild the scene after the clip)");
                    Check(catcher.minRootRise >= 0.5f * disc.gravity * Mathf.Pow(catcher.clipRiseDuration / 1.8f, 2f) - 1e-3f, "minRootRise keeps clip playback <= 1.8x (rise>=" + (0.5f * disc.gravity * Mathf.Pow(catcher.clipRiseDuration / 1.8f, 2f)).ToString("F3") + ")");
                    Check(catcher.dogRoot != null && catcher.dogRoot.GetComponent<VRCObjectSync>() != null, "dog root has VRCObjectSync");
                    Check(catcher.dogAnimator != null && catcher.dogAnimator.runtimeAnimatorController == ac, "dog Animator uses AC_KD_Dog_Lab");
                    Check(catcher.dogRoot != null && catcher.dogRoot.GetComponentsInChildren<UdonBehaviour>(true).Length == 0, "dog subtree carries no world-scene behaviours");
                    Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(catcher).SyncMethod == Networking.SyncType.Manual, "catcher sync=Manual");
                    Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(disc).SyncMethod == Networking.SyncType.None, "disc sync=None (VRCObjectSync carries the transform)");
                    Check(disc.GetComponent<VRCObjectSync>() != null && disc.GetComponent<VRCPickup>() != null && disc.GetComponent<Rigidbody>() != null, "disc has ObjectSync/Pickup/Rigidbody");
                    Check(disc.catcher == catcher, "disc -> catcher");
                    Check(launchers.All(l => l.frisbee == disc && l.muzzle != null && l.catcher == catcher) && launchers.Count(l => l.recallOnly) == 1, "launchers wired (one recall, one throw)");
                    var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(catcher);
                    Check(backing.publicVariables.TryGetVariableValue("dogRoot", out Transform bd) && bd == catcher.dogRoot, "backing heap holds dogRoot");
                    Check(backing.publicVariables.TryGetVariableValue("mouthOffsetCatch", out Vector3 bm) && bm == catcher.mouthOffsetCatch, "backing heap holds mouthOffsetCatch");
                    Check(backing.publicVariables.TryGetVariableValue("clipRiseDuration", out float br) && Mathf.Abs(br - catcher.clipRiseDuration) < 1e-5f, "backing heap holds clipRiseDuration");
                    var desc = open.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<VRCSceneDescriptor>(true)).FirstOrDefault();
                    Check(desc != null && desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null, "scene descriptor with spawn");
                    Check(desc != null && desc.NetworkIDCollection.All(x => x.gameObject != null), "no dangling NetworkIDs");
                    // Closed-form flight against a small-step integration of the same model (the honest check that
                    // prediction == flight is by construction: KH_Frisbee.Update steps _PositionAt with the same facts).
                    float maxErr = 0f;
                    var p0 = new Vector3(0f, 1.3f, 0f);
                    foreach (var v0 in new[] { new Vector3(0f, 1.2f, 8f), new Vector3(3f, 0.4f, 10.5f), new Vector3(0f, 2.5f, 4f) })
                    {
                        Vector3 p = p0, v = v0; float dt = 0.0002f;
                        float vh2 = Mathf.Min(v0.x * v0.x + v0.z * v0.z, disc.liftCap * disc.gravity / disc.lift);
                        for (int i = 1; i <= 15000; i++)
                        {
                            float t = i * dt;
                            var a = new Vector3(-disc.drag * v.x, -disc.gravity + disc.lift * vh2 * Mathf.Exp(-2f * disc.drag * t), -disc.drag * v.z);
                            v += a * dt; p += v * dt;
                            if (i % 1000 == 0) { float err = Vector3.Distance(p, disc._PositionAt(p0, v0, t)); if (err > maxErr) maxErr = err; }
                        }
                    }
                    Check(maxErr < 0.01f, "closed-form flight matches integrated model (max err=" + maxErr.ToString("F4") + " m over 3 s)");
                }
            }
            else log.AppendLine("INFO prototype scene missing; scene wiring not checked");
            }
            finally { if (opened) ClosePrototype(previousScene, open); }
            log.AppendLine("RESULT failures=" + fail);
            Report("verify", log.ToString());
            if (fail > 0) Debug.LogError("[KH Frisbee] verify failures=" + fail);
        }
    }
}
