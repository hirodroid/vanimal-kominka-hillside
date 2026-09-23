using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    // Shared recipes keep 60 identical particle components out of the scene YAML.
    internal static class KD_BundledPetFxPrefabs
    {
        internal const string Folder = "Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/PetFx";
        static string Settings(Component component, bool ignoreInactiveSphereSettings = false)
        {
            var root = JObject.Parse(EditorJsonUtility.ToJson(component));
            var data = root[component.GetType().Name] as JObject ?? root;
            foreach (string key in new[] { "m_GameObject", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset" }) data.Remove(key);
            if (ignoreInactiveSphereSettings)
            {
                var initial = data["InitialModule"] as JObject; var shape = data["ShapeModule"] as JObject;
                if (initial == null || shape == null) throw new InvalidOperationException("Particle serialization format changed");
                initial.Remove("startSizeY"); initial.Remove("startSizeZ"); shape.Remove("angle"); shape.Remove("m_Rotation");
            }
            return data.ToString(Newtonsoft.Json.Formatting.None);
        }
        internal static void Apply()
        {
            var pets = KD_DogDefs.All.Select(d => KD_SceneObjects.Find(d.PetSystemName).GetComponent<KD_DogPetController>()).ToArray();
            // Validate every source before creating assets or changing scene references.
            foreach (var pet in pets)
                if (pet == null || pet.fxVariants == null || pet.fxVariants2 == null || pet.fxVariants.Length != 11 || pet.fxVariants2.Length != 11)
                    throw new InvalidOperationException("All 11 effects must exist before sharing bundled recipes");
            for (int i = 6; i < 11; i++) for (int layer = 0; layer < 2; layer++)
            {
                string expectedParticle = null, expectedRenderer = null;
                foreach (var pet in pets)
                {
                    var ps = layer == 0 ? pet.fxVariants[i] : pet.fxVariants2[i];
                    if (ps == null || ps.GetComponent<ParticleSystemRenderer>() == null || ps.transform.childCount != 0
                        || ps.GetComponents<Component>().Any(c => !(c is Transform) && !(c is ParticleSystem) && !(c is ParticleSystemRenderer)))
                        throw new InvalidOperationException("Expected standalone particle root: " + pet.name);
                    if (ps.main.startSize3D || ps.shape.shapeType != ParticleSystemShapeType.Sphere)
                        throw new InvalidOperationException("Shared recipe expects uniform-size sphere particles: " + pet.name);
                    string path = Folder + "/" + ps.name + ".prefab";
                    if (PrefabUtility.IsPartOfPrefabInstance(ps) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ps.gameObject) != path)
                        throw new InvalidOperationException("Unexpected effect prefab: " + ps.name);
                    string particle = Settings(ps, true), renderer = Settings(ps.GetComponent<ParticleSystemRenderer>());
                    if (expectedParticle != null && (expectedParticle != particle || expectedRenderer != renderer))
                        throw new InvalidOperationException("Breed-specific active particle settings cannot be shared: " + pet.name + "/" + ps.name);
                    expectedParticle = particle; expectedRenderer = renderer;
                }
            }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs", "PetFx");
            int count = 0;
            for (int i = 6; i < 11; i++) for (int layer = 0; layer < 2; layer++)
            {
                var source = layer == 0 ? pets[0].fxVariants[i] : pets[0].fxVariants2[i];
                string path = Folder + "/" + source.name + ".prefab";
                // Snapshot preserves semantics across conversion; prefab bookkeeping is not a recipe.
                var particles = pets.Select(p => layer == 0 ? p.fxVariants[i] : p.fxVariants2[i]).ToArray();
                foreach (var ps in particles)
                {
                    // Source hearts carried breed-specific values for unused 3D size axes and cone settings.
                    // These bundled recipes use uniform size and a sphere; canonicalize inactive settings.
                    var main = ps.main; var shape = ps.shape;
                    if (main.startSize3D || shape.shapeType != ParticleSystemShapeType.Sphere)
                        throw new InvalidOperationException("Shared recipe expects uniform-size sphere particles");
                    main.startSizeY = 1f; main.startSizeZ = 1f; shape.angle = 25f; shape.rotation = Vector3.zero;
                }
                var before = particles.Select(p => Settings(p)).ToArray();
                var rendererBefore = particles.Select(p => Settings(p.GetComponent<ParticleSystemRenderer>())).ToArray();
                var positions = particles.Select(p => p.transform.localPosition).ToArray();
                var rotations = particles.Select(p => p.transform.localRotation).ToArray();
                var scales = particles.Select(p => p.transform.localScale).ToArray();
                var clone = Object.Instantiate(source.gameObject);
                clone.name = source.name;
                if (PrefabUtility.IsPartOfPrefabInstance(clone)) PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.transform.localScale = Vector3.one;
                try { PrefabUtility.SaveAsPrefabAsset(clone, path); }
                finally { Object.DestroyImmediate(clone); }
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                for (int d = 0; d < particles.Length; d++)
                {
                    var ps = particles[d]; var go = ps.gameObject;
                    if (!PrefabUtility.IsPartOfPrefabInstance(go))
                        PrefabUtility.ConvertToPrefabInstance(go, asset, new ConvertToPrefabInstanceSettings(), InteractionMode.AutomatedAction);
                    else if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) != path)
                        throw new InvalidOperationException("Unexpected effect prefab: " + go.name);
                    // Identical recipe lives in the prefab; root transform is the only instance placement.
                    PrefabUtility.RevertObjectOverride(go.GetComponent<ParticleSystem>(), InteractionMode.AutomatedAction);
                    PrefabUtility.RevertObjectOverride(go.GetComponent<ParticleSystemRenderer>(), InteractionMode.AutomatedAction);
                    go.transform.localPosition = positions[d]; go.transform.localRotation = rotations[d]; go.transform.localScale = scales[d];
                    PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
                    if (ps != go.GetComponent<ParticleSystem>() || Settings(ps) != before[d]
                        || Settings(go.GetComponent<ParticleSystemRenderer>()) != rendererBefore[d])
                        throw new InvalidOperationException("Particle recipe/reference changed during prefab conversion: " + pets[d].name + "/" + go.name);
                    count++;
                }
            }
            foreach (var pet in pets)
            {
                KD_ProductsEditor.Copy(pet);
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(pet);
                foreach (string field in new[] { "fxVariants", "fxVariants2" })
                {
                    backing.publicVariables.TryGetVariableValue(field, out object raw);
                    var refs = raw as Object[];
                    var expected = field == "fxVariants" ? pet.fxVariants : pet.fxVariants2;
                    if (refs == null || refs.Length != 11 || Enumerable.Range(6, 5).Any(i => refs[i] != expected[i]))
                        throw new InvalidOperationException("Backing particle references differ after conversion: " + pet.name + "/" + field);
                }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/bundled-petfx-prefabs.txt", "PASS: 10 shared prefabs, " + count + " scene particle references; serialized particle/renderer settings and transforms retained; all 60 backing references match.\n");
        }
    }
}
