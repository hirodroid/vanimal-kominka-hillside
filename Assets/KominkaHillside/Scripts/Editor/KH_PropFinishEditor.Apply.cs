using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
namespace KominkaHillside.Editor
{
    public static partial class KH_PropFinishEditor
    {
        const string Visual="FinishedVisual";
        static readonly string[] Models={"Shelf","Rack","RackGuide","PhotoFrame","Bottle"};
        public static bool Ready => AssetDatabase.LoadAssetAtPath<Material>(AssetsDir+"/KH_PropFinish.mat")!=null;
        static Transform Model(Transform parent,string name)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(AssetsDir+"/"+name+".fbx");
            if(source==null)throw new Exception("Missing prop model "+name);
            var mf=source.GetComponentsInChildren<MeshFilter>(true).Single();
            var t=parent.Find(Visual);if(t==null){t=new GameObject(Visual).transform;t.SetParent(parent,false);}
            t.localPosition=mf.transform.position;t.localRotation=mf.transform.rotation;t.localScale=mf.transform.lossyScale;t.gameObject.layer=parent.gameObject.layer;
            var f=t.GetComponent<MeshFilter>();if(f==null)f=t.gameObject.AddComponent<MeshFilter>();f.sharedMesh=mf.sharedMesh;
            var r=t.GetComponent<MeshRenderer>();if(r==null)r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(AssetsDir+"/KH_PropFinish.mat");r.enabled=true;r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;r.lightProbeUsage=LightProbeUsage.BlendProbes;
            EditorUtility.SetDirty(r);EditorUtility.SetDirty(f);return t;
        }
        static void Hide(Transform parent,params string[] paths){foreach(var path in paths){var t=parent.Find(path);var r=t==null?null:t.GetComponent<MeshRenderer>();if(r!=null){r.enabled=false;EditorUtility.SetDirty(r);}}}
        public static void FinishShelf(Transform station){if(!Ready)return;Model(station,"Shelf");Hide(station,"ImportedShelf/Mesh","Shelf","ShelfLeg_0","ShelfLeg_1","ShelfLeg_2","ShelfLeg_3");}
        public static void FinishBottle(Transform bottle){if(!Ready)return;Model(bottle,"Bottle");Hide(bottle,"Body","Shoulder","Cap","Spout");}
        public static void FinishRack(Transform rack){if(!Ready)return;Model(rack,"Rack");Hide(rack,"Crossbar","LeftPost","RightPost");var guide=rack.Find("WalkGuide");if(guide!=null)FinishRackGuide(guide);}
        public static void FinishRackGuide(Transform guide){if(!Ready)return;Model(guide,"RackGuide");Hide(guide,"GuidePostL","GuidePostR","GuideBack");}
        public static void FinishPhotoFrame(Transform root)
        {
            if(!Ready)return;var st=root.Find("Structure");if(st==null)return;
            Model(st,"PhotoFrame");Hide(st,"Back","PostL","PostR","RoofFront","RoofBack","Ridge");
        }
        static void Prepare()
        {
            foreach(var name in Models)
            {
                string path=AssetsDir+"/"+name+".fbx";AssetDatabase.ImportAsset(path);
                var im=(ModelImporter)AssetImporter.GetAtPath(path);im.materialImportMode=ModelImporterMaterialImportMode.None;im.importAnimation=false;im.importCameras=false;im.importLights=false;im.isReadable=false;im.addCollider=false;im.importNormals=ModelImporterNormals.Import;im.importTangents=ModelImporterTangents.CalculateMikk;im.generateSecondaryUV=name!="Bottle";im.SaveAndReimport();
            }
            foreach(var name in new[]{"Props_BaseColor","Props_Normal","Props_MetallicSmoothness"})
            {
                string path=AssetsDir+"/"+name+".png";AssetDatabase.ImportAsset(path);var im=(TextureImporter)AssetImporter.GetAtPath(path);
                im.textureType=name.EndsWith("Normal")?TextureImporterType.NormalMap:TextureImporterType.Default;im.sRGBTexture=name.EndsWith("BaseColor");im.isReadable=false;im.mipmapEnabled=true;im.maxTextureSize=2048;im.wrapMode=TextureWrapMode.Clamp;im.filterMode=FilterMode.Trilinear;im.anisoLevel=4;im.textureCompression=TextureImporterCompression.CompressedHQ;
                foreach(string platform in new[]{"Android","iPhone"}){var ps=im.GetPlatformTextureSettings(platform);ps.name=platform;ps.overridden=true;ps.maxTextureSize=1024;ps.format=TextureImporterFormat.ASTC_6x6;im.SetPlatformTextureSettings(ps);}im.SaveAndReimport();
            }
            var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetsDir+"/KH_PropFinish.mat");if(mat==null){mat=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(mat,AssetsDir+"/KH_PropFinish.mat");}
            mat.shader=Shader.Find("Standard");mat.color=Color.white;mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(AssetsDir+"/Props_BaseColor.png");mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(AssetsDir+"/Props_Normal.png"));mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(AssetsDir+"/Props_MetallicSmoothness.png"));mat.SetFloat("_BumpScale",.65f);mat.SetFloat("_GlossMapScale",1);mat.SetFloat("_Metallic",0);mat.SetFloat("_Glossiness",.22f);mat.SetFloat("_Mode",0);mat.EnableKeyword("_NORMALMAP");mat.EnableKeyword("_METALLICGLOSSMAP");mat.enableInstancing=true;EditorUtility.SetDirty(mat);AssetDatabase.SaveAssets();
        }
        static List<Transform> Roots()=>UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="KH_LeashRack"||t.name=="KH_Station_Viewpoint"||t.name=="KH_Station_Plaza").ToList();
        [MenuItem("Tools/KominkaHillside/Course/162 - Apply Close Prop Finish")]
        public static void Apply()
        {
            Idle();var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.isDirty)throw new Exception("Save scene first");if(!File.Exists(Evidence+"/before.unity"))throw new Exception("Capture baseline first");
            Prepare();var components=UnityEngine.Object.FindObjectsOfType<Component>(true).Where(c=>c!=null&&c.gameObject.scene==scene&&Target(c.transform)&&!(c is MeshRenderer)&&!(c is MeshFilter)&&!(c is Transform)).ToArray();
            var before=components.Select(c=>EditorJsonUtility.ToJson(c)).ToArray();
            foreach(var st in UnityEngine.Object.FindObjectsOfType<Transform>(true).Where(t=>t.gameObject.scene==scene&&t.Find("ImportedShelf")!=null).ToArray())FinishShelf(st);
            foreach(var t in UnityEngine.Object.FindObjectsOfType<KH_WashTool>(true).Where(t=>t.tool==KH_DogWash.ToolBottle&&t.transform.Find("Body")!=null))FinishBottle(t.transform);   // 2026-10-15 BUG-ENV-005: the premium FoamCannon also has tool == ToolBottle (IMP-DOG-049); only the shampoo bottles have the Body/Shoulder/Cap/Spout children
            foreach(var rack in Roots())FinishRack(rack);FinishPhotoFrame(GameObject.Find("KH_PhotoBoard").transform);
            var after=components.Select(c=>EditorJsonUtility.ToJson(c)).ToArray();if(!before.SequenceEqual(after))throw new Exception("Existing functional component changed; discard scene changes");
            File.WriteAllText(Evidence+"/runtime-preservation.txt","Existing non-render, non-transform components compared via EditorJsonUtility: "+components.Length+" unchanged.\n");
            EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Save failed");Verify();
        }
        static Bounds LocalBounds(Transform visual,Transform parent)
        {
            var m=visual.GetComponent<MeshFilter>().sharedMesh.bounds;var matrix=parent.worldToLocalMatrix*visual.localToWorldMatrix;var b=new Bounds();
            for(int i=0;i<8;i++){var p=matrix.MultiplyPoint3x4(m.center+Vector3.Scale(m.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));if(i==0)b=new Bounds(p,Vector3.zero);else b.Encapsulate(p);}return b;
        }
        [MenuItem("Tools/KominkaHillside/Course/163 - Verify Close Prop Finish")]
        public static void Verify()
        {
            Idle();var lines=new List<string>();int errors=0;Action<bool,string> need=(ok,msg)=>{lines.Add((ok?"PASS ":"FAIL ")+msg);if(!ok)errors++;};
            var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetsDir+"/KH_PropFinish.mat");need(mat!=null&&mat.GetTexture("_BumpMap")!=null&&mat.IsKeywordEnabled("_NORMALMAP"),"shared material with normal map");
            var visuals=UnityEngine.Object.FindObjectsOfType<MeshFilter>(true).Where(f=>f.gameObject.scene.IsValid()&&f.name==Visual&&AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(AssetsDir)).ToArray();
            need(visuals.Length==15,"15 visual models (4 shelves, 4 bottles, 3 racks, 3 guides, 1 board): "+visuals.Length);
            foreach(var f in visuals){var r=f.GetComponent<MeshRenderer>();need(r.enabled&&r.sharedMaterials.Length==1&&r.sharedMaterial==mat,f.transform.parent.name+" shared material");need(f.GetComponentsInChildren<Collider>(true).Length==0,f.transform.parent.name+" no added collider");need(f.sharedMesh.tangents.Length==f.sharedMesh.vertexCount,f.transform.parent.name+" tangents");var b=LocalBounds(f.transform,f.transform.parent);lines.Add("BOUNDS "+PathOf(f.transform)+" "+b.ToString("F5"));
                string mesh=AssetDatabase.GetAssetPath(f.sharedMesh);if(mesh.EndsWith("Shelf.fbx"))need(Mathf.Abs(b.size.x-1.2f)<.001f&&Mathf.Abs(b.size.z-.45f)<.001f&&Mathf.Abs(b.min.y)<.001f,"shelf footprint and feet");
                if(mesh.EndsWith("Bottle.fbx")){need(Mathf.Abs(b.min.y+.085f)<.001f&&b.size.x<.061f,"bottle footprint and bottom");var tool=f.transform.parent.GetComponent<KH_WashTool>();var end=new Vector3(0,.092f,0)+new Vector3(0,Mathf.Cos(Mathf.PI/6),.5f)*.03f;need(Vector3.Distance(tool.nozzle.localPosition,end)<.0001f,"bottle existing nozzle matches model outlet");}
            }
            foreach(var name in new[]{"Props_BaseColor","Props_Normal","Props_MetallicSmoothness"}){var ti=(TextureImporter)AssetImporter.GetAtPath(AssetsDir+"/"+name+".png");need(!ti.isReadable&&ti.mipmapEnabled,name+" mipmaps, no CPU copy");need(ti.sRGBTexture==name.EndsWith("BaseColor"),name+" color space");if(name.EndsWith("Normal"))need(ti.textureType==TextureImporterType.NormalMap,"normal map importer");foreach(var platform in new[]{"Android","iPhone"}){var ps=ti.GetPlatformTextureSettings(platform);need(ps.overridden&&ps.maxTextureSize==1024&&ps.format==TextureImporterFormat.ASTC_6x6,name+" "+platform+" ASTC1024");}}
            lines.Add("ERRORS "+errors);File.WriteAllLines(Evidence+"/verify.txt",lines);if(errors>0)throw new Exception("Prop finish errors="+errors);Debug.Log("[PropFinish] verify errors=0 models="+visuals.Length);
        }
    }
}
