using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    // Static layout evidence only. Never calls the live panel, Udon, or its button events.
    public static class KH_HalloweenParadeUiPreview
    {
        [Serializable] class TextMetric
        {
            public string path,text,font;
            public bool overflowing;
            public float preferredHeight,preferredWidth,rectHeight,rectWidth,fontSize,fontSizeMin;
            public int characters,lines;
        }
        [Serializable] class View
        {
            public string language,state,image;
            public int phase,message,pending=3,consentSeconds=27,remainingSeconds=30,recoveryFailedMask;
            public bool needs,host,reduced,muted;
            public Vector3 eye,panelPosition;
            public List<string> buttons=new List<string>();
            public List<TextMetric> texts=new List<TextMetric>();
        }
        [Serializable] class Report
        {
            public string utc,directory,panelSourceSha,wordsSourceSha,originalBefore,originalAfter,error;
            public bool dirtyBefore,dirtyAfter;
            public string limits="Static Edit-mode mono layouts, real Surface clone and actual fonts/buttons. No Udon events, player interaction, network or VR/device behavior acceptance. Font/atlas/material copies protect source assets. Recovery failure uses all six names as a worst-case layout.";
            public List<View> views=new List<View>();
            public List<FontCoverage> fontCoverage=new List<FontCoverage>();
        }
        [Serializable] class FontCoverage
        {
            public string language,missingCharacters,missingCodepoints;
            public int uniqueCharacters,missingCount;
            public string scope="All 35 words plus six dog names; current font and cloned configured/global fallbacks. Missing Editor glyphs require VRChat client fallback verification; no font assets are added.";
        }
        const string PanelSource="Assets/KominkaHillside/Scripts/KH_HalloweenParadePanel.cs";
        const string WordsSource="Assets/KominkaHillside/Data/ParadeWords.json";

        [MenuItem("Tools/KominkaHillside/Parade/12 - Capture UI Layouts")]
        public static void Capture(){CaptureLanguages(new[]{"ja","en","de"});}
        [MenuItem("Tools/KominkaHillside/Parade/12b - Capture Remaining UI Languages")]
        public static void CaptureRemaining(){CaptureLanguages(new[]{"ru","uk","ko","zh-cn","es","fr","pt-br","it","pl","zh-hk"});}
        static void CaptureLanguages(string[] languages)
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||scene.isDirty||scene.path!="Assets/Scenes/VRCDefaultWorldScene.unity")
                throw new InvalidOperationException("Saved hillside scene in Edit mode required");
            var panel=UnityEngine.Object.FindObjectsOfType<KH_HalloweenParadePanel>(true).Single(x=>x.gameObject.scene==scene);
            if(panel.surface==null||panel.title==null||panel.body==null||panel.progress==null||panel.wordCount!=35||panel.words.Length!=455)
                throw new InvalidOperationException("Apply and verify the 35-word panel first");
            var playback=panel.playback;
            if(playback==null||playback.stageRoot==null)throw new InvalidOperationException("Bound parade stage required");
            var descriptor=UnityEngine.Object.FindObjectsOfType<VRC.SDKBase.VRC_SceneDescriptor>(true).Single(x=>x.gameObject.scene==scene);
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),directory="Temp/halloween-parade/ui-preview/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),dirtyBefore=scene.isDirty,
                panelSourceSha=Hash(File.ReadAllText(PanelSource)),wordsSourceSha=Hash(File.ReadAllText(WordsSource))};
            report.originalBefore=OriginalSnapshot(panel,descriptor);
            Directory.CreateDirectory(report.directory);
            var owned=new List<UnityEngine.Object>();
            var fonts=new Dictionary<TMP_FontAsset,TMP_FontAsset>();
            var root=new GameObject("KH_DisposableUiLayouts"){hideFlags=HideFlags.HideAndDontSave};root.SetActive(false);
            RenderTexture rt=null;Texture2D pixels=null;var oldRT=RenderTexture.active;
            try
            {
                var copy=UnityEngine.Object.Instantiate(panel.surface,root.transform,false);copy.name="Surface";
                foreach(var t in copy.GetComponentsInChildren<Transform>(true))t.gameObject.hideFlags=HideFlags.HideAndDontSave;
                foreach(var b in copy.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true))b.enabled=false;
                foreach(var b in copy.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true))b.enabled=false;
                foreach(var b in copy.GetComponentsInChildren<Button>(true))b.onClick=new Button.ButtonClickedEvent();
                foreach(var b in copy.GetComponentsInChildren<GraphicRaycaster>(true))b.enabled=false;
                foreach(var b in copy.GetComponentsInChildren<Collider>(true))b.enabled=false;
                foreach(var sub in copy.GetComponentsInChildren<TMP_SubMeshUI>(true))UnityEngine.Object.DestroyImmediate(sub.gameObject);
                foreach(var text in copy.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    var font=CopyFont(text.font,fonts,owned);
                    // Include cloned global fallbacks before TMP could consult the live assets.
                    foreach(var fallback in TMP_Settings.fallbackFontAssets??new List<TMP_FontAsset>())
                    {var f=CopyFont(fallback,fonts,owned);if(f!=null&&f!=font&&!font.fallbackFontAssetTable.Contains(f))font.fallbackFontAssetTable.Add(f);}
                    var df=CopyFont(TMP_Settings.defaultFontAsset,fonts,owned);
                    if(df!=null&&df!=font&&!font.fallbackFontAssetTable.Contains(df))font.fallbackFontAssetTable.Add(df);
                    var material=text.fontSharedMaterial;
                    text.font=font;
                    if(material!=null){var m=UnityEngine.Object.Instantiate(material);m.hideFlags=HideFlags.HideAndDontSave;owned.Add(m);m.mainTexture=font.atlasTextures[0];text.fontSharedMaterial=m;}
                }
                var cameraGo=new GameObject("UiLayoutCamera"){hideFlags=HideFlags.HideAndDontSave};cameraGo.transform.SetParent(root.transform,false);
                var camera=cameraGo.AddComponent<Camera>();
                var reference=descriptor.ReferenceCamera!=null?descriptor.ReferenceCamera.GetComponent<Camera>():null;
                if(reference!=null)camera.CopyFrom(reference);
                camera.enabled=false;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.aspect=16f/9;camera.fieldOfView=60;
                camera.nearClipPlane=.05f;camera.farClipPlane=2200;camera.allowHDR=false;camera.allowMSAA=false;
                rt=new RenderTexture(1920,1080,24){hideFlags=HideFlags.HideAndDontSave};rt.Create();camera.targetTexture=rt;
                pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false){hideFlags=HideFlags.HideAndDontSave};
                var stage=playback.stageRoot.transform;var eye=stage.TransformPoint(new Vector3(0,1.62f,-7));
                var forward=stage.forward;forward.y=0;forward.Normalize();
                camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(forward));
                // Same full-panel placement contract as KH_HalloweenParadePanel.PlaceSurface.
                copy.transform.SetPositionAndRotation(eye+forward*1.35f+Vector3.down*.12f,Quaternion.LookRotation(forward));
                copy.SetActive(true);root.SetActive(true);
                foreach(string language in languages)
                {
                    int at=Array.IndexOf(panel.languageCodes,language);
                    var chars=string.Concat(panel.words.Skip(at*panel.wordCount).Take(panel.wordCount))+string.Join("",Enumerable.Range(0,6).Select(i=>DogName(panel,language,i)));
                    var unique=chars.Where(c=>!char.IsControl(c)&&!char.IsWhiteSpace(c)).Distinct().OrderBy(c=>c).ToArray();
                    var font=Mapped(panel.body,panel,copy.transform).font;
                    var missing=unique.Where(c=>!font.HasCharacter(c,true)).ToArray();
                    report.fontCoverage.Add(new FontCoverage{language=language,uniqueCharacters=unique.Length,missingCount=missing.Length,missingCharacters=new string(missing),missingCodepoints=string.Join(" ",missing.Select(c=>"U+"+((int)c).ToString("X4")))});
                }
                foreach(string language in languages)
                foreach(string state in new[]{"consent","restore","recoveryfail","playing"})
                {
                    var view=new View{language=language,state=state,eye=eye,panelPosition=copy.transform.position};
                    ApplyLayout(panel,copy.transform,view);
                    Canvas.ForceUpdateCanvases();
                    foreach(var text in copy.GetComponentsInChildren<TextMeshProUGUI>(false))text.ForceMeshUpdate();
                    Canvas.ForceUpdateCanvases();
                    camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
                    view.image=report.directory+"/"+language+"-"+state+".png";File.WriteAllBytes(view.image,pixels.EncodeToPNG());
                    foreach(var button in copy.GetComponentsInChildren<Button>(false))view.buttons.Add(AnimationUtility.CalculateTransformPath(button.transform,copy.transform));
                    foreach(var text in copy.GetComponentsInChildren<TextMeshProUGUI>(false))
                    {
                        var rect=text.rectTransform.rect;
                        view.texts.Add(new TextMetric{path=AnimationUtility.CalculateTransformPath(text.transform,copy.transform),text=text.text,font=text.font.name,
                            overflowing=text.isTextOverflowing,preferredHeight=text.preferredHeight,preferredWidth=text.preferredWidth,rectHeight=rect.height,rectWidth=rect.width,
                            fontSize=text.fontSize,fontSizeMin=text.fontSizeMin,characters=text.textInfo.characterCount,lines=text.textInfo.lineCount});
                    }
                    report.views.Add(view);
                }
            }
            catch(Exception e){report.error=e.ToString();throw;}
            finally
            {
                RenderTexture.active=oldRT;UnityEngine.Object.DestroyImmediate(root);
                // Dynamic clone atlases may have grown while rendering; only dispose nonpersistent textures.
                foreach(var f in fonts.Values)if(f!=null)foreach(var t in f.atlasTextures)
                    if(t!=null&&!AssetDatabase.Contains(t)&&!owned.Contains(t))owned.Add(t);
                foreach(var o in owned.AsEnumerable().Reverse())if(o!=null)UnityEngine.Object.DestroyImmediate(o);
                if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);
                report.dirtyAfter=scene.isDirty;report.originalAfter=OriginalSnapshot(panel,descriptor);
                File.WriteAllText(report.directory+"/report.json",JsonUtility.ToJson(report,true));
                File.WriteAllText("Temp/halloween-parade/ui-preview/latest.json",JsonUtility.ToJson(report,true));
            }
            if(report.dirtyAfter||report.originalBefore!=report.originalAfter)throw new InvalidOperationException("UI preview changed source state; inspect report and do not save");
            Debug.Log("PARADE_UI_LAYOUTS_OK count="+report.views.Count+" overflow="+report.views.Sum(v=>v.texts.Count(t=>t.overflowing))+" missingEditorGlyphs="+report.fontCoverage.Sum(f=>f.missingCount)+" dir="+report.directory+" (layout evidence only)");
        }

        [MenuItem("Tools/KominkaHillside/Parade/16 - Capture Paid Live Entry")]
        public static void CaptureLiveEntry()
        {
            var activeScene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||activeScene.isDirty)throw new InvalidOperationException("Saved Edit-mode scene required");
            var pouch=UnityEngine.Object.FindObjectsOfType<KominkaDogRun.KD_TreatPouch>(true).Single(p=>p.gameObject.scene==activeScene);
            var panel=UnityEngine.Object.FindObjectsOfType<KH_HalloweenParadePanel>(true).Single(p=>p.gameObject.scene==activeScene);
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var owned=new List<UnityEngine.Object>();var cache=new Dictionary<TMP_FontAsset,TMP_FontAsset>();
            var previous=RenderTexture.active;RenderTexture rt=null;Texture2D image=null;
            string directory="Temp/halloween-parade/live-entry";Directory.CreateDirectory(directory);
            var report=new StringBuilder("Static Live tab preview; cloned UI/fonts. No click or multiplayer simulation.\n");int overflow=0;
            try
            {
                var clone=UnityEngine.Object.Instantiate(pouch.panelRoot);clone.SetActive(false);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,scene);
                foreach(var b in clone.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true))UnityEngine.Object.DestroyImmediate(b);
                foreach(var b in clone.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true))UnityEngine.Object.DestroyImmediate(b);
                foreach(var b in clone.GetComponentsInChildren<Button>(true))b.onClick=new Button.ButtonClickedEvent();
                foreach(var t in clone.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                foreach(var t in clone.GetComponentsInChildren<TextMeshProUGUI>(true)){t.font=CopyFont(t.font,cache,owned);t.fontSharedMaterial=t.font.material;}
                clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.transform.localScale=Vector3.one*.001f;
                var rect=clone.GetComponent<RectTransform>();var canvas=clone.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                for(int i=0;i<pouch.tabPages.Length;i++){var page=clone.transform.Find(AnimationUtility.CalculateTransformPath(pouch.tabPages[i].transform,pouch.panelRoot.transform));if(page!=null)page.gameObject.SetActive(i==KominkaDogRun.KD_TreatPouch.TabLive);}
                foreach(var go in new[]{pouch.liveDetailRoot,pouch.liveDetailBack,pouch.liveDetailAction})if(go!=null){var t=clone.transform.Find(AnimationUtility.CalculateTransformPath(go.transform,pouch.panelRoot.transform));if(t!=null)t.gameObject.SetActive(false);}
                var scroll=clone.transform.Find(AnimationUtility.CalculateTransformPath(pouch.tabScrolls[4].transform,pouch.panelRoot.transform)).GetComponent<ScrollRect>();scroll.gameObject.SetActive(true);
                var entry=scroll.content.Find("LiveRow1");
                var band=clone.transform.Find(AnimationUtility.CalculateTransformPath(pouch.eventBandRoot.transform,pouch.panelRoot.transform));band.gameObject.SetActive(true);
                var labels=new[]{entry.Find("Label").GetComponent<TextMeshProUGUI>(),entry.Find("Sub").GetComponent<TextMeshProUGUI>(),entry.Find("Button/Label").GetComponent<TextMeshProUGUI>(),band.Find("Label").GetComponent<TextMeshProUGUI>()};
                var translations=KominkaDogRun.Editor.KD_LocalizationEditor.Load();
                var title=clone.transform.Find("SectionTitle");var liveTabLabel=clone.transform.Find("Tab4/Label");if(title!=null&&liveTabLabel!=null)title.GetComponent<TextMeshProUGUI>().text=liveTabLabel.GetComponent<TextMeshProUGUI>().text;
                for(int i=0;i<6;i++){var tab=clone.transform.Find("Tab"+i);if(tab!=null){tab.GetComponent<Image>().color=i==4?pouch.tabOnColor:pouch.tabOffColor;if(pouch.tabUnderlines!=null&&i<pouch.tabUnderlines.Length&&pouch.tabUnderlines[i]!=null){var line=tab.Find(pouch.tabUnderlines[i].name);if(line!=null)line.gameObject.SetActive(i==4);}}}
                var cameraGo=new GameObject("LiveEntryPreviewCamera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);cameraGo.transform.position=new Vector3(0,0,-2);
                var camera=cameraGo.AddComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=rect.rect.height*.0005f;camera.aspect=rect.rect.width/rect.rect.height;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.1f,.1f);
                rt=new RenderTexture((int)rect.rect.width,(int)rect.rect.height,24);camera.targetTexture=rt;image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                clone.SetActive(true);Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);scroll.verticalNormalizedPosition=1;
                for(int language=0;language<panel.languageCodes.Length;language++)
                {
                    string code=panel.languageCodes[language];string L(string k)=>KominkaDogRun.Editor.KD_LocalizationEditor.Text(translations,code,k);
                    string[] values={L(pouch.liveProducts[1].displayNameKey),"300 C",L("KD_TreatPouch.WordLiveMore"),L("event.halloween.text")};
                    scroll.gameObject.SetActive(true);
                    foreach(var go in new[]{pouch.liveDetailRoot,pouch.liveDetailBack,pouch.liveDetailAction})clone.transform.Find(AnimationUtility.CalculateTransformPath(go.transform,pouch.panelRoot.transform)).gameObject.SetActive(false);
                    for(int i=0;i<labels.Length;i++){labels[i].text=values[i];labels[i].ForceMeshUpdate();bool bad=labels[i].isTextOverflowing||(labels[i].enableAutoSizing&&labels[i].fontSize<=labels[i].fontSizeMin+.05f);overflow+=bad?1:0;report.AppendLine(code+" "+labels[i].transform.parent.name+" overflowOrMinSize="+bad+" pt="+labels[i].fontSize+" rect="+labels[i].rectTransform.rect.size);}
                    void Capture(string suffix){if(code!="ja"&&code!="en")return;Canvas.ForceUpdateCanvases();foreach(var mask in clone.GetComponentsInChildren<RectMask2D>())mask.PerformClipping();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(directory+"/"+code+"-"+suffix+".png",image.EncodeToPNG());}
                    Capture("live-entry");
                    scroll.gameObject.SetActive(false);
                    foreach(var go in new[]{pouch.liveDetailRoot,pouch.liveDetailBack,pouch.liveDetailAction})clone.transform.Find(AnimationUtility.CalculateTransformPath(go.transform,pouch.panelRoot.transform)).gameObject.SetActive(true);
                    foreach(var go in new[]{pouch.liveDetailSongsRow,pouch.liveDetailFxRow,pouch.liveDetailAccRow})if(go!=null)clone.transform.Find(AnimationUtility.CalculateTransformPath(go.transform,pouch.panelRoot.transform)).gameObject.SetActive(false);
                    var originals=new[]{pouch.liveDetailTitle,pouch.liveDetailMeta,pouch.liveDetailMood,pouch.liveDetailScene,pouch.liveDetailNote,pouch.liveDetailActionLabel,pouch.liveDetailPosterLabel};
                    string prefix="live.detail.hillside_halloween_parade.";
                    string[] detailValues={L(prefix+"jp"),L(prefix+"meta")+" · 300 C",L(prefix+"mood"),L(prefix+"scene"),L("live.parade.pending"),L("KD_TreatPouch.WordBuyHere"),L(prefix+"jp")};
                    for(int i=0;i<originals.Length;i++){var text=clone.transform.Find(AnimationUtility.CalculateTransformPath(originals[i].transform,pouch.panelRoot.transform)).GetComponent<TextMeshProUGUI>();text.text=detailValues[i];text.ForceMeshUpdate();bool bad=text.isTextOverflowing||(text.enableAutoSizing&&text.fontSize<=text.fontSizeMin+.05f);overflow+=bad?1:0;report.AppendLine(code+" detail/"+text.name+" overflowOrMinSize="+bad+" pt="+text.fontSize);}
                    var buy=clone.transform.Find(AnimationUtility.CalculateTransformPath(pouch.liveDetailAction.transform,pouch.panelRoot.transform));buy.GetComponent<Button>().interactable=false;buy.GetComponent<Image>().color=pouch.recallOffColor;
                    Capture("live-detail-pending");
                }
            }
            finally
            {
                RenderTexture.active=previous;UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                if(image!=null)UnityEngine.Object.DestroyImmediate(image);if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}foreach(var o in owned.AsEnumerable().Reverse())if(o!=null)UnityEngine.Object.DestroyImmediate(o);
            }
            report.AppendLine("overflow="+overflow+" sourceSceneDirty="+activeScene.isDirty);File.WriteAllText(directory+"/layout.txt",report.ToString());
            if(overflow!=0||activeScene.isDirty)throw new InvalidOperationException("Live entry layout needs inspection: "+report);
            Debug.Log("PARADE_LIVE_ENTRY_CAPTURE_OK: 13 languages, overflow=0; static layout only");
        }

        static T Mapped<T>(T original,KH_HalloweenParadePanel panel,Transform copy) where T:Component
        {
            if(original==null||!original.transform.IsChildOf(panel.surface.transform))return null;
            string path=AnimationUtility.CalculateTransformPath(original.transform,panel.surface.transform);
            var t=string.IsNullOrEmpty(path)?copy:copy.Find(path);
            if(t==null)throw new InvalidOperationException("Missing cloned UI path "+path);
            return t.GetComponent<T>();
        }
        static void Active(Button original,KH_HalloweenParadePanel panel,Transform copy,bool active)
        {var b=Mapped(original,panel,copy);if(b!=null)b.gameObject.SetActive(active);}
        static void ApplyLayout(KH_HalloweenParadePanel panel,Transform copy,View v)
        {
            int lang=Array.IndexOf(panel.languageCodes,v.language);if(lang<0)throw new InvalidOperationException(v.language);
            Func<int,string> word=n=>panel.words[lang*panel.wordCount+n];
            // Mirrors the named branches in the current _Refresh; it does not simulate their transitions.
            v.phase=v.state=="consent"?1:v.state=="restore"?6:v.state=="playing"?5:7;
            v.needs=v.phase==1;v.host=v.phase==5;v.reduced=v.phase==5;v.muted=v.phase==5;
            v.message=v.needs?2:v.phase==6?32:v.phase==5?6:22;
            v.recoveryFailedMask=v.message==22?63:0;
            string body=word(v.message);
            if(v.message==22)body+="\n"+word(25)+" "+string.Join(", ",Enumerable.Range(0,6).Select(i=>DogName(panel,v.language,i)));
            Mapped(panel.title,panel,copy).text=word(v.needs?20:0);
            Mapped(panel.body,panel,copy).text=body;
            Mapped(panel.progress,panel,copy).text=v.phase==1?word(33).Replace("{0}",v.pending.ToString()).Replace("{1}",v.consentSeconds.ToString()):v.phase==5?word(34).Replace("{0}",v.remainingSeconds.ToString()):"";
            for(int i=0;i<panel.buttonLabels.Length&&i<panel.buttonWords.Length;i++)
            {
                var label=Mapped(panel.buttonLabels[i],panel,copy);if(label==null)continue;
                int key=panel.buttonWords[i],index=key==16?(v.reduced?27:26):key==17?(v.muted?29:28):key==24&&!v.needs?30:key;
                label.text=word(index);bool selected=key==16&&v.reduced||key==17&&v.muted;
                label.color=selected?new Color(1,.75f,.35f):new Color(.96f,.94f,.88f);
            }
            Active(panel.startButton,panel,copy,false);Active(panel.requestButton,panel,copy,false);
            Active(panel.cancelButton,panel,copy,v.phase>=1&&v.phase<=5&&v.host);
            Active(panel.approveButton,panel,copy,v.needs);Active(panel.declineButton,panel,copy,v.needs);
        }
        static string DogName(KH_HalloweenParadePanel panel,string code,int dog)
        {
            string key=panel.dogNameKeys!=null&&dog<panel.dogNameKeys.Length?panel.dogNameKeys[dog]:"";
            var language=panel.language;string name=key;
            if(language!=null)
            {
                int li=Array.IndexOf(language.languageCodes,code),fallback=Array.IndexOf(language.languageCodes,language.fallbackLanguage);
                int keyIndex=Array.IndexOf(language.keys??new string[0],key);var values=language.texts;int count=language.keys==null?0:language.keys.Length;
                if(keyIndex<0){keyIndex=Array.IndexOf(language.extensionKeys??new string[0],key);values=language.extensionTexts;count=language.extensionKeys==null?0:language.extensionKeys.Length;}
                if(keyIndex>=0&&values!=null)
                {
                    int at=li*count+keyIndex;if(li>=0&&at<values.Length)name=values[at];
                    if(string.IsNullOrEmpty(name)&&fallback>=0&&fallback*count+keyIndex<values.Length)name=values[fallback*count+keyIndex];
                }
            }
            return !string.IsNullOrEmpty(name)&&name!=key?name:panel.dogNames!=null&&dog<panel.dogNames.Length?panel.dogNames[dog]:(dog+1).ToString();
        }
        static TMP_FontAsset CopyFont(TMP_FontAsset source,Dictionary<TMP_FontAsset,TMP_FontAsset> cache,List<UnityEngine.Object> owned)
        {
            if(source==null)return null;if(cache.TryGetValue(source,out var existing))return existing;
            var font=UnityEngine.Object.Instantiate(source);font.name=source.name+" (layout copy)";font.hideFlags=HideFlags.HideAndDontSave;cache.Add(source,font);owned.Add(font);
            font.atlasTextures=source.atlasTextures.Select(t=>{if(t==null)return null;var c=UnityEngine.Object.Instantiate(t);c.hideFlags=HideFlags.HideAndDontSave;owned.Add(c);return c;}).ToArray();
            if(source.material!=null){var m=UnityEngine.Object.Instantiate(source.material);m.hideFlags=HideFlags.HideAndDontSave;owned.Add(m);m.mainTexture=font.atlasTextures[0];font.material=m;}
            font.fallbackFontAssetTable=(source.fallbackFontAssetTable??new List<TMP_FontAsset>()).Select(f=>CopyFont(f,cache,owned)).Where(f=>f!=null).ToList();
            return font;
        }
        static string OriginalSnapshot(KH_HalloweenParadePanel panel,VRC.SDKBase.VRC_SceneDescriptor descriptor)
        {
            var text=new StringBuilder(EditorJsonUtility.ToJson(panel));text.Append(EditorJsonUtility.ToJson(descriptor));
            foreach(var c in panel.surface.GetComponentsInChildren<Component>(true))if(c!=null)text.Append(EditorJsonUtility.ToJson(c));
            foreach(var f in panel.surface.GetComponentsInChildren<TextMeshProUGUI>(true).Select(t=>t.font).Distinct())if(f!=null)text.Append(EditorJsonUtility.ToJson(f));
            return Hash(text.ToString());
        }
        static string Hash(string text)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
    }
}
