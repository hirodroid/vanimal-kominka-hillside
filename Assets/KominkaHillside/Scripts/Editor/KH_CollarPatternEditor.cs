using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-050 首輪とリードのおそろいセット (2026-10-13 ユーザー決定「推奨で」: 別の挙動・新分類 collar・小見出し・ミニチュアは基本色)。
    ///
    /// Tools/KominkaHillside/Collar/ (本番シーンの局所編集。**シーンは保存しない**。統括が保存する):
    /// - 0 - Create Collar Programs: KH_CollarPattern / KH_CollarTag / KH_CollarLeash の Program Asset (無いものだけ)。この後 U# コンパイル
    /// - 1 - Setup Collar Pattern (冪等): KH_Dogs/KH_CollarPattern (Manual) / KH_CollarLeash (None) / KH_CollarTag (Manual + VRCPlayerObject) を作り、
    ///   犬 6 匹の首輪の Band (と埋もれ 2 犬種の AttachmentRing)・リードの縄 / スナップ / 持ち手・カタログの collar 行・柄のテクスチャを配線する。
    ///   カタログの通知先に KH_CollarLeash を足す (Menu/4 も残す)。手元メニューの かざりの Content に「首輪とリード」の小見出し 3 セルと 5 行を
    ///   FxRow0 / FxHead0..1 の複製で足し (Menu/7 は周回版で実行禁止なので、撫でFX の同梱の拡張 (KD_BundledPetFxEditor) と同じ局所の足し方)、
    ///   [えらぶ/買う] を _Collar{i} へ結ぶ。文言の字形 (ja / en) を共通フォントへ足し、KD_Language の表を作り直す。
    ///   前提: Collar/0 → U# コンパイル → Menu/4 Setup Shop (collar の 5 行を作る)。新しい同期物なので、この後 Menu/19b Assign Network IDs → 保存。
    /// - 2 - Verify Collar Pattern: 配線・表・文言・テクスチャ・行の並び・NetworkID を数えて COLLAR_OK n / COLLAR_FAIL を出す。直さない。
    ///
    /// 柄のテクスチャは仮 (`*_provisional.png`、scripts/art/leash-assets.cjs --patterns)。本番の絵は BUG-DOG-011 (金具の位置) の VR 確認の後。</summary>
    public static class KH_CollarPatternEditor
    {
        const string Menu = "Tools/KominkaHillside/Collar/";
        public const string HeadPrefix = "CollarHead", RowPrefix = "CollarRow";
        public const int HeadCount = 3, RowCount = 5;
        const string ScriptDir = "Assets/KominkaHillside/Scripts/";
        static readonly string[] Programs = { "KH_CollarPattern", "KH_CollarTag", "KH_CollarLeash" };
        const string TexDir = "Assets/KominkaHillside/Textures/Walking/";
        public const string BaseTexture = TexDir + "KH_Leash_BaseColor.png";
        const string SharedMaterialPath = "Assets/KominkaHillside/Data/Walking/KH_Leash_WovenMetal.mat";
        const string OutDir = "Temp/KH_Collar";
        const string ConsumerEvent = "_OnShopSelectionChanged";

        /// <summary>首輪の表 (option index = 行。[0] = 無料の既定 = 今の犬別の色)。価格の正本は KD_ProductDefinitions で、Setup / Verify が突き合わせる。</summary>
        public static readonly (string key, string scene, int price, string nameKey, string texture)[] Table =
        {
            ("collar.basic", "KD_ShopItem_Collar_Basic", 0, "collar.name.basic", BaseTexture),
            ("collar.stripe", "KD_ShopItem_Collar_Stripe", 30, "collar.name.stripe", TexDir + "KH_Leash_Pattern_Stripe_provisional.png"),
            ("collar.check", "KD_ShopItem_Collar_Check", 30, "collar.name.check", TexDir + "KH_Leash_Pattern_Check_provisional.png"),
            ("collar.momiji", "KD_ShopItem_Collar_Momiji", 30, "collar.name.momiji", TexDir + "KH_Leash_Pattern_Momiji_provisional.png"),
            ("collar.leather_brass", "KD_ShopItem_Collar_LeatherBrass", 50, "collar.name.leatherBrass", TexDir + "KH_Leash_Pattern_LeatherBrass_provisional.png"),
        };
        /// <summary>金具 (埋もれ 2 犬種の AttachmentRing) の色を替える柄 = 革と真鍮。ほかの柄は材質 (KH_ClipNeutral) の色のまま。</summary>
        public const int BrassOption = 4;
        /// <summary>真鍮の色 (仮。テクスチャの金具の帯 y 768..864 と同じ色)。本番は BUG-DOG-011 の後に絵と一緒に決める。</summary>
        public static readonly Color BrassRing = new Color(0.69f, 0.52f, 0.24f, 1f);
        /// <summary>帯を毛に埋めて金具とリードで見せる犬種 (BUG-DOG-011 案 1。KH_LeashProductionEditor.BuriedInFur と同じ並び = KD_DogDefs の順)。</summary>
        static readonly bool[] BuriedInFur = { false, true, false, false, false, true };
        public static readonly string[] WordKeys = { "collar.group", "collar.name.basic", "collar.name.stripe", "collar.name.check", "collar.name.momiji",
            "collar.name.leatherBrass", "collar.setBody", "collar.myLeash", "collar.onLeash" };

        // ============================================================
        // Collar/0
        // ============================================================

        [MenuItem(Menu + "0 - Create Collar Programs")]
        public static void CreatePrograms()
        {
            var created = new List<string>(); var existing = new List<string>();
            foreach (var n in Programs)
            {
                string path = ScriptDir + n;
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") != null) { existing.Add(n); continue; }
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path + ".cs");
                if (script == null) { Debug.LogError("[KH Collar] COLLAR_PROGRAM_FAIL " + path + ".cs not imported yet (refresh first)"); return; }
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = script;
                AssetDatabase.CreateAsset(asset, path + ".asset");
                created.Add(n);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[KH Collar] COLLAR_PROGRAM_OK created [" + string.Join(", ", created) + "], already [" + string.Join(", ", existing) + "]."
                + (created.Count > 0 ? " Compile U# (unity-compile.sh --udon) before Collar/1." : ""));
        }

        // ============================================================
        // Collar/1
        // ============================================================

        [MenuItem(Menu + "1 - Setup Collar Pattern")]
        public static void Setup()
        {
            KD_Hillside.Guard();
            foreach (var n in Programs)
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ScriptDir + n + ".asset") == null)
                    throw new InvalidOperationException(n + ".asset missing: run Collar/0, then compile U#");
            var log = new StringBuilder("[KH Collar] Setup (IMP-DOG-050)\n");
            var p = KH_CourseTravelEditor.FindPouch();
            if (new SerializedObject(p).FindProperty("collarPatterns") == null) throw new InvalidOperationException("KD_TreatPouch has no collarPatterns field yet: compile first");
            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int nDogs);
            if (dogsGo == null || nDogs != 1) throw new InvalidOperationException(KD_Hillside.DogsRootName + " missing or duplicated");
            var dogs = dogsGo.transform;

            // ---- 商品 (Menu/4 が作ったカタログの collar 行) ----
            var catalogs = KD_ProductsEditor.SceneComponents<KD_ShopCatalog>();
            if (catalogs.Length != 1) throw new InvalidOperationException("KD_ShopCatalog must exist once (" + catalogs.Length + ")");
            var catalog = catalogs[0];
            if (catalog.categoryKeys == null || !catalog.categoryKeys.Contains(KH_CollarPattern.CategoryKey))
                throw new InvalidOperationException("catalog has no category collar: run Menu/4 Setup Shop first");
            var items = Items(catalog);

            // ---- テクスチャ (柄は基本の BaseColor と同じ取り込み設定にそろえる) ----
            var textures = new Texture2D[Table.Length];
            for (int i = 0; i < Table.Length; i++)
            {
                if (i > 0) MatchImport(Table[i].texture, log);
                textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(Table[i].texture);
                if (textures[i] == null) throw new InvalidOperationException("Missing texture " + Table[i].texture + " (run node scripts/art/leash-assets.cjs --patterns, then refresh)");
            }

            // ---- 犬 6 匹の首輪とリード (KH_LeashManager.collarAnchors の順 = KD_DogDefs の順) ----
            var manager = Object.FindObjectOfType<KH_LeashManager>();
            if (manager == null || manager.collarAnchors == null || manager.collarAnchors.Length != KH_CollarPattern.DogCount) throw new InvalidOperationException("KH_LeashManager with six collar anchors required");
            var shared = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            var bands = new Renderer[6]; var rings = new Renderer[6]; var ropes = new Renderer[6]; var snaps = new Renderer[6]; var handles = new Renderer[6];
            Color ringBase = Color.white; bool ringBaseFound = false;
            for (int d = 0; d < 6; d++)
            {
                var collar = Collar(manager, d);
                var band = collar.transform.Find("Band");
                bands[d] = band == null ? null : band.GetComponent<MeshRenderer>();
                if (bands[d] == null || bands[d].sharedMaterial != shared) throw new InvalidOperationException("dog " + d + ": collar Band renderer with " + SharedMaterialPath + " required (Walking/Production 2 Apply)");
                if (BuriedInFur[d])
                {
                    var ring = collar.anchor == null ? null : collar.anchor.Find("AttachmentRing");
                    rings[d] = ring == null ? null : ring.GetComponent<Renderer>();
                    if (rings[d] == null || !ring.gameObject.activeSelf) { log.AppendLine("   WARN dog " + d + ": AttachmentRing missing or inactive (BUG-DOG-011 R12) — ring not wired"); rings[d] = null; }
                    else if (!ringBaseFound && rings[d].sharedMaterial != null && rings[d].sharedMaterial.HasProperty("_Color")) { ringBase = rings[d].sharedMaterial.color; ringBaseFound = true; }
                }
                ropes[d] = collar.ropeMesh; snaps[d] = collar.snapVisual; handles[d] = collar.handleVisual;
                if (ropes[d] == null || snaps[d] == null || handles[d] == null) throw new InvalidOperationException("dog " + d + ": KH_LeashCollar rope / snap / handle not wired (Walking/Production 2 Apply)");
            }
            var ringColors = new Color[Table.Length];
            for (int i = 0; i < Table.Length; i++) ringColors[i] = i == BrassOption ? BrassRing : ringBase;
            if (!ringBaseFound) log.AppendLine("   WARN no ring material colour found; ringColors use white for the non-brass options");

            // ---- 3 つの器 (KH_Dogs 直下) ----
            var pattern = Ensure<KH_CollarPattern>(dogs, KH_CollarPattern.ObjectName, Networking.SyncType.Manual, log);
            var leash = Ensure<KH_CollarLeash>(dogs, KH_CollarLeash.ObjectName, Networking.SyncType.None, log);
            var tag = Ensure<KH_CollarTag>(dogs, KH_CollarTag.ObjectName, Networking.SyncType.Manual, log);
            if (tag.GetComponent<VRC.SDK3.Components.VRCPlayerObject>() == null) { tag.gameObject.AddComponent<VRC.SDK3.Components.VRCPlayerObject>(); log.AppendLine("added VRCPlayerObject to " + KH_CollarTag.ObjectName); }

            pattern.productKeys = Table.Select(x => x.key).ToArray();
            pattern.items = items; pattern.textures = textures; pattern.ringColors = ringColors;
            pattern.bandRenderers = bands; pattern.ringRenderers = rings;
            KD_ProductsEditor.Copy(pattern); EditorUtility.SetDirty(pattern);
            leash.catalog = catalog; leash.categoryKey = KH_CollarPattern.CategoryKey; leash.defaultProductKey = Table[0].key;
            leash.patterns = pattern; leash.manager = manager;
            leash.ropeRenderers = ropes; leash.snapRenderers = snaps; leash.handleRenderers = handles;
            KD_ProductsEditor.Copy(leash); EditorUtility.SetDirty(leash);
            KD_ProductsEditor.Copy(tag); EditorUtility.SetDirty(tag);

            // ---- カタログの通知先 (ほかの消費者を残して足す。Menu/4 も KH_CollarLeash を残す) ----
            var consumers = (catalog.consumers ?? new UdonSharpBehaviour[0]).ToList();
            var events = (catalog.consumerEvents ?? new string[0]).ToList();
            while (events.Count < consumers.Count) events.Add("");
            if (events.Count > consumers.Count) events.RemoveRange(consumers.Count, events.Count - consumers.Count);
            int at = consumers.IndexOf(leash);
            if (at < 0) { consumers.Add(leash); events.Add(ConsumerEvent); } else events[at] = ConsumerEvent;
            catalog.consumers = consumers.ToArray(); catalog.consumerEvents = events.ToArray();
            KD_ProductsEditor.Copy(catalog);
            log.AppendLine("catalog.consumers " + consumers.Count + " (KH_CollarLeash at " + consumers.IndexOf(leash) + ")");

            // ---- 手元メニュー: かざりの Content に小見出しと 5 行 ----
            var cat = KD_LocalizationEditor.Load();
            BuildRows(p, pattern, cat, log);

            // ---- 文言: 字形 (ja / en) と KD_Language の表 ----
            if (p.language == null) throw new InvalidOperationException("pouch.language (KD_Language) missing");
            KD_ShopSetupEditor.AddGlyphs(string.Concat(cat.languages.Where(KD_LocalizationEditor.Bundled).SelectMany(l => l.texts.Where(t => t.key.StartsWith("collar.")).Select(t => t.text))), log);
            KD_LocalizationEditor.Configure(p.language, cat, log);
            KD_ProductsEditor.Copy(p.language);

            EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
            log.AppendLine("COLLAR_SETUP_OK (scene not saved). Next: Menu/19b Assign Network IDs -> save -> Collar/2 Verify, Menu/4a, Menu/20, Menu/21.");
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/setup.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        static KD_ShopItem[] Items(KD_ShopCatalog catalog)
        {
            var items = new KD_ShopItem[Table.Length];
            for (int i = 0; i < Table.Length; i++)
            {
                var found = (catalog.items ?? new KD_ShopItem[0]).Where(x => x != null && x.productKey == Table[i].key).ToArray();
                if (found.Length != 1) throw new InvalidOperationException("catalog row " + Table[i].key + ": " + found.Length + " (run Menu/4 Setup Shop)");
                var def = KD_ProductDefinitions.Get(Table[i].key);
                if (found[0].categoryKey != KH_CollarPattern.CategoryKey || found[0].price != Table[i].price || def.price != Table[i].price || def.sceneName != Table[i].scene || found[0].IsCE() || found[0].consumable)
                    throw new InvalidOperationException("collar row " + Table[i].key + " differs: item " + found[0].categoryKey + "/" + found[0].price + " / definitions " + def.price + " " + def.sceneName + " / table " + Table[i].price);
                items[i] = found[0];
            }
            if (!items[0].IsFree()) throw new InvalidOperationException("collar [0] must be the free default");
            return items;
        }

        static KH_LeashCollar Collar(KH_LeashManager manager, int d)
        {
            var anchor = manager.collarAnchors[d];
            var collar = anchor == null ? null : anchor.GetComponentInParent<KH_LeashCollar>();
            if (collar == null || collar.dogIndex != d) throw new InvalidOperationException("collar " + d + ": KH_LeashCollar with dogIndex " + d + " required (" + (collar == null ? "none" : "dogIndex " + collar.dogIndex) + ")");
            return collar;
        }

        static void MatchImport(string path, StringBuilder log)
        {
            var t = AssetImporter.GetAtPath(path) as TextureImporter;
            if (t == null) throw new InvalidOperationException("Missing texture " + path + " (run node scripts/art/leash-assets.cjs --patterns, then refresh)");
            bool same = t.textureType == TextureImporterType.Default && t.sRGBTexture && t.alphaSource == TextureImporterAlphaSource.FromInput && !t.alphaIsTransparency
                && t.mipmapEnabled && t.maxTextureSize == 1024 && t.textureCompression == TextureImporterCompression.CompressedHQ
                && t.wrapMode == TextureWrapMode.Repeat && t.filterMode == FilterMode.Trilinear && t.anisoLevel == 4;
            if (same) return;
            // KH_LeashProductionEditor.Import と同じ値 (BaseColor)
            t.textureType = TextureImporterType.Default; t.sRGBTexture = true; t.alphaSource = TextureImporterAlphaSource.FromInput; t.alphaIsTransparency = false;
            t.mipmapEnabled = true; t.maxTextureSize = 1024; t.textureCompression = TextureImporterCompression.CompressedHQ;
            t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4; t.SaveAndReimport();
            log.AppendLine("import settings -> BaseColor's: " + path);
        }

        static T Ensure<T>(Transform dogs, string name, Networking.SyncType sync, StringBuilder log) where T : UdonSharpBehaviour
        {
            var all = KD_ProductsEditor.SceneComponents<T>();
            if (all.Length > 1) throw new InvalidOperationException("More than one " + typeof(T).Name + " (" + all.Length + ")");
            T b = all.Length == 1 ? all[0] : null;
            if (b != null && (b.transform.parent != dogs || b.name != name)) throw new InvalidOperationException(typeof(T).Name + " exists outside " + KD_Hillside.DogsRootName + "/" + name + ": " + KD_SceneObjects.PathOf(b.gameObject));
            if (b == null)
            {
                var t = dogs.Find(name);
                var go = t != null ? t.gameObject : new GameObject(name);
                if (t == null) { Undo.RegisterCreatedObjectUndo(go, "Create " + name); go.transform.SetParent(dogs, false); log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + name); }
                b = go.AddUdonSharpComponent<T>();
            }
            if (b.GetComponents<VRC.Udon.UdonBehaviour>().Length != 1) throw new InvalidOperationException(name + " must hold exactly one UdonBehaviour (sync modes cannot mix)");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (backing == null) throw new InvalidOperationException(name + ": backing UdonBehaviour missing (compile and run again)");
            // vrc-udonsharp §2: AddUdonSharpComponent right after the Program Asset was created can bake Continuous — fix it every run
            if (backing.SyncMethod != sync) { backing.SyncMethod = sync; EditorUtility.SetDirty(backing); log.AppendLine(name + " backing sync -> " + sync); }
            return b;
        }

        /// <summary>かざりの Content の末尾 (FxRow10 の後ろ) に CollarHead0..2 と CollarRow0..4 を置き、pouch へ配線する。既にあれば作り直さず並びと配線だけそろえる。</summary>
        static void BuildRows(KD_TreatPouch p, KH_CollarPattern pattern, KD_LocalizationEditor.Catalog cat, StringBuilder log)
        {
            var scroll = p.tabScrolls != null && p.tabScrolls.Length > KD_TreatPouch.TabAcc ? p.tabScrolls[KD_TreatPouch.TabAcc] : null;
            var content = scroll == null ? null : scroll.content;
            if (content == null || content.GetComponent<GridLayoutGroup>() == null) throw new InvalidOperationException("accessory scroll content (grid) missing");
            var rowTemplate = content.Find(KD_HandMenuScrollEditor.AccFxPrefix + "0");
            var headTemplate = content.Find(KD_TreatSetupEditor.FxHeadPrefix + "0");
            var blankTemplate = content.Find(KD_TreatSetupEditor.FxHeadPrefix + "1");
            if (rowTemplate == null || headTemplate == null || blankTemplate == null || rowTemplate.Find("Label") == null || rowTemplate.Find("Sub") == null || rowTemplate.Find("Button") == null)
                throw new InvalidOperationException("FxRow0 (Label / Sub / Button) and FxHead0..1 required in the accessory content");
            if (PrefabUtility.IsPartOfPrefabInstance(rowTemplate.gameObject)) throw new InvalidOperationException("FxRow0 is part of a prefab instance");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);

            var heads = new GameObject[HeadCount];
            for (int h = 0; h < HeadCount; h++)
            {
                var t = content.Find(HeadPrefix + h);
                if (t == null)
                {
                    var clone = Object.Instantiate((h == 1 ? headTemplate : blankTemplate).gameObject, content);
                    Undo.RegisterCreatedObjectUndo(clone, "Create " + HeadPrefix + h);
                    clone.name = HeadPrefix + h; t = clone.transform;
                    log.AppendLine("created " + HeadPrefix + h);
                }
                foreach (var b in t.GetComponentsInChildren<Button>(true)) Object.DestroyImmediate(b.gameObject);   // 見出しは押せない
                t.SetAsLastSibling(); t.gameObject.SetActive(false);   // 実行時 RedrawCollarRows がカタログ階層で出す
                heads[h] = t.gameObject;
            }
            var headLabelT = heads[1].transform.Find("Label");
            var headLabel = headLabelT == null ? null : headLabelT.GetComponent<TextMeshProUGUI>();
            if (headLabel == null) throw new InvalidOperationException(HeadPrefix + "1/Label missing");
            headLabel.text = KD_LocalizationEditor.Text(cat, "ja", "collar.group"); EditorUtility.SetDirty(headLabel);
            foreach (int h in new[] { 0, 2 }) { var l = heads[h].transform.Find("Label"); if (l != null) Object.DestroyImmediate(l.gameObject); }

            var labels = new TextMeshProUGUI[RowCount]; var subs = new TextMeshProUGUI[RowCount];
            var buttons = new GameObject[RowCount]; var images = new Image[RowCount]; var buttonLabels = new TextMeshProUGUI[RowCount];
            for (int i = 0; i < RowCount; i++)
            {
                var row = content.Find(RowPrefix + i);
                if (row == null)
                {
                    var clone = Object.Instantiate(rowTemplate.gameObject, content);
                    Undo.RegisterCreatedObjectUndo(clone, "Create " + RowPrefix + i);
                    clone.name = RowPrefix + i; row = clone.transform;
                    log.AppendLine("created " + RowPrefix + i);
                }
                row.SetAsLastSibling(); row.gameObject.SetActive(false);   // 実行時 RedrawCollarRows が出す
                labels[i] = row.Find("Label").GetComponent<TextMeshProUGUI>();
                subs[i] = row.Find("Sub").GetComponent<TextMeshProUGUI>();
                var button = row.Find("Button").GetComponent<Button>();
                button.onClick = new Button.ButtonClickedEvent();
                UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, "_Collar" + i);
                button.gameObject.SetActive(true); button.interactable = true;
                labels[i].text = KD_LocalizationEditor.Text(cat, "ja", Table[i].nameKey); subs[i].text = "";
                buttons[i] = button.gameObject; images[i] = button.GetComponent<Image>();
                buttonLabels[i] = button.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                EditorUtility.SetDirty(button); EditorUtility.SetDirty(labels[i]); EditorUtility.SetDirty(subs[i]);
            }
            p.collarPatterns = pattern;
            p.collarHeadRows = heads; p.collarHeadLabel = headLabel;
            p.collarLabels = labels; p.collarSubLabels = subs; p.collarButtons = buttons; p.collarButtonImages = images; p.collarButtonLabels = buttonLabels;
            KD_ProductsEditor.Copy(p); EditorUtility.SetDirty(p);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content);
            log.AppendLine("pouch: " + HeadCount + " heading cells + " + RowCount + " collar rows after " + KD_HandMenuScrollEditor.AccFxPrefix + (KD_TreatSetupEditor.HillsideFxRowCount - 1) + " (wired to _Collar0.." + (RowCount - 1) + ")");
        }

        // ============================================================
        // Collar/2
        // ============================================================

        [MenuItem(Menu + "2 - Verify Collar Pattern")]
        public static void Verify()
        {
            var log = new StringBuilder("[KH Collar] Verify (IMP-DOG-050)\n");
            int pass = 0, fail = 0;
            Func<bool, string, bool> check = (ok, what) => { log.AppendLine((ok ? "PASS " : "FAIL ") + what); if (ok) pass++; else fail++; return ok; };
            try { VerifyInto(check, log); }
            catch (Exception e) { check(false, "exception: " + e.Message); }
            log.AppendLine(fail == 0 ? "COLLAR_OK " + pass : "COLLAR_FAIL " + fail + " / " + (pass + fail));
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/verify.txt", log.ToString());
            if (fail == 0) Debug.Log(log.ToString()); else Debug.LogError(log.ToString());
        }

        static void VerifyInto(Func<bool, string, bool> check, StringBuilder log)
        {
            check(KD_Hillside.InScene() && !EditorApplication.isPlaying, KD_Hillside.ScenePath + " open in Edit mode");
            // ---- Program Assets ----
            var modes = new Dictionary<string, BehaviourSyncMode> { { "KH_CollarPattern", BehaviourSyncMode.Manual }, { "KH_CollarTag", BehaviourSyncMode.Manual }, { "KH_CollarLeash", BehaviourSyncMode.None } };
            foreach (var n in Programs)
            {
                var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ScriptDir + n + ".asset");
                check(pa != null && pa.sourceCsScript == AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptDir + n + ".cs") && pa.behaviourSyncMode == modes[n], n + " program asset -> " + n + ".cs / " + modes[n]);
            }

            // ---- 商品表 (KD_ProductDefinitions) ----
            var cats = KD_ProductDefinitions.Categories; var defs = KD_ProductDefinitions.Defaults;
            int ci = Array.IndexOf(cats, KH_CollarPattern.CategoryKey);
            check(cats.Length == defs.Length && cats.Count(c => c == KH_CollarPattern.CategoryKey) == 1 && ci >= 0 && defs[ci] == Table[0].key,
                "category collar once, Defaults aligned (default " + (ci >= 0 && ci < defs.Length ? defs[ci] : "-") + ") — AI の仮決定: 無料の collar.basic");
            var rows = KD_ProductDefinitions.All.Where(r => r.categoryKey == KH_CollarPattern.CategoryKey).ToArray();
            check(rows.Select(r => r.key).SequenceEqual(Table.Select(t => t.key)), "definitions: the collar rows are exactly the table (" + string.Join(", ", rows.Select(r => r.key + " " + r.price + "pt")) + ")");
            check(rows.All(r => { var t = Table.First(x => x.key == r.key); return r.price == t.price && r.sceneName == t.scene && !r.IsCE && r.packCount == 0; }), "definitions: prices 0 / 30 / 30 / 30 / 50, scene names KD_ShopItem_Collar_*, Point, not consumable");
            check(rows.Where(r => r.price > 0).Sum(r => r.price) == 140, "four sets cost 140 pt in total (IMP-DOG-048)");
            try { KD_ProductDefinitions.Validate(KD_ProductDefinitions.All, cats); check(true, "KD_ProductDefinitions.Validate"); }
            catch (Exception e) { check(false, "KD_ProductDefinitions.Validate: " + e.Message); }

            // ---- 文言 ----
            var cat = KD_LocalizationEditor.Load();
            check(cat.languages.Length == 13, "13 languages in UITranslations.json (" + cat.languages.Length + ")");
            foreach (var k in WordKeys)
            {
                var missing = cat.languages.Where(l => l.texts == null || !l.texts.Any(t => t.key == k && !string.IsNullOrWhiteSpace(t.text))).Select(l => l.code).ToArray();
                check(missing.Length == 0, "word " + k + " in every language" + (missing.Length > 0 ? " (missing " + string.Join(",", missing) + ")" : ""));
            }
            string bundled = string.Concat(cat.languages.Where(KD_LocalizationEditor.Bundled).SelectMany(l => l.texts.Where(t => t.key.StartsWith("collar.")).Select(t => t.text)));
            string lacking = KD_ShopSetupEditor.MissingGlyphs(bundled);
            check(lacking.Length == 0, "ja / en glyphs of the collar words in the shared font" + (lacking.Length > 0 ? " (missing \"" + lacking + "\" — run Collar/1)" : ""));
            log.AppendLine("NOTE the 13-language words are an AI draft (unproofread)");

            // ---- カタログ ----
            var catalogs = KD_ProductsEditor.SceneComponents<KD_ShopCatalog>();
            if (!check(catalogs.Length == 1, "one KD_ShopCatalog")) return;
            var catalog = catalogs[0];
            check(catalog.categoryKeys != null && catalog.categoryKeys.SequenceEqual(cats) && catalog.defaultProducts != null && catalog.defaultProducts.SequenceEqual(defs), "catalog.categoryKeys / defaultProducts == the table (Menu/4 ran after the change)");
            KD_ShopItem[] items = null;
            try { items = Items(catalog); check(true, "catalog has the five collar rows with the table's category / price"); }
            catch (Exception e) { check(false, e.Message); }
            if (items != null)
                for (int i = 0; i < Table.Length; i++)
                    check(items[i].displayNameKey == Table[i].nameKey && items[i].IsStarter() == (i == 0), Table[i].key + ": name key " + items[i].displayNameKey + ", starter " + items[i].IsStarter());

            // ---- 器 ----
            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int nDogs);
            var patterns = KD_ProductsEditor.SceneComponents<KH_CollarPattern>();
            var leashes = KD_ProductsEditor.SceneComponents<KH_CollarLeash>();
            var tags = KD_ProductsEditor.SceneComponents<KH_CollarTag>();
            if (!check(nDogs == 1 && patterns.Length == 1 && leashes.Length == 1 && tags.Length == 1, "one each of KH_CollarPattern / KH_CollarLeash / KH_CollarTag (" + patterns.Length + "/" + leashes.Length + "/" + tags.Length + ")")) return;
            var pattern = patterns[0]; var leash = leashes[0]; var tag = tags[0];
            foreach (var (b, name, sync) in new (UdonSharpBehaviour, string, Networking.SyncType)[] { (pattern, KH_CollarPattern.ObjectName, Networking.SyncType.Manual), (leash, KH_CollarLeash.ObjectName, Networking.SyncType.None), (tag, KH_CollarTag.ObjectName, Networking.SyncType.Manual) })
            {
                var ubs = b.GetComponents<VRC.Udon.UdonBehaviour>();
                check(b.transform.parent == dogsGo.transform && b.name == name && b.gameObject.activeInHierarchy && b.enabled, KD_Hillside.DogsRootName + "/" + name + " active");
                check(ubs.Length == 1 && ubs[0].SyncMethod == sync && UdonSharpEditorUtility.GetBackingUdonBehaviour(b) == ubs[0], name + ": one UdonBehaviour, SyncMethod " + sync + " (actual backing)");
                check(b.GetComponent<VRC.SDK3.Components.VRCObjectSync>() == null, name + ": no VRCObjectSync");
            }
            check(tag.GetComponent<VRC.SDK3.Components.VRCPlayerObject>() != null, "KH_CollarTag has VRCPlayerObject (cloned per player)");
            check(SyncedFields(typeof(KH_CollarPattern)) == "Int32[] _changedMs,Int32[] _changerId,Int32[] _pattern", "KH_CollarPattern synced: int[] _pattern, _changerId, _changedMs (" + SyncedFields(typeof(KH_CollarPattern)) + ")");
            check(SyncedFields(typeof(KH_CollarTag)) == "String _selected", "KH_CollarTag synced: string _selected only");
            check(SyncedFields(typeof(KH_CollarLeash)) == "", "KH_CollarLeash has no synced variable");
            string src = File.Exists(ScriptDir + "KH_CollarPattern.cs") ? File.ReadAllText(ScriptDir + "KH_CollarPattern.cs") : "";
            check(src.Contains("OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) { return false; }") && !src.Contains("SetOwner(") && !src.Contains(".isMaster")
                && src.Contains("NetworkCalling.CallingPlayer") && src.Contains("OwnedBy(player)") && src.Contains("RemainingFor(dog, player.playerId)"),
                "KH_CollarPattern: Owner writes after CallingPlayer / OwnedBy / 60 s checks, nobody takes ownership — source check");

            // ---- KH_CollarPattern の表と配線 ----
            var textures = Table.Select(t => AssetDatabase.LoadAssetAtPath<Texture2D>(t.texture)).ToArray();
            check(pattern.productKeys != null && pattern.productKeys.SequenceEqual(Table.Select(t => t.key)), "pattern.productKeys = table");
            check(items != null && pattern.items != null && pattern.items.SequenceEqual(items), "pattern.items = the catalog rows");
            check(pattern.textures != null && pattern.textures.Length == Table.Length && Enumerable.Range(0, Table.Length).All(i => textures[i] != null && pattern.textures[i] == textures[i]), "pattern.textures = [base, stripe, check, momiji, leather & brass] (provisional PNGs)");
            for (int i = 1; i < Table.Length; i++)
            {
                var t = AssetImporter.GetAtPath(Table[i].texture) as TextureImporter;
                check(t != null && t.sRGBTexture && t.mipmapEnabled && t.maxTextureSize == 1024 && t.textureCompression == TextureImporterCompression.CompressedHQ && textures[i] != null && textures[i].width == 1024 && textures[i].height == 1024,
                    Path.GetFileName(Table[i].texture) + ": 1024², sRGB, mips, max 1024, CompressedHQ (same as KH_Leash_BaseColor)");
            }
            check(pattern.ringColors != null && pattern.ringColors.Length == Table.Length && pattern.ringColors[BrassOption] == BrassRing
                && Enumerable.Range(0, Table.Length).Where(i => i != BrassOption).All(i => pattern.ringColors[i] == pattern.ringColors[0]), "ringColors: the ring material's colour, brass for the leather & brass set");
            var manager = Object.FindObjectOfType<KH_LeashManager>();
            var shared = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            bool wired = manager != null && manager.collarAnchors != null && manager.collarAnchors.Length == 6
                && pattern.bandRenderers != null && pattern.bandRenderers.Length == 6 && pattern.ringRenderers != null && pattern.ringRenderers.Length == 6
                && leash.ropeRenderers != null && leash.ropeRenderers.Length == 6 && leash.snapRenderers != null && leash.snapRenderers.Length == 6 && leash.handleRenderers != null && leash.handleRenderers.Length == 6;
            if (check(wired, "six dogs: band / ring / rope / snap / handle arrays of 6"))
                for (int d = 0; d < 6; d++)
                {
                    KH_LeashCollar collar = null;
                    try { collar = Collar(manager, d); } catch (Exception e) { check(false, e.Message); continue; }
                    var band = collar.transform.Find("Band");
                    check(band != null && pattern.bandRenderers[d] == band.GetComponent<MeshRenderer>() && pattern.bandRenderers[d].sharedMaterial == shared, "dog " + d + ": band = " + KD_SceneObjects.PathOf(collar.gameObject) + "/Band (shared KH_Leash_WovenMetal)");
                    var ring = collar.anchor == null ? null : collar.anchor.Find("AttachmentRing");
                    if (BuriedInFur[d]) check(ring != null && ring.gameObject.activeSelf && pattern.ringRenderers[d] == ring.GetComponent<Renderer>(), "dog " + d + " (fur-buried): ring = ClipAnchor/AttachmentRing (active)");
                    else check(pattern.ringRenderers[d] == null, "dog " + d + ": no ring (the band's own hardware takes the texture)");
                    check(leash.ropeRenderers[d] == collar.ropeMesh && leash.snapRenderers[d] == collar.snapVisual && leash.handleRenderers[d] == collar.handleVisual && collar.ropeMesh != null,
                        "dog " + d + ": leash rope / snap / handle = KH_LeashCollar's");
                }
            check(leash.catalog == catalog && leash.patterns == pattern && leash.manager == manager && leash.categoryKey == KH_CollarPattern.CategoryKey && leash.defaultProductKey == Table[0].key,
                "KH_CollarLeash: catalog / patterns / manager / category collar / default collar.basic");
            int at = catalog.consumers == null ? -1 : Array.IndexOf(catalog.consumers, leash);
            check(at >= 0 && catalog.consumerEvents != null && catalog.consumerEvents.Length == catalog.consumers.Length && catalog.consumerEvents[at] == ConsumerEvent,
                "catalog notifies KH_CollarLeash._OnShopSelectionChanged (kept by Menu/4)");

            // ---- 手元メニュー ----
            var p = KH_CourseTravelEditor.FindPouch();
            check(p.collarPatterns == pattern, "pouch.collarPatterns wired");
            var content = p.tabScrolls != null && p.tabScrolls.Length > KD_TreatPouch.TabAcc && p.tabScrolls[KD_TreatPouch.TabAcc] != null ? p.tabScrolls[KD_TreatPouch.TabAcc].content : null;
            if (check(content != null, "accessory scroll content"))
            {
                var want = new List<string>();
                foreach (var (prefix, count) in KD_HandMenuScrollEditor.RowsFor(KD_TreatPouch.TabAcc)) for (int i = 0; i < count; i++) want.Add(prefix + i);
                var got = new List<string>(); foreach (Transform row in content) got.Add(row.name);
                check(want.SequenceEqual(got), "content order AccRow0..15, FxHead0..1, FxRow0..10, CollarHead0..2, CollarRow0..4 (" + got.Count + "/" + want.Count + ")");
            }
            check(p.collarHeadRows != null && p.collarHeadRows.Length == HeadCount && Enumerable.Range(0, HeadCount).All(h => p.collarHeadRows[h] != null && p.collarHeadRows[h].name == HeadPrefix + h && p.collarHeadRows[h].transform.parent == content)
                && p.collarHeadLabel != null && p.collarHeadLabel.transform.parent == p.collarHeadRows[1].transform, "heading cells CollarHead0..2 wired, label under CollarHead1");
            var pouchBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            bool arrays = p.collarLabels != null && p.collarLabels.Length == RowCount && p.collarSubLabels != null && p.collarSubLabels.Length == RowCount
                && p.collarButtons != null && p.collarButtons.Length == RowCount && p.collarButtonImages != null && p.collarButtonImages.Length == RowCount && p.collarButtonLabels != null && p.collarButtonLabels.Length == RowCount;
            if (check(arrays, "collar row arrays of " + RowCount))
                for (int i = 0; i < RowCount; i++)
                {
                    var btn = p.collarButtons[i] == null ? null : p.collarButtons[i].GetComponent<Button>();
                    string evt = null;
                    if (btn != null && btn.onClick.GetPersistentEventCount() == 1)
                    {
                        var calls = new SerializedObject(btn).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                        evt = calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
                    }
                    check(p.collarLabels[i] != null && p.collarLabels[i].transform.parent.name == RowPrefix + i && p.collarSubLabels[i] != null && p.collarSubLabels[i].transform.parent.name == RowPrefix + i
                        && btn != null && btn.onClick.GetPersistentTarget(0) == pouchBacking && evt == "_Collar" + i && typeof(KD_TreatPouch).GetMethod("_Collar" + i) != null,
                        RowPrefix + i + ": label / sub / [えらぶ・買う] -> _Collar" + i);
                }
            // 2 列の格子: 一覧の面で見出しが段の左端に来るか (実行時は前のセルが奇数のとき CollarHead0 の空きを出す)
            var acc = p.accessories;
            int accCount = p.accProductIds == null ? 0 : p.accProductIds.Count(x => acc == null || acc.setMembers == null || !acc.setMembers.Contains(x));
            int fxCount = p.fxItems == null ? 0 : p.fxItems.Take(p.fxLabels == null ? 0 : p.fxLabels.Length).Count(x => x != null);
            int before = accCount + 2 + fxCount;
            log.AppendLine("NOTE catalog-level cells before the heading: accessories " + accCount + " + petting heading 2 + petting rows " + fxCount + " = " + before
                + (before % 2 == 1 ? " (odd: the pad cell CollarHead0 is shown)" : " (even: no pad cell)"));
            check(p.collarHeadRows != null && p.collarHeadRows.Length == HeadCount, "pad cell available for the grid parity (" + (before % 2 == 1 ? "used" : "unused") + ")");

            // ---- NetworkID (Menu/19b) ----
            var desc = Object.FindObjectOfType<VRC_SceneDescriptor>();
            check(desc != null && desc.NetworkIDCollection.Any(x => x.gameObject == pattern.gameObject) && desc.NetworkIDCollection.Any(x => x.gameObject == tag.gameObject),
                "KH_CollarPattern and KH_CollarTag have NetworkIDs (Menu/19b)");
            check(desc != null && desc.NetworkIDCollection.Count(x => x.gameObject == null) == 0, "no dangling NetworkIDs");
            log.AppendLine("NOTE not checked here (Play / VR / 2 players): the pattern drawn on bands and ropes, the 60 s rule between players, late joiners, the leash on re-entry (PlayerData), Quest / iOS");
        }

        static string SyncedFields(Type t)
        {
            return string.Join(",", t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(f => f.GetCustomAttributes(typeof(UdonSyncedAttribute), true).Length > 0).Select(f => f.FieldType.Name + " " + f.Name).OrderBy(x => x, StringComparer.Ordinal));   // sorted: reflection order is not guaranteed
        }
    }
}
