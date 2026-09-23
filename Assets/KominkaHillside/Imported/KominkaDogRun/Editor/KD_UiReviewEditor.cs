using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    // Read-only layout regression audit. Only display components are copied to a preview scene.
    public static class KD_UiReviewEditor
    {
        const string Output = "Temp/ui-review-2026-09-07";
        static readonly Dictionary<Transform, Transform> Copies = new Dictionary<Transform, Transform>();
        static readonly StringBuilder Log = new StringBuilder();
        static Scene preview;

        [MenuItem("Tools/KominkaHillside/Menu/16b Verify Volume Layout")]
        public static void InspectVolume()
        {
            Directory.CreateDirectory(Output);
            Log.Clear();
            var scene = SceneManager.GetActiveScene();
            preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var pouch in Resources.FindObjectsOfTypeAll<KD_TreatPouch>())
                {
                    if (pouch.gameObject.scene != scene || pouch.panelRoot == null) continue;
                    VerifyReferences(pouch, new[] { "settingsHome", "volumeSettingsPage", "sleepAudio", "liveVolumeSlider", "bedroomLiveVolumeSlider", "liveVolumeValueLabel", "bedroomLiveVolumeValueLabel", "bedroomBgmVolumeSlider", "bedroomVoiceVolumeSlider", "bedroomBgmVolumeValueLabel", "bedroomVoiceVolumeValueLabel" });
                    var root = Build(pouch.panelRoot);
                    foreach (var tab in pouch.tabImages) if (tab != null) Copies[tab.transform].GetComponent<Image>().color = pouch.tabOffColor;
                    foreach (var underline in pouch.tabUnderlines) if (underline != null) Copies[underline.transform].gameObject.SetActive(false);
                    Copies[pouch.settingsButtonImage.transform].GetComponent<Image>().color = pouch.tabOnColor;
                    foreach (var page in pouch.tabPages) if (page != null) Copies[page.transform].gameObject.SetActive(false);
                    Copies[pouch.settingsPage.transform].gameObject.SetActive(true);
                    Copies[pouch.settingsHome.transform].gameObject.SetActive(true);
                    Copies[pouch.volumeSettingsPage.transform].gameObject.SetActive(false);
                    Set(pouch.sectionTitleLabel, KD_TreatPouch.WordSettings);
                    Render(root, "settings_home", 1520, 1040, 520);
                    Copies[pouch.settingsHome.transform].gameObject.SetActive(false);
                    Copies[pouch.volumeSettingsPage.transform].gameObject.SetActive(true);
                    Set(pouch.sectionTitleLabel, KD_TreatPouch.WordVolumeSettings);
                    foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>()) Measure(t, t.text, "volume/" + t.name);
                    Render(root, "volume_settings", 1520, 1040, 520);
                    UnityEngine.Object.DestroyImmediate(root);
                }
                File.WriteAllText("Temp/sleep-audio-ui-layout.txt", Log.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        [MenuItem("Tools/KominkaHillside/Menu/17a Verify Layout")]
        public static void Inspect()
        {
            Directory.CreateDirectory(Output);
            Log.Clear();
            var sourceScene = SceneManager.GetActiveScene();
            Log.AppendLine("SOURCE " + sourceScene.path + " dirty=" + sourceScene.isDirty);
            preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var shelf in Resources.FindObjectsOfTypeAll<KD_ShopShelf>())
                {
                    if (shelf.gameObject.scene != sourceScene || shelf.confirmRoot == null) continue;
                    VerifyReferences(shelf, new[] { "confirmTitle", "confirmDetail", "confirmPrice" });
                    var root = Build(shelf.confirmRoot);
                    var title = TextCopy(shelf.confirmTitle);
                    var bg = root.transform.Find("Bg").GetComponent<Image>();
                    Log.AppendLine("PANEL " + PathOf(shelf.confirmRoot.transform) + " rect=" + ((RectTransform)root.transform).rect
                        + " bgSprite=" + (bg.sprite == null ? "null" : bg.sprite.name) + " bgType=" + bg.type);
                    int index = 0;
                    foreach (var item in shelf.items)
                    {
                        if (item == null || item.menuOnly) continue;
                        string name = item.displayName;
                        Measure(title, name, "purchase/name/" + item.name);
                        Set(shelf.confirmTitle, name);
                        string detail = item.IsStocked() ? item.packCount + KD_ShopShelf.WordPack + " / " + KD_ShopShelf.WordStock + " 5" + KD_ShopShelf.WordCount
                            : item.IsCE() ? KD_ShopShelf.WordSpecial + " / " + KD_ShopShelf.WordLocked : KD_ShopShelf.WordOwned;
                        string price = item.IsStocked() || (!item.IsCE() && !item.IsFree()) ? item.price + " pt" : "";
                        Set(shelf.confirmDetail, detail);
                        Set(shelf.confirmPrice, price);
                        foreach (var value in new[] { detail, KD_ShopShelf.WordInUse, KD_ShopShelf.WordLoading, "あと 99999 pt" })
                            Measure(TextCopy(shelf.confirmDetail), value, "purchase/detail/" + item.name);
                        foreach (var value in new[] { price, KD_ShopShelf.WordEnough, "99999 pt / あと 99999 pt" })
                            Measure(TextCopy(shelf.confirmPrice), value, "purchase/price/" + item.name);
                        if (shelf.yesButton != null) Copies[shelf.yesButton.transform].gameObject.SetActive(!item.IsCE());
                        if (shelf.confirmBar != null) Copies[shelf.confirmBar.transform].gameObject.SetActive(price.Length > 0);
                        if (shelf.confirmBarFill != null) ((RectTransform)Copies[shelf.confirmBarFill]).anchorMax = new Vector2(1f, 1f);
                        foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>()) Measure(text, text.text, "purchase/current/" + item.name);
                        Render(root, "shop_" + shelf.name + "_" + index, 840, 540, 270);
                        if (item.IsStocked() && index == 0)
                        {
                            var sourceButton = shelf.yesButton.GetComponent<Button>();
                            var copyImage = Copies[shelf.yesButton.transform].GetComponent<Image>();
                            Color normal = copyImage.color;
                            copyImage.color = normal * sourceButton.colors.highlightedColor;
                            Render(root, "purchase_hover", 840, 540, 270);
                            copyImage.color = normal * sourceButton.colors.pressedColor;
                            Render(root, "purchase_pressed", 840, 540, 270);
                            copyImage.color = normal;
                            Copies[shelf.yesButton.transform].gameObject.SetActive(false);
                            Set(shelf.confirmPrice, item.price + " pt / あと " + item.price + " pt");
                            ((RectTransform)Copies[shelf.confirmBarFill]).anchorMax = new Vector2(0f, 1f);
                            Render(root, "purchase_shortfall", 840, 540, 270);
                            Set(shelf.confirmDetail, item.packCount + KD_ShopShelf.WordPack + " / 残り 24個");
                            Set(shelf.confirmPrice, KD_ShopShelf.WordEnough);
                            Copies[shelf.confirmBar.transform].gameObject.SetActive(false);
                            Render(root, "purchase_stock_cap", 840, 540, 270);
                        }
                        index++;
                    }

                    UnityEngine.Object.DestroyImmediate(root);
                }
                foreach (var pouch in Resources.FindObjectsOfTypeAll<KD_TreatPouch>())
                {
                    if (pouch.gameObject.scene != sourceScene || pouch.panelRoot == null) continue;
                    VerifyReferences(pouch, new[] { "sectionTitleLabel", "feedbackLabel", "openToAllLabel", "rowHintLabels", "fxButtonImages", "touchLauncherButton", "settingsPage", "settingsButtonImage", "dogVoiceSlider", "dogVoiceValueLabel", "dogVoiceSources", "dogVoiceBaseVolumes", "bgmPlayer", "bgmSlider", "bgmValueLabel", "uiAudio", "uiSuccessClip", "uiRejectClip" });
                    var root = Build(pouch.panelRoot);
                    foreach (var item in pouch.fxItems)
                    {
                        if (item != null) Measure(TextCopy(pouch.fxConfirmLabel), item.displayName + "  " + item.price + "pt", "fx_confirm/" + item.name);
                    }
                    foreach (var item in pouch.liveItems)
                    {
                        if (item != null) Measure(TextCopy(pouch.liveConfirmLabel), item.displayName + "  " + item.price + "pt", "live_confirm/" + item.name);
                    }
                    Set(pouch.balanceLabel, "ポイント 120 pt");
                    foreach (var value in new[] { "ポイント " + KD_ProgressLedger.MaxPoints + " pt", KD_ShopShelf.WordLoading + "..." })
                        Measure(TextCopy(pouch.balanceLabel), value, "balance/max");
                    foreach (var value in new[] { KD_TreatPouch.WordOpenOn, KD_TreatPouch.WordOpenOff })
                        Measure(TextCopy(pouch.openToAllLabel), value, "toggle/state");
                    Measure(TextCopy(pouch.liveStatusLabel), KD_TreatPouch.WordLiveRejected, "live/rejected");
                    Set(pouch.closeHintLabel, pouch.desktopToggleKey.ToString() + " " + KD_TreatPouch.WordToggleHint);
                    for (int i = 0; i < pouch.rowLabels.Length; i++) Set(pouch.rowLabels[i], pouch.items[i].displayName + " / 5個");
                    Measure(TextCopy(pouch.statusLabel), KD_TreatPouch.WordHolding, "treat/holding");
                    Measure(TextCopy(pouch.closeHintLabel), pouch.desktopToggleKey.ToString() + " " + KD_TreatPouch.WordToggleHint, "hand/recall_hint");
                    Set(pouch.statusLabel, "");
                    foreach (var button in pouch.rowButtons) Copies[button.transform].gameObject.SetActive(true);
                    Copies[pouch.putAwayButton.transform].gameObject.SetActive(false);
                    for (int i = 0; i < pouch.toyLabels.Length; i++)
                    {
                        Set(pouch.toyLabels[i], pouch.toyNames[i]);
                        Set(pouch.toyReasonLabels[i], i == 0 ? "だれかが持っている" : "");
                        Copies[pouch.toyButtons[i].transform].gameObject.SetActive(true);
                        Copies[pouch.toyButtonImages[i].transform].GetComponent<Image>().color = i == 0
                            ? pouch.recallOffColor * pouch.toyButtonComponents[i].colors.disabledColor : pouch.recallOnColor;
                        Set(pouch.sleepLabels[i], pouch.toyNames[i]);
                        Set(pouch.sleepStatusLabels[i], "");
                        Copies[pouch.sleepButtons[i].transform].gameObject.SetActive(true);
                    }
                    for (int i = 0; i < pouch.fxLabels.Length; i++)
                    {
                        bool ce = pouch.fxItems[i].IsCE();
                        Set(pouch.fxLabels[i], pouch.fxItems[i].displayName + (ce ? "  " + KD_ShopShelf.WordSpecial : ""));
                        Copies[pouch.fxButtons[i].transform].gameObject.SetActive(!ce);
                    }
                    for (int i = 0; i < pouch.liveLabels.Length; i++)
                    {
                        Set(pouch.liveLabels[i], pouch.liveTitles[i]);
                        bool ce = pouch.liveItems[i].IsCE();
                        Set(pouch.liveSubLabels[i], ce ? "[Pack]  " + KD_ShopShelf.WordSpecial : "");
                        Set(pouch.liveButtonLabels[i], KD_TreatPouch.WordLiveHost);
                        Copies[pouch.liveButtons[i].transform].gameObject.SetActive(!ce);
                    }
                    Set(pouch.liveStatusLabel, KD_TreatPouch.WordLiveReady);
                    Set(pouch.accHeaderLabel, KD_TreatPouch.WordTabAcc);
                    for (int i = 0; i < pouch.accRowLabels.Length; i++)
                    {
                        bool present = pouch.accNames != null && i < pouch.accNames.Length;
                        Copies[pouch.accRows[i].transform].gameObject.SetActive(present);
                        if (!present) continue;
                        Set(pouch.accRowLabels[i], pouch.accNames[i]);
                        Set(pouch.accRowSubLabels[i], pouch.accSlotNames[i]);
                        Set(pouch.accButtonLabels[i], KD_TreatPouch.WordAccChoose);
                    }
                    if (pouch.accBackButton != null) Copies[pouch.accBackButton.transform].gameObject.SetActive(false);
                    if (pouch.fxConfirmRoot != null) Copies[pouch.fxConfirmRoot.transform].gameObject.SetActive(false);
                    if (pouch.liveConfirmRoot != null) Copies[pouch.liveConfirmRoot.transform].gameObject.SetActive(false);
                    for (int page = 0; page < pouch.tabPages.Length; page++)
                    {
                        for (int i = 0; i < pouch.tabPages.Length; i++)
                        {
                            Copies[pouch.tabPages[i].transform].gameObject.SetActive(i == page);
                            Copies[pouch.tabUnderlines[i].transform].gameObject.SetActive(i == page);
                            Copies[pouch.tabImages[i].transform].GetComponent<Image>().color = i == page ? pouch.tabOnColor : pouch.tabOffColor;
                        }
                        string[] headings = { KD_TreatPouch.WordTitle, KD_TreatPouch.WordToyTitle, KD_TreatPouch.WordFxTitle,
                            KD_TreatPouch.WordSleepTitle, KD_TreatPouch.WordLiveTitle, KD_TreatPouch.WordAccTitle };
                        Set(pouch.sectionTitleLabel, headings[page]);
                        if (pouch.openToAllButton != null) Copies[pouch.openToAllButton.transform].gameObject.SetActive(true);
                        foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>()) Measure(t, t.text, "hand/" + page + "/" + PathOf(t.transform));
                        Render(root, "hand_" + page, 1140, 840, 560);
                        if (page == KD_TreatPouch.TabPetFx)
                        {
                            Copies[pouch.fxConfirmRoot.transform].gameObject.SetActive(true);
                            Copies[pouch.fxConfirmYes.transform].gameObject.SetActive(true);
                            Set(pouch.fxButtonLabels[1], KD_TreatPouch.WordBuyHere);
                            Copies[pouch.fxButtonImages[1].transform].GetComponent<Image>().color = pouch.recallOnColor;
                            Set(pouch.fxLabels[1], pouch.fxItems[1].displayName + "  " + pouch.fxItems[1].price + " pt");
                            Set(pouch.fxConfirmLabel, pouch.fxItems[1].displayName + "  " + pouch.fxItems[1].price + " pt");
                            Render(root, "hand_fx_confirm", 1140, 840, 560);
                            Copies[pouch.fxConfirmRoot.transform].gameObject.SetActive(false);
                        }
                        if (page == KD_TreatPouch.TabLive)
                        {
                            Set(pouch.liveStatusLabel, KD_TreatPouch.WordLivePending);
                            foreach (var img in pouch.liveButtonImages) Copies[img.transform].GetComponent<Image>().color = pouch.recallOffColor;
                            Render(root, "hand_live_pending", 1140, 840, 560);
                        }
                    }
                    foreach (var page in pouch.tabPages) Copies[page.transform].gameObject.SetActive(false);
                    foreach (var underline in pouch.tabUnderlines) Copies[underline.transform].gameObject.SetActive(false);
                    foreach (var tab in pouch.tabImages) Copies[tab.transform].GetComponent<Image>().color = pouch.tabOffColor;
                    Copies[pouch.settingsButtonImage.transform].GetComponent<Image>().color = pouch.tabOnColor;
                    Copies[pouch.settingsPage.transform].gameObject.SetActive(true);
                    Set(pouch.sectionTitleLabel, KD_TreatPouch.WordVolumeSettings);
                    Copies[pouch.settingsHome.transform].gameObject.SetActive(false);
                    Copies[pouch.volumeSettingsPage.transform].gameObject.SetActive(true);
                    foreach (var value in new[] { "100%", "50%", "0%" })
                        Measure(TextCopy(pouch.dogVoiceValueLabel), value, "settings/volume");
                    // IMP-ENV-009: BGM の節も同じ字を出す (数値と ON/OFF)
                    foreach (var value in new[] { "100%", "50%", "0%" })
                        Measure(TextCopy(pouch.bgmValueLabel), value, "settings/bgm_volume");
                    Set(pouch.dogVoiceValueLabel, "100%");
                    Set(pouch.bgmValueLabel, "100%");
                    foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>()) Measure(t, t.text, "settings/" + PathOf(t.transform));
                    Render(root, "hand_settings", 1140, 840, 560);
                    UnityEngine.Object.DestroyImmediate(root);
                }
                foreach (var keeper in Resources.FindObjectsOfTypeAll<KD_ShopKeeper>())
                {
                    if (keeper.gameObject.scene != sourceScene || keeper.menuRoot == null) continue;
                    var root = Build(keeper.menuRoot);
                    Set(keeper.balanceLabel, "ポイント 120 pt");
                    foreach (var f in typeof(KD_ShopKeeper).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                    {
                        if (!f.Name.StartsWith("Info") || f.FieldType != typeof(string)) continue;
                        var text = (string)f.GetValue(null);
                        var t = TextCopy(keeper.infoBody);
                        Measure(t, text, "keeper_scroll_content/" + f.Name, true);
                    }
                    Set(keeper.infoTitle, KD_ShopKeeper.WordHowTo);
                    Set(keeper.infoBody, KD_ShopKeeper.InfoHowTo);
                    var body = TextCopy(keeper.infoBody);
                    body.rectTransform.sizeDelta = new Vector2(0f, body.GetPreferredValues(body.text, body.rectTransform.rect.width, 0f).y);
                    Render(root, "npc_howto", 1140, 840, 560);
                    UnityEngine.Object.DestroyImmediate(root);
                }
                foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
                {
                    if (button.gameObject.scene != sourceScene || !PathOf(button.transform).Contains("====KominkaDogRun====")) continue;
                    var img = button.targetGraphic as Image;
                    var c = button.colors;
                    Log.AppendLine("BUTTON " + PathOf(button.transform) + " transition=" + button.transition + " normal=" + c.normalColor
                        + " hover=" + c.highlightedColor + " pressed=" + c.pressedColor + " selected=" + c.selectedColor
                        + " disabled=" + c.disabledColor + " fade=" + c.fadeDuration
                        + " sprite=" + (img == null || img.sprite == null ? "null" : img.sprite.name) + " type=" + (img == null ? "NA" : img.type.ToString()));
                }
            }
            catch (Exception e) { Log.AppendLine("ERROR " + e); }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                Log.AppendLine("SOURCE AFTER dirty=" + sourceScene.isDirty + " roots=" + sourceScene.rootCount);
                File.WriteAllText(Output + "/measurements.txt", Log.ToString());
            }
        }

        static void VerifyReferences(UdonSharp.UdonSharpBehaviour proxy, string[] names)
        {
            var udon = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            foreach (string name in names)
            {
                object raw;
                bool found = udon != null && udon.publicVariables.TryGetVariableValue(name, out raw);
                object expected = proxy.GetType().GetField(name).GetValue(proxy);
                object actual = null;
                if (udon != null) udon.publicVariables.TryGetVariableValue(name, out actual);
                bool match = found && Same(expected, actual);
                if (expected is Array array && actual is Array other)
                {
                    match = array.Length == other.Length;
                    for (int i = 0; i < array.Length && match; i++) match = Same(array.GetValue(i), other.GetValue(i));
                }
                Log.AppendLine((match ? "REF_OK " : "REF_FAIL ") + proxy.name + "." + name);
            }
        }

        /// <summary>proxy と backing の突き合わせ。**UdonSharpBehaviour の参照は
        /// backing UdonBehaviour として格納される**ので、そのまま比べると必ず食い違う。</summary>
        static bool Same(object expected, object actual)
        {
            if (expected == null) return false;
            if (expected.Equals(actual)) return true;
            var proxy = expected as UdonSharp.UdonSharpBehaviour;
            return proxy != null && Equals(UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy), actual);
        }

        static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
        static GameObject Build(GameObject source)
        {
            Copies.Clear();
            var root = Copy(source.transform, null).gameObject;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            root.SetActive(true);
            var canvas = root.GetComponent<Canvas>();
            if (canvas == null) canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            return root;
        }
        static Transform Copy(Transform source, Transform parent)
        {
            var go = new GameObject(source.name, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(go, preview);
            go.hideFlags = HideFlags.HideAndDontSave;
            var rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent, false);
            var sr = source as RectTransform;
            if (sr != null) { rt.anchorMin = sr.anchorMin; rt.anchorMax = sr.anchorMax; rt.pivot = sr.pivot; rt.sizeDelta = sr.sizeDelta; rt.anchoredPosition3D = sr.anchoredPosition3D; }
            else rt.localPosition = source.localPosition;
            rt.localRotation = source.localRotation; rt.localScale = source.localScale;
            Copies[source] = rt;
            if (source.GetComponent<Canvas>() != null) go.AddComponent<Canvas>();
            var image = source.GetComponent<Image>();
            if (image != null) EditorUtility.CopySerialized(image, go.AddComponent<Image>());
            var text = source.GetComponent<TextMeshProUGUI>();
            if (text != null)
            {
                var copy = go.AddComponent<TextMeshProUGUI>();
                EditorUtility.CopySerialized(text, copy);
                copy.isOrthographic = true;
            }
            if (source.GetComponent<RectMask2D>() != null) EditorUtility.CopySerialized(source.GetComponent<RectMask2D>(), go.AddComponent<RectMask2D>());
            foreach (Transform child in source) Copy(child, rt);
            go.SetActive(source.gameObject.activeSelf);
            return rt;
        }
        static TextMeshProUGUI TextCopy(TextMeshProUGUI original) { return original == null ? null : Copies[original.transform].GetComponent<TextMeshProUGUI>(); }
        static void Set(TextMeshProUGUI original, string value) { var t = TextCopy(original); if (t != null) t.text = value; }
        static void Measure(TextMeshProUGUI text, string value, string key, bool scrollContent = false)
        {
            if (text == null || String.IsNullOrEmpty(value)) return;
            Vector2 size = text.GetPreferredValues(value, text.rectTransform.rect.width, 0f);
            Rect rect = text.rectTransform.rect;
            bool over = size.x > rect.width + 1 || (!scrollContent && size.y > rect.height + 1);
            Log.AppendLine((over ? "OVER " : "FIT ") + key + " font=" + text.fontSize + " rect=" + rect.width + "," + rect.height
                + " need=" + size.x + "," + size.y + " wrap=" + text.enableWordWrapping + " auto=" + text.enableAutoSizing
                + " overflow=" + text.overflowMode + " scrollContent=" + scrollContent + " text=" + value.Replace("\n", " / "));
        }
        static void Render(GameObject root, string name, int width, int height, float worldHeight)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>()) t.ForceMeshUpdate();
            var cameraGo = new GameObject("AuditCamera");
            SceneManager.MoveGameObjectToScene(cameraGo, preview);
            var camera = cameraGo.AddComponent<Camera>();
            camera.scene = preview;
            camera.transform.position = new Vector3(0, 0, -1000);
            camera.orthographic = true; camera.orthographicSize = worldHeight * 0.5f;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 2000;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.27f,0.28f,0.27f);
            var target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image); target.Release(); UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(cameraGo);
        }
    }
}
