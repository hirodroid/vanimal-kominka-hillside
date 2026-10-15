using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon;
using KominkaDogRun;
using KominkaDogRun.Editor;

namespace KominkaHillside.Editor
{
    public static partial class KH_WalkIntroEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/Walk Intro/3 Verify")]
        public static void VerifyMenu() { Verify(); }
        public static string Verify()
        {
            Guard(); var s=Find<KH_WalkIntro>();var u=UdonSharpEditorUtility.GetBackingUdonBehaviour(s);
            int total=0,fail=0;var log=new StringBuilder();
            Action<bool,string> check=(ok,msg)=>{total++;if(!ok)fail++;log.AppendLine((ok?"PASS ":"FAIL ")+msg);};
            check(s.gameObject.activeInHierarchy && s.enabled && u.enabled,"observer always enabled");
            check(u.SyncMethod==Networking.SyncType.None,"actual Udon sync None");
            check(u.programSource==AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Program+".asset"),"program source");
            check(s.GetComponent<VRCObjectSync>()==null,"no network transform");
            check(s.manager!=null && s.pouch!=null && s.language!=null && s.manager.walkIntro==s,"manager/pouch/language and callback");
            foreach(string field in new[]{"manager","pouch","language"})
            {
                object stored;u.publicVariables.TryGetVariableValue(field,out stored);
                var proxy=typeof(KH_WalkIntro).GetField(field).GetValue(s) as UdonSharp.UdonSharpBehaviour;
                check(proxy!=null && stored as UnityEngine.Object==UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy),"serialized backing "+field);
            }
            object observer;UdonSharpEditorUtility.GetBackingUdonBehaviour(s.manager).publicVariables.TryGetVariableValue("walkIntro",out observer);
            check(observer as UnityEngine.Object==u,"manager serialized observer");
            check(s.enterRadius==10 && s.leaveRadius==12 && s.rackRadius==2 && s.readyDelay==2 && s.successSeconds==5,"authored thresholds");
            check(s.entrance!=null && s.entrance.name=="KH_LeashRack" && s.menuTarget!=null,"entrance and real menu plate");
            object savedRing; u.publicVariables.TryGetVariableValue("pickupRing",out savedRing);
            check(s.pickupRing!=null && savedRing as UnityEngine.Object==s.pickupRing && !s.pickupRing.gameObject.activeSelf,"pickup ring wired in actual Udon and hidden");
            check(s.pickupRing.GetComponentsInChildren<Collider>(true).Length==0 && Mathf.Abs(s.pickupRing.GetComponent<Renderer>().bounds.size.x-.21f)<.001f,"pickup ring encloses one tool without intercepting input");
            var guide=s.entrance.Find("WalkGuide/Canvas");var bar=s.entrance.Find("Crossbar");
            check(Vector3.Dot(s.menuTarget.forward,guide.forward)>.999f && s.menuTarget.localPosition.z+s.menuTarget.localScale.z*.5f < bar.localPosition.z-bar.localScale.z*.5f,"entrance menu plate faces same side as guide and clears crossbar front");
            check(Vector3.Dot(s.closePanel.transform.forward,guide.forward)>.999f,"close panel faces entrance guide direction");
            check(!s.arrow.gameObject.activeSelf && !s.messagePanel.activeSelf && !s.closePanel.activeSelf,"visuals hidden in saved scene");
            object markStored;u.publicVariables.TryGetVariableValue("destinationMark",out markStored);
            check(s.destinationMark!=null && markStored as UnityEngine.Object==s.destinationMark && !s.destinationMark.gameObject.activeSelf,"destination marker wired and initially hidden");
            check(s.destinationMark.transform.IsChildOf(s.pouch.tabPages[KD_TreatPouch.TabWalk].transform) && !s.destinationMark.blocksRaycasts && s.destinationMark.GetComponentsInChildren<Image>(true).All(i=>!i.raycastTarget) && s.destinationMark.GetComponentsInChildren<Collider>(true).Length==0,"destination marker follows walk page without intercepting input");
            check(s.collarRings.Length==s.manager.collarAnchors.Length && s.collarRings.All(t=>t!=null && !t.gameObject.activeSelf),"one hidden local ring per collar");
            check(s.arrow.GetComponentsInChildren<Collider>(true).Length==0 && s.messagePanel.GetComponentsInChildren<Collider>(true).Length==0 && s.collarRings.All(t=>t.GetComponent<Collider>()==null),"read-only visuals have no colliders");
            check(s.arrow.GetComponent<Renderer>().sharedMaterial.renderQueue==5000 && s.collarRings.All(t=>t.GetComponent<Renderer>().sharedMaterial.renderQueue==5000) && s.messageLabel.fontSharedMaterial.renderQueue==5001,"tutorial cues render after night overlay (4999)");
            check(s.closePanel.layer==0 && s.closePanel.GetComponent<VRCUiShape>()!=null && s.closePanel.GetComponent<BoxCollider>()!=null && s.closePanel.GetComponent<GraphicRaycaster>()!=null,"stable close panel input");
            check(s.closePanel.transform.parent==s.transform && s.closePanel.transform!=s.messageRoot,"close independent from animated/following visuals");
            check(s.pouch.extraSettingsPages.Contains(s.settingsPage) && !s.settingsPage.activeSelf,"settings extra page registered and hidden");
            check(s.settingsEntryLabel.transform.parent.parent==s.pouch.settingsHome.transform,"settings entry is on the existing home");
            var entry=(RectTransform)s.settingsEntryLabel.transform.parent;
            check(entry.anchoredPosition==KD_MenuFinishEditor.HillsideWalkIntroPos && entry.sizeDelta==KD_MenuFinishEditor.HillsideReadEntrySize,"entry fits free right cell"); // IMP-WLD-017: the read row constants (4 x 260)
            { // IMP-WLD-017 (Codex): the seat must not overlap any other active button on the settings home (Credit / Information at 340 wide in an older scene would)
                var r0=new Rect(entry.anchoredPosition-entry.sizeDelta*0.5f,entry.sizeDelta); var hit="";
                foreach(Transform ch in s.pouch.settingsHome.transform){ if(ch==entry||!ch.gameObject.activeSelf||ch.GetComponent<Button>()==null)continue; var rt=(RectTransform)ch; var r1=new Rect(rt.anchoredPosition-rt.sizeDelta*0.5f,rt.sizeDelta); if(r0.Overlaps(r1)){hit=ch.name;break;} }
                check(hit=="","settings entry overlaps no other settings-home button"+(hit==""?"":" ("+hit+")"));
            }
            foreach(var pair in new[]{(s.closeLabel,"_Close"),(s.settingsEntryLabel,"_OpenSettings"),(s.replayLabel,"_Replay"),(s.autoLabel,"_ToggleAuto"),(s.backLabel,"_BackSettings")})
            {
                var button=pair.Item1.transform.parent.GetComponent<Button>();
                var calls=new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                check(calls.arraySize==1 && button.onClick.GetPersistentTarget(0)==u && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue==pair.Item2,"button "+pair.Item2);
            }
            var catalog=KD_LocalizationEditor.Load(); var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            var keys=catalog.languages.Single(l=>l.code=="ja").texts.Where(t=>t.key.StartsWith("walkIntro.")).Select(t=>t.key).ToArray();
            check(keys.Length==17 && catalog.languages.Length==13,"17 onboarding strings x 13 languages");
            foreach(var lang in catalog.languages)
            {
                check(keys.All(k=>lang.texts.Any(t=>t.key==k && !string.IsNullOrWhiteSpace(t.text))),lang.code+" complete translations");
                check(keys.All(k=>s.language._TextForLanguage(k,lang.code)==KD_LocalizationEditor.Text(catalog,lang.code,k)),lang.code+" serialized runtime table");
                if(KD_LocalizationEditor.Bundled(lang))check(KD_ShopSetupEditor.MissingGlyphs(string.Concat(lang.texts.Where(t=>t.key.StartsWith("walkIntro.")).Select(t=>t.text))).Length==0,lang.code+" bundled glyphs");
                else log.AppendLine("DEVICE_CHECK "+lang.code+" uses the existing VRChat client font fallback policy");
            }
            // Real layout with TMP, not just string lengths. Client-fallback glyph metrics require device acceptance.
            string before=s.messageLabel.text; bool was=s.messagePanel.activeSelf;
            try
            {
                s.messagePanel.SetActive(true);
                foreach(var lang in catalog.languages.Where(KD_LocalizationEditor.Bundled)) foreach(string key in keys.Where(k=>new[]{"take","attach","approach","success","destination","pending","noDog","noTool","menu","menuAttach"}.Contains(k.Substring(10))))
                {
                    s.messageLabel.text=KD_LocalizationEditor.Text(catalog,lang.code,key);s.messageLabel.ForceMeshUpdate(true);
                    check(!s.messageLabel.isTextOverflowing,lang.code+" instruction fits "+key);
                }
            }
            finally{s.messageLabel.text=before;s.messagePanel.SetActive(was);}
            check(KH_WalkIntro.ConnectedKey=="KH_WalkIntro.connected.v1" && KH_WalkIntro.HiddenKey=="KH_WalkIntro.hidden.v1","versioned local persistence keys");
            log.AppendLine("TOTAL "+total+" FAILED "+fail);
            Report("verify",log.ToString());return log.ToString();
        }

        // Offscreen Edit-mode captures only. Originals are restored even if rendering throws; no Play or PlayerData writes.
        public static string Capture()
        {
            Guard();var s=Find<KH_WalkIntro>();string dir="Temp/KH_WalkIntro";Directory.CreateDirectory(dir);
            var points=new[]{s.arrow,s.pickupRing,s.messageRoot,s.closePanel.transform}.Concat(s.collarRings).ToArray();
            var positions=points.Select(t=>t.position).ToArray();var rotations=points.Select(t=>t.rotation).ToArray();
            var scales=points.Select(t=>t.localScale).ToArray();var active=points.Select(t=>t.gameObject.activeSelf).ToArray();
            var ui=s.GetComponentsInChildren<Component>(true).Where(c=>c is RectTransform || c is TMP_Text || c is Canvas).ToArray();
            var uiBefore=ui.Select(c=>EditorJsonUtility.ToJson(c)).ToArray();
            string before=s.messageLabel.text;
            Vector3 eye=new Vector3(47.639f,3.206f,40.084f);Vector3 look=eye+Quaternion.Euler(0,352,0)*Vector3.forward*5;
            try
            {
                s.messagePanel.SetActive(true);s.closePanel.SetActive(true);
                s.arrow.gameObject.SetActive(true);s.pickupRing.gameObject.SetActive(true);
                var clip=s.manager.localClips.Where(c=>Vector3.Distance(c.transform.position,s.entrance.position)<s.rackRadius).OrderBy(c=>Vector3.Distance(c.transform.position,eye)).First();
                PreviewTakeCue(s,clip.transform,eye);
                s.messageRoot.rotation=Quaternion.LookRotation(new Vector3(s.messageRoot.position.x-eye.x,0,s.messageRoot.position.z-eye.z));
                s.messageLabel.text=s.language._TextForLanguage("walkIntro.take","ja");
                KH_ViewShotsEditor.Shot("take-spawn",eye,look,70,dir);
                KH_ViewShotsEditor.Shot("take-low",eye-Vector3.up*.55f,look-Vector3.up*.55f,70,dir);
                var rackEye=s.entrance.position+new Vector3(.5f,.8f,-1.5f);
                PreviewTakeCue(s,clip.transform,rackEye);
                KH_ViewShotsEditor.Shot("take-close",rackEye,s.entrance.position+Vector3.up*.25f,65,dir);
                PreviewTakeCue(s,clip.transform,eye);
                // The world's actual night recipe, with temporary material instances only.
                var night=Find<KH_NightMode>();var oldSky=RenderSettings.skybox;var oldOverlay=night.overlayRenderer.sharedMaterial;bool wasNight=night.overlayRenderer.enabled;
                var sky=new Material(oldSky);var overlay=new Material(oldOverlay);
                try
                {
                    sky.SetFloat("_Exposure",KH_NightMode.NightSkyExposure);sky.SetColor("_Tint",new Color(KH_NightMode.NightTintR,KH_NightMode.NightTintG,KH_NightMode.NightTintB,KH_NightMode.SkyTintA));
                    overlay.SetFloat("_Darkness",1f);RenderSettings.skybox=sky;night.overlayRenderer.sharedMaterial=overlay;night.overlayRenderer.enabled=true;
                    KH_ViewShotsEditor.Shot("take-night",eye,look,70,dir);
                }
                finally{RenderSettings.skybox=oldSky;night.overlayRenderer.sharedMaterial=oldOverlay;night.overlayRenderer.enabled=wasNight;UnityEngine.Object.DestroyImmediate(sky);UnityEngine.Object.DestroyImmediate(overlay);}
                s.arrow.gameObject.SetActive(false);s.pickupRing.gameObject.SetActive(false);s.messageLabel.text=s.language._TextForLanguage("walkIntro.attach","ja");
                var nearEye=s.manager.dogRoots[0].position+new Vector3(0,1.6f,-1.8f);
                var nearLook=s.manager.dogRoots[0].position+Vector3.up*.7f;
                s.messageRoot.position=(Vector3)typeof(KH_WalkIntro).GetMethod("HintPosition",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s,new object[]{0,nearEye});
                s.messageRoot.rotation=Quaternion.LookRotation(new Vector3(s.messageRoot.position.x-nearEye.x,0,s.messageRoot.position.z-nearEye.z));
                foreach(var ring in s.collarRings){ring.gameObject.SetActive(true);ring.rotation=Quaternion.identity;}
                KH_ViewShotsEditor.Shot("attach-layout",nearEye,nearLook,70,dir);
                foreach(var ring in s.collarRings)ring.gameObject.SetActive(false);
                s.closePanel.SetActive(false);s.messageLabel.text=s.language._TextForLanguage("walkIntro.success","ja");
                KH_ViewShotsEditor.Shot("success-layout",nearEye,nearLook,70,dir);
                s.messagePanel.SetActive(false);
                CaptureSettings(s,dir);
            }
            finally
            {
                for(int i=0;i<points.Length;i++){points[i].SetPositionAndRotation(positions[i],rotations[i]);points[i].localScale=scales[i];points[i].gameObject.SetActive(active[i]);}
                s.messageLabel.text=before;
                for(int i=0;i<ui.Length;i++)EditorJsonUtility.FromJsonOverwrite(uiBefore[i],ui[i]);
            }
            return dir;
        }

        static void PreviewTakeCue(KH_WalkIntro s,Transform clip,Vector3 eye)
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var target=typeof(KH_WalkIntro).GetField("_arrowTarget",flags);var previous=target.GetValue(s);
            try
            {
                target.SetValue(s,clip);
                typeof(KH_WalkIntro).GetMethod("UpdateTakeCue",flags).Invoke(s,new object[]{eye,.5f});
                s.messageRoot.rotation=Quaternion.LookRotation(new Vector3(s.messageRoot.position.x-eye.x,0,s.messageRoot.position.z-eye.z));
            }
            finally { target.SetValue(s,previous); }
        }

        static void CaptureSettings(KH_WalkIntro s,string dir)
        {
            var p=s.pouch;var panel=p.panelRoot.transform;var entries=new[]{p.panelRoot,p.settingsPage,p.settingsHome,s.settingsPage,p.welcomePage}.Concat(p.tabPages).Where(g=>g!=null).Concat(p.extraSettingsPages).Distinct().ToArray();
            bool[] was=entries.Select(g=>g.activeSelf).ToArray();
            // Enabling a saved-inactive TMP/Slider also changes derived serialized properties in Edit mode.
            // Snapshot those components, not just visibility, so previewing cannot change the authored menu.
            var components=p.panelRoot.GetComponentsInChildren<Component>(true).Where(c=>c is RectTransform || c is TMP_Text || c is Canvas).ToArray();
            var snapshots=components.Select(c=>EditorJsonUtility.ToJson(c)).ToArray();
            try
            {
                panel.SetPositionAndRotation(new Vector3(47.639f,3.006f,41.4f),Quaternion.identity);
                p.sectionTitleLabel.text=s.language._TextForLanguage("KD_TreatPouch.WordSettings","ja");
                foreach(var g in entries)g.SetActive(false);
                p.panelRoot.SetActive(true);p.settingsPage.SetActive(true);p.settingsHome.SetActive(true);
                var center=panel.position;var camera=center-panel.forward*1.35f;
                KH_ViewShotsEditor.Shot("settings-entry",camera,center,60,dir);
                p.settingsHome.SetActive(false);s.settingsPage.SetActive(true);
                p.sectionTitleLabel.text=s.language._TextForLanguage("walkIntro.title","ja");
                KH_ViewShotsEditor.Shot("settings-page",camera,center,60,dir);
            }
            finally
            {
                for(int i=0;i<entries.Length;i++)entries[i].SetActive(was[i]);
                for(int i=0;i<components.Length;i++)EditorJsonUtility.FromJsonOverwrite(snapshots[i],components[i]);
            }
        }

        public static string CaptureDestination()
        {
            Guard();var original=Find<KH_WalkIntro>();string dir="Temp/KH_WalkIntro";Directory.CreateDirectory(dir);
            var clone=UnityEngine.Object.Instantiate(original.pouch.gameObject);clone.name="Destination preview";clone.hideFlags=HideFlags.HideAndDontSave;
            var hint=UnityEngine.Object.Instantiate(original.messageRoot.gameObject);hint.hideFlags=HideFlags.HideAndDontSave;
            bool shown=original.pouch.panelRoot.activeSelf;
            try
            {
                original.pouch.panelRoot.SetActive(false);
                var p=clone.GetComponent<KD_TreatPouch>();p.panelRoot.SetActive(true);p.settingsPage.SetActive(false);p.welcomePage.SetActive(false);
                foreach(var page in p.tabPages)page.SetActive(false);p.tabPages[KD_TreatPouch.TabWalk].SetActive(true);
                var mark=p.tabPages[KD_TreatPouch.TabWalk].transform.Find("WalkIntroDestinationMark");mark.gameObject.SetActive(true);mark.GetComponent<CanvasGroup>().alpha=1;
                p.sectionTitleLabel.text=original.language._TextForLanguage("KD_TreatPouch.WordWalkTitle","ja");
                Vector3 dog=original.manager.collarAnchors[0].position;Vector3 eye=dog+new Vector3(0,1.0f,-2f);
                typeof(KD_TreatPouch).GetMethod("PlaceWalkIntroBoard",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(p,new object[]{dog,eye,Vector3.forward});
                var panel=(RectTransform)p.panelRoot.transform;
                hint.SetActive(true);foreach(var g in hint.GetComponentsInChildren<CanvasGroup>(true))g.alpha=1;
                foreach(var t in hint.GetComponentsInChildren<TMP_Text>(true))t.text=original.language._TextForLanguage("walkIntro.destination","ja");
                hint.transform.position=panel.TransformPoint(new Vector3(panel.rect.center.x,panel.rect.yMax+120,0));hint.transform.rotation=p.transform.rotation;
                Canvas.ForceUpdateCanvases();
                KH_ViewShotsEditor.Shot("destination-layout",eye,panel.position,65,dir);
            }
            finally{UnityEngine.Object.DestroyImmediate(clone);UnityEngine.Object.DestroyImmediate(hint);original.pouch.panelRoot.SetActive(shown);}
            return dir+"/destination-layout.png";
        }

        // Execute the real completion transition in an isolated clone. SDK I/O is intentionally excluded:
        // no OnPlayerRestored, no player spoofing, and _restored stays false (therefore no PlayerData writes).
        public static string ProbeTransitions()
        {
            Guard();var original=Find<KH_WalkIntro>();var clone=UnityEngine.Object.Instantiate(original.gameObject);
            clone.name="WalkIntro Probe (temporary)";clone.hideFlags=HideFlags.HideAndDontSave;
            var s=clone.GetComponent<KH_WalkIntro>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            Action<string,object> set=(k,v)=>typeof(KH_WalkIntro).GetField(k,flags).SetValue(s,v);
            Func<string,object> get=k=>typeof(KH_WalkIntro).GetField(k,flags).GetValue(s);
            Action<int> observe=mask=>typeof(KH_WalkIntro).GetMethod("ObserveMask",flags).Invoke(s,new object[]{mask});
            var log=new StringBuilder();int count=0,failed=0;
            Action<bool,string> check=(ok,name)=>{count++;if(!ok)failed++;log.AppendLine((ok?"PASS ":"FAIL ")+name);};
            try
            {
                set("_restored",false);set("_completed",false);set("_sessionSuccess",false);set("_state",KH_WalkIntro.Quiet);set("_engaged",false);set("_seenMask",0);
                observe(0);check(!(bool)get("_completed"),"no attachment does not complete");
                observe(4);check((bool)get("_sessionSuccess") && (bool)get("_completed"),"attachment before persistence restore is remembered");
                observe(0);check((bool)get("_sessionSuccess") && (bool)get("_completed"),"detach before restore does not erase first success");
                check((int)get("_state")==KH_WalkIntro.Quiet,"hidden success produces no surprise toast");
                set("_engaged",true);set("_state",KH_WalkIntro.Pending);observe(2);
                check((int)get("_state")==KH_WalkIntro.Success && (int)get("_successDog")==1,"confirmed attachment wins over pending");
                var hint=typeof(KH_WalkIntro).GetMethod("SuccessHint",flags);
                check((string)hint.Invoke(s,new object[]{5.1f})=="success" && (string)hint.Invoke(s,new object[]{5f})=="destination", "departure precedes destination hint without a new task");
                float until=(float)get("_successUntil");observe(2);
                check((int)get("_state")==KH_WalkIntro.Success && (float)get("_successUntil")==until,"repeat snapshot or clip auto-drop cannot restart toast");
                observe(0);check((int)get("_state")==KH_WalkIntro.Quiet && (bool)get("_completed"),"immediate release closes toast, preserves completion");
                set("_dismissed",true);set("_engaged",false);observe(1);
                check((int)get("_state")==KH_WalkIntro.Quiet && (bool)get("_completed"),"session dismissal still records independent attachment");
                set("_replay",true);set("_engaged",false);observe(8);
                check(!(bool)get("_replay"),"attachment before replay is drawn ends the replay without an extra task");
                check(!(bool)get("_restored"),"all probes excluded persistence I/O");
            }
            finally{UnityEngine.Object.DestroyImmediate(clone);}
            log.AppendLine("TOTAL "+count+" FAILED "+failed+" (Edit-mode transitions; not SDK event/persistence acceptance)");
            Report("transitions",log.ToString());return log.ToString();
        }
    }
}
