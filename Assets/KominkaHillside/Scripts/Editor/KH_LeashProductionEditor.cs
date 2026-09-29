using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;

namespace KominkaHillside.Editor
{
    // Explicit production migration. Import, apply and verify remain separate steps.
    public static class KH_LeashProductionEditor
    {
        const string Models="Assets/KominkaHillside/Models/Walking/";
        const string Textures="Assets/KominkaHillside/Textures/Walking/";
        const string Data="Assets/KominkaHillside/Data/Walking/";
        static readonly string[] Keys={"Corgi","Pome","Lab","Dachs","Shiba","Samoyed"};
        static readonly float[] RingY={.10f,.105f,.15f,.09f,.105f,.185f};
        // BUG-DOG-002: the one place the handle placement values live (Production 7 writes them, Production 3 checks
        // them, Walking/3 Build uses them). Values tuned by feel in PCVR come back HERE, then Production 7.
        // HandleGripOffset: the rear belt (handle local +Y 0.062..0.065) sits on the grip point at .062; larger = the
        // loop moves forward along the rope axis, smaller = back into the fist (forward only, no up/down/sideways).
        // HandleRollDegrees: roll of the ring plane about the rope axis for the right hand; the left hand uses the
        // negated value. 0 = the belt runs along the knuckle line (finger-bone path).
        public const float HandleGripOffset=.062f, HandleRollDegrees=0f;
        static void Guard()
        {
            if(Application.isPlaying || EditorSceneManager.GetActiveScene().path!="Assets/Scenes/VRCDefaultWorldScene.unity")
                throw new InvalidOperationException("Open the hillside scene in Edit mode");
        }
        static void Report(string name,string value)
        {Directory.CreateDirectory("Temp/KH_LeashRelease");File.WriteAllText("Temp/KH_LeashRelease/"+name+".txt",value);Debug.Log("[KH Production] "+name);}
        static Mesh Mesh(string name)=>AssetDatabase.LoadAssetAtPath<Mesh>(Data+name+"_Blender.asset");
        static Material Material=>AssetDatabase.LoadAssetAtPath<Material>(Data+"KH_Leash_WovenMetal.mat");
        static void ReplaceMesh(Mesh target,Mesh source)
        {
            // CopySerialized alone can leave a stale GPU buffer in this Unity version.
            target.Clear();target.indexFormat=source.indexFormat;target.vertices=source.vertices;
            target.normals=source.normals;target.tangents=source.tangents;target.uv=source.uv;target.uv2=source.uv2;
            target.subMeshCount=source.subMeshCount;
            for(int i=0;i<source.subMeshCount;i++)target.SetTriangles(source.GetTriangles(i),i,false);
            target.bindposes=source.bindposes;target.boneWeights=source.boneWeights;target.bounds=source.bounds;
            target.name=source.name;target.UploadMeshData(false);EditorUtility.SetDirty(target);
        }

