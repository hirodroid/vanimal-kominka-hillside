using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // IMP-DOG-025 builder: the tool shelf KH_WashStation (scene root) with 2 sponges, 2 showers and 1 towel. Placeholder
    // primitives; the look is replaced later (IMP-ENV-007). The shelf position is human-owned once it exists.
    public static partial class KH_DogWashSetupEditor
    {
        class ToolSpec
        {
            public string name; public int tool; public Vector3 slot; public Vector3 euler;
            public ToolSpec(string name, int tool, Vector3 slot, Vector3 euler) { this.name = name; this.tool = tool; this.slot = slot; this.euler = euler; }
        }

        const float ShelfHeight = 0.8f, ShelfWidth = 1.2f, ShelfDepth = 0.45f;
        static readonly ToolSpec[] ToolSpecs =
        {
            new ToolSpec("KH_WashSponge_0", KH_DogWash.ToolSponge, new Vector3(-0.46f, ShelfHeight + 0.031f, 0f), Vector3.zero),
            new ToolSpec("KH_WashSponge_1", KH_DogWash.ToolSponge, new Vector3(-0.30f, ShelfHeight + 0.031f, 0f), Vector3.zero),
            new ToolSpec("KH_WashShower_0", KH_DogWash.ToolShower, new Vector3(-0.08f, ShelfHeight + 0.081f, 0f), Vector3.zero),
            new ToolSpec("KH_WashShower_1", KH_DogWash.ToolShower, new Vector3(0.10f, ShelfHeight + 0.081f, 0f), Vector3.zero),
            new ToolSpec("KH_WashTowel_0", KH_DogWash.ToolTowel, new Vector3(0.38f, ShelfHeight + 0.011f, 0f), Vector3.zero),
        };
        static readonly string[] ToolLabels = { "Sponge", "Shower", "Towel" };

        static List<KH_WashTool> BuildStation(SceneCtx c, Shared shared, Audio audio, BuildReport r)
        {
            var scene = c.dogsRoot.gameObject.scene;
            var roots = scene.GetRootGameObjects().Where(g => g.name == StationName).ToArray();
            if (roots.Length > 1) throw new InvalidOperationException(roots.Length + " " + StationName + " at the scene root (make it one, then run again)");
            if (KominkaDogRun.Editor.KD_SceneObjects.Find(StationName, out int anywhere) != null && anywhere != roots.Length)
                throw new InvalidOperationException(StationName + " exists below another object (move it to the scene root, then run again)");
            GameObject station;
            if (roots.Length == 1) { station = roots[0]; r.stationKept = true; }
            else
            {
                station = new GameObject(StationName);
                var p = c.rack.position + new Vector3(StationOffsetX, 0f, 0f);
                p.y = Ground(p.x, p.z, c.rack.position.y);
                station.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, c.rack.eulerAngles.y, 0f));
                r.log.Add("created " + StationName + " at " + p.ToString("F2") + " (" + RackName + " x " + StationOffsetX + " m, ground)");
            }
            station.transform.localScale = Vector3.one;
            r.stationPosition = station.transform.position;

            // ---- placeholder shelf (collider kept: players and tools rest on it) ----
            var shelf = Prim(station.transform, "Shelf", PrimitiveType.Cube, true);
            shelf.transform.localPosition = new Vector3(0f, ShelfHeight * 0.5f, 0f);
            shelf.transform.localRotation = Quaternion.identity;
            shelf.transform.localScale = new Vector3(ShelfWidth, ShelfHeight, ShelfDepth);
            shelf.layer = 0;
            SetLook(shelf, shared.shelf);

            var tools = new List<KH_WashTool>();
            foreach (var spec in ToolSpecs) tools.Add(BuildTool(station.transform, spec, shared, audio));
            // stale tools of an earlier layout
            foreach (Transform t in station.transform.Cast<Transform>().ToArray())
                if ((t.name.StartsWith("KH_WashSponge_") || t.name.StartsWith("KH_WashShower_") || t.name.StartsWith("KH_WashTowel_")) && !ToolSpecs.Any(s => s.name == t.name))
                { r.log.Add("removed stale tool " + t.name); UnityEngine.Object.DestroyImmediate(t.gameObject); }
            return tools;
        }

        static KH_WashTool BuildTool(Transform station, ToolSpec spec, Shared shared, Audio audio)
        {
            var root = Child(station, spec.name);
            root.gameObject.layer = PickupLayer;
            root.localPosition = spec.slot; root.localRotation = Quaternion.Euler(spec.euler); root.localScale = Vector3.one;   // code-owned resting slot

            // ---- the one collider on the tool root (Pickup) ----
            foreach (var col in root.GetComponents<Collider>())
            {
                bool want = spec.tool == KH_DogWash.ToolShower ? col is CapsuleCollider : col is BoxCollider;
                if (!want) UnityEngine.Object.DestroyImmediate(col);
            }
            var nozzle = Child(root, "Nozzle");
            nozzle.gameObject.layer = PickupLayer;
            Transform sprayT = null;
            if (spec.tool == KH_DogWash.ToolShower)
            {
                var cap = root.GetComponent<CapsuleCollider>(); if (cap == null) cap = root.gameObject.AddComponent<CapsuleCollider>();
                cap.isTrigger = false; cap.direction = 1; cap.radius = 0.028f; cap.height = 0.20f; cap.center = new Vector3(0f, 0.01f, 0f);
                var handle = Prim(root, "Handle", PrimitiveType.Cylinder, false);
                handle.transform.localPosition = Vector3.zero; handle.transform.localRotation = Quaternion.identity; handle.transform.localScale = new Vector3(0.036f, 0.08f, 0.036f);   // 0.16 m tall
                SetLook(handle, shared.showerBody);
                var head = Prim(root, "Head", PrimitiveType.Cylinder, false);
                head.transform.localPosition = new Vector3(0f, 0.09f, 0.015f); head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); head.transform.localScale = new Vector3(0.07f, 0.015f, 0.07f);   // face along +Z
                SetLook(head, shared.showerHead);
                nozzle.localPosition = new Vector3(0f, 0.09f, 0.035f); nozzle.localRotation = Quaternion.identity; nozzle.localScale = Vector3.one;   // spray along +Z
                sprayT = Child(nozzle, "Spray");
                sprayT.gameObject.layer = PickupLayer;
                sprayT.localPosition = Vector3.zero; sprayT.localRotation = Quaternion.identity; sprayT.localScale = Vector3.one;
            }
            else
            {
                var box = root.GetComponent<BoxCollider>(); if (box == null) box = root.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = false; box.center = Vector3.zero;
                var look = Prim(root, "Visual", PrimitiveType.Cube, false);
                look.transform.localPosition = Vector3.zero; look.transform.localRotation = Quaternion.identity;
                if (spec.tool == KH_DogWash.ToolSponge)
                {
                    box.size = new Vector3(0.12f, 0.06f, 0.08f);
                    look.transform.localScale = box.size;
                    SetLook(look, shared.sponge);
                    nozzle.localPosition = new Vector3(0f, -0.02f, 0f);
                }
                else
                {
                    box.size = new Vector3(0.30f, 0.02f, 0.20f);
                    look.transform.localScale = box.size;
                    SetLook(look, shared.towel);
                    nozzle.localPosition = Vector3.zero;
                }
                nozzle.localRotation = Quaternion.identity; nozzle.localScale = Vector3.one;
                var oldSpray = nozzle.Find("Spray"); if (oldSpray != null) UnityEngine.Object.DestroyImmediate(oldSpray.gameObject);
            }

            // ---- physics / pickup / sync (the disc and the leash tips are the precedent) ----
            var rb = root.GetComponent<Rigidbody>(); if (rb == null) rb = root.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
            var pickup = root.GetComponent<VRCPickup>(); if (pickup == null) pickup = root.gameObject.AddComponent<VRCPickup>();
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;   // Desktop: the pickup stays in hand and Use reaches OnPickupUseDown
            pickup.orientation = VRC_Pickup.PickupOrientation.Any;
            pickup.proximity = 0.3f; pickup.pickupable = true; pickup.allowManipulationWhenEquipped = false;
            pickup.InteractionText = ToolLabels[spec.tool];
            pickup.UseText = spec.tool == KH_DogWash.ToolShower ? "Spray" : "Wash";
            if (root.GetComponent<VRCObjectSync>() == null) root.gameObject.AddComponent<VRCObjectSync>();
            EditorUtility.SetDirty(rb); EditorUtility.SetDirty(pickup);

            // ---- KH_WashTool (NoVariableSync = None) + State (Manual, a child: VRCObjectSync is Continuous) ----
            var tool = AddUdon<KH_WashTool>(root.gameObject, Networking.SyncType.None);
            var stateT = Child(root, "State");
            stateT.gameObject.layer = PickupLayer;
            stateT.localPosition = Vector3.zero; stateT.localRotation = Quaternion.identity; stateT.localScale = Vector3.one;
            var state = AddUdon<KH_WashToolState>(stateT.gameObject, Networking.SyncType.Manual);
            state.washTool = tool; EditorUtility.SetDirty(state);

            tool.tool = spec.tool;
            tool.nozzle = nozzle;
            tool.state = state;
            tool.washLayer = 1 << WashLayer;
            if (spec.tool == KH_DogWash.ToolShower)
            {
                tool.spray = SprayParticles(sprayT.gameObject, shared.spray);
                tool.sprayAudio = LoopSource(nozzle.gameObject, audio.clips[ClipSpray], 0.7f);
            }
            else
            {
                tool.spray = null;
                var src = nozzle.GetComponent<AudioSource>(); if (src != null) UnityEngine.Object.DestroyImmediate(src);
                tool.sprayAudio = null;
            }
            EditorUtility.SetDirty(tool);
            return tool;
        }

        /// <summary>放水: 円錐、3〜4 m/s、寿命 0.4 s、重力あり、World。Collision (World) で地面・環境に当たって消え、Trigger で部位 Collider に入ると消える
        /// (Collision モジュールは Trigger の Collider に当たらないため)。当たった位置の飛沫は KH_DogWash が出すのでサブエミッターは使わない。</summary>
        static ParticleSystem SprayParticles(GameObject go, Material mat)
        {
            var ps = go.GetComponent<ParticleSystem>(); if (ps == null) ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.4f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
            main.gravityModifier = 1f;
            main.maxParticles = 160;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 140f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 7f; sh.radius = 0.012f;
            sh.rotation = Vector3.zero; sh.position = Vector3.zero;
            var colMod = ps.collision;
            colMod.enabled = true; colMod.type = ParticleSystemCollisionType.World; colMod.mode = ParticleSystemCollisionMode.Collision3D;
            colMod.collidesWith = (1 << 0) | (1 << 11);   // Default + Environment (the terrain and the course)
            colMod.lifetimeLoss = 1f; colMod.bounce = 0f; colMod.dampen = 1f;
            colMod.quality = ParticleSystemCollisionQuality.Medium; colMod.sendCollisionMessages = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(.8f, 0f), new GradientAlphaKey(.4f, 1f) });
            col.color = g;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = mat; rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.02f; rend.lengthScale = 1.5f;
            rend.shadowCastingMode = ShadowCastingMode.Off; rend.receiveShadows = false;
            EditorUtility.SetDirty(ps); EditorUtility.SetDirty(rend);
            return ps;
        }

        /// <summary>Trigger モジュール: 部位 Collider (全部の犬) に入った粒を消す。</summary>
        static void SetSprayTriggers(ParticleSystem ps, Collider[] colliders)
        {
            var tr = ps.trigger;
            while (tr.colliderCount > 0) tr.RemoveCollider(0);
            foreach (var c in colliders) if (c != null) tr.AddCollider(c);
            tr.enabled = colliders.Length > 0;
            tr.enter = ParticleSystemOverlapAction.Kill; tr.inside = ParticleSystemOverlapAction.Kill;
            tr.exit = ParticleSystemOverlapAction.Ignore; tr.outside = ParticleSystemOverlapAction.Ignore;
            tr.radiusScale = 1f;
            EditorUtility.SetDirty(ps);
        }

        static GameObject Prim(Transform parent, string name, PrimitiveType type, bool keepCollider)
        {
            var t = parent.Find(name);
            GameObject go;
            if (t == null) { go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false); }
            else go = t.gameObject;
            if (go.GetComponent<MeshFilter>() == null) go.AddComponent<MeshFilter>();
            if (go.GetComponent<MeshRenderer>() == null) go.AddComponent<MeshRenderer>();
            if (!keepCollider) foreach (var c in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            else if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
            go.layer = parent.gameObject.layer;
            return go;
        }

        static void SetLook(GameObject go, Material mat)
        {
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
            EditorUtility.SetDirty(r);
        }

        /// <summary>地面の高さ: その点を覆う Terrain の SampleHeight、無ければ下向きの Raycast (Default + Environment、Trigger 無視)。</summary>
        static float Ground(float x, float z, float yHint)
        {
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                var p = t.transform.position; var s = t.terrainData.size;
                if (x >= p.x && x <= p.x + s.x && z >= p.z && z <= p.z + s.z) return t.SampleHeight(new Vector3(x, 0f, z)) + p.y;
            }
            if (Physics.Raycast(new Vector3(x, yHint + 20f, z), Vector3.down, out RaycastHit hit, 60f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore)) return hit.point.y;
            throw new InvalidOperationException("No ground at " + x + ", " + z);
        }
    }
}
