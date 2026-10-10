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
        const string Program = "Assets/KominkaHillside/Scripts/KH_WalkIntro";
        const string Data = "Assets/KominkaHillside/Models/WalkIntro/";
        const string World = "Assets/Scenes/VRCDefaultWorldScene.unity";
        const string Root = "KH_WalkIntro";
        static readonly Color Ink = new Color(.97f,.96f,.89f), Surface = new Color(.08f,.095f,.085f,.96f);
        static void Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != World)
                throw new InvalidOperationException("Open the hillside scene in Edit mode");
        }
        static T Find<T>() where T : Component { return UnityEngine.Object.FindObjectsOfType<T>(true).Single(); }
        static void Save(UdonSharpBehaviour b)
        { UdonSharpEditorUtility.CopyProxyToUdon(b); EditorUtility.SetDirty(b); EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(b)); }

        [MenuItem("Tools/KominkaHillside/Course/Walk Intro/1 Create Program")]
        public static void CreateProgram()
        {
            Guard();
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Program + ".asset") != null) return;
            var a = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            a.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(Program + ".cs");
            AssetDatabase.CreateAsset(a, Program + ".asset"); AssetDatabase.SaveAssets();
        }

        public static string SetupDestinationCue()
        {
            Guard(); var intro=Find<KH_WalkIntro>(); var pouch=intro.pouch;
            var page=(RectTransform)pouch.tabPages[KD_TreatPouch.TabWalk].transform;
            Vector2 min=new Vector2(float.MaxValue,float.MaxValue),max=new Vector2(float.MinValue,float.MinValue);
            var corners=new Vector3[4];
            foreach(var button in pouch.walkHereButtons)
            {
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach(var point in corners){Vector2 p=page.InverseTransformPoint(point);min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
            }
            min-=Vector2.one*10;max+=Vector2.one*10;
            var mark=page.Find("WalkIntroDestinationMark") as RectTransform;
            if(mark==null){var go=new GameObject("WalkIntroDestinationMark",typeof(RectTransform),typeof(CanvasGroup));mark=(RectTransform)go.transform;mark.SetParent(page,false);}
            mark.anchorMin=mark.anchorMax=page.pivot;mark.pivot=Vector2.one*.5f;mark.anchoredPosition=(min+max)*.5f;mark.sizeDelta=max-min;mark.SetAsLastSibling();
            var group=mark.GetComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;
            for(int i=0;i<4;i++)for(int axis=0;axis<2;axis++)for(int layer=0;layer<2;layer++)
            {
                string name="Corner"+i+"_"+axis+"_"+layer;var t=mark.Find(name) as RectTransform;
                if(t==null){var go=new GameObject(name,typeof(RectTransform),typeof(Image));t=(RectTransform)go.transform;t.SetParent(mark,false);}
                float x=i%2,y=i/2;t.anchorMin=t.anchorMax=new Vector2(x,y);t.pivot=new Vector2(x,y);t.anchoredPosition=layer==0?new Vector2(x==0?-2:2,y==0?-2:2):Vector2.zero;
                float length=layer==0?46:42,thickness=layer==0?9:5;
                t.sizeDelta=axis==0?new Vector2(length,thickness):new Vector2(thickness,length);
                var image=t.GetComponent<Image>();image.color=layer==0?new Color32(0x36,0x4E,0x3C,255):new Color32(0xFF,0xFD,0xF8,255);image.raycastTarget=false;
            }
            group.gameObject.SetActive(false);intro.destinationMark=group;Save(intro);EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return "destination corners around "+pouch.walkHereButtons.Length+" buttons: "+mark.sizeDelta;
        }

        // Refresh only the language data; preserve authored scene geometry and all menu state.
        public static string RefreshText()
        {
            Guard(); var language=Find<KD_Language>(); var catalog=KD_LocalizationEditor.Load(); var log=new StringBuilder();
            KD_LocalizationEditor.Configure(language,catalog,log);
            string glyphs=string.Concat(catalog.languages.Where(KD_LocalizationEditor.Bundled).SelectMany(l=>l.texts).Where(t=>t.key.StartsWith("walkIntro.")).Select(t=>t.text));
            KD_ShopSetupEditor.AddGlyphs(glyphs,log); Save(language);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            return log.ToString();
        }

        [MenuItem("Tools/KominkaHillside/Course/Walk Intro/2 Setup")]
        public static void Setup()
        {
            Guard();
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Program + ".asset") == null)
                throw new InvalidOperationException("Create Program and compile Udon first");
            var manager = Find<KH_LeashManager>(); var pouch = Find<KD_TreatPouch>(); var language = Find<KD_Language>();
            var rack = GameObject.Find("KH_Dogs/KH_LeashRack").transform;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            var catalog = KD_LocalizationEditor.Load(); var log = new StringBuilder();
            KD_LocalizationEditor.Configure(language, catalog, log);
            string glyphs = string.Concat(catalog.languages.Where(KD_LocalizationEditor.Bundled).SelectMany(l => l.texts).Where(t => t.key.StartsWith("walkIntro.")).Select(t => t.text));
            KD_ShopSetupEditor.AddGlyphs(glyphs, log); Save(language);
            Directory.CreateDirectory(Data); AssetDatabase.Refresh();
            var root = GameObject.Find(Root); if (root == null) root = new GameObject(Root);
            var intro = root.GetComponent<KH_WalkIntro>(); if (intro == null) intro = root.AddUdonSharpComponent<KH_WalkIntro>();
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(intro); udon.SyncMethod = Networking.SyncType.None;
            intro.manager = manager; intro.pouch = pouch; intro.language = language; intro.entrance = rack;
            intro.menuTarget = rack.Find("MenuPlate");
            intro.enterRadius = 10f; intro.leaveRadius = 12f; intro.rackRadius = 2f; intro.readyDelay = 2f; intro.successSeconds = 5f;
            var arrow = Child(root.transform, "Arrow");
            MeshObject(arrow, ArrowMesh(), Mat("Arrow",new Color(1f,.69f,.15f)));
            var outline = Child(arrow, "Outline");
            MeshObject(outline, ArrowMesh(), Mat("Outline",Ink));
            outline.localPosition = new Vector3(0,.225f, .003f); outline.localScale = new Vector3(1.10f,1.07f,1);
            // mesh bottom is y=0; keep the expanded silhouette centered around the same shape
            outline.localPosition -= new Vector3(0,.225f * 1.07f,0);
            intro.arrow = arrow; arrow.gameObject.SetActive(false);
            var message = WorldCanvas(root.transform, "Message", new Vector2(600,120), false, out bool madeMessage);
            if (madeMessage) { message.position = rack.position + new Vector3(0,.95f,-.28f); message.rotation = rack.rotation; }
            Background(message, new Vector2(600,120));
            intro.messageLabel = Label(message, "Text", Vector2.zero, new Vector2(564,104), font, T(catalog,"take"), 34,24);
            intro.messageRoot = message; intro.messagePanel = message.gameObject;
            intro.messageFade = Comp<CanvasGroup>(message.gameObject); intro.messageFade.blocksRaycasts = false;
            var close = WorldCanvas(root.transform, "Close",new Vector2(130,48), true, out bool madeClose);
            if (madeClose) { close.position = rack.position + new Vector3(.78f,.90f,-.30f); close.rotation = rack.rotation; }
            intro.closeLabel = Button(close,"Button",Vector2.zero,new Vector2(130,48),font,T(catalog,"close"),udon,"_Close");
            intro.closePanel = close.gameObject;
            ConfigureTakeCue(intro, rack);
            message.gameObject.SetActive(false); close.gameObject.SetActive(false);
            intro.collarRings = new Transform[manager.collarAnchors.Length];
            var rings = Child(root.transform,"CollarRings");
            for (int i=0;i<intro.collarRings.Length;i++)
            {
                var ring=Child(rings,"Ring"+i); MeshObject(ring,RingMesh(),Mat("Collar",new Color(.45f,1f,.59f)));
                ring.position=manager.collarAnchors[i].position; ring.gameObject.SetActive(false); intro.collarRings[i]=ring;
            }
            var page=KD_ShopSetupEditor.EnsureUiChild(pouch.settingsPage.transform,"WalkIntroPage",Vector2.zero,new Vector2(1140,700));
            intro.settingsPage=page;
            intro.settingsNote=Label(page.transform,"Note",new Vector2(0,90),new Vector2(950,100),font,T(catalog,"note"),28,22);
            intro.replayLabel=Button(page.transform,"Replay",new Vector2(0,-20),new Vector2(640,70),font,T(catalog,"replay"),udon,"_Replay");
            intro.autoLabel=Button(page.transform,"Auto",new Vector2(0,-115),new Vector2(640,70),font,T(catalog,"autoOn"),udon,"_ToggleAuto");
            intro.backLabel=Button(page.transform,"Back",new Vector2(-350,-245),new Vector2(340,60),font,T(catalog,"back"),udon,"_BackSettings");
            // Existing settings home has a free right cell on the Read row. Keep all other entries untouched.
            intro.settingsEntryLabel=Button(pouch.settingsHome.transform,"WalkIntroButton",new Vector2(455,-214),new Vector2(190,60),font,T(catalog,"title"),udon,"_OpenSettings");
            pouch.extraSettingsPages=(pouch.extraSettingsPages ?? new GameObject[0]).Where(x=>x!=null).Concat(new[]{page}).Distinct().ToArray();
            page.SetActive(false); manager.walkIntro=intro;
            Save(intro); Save(manager); Save(pouch);
            SetupDestinationCue(); // Full rebuild includes the final destination hint.
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            // Verify is a separate action; the caller saves only after inspecting its result.
            Report("setup",log.ToString()+"\nSetup complete; scene is dirty. Run Verify, then save.");
        }

        // Narrow update for the entrance cue: leaves language tables, settings and dog wiring untouched.
        [MenuItem("Tools/KominkaHillside/Course/Walk Intro/4 Apply Pickup Cue")]
        public static void ApplyTakeCue()
        {
            Guard(); var intro = Find<KH_WalkIntro>();
            ConfigureTakeCue(intro, intro.entrance);
            Save(intro); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
        static void ConfigureTakeCue(KH_WalkIntro intro, Transform rack)
        {
            var guide = rack.Find("WalkGuide/Canvas"); var bar = rack.Find("Crossbar");
            if (guide == null || bar == null || intro.menuTarget == null) throw new InvalidOperationException("Entrance guide and menu plate required");
            // The plate was behind the crossbar (+Z), hidden from the spawn side. Put it just outside the front face.
            var plate = intro.menuTarget; var pos = plate.localPosition;
            pos.z = bar.localPosition.z - bar.localScale.z * .5f - plate.localScale.z * .5f - .005f;
            plate.localPosition = pos; plate.rotation = guide.rotation;
            intro.closePanel.transform.rotation = guide.rotation;
            EditorUtility.SetDirty(plate); EditorUtility.SetDirty(intro.closePanel.transform);
            var ring = Child(intro.transform, "PickupRing");
            MeshObject(ring, RingMesh(), Mat("Arrow", new Color(1f,.69f,.15f)));
            ring.localScale = Vector3.one * 1.4f; // 0.21 m diameter, enclosing one of the two small real clips
            var clip = intro.manager.localClips.Where(c => c != null).OrderBy(c => Vector3.Distance(c.transform.position,rack.position)).First();
            ring.position = clip.transform.position; ring.rotation = guide.rotation;
            ring.gameObject.SetActive(false); intro.pickupRing = ring;
        }

        static string T(KD_LocalizationEditor.Catalog c,string key) { return KD_LocalizationEditor.Text(c,c.fallbackLanguage,"walkIntro."+key); }
        static T Comp<T>(GameObject g) where T:Component { var c=g.GetComponent<T>(); return c!=null?c:g.AddComponent<T>(); }
        static Transform Child(Transform p,string name)
        { var t=p.Find(name); if(t!=null)return t; var g=new GameObject(name); g.transform.SetParent(p,false); return g.transform; }
        static Transform WorldCanvas(Transform parent,string name,Vector2 size,bool interactive,out bool made)
        {
            var t=parent.Find(name); made=t==null;
            if(made){var g=new GameObject(name,typeof(RectTransform)); t=g.transform;t.SetParent(parent,false);}
            var rt=(RectTransform)t;rt.sizeDelta=size;rt.localScale=Vector3.one*.002f;
            t.gameObject.layer=0;Comp<Canvas>(t.gameObject).renderMode=RenderMode.WorldSpace;
            if(interactive)
            {
                Comp<GraphicRaycaster>(t.gameObject);Comp<VRCUiShape>(t.gameObject);
                var box=Comp<BoxCollider>(t.gameObject);box.size=new Vector3(size.x,size.y,1f);box.isTrigger=true;
            }
            return t;
        }
        static void Background(Transform p,Vector2 size)
        {var g=KD_ShopSetupEditor.EnsureUiChild(p,"Background",Vector2.zero,size);var img=Comp<Image>(g);img.color=Surface;img.raycastTarget=false;g.transform.SetAsFirstSibling();}
        static TextMeshProUGUI Label(Transform p,string name,Vector2 pos,Vector2 size,TMP_FontAsset font,string text,float max,float min)
        {
            var g=KD_ShopSetupEditor.EnsureUiChild(p,name,pos,size);var label=Comp<TextMeshProUGUI>(g);
            label.font=font;label.text=text;label.fontSize=max;label.fontSizeMax=max;label.fontSizeMin=min;label.enableAutoSizing=true;
            label.fontSharedMaterial=TextMaterial(font);
            label.enableWordWrapping=true;label.alignment=TextAlignmentOptions.Center;label.color=Ink;label.raycastTarget=false;
            label.overflowMode=TextOverflowModes.Overflow;return label;
        }
        static TextMeshProUGUI Button(Transform p,string name,Vector2 pos,Vector2 size,TMP_FontAsset font,string text,UdonBehaviour target,string evt)
        {
            var g=KD_ShopSetupEditor.EnsureButton(p,name,pos,size,KD_UiSpriteEditor.ActionNeutral,font,text,target,evt,new StringBuilder());
            var label=g.GetComponentInChildren<TextMeshProUGUI>();label.enableAutoSizing=true;label.fontSizeMax=26;label.fontSizeMin=14;
            label.fontSharedMaterial=TextMaterial(font);
            label.enableWordWrapping=true;return label;
        }
        static Material Mat(string name,Color color)
        {
            string path=Data+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("Unlit/Color"));AssetDatabase.CreateAsset(m,path);}m.color=color;
            m.renderQueue=5000; // after KH_NightOverlay (4999); keep the short tutorial legible at night
            EditorUtility.SetDirty(m);return m;
        }
        static Material TextMaterial(TMP_FontAsset font)
        {
            string path=Data+"Text.mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(font.material);AssetDatabase.CreateAsset(m,path);}
            m.CopyPropertiesFromMaterial(font.material);m.renderQueue=5001;EditorUtility.SetDirty(m);return m;
        }
        static void MeshObject(Transform t,Mesh mesh,Material mat)
        {
            Comp<MeshFilter>(t.gameObject).sharedMesh=mesh;var r=Comp<MeshRenderer>(t.gameObject);r.sharedMaterial=mat;
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;
            r.lightProbeUsage=UnityEngine.Rendering.LightProbeUsage.Off;r.reflectionProbeUsage=UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
        static Mesh ArrowMesh()
        {
            string path=Data+"Arrow.asset";var m=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(m!=null)return m;
            m=new Mesh {name="WalkIntro Arrow"};
            m.vertices=new[]{new Vector3(0,0,0),new Vector3(-.15f,.18f,0),new Vector3(-.065f,.18f,0),new Vector3(-.065f,.45f,0),new Vector3(.065f,.45f,0),new Vector3(.065f,.18f,0),new Vector3(.15f,.18f,0)};
            m.triangles=new[]{0,1,6,2,3,4,2,4,5,6,1,0,4,3,2,5,4,2};m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.CreateAsset(m,path);return m;
        }
        static Mesh RingMesh()
        {
            string path=Data+"Ring.asset";var m=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(m!=null)return m;
            var v=new Vector3[64];var tri=new int[32*12];
            for(int i=0;i<32;i++)
            {
                float a=i*Mathf.PI*2/32;v[2*i]=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*.075f;v[2*i+1]=v[2*i]*(.052f/.075f);
                int a0=i*2,b=i*2+1,c=((i+1)%32)*2,d=c+1,k=i*12;
                int[] q={a0,b,c,c,b,d,c,b,a0,d,b,c};for(int j=0;j<12;j++)tri[k+j]=q[j];
            }
            m=new Mesh{name="WalkIntro Collar Ring",vertices=v,triangles=tri};m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.CreateAsset(m,path);return m;
        }
        static void Report(string name,string body)
        {Directory.CreateDirectory("Temp/KH_WalkIntro");File.WriteAllText("Temp/KH_WalkIntro/"+name+".txt",body);Debug.Log("[KH WalkIntro] "+body);}

        public static string Inspect()
        {
            Guard();var p=Find<KD_TreatPouch>();
            return string.Join("\n",p.settingsHome.GetComponentsInChildren<RectTransform>(true).Where(t=>t.parent==p.settingsHome.transform).Select(t=>t.name+" "+t.anchoredPosition+" "+t.sizeDelta));
        }
    }
}
