using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PhotoInformationEditor
    {
        const string Menu = "Tools/KominkaHillside/Menu/Imported-Unused/Photo and Information/";
        static void Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != KD_Hillside.ScenePath /* 裏山改変: シーンの正本は KD_Hillside */)
                throw new InvalidOperationException("Stopped KominkaDogRun scene required");
        }
        static string T(string key) => KD_LocalizationEditor.Text(KD_LocalizationEditor.Load(), "ja", key);
        static TMP_Text Label(Transform parent, string name, Vector2 pos, Vector2 size, string key, TMP_FontAsset font, float points = 24)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var text = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = points; text.enableAutoSizing = false; text.isOrthographic = true;
            text.text = T(key); text.color = Color.white; text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = true; text.raycastTarget = false; return text;
        }
        static Button Button(Transform parent, string name, Vector2 pos, Vector2 size, string key, TMP_FontAsset font, UdonBehaviour target, string method, StringBuilder log)
        {
            var go = KD_ShopSetupEditor.EnsureButton(parent, name, pos, size, KD_UiSpriteEditor.ActionNeutral, font, T(key), target, method, log);
            var text = go.GetComponentInChildren<TMP_Text>(); text.fontSize = 24; text.enableAutoSizing = false;
            return go.GetComponent<Button>();
        }
        internal static ScrollRect Reading(KD_TreatPouch pouch, KD_MenuSettings settings, string name, string key, TMP_FontAsset font, StringBuilder log)
        {
            var page = KD_ShopSetupEditor.EnsureUiChild(pouch.settingsPage.transform, name, Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            var existing = page.transform.Find("ScrollView");
            var scroll = existing != null ? existing.GetComponent<ScrollRect>() : KD_CreditSetupEditor.BuildScrollArea(page.transform, "ScrollView", Vector2.zero, new Vector2(1000,400),36,
                new Color(.12f,.11f,.10f,.8f),new Color(.20f,.18f,.16f,1),KD_UiSpriteEditor.ActionNeutral,out _);
            scroll.inertia = false;
            var body = Label(scroll.viewport,"Content",Vector2.zero,new Vector2(934,400),key,font);
            body.margin = new Vector4(12,12,12,12); body.raycastTarget = true;
            var rect = body.rectTransform; rect.anchorMin = new Vector2(0,1); rect.anchorMax = new Vector2(1,1); rect.pivot = new Vector2(.5f,1); rect.sizeDelta = new Vector2(0,400);
            var fitter = body.GetComponent<ContentSizeFitter>() ?? body.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = rect; scroll.verticalNormalizedPosition = 1;
            Button(page.transform,"BackInformation",new Vector2(-310,-250),new Vector2(380,50),"information.back",font,UdonSharpEditorUtility.GetBackingUdonBehaviour(settings),"_OpenInformation",log);
            page.SetActive(false); return scroll;
        }
        // Called by the existing menu builder too, so rebuilding settings does not restore an obsolete layout.
        public static void BuildPages(KD_TreatPouch pouch, KD_MenuSettings settings, TMP_FontAsset font, StringBuilder log)
        {
            var target = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            var info = settings.informationPage.transform;
            Button(info,"VanimalInformationButton",new Vector2(0,40),new Vector2(640,70),"information.vanimal.title",font,target,"_OpenVanimalInformation",log);
            Button(info,"TrainerInformationButton",new Vector2(0,-60),new Vector2(640,70),"information.trainer.title",font,target,"_OpenTrainerInformation",log);
            // IMP-INF-020 (2026-09-17): 「はじめに」を先頭 (y=140) に。既存の 3 行は 1 段下げる
            KD_WelcomeGuideEditor.BuildReread(pouch,settings,font,log);
            ShiftInformationRows(info);
            settings.vanimalInformationScroll = Reading(pouch,settings,"VanimalInformation","information.vanimal.body",font,log);
            settings.trainerInformationScroll = Reading(pouch,settings,"TrainerInformation","information.trainer.body",font,log);
            settings.vanimalInformationPage = settings.vanimalInformationScroll.transform.parent.gameObject;
            settings.trainerInformationPage = settings.trainerInformationScroll.transform.parent.gameObject;
            var photo = KD_ShopSetupEditor.EnsureUiChild(pouch.settingsPage.transform,"PhotoSettings",Vector2.zero,new Vector2(KD_TreatSetupEditor.PanelWidth,KD_TreatSetupEditor.PanelHeight));
            settings.photoSettingsPage = photo;
            // New top row; all existing controls retain their position.
            Button(pouch.settingsHome.transform,"PhotoSettingsButton",new Vector2(0,158),new Vector2(320,64),"photo.settings.title",font,target,"_OpenPhotoSettings",log);   // 2026-09-17: タブの下端 201 から 11px あける
            Button(photo.transform,"BackSettings",new Vector2(-380,-250),new Vector2(240,50),"KD_TreatPouch.WordBackSettings",font,UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch),"_OpenSettings",log);
            var pages = new List<GameObject>(pouch.extraSettingsPages ?? Array.Empty<GameObject>());
            foreach(var page in new[]{settings.vanimalInformationPage,settings.trainerInformationPage,photo}) if(!pages.Contains(page)) pages.Add(page);
            pouch.extraSettingsPages = pages.ToArray(); photo.SetActive(false);
        }
        /// <summary>インフォメーションの行の並び (IMP-INF-020): はじめに 140 / VAnimal 40 / トレーナー -60 / 生成AI -160。**ここが正本** — Welcome Guide/1 からも呼ぶ。</summary>
        internal static void ShiftInformationRows(Transform info)
        {
            ((RectTransform)info.Find("VanimalInformationButton")).anchoredPosition = new Vector2(0,40);
            ((RectTransform)info.Find("TrainerInformationButton")).anchoredPosition = new Vector2(0,-60);
            ((RectTransform)info.Find("AiInformationButton")).anchoredPosition = new Vector2(0,-160);
        }
        [MenuItem(Menu + "1 - Setup")]
        public static void Setup()
        {
            Guard(); var log = new StringBuilder();
            var settings = KD_ProductsEditor.SceneComponents<KD_MenuSettings>().Single(); var pouch = settings.pouch; var font = pouch.sectionTitleLabel.font;
            var missing = KD_ShopSetupEditor.MissingGlyphs(KD_LocalizationEditor.BundledCharacters(KD_LocalizationEditor.Load()));
            if(missing.Length > 0) KD_ShopSetupEditor.AddGlyphs(missing,log);
            BuildPages(pouch,settings,font,log);
            var cap = KD_ProductsEditor.SceneComponents<KD_PhotoCapture>().Single();
            var child = cap.transform.Find("KD_PhotoSettings");
            if(child == null) { var go = new GameObject("KD_PhotoSettings"); go.transform.SetParent(cap.transform,false); child = go.transform; go.AddUdonSharpComponent<KD_PhotoSettings>(); }
            var control = child.GetComponent<KD_PhotoSettings>(); control.capture = cap; control.language = pouch.language; cap.settings = control;
            var main = cap.GetComponent<KD_GalleryController>(); var bed = KD_SceneObjects.Find("KD_BedroomPhoto").GetComponent<KD_GalleryController>();
            control.slots = main.slotTextures.Concat(bed.slotTextures).ToArray(); control.boards = main.boards.Concat(bed.boards).ToArray();
            control.galleries = new[] { main, bed };
            control.viewer = KD_ProductsEditor.SceneComponents<KD_PhotoViewer>().Single();
            var target = UdonSharpEditorUtility.GetBackingUdonBehaviour(control); target.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            var page = settings.photoSettingsPage.transform;
            control.highButton = Button(page,"High",new Vector2(-340,155),new Vector2(300,64),"photo.settings.high",font,target,"_High",log);
            control.standardButton = Button(page,"Standard",new Vector2(0,155),new Vector2(300,64),"photo.settings.standard",font,target,"_Standard",log);
            control.offButton = Button(page,"Off",new Vector2(340,155),new Vector2(300,64),"photo.settings.off",font,target,"_Off",log);
            control.stateLabel = Label(page,"State",new Vector2(0,85),new Vector2(980,52),"photo.settings.loading",font);
            Label(page,"Note",new Vector2(0,-75),new Vector2(980,250),"photo.settings.note",font,22);
            BuildHelp(main,"KD_PhotoHelpMain",font,log); BuildHelp(bed,"KD_PhotoHelpBedroom",font,log);
            var boardButtons = new List<Button>(); var boardStates = new List<TMP_Text>();
            foreach(string name in new[]{"KD_PhotoHelpMain","KD_PhotoHelpBedroom"})
                BuildBoardSettings(KD_SceneObjects.Find(name).GetComponent<KD_PhotoHelp>(),control,font,boardButtons,boardStates,log);
            control.boardButtons = boardButtons.ToArray(); control.boardStateLabels = boardStates.ToArray();
            var catalog = KD_LocalizationEditor.Load(); KD_LocalizationEditor.Configure(pouch.language,catalog,log); KD_LocalizationEditor.Bind(pouch.language,catalog,log);
            foreach(var b in new UdonSharp.UdonSharpBehaviour[]{settings,pouch,pouch.language,cap,control}) KD_ProductsEditor.Copy(b);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(cap.gameObject.scene); KD_VerifyLog.Write("Logs/photo-information-setup.txt",log);
        }
        static void BuildHelp(KD_GalleryController gallery,string name,TMP_FontAsset font,StringBuilder log)
        {
            var existing = KD_SceneObjects.Find(name);
            if(existing == null)
            {
                existing = new GameObject(name,typeof(RectTransform));
                // Use the visible boards, including hand-adjusted bedroom positions, to place the initial marker.
                var reference = gallery.boards[0].transform;
                existing.transform.SetParent(reference.parent,false); existing.transform.rotation = reference.rotation;
                Vector3 center = Vector3.zero; float top = float.NegativeInfinity;
                foreach(var board in gallery.boards) { center += board.transform.position; top = Mathf.Max(top,board.GetComponent<Renderer>().bounds.max.y); }
                center /= gallery.boards.Length; center.y = top + .23f;
                existing.transform.position = center - existing.transform.forward * .04f;
                existing.transform.localScale = Vector3.one * (.001f / existing.transform.parent.lossyScale.x);
                existing.AddUdonSharpComponent<KD_PhotoHelp>();
            }
            // The VRC pointer hits transparent Default canvases. Drawing uses separate sibling
            // UI canvases, outside those Canvas hierarchies, so camera culling really excludes it.
            foreach(var component in new UnityEngine.Component[]{existing.GetComponent<GraphicRaycaster>(),existing.GetComponent<VRCUiShape>(),existing.GetComponent<BoxCollider>(),existing.GetComponent<Canvas>()})
                if(component!=null)UnityEngine.Object.DestroyImmediate(component);
            existing.layer=0;
            var retiredOpen=existing.transform.Find("Open");if(retiredOpen!=null)UnityEngine.Object.DestroyImmediate(retiredOpen.gameObject);
            var oldPanel=existing.transform.Find("Panel");if(oldPanel!=null && oldPanel.Find("Visual")==null)UnityEngine.Object.DestroyImmediate(oldPanel.gameObject);
            var help=existing.GetComponent<KD_PhotoHelp>();var target=UdonSharpEditorUtility.GetBackingUdonBehaviour(help);
            target.SyncMethod=VRC.SDKBase.Networking.SyncType.None;
            var input=HelpCanvas(existing.transform,"MarkerInput",Vector2.zero,new Vector2(120,120),true);
            var open=Button(input,"Open",Vector2.zero,new Vector2(120,120),"photo.help.mark",font,target,"_Toggle",log);
            InvisibleButton(open);
            var visual=HelpCanvas(existing.transform,"MarkerVisual",Vector2.zero,new Vector2(120,120),false);
            var marker=visual.GetComponent<Image>();if(marker==null)marker=visual.gameObject.AddComponent<Image>();marker.color=KD_UiSpriteEditor.ActionNeutral;marker.raycastTarget=false;KD_UiSpriteEditor.ApplyFlatSkin(marker);
            var mark=Label(visual,"Label",Vector2.zero,new Vector2(120,120),"photo.help.mark",font,64);mark.alignment=TextAlignmentOptions.Center;mark.gameObject.layer=5;
            var panelTransform=existing.transform.Find("Panel");
            var panel=panelTransform!=null ? panelTransform.gameObject : KD_ShopSetupEditor.EnsureUiChild(existing.transform,"Panel",new Vector2(0,-550),new Vector2(1000,920));
            var drawing=HelpCanvas(panel.transform,"Visual",Vector2.zero,new Vector2(1000,920),false);
            var bg=drawing.GetComponent<Image>();if(bg==null)bg=drawing.gameObject.AddComponent<Image>();bg.color=new Color(.12f,.10f,.08f,1);bg.raycastTarget=false;KD_UiSpriteEditor.ApplyFlatSkin(bg);
            Label(drawing,"Title",new Vector2(0,394),new Vector2(904,56),"photo.help.title",font,32);
            Label(drawing,"Body",Vector2.zero,new Vector2(904,688),"photo.help.body",font,26);
            var closePlate=KD_ShopSetupEditor.EnsureUiChild(drawing,"Close",new Vector2(0,-404),new Vector2(240,64));
            var closeImage=closePlate.GetComponent<Image>();if(closeImage==null)closeImage=closePlate.AddComponent<Image>();closeImage.color=KD_UiSpriteEditor.ActionNeutral;closeImage.raycastTarget=false;KD_UiSpriteEditor.ApplyFlatSkin(closeImage);
            var closeLabel=Label(closePlate.transform,"Label",Vector2.zero,new Vector2(240,64),"photo.help.close",font);closeLabel.alignment=TextAlignmentOptions.Center;
            foreach(var tr in drawing.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=5;
            var panelInput=HelpCanvas(panel.transform,"Input",Vector2.zero,new Vector2(1000,920),true);
            InvisibleButton(Button(panelInput,"Close",new Vector2(0,-404),new Vector2(240,64),"photo.help.close",font,target,"_Close",log));
            help.panel=panel;panel.SetActive(false);KD_ProductsEditor.Copy(help);
        }
        static void InvisibleButton(Button button)
        {
            button.transition=Selectable.Transition.None;button.targetGraphic.color=Color.clear;
            var label=button.GetComponentInChildren<TMP_Text>();if(label!=null)UnityEngine.Object.DestroyImmediate(label.gameObject);
        }
        static Button BoardButton(Transform input,Transform visual,string name,Vector2 pos,Vector2 size,string key,TMP_FontAsset font,UdonBehaviour target,string method,StringBuilder log)
        {
            var plate=KD_ShopSetupEditor.EnsureUiChild(visual,name,pos,size);
            var image=plate.GetComponent<Image>();if(image==null)image=plate.AddComponent<Image>();
            image.color=method.StartsWith("_Close")?KD_UiSpriteEditor.ActionClose:KD_UiSpriteEditor.ActionNeutral;
            image.raycastTarget=false;KD_UiSpriteEditor.ApplyRoundSkin(image,KD_UiSpriteEditor.LoadRound());
            var label=Label(plate.transform,"Label",Vector2.zero,size-new Vector2(24,4),key,font);label.alignment=TextAlignmentOptions.Center;
            foreach(var tr in plate.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=5;
            var button=Button(input,name,pos,size,key,font,target,method,log);InvisibleButton(button);
            button.targetGraphic=image;KD_UiSpriteEditor.ApplyButtonFeedback(button);
            return button;
        }
        static void BuildBoardSettings(KD_PhotoHelp help,KD_PhotoSettings control,TMP_FontAsset font,List<Button> buttons,List<TMP_Text> states,StringBuilder log)
        {
            var target=UdonSharpEditorUtility.GetBackingUdonBehaviour(help);
            var markerInput=HelpCanvas(help.transform,"SettingsMarkerInput",new Vector2(255,0),new Vector2(330,120),true);
            var markerVisual=HelpCanvas(help.transform,"SettingsMarkerVisual",new Vector2(255,0),new Vector2(330,120),false);
            BoardButton(markerInput,markerVisual,"Open",Vector2.zero,new Vector2(330,120),"photo.settings.title",font,target,"_ToggleSettings",log);
            var existing=help.transform.Find("SettingsPanel");
            var panel=existing!=null?existing.gameObject:KD_ShopSetupEditor.EnsureUiChild(help.transform,"SettingsPanel",Vector2.zero,new Vector2(1000,760));
            if(existing==null)panel.transform.SetPositionAndRotation(help.panel.transform.position,help.panel.transform.rotation);
            var visual=HelpCanvas(panel.transform,"Visual",Vector2.zero,new Vector2(1000,760),false);
            var bg=visual.GetComponent<Image>();if(bg==null)bg=visual.gameObject.AddComponent<Image>();bg.color=new Color(.12f,.10f,.08f,1);bg.raycastTarget=false;KD_UiSpriteEditor.ApplyFlatSkin(bg);
            var input=HelpCanvas(panel.transform,"Input",Vector2.zero,new Vector2(1000,760),true);
            Label(visual,"Title",new Vector2(0,308),new Vector2(904,56),"photo.settings.title",font,32);
            var controller=UdonSharpEditorUtility.GetBackingUdonBehaviour(control);
            string[] names={"High","Standard","Off"},keys={"photo.settings.high","photo.settings.standard","photo.settings.off"},events={"_High","_Standard","_Off"};
            for(int i=0;i<3;i++)buttons.Add(BoardButton(input,visual,names[i],new Vector2((i-1)*310,216),new Vector2(290,72),keys[i],font,controller,events[i],log));
            states.Add(Label(visual,"State",new Vector2(0,137),new Vector2(904,52),"photo.settings.loading",font));
            Label(visual,"Note",new Vector2(0,-70),new Vector2(904,320),"photo.settings.note",font,24);
            BoardButton(input,visual,"Close",new Vector2(0,-310),new Vector2(240,64),"photo.help.close",font,target,"_CloseSettings",log);
            foreach(var tr in visual.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=5;
            help.settingsPanel=panel;panel.SetActive(false);KD_ProductsEditor.Copy(help);
        }
        static Transform HelpCanvas(Transform parent,string name,Vector2 pos,Vector2 size,bool input)
        {
            var go=KD_ShopSetupEditor.EnsureUiChild(parent,name,pos,size);go.layer=input?0:5;
            var canvas=go.GetComponent<Canvas>();if(canvas==null)canvas=go.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
            if(input)
            {
                if(go.GetComponent<GraphicRaycaster>()==null)go.AddComponent<GraphicRaycaster>();
                if(go.GetComponent<VRCUiShape>()==null)go.AddComponent<VRCUiShape>();
                var box=go.GetComponent<BoxCollider>();if(box==null)box=go.AddComponent<BoxCollider>();box.isTrigger=true;box.size=new Vector3(size.x,size.y,1);
            }
            return go.transform;
        }
    }
}
