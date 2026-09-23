using System;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    // Product wiring only: existing pose miniatures, Animator clips and panel placement stay intact.
    public static class KD_ActionProductsEditor
    {
        static readonly string[] Keys = { "action.belly_up", "action.chin_rest", "action.give_paw", "action.stretch_yawn" };
        static readonly string[] Names = { "へそ天", "あご乗せ", "お手", "伸び・あくび" };

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Call Poses/6 - Connect Point Products")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/KominkaDogRun.unity")
                throw new InvalidOperationException("Stopped KominkaDogRun scene required");
            var c = KD_ProductsEditor.SceneComponents<KD_CallPose>().Single();
            var p = c.pouch;
            if (c.panel == null || c.board == null || p == null || p.catalog == null || c.poseButtons.Length != 5)
                throw new InvalidOperationException("Existing call pose menu required");
            var slots = Enumerable.Range(0, 6).Select(i => c.board.SlotOf(i)).ToArray();
            if (slots.Any(s => s == null)) throw new InvalidOperationException("Six existing slots required");
            foreach (string key in Keys)
            {
                var go = KD_SceneObjects.Find(KD_ProductDefinitions.SceneName(key), out int count);
                if (count > 1 || (go != null && (go.GetComponent<KD_ShopItem>() == null || go.GetComponent<Collider>() != null)))
                    throw new InvalidOperationException("Invalid action product: " + key);
            }
            var log = new StringBuilder();
            var translations = KD_LocalizationEditor.Load();
            var font = c.title.font;
            string characters = KD_LocalizationEditor.BundledCharacters(translations);
            if (KD_ShopSetupEditor.MissingGlyphs(characters).Length > 0)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/KominkaHillside/Font/Noto_Serif_JP/static/NotoSerifJP-Regular.ttf");
                if (source == null) throw new InvalidOperationException("Font source required");
                var so = new SerializedObject(font);
                so.FindProperty("m_SourceFontFile").objectReferenceValue = source;
                so.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue = source;
                so.ApplyModifiedPropertiesWithoutUndo(); font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                KD_ShopSetupEditor.AddGlyphs(characters, log);
                if (KD_ShopSetupEditor.MissingGlyphs(characters).Length > 0) throw new InvalidOperationException("Missing action glyphs");
            }
            c.actionProducts = new KD_ShopItem[4];
            for (int i = 0; i < Keys.Length; i++)
            {
                var go = KD_SceneObjects.Find(KD_ProductDefinitions.SceneName(Keys[i]));
                if (go == null)
                {
                    go = new GameObject(KD_ProductDefinitions.SceneName(Keys[i]));
                    go.transform.SetParent(c.transform, false); go.AddUdonSharpComponent<KD_ShopItem>();
                }
                var item = go.GetComponent<KD_ShopItem>();
                KD_ProductDefinitions.Apply(item, Keys[i]); item.displayName = Names[i]; item.displayNameKey = Keys[i];
                item.menuOnly = true; item.shelf = null; item.ledger = p.catalog.ledger;
                UdonSharpEditorUtility.GetBackingUdonBehaviour(item).SyncMethod = VRC.SDKBase.Networking.SyncType.None;
                c.actionProducts[i] = item; KD_ProductsEditor.Copy(item);
            }
            foreach (var slot in slots) { slot.actionProducts = c.actionProducts; KD_ProductsEditor.Copy(slot); }
            KD_ProductsEditor.BindCatalog(p.catalog, p.catalog.ledger);
            var target = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            // P13 (2026-09-16): 追加動作の確認も板の手前の共有カード (KD_TreatPouch.confirmRoot)。旧 Purchase パネルは消す
            var legacyPurchase = c.panel.transform.Find("Purchase");
            if (legacyPurchase != null) { UnityEngine.Object.DestroyImmediate(legacyPurchase.gameObject); log.AppendLine("removed CallPosePanel/Purchase (P13: 確認は共有カードへ)"); }
            if (p.confirmRoot == null) throw new InvalidOperationException("Shared confirm card missing — run Treat System/2 - Setup (P13) first");
            c.purchaseControls = new[] { c.placeButton.gameObject, c.applyButton.gameObject, c.releaseButton.gameObject,
                c.pawButton.gameObject, c.stretchButton.gameObject, c.panel.transform.Find("Back").gameObject, c.status.gameObject };
            KD_LocalizationEditor.Configure(p.language, translations, log);
            KD_LocalizationEditor.Bind(p.language, translations, log);
            KD_ProductsEditor.Copy(p.language); KD_ProductsEditor.Copy(c);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Call Poses/7 - Verify Point Products")]
        public static void Verify()
        {
            var v = new KD_VerifyLog("Call action products");
            var c = KD_ProductsEditor.SceneComponents<KD_CallPose>().Single();
            v.Check(c.actionProducts != null && c.actionProducts.Length == 4, "four shared products");
            for (int i = 0; i < Keys.Length; i++)
            {
                var item = c.actionProducts[i];
                v.Check(item != null && item.productKey == Keys[i] && item.price == 30 && !item.IsCE() && !item.IsStocked(), Keys[i] + " permanent 30pt");
                v.Check(KD_ProductsEditor.HasMenuEntry(item), Keys[i] + " call menu purchase entry");
                for (int d = 0; d < 6; d++)
                {
                    var slot = c.board.SlotOf(d);
                    var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(slot);
                    var wired = backing.publicVariables.TryGetVariableValue("actionProducts", out object raw);
                    var values = raw as UnityEngine.Object[];
                    v.Check(slot.actionProducts.Length == 4 && slot.actionProducts[i] == item && wired && values != null && values.Length == 4
                        && values[i] == UdonSharpEditorUtility.GetBackingUdonBehaviour(item), Keys[i] + " dog " + d + " proxy/backing entitlement (" + (raw == null ? "null" : raw.GetType().Name) + ")");
                }
            }
            var point = KD_ProductDefinitions.All.Where(r => !r.IsCE && r.packCount == 0 && r.price > 0).ToArray();
            v.Check(point.Length == 28 && point.Sum(r => r.price) == 830, "28 Point unlocks total 830pt");
            v.Check(c.pouch != null && c.pouch.confirmRoot != null && c.panel.transform.Find("Purchase") == null && c.purchaseControls.Length == 7, "confirmation on the shared card (P13); legacy Purchase panel absent");
            v.Finish("Logs/action-products-verify.txt");
            if (v.Fail > 0) throw new InvalidOperationException("Action product verification failed: " + v.Fail);
        }
    }
}
