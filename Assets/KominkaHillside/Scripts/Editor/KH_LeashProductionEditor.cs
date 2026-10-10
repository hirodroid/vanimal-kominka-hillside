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
        // BUG-DOG-011: the ring is centred on the neck cross-section of the karada skin, not on the bone axis
        // (the neck bone runs along the back of the neck, so the old Lerp(Bone.003, Bone.004, .45) floated on top
        // and sank under the throat). Tuning points are the five tables BandRadii / FurMargin / TiltBlend /
        // AlongOffset / ExtraOffset; their values are refined by the multi-view shots (artifacts/collar-review).
        // Production 8b Collar Fit Report is the dry run (writes Temp/KH_CollarFit/report.txt, scene untouched).
        public struct CollarFit
        {
            public Vector3 position;public Quaternion rotation;public Vector2 scaleXY;
            public Vector3 centerShift;public Vector2 halfExtents;public Vector2 targetRadii;public int samples;public string note;
        }
        // Band ellipse radii (m; x = left/right, y = up/down). Same values as `radii` in scripts/art/leash-assets.cjs.
        static readonly Vector2[] BandRadii={new Vector2(.090f,.075f),new Vector2(.09f,.08f),new Vector2(.100f,.125f),new Vector2(.07f,.065f),new Vector2(.09f,.080f),new Vector2(.16f,.16f)};
        // Clearance added to the skin cross-section for fur (m). Provisional values to be tuned by shots: the material
        // fur push-out (_FurVector.w x _FurVectorScale) is Corgi .02 / Pome .02 / Lab .025 / Dachs .0325 / Shiba .01 /
        // Samoyed .033, and the visible fur is thinner than that.
        static readonly float[] FurMargin={.012f,0f,.003f,.003f,.004f,0f};
        // Per-breed cap on the ellipse scale (X / Y separately).
        static readonly float[] MaxScaleByBreed={1.7f,1.35f,1.7f,1.6f,1.7f,1.25f};
        // 2026-10-10 user decision (BUG-DOG-011, option 1): on the long-coated dogs (Pome, Samoyed) a real collar sits on
        // the skin under the ruff and is hidden; only the hardware and the leash show. For them the band keeps scale 1
        // (no stretched hardware), its top edge is placed on the top of the skin section (buried in the fur, the bottom
        // ends up inside the body), and the attachment ring is lifted by RingLift so it emerges from the fur.
        static readonly bool[] BuriedInFur={false,true,false,false,false,true};
        // Lift is measured by shots: Samoyed's shader fur reaches ~9cm above the karada skin at the nape, Pome's ~2cm.
        static readonly float[] RingLift={0f,.036f,0f,0f,0f,.065f};
        // The fur is alpha-cut shell layers, so anything between the skin and the outer shells shows through the gaps
        // (rounds 5-6: band on / just under the skin top still read as fur poking through a band). The buried band is
        // therefore placed fully INSIDE the opaque body: top edge SinkDepth under the skin top, sides SideInset inside the
        // skin section (uniform scale), and only the lifted attachment ring (and the leash) emerge from the fur.
        static readonly float[] SinkDepth={0f,.030f,0f,0f,0f,.025f};
        static readonly float[] SideInset={0f,.020f,0f,0f,0f,.030f};
        // 0 = ring plane perpendicular to the neck axis, 1 = vertical plane (axis flattened to horizontal). The blend
        // tips the throat side forward/down onto the chest (BUG-DOG-011 fix B).
        // 2026-10-10 user feedback on the side views: the tilted band leaned forward and left gaps at the nape and the
        // throat on the four visible breeds, so they are now perpendicular to the neck axis (0). The buried breeds keep
        // their value (the band is hidden; only the ring height matters).
        static readonly float[] TiltBlend={0f,.15f,0f,0f,0f,.3f};
        // Slide of the section plane along the neck axis from the 45% point (m, + = toward the head).
        static readonly float[] AlongOffset={0f,0f,0f,0f,0f,0f};
        // Final nudge in collar local space (m; +X right, +Y dorsal (attachment-ring side), +Z head). The old hand corrections
        // (Lab forward 40mm / down 8mm, Shiba down 20mm) are expected to be unnecessary with the section fit;
        // confirm by shots before putting anything here.
        static readonly Vector3[] ExtraOffset={Vector3.zero,Vector3.zero,Vector3.zero,Vector3.zero,Vector3.zero,Vector3.zero};
        const float SliceHalfThickness=.012f,SearchRadiusFactor=2.4f,MinScale=.75f,MaxScale=1.7f;const int MinSamples=20;

        // Pure: reads the current Edit-mode pose of the dog and changes nothing.
        public static CollarFit Solve(Transform dog,int breed)
        {
            var bones=dog.GetComponentsInChildren<Transform>(true);
            var neck=bones.First(t=>t.name=="Bone.003");var head=bones.First(t=>t.name=="Bone.004");
            var fit=new CollarFit{scaleXY=Vector2.one,note=""};
            var a=(head.position-neck.position).normalized;
            var p0=Vector3.Lerp(neck.position,head.position,.45f)+a*AlongOffset[breed];
            var h=a-Vector3.up*Vector3.Dot(a,Vector3.up);
            h=h.sqrMagnitude>1e-6f?h.normalized:a;
            var n=Vector3.Slerp(a,h,TiltBlend[breed]).normalized;
            var up=dog.up-n*Vector3.Dot(dog.up,n);
            if(up.sqrMagnitude<1e-6f){fit.note="ring normal parallel to dog.up; ";up=Vector3.up-n*Vector3.Dot(Vector3.up,n);}
            up=up.normalized;var right=Vector3.Cross(up,n);
            var rotation=Quaternion.LookRotation(n,up);
            if((rotation*Vector3.right-right).sqrMagnitude>1e-6f)fit.note+="WARN basis mismatch LookRotation.x vs Cross(up,n); ";
            var skin=dog.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s=>s.name=="karada");
            float search=SearchRadiusFactor*Mathf.Max(BandRadii[breed].x,BandRadii[breed].y);
            float minX=float.MaxValue,maxX=float.MinValue,minY=float.MaxValue,maxY=float.MinValue;int count=0;
            if(skin!=null&&skin.sharedMesh!=null)
                foreach(var v in KH_LeashFitInspector.SkinWorldVertices(skin))
                {
                    var d=v-p0;float along=Vector3.Dot(d,n);
                    if(Mathf.Abs(along)>=SliceHalfThickness||(d-n*along).magnitude>=search)continue;
                    float x=Vector3.Dot(d,right),y=Vector3.Dot(d,up);
                    minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);count++;
                }
            fit.samples=count;
            if(count<MinSamples)
            {
                // Fallback: the pre-BUG-DOG-011 placement (bone axis + the old Lab / Shiba corrections), scale 1.
                fit.note+="ERROR "+(skin==null?"karada skin not found":"only "+count+" section vertices (<"+MinSamples+")")+"; legacy placement";
                fit.position=Vector3.Lerp(neck.position,head.position,.45f);
                if(breed==2)fit.position+=dog.TransformDirection(new Vector3(0,-.008f,.040f));
                if(breed==4)fit.position+=dog.TransformDirection(new Vector3(0,-.020f,0));
                fit.rotation=Quaternion.LookRotation(head.position-neck.position,dog.up);
                return fit;
            }
            float cx=(minX+maxX)*.5f,cy=(minY+maxY)*.5f,hx=(maxX-minX)*.5f,hy=(maxY-minY)*.5f;
            var target=new Vector2(hx+FurMargin[breed],hy+FurMargin[breed]);
            // X and Y scale separately so the ellipse follows the section; Z (band width) stays 1 so the belt does not fatten.
            float rawX=target.x/BandRadii[breed].x,rawY=target.y/BandRadii[breed].y;
            float maxScale=Mathf.Min(MaxScale,MaxScaleByBreed[breed]);
            fit.scaleXY=new Vector2(Mathf.Clamp(rawX,MinScale,maxScale),Mathf.Clamp(rawY,MinScale,maxScale));
            if(!Mathf.Approximately(rawX,fit.scaleXY.x)||!Mathf.Approximately(rawY,fit.scaleXY.y))fit.note+="scale clamped from ("+rawX.ToString("F3")+","+rawY.ToString("F3")+"); ";
            if(BuriedInFur[breed])
            {
                // Band top on the skin top; the rest of the band is under the fur / inside the body and never seen.
                // Uniform scale (no stretched hardware), never wider than the skin section; band top SinkDepth below the skin top.
                float k=Mathf.Clamp(Mathf.Min(1f,(hx-SideInset[breed])/BandRadii[breed].x),MinScale,1f);
                fit.scaleXY=new Vector2(k,k);cy=cy+hy-SinkDepth[breed]-BandRadii[breed].y*k;
                fit.note+="buried-in-fur: scale "+k.ToString("F3")+", band top "+(SinkDepth[breed]*1000).ToString("F0")+"mm under skin top, ring lift "+(RingLift[breed]*1000).ToString("F0")+"mm; ";
            }
            fit.rotation=rotation;
            fit.position=p0+right*cx+up*cy+rotation*ExtraOffset[breed];
            fit.centerShift=new Vector3(cx,cy,0);fit.halfExtents=new Vector2(hx,hy);fit.targetRadii=target;
            if(fit.note.Length==0)fit.note="ok";
            return fit;
        }
        public static void Fit(Transform collar,Transform anchor,Transform dog,int breed)
        {
            var fit=Solve(dog,breed);
            var neck=dog.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Bone.003");
            collar.SetParent(neck,true);
            collar.SetPositionAndRotation(fit.position,fit.rotation);
            var s=neck.lossyScale;collar.localScale=new Vector3(fit.scaleXY.x/s.x,fit.scaleXY.y/s.y,1f/s.z);
            // The anchor is a child of the collar, so it scales with the band.
            anchor.localPosition=new Vector3(0,RingY[breed]+RingLift[breed],0);anchor.localRotation=Quaternion.identity;
            // The visible D-ring of the Blender band is part of the band mesh and goes under the fur with it on the buried
            // breeds, so for them the procedural AttachmentRing under ClipAnchor (disabled since the Blender import) is
            // switched on: it rides the lifted anchor and is what emerges from the fur. Visible breeds are left untouched.
            if(BuriedInFur[breed])
            {
                var ring=anchor.Find("AttachmentRing");
                if(ring!=null){ring.gameObject.SetActive(true);var rr=ring.GetComponent<Renderer>();if(rr!=null)rr.enabled=true;}
                else Debug.LogWarning("[KH Production] collar fit "+Keys[breed]+": AttachmentRing not found under ClipAnchor");
            }
            // The Interact trigger (KH_DogWalkingEditor adds it with radius .10) lives on the scaled collar: keep its
            // world radius at .10 so the neck/head colliders and the ray priority stay as before (review 2026-10-10).
            var hit=collar.GetComponent<SphereCollider>();
            if(hit!=null){hit.radius=.10f/Mathf.Max(fit.scaleXY.x,fit.scaleXY.y,1f);hit.center=anchor.localPosition;}
            if(fit.note.Contains("ERROR")||fit.note.Contains("WARN"))Debug.LogWarning("[KH Production] collar fit "+Keys[breed]+": "+fit.note);
        }
        // Dry run for BUG-DOG-011: solves all six collars and reports, without touching the scene.
        [MenuItem("Tools/KominkaHillside/Walking/Production 8b Collar Fit Report")]
        public static void CollarFitReport()
        {
            Guard();var m=UnityEngine.Object.FindObjectOfType<KH_LeashManager>();
            if(m==null||m.dogRoots==null||m.dogRoots.Length!=6)throw new InvalidOperationException("Six dog manager required");
            Func<float,string> mm=x=>(x*1000).ToString("F1");
            var log=new StringBuilder("# per dog: key samples=<n> centerShift=(x,y mm) halfExtents=(x,y mm) targetRadii=(x,y mm) kXY=(x,y) delta=<mm from current> note=<text>\n");
            for(int i=0;i<6;i++)
            {
                var fit=Solve(m.dogRoots[i],i);
                var collar=m.collarAnchors!=null&&i<m.collarAnchors.Length&&m.collarAnchors[i]!=null?m.collarAnchors[i].GetComponentInParent<KH_LeashCollar>():null;
                string delta=collar!=null?mm(Vector3.Distance(collar.transform.position,fit.position)):"n/a";
                log.AppendLine(Keys[i]+" samples="+fit.samples+" centerShift=("+mm(fit.centerShift.x)+","+mm(fit.centerShift.y)+")"+
                    " halfExtents=("+mm(fit.halfExtents.x)+","+mm(fit.halfExtents.y)+") targetRadii=("+mm(fit.targetRadii.x)+","+mm(fit.targetRadii.y)+")"+
                    " kXY=("+fit.scaleXY.x.ToString("F3")+","+fit.scaleXY.y.ToString("F3")+") delta="+delta+" note="+fit.note);
            }
            Directory.CreateDirectory("Temp/KH_CollarFit");File.WriteAllText("Temp/KH_CollarFit/report.txt",log.ToString());
            Debug.Log("[KH Production] collar fit report -> Temp/KH_CollarFit/report.txt");
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
                // The Blender band carries its own ring; the procedural ring stays only on the buried breeds (see Fit).
                foreach(string obsolete in new[]{"RingMount","ClipAnchor/AttachmentRing"})
                {if(obsolete.EndsWith("AttachmentRing")&&BuriedInFur[i])continue;var old=t.Find(obsolete);if(old!=null)old.gameObject.SetActive(false);}
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
