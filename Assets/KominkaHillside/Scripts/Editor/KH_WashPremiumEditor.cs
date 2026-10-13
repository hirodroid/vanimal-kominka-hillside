using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;
namespace KominkaHillside.Editor
{
public static partial class KH_WashPremiumEditor
{
    const string Root = "Assets/KominkaHillside/Models/WashPremium/";
    const string Prefabs = "Assets/KominkaHillside/Prefabs/WashPremium/";
    const string Menu = "Tools/KominkaHillside/WashPremium/";
    const string ScenePath = "Assets/Scenes/VRCDefaultWorldScene.unity";
    static readonly string[] Names = {"FoamCannon","DuckWaterGun","FishWaterGun","BambooWaterGun","FlowerShower","RainShower","WateringCan","Dryer","PatternTowel","PawMitt","LargeSponge","GroomingBrush","Cologne","HinokiBath","StoneBath","TileBath","FloatingDuck","TelescopicSupport"};
    static bool Fixture(string n) { return n.EndsWith("Bath") || n == "TelescopicSupport"; }
    static void Guard() { if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorSceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Correct scene in edit mode required"); }
    static void Receipt(string n, string s) { Directory.CreateDirectory("Temp/WashPremium"); File.WriteAllText("Temp/WashPremium/" + n + ".txt", DateTime.UtcNow.ToString("o") + "\n" + s); }
    static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    static KH_WashStationCall[] Stations() { return UnityEngine.Object.FindObjectsOfType<KH_WashStationCall>(true).Where(x => x.gameObject.scene.path == ScenePath).OrderBy(x => x.stationIndex).ToArray(); }
    [MenuItem(Menu + "10 - Import Visuals")]
    public static void Import()
    {
        Guard(); Directory.CreateDirectory(Prefabs); AssetDatabase.Refresh();
        foreach (string g in new[]{"Tools","Fixture"})
        {
            foreach (string channel in new[]{"BaseColor","Normal","MetallicSmoothness"})
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(Root + "Premium" + g + "_" + channel + ".png");
                ti.textureType = channel == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = channel == "BaseColor"; ti.alphaSource = channel == "MetallicSmoothness" ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.alphaIsTransparency = false; ti.isReadable = false; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4;
                ti.maxTextureSize = g == "Tools" ? 2048 : 1024;
                var mobile = ti.GetPlatformTextureSettings("Android"); mobile.overridden = true; mobile.maxTextureSize = 1024; mobile.format = TextureImporterFormat.ASTC_6x6; mobile.compressionQuality = 100; ti.SetPlatformTextureSettings(mobile);
                ti.SaveAndReimport();
            }
            string matPath = Root + "PremiumWash_" + g + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat,matPath); }
            mat.color = Color.white; mat.SetFloat("_Metallic",0); mat.SetFloat("_GlossMapScale",1); mat.SetFloat("_BumpScale",.65f);
            mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Premium"+g+"_BaseColor.png"));
            mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Premium"+g+"_Normal.png"));
            mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Premium"+g+"_MetallicSmoothness.png"));
            mat.EnableKeyword("_NORMALMAP"); mat.EnableKeyword("_METALLICGLOSSMAP"); mat.enableInstancing=true; EditorUtility.SetDirty(mat);
        }
        var report = new StringBuilder();
        foreach (string n in Names)
        {
            string path = Root+n+".fbx"; var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            mi.globalScale=1; mi.importAnimation=false; mi.importCameras=false; mi.importLights=false; mi.importBlendShapes=true;
            mi.importNormals=ModelImporterNormals.Import; mi.importTangents=ModelImporterTangents.CalculateMikk; mi.meshCompression=ModelImporterMeshCompression.Off; mi.isReadable=false; mi.materialImportMode=ModelImporterMaterialImportMode.None; mi.SaveAndReimport();
            var holder = new GameObject(n);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path)); model.transform.SetParent(holder.transform,false);
                foreach (var rend in holder.GetComponentsInChildren<Renderer>(true))
                {
                    rend.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"PremiumWash_"+(Fixture(n)?"Fixture":"Tools")+".mat");
                    if (rend is SkinnedMeshRenderer sk) { sk.updateWhenOffscreen=false; SetSupportBounds(sk); }
                }
                PrefabUtility.SaveAsPrefabAsset(holder,Prefabs+n+".prefab");
                report.AppendLine(n+" bounds="+WorldBounds(holder));
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }
        const string scriptRoot="Assets/KominkaHillside/Scripts/KH_WashSupportVisual";
        if(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(scriptRoot+".asset")==null)
        { var p=ScriptableObject.CreateInstance<UdonSharpProgramAsset>(); p.sourceCsScript=AssetDatabase.LoadAssetAtPath<MonoScript>(scriptRoot+".cs"); AssetDatabase.CreateAsset(p,scriptRoot+".asset"); }
        AssetDatabase.SaveAssets(); Receipt("import",report.ToString());
    }
    static int ExtendIndex(SkinnedMeshRenderer sk)
    { for(int i=0;i<sk.sharedMesh.blendShapeCount;i++) if(sk.sharedMesh.GetBlendShapeName(i).EndsWith("Lift_Extend"))return i; throw new InvalidOperationException("Missing Lift_Extend"); }
    static void SetSupportBounds(SkinnedMeshRenderer sk)
    {
        int i=ExtendIndex(sk); float previousWeight=sk.GetBlendShapeWeight(i); var mesh=new Mesh(); var b=new Bounds();
        try { for(int p=0;p<=4;p++){sk.SetBlendShapeWeight(i,p*25);sk.BakeMesh(mesh); mesh.RecalculateBounds(); if(p==0)b=mesh.bounds;else b.Encapsulate(mesh.bounds);} b.Expand(.02f);sk.localBounds=b; }
        finally {sk.SetBlendShapeWeight(i,previousWeight);UnityEngine.Object.DestroyImmediate(mesh);}
    }
    static Bounds WorldBounds(GameObject go)
    { var rr=go.GetComponentsInChildren<Renderer>(true); var b=rr[0].bounds;foreach(var r in rr)b.Encapsulate(r.bounds);return b; }
    [MenuItem(Menu+"11 - Survey Stations")]
    public static void Survey()
    {
        Guard();var s=new StringBuilder(); foreach(var c in Stations())
        {s.AppendLine(PathOf(c.transform)+" lift="+PathOf(c.liftRoot)+" max="+c.liftMax+" station scale="+c.transform.lossyScale);foreach(var box in c.liftRoot.GetComponentsInChildren<BoxCollider>(true))s.AppendLine("  "+PathOf(box.transform)+" pos="+box.transform.localPosition+" size="+box.size+" center="+box.center);}
        Receipt("survey",s.ToString());
    }
    static string RuntimeSnapshot()
    {
        var b=new StringBuilder();
        foreach(var c in Stations())
        {
            b.AppendLine(PathOf(c.transform)+StripStand(EditorJsonUtility.ToJson(c)));
            foreach(var t in c.transform.parent.GetComponentsInChildren<KH_WashTool>(true).OrderBy(x=>PathOf(x.transform)))b.AppendLine(PathOf(t.transform)+EditorJsonUtility.ToJson(t));
            foreach(var col in c.liftRoot.GetComponentsInChildren<Collider>(true)) b.AppendLine(PathOf(col.transform)+EditorJsonUtility.ToJson(col));
        }
        return b.ToString();
    }
    [MenuItem(Menu+"20 - Apply Supports and Preview")]
    public static void Apply()
    {
        Guard();if(EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save/reconcile unrelated scene edits first");
        var calls=Stations();if(calls.Length!=4)throw new InvalidOperationException("Expected four stations");
        var before=RuntimeSnapshot();Directory.CreateDirectory("Temp/WashPremium");File.WriteAllText("Temp/WashPremium/runtime-before.txt",before);File.WriteAllText("Temp/WashPremium/runtime-before-stable.txt",Stable(before));
        foreach(var c in calls)
        {
            var station=c.liftRoot.parent;
            if((station.lossyScale-Vector3.one).sqrMagnitude>1e-6f || Vector3.Dot(station.up,Vector3.up)<.9999f || Mathf.Abs(c.liftMax-.9f)>.0001f)throw new InvalidOperationException("Unexpected station basis");
            var table=c.liftRoot.Find("WashTable").GetComponent<BoxCollider>();
            var existing=station.Find("SupportGroundRoot");
            GameObject support;
            if(existing==null){support=new GameObject("SupportGroundRoot");Undo.RegisterCreatedObjectUndo(support,"Wash premium support");support.transform.SetParent(station,false);var v=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"TelescopicSupport.prefab"));v.transform.SetParent(support.transform,false);}
            else support=existing.gameObject;
            var sk=support.GetComponentInChildren<SkinnedMeshRenderer>(true);SetSupportBounds(sk);
            var bottom=table.transform.TransformPoint(table.center-Vector3.up*table.size.y*.5f);
            var local=station.InverseTransformPoint(bottom);support.transform.localPosition=local;support.transform.localRotation=Quaternion.identity;support.transform.localScale=Vector3.one;
            var visual=support.GetComponent<KH_WashSupportVisual>();if(visual==null)visual=support.AddUdonSharpComponent<KH_WashSupportVisual>();
            visual.tableCollider=table;visual.sleeves=sk;visual.extendIndex=ExtendIndex(sk);visual.strokeMeters=c.liftMax;visual.baseLocalY=local.y;visual.mountBelowCollider=0;
            var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(visual);backing.SyncMethod=VRC.SDKBase.Networking.SyncType.None;UdonSharpEditorUtility.CopyProxyToUdon(visual);EditorUtility.SetDirty(visual);EditorUtility.SetDirty(backing);
        }
        const string previewName="KH_WashPremiumPreview_Inactive";
        if(!EditorSceneManager.GetActiveScene().GetRootGameObjects().Any(x=>x.name==previewName))
        {
            var gallery=new GameObject(previewName);gallery.SetActive(false);var station=calls[0].liftRoot.parent;gallery.transform.position=station.TransformPoint(new Vector3(0,0,-4));gallery.transform.rotation=station.rotation;
            for(int i=0;i<17;i++)
            {
                var n=Names[i];var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+n+".prefab"));go.transform.SetParent(gallery.transform,false);
                if(!n.EndsWith("Bath"))go.transform.localRotation=Quaternion.Euler(0,180,0);
                if(i>=13&&i<=15)go.transform.localPosition=new Vector3((i-14)*1.5f,.5f,.65f);
                else {int j=i==16?13:i;go.transform.localPosition=new Vector3((j%7-3)*.4f,.6f,-(j/7)*.55f);}
            }
            PrefabUtility.SaveAsPrefabAsset(gallery,Prefabs+"PremiumWashPreview.prefab");
        }
        if(before!=RuntimeSnapshot())throw new InvalidOperationException("Existing runtime setup changed");
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());Receipt("apply","supports=4; preview=17 inactive; existing runtime unchanged; NOT saved until Verify");
    }
    [MenuItem(Menu+"21 - Verify Integration")]
    public static void Verify()
    {
        Guard();var log=new StringBuilder();int triangles=0;
        foreach(var n in Names)
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+n+".prefab");if(p==null)throw new Exception("Missing prefab "+n);
            int tri=0;foreach(var mf in p.GetComponentsInChildren<MeshFilter>(true))tri+=mf.sharedMesh.triangles.Length/3;
            foreach(var sk in p.GetComponentsInChildren<SkinnedMeshRenderer>(true))tri+=sk.sharedMesh.triangles.Length/3;
            triangles+=tri;
            foreach(var rr in p.GetComponentsInChildren<Renderer>(true))if(rr.sharedMaterials.Length!=1 || rr.sharedMaterial!=AssetDatabase.LoadAssetAtPath<Material>(Root+"PremiumWash_"+(Fixture(n)?"Fixture":"Tools")+".mat"))throw new Exception("Material mismatch "+n);
            log.AppendLine(n+" tris="+tri);
        }
        // 0.5.0: six offered models redesigned; the other twelve remain at 0.3.0 (lab validation/0.5.0).
        if(triangles!=19952)throw new Exception("0.5.0 base-model triangle drift "+triangles+" (want 19952)");
        VerifyAddons(log);   // IMP-DOG-049 / 044: the 0.4.0 prefabs (two materials each: per-child map, KH_WashPremiumEditor.Addons.cs)
        foreach(var g in new[]{"Tools","Fixture"})foreach(var channel in new[]{"BaseColor","Normal","MetallicSmoothness"})
        {var ti=(TextureImporter)AssetImporter.GetAtPath(Root+"Premium"+g+"_"+channel+".png");var ps=ti.GetPlatformTextureSettings("Android");if(!ps.overridden||ps.maxTextureSize!=1024||ps.format!=TextureImporterFormat.ASTC_6x6||ti.isReadable||ti.sRGBTexture!=(channel=="BaseColor"))throw new Exception("Texture import "+g+channel);}
        int count=0;
        foreach(var c in Stations())
        {
            var v=c.liftRoot.parent.Find("SupportGroundRoot").GetComponent<KH_WashSupportVisual>();var ub=UdonSharpEditorUtility.GetBackingUdonBehaviour(v);
            if(ub.SyncMethod!=VRC.SDKBase.Networking.SyncType.None||v.transform.parent!=c.liftRoot.parent||v.tableCollider.transform.parent!=c.liftRoot)throw new Exception("Support hierarchy/sync mismatch");
            if(!ub.publicVariables.TryGetVariableValue<BoxCollider>("tableCollider",out var tb)||tb!=v.tableCollider||!ub.publicVariables.TryGetVariableValue<SkinnedMeshRenderer>("sleeves",out var sm)||sm!=v.sleeves)throw new Exception("Backing refs mismatch");
            if(!ub.publicVariables.TryGetVariableValue<float>("baseLocalY",out var by)||Mathf.Abs(by-v.baseLocalY)>.00001f)throw new Exception("Backing base mismatch");
            var oldLift=c.liftRoot.localPosition;var oldTable=v.tableCollider.transform.localPosition;var oldScale=v.tableCollider.transform.localScale;var oldSupport=v.transform.localPosition;float oldWeight=v.sleeves.GetBlendShapeWeight(v.extendIndex);var mesh=new Mesh();
            try
            {
                for(int pose=0;pose<=4;pose++)
                {
                    c.liftRoot.localPosition=oldLift+Vector3.up*(pose*.225f);
                    // Exercise the shared update body in Edit mode; Unity-event proxy hooks are suppressed by UdonSharp.
                    v._RefreshVisual();
                    if(Mathf.Abs(v.sleeves.GetBlendShapeWeight(v.extendIndex)-pose*25)>.02f||Mathf.Abs(v.transform.localPosition.y-v.baseLocalY)>.00001f)throw new Exception("Support lift mismatch station="+c.stationIndex+" pose="+pose+" weight="+v.sleeves.GetBlendShapeWeight(v.extendIndex)+" base="+v.baseLocalY+" pos="+v.transform.localPosition+" oldLift="+oldLift);
                    v.sleeves.BakeMesh(mesh);foreach(var vert in mesh.vertices)if(!v.sleeves.localBounds.Contains(vert))throw new Exception("Skinned bounds clips extended pose: pose="+pose+" vert="+vert+" bounds="+v.sleeves.localBounds+" scale="+v.sleeves.transform.lossyScale);
                    float top=float.NegativeInfinity;foreach(var vert in mesh.vertices)top=Mathf.Max(top,v.sleeves.transform.TransformPoint(vert).y);
                    float underside=v.tableCollider.transform.TransformPoint(v.tableCollider.center-Vector3.up*v.tableCollider.size.y*.5f).y;
                    if(Mathf.Abs(top-underside)>.001f)throw new Exception("Mount gap "+(top-underside));
                    log.AppendLine("station="+c.stationIndex+" pose="+pose+" weight="+v.sleeves.GetBlendShapeWeight(v.extendIndex)+" mountGap="+(top-underside));
                }
                v.tableCollider.transform.localPosition=oldTable+new Vector3(.2f,0,.1f);v.tableCollider.transform.localScale=new Vector3(.5f,1,.6f);
                v._RefreshVisual();
                var world=v.tableCollider.transform.TransformPoint(v.tableCollider.center-Vector3.up*v.tableCollider.size.y*.5f);var local=v.transform.parent.InverseTransformPoint(world);
                if(Mathf.Abs(v.transform.localPosition.x-local.x)>.00001f||Mathf.Abs(v.transform.localPosition.z-local.z)>.00001f||v.transform.localScale!=Vector3.one)throw new Exception("Size/center coupling");
            }
            finally { c.liftRoot.localPosition=oldLift;v.tableCollider.transform.localPosition=oldTable;v.tableCollider.transform.localScale=oldScale;v.transform.localPosition=oldSupport;v.sleeves.SetBlendShapeWeight(v.extendIndex,oldWeight);UnityEngine.Object.DestroyImmediate(mesh); }
            count++;
        }
        if(count!=4)throw new Exception("Support count");
        // The baseline (Temp, per machine): menu 20 wrote it on 2026-10-08; 21b captures it again from the saved scene before a change
        // (IMP-DOG-049: before 49). No baseline = no PASS (Codex review 2026-10-11: a SKIP here must not end in an overall PASS).
        if(File.Exists("Temp/WashPremium/runtime-before-stable.txt"))
        {
            string before=StripStand(File.ReadAllText("Temp/WashPremium/runtime-before-stable.txt")),after=Stable(RuntimeSnapshot());   // IMP-ENV-013: StripStand on both sides
            if(before!=after)
            {
                // 2026-10-11: keep the evidence so a reviewer can judge the difference (no allow-list: any change still fails here).
                File.WriteAllText("Temp/WashPremium/runtime-after-stable.txt",after);
                string[] a=before.Split('\n'),b=after.Split('\n');var d=new StringBuilder("line count "+a.Length+" -> "+b.Length+"\n");
                for(int i=0;i<Math.Max(a.Length,b.Length);i++){string x=i<a.Length?a[i]:"",y=i<b.Length?b[i]:"";if(x!=y)d.AppendLine("line "+i+"\n- "+x+"\n+ "+y);}
                File.WriteAllText("Temp/WashPremium/runtime-diff.txt",d.ToString());
                throw new Exception("Stable runtime preservation failed (Temp/WashPremium/runtime-diff.txt)");
            }
        }
        else if(File.Exists("Temp/WashPremium/runtime-before.txt")){if(StripStand(File.ReadAllText("Temp/WashPremium/runtime-before.txt"))!=RuntimeSnapshot())throw new Exception("Runtime preservation failed");}
        else throw new Exception("No runtime baseline in Temp/WashPremium: run 21b - Capture Integration Baseline on the saved scene BEFORE the change (49), then 21 after it");
        var gallery=EditorSceneManager.GetActiveScene().GetRootGameObjects().Single(x=>x.name=="KH_WashPremiumPreview_Inactive");// IMP-DOG-047 (2026-10-11): WashPremium/48 keeps only the offered items in the gallery (the dropped ones ship otherwise).
        if(gallery.activeSelf||gallery.transform.childCount!=KH_WashSelectionEditor.Order.Length)throw new Exception("Preview unexpectedly active or not the "+KH_WashSelectionEditor.Order.Length+" offered items ("+gallery.transform.childCount+")");
        log.AppendLine("PASS 18 models / 19604 tris / 2 materials / 6 texture imports / 4 support backing refs / 20 lift poses / center+size independence / existing runtime preservation (Temp/WashPremium/runtime-before*.txt) / gallery "+KH_WashSelectionEditor.Order.Length+" offered items; Edit-mode only");Receipt("verify",log.ToString());
    }
    /// <summary>IMP-DOG-049 (2026-10-11): the baseline 21 compares against (the stations' KH_WashStationCall, every KH_WashTool, the lift
    /// colliders), from the SAVED scene, raw and in the stable form (globalIDs). Run it right before a change (49); 21 after the change
    /// must find the same stable snapshot. Writes Temp only.</summary>
    [MenuItem(Menu+"21b - Capture Integration Baseline")]
    public static void CaptureBaseline()
    {
        Guard();if(EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save first: the baseline is the saved scene");
        if(Stations().Length!=4)throw new InvalidOperationException("Expected four stations");
        var raw=RuntimeSnapshot();Directory.CreateDirectory("Temp/WashPremium");File.WriteAllText("Temp/WashPremium/runtime-before.txt",raw);File.WriteAllText("Temp/WashPremium/runtime-before-stable.txt",Stable(raw));
        Receipt("baseline","stations=4 snapshot lines="+raw.Split(new[]{(char)10},StringSplitOptions.RemoveEmptyEntries).Length+" (KH_WashStationCall, KH_WashTool, lift colliders); 21 compares the stable form after the change");
    }
    [MenuItem(Menu+"22 - Refresh Support Bounds")]
    public static void RefreshBounds() { Guard(); foreach(var c in Stations())SetSupportBounds(c.liftRoot.parent.Find("SupportGroundRoot").GetComponentInChildren<SkinnedMeshRenderer>(true));Receipt("bounds","updated from recalculated baked mesh bounds"); }
    [MenuItem(Menu+"30 - Capture World Materials")]
    public static void Capture()
    {
        Guard();Directory.CreateDirectory("Temp/WashPremium/images");var gallery=EditorSceneManager.GetActiveScene().GetRootGameObjects().Single(x=>x.name=="KH_WashPremiumPreview_Inactive");
        var camGo=new GameObject("WashPremiumEvidenceCamera");camGo.hideFlags=HideFlags.HideAndDontSave;var cam=camGo.AddComponent<Camera>();cam.enabled=false;cam.fieldOfView=36;cam.nearClipPlane=.05f;cam.farClipPlane=100;cam.allowHDR=true;cam.allowMSAA=true;
        var rt=new RenderTexture(1800,1200,24,RenderTextureFormat.ARGB32);rt.antiAliasing=4;var previous=RenderTexture.active;
        try
        {
            gallery.SetActive(true);
            CaptureAt(cam,rt,gallery.transform.TransformPoint(new Vector3(2.7f,2.9f,-4.3f)),gallery.transform.TransformPoint(new Vector3(0,.55f,.1f)),"collection");
            CaptureAt(cam,rt,gallery.transform.TransformPoint(new Vector3(.5f,1.35f,-2.0f)),gallery.transform.TransformPoint(new Vector3(.1f,.65f,-.4f)),"finishing");
            CaptureAt(cam,rt,gallery.transform.TransformPoint(new Vector3(-.4f,1.65f,-1.4f)),gallery.transform.TransformPoint(new Vector3(-1.5f,.52f,.65f)),"hinoki");
            gallery.SetActive(false);
            var c=Stations()[0];var station=c.liftRoot.parent;var v=station.Find("SupportGroundRoot").GetComponent<KH_WashSupportVisual>();var old=c.liftRoot.localPosition;
            try { for(int p=0;p<3;p++){c.liftRoot.localPosition=old+Vector3.up*(p*.45f);v._RefreshVisual();CaptureAt(cam,rt,station.TransformPoint(new Vector3(1.55f,1.0f,-3.1f)),station.TransformPoint(new Vector3(0,.45f,-1.2f)),"support-"+p);} }
            finally{c.liftRoot.localPosition=old;v._RefreshVisual();}
        }
        finally{gallery.SetActive(false);cam.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camGo);}
        Receipt("capture","6 images, actual scene lighting, edit Camera.Render; preview restored inactive; no Play");
    }
    static void CaptureAt(Camera cam,RenderTexture rt,Vector3 from,Vector3 target,string name)
    {cam.transform.position=from;cam.transform.rotation=Quaternion.LookRotation(target-from);cam.targetTexture=rt;
        var skins=Stations().Select(x=>x.liftRoot.parent.Find("SupportGroundRoot").GetComponentInChildren<SkinnedMeshRenderer>()).ToArray();
        var old=skins.Select(x=>x.forceMatrixRecalculationPerRender).ToArray();
        try{foreach(var sk in skins)sk.forceMatrixRecalculationPerRender=true;cam.Render();}finally{for(int i=0;i<skins.Length;i++)skins[i].forceMatrixRecalculationPerRender=old[i];}
        RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);try{tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes("Temp/WashPremium/images/"+name+".png",tex.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(tex);}}
    public static void FacePreview()
    {Guard();var gallery=EditorSceneManager.GetActiveScene().GetRootGameObjects().Single(x=>x.name=="KH_WashPremiumPreview_Inactive");foreach(Transform t in gallery.transform)if(!t.name.EndsWith("Bath"))t.localRotation=Quaternion.Euler(0,180,0);PrefabUtility.SaveAsPrefabAsset(gallery,Prefabs+"PremiumWashPreview.prefab");}
    /// <summary>IMP-ENV-013 (2026-10-12): the stand-size fields Wash/22 adds to KH_WashStationCall (standTable / standSize / standIdle /
    /// standBase* / standSpeed) are left out of this preservation check, the same limited strip on both sides (the snapshot here, the
    /// baseline file when read): a baseline taken before the fields existed and one taken after both compare without them. Wash/23
    /// checks those fields (proxy, backing, the measured values). Each is preceded by a comma (stationIndex is the first field) and its
    /// value is flat: a number, {"x":..}/{"instanceID":..}/{"globalID":".."} or an array of such objects.</summary>
    static readonly string[] StandFields={"standTable","standSize","standIdle","standBaseSize","standBasePos","standBaseScale","standSpeed"};
    static string StripStand(string s){return System.Text.RegularExpressions.Regex.Replace(s,",\"(?:"+string.Join("|",StandFields)+")\":(?:\\{[^{}]*\\}|\\[(?:\\{[^{}]*\\},?)*\\]|[^,{}\\[\\]]+)","");}
    static string Stable(string raw)
    {return System.Text.RegularExpressions.Regex.Replace(raw,"\"instanceID\":(-?[0-9]+)",m=>{var obj=EditorUtility.InstanceIDToObject(int.Parse(m.Groups[1].Value));return "\"globalID\":\""+(obj==null?"null":GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString())+"\"";});}
    public static void PreserveStableBaseline()
    {Guard();var raw=StripStand(File.ReadAllText("Temp/WashPremium/runtime-before.txt"));if(raw!=RuntimeSnapshot())throw new Exception("Cannot normalize a drifted baseline");File.WriteAllText("Temp/WashPremium/runtime-before-stable.txt",Stable(raw));}
}
}
