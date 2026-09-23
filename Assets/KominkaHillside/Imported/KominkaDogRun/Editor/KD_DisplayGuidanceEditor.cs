using System;
using System.IO;
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
    public static class KD_DisplayGuidanceEditor
    {
        const string Output = "Temp/display-guidance/";
        static void Guard()
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/KominkaDogRun.unity")
                throw new InvalidOperationException("Open the target scene in Edit mode");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Display Guidance/1 - Setup")]
        public static void Setup()
        {
            Guard(); Directory.CreateDirectory(Output);
            if (!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Output + "before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true))
                throw new InvalidOperationException("Scene backup failed");
            var settings = KD_SceneObjects.Find("KD_MenuSettings").GetComponent<KD_MenuSettings>();
            var pouch = settings.pouch; var language = settings.language;
            var catalog = KD_LocalizationEditor.Load(); var log = new StringBuilder();
            KD_ShopSetupEditor.AddGlyphs(KD_LocalizationEditor.BundledCharacters(catalog), log);
            KD_MenuFinishEditor.BuildInformation(pouch, settings, pouch.sectionTitleLabel.font, log);
            var scope = settings.radioPage.transform.Find("Scope").GetComponent<TMP_Text>();
            scope.rectTransform.sizeDelta = new Vector2(480,64);
            KD_LocalizationEditor.Configure(language, catalog, log);
            KD_LocalizationEditor.Bind(language, catalog, log);
            KD_ProductsEditor.Copy(language); KD_ProductsEditor.Copy(settings); KD_ProductsEditor.Copy(pouch);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(pouch.gameObject.scene);
            File.WriteAllText(Output + "setup.txt", log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Display Guidance/3 - Render")]
        public static void Render()
        {
            Guard(); Directory.CreateDirectory(Output);
            var settings=KD_SceneObjects.Find("KD_MenuSettings").GetComponent<KD_MenuSettings>();
            var pouch=settings.pouch; var language=settings.language;
            var panel=KD_SceneObjects.Find("KD_TreatPouchPanel");
            var targets=panel.GetComponentsInChildren<Transform>(true).Concat(panel.GetComponentsInParent<Transform>(true)).Distinct().ToArray();
            var active=targets.Select(t=>t.gameObject.activeSelf).ToArray();
            var texts=targets.Select(t=>t.GetComponent<TMP_Text>()).Where(t=>t!=null).ToArray();
            var strings=texts.Select(t=>t.text).ToArray();
            var rects=targets.OfType<RectTransform>().ToArray();
            var sizes=rects.Select(t=>t.sizeDelta).ToArray(); var positions=rects.Select(t=>t.anchoredPosition).ToArray();
            var scroll=settings.aiInformationScroll;
            var panelPosition=panel.transform.localPosition; var panelRotation=panel.transform.localRotation;
            try
            {
                // The stored hand menu is parked inside the house. Preview its open UI in isolation,
                // then restore the exact placement; this is not a screenshot of a VRChat session.
                panel.transform.SetPositionAndRotation(new Vector3(0,1000,0),Quaternion.identity);
                foreach(var t in panel.GetComponentsInParent<Transform>(true))t.gameObject.SetActive(true);
                foreach(var p in pouch.tabPages)if(p!=null)p.SetActive(false);
                pouch.settingsPage.SetActive(true);
                foreach(Transform child in pouch.settingsPage.transform)child.gameObject.SetActive(false);
                foreach(string code in new[]{"ja","en"})
                {
                    for(int i=0;i<language.fixedLabels.Length;i++)
                        if(language.fixedLabels[i]!=null && language.fixedLabels[i].transform.IsChildOf(panel.transform))language.fixedLabels[i].text=language._TextForLanguage(language.fixedKeys[i],code);
                    foreach(var page in new[]{pouch.settingsHome,settings.informationPage,settings.aiInformationPage,settings.radioPage})
                    {
                        page.SetActive(true);
                        string title=page==pouch.settingsHome?"KD_TreatPouch.WordSettings":page==settings.informationPage?"information.title":page==settings.aiInformationPage?"information.ai.title":"ui.extra.028";
                        pouch.sectionTitleLabel.text=language._TextForLanguage(title,code);
                        Canvas.ForceUpdateCanvases();
                        if(page==settings.aiInformationPage){scroll.verticalNormalizedPosition=1f;Canvas.ForceUpdateCanvases();}
                        RenderPanel(panel.transform,code+"-"+page.name);
                        if(page==settings.aiInformationPage){scroll.verticalNormalizedPosition=0f;Canvas.ForceUpdateCanvases();RenderPanel(panel.transform,code+"-"+page.name+"-end");}
                        page.SetActive(false);
                    }
                }
            }
            finally
            {
                for(int i=0;i<texts.Length;i++)texts[i].text=strings[i];
                for(int i=0;i<targets.Length;i++)targets[i].gameObject.SetActive(active[i]);
                for(int i=0;i<rects.Length;i++){rects[i].sizeDelta=sizes[i];rects[i].anchoredPosition=positions[i];}
                panel.transform.localPosition=panelPosition;panel.transform.localRotation=panelRotation;
            }
        }

        internal static void RenderPanel(Transform panel,string name,string output=Output)
        {
            var go=new GameObject("KD_GuidancePreviewCamera");var camera=go.AddComponent<Camera>();
            var rt=new RenderTexture(1400,1000,24);var image=new Texture2D(1400,1000,TextureFormat.RGB24,false);
            var old=RenderTexture.active;
            try
            {
                float height=((RectTransform)panel).rect.height*panel.lossyScale.y;
                camera.enabled=false;camera.fieldOfView=50;camera.nearClipPlane=.01f;camera.farClipPlane=80;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.09f,.08f);
                camera.transform.position=panel.position-panel.forward*(height*.62f/Mathf.Tan(25f*Mathf.Deg2Rad));
                camera.transform.rotation=Quaternion.LookRotation(panel.position-camera.transform.position,panel.up);
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,1400,1000),0,0);image.Apply();File.WriteAllBytes(output+name+".png",image.EncodeToPNG());
            }
            finally{RenderTexture.active=old;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Display Guidance/2 - Verify")]
        public static void Verify()
        {
            Guard(); Directory.CreateDirectory(Output); var log = new StringBuilder(); int failures=0;
            void Check(bool ok, string label) { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
            var settings = KD_SceneObjects.Find("KD_MenuSettings").GetComponent<KD_MenuSettings>();
            var language = settings.language; var catalog = KD_LocalizationEditor.Load();
            KD_LocalizationEditor.ValidateScene(language, catalog);
            Check(language.pickupTargets.Length == 3 && language.pickupTargets.All(p => p != null), "three treat pickups");
            Check(language.pickupKeys.All(k => k == "interact.treat"), "treat localization keys");
            VerifyPromptRefresh(language, Check);
            object stored;
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(language);
            bool found = backing.publicVariables.TryGetVariableValue("pickupTargets", out stored);
            var storedPickups = stored as Array;
            Check(found && storedPickups != null && storedPickups.Length == 3 && Enumerable.Range(0,3).All(i => storedPickups.GetValue(i) as Component == language.pickupTargets[i]), "backing pickup references");
            string[] storedKeys;
            Check(backing.publicVariables.TryGetVariableValue("pickupKeys", out storedKeys) && storedKeys.SequenceEqual(language.pickupKeys), "backing pickup keys");
            var settingsBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            foreach (string field in new[] { "informationPage", "aiInformationPage", "aiInformationScroll" })
            {
                settingsBacking.publicVariables.TryGetVariableValue(field, out stored);
                Check(stored as UnityEngine.Object == (UnityEngine.Object)typeof(KD_MenuSettings).GetField(field).GetValue(settings), "backing " + field);
            }
            Check(settings.pouch.extraSettingsPages.Contains(settings.radioPage) && settings.pouch.extraSettingsPages.Contains(settings.informationPage) && settings.pouch.extraSettingsPages.Contains(settings.aiInformationPage), "all extra settings pages managed");
            Check(!settings.informationPage.activeSelf && !settings.aiInformationPage.activeSelf, "information starts hidden");
            Check(settings.aiInformationScroll.vertical && !settings.aiInformationScroll.horizontal && settings.aiInformationScroll.viewport.GetComponent<RectMask2D>() != null
                && settings.aiInformationScroll.verticalScrollbar != null, "vertical scroll, mask and scrollbar");
            Check(settings.informationPage.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length == 0 && settings.aiInformationPage.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length == 0, "no additional network behaviours");
            void Event(Transform t, string method, VRC.Udon.UdonBehaviour target)
            {
                var button=t.GetComponent<Button>(); var so=new SerializedObject(button);
                var calls=so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                Check(calls.arraySize==1 && button.onClick.GetPersistentTarget(0)==target && button.onClick.GetPersistentMethodName(0)=="SendCustomEvent"
                    && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue==method, t.name+" -> "+method);
            }
            Event(settings.pouch.settingsHome.transform.Find("InformationButton"),"_OpenInformation",settingsBacking);
            Event(settings.informationPage.transform.Find("AiInformationButton"),"_OpenAiInformation",settingsBacking);
            Event(settings.aiInformationPage.transform.Find("BackInformation"),"_OpenInformation",settingsBacking);
            Event(settings.informationPage.transform.Find("BackSettings"),"_OpenSettings",UdonSharpEditorUtility.GetBackingUdonBehaviour(settings.pouch));
            var body=settings.aiInformationScroll.content.GetComponent<TextMeshProUGUI>();
            Check(!body.richText && body.fontSize==24 && body.GetComponent<ContentSizeFitter>().verticalFit==ContentSizeFitter.FitMode.PreferredSize, "24pt literal text with preferred-height content");
            var guide=KD_SceneObjects.Find("KD_SleepGuide").transform.Find("Caption").GetComponent<TextMeshPro>();
            string oldGuide=guide.text;
            try
            {
                foreach(var code in new[]{"ja","en"})
                {
                    string text=KD_LocalizationEditor.Text(catalog,code,"information.ai.body");
                    var size=body.GetPreferredValues(text,settings.aiInformationScroll.viewport.rect.width,0);
                    Check(size.y>settings.aiInformationScroll.viewport.rect.height,code+" AI body scrolls: height="+size.y.ToString("F1"));
                    guide.text=KD_LocalizationEditor.Text(catalog,code,"guide.sleep"); guide.ForceMeshUpdate(true,true);
                    Check(guide.textInfo.lineCount<=2 && !guide.isTextOverflowing,code+" sleep guide lines="+guide.textInfo.lineCount);
                    Check(!string.IsNullOrEmpty(language._TextForLanguage("interact.treat",code)),code+" treat prompt resolves");
                    foreach(string key in new[]{"radio.interact.power","radio.interact.channel","radio.interact.volume"})
                    {
                        int i=Array.IndexOf(language.interactKeys,key);
                        Check(i>=0 && language.interactTargets[i]!=null && language._TextForLanguage(key,code).Contains(code=="ja"?"ローカル":"local"),code+" "+key);
                    }
                }
            }
            finally { guide.text=oldGuide; guide.ForceMeshUpdate(true,true); }
            log.AppendLine("failures="+failures); File.WriteAllText(Output+"verify.txt",log.ToString());
            if(failures>0)throw new InvalidOperationException("Display guidance verification failed: "+failures);
        }

        static void VerifyPromptRefresh(KD_Language language, Action<bool,string> check)
        {
            var type=typeof(KD_Language);var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            // Isolate the changed prompt consumers while exercising the real language Apply method.
            // No menu redraw, player preference save, or shared toy selection is needed in Edit mode.
            var detached=new[]{"fixedLabels","shareField","choiceLabel","pouch","keeper","toyBox","shelves"}.Select(n=>type.GetField(n)).ToArray();
            var values=detached.Select(f=>f.GetValue(language)).ToArray();
            var privateFields=new[]{"_initialized","_languageIndex","_lookup"}.Select(n=>type.GetField(n,flags)).ToArray();
            var privateValues=privateFields.Select(f=>f.GetValue(language)).ToArray();
            var pickupTexts=language.pickupTargets.Select(p=>p.InteractionText).ToArray();
            var radioTexts=language.interactTargets.Select(p=>p.InteractionText).ToArray();
            try
            {
                foreach(var f in detached)f.SetValue(language,null);
                privateFields[0].SetValue(language,true);
                foreach(string code in new[]{"ja","en"})
                {
                    privateFields[1].SetValue(language,language.ResolveLanguage(code));
                    type.GetMethod("Apply",flags).Invoke(language,null);
                    for(int i=0;i<3;i++)check(language.pickupTargets[i].InteractionText==(code=="ja"?"おやつ":"Treat"),code+" actual language Apply / treat "+i);
                    foreach(string key in new[]{"radio.interact.power","radio.interact.channel","radio.interact.volume"})
                    {
                        int i=Array.IndexOf(language.interactKeys,key);
                        check(i>=0 && language.interactTargets[i].InteractionText==language._TextForLanguage(key,code),code+" actual language Apply / "+key);
                    }
                }
            }
            finally
            {
                for(int i=0;i<detached.Length;i++)detached[i].SetValue(language,values[i]);
                for(int i=0;i<privateFields.Length;i++)privateFields[i].SetValue(language,privateValues[i]);
                for(int i=0;i<pickupTexts.Length;i++)language.pickupTargets[i].InteractionText=pickupTexts[i];
                for(int i=0;i<radioTexts.Length;i++)language.interactTargets[i].InteractionText=radioTexts[i];
            }
        }
    }
}
