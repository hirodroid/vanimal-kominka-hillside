using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // IMP-DOG-025 builder: the tool shelf KH_WashStation (scene root) with 2 sponges, 2 showers and 1 towel.
    // IMP-ENV-007 (main wash station, docs/plans/2026-09-30-wash-station-layout.md): a low wooden stand (top "Shelf" +
    // 4 legs), the duckboard "WashTable" 1.2 m in front of it (station local -Z, toward the spawn), a tub, a small board
    // sign, and a home Transform per tool under "Homes" (KH_WashTool.home), and the reset button "ResetButton" (KH_WashStationReset: sends every tool nobody holds
    // home; the automatic return is off by the user's decision 2026-09-30, returnSeconds 0).
    // Everything is a child of KH_WashStation with code-owned local transforms, so it moves with the station. The
    // station's own position and rotation are human-owned once it exists (never written here after creation); the
    // duckboard and the tub are seated on the ground under them on every Build (run 11 again after moving the station).
    public static partial class KH_DogWashSetupEditor
    {
        class ToolSpec
        {
            public string name; public int tool; public Vector3 slot; public Vector3 euler;
            public ToolSpec(string name, int tool, Vector3 slot, Vector3 euler) { this.name = name; this.tool = tool; this.slot = slot; this.euler = euler; }
        }

        const float ShelfHeight = 0.8f, ShelfWidth = 1.2f, ShelfDepth = 0.45f;
        // ---- IMP-ENV-007 layout (station local; +X east, -Z = the front, toward the spawn) ----
        const float ShelfTopThickness = 0.04f, ShelfLegSize = 0.05f;
        static readonly Vector2 ShelfLegInset = new Vector2(0.05f, 0.04f);             // leg centre from the top's corner (x, z)
        const float TableFront = 1.2f;                                                  // duckboard centre: 1.2 m in front of the shelf centre
        static readonly Vector3 TableSize = new Vector3(0.6f, 0.03f, 0.9f);             // 0.9 m along Z (the dog stands along it), top 0.03 m above the ground
        const int TableSlats = 5;
        const float SideMargin = 1.0f;                                                  // free standing room on each side (±X) of the duckboard
        static readonly Vector3 TubLocal = new Vector3(-0.95f, 0f, -0.05f);            // west of the shelf (the east side faces the leash rack)
        const float TubRadius = 0.2f, TubHeight = 0.26f;
        static readonly Vector3 SignSize = new Vector3(0.34f, 0.12f, 0.02f);
        const float SignZ = 0.19f;                                                      // at the back of the shelf top, facing the front (-Z)
        const string SignText = "WASH";                                                 // ASCII placeholder; the Japanese is typed in the Inspector (layout doc §5)
        const string HomesName = "Homes", TableName = "WashTable", TubName = "Tub", SignName = "Sign", SignTextName = "SignText";
        // 2026-09-30 (user): no automatic return; the reset button sends the tools home. 0 = never returns by itself.
        const float ReturnSeconds = 0f, HomeTolerance = 0.05f;
        // ---- the reset button: a small board hanging under the top's front edge (east), below the towels ----
        const string ResetName = "ResetButton", ResetTextName = "ResetText", ResetProgram = "KH_WashStationReset";
        static readonly Vector3 ResetLocal = new Vector3(0.40f, 0.72f, -(ShelfDepth * 0.5f) - 0.012f);
        static readonly Vector3 ResetSize = new Vector3(0.22f, 0.10f, 0.02f);
        const string ResetInteractText = "Reset tools", ResetLabel = "RESET";   // ASCII placeholders; the Japanese is typed in the Inspector
        /// <summary>Not the tool's own name (a scene-wide search by the tool's name stays unique).</summary>
        static string HomeName(ToolSpec s) { return "Home_" + s.name; }
        const string WoodMatPath = "Assets/KominkaHillside/Data/Environment/River/KH_RiverWood.mat";
        // Sponge yellow and shower-head silver are the shared tool materials (Dog.cs); the towel gets its own unbleached one here.
        static readonly Color TowelKinari = new Color(0.93f, 0.89f, 0.80f);
        const string TowelKinariMat = "KH_WashTool_TowelKinari";
        static readonly Color SignInk = new Color(0xF2 / 255f, 0xE8 / 255f, 0xD0 / 255f, 1f);
        static readonly ToolSpec[] ToolSpecs =
        {
            // IMP-ENV-007: 2 sets (design §2). Towels turned 90° (0.20 m along X, 0.30 m along Z) so both fit the 1.2 m top.
            new ToolSpec("KH_WashSponge_0", KH_DogWash.ToolSponge, new Vector3(-0.52f, ShelfHeight + 0.031f, 0f), Vector3.zero),
            new ToolSpec("KH_WashSponge_1", KH_DogWash.ToolSponge, new Vector3(-0.36f, ShelfHeight + 0.031f, 0f), Vector3.zero),
            new ToolSpec("KH_WashShower_0", KH_DogWash.ToolShower, new Vector3(-0.18f, ShelfHeight + 0.081f, 0f), Vector3.zero),
            new ToolSpec("KH_WashShower_1", KH_DogWash.ToolShower, new Vector3(-0.02f, ShelfHeight + 0.081f, 0f), Vector3.zero),
            new ToolSpec("KH_WashTowel_0", KH_DogWash.ToolTowel, new Vector3(0.22f, ShelfHeight + 0.011f, 0f), new Vector3(0f, 90f, 0f)),
            new ToolSpec("KH_WashTowel_1", KH_DogWash.ToolTowel, new Vector3(0.46f, ShelfHeight + 0.011f, 0f), new Vector3(0f, 90f, 0f)),
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

            var wood = AssetDatabase.LoadAssetAtPath<Material>(WoodMatPath);
            if (wood == null) { wood = shared.shelf; r.log.Add("WARN " + WoodMatPath + " missing: the stand uses " + (shared.shelf != null ? shared.shelf.name : "null")); }
            BuildStand(station.transform, wood, shared.shelf, r);

            // ---- homes (one per tool; the tools rest there and return there) ----
            var homes = Child(station.transform, HomesName);
            homes.gameObject.layer = 0;
            homes.localPosition = Vector3.zero; homes.localRotation = Quaternion.identity; homes.localScale = Vector3.one;
            foreach (Transform h in homes.Cast<Transform>().ToArray())
                if (!ToolSpecs.Any(s => HomeName(s) == h.name)) { r.log.Add("removed stale home " + h.name); UnityEngine.Object.DestroyImmediate(h.gameObject); }
            var towelMat = ToolMaterial(TowelKinariMat, TowelKinari);

            var tools = new List<KH_WashTool>();
            foreach (var spec in ToolSpecs)
            {
                var home = Child(homes, HomeName(spec));
                home.gameObject.layer = 0;
                home.localPosition = spec.slot; home.localRotation = Quaternion.Euler(spec.euler); home.localScale = Vector3.one;
                foreach (var comp in home.GetComponents<Component>()) if (!(comp is Transform)) UnityEngine.Object.DestroyImmediate(comp);   // a bare marker
                tools.Add(BuildTool(station.transform, spec, shared, audio, home, towelMat));
            }
            // stale tools of an earlier layout
            foreach (Transform t in station.transform.Cast<Transform>().ToArray())
                if ((t.name.StartsWith("KH_WashSponge_") || t.name.StartsWith("KH_WashShower_") || t.name.StartsWith("KH_WashTowel_")) && !ToolSpecs.Any(s => s.name == t.name))
                { r.log.Add("removed stale tool " + t.name); UnityEngine.Object.DestroyImmediate(t.gameObject); }
            BuildReset(station.transform, tools, shared.shelf, r);
            return tools;
        }

        // ================================================================ IMP-ENV-007: the reset button (10b makes its program)

        [MenuItem(Menu + "10b - Create Station Programs")]
        public static void CreateStationPrograms()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Wash Setup] Edit mode only (not compiling)");
            string asset = ScriptsDir + "/" + ResetProgram + ".asset";
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(asset) != null) { Debug.Log("[KH Wash Setup] " + ResetProgram + " program asset exists; nothing changed"); return; }
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptsDir + "/" + ResetProgram + ".cs");
            if (script == null) throw new InvalidOperationException(ResetProgram + ".cs not imported yet; refresh first");
            var created = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            created.sourceCsScript = script;
            AssetDatabase.CreateAsset(created, asset);
            AssetDatabase.SaveAssets();
            UdonSharpProgramAsset.CompileAllCsPrograms(true);
            Debug.Log("[KH Wash Setup] created " + asset + ". Compile U# (scripts/unity-compile.sh --udon), then run 11.");
        }

        static void BuildReset(Transform station, List<KH_WashTool> tools, Material dark, BuildReport r)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ScriptsDir + "/" + ResetProgram + ".asset") == null)
                throw new InvalidOperationException(ResetProgram + ".asset missing: run Wash/10b, compile U#, then 11 again (reopen the scene without saving to drop this run)");
            var board = Prim(station, ResetName, PrimitiveType.Cube, true);   // the collider is what Interact aims at
            board.layer = 0;
            board.transform.localPosition = ResetLocal;
            board.transform.localRotation = Quaternion.identity;
            board.transform.localScale = ResetSize;
            UnitBox(board);
            SetLook(board, dark);
            var reset = AddUdon<KH_WashStationReset>(board, Networking.SyncType.None);
            reset.tools = tools.ToArray();
            EditorUtility.SetDirty(reset);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(reset);
            // The words are the user's once written: only the UdonBehaviour default ("Use") or an empty text gets the placeholder.
            if (string.IsNullOrEmpty(backing.interactText) || backing.interactText == "Use") { backing.interactText = ResetInteractText; EditorUtility.SetDirty(backing); }
            if (backing.proximity < 1.5f) { backing.proximity = 1.5f; EditorUtility.SetDirty(backing); }

            // the label on the board's front (-Z), like the sign
            var textT = Child(station, ResetTextName);
            textT.gameObject.layer = 0;
            var tmp = textT.GetComponent<TextMeshPro>();
            bool fresh = tmp == null;
            if (fresh) tmp = textT.gameObject.AddComponent<TextMeshPro>();
            textT = tmp.transform;
            textT.localPosition = new Vector3(ResetLocal.x, ResetLocal.y, ResetLocal.z - ResetSize.z * 0.5f - 0.002f);
            textT.localRotation = Quaternion.identity; textT.localScale = Vector3.one;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath);
            if (font == null) throw new InvalidOperationException("Shared UI font missing: " + KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath);
            tmp.font = font;
            if (fresh || string.IsNullOrEmpty(tmp.text)) tmp.text = ResetLabel;
            tmp.alignment = TextAlignmentOptions.Center; tmp.enableWordWrapping = false;
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.15f; tmp.fontSizeMax = 0.7f;
            tmp.color = SignInk;
            tmp.rectTransform.sizeDelta = new Vector2(ResetSize.x - 0.03f, ResetSize.y - 0.02f);
            var mr = tmp.GetComponent<MeshRenderer>();
            if (mr != null) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; EditorUtility.SetDirty(mr); }
            EditorUtility.SetDirty(tmp);

            Copy(reset);   // the main Build copies the tools; this behaviour is the station's own (its tools are wired above)
            r.log.Add(ResetName + " wired to " + tools.Count + " tools");
        }

        static KH_WashTool BuildTool(Transform station, ToolSpec spec, Shared shared, Audio audio, Transform home, Material towelMat)
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
                    SetLook(look, towelMat != null ? towelMat : shared.towel);
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
            tool.home = home;                       // IMP-ENV-007: the return target (moves with the station)
            tool.returnSeconds = ReturnSeconds;
            tool.homeTolerance = HomeTolerance;
            tool.debugHeldOverride = 0;             // harness only
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

        // ================================================================ IMP-ENV-007: the stand, the duckboard, the tub, the sign

        static void BuildStand(Transform station, Material wood, Material dark, BuildReport r)
        {
            // ---- the stand: the top is "Shelf" (the tools rest on it; Wash/12 checks its solid collider) + 4 legs ----
            var top = Prim(station, "Shelf", PrimitiveType.Cube, true);
            top.layer = 0;
            top.transform.localPosition = new Vector3(0f, ShelfHeight - ShelfTopThickness * 0.5f, 0f);
            top.transform.localRotation = Quaternion.identity;
            top.transform.localScale = new Vector3(ShelfWidth, ShelfTopThickness, ShelfDepth);
            UnitBox(top);
            SetLook(top, wood);
            float legH = ShelfHeight - ShelfTopThickness;
            for (int i = 0; i < 4; i++)
            {
                var leg = Prim(station, "ShelfLeg_" + i, PrimitiveType.Cube, true);
                leg.layer = 0;
                float sx = (i & 1) == 0 ? -1f : 1f, sz = (i & 2) == 0 ? -1f : 1f;
                leg.transform.localPosition = new Vector3(sx * (ShelfWidth * 0.5f - ShelfLegInset.x), legH * 0.5f, sz * (ShelfDepth * 0.5f - ShelfLegInset.y));
                leg.transform.localRotation = Quaternion.identity;
                leg.transform.localScale = new Vector3(ShelfLegSize, legH, ShelfLegSize);
                UnitBox(leg);
                SetLook(leg, wood);
            }

            // ---- the duckboard (すのこ): one solid, walkable BoxCollider on Default (players, and the dogs' ground rays 0|11) + slats ----
            var table = Child(station, TableName);
            table.gameObject.layer = 0;
            float tableY = MaxGround(station, new Vector3(0f, 0f, -TableFront), TableSize.x, TableSize.z);
            table.localPosition = new Vector3(0f, tableY, -TableFront);
            table.localRotation = Quaternion.identity; table.localScale = Vector3.one;
            foreach (var c in table.GetComponents<Collider>()) if (!(c is BoxCollider)) UnityEngine.Object.DestroyImmediate(c);
            var box = table.GetComponent<BoxCollider>(); if (box == null) box = table.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = false; box.enabled = true; box.size = TableSize; box.center = new Vector3(0f, TableSize.y * 0.5f, 0f);
            EditorUtility.SetDirty(box);
            float slatDepth = 0.15f, pitch = (TableSize.z - slatDepth) / (TableSlats - 1);
            for (int i = 0; i < TableSlats; i++)
            {
                var slat = Prim(table, "Slat_" + i, PrimitiveType.Cube, false);
                slat.transform.localPosition = new Vector3(0f, TableSize.y * 0.5f, -TableSize.z * 0.5f + slatDepth * 0.5f + pitch * i);
                slat.transform.localRotation = Quaternion.identity;
                slat.transform.localScale = new Vector3(TableSize.x, TableSize.y, slatDepth);
                SetLook(slat, wood);
            }
            foreach (Transform t in table.Cast<Transform>().ToArray())
                if (t.name.StartsWith("Slat_") && (!int.TryParse(t.name.Substring(5), out int k) || k >= TableSlats)) UnityEngine.Object.DestroyImmediate(t.gameObject);
            r.log.Add(TableName + " local y " + tableY.ToString("F3") + " (highest ground under the board)");

            // ---- the tub (桶) ----
            var tub = Prim(station, TubName, PrimitiveType.Cylinder, true);
            tub.layer = 0;
            float tubY = MaxGround(station, TubLocal, TubRadius * 2f, TubRadius * 2f);
            tub.transform.localPosition = new Vector3(TubLocal.x, tubY + TubHeight * 0.5f, TubLocal.z);
            tub.transform.localRotation = Quaternion.identity;
            tub.transform.localScale = new Vector3(TubRadius * 2f, TubHeight * 0.5f, TubRadius * 2f);   // the Cylinder primitive is 2 units tall
            SetLook(tub, dark);

            // ---- the sign: a small board at the back of the top, text facing the front (-Z; TMP faces -Z) ----
            var sign = Prim(station, SignName, PrimitiveType.Cube, false);   // no collider: never blocks a pickup ray
            sign.layer = 0;
            float signY = ShelfHeight + SignSize.y * 0.5f;
            sign.transform.localPosition = new Vector3(0f, signY, SignZ);
            sign.transform.localRotation = Quaternion.identity;
            sign.transform.localScale = SignSize;
            SetLook(sign, dark);
            var textT = Child(station, SignTextName);
            textT.gameObject.layer = 0;
            var tmp = textT.GetComponent<TextMeshPro>();
            bool fresh = tmp == null;
            if (fresh) tmp = textT.gameObject.AddComponent<TextMeshPro>();
            textT = tmp.transform;
            textT.localPosition = new Vector3(0f, signY, SignZ - SignSize.z * 0.5f - 0.002f);
            textT.localRotation = Quaternion.identity; textT.localScale = Vector3.one;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath);
            if (font == null) throw new InvalidOperationException("Shared UI font missing: " + KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath);
            tmp.font = font;
            // The words are the user's once written (the Japanese is typed in the Inspector): only an empty label gets the placeholder.
            if (fresh || string.IsNullOrEmpty(tmp.text)) tmp.text = SignText;
            tmp.alignment = TextAlignmentOptions.Center; tmp.enableWordWrapping = false;
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.2f; tmp.fontSizeMax = 0.9f;
            tmp.color = SignInk;
            tmp.rectTransform.sizeDelta = new Vector2(SignSize.x - 0.03f, SignSize.y - 0.02f);
            var tr = tmp.GetComponent<MeshRenderer>();
            if (tr != null) { tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false; EditorUtility.SetDirty(tr); }
            EditorUtility.SetDirty(tmp);
        }

        /// <summary>A primitive whose BoxCollider is the unit box (the transform's scale sizes it).</summary>
        static void UnitBox(GameObject go)
        {
            var b = go.GetComponent<BoxCollider>();
            if (b == null) return;
            b.isTrigger = false; b.center = Vector3.zero; b.size = Vector3.one; EditorUtility.SetDirty(b);
        }

        /// <summary>The highest ground (station local y) under a rectangle (centre + corners) around a station-local point.</summary>
        static float MaxGround(Transform station, Vector3 local, float sizeX, float sizeZ)
        {
            float best = float.NegativeInfinity;
            foreach (var p in RectSamples(local, sizeX, sizeZ))
                best = Mathf.Max(best, StationGround(station, station.TransformPoint(p)) - station.position.y);
            return best;
        }

        static Vector3[] RectSamples(Vector3 c, float sx, float sz)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            return new[] { c, c + new Vector3(-hx, 0f, -hz), c + new Vector3(hx, 0f, -hz), c + new Vector3(-hx, 0f, hz), c + new Vector3(hx, 0f, hz) };
        }

        /// <summary>The ground under a world point = the terrain only (the course tiles are Terrains on layer 11). A rock,
        /// a stand or a board is never taken for the ground (Codex 2026-09-30 7-1: the side-room check lifted its box onto
        /// whatever solid top it found). The TerrainCollider hit is used when there is one (what the players stand on),
        /// otherwise the covering Terrain's SampleHeight. No Terrain under the point: an exception (nothing is guessed).</summary>
        static float StationGround(Transform station, Vector3 world)
        {
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(new Vector3(world.x, world.y + 3f, world.z), Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.PositiveInfinity, y = 0f;
            foreach (var h in hits)
            {
                if (!(h.collider is TerrainCollider)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; y = h.point.y; }
            }
            if (bestDist < float.PositiveInfinity) return y;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                var p = t.transform.position; var s = t.terrainData.size;
                if (world.x >= p.x && world.x <= p.x + s.x && world.z >= p.z && world.z <= p.z + s.z) return t.SampleHeight(world) + p.y;
            }
            throw new InvalidOperationException("No Terrain under " + world.ToString("F2") + " (the wash station is seated on the terrain only)");
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
