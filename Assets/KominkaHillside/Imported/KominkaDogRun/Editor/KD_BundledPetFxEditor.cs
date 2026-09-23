using System;
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
    /// <summary>同梱FXの商品と既存パーティクル一覧だけを拡張する。配置・他タブは保持。</summary>
    public static class KD_BundledPetFxEditor
    {
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/10 - Connect Bundled Catalog")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/KominkaDogRun.unity")
                throw new InvalidOperationException("Target Edit scene required");
            var p = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            var scroll = p.tabScrolls[KD_TreatPouch.TabPetFx];
            var content = scroll.content;
            var template = content.Find("FxRow5");
            var first = KD_SceneObjects.Find(KD_ProductDefinitions.SceneName("petfx.heart")).GetComponent<KD_ShopItem>();
            if (template == null || PrefabUtility.IsPartOfPrefabInstance(template.gameObject)
                || content.GetComponent<GridLayoutGroup>() == null || p.catalog == null || p.language == null)
                throw new InvalidOperationException("Existing custom particle grid, catalog and language required");
            var log = new StringBuilder();
            KD_ProductDefinitions.EnsureCeAssets(log);
            KD_PetHeartsSetupEditor.GetShopEntries(out var keys, out var labels, out _, out _, out _);
            var items = new KD_ShopItem[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                string name = KD_ProductDefinitions.SceneName(keys[i]);
                var go = KD_SceneObjects.Find(name, out int count);
                if (count > 1) throw new InvalidOperationException("Duplicate product: " + name);
                if (go == null)
                {
                    go = new GameObject(name); go.transform.SetParent(first.transform.parent, false);
                    go.AddUdonSharpComponent<KD_ShopItem>();
                }
                var item = go.GetComponent<KD_ShopItem>();
                if (item == null || go.GetComponent<Collider>() != null)
                    throw new InvalidOperationException("Invalid menu-only item: " + name);
                KD_ProductDefinitions.Apply(item, keys[i]);
                item.ledger = p.catalog.ledger; item.menuOnly = true; item.shelf = null; item.displayName = labels[i];
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(item);
                backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
                KD_ProductsEditor.Copy(item); items[i] = item;
            }
            KD_ProductsEditor.BindCatalog(p.catalog, p.catalog.ledger);
            p.fxItems = items;
            p.fxLabels = new TextMeshProUGUI[keys.Length]; p.fxSubLabels = new TextMeshProUGUI[keys.Length];
            p.fxButtons = new GameObject[keys.Length]; p.fxButtonImages = new Image[keys.Length];
            p.fxButtonLabels = new TextMeshProUGUI[keys.Length];
            var target = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            for (int i = 0; i < keys.Length; i++)
            {
                var row = content.Find("FxRow" + i);
                if (row == null)
                {
                    var clone = Object.Instantiate(template.gameObject, content);
                    clone.name = "FxRow" + i; row = clone.transform;
                }
                row.SetAsLastSibling(); row.gameObject.SetActive(true);
                var label = row.Find("Label").GetComponent<TextMeshProUGUI>();
                var sub = row.Find("Sub");
                if (sub == null)
                {
                    sub = Object.Instantiate(label.gameObject, row).transform; sub.name = "Sub";
                    var text = sub.GetComponent<TextMeshProUGUI>();
                    text.rectTransform.anchoredPosition = new Vector2(-76, -18);
                    text.rectTransform.sizeDelta = new Vector2(328, 60); text.fontSize = 18;
                    text.enableAutoSizing = false; text.enableWordWrapping = true;
                }
                var button = row.Find("Button").GetComponent<Button>();
                button.onClick = new Button.ButtonClickedEvent();
                UnityEventTools.AddStringPersistentListener(button.onClick, target.SendCustomEvent, "_Fx" + i);
                button.gameObject.SetActive(true);
                button.interactable = true;
                label.text = labels[i]; sub.GetComponent<TextMeshProUGUI>().text = "";
                p.fxLabels[i] = label; p.fxSubLabels[i] = sub.GetComponent<TextMeshProUGUI>();
                p.fxButtons[i] = button.gameObject; p.fxButtonImages[i] = button.GetComponent<Image>();
                p.fxButtonLabels[i] = button.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                EditorUtility.SetDirty(button);
            }
            var translations = KD_LocalizationEditor.Load();
            var font = p.sectionTitleLabel.font;
            string characters = KD_LocalizationEditor.BundledCharacters(translations);
            if (KD_ShopSetupEditor.MissingGlyphs(characters).Length > 0)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/KominkaHillside/Font/Noto_Serif_JP/static/NotoSerifJP-Regular.ttf");
                if (source == null) throw new InvalidOperationException("Source font missing");
                var so = new SerializedObject(font);
                so.FindProperty("m_SourceFontFile").objectReferenceValue = source;
                so.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue = source;
                so.ApplyModifiedPropertiesWithoutUndo(); font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                KD_ShopSetupEditor.AddGlyphs(characters, log);
                if (KD_ShopSetupEditor.MissingGlyphs(characters).Length > 0) throw new InvalidOperationException("Missing declared glyphs");
            }
            KD_LocalizationEditor.Configure(p.language, translations, log);
            KD_LocalizationEditor.Bind(p.language, translations, log);
            KD_ProductsEditor.Copy(p.language); KD_ProductsEditor.Copy(p);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.verticalNormalizedPosition = 1;
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
            log.AppendLine("Connected " + keys.Length + " particle rows. Other tabs and panel placement preserved.");
            File.WriteAllText("Logs/bundled-petfx-catalog.txt", log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/11 - Verify Bundled Catalog")]
        public static void Verify()
        {
            var v = new KD_VerifyLog("Bundled particle catalog");
            var p = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            var content = p.tabScrolls[KD_TreatPouch.TabPetFx].content;
            KD_PetHeartsSetupEditor.GetShopEntries(out var keys, out _, out _, out _, out _);
            v.Check(keys.Length == 11 && p.fxItems.Length == 11 && p.fxSubLabels.Length == 11 && content.childCount == 11, "11 particle products and rows");
            for (int i = 0; i < keys.Length; i++)
            {
                var item = p.fxItems[i];
                v.Check(item != null && item.productKey == keys[i], "ordered product " + keys[i]);
                var row = content.Find("FxRow" + i);
                v.Check(row != null && p.fxSubLabels[i].transform.parent == row, "independent bundle label " + i);
                if (i < 6) continue;
                string parentKey = i == 6 ? "live.candy_step_set" : i == 7 ? "live.pumpkin_parade_set" : "live.kominka_pack";
                var parent = p.catalog.items.Single(x => x.productKey == parentKey);
                v.Check(item.IsCE() && item.price == 0 && item.ceProduct == parent.ceProduct && item.ceListingId == parent.ceListingId
                    && item.ceCredits == parent.ceCredits, keys[i] + " shares set ownership/listing/credits; no Point price");
                v.Check(item.menuOnly && item.GetComponent<Collider>() == null, keys[i] + " menu-only purchase");
                v.Check(!string.IsNullOrEmpty(item.displayNameKey), keys[i] + " localized name");
            }
            v.Check(p.fxConfirmRoot != null && !p.fxConfirmRoot.transform.IsChildOf(content), "confirmation stays outside scroll");
            v.Finish("Logs/bundled-petfx-verify.txt");
            if (v.Fail > 0) throw new InvalidOperationException("Bundled FX verification failed: " + v.Fail);
        }
    }
}
