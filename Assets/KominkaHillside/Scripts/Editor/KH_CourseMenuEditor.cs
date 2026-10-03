using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase.Editor.BuildPipeline;

namespace KominkaHillside.Editor
{
    /// <summary>Extends only the course copy. Main menu/catalog remain usable without the optional rows.</summary>
    public static class KH_CourseMenuEditor
    {
        public const string CatalogPath="Assets/KominkaHillside/Data/Environment/Course/CourseUITranslations.json";
        static readonly int[] Destinations={1,0,2,3,4,5};
        static readonly string[] Keys={"KD_TreatPouch.WordWalkPlaza","KD_TreatPouch.WordWalkGate","course.walk.forward","course.walk.reverse","course.walk.upper","course.walk.river"};
        static readonly string[] Events={"_WalkPlaza","_WalkGate","_WalkLoopForward","_WalkLoopReverse","_WalkUpperPlaza","_WalkRiverbank"};

        [MenuItem("Tools/KominkaHillside/Course/27 - Add Course UI Glyphs")]
        public static void AddGlyphs()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p=Pouch();var catalog=Catalog(p.language);var log=new System.Text.StringBuilder();
            KD_ShopSetupEditor.AddGlyphs(KD_LocalizationEditor.BundledCharacters(catalog)+KH_FrisbeePrototypeEditor.AimHint+KH_FrisbeePrototypeEditor.AimFarHint,log); // BUG-DOG-007: the two aim hints share this font
            Directory.CreateDirectory("Temp/des-env-002");File.WriteAllText("Temp/des-env-002/course-menu-glyphs.txt",log.ToString());
            KD_LocalizationEditor.ValidateGlyphs(catalog,p.sectionTitleLabel.font);
            Debug.Log("[KH Course Menu] Course glyphs present; font identity and existing glyphs retained");
        }

        static KD_TreatPouch Pouch()
        {
            var all=UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if(all.Length!=1)throw new InvalidOperationException("Exactly one menu required");
            var p=all[0];
            if(p.manager==null||p.manager.route==null||!p.manager.route.closedLoop||p.language==null)
                throw new InvalidOperationException("Course route/language not wired");
            return p;
        }
        static KD_LocalizationEditor.Catalog Catalog(KD_Language language)
        {
            var c=JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(File.ReadAllText(CatalogPath));
            KD_LocalizationEditor.Validate(c);
            if(!language.languageCodes.SequenceEqual(c.languages.Select(l=>l.code))||c.fallbackLanguage!=language.fallbackLanguage||c.defaultLanguage!=language.defaultLanguage)
                throw new InvalidOperationException("Course/base language order or fallback differs");
            if(KD_LocalizationEditor.IncompleteLanguages(c).Length!=0)throw new InvalidOperationException("Incomplete course translations");
            var keys=c.languages.First(l=>l.code==c.fallbackLanguage).texts.Select(e=>e.key).ToArray();
            if(keys.Any(k=>language.keys.Contains(k)))throw new InvalidOperationException("Course catalog cannot override base keys");
            return c;
        }
        // DES-UI-006: Course/120 の 2 列でも行は同じ Content に残る (Grid / Fitter は無効にして残す) ので、この検査はそのまま通る
        static RectTransform Content(KD_TreatPouch p)
        {
            var scroll=p.tabScrolls[KD_TreatPouch.TabWalk];
            if(scroll==null||scroll.content==null||scroll.viewport==null||scroll.viewport.GetComponent<RectMask2D>()==null||scroll.verticalScrollbar==null)
                throw new InvalidOperationException("Existing masked scroll with scrollbar required");
            var c=scroll.content;
            var g=c.GetComponent<GridLayoutGroup>();var f=c.GetComponent<ContentSizeFitter>();
            if(g==null||g.constraintCount!=2||g.cellSize!=new Vector2(508,112)||f==null||f.verticalFit!=ContentSizeFitter.FitMode.PreferredSize)
                throw new InvalidOperationException("Existing 2-column layout required");
            return c;
        }

