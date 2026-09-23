using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    public static class KD_AccessorySetChecksEditor
    {
        const string Dir = "Temp/accessory-sets-integration";
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static void Field(object target, string name, object value) { target.GetType().GetField(name, Private).SetValue(target, value); }
        static object Call(object target, string name, params object[] args) { return target.GetType().GetMethod(name, Private).Invoke(target, args); }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Sets - Check Menu And Render")]
        public static void CheckMenu()
        {
            Directory.CreateDirectory(Dir); var log = new StringBuilder(); int pass = 0, fail = 0;
            void Check(bool value, string name) { log.AppendLine((value ? "PASS " : "FAIL ") + name); if (value) pass++; else fail++; }
            var source = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            for (int s = 0; s < source.accessories.setIds.Length; s++)
                for (int member = 0; member < 3; member++)
                {
                    int product = source.accessories.setMembers[s * 3 + member];
                    int choice = Array.IndexOf(source.accProductIds, product);
                    Check(choice >= 0 && ReferenceEquals(source.accItems[choice], source.accessories.setItems[s]),
                        "real scene member uses identical set product " + product);
                }
            var scene = EditorSceneManager.NewPreviewScene(); var prior = RenderTexture.active;
            var rt = new RenderTexture(1368, 840, 24); var image = new Texture2D(1368, 840, TextureFormat.RGB24, false);
            try
            {
                var panel = Object.Instantiate(source.panelRoot); panel.SetActive(false); SceneManager.MoveGameObjectToScene(panel, scene);
                foreach (var c in panel.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true)) Object.DestroyImmediate(c);
                foreach (var c in panel.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true)) Object.DestroyImmediate(c);
                foreach (var t in panel.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                panel.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); panel.transform.localScale = Vector3.one * .001f;
                panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                Transform Map(Transform t) { return panel.transform.Find(AnimationUtility.CalculateTransformPath(t, source.panelRoot.transform)); }
                var fixture = new GameObject("AccessoryFixture"); SceneManager.MoveGameObjectToScene(fixture, scene);
                var p = fixture.AddComponent<KD_TreatPouch>(); p.panelRoot = panel;
                p.language = fixture.AddComponent<KD_Language>(); p.toyNameKeys = source.toyNameKeys;
                p.language.keys = source.language.keys; p.language.texts = source.language.texts;
                p.language.languageCodes = source.language.languageCodes;
                Field(p.language, "_initialized", true); Field(p.language, "_languageIndex", p.language.ResolveLanguage("ja"));
                p.tabPages = source.tabPages.Select(g => Map(g.transform).gameObject).ToArray();
                p.tabScrolls = source.tabScrolls.Select(s => Map(s.transform).GetComponent<ScrollRect>()).ToArray();
                p.accRows = source.accRows.Select(g => Map(g.transform).gameObject).ToArray();
                p.accRowLabels = source.accRowLabels.Select(c => Map(c.transform).GetComponent<TextMeshProUGUI>()).ToArray();
                p.accRowSubLabels = source.accRowSubLabels.Select(c => Map(c.transform).GetComponent<TextMeshProUGUI>()).ToArray();
                p.accButtons = source.accButtons.Select(g => Map(g.transform).gameObject).ToArray();
                p.accButtonComponents = p.accButtons.Select(g => g.GetComponent<Button>()).ToArray();
                p.accButtonLabels = source.accButtonLabels.Select(c => Map(c.transform).GetComponent<TextMeshProUGUI>()).ToArray();
                p.accPreviewDogs = source.accPreviewDogs.Select(g => Map(g.transform).gameObject).ToArray();
                p.accPreviewItems = source.accPreviewItems.Select(g => Map(g.transform).gameObject).ToArray();
                p.accConfirmRoot = Map(source.accConfirmRoot.transform).gameObject;
                p.accConfirmLabel = Map(source.accConfirmLabel.transform).GetComponent<TextMeshProUGUI>();
                p.accConfirmYes = Map(source.accConfirmYes.transform).gameObject;
                p.accBackButton = Map(source.accBackButton.transform).gameObject;
                p.accHeaderLabel = Map(source.accHeaderLabel.transform).GetComponent<TextMeshProUGUI>();
                p.accProductIds = source.accProductIds; p.accNameKeys = source.accNameKeys; p.accSlotNameKeys = source.accSlotNameKeys;
                p.accessories = fixture.AddComponent<KD_AccessoryBox>(); var box = p.accessories;
                box.productIds = source.accessories.productIds; box.productSlots = source.accessories.productSlots;
                box.setIds = source.accessories.setIds; box.setMembers = source.accessories.setMembers;
                box.dogCount = 6; Call(box, "EnsureArrays");
                var ledger = fixture.AddComponent<KD_ProgressLedger>(); ledger.storageNamespace = KD_ProgressLedger.TestNamespace; Field(ledger, "_loaded", true); Field(ledger, "_earned", 200);
                p.accItems = source.accItems.Select(item =>
                {
                    var existing = fixture.GetComponents<KD_ShopItem>().FirstOrDefault(i => i.productKey == "test." + item.productKey);
                    if (existing != null) return existing;
                    var copy = fixture.AddComponent<KD_ShopItem>(); copy.ledger = ledger; copy.price = item.price; copy.productKey = "test." + item.productKey; copy.categoryKey = item.categoryKey;
                    copy.displayNameKey = item.displayNameKey; return copy;
                }).ToArray();
                ledger._ConfigureCatalog(fixture.GetComponents<KD_ShopItem>());
                Field(ledger,"_earnedValid",true); Field(ledger,"_spentValid",true); Field(ledger,"_ownValid",Enumerable.Repeat(true,ledger.products.Length).ToArray());
                void Own(string key) { Field(ledger,"_owned",ledger.products.Select(i=>i.productKey==key).ToArray()); }
                box.setItems = source.accessories.setItems.Select(item => p.accItems.First(i => i.productKey == "test." + item.productKey)).ToArray();
                Field(p, "_tab", KD_TreatPouch.TabAcc);
                panel.SetActive(true);
                for (int tab = 0; tab < p.tabPages.Length; tab++) p.tabPages[tab].SetActive(tab == KD_TreatPouch.TabAcc);
                if (source.settingsPage != null) Map(source.settingsPage.transform).gameObject.SetActive(false);
                var camGo = new GameObject("Camera"); SceneManager.MoveGameObjectToScene(camGo, scene);
                var camera = camGo.AddComponent<Camera>(); camera.scene = scene; camera.orthographic = true; camera.orthographicSize = .35f;
                camera.aspect = 1368f / 840; camera.transform.position = new Vector3(0, 0, -2); camera.targetTexture = rt;
                camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.14f, .16f, .18f);
                void Render(string name)
                {
                    for (int i = 0; i < source.language.fixedLabels.Length; i++)
                    {
                        var label = source.language.fixedLabels[i];
                        if (label != null && label.transform.IsChildOf(source.panelRoot.transform))
                            Map(label.transform).GetComponent<TMP_Text>().text = p.language._Text(source.language.fixedKeys[i]);
                    }
                    Call(p, "RedrawAcc"); // Runtime Apply refreshes dynamic rows after fixed labels too.
                    Canvas.ForceUpdateCanvases(); foreach (var text in panel.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                    foreach (var mask in panel.GetComponentsInChildren<RectMask2D>()) mask.PerformClipping();
                    camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
                    File.WriteAllBytes(Dir + "/menu-" + name + ".png", image.EncodeToPNG());
                }
                void Root()
                {
                    Field(p, "_accProduct", -1); Field(p, "_accSet", -1); Field(p, "_accSetMenu", 0); Field(p, "_accConfirm", false);
                    Call(p, "RedrawAcc");
                }
                int ActiveRows() { return p.accRows.Count(r => r.activeSelf); }
                int PreviewCount() { return p.accPreviewItems.Count(g => g.activeInHierarchy); }
                Root(); Render("list");
                Check(ActiveRows() == 12, "catalog has three sets and nine standalone goods");
                for (int row = 0; row < 12; row++)
                    Check((int)Call(p, "AccCatalogRow", row) == (row < 7 ? row : row + 9), "catalog entry " + row);
                p._Acc12(); Check((int)typeof(KD_TreatPouch).GetField("_accProduct", Private).GetValue(p) == -1, "hidden member row cannot be opened from catalog");
                for (int set = 0; set < 3; set++)
                {
                    Own(""); Field(ledger, "_earned", 200); Root();
                    Call(p, "AccRow", set); Check(PreviewCount() == 18 && ActiveRows() == 6, "unowned set " + set + " previews all three on six dogs");
                    p._Acc2(); Check(p.accConfirmRoot.activeSelf && p.accConfirmYes.activeSelf, "set " + set + " opens affordable purchase confirmation");
                    Check(PreviewCount() == 18, "purchase retains full set preview " + set);
                    Check(p.accButtonComponents.Take(6).All(b => !b.interactable), "purchase confirmation blocks background dog buttons " + set);
                    Render(set == 0 ? "royal-purchase" : set == 1 ? "summer-purchase" : "floral-purchase");
                    p._AccConfirmNo(); Check(!p.accConfirmRoot.activeSelf && PreviewCount() == 18, "cancel retains unowned preview " + set);
                    p._Acc2(); Field(ledger, "_earned", 0); Call(p, "RedrawAcc"); Check(!p.accConfirmYes.activeSelf, "insufficient balance disables purchase " + set);
                    Field(ledger, "_loaded", false); Call(p, "RedrawAcc"); Check(!p.accConfirmYes.activeSelf, "loading disables purchase " + set);
                    Field(ledger, "_loaded", true); Own(box.setItems[set].productKey); Call(p, "RedrawAcc");
                    Check(!p.accConfirmRoot.activeSelf && ActiveRows() == 2 && PreviewCount() == 0, "ownership opens equip mode without equipping " + set);
                    Check(p.accRowLabels[0].text == p.language._Text("accessory.chooseSet") && p.accRowLabels[1].text == p.language._Text("accessory.chooseIndividual"), "set or individual mode labels " + set);
                    Render("mode-" + set);
                    p._Acc0(); Check(ActiveRows() == 6 && PreviewCount() == 18, "equip set opens six dogs " + set);
                    Check(p.accButtonLabels[0].text == p.language._Text("accessory.equipSet"), "set button equips instead of buying " + set);
                    p._AccBack(); Check(ActiveRows() == 2 && PreviewCount() == 0, "set dogs back to modes " + set);
                    p._Acc1(); Check(ActiveRows() == 3, "individual mode lists exactly three members " + set);
                    Render("members-" + set);
                    for (int member = 0; member < 3; member++)
                    {
                        int id = box.setMembers[set * 3 + member]; int choice = Array.IndexOf(p.accProductIds, id);
                        Check(p.accRowLabels[member].text == p.language._Text(p.accNameKeys[choice]), "correct member " + id);
                        Check(!p.accRowSubLabels[member].text.Contains("pt"), "member has no purchase price " + id);
                        Call(p, "AccRow", member);
                        Check(PreviewCount() == 6 && !p.accConfirmRoot.activeSelf, "individual preview only " + id);
                        Check(Enumerable.Range(0, box.nodes == null ? source.accessories.nodes.Length : box.nodes.Length).All(i => p.accPreviewItems[i].activeInHierarchy == (box.productIds[i / 6] == id)), "only selected member mesh " + id);
                        Check(p.accButtonLabels[0].text != p.language._Text("KD_TreatPouch.WordBuyHere"), "member cannot be bought " + id);
                        Check(Enumerable.Range(0, 30).All(i => box.EquippedOf(i / 5, i % 5) == -1), "browsing leaves world unchanged " + id);
                        p._AccBack(); Check(ActiveRows() == 3, "individual dogs back to members " + id);
                    }
                    p._AccBack(); Check(ActiveRows() == 2, "members back to modes " + set);
                    p._AccBack(); Check(ActiveRows() == 12 && !p.accBackButton.activeSelf, "modes back to catalog " + set);
                    Call(p, "AccRow", set); Check(ActiveRows() == 2, "owned set opens modes directly " + set);
                }
                Own(""); Field(ledger, "_earned", 200); Root();
                for (int row = 3; row < 12; row++)
                {
                    Call(p, "AccRow", row); Check(PreviewCount() == 6, "standalone item preview " + row);
                    if (row > 3) { p._Acc2(); Check(p.accConfirmRoot.activeSelf, "standalone remains purchasable " + row); p._AccConfirmNo(); }
                    p._AccBack(); Check(ActiveRows() == 12, "standalone back to catalog " + row);
                }
                // Even stale or forged local selection state cannot open a member purchase.
                Field(p, "_accProduct", 7); Field(p, "_accConfirm", true); p._AccConfirmBuy();
                Check((int)typeof(KD_ShopItem).GetField("_dbgBought", Private).GetValue(p.accItems[7]) == 0, "stale member confirmation cannot purchase");
                Field(p, "_accConfirm", false); p._Acc1(); Check(!p.accConfirmRoot.activeSelf, "unowned member cannot open confirmation");
                Root(); Own(box.setItems[0].productKey); p._Acc0(); p._Acc0();
                box.requestDog = 2; box.requestSelection = 100; box.requestState = 3; Call(p, "RedrawAcc");
                Check(p.accRowSubLabels[2].text == p.language._Text("accessory.rejected"), "set rejection visible for requested dog");
                Check(p.accRowSubLabels[1].text != p.accRowSubLabels[2].text, "other dog has no stale result");
                box.requestState = 4; Call(p, "RedrawAcc"); Check(p.accRowSubLabels[2].text == p.language._Text("accessory.unknown"), "set timeout visible");
                p._AccBack(); p._Acc1(); p._Acc0(); Check(p.accRowSubLabels[2].text != p.language._Text("accessory.unknown"), "other product has no stale result");
                box.requestState = 0;
                var slots = Enumerable.Repeat(-1, 30).ToArray(); slots[0] = 0; Field(box, "_slotProduct", slots);
                Check(!box.SetEquipped(0, 100), "partial set is not full set"); slots[2] = 5; slots[4] = 6; slots[1] = 7;
                Check(box.SetEquipped(0, 100) && box.EquippedOf(0, 1) == 7, "full set permits unrelated face slot");
                Field(p.language, "_languageIndex", p.language.ResolveLanguage("en")); Root(); p._Acc0(); Render("mode-en");
                p._Acc1(); Render("members-en"); p._Acc0(); Render("individual-en");
                p._AccBack(); p._AccBack(); p._Acc0(); Render("royal-en");
                Own(""); Call(p, "RedrawAcc"); Check(ActiveRows() == 12 && PreviewCount() == 0, "lost entitlement returns to catalog");
                Root(); p._Acc0(); p._Acc1(); p._Tab0();
                Check(!(bool)typeof(KD_TreatPouch).GetField("_accConfirm", Private).GetValue(p) && !p.accConfirmRoot.activeInHierarchy, "tab change cancels purchase");
                Check(p.accPreviewDogs.All(g => !g.activeInHierarchy), "other tab hides previews");
                p._Tab5(); Check(!p.accConfirmRoot.activeSelf, "returning to accessories does not restore purchase confirmation");
                foreach (string code in new[] { "ja", "en" })
                {
                    Field(p.language,"_languageIndex",p.language.ResolveLanguage(code)); Root();
                    p.tabScrolls[KD_TreatPouch.TabAcc].verticalNormalizedPosition = 0;
                    Render("everyday-list-"+code);
                    for (int row = 7; row < 12; row++)
                    {
                        Root(); int choice = (int)Call(p,"AccCatalogRow",row);
                        Call(p,"AccRow",row); Check(PreviewCount() == 6,"new item six dog previews "+code+"/"+row);
                        p._Acc2(); Check(p.accConfirmRoot.activeSelf && p.accConfirmYes.activeSelf,"new item purchase confirmation "+code+"/"+row);
                        Check(p.accConfirmLabel.text.Contains(p.language._Text(p.accNameKeys[choice])),"localized purchase name "+code+"/"+row);
                        Render("everyday-purchase-"+code+"-"+row);
                        p._AccConfirmNo();
                    }
                }
            }
            catch (Exception e)
            {
                Check(false, "exception: " + e); throw;
            }
            finally
            {
                RenderTexture.active = prior; EditorSceneManager.ClosePreviewScene(scene); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
                log.AppendLine("RESULT pass=" + pass + " fail=" + fail); File.WriteAllText(Dir + "/menu-checks.txt", log.ToString());
            }
            if (fail > 0) throw new InvalidOperationException("Accessory menu checks failed: " + fail);
        }
    }
}
