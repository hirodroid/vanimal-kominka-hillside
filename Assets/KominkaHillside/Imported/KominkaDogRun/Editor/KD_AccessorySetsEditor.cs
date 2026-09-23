using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    public static class KD_AccessorySetsEditor
    {
        public const int MenuCount = 16;
        const string Output = "Temp/accessory-sets-integration";
        public static readonly string[] Keys = { "Crown", "StrawHat", "BowTie", "TailRibbon", "Bandana", "RoyalNecklace", "RoyalTail", "Sunglasses", "SunflowerNecklace", "MarineCap", "FlowerCrown", "FlowerCorsage", "FlowerTail", "TopHat", "Cap", "SpiralGlasses", "StarGlasses", "Backpack", "CandyDonutBeret", "CandyPawMedallion", "CandyClip", "PumpkinWitchHat", "PumpkinBrooch", "PumpkinBatTail" };
        // 一覧の並び (IMP-INF-006 P9、2026-09-14 ユーザー決定): **安い → 高い、CE (Credits) は最後**。所持で並びは変えない。
        // 見える行 = バンダナ 0 → 尻尾リボン 10 → 蝶ネクタイ / キャップ / ぐるぐる眼鏡 20 → 麦わら / シルクハット / 星眼鏡 30 → リュック 40
        //          → お花のおめかし 60pt → 小さな王様 / 夏のおでかけ 200C → キャンディ・ステップ 300C → パンプキン・パレード 300C。
        // セットの構成品 (setMembers。一覧には出ない) は各セットの直後に置く。並びは表示だけで、行 → 商品は IndexOf で引く
        static readonly int[] Selections = { 4, 3, 2, 14, 15, 1, 13, 16, 17, 102, 10, 11, 12, 100, 0, 5, 6, 101, 9, 7, 8, 103, 18, 19, 20, 104, 21, 22, 23 };
        static readonly string[] Goods = { "accessory.bandana", "accessory.straw_hat", "accessory.bow_tie", "accessory.tail_ribbon", "accessory.royal_set", "accessory.summer_set", "accessory.floral_set", "accessory.top_hat", "accessory.cap", "accessory.spiral_glasses", "accessory.star_glasses", "accessory.backpack", "live.candy_step_set", "live.pumpkin_parade_set" };
        static readonly string[] GoodsKeys = { "Bandana", "StrawHat", "BowTie", "TailRibbon", "Royal", "Summer", "Floral", "TopHat", "Cap", "SpiralGlasses", "StarGlasses", "Backpack", "CandyStep", "PumpkinParade" };
        static readonly int[] SetGoods = { 4, 5, 6, 12, 13 };

        static readonly int[] ProductGoods = { 4, 1, 2, 3, 0, 4, 4, 5, 5, 5, 6, 6, 6, 7, 8, 9, 10, 11, 12, 12, 12, 13, 13, 13 };
        static readonly string[] CandyModels = { "DonutBeret", "PawMedallion", "CandyClip", "WitchHat", "PumpkinBrooch", "BatTail" };

        // Called on every normal Setup, preserving the fitted nodes copied from the approved live.
        static void BindCandyNodes(KD_AccessoryBox box)
        {
            int dogs = KD_DogDefs.All.Length;
            if (box.dogCount != dogs || box.productIds == null || box.productIds.Length < 18
                || !box.productIds.Take(18).SequenceEqual(Enumerable.Range(0, 18))
                || box.nodes == null || box.nodes.Length < 18 * dogs || box.productSlots == null || box.productSlots.Length < 18)
                throw new InvalidOperationException("Existing accessory IDs 0..17 must be wired before Candy");
            var candy = new GameObject[6 * dogs];
            int[] slots = { (int)KD_AccessorySetupEditor.Slot.Head, (int)KD_AccessorySetupEditor.Slot.Chest, (int)KD_AccessorySetupEditor.Slot.Tail };
            for (int product = 0; product < 6; product++) for (int d = 0; d < dogs; d++)
            {
                string name = (product < 3 ? "KD_CandyCostume_" : "KD_PumpkinCostume_") + CandyModels[product] + "_" + KD_DogDefs.All[d].key;
                var node = KD_SceneObjects.Find(name, out int count);
                var dog = KD_SceneObjects.Find(KD_DogDefs.All[d].DogName);
                if (node == null || count != 1 || dog == null
                    || node.transform.parent != KD_AccessorySetupEditor.AnchorFor(KD_DogRig.Resolve(dog), (KD_AccessorySetupEditor.Slot)slots[product % 3]))
                    throw new InvalidOperationException("Copy the fitted Candy node onto its matching dog anchor first: " + name);
                candy[product * dogs + d] = node;
            }
            box.nodes = box.nodes.Take(18 * dogs).Concat(candy).ToArray();
            box.candyPerformanceNodes = candy.Take(18).ToArray();
            box.productIds = Enumerable.Range(0, Keys.Length).ToArray();
            box.productSlots = box.productSlots.Take(18).Concat(slots).Concat(slots).ToArray();
            foreach (var node in candy) node.SetActive(false);
        }

        /// <summary>Metadata and existing accessory rows only; no fitting, panel rebuild or save.</summary>
        public static void RefreshCandyIntegration(KD_AccessoryBox box, KD_TreatPouch p, StringBuilder log)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || box == null || p == null
                || box.gameObject.scene != p.gameObject.scene || p.gameObject.scene != EditorSceneManager.GetActiveScene())
                throw new InvalidOperationException("Stopped active world with matching accessory box and pouch required");
            Wire(box, p, log);
            p.accessories = box;
            BuildRows(p, log);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)p.accRows[0].transform.parent);
            EditorUtility.SetDirty(p); UdonSharpEditorUtility.CopyProxyToUdon(p);
            EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
        }
        static KD_TreatPouch Pouch() { return KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>(); }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Sets - Build Integration")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            Directory.CreateDirectory(Output);
            var p = Pouch();
            var before = OriginalPlacements();
            if (!File.Exists(Output + "/original-placements.json")) File.WriteAllText(Output + "/original-placements.json", before);
            string backup = Output + "/before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
            EditorSceneManager.SaveScene(p.gameObject.scene, backup, true);
            KD_AccessorySetupEditor.SetupNodes();
            if (p.accessories.nodes.Length != Keys.Length * KD_DogDefs.All.Length) throw new InvalidOperationException("Expected all fitted accessory nodes");
            var log = new StringBuilder();
            BuildRows(p, log);
            var catalog = KD_LocalizationEditor.Load();
            KD_LocalizationEditor.Configure(p.language, catalog, log);
            KD_LocalizationEditor.Bind(p.language, catalog, log);
            string missing = new string(KD_LocalizationEditor.BundledCharacters(catalog).Where(c => !char.IsControl(c) && !KD_LocalizationEditor.HasGlyph(p.sectionTitleLabel.font, c)).Distinct().ToArray());
            if (missing.Length > 0)
            {
                var font = p.sectionTitleLabel.font;
                var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/KominkaHillside/Font/Noto_Serif_JP/static/NotoSerifJP-Regular.ttf");
                if (source == null) throw new InvalidOperationException("Japanese source font missing");
                var serialized = new SerializedObject(font);
                serialized.FindProperty("m_SourceFontFile").objectReferenceValue = source;
                serialized.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue = source;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                log.AppendLine("Font before: " + font.atlasWidth + "x" + font.atlasHeight + " glyphs=" + font.glyphTable.Count + " missing=" + missing.Length);
                font.TryAddCharacters(missing, out missing); EditorUtility.SetDirty(font);
            }
            if (missing.Length > 0) throw new InvalidOperationException("Missing font glyphs: " + missing);
            EditorUtility.SetDirty(p.language); UdonSharpEditorUtility.CopyProxyToUdon(p.language);
            EditorUtility.SetDirty(p); UdonSharpEditorUtility.CopyProxyToUdon(p);
            if (before.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Any(line => !OriginalPlacements().Contains(line + Environment.NewLine))) throw new InvalidOperationException("Original accessory placement changed");
            log.AppendLine("Original placements preserved; 24 accessory types; 14 goods; 16 UI rows; 29 selection records");
            File.WriteAllText(Output + "/build.txt", log.ToString());
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
        }

        // Called by the original Setup too, so later setup cannot truncate the product table.
        public static void Wire(KD_AccessoryBox box, KD_TreatPouch p, StringBuilder log)
        {
            BindCandyNodes(box);
            var catalog = KD_LocalizationEditor.Load();
            var items = new KD_ShopItem[Goods.Length];
            var ledger = Object.FindObjectOfType<KD_ProgressLedger>();
            if (ledger == null) throw new InvalidOperationException("Ledger missing");
            for (int i = 0; i < Goods.Length; i++)
            {
                string name = KD_ProductDefinitions.SceneName(Goods[i]);
                var go = KD_SceneObjects.Find(name);
                if (go == null) { go = new GameObject(name); go.transform.SetParent(box.transform, false); }
                var item = go.GetComponent<KD_ShopItem>();
                if (item == null) item = go.AddUdonSharpComponent<KD_ShopItem>();
                item.ledger = ledger; item.productKey = Goods[i];
                item.categoryKey = "accessory"; item.price = KD_ProductDefinitions.Get(Goods[i]).price; item.consumable = false; item.menuOnly = true;
                item.packCount = 0; item.initialStock = 0; item.stockCap = 0;
                KD_ProductDefinitions.Apply(item, Goods[i]); item.ceListingId = KD_ProductDefinitions.Get(Goods[i]).ceListingId; // User decision 09-10: Point for now; CE allocation is a later task.
                item.displayNameKey = Goods[i].StartsWith("live.", StringComparison.Ordinal)
                    ? name + ".displayNameKey.0" : "accessory." + GoodsKeys[i];
                item.displayName = KD_LocalizationEditor.Text(catalog, "ja", item.displayNameKey);
                item.listener = null; item.shelf = null;
                EditorUtility.SetDirty(item); UdonSharpEditorUtility.CopyProxyToUdon(item); items[i] = item;
            }
            box.productItems = ProductGoods.Select(i => items[i]).ToArray();
            box.setIds = new[] { 100, 101, 102, 103, 104 };
            box.setMembers = new[] { 0, 5, 6, 9, 7, 8, 10, 11, 12, 18, 19, 20, 21, 22, 23 };
            box.setItems = SetGoods.Select(i => items[i]).ToArray();
            EditorUtility.SetDirty(box); UdonSharpEditorUtility.CopyProxyToUdon(box);
            p.accProductIds = Selections.ToArray();
            p.accItems = Selections.Select(id => id >= 100 ? items[SetGoods[id - 100]] : box.productItems[id]).ToArray();
            p.accNameKeys = Selections.Select(id => "accessory." + (id >= 100 ? GoodsKeys[SetGoods[id - 100]] : Keys[id])).ToArray();
            p.accSlotNameKeys = Selections.Select(id => id >= 100 ? "accessory.set" : "accessory.slot." + box.productSlots[id]).ToArray();
            p.accNames = p.accNameKeys.Select(k => KD_LocalizationEditor.Text(catalog, "ja", k)).ToArray();
            p.accSlotNames = p.accSlotNameKeys.Select(k => KD_LocalizationEditor.Text(catalog, "ja", k)).ToArray();
            log.AppendLine("Accessory node IDs 0..20; Candy set103 and members18/19/20 share live.candy_step_set; existing IDs retained");
        }

        static void BuildRows(KD_TreatPouch p, StringBuilder log)
        {
            var old = p.accRows;
            var parent = old[0].transform.parent;
            var visible = Selections.Where(id => !p.accessories.setMembers.Contains(id)).ToArray();
            if (visible.Length > MenuCount) throw new InvalidOperationException("Accessory goods exceed menu event capacity");
            p.accRows = new GameObject[MenuCount]; p.accRowLabels = new TextMeshProUGUI[MenuCount];
            p.accRowSubLabels = new TextMeshProUGUI[MenuCount]; p.accButtons = new GameObject[MenuCount];
            p.accButtonComponents = new Button[MenuCount]; p.accButtonLabels = new TextMeshProUGUI[MenuCount];
            p.accGiftButtons = new GameObject[MenuCount]; p.accGiftLabels = new TextMeshProUGUI[MenuCount];
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            for (int i = 0; i < MenuCount; i++)
            {
                var row = parent.Find("AccRow" + i);
                if (row == null)
                {
                    var go = Object.Instantiate(old[0], parent); go.name = "AccRow" + i; row = go.transform;
                    var miniature = row.Find("DogPreview"); if (miniature != null) Object.DestroyImmediate(miniature.gameObject);
                }
                p.accRows[i] = row.gameObject; p.accRowLabels[i] = row.Find("Label").GetComponent<TextMeshProUGUI>();
                p.accRowSubLabels[i] = row.Find("Sub").GetComponent<TextMeshProUGUI>();
                var button = row.Find("Button").GetComponent<Button>();
                for (int e = button.onClick.GetPersistentEventCount() - 1; e >= 0; e--) UnityEventTools.RemovePersistentListener(button.onClick, e);
                UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, "_Acc" + i);
                p.accButtons[i] = button.gameObject; p.accButtonComponents[i] = button; p.accButtonLabels[i] = button.GetComponentInChildren<TextMeshProUGUI>(true);
                // [ギフト] (IMP-PRG-012): 複製した行も口を自分の添字へ結び直す。無い (古い Setup) なら Treat Setup を先に流す
                var giftT = row.Find("Gift");
                if (giftT == null) throw new InvalidOperationException("AccRow" + i + " has no Gift button - run Treat Setup first (IMP-PRG-012)");
                var gift = giftT.GetComponent<Button>();
                for (int e = gift.onClick.GetPersistentEventCount() - 1; e >= 0; e--) UnityEventTools.RemovePersistentListener(gift.onClick, e);
                UnityEventTools.AddStringPersistentListener(gift.onClick, backing.SendCustomEvent, "_AccGift" + i);
                gift.gameObject.SetActive(false);
                p.accGiftButtons[i] = gift.gameObject; p.accGiftLabels[i] = gift.GetComponentInChildren<TextMeshProUGUI>(true);
                if (i < visible.Length)
                {
                    int metadata = Array.IndexOf(Selections, visible[i]);
                    p.accRowLabels[i].text = p.accNames[metadata];
                    var item = p.accItems[metadata];
                    p.accRowSubLabels[i].text = p.accSlotNames[metadata] + " / "
                        + (item.IsCE() ? KD_ShopShelf.WordSpecial : item.price + " pt");
                    p.accButtonLabels[i].text = KD_TreatPouch.WordAccChoose;
                }
                row.gameObject.SetActive(i < visible.Length); // First view contains sets and standalone goods.
            }
        }

        public static bool PlaceNew(Transform holder, GameObject dog, KD_DogRig rig, string key, Vector3 modelSize)
        {
            string dogKey = KD_DogDefs.All.First(d => d.DogName == dog.name).key;
            string reference = key == "MarineCap" || key == "FlowerCrown" ? "Crown" : key.EndsWith("Tail") ? "TailRibbon" : "BowTie";
            var source = KD_SceneObjects.Find("KD_Acc_" + reference + "_" + dogKey).transform;
            Vector3 up = dog.transform.up;
            Vector3 forward = Vector3.ProjectOnPlane(rig.nose.position - rig.head.position, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            float width = ProjectedWidth(source, right);
            if (key == "MarineCap") width *= 1.18f;
            if (key == "FlowerCrown") width *= 1.18f;
            if (key == "Bandana" || key == "RoyalNecklace" || key == "SunflowerNecklace") width *= 1.35f;
            holder.SetPositionAndRotation(source.position, source.rotation);
            if (key == "Sunglasses")
            {
                // Face mesh is placed in front of the eyes, not at the muzzle tip.
                width = rig.headScale * 2.15f;
                bool broadMuzzle = dogKey == "Corgi" || dogKey == "Shiba";
                holder.SetPositionAndRotation(rig.head.position + forward * rig.headScale * (broadMuzzle ? 1.35f : 1.15f) + up * rig.headScale * (broadMuzzle ? .40f : .28f), Quaternion.LookRotation(forward, up));
            }
            float scale = width / Mathf.Max(.0001f, modelSize.x);
            var inherited = holder.parent.lossyScale;
            holder.localScale = new Vector3(scale / inherited.x, scale / inherited.y, scale / inherited.z);
            if (key == "Bandana") LiftFromSurface(holder, dog, forward, up, .004f);
            if (dogKey == "Samoyed" && key.EndsWith("Tail")) LiftFromSurface(holder, dog, up, forward, .002f);
            return true;
        }

        internal static void LiftFromSurface(Transform holder, GameObject dog, Vector3 forward, Vector3 up, float clearance)
        {
            var right = Vector3.Cross(up, forward).normalized;
            var positions = holder.GetComponentsInChildren<MeshFilter>(true).SelectMany(m => m.sharedMesh.vertices.Select(v => m.transform.TransformPoint(v))).ToArray();
            float low = positions.Min(v => Vector3.Dot(v, up)), high = positions.Max(v => Vector3.Dot(v, up));
            float left = positions.Min(v => Vector3.Dot(v, right)), rightmost = positions.Max(v => Vector3.Dot(v, right));
            float face = positions.Where(v => Vector3.Dot(v, forward) > Vector3.Dot(holder.position, forward) - .01f).Select(v => Vector3.Dot(v, forward)).DefaultIfEmpty(Vector3.Dot(holder.position, forward)).Min();
            float surface = float.NegativeInfinity;
            foreach (var skin in dog.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                var mesh = new Mesh();
                try
                {
                    skin.BakeMesh(mesh, true);
                    foreach (var v in mesh.vertices)
                    {
                        var world = skin.transform.TransformPoint(v); float y = Vector3.Dot(world, up), x = Vector3.Dot(world, right);
                        if (y < low || y > high || x < left || x > rightmost) continue;
                        surface = Mathf.Max(surface, Vector3.Dot(world, forward));
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
            if (!float.IsInfinity(surface)) holder.position += forward * Mathf.Max(0, surface + clearance - face);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Sets - Fit New Nodes")]
        public static void FitNewNodes()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var before = OriginalPlacements(4);
            foreach (var def in KD_DogDefs.All)
            {
                var dog = KD_SceneObjects.Find(def.DogName); var rig = KD_DogRig.Resolve(dog);
                foreach (string key in Keys.Skip(4).Take(9))
                {
                    var holder = KD_SceneObjects.Find("KD_Acc_" + key + "_" + def.key).transform;
                    // Imported geometry dimensions in holder space, independent of current placement.
                    var points = holder.GetComponentsInChildren<MeshFilter>(true).SelectMany(m => m.sharedMesh.vertices.Select(v => holder.InverseTransformPoint(m.transform.TransformPoint(v)))).ToArray();
                    float width = points.Max(v => v.x) - points.Min(v => v.x);
                    PlaceNew(holder, dog, rig, key, new Vector3(width, 1, 1)); EditorUtility.SetDirty(holder);
                }
            }
            if (before != OriginalPlacements(4)) throw new InvalidOperationException("Original placement changed");
            EditorSceneManager.MarkSceneDirty(Pouch().gameObject.scene);
        }

        static float ProjectedWidth(Transform root, Vector3 right)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) foreach (var v in mf.sharedMesh.vertices)
            { float x = Vector3.Dot(mf.transform.TransformPoint(v), right); min = Mathf.Min(min, x); max = Mathf.Max(max, x); }
            return max - min;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Sets - Render Fits")]
        public static void RenderFits()
        {
            Directory.CreateDirectory(Output);
            int[][] sets = { new[] { 0, 5, 6 }, new[] { 9, 7, 8 }, new[] { 10, 11, 12 }, new[] { 4 } };
            foreach (var def in KD_DogDefs.All) for (int pose = 0; pose < 2; pose++)
            {
                var sheet = new Texture2D(1600, 1200, TextureFormat.RGB24, false);
                try
                {
                    for (int set = 0; set < 4; set++) for (int angle = 0; angle < 2; angle++)
                    {
                        var preview = new PreviewRenderUtility(); var meshes = new List<Mesh>(); GameObject clone = null;
                        try
                        {
                            clone = Object.Instantiate(KD_SceneObjects.Find(def.DogName));
                            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.SetActive(true);
                            KD_DogPreviewEditor.SamplePose(clone, pose == 0 ? def.idle : def.sleepLoop);
                            foreach (var t in clone.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("KD_Acc_"))
                                t.gameObject.SetActive(sets[set].Any(id => t.name == "KD_Acc_" + Keys[id] + "_" + def.key));
                            bool first = true; Bounds bounds = new Bounds();
                            foreach (var r in clone.GetComponentsInChildren<Renderer>())
                            {
                                if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                                Mesh mesh;
                                if (r is SkinnedMeshRenderer skin) { mesh = new Mesh(); skin.BakeMesh(mesh, true); meshes.Add(mesh); }
                                else { var filter = r.GetComponent<MeshFilter>(); if (filter == null) continue; mesh = filter.sharedMesh; }
                                var go = new GameObject("FitGeometry"); go.AddComponent<MeshFilter>().sharedMesh = mesh;
                                bool accessory = false;
                                for (var t = r.transform; t != null && t != clone.transform; t = t.parent) if (t.name.StartsWith("KD_Acc_")) accessory = true;
                                // Solid preview materials avoid fur shaders depending on the live renderer/lighting.
                                go.AddComponent<MeshRenderer>().sharedMaterials = accessory ? r.sharedMaterials : r.sharedMaterials.Select(KD_DogPreviewEditor.PreviewMaterial).ToArray();
                                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation); go.transform.localScale = r.transform.lossyScale;
                                preview.AddSingleGO(go);
                                var b = go.GetComponent<Renderer>().bounds;
                                if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
                            }
                            var rig = KD_DogRig.Resolve(clone);
                            Vector3 up = clone.transform.up;
                            Vector3 forward = Vector3.ProjectOnPlane(rig.nose.position - rig.head.position, up).normalized;
                            Vector3 right = Vector3.Cross(up, forward).normalized;
                            Vector3 direction = (forward * (angle == 0 ? .85f : -.75f) + right * .65f + up * .25f).normalized;
                            var camera = preview.camera; camera.fieldOfView = 30; camera.nearClipPlane = .001f; camera.farClipPlane = 100;
                            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.20f, .24f, .22f);
                            camera.transform.position = bounds.center + direction * bounds.size.magnitude * 1.9f;
                            camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, up);
                            preview.ambientColor = new Color(.45f, .45f, .45f);
                            preview.lights[0].intensity = 1.1f; preview.lights[0].transform.rotation = Quaternion.LookRotation(-forward - up);
                            preview.lights[1].intensity = .6f; preview.lights[1].transform.rotation = Quaternion.LookRotation(forward - right - up);
                            preview.BeginStaticPreview(new Rect(0, 0, 400, 600)); preview.Render(true);
                            var image = preview.EndStaticPreview();
                            sheet.SetPixels(set * 400, (1 - angle) * 600, 400, 600, image.GetPixels()); Object.DestroyImmediate(image);
                        }
                        finally { if (clone != null) Object.DestroyImmediate(clone); preview.Cleanup(); foreach (var mesh in meshes) Object.DestroyImmediate(mesh); }
                    }
                    sheet.Apply(); File.WriteAllBytes(Output + "/fit-" + def.key + "-" + pose + ".png", sheet.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(sheet); }
            }
        }

        static string OriginalPlacements(int count = int.MaxValue)
        {
            var log = new StringBuilder();
            foreach (var dog in KD_DogDefs.All) foreach (var key in Keys.Take(count))
            {
                var go = KD_SceneObjects.Find("KD_Acc_" + key + "_" + dog.key);
                if (go == null) continue;
                var t = go.transform;
                log.AppendLine(t.name + "|" + t.parent.GetInstanceID() + "|" + JsonUtility.ToJson(t.localPosition) + "|" + JsonUtility.ToJson(t.localRotation) + "|" + JsonUtility.ToJson(t.localScale));
            }
            return log.ToString();
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Sets - Verify Integration")]
        public static void Verify()
        {
            Directory.CreateDirectory(Output); var log = new StringBuilder(); int failures = 0;
            void Check(bool ok, string label) { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
            var p = Pouch(); var box = p.accessories;
            Check(box.productIds.SequenceEqual(Enumerable.Range(0, Keys.Length)) && box.nodes.Length == Keys.Length * 6, "all products x 6 dogs; original node IDs retained");
            Check(p.accRows.Length == MenuCount && p.accProductIds.SequenceEqual(Selections) && p.accItems.Length == Selections.Length, "16 UI rows and 29 stable selection records");
            Check(p.accGiftButtons != null && p.accGiftButtons.Length == MenuCount && p.accGiftLabels != null && p.accGiftLabels.Length == MenuCount
                && Enumerable.Range(0, MenuCount).All(i => p.accGiftButtons[i] != null && !p.accGiftButtons[i].activeSelf && p.accGiftLabels[i] != null
                    && p.accGiftButtons[i].transform.parent == p.accRows[i].transform), "16 gift buttons wired, hidden by default, one per row (IMP-PRG-012)");
            Check(Enumerable.Range(0, MenuCount).All(i => typeof(KD_TreatPouch).GetMethod("_AccGift" + i, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) != null),
                "every _AccGift{i} event exists on KD_TreatPouch (MenuCount = " + MenuCount + "; add events before raising it)");
            Check(p.accNameKeys.Length == Selections.Length && p.accSlotNameKeys.Length == Selections.Length, "localized metadata covers all selections after binding");
            Check(box.candyPerformanceNodes != null && box.candyPerformanceNodes.SequenceEqual(box.nodes.Skip(108).Take(18)), "same 18 fitted Candy nodes for everyday selection and performance overlay");
            var catalog = KD_ProductsEditor.SceneComponents<KD_ShopCatalog>().Single(c => !c.name.StartsWith("Test"));
            foreach (var key in Goods)
            {
                var item = KD_SceneObjects.Find(KD_ProductDefinitions.SceneName(key)).GetComponent<KD_ShopItem>();
                Check(catalog.items.Count(i => i == item) == 1 && catalog.ledger.products.Count(i => i == item) == 1, "catalog and ledger registered once " + key);
                Check(item.price == KD_ProductDefinitions.Get(key).price, "price " + key);
            }
            Check(box.setIds.SequenceEqual(new[] { 100, 101, 102, 103, 104 })
                && box.setMembers.SequenceEqual(new[] { 0, 5, 6, 9, 7, 8, 10, 11, 12, 18, 19, 20, 21, 22, 23 }), "set membership");
            // P9: 見える行は 安い → 高い、CE は最後 (Point 同士は price、CE 同士は creditPrice で非減少)
            {
                var visibleItems = new System.Collections.Generic.List<KD_ShopItem>();
                for (int i = 0; i < p.accProductIds.Length; i++)
                    if (!box.setMembers.Contains(p.accProductIds[i])) visibleItems.Add(p.accItems[i]);
                bool ordered = true; string order = "";
                for (int i = 0; i < visibleItems.Count; i++)
                {
                    var it = visibleItems[i]; bool ce = !string.IsNullOrEmpty(it.ceListingId);
                    order += (i > 0 ? " > " : "") + (ce ? it.ceCredits + "C" : it.price + "pt");
                    if (i == 0) continue;
                    var prev = visibleItems[i - 1]; bool prevCe = !string.IsNullOrEmpty(prev.ceListingId);
                    if (prevCe && !ce) ordered = false;
                    else if (prevCe == ce && (ce ? prev.ceCredits > it.ceCredits : prev.price > it.price)) ordered = false;
                }
                Check(ordered, "visible rows cheap to expensive, CE last (P9): " + order);
            }
            for (int i = 0; i < box.nodes.Length; i++)
            {
                var node = box.nodes[i]; int product = i / 6; var dog = KD_SceneObjects.Find(KD_DogDefs.All[i % 6].DogName);
                var rig = KD_DogRig.Resolve(dog);
                Check(node != null && node.transform.parent == KD_AccessorySetupEditor.AnchorFor(rig, (KD_AccessorySetupEditor.Slot)box.productSlots[product]), "anchor " + Keys[product] + "/" + dog.name);
                Check(node != null && !node.activeSelf && node.GetComponentsInChildren<Renderer>(true).Length == 1, "inactive single renderer " + i);
            }
            for (int set = 0; set < box.setIds.Length; set++)
            {
                var slots = new HashSet<int>();
                for (int m = 0; m < 3; m++) { int id = box.setMembers[set * 3 + m]; slots.Add(box.productSlots[id]); Check(box.productItems[id] == box.setItems[set], "shared entitlement " + id); }
                Check(slots.Count == 3, "distinct slots set " + set);
            }
            foreach (string id in Goods)
            {
                var item = KD_SceneObjects.Find(KD_ProductDefinitions.SceneName(id)).GetComponent<KD_ShopItem>();
                Check(item.IsConfigured() && item.IsCE() == !string.IsNullOrEmpty(KD_ProductDefinitions.Get(id).ceListingId)
                    && item.menuOnly && item.GetComponent<Collider>() == null, "configured acquisition type " + id);
                if (id == "live.candy_step_set")
                    Check(item.categoryKey == "live" && box.productItems.Skip(18).Take(3).All(i => i == item)
                        && box.setItems[3] == item && item.IsCE() && item.ceCredits == KD_ProductDefinitions.Get(id).ceCredits, "Candy shared live entitlement is one CE entitlement (300C, listing since 2026-09-15; the 09-13 point interim is over)");
            }
            Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(box).SyncMethod == VRC.SDKBase.Networking.SyncType.Manual, "backing Manual");
            GameObject[] wired; var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(box);
            Check(backing.publicVariables.TryGetVariableValue("nodes", out wired) && wired.SequenceEqual(box.nodes), "backing 144 nodes");
            log.AppendLine("FAILURES=" + failures); File.WriteAllText(Output + "/verify.txt", log.ToString());
            if (failures > 0) throw new InvalidOperationException("Accessory integration failed: " + failures);
        }
    }
}
