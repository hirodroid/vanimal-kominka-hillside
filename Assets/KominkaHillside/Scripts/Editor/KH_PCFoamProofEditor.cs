using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // G0 deliberately uses a preview scene and cloned transforms only: no production-scene writes or Play.
    public static class KH_PCFoamProofEditor
    {
        const string Output = "Temp/PCFoamProof";
        const string ProbeAsset = "Assets/KominkaHillside/Data/Wash/KH_PCFoamProof.asset";
        [Serializable] public class Shot { public string name; public int pixels, width, height; public float centreErrorPixels; }
        [Serializable] public class Report
        {
            public string version = "IMP-DOG-052-G0-v1", result, error, device, worldCentre, worldBounds;
            public bool sceneDirtyBefore, sceneDirtyAfter;
            public int anchor, vertices, triangles, thinPasses;
            public float skinErrorMetres, rotatedSkinErrorMetres, intermediateWidthRatio, boneMatrixErrorMetres;
            public List<Shot> shots = new List<Shot>();
        }
        // v1 sphere proof is retained as source history only; its former menu is intentionally removed.
        public static void Prove()
        {
            if (KH_PCFoamGenerateEditor.Version != 1) throw new InvalidOperationException("Legacy sphere proof cannot verify the continuous surface; use v3 checks.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required");
            var active = SceneManager.GetActiveScene();
            if (active.path != "Assets/Scenes/VRCDefaultWorldScene.unity") throw new InvalidOperationException("Wrong scene");
            if (AssetDatabase.LoadAssetAtPath<Mesh>(ProbeAsset) != null) throw new InvalidOperationException("Previous proof asset exists; inspect before retry");
            Directory.CreateDirectory(Output);
            string runOutput=Output+"/"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");Directory.CreateDirectory(runOutput);
            var report = new Report { sceneDirtyBefore = active.isDirty, device = SystemInfo.graphicsDeviceType.ToString() };
            var scene = EditorSceneManager.NewPreviewScene();
            var owned = new List<UnityEngine.Object>(); AssetBundle bundle = null;
            T Own<T>(T o) where T : UnityEngine.Object { o.hideFlags=HideFlags.HideAndDontSave;owned.Add(o); return o; }
            try
            {
                Require(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11,"D3D11 required");
                var wash = UnityEngine.Object.FindObjectsOfType<KH_DogWash>().Single(w=>w.name == "KH_Wash_Shiba");
                var source = wash.shellRenderer;
                var mesh = source.sharedMesh;
                int anchor = UnmorphedAnchor(mesh); report.anchor = anchor;
                var shader = Shader.Find("KominkaHillside/PC Foam");
                var thin = Own(new Material(Shader.Find("KominkaHillside/Wash Shell PC Thin")));
                Require(shader != null && !ShaderUtil.ShaderHasError(shader),"Foam shader compile");
                Require(!ShaderUtil.ShaderHasError(thin.shader),"Thin shader compile");
                report.thinPasses = thin.passCount;
                Require(thin.passCount == 3 && thin.GetPassName(0)=="BASE" && thin.GetPassName(1)=="FOAM0" && thin.GetPassName(2)=="FOAM1","UsePass names/count");

                var transforms = new Dictionary<Transform,Transform>();
                Transform Clone(Transform original)
                {
                    if (original == null) return null;
                    if (transforms.TryGetValue(original,out var found)) return found;
                    var parent = Clone(original.parent);
                    var go = new GameObject(original.name); SceneManager.MoveGameObjectToScene(go,scene);
                    go.transform.SetParent(parent,false); go.transform.localPosition = original.localPosition;
                    go.transform.localRotation = original.localRotation; go.transform.localScale = original.localScale;
                    transforms.Add(original,go.transform); return go.transform;
                }
                var cloneBody = Clone(source.transform).gameObject.AddComponent<SkinnedMeshRenderer>();
                cloneBody.sharedMesh=mesh; cloneBody.bones=source.bones.Select(Clone).ToArray(); cloneBody.rootBone=Clone(source.rootBone);
                cloneBody.quality=source.quality; cloneBody.localBounds=source.localBounds; cloneBody.enabled=false;
                var lobe = new KH_PCFoamMesh.Lobe {vertex=anchor,position=mesh.vertices[anchor],normal=mesh.normals[anchor],
                    radius=.03f,uv=new Vector2(.15f,.5f),supportA=new Vector4(.35f,.5f,.55f,.5f),supportB=new Vector4(.75f,.5f,.95f,.5f)};
                var generated = KH_PCFoamMesh.Create(mesh,new[]{lobe});
                AssetDatabase.CreateAsset(generated,ProbeAsset); AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(ProbeAsset,ImportAssetOptions.ForceUpdate);
                var saved=AssetDatabase.LoadAssetAtPath<Mesh>(ProbeAsset);
                CheckStreams(saved); report.vertices=saved.vertexCount; report.triangles=saved.triangles.Length/3;
                var goFoam = new GameObject("Foam"); SceneManager.MoveGameObjectToScene(goFoam,scene);
                goFoam.transform.SetParent(cloneBody.transform.parent,false);
                goFoam.transform.localPosition=cloneBody.transform.localPosition;goFoam.transform.localRotation=cloneBody.transform.localRotation;goFoam.transform.localScale=cloneBody.transform.localScale;
                var foam=goFoam.AddComponent<SkinnedMeshRenderer>();foam.sharedMesh=saved;foam.bones=cloneBody.bones;foam.rootBone=cloneBody.rootBone;foam.quality=cloneBody.quality;
                // localBounds is in rootBone space for these rigs; bound margin uses its smallest world scale.
                var scale=(foam.rootBone ?? foam.transform).lossyScale;
                float minScale=Mathf.Min(Mathf.Abs(scale.x),Mathf.Min(Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
                var bounds=source.localBounds;bounds.Expand(.06f/Mathf.Max(minScale,1e-5f));foam.localBounds=bounds;
                foam.updateWhenOffscreen=false;foam.shadowCastingMode=ShadowCastingMode.Off;foam.receiveShadows=false;
                // This probe renders several poses within one Editor update. Production uses ordinary frame updates.
                foam.forceMatrixRecalculationPerRender=true;
                var material=Own(new Material(shader));material.SetFloat("_Diagnostic",1);foam.sharedMaterial=material;
                var mask=Own(new Texture2D(10,1,TextureFormat.RGBA32,false,true));mask.filterMode=FilterMode.Point;mask.wrapMode=TextureWrapMode.Clamp;material.SetTexture("_Mask",mask);
                var cameraGo=new GameObject("ProofCamera");SceneManager.MoveGameObjectToScene(cameraGo,scene);
                var camera=cameraGo.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.orthographic=true;camera.orthographicSize=.04f;
                camera.nearClipPlane=.01f;camera.farClipPlane=2;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                var rt=Own(new RenderTexture(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));rt.Create();camera.targetTexture=rt;
                var read=Own(new Texture2D(256,256,TextureFormat.RGBA32,false,true));
                var bakedBody=Own(new Mesh());var bakedFoam=Own(new Mesh());
                Vector3 Centre(out float error)
                {
                    // Compensate for the renderer scale before applying its localToWorld matrix (these dogs use 50/100).
                    cloneBody.BakeMesh(bakedBody,true);foam.BakeMesh(bakedFoam,true);
                    var c=cloneBody.transform.TransformPoint(bakedBody.vertices[anchor]);error=0;
                    foreach(var p in bakedFoam.vertices) error=Mathf.Max(error,Vector3.Distance(c,foam.transform.TransformPoint(p)));
                    var weight=mesh.boneWeights[anchor];var bind=mesh.bindposes;var bones=cloneBody.bones;var original=mesh.vertices[anchor];
                    var direct=(bones[weight.boneIndex0].localToWorldMatrix*bind[weight.boneIndex0]).MultiplyPoint3x4(original)*weight.weight0
                        +(bones[weight.boneIndex1].localToWorldMatrix*bind[weight.boneIndex1]).MultiplyPoint3x4(original)*weight.weight1
                        +(bones[weight.boneIndex2].localToWorldMatrix*bind[weight.boneIndex2]).MultiplyPoint3x4(original)*weight.weight2
                        +(bones[weight.boneIndex3].localToWorldMatrix*bind[weight.boneIndex3]).MultiplyPoint3x4(original)*weight.weight3;
                    report.boneMatrixErrorMetres=Vector3.Distance(c,direct);
                    Require(report.boneMatrixErrorMetres<.0001f,"Bake world coordinates vs direct bone matrices");
                    return c;
                }
                void Aim(Vector3 c) { camera.transform.position=c+Vector3.back*.3f;camera.transform.rotation=Quaternion.identity; }
                void Mask(float f,int zero=-1) { var colours=Enumerable.Repeat(new Color(0,f,0,1),10).ToArray();if(zero>=0)colours[zero]=new Color(0,0,0,1);mask.SetPixels(colours);mask.Apply(); }
                Shot Capture(string name)
                {
                    camera.Render();var old=RenderTexture.active;
                    try { RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,256,256),0,0);read.Apply(); }
                    finally { RenderTexture.active=old; }
                    var pixels=read.GetPixels32();int count=0,minX=256,maxX=-1,minY=256,maxY=-1;
                    for(int i=0;i<pixels.Length;i++)
                    {
                        var p=pixels[i];Require(!(p.r>180&&p.b>180&&p.g<60),"Magenta shader failure");
                        if(p.r<20&&p.g<20&&p.b<20)continue;
                        count++;int x=i%256,y=i/256;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                    }
                    var shot=new Shot{name=name,pixels=count,width=count==0?0:maxX-minX+1,height=count==0?0:maxY-minY+1,
                        centreErrorPixels=count==0?0:Mathf.Max(Mathf.Abs((minX+maxX)*.5f-127.5f),Mathf.Abs((minY+maxY)*.5f-127.5f))};
                    report.shots.Add(shot);File.WriteAllBytes(runOutput+"/"+name+".png",read.EncodeToPNG());return shot;
                }
                var centre=Centre(out report.skinErrorMetres);Require(report.skinErrorMetres<.0001f,"Initial skin centre");Aim(centre);
                // Positive control: same sphere topology, conventional non-degenerate mesh, same world radius.
                KH_PCFoamMesh.Sphere(out var sv,out var si);
                var controlMesh=Own(new Mesh {vertices=sv,normals=sv,triangles=si});
                var control=new GameObject("OrdinarySphere");SceneManager.MoveGameObjectToScene(control,scene);control.transform.position=centre;control.transform.localScale=Vector3.one*.03f;
                control.AddComponent<MeshFilter>().sharedMesh=controlMesh;control.AddComponent<MeshRenderer>().sharedMaterial=Own(new Material(Shader.Find("Unlit/Color")));
                foam.enabled=false;var ordinary=Capture("ordinary");Require(ordinary.width>150,"Ordinary sphere control");control.SetActive(false);foam.enabled=true;
                // Isolate shader expansion from SkinnedMeshRenderer's preview skin buffer and culling.
                var staticMesh=Own(UnityEngine.Object.Instantiate(saved));staticMesh.vertices=bakedFoam.vertices;staticMesh.normals=bakedFoam.normals;
                var staticObject=new GameObject("BakedFoamControl");SceneManager.MoveGameObjectToScene(staticObject,scene);
                staticObject.transform.SetParent(foam.transform.parent,false);staticObject.transform.localPosition=foam.transform.localPosition;
                staticObject.transform.localRotation=foam.transform.localRotation;staticObject.transform.localScale=foam.transform.localScale;
                var localCentre=staticObject.transform.InverseTransformPoint(centre);
                var inverse=staticObject.transform.worldToLocalMatrix;
                var extents=new Vector3(((Vector3)inverse.GetRow(0)).magnitude,((Vector3)inverse.GetRow(1)).magnitude,((Vector3)inverse.GetRow(2)).magnitude)*.03f;
                staticMesh.bounds=new Bounds(localCentre,extents*2);
                staticObject.AddComponent<MeshFilter>().sharedMesh=staticMesh;staticObject.AddComponent<MeshRenderer>().sharedMaterial=material;
                foam.enabled=false;Mask(1);var staticShot=Capture("static-expanded");
                Require(Math.Abs(staticShot.width-ordinary.width)<=2 && staticShot.centreErrorPixels<2,"Static expanded world radius/centre");
                staticObject.SetActive(false);foam.enabled=true;
                Mask(1);var full=Capture("full");
                report.worldCentre=centre.ToString("F5");report.worldBounds=foam.bounds.ToString("F5");
                Require(Math.Abs(full.width-ordinary.width)<=2 && full.centreErrorPixels<2,"Expanded world radius/centre");
                Mask(0);Require(Capture("zero").pixels==0,"Mask zero must disappear");
                Mask(.5f);var mid=Capture("half");report.intermediateWidthRatio=(float)mid.width/full.width;
                Require(Mathf.Abs(report.intermediateWidthRatio-Mathf.SmoothStep(0,1,(.5f-.18f)/(.75f-.18f)))<.04f,"Intermediate growth ratio");
                foreach(int index in new[]{1,3,5,7,9}) {Mask(1,index);Require(Capture("support-"+index).pixels==0,"Each support controls disappearance");}
                var bw=mesh.boneWeights[anchor];foam.bones[bw.boneIndex0].localRotation*=Quaternion.Euler(20,30,0);
                centre=Centre(out report.rotatedSkinErrorMetres);Require(report.rotatedSkinErrorMetres<.0001f,"Rotated skin centre");Aim(centre);Mask(1);
                var rotated=Capture("rotated");Require(Math.Abs(rotated.width-full.width)<=2&&rotated.centreErrorPixels<2,"Rotated GPU skin radius/centre");

                // Render UsePass output, not just its pass names. Keep this ordinary quad independent of skinning.
                var quad=new GameObject("ThinPassControl");SceneManager.MoveGameObjectToScene(quad,scene);quad.transform.position=centre;
                var quadMesh=Own(new Mesh{vertices=new[]{new Vector3(-.03f,-.03f,0),new Vector3(.03f,-.03f,0),new Vector3(.03f,.03f,0),new Vector3(-.03f,.03f,0)},
                    normals=Enumerable.Repeat(Vector3.back,4).ToArray(),uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},triangles=new[]{0,2,1,0,3,2}});
                quad.AddComponent<MeshFilter>().sharedMesh=quadMesh;quad.AddComponent<MeshRenderer>().sharedMaterial=thin;
                thin.CopyPropertiesFromMaterial(wash.shellRenderer.sharedMaterial);thin.SetTexture("_Mask",mask);thin.SetTexture("_PosMap",Texture2D.blackTexture);
                var lightObject=new GameObject("ThinLight");SceneManager.MoveGameObjectToScene(lightObject,scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(25,150,0);
                foam.enabled=false;Mask(1);var thinShot=Capture("thin-full");Require(thinShot.pixels>1000,"Thin UsePass renders foam");
                Mask(0);Require(Capture("thin-zero").pixels==0,"Thin zero mask clears");quad.SetActive(false);lightObject.SetActive(false);foam.enabled=true;Mask(1);

                string bundleDir=Output+"/bundle-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");Directory.CreateDirectory(bundleDir);
                var manifest=BuildPipeline.BuildAssetBundles(bundleDir,new[]{new AssetBundleBuild{assetBundleName="foamproof",assetNames=new[]{ProbeAsset,AssetDatabase.GetAssetPath(shader),AssetDatabase.GetAssetPath(thin.shader)}}},BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneWindows64);
                Require(manifest!=null,"Bundle build");bundle=AssetBundle.LoadFromFile(bundleDir+"/foamproof");Require(bundle!=null,"Bundle load");
                var bundled=bundle.LoadAsset<Mesh>(ProbeAsset);CheckStreams(bundled);
                Require(saved.triangles.SequenceEqual(bundled.triangles)&&saved.vertices.SequenceEqual(bundled.vertices),"Bundle topology/anchors");
                for(int channel=0;channel<5;channel++){var a=new List<Vector4>();var b=new List<Vector4>();saved.GetUVs(channel,a);bundled.GetUVs(channel,b);Require(a.SequenceEqual(b),"Bundle UV "+channel);}
                Require(saved.bindposes.SequenceEqual(bundled.bindposes)&&saved.boneWeights.SequenceEqual(bundled.boneWeights),"Bundle skin data");
                foam.sharedMesh=bundled;material.shader=bundle.LoadAsset<Shader>(AssetDatabase.GetAssetPath(shader));material.SetFloat("_Diagnostic",1);material.SetTexture("_Mask",mask);
                var loaded=Capture("bundle");Require(loaded.width==rotated.width&&loaded.pixels==rotated.pixels,"Loaded bundle rendering");
                thin.shader=bundle.LoadAsset<Shader>(AssetDatabase.GetAssetPath(thin.shader));Require(thin.shader!=null&&thin.passCount==3,"Bundled UsePass retained");
                foam.enabled=false;quad.SetActive(true);lightObject.SetActive(true);var bundledThin=Capture("thin-bundle");
                Require(bundledThin.pixels==thinShot.pixels&&bundledThin.width==thinShot.width,"Bundled thin renders same footprint");
                report.result="PASS";
            }
            catch(Exception ex){report.result="FAIL";report.error=ex.ToString();Debug.LogError("[PC Foam G0] "+ex);}
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach(var o in owned)if(o!=null)UnityEngine.Object.DestroyImmediate(o);
                if(bundle!=null)bundle.Unload(true);
                if(AssetDatabase.LoadAssetAtPath<Mesh>(ProbeAsset)!=null)AssetDatabase.DeleteAsset(ProbeAsset);
                report.sceneDirtyAfter=active.isDirty;
                if(report.sceneDirtyAfter!=report.sceneDirtyBefore){report.result="FAIL";report.error+="\nProduction dirty state changed";}
                File.WriteAllText(Output+"/report.json",JsonUtility.ToJson(report,true));
                File.WriteAllText(runOutput+"/report.json",JsonUtility.ToJson(report,true));
            }
        }

        static void CheckStreams(Mesh m)
        {
            Require(m!=null && m.vertexCount==42 && m.triangles.Length==240,"42 vertices/80 triangles");
            for(int i=0;i<5;i++){var uv=new List<Vector4>();m.GetUVs(i,uv);Require(uv.Count==42,"UV channel "+i);}
            Require(m.vertices.All(p=>p==m.vertices[0]),"Collapsed positions retained");
        }
        static int UnmorphedAnchor(Mesh m)
        {
            var affected=new bool[m.vertexCount];var p=new Vector3[m.vertexCount];var n=new Vector3[m.vertexCount];var t=new Vector3[m.vertexCount];
            for(int s=0;s<m.blendShapeCount;s++)for(int f=0;f<m.GetBlendShapeFrameCount(s);f++)
            {m.GetBlendShapeFrameVertices(s,f,p,n,t);for(int i=0;i<p.Length;i++)affected[i]|=p[i].sqrMagnitude>1e-16f||n[i].sqrMagnitude>1e-16f;}
            var weights=m.boneWeights;
            for(int i=0;i<affected.Length;i++)if(!affected[i]&&weights[i].weight1>.1f)return i;
            for(int i=0;i<affected.Length;i++)if(!affected[i])return i;
            throw new InvalidOperationException("No morph-free proof anchor");
        }
        static void Require(bool condition,string label) {if(!condition)throw new InvalidOperationException(label);}
    }
}
