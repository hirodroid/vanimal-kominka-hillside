using System;
using System.IO;
using System.Linq;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PhotoInformationEditor
    {
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo and Information/4 - Verify Camera Culling")]
        public static void VerifyCameraCulling()
        {
            Guard(); var v=new KD_VerifyLog("Photo help camera exclusion");
            var go=new GameObject("KD_PhotoHelpProbe");var cam=go.AddComponent<Camera>();cam.enabled=false;cam.nearClipPlane=.01f;cam.farClipPlane=50;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.black;
            var rt=new RenderTexture(640,640,24);var tex=new Texture2D(640,640,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try
            {
                cam.targetTexture=rt;
                foreach(var help in KD_ProductsEditor.SceneComponents<KD_PhotoHelp>())
                {
                    foreach(var surface in new[]{help.panel,help.settingsPanel})
                    {
                    var panel=surface.transform;cam.transform.position=panel.position-panel.forward*1.4f;cam.transform.rotation=Quaternion.LookRotation(panel.position-cam.transform.position,panel.up);cam.fieldOfView=50;
                    foreach(int mask in new[]{~0,~(1<<5)})
                    {
                        cam.cullingMask=mask;help.gameObject.SetActive(false);cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,640,640),0,0);tex.Apply();var baseline=tex.GetPixels32();
                        help.gameObject.SetActive(true);surface.SetActive(true);Canvas.ForceUpdateCanvases();cam.Render();tex.ReadPixels(new Rect(0,0,640,640),0,0);tex.Apply();var shown=tex.GetPixels32();
                        int changed=0;for(int i=0;i<shown.Length;i++)if(!shown[i].Equals(baseline[i]))changed++;
                        v.Check(mask==~0?changed>1000:changed==0,help.name+" "+surface.name+" mask="+mask+" changed="+changed);
                    }
                    surface.SetActive(false);
                    }
                }
            }
            finally {RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
            v.Finish("Logs/photo-help-culling.txt");
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo and Information/2 - Verify")]
        public static void Verify()
        {
            Guard(); var v = new KD_VerifyLog("Photo settings and information");
            var s = KD_ProductsEditor.SceneComponents<KD_PhotoSettings>().Single();
            var menu = KD_ProductsEditor.SceneComponents<KD_MenuSettings>().Single(); var p = menu.pouch;
            var catalog = KD_LocalizationEditor.Load(); KD_LocalizationEditor.ValidateScene(p.language,catalog);
            v.Check(s.slots.Length==28 && s.slots.Distinct().Count()==28 && s.boards.Length==28,"24 gallery + 4 bedroom distinct slots and boards");
            v.Check(s.capture.settings==s && s.capture==KD_ProductsEditor.SceneComponents<KD_BedroomPhotoDirector>().Single().capture,"single shared capture gate for both directors");
            v.Check(s.slots.All(t=>t!=null && t.width==1280 && t.height==720 && t.depth==0 && !t.useMipMap),"authoring RTs stay 720p depthless on every build target");
            var viewer=KD_ProductsEditor.SceneComponents<KD_PhotoViewer>().Single();
            v.Check(s.viewer==viewer && s.galleries!=null && s.galleries.Length==2
                && s.galleries.All(g=>g!=null) && s.galleries.SelectMany(g=>g.slotTextures).SequenceEqual(s.slots),
                "runtime RT bindings cover both galleries and the shared viewer");
            v.Check(s.slots.SequenceEqual(viewer.slotTextures),"viewer and resize use same 28 references");
            v.Check(s.slots.Select((t,i)=>s.boards[i].GetComponent<Renderer>().sharedMaterial.mainTexture==t).All(x=>x),"all board materials keep the resized RT identity");
            v.Check(s.boardButtons.Length==6 && s.boardStateLabels.Length==2,"two resolution panels share the same settings controller");
            foreach(var page in new[]{menu.vanimalInformationPage,menu.trainerInformationPage,menu.photoSettingsPage}) v.Check(p.extraSettingsPages.Contains(page),"managed page "+page.name);
            foreach(var help in KD_ProductsEditor.SceneComponents<KD_PhotoHelp>())
            {
                v.Check(help.panel!=null && !help.panel.activeSelf,"help closed in saved scene: "+help.name);
                v.Check(help.settingsPanel!=null && !help.settingsPanel.activeSelf,"resolution panel closed in saved scene: "+help.name);
                var inputs=help.GetComponentsInChildren<VRC.SDK3.Components.VRCUiShape>(true);
                v.Check(inputs.Length==4 && inputs.All(t=>t.gameObject.layer==0 && t.GetComponent<Canvas>()!=null && t.GetComponent<GraphicRaycaster>()!=null && t.GetComponent<BoxCollider>()!=null),"four complete Default interaction canvases: "+help.name);
                v.Check(help.GetComponentsInChildren<Graphic>(true).All(t=>t.GetComponentInParent<Canvas>(true).gameObject.layer==5 || t.color.a==0),"only UI canvases have visible graphics: "+help.name);
                v.Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(help).SyncMethod==VRC.SDKBase.Networking.SyncType.None,"local help: "+help.name);
            }
            var labels = p.language.fixedLabels; var keys = p.language.fixedKeys;
            for(int i=0;i<labels.Length;i++)
            {
                if(!keys[i].StartsWith("photo.help.") && !keys[i].StartsWith("photo.settings.") && !keys[i].StartsWith("information.vanimal.") && !keys[i].StartsWith("information.trainer."))continue;
                var text=labels[i]; string old=text.text;
                try
                {
                    foreach(var clientLang in catalog.languages.Where(l=>!KD_LocalizationEditor.Bundled(l)))v.Check(true,"CLIENT_UNMEASURED "+clientLang.code+" "+keys[i]+" (verify on device)");
                    foreach(string code in catalog.languages.Where(KD_LocalizationEditor.Bundled).Select(l=>l.code)) // client 言語は Editor で測れない (IMP-INF-010)
                    {
                        text.text=KD_LocalizationEditor.Text(catalog,code,keys[i]); text.isOrthographic=true;
                        var size=text.GetPreferredValues(text.text,text.rectTransform.rect.width,0);
                        bool scrolling=keys[i].EndsWith(".body") && keys[i].StartsWith("information.");
                        v.Check(scrolling || size.y<=text.rectTransform.rect.height+1,code+" fits "+keys[i]+" height="+size.y.ToString("F1")+" / "+text.rectTransform.rect.height);
                        v.Check(text.text.Where(c=>!char.IsControl(c)).All(c=>KD_LocalizationEditor.HasGlyph(text.font,c)),code+" glyphs "+keys[i]);
                    }
                }
                finally {text.text=old;}
            }
            foreach(var button in p.settingsPage.GetComponentsInChildren<Button>(true).Concat(KD_ProductsEditor.SceneComponents<KD_PhotoHelp>().SelectMany(h=>h.GetComponentsInChildren<Button>(true))))
            {
                for(int i=0;i<button.onClick.GetPersistentEventCount();i++)
                {
                    var serialized=new SerializedObject(button); var call=serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").GetArrayElementAtIndex(i);
                    var eventName=call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
                    if(string.IsNullOrEmpty(eventName))continue;
                    var udon=button.onClick.GetPersistentTarget(i) as VRC.Udon.UdonBehaviour;
                    var proxy=udon==null?null:UdonSharpEditorUtility.GetProxyBehaviour(udon);
                    v.Check(proxy!=null && proxy.GetType().GetMethod(eventName)!=null,"event exists "+button.name+" -> "+eventName);
                }
            }
            v.Finish("Logs/photo-information-verify.txt");
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo and Information/3 - Render")]
        public static void Render()
        {
            Guard(); const string output="Logs/photo-information-preview/"; Directory.CreateDirectory(output);
            var menu=KD_ProductsEditor.SceneComponents<KD_MenuSettings>().Single(); var p=menu.pouch; var lang=p.language;
            var root=KD_SceneObjects.Find("KD_TreatPouchPanel").transform;
            var transforms=root.GetComponentsInChildren<Transform>(true).Concat(root.GetComponentsInParent<Transform>(true)).Distinct().ToArray();
            var active=transforms.Select(t=>t.gameObject.activeSelf).ToArray();
            var texts=lang.fixedLabels.ToArray();var strings=texts.Select(t=>t.text).ToArray();
            var photoSettings=KD_ProductsEditor.SceneComponents<KD_PhotoSettings>().Single();
            var stateLabels=new[]{photoSettings.stateLabel}.Concat(photoSettings.boardStateLabels).ToArray();
            var stateStrings=stateLabels.Select(t=>t.text).ToArray();
            var rects=root.GetComponentsInChildren<RectTransform>(true); var sizes=rects.Select(t=>t.sizeDelta).ToArray();var positions=rects.Select(t=>t.anchoredPosition).ToArray();
            var position=root.position;var rotation=root.rotation;var title=p.sectionTitleLabel.text;
            try
            {
                foreach(string code in new[]{"ja","en"})
                {
                    for(int i=0;i<texts.Length;i++)texts[i].text=lang._TextForLanguage(lang.fixedKeys[i],code);
                    foreach(var state in stateLabels)state.text=lang._TextForLanguage("photo.settings.loading",code);
                    foreach(var help in KD_ProductsEditor.SceneComponents<KD_PhotoHelp>())
                    {
                        help.panel.SetActive(true);Canvas.ForceUpdateCanvases();
                        KD_DisplayGuidanceEditor.RenderPanel(help.panel.transform,code+"-"+help.name,output);
                        help.panel.SetActive(false);
                        help.settingsPanel.SetActive(true);Canvas.ForceUpdateCanvases();
                        KD_DisplayGuidanceEditor.RenderPanel(help.settingsPanel.transform,code+"-"+help.name+"-settings",output);
                        help.settingsPanel.SetActive(false);
                        var frame=new GameObject("KD_PhotoBoardPreviewFrame",typeof(RectTransform));
                        try
                        {
                            frame.transform.SetPositionAndRotation(help.panel.transform.position+help.transform.up*.35f,help.transform.rotation);
                            frame.transform.localScale=Vector3.one*.001f;((RectTransform)frame.transform).sizeDelta=new Vector2(1600,1600);
                            KD_DisplayGuidanceEditor.RenderPanel(frame.transform,code+"-"+help.name+"-closed",output);
                        }
                        finally{UnityEngine.Object.DestroyImmediate(frame);}
                    }
                    root.SetPositionAndRotation(new Vector3(0,1000,0),Quaternion.identity);
                    foreach(var tr in root.GetComponentsInParent<Transform>(true))tr.gameObject.SetActive(true);
                    foreach(var page in p.tabPages)if(page!=null)page.SetActive(false);
                    p.settingsPage.SetActive(true);foreach(Transform child in p.settingsPage.transform)child.gameObject.SetActive(false);
                    foreach(var page in new[]{p.settingsHome,menu.informationPage,menu.vanimalInformationPage,menu.trainerInformationPage,menu.photoSettingsPage})
                    {
                        page.SetActive(true);Canvas.ForceUpdateCanvases();
                        string key=page==menu.vanimalInformationPage?"information.vanimal.title":page==menu.trainerInformationPage?"information.trainer.title":page==menu.photoSettingsPage?"photo.settings.title":page==menu.informationPage?"information.title":"KD_TreatPouch.WordSettings";
                        p.sectionTitleLabel.text=lang._TextForLanguage(key,code);
                        var scroll=page.GetComponentInChildren<ScrollRect>();
                        if(scroll!=null){scroll.verticalNormalizedPosition=1;Canvas.ForceUpdateCanvases();}
                        KD_DisplayGuidanceEditor.RenderPanel(root,code+"-"+page.name,output);
                        if(scroll!=null){scroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();KD_DisplayGuidanceEditor.RenderPanel(root,code+"-"+page.name+"-end",output);}
                        page.SetActive(false);
                    }
                    root.SetPositionAndRotation(position,rotation);
                }
            }
            finally
            {
                for(int i=0;i<texts.Length;i++)texts[i].text=strings[i];
                for(int i=0;i<stateLabels.Length;i++)stateLabels[i].text=stateStrings[i];
                for(int i=0;i<transforms.Length;i++)transforms[i].gameObject.SetActive(active[i]);
                for(int i=0;i<rects.Length;i++){rects[i].sizeDelta=sizes[i];rects[i].anchoredPosition=positions[i];}
                root.SetPositionAndRotation(position,rotation);p.sectionTitleLabel.text=title;
            }
        }
    }
}
