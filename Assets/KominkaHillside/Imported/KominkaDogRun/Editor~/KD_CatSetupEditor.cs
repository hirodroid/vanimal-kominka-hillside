using System;
using System.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.Udon;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-CAT-001: one stationary cat. Setup preserves existing placement and tuning.</summary>
    public static class KD_CatSetupEditor
    {
        internal const string UnitName = "KD_CatUnit";
        internal const string SystemName = "KD_CatSystem";
        internal const string ProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CatController.asset";
        internal const string ModelGuid = "f063b69bf6e0847488f18c1cf6564642";
        internal const string ModelPath = "hojo_born/Bone/Bone_end/PB3/neko";
        const string PurrGuid = "e7f29db63567a164f83c449eaddc7a26";
        const string MeowGuid = "7102ef132e051604dad8d605404099f8";
        const string HeartMaterialPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_Heart.mat";
        const string HeadName = "ボーン.004";

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Cat/1 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            var v = new KD_VerifyLog("[KD_CatSetup] Create Program Asset");
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath);
            if (asset == null)
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CatController.cs");
                if (!v.Check(script != null, "cat MonoScript exists")) { v.Finish("Logs/imp-cat-001-program.txt"); return; }
                asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = script;
                AssetDatabase.CreateAsset(asset, ProgramPath);
                AssetDatabase.SaveAssets();
            }
            v.Check(asset.sourceCsScript != null && asset.sourceCsScript.GetClass() == typeof(KD_CatController), "correct source script");
            v.Note("Refresh assets and wait for UdonSharp compilation before Cat/3. Creation does not run Setup.");
            v.Finish("Logs/imp-cat-001-program.txt", "PROGRAM RESULT");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Cat/3 - Setup Cat Unit")]
        public static void Setup()
        {
            var v = new KD_VerifyLog("[KD_CatSetup] Setup");
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    throw new InvalidOperationException("Setup requires idle Edit mode.");
                var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath);
                if (program == null || program.behaviourSyncMode != BehaviourSyncMode.Manual)
                    throw new InvalidOperationException("Run Cat/1 and finish UdonSharp compilation before Setup.");
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(KD_CatAnimatorBuilderEditor.ControllerPath);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(ModelGuid));
                var heartMaterial = AssetDatabase.LoadAssetAtPath<Material>(HeartMaterialPath);
                var purr = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(PurrGuid));
                if (controller == null || prefab == null || heartMaterial == null || purr == null)
                    throw new InvalidOperationException("Missing controller / v3 prefab / existing heart material / purr clip.");
                // Resolve all globally unique names before creating anything.
                var unit = Unique(UnitName);
                var cat = Unique(KD_CatMaterialEditor.CatRootName);
                var system = Unique(SystemName);
                if (unit == null && (cat != null || system != null))
                    throw new InvalidOperationException("Orphan Cat/System exists without CatUnit. Resolve before Setup.");
                if (unit != null && ((cat != null && cat.transform.parent != unit.transform) || (system != null && system.transform.parent != unit.transform)))
                    throw new InvalidOperationException("Cat/System are outside their CatUnit. Setup will not reparent existing objects.");
                if (unit == null)
                {
                    unit = new GameObject(UnitName);
                    Undo.RegisterCreatedObjectUndo(unit, "Create Cat Unit");
                    unit.transform.position = new Vector3(.8728f, .626f, -2.3233f);
                    // The original placement marker is scene-root only; model 'neko' is not a marker.
                    var marker = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "neko" && g.GetComponent<BoxCollider>() != null);
                    if (marker != null) unit.transform.position = marker.transform.position;
                    KD_SceneObjects.PlaceIfNew(unit, KD_DogDefs.PlacesPath);
                }
                if (cat == null) cat = Child(unit.transform, KD_CatMaterialEditor.CatRootName).gameObject;
                Transform chain = cat.transform;
                foreach (string segment in new[] { "hojo_born", "Bone", "Bone_end", "PB3" }) chain = Child(chain, segment);
                var model = chain.Find("neko");
                if (model == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, chain);
                    Undo.RegisterCreatedObjectUndo(instance, "Create cat model");
                    instance.name = "neko";
                    model = instance.transform;
                    model.localPosition = Vector3.zero;
                    model.localRotation = Quaternion.identity;
                    model.localScale = Vector3.one;
                }
                if (PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject) != prefab)
                    throw new InvalidOperationException("Existing neko is not the approved neko_v3 Variant instance.");
                var head = FindDescendant(model, HeadName);
                var body = model.Find("立方体");
                var face = body == null ? null : body.GetComponent<SkinnedMeshRenderer>();
                if (head == null || face == null || face.sharedMesh == null || face.sharedMesh.GetBlendShapeIndex("metozi") < 0)
                    throw new InvalidOperationException("Measured head / body / metozi missing.");
                var animator = cat.GetComponent<Animator>();
                if (animator == null) animator = Undo.AddComponent<Animator>(cat);
                Undo.RecordObject(animator, "Wire cat Animator");
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;
                // Anchor offsets are measured in this model's head-local space; existing tuning survives.
                var headAnchor = Anchor(head, "KD_CatAnchor_Head", new Vector3(0f, .035f, -.065f));
                var cheekL = Anchor(head, "KD_CatAnchor_CheekL", new Vector3(-.04f, .055f, .015f));
                var cheekR = Anchor(head, "KD_CatAnchor_CheekR", new Vector3(.04f, .055f, .015f));
                if (system == null) system = Child(unit.transform, SystemName).gameObject;
                if (PrefabUtility.IsPartOfPrefabInstance(system)) throw new InvalidOperationException("Cat Udon must be outside prefab.");
                var proxy = system.GetUdonSharpComponent<KD_CatController>();
                if (proxy == null)
                {
                    proxy = system.AddUdonSharpComponent<KD_CatController>();
                    // Seated rest faces cat-local +Z; head-local +Y points at the nose.
                    proxy.neutralHeadDir = Vector3.forward;
                }
                Undo.RecordObject(proxy, "Wire cat controller");
                // Migrate only the old shipped side-sleep default; preserve other Inspector tuning.
                var oldFacing = new Vector3(-.756f, 0f, .655f).normalized;
                if (Vector3.Distance(proxy.neutralHeadDir, oldFacing) < .001f)
                    proxy.neutralHeadDir = Vector3.forward;
                proxy.catAnimator = animator;
                proxy.headBone = head;
                proxy.headAnchor = headAnchor;
                proxy.cheekAnchorL = cheekL;
                proxy.cheekAnchorR = cheekR;
                proxy.faceRenderer = face;
                var audioHost = Child(system.transform, "KD_CatPurr");
                var audio = audioHost.GetComponent<AudioSource>();
                if (audio == null)
                {
                    audio = Undo.AddComponent<AudioSource>(audioHost.gameObject);
                    audio.volume = .30f;
                    audio.spatialBlend = 1f;
                    audio.minDistance = .9f;
                    audio.maxDistance = 3f;
                    audio.rolloffMode = AudioRolloffMode.Logarithmic;
                    audio.dopplerLevel = 0f;
                    audioHost.position = headAnchor.position;
                }
                audio.clip = purr;
                audio.loop = true;
                audio.playOnAwake = false;
                var spatial = audio.GetComponent<VRCSpatialAudioSource>();
                if (spatial == null)
                {
                    spatial = Undo.AddComponent<VRCSpatialAudioSource>(audio.gameObject);
                    spatial.Gain = 0f;
                    spatial.Near = .9f;
                    spatial.Far = 3f;
                    spatial.VolumetricRadius = 0f;
                    spatial.EnableSpatialization = true;
                    spatial.UseAudioSourceVolumeCurve = false;
                }
                v.Note("Purr spatial Gain=" + spatial.Gain + " / Near=" + spatial.Near + " / Far=" + spatial.Far + " (existing ear tuning retained)");
                proxy.purrSource = audio;
                proxy.purrClip = purr;
                foreach (string guid in new[] { PurrGuid, MeowGuid })
                {
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as AudioImporter;
                    if (importer == null) throw new InvalidOperationException("Missing cat AudioImporter: " + guid);
                    if (!importer.forceToMono) { importer.forceToMono = true; importer.SaveAndReimport(); }
                }
                var fxHost = Child(system.transform, "KD_CatHearts");
                var fx = fxHost.GetComponent<ParticleSystem>();
                if (fx == null)
                {
                    fx = Undo.AddComponent<ParticleSystem>(fxHost.gameObject);
                    KD_PetHeartsSetupEditor.ConfigureHearts(fx, v.Text);
                    var main = fx.main;
                    main.maxParticles = 6;
                    main.startSize = .035f;
                    var emission = fx.emission;
                    emission.rateOverTime = 0f;
                    var shape = fx.shape;
                    shape.radius = .045f;
                    fxHost.position = headAnchor.position + Vector3.up * proxy.heartOffsetY;
                }
                fx.GetComponent<ParticleSystemRenderer>().sharedMaterial = heartMaterial;
                proxy.heartFx = fx;
                var collider = system.GetComponent<BoxCollider>();
                if (collider == null) collider = Undo.AddComponent<BoxCollider>(system);
                Undo.RecordObject(collider, "Limit cat Interact to head");
                collider.enabled = true;
                collider.isTrigger = true;
                collider.center = system.transform.InverseTransformPoint(head.TransformPoint(new Vector3(0f, .035f, 0f)));
                collider.size = Vector3.one * .22f;
                proxy.interactCollider = collider;
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
                if (backing == null) throw new InvalidOperationException("Cat backing UdonBehaviour not generated.");
                backing.SyncMethod = VRC.SDKBase.Networking.SyncType.Manual;
                backing.interactText = "なでる";
                backing.proximity = proxy.interactProximity;
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
                foreach (var obj in new UnityEngine.Object[] { proxy, backing, animator, audio, spatial, collider, fx }) EditorUtility.SetDirty(obj);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                v.Check(true, "cat references copied to backing; scene not saved");
                v.Note("Next Cat/4 materials, Distance Fade/2, then Cat/5 and Cat/6. Placement and existing tuning preserved.");
            }
            catch (Exception e) { v.Check(false, e.ToString()); }
            v.Finish("Logs/imp-cat-001-setup.txt", "SETUP RESULT");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Cat/5 - Verify Cat Unit")]
        public static void Verify()
        {
            var v = new KD_VerifyLog("[KD_CatSetup] Verify");
            try
            {
                var unit = Unique(UnitName);
                var cat = Unique(KD_CatMaterialEditor.CatRootName);
                var system = Unique(SystemName);
                if (!v.Check(unit != null && cat != null && system != null, "one CatUnit / Cat / CatSystem")) { v.Finish("Logs/imp-cat-001-verify.txt"); return; }
                v.Check(KD_SceneObjects.PathOf(unit) == KD_DogDefs.PlacesPath + "/" + UnitName, "CatUnit under own Places");
                v.Check(cat.transform.parent == unit.transform && system.transform.parent == unit.transform, "model and system are sibling children of CatUnit");
                var model = cat.transform.Find(ModelPath);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(ModelGuid));
                v.Check(model != null && prefab != null && PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject) == prefab, "binding hierarchy and retained v3 Variant instance");
                v.Check(!PrefabUtility.IsPartOfPrefabInstance(system), "Udon outside purchased prefab");
                int missing = unit.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                v.Check(missing == 0, "missing scripts=" + missing);
                v.Check(unit.activeInHierarchy && cat.activeInHierarchy && system.activeInHierarchy, "unit active");
                var proxy = system.GetUdonSharpComponent<KD_CatController>();
                if (!v.Check(proxy != null && proxy.enabled, "cat proxy enabled")) { v.Finish("Logs/imp-cat-001-verify.txt"); return; }
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
                var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath);
                v.Check(backing != null && backing.enabled && system.GetComponents<UdonBehaviour>().Length == 1 && backing.programSource == program
                    && backing.SyncMethod == VRC.SDKBase.Networking.SyncType.Manual, "single enabled backing / correct program / Manual");
                v.Check(program != null && program.behaviourSyncMode == BehaviourSyncMode.Manual, "compiled Program Asset Manual");
                v.Check(backing != null && backing.interactText == "なでる" && Mathf.Abs(backing.proximity - proxy.interactProximity) < .001f, "Interact label and proximity wired");
                var animator = cat.GetComponent<Animator>();
                v.Check(animator != null && animator == proxy.catAnimator && animator.enabled && !animator.applyRootMotion
                    && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate
                    && animator.runtimeAnimatorController == AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(KD_CatAnimatorBuilderEditor.ControllerPath), "Animator enabled / independent controller / RootMotion off / AlwaysAnimate");
                v.Check(proxy.headBone != null && model != null && proxy.headBone == FindDescendant(model, HeadName), "measured head bone .004");
                foreach (var anchor in new[] { proxy.headAnchor, proxy.cheekAnchorL, proxy.cheekAnchorR })
                    v.Check(anchor != null && anchor.parent == proxy.headBone && Vector3.Distance(anchor.position, proxy.headBone.position) < .2f, "head-local anchor within 20 cm: " + (anchor == null ? "null" : anchor.name));
                v.Check(proxy.headAnchor != proxy.cheekAnchorL && proxy.headAnchor != proxy.cheekAnchorR && proxy.cheekAnchorL != proxy.cheekAnchorR, "three distinct anchors");
                v.Check(proxy.faceRenderer != null && proxy.faceRenderer.transform.IsChildOf(cat.transform)
                    && proxy.faceRenderer.sharedMesh != null && proxy.faceRenderer.sharedMesh.GetBlendShapeIndex("metozi") >= 0, "cat face / metozi wired");
                v.Check(proxy.touchRadius > 0 && proxy.releaseRadius > proxy.touchRadius && proxy.minStrokeSpeed > 0 && proxy.strokeGrace > 0, "contact hysteresis and motion gate");
                v.Check(proxy.interactDuration > proxy.fuyAfter && proxy.fuyAfter > 0 && proxy.fuyChance > 0 && proxy.fuyChance < 1 && proxy.fuyCooldown > 0
                    && proxy.noticeRadius > 0 && proxy.noticeCooldown > 0, "Interact reaches fuy / probabilistic sleepy turn / notice enabled");
                // Hand petting drops out on every stroke pause, so without this grace only Interact ever reaches fuy.
                v.Check(proxy.fuyContinuityGrace > proxy.strokeGrace && proxy.fuyContinuityGrace < proxy.fuyAfter,
                    "hand pet reaches fuy: continuity grace=" + proxy.fuyContinuityGrace + " > strokeGrace=" + proxy.strokeGrace);
                {
                    object graceStored = null;
                    bool graceGot = backing != null && backing.publicVariables.TryGetVariableValue("fuyContinuityGrace", out graceStored);
                    v.Check(graceGot && graceStored is float && Mathf.Abs((float)graceStored - proxy.fuyContinuityGrace) < .001f,
                        "backing value: fuyContinuityGrace=" + graceStored);
                }
                v.Check(proxy.petEyeClose > 1 && proxy.petEyeClose <= 100 && proxy.neutralHeadDir.sqrMagnitude > .9f, "eye closure uses 0..100 and facing direction valid");
                v.Check(Vector3.Distance(proxy.neutralHeadDir, new Vector3(-.756f, 0f, .655f).normalized) > .001f,
                    "old side-sleep facing migrated; actual=" + proxy.neutralHeadDir);
                var box = system.GetComponent<BoxCollider>();
                v.Check(box != null && box == proxy.interactCollider && box.enabled && box.isTrigger
                    && Vector3.Distance(box.size, Vector3.one * .22f) < .001f, "head-only Interact collider: 22 cm cube / wired / enabled / trigger");
                v.Check(box != null && proxy.headBone != null && Vector3.Distance(box.transform.TransformPoint(box.center),
                    proxy.headBone.TransformPoint(new Vector3(0f, .035f, 0f))) < .001f, "Interact center follows head (saved pose)");
                var audio = proxy.purrSource;
                v.Check(audio != null && audio.transform.IsChildOf(system.transform) && audio.clip == proxy.purrClip
                    && proxy.purrClip == AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(PurrGuid)), "dedicated purr source / supplied clip");
                v.Check(audio != null && audio.enabled && audio.loop && !audio.playOnAwake && audio.spatialBlend == 1f && audio.volume > 0f
                    && audio.minDistance > 0 && audio.maxDistance > audio.minDistance && audio.maxDistance <= 3f && audio.dopplerLevel == 0, "quiet local 3D loop / no autoplay / no Doppler");
                var spatial = audio == null ? null : audio.GetComponent<VRCSpatialAudioSource>();
                v.Check(spatial != null && spatial.enabled && spatial.EnableSpatialization && !spatial.UseAudioSourceVolumeCurve && spatial.Near > 0f
                    && spatial.Far > spatial.Near && spatial.Far <= 3f && spatial.VolumetricRadius == 0f,
                    "VRC spatial audio uses Near/Far, local range <=3m");
                if (spatial != null) v.Note("Purr spatial Gain=" + spatial.Gain + " / Near=" + spatial.Near + " / Far=" + spatial.Far);
                // The ear-tuned values themselves: the cat's head sits at y~0.89 m, so a standing
                // petter's ear is 0.7-0.9 m away and Near 0.5 / volume 0.18 was already in the falloff.
                v.Check(audio != null && Mathf.Abs(audio.volume - .30f) < .001f && Mathf.Abs(audio.minDistance - .9f) < .001f
                    && Mathf.Abs(audio.maxDistance - 3f) < .001f,
                    "purr heard while petting: volume=" + (audio == null ? "-" : audio.volume.ToString())
                    + " min=" + (audio == null ? "-" : audio.minDistance.ToString())
                    + " max=" + (audio == null ? "-" : audio.maxDistance.ToString()) + " (want .30 / .9 / 3)");
                v.Check(spatial != null && Mathf.Abs(spatial.Near - .9f) < .001f && Mathf.Abs(spatial.Far - 3f) < .001f,
                    "purr spatial range: Near=" + (spatial == null ? "-" : spatial.Near.ToString())
                    + " Far=" + (spatial == null ? "-" : spatial.Far.ToString()) + " (want .9 / 3)");
                v.Check(proxy.purrChance > 0 && proxy.purrChance < 1 && proxy.purrCooldown > 0,
                    "purr is occasional: chance=" + proxy.purrChance + " cooldown=" + proxy.purrCooldown);
                {
                    object chanceStored = null;
                    object cooldownStored = null;
                    bool chanceGot = backing != null && backing.publicVariables.TryGetVariableValue("purrChance", out chanceStored);
                    bool cooldownGot = backing != null && backing.publicVariables.TryGetVariableValue("purrCooldown", out cooldownStored);
                    v.Check(chanceGot && chanceStored is float && Mathf.Abs((float)chanceStored - proxy.purrChance) < .001f
                        && cooldownGot && cooldownStored is float && Mathf.Abs((float)cooldownStored - proxy.purrCooldown) < .001f,
                        "backing value: purrChance=" + chanceStored + " purrCooldown=" + cooldownStored);
                }
                foreach (string guid in new[] { PurrGuid, MeowGuid })
                {
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as AudioImporter;
                    v.Check(importer != null && importer.forceToMono, "cat SE mono: " + guid);
                }
                var fx = proxy.heartFx;
                v.Check(fx != null && fx.transform.IsChildOf(system.transform) && !fx.main.playOnAwake && fx.main.simulationSpace == ParticleSystemSimulationSpace.World
                    && fx.GetComponent<ParticleSystemRenderer>().sharedMaterial != null && proxy.heartRate > 0 && proxy.heartRate <= 3f, "dedicated restrained hearts, world simulation, no autoplay");
                foreach (string field in new[] { "catAnimator", "headBone", "headAnchor", "cheekAnchorL", "cheekAnchorR", "faceRenderer", "purrSource", "purrClip", "heartFx", "interactCollider" })
                {
                    object stored = null;
                    var expected = typeof(KD_CatController).GetField(field).GetValue(proxy) as UnityEngine.Object;
                    bool got = backing != null && backing.publicVariables.TryGetVariableValue(field, out stored);
                    v.Check(got && expected != null && stored as UnityEngine.Object == expected, "backing reference: " + field);
                }
                KD_CatAnimatorBuilderEditor.Verify(v, cat);
                v.Note("Material/fade validation is Cat/6 after Distance Fade/2. Play motion, audio and platform behavior need user checks.");
            }
            catch (Exception e) { v.Check(false, e.ToString()); }
            v.Finish("Logs/imp-cat-001-verify.txt");
        }

        static GameObject Unique(string name)
        {
            int count;
            var obj = KD_SceneObjects.Find(name, out count);
            if (count > 1) throw new InvalidOperationException(name + " duplicate count=" + count);
            return obj;
        }

        static Transform Child(Transform parent, string name)
        {
            var matches = parent.Cast<Transform>().Where(t => t.name == name).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("Duplicate child: " + name);
            if (matches.Length == 1) return matches[0];
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Transform Anchor(Transform head, string name, Vector3 offset)
        {
            var existing = head.Find(name);
            var anchor = Child(head, name);
            if (existing == null) anchor.localPosition = offset;
            return anchor;
        }

        static Transform FindDescendant(Transform root, string name)
        {
            var matches = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }
}
