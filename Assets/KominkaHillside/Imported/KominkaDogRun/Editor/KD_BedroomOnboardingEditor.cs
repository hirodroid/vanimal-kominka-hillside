using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    public static class KD_BedroomOnboardingEditor
    {
        const string Output = "Temp/bedroom-onboarding/";
        const string Textures = "Assets/KominkaHillside/Imported/KominkaDogRun/Textures/";
        const string Materials = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/";
        const string BedroomName = "KD_BedroomPhoto";
        const string HintName = "KD_HintDirector";
        static void Write(string name, StringBuilder log) { Directory.CreateDirectory(Output); File.WriteAllText(Output + name + ".txt", log.ToString()); Debug.Log(log.ToString()); }
        static T Find<T>(string name) where T : Component { var go = KD_SceneObjects.Find(name); if (go == null) throw new InvalidOperationException("Missing " + name); return go.GetComponent<T>(); }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Bedroom and Onboarding/0 - Survey")]
        public static void Survey()
        {
            Directory.CreateDirectory(Output);
            var scene = EditorSceneManager.GetActiveScene();
            var log = new StringBuilder("dirty=" + scene.isDirty + "\n");
            if (!File.Exists(Output + "before.unity")) EditorSceneManager.SaveScene(scene, Output + "before.unity", true);
            foreach (string name in new[] { "KD_SleepAudio", "KD_Navigation", "KD_PhotoSystem", "KD_ShopKeeper" })
            {
                var go = KD_SceneObjects.Find(name); if (go == null) continue;
                log.AppendLine(name + " pos=" + go.transform.position + " rot=" + go.transform.eulerAngles + " scale=" + go.transform.lossyScale);
                if (name == "KD_Navigation") foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var sr = r as SpriteRenderer;
                    log.AppendLine("renderer " + r.name + " bounds=" + r.bounds + " texture=" + AssetDatabase.GetAssetPath(sr != null && sr.sprite != null ? sr.sprite.texture : r.sharedMaterial.mainTexture));
                }
            }
            var room = Find<KD_SleepAudio>("KD_SleepAudio").roomBounds;
            log.AppendLine("room center=" + room.transform.TransformPoint(room.center) + " local size=" + room.size + " scale=" + room.transform.lossyScale + " rot=" + room.transform.eulerAngles);
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                var origin = room.transform.TransformPoint(room.center) + new Vector3(x, 0, z);
                if (Physics.Raycast(origin, Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore)) log.AppendLine("floor "+x+","+z+" y="+hit.point.y+" "+hit.collider.name);
            }
            var d = Find<KD_PhotoDirector>("KD_PhotoSystem");
            log.AppendLine("camera mask=" + d.photoCamera.cullingMask + " gallery=" + d.gallery.name);
            Write("survey", log);
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Bedroom and Onboarding/1 - Create Programs")]
        public static void Programs()
        {
            foreach (var name in new[] { "KD_BedroomPhotoDirector", "KD_HintDirector" })
            {
                string path = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/" + name;
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") != null) continue;
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path + ".cs");
                AssetDatabase.CreateAsset(asset, path + ".asset");
            }
            AssetDatabase.SaveAssets();
        }

        static GameObject Child(Transform parent, string name, out bool created)
        {
            var t = parent.Find(name); created = t == null;
            if (t != null) return t.gameObject;
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go;
        }
        static T Udon<T>(GameObject go) where T : UdonSharpBehaviour
        {
            var b = go.GetUdonSharpComponent<T>();
            if (b == null) b = go.AddUdonSharpComponent<T>();
            var u = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            u.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(u); return b;
        }
        static void Copy(UdonSharpBehaviour b) { EditorUtility.SetDirty(b); UdonSharpEditorUtility.CopyProxyToUdon(b); }
        /// <summary>Text-to-plate depth (m). 6mm is the value KD_SleepGuide has been shipping with.</summary>
        const float PlateDepth = .006f;
        /// <summary>BUG-CAM-005: text/plate coplanarity check. Verify fails below this depth or above this in-plane drift.</summary>
        const float PlateMinDepth = .003f, PlateMaxDrift = .001f, PlateWallMinGap = .002f;

        static TextMeshPro WorldText(Transform parent, string name, Vector3 position, float width, string text, TMP_FontAsset font)
        {
            var go = Child(parent, name, out bool fresh); if (fresh) go.transform.localPosition = position;
            var tmp = go.GetComponent<TextMeshPro>(); if (tmp == null) tmp = go.AddComponent<TextMeshPro>();
            if (fresh) go.transform.localScale = Vector3.one * .1f;
            go.layer = 5;
            tmp.font = font; tmp.fontSize = name == "Title" ? 6f : 5f; tmp.color = Color.white; tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center; tmp.enableWordWrapping = true;
            tmp.rectTransform.sizeDelta = new Vector2(width * 10f, 3.8f);
            var plate = Child(parent, name + "Plate", out bool newPlate);
            if (newPlate)
            {
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Quad);
                plate.AddComponent<MeshFilter>().sharedMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(primitive); plate.AddComponent<MeshRenderer>();
            }
            const string platePath = Materials + "M_KD_GuidePlate.mat";
            var plateMaterial = AssetDatabase.LoadAssetAtPath<Material>(platePath);
            if (plateMaterial == null) { plateMaterial = new Material(Shader.Find("Unlit/Color")); plateMaterial.color = new Color(.13f,.11f,.09f); AssetDatabase.CreateAsset(plateMaterial, platePath); }
            plate.layer = 5;
            // Placement belongs to the scene author, including separately moved title plates.
            // BUG-CAM-005: the plate sits PlateDepth behind the text along the TEXT's own normal (not the
            // parent's forward) and shares its rotation — a text later rotated toward a wall otherwise keeps
            // the offset in-plane, leaving text and plate coplanar (z-fight = flickering glyphs).
            if (newPlate)
            {
                plate.transform.localRotation = go.transform.localRotation;
                plate.transform.localPosition = go.transform.localPosition + go.transform.localRotation * Vector3.forward * PlateDepth;
                plate.transform.localScale = new Vector3(width + .04f, name == "Title" ? .14f : .27f, 1f);
            }
            plate.GetComponent<Renderer>().sharedMaterial = plateMaterial;
            return tmp;
        }
        static TextMeshProUGUI HintPanel(Transform parent, string name, TMP_FontAsset font, Vector2 size)
        {
            var t = parent.Find(name); GameObject go;
            if (t == null) { go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); }
            else go = t.gameObject;
            go.layer = 5;
            var canvas = go.GetComponent<Canvas>(); if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.localScale = Vector3.one * .001f;
            var bg = go.GetComponent<Image>(); if (bg == null) bg = go.AddComponent<Image>();
            bg.color = KD_UiSpriteEditor.SurfacePanel; bg.raycastTarget = false;
            var labelGo = KD_ShopSetupEditor.EnsureUiChild(go.transform, "Label", Vector2.zero, size - new Vector2(32, 20));
            labelGo.layer = 5;
            var label = labelGo.GetComponent<TextMeshProUGUI>(); if (label == null) label = labelGo.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = 26; label.color = Color.white; label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = true; label.raycastTarget = false;
            go.SetActive(false); return label;
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Bedroom and Onboarding/2 - Build")]
        public static void Build()
        {
            var log = new StringBuilder(); Directory.CreateDirectory(Output);
            var room = Find<KD_SleepAudio>("KD_SleepAudio");
            var source = Find<KD_PhotoDirector>("KD_PhotoSystem");
            var pouch = Find<KD_TreatPouch>("KD_TreatPouch");
            var keeper = Find<KD_ShopKeeper>("KD_ShopKeeper");
            var language = pouch.language;
            var catalog = KD_LocalizationEditor.Load();
            KD_ShopSetupEditor.AddGlyphs(KD_LocalizationEditor.BundledCharacters(catalog), log);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            var places = KD_SceneObjects.Find("KD_Places").transform;
            var systems = KD_SceneObjects.Find("KD_Systems").transform;
            var center = room.roomBounds.transform.TransformPoint(room.roomBounds.center);
            float floorY = 0f;
            if (Physics.Raycast(center + Vector3.left, Vector3.down, out var floorHit, 5f, ~0, QueryTriggerInteraction.Ignore)) floorY = floorHit.point.y;
            var root = Child(places, BedroomName, out bool freshRoot);
            if (freshRoot)
            {
                root.transform.position = new Vector3(center.x - .65f, floorY + 1.25f, center.z - 1.78f);
                root.transform.rotation = Quaternion.Euler(0, 180, 0);
            }
            var bedroom = Udon<KD_BedroomPhotoDirector>(root);
            var gallery = Udon<KD_GalleryController>(root);
            bedroom.capture = source.photoCapture; bedroom.gallery = gallery; bedroom.room = room;
            bedroom.dogRoots = source.dogRoots; bedroom.dogHeads = source.dogHeadAnchors;
            bedroom.wander = source.wanderControllers; bedroom.pet = source.petControllers;
            var floor = Child(root.transform, "Floor", out bool freshFloor);
            if (freshFloor) floor.transform.position = new Vector3(center.x, floorY, center.z);
            bedroom.floor = floor.transform;
            bedroom.cameraPositions = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var anchor = Child(root.transform, "KD_BedroomCam_" + i, out bool fresh);
                if (fresh)
                {
                    anchor.transform.position = new Vector3(center.x + (i == 0 ? -1.65f : 1.55f), floorY + (i == 0 ? .35f : 1.8f), center.z + (i == 0 ? -1.45f : .65f));
                    anchor.transform.rotation = Quaternion.LookRotation(new Vector3(center.x, floorY + (i == 0 ? .35f : .8f), center.z) - anchor.transform.position);
                }
                bedroom.cameraPositions[i] = anchor.transform;
            }
            gallery.slotGroupCount = 1; gallery.slotTextures = new RenderTexture[4]; gallery.boards = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                string rtPath = Textures + "KD_PhotoSlotBed" + i + ".renderTexture";
                var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
                if (rt == null) { rt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32); rt.name = "KD_PhotoSlotBed" + i; AssetDatabase.CreateAsset(rt, rtPath); }
                rt.depth = 0; rt.antiAliasing = 1; rt.useMipMap = false;
                string matPath = Materials + "M_KD_BedroomSlot" + i + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(source.gallery.boards[0].GetComponent<Renderer>().sharedMaterial);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                mat.mainTexture = rt; mat.SetFloat("_DebugProbe", 0f);
                var board = Child(root.transform, "Board" + i, out bool fresh);
                if (fresh)
                {
                    var primitive = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    board.AddComponent<MeshFilter>().sharedMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(primitive);
                    board.AddComponent<MeshRenderer>();
                    board.transform.localPosition = new Vector3((i % 2 - .5f) * .56f, i < 2 ? .18f : -.18f, 0f);
                    board.transform.localScale = new Vector3(.48f, .27f, 1f);
                }
                board.layer = 0; board.GetComponent<Renderer>().sharedMaterial = mat;
                var box = board.GetComponent<BoxCollider>(); if (box == null) box = board.AddComponent<BoxCollider>();
                box.size = new Vector3(1, 1, .01f);
                var b = Udon<KD_PhotoBoard>(board); b.viewer = Find<KD_PhotoViewer>("KD_PhotoViewer"); b.slotIndex = 24 + i;
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b); backing.interactText = KD_PhotoBoard.InteractWord; backing.proximity = 2f;
                Copy(b); EditorUtility.SetDirty(rt); EditorUtility.SetDirty(mat);
                gallery.boards[i] = board; gallery.slotTextures[i] = rt;
                board.SetActive(false);
            }
            WorldText(root.transform, "Title", new Vector3(0, .48f, 0), 1.1f, KD_LocalizationEditor.Text(catalog, "ja", "gallery.bedroom.title"), font);
            source.bedroom = bedroom;
            RewirePhoto(source, Find<KD_PhotoViewer>("KD_PhotoViewer"), source.gallery.slotTextures);
            Copy(bedroom); Copy(gallery); Copy(source);

            var existingPivot = keeper.menuRoot != null ? keeper.menuRoot.transform.parent : null;
            var pivotPosition = existingPivot != null ? existingPivot.localPosition : Vector3.zero;
            var pivotRotation = existingPivot != null ? existingPivot.localRotation : Quaternion.identity;
            var pivotScale = existingPivot != null ? existingPivot.localScale : Vector3.one;
            KD_ShopKeeperSetupEditor.BuildMenu(keeper.gameObject, keeper, UdonSharpEditorUtility.GetBackingUdonBehaviour(keeper), font, log);
            if (existingPivot != null)
            { existingPivot.localPosition = pivotPosition; existingPivot.localRotation = pivotRotation; existingPivot.localScale = pivotScale; }
            Copy(keeper);
            var guide = Child(places, "KD_SleepGuide", out bool freshGuide);
            if (freshGuide)
            {
                guide.transform.position = new Vector3(center.x + .65f, floorY + 1.35f, center.z - 1.78f);
                guide.transform.rotation = Quaternion.Euler(0, 180, 0);
            }
            WorldText(guide.transform, "Caption", new Vector3(0, -.19f, 0), .72f, KD_LocalizationEditor.Text(catalog, "ja", "guide.sleep"), font);
            var photo = Child(guide.transform, "Photo", out bool freshPhoto);
            if (freshPhoto) photo.transform.localPosition = new Vector3(0, .16f, 0);
            var spriteRenderer = photo.GetComponent<SpriteRenderer>(); if (spriteRenderer == null) spriteRenderer = photo.AddComponent<SpriteRenderer>();
            var guideImporter = AssetImporter.GetAtPath(Textures + "KD_SleepGuidePhoto.png") as TextureImporter;
            if (guideImporter != null && guideImporter.textureType != TextureImporterType.Sprite)
            { guideImporter.textureType = TextureImporterType.Sprite; guideImporter.spriteImportMode = SpriteImportMode.Single; guideImporter.mipmapEnabled = false; guideImporter.SaveAndReimport(); }
            var photoSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Textures + "KD_SleepGuidePhoto.png");
            if (photoSprite != null) { spriteRenderer.sprite = photoSprite; photo.transform.localScale = Vector3.one * (.72f / photoSprite.bounds.size.x); }
            photo.SetActive(spriteRenderer.sprite != null);
            log.AppendLine("sleepGuidePhoto=" + (spriteRenderer.sprite != null ? "ready" : "pending user photo"));
            const string posterPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Data/Panfret/explain-onboarding.png";
            var navigation = Find<Transform>("KD_Navigation").GetComponentInChildren<SpriteRenderer>(true);
            var posterImporter = AssetImporter.GetAtPath(posterPath) as TextureImporter;
            var originalImporter = AssetImporter.GetAtPath("Assets/KominkaHillside/Imported/KominkaDogRun/Data/Panfret/explain.png") as TextureImporter;
            if (posterImporter != null && originalImporter != null)
            {
                var settings = new TextureImporterSettings(); originalImporter.ReadTextureSettings(settings);
                posterImporter.SetTextureSettings(settings); posterImporter.SaveAndReimport();
                navigation.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(posterPath);
                EditorUtility.SetDirty(navigation);
            }

            var hintGo = Child(systems, HintName, out bool freshHint);
            var hints = Udon<KD_HintDirector>(hintGo);
            hints.language = language; hints.pouch = pouch; hints.keeper = keeper;
            hints.viewer = Find<KD_PhotoViewer>("KD_PhotoViewer"); hints.live = Find<KD_LiveSystem>("KD_LiveSystem");
            hints.dogs = source.dogRoots; hints.pet = source.petControllers;
            hints.sleepSlots = KD_DogDefs.All.Select(d => Find<KD_SleepSlot>(KD_DogDefs.SleepSlotNameOf(d.key))).ToArray();
            hints.balls = KD_DogDefs.All.Select(d => Find<KD_BallController>(d.BallName)).ToArray();
            hints.pickups = hints.balls.Select(b => b.GetComponent<VRC_Pickup>()).ToArray();
            hints.noticeLabel = HintPanel(hintGo.transform, "Notice", font, new Vector2(780, 130)); hints.noticeRoot = hints.noticeLabel.transform.parent.gameObject;
            hints.hintLabel = HintPanel(hintGo.transform, "Context", font, new Vector2(650, 112)); hints.hintRoot = hints.hintLabel.transform.parent.gameObject;
            hints.noticeLabel.text = KD_LocalizationEditor.Text(catalog, "ja", "hint.menu.vr");
            hints.hintLabel.text = KD_LocalizationEditor.Text(catalog, "ja", "hint.pet");
            pouch.hints = hints;
            KD_LocalizationEditor.Configure(language, catalog, log);
            KD_LocalizationEditor.Bind(language, catalog, log);
            Copy(pouch); Copy(hints); Copy(language);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(root.scene);
            log.AppendLine("bedroom cameras=2 slots=4 viewer=28; NPC topics=7; local hints ready"); Write("build", log);
        }
        internal static void RewirePhoto(KD_PhotoDirector director, KD_PhotoViewer viewer, RenderTexture[] garden)
        {
            var root = KD_SceneObjects.Find(BedroomName); if (root == null) return;
            var bedroom = root.GetUdonSharpComponent<KD_BedroomPhotoDirector>();
            var gallery = root.GetUdonSharpComponent<KD_GalleryController>();
            if (bedroom == null || gallery == null || gallery.slotTextures == null || gallery.slotTextures.Length != 4) return;
            director.bedroom = bedroom;
            viewer.slotTextures = garden.Concat(gallery.slotTextures).ToArray();
            Copy(director); Copy(viewer);
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Bedroom and Onboarding/3 - Verify")]
        public static void Verify()
        {
            var log = new StringBuilder(); int checks = 0, failures = 0;
            Action<bool, string> check = (ok, label) => { checks++; if (!ok) failures++; log.AppendLine((ok ? "PASS " : "FAIL ") + label); };
            var b = Find<KD_BedroomPhotoDirector>(BedroomName);
            var source = Find<KD_PhotoDirector>("KD_PhotoSystem");
            var viewer = Find<KD_PhotoViewer>("KD_PhotoViewer");
            var h = Find<KD_HintDirector>(HintName);
            check(source.bedroom == b && b.capture == source.photoCapture, "shared capture and garden routing");
            check(b.cameraPositions.Length == 2 && b.cameraPositions.All(a => a != null && b.room.GainAt(a.position) <= 0), "two anchors inside bedroom");
            check(b.floor != null && Mathf.Abs(b.floor.position.y - .0275f) < .01f, "tatami height");
            check(b.gallery.slotGroupCount == 1 && b.gallery.slotTextures.Length == 4 && b.gallery.boards.Length == 4, "four bedroom slots / one group");
            check(b.gallery.slotTextures.Distinct().Count() == 4 && b.gallery.slotTextures.All(r => r != null && r.width == 1280 && r.height == 720 && r.depth == 0), "distinct 1280x720 depthless RTs");
            check(source.gallery.slotTextures.Length == 24 && viewer.slotTextures.Length == 28 && viewer.slotTextures.Take(24).SequenceEqual(source.gallery.slotTextures) && viewer.slotTextures.Skip(24).SequenceEqual(b.gallery.slotTextures), "garden 24 preserved / bedroom appended");
            for (int i = 0; i < 4; i++)
            {
                var board = b.gallery.boards[i]; var pb = board.GetUdonSharpComponent<KD_PhotoBoard>();
                check(pb.viewer == viewer && pb.slotIndex == 24+i && board.layer == 0 && board.GetComponent<Collider>() != null, "board " + i + " interact / viewer");
                check(board.GetComponent<Renderer>().sharedMaterial.mainTexture == b.gallery.slotTextures[i], "board " + i + " texture");
            }
            foreach (var u in new UdonSharpBehaviour[] {b, b.gallery, h}) check(UdonSharpEditorUtility.GetBackingUdonBehaviour(u).SyncMethod == Networking.SyncType.None, u.GetType().Name + " local-only backing");
            // BUG-CAM-005: text and its plate must be separated along the text normal, not in-plane.
            foreach (var pair in new[] { BedroomName + "/Title", "KD_SleepGuide/Caption" })
            {
                var parts = pair.Split('/');
                var text = Find<Transform>(parts[0]).Find(parts[1]); var plate = text == null ? null : text.parent.Find(parts[1] + "Plate");
                if (text == null || plate == null) { check(false, pair + " text + plate exist"); continue; }
                Vector3 d = plate.position - text.position; float depth = Vector3.Dot(d, text.forward); float drift = (d - text.forward * depth).magnitude;
                check(depth >= PlateMinDepth && drift <= PlateMaxDrift, pair + " plate behind text (depth=" + depth.ToString("F4") + " drift=" + drift.ToString("F4") + ")");
                RaycastHit hit; bool wallHit = Physics.Raycast(plate.position - text.forward * .05f, text.forward, out hit, 1f, ~(1 << 5), QueryTriggerInteraction.Ignore);
                check(!wallHit || hit.distance - .05f >= PlateWallMinGap, pair + " plate clear of wall (gap=" + (wallHit ? (hit.distance - .05f).ToString("F4") : "none") + ")");
            }
            foreach (var spec in new[] {"cameraPositions:2", "dogRoots:6", "dogHeads:6", "wander:6", "pet:6"})
            {
                var parts = spec.Split(':'); object value;
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
                check(backing.publicVariables.TryGetVariableValue(parts[0],out value) && value is Array && ((Array)value).Length == int.Parse(parts[1]), "bedroom backing " + spec);
            }
            foreach (var field in new[] {"language", "pouch", "keeper", "viewer", "live", "noticeRoot", "noticeLabel", "hintRoot", "hintLabel"})
            {
                object value; var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(h);
                check(backing.publicVariables.TryGetVariableValue(field,out value) && value as Object != null, "hint backing " + field);
            }
            check(h.pouch.hints == h && h.language != null && h.viewer == viewer && h.live != null && h.keeper != null && h.sleepSlots.Length == 6 && h.sleepSlots.All(s => s != null), "hint focus hooks");
            check(h.dogs.Length == 6 && h.pet.Length == 6 && h.balls.Length == 6 && h.pickups.Length == 6 && h.balls.All(v => v != null) && h.pickups.All(v => v != null), "six dogs / balls");
            check(!h.sessionOnly && h.sleepNoticeEnabled, "production persistence and sleep notice enabled");
            foreach (var root in new[] {h.noticeRoot, h.hintRoot})
                check(!root.activeSelf && root.layer == 5 && root.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace && root.GetComponent<GraphicRaycaster>() == null && root.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), root.name + " hidden non-interactive world UI");
            check(h.NoticeCandidate(500, false, 0, false, false, 0, 1000) == 0, "no notices before restore");
            check(h.NoticeCandidate(179, true, 0, false, false, 0, 1000) == 0, "menu delay");
            check(h.NoticeCandidate(180, true, 0, false, false, 0, 1000) == 1, "first menu notice");
            check(h.NoticeCandidate(500, true, 3, false, false, 0, 1000) == 0, "no repeated notices after restore");
            check(h.NoticeCandidate(300, true, 1, true, false, 0, 1000) == 2, "sleep notice after menu used");
            check(h.NoticeCandidate(500, true, 0, true, true, 0, 1000) == 0, "activity suppresses notices");
            check(h.NoticeCandidate(500, true, 0, false, false, 3, 1000) == 0 && h.NoticeCandidate(500, true, 0, false, false, 0, 74) == 0, "count and gap caps");
            var nav = Find<Transform>("KD_Navigation").GetComponentInChildren<SpriteRenderer>(true);
            check(nav.sprite != null && AssetDatabase.GetAssetPath(nav.sprite).EndsWith("explain-onboarding.png"), "entry poster connected");
            var originalPoster = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/KominkaHillside/Imported/KominkaDogRun/Data/Panfret/explain.png");
            check(originalPoster != null && (nav.sprite.bounds.size-originalPoster.bounds.size).sqrMagnitude < .000001f, "entry poster physical dimensions preserved");
            var catalog = KD_LocalizationEditor.Load();
            foreach(var name in new[] {BedroomName,"KD_SleepGuide"}) foreach(var tmp in Find<Transform>(name).GetComponentsInChildren<TextMeshPro>(true))
            {
                string before=tmp.text;
                try {foreach(var lang in new[]{"ja","en"}) {
                    tmp.text=KD_LocalizationEditor.Text(catalog,lang,name==BedroomName?"gallery.bedroom.title":"guide.sleep");tmp.ForceMeshUpdate(true,true);
                    check(tmp.textInfo.lineCount <= (name==BedroomName?1:2),lang+" "+name+" short caption lines="+tmp.textInfo.lineCount);
                }} finally {tmp.text=before;tmp.ForceMeshUpdate(true,true);}
            }
            var photo = Find<Transform>("KD_SleepGuide").Find("Photo").GetComponent<SpriteRenderer>();
            log.AppendLine(photo.sprite == null ? "PENDING actual VR sleep photo; slot hidden" : "INFO sleep photo present");
            log.AppendLine("checks=" + checks + " failures=" + failures); Write("verify", log);
            if (failures > 0) throw new InvalidOperationException("Bedroom/onboarding verification failed: " + failures);
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Bedroom and Onboarding/4 - Render")]
        public static void Render()
        {
            Directory.CreateDirectory(Output);
            var room = Find<KD_SleepAudio>("KD_SleepAudio").roomBounds;
            var center = room.transform.TransformPoint(room.center);
            var survey = new StringBuilder();
            foreach (var direction in new[] {Vector3.left, Vector3.right, Vector3.forward, Vector3.back})
                foreach (var hit in Physics.RaycastAll(center, direction, 4, ~0, QueryTriggerInteraction.Ignore)) survey.AppendLine(direction+" -> "+hit.collider.name+" "+hit.point+" distance="+hit.distance);
            Write("walls", survey);
            RenderAt("room", new Vector3(center.x - 1.4f, 1.7f, center.z - 1.4f), new Vector3(center.x + .4f, .65f, center.z + .4f), 75);
            RenderAt("wall", new Vector3(center.x, 1.35f, center.z + .1f), new Vector3(center.x, 1.35f, center.z - 1.5f), 70);
            var bedroom = KD_SceneObjects.Find(BedroomName)?.GetComponent<KD_BedroomPhotoDirector>();
            if (bedroom != null)
            {
                for (int i = 0; i < 2; i++) RenderAt("camera-" + i, bedroom.cameraPositions[i].position, new Vector3(center.x, .4f, center.z), 50);
                var boards = bedroom.gallery.boards;
                var active = boards.Select(b => b.activeSelf).ToArray();
                var mats = boards.Select(b => b.GetComponent<Renderer>().sharedMaterial).ToArray();
                var previewMats = new Material[4]; var previews = new Texture2D[2];
                try
                {
                    for (int i=0;i<2;i++) {previews[i]=new Texture2D(2,2);previews[i].LoadImage(File.ReadAllBytes(Output+"camera-"+i+".png"));}
                    for (int i=0;i<4;i++) {previewMats[i]=new Material(Shader.Find("Unlit/Texture"));previewMats[i].mainTexture=previews[i%2];boards[i].GetComponent<Renderer>().sharedMaterial=previewMats[i];boards[i].SetActive(true);}
                    RenderAt("wall-with-sample-frames",new Vector3(center.x,1.35f,center.z+.1f),new Vector3(center.x,1.35f,center.z-1.5f),70);
                }
                finally
                {
                    for(int i=0;i<4;i++) {boards[i].GetComponent<Renderer>().sharedMaterial=mats[i];boards[i].SetActive(active[i]);if(previewMats[i]!=null)Object.DestroyImmediate(previewMats[i]);}
                    foreach(var texture in previews) if(texture!=null)Object.DestroyImmediate(texture);
                }
            }
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Bedroom and Onboarding/5 - Render UI")]
        public static void RenderUi()
        {
            var h = Find<KD_HintDirector>(HintName); var keeper = h.keeper;
            var roots = new[] { keeper.menuRoot, h.noticeRoot, h.hintRoot };
            var active = roots.Select(r => r.activeSelf).ToArray();
            var texts = roots.SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)).ToArray();
            var oldTexts = texts.Select(t => t.text).ToArray();
            var scrollPosition = keeper.contentScroll.content.anchoredPosition;
            var positions = roots.Select(r => r.transform.position).ToArray(); var rotations = roots.Select(r => r.transform.rotation).ToArray();
            var catalog = KD_LocalizationEditor.Load(); var log = new StringBuilder();
            try
            {
                for (int n=0; n<roots.Length; n++) roots[n].SetActive(false);
                foreach (string lang in new[] { "ja", "en" })
                {
                    keeper.menuRoot.SetActive(true);
                    keeper.infoBody.text = KD_LocalizationEditor.Text(catalog, lang, "KD_ShopKeeper.InfoWays");
                    foreach (var t in keeper.menuRoot.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (t.name == "InfoTitle") t.text = KD_LocalizationEditor.Text(catalog, lang, "KD_ShopKeeper.WordWays");
                        string[] names = {"HowTo", "Petting", "Dogs", "Photos", "Shop", "Treats", "Ways", "Call", "Bye"};
                        foreach (string name in names) if (t.transform.parent.name == name+"Button") t.text = KD_LocalizationEditor.Text(catalog, lang, "KD_ShopKeeper.Word"+name);
                    }
                    Canvas.ForceUpdateCanvases(); keeper.contentScroll.verticalNormalizedPosition = 1f; Canvas.ForceUpdateCanvases();
                    var tr = keeper.menuRoot.transform;
                    float height = ((RectTransform)tr).rect.height * tr.lossyScale.y;
                    RenderAt("npc-"+lang, tr.position-tr.forward*(height*.55f/Mathf.Tan(25*Mathf.Deg2Rad)),tr.position,50);
                    keeper.menuRoot.SetActive(false);
                    foreach (var root in new[] {h.noticeRoot,h.hintRoot})
                    {
                        root.transform.SetPositionAndRotation(new Vector3(0,-1000,0),Quaternion.identity); root.SetActive(true);
                        var label = root.GetComponentInChildren<TextMeshProUGUI>(true);
                        string prefix = root == h.noticeRoot ? "hint.menu." : "hint.throw.";
                        foreach (string suffix in new[] {"vr","desktop","touch"})
                        {
                            label.text = KD_LocalizationEditor.Text(catalog,lang,prefix+suffix); Canvas.ForceUpdateCanvases(); label.ForceMeshUpdate(true,true);
                            bool fits = label.preferredHeight <= label.rectTransform.rect.height+.5f;
                            log.AppendLine((fits?"PASS ":"FAIL ")+lang+" "+prefix+suffix+" height="+label.preferredHeight+" frame="+label.rectTransform.rect.height);
                            RenderAt(root.name+"-"+lang+"-"+suffix,root.transform.position-Vector3.forward*.95f,root.transform.position,32);
                        }
                        root.SetActive(false);
                    }
                }
            }
            finally
            {
                for (int i=0;i<texts.Length;i++) texts[i].text=oldTexts[i];
                keeper.menuRoot.SetActive(true); Canvas.ForceUpdateCanvases();
                keeper.contentScroll.content.anchoredPosition = scrollPosition;
                for (int i=0;i<roots.Length;i++) {roots[i].transform.SetPositionAndRotation(positions[i],rotations[i]);roots[i].SetActive(active[i]);}
            }
            Write("ui", log);
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Bedroom and Onboarding/6 - Snapshot Layout")]
        public static void SnapshotLayout()
        {
            var b = Find<KD_BedroomPhotoDirector>(BedroomName); var h = Find<KD_HintDirector>(HintName);
            var log = new StringBuilder();
            var targets = b.GetComponentsInChildren<Transform>(true)
                .Concat(Find<Transform>("KD_SleepGuide").GetComponentsInChildren<Transform>(true))
                .Concat(new[] {b.floor,b.cameraPositions[0],b.cameraPositions[1],Find<Transform>("KD_Navigation"),h.keeper.menuRoot.transform.parent})
                .Distinct().OrderBy(t => t.name);
            foreach(var t in targets)
                log.AppendLine(t.name+" position="+t.position.ToString("F6")+" rotation="+t.rotation.ToString("F6")+" scale="+t.lossyScale.ToString("F6"));
            Write("layout-snapshot",log);
        }
        static void RenderAt(string name, Vector3 position, Vector3 target, float fov)
        {
            var go = new GameObject("KD_EditorPhotoPreview"); var cam = go.AddComponent<Camera>();
            var rt = new RenderTexture(1280, 720, 24); var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                cam.enabled = false; cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target-position));
                cam.fieldOfView = fov; cam.nearClipPlane = .03f; cam.farClipPlane = 60f; cam.cullingMask = ~0;
                cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); File.WriteAllBytes(Output+name+".png", tex.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; cam.targetTexture = null; Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
        }
    }
}