        [MenuItem("Tools/KominkaHillside/Course/25 - Add Course Destinations")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p=Pouch();var content=Content(p);var language=p.language;var catalog=Catalog(language);
            KD_LocalizationEditor.ValidateScene(language,KD_LocalizationEditor.Load());
            KD_LocalizationEditor.ValidateGlyphs(catalog,p.sectionTitleLabel.font);
            if(content.childCount!=8||p.walkLabels.Length!=8||p.walkHereButtons.Length!=2||(p.walkDestinationIds!=null&&p.walkDestinationIds.Length!=0))
                throw new InvalidOperationException("Initial 8-row menu required; existing course menu preserved");
            var template=content.Find("WalkRow6");
            if(template==null||PrefabUtility.IsPartOfPrefabInstance(template)||template.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true).Length!=0)
                throw new InvalidOperationException("Plain destination-row template required");
            foreach(var name in new[]{"Label","Reason","Here","Here/Label"})
                if(template.Find(name)==null)throw new InvalidOperationException("Incomplete destination row: "+name);
            var target=UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            if(target==null)throw new InvalidOperationException("Missing menu backing behaviour");
            File.Copy(KH_CourseGroundEditor.ScenePath,"Temp/des-env-002/before-course-menu.unity",true);
            var created=new List<GameObject>();
            var oldLabels=p.walkLabels;var oldStates=p.walkStateLabels;var oldButtons=p.walkHereButtons;var oldImages=p.walkHereImages;
            var oldHereLabels=p.walkHereLabels;
            var oldIds=p.walkDestinationIds;var oldKeys=p.walkDestinationKeys;var oldExtraKeys=language.extensionKeys;var oldExtraTexts=language.extensionTexts;
            try
            {
                for(int i=8;i<12;i++)
                {
                    var row=UnityEngine.Object.Instantiate(template.gameObject,content);created.Add(row);row.name="WalkRow"+i;
                    row.transform.SetAsLastSibling();row.SetActive(true);
                    var button=row.transform.Find("Here").GetComponent<Button>();
                    button.onClick=new Button.ButtonClickedEvent();
                    UnityEventTools.AddStringPersistentListener(button.onClick,target.SendCustomEvent,Events[i-6]);
                    row.transform.Find("Label").GetComponent<TextMeshProUGUI>().text=KD_LocalizationEditor.Text(catalog,"ja",Keys[i-6]);
                    row.transform.Find("Reason").GetComponent<TextMeshProUGUI>().text=KD_LocalizationEditor.Text(KD_LocalizationEditor.Load(),"ja","KD_TreatPouch.WordWalkStateDest");
                }
                p.walkLabels=new TextMeshProUGUI[12];p.walkStateLabels=new TextMeshProUGUI[12];
                p.walkHereButtons=new Button[6];p.walkHereImages=new Image[6];
                p.walkHereLabels=new TextMeshProUGUI[6];
                for(int i=0;i<12;i++)
                {
                    var row=content.Find("WalkRow"+i);
                    p.walkLabels[i]=row.Find("Label").GetComponent<TextMeshProUGUI>();
                    p.walkStateLabels[i]=row.Find("Reason").GetComponent<TextMeshProUGUI>();
                    if(i>=6){p.walkHereButtons[i-6]=row.Find("Here").GetComponent<Button>();p.walkHereImages[i-6]=row.Find("Here").GetComponent<Image>();p.walkHereLabels[i-6]=row.Find("Here/Label").GetComponent<TextMeshProUGUI>();}
                }
                p.walkDestinationIds=(int[])Destinations.Clone();p.walkDestinationKeys=(string[])Keys.Clone();
                language.extensionKeys=catalog.languages.First(l=>l.code==catalog.fallbackLanguage).texts.Select(e=>e.key).ToArray();
                language.extensionTexts=catalog.languages.SelectMany(l=>language.extensionKeys.Select(k=>KD_LocalizationEditor.Text(catalog,l.code,k))).ToArray();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                UdonSharpEditorUtility.CopyProxyToUdon(p);UdonSharpEditorUtility.CopyProxyToUdon(language);
                EditorUtility.SetDirty(p);EditorUtility.SetDirty(language);EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
                if(!EditorSceneManager.SaveScene(p.gameObject.scene))throw new IOException("Course menu save failed");
            }
            catch
            {
                foreach(var row in created)if(row!=null)UnityEngine.Object.DestroyImmediate(row);
                p.walkLabels=oldLabels;p.walkStateLabels=oldStates;p.walkHereButtons=oldButtons;p.walkHereImages=oldImages;
                p.walkHereLabels=oldHereLabels;
                p.walkDestinationIds=oldIds;p.walkDestinationKeys=oldKeys;language.extensionKeys=oldExtraKeys;language.extensionTexts=oldExtraTexts;
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                UdonSharpEditorUtility.CopyProxyToUdon(p);UdonSharpEditorUtility.CopyProxyToUdon(language);
                EditorSceneManager.MarkSceneDirty(p.gameObject.scene);EditorSceneManager.SaveScene(p.gameObject.scene);
                throw;
            }
            Debug.Log("[KH Course Menu] Added four destinations. Save/reload and run menu 26 for independent verification.");
        }

        [Serializable] class Report
        {
            public string utc,scene,status;
            public int rows,buttons,languages,translations,errors;
            public string[] findings;
            public string[] labelFit;
            public string limitations="Editor structure, localization and saved Udon values only. VR/touch scrolling, remote request acknowledgement and device font fallback are not tested.";
        }
        [MenuItem("Tools/KominkaHillside/Course/26 - Verify Course Destinations")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p=Pouch();var errors=new List<string>();
            Validate(p,errors);
            var fit=MeasureLabels(p,errors);
            var r=new Report{utc=DateTime.UtcNow.ToString("o"),scene=p.gameObject.scene.path,status=errors.Count==0?"COURSE_MENU_DATA_OK":"FAIL",rows=p.walkLabels.Length,buttons=p.walkHereButtons.Length,languages=p.language.languageCodes.Length,translations=p.language.extensionTexts.Length,errors=errors.Count,findings=errors.ToArray()};
            r.labelFit=fit;
            Directory.CreateDirectory("Temp/des-env-002");File.WriteAllText("Temp/des-env-002/full-course-menu-verification.json",JsonUtility.ToJson(r,true));
            if(errors.Count!=0)throw new InvalidOperationException(string.Join("\n",errors));
            Debug.Log("[KH Course Menu] COURSE_MENU_DATA_OK rows=12 destinations=6 languages="+r.languages);
        }
        static string[] MeasureLabels(KD_TreatPouch p,List<string> errors)
        {
            var lines=new List<string>();
            // DES-UI-006: 2 列 (Course/120) では行き先はタイル、まとめは右列。測り方は Course/121 と同じ (13 言語)
            if(KH_WalkTabLayoutEditor.IsApplied(p)){KH_WalkTabLayoutEditor.Measure(p,errors,lines,null);return lines.ToArray();}
            var go=new GameObject("CourseMenuMeasure",typeof(RectTransform));go.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                var sample=go.AddComponent<TextMeshProUGUI>();
                foreach(string code in new[]{"ja","en"})
                {
                    for(int k=0;k<6;k++)
                    {
                        string name=p.language._TextForLanguage(Keys[k],code);
                        Measure(sample,p.walkLabels[6+k],name,code+" title "+k,lines,errors);
                        Measure(sample,p.walkHereLabels[k],p.language._TextForLanguage("KD_TreatPouch.WordWalkHere",code),code+" action "+k,lines,errors);
                        foreach(string key in new[]{"KD_TreatPouch.WordWalkStateDest","KD_TreatPouch.WordWalkStateDestChosen"})
                            Measure(sample,p.walkStateLabels[6+k],p.language._TextForLanguage(key,code),code+" state "+k+" "+key,lines,errors);
                        Measure(sample,p.feedbackLabel,p.language._TextForLanguage("course.walk.summary",code).Replace("{0}","6").Replace("{1}",name),code+" summary "+k,lines,errors);
                    }
                    foreach(string key in new[]{"course.walk.mixed","course.walk.confirmed"})
                        Measure(sample,p.feedbackLabel,p.language._TextForLanguage(key,code).Replace("{0}","6"),code+" "+key,lines,errors);
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
            return lines.ToArray();
        }
        static void Measure(TextMeshProUGUI sample,TMP_Text target,string text,string name,List<string> lines,List<string> errors)
        {
            int result=KD_LocalizationEditor.MeasureLabelFit(sample,target,text,out Vector2 preferred);
            lines.Add(name+" preferred="+preferred+" rect="+target.rectTransform.rect.size+" points="+target.fontSize+" fit="+result);
            if(result==2)errors.Add("Label overflow: "+name);
        }
        public static void Validate(KD_TreatPouch p,List<string> errors)
        {
            var c=Content(p);var lang=p.language;var catalog=Catalog(lang);
            KD_LocalizationEditor.ValidateScene(lang,KD_LocalizationEditor.Load());
            KD_LocalizationEditor.ValidateGlyphs(catalog,p.sectionTitleLabel.font);
            if(c.childCount!=12||p.walkLabels.Length!=12||p.walkStateLabels.Length!=12||p.walkHereButtons.Length!=6||p.walkHereImages.Length!=6||p.walkHereLabels==null||p.walkHereLabels.Length!=6)throw new InvalidOperationException("Expected 12 rows / 6 destinations");
            if(!Destinations.SequenceEqual(p.walkDestinationIds)||!Keys.SequenceEqual(p.walkDestinationKeys))errors.Add("Destination mapping differs");
            var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            for(int i=0;i<12;i++)
            {
                var row=c.Find("WalkRow"+i);
                if(row==null||row.GetSiblingIndex()!=i||p.walkLabels[i]!=row.Find("Label").GetComponent<TextMeshProUGUI>()||p.walkStateLabels[i]!=row.Find("Reason").GetComponent<TextMeshProUGUI>())throw new InvalidOperationException("Missing/reordered row "+i);
                if(i<6)continue;
                var button=row.Find("Here").GetComponent<Button>();int k=i-6;
                if(p.walkHereLabels[k]!=row.Find("Here/Label").GetComponent<TextMeshProUGUI>())errors.Add("Action label "+k);
                if(p.walkHereButtons[k]!=button||p.walkHereImages[k]!=button.GetComponent<Image>()||!button.interactable||!button.gameObject.activeSelf)errors.Add("Button wiring/state "+k);
                if(button.onClick.GetPersistentEventCount()!=1||button.onClick.GetPersistentTarget(0)!=backing||button.onClick.GetPersistentMethodName(0)!="SendCustomEvent")errors.Add("Button target "+k);
                var so=new SerializedObject(button);var call=so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                if(call.arraySize!=1||call.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue!=Events[k])errors.Add("Button event "+k);
                if(p.manager.route._DestinationNode(Destinations[k])<0)errors.Add("Invalid destination "+k);
            }
            foreach(var language in catalog.languages)foreach(var entry in language.texts)
                if(lang._TextForLanguage(entry.key,language.code)!=entry.text)errors.Add("Translation differs "+language.code+":"+entry.key);
            CheckArray(backing,"walkLabels",p.walkLabels,errors);CheckArray(backing,"walkStateLabels",p.walkStateLabels,errors);
            CheckArray(backing,"walkHereButtons",p.walkHereButtons,errors);CheckArray(backing,"walkHereImages",p.walkHereImages,errors);
            CheckArray(backing,"walkHereLabels",p.walkHereLabels,errors);
            CheckArray(backing,"walkDestinationIds",p.walkDestinationIds,errors);CheckArray(backing,"walkDestinationKeys",p.walkDestinationKeys,errors);
            var lb=UdonSharpEditorUtility.GetBackingUdonBehaviour(lang);
            CheckArray(lb,"extensionKeys",lang.extensionKeys,errors);CheckArray(lb,"extensionTexts",lang.extensionTexts,errors);
            // DES-UI-006: 2 列の配置・配線・押せる面 (収まりは Course/26・121 が測る)。ビルド前の検査 (KH_CourseMenuBuildGuard) もここを通る
            if(KH_WalkTabLayoutEditor.IsApplied(p))KH_WalkTabLayoutEditor.Validate(p,errors,null,null,null);
        }
        static void CheckArray(VRC.Udon.UdonBehaviour b,string field,Array expected,List<string> errors)
        {
            object value;
            if(b==null||!b.publicVariables.TryGetVariableValue(field,out value)||!(value is Array actual)||actual.Length!=expected.Length){errors.Add("Backing array "+field);return;}
            for(int i=0;i<expected.Length;i++)if(!Equals(expected.GetValue(i),actual.GetValue(i))){errors.Add("Backing value "+field+"["+i+"]");break;}
        }
    }
    public class KH_CourseMenuBuildGuard : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder=>11;
        public bool OnBuildRequested(VRCSDKRequestedBuildType type)
        {
            if(type==VRCSDKRequestedBuildType.Avatar)return true;
            foreach(var p in UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true))
                if(p.manager!=null&&p.manager.route!=null&&p.manager.route.closedLoop)
                {
                    try{var e=new List<string>();KH_CourseMenuEditor.Validate(p,e);if(e.Count>0)throw new InvalidOperationException(string.Join("\n",e));}
                    catch(Exception e){Debug.LogError("[KH Course Menu] "+e.Message);return false;}
                }
            return true;
        }
    }
}
