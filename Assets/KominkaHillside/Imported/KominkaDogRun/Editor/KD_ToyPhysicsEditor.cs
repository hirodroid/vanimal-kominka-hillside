using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-FET-012: shared toy physics. Explicit tuning migration; ordinary Fetch Setup
    /// preserves existing values. No Udon/ownership/ground asset changes.</summary>
    public static class KD_ToyPhysicsEditor
    {
        public const float LinearDrag = 0.05f;
        public const float AngularDrag = 2f;
        public const float ThrowBoost = 1.4f;
        public const float ThrowBoostMinSpeed = 1f;
        public const float Bounciness = 0.6f;
        public const string MaterialPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_ToyPhysics.physicMaterial";
        const string Output = "Temp/fet-012/";

        // Only new balls receive these defaults automatically. Existing balls use Apply explicitly.
        public static void ConfigureNewBall(GameObject ball)
        {
            var rb = ball.GetComponent<Rigidbody>();
            var pickup = ball.GetComponent<VRCPickup>();
            rb.drag = LinearDrag;
            rb.angularDrag = AngularDrag;
            pickup.ThrowVelocityBoostScale = ThrowBoost;
            pickup.ThrowVelocityBoostMinSpeed = ThrowBoostMinSpeed;
            ball.GetComponent<SphereCollider>().sharedMaterial = LoadOrCreateMaterial();
        }

        static PhysicMaterial CreateMaterial()
        {
            var material = new PhysicMaterial("KD_ToyPhysics");
            SetMaterialValues(material);
            return material;
        }

        static void SetMaterialValues(PhysicMaterial material)
        {
            material.dynamicFriction = 0.6f;
            material.staticFriction = 0.6f;
            material.frictionCombine = PhysicMaterialCombine.Average;
            material.bounciness = Bounciness;
            material.bounceCombine = PhysicMaterialCombine.Maximum;
        }

        static PhysicMaterial LoadOrCreateMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(MaterialPath);
            if (material == null)
            {
                material = CreateMaterial();
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            return material;
        }

        static void RequireScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SceneManager.GetActiveScene().path != "Assets/Scenes/KominkaDogRun.unity")
                throw new InvalidOperationException("Open KominkaDogRun in Edit Mode first.");
        }

        static GameObject SceneBall(KD_DogDef def)
        {
            var ball = KD_SceneObjects.Find(def.BallName, out int count);
            if (count != 1 || ball == null || ball.GetComponent<Rigidbody>() == null ||
                ball.GetComponent<SphereCollider>() == null || ball.GetComponent<VRCPickup>() == null ||
                ball.GetComponent<VRCObjectSync>() == null || !KD_SceneObjects.HasStaticAncestry(ball, out _))
                throw new InvalidOperationException("Invalid ball: " + def.BallName);
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ball) != def.BallPrefabPath)
                throw new InvalidOperationException("Unexpected prefab: " + def.BallName);
            return ball;
        }

        static string Values(GameObject ball)
        {
            var rb = ball.GetComponent<Rigidbody>();
            var p = ball.GetComponent<VRCPickup>();
            var c = ball.GetComponent<SphereCollider>();
            return $"mass={rb.mass} drag={rb.drag} angularDrag={rb.angularDrag} gravity={rb.useGravity} " +
                $"kinematic={rb.isKinematic} boost={p.ThrowVelocityBoostScale} min={p.ThrowVelocityBoostMinSpeed} " +
                $"autoHold={p.AutoHold} radius={c.radius * ball.transform.lossyScale.x} " +
                $"material={(c.sharedMaterial == null ? "default" : c.sharedMaterial.name)}" +
                (c.sharedMaterial == null ? "" : $" bounce={c.sharedMaterial.bounciness} combine={c.sharedMaterial.bounceCombine}");
        }

        static Collider GroundBelow(GameObject ball)
        {
            Collider nearest = null;
            float distance = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(ball.transform.position + Vector3.up * 0.25f,
                Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.attachedRigidbody != null || hit.distance >= distance) continue;
                nearest = hit.collider;
                distance = hit.distance;
            }
            return nearest;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Physics/1 - Survey")]
        public static void Survey()
        {
            RequireScene();
            var log = new StringBuilder();
            log.AppendLine($"gravity={Physics.gravity} fixedDeltaTime={Time.fixedDeltaTime} sceneDirty={SceneManager.GetActiveScene().isDirty}");
            foreach (var def in KD_DogDefs.All)
            {
                var ball = SceneBall(def);
                log.AppendLine(def.BallName + " scene " + Values(ball));
                log.AppendLine(def.BallName + " prefab " + Values(AssetDatabase.LoadAssetAtPath<GameObject>(def.BallPrefabPath)));
                var ground = GroundBelow(ball);
                var m = ground == null ? null : ground.sharedMaterial;
                log.AppendLine($"  ground={(ground == null ? "NOT_FOUND" : ground.name)} material={(m == null ? "default" : m.name)} " +
                    (m == null ? "" : $"friction={m.dynamicFriction}/{m.staticFriction} bounce={m.bounciness} combine={m.frictionCombine}/{m.bounceCombine}"));
            }
            Write("survey.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Physics/2 - Compare Isolated Physics")]
        public static void Compare()
        {
            RequireScene();
            var log = new StringBuilder("Isolated Unity PhysicsScene, Edit Mode. No SDK, Udon, real terrain or hand tracking.\n");
            log.AppendLine($"Baseline = current scene values. Candidate = drag {LinearDrag} / angularDrag {AngularDrag} / bounce {Bounciness} Maximum. Boost is NOT simulated.");
            var candidateMaterial = CreateMaterial();
            try
            {
                foreach (var def in KD_DogDefs.All)
                {
                    var ball = SceneBall(def);
                    var source = ball.GetComponent<Rigidbody>();
                    var ground = GroundBelow(ball);
                    for (int mode = 0; mode < 2; mode++)
                    {
                        float drag = mode == 0 ? source.drag : LinearDrag;
                        float angular = mode == 0 ? source.angularDrag : AngularDrag;
                        var mat = mode == 0 ? ball.GetComponent<SphereCollider>().sharedMaterial : candidateMaterial;
                        for (int scenario = 0; scenario < 5; scenario++)
                            log.AppendLine(def.key + (mode == 0 ? " baseline " : " candidate ") +
                                Simulate(ball, ground, drag, angular, mat, scenario));
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(candidateMaterial); }
            Write("comparison.txt", log);
        }

        static string Simulate(GameObject original, Collider ground, float drag, float angular, PhysicMaterial material, int scenario)
        {
            // Separate physics world: never step the user's world, add Udon objects or enter Play.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var physics = scene.GetPhysicsScene();
                if (!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene))
                    throw new InvalidOperationException("Preview physics is not isolated; comparison aborted.");
                var floor = new GameObject("ComparisonGround");
                SceneManager.MoveGameObjectToScene(floor, scene);
                var box = floor.AddComponent<BoxCollider>();
                box.size = new Vector3(100f, 1f, 100f);
                floor.transform.position = Vector3.down * 0.5f;
                box.sharedMaterial = ground == null ? null : ground.sharedMaterial;
                if (scenario == 3) floor.transform.rotation = Quaternion.Euler(0f, 0f, -5f);
                var go = new GameObject("ComparisonBall");
                SceneManager.MoveGameObjectToScene(go, scene);
                var sphere = go.AddComponent<SphereCollider>();
                var srcSphere = original.GetComponent<SphereCollider>();
                sphere.radius = srcSphere.radius * original.transform.lossyScale.x;
                sphere.sharedMaterial = material;
                var rb = go.AddComponent<Rigidbody>();
                var srcRb = original.GetComponent<Rigidbody>();
                rb.mass = srcRb.mass;
                rb.drag = drag;
                rb.angularDrag = angular;
                rb.useGravity = srcRb.useGravity;
                rb.maxAngularVelocity = srcRb.maxAngularVelocity;
                rb.sleepThreshold = srcRb.sleepThreshold;
                rb.collisionDetectionMode = srcRb.collisionDetectionMode;
                // residual roll / 0.5m drop / weak throw / slope released from rest
                go.transform.position = Vector3.up * (sphere.radius + (scenario == 0 ? 0.001f : scenario == 2 || scenario == 4 ? 1f : 0.5f));
                Vector3 initial = go.transform.position;
                rb.velocity = scenario == 0 ? Vector3.right * 0.2f : scenario == 2 ? new Vector3(3.5f, 1.2f, 0f) : Vector3.zero;
                if (scenario == 0) rb.angularVelocity = Vector3.back * (0.2f / sphere.radius);
                float sleepAt = -1f;
                float firstContactX = float.NaN;
                int bounces = 0;
                float firstReboundHeight = 0f;
                for (int step = 0; step < 600; step++)
                {
                    float previousVerticalSpeed = rb.velocity.y;
                    physics.Simulate(0.02f);
                    if (previousVerticalSpeed < -0.1f && rb.velocity.y > 0.1f) bounces++;
                    if (bounces == 1) firstReboundHeight = Mathf.Max(firstReboundHeight, rb.position.y - sphere.radius);
                    if (float.IsNaN(firstContactX) && rb.position.y <= sphere.radius + 0.015f)
                        firstContactX = rb.position.x;
                    if (rb.IsSleeping() && sleepAt < 0f) sleepAt = (step + 1) * 0.02f;
                }
                return $"case={scenario} sleepAt={sleepAt:F2}s travelX={rb.position.x - initial.x:F3}m " +
                    $"speedAt12s={rb.velocity.magnitude:F4}m/s firstContactX={firstContactX:F3}m " +
                    $"bounces={bounces} firstReboundHeight={firstReboundHeight:F3}m";
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Physics/3 - Apply Tuning")]
        public static void Apply()
        {
            RequireScene();
            // Validate every target before any write. Only the six own ball prefab paths are allowed.
            foreach (var def in KD_DogDefs.All)
            {
                SceneBall(def);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.BallPrefabPath);
                if (!def.BallPrefabPath.StartsWith("Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/KD_Ball_", StringComparison.Ordinal) ||
                    prefab == null || prefab.GetComponent<Rigidbody>() == null || prefab.GetComponent<VRCPickup>() == null ||
                    prefab.GetComponent<SphereCollider>() == null)
                    throw new InvalidOperationException("Invalid prefab: " + def.BallPrefabPath);
            }
            Directory.CreateDirectory(Output);
            string backup = Output + "before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), backup, true))
                throw new IOException("Could not back up unsaved scene.");
            var log = new StringBuilder("Backup (including existing unsaved edits): " + backup + "\n");
            // Explicit Apply owns the shared tuning. Ordinary Setup only loads existing material.
            var material = LoadOrCreateMaterial();
            Undo.RecordObject(material, "Tune toy bounce");
            SetMaterialValues(material);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            foreach (var def in KD_DogDefs.All)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.BallPrefabPath);
                SetValues(prefab);
                PrefabUtility.SavePrefabAsset(prefab, out bool saved);
                if (!saved) throw new IOException("Could not save " + def.BallPrefabPath);
                var ball = SceneBall(def);
                SetValues(ball);
                log.AppendLine(def.BallName + " applied physics tuning to prefab + scene.");
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Write("apply.txt", log);
        }

        static void SetValues(GameObject go)
        {
            var rb = go.GetComponent<Rigidbody>();
            var p = go.GetComponent<VRCPickup>();
            var c = go.GetComponent<SphereCollider>();
            Undo.RecordObjects(new UnityEngine.Object[] { rb, p, c }, "Tune toy physics");
            ConfigureNewBall(go);
            EditorUtility.SetDirty(rb);
            EditorUtility.SetDirty(p);
            EditorUtility.SetDirty(c);
            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(rb);
                PrefabUtility.RecordPrefabInstancePropertyModifications(p);
                PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            }
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Physics/4 - Verify")]
        public static void Verify()
        {
            RequireScene();
            var log = new StringBuilder();
            int failures = 0;
            foreach (var def in KD_DogDefs.All)
            {
                foreach (var go in new[] { SceneBall(def), AssetDatabase.LoadAssetAtPath<GameObject>(def.BallPrefabPath) })
                {
                    var rb = go.GetComponent<Rigidbody>();
                    var p = go.GetComponent<VRCPickup>();
                    var material = go.GetComponent<SphereCollider>().sharedMaterial;
                    bool pass = Mathf.Approximately(rb.drag, LinearDrag) && Mathf.Approximately(rb.angularDrag, AngularDrag) &&
                        Mathf.Approximately(p.ThrowVelocityBoostScale, ThrowBoost) && Mathf.Approximately(p.ThrowVelocityBoostMinSpeed, ThrowBoostMinSpeed) &&
                        rb.useGravity && !rb.isKinematic && Mathf.Approximately(rb.mass, 0.2f) &&
                        rb.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic &&
                        material != null && AssetDatabase.GetAssetPath(material) == MaterialPath &&
                        Mathf.Approximately(material.bounciness, Bounciness) && material.bounceCombine == PhysicMaterialCombine.Maximum &&
                        Mathf.Approximately(material.dynamicFriction, 0.6f) && Mathf.Approximately(material.staticFriction, 0.6f) &&
                        material.frictionCombine == PhysicMaterialCombine.Average;
                    if (!pass) failures++;
                    log.AppendLine((pass ? "PASS " : "FAIL ") + def.key + (go.scene.IsValid() ? " scene " : " prefab ") + Values(go));
                }
            }
            log.AppendLine($"RESULT: {(failures == 0 ? "PASS" : "FAIL")} failures={failures} targets={KD_DogDefs.All.Length * 2}");
            Write("verify.txt", log);
        }

        // ================================================================ IMP-FET-013
        // Pickup proximity は犬種別 (小 0.1 / 中 0.3 / 大 1.0)。正本は KD_DogDef.pickupProximity。
        // 3 - Apply Tuning とは別メニューにしてある — 物理の共通値と、犬種で違う値を
        // 同じボタンに混ぜると「共通値を直したつもりで proximity も書き戻る」が起きる。

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Physics/5 - Apply Pickup Proximity")]
        public static void ApplyProximity()
        {
            RequireScene();
            foreach (var def in KD_DogDefs.All)
            {
                SceneBall(def);
                if (def.pickupProximity <= 0f)
                    throw new InvalidOperationException("pickupProximity undefined: " + def.key);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.BallPrefabPath);
                if (!def.BallPrefabPath.StartsWith("Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/KD_Ball_", StringComparison.Ordinal) ||
                    prefab == null || prefab.GetComponent<VRCPickup>() == null)
                    throw new InvalidOperationException("Invalid prefab: " + def.BallPrefabPath);
            }
            Directory.CreateDirectory(Output);
            string backup = Output + "before-proximity-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), backup, true))
                throw new IOException("Could not back up unsaved scene.");
            var log = new StringBuilder("Backup (including existing unsaved edits): " + backup + "\n");
            foreach (var def in KD_DogDefs.All)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.BallPrefabPath);
                SetProximity(prefab, def.pickupProximity);
                PrefabUtility.SavePrefabAsset(prefab, out bool saved);
                if (!saved) throw new IOException("Could not save " + def.BallPrefabPath);
                SetProximity(SceneBall(def), def.pickupProximity);
                log.AppendLine(def.BallName + " proximity=" + def.pickupProximity + " applied to prefab + scene.");
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Write("apply-proximity.txt", log);
        }

        static void SetProximity(GameObject go, float proximity)
        {
            var p = go.GetComponent<VRCPickup>();
            Undo.RecordObject(p, "Tune toy pickup proximity");
            p.proximity = proximity;
            EditorUtility.SetDirty(p);
            if (PrefabUtility.IsPartOfPrefabInstance(go))
                PrefabUtility.RecordPrefabInstancePropertyModifications(p);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Physics/6 - Verify Pickup Proximity")]
        public static void VerifyProximity()
        {
            RequireScene();
            var log = new StringBuilder();
            int failures = 0;
            foreach (var def in KD_DogDefs.All)
            {
                foreach (var go in new[] { SceneBall(def), AssetDatabase.LoadAssetAtPath<GameObject>(def.BallPrefabPath) })
                {
                    float actual = go.GetComponent<VRCPickup>().proximity;
                    bool pass = def.pickupProximity > 0f && Mathf.Abs(actual - def.pickupProximity) < 0.001f;
                    if (!pass) failures++;
                    log.AppendLine((pass ? "PASS " : "FAIL ") + def.key + (go.scene.IsValid() ? " scene " : " prefab ")
                        + "proximity=" + actual + " expected=" + def.pickupProximity);
                }
            }
            log.AppendLine($"RESULT: {(failures == 0 ? "PASS" : "FAIL")} failures={failures} targets={KD_DogDefs.All.Length * 2}");
            Write("verify-proximity.txt", log);
        }

        static void Write(string name, StringBuilder log)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + name, log.ToString());
            Debug.Log("[KD_ToyPhysics] " + Output + name + "\n" + log);
        }
    }
}
