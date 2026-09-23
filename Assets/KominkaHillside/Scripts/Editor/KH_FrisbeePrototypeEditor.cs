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
            var twistFacts = ReadFacts("Twist");

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
            ConfigureCatcher(catcher, disc, dogT, animator, mouth, facts, twistFacts);
            // IMP-DOG-008 wave 2: the purchased body hangs off the 追従GIMMICK ParentConstraint and trails the root
            // by up to 30 deg / 0.5 m while the dog turns and jumps, which put the real mouth 1.3 m from where the
            // plan (root + rotation * mouthOffsetCatch) put it. Idempotent, so a rebuild on an existing dog is fine.
            KH_FrisbeeJumpClipEditor.MakeBodyRigid(dogT.gameObject);
            var launcher = Add<KH_FrisbeeLauncher>(stand); launcher.frisbee = disc; launcher.catcher = catcher; launcher.muzzle = muzzle; launcher.recallOnly = false;
            UdonSharpEditorUtility.GetBackingUdonBehaviour(launcher).InteractionText = "Throw (fixed)";
            var recaller = Add<KH_FrisbeeLauncher>(recall); recaller.frisbee = disc; recaller.catcher = catcher; recaller.muzzle = muzzle; recaller.recallOnly = true;
            UdonSharpEditorUtility.GetBackingUdonBehaviour(recaller).InteractionText = "Recall disc";
            // IMP-DOG-008: the prototype scene has no hand menu, so a third cube cycles the variant.
            var cycle = Prim("KH_FrisbeeVariant", PrimitiveType.Cube, props.transform);
            cycle.transform.position = new Vector3(-1.6f, .35f, -4f); cycle.transform.localScale = new Vector3(.3f, .7f, .3f);
            cycle.GetComponent<Renderer>().sharedMaterial = Mat("KH_FrisbeeVariant", new Color(.45f, .35f, .22f));
            var cycler = Add<KH_FrisbeeLauncher>(cycle); cycler.frisbee = disc; cycler.catcher = catcher; cycler.muzzle = muzzle; cycler.recallOnly = false; cycler.cycleVariant = true;
            UdonSharpEditorUtility.GetBackingUdonBehaviour(cycler).InteractionText = "Next variant";

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

        // Pose facts (from both clip JSONs) and the code-owned tuning; neighbours / play area are the caller's.
        public static void ConfigureCatcher(KH_FrisbeeCatch catcher, KH_Frisbee disc, Transform dogRoot, Animator animator, Transform mouth, Facts facts, Facts twist)
        {
            catcher.dogRoot = dogRoot; catcher.dogAnimator = animator; catcher.mouthAnchor = mouth; catcher.frisbee = disc;
            catcher.mouthOffsetCatch = facts.mouthOffsetCatch; catcher.mouthOffsetStand = facts.mouthOffsetStand;
            catcher.clipCrouchDuration = facts.crouch; catcher.clipRiseDuration = facts.rise; catcher.clipRecoverDuration = facts.recover;
            // IMP-DOG-008 v4: the twist clip's own facts. The standing mouth is shared (both clips start from idle).
            catcher.mouthOffsetCatchTwist = twist.mouthOffsetCatch;
            catcher.clipCrouchDurationTwist = twist.crouch; catcher.clipRiseDurationTwist = twist.rise; catcher.clipRecoverDurationTwist = twist.recover;
            catcher.groundLayerMask = (1 << 0) | (1 << 11);
            // Code-owned, same rule as the flight model. IMP-DOG-008 round 2: the intercept planner promises to be
            // standing under the catch point in time, and only a sprint keeps that promise (a dog IS fast).
            // Round 4 (PCVR 3 "it is a machine"): the sprint and the snap turn are gone. The hook approach delivers
            // the facing, so the run can be 8 m/s with a visible 12 m/s2 start and a 10 m/s2 roll-out, and the turn
            // rates can be a dog's (240 running / 180 walking / 120 standing in a catch state).
            catcher.chaseRunSpeed = 9.5f; catcher.chaseAcceleration = 14f; catcher.chaseDeceleration = 10f;
            catcher.maxRunAnimScale = 2.5f;
            catcher.noticeDelay = 0.1f; catcher.settleTime = 0.2f; catcher.returnRunSpeed = 4.5f;
            catcher.turnSpeedDegPerSec = 240f; catcher.walkTurnDegPerSec = 180f; catcher.standTurnDegPerSec = 120f;
            catcher.maxRootRise = 0.9f;  // how high the standing jump reaches; also bounds the clip playback below
            catcher.standoffDistance = 8f;
            // IMP-DOG-013 (user 2026-09-22 "受け取ったらすぐ走っていってしまう"): the よしよし window before the
            // stand-off run. Code-owned like the rest of the Ready geometry - the pair only reads as "it waited
            // for me" when both halves agree, so they are not left to drift apart in an Inspector.
            catcher.readyGraceSeconds = 2f; catcher.readyGraceDistance = 2.5f;
            catcher.variant = KH_FrisbeeCatch.VariantNormal; catcher.pendingVariant = -1; // code-owned: every instance starts on the baseline
            catcher.playAreaMargin = 0f; // the ring the holder sees is exactly the acceptance circle (Codex P)
            // Inspector tuning survives a rebuild for everything not written above, except the one value the clip dictates: the smallest rise
            // must keep clip playback at or below 1.8x (review D2), so an older scene value is raised here.
            // Both clips play from the same minRootRise, so the floor follows the longer rise (Codex P: the
            // floor is a height, computed from the clip's rise TIME).
            float riseFloor = 0.5f * disc.gravity * Mathf.Pow(Mathf.Max(facts.rise, twist.rise) / 1.8f, 2f);
            if (catcher.minRootRise < riseFloor) catcher.minRootRise = Mathf.Ceil(riseFloor * 100f) / 100f;
            disc.catcher = catcher;
        }

        // IMP-DOG-013: the pet-resume rule over every state number, shared by both Verify menus. KH_FrisbeeCatch
        // (and KD_DogFetchController through PetAllowed) resumes 撫で exactly where the dog is standing still with
        // nothing queued: Idle, Ready (the stand-off and the grace) and the Drop wait with the disc in its mouth.
        // Everywhere it runs, jumps, carries or picks up, the head belongs to the clip alone.
        public static bool PetStatesOk()
        {
            foreach (int s in new[] { KH_FrisbeeCatch.StateIdle, KH_FrisbeeCatch.StateReady, KH_FrisbeeCatch.StateDrop })
                if (!KH_FrisbeeCatch.PetAllowedIn(s)) return false;
            foreach (int s in new[] { KH_FrisbeeCatch.StateChase, KH_FrisbeeCatch.StateJump, KH_FrisbeeCatch.StateJumpCarry,
                KH_FrisbeeCatch.StateLand, KH_FrisbeeCatch.StateLandCarry, KH_FrisbeeCatch.StateRetrieve,
                KH_FrisbeeCatch.StatePickup, KH_FrisbeeCatch.StateReturn })
                if (KH_FrisbeeCatch.PetAllowedIn(s)) return false;
            return true;
        }

        public struct Facts { public Vector3 mouthOffsetCatch, mouthOffsetStand; public float crouch, rise, recover, clipLength; }

        public static Facts ReadFacts() { return ReadFacts("Jump"); }

        // One clip variant's facts (KH_FrisbeeJumpClipEditor.Variants(): "Jump", "Twist").
        public static Facts ReadFacts(string name)
        {
            string path = KH_FrisbeeJumpClipEditor.FactsPathFor(name);
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
            // Counting three exits is not enough: they must lead back to the three locomotion states by name, or a
            // clip could exit into itself and never hand the dog back (Codex M).
            Func<AnimatorState, bool> exitsToLocomotion = s => s != null && s.transitions.Length == 3
                && new[] { "Run", "Walk", "Idle" }.All(n => s.transitions.Any(t => t.destinationState != null && t.destinationState.name == n));
            Check(exitsToLocomotion(jump), "Jump exits to Run/Walk/Idle (by name)");
            // IMP-DOG-008 v4: the twist clip is the second member of the clip family and gets the same treatment.
            var twistVariant = KH_FrisbeeJumpClipEditor.Variants().FirstOrDefault(v => v.name == "Twist");
            var twistClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(KH_FrisbeeJumpClipEditor.ClipPathFor("Twist"));
            Check(twistClip != null, "twist clip exists " + KH_FrisbeeJumpClipEditor.ClipPathFor("Twist"));
            Check(twistClip != null && Mathf.Abs(twistClip.length - twistVariant.EndAt) < 0.02f, "twist clip length " + (twistClip == null ? "-" : twistClip.length.ToString("F3")) + " == " + twistVariant.EndAt.ToString("F3"));
            Check(ac != null && ac.parameters.Any(p => p.name == "Twist" && p.type == AnimatorControllerParameterType.Bool), "controller has Bool Twist");
            var twistState = ac == null ? null : ac.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Twist");
            Check(twistState != null && twistState.motion == twistClip, "controller Twist state uses the twist clip");
            Check(ac != null && ac.layers[0].stateMachine.anyStateTransitions.Any(t => t.destinationState == twistState && !t.canTransitionToSelf), "AnyState -> Twist (no self transition)");
            Check(exitsToLocomotion(twistState), "Twist exits to Run/Walk/Idle (by name)");
            // The curving preset is a right bend of a sane size: 0 would make v5 the same as v0, and beyond 0.5 1/m
            // a 7.5 m/s throw leaves the plaza sideways.
            Check(KH_FrisbeeCatch.CurveDefault > 0f && KH_FrisbeeCatch.CurveDefault <= 0.5f, "flight curve default " + KH_FrisbeeCatch.CurveDefault.ToString("F2") + " in (0, 0.5] 1/m");
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
                        // Every other member of the clip family gets the same mouth-vs-JSON comparison, on the same
                        // dog (Codex M): a twist clip rebuilt without rewriting its facts must fail here.
                        foreach (var cv in KH_FrisbeeJumpClipEditor.Variants())
                        {
                            if (cv.name == "Jump") continue;   // covered above, with the full pose checks
                            var vClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(cv.ClipPath);
                            Vector3 vMouth = Vector3.zero; bool vOk = false;
                            if (vClip != null)
                            {
                                vClip.SampleAnimation(dog, cv.CatchAt); KH_DogRig.ApplyConstraints(dog);
                                var vRig = KH_DogRig.Resolve(dog, KH_FrisbeeJumpClipEditor.MouthAnchorName);
                                if (vRig.Ok)
                                {
                                    vMouth = vRig.mouthAnchor.position - dog.transform.position;
                                    vOk = Vector3.Distance(vMouth, ReadFacts(cv.name).mouthOffsetCatch) < 0.005f;
                                }
                            }
                            Check(vOk, "facts mouthOffsetCatch matches the " + cv.name + " clip (" + vMouth.ToString("F3") + ")");
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
                Check(catcher != null && disc != null && launchers.Length == 3, "scene has catcher, disc and three launchers");
                if (catcher != null && disc != null)
                {
                    Check(catcher.dogRoot != null && catcher.dogAnimator != null && catcher.mouthAnchor != null && catcher.frisbee == disc, "catcher wiring");
                    Check(catcher.mouthOffsetCatch.sqrMagnitude > 1e-6f && catcher.clipRiseDuration > 0.05f, "catcher pose facts present");
                    var sceneFacts = ReadFacts(); // JSON == proxy == backing, so a rebuilt clip cannot leave stale facts in the scene (review M3)
                    Check(Vector3.Distance(catcher.mouthOffsetCatch, sceneFacts.mouthOffsetCatch) < 1e-4f && Vector3.Distance(catcher.mouthOffsetStand, sceneFacts.mouthOffsetStand) < 1e-4f
                        && Mathf.Abs(catcher.clipCrouchDuration - sceneFacts.crouch) < 1e-4f && Mathf.Abs(catcher.clipRiseDuration - sceneFacts.rise) < 1e-4f && Mathf.Abs(catcher.clipRecoverDuration - sceneFacts.recover) < 1e-4f,
                        "catcher pose facts equal the clip JSON (rebuild the scene after the clip)");
                    var sceneTwist = ReadFacts("Twist");
                    Check(Vector3.Distance(catcher.mouthOffsetCatchTwist, sceneTwist.mouthOffsetCatch) < 1e-4f
                        && Mathf.Abs(catcher.clipCrouchDurationTwist - sceneTwist.crouch) < 1e-4f && Mathf.Abs(catcher.clipRiseDurationTwist - sceneTwist.rise) < 1e-4f && Mathf.Abs(catcher.clipRecoverDurationTwist - sceneTwist.recover) < 1e-4f,
                        "catcher twist pose facts equal the twist clip JSON (mouth " + catcher.mouthOffsetCatchTwist.ToString("F3") + ")");
                    // Both clips play at the same minRootRise, so the floor follows the longer rise time.
                    float longestRise = Mathf.Max(catcher.clipRiseDuration, catcher.clipRiseDurationTwist);
                    Check(catcher.minRootRise >= 0.5f * disc.gravity * Mathf.Pow(longestRise / 1.8f, 2f) - 1e-3f, "minRootRise keeps both clips' playback <= 1.8x (longest rise " + longestRise.ToString("F2") + " s -> rise>=" + (0.5f * disc.gravity * Mathf.Pow(longestRise / 1.8f, 2f)).ToString("F3") + ")");
                    Check(catcher.dogRoot != null && catcher.dogRoot.GetComponent<VRCObjectSync>() != null, "dog root has VRCObjectSync");
                    Check(catcher.dogAnimator != null && catcher.dogAnimator.runtimeAnimatorController == ac, "dog Animator uses AC_KD_Dog_Lab");
                    Check(catcher.dogRoot != null && catcher.dogRoot.GetComponentsInChildren<UdonBehaviour>(true).Length == 0, "dog subtree carries no world-scene behaviours");
                    // IMP-DOG-008 wave 2: without this the visible body trails the root (追従GIMMICK ParentConstraint)
                    // and the authored mouth facts do not hold in flight - the twist catch missed by 1.3 m.
                    Check(catcher.dogRoot != null && KH_FrisbeeJumpClipEditor.IsBodyRigid(catcher.dogRoot.gameObject), "frisbee dog body is rigid to its root (追従 ParentConstraint inactive)");
                    // Codex D2: and nothing else was switched off on the way past.
                    Check(catcher.dogRoot != null && KH_FrisbeeJumpClipEditor.OtherConstraintsUntouched(catcher.dogRoot.gameObject),
                        "every other ParentConstraint under the frisbee dog is still active (the change is scoped to " + KH_FrisbeeJumpClipEditor.BodyFollowPath + ")");
                    Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(catcher).SyncMethod == Networking.SyncType.Manual, "catcher sync=Manual");
                    Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(disc).SyncMethod == Networking.SyncType.None, "disc sync=None (VRCObjectSync carries the transform)");
                    Check(disc.GetComponent<VRCObjectSync>() != null && disc.GetComponent<VRCPickup>() != null && disc.GetComponent<Rigidbody>() != null, "disc has ObjectSync/Pickup/Rigidbody");
                    Check(disc.catcher == catcher, "disc -> catcher");
                    Check(launchers.All(l => l.frisbee == disc && l.muzzle != null && l.catcher == catcher) && launchers.Count(l => l.recallOnly) == 1 && launchers.Count(l => l.cycleVariant) == 1,
                        "launchers wired (one recall, one throw, one variant cycle)");
                    var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(catcher);
                    Check(backing.publicVariables.TryGetVariableValue("dogRoot", out Transform bd) && bd == catcher.dogRoot, "backing heap holds dogRoot");
                    Check(backing.publicVariables.TryGetVariableValue("mouthOffsetCatch", out Vector3 bm) && bm == catcher.mouthOffsetCatch, "backing heap holds mouthOffsetCatch");
                    Check(backing.publicVariables.TryGetVariableValue("clipRiseDuration", out float br) && Mathf.Abs(br - catcher.clipRiseDuration) < 1e-5f, "backing heap holds clipRiseDuration");
                    Check(backing.publicVariables.TryGetVariableValue("mouthOffsetCatchTwist", out Vector3 bmt) && bmt == catcher.mouthOffsetCatchTwist
                        && backing.publicVariables.TryGetVariableValue("clipCrouchDurationTwist", out float bct) && Mathf.Abs(bct - catcher.clipCrouchDurationTwist) < 1e-5f
                        && backing.publicVariables.TryGetVariableValue("clipRiseDurationTwist", out float brt) && Mathf.Abs(brt - catcher.clipRiseDurationTwist) < 1e-5f
                        && backing.publicVariables.TryGetVariableValue("clipRecoverDurationTwist", out float bvt) && Mathf.Abs(bvt - catcher.clipRecoverDurationTwist) < 1e-5f, "backing heap holds the twist pose facts");
                    // IMP-DOG-008: the switch starts on the baseline and the run-in preset fits this dog and this disc.
                    Check(catcher.variant == KH_FrisbeeCatch.VariantNormal && catcher.pendingVariant == -1, "catcher starts on variant Normal with nothing queued");
                    Check(backing.publicVariables.TryGetVariableValue("variant", out int bvar) && bvar == KH_FrisbeeCatch.VariantNormal
                        && backing.publicVariables.TryGetVariableValue("pendingVariant", out int bpend) && bpend == -1, "backing heap holds variant 0 with nothing queued");
                    // IMP-DOG-008 round 2 (intercept-first): the plan promises the dog will be STANDING under the
                    // catch point, settled, before the crouch. Only a sprint can keep that promise.
                    Check(catcher.chaseRunSpeed >= 9.5f, "chase sprint " + catcher.chaseRunSpeed.ToString("F1") + " m/s >= 9.5 (round 5: the ETA the planner promises, and the reach of a jump candidate)");
                    // Round 4: both of these are now bands, not floors. Too low and the plan cannot keep its ETA;
                    // too high and the dog reads as a machine (PCVR 3), which no catch rate can make up for.
                    Check(catcher.chaseAcceleration >= 10f && catcher.chaseAcceleration <= 18f, "chase acceleration " + catcher.chaseAcceleration.ToString("F1") + " m/s2 in [10, 18] (reached, but visibly: " + (catcher.chaseRunSpeed / catcher.chaseAcceleration).ToString("F2") + " s to the run)");
                    Check(catcher.settleTime >= 0.1f && catcher.settleTime <= 0.6f, "settle time " + catcher.settleTime.ToString("F2") + " s in [0.10, 0.60]");
                    Check(catcher.turnSpeedDegPerSec >= 180f && catcher.turnSpeedDegPerSec <= 360f, "turn rate " + catcher.turnSpeedDegPerSec.ToString("F0") + " deg/s in [180, 360] (a dog's turn, not a turret's)");
                    Check(KH_FrisbeeCatch.HookLength >= 1f && KH_FrisbeeCatch.HookLength <= 3f, "hook length " + KH_FrisbeeCatch.HookLength.ToString("F2") + " m in [1, 3] (the straightening run before the spot)");
                    Check(KH_FrisbeeCatch.HookAngleDeg >= 20f && KH_FrisbeeCatch.HookAngleDeg <= 70f, "hook angle " + KH_FrisbeeCatch.HookAngleDeg.ToString("F0") + " deg in [20, 70] (a curve in, not a reverse park)");
                    // Codex M5: the momentum grid, not just the wait hook. These are the loops the planner really
                    // runs (HookLenAt / HookAngAt walk the two ranges), so they are the numbers that decide whether
                    // spare time is spent running or standing.
                    Check(KH_FrisbeeCatch.HookLengthMin >= 0.5f && KH_FrisbeeCatch.HookLengthMin <= KH_FrisbeeCatch.HookLength
                        && KH_FrisbeeCatch.HookLengthMax >= KH_FrisbeeCatch.HookLength && KH_FrisbeeCatch.HookLengthMax <= 6f,
                        "hook loop lengths " + KH_FrisbeeCatch.HookLengthMin.ToString("F2") + " - " + KH_FrisbeeCatch.HookLengthMax.ToString("F2")
                        + " m bracket the wait hook " + KH_FrisbeeCatch.HookLength.ToString("F2") + " and stay within [0.5, 6]");
                    Check(KH_FrisbeeCatch.HookAngleMin >= 10f && KH_FrisbeeCatch.HookAngleMin <= KH_FrisbeeCatch.HookAngleDeg
                        && KH_FrisbeeCatch.HookAngleMax >= KH_FrisbeeCatch.HookAngleDeg && KH_FrisbeeCatch.HookAngleMax <= 120f,
                        "hook loop angles " + KH_FrisbeeCatch.HookAngleMin.ToString("F0") + " - " + KH_FrisbeeCatch.HookAngleMax.ToString("F0")
                        + " deg bracket the wait hook " + KH_FrisbeeCatch.HookAngleDeg.ToString("F0") + " and stay within [10, 120]");
                    Check(KH_FrisbeeCatch.HookCombos >= 2 && KH_FrisbeeCatch.HookCombos <= 16,
                        "hook loop count " + KH_FrisbeeCatch.HookCombos + " in [2, 16] (walked per candidate per replan)");
                    // Codex D1 高: the landing rule, as pure geometry. The prototype scene has no play area
                    // (playAreaRadius stays 0, so DogMayGo is always true there - see the harness notCovered), so
                    // this is the only place the edge case is exercised at all. Both halves are the SAME code the
                    // planner and the liftoff gate use: LandingOf for the arc, InCircleXZ for the circle.
                    {
                        var c0 = new Vector3(3f, 0f, -7f); float rad = 10f;   // an off-origin circle, so a sign error shows
                        var fwd = new Vector3(0.6f, 0f, 0.8f);                // normalised, and not axis-aligned
                        float vTake = 2f, tUp = 0.25f;                        // the jump travels vTake * 2 * tUp = 1.00 m
                        var liftOut = c0 + fwd * (rad - 0.7f);                // 0.70 m inside the ring
                        var landOut = KH_FrisbeeCatch.LandingOf(liftOut, fwd, vTake, tUp);
                        var liftIn = c0 + fwd * (rad - 1.7f);                 // the same jump one metre further in
                        var landIn = KH_FrisbeeCatch.LandingOf(liftIn, fwd, vTake, tUp);
                        float outBy = (landOut - c0).magnitude - rad, inBy = rad - (landIn - c0).magnitude;
                        Check(Mathf.Abs(outBy - 0.3f) < 1e-3f && Mathf.Abs(inBy - 0.7f) < 1e-3f,
                            "LandingOf puts the take-off + " + (vTake * 2f * tUp).ToString("F2") + " m arc 0.30 m outside / 0.70 m inside the ring (measured "
                            + outBy.ToString("F3") + " / " + inBy.ToString("F3") + ")");
                        Check(KH_FrisbeeCatch.InCircleXZ(liftOut, c0, rad, 0f) && !KH_FrisbeeCatch.InCircleXZ(landOut, c0, rad, 0f)
                            && KH_FrisbeeCatch.InCircleXZ(landIn, c0, rad, 0f),
                            "the play-area predicate rejects a landing 0.30 m outside the ring even though the take-off is inside, and accepts the one 0.70 m in (Codex D1)");
                    }
                    Check(catcher.chaseDeceleration > 0f && catcher.chaseDeceleration < catcher.deceleration, "chase braking " + catcher.chaseDeceleration.ToString("F1") + " m/s2 < the generic " + catcher.deceleration.ToString("F1") + " (the chase rolls out, it does not stop dead)");
                    Check(catcher.standTurnDegPerSec <= catcher.walkTurnDegPerSec && catcher.walkTurnDegPerSec <= catcher.turnSpeedDegPerSec && catcher.standTurnDegPerSec > catcher.idleTurnSpeedDegPerSec,
                        "turn rates stand " + catcher.standTurnDegPerSec.ToString("F0") + " <= walk " + catcher.walkTurnDegPerSec.ToString("F0") + " <= run " + catcher.turnSpeedDegPerSec.ToString("F0") + ", all above idle " + catcher.idleTurnSpeedDegPerSec.ToString("F0"));
                    Check(catcher.maxRunAnimScale >= 1.5f && catcher.maxRunAnimScale <= 3f, "run clip speed cap " + catcher.maxRunAnimScale.ToString("F1") + "x in [1.5, 3.0] (higher reads as jitter)");
                    Check(KH_FrisbeeCatch.HoldEnterRadius >= 0.2f && KH_FrisbeeCatch.HoldEnterRadius <= 0.6f, "hold band " + KH_FrisbeeCatch.HoldEnterRadius.ToString("F2") + " m in [0.20, 0.60] (must sit outside the 0.1 m arrive radius)");
                    Check(catcher.returnRunSpeed <= catcher.chaseRunSpeed, "return pace " + catcher.returnRunSpeed.ToString("F1") + " <= chase sprint " + catcher.chaseRunSpeed.ToString("F1") + " (only the chase has a deadline)");
                    // The highest jump the planner may pick still has to play the clip at a believable speed.
                    // Codex M5: the SLOWEST playback is the shortest clip over the longest air time, so the floor
                    // has to be read off the shorter rise. Taking the longer one checked the easier clip and let
                    // the other fall below 0.60x unnoticed.
                    float shortestRiseDur = Mathf.Min(catcher.clipRiseDuration, catcher.clipRiseDurationTwist);
                    float topTUp = Mathf.Sqrt(2f * catcher.maxRootRise / disc.gravity);
                    float topScale = shortestRiseDur / topTUp;
                    Check(topScale >= 0.6f, "maxRootRise " + catcher.maxRootRise.ToString("F2") + " m plays the SLOWEST clip at " + topScale.ToString("F2")
                        + "x >= 0.60 (shortest rise " + shortestRiseDur.ToString("F2") + " s; jump " + (catcher.clipRiseDuration / topTUp).ToString("F2")
                        + "x, twist " + (catcher.clipRiseDurationTwist / topTUp).ToString("F2") + "x)");
                    Check(KH_FrisbeeCatch.RunInReadyTurnDeg >= 20f && KH_FrisbeeCatch.RunInReadyTurnDeg <= 90f, "Ready turn threshold " + KH_FrisbeeCatch.RunInReadyTurnDeg.ToString("F0") + " deg in [20, 90]");
                    // IMP-DOG-013: the よしよし window. Too short and the dog still bolts; too long and it stops
                    // reading as a dog waiting for the next throw. The distance must be arm's reach, not a room.
                    Check(catcher.readyGraceSeconds >= 1f && catcher.readyGraceSeconds <= 6f,
                        "ready grace " + catcher.readyGraceSeconds.ToString("F1") + " s in [1, 6] (round 5: 2 s - long enough for a よしよし, short enough to still want the throw)");
                    Check(catcher.readyGraceDistance >= 1f && catcher.readyGraceDistance <= 5f,
                        "ready grace distance " + catcher.readyGraceDistance.ToString("F1") + " m in [1, 5] (arm's reach, not the whole plaza)");
                    Check(backing.publicVariables.TryGetVariableValue("readyGraceSeconds", out float bgs) && Mathf.Abs(bgs - catcher.readyGraceSeconds) < 1e-5f
                        && backing.publicVariables.TryGetVariableValue("readyGraceDistance", out float bgd) && Mathf.Abs(bgd - catcher.readyGraceDistance) < 1e-5f,
                        "backing heap holds the ready grace pair");
                    // BUG-DOG-003: the window a launch notice may wait for the pickup sync to catch up, and the
                    // harness-only override that drives that race. The constant is a const, so it never reaches the
                    // backing heap and is checked statically; the override is checked in the proxy AND on the heap,
                    // where a missing symbol or a different type FAILS rather than reading as zero.
                    Check(KH_FrisbeeCatch.PendingHoldSeconds >= 0.5f && KH_FrisbeeCatch.PendingHoldSeconds <= 2f,
                        "pending launch hold " + KH_FrisbeeCatch.PendingHoldSeconds.ToString("F2") + " s in [0.5, 2] (long enough for the arrival-order race, short enough to still catch)");
                    Check(catcher.debugHeldOverride == 0 && backing.publicVariables.TryGetVariableValue("debugHeldOverride", out int bheld) && bheld == 0,
                        "held override off (harness only), proxy and backing heap");
                    // The pet-resume rule as the runtime reads it: petting is allowed exactly where the dog stands
                    // still without moving next - Idle, Ready and the Drop wait with the disc in its mouth - and
                    // nowhere it runs or jumps. Static, so the rule cannot rot unnoticed between Play sessions.
                    Check(PetStatesOk(), "petting resumes in Idle / Ready / Drop and stays suppressed in every moving state (PetAllowed)");
                    // Round 5 (PCVR 2026-09-22 "もっと遠くから跳んでいい"): the take-off band sets how far out the
                    // dog leaves the ground. Head-on stays well under the general ceiling because the closing rate
                    // there is the sum of both speeds.
                    Check(KH_FrisbeeCatch.RunJumpMaxTakeoff >= 5f && KH_FrisbeeCatch.RunJumpMaxTakeoff <= 9f
                        && KH_FrisbeeCatch.RunJumpMinTakeoff < KH_FrisbeeCatch.RunJumpHeadOnMaxTakeoff
                        && KH_FrisbeeCatch.RunJumpHeadOnMaxTakeoff < KH_FrisbeeCatch.RunJumpMaxTakeoff,
                        "take-off band " + KH_FrisbeeCatch.RunJumpMinTakeoff.ToString("F1") + " / head-on " + KH_FrisbeeCatch.RunJumpHeadOnMaxTakeoff.ToString("F1")
                        + " / " + KH_FrisbeeCatch.RunJumpMaxTakeoff.ToString("F1") + " m/s is ordered, ceiling in [5, 9]");
                    Check(KH_FrisbeeCatch.RunJumpMaxLoiter > 0f && KH_FrisbeeCatch.RunJumpMaxLoiter <= 0.6f,
                        "a running jump may arrive at most " + KH_FrisbeeCatch.RunJumpMaxLoiter.ToString("F2") + " s early (in (0, 0.60]); beyond that it would stand on the take-off point");
                    Check(KH_FrisbeeCatch.StandStraightSlack >= 0.1f && KH_FrisbeeCatch.StandStraightSlack <= 1f,
                        "thin stand catches run straight below " + KH_FrisbeeCatch.StandStraightSlack.ToString("F2") + " s of slack (in [0.10, 1.00])");
                    // The overtake is every preset's now, so its pace floor has to be a real chase-down for all of
                    // them: below the walking-world run it would start taking throws a jog could reach.
                    Check(KH_FrisbeeCatch.OvertakeMinPace > catcher.runSpeed && KH_FrisbeeCatch.OvertakeMinPace <= catcher.chaseRunSpeed,
                        "overtake pace floor " + KH_FrisbeeCatch.OvertakeMinPace.ToString("F1") + " m/s is above the walking run "
                        + catcher.runSpeed.ToString("F1") + " and within the sprint " + catcher.chaseRunSpeed.ToString("F1"));
                    var desc = open.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<VRCSceneDescriptor>(true)).FirstOrDefault();
                    Check(desc != null && desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null, "scene descriptor with spawn");
                    Check(desc != null && desc.NetworkIDCollection.All(x => x.gameObject != null), "no dangling NetworkIDs");
                    // Closed-form flight against a small-step integration of the same model (the honest check that
                    // prediction == flight is by construction: KH_Frisbee.Update steps _PositionAt with the same facts).
                    // The curving preset (v5) is part of the same model, so the integrator carries the sideways
                    // acceleration too and the check runs straight, right-curving and left-curving throws.
                    float maxErr = 0f; float maxDrift = 0f;
                    var p0 = new Vector3(0f, 1.3f, 0f);
                    foreach (float curve in new[] { 0f, KH_FrisbeeCatch.CurveDefault, -KH_FrisbeeCatch.CurveDefault })
                    foreach (var v0 in new[] { new Vector3(0f, 1.2f, 8f), new Vector3(3f, 0.4f, 10.5f), new Vector3(0f, 2.5f, 4f) })
                    {
                        Vector3 p = p0, v = v0; float dt = 0.0002f;
                        float vh2 = Mathf.Min(v0.x * v0.x + v0.z * v0.z, disc.liftCap * disc.gravity / disc.lift);
                        Vector3 flat = new Vector3(v0.x, 0f, v0.z);
                        Vector3 side = flat.magnitude < 1e-4f ? Vector3.zero : Vector3.Cross(Vector3.up, flat.normalized);
                        for (int i = 1; i <= 15000; i++)
                        {
                            float t = i * dt;
                            float decay = Mathf.Exp(-2f * disc.drag * t);
                            var a = new Vector3(-disc.drag * v.x, -disc.gravity + disc.lift * vh2 * decay, -disc.drag * v.z) + side * (curve * vh2 * decay);
                            v += a * dt; p += v * dt;
                            if (i % 1000 == 0) { float err = Vector3.Distance(p, disc._PositionAt(p0, v0, curve, t)); if (err > maxErr) maxErr = err; }
                        }
                        float drift = Mathf.Abs(Vector3.Dot(disc._PositionAt(p0, v0, curve, 3f) - disc._PositionAt(p0, v0, 0f, 3f), side));
                        if (drift > maxDrift) maxDrift = drift;
                    }
                    Check(maxErr < 0.01f, "closed-form flight matches integrated model for curve 0 / +-" + KH_FrisbeeCatch.CurveDefault.ToString("F2") + " (max err=" + maxErr.ToString("F4") + " m over 3 s)");
                    log.AppendLine("INFO sideways drift at 3 s with curve " + KH_FrisbeeCatch.CurveDefault.ToString("F2") + ": up to " + maxDrift.ToString("F2") + " m");
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
