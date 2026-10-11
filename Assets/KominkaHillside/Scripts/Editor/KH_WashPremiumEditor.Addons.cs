using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
namespace KominkaHillside.Editor
{
// IMP-DOG-049 / IMP-DOG-044 (2026-10-11): the lab's 0.4.0 additions (vrc-asset-lab assets/wash-premium/handoff.md "0.4.0 追加"): WaterPistol,
// Hose, PoolBath + the PremiumAddons maps. Menu 10 stays the 0.3.0 import (18 names) and is not re-run for these.
// Materials by child object (the FBX material slot of each child, one per child: lab validation/build-v04.json):
//   *_Mesh (pistol body, hose nozzle) -> PremiumWash_Tools (existing), Hose_Tube / PoolBath_Floor|Wall|Rim -> PremiumWash_Addons (new, opaque,
//   as Tools / Fixture), WaterPistol_Window -> PremiumWash_Window (Fade), PoolBath_Water -> PremiumWash_Water (Fade).
// Render queues (2026-10-11): every wash particle material is 3000 (WashMist, KH_WashParticle_Foam / Spray / Splash) and the wet / foam
// shell on the dog is Transparent+10 = 3010 (KH_WashShell.shader). The two Fade materials draw at 2990, before both: water surface and
// window first, then the spray / foam particles over them, then the shell (paws seen through the water keep their foam). Not sorted per
// object against the particles at runtime; the real look is a Play / device check (IMP-DOG-044 CHECK).
public static partial class KH_WashPremiumEditor
{
    public static readonly string[] AddonNames={"WaterPistol","Hose","PoolBath"};
    public const int AddonQueue=2990;
    public const int AddonTriangles=2508+2560+3346;   // lab build-v04.json: WaterPistol 2252+256, Hose 2084+476, PoolBath 444+1568+1280+54
    static readonly string[] AddonChannels={"BaseColor","Normal","MetallicSmoothness"};
    /// <summary>The material a 0.4.0 child renderer must carry, by its object name (null = unexpected renderer).</summary>
    public static string AddonMaterialFor(string child)
    {
        if(child=="WaterPistol_Mesh"||child=="Hose_Mesh")return "PremiumWash_Tools";
        if(child=="Hose_Tube"||child=="PoolBath_Floor"||child=="PoolBath_Wall"||child=="PoolBath_Rim")return "PremiumWash_Addons";
        if(child=="WaterPistol_Window")return "PremiumWash_Window";
        if(child=="PoolBath_Water")return "PremiumWash_Water";
        return null;
    }
    static Material AddonMaterial(string name){var m=AssetDatabase.LoadAssetAtPath<Material>(Root+name+".mat");if(m==null)throw new InvalidOperationException("Missing "+Root+name+".mat (run 12)");return m;}
    // Standard, Fade (the whole surface fades: no reliance on reflections), queue AddonQueue, reflections off, modest smoothness.
    static Material FadeMaterial(string name,Color color,float smoothness)
    {
        string path=Root+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(m,path);}
        m.shader=Shader.Find("Standard");m.color=color;m.SetFloat("_Metallic",0f);m.SetFloat("_Glossiness",smoothness);m.SetFloat("_GlossMapScale",1f);
        m.SetFloat("_Mode",2f);m.SetOverrideTag("RenderType","Transparent");m.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);m.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);m.SetInt("_ZWrite",0);
        m.DisableKeyword("_ALPHATEST_ON");m.EnableKeyword("_ALPHABLEND_ON");m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetFloat("_GlossyReflections",0f);m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");m.SetFloat("_SpecularHighlights",1f);m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.SetTexture("_MainTex",null);m.SetTexture("_BumpMap",null);m.SetTexture("_MetallicGlossMap",null);m.DisableKeyword("_NORMALMAP");m.DisableKeyword("_METALLICGLOSSMAP");
        // Changing the mode in the Inspector resets the queue to 3000 (StandardShaderGUI): 21 / 44 check it.
        m.renderQueue=AddonQueue;m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
    }
    // AI-chosen provisional colours (IMP-DOG-044 Q14 "thin see-through water"; the lab's Blender colour was a placeholder alpha .38).
    public static readonly Color WaterColour=new Color(.62f,.80f,.84f,.38f), WindowColour=new Color(.74f,.88f,.90f,.45f);
    public const float WaterSmoothness=.55f, WindowSmoothness=.5f;
    [MenuItem(Menu+"12 - Import 0.4.0 Visuals")]
    public static void ImportAddons()
    {
        Guard();Directory.CreateDirectory(Prefabs);AssetDatabase.Refresh();
        foreach(string n in AddonNames)if(AssetImporter.GetAtPath(Root+n+".fbx")==null)throw new InvalidOperationException("Missing "+Root+n+".fbx (copy the lab's exports/0.4.0 first)");
        // the PremiumAddons maps: the same importer settings as menu 10 (Fixture size 1024; BaseColor sRGB only; MS alpha from input)
        foreach(string channel in AddonChannels)
        {
            var ti=(TextureImporter)AssetImporter.GetAtPath(Root+"PremiumAddons_"+channel+".png");if(ti==null)throw new InvalidOperationException("Missing PremiumAddons_"+channel+".png");
            ti.textureType=channel=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
            ti.sRGBTexture=channel=="BaseColor";ti.alphaSource=channel=="MetallicSmoothness"?TextureImporterAlphaSource.FromInput:TextureImporterAlphaSource.None;
            ti.alphaIsTransparency=false;ti.isReadable=false;ti.mipmapEnabled=true;ti.wrapMode=TextureWrapMode.Clamp;ti.filterMode=FilterMode.Trilinear;ti.anisoLevel=4;ti.maxTextureSize=1024;
            var mobile=ti.GetPlatformTextureSettings("Android");mobile.overridden=true;mobile.maxTextureSize=1024;mobile.format=TextureImporterFormat.ASTC_6x6;mobile.compressionQuality=100;ti.SetPlatformTextureSettings(mobile);
            ti.SaveAndReimport();
        }
        // PremiumWash_Addons: Standard opaque, as menu 10 writes Tools / Fixture
        string addonsPath=Root+"PremiumWash_Addons.mat";var addons=AssetDatabase.LoadAssetAtPath<Material>(addonsPath);
        if(addons==null){addons=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(addons,addonsPath);}
        addons.color=Color.white;addons.SetFloat("_Metallic",0);addons.SetFloat("_GlossMapScale",1);addons.SetFloat("_BumpScale",.65f);
        addons.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"PremiumAddons_BaseColor.png"));
        addons.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"PremiumAddons_Normal.png"));
        addons.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"PremiumAddons_MetallicSmoothness.png"));
        addons.EnableKeyword("_NORMALMAP");addons.EnableKeyword("_METALLICGLOSSMAP");addons.enableInstancing=true;EditorUtility.SetDirty(addons);
        FadeMaterial("PremiumWash_Window",WindowColour,WindowSmoothness);
        FadeMaterial("PremiumWash_Water",WaterColour,WaterSmoothness);
        if(AssetDatabase.LoadAssetAtPath<Material>(Root+"PremiumWash_Tools.mat")==null)throw new InvalidOperationException("PremiumWash_Tools.mat missing (menu 10 / 0.3.0)");
        AssetDatabase.SaveAssets();
        var report=new StringBuilder();var fails=new List<string>();
        foreach(string n in AddonNames)
        {
            string path=Root+n+".fbx";var mi=(ModelImporter)AssetImporter.GetAtPath(path);
            // the same model importer settings as menu 10
            mi.globalScale=1;mi.importAnimation=false;mi.importCameras=false;mi.importLights=false;mi.importBlendShapes=true;
            mi.importNormals=ModelImporterNormals.Import;mi.importTangents=ModelImporterTangents.CalculateMikk;mi.meshCompression=ModelImporterMeshCompression.Off;mi.isReadable=false;mi.materialImportMode=ModelImporterMaterialImportMode.None;mi.SaveAndReimport();
            var holder=new GameObject(n);var part=new StringBuilder();
            try
            {
                var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));model.transform.SetParent(holder.transform,false);
                int tris=0;
                foreach(var rend in holder.GetComponentsInChildren<Renderer>(true))
                {
                    string want=AddonMaterialFor(rend.name);var mf=rend.GetComponent<MeshFilter>();
                    if(want==null||!(rend is MeshRenderer)||mf==null||mf.sharedMesh==null)throw new InvalidOperationException(n+": unexpected renderer "+rend.name+" (the lab's 0.4.0 children are *_Mesh / *_Window / Hose_Tube / PoolBath_Floor|Wall|Rim|Water)");
                    // one material per child (the lab exports one slot per child); a mesh with more sub-meshes is a changed FBX: stop
                    if(mf.sharedMesh.subMeshCount!=1)throw new InvalidOperationException(n+"/"+rend.name+": "+mf.sharedMesh.subMeshCount+" sub-meshes, expected 1 (check the FBX against handoff 0.4.0)");
                    rend.sharedMaterials=new[]{AddonMaterial(want)};
                    bool clear=want=="PremiumWash_Window"||want=="PremiumWash_Water";
                    rend.shadowCastingMode=clear?ShadowCastingMode.Off:ShadowCastingMode.On;rend.receiveShadows=!clear;
                    int t=mf.sharedMesh.triangles.Length/3;tris+=t;part.AppendLine("  "+n+"/"+rend.name+" -> "+want+" tris="+t+(clear?" (no shadows)":""));
                }
                // the anchors as Unity reads them, in the prefab's space (= the model space of the lab's handoff table)
                var root=holder.transform;
                foreach(var t in holder.GetComponentsInChildren<Transform>(true).Where(x=>x.GetComponent<Renderer>()==null&&x!=root&&x!=model.transform))
                    part.AppendLine("  anchor "+t.name+" pos="+root.InverseTransformPoint(t.position).ToString("F4")+" +X="+root.InverseTransformDirection(t.right).ToString("F3")+" +Y="+root.InverseTransformDirection(t.up).ToString("F3")+" +Z="+root.InverseTransformDirection(t.forward).ToString("F3"));
                // Gun hold (WaterPistol, Hose): Exact Gun +X = spray (NozzleAnchor +Z), +Z = the model's up. FAIL loudly over 1 degree.
                var hold=holder.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==n+"_GunHold");
                var noz=holder.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==n+"_NozzleAnchor");
                if(n!="PoolBath")
                {
                    if(hold==null||noz==null)fails.Add(n+": "+n+"_GunHold / "+n+"_NozzleAnchor missing");
                    else
                    {
                        float aim=Vector3.Angle(hold.right,noz.forward),up=Vector3.Angle(hold.forward,root.up);
                        part.AppendLine("  gun "+n+" angle(GunHold +X, NozzleAnchor forward)="+aim.ToString("F3")+"deg angle(GunHold +Z, model up)="+up.ToString("F3")+"deg nozzleInHold="+hold.InverseTransformPoint(noz.position).ToString("F3"));
                        if(aim>1f||up>1f)fails.Add(n+": GunHold off by aim "+aim.ToString("F2")+" / up "+up.ToString("F2")+" deg (> 1)");
                    }
                }
                else
                {
                    var mount=holder.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="PoolBath_SupportMount");
                    if(mount==null)fails.Add("PoolBath_SupportMount missing");else part.AppendLine("  pool mount depth (below the model origin) "+(-root.InverseTransformPoint(mount.position).y).ToString("F4")+" m (BathDepth .036)");
                }
                PrefabUtility.SaveAsPrefabAsset(holder,Prefabs+n+".prefab");
                report.AppendLine(n+" bounds="+WorldBounds(holder)+" tris="+tris);report.Append(part);
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }
        foreach(var q in new[]{"PremiumWash_Window","PremiumWash_Water"})report.AppendLine("material "+q+" queue="+AddonMaterial(q).renderQueue+" (particles 3000: WashMist / KH_WashParticle_*; wet shell 3010)");
        AssetDatabase.SaveAssets();
        Receipt("import-040",(fails.Count==0?"PASS":"FAIL "+string.Join("; ",fails))+"\n"+report+"NOT_TESTED: the transparent sort against the spray / foam particles and the wet shell (Play / device); the hold in VRChat (PCVR / Desktop)");
        if(fails.Count>0)throw new InvalidOperationException("0.4.0 import: "+string.Join("; ",fails)+" (receipt Temp/WashPremium/import-040.txt)");
    }
    /// <summary>Menu 21's 0.4.0 part: three prefabs, the per-child materials, the triangle total, the Addons maps and the Fade materials.</summary>
    static void VerifyAddons(StringBuilder log)
    {
        int triangles=0;
        foreach(var n in AddonNames)
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+n+".prefab");if(p==null)throw new Exception("Missing prefab "+n+" (run 12)");
            int tri=0;var mats=new List<string>();
            foreach(var rr in p.GetComponentsInChildren<Renderer>(true))
            {
                string want=AddonMaterialFor(rr.name);var mf=rr.GetComponent<MeshFilter>();
                if(want==null||mf==null||mf.sharedMesh==null||rr.sharedMaterials.Length!=1||rr.sharedMaterial!=AssetDatabase.LoadAssetAtPath<Material>(Root+want+".mat"))throw new Exception("Material mismatch "+n+"/"+rr.name+" (want "+(want??"no renderer")+")");
                tri+=mf.sharedMesh.triangles.Length/3;mats.Add(rr.name+"="+want);
            }
            triangles+=tri;log.AppendLine(n+" tris="+tri+" "+string.Join(" ",mats));
        }
        if(triangles!=AddonTriangles)throw new Exception("0.4.0 triangle drift "+triangles+" (want "+AddonTriangles+")");
        foreach(var channel in AddonChannels)
        {var ti=(TextureImporter)AssetImporter.GetAtPath(Root+"PremiumAddons_"+channel+".png");var ps=ti.GetPlatformTextureSettings("Android");if(!ps.overridden||ps.maxTextureSize!=1024||ps.format!=TextureImporterFormat.ASTC_6x6||ti.isReadable||ti.sRGBTexture!=(channel=="BaseColor")||ti.maxTextureSize!=1024)throw new Exception("Texture import Addons"+channel);}
        foreach(var q in new[]{"PremiumWash_Window","PremiumWash_Water"})
        {var m=AssetDatabase.LoadAssetAtPath<Material>(Root+q+".mat");if(m==null||m.renderQueue!=AddonQueue||m.GetInt("_ZWrite")!=0||!m.IsKeywordEnabled("_ALPHABLEND_ON")||m.color.a>=1f)throw new Exception("Fade material "+q+" (queue "+(m==null?-1:m.renderQueue)+", want "+AddonQueue+")");log.AppendLine("material "+q+" queue="+m.renderQueue+" colour="+m.color);}
        var addons=AssetDatabase.LoadAssetAtPath<Material>(Root+"PremiumWash_Addons.mat");if(addons==null||addons.renderQueue>=2500||addons.GetTexture("_MainTex")==null)throw new Exception("PremiumWash_Addons opaque material");
        log.AppendLine("PASS 3 0.4.0 models / "+AddonTriangles+" tris / per-child materials (Tools, Addons, Window, Water) / 3 Addons texture imports / 2 Fade materials at queue "+AddonQueue);
    }
}
}
