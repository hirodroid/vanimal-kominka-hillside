using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDKBase;
using VRC.SDK3.Components;
namespace KominkaHillside.Editor
{
public static partial class KH_WashSelectionEditor
{
    const string Data="Assets/KominkaHillside/Data/WashPremium/", Prefabs="Assets/KominkaHillside/Prefabs/WashPremium/", Menu="Tools/KominkaHillside/WashPremium/";
    // Item ids: the 17 of 2026-10-08 (0..16), then IMP-DOG-049 / 044 appended 17 WaterPistol, 18 Hose (water, slot 2 with the can), 19 PoolBath
    // (platform) from the lab's 0.4.0 (WashPremium/12 imports them, 49 places them). Never renumber: the scene arrays and the harness use the ids.
    static readonly string[] Names={"FoamCannon","DuckWaterGun","FishWaterGun","BambooWaterGun","FlowerShower","RainShower","WateringCan","Dryer","PatternTowel","PawMitt","LargeSponge","GroomingBrush","Cologne","HinokiBath","StoneBath","TileBath","FloatingDuck","WaterPistol","Hose","PoolBath"};
    // Slots by item id for the tools (-1 = a platform; read only for tools)
    static readonly int[] Groups={0,1,1,1,1,1,1,2,2,0,0,2,2,3,3,3,3,1,1,3}, Slots={0,2,2,2,2,2,2,4,3,1,1,5,6,-1,-1,-1,-1,2,2};
    public const int WaterPistolId=17, HoseId=18, PoolBathId=19;
    static readonly Color Paper=new Color32(247,244,236,255), Forest=new Color32(54,78,60,255), Ink=new Color32(34,44,36,255), Gold=new Color32(184,137,69,255);
    [Serializable] public class WordsFile {public Lang[] languages;}
    [Serializable] public class Lang {public string code;public string[] words;}
    static WordsFile ReadWords(){return JsonUtility.FromJson<WordsFile>(File.ReadAllText(Data+"words.json"));}
    static KH_WashStationCall[] Calls(){return UnityEngine.Object.FindObjectsOfType<KH_WashStationCall>(true).Where(x=>x.gameObject.scene.path=="Assets/Scenes/VRCDefaultWorldScene.unity").OrderBy(x=>x.stationIndex).ToArray();}
    static void Guard(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorSceneManager.GetActiveScene().path!="Assets/Scenes/VRCDefaultWorldScene.unity")throw new Exception("Correct scene, edit mode required");}
    static void Log(string name,string txt){Directory.CreateDirectory("Temp/WashSelection");File.WriteAllText("Temp/WashSelection/"+name+".txt",DateTime.UtcNow.ToString("o")+"\n"+txt);}
    [MenuItem(Menu+"40 - Create Selection Programs")]
    public static void Programs(){Guard();foreach(var n in new[]{"KH_WashSelection","KH_WashSelectionBoard","KH_WashFinishFx"}){string p="Assets/KominkaHillside/Scripts/"+n; if(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(p+".asset")!=null)continue;var a=ScriptableObject.CreateInstance<UdonSharpProgramAsset>();a.sourceCsScript=AssetDatabase.LoadAssetAtPath<MonoScript>(p+".cs");AssetDatabase.CreateAsset(a,p+".asset");}AssetDatabase.SaveAssets();Log("programs","created; compile Udon before Build");}
    static TMP_FontAsset Font(){return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Data+"CareFont.asset") ?? Calls()[0].transform.parent.Find("Board").GetComponent<KH_WashStationBoard>().statusText.font;}
    static RectTransform Rect(Transform parent,string name,Vector2 pos,Vector2 size)
    {var go=new GameObject(name,typeof(RectTransform));var rt=go.GetComponent<RectTransform>();rt.SetParent(parent,false);rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);rt.anchoredPosition=pos;rt.sizeDelta=size;return rt;}
    static TextMeshProUGUI Text(Transform parent,string name,string text,Vector2 pos,Vector2 size,float fontSize,Color color)
    {var rt=Rect(parent,name,pos,size);var t=rt.gameObject.AddComponent<TextMeshProUGUI>();t.font=Font();t.text=text;t.fontSize=fontSize;t.enableWordWrapping=true;t.enableAutoSizing=true;t.fontSizeMin=fontSize*.8f;t.fontSizeMax=fontSize;t.alignment=TextAlignmentOptions.MidlineLeft;t.color=color;t.raycastTarget=false;return t;}
    static GameObject CanvasRoot(string name,int width,int height)
    {var go=new GameObject(name,typeof(RectTransform),typeof(Canvas));var rt=go.GetComponent<RectTransform>();rt.sizeDelta=new Vector2(width,height);go.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;return go;}
    // IMP-DOG-047 (2026-10-11): listing captions (ja) for the offered items only (Order); 43 bakes them. AI-written, not proofread.
    // IMP-DOG-049 / 044 (2026-10-11): water and station sets and the total follow the three 0.4.0 items (9 items).
    public static readonly string[] ListingSubtitles={"ふんわり泡と、やさしい手触り。2つの道具でお手入れ。","じょうろ・ホース・水鉄砲。3つの水の道具。","乾かして、拭く。2つの道具で仕上げの時間。","香りのよい檜の台と、水を張ったプール。","泡・水・仕上げ・洗い場。全9アイテムのコレクション。"};
    public const string PhotoExtras="VAnimal / CARE COLLECTION 古民家の裏山 お手入れの時間 セット _ · 1234567890 ";
    // The 17-item set of 2026-10-08 and the 6-item set of 2026-10-11 (artifacts/wash-listings/2026-10-11, IMP-DOG-047) stay as they were;
    // the 9-item set with the 0.4.0 items goes to its own folder.
    const string ListingDir="artifacts/wash-listings/2026-10-11-040";
    [MenuItem(Menu+"41 - Photograph Listings")]
    public static void Photos()
    {
        Guard();Directory.CreateDirectory(Data+"Photos");Directory.CreateDirectory(ListingDir);var words=ReadWords().languages[0].words;
        var font=Font();string glyphs=string.Concat(words)+"VAnimal CARE COLLECTION FOAM WATER FINISH STATION COMPLETE";var missing=new string(glyphs.Where(c=>!char.IsControl(c)&&!font.HasCharacter(c,true)).Distinct().ToArray());if(missing.Length>0){font.TryAddCharacters(missing,out var notAdded);EditorUtility.SetDirty(font);}
        foreach(int i in Order)Shoot(new[]{i},Data+"Photos/Item"+i+".png",null,null,512);
        for(int g=0;g<5;g++){int gg=g;var ids=Order.Where(i=>gg==4||Groups[i]==gg).ToArray();string output=ListingDir+"/0"+(g+1)+"-"+new[]{"foam","water","finish","station","complete"}[g]+".png";Shoot(ids,output,words[10+g]+"セット",ListingSubtitles[g],1024);File.Copy(output,Data+"Photos/Set"+g+".png",true);}
        AssetDatabase.Refresh();foreach(var p in Directory.GetFiles(Data+"Photos","*.png")){var ti=(TextureImporter)AssetImporter.GetAtPath(p.Replace('\\','/'));ti.maxTextureSize=512;ti.mipmapEnabled=true;ti.isReadable=false;ti.sRGBTexture=true;ti.textureCompression=TextureImporterCompression.CompressedHQ;var a=ti.GetPlatformTextureSettings("Android");a.overridden=true;a.maxTextureSize=512;a.format=TextureImporterFormat.ASTC_6x6;ti.SetPlatformTextureSettings(a);ti.SaveAndReimport();}AssetDatabase.SaveAssets();Log("photos",Order.Length+" item photos (offered items) + 5 listing photos in "+ListingDir+", listings 1024 square / no price");
    }
    static void Shoot(int[] ids,string path,string title,string subtitle,int size)
    {
        var p=new PreviewRenderUtility();var holder=new GameObject("PhotoObjects");
        try
        {
            p.AddSingleGO(holder);p.camera.orthographic=true;p.camera.orthographicSize=title==null?1.05f:5.12f;p.camera.nearClipPlane=.01f;p.camera.farClipPlane=40;p.camera.transform.position=new Vector3(0,0,-12);p.camera.transform.rotation=Quaternion.identity;p.camera.clearFlags=CameraClearFlags.SolidColor;p.camera.backgroundColor=Paper;
            p.lights[0].intensity=1.15f;p.lights[0].transform.rotation=Quaternion.Euler(35,330,0);p.lights[1].intensity=.7f;p.lights[1].transform.rotation=Quaternion.Euler(340,30,0);p.ambientColor=new Color(.4f,.4f,.4f);
            int cols=Math.Min(ids.Length<=4?2:ids.Length<=6?3:5,Math.Max(1,ids.Length));
            for(int j=0;j<ids.Length;j++)
            {
                var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+Names[ids[j]]+".prefab"));go.transform.SetParent(holder.transform,false);go.transform.rotation=Quaternion.Euler(20,155,0);
                var rr=go.GetComponentsInChildren<Renderer>();var b=rr[0].bounds;foreach(var r in rr)b.Encapsulate(r.bounds);
                if(title==null){float s=1.65f/Mathf.Max(b.size.x,b.size.y,b.size.z);go.transform.localScale=Vector3.one*s;go.transform.position=-b.center*s;}
                else {int rows=(ids.Length+cols-1)/cols;float cell=ids.Length<=6?3.15f:1.7f;float s=(cell*(ids.Length>6?.70f:.82f))/Mathf.Max(b.size.x,b.size.y,b.size.z);go.transform.localScale=Vector3.one*s;go.transform.position=new Vector3((j%cols-(cols-1)*.5f)*cell,(ids.Length>6?1.6f:1.0f)-(j/cols)*(ids.Length>6?1.35f:cell*.8f)+(rows==1?-.8f:0),0)-b.center*s;}
            }
            if(title!=null)
            {
                PhotoText(holder.transform,"Brand","VAnimal  /  CARE COLLECTION",440,38,24,Forest);
                PhotoText(holder.transform,"Title",title,358,88,54,Ink);
                PhotoText(holder.transform,"Description",subtitle,-365,100,29,Forest);
                PhotoText(holder.transform,"World","古民家の裏山  ·  お手入れの時間",-458,32,20,Forest);
            }
            p.BeginStaticPreview(new Rect(0,0,size,size));p.Render(true);var tex=p.EndStaticPreview();File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
        }
        finally{p.Cleanup();if(holder!=null)UnityEngine.Object.DestroyImmediate(holder);}
    }
    static void PhotoText(Transform parent,string name,string words,float y,float height,float size,Color color){var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshPro));go.transform.SetParent(parent,false);go.transform.localPosition=new Vector3(0,y*.01f,-4);go.transform.localScale=Vector3.one*.01f;var t=go.GetComponent<TextMeshPro>();t.font=Font();t.isOrthographic=true;t.rectTransform.sizeDelta=new Vector2(850,height);t.fontSize=size;t.enableAutoSizing=false;t.enableWordWrapping=true;t.alignment=TextAlignmentOptions.MidlineLeft;t.color=color.linear;t.text=words;t.ForceMeshUpdate(true,true);}

}
}
