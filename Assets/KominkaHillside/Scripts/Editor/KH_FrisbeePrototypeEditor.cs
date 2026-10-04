using System;
using System.Collections.Generic;
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
    // walk/fetch/pet systems), one disc, a fixed-throw stand and a recall button. IMP-DOG-012: one such lane per
    // breed (LaneBreeds), the Lab lane unchanged and the others shifted along x (LaneOffsetX). Everything here is
    // code-owned and rebuilt on every Build; nothing in the hillside world scene is touched.
    public static class KH_FrisbeePrototypeEditor
    {
        public const string ScenePath = "Assets/Scenes/KH_FrisbeePrototype.unity";
        // Shown near the aim marker when the holder stands or aims where the dog cannot go (font glyphs via Menu/2).
        // DES-DOG-005 C-4 (was "広場の中で遊ぼう"): Course/92 writes the same text to the six discs of the world scene.
        public const string AimHint = "犬はここへ行けません";
        // BUG-DOG-007 (決定 2026-09-29): the dog is beyond KH_FrisbeeCatch.readyMaxDistance. Course/92 writes it to KH_Frisbee.aimFarHint;
        // Course/27 adds its glyphs. The prototype keeps readyMaxDistance 0, so it never shows there.
        public const string AimFarHint = "犬が遠くにいます";
        const string Data = "Assets/KominkaHillside/Data/Frisbee";
        static readonly string[] Programs = { "KH_Frisbee", "KH_FrisbeeCatch", "KH_FrisbeeLauncher" };
        const int DogLayer = 14, PickupLayer = 13;

        // IMP-DOG-012: one lane per breed, side by side along x. The Lab lane is the original prototype, unmoved and
        // with its original object names (KH_Frisbee / KH_FrisbeeStand / KH_FrisbeeRecall / KH_FrisbeeVariant / Wall),
        // so earlier harness records stay comparable; every other lane is the same layout shifted by LaneSpacing and
        // its objects carry the breed as a suffix. The ClientSim harness (--breed / --mode=sixdogs) finds them by
        // these names. 12 m keeps a side throw (about 6 m of drift) and the dog chasing it out of the next lane.
        public static readonly string[] LaneBreeds = KH_FrisbeeJumpClipEditor.Breeds;
        public const float LaneSpacing = 12f;
        public static float LaneOffsetX(string breed)
        {
            int i = Array.IndexOf(LaneBreeds, breed), lab = Array.IndexOf(LaneBreeds, KH_FrisbeeJumpClipEditor.Breed);
            if (i < 0) throw new InvalidOperationException("No prototype lane for breed " + breed);
            return (i - lab) * LaneSpacing;
        }
        public static string LaneName(string baseName, string breed) { return breed == KH_FrisbeeJumpClipEditor.Breed ? baseName : baseName + "_" + breed; }

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
            if (m == null) { m = new Material(Shader.Find("VRChat/Mobile/Standard Lite")); m.color = color; KH_MatteMaterialsEditor.Matte(m); AssetDatabase.CreateAsset(m, path); }
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

        // 2026-09-28 (M4): opening the prototype additively let UdonSharp's scene hooks rewrite the world scene's backings (removed
        // fields) and left it dirty. Course/92 now purges those, and Frisbee/4 / 5 say so if another loaded scene turns dirty.
        static List<Scene> CleanOtherScenes()
        {
            var list = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isLoaded && !s.isDirty && s.path != ScenePath) list.Add(s); }
            return list;
        }
        static void WarnDirtied(List<Scene> clean, string menu)
        {
            foreach (var s in clean)
                if (s.IsValid() && s.isLoaded && s.isDirty)
                    Debug.LogWarning("[KH Frisbee] " + menu + " left " + s.path + " dirty (not saved by this menu). Reopen it from disk; if it repeats, run Course/92 there (it purges the backing variables M4 removed) and save.");
        }

        [MenuItem("Tools/KominkaHillside/Frisbee/4 Build Scene")]
        public static void BuildScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var clean = CleanOtherScenes();
            var previous = OpenPrototype(true);
            var scene = SceneManager.GetActiveScene();
            try { BuildInto(scene); }
            finally { ClosePrototype(previous, scene); WarnDirtied(clean, "Frisbee/4"); }
        }

        static void BuildInto(Scene scene)
        {
            // IMP-DOG-012: one lane per breed (LaneBreeds). Every breed's facts are read BEFORE anything is built, so
            // a breed without its Frisbee/2 output stops the build instead of leaving the scene half-made.
            Facts[] laneFacts, laneTwist;
            ReadAllFacts(LaneBreeds, out laneFacts, out laneTwist);   // Codex M1: typed + validated, all breeds, before any change
            var facts = laneFacts[Array.IndexOf(LaneBreeds, KH_FrisbeeJumpClipEditor.Breed)];

            // World: descriptor, spawn, flat ground, one wall per lane for the "hits a wall" case.
            var world = Obj("KH_FrisbeeWorld");
            var desc = world.GetComponent<VRCSceneDescriptor>(); if (desc == null) desc = world.AddComponent<VRCSceneDescriptor>();
            var spawn = Obj("Spawn", world.transform).transform; spawn.position = new Vector3(0f, 0f, -6f); spawn.rotation = Quaternion.identity;
            desc.spawns = new[] { spawn }; desc.RespawnHeightY = -20f; desc.spawnOrder = VRC_SceneDescriptor.SpawnOrder.First;
            // IMP-DOG-012: the plane grows from 80 m to 120 m square so the outer lanes (x -24 .. +36, side throws drift
            // about 6 m further) still have ground under them. Same height (y = 0) and centre, so the Lab lane is unchanged.
            // DES-DOG-005 M1: 120 -> 140 m so the ten Terrain pads (x +-58.5, slabs +-7 m) stay on it. Same height and centre.
            var ground = Prim("Ground", PrimitiveType.Plane, world.transform); ground.transform.localScale = Vector3.one * 14f; ground.isStatic = true;
            ground.GetComponent<Renderer>().sharedMaterial = Mat("KH_FrisbeeGround", new Color(.45f, .58f, .32f));

            var dogs = Obj("KH_FrisbeeDogs");
            var props = Obj("KH_FrisbeeProps");
            for (int li = 0; li < LaneBreeds.Length; li++)
            {
                string breed = LaneBreeds[li];
                bool lab = breed == KH_FrisbeeJumpClipEditor.Breed;
                Vector3 lane = new Vector3(LaneOffsetX(breed), 0f, 0f);
                var wall = Prim(LaneName("Wall", breed), PrimitiveType.Cube, world.transform); wall.transform.position = lane + new Vector3(0f, 1.25f, 22f); wall.transform.localScale = new Vector3(16f, 2.5f, .3f); wall.isStatic = true;
                wall.GetComponent<Renderer>().sharedMaterial = Mat("KH_FrisbeeWall", new Color(.6f, .55f, .5f));
                int colour = Array.IndexOf(KH_DogWalkingEditor.FrisbeeKeys, breed);
                BuildLane(scene, dogs, props, breed, "KD_Dog_" + breed, "KH_FrisbeeCatch_" + breed,
                    lab ? "KH_Frisbee" : LaneName("KH_Frisbee", breed), lab ? "KH_FrisbeeDisc" : "KH_FrisbeeDisc_" + breed,
                    lab ? new Color(1f, .5f, .15f) : KH_DogWalkingEditor.DiscColors[colour < 0 ? 0 : colour],
                    LaneName("KH_FrisbeeStand", breed), LaneName("KH_FrisbeeRecall", breed), LaneName("KH_FrisbeeVariant", breed),
                    lane, lane + new Vector3(1.5f, 0f, 6.5f), laneFacts[li], laneTwist[li]);
            }
            // DES-DOG-005 M1: the Terrain lane (harness --mode=terrain). A second Lab (its own dog, catcher, disc, stand,
            // recall and variant cube, all suffixed _Terrain) parked south of the pads; the harness moves the stand, the
            // dog and the player onto one pad per case. Not in LaneBreeds, so the breed lanes and sixdogs are untouched.
            int labLane = Array.IndexOf(LaneBreeds, TerrainBreed);
            BuildTerrainPads(world.transform);
            BuildLane(scene, dogs, props, TerrainBreed, "KD_Dog_" + TerrainTag, "KH_FrisbeeCatch_" + TerrainTag,
                "KH_Frisbee_" + TerrainTag, "KH_FrisbeeDisc_" + TerrainBreed, KH_DogWalkingEditor.DiscColors[Math.Max(0, Array.IndexOf(KH_DogWalkingEditor.FrisbeeKeys, TerrainBreed))],
                "KH_FrisbeeStand_" + TerrainTag, "KH_FrisbeeRecall_" + TerrainTag, "KH_FrisbeeVariant_" + TerrainTag,
                TerrainParkOrigin, TerrainParkOrigin + new Vector3(1.5f, 0f, 6.5f), laneFacts[labLane], laneTwist[labLane]);

            foreach (var u in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<UdonSharpBehaviour>(true))) { UdonSharpEditorUtility.CopyProxyToUdon(u); EditorUtility.SetDirty(u); }
            KH_CourseFrisbeeAnywhereEditor.PurgeRemovedM4Vars(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<UdonSharpBehaviour>(true)), false, null);   // M4 removed fields
            int dropped = desc.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0));
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Report("build-scene", "scene=" + ScenePath + " lanes=" + string.Join(",", LaneBreeds) + " + " + TerrainTag + " (" + TerrainCases.Length + " pads) (Lab mouthOffsetCatch=" + facts.mouthOffsetCatch.ToString("F3") + ") droppedNetworkIds=" + dropped + "; run 5 Verify.");
        }

        // One lane: the dog (a clone of the port-source dog of cloneBreed, without its world behaviours), the stand /
        // recall / variant cubes, the disc and the catcher. lane = the lane origin (stand at lane + (0, .5, -4)).
        static void BuildLane(Scene scene, GameObject dogs, GameObject props, string cloneBreed, string dogName, string catcherName,
            string discName, string discMaterial, Color discColor, string standName, string recallName, string cyclerName,
            Vector3 lane, Vector3 dogAt, Facts laneFact, Facts laneTwistFact)
        {
            {
                // Dog: copy of the port-source dog of this breed, without walk/fetch/pet behaviours (they belong to the world scene).
                var dogT = dogs.transform.Find(dogName);
                if (dogT == null)
                {
                    var dog = KH_FrisbeeJumpClipEditor.CloneSourceDog(scene, cloneBreed);
                    dog.name = dogName;
                    dog.transform.SetParent(dogs.transform, true); dogT = dog.transform;
                    foreach (var u in dog.GetComponentsInChildren<UdonSharpBehaviour>(true)) UnityEngine.Object.DestroyImmediate(u);
                    foreach (var u in dog.GetComponentsInChildren<UdonBehaviour>(true)) UnityEngine.Object.DestroyImmediate(u);
                    foreach (var t in dog.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                }
                dogT.SetPositionAndRotation(dogAt, Quaternion.Euler(0f, 180f, 0f)); // beside where the fixed throw comes down (z 6-8), watching the stand (user, 2026-09-21)
                foreach (var t in dogT.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = DogLayer;
                if (dogT.GetComponent<VRCObjectSync>() == null) dogT.gameObject.AddComponent<VRCObjectSync>();
                var animator = dogT.GetComponentInChildren<Animator>(true);
                if (animator == null) throw new InvalidOperationException("Dog " + dogName + " has no Animator");
                var mouth = dogT.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == KH_FrisbeeJumpClipEditor.MouthAnchorName);
                if (mouth == null) throw new InvalidOperationException("Dog " + dogName + " has no " + KH_FrisbeeJumpClipEditor.MouthAnchorName);

                // Stand (fixed throw) and recall button.
                var stand = Prim(standName, PrimitiveType.Cube, props.transform);
                stand.transform.position = lane + new Vector3(0f, .5f, -4f); stand.transform.localScale = new Vector3(.4f, 1f, .4f);
                stand.GetComponent<Renderer>().sharedMaterial = Mat("KH_FrisbeeStand", new Color(.35f, .25f, .18f));
                var muzzle = Obj("Muzzle", stand.transform).transform; muzzle.localPosition = new Vector3(0f, .75f, 0f); muzzle.localRotation = Quaternion.identity; muzzle.localScale = new Vector3(2.5f, 1f, 2.5f);
                var recall = Prim(recallName, PrimitiveType.Cube, props.transform);
                recall.transform.position = lane + new Vector3(-.8f, .35f, -4f); recall.transform.localScale = new Vector3(.3f, .7f, .3f);
                recall.GetComponent<Renderer>().sharedMaterial = Mat("KH_FrisbeeRecall", new Color(.25f, .35f, .5f));

                // Disc + catcher: the same helpers place the discs in the world scene (KH_DogWalkingEditor). The Lab lane
                // keeps its orange KH_FrisbeeDisc; the other lanes wear the world scene's colour for that dog.
                bool discCreated;
                var disc = BuildDisc(props.transform, discName, discMaterial, discColor, out discCreated);
                disc.transform.position = muzzle.position; disc.transform.rotation = Quaternion.identity; // prototype: always on the stand
                var catcher = Add<KH_FrisbeeCatch>(Obj(catcherName, dogs.transform), true);
                ConfigureCatcher(catcher, disc, dogT, animator, mouth, laneFact, laneTwistFact);
                // IMP-DOG-008 wave 2: the purchased body hangs off the 追従GIMMICK ParentConstraint and trails the root
                // by up to 30 deg / 0.5 m while the dog turns and jumps, which put the real mouth 1.3 m from where the
                // plan (root + rotation * mouthOffsetCatch) put it. Idempotent, so a rebuild on an existing dog is fine.
                KH_FrisbeeJumpClipEditor.MakeBodyRigid(dogT.gameObject);
                var launcher = Add<KH_FrisbeeLauncher>(stand); launcher.frisbee = disc; launcher.catcher = catcher; launcher.muzzle = muzzle; launcher.recallOnly = false;
                UdonSharpEditorUtility.GetBackingUdonBehaviour(launcher).InteractionText = "Throw (fixed)";
                var recaller = Add<KH_FrisbeeLauncher>(recall); recaller.frisbee = disc; recaller.catcher = catcher; recaller.muzzle = muzzle; recaller.recallOnly = true;
                UdonSharpEditorUtility.GetBackingUdonBehaviour(recaller).InteractionText = "Recall disc";
                // IMP-DOG-008: the prototype scene has no hand menu, so a third cube cycles the variant.
                var cycle = Prim(cyclerName, PrimitiveType.Cube, props.transform);
                cycle.transform.position = lane + new Vector3(-1.6f, .35f, -4f); cycle.transform.localScale = new Vector3(.3f, .7f, .3f);
                cycle.GetComponent<Renderer>().sharedMaterial = Mat("KH_FrisbeeVariant", new Color(.45f, .35f, .22f));
                var cycler = Add<KH_FrisbeeLauncher>(cycle); cycler.frisbee = disc; cycler.catcher = catcher; cycler.muzzle = muzzle; cycler.recallOnly = false; cycler.cycleVariant = true;
                UdonSharpEditorUtility.GetBackingUdonBehaviour(cycler).InteractionText = "Next variant";
            }
        }

        // ---------------------------------------------------------------- DES-DOG-005 M1 Terrain lane
        // Ten pads side by side along x at z = TerrainZ (the breed lanes sit at z -6 .. +22, so nothing overlaps; the
        // 140 m plane reaches x +-70). Pad origin O = TerrainPad(case); every pad's geometry is static, layer 0 (in both the
        // ground and the obstacle masks), rebuilt on every Build. The harness (--mode=terrain) places the stand at
        // O + TerrainStandLocal, the dog at O + TerrainDogLocal (heights from a ground ray) and throws +Z:
        //   T-slope10 / T-slope20  a slab tilted 10 / 20 deg about z (rising to +x, x in O +-7, z in O -10 .. +20)
        //   T-up10 / T-down10      a 10 deg slab along the throw (rising / falling toward +z, same extent; Codex M1 1-2)
        //   T-down04 / T-down08    a 0.4 / 0.8 m platform under the stand and the dog, its edge at z = O + 4.4 / 7.5 (lower beyond)
        //   T-up04                 a 0.4 m step from z = O + 4.4 on; the stand on a 0.4 m pedestal (x O +-3, z O -8 .. -2),
        //                          the dog on the plane
        //   T-cliff                a 5 m box (x O +-6, z O -14 .. -1): the stand and the dog on top, the disc flies off it
        //   T-boxtop               a 2.5 m box (x O +-6, z O +3 .. +20) the disc lands ON, thrown from a 2 m pedestal
        //                          (x O +-3, z O -8 .. -2); the dog on the plane in front of the box (Codex M1 1-3)
        //   T-rock                 a 0.45 m high, 0.2 m thick rock band at z = O + 1.0 .. 1.2 (x O +-5), the dog 0.5 m before it
        public const string TerrainTag = "Terrain", TerrainBreed = "Lab";
        public const float TerrainZ = -45f, TerrainPadSpacing = 13f;
        public static readonly string[] TerrainCases = { "T-slope10", "T-up10", "T-down10", "T-slope20", "T-down04", "T-up04", "T-down08", "T-cliff", "T-boxtop", "T-rock" };
        // Where the terrain dog and stand wait between runs (plane, south of the pads).
        public static readonly Vector3 TerrainParkOrigin = new Vector3(0f, 0f, TerrainZ - 16f);
        public static Vector3 TerrainPad(string name)
        {
            int i = Array.IndexOf(TerrainCases, name);
            if (i < 0) throw new InvalidOperationException("No terrain pad " + name);
            return new Vector3((i - (TerrainCases.Length - 1) * 0.5f) * TerrainPadSpacing, 0f, TerrainZ);
        }
        public static Vector3 TerrainStandLocal(string name) { return new Vector3(0f, 0f, -4f); }
        public static Vector3 TerrainDogLocal(string name)
        {
            if (name == "T-cliff") return new Vector3(1.5f, 0f, -8f);   // behind the stand on the box: no catch before the edge
            if (name == "T-rock") return new Vector3(1.5f, 0f, 0.5f);   // 0.8 m in front of the rock band: every run-up crosses it
            // T-down04 / T-up04: the v3 chase spot (4 m ahead of the stand, 2.5 m aside, facing +Z; the harness sets v3 and
            // turns the dog): B takes off ~2 m before and lands ~2 m after the catch point, which is near the step's edge.
            if (name == "T-down04" || name == "T-up04") return new Vector3(KH_FrisbeeCatch.ReadyLateral3, 0f, -4f + KH_FrisbeeCatch.ReadyAhead3);
            if (name == "T-boxtop") return new Vector3(1.5f, 0f, 0f);   // on the plane, in front of the box the disc lands on
            return new Vector3(1.5f, 0f, 6.5f);                          // the breed lanes' spot
        }
        // The rock is 0.45 m high and 0.2 m thick: above the lane ray (0.3 m) so a run-up across it is refused, below
        // runMaxStep so PathOk / the walk still let the dog fetch across it (the case the design wants: why=lane, and a stop
        // on the way would say reason=ground). It sits 0.5 m in front of the dog (z 1.0 .. 1.2), closer than any crouch start
        // (the B / C run-ups measured 1.5 - 2.7 m): in runs 20260928-145752 (z 3) and -154904 (z 1.3 .. 2.1) the crouch start
        // fell ON the rock, its measured ground was the rock top and the (correct, slope-following) lane went over it.
        // TerrainCrossEdgeZ (T-down04 / T-up04): the B chase from the v3 spot took off at z +2.6 and landed at z +6.1 over
        // level ground in run 20260928-154904 (catch point ~ +4.4), so the step's edge is put at +4.4, between the two.
        public const float TerrainStepEdgeZ = 7.5f, TerrainCrossEdgeZ = 4.4f, TerrainCliffHeight = 5f, TerrainRockZ = 1.1f, TerrainRockHeight = 0.45f, TerrainRockDepth = 0.2f, TerrainUpPedestal = 0.4f,
            TerrainBoxTop = 2.5f, TerrainPedestal = 2f;

        static void BuildTerrainPads(Transform world)
        {
            var root = Obj("KH_FrisbeeTerrain", world).transform;
            var mat = Mat("KH_FrisbeeTerrain", new Color(.52f, .47f, .36f));
            var rockMat = Mat("KH_FrisbeeRock", new Color(.42f, .42f, .44f));
            foreach (string c in TerrainCases)
            {
                var pad = Obj(c, root).transform; pad.position = TerrainPad(c); pad.rotation = Quaternion.identity; pad.localScale = Vector3.one;
                Vector3 o = pad.position;
                if (c == "T-boxtop")
                {
                    // Two parts: the pedestal the stand (and the player) stand on, and the box the disc lands on.
                    var ped = Prim("Pedestal", PrimitiveType.Cube, pad);
                    ped.transform.SetPositionAndRotation(o + new Vector3(0f, TerrainPedestal * 0.5f, -5f), Quaternion.identity); ped.transform.localScale = new Vector3(6f, TerrainPedestal, 6f);
                    ped.layer = 0; ped.isStatic = true; ped.GetComponent<Renderer>().sharedMaterial = mat;
                    var box = Prim("Box", PrimitiveType.Cube, pad);
                    box.transform.SetPositionAndRotation(o + new Vector3(0f, TerrainBoxTop * 0.5f, 11.5f), Quaternion.identity); box.transform.localScale = new Vector3(12f, TerrainBoxTop, 17f);
                    box.layer = 0; box.isStatic = true; box.GetComponent<Renderer>().sharedMaterial = mat;
                    continue;
                }
                string part; Vector3 centre, scale; Quaternion rot = Quaternion.identity; Material m = mat;
                switch (c)
                {
                    case "T-slope10":
                    case "T-slope20":
                    {
                        float th = c == "T-slope10" ? 10f : 20f, rad = th * Mathf.Deg2Rad;
                        float h0 = 7f * Mathf.Tan(rad) + 0.05f;   // surface height at x = O.x (0.05 m above the plane at x = O.x - 7)
                        part = "Slab"; rot = Quaternion.Euler(0f, 0f, th);
                        scale = new Vector3(14f / Mathf.Cos(rad), 0.5f, 30f);
                        centre = o + new Vector3(0f, h0 - 0.25f / Mathf.Cos(rad), 5f);
                        break;
                    }
                    case "T-up10":
                    case "T-down10":
                    {
                        // Along the throw: rising (up) / falling (down) toward +z, 0.05 m above the plane at its low end.
                        float rad = 10f * Mathf.Deg2Rad;
                        float hMid = 0.05f + 15f * Mathf.Tan(rad);   // surface height at z = O + 5 (the slab's middle)
                        part = "Slab"; rot = Quaternion.Euler(c == "T-up10" ? -10f : 10f, 0f, 0f);
                        scale = new Vector3(14f, 0.5f, 30f / Mathf.Cos(rad));
                        centre = o + new Vector3(0f, hMid - 0.25f / Mathf.Cos(rad), 5f);
                        break;
                    }
                    case "T-down04":
                    case "T-down08":
                    {
                        float top = c == "T-down04" ? 0.4f : 0.8f;
                        float edge = c == "T-down04" ? TerrainCrossEdgeZ : TerrainStepEdgeZ;
                        part = "Platform"; scale = new Vector3(12f, top, edge + 10f);
                        centre = o + new Vector3(0f, top * 0.5f, (edge - 10f) * 0.5f);
                        break;
                    }
                    case "T-up04":
                        part = "Step"; scale = new Vector3(12f, 0.4f, 20f - TerrainCrossEdgeZ);
                        centre = o + new Vector3(0f, 0.2f, (TerrainCrossEdgeZ + 20f) * 0.5f);
                        break;
                    case "T-cliff":
                        part = "Cliff"; scale = new Vector3(12f, TerrainCliffHeight, 13f);
                        centre = o + new Vector3(0f, TerrainCliffHeight * 0.5f, -7.5f);
                        break;
                    default:   // T-rock
                        part = "Rock"; scale = new Vector3(10f, TerrainRockHeight, TerrainRockDepth); m = rockMat;
                        centre = o + new Vector3(0f, TerrainRockHeight * 0.5f, TerrainRockZ);
                        break;
                }
                var go = Prim(part, PrimitiveType.Cube, pad);
                go.transform.SetPositionAndRotation(centre, rot); go.transform.localScale = scale;
                go.layer = 0; go.isStatic = true;
                go.GetComponent<Renderer>().sharedMaterial = m;
                if (c == "T-up04")
                {
                    // Run 20260928-160613: from the plane the chase's rise was ~0.3 m, and a landing 0.4 m up needs the apex
                    // to clear it by JumpApexClearance (rise >= 0.5), so every crossing candidate was refused (B:landing)
                    // and the dog fetched from the ground. The rule is right; the throw was too low. The stand gets a
                    // 0.4 m pedestal (T-down04 mirrored: the same absolute flight, whose catch there read rise 0.29 over
                    // 0.4 ground = ~0.69 over the plane here), so the crossing jump clears the step.
                    var ped = Prim("Pedestal", PrimitiveType.Cube, pad);
                    ped.transform.SetPositionAndRotation(o + new Vector3(0f, TerrainUpPedestal * 0.5f, -5f), Quaternion.identity); ped.transform.localScale = new Vector3(6f, TerrainUpPedestal, 6f);
                    ped.layer = 0; ped.isStatic = true; ped.GetComponent<Renderer>().sharedMaterial = mat;
                }
            }
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
            // DES-DOG-005 M4: the play-area ring is gone (KH_Frisbee.areaRing removed); a ring left by an older Build is deleted.
            var oldRing = aimRoot.transform.Find("AreaRing"); if (oldRing != null) UnityEngine.Object.DestroyImmediate(oldRing.gameObject);
            var hintGo = Obj("Hint", aimRoot.transform);
            var hint = hintGo.GetComponent<TMPro.TextMeshPro>(); if (hint == null) hint = hintGo.AddComponent<TMPro.TextMeshPro>();
            hint.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath /* KH_HandMenuEditor は 2026-09-23 に退役 (IMP-UI-001 段階 0-b)。同じ Assets/KominkaHillside/Font/KD_NotoSerif SDF.asset */); hint.fontSize = 3f; hint.alignment = TMPro.TextAlignmentOptions.Center; hint.color = Color.white;
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
            BuildFaces(discGo, FaceKeyFor(materialName), disc);
            return disc;
        }

        // ---------------------------------------------------------------- IMP-DOG-018 dog face prints
        // A round print of the dog's face (Frisbee/6 Capture Dog Faces) on both faces of the disc, so whose disc it is
        // reads without the colour (BUG-DOG-005: iOS loses the colours). Two child renderers of one shared fan mesh,
        // no colliders (the parent's box stays the only one). The holder does not see them while holding: KH_Frisbee
        // turns faceRenderers off with the see-through material and back on at the drop.
        public const string FaceMeshPath = Data + "/KH_FrisbeeFace.asset";
        public const float FaceDiameter = .16f, FaceLift = .001f;
        public const int FaceSegments = 24;
        public static readonly string[] FaceChildren = { "FaceTop", "FaceBottom" };
        public static string FaceMaterialPathFor(string key) { return Data + "/KH_FrisbeeFace_" + key + ".mat"; }

        // The dog key from the disc material name: KH_FrisbeeDisc_<key> (world scene and the prototype's other lanes);
        // the bare KH_FrisbeeDisc is the prototype's orange Lab lane.
        public static string FaceKeyFor(string materialName)
        {
            const string prefix = "KH_FrisbeeDisc_";
            if (materialName.StartsWith(prefix, StringComparison.Ordinal)) return materialName.Substring(prefix.Length);
            if (materialName == "KH_FrisbeeDisc") return KH_FrisbeeJumpClipEditor.Breed;
            throw new InvalidOperationException("No dog key in disc material name " + materialName);
        }

        // Unit disc (diameter 1) in the xz plane facing +y: centre + FaceSegments rim vertices, UV (cos, sin) * 0.5 + 0.5,
        // so the square picture is seen unmirrored from above. Made once and reused (delete the asset to regenerate).
        public static Mesh FaceMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FaceMeshPath);
            if (mesh != null) return mesh;
            Directory.CreateDirectory(Data);
            int n = FaceSegments;
            var v = new Vector3[n + 1]; var uv = new Vector2[n + 1]; var nm = new Vector3[n + 1]; var tri = new int[n * 3];
            v[0] = Vector3.zero; uv[0] = new Vector2(.5f, .5f); nm[0] = Vector3.up;
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n, c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i + 1] = new Vector3(c * .5f, 0f, s * .5f); uv[i + 1] = new Vector2(c * .5f + .5f, s * .5f + .5f); nm[i + 1] = Vector3.up;
                // Clockwise seen from +y (Unity's front face): centre, next rim vertex, this rim vertex.
                tri[i * 3] = 0; tri[i * 3 + 1] = (i + 1) % n + 1; tri[i * 3 + 2] = i + 1;
            }
            mesh = new Mesh { name = "KH_FrisbeeFace", vertices = v, uv = uv, normals = nm, triangles = tri };
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh, FaceMeshPath);
            return mesh;
        }

        // KH_FrisbeeFace_<key>.mat: Mobile Standard Lite, white, _MainTex = the captured PNG. The texture is re-linked
        // on every Build, so capturing after the first Build only needs a rebuild. No PNG yet: material without a
        // texture and a warning (the Build goes on; Frisbee/5 Verify fails on it).
        public static Material FaceMaterial(string key)
        {
            Directory.CreateDirectory(Data);
            string path = FaceMaterialPathFor(key); var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("VRChat/Mobile/Standard Lite")); m.color = Color.white; KH_MatteMaterialsEditor.Matte(m); AssetDatabase.CreateAsset(m, path); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(KH_DogFaceCaptureEditor.TexturePathFor(key));
            if (tex == null) Debug.LogWarning("[KH Frisbee] " + KH_DogFaceCaptureEditor.TexturePathFor(key) + " missing: run Tools/KominkaHillside/Frisbee/6 Capture Dog Faces, then Build again (" + path + " has no texture)");
            if (m.mainTexture != tex || m.color != Color.white) { m.mainTexture = tex; m.color = Color.white; EditorUtility.SetDirty(m); }
            return m;
        }

        static void BuildFaces(GameObject discGo, string key, KH_Frisbee disc)
        {
            var mesh = FaceMesh(); var mat = FaceMaterial(key);
            Vector3 ps = discGo.transform.localScale;   // (0.24, 0.012, 0.24): undo it so the print is FaceDiameter across
            var renderers = new Renderer[FaceChildren.Length];
            for (int k = 0; k < FaceChildren.Length; k++)
            {
                bool top = k == 0;
                var go = Obj(FaceChildren[k], discGo.transform);
                go.layer = discGo.layer;
                // The Cylinder primitive's caps are at local y = +-1; lift the print FaceLift (world) off each cap.
                go.transform.localPosition = new Vector3(0f, (top ? 1f : -1f) * (1f + FaceLift / ps.y), 0f);
                // Bottom: turned over about z, so it faces down and still reads unmirrored from below.
                go.transform.localRotation = top ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f);
                go.transform.localScale = new Vector3(FaceDiameter / ps.x, 1f, FaceDiameter / ps.z);
                foreach (var col in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
                var mf = go.GetComponent<MeshFilter>(); if (mf == null) mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
                var mr = go.GetComponent<MeshRenderer>(); if (mr == null) mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat; mr.enabled = true;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = true;
                renderers[k] = mr;
            }
            disc.faceRenderers = renderers;
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
            // chaseRunSpeed is per breed since PCVR 2026-09-26 (ChaseRunSpeedFor, written below with the other breed values).
            catcher.chaseAcceleration = 14f; catcher.chaseDeceleration = 10f;
            catcher.maxRunAnimScale = MaxRunAnimScaleFor(facts.breed);   // per breed (PCVR 2026-09-26, table below)
            catcher.noticeDelay = 0.1f; catcher.settleTime = 0.2f; catcher.returnRunSpeed = 4.5f;
            catcher.turnSpeedDegPerSec = 240f; catcher.walkTurnDegPerSec = 180f; catcher.standTurnDegPerSec = 120f;
            // IMP-DOG-012 (Codex D1): how high the standing jump reaches is the BREED's ceiling from its facts (Lab 0.90,
            // the others scaled by the standing mouth height, floor 0.30; F1 Frisbee/2). Also bounds the clip playback below.
            catcher.maxRootRise = facts.maxRootRise;
            catcher.standoffDistance = 8f;
            // IMP-DOG-013 (user 2026-09-22 "受け取ったらすぐ走っていってしまう"): the よしよし window before the
            // stand-off run. Code-owned like the rest of the Ready geometry - the pair only reads as "it waited
            // for me" when both halves agree, so they are not left to drift apart in an Inspector.
            catcher.readyGraceSeconds = 2f; catcher.readyGraceDistance = 2.5f;
            catcher.variant = KH_FrisbeeCatch.VariantNormal; catcher.pendingVariant = -1; // code-owned: every instance starts on the baseline
            catcher.readyMaxDistance = 0f;   // BUG-DOG-007: no distance limit in the prototypes / harness scenes (the world gets 40 m from Course/92)
            // DES-DOG-005 M1: where the dog may jump / go at all. Code-owned defaults (the world scene gets the same numbers
            // from Course/92); PCVR tunes them in the Inspector, then the numbers are copied into KH_FrisbeeCatch.Default*.
            WriteAnywhereDefaults(catcher);
            // Inspector tuning survives a rebuild for everything not written above, except the one value the clip dictates: the smallest rise
            // must keep clip playback at or below 1.8x (review D2), so an older scene value is raised here.
            // Both clips play from the same minRootRise, so the floor follows the longer rise (Codex P: the
            // floor is a height, computed from the clip's rise TIME).
            float riseFloor = 0.5f * disc.gravity * Mathf.Pow(Mathf.Max(facts.rise, twist.rise) / 1.8f, 2f);
            if (catcher.minRootRise < riseFloor) catcher.minRootRise = Mathf.Ceil(riseFloor * 100f) / 100f;
            // IMP-DOG-012: a small breed's ceiling must still leave a band of jumps above the playback floor.
            if (catcher.maxRootRise <= catcher.minRootRise) throw new InvalidOperationException(catcher.name + ": maxRootRise " + catcher.maxRootRise + " <= minRootRise " + catcher.minRootRise + " (no jump band left)");
            // IMP-DOG-017 (user 2026-09-26 "リアルの犬を検討しつつ不自然でない推奨値"): the breed's own chase-down sprint,
            // the turn from a standstill and the rise that makes a jump a jump. Code-owned per breed, like maxRootRise.
            catcher.chaseRunSpeed = ChaseRunSpeedFor(facts.breed);   // PCVR 2026-09-26 "全体的に足早すぎ、特に小型犬"
            catcher.chaseSprintSpeed = ChaseSprintSpeedFor(facts.breed);
            catcher.launchTurnDegPerSec = LaunchTurnDegPerSec;
            catcher.jumpFloorRise = JumpFloorRiseFor(facts);
            // The planner's real floor is max(jumpFloorRise, minRootRise): a playback floor above the breed's jump floor
            // silently takes the low jumps away again (the small-breed "does not jump" of IMP-DOG-017).
            if (catcher.minRootRise > catcher.jumpFloorRise + 1e-4f)
                Debug.LogWarning("[KH Frisbee] " + catcher.name + ": minRootRise " + catcher.minRootRise.ToString("F3") + " > jumpFloorRise " + catcher.jumpFloorRise.ToString("F3")
                    + " (" + facts.breed + "): the jump floor does not take effect, the clip playback floor decides");
            disc.catcher = catcher;
        }

        // DES-DOG-005 M1: the four reachability values, the same in the prototype (ConfigureCatcher) and the world scene
        // (Course/92). Raw assignment here because ConfigureCatcher's caller copies proxy -> Udon for the whole scene.
        public static void WriteAnywhereDefaults(KH_FrisbeeCatch c)
        {
            c.jumpMaxSlopeDeg = KH_FrisbeeCatch.DefaultJumpMaxSlopeDeg; c.jumpMaxRise = KH_FrisbeeCatch.DefaultJumpMaxRise;
            c.runMaxSlopeDeg = KH_FrisbeeCatch.DefaultRunMaxSlopeDeg; c.runMaxStep = KH_FrisbeeCatch.DefaultRunMaxStep;
        }

        // What Verify / Course/93 / Walking/5 require of one catcher's DES-DOG-005 values: proxy AND backing heap equal the
        // defaults. Empty = fine.
        public static string AnywhereProblems(KH_FrisbeeCatch c)
        {
            var bad = new System.Collections.Generic.List<string>();
            var b = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            void One(string name, float proxy, float want)
            {
                if (Mathf.Abs(proxy - want) > 1e-4f) bad.Add(name + " proxy " + proxy + " != " + want);
                if (b == null || !b.publicVariables.TryGetVariableValue(name, out float heap) || Mathf.Abs(heap - want) > 1e-4f) bad.Add(name + " backing heap is not " + want);
            }
            One("jumpMaxSlopeDeg", c.jumpMaxSlopeDeg, KH_FrisbeeCatch.DefaultJumpMaxSlopeDeg);
            One("jumpMaxRise", c.jumpMaxRise, KH_FrisbeeCatch.DefaultJumpMaxRise);
            One("runMaxSlopeDeg", c.runMaxSlopeDeg, KH_FrisbeeCatch.DefaultRunMaxSlopeDeg);
            One("runMaxStep", c.runMaxStep, KH_FrisbeeCatch.DefaultRunMaxStep);
            return string.Join("; ", bad);
        }

        // DES-DOG-005 B (replaces the circle half of Codex D1's pure check): the asymmetric arc as pure geometry, the same
        // statics the planner and the liftoff gate call (DescentTime / AirTime / LandingOf). Returns "" or the failures.
        public static string AsymmetricLandingProblems()
        {
            var bad = new System.Collections.Generic.List<string>();
            float g = 9.81f, tUp = 0.25f, vTake = 2f;               // rise = 0.5 g tUp^2 = 0.307 m
            var lift = new Vector3(3f, 1f, -7f); var fwd = new Vector3(0.6f, 0f, 0.8f);
            float level = KH_FrisbeeCatch.DescentTime(tUp, g, 0f);
            if (Mathf.Abs(level - tUp) > 1e-4f) bad.Add("level ground: tDown " + level.ToString("F4") + " != tUp " + tUp);
            if ((KH_FrisbeeCatch.LandingOf(lift, fwd, vTake, tUp, level) - KH_FrisbeeCatch.LandingOf(lift, fwd, vTake, tUp)).magnitude > 1e-4f)
                bad.Add("level ground: the asymmetric LandingOf differs from the symmetric one");
            float down = KH_FrisbeeCatch.DescentTime(tUp, g, -0.4f), up = KH_FrisbeeCatch.DescentTime(tUp, g, 0.15f);
            float downWant = Mathf.Sqrt(2f * (0.5f * g * tUp * tUp + 0.4f) / g), upWant = Mathf.Sqrt(2f * (0.5f * g * tUp * tUp - 0.15f) / g);
            if (Mathf.Abs(down - downWant) > 1e-4f || down <= tUp) bad.Add("0.4 m lower: tDown " + down.ToString("F4") + " (want " + downWant.ToString("F4") + ", > tUp)");
            if (Mathf.Abs(up - upWant) > 1e-4f || up >= tUp) bad.Add("0.15 m higher: tDown " + up.ToString("F4") + " (want " + upWant.ToString("F4") + ", < tUp)");
            float dDown = (KH_FrisbeeCatch.LandingOf(lift, fwd, vTake, tUp, down) - lift).magnitude;
            if (Mathf.Abs(dDown - vTake * (tUp + downWant)) > 1e-3f) bad.Add("0.4 m lower: the jump travels " + dDown.ToString("F3") + " m, want " + (vTake * (tUp + downWant)).ToString("F3"));
            // The apex must clear the landing ground by JumpApexClearance (0.307 m rise: a 0.25 m step up is refused).
            if (KH_FrisbeeCatch.DescentTime(tUp, g, 0.25f) >= 0f) bad.Add("a landing 0.25 m up (apex clears it by only 0.057 m) was not refused");
            // AirTime (the liftoff's form, from vy) agrees with tUp + DescentTime when vy = g tUp.
            float ta = KH_FrisbeeCatch.AirTime(g * tUp, g, -0.4f);
            if (Mathf.Abs(ta - (tUp + downWant)) > 1e-4f) bad.Add("AirTime " + ta.ToString("F4") + " != tUp + tDown " + (tUp + downWant).ToString("F4"));
            // The slope rule reads acos(normal.y): 14.9 deg passes 15, 15.1 does not.
            if (!KH_FrisbeeCatch.SlopeOk(Mathf.Cos(14.9f * Mathf.Deg2Rad), 15f) || KH_FrisbeeCatch.SlopeOk(Mathf.Cos(15.2f * Mathf.Deg2Rad), 15f)) bad.Add("SlopeOk does not split 14.9 / 15.2 deg at 15");
            return string.Join("; ", bad);
        }

        // IMP-DOG-017 breed table. Real sprints: Lab / Shiba / Samoyed 30-40 km/h (8-11 m/s), Corgi / Dachs 25-30,
        // Pome 20-25. PCVR 2026-09-26 "全体的に足早すぎ、特に小型犬": the shared 9.5 m/s chase is gone, so both the
        // chase run (A / C and every other chase) and the chase-down burst (B only) are per breed, the small dogs
        // well below the large ones:
        //   breed    chaseRunSpeed  chaseSprintSpeed
        //   Lab           8.5           10.0
        //   Samoyed       8.5           10.0
        //   Shiba         8.0            9.5
        //   Corgi         7.0            8.5
        //   Dachs         7.0            8.5
        //   Pome          6.5            8.0
        // chaseAcceleration stays 14 for all. 480 deg/s = turning from a standstill (a real dog swings half round in
        // 0.3 s = 600 deg/s); running turns stay at turnSpeedDegPerSec.
        public const float LaunchTurnDegPerSec = 480f;
        public const float JumpFloorRiseCap = 0.25f, JumpFloorMouthRatio = 0.6f;
        public static float ChaseRunSpeedFor(string breed)
        {
            switch (breed)
            {
                case "Lab": case "Samoyed": return 8.5f;
                case "Shiba": return 8.0f;
                case "Corgi": case "Dachs": return 7.0f;
                case "Pome": return 6.5f;
                default: throw new InvalidOperationException("No chaseRunSpeed for breed " + breed + " (IMP-DOG-017 table)");
            }
        }
        // Run clip speed-up cap per breed. PCVR 2026-09-26 "大型犬は一歩がでかい感じの速さにしたい": at the sprint the
        // shared 2.5x turned the big dogs' run into tiny rapid steps (Samoyed worst). Big dogs read as long strides
        // at the cost of some foot slide; the small dogs' quick cadence reads as cute and keeps 2.5x.
        //   Lab / Samoyed 1.7, Shiba 2.0, Corgi / Dachs / Pome 2.5
        public static float MaxRunAnimScaleFor(string breed)
        {
            switch (breed)
            {
                case "Lab": case "Samoyed": return 1.7f;
                case "Shiba": return 2.0f;
                case "Corgi": case "Dachs": case "Pome": return 2.5f;
                default: throw new InvalidOperationException("No maxRunAnimScale for breed " + breed + " (IMP-DOG-017 table)");
            }
        }
        public static float ChaseSprintSpeedFor(string breed)
        {
            switch (breed)
            {
                case "Lab": case "Samoyed": return 10.0f;
                case "Shiba": return 9.5f;
                case "Corgi": case "Dachs": return 8.5f;
                case "Pome": return 8.0f;
                default: throw new InvalidOperationException("No chaseSprintSpeed for breed " + breed + " (IMP-DOG-017 table)");
            }
        }
        // min(0.25, 0.6 x standing mouth height): Pome 0.094 / Corgi 0.137 / Dachs 0.138 / Shiba 0.215 / Samoyed, Lab 0.25.
        public static float JumpFloorRiseFor(Facts facts) { return Mathf.Min(JumpFloorRiseCap, JumpFloorMouthRatio * facts.mouthOffsetStand.y); }

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
                KH_FrisbeeCatch.StatePickup, KH_FrisbeeCatch.StateReturn,
                KH_FrisbeeCatch.StateWaterChase, KH_FrisbeeCatch.StateWaterCarry, KH_FrisbeeCatch.StateWaterExit })   // DES-DOG-005 M3
                if (KH_FrisbeeCatch.PetAllowedIn(s)) return false;
            return true;
        }

        // name / breed are what the JSON itself says (checked against the path it was read from). maxRootRise is the
        // breed's own jump ceiling (IMP-DOG-012, written by F1's Frisbee/2): ConfigureCatcher copies it to the catcher.
        public struct Facts { public string name, breed; public Vector3 mouthOffsetCatch, mouthOffsetStand; public float crouch, rise, recover, clipLength, maxRootRise; }

        public static Facts ReadFacts() { return ReadFacts("Jump"); }

        // One clip variant's facts (KH_FrisbeeJumpClipEditor.Variants(): "Jump", "Twist").
        public static Facts ReadFacts(string name) { return ReadFacts(name, KH_FrisbeeJumpClipEditor.Breed); }

        // IMP-DOG-012 (Codex M1): the one typed reader for Data/Frisbee/KH_{name}_{breed}.json. It throws unless
        // the file exists, names THIS variant and THIS breed (a Lab file copied onto the Pome path is refused), has
        // every key with 3-component vectors, and carries plausible times and a maxRootRise (no fall-back value).
        public static Facts ReadFacts(string name, string breed)
        {
            string path = KH_FrisbeeJumpClipEditor.FactsPathFor(name, breed);
            if (!File.Exists(path)) throw new InvalidOperationException("Run 2 Build Jump Clip first for " + breed + " (" + path + ")");
            string s = File.ReadAllText(path);
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            int KeyAt(string key)
            {
                int i = s.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
                if (i < 0) throw new InvalidOperationException(path + ": key \"" + key + "\" missing (re-run Frisbee/2 for " + breed + ")");
                return i + key.Length + 2;
            }
            string Str(string key)
            {
                int i = KeyAt(key); int a = s.IndexOf('"', s.IndexOf(':', i) + 1), b = a < 0 ? -1 : s.IndexOf('"', a + 1);
                if (a < 0 || b < 0) throw new InvalidOperationException(path + ": \"" + key + "\" is not a string");
                return s.Substring(a + 1, b - a - 1);
            }
            Vector3 Vec(string key)
            {
                int i = KeyAt(key); int a = s.IndexOf('[', i), b = a < 0 ? -1 : s.IndexOf(']', a);
                if (a < 0 || b < 0) throw new InvalidOperationException(path + ": \"" + key + "\" is not an array");
                var parts = s.Substring(a + 1, b - a - 1).Split(',');
                if (parts.Length != 3) throw new InvalidOperationException(path + ": \"" + key + "\" has " + parts.Length + " components, expected 3");
                var v = new float[3];
                for (int k = 0; k < 3; k++)
                    if (!float.TryParse(parts[k].Trim(), System.Globalization.NumberStyles.Float, ic, out v[k]) || float.IsNaN(v[k]) || float.IsInfinity(v[k]))
                        throw new InvalidOperationException(path + ": \"" + key + "\"[" + k + "] is not a finite number");
                return new Vector3(v[0], v[1], v[2]);
            }
            float Num(string key)
            {
                int i = KeyAt(key); int a = s.IndexOf(':', i) + 1; int b = s.IndexOfAny(new[] { ',', '\n', '}' }, a);
                float v;
                if (a <= 0 || b < 0 || !float.TryParse(s.Substring(a, b - a).Trim(), System.Globalization.NumberStyles.Float, ic, out v) || float.IsNaN(v) || float.IsInfinity(v))
                    throw new InvalidOperationException(path + ": \"" + key + "\" is not a finite number");
                return v;
            }
            var f = new Facts
            {
                name = Str("name"), breed = Str("breed"),
                mouthOffsetCatch = Vec("mouthOffsetCatch"), mouthOffsetStand = Vec("mouthOffsetStand"),
                crouch = Num("crouch"), rise = Num("rise"), recover = Num("recover"), clipLength = Num("clipLength"), maxRootRise = Num("maxRootRise"),
            };
            var bad = new System.Collections.Generic.List<string>();
            if (f.name != name) bad.Add("name \"" + f.name + "\" != \"" + name + "\"");
            if (f.breed != breed) bad.Add("breed \"" + f.breed + "\" != \"" + breed + "\"");
            if (f.crouch <= 0.05f || f.crouch > 1f) bad.Add("crouch " + f.crouch + " not in (0.05, 1]");
            if (f.rise <= 0.05f || f.rise > 1f) bad.Add("rise " + f.rise + " not in (0.05, 1]");
            if (f.recover <= 0.05f || f.recover > 1.5f) bad.Add("recover " + f.recover + " not in (0.05, 1.5]");
            float expectLen = f.crouch + 2f * f.rise + f.recover;
            if (Mathf.Abs(f.clipLength - expectLen) > 0.02f) bad.Add("clipLength " + f.clipLength + " != crouch + 2 rise + recover " + expectLen.ToString("F3"));
            // The ceiling decision (2026-09-24): Lab keeps 0.90, the others scale by the standing mouth height, floor 0.30.
            if (f.maxRootRise < MinMaxRootRise - 1e-3f || f.maxRootRise > MaxMaxRootRise) bad.Add("maxRootRise " + f.maxRootRise + " not in [" + MinMaxRootRise + ", " + MaxMaxRootRise + "]");
            if (f.mouthOffsetStand.y <= 0f || f.mouthOffsetCatch.y <= 0f) bad.Add("mouth heights must be above the root (stand y " + f.mouthOffsetStand.y + ", catch y " + f.mouthOffsetCatch.y + ")");
            if (bad.Count > 0) throw new InvalidOperationException(path + ": " + string.Join("; ", bad));
            return f;
        }

        // Bounds on a breed's jump ceiling as read from its facts (0.30 = the floor of the ceiling rule, 1.5 m = no dog).
        public const float MinMaxRootRise = 0.30f, MaxMaxRootRise = 1.5f;

        // IMP-DOG-012 (Codex M1): every breed x both variants, read and validated BEFORE a builder touches its scene.
        // All problems are collected and thrown once. The Jump and Twist files of a breed must agree on maxRootRise
        // (the catcher has one ceiling) and on the standing mouth (both clips start from the same idle pose).
        public static void ReadAllFacts(string[] breeds, out Facts[] jump, out Facts[] twist)
        {
            jump = new Facts[breeds.Length]; twist = new Facts[breeds.Length];
            var bad = new System.Collections.Generic.List<string>();
            for (int i = 0; i < breeds.Length; i++)
            {
                try { jump[i] = ReadFacts("Jump", breeds[i]); } catch (Exception e) { bad.Add(e.Message); continue; }
                try { twist[i] = ReadFacts("Twist", breeds[i]); } catch (Exception e) { bad.Add(e.Message); continue; }
                if (Mathf.Abs(jump[i].maxRootRise - twist[i].maxRootRise) > 1e-3f)
                    bad.Add(breeds[i] + ": Jump maxRootRise " + jump[i].maxRootRise + " != Twist " + twist[i].maxRootRise);
                if (Vector3.Distance(jump[i].mouthOffsetStand, twist[i].mouthOffsetStand) > 1e-3f)
                    bad.Add(breeds[i] + ": Jump and Twist disagree on mouthOffsetStand");
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(KH_FrisbeeJumpClipEditor.ClipPathFor("Jump", breeds[i]));
                var tclip = AssetDatabase.LoadAssetAtPath<AnimationClip>(KH_FrisbeeJumpClipEditor.ClipPathFor("Twist", breeds[i]));
                if (clip == null) bad.Add("clip missing: " + KH_FrisbeeJumpClipEditor.ClipPathFor("Jump", breeds[i]));
                else if (Mathf.Abs(clip.length - jump[i].clipLength) > 0.02f) bad.Add(breeds[i] + ": Jump clip length " + clip.length.ToString("F3") + " != facts " + jump[i].clipLength.ToString("F3"));
                if (tclip == null) bad.Add("clip missing: " + KH_FrisbeeJumpClipEditor.ClipPathFor("Twist", breeds[i]));
                else if (Mathf.Abs(tclip.length - twist[i].clipLength) > 0.02f) bad.Add(breeds[i] + ": Twist clip length " + tclip.length.ToString("F3") + " != facts " + twist[i].clipLength.ToString("F3"));
            }
            if (bad.Count > 0) throw new InvalidOperationException("Frisbee facts / clips not usable (run Frisbee/2 Build Jump Clip first): " + string.Join(" | ", bad));
        }

        // IMP-DOG-018: what Build promised about one disc's face prints; empty when all is well. Public so another
        // builder's Verify (the world scene discs) can reuse it.
        public static string FaceProblems(KH_Frisbee disc, string key)
        {
            var bad = new System.Collections.Generic.List<string>();
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FaceMeshPath);
            if (mesh == null) bad.Add("mesh " + FaceMeshPath + " missing");
            var found = new Renderer[FaceChildren.Length];
            for (int k = 0; k < FaceChildren.Length; k++)
            {
                var t = disc.transform.Find(FaceChildren[k]);
                if (t == null) { bad.Add(FaceChildren[k] + " missing"); continue; }
                if (disc.transform.Cast<Transform>().Count(c => c.name == FaceChildren[k]) != 1) bad.Add(FaceChildren[k] + " duplicated");
                var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>(); found[k] = mr;
                if (mf == null || mf.sharedMesh != mesh || mesh == null) bad.Add(FaceChildren[k] + " does not use " + FaceMeshPath);
                if (mr == null || !mr.enabled) { bad.Add(FaceChildren[k] + " has no enabled MeshRenderer"); continue; }
                var mat = mr.sharedMaterial;
                if (mat == null || AssetDatabase.GetAssetPath(mat) != FaceMaterialPathFor(key)) bad.Add(FaceChildren[k] + " material is not " + FaceMaterialPathFor(key));
                else if (mat.mainTexture == null) bad.Add(FaceMaterialPathFor(key) + " has no texture (Frisbee/6, then Build)");
                else
                {
                    string tp = AssetDatabase.GetAssetPath(mat.mainTexture), why;
                    if (tp != KH_DogFaceCaptureEditor.TexturePathFor(key)) bad.Add("texture is " + tp + ", expected " + KH_DogFaceCaptureEditor.TexturePathFor(key));
                    else if (!KH_DogFaceCaptureEditor.ImporterOk(tp, out why)) bad.Add(tp + " importer: " + why);
                }
                if (t.GetComponents<Collider>().Length > 0) bad.Add(FaceChildren[k] + " carries a collider");
                if (t.gameObject.layer != disc.gameObject.layer) bad.Add(FaceChildren[k] + " layer " + t.gameObject.layer + " != disc " + disc.gameObject.layer);
                float facing = Vector3.Dot(t.up, disc.transform.up);
                if (k == 0 ? facing < .99f : facing > -.99f) bad.Add(FaceChildren[k] + " faces the wrong way (up . disc up = " + facing.ToString("F2") + ")");
                float across = (t.lossyScale.x + t.lossyScale.z) * .5f;
                if (Mathf.Abs(across - FaceDiameter) > .005f) bad.Add(FaceChildren[k] + " is " + across.ToString("F3") + " m across, expected " + FaceDiameter.ToString("F3"));
            }
            if (disc.faceRenderers == null || disc.faceRenderers.Length != FaceChildren.Length || !disc.faceRenderers.SequenceEqual(found)) bad.Add("KH_Frisbee.faceRenderers (proxy) is not FaceTop / FaceBottom");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(disc);
            if (backing == null || !backing.publicVariables.TryGetVariableValue("faceRenderers", out Renderer[] bfr) || bfr == null || !bfr.SequenceEqual(found))
                bad.Add("backing heap faceRenderers is not FaceTop / FaceBottom (Build copies the proxy)");
            return string.Join("; ", bad);
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
            // IMP-DOG-018: the six captured face pictures (Frisbee/6), each at most 256 on every platform.
            foreach (string key in KH_DogWalkingEditor.FrisbeeKeys)
            {
                string fp = KH_DogFaceCaptureEditor.TexturePathFor(key);
                var ft = AssetDatabase.LoadAssetAtPath<Texture2D>(fp);
                string why = "missing";
                Check(ft != null && KH_DogFaceCaptureEditor.ImporterOk(fp, out why) && Mathf.Max(ft.width, ft.height) <= KH_DogFaceCaptureEditor.Size,
                    "face picture " + fp + (ft == null ? " missing (run Frisbee/6 Capture Dog Faces)" : " " + ft.width + "x" + ft.height + ", importer " + why));
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
            var cleanOthers = CleanOtherScenes();
            if (File.Exists(ScenePath)) { previousScene = OpenPrototype(false); open = SceneManager.GetActiveScene(); opened = true; }
            try
            {
            if (opened)
            {
                // IMP-DOG-012: six lanes. The Lab lane gets every check below (it is the lane all earlier records were
                // taken on); the other lanes get the per-breed wiring checks in the block after it.
                var allCatchers = open.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<KH_FrisbeeCatch>(true)).ToArray();
                var allLaunchers = open.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<KH_FrisbeeLauncher>(true)).ToArray();
                var catcher = allCatchers.FirstOrDefault(x => x.name == "KH_FrisbeeCatch_" + KH_FrisbeeJumpClipEditor.Breed);
                var disc = catcher == null ? null : catcher.frisbee;
                var launchers = allLaunchers.Where(l => catcher != null && l.catcher == catcher).ToArray();
                Check(catcher != null && disc != null && launchers.Length == 3, "Lab lane has catcher, disc and three launchers");
                // DES-DOG-005 M1: + the Terrain lane (one more catcher and three more launchers).
                Check(allCatchers.Length == LaneBreeds.Length + 1 && allLaunchers.Length == 3 * (LaneBreeds.Length + 1),
                    "one lane per breed + the Terrain lane: " + allCatchers.Length + " catchers / " + allLaunchers.Length + " launchers (expect " + (LaneBreeds.Length + 1) + " / " + (3 * (LaneBreeds.Length + 1)) + ")");
                foreach (var kc in allCatchers)
                {
                    string ap = AnywhereProblems(kc);
                    Check(ap.Length == 0, kc.name + ": DES-DOG-005 values jumpMaxSlopeDeg / jumpMaxRise / runMaxSlopeDeg / runMaxStep == defaults, proxy and backing" + (ap.Length == 0 ? "" : " -- " + ap));
                }
                {
                    // DES-DOG-005 M1 Terrain lane: its catcher / disc / launchers, and every pad's geometry.
                    var tc = allCatchers.FirstOrDefault(x => x.name == "KH_FrisbeeCatch_" + TerrainTag);
                    var td = tc == null ? null : tc.frisbee;
                    var tl = allLaunchers.Where(l => tc != null && l.catcher == tc).ToArray();
                    Check(tc != null && td != null && td.name == "KH_Frisbee_" + TerrainTag && td.catcher == tc && tc.dogRoot != null && tc.dogRoot.name == "KD_Dog_" + TerrainTag
                        && tc.dogAnimator != null && tc.mouthAnchor != null && tl.Length == 3 && tl.All(l => l.frisbee == td && l.muzzle != null)
                        && tl.Any(l => !l.recallOnly && !l.cycleVariant && l.name == "KH_FrisbeeStand_" + TerrainTag),
                        "Terrain lane: catcher, disc KH_Frisbee_" + TerrainTag + ", dog KD_Dog_" + TerrainTag + ", three launchers (KH_FrisbeeStand_" + TerrainTag + " throws)");
                    if (tc != null)
                    {
                        var tf = ReadFacts("Jump", TerrainBreed);
                        Check(Vector3.Distance(tc.mouthOffsetCatch, tf.mouthOffsetCatch) < 1e-4f && Mathf.Abs(tc.maxRootRise - tf.maxRootRise) < 1e-4f
                            && Mathf.Abs(tc.chaseRunSpeed - ChaseRunSpeedFor(TerrainBreed)) < 1e-4f && tc.playAreaRadius == 0f && tc.variant == KH_FrisbeeCatch.VariantNormal && tc.debugHeldOverride == 0,
                            "Terrain lane: the " + TerrainBreed + " facts and breed table, no play area, variant Normal, held override 0");
                    }
                    var troot = open.GetRootGameObjects().Select(r => r.transform.Find("KH_FrisbeeTerrain")).FirstOrDefault(t => t != null);
                    foreach (string tcase in TerrainCases)
                    {
                        var pad = troot == null ? null : troot.Find(tcase);
                        bool padOk = pad != null && Vector3.Distance(pad.position, TerrainPad(tcase)) < 1e-3f && pad.childCount == (tcase == "T-boxtop" || tcase == "T-up04" ? 2 : 1)
                            && pad.Cast<Transform>().All(ch => ch.GetComponent<Collider>() != null && ch.gameObject.layer == 0 && ch.gameObject.isStatic);
                        Check(padOk, "Terrain pad " + tcase + " at " + TerrainPad(tcase).ToString("F0") + " with its static layer-0 collider(s)" + (pad == null ? " (missing: Frisbee/4 Build Scene)" : ""));
                    }
                }
                foreach (string breed in LaneBreeds)
                {
                    var lc = allCatchers.FirstOrDefault(x => x.name == "KH_FrisbeeCatch_" + breed);
                    var ld = lc == null ? null : lc.frisbee;
                    bool wired = lc != null && ld != null && ld.name == LaneName("KH_Frisbee", breed) && ld.catcher == lc && lc.dogRoot != null && lc.dogRoot.name == "KD_Dog_" + breed
                        && lc.dogAnimator != null && lc.mouthAnchor != null;
                    Check(wired, "lane " + breed + ": catcher, own disc " + LaneName("KH_Frisbee", breed) + ", dog KD_Dog_" + breed);
                    if (!wired) continue;
                    var lf = ReadFacts("Jump", breed); var lt = ReadFacts("Twist", breed);
                    Check(Vector3.Distance(lc.mouthOffsetCatch, lf.mouthOffsetCatch) < 1e-4f && Vector3.Distance(lc.mouthOffsetStand, lf.mouthOffsetStand) < 1e-4f
                        && Mathf.Abs(lc.clipCrouchDuration - lf.crouch) < 1e-4f && Mathf.Abs(lc.clipRiseDuration - lf.rise) < 1e-4f && Mathf.Abs(lc.clipRecoverDuration - lf.recover) < 1e-4f
                        && Vector3.Distance(lc.mouthOffsetCatchTwist, lt.mouthOffsetCatch) < 1e-4f && Mathf.Abs(lc.clipRiseDurationTwist - lt.rise) < 1e-4f,
                        "lane " + breed + ": pose facts (Jump + Twist) equal that breed's JSON");
                    // Codex D1: the jump ceiling is the breed's own, proxy and backing heap.
                    var lcb = UdonSharpEditorUtility.GetBackingUdonBehaviour(lc);
                    Check(Mathf.Abs(lc.maxRootRise - lf.maxRootRise) < 1e-4f && lcb.publicVariables.TryGetVariableValue("maxRootRise", out float lmr) && Mathf.Abs(lmr - lf.maxRootRise) < 1e-4f,
                        "lane " + breed + ": catcher maxRootRise " + lc.maxRootRise.ToString("F2") + " == facts " + lf.maxRootRise.ToString("F2") + " (proxy and backing)");
                    // IMP-DOG-017: the breed table, proxy and backing heap.
                    float lfloor = JumpFloorRiseFor(lf);
                    Check(Mathf.Abs(lc.maxRunAnimScale - MaxRunAnimScaleFor(breed)) < 1e-4f && lc.maxRunAnimScale >= 1.5f && lc.maxRunAnimScale <= 3f
                        && lcb.publicVariables.TryGetVariableValue("maxRunAnimScale", out float lra) && Mathf.Abs(lra - lc.maxRunAnimScale) < 1e-4f,
                        "lane " + breed + ": run clip speed cap " + lc.maxRunAnimScale.ToString("F1") + "x == breed table " + MaxRunAnimScaleFor(breed).ToString("F1") + " within [1.5, 3.0], proxy and backing (PCVR 2026-09-26)");
                    Check(Mathf.Abs(lc.chaseRunSpeed - ChaseRunSpeedFor(breed)) < 1e-4f && Mathf.Abs(lc.chaseSprintSpeed - ChaseSprintSpeedFor(breed)) < 1e-4f && Mathf.Abs(lc.launchTurnDegPerSec - LaunchTurnDegPerSec) < 1e-4f && Mathf.Abs(lc.jumpFloorRise - lfloor) < 1e-3f
                        && lcb.publicVariables.TryGetVariableValue("chaseRunSpeed", out float lrs) && Mathf.Abs(lrs - lc.chaseRunSpeed) < 1e-4f
                        && lcb.publicVariables.TryGetVariableValue("chaseSprintSpeed", out float lss) && Mathf.Abs(lss - lc.chaseSprintSpeed) < 1e-4f
                        && lcb.publicVariables.TryGetVariableValue("launchTurnDegPerSec", out float llt) && Mathf.Abs(llt - lc.launchTurnDegPerSec) < 1e-4f
                        && lcb.publicVariables.TryGetVariableValue("jumpFloorRise", out float ljf) && Mathf.Abs(ljf - lc.jumpFloorRise) < 1e-4f,
                        "lane " + breed + ": chase run " + lc.chaseRunSpeed.ToString("F1") + " / chase-down sprint " + lc.chaseSprintSpeed.ToString("F1") + " / launch turn " + lc.launchTurnDegPerSec.ToString("F0") + " / jump floor " + lc.jumpFloorRise.ToString("F3")
                        + " == breed table (floor = min(0.25, 0.6 x stand mouth " + lf.mouthOffsetStand.y.ToString("F3") + ") = " + lfloor.ToString("F3") + "), proxy and backing");
                    Check(lc.chaseSprintSpeed >= lc.chaseRunSpeed && lc.chaseSprintSpeed <= 11f && lc.launchTurnDegPerSec >= 240f && lc.launchTurnDegPerSec <= 720f && lc.jumpFloorRise < lc.maxRootRise,
                        "lane " + breed + ": sprint in [chaseRunSpeed " + lc.chaseRunSpeed.ToString("F1") + ", 11], launch turn in [240, 720], jump floor < maxRootRise " + lc.maxRootRise.ToString("F2"));
                    if (lc.minRootRise > lc.jumpFloorRise + 1e-4f)
                        log.AppendLine("INFO lane " + breed + ": minRootRise " + lc.minRootRise.ToString("F3") + " > jumpFloorRise " + lc.jumpFloorRise.ToString("F3") + " (the playback floor decides the lowest jump)");
                    var lac = lc.dogAnimator.runtimeAnimatorController as AnimatorController;
                    var ljump = AssetDatabase.LoadAssetAtPath<AnimationClip>(KH_FrisbeeJumpClipEditor.ClipPathFor("Jump", breed));
                    var ltwist = AssetDatabase.LoadAssetAtPath<AnimationClip>(KH_FrisbeeJumpClipEditor.ClipPathFor("Twist", breed));
                    Check(lac != null && AssetDatabase.GetAssetPath(lac) == KH_FrisbeeJumpClipEditor.ControllerPathFor(breed)
                        && lac.layers[0].stateMachine.states.Any(st => st.state.name == "Jump" && st.state.motion == ljump && ljump != null)
                        && lac.layers[0].stateMachine.states.Any(st => st.state.name == "Twist" && st.state.motion == ltwist && ltwist != null),
                        "lane " + breed + ": Animator " + KH_FrisbeeJumpClipEditor.ControllerPathFor(breed) + " plays that breed's Jump / Twist clips");
                    Check(KH_FrisbeeJumpClipEditor.IsBodyRigid(lc.dogRoot.gameObject) && lc.dogRoot.GetComponent<VRCObjectSync>() != null
                        && lc.dogRoot.GetComponentsInChildren<UdonBehaviour>(true).Length == 0, "lane " + breed + ": body rigid, VRCObjectSync, no world-scene behaviours");
                    var ll = allLaunchers.Where(l => l.catcher == lc).ToArray();
                    Check(ll.Length == 3 && ll.All(l => l.frisbee == ld && l.muzzle != null) && ll.Count(l => l.recallOnly) == 1 && ll.Count(l => l.cycleVariant) == 1
                        && ll.Any(l => !l.recallOnly && !l.cycleVariant && l.name == LaneName("KH_FrisbeeStand", breed)),
                        "lane " + breed + ": three launchers (" + LaneName("KH_FrisbeeStand", breed) + " throw, recall, variant) on its own disc");
                    float dx = LaneOffsetX(breed);
                    Check(Mathf.Abs(lc.dogRoot.position.x - (dx + 1.5f)) < 0.01f && Mathf.Abs(lc.dogRoot.position.z - 6.5f) < 0.01f && Mathf.Abs(lc.dogRoot.position.y) < 0.01f,
                        "lane " + breed + ": dog stands at the lane's copy of the Lab spot (x " + (dx + 1.5f).ToString("F1") + ", z 6.5, y 0)");
                    Check(lc.variant == KH_FrisbeeCatch.VariantNormal && lc.pendingVariant == -1 && lc.debugHeldOverride == 0 && lc.playAreaRadius == 0f,
                        "lane " + breed + ": variant Normal, nothing queued, held override 0, no play area (same as the Lab lane)");
                    string faceProblem = FaceProblems(ld, breed);
                    Check(faceProblem.Length == 0, "lane " + breed + ": FaceTop / FaceBottom prints of " + breed + " on " + ld.name + (faceProblem.Length == 0 ? "" : " -- " + faceProblem));
                }
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
                    // IMP-DOG-008 round 2 (intercept-first) promised the catch with a shared 9.5 m/s sprint. PCVR 2026-09-26
                    // ("全体的に足早すぎ、特に小型犬"): the chase run is the breed's own now (IMP-DOG-017 table).
                    Check(Mathf.Abs(catcher.chaseRunSpeed - ChaseRunSpeedFor(KH_FrisbeeJumpClipEditor.Breed)) < 1e-4f,
                        "chase run " + catcher.chaseRunSpeed.ToString("F1") + " m/s == breed table " + ChaseRunSpeedFor(KH_FrisbeeJumpClipEditor.Breed).ToString("F1") + " (" + KH_FrisbeeJumpClipEditor.Breed + ", PCVR 2026-09-26)");
                    // Round 4: both of these are now bands, not floors. Too low and the plan cannot keep its ETA;
                    // too high and the dog reads as a machine (PCVR 3), which no catch rate can make up for.
                    Check(catcher.chaseAcceleration >= 10f && catcher.chaseAcceleration <= 18f, "chase acceleration " + catcher.chaseAcceleration.ToString("F1") + " m/s2 in [10, 18] (reached, but visibly: " + (catcher.chaseRunSpeed / catcher.chaseAcceleration).ToString("F2") + " s to the run)");
                    Check(catcher.settleTime >= 0.1f && catcher.settleTime <= 0.6f, "settle time " + catcher.settleTime.ToString("F2") + " s in [0.10, 0.60]");
                    Check(catcher.turnSpeedDegPerSec >= 180f && catcher.turnSpeedDegPerSec <= 360f, "turn rate " + catcher.turnSpeedDegPerSec.ToString("F0") + " deg/s in [180, 360] (a dog's turn, not a turret's)");
                    // IMP-DOG-017: the hook loop grid (Hook*) is gone. What replaced it is per catcher: the chase-down
                    // sprint (B only) sits between the chase run and a real dog's burst, the launch turn between the
                    // running turn and a spin, and the jump floor is the breed table's and still leaves a band to jump in.
                    Check(catcher.chaseSprintSpeed >= catcher.chaseRunSpeed && catcher.chaseSprintSpeed <= 11f,
                        "chase-down sprint " + catcher.chaseSprintSpeed.ToString("F1") + " m/s in [chaseRunSpeed " + catcher.chaseRunSpeed.ToString("F1") + ", 11.0] (B only; A / C run at chaseRunSpeed)");
                    Check(catcher.launchTurnDegPerSec >= 240f && catcher.launchTurnDegPerSec <= 720f,
                        "launch turn " + catcher.launchTurnDegPerSec.ToString("F0") + " deg/s in [240, 720] (from a standstill; running turns stay at " + catcher.turnSpeedDegPerSec.ToString("F0") + ")");
                    Check(Mathf.Abs(catcher.jumpFloorRise - JumpFloorRiseFor(sceneFacts)) < 1e-3f && catcher.jumpFloorRise < catcher.maxRootRise,
                        "jump floor " + catcher.jumpFloorRise.ToString("F3") + " == min(0.25, 0.6 x stand mouth " + sceneFacts.mouthOffsetStand.y.ToString("F3") + ") and < maxRootRise " + catcher.maxRootRise.ToString("F2"));
                    Check(KH_FrisbeeCatch.HookMinPace > 0f && KH_FrisbeeCatch.HookMinPace < catcher.chaseRunSpeed,
                        "approach pace floor " + KH_FrisbeeCatch.HookMinPace.ToString("F1") + " m/s in (0, chaseRunSpeed) (a jog, never a stop)");
                    // DES-DOG-005 B (replaces Codex D1's circle check, 0-9): the circle no longer decides anything at run
                    // time; the landing is the ASYMMETRIC arc, checked here as pure geometry with the same statics the
                    // planner and the liftoff gate call (DescentTime / AirTime / LandingOf / SlopeOk).
                    {
                        string asym = AsymmetricLandingProblems();
                        Check(asym.Length == 0, "asymmetric landing: level ground == LandingOf(2 tUp), 0.4 m lower lands further (tDown > tUp), 0.15 m higher shorter, a landing the apex does not clear by "
                            + KH_FrisbeeCatch.JumpApexClearance.ToString("F2") + " m is refused, AirTime == tUp + tDown, 15 deg slope split" + (asym.Length == 0 ? "" : " -- " + asym));
                    }
                    Check(catcher.chaseDeceleration > 0f && catcher.chaseDeceleration < catcher.deceleration, "chase braking " + catcher.chaseDeceleration.ToString("F1") + " m/s2 < the generic " + catcher.deceleration.ToString("F1") + " (the chase rolls out, it does not stop dead)");
                    Check(catcher.standTurnDegPerSec <= catcher.walkTurnDegPerSec && catcher.walkTurnDegPerSec <= catcher.turnSpeedDegPerSec && catcher.standTurnDegPerSec > catcher.idleTurnSpeedDegPerSec,
                        "turn rates stand " + catcher.standTurnDegPerSec.ToString("F0") + " <= walk " + catcher.walkTurnDegPerSec.ToString("F0") + " <= run " + catcher.turnSpeedDegPerSec.ToString("F0") + ", all above idle " + catcher.idleTurnSpeedDegPerSec.ToString("F0"));
                    // PCVR 2026-09-26 "大型犬は一歩がでかい感じの速さにしたい": the cap is the breed's own; [1.5, 3.0] stays as a sanity bound.
                    Check(Mathf.Abs(catcher.maxRunAnimScale - MaxRunAnimScaleFor(KH_FrisbeeJumpClipEditor.Breed)) < 1e-4f && catcher.maxRunAnimScale >= 1.5f && catcher.maxRunAnimScale <= 3f,
                        "run clip speed cap " + catcher.maxRunAnimScale.ToString("F1") + "x == breed table " + MaxRunAnimScaleFor(KH_FrisbeeJumpClipEditor.Breed).ToString("F1") + " (" + KH_FrisbeeJumpClipEditor.Breed + "), within [1.5, 3.0] (higher reads as jitter)");
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
            finally { if (opened) ClosePrototype(previousScene, open); WarnDirtied(cleanOthers, "Frisbee/5"); }
            // 2026-09-28 計画プローブ (Frisbee/10) を同じ Verify で: 期待の形にならないケースは WARN PROBE_MISMATCH (失敗数には数えない。
            // 最初の計画だけを見るので)。試作シーンだけが開いているときに限る (他のシーンのコライダーがレイに当たらないように)。
            if (SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().path == ScenePath)
            {
                foreach (string probeLine in KH_FrisbeePlanProbeEditor.Preflight("Frisbee/5 Verify", () => KH_FrisbeePlanProbeEditor.RunPrototype(true, true, null)))
                    log.AppendLine((probeLine.StartsWith("PLAN_PROBE_OK") ? "INFO " : "WARN ") + probeLine);
            }
            else log.AppendLine("INFO PLAN_PROBE skipped: open " + ScenePath + " alone and run Frisbee/10 Plan Probe");
            log.AppendLine("RESULT failures=" + fail);
            Report("verify", log.ToString());
            if (fail > 0) Debug.LogError("[KH Frisbee] verify failures=" + fail);
        }
    }
}
