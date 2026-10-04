using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace KominkaHillside.Editor
{
    // Imported wash-tool visuals. Keep the runtime roots, Nozzle/State, pickups, homes,
    // Udon wiring and walking colliders. The shower capsule is fitted to the new head/handle;
    // sponge/towel art fits existing boxes. Called by the normal Wash/11 builder too.
    public static partial class KH_DogWashSetupEditor
    {
        const string ArtPath = "Assets/KominkaHillside/Models/WashTools/";
        const string ArtMaterial = ArtPath + "KH_WashTools.mat";
        const string ArtReport = "Temp/WashToolsImport/";
        static readonly string[] ArtNames = { "Shower", "Towel", "Sponge", "ToolShelf", "WashPlatform" };

        [MenuItem(Menu + "31 - Import and Apply Tool Art")]
        public static void ImportAndApplyArt()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Save current changes before applying wash art.");
            // Validate every source before scene mutation.
            foreach (var name in ArtNames)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPath + name + ".fbx");
                if (model == null || model.GetComponentsInChildren<MeshFilter>(true).Length != 1)
                    throw new InvalidOperationException("Expected one mesh: " + name);
            }
            var stations = AllStationTransforms(scene);
            if (stations.Count != 4) throw new InvalidOperationException("Expected all four existing wash stations.");
            Directory.CreateDirectory(ArtReport);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            File.Copy(scene.path, ArtReport + "before-" + stamp + ".unity");
            var before = ArtRuntimeSnapshot(stations);
            File.WriteAllLines(ArtReport + "runtime-before.txt", before);
            PrepareArtAssets();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Import wash-tool visuals");
            foreach (var st in stations)
            {
                Undo.RegisterFullObjectHierarchyUndo(st.gameObject, "Wash-tool visuals");
                ApplyStationArt(st);
                foreach (var tool in st.GetComponentsInChildren<KH_WashTool>(true)) ApplyToolArt(tool.transform, tool.tool);
            }
            Undo.CollapseUndoOperations(group);
            var after = ArtRuntimeSnapshot(stations);
            File.WriteAllLines(ArtReport + "runtime-after.txt", after);
            if (!before.SequenceEqual(after)) throw new InvalidOperationException("Runtime wiring changed; undo before saving.");
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save wash art.");
            VerifyArt();
        }

        static string[] ArtRuntimeSnapshot(List<Transform> stations)
        {
            var lines = new List<string>();
            foreach (var st in stations)
            {
                lines.Add(st.name + "|" + st.position.ToString("F5") + "|" + st.eulerAngles.ToString("F5"));
                foreach (var tool in st.GetComponentsInChildren<KH_WashTool>(true))
                {
                    var backing = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(tool);
                    var pickup = tool.GetComponent<VRC.SDK3.Components.VRCPickup>();
                    lines.Add(tool.GetInstanceID() + "|" + tool.name + "|" + tool.transform.localPosition.ToString("F5") + "|" + tool.transform.localEulerAngles.ToString("F5")
                        + "|" + tool.nozzle.GetInstanceID() + "|" + tool.nozzle.localPosition.ToString("F5") + "|" + tool.nozzle.localEulerAngles.ToString("F5")
                        + "|" + tool.home.GetInstanceID() + "|" + tool.home.localPosition.ToString("F5") + "|" + tool.home.localEulerAngles.ToString("F5")
                        + "|" + tool.returnSeconds + "|" + tool.state.GetInstanceID() + "|" + backing.GetInstanceID()
                        + "|" + tool.GetComponent<VRC.SDK3.Components.VRCObjectSync>().GetInstanceID()
                        + "|" + pickup.GetInstanceID() + "|" + pickup.UseText + "|" + string.Join(",", tool.washes.Select(w=>w.GetInstanceID())));
                }
                var reset = st.GetComponentInChildren<KH_WashStationReset>(true);
                lines.Add("reset|" + reset.GetInstanceID() + "|" + string.Join(",", reset.tools.Select(t=>t.GetInstanceID())));
            }
            return lines.ToArray();
        }

        static void PrepareArtAssets()
        {
            foreach (var name in ArtNames)
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(ArtPath + name + ".fbx");
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false; imp.importCameras = false; imp.importLights = false;
                imp.isReadable = false; imp.addCollider = false;
                imp.generateSecondaryUV = name == "ToolShelf" || name == "WashPlatform";
                imp.SaveAndReimport();
            }
            ConfigureArtTexture("WashTools_BaseColor.png", true, 1024, 512, false);
            ConfigureArtTexture("WashTools_Roughness.png", false, 1024, 256, true);
            var rough = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "WashTools_Roughness.png");
            var packed = new Texture2D(256,256,TextureFormat.RGBA32,false,true);
            var colors = new Color[256*256];
            for (int y=0;y<256;y++) for (int x=0;x<256;x++)
                colors[y*256+x]=new Color(0,0,0,1f-rough.GetPixelBilinear((x+.5f)/256,(y+.5f)/256).r);
            packed.SetPixels(colors); packed.Apply();
            File.WriteAllBytes(ArtPath + "WashTools_MetallicSmoothness.png", packed.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(packed);
            AssetDatabase.ImportAsset(ArtPath + "WashTools_MetallicSmoothness.png");
            ConfigureArtTexture("WashTools_Roughness.png", false, 1024, 256, false);
            ConfigureArtTexture("WashTools_MetallicSmoothness.png", false, 256, 256, false);
            var shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Standard shader missing");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ArtMaterial);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, ArtMaterial); }
            mat.shader=shader; mat.color=Color.white;
            mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath+"WashTools_BaseColor.png");
            mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath+"WashTools_MetallicSmoothness.png"));
            mat.SetFloat("_Metallic",0); mat.SetFloat("_GlossMapScale",1); mat.SetFloat("_Glossiness",.18f);
            mat.SetFloat("_Mode",0); mat.EnableKeyword("_METALLICGLOSSMAP");
            mat.enableInstancing=true; EditorUtility.SetDirty(mat);
        }

        static void ConfigureArtTexture(string name, bool srgb, int size, int mobile, bool readable)
        {
            var ti=(TextureImporter)AssetImporter.GetAtPath(ArtPath+name);
            ti.textureType=TextureImporterType.Default; ti.sRGBTexture=srgb; ti.isReadable=readable;
            ti.mipmapEnabled=true; ti.maxTextureSize=size; ti.textureCompression=TextureImporterCompression.Compressed;
            ti.wrapMode=TextureWrapMode.Clamp;
            ti.alphaSource=name.Contains("MetallicSmoothness") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            foreach (string platform in new[]{"Android","iPhone"})
            {
                var ps=ti.GetPlatformTextureSettings(platform);
                ps.name=platform; ps.overridden=true; ps.maxTextureSize=mobile;
                ps.format=TextureImporterFormat.ASTC_6x6; ps.compressionQuality=50;
                ti.SetPlatformTextureSettings(ps);
            }
            ti.SaveAndReimport();
        }

        static bool ArtReady { get { return AssetDatabase.LoadAssetAtPath<Material>(ArtMaterial)!=null; } }

        // Plain child renderer, no nested prefab/Udon boundary. Preserve imported FBX axis conversion.
        static Transform ArtModel(Transform parent,string childName,string asset)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(ArtPath+asset+".fbx");
            if(source==null) throw new InvalidOperationException("Missing " + asset);
            var mf=source.GetComponentInChildren<MeshFilter>(true);
            var wrap=Child(parent,childName);
            wrap.localPosition=Vector3.zero;wrap.localRotation=Quaternion.identity;wrap.localScale=Vector3.one;
            wrap.gameObject.layer=parent.gameObject.layer;
            var mesh=Child(wrap,"Mesh"); mesh.gameObject.layer=wrap.gameObject.layer;
            mesh.localPosition=mf.transform.position; mesh.localRotation=mf.transform.rotation; mesh.localScale=mf.transform.lossyScale;
            var filter=mesh.GetComponent<MeshFilter>();if(filter==null)filter=mesh.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh=mf.sharedMesh;
            var renderer=mesh.GetComponent<MeshRenderer>();if(renderer==null)renderer=mesh.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(ArtMaterial);renderer.enabled=true;
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
            renderer.lightProbeUsage=LightProbeUsage.BlendProbes;
            foreach(var t in source.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("Anchor") || t.name.EndsWith("Rest")))
            {
                var a=Child(wrap,t.name);a.localPosition=t.position;a.localRotation=t.rotation;a.localScale=Vector3.one;
            }
            return wrap;
        }

        static Bounds ArtBounds(Transform root,Transform relative)
        {
            bool first=true;var b=new Bounds();
            foreach(var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mb=mf.sharedMesh.bounds;var m=relative.worldToLocalMatrix*mf.transform.localToWorldMatrix;
                for(int k=0;k<8;k++)
                {
                    var p=m.MultiplyPoint3x4(mb.center+Vector3.Scale(mb.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1)));
                    if(first){b=new Bounds(p,Vector3.zero);first=false;}else b.Encapsulate(p);
                }
            }
            return b;
        }

        static void HideArtPrimitive(Transform parent,string name)
        {
            var t=parent.Find(name);if(t==null)return;
            var renderer=t.GetComponent<MeshRenderer>();if(renderer!=null){renderer.enabled=false;EditorUtility.SetDirty(renderer);}
        }

        static void ApplyToolArt(Transform root,int kind)
        {
            // Prop finish adds a visual only; the original nozzle/pickup/collider remain authoritative.
            if(kind==KH_DogWash.ToolBottle)
            {
                var stale=root.Find("ImportedVisual");if(stale!=null)UnityEngine.Object.DestroyImmediate(stale.gameObject);
                KH_PropFinishEditor.FinishBottle(root);
                return;
            }
            if(!ArtReady)return;
            string asset=kind==KH_DogWash.ToolShower?"Shower":kind==KH_DogWash.ToolSponge?"Sponge":"Towel";
            var art=ArtModel(root,"ImportedVisual",asset);
            if(kind==KH_DogWash.ToolShower)
            {
                HideArtPrimitive(root,"Handle");HideArtPrimitive(root,"Head");
                art.localScale=Vector3.one*.75f;
                var a=art.Find("Shower_NozzleAnchor");var nozzle=root.Find("Nozzle");
                art.localPosition=nozzle.localPosition-root.InverseTransformPoint(a.position);
                var bounds=ArtBounds(art,root);var cap=root.GetComponent<CapsuleCollider>();
                cap.center=bounds.center;cap.height=bounds.size.y;cap.radius=Mathf.Max(bounds.size.x,bounds.size.z)*.5f;
                EditorUtility.SetDirty(cap);
            }
            else
            {
                HideArtPrimitive(root,"Visual");
                var box=root.GetComponent<BoxCollider>();var b=ArtBounds(art,root);
                art.localScale=new Vector3(box.size.x/b.size.x,box.size.y/b.size.y,box.size.z/b.size.z);
                var fitted=ArtBounds(art,root);art.localPosition=box.center-fitted.center;
            }
        }

        static void ApplyStationArt(Transform station)
        {
            if(!ArtReady)return;
            var shelf=ArtModel(station,"ImportedShelf","ToolShelf");
            var b=ArtBounds(shelf,station);
            shelf.localScale=new Vector3(ShelfWidth/b.size.x,ShelfHeight/.7725f,ShelfDepth/b.size.z);
            shelf.localRotation=Quaternion.Euler(0,180,0); // FBX front +Z -> station operation side -Z
            HideArtPrimitive(station,"Shelf");
            for(int i=0;i<4;i++)HideArtPrimitive(station,"ShelfLeg_"+i);
            // Keep the existing walking footprint/top, avoiding changes to navigation and ground-ray contracts.
            var table=FindTable(station);var platform=ArtModel(table,"ImportedPlatform","WashPlatform");
            var pb=ArtBounds(platform,table);
            platform.localScale=new Vector3(TableSize.x/pb.size.x,TableSize.y/pb.size.y,TableSize.z/pb.size.z);
            for(int i=0;i<TableSlats;i++)HideArtPrimitive(table,"Slat_"+i);
            KH_PropFinishEditor.FinishShelf(station);
        }

        [MenuItem(Menu + "32 - Verify Imported Tool Art")]
        public static void VerifyArt()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var lines=new List<string>();int fails=0,triangles=0,models=0;
            Action<bool,string> need=(ok,msg)=>{lines.Add((ok?"PASS ":"FAIL ")+msg);if(!ok)fails++;};
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();var stations=AllStationTransforms(scene);
            var mat=AssetDatabase.LoadAssetAtPath<Material>(ArtMaterial);
            need(stations.Count==4,"four wash stations");
            need(mat!=null && mat.shader.name=="Standard" && mat.mainTexture!=null && mat.GetTexture("_MetallicGlossMap")!=null,"shared opaque textured material");
            foreach(var st in stations)
            {
                var table=FindTable(st);   // under Lift since IMP-ENV-011
                var shelf=st.Find("ImportedShelf");var platform=table.Find("ImportedPlatform");
                need(shelf!=null && platform!=null,st.name+" shelf/platform present");
                if(shelf==null || platform==null)continue;
                var sb=ArtBounds(shelf,st);var pb=ArtBounds(platform,table);
                need(Mathf.Abs(sb.size.x-ShelfWidth)<.001f && Mathf.Abs(sb.size.z-ShelfDepth)<.001f,st.name+" shelf footprint "+sb.size);
                need(Vector3.Distance(pb.size,TableSize)<.001f && Mathf.Abs(pb.min.y)<.001f,st.name+" platform matches solid walkable collider "+pb.size);
                var surface=shelf.Find("ToolShelf_SpongeRest");
                need(Mathf.Abs(st.InverseTransformPoint(surface.position).y-ShelfHeight)<.001f,st.name+" shelf surface aligns with tools/collider");
                foreach(var name in new[]{"Shelf","ShelfLeg_0","ShelfLeg_1","ShelfLeg_2","ShelfLeg_3"})
                    need(!st.Find(name).GetComponent<MeshRenderer>().enabled,st.name+" old "+name+" hidden");
                foreach(var tool in st.GetComponentsInChildren<KH_WashTool>(true))
                {
                    if(tool.tool==KH_DogWash.ToolBottle)
                    {
                        bool finished=KH_PropFinishEditor.Ready;
                        need(tool.transform.Find("ImportedVisual")==null && tool.transform.Find("Body")!=null && tool.transform.Find("Body").GetComponent<MeshRenderer>().enabled!=finished && (!finished || tool.transform.Find("FinishedVisual")!=null),tool.name+" bottle visual matches installed prop finish");
                        continue;
                    }
                    var art=tool.transform.Find("ImportedVisual");need(art!=null,tool.name+" imported visual");if(art==null)continue;
                    need(art.GetComponentsInChildren<Collider>(true).Length==0,tool.name+" no extra pickup colliders");
                    if(tool.tool==KH_DogWash.ToolShower)
                    {
                        var a=art.Find("Shower_NozzleAnchor");
                        need(Vector3.Distance(a.position,tool.nozzle.position)<.001f && Vector3.Dot(a.forward,tool.nozzle.forward)>.999f,tool.name+" nozzle position/direction match spray");
                        var bounds=ArtBounds(art,tool.transform);var cap=tool.GetComponent<CapsuleCollider>();
                        need(cap.direction==1 && !cap.isTrigger && Vector3.Distance(cap.center,bounds.center)<.001f && Mathf.Abs(cap.height-bounds.size.y)<.001f
                            && Mathf.Abs(cap.radius-Mathf.Max(bounds.size.x,bounds.size.z)*.5f)<.001f,tool.name+" capsule fitted to imported shower: center="+cap.center+" height="+cap.height.ToString("F5")+" radius="+cap.radius.ToString("F5"));
                        need(!tool.transform.Find("Handle").GetComponent<MeshRenderer>().enabled && !tool.transform.Find("Head").GetComponent<MeshRenderer>().enabled,tool.name+" primitive art hidden");
                    }
                    else
                    {
                        var box=tool.GetComponent<BoxCollider>();var tb=ArtBounds(art,tool.transform);
                        need(Vector3.Distance(tb.size,box.size)<.001f && Vector3.Distance(tb.center,box.center)<.001f,tool.name+" mesh fits pickup collider");
                        need(!tool.transform.Find("Visual").GetComponent<MeshRenderer>().enabled,tool.name+" primitive art hidden");
                    }
                    var bb=ArtBounds(art,st);need(bb.min.y>=ShelfHeight-.003f,tool.name+" above shelf (minY="+bb.min.y.ToString("F4")+")");
                }
                foreach(var mf in st.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(!AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith(ArtPath))continue;
                    models++;triangles+=(int)mf.sharedMesh.GetIndexCount(0)/3;
                    var renderer=mf.GetComponent<MeshRenderer>();bool replaced=mf.transform.parent.name=="ImportedShelf"&&KH_PropFinishEditor.Ready;
                    need(renderer.enabled!=replaced && renderer.sharedMaterials.Length==1 && renderer.sharedMaterial==mat,mf.transform.parent.name+" legacy material and expected visibility");
                }
            }
            need(models==23,"23 models (15 tools + 8 fixtures): "+models);
            foreach(string filename in new[]{"WashTools_BaseColor.png","WashTools_MetallicSmoothness.png"})
            {
                var ti=(TextureImporter)AssetImporter.GetAtPath(ArtPath+filename);
                need(!ti.isReadable,filename+" no CPU read/write copy");
                foreach(string platform in new[]{"Android","iPhone"})
                {var ps=ti.GetPlatformTextureSettings(platform);need(ps.overridden&&ps.maxTextureSize<=512&&ps.format==TextureImporterFormat.ASTC_6x6,filename+" "+platform+" bounded ASTC");}
            }
            lines.Add("TRIANGLES "+triangles+" MODELS "+models+" FAILURES "+fails);
            Directory.CreateDirectory(ArtReport);File.WriteAllLines(ArtReport+"verify.txt",lines);
            if(fails>0)throw new InvalidOperationException("Imported wash art: "+fails+" failures. "+ArtReport+"verify.txt");
        }

        [MenuItem(Menu + "33 - Capture Imported Tools")]
        public static void CaptureArt()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var st=GameObject.Find(StationName).transform;
            var go=new GameObject("KH_WashArtPreviewCamera");go.hideFlags=HideFlags.HideAndDontSave;
            var cam=go.AddComponent<Camera>();cam.fieldOfView=48;cam.nearClipPlane=.03f;cam.farClipPlane=1500;
            cam.clearFlags=CameraClearFlags.Skybox;cam.cullingMask=~0;
            var rt=new RenderTexture(1400,1000,24);var prior=RenderTexture.active;
            try
            {
                cam.targetTexture=rt;
                for(int i=0;i<2;i++)
                {
                    cam.transform.position=st.TransformPoint(i==0?new Vector3(1.9f,1.8f,-3.3f):new Vector3(.8f,1.45f,-1.4f));
                    cam.transform.LookAt(st.TransformPoint(i==0?new Vector3(0,.4f,-.55f):new Vector3(0,.85f,0)));
                    cam.Render();RenderTexture.active=rt;
                    var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();
                    Directory.CreateDirectory(ArtReport);File.WriteAllBytes(ArtReport+(i==0?"station.png":"tools.png"),tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
                }
            }
            finally{RenderTexture.active=prior;cam.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);}
        }

        [MenuItem(Menu + "30 - Survey Imported Tools")]
        public static void SurveyArt()
        {
            var lines = new List<string>();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            lines.Add("scene=" + scene.path + " dirty=" + scene.isDirty + " play=" + EditorApplication.isPlaying);
            foreach (var name in ArtNames)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPath + name + ".fbx");
                lines.Add("MODEL " + name + " " + (model != null));
                if (model == null) continue;
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    lines.Add(t.name + " local=" + t.localPosition.ToString("F5") + " rot=" + t.localEulerAngles.ToString("F2") + " scale=" + t.localScale.ToString("F5") + " world=" + t.position.ToString("F5") + " forward=" + t.forward.ToString("F5"));
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                    lines.Add("MESH " + mf.name + " bounds=" + mf.sharedMesh.bounds + " tris=" + mf.sharedMesh.triangles.Length/3);
            }
            foreach (var st in AllStationTransforms(scene))
            {
                lines.Add("STATION " + st.name + " pos=" + st.position.ToString("F4") + " rot=" + st.eulerAngles.ToString("F2"));
                foreach (var t in st.GetComponentsInChildren<KH_WashTool>(true))
                    lines.Add("TOOL " + t.name + " pos=" + t.transform.localPosition.ToString("F4") + " home=" + (t.home != null ? t.home.name : "NULL") + " nozzle=" + t.nozzle.localPosition.ToString("F4"));
            }
            Directory.CreateDirectory(ArtReport);
            File.WriteAllLines(ArtReport + "survey.txt", lines);
        }
    }
}