        [MenuItem("Tools/KominkaHillside/Walking/Production 1 Import")]
        public static void Import()
        {
            Guard();var log=new StringBuilder();
            foreach(var name in Keys.Select(x=>"KH_Collar_"+x).Concat(new[]{"KH_LeashSnap","KH_LeashHandle","KH_LeashRope"}))
            {
                var importer=(ModelImporter)AssetImporter.GetAtPath(Models+name+".fbx");
                if(importer==null)throw new InvalidOperationException("Missing Blender FBX: "+name);
                importer.importAnimation=false;importer.materialImportMode=ModelImporterMaterialImportMode.None;
                importer.importNormals=ModelImporterNormals.Import;importer.importTangents=ModelImporterTangents.CalculateMikk;
                importer.isReadable=false;importer.bakeAxisConversion=true;importer.SaveAndReimport();
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Models+name+".fbx");
                var filters=prefab.GetComponentsInChildren<MeshFilter>(true);
                if(filters.Length!=1)throw new InvalidOperationException("Expected one authored mesh: "+name);
                var source=filters[0];var mesh=new Mesh{name=name+"_Blender"};
                mesh.CombineMeshes(new[]{new CombineInstance{mesh=source.sharedMesh,transform=source.transform.localToWorldMatrix}},true,true);
                mesh.RecalculateBounds();mesh.RecalculateTangents();
                var old=Mesh(name);if(old==null)AssetDatabase.CreateAsset(mesh,Data+name+"_Blender.asset");
                else{ReplaceMesh(old,mesh);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;}
                log.AppendLine(name+" triangles="+mesh.triangles.Length/3+" bounds="+mesh.bounds.ToString("F4")+" uv="+mesh.uv.Length);
            }
            foreach(string name in new[]{"BaseColor","Normal","Metallic"})
            {
                var t=(TextureImporter)AssetImporter.GetAtPath(Textures+"KH_Leash_"+name+".png");
                t.textureType=name=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
                t.sRGBTexture=name=="BaseColor";t.alphaSource=TextureImporterAlphaSource.FromInput;t.alphaIsTransparency=false;
                t.mipmapEnabled=true;t.maxTextureSize=1024;t.textureCompression=TextureImporterCompression.CompressedHQ;
                t.wrapMode=TextureWrapMode.Repeat;t.filterMode=FilterMode.Trilinear;t.anisoLevel=4;t.SaveAndReimport();
            }
            var mat=Material;
            if(mat==null){mat=new Material(Shader.Find("VRChat/Mobile/Standard Lite"));AssetDatabase.CreateAsset(mat,Data+"KH_Leash_WovenMetal.mat");}
            mat.color=Color.white;mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Textures+"KH_Leash_BaseColor.png");
            mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Textures+"KH_Leash_Normal.png"));
            mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Textures+"KH_Leash_Metallic.png"));
            mat.SetFloat("_BumpScale",.35f);mat.SetFloat("_Glossiness",.45f);mat.EnableKeyword("_NORMALMAP");mat.EnableKeyword("_METALLICGLOSSMAP");
            mat.SetFloat("_SpecularHighlights",1);mat.SetFloat("_GlossyReflections",1);mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");mat.DisableKeyword("_GLOSSYREFLECTIONS_OFF");
            EditorUtility.SetDirty(mat);AssetDatabase.SaveAssets();Report("import",log.ToString());
        }
        static Transform Child(Transform parent,string name)
        {
            var t=parent.Find(name);if(t!=null)return t;
            t=new GameObject(name).transform;t.SetParent(parent,false);t.gameObject.layer=14;return t;
        }
        static MeshRenderer Visual(Transform t,Mesh mesh,Material material)
        {
            var filter=t.GetComponent<MeshFilter>();if(filter==null)filter=t.gameObject.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
            var renderer=t.GetComponent<MeshRenderer>();if(renderer==null)renderer=t.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            return renderer;
        }
        public static void Fit(Transform collar,Transform anchor,Transform dog,int breed)
        {
            var bones=dog.GetComponentsInChildren<Transform>(true);
            var neck=bones.First(t=>t.name=="Bone.003");var head=bones.First(t=>t.name=="Bone.004");
            collar.SetParent(neck,true);
            collar.position=Vector3.Lerp(neck.position,head.position,.45f);
            if(breed==2)collar.position+=dog.TransformDirection(new Vector3(0,-.008f,.040f));
            if(breed==4)collar.position+=dog.TransformDirection(new Vector3(0,-.020f,0));
            collar.rotation=Quaternion.LookRotation(head.position-neck.position,dog.up);
            var s=neck.lossyScale;collar.localScale=new Vector3(1/s.x,1/s.y,1/s.z);
            anchor.localPosition=new Vector3(0,RingY[breed],0);anchor.localRotation=Quaternion.identity;
        }
        [MenuItem("Tools/KominkaHillside/Walking/Production 2 Apply")]
        public static void Apply()
        {
            Guard();var manager=UnityEngine.Object.FindObjectOfType<KH_LeashManager>();
            if(manager==null||manager.dogRoots.Length!=6)throw new InvalidOperationException("Six dog manager required");
            foreach(var key in Keys)if(Mesh("KH_Collar_"+key)==null)throw new InvalidOperationException("Run production import first");
            var ropeBounds=Mesh("KH_LeashRope").bounds;
            if(Mathf.Abs(ropeBounds.min.z)>.0001f||Mathf.Abs(ropeBounds.max.z-1f)>.0001f)
                throw new InvalidOperationException("Rope export endpoints must be Z=0 and Z=1 metre");
            var log=new StringBuilder();
            for(int i=0;i<6;i++)
            {
                var collar=manager.collarAnchors[i].GetComponentInParent<KH_LeashCollar>();var t=collar.transform;
                Fit(t,collar.anchor,manager.dogRoots[i],i);
                Visual(t.Find("Band"),Mesh("KH_Collar_"+Keys[i]),Material);
                foreach(string obsolete in new[]{"RingMount","ClipAnchor/AttachmentRing"})
                {var old=t.Find(obsolete);if(old!=null)old.gameObject.SetActive(false);}
                collar.GetComponent<SphereCollider>().center=collar.anchor.localPosition;
                var root=Child(manager.transform,"KH_ProductionLead_"+Keys[i]);root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.localScale=Vector3.one;
                var rope=root.GetComponent<SkinnedMeshRenderer>();if(rope==null)rope=root.gameObject.AddComponent<SkinnedMeshRenderer>();
                var bones=new Transform[12];var bind=new Matrix4x4[12];
                for(int j=0;j<12;j++){bones[j]=Child(root,"Rope"+j);bones[j].localPosition=new Vector3(0,0,j/11f);bones[j].localRotation=Quaternion.identity;bind[j]=bones[j].worldToLocalMatrix*root.localToWorldMatrix;}
                var mesh=UnityEngine.Object.Instantiate(Mesh("KH_LeashRope"));mesh.name="KH_LeashRope_Skinned";
                var weights=new BoneWeight[mesh.vertexCount];var vertices=mesh.vertices;
                for(int j=0;j<weights.Length;j++)weights[j]=new BoneWeight{boneIndex0=Mathf.Clamp(Mathf.RoundToInt(vertices[j].z*11),0,11),weight0=1};
                mesh.boneWeights=weights;mesh.bindposes=bind;
                string path=Data+"KH_LeashRope_Skinned.asset";var oldMesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(oldMesh==null)AssetDatabase.CreateAsset(mesh,path);else{ReplaceMesh(oldMesh,mesh);UnityEngine.Object.DestroyImmediate(mesh);mesh=oldMesh;}
                rope.sharedMesh=mesh;rope.sharedMaterial=Material;rope.bones=bones;rope.rootBone=root;rope.quality=SkinQuality.Bone1;rope.localBounds=new Bounds(new Vector3(0,0,.5f),Vector3.one*2);rope.enabled=false;
                collar.ropeMesh=rope;collar.ropeBones=bones;
                collar.snapVisual=Visual(Child(manager.transform,"KH_ConnectedSnap_"+Keys[i]),Mesh("KH_LeashSnap"),Material);
                collar.handleVisual=Visual(Child(manager.transform,"KH_ConnectedHandle_"+Keys[i]),Mesh("KH_LeashHandle"),Material);
                collar.snapVisual.enabled=false;collar.handleVisual.enabled=false;collar.line.enabled=false;
                UdonSharpEditorUtility.CopyProxyToUdon(collar);EditorUtility.SetDirty(collar);
                var clip=manager.localClips[i];clip.transform.localScale=Vector3.one;
                Visual(clip.transform,Mesh("KH_LeashSnap"),Material);
                // Reuse a small prototype ring only as the readiness indicator; the snap keeps its authored material.
                var led=Child(clip.transform,"ReadyIndicator");led.localPosition=new Vector3(.015f,.01f,0);led.localScale=Vector3.one*.19f;
                clip.indicator=Visual(led,AssetDatabase.LoadAssetAtPath<Mesh>(Data+"KH_AttachmentRing.asset"),clip.blockedMaterial);
                clip.tip.localPosition=new Vector3(0,.045f,0);
                UdonSharpEditorUtility.CopyProxyToUdon(clip);EditorUtility.SetDirty(clip);
                log.AppendLine(Keys[i]+" parent="+t.parent.name+" positionDog="+manager.dogRoots[i].InverseTransformPoint(t.position).ToString("F4"));
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());Report("apply",log.ToString());
        }
        [MenuItem("Tools/KominkaHillside/Walking/Production 3 Verify")]
        public static void Verify()
        {
            Guard();var m=UnityEngine.Object.FindObjectOfType<KH_LeashManager>();var log=new StringBuilder();int failed=0;
            Action<bool,string> check=(ok,label)=>{log.AppendLine((ok?"PASS ":"FAIL ")+label);if(!ok)failed++;};
            check(m!=null&&m.dogRoots.Length==6,"six dogs");
            for(int i=0;i<6;i++)
            {
                var c=m.collarAnchors[i].GetComponentInParent<KH_LeashCollar>();var band=c.transform.Find("Band").GetComponent<MeshFilter>();
                check(c.transform.parent.name=="Bone.003",Keys[i]+" neck root attachment");
                check(band.sharedMesh==Mesh("KH_Collar_"+Keys[i]),Keys[i]+" Blender collar");
                check(c.ropeMesh!=null&&c.ropeBones.Length==12&&c.ropeBones.All(x=>x!=null),Keys[i]+" twelve rope bones");
                check(c.ropeMesh.sharedMesh.boneWeights.All(w=>w.weight0==1&&w.boneIndex0<12),Keys[i]+" rope weights");
                check(Enumerable.Range(0,12).All(b=>c.ropeMesh.sharedMesh.boneWeights.Any(w=>w.boneIndex0==b&&w.weight0>0)),Keys[i]+" every rope bone has vertices");
                var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
                backing.publicVariables.TryGetVariableValue("ropeBones",out Transform[] stored);
                check(stored!=null&&stored.SequenceEqual(c.ropeBones),Keys[i]+" rope backing references");
                backing.publicVariables.TryGetVariableValue("ropeMesh",out SkinnedMeshRenderer storedRenderer);
                check(storedRenderer==c.ropeMesh,Keys[i]+" mesh backing reference");
                // BUG-DOG-002: the grip offset is applied with rotation only, so the handle must stay at scale 1
                // or the offset lands at the wrong distance from the hand.
                check(c.handleVisual!=null&&Vector3.Distance(c.handleVisual.transform.lossyScale,Vector3.one)<1e-4f,Keys[i]+" handle scale one");
                var clip=m.localClips[i];check(clip.indicator.transform.name=="ReadyIndicator"&&clip.GetComponent<Renderer>().sharedMaterial==Material,Keys[i]+" separate readiness indicator");
                check(Vector3.Distance(clip.tip.localPosition,new Vector3(0,.045f,0))<.0001f,Keys[i]+" physical contact preserved");
            }
            check(Material.shader.isSupported,"supported shared material");
            // BUG-DOG-002: the handle placement values the six collars share. Reported every run so the PCVR
            // judgement has the numbers it was made against; only the usable range is enforced here.
            log.AppendLine("handleGripOffset="+m.handleGripOffset.ToString("F4")+"m handleRollDegrees="+m.handleRollDegrees.ToString("F1")+"deg (right hand; left hand uses the negated roll)");
            check(m.handleGripOffset>=0f&&m.handleGripOffset<=.1f,"handle grip offset within 0..0.1 m");
            check(m.handleRollDegrees>=-180f&&m.handleRollDegrees<=180f,"handle ring roll within -180..180 deg");
            // Builder-owned: the scene must hold the editor constants (Production 7), not an Inspector leftover
            var mb=UdonSharpEditorUtility.GetBackingUdonBehaviour(m);
            mb.publicVariables.TryGetVariableValue("handleGripOffset",out float storedGrip);
            mb.publicVariables.TryGetVariableValue("handleRollDegrees",out float storedRoll);
            check(Mathf.Abs(m.handleGripOffset-HandleGripOffset)<1e-5f&&Mathf.Abs(storedGrip-HandleGripOffset)<1e-5f,"handle grip offset == builder "+HandleGripOffset.ToString("F4")+" (proxy and backing)");
            check(Mathf.Abs(m.handleRollDegrees-HandleRollDegrees)<1e-4f&&Mathf.Abs(storedRoll-HandleRollDegrees)<1e-4f,"handle ring roll == builder "+HandleRollDegrees.ToString("F1")+" (proxy and backing)");
            log.AppendLine("failed="+failed);Report("verify",log.ToString());
            if(failed>0)throw new InvalidOperationException("Production verification failed");
        }
        // BUG-DOG-002: Walking/3 Build is refused on the river-loop production scene (KH_ProductionScene), so this is the
        // builder that writes the handle placement values. Touches only these two fields of KH_LeashManager.
        [MenuItem("Tools/KominkaHillside/Walking/Production 7 Apply Handle Tuning")]
        public static void ApplyHandleTuning()
        {
            Guard();var m=UnityEngine.Object.FindObjectOfType<KH_LeashManager>();
            if(m==null)throw new InvalidOperationException("KH_LeashManager required");
            string before="handleGripOffset="+m.handleGripOffset.ToString("F4")+" handleRollDegrees="+m.handleRollDegrees.ToString("F1");
            m.handleGripOffset=HandleGripOffset;m.handleRollDegrees=HandleRollDegrees;
            UdonSharpEditorUtility.CopyProxyToUdon(m);EditorUtility.SetDirty(m);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Report("handle-tuning","before "+before+"\nafter handleGripOffset="+HandleGripOffset.ToString("F4")+" handleRollDegrees="+HandleRollDegrees.ToString("F1")+"\n(scene marked dirty; save it, then Production 3 Verify)");
        }
        [MenuItem("Tools/KominkaHillside/Walking/Production 4 Capture")]
        public static void Capture()
        {
            Guard();var m=UnityEngine.Object.FindObjectOfType<KH_LeashManager>();string dir="Temp/KH_LeashRelease/World";Directory.CreateDirectory(dir);
            for(int i=0;i<6;i++)
            {
                var c=m.collarAnchors[i];var dog=m.dogRoots[i];
                KH_ViewShotsEditor.Shot(Keys[i]+"-front",c.position+dog.TransformDirection(new Vector3(.4f,.12f,.6f)),c.position,45,dir);
                KH_ViewShotsEditor.Shot(Keys[i]+"-back",c.position+dog.TransformDirection(new Vector3(-.3f,.25f,-.5f)),c.position,45,dir);
            }
        }
        [MenuItem("Tools/KominkaHillside/Walking/Production 6 Check Rope Geometry")]
        public static void CheckRopeGeometry()
        {
            Guard();var m=UnityEngine.Object.FindObjectOfType<KH_LeashManager>();
            var c=m.collarAnchors[0].GetComponentInParent<KH_LeashCollar>();var rope=c.ropeMesh;
            var transforms=c.ropeBones.Concat(new[]{rope.transform,c.snapVisual.transform,c.handleVisual.transform}).ToArray();var positions=transforms.Select(t=>t.position).ToArray();var rotations=transforms.Select(t=>t.rotation).ToArray();
            var oldBounds=rope.localBounds;bool enabled=rope.enabled,snapEnabled=c.snapVisual.enabled,handleEnabled=c.handleVisual.enabled;var log=new StringBuilder();
            var update=typeof(KH_LeashCollar).GetMethod("UpdateGeometry",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            var a=c.anchor.position+new Vector3(.8f,.7f,-1);var ends=new[]{a+Vector3.forward*1.5f,a+Vector3.up*2,a+Vector3.right*4,a};
            int fail=0;
            try
            {
                for(int sample=0;sample<ends.Length;sample++)
                {
                    update.Invoke(c,new object[]{a,ends[sample],true});
                    var vertices=KH_LeashFitInspector.SkinWorldVertices(rope);var weights=rope.sharedMesh.boneWeights;
                    Vector3 first=Vector3.zero,last=Vector3.zero;int nf=0,nl=0;float radialError=0;
                    for(int i=0;i<vertices.Length;i++)
                    {
                        int b=weights[i].boneIndex0;radialError=Mathf.Max(radialError,Mathf.Abs(Vector3.Distance(vertices[i],c.ropeBones[b].position)-.006f));
                        if(b==0){first+=vertices[i];nf++;}if(b==11){last+=vertices[i];nl++;}
                    }
                    float startError=Vector3.Distance(first/nf,a),endError=Vector3.Distance(last/nl,ends[sample]);
                    bool ok=startError<.002f&&endError<.002f&&radialError<.0001f;
                    if(!ok)fail++;log.AppendLine((ok?"PASS ":"FAIL ")+"sample="+sample+" startMm="+(startError*1000).ToString("F3")+" endMm="+(endError*1000).ToString("F3")+" radiusErrorMm="+(radialError*1000).ToString("F3"));
                }
                typeof(KH_LeashCollar).GetMethod("UpdateModeledGeometry",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(c,new object[]{a,c.anchor.position,Quaternion.identity});update.Invoke(c,new object[]{c.handleVisual.transform.TransformPoint(new Vector3(0,-.077f,0)),c.snapVisual.transform.TransformPoint(new Vector3(0,-.047f,0)),true});rope.enabled=true;c.snapVisual.enabled=true;c.handleVisual.enabled=true;
                string dir="Temp/KH_LeashRelease/World";Directory.CreateDirectory(dir);
                // Independent CPU-LBS diagnostic; this does not claim runtime GPU skinning was tested.
                var baked=new Mesh();baked.vertices=KH_LeashFitInspector.SkinWorldVertices(rope);baked.uv=rope.sharedMesh.uv;baked.triangles=rope.sharedMesh.triangles;baked.RecalculateNormals();baked.RecalculateBounds();baked.UploadMeshData(false);var frozen=new GameObject("KH_RopeEvidence");
                try
                {

                    frozen.AddComponent<MeshFilter>().sharedMesh=baked;frozen.AddComponent<MeshRenderer>().sharedMaterial=Material;rope.enabled=false;
                    KH_ViewShotsEditor.Shot("rope-editor-shape",Vector3.Lerp(a,c.anchor.position,.5f)+new Vector3(2,.5f,.3f),Vector3.Lerp(a,c.anchor.position,.5f),50,dir);
                }
                finally{UnityEngine.Object.DestroyImmediate(frozen);UnityEngine.Object.DestroyImmediate(baked);}
                log.AppendLine("Editor proxy geometry and skinned vertices only; Udon runtime/VR not exercised. failed="+fail);
            }
            finally
            {
                rope.transform.SetPositionAndRotation(positions[12],rotations[12]);
                for(int i=0;i<12;i++)c.ropeBones[i].SetPositionAndRotation(positions[i],rotations[i]);
                for(int i=13;i<transforms.Length;i++)transforms[i].SetPositionAndRotation(positions[i],rotations[i]);
                rope.localBounds=oldBounds;rope.enabled=enabled;c.snapVisual.enabled=snapEnabled;c.handleVisual.enabled=handleEnabled;
            }
            Report("rope-geometry",log.ToString());if(fail>0)throw new InvalidOperationException("Rope geometry failed");
        }
    }
}
