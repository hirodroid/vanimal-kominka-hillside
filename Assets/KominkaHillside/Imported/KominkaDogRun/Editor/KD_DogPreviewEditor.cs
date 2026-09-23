using Object = UnityEngine.Object;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    // Bakes only display geometry into dog-root coordinates. No cloned behaviours enter the world.
    // Rebuild after changing dog scale, accessory fit or the selected pose clip.
    public static class KD_DogPreviewEditor
    {
        const string Dir = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/DogPreviews";
        const string GhostMat = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_SleepPreview.mat";
        const string ShaderName = "KominkaDogRun/Dog Preview";

        static GameObject Child(Transform parent, string name)
        {
            var found = parent.Find(name);
            if (found != null) return found.gameObject;
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.layer = 2;
            return go;
        }

        static KD_TreatPouch Pouch() { return KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>(); }

        [MenuItem("Tools/KominkaHillside/Menu/18 Build Dog Previews")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var pouch = Pouch();
            if (pouch.sleepPlacement == null || pouch.accessories == null || pouch.accRows.Length < KD_DogDefs.All.Length)
                throw new InvalidOperationException("Sleep placement and accessory menu must be built first");
            Directory.CreateDirectory("Temp/dog-previews");
            if (!File.Exists("Temp/dog-previews/before.unity")) EditorSceneManager.SaveScene(pouch.gameObject.scene, "Temp/dog-previews/before.unity", true);
            Directory.CreateDirectory(Dir); AssetDatabase.Refresh();
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("Preview shader missing");
            var ghost = AssetDatabase.LoadAssetAtPath<Material>(GhostMat);
            if (ghost == null) { ghost = new Material(shader); AssetDatabase.CreateAsset(ghost, GhostMat); }
            ghost.shader = shader; ghost.color = new Color(.58f,.86f,1f,.38f);
            ghost.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            ghost.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            ghost.SetFloat("_ZWrite", 0); ghost.SetOverrideTag("RenderType", "Transparent"); ghost.renderQueue = 3000;
            EditorUtility.SetDirty(ghost);
            int dogs = KD_DogDefs.All.Length;
            pouch.accPreviewDogs = new GameObject[dogs];
            pouch.accPreviewItems = new GameObject[pouch.accessories.productIds.Length * dogs];
            var log = new StringBuilder();
            for (int d = 0; d < dogs; d++)
            {
                var def = KD_DogDefs.All[d];
                var source = KD_SceneObjects.Find(def.DogName);
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var clone = Object.Instantiate(source);
                    SceneManager.MoveGameObjectToScene(clone, scene);
                    clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    clone.SetActive(true);
                    SamplePose(clone, def.sleepLoop);
                    var sleep = Child(pouch.sleepPlacement.markerDogs[d].transform, "PosePreview");
                    BakeBody(clone, sleep.transform, def.key + "_sleep", ghost);
                    sleep.SetActive(true);
                    // Start a fresh copy: animation curves do not reset properties absent in the next clip.
                    Object.DestroyImmediate(clone);
                    clone = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(clone, scene);
                    clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.SetActive(true);
                    SamplePose(clone, def.idle);
                    bool newMini = pouch.accRows[d].transform.Find("DogPreview") == null;
                    var mini = Child(pouch.accRows[d].transform, "DogPreview");
                    var art = Child(mini.transform, "Art");
                    BakeBody(clone, art.transform, def.key + "_idle", null);
                    for (int p = 0; p < pouch.accessories.productIds.Length; p++)
                    {
                        int pi = pouch.accessories.IndexOfProduct(pouch.accessories.productIds[p]);
                        if (pi < 0) throw new InvalidOperationException("Unknown preview product");
                        var node = pouch.accessories.nodes[pi * dogs + d];
                        if (node == null || !node.transform.IsChildOf(source.transform)) throw new InvalidOperationException("Accessory outside dog");
                        string path = AnimationUtility.CalculateTransformPath(node.transform, source.transform);
                        var sampledNode = clone.transform.Find(path);
                        if (sampledNode == null) throw new InvalidOperationException("Missing cloned accessory " + path);
                        var previewItem = Child(art.transform, "Item_" + pouch.accessories.productIds[p]);
                        // An accessory is the same geometry on every dog -- only the fitted placement
                        // differs -- so share the source mesh and put the placement on the Transform.
                        // Baking it per dog cost 78 meshes / 5.7MB of the download for nothing (2026-09-10).
                        PlaceRenderers(sampledNode.GetComponentsInChildren<Renderer>(true), previewItem.transform);
                        previewItem.SetActive(false); pouch.accPreviewItems[p * dogs + d] = previewItem;
                    }
                    // Dog meshes are baked in metres; menu coordinates are pixels. Only the outer miniature scales.
                    if (newMini) FitMiniature(mini);
                    pouch.accPreviewDogs[d] = mini; mini.SetActive(false);
                    log.AppendLine(def.key + " sleep=" + sleep.GetComponentsInChildren<Renderer>(true).Length + " miniature=" + mini.GetComponentsInChildren<Renderer>(true).Length);
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            BuildConfirmation(pouch, log);
            KD_CallPoseEditor.RebuildIfPresent();
            log.AppendLine("orphanBakesDeleted=" + DeleteOrphanBakes());
            if (pouch.language != null)
            {
                KD_LocalizationEditor.Bind(pouch.language, KD_LocalizationEditor.Load(), log);
                EditorUtility.SetDirty(pouch.language); UdonSharpEditorUtility.CopyProxyToUdon(pouch.language);
            }
            EditorUtility.SetDirty(pouch); UdonSharpEditorUtility.CopyProxyToUdon(pouch);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(pouch.gameObject.scene);
            File.WriteAllText("Temp/dog-previews/build.txt", log.ToString());
        }

        internal static void RewireMenu(KD_TreatPouch pouch, StringBuilder log)
        {
            if (pouch.accPreviewDogs == null || pouch.accPreviewDogs.Length == 0) return;
            BuildConfirmation(pouch, log);
        }

        /// <summary>Refresh fitted accessory preview geometry without rebuilding the dog bodies,
        /// sleep previews or any menu rows. Uses the existing sampled-idle preview frame.</summary>
        public static void RefreshAccessoryItems(KD_TreatPouch pouch, StringBuilder log)
        {
            int dogs = KD_DogDefs.All.Length;
            if (EditorApplication.isPlayingOrWillChangePlaymode || pouch == null || pouch.accessories == null
                || pouch.accPreviewDogs == null || pouch.accPreviewDogs.Length != dogs
                || pouch.gameObject.scene != SceneManager.GetActiveScene())
                throw new InvalidOperationException("Stopped active scene and existing dog miniatures required");
            for (int d = 0; d < dogs; d++)
                if (pouch.accPreviewDogs[d] == null || pouch.accPreviewDogs[d].transform.Find("Art") == null)
                    throw new InvalidOperationException("Missing existing miniature " + d);
            var items = new GameObject[pouch.accessories.productIds.Length * dogs];
            for (int d = 0; d < dogs; d++)
            {
                var def = KD_DogDefs.All[d];
                var source = KD_SceneObjects.Find(def.DogName);
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var clone = Object.Instantiate(source);
                    SceneManager.MoveGameObjectToScene(clone, scene);
                    clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    clone.SetActive(true); SamplePose(clone, def.idle);
                    var art = pouch.accPreviewDogs[d].transform.Find("Art");
                    for (int p = 0; p < pouch.accessories.productIds.Length; p++)
                    {
                        var node = pouch.accessories.nodes[p * dogs + d];
                        if (node == null || !node.transform.IsChildOf(source.transform))
                            throw new InvalidOperationException("Accessory outside matching dog");
                        var sampled = clone.transform.Find(AnimationUtility.CalculateTransformPath(node.transform, source.transform));
                        if (sampled == null) throw new InvalidOperationException("Missing sampled accessory " + node.name);
                        var preview = Child(art, "Item_" + pouch.accessories.productIds[p]);
                        PlaceRenderers(sampled.GetComponentsInChildren<Renderer>(true), preview.transform);
                        preview.SetActive(false); items[p * dogs + d] = preview;
                    }
                    pouch.accPreviewDogs[d].SetActive(false);
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            pouch.accPreviewItems = items;
            EditorUtility.SetDirty(pouch); UdonSharpEditorUtility.CopyProxyToUdon(pouch);
            EditorSceneManager.MarkSceneDirty(pouch.gameObject.scene);
            log.AppendLine("Accessory miniatures refreshed: " + items.Length + "; dog/sleep meshes and menu rows preserved");
        }

        [MenuItem("Tools/KominkaHillside/Menu/18a Fit Miniatures")]
        public static void FitMiniatures()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");
            foreach(var mini in Pouch().accPreviewDogs)FitMiniature(mini);
            EditorSceneManager.MarkSceneDirty(Pouch().gameObject.scene);
        }

        static void FitMiniature(GameObject mini)
        {
            var art=mini.transform.Find("Art");
            art.localRotation=Quaternion.Euler(0,145,0);
            var bounds=LocalBounds(art);
            float scale=Mathf.Min(132f/bounds.size.x,96f/bounds.size.y);
            mini.transform.localPosition=new Vector3(-174,0,-90);
            mini.transform.localScale=Vector3.one*scale;
            art.localPosition=-bounds.center;
        }

        /// <summary>P13 (2026-09-16): かざりの確認も板の手前の共有カード (KD_TreatSetupEditor.EnsureConfirmPanel)。
        /// 旧 AccConfirm (タブ内の帯) は消し、参照は共有カードへ向ける。</summary>
        static void BuildConfirmation(KD_TreatPouch p, StringBuilder log)
        {
            var page = p.tabPages[KD_TreatPouch.TabAcc].transform;
            var legacy = page.Find("AccConfirm");
            if (legacy != null) { Object.DestroyImmediate(legacy.gameObject); log.AppendLine("removed AccConfirm (P13: 確認は共有カードへ)"); }
            if (p.confirmRoot == null) throw new InvalidOperationException("Shared confirm card missing — run Treat System/2 - Setup (P13) first");
            p.accConfirmRoot = p.confirmRoot; p.accConfirmLabel = p.confirmBodyLabel; p.accConfirmYes = p.confirmYes;
        }

        internal static void SamplePose(GameObject clone, KD_ClipRef reference)
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(reference.guid));
            if(clip==null)throw new InvalidOperationException("Pose clip missing " + reference.guid);
            if(Mathf.Abs(clip.length-reference.length)>.05f)throw new InvalidOperationException("Pose clip length mismatch " + reference.label);
            clip.SampleAnimation(clone,0);
            ApplyConstraints(clone);
        }

        internal static void ApplyConstraints(GameObject clone)
        {
            // Same full-weight single-source constraints used by the actual two-tier dog rigs.
            // A source weight of 0 contributes nothing (Corgi/Dachs hip Bone.002 follows a sleep-only
            // driver bone whose weight the clips animate); pulling it anyway tore the idle bake apart.
            foreach(var t in clone.GetComponentsInChildren<Transform>(true))
            {
                var pc=t.GetComponent<PositionConstraint>();
                if(pc!=null && pc.constraintActive && pc.weight>.99f && pc.sourceCount>0 && pc.GetSource(0).weight>.99f)
                {
                    if(pc.sourceCount!=1 || pc.translationAxis!=(Axis.X|Axis.Y|Axis.Z))throw new InvalidOperationException("Unsupported position constraint");
                    // PositionConstraint's offset is in the constrained parent's space.
                    // These imported rigs have rotated/scaled parents; adding it in world space sinks the body.
                    t.position=pc.GetSource(0).sourceTransform.position
                        +(t.parent==null ? pc.translationOffset : t.parent.TransformVector(pc.translationOffset));
                }
                var rc=t.GetComponent<RotationConstraint>();
                if(rc!=null && rc.constraintActive && rc.weight>.99f && rc.sourceCount>0 && rc.GetSource(0).weight>.99f)
                {
                    if(rc.sourceCount!=1 || rc.rotationAxis!=(Axis.X|Axis.Y|Axis.Z))throw new InvalidOperationException("Unsupported rotation constraint");
                    t.rotation=rc.GetSource(0).sourceTransform.rotation*Quaternion.Euler(rc.rotationOffset);
                }
            }
        }

        internal static void BakeBody(GameObject clone, Transform destination, string key, Material material)
        {
            var renderers=new List<Renderer>();
            foreach(var r in clone.GetComponentsInChildren<Renderer>(true))
            {
                bool accessory=false;
                for(var t=r.transform;t!=clone.transform;t=t.parent)
                    if(t.name.StartsWith("KD_Acc_") || t.name.StartsWith("KD_CandyCostume_"))accessory=true;
                if(!accessory && r.enabled && r.gameObject.activeInHierarchy && (r is SkinnedMeshRenderer || r is MeshRenderer))renderers.Add(r);
            }
            if(renderers.Count==0)throw new InvalidOperationException("No visible dog geometry: "+key);
            BakeRenderers(renderers.ToArray(), destination, key, material);
        }

        static void BakeRenderers(Renderer[] renderers, Transform destination, string key, Material forced)
        {
            int i=0;
            foreach(var r in renderers)
            {
                Mesh input;bool temporary=false;
                // useScale=true compensates the renderer's 50x/100x scale before localToWorld.
                // Independent bone-weight audit: false caused 18-120m errors; true < 0.000002m.
                if(r is SkinnedMeshRenderer skin) { input=new Mesh();skin.BakeMesh(input,true);temporary=true; }
                else { var filter=r.GetComponent<MeshFilter>();if(filter==null || filter.sharedMesh==null)continue;input=filter.sharedMesh; }
                var baked=Object.Instantiate(input);
                // Source copy sits at world origin with the live dog's scale preserved.
                var matrix=r.transform.localToWorldMatrix;
                var vertices=baked.vertices;var normals=baked.normals;
                for(int v=0;v<vertices.Length;v++)vertices[v]=matrix.MultiplyPoint3x4(vertices[v]);
                var normalMatrix=matrix.inverse.transpose;
                for(int v=0;v<normals.Length;v++)normals[v]=normalMatrix.MultiplyVector(normals[v]).normalized;
                baked.vertices=vertices;baked.normals=normals;baked.RecalculateBounds();
                string path=Dir+"/"+key+"_"+i+".asset";
                baked.name=key+"_"+i;
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null){mesh=baked;AssetDatabase.CreateAsset(mesh,path);}
                else
                {
                    // CopySerialized leaves stale native vertex buffers on an already rendered Mesh.
                    mesh.Clear();mesh.name=baked.name;mesh.indexFormat=baked.indexFormat;
                    mesh.vertices=vertices;mesh.normals=normals;mesh.uv=baked.uv;
                    mesh.subMeshCount=baked.subMeshCount;
                    for(int sub=0;sub<baked.subMeshCount;sub++)mesh.SetIndices(baked.GetIndices(sub),baked.GetTopology(sub),sub);
                    mesh.RecalculateBounds();mesh.UploadMeshData(false);
                    Object.DestroyImmediate(baked);EditorUtility.SetDirty(mesh);
                }
                var go=Child(destination,"Mesh_"+i);
                // Baked vertices already carry the placement, unlike PlaceRenderers' shared meshes.
                go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
                ApplyDisplay(go,mesh,r.sharedMaterials,forced);
                if(temporary)Object.DestroyImmediate(input);i++;
            }
            HideStale(destination,i);
        }

        // Display copies that do NOT bake: the mesh asset is shared and the placement lives on the
        // Transform. Only valid for rigid MeshRenderers -- a SkinnedMeshRenderer's vertices depend on
        // the dog's bones, so it has to be baked per dog and this throws instead of shipping a wrong
        // preview.
        internal static void PlaceRenderers(Renderer[] renderers, Transform destination)
        {
            int i=0;
            foreach(var r in renderers)
            {
                if(r is SkinnedMeshRenderer)throw new InvalidOperationException("Skinned renderer cannot share a mesh: "+r.name);
                if(!(r is MeshRenderer))continue;
                var filter=r.GetComponent<MeshFilter>();
                if(filter==null || filter.sharedMesh==null)continue;
                var go=Child(destination,"Mesh_"+i);
                // The clone sits at the origin unrotated, so the renderer's world matrix *is* its
                // placement in dog-root coordinates -- the space the baked vertices used to carry.
                go.transform.localPosition=r.transform.position;
                go.transform.localRotation=r.transform.rotation;
                go.transform.localScale=r.transform.lossyScale;
                // TRS cannot express a sheared matrix, so prove the placement round-trips.
                var placed=Matrix4x4.TRS(go.transform.localPosition,go.transform.localRotation,go.transform.localScale);
                var wanted=r.transform.localToWorldMatrix;float error=0f;
                for(int col=0;col<4;col++)for(int row=0;row<4;row++)error=Mathf.Max(error,Mathf.Abs(placed[row,col]-wanted[row,col]));
                if(error>1e-3f)throw new InvalidOperationException("Placement is not a rigid transform ("+error+"): "+r.name);
                ApplyDisplay(go,filter.sharedMesh,r.sharedMaterials,null);
                i++;
            }
            HideStale(destination,i);
        }

        // The same pose shown twice (real size ghost + miniature) differs only by material, so the
        // second display reuses the first one's meshes instead of baking them again (2026-09-10).
        internal static void MirrorBakedMeshes(Transform source, Transform destination, Material forced)
        {
            if(forced==null)throw new InvalidOperationException("Mirroring needs a forced material");
            int i=0;
            for(int c=0;c<source.childCount;c++)
            {
                var child=source.GetChild(c);
                if(!child.name.StartsWith("Mesh_") || !child.gameObject.activeSelf)continue;
                var renderer=child.GetComponent<MeshRenderer>();
                if(renderer==null || !renderer.enabled)continue;
                var filter=child.GetComponent<MeshFilter>();
                if(filter==null || filter.sharedMesh==null)continue;
                var go=Child(destination,"Mesh_"+i);
                go.transform.localPosition=child.localPosition;
                go.transform.localRotation=child.localRotation;
                go.transform.localScale=child.localScale;
                ApplyDisplay(go,filter.sharedMesh,null,forced);
                i++;
            }
            HideStale(destination,i);
        }

        // Meshes the old per-dog accessory bake and the duplicated miniature bake left behind. Nothing
        // references them once Build has run, and the folder is regenerable, so removing them keeps a
        // rebuilt clone from carrying 168 dead assets. `2 - Verify` catches it if one was still in use.
        static int DeleteOrphanBakes()
        {
            int removed=0;
            foreach(var file in Directory.GetFiles(Dir,"*.asset"))
            {
                string name=Path.GetFileNameWithoutExtension(file);
                if(!name.Contains("_acc_") && !name.Contains("_callmini"))continue;
                if(AssetDatabase.DeleteAsset(file.Replace('\\','/')))removed++;
            }
            return removed;
        }

        static void ApplyDisplay(GameObject go, Mesh mesh, Material[] sources, Material forced)
        {
            var mf=go.GetComponent<MeshFilter>();if(mf==null)mf=go.AddComponent<MeshFilter>();mf.sharedMesh=mesh;
            var mr=go.GetComponent<MeshRenderer>();if(mr==null)mr=go.AddComponent<MeshRenderer>();
            var mats=new Material[mesh.subMeshCount];
            for(int m=0;m<mats.Length;m++)mats[m]=forced!=null?forced:PreviewMaterial(sources[Mathf.Min(m,sources.Length-1)]);
            mr.sharedMaterials=mats;mr.shadowCastingMode=ShadowCastingMode.Off;mr.receiveShadows=false;
            mr.lightProbeUsage=LightProbeUsage.Off;mr.reflectionProbeUsage=ReflectionProbeUsage.Off;
        }

        static void HideStale(Transform destination, int used)
        {
            for(int c=0;c<destination.childCount;c++)
                if(destination.GetChild(c).name.StartsWith("Mesh_"))
                    destination.GetChild(c).gameObject.SetActive(int.Parse(destination.GetChild(c).name.Substring(5))<used);
        }

        internal static Material PreviewMaterial(Material source)
        {
            string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            if(string.IsNullOrEmpty(guid))throw new InvalidOperationException("Unpersisted source material");
            string path=Dir+"/Mat_"+guid+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(Shader.Find(ShaderName));AssetDatabase.CreateAsset(mat,path);}
            mat.mainTexture=source.HasProperty("_MainTex")?source.GetTexture("_MainTex"):null;
            mat.color=source.HasProperty("_Color")?source.GetColor("_Color"):Color.white;
            if(source.HasProperty("_MainTex")){mat.mainTextureScale=source.GetTextureScale("_MainTex");mat.mainTextureOffset=source.GetTextureOffset("_MainTex");}
            EditorUtility.SetDirty(mat);return mat;
        }

        static Bounds LocalBounds(Transform art)
        {
            bool first=true;Bounds bounds=new Bounds();
            foreach(var mf in art.GetComponentsInChildren<MeshFilter>(true))
            {
                if(mf.sharedMesh==null)continue;
                // Shared accessory meshes are placed by their Transform, so read every vertex through
                // the matrix down to art. Baked meshes sit at identity and are unaffected.
                Matrix4x4 toArt=art.worldToLocalMatrix*mf.transform.localToWorldMatrix;
                // Art rotation is included, but placement and outer pixel conversion are not.
                foreach(var vertex in mf.sharedMesh.vertices)
                {
                    Vector3 point=art.localRotation*toArt.MultiplyPoint3x4(vertex);
                    if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
                }
            }
            return bounds;
        }

        [MenuItem("Tools/KominkaHillside/Menu/25 Verify Dog Previews")]
        public static void Verify()
        {
            var p=Pouch();var log=new StringBuilder();int fail=0,pass=0;
            void Check(bool ok,string name){if(ok)pass++;else fail++;log.AppendLine((ok?"PASS ":"FAIL ")+name);}
            int dogs=KD_DogDefs.All.Length;
            Check(p.accPreviewDogs!=null && p.accPreviewDogs.Length==dogs,"six miniatures");
            Check(p.accPreviewItems!=null && p.accPreviewItems.Length==dogs*p.accessories.productIds.Length,"product x dog preview nodes");
            for(int d=0;d<dogs;d++)
            {
                var root=p.sleepPlacement.markerDogs[d].transform.Find("PosePreview");
                Check(root!=null,"sleep pose "+d);
                if(root!=null)
                {
                    CheckDisplay(root, true,Check);
                    Check(root.localPosition==Vector3.zero && root.localRotation==Quaternion.identity && root.localScale==Vector3.one,"sleep uses marker coordinates "+d);
                    float extent=0;
                    foreach(var mf in root.GetComponentsInChildren<MeshFilter>(true))if(mf.sharedMesh!=null)extent=Mathf.Max(extent,mf.sharedMesh.bounds.size.magnitude);
                    Check(extent>.1f && extent<3f,"sleep geometry metre range "+d+" size="+extent);
                }
                var mini=p.accPreviewDogs[d];
                Check(mini!=null && mini.transform.parent==p.accRows[d].transform && !mini.activeSelf,"row miniature hidden "+d);
                if(mini!=null)
                {
                    CheckDisplay(mini.transform,false,Check);
                    var bounds=LocalBounds(mini.transform.Find("Art"));
                    var scale=mini.transform.localScale;
                    Check(bounds.size.x*scale.x<=132.1f && bounds.size.y*scale.y<=96.1f,"miniature fits row "+d);
                    Check(mini.transform.localPosition.z+Mathf.Abs(bounds.size.z*scale.z/2)<0,"miniature in front of canvas "+d);
                }
            }
            for(int i=0;i<p.accPreviewItems.Length;i++)Check(p.accPreviewItems[i]!=null && !p.accPreviewItems[i].activeSelf && p.accPreviewItems[i].transform.IsChildOf(p.accPreviewDogs[i%dogs].transform) && p.accPreviewItems[i]!=p.accessories.nodes[i],"isolated accessory "+i);
            Check(p.accConfirmRoot!=null && !p.accConfirmRoot.activeSelf,"confirmation hidden");
            foreach(var button in p.accConfirmRoot.GetComponentsInChildren<Button>(true))
            {
                Check(button.onClick.GetPersistentEventCount()==1 && button.onClick.GetPersistentTarget(0)==UdonSharpEditorUtility.GetBackingUdonBehaviour(p),"confirmation button target "+button.name);
                var label=button.GetComponentInChildren<TextMeshProUGUI>();
                Check(label!=null && Array.IndexOf(p.language.fixedLabels,label)>=0,"localized confirmation "+button.name);
            }
            var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            GameObject[] wired;
            Check(backing.publicVariables.TryGetVariableValue("accPreviewDogs",out wired) && wired.Length==dogs,"backing miniatures");
            Check(backing.publicVariables.TryGetVariableValue("accPreviewItems",out wired) && wired.Length==p.accPreviewItems.Length,"backing accessories");
            var descriptor=Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();int broken=0;
            foreach(var entry in descriptor.NetworkIDCollection)if(entry.gameObject==null)broken++;
            log.AppendLine("networkIdsBroken="+broken);
            log.AppendLine("RESULT pass="+pass+" fail="+fail);
            File.WriteAllText("Temp/dog-previews/verify.txt",log.ToString());Debug.Log(log.ToString());
        }

        static void CheckDisplay(Transform root,bool ghost,Action<bool,string> check)
        {
            int renderers=0;
            foreach(var c in root.GetComponentsInChildren<Component>(true))
                check(c!=null && (c is Transform || c is MeshFilter || c is MeshRenderer),"display-only "+root.name+"/"+(c==null?"missing":c.GetType().Name));
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderers++;
                check(r.GetComponent<MeshFilter>().sharedMesh!=null,"mesh "+r.name);
                foreach(var mat in r.sharedMaterials)check(mat!=null && mat.shader.name==ShaderName && (!ghost || (mat.color.a>0 && mat.color.a<1 && mat.GetFloat("_ZWrite")==0)),"preview material "+r.name);
                check(r.gameObject.layer==2 && r.shadowCastingMode==ShadowCastingMode.Off,"no interaction/shadow "+r.name);
            }
            check(renderers>0,"renderers "+root.name);
        }
    
        // ---- KD_DogPreviewChecksEditor から併合 (IMP-INF-019 C3、2026-09-13) ----
        [MenuItem("Tools/KominkaHillside/Menu/25c Check Menu States")]
        public static void CheckMenuStates()
        {
            var scene=EditorSceneManager.NewPreviewScene();var log=new StringBuilder();int pass=0,fail=0;
            void Check(bool ok,string name){if(ok)pass++;else fail++;log.AppendLine((ok?"PASS ":"FAIL ")+name);}
            GameObject Go(string name,Transform parent=null){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);if(parent!=null)go.transform.SetParent(parent,false);return go;}
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try
            {
                var p=Go("MenuFixture").AddComponent<KD_TreatPouch>();
                p.panelRoot=Go("Panel");p.tabPages=new GameObject[KD_TreatPouch.TabCount];
                for(int i=0;i<KD_TreatPouch.TabCount;i++)p.tabPages[i]=Go("Page"+i,p.panelRoot.transform);
                p.accessories=Go("AccessoryFixture").AddComponent<KD_AccessoryBox>();
                p.accessories.productIds=new[]{7,11};p.accessories.productSlots=new[]{0,2};p.accessories.dogCount=6;
                var item=Go("UnownedProduct").AddComponent<KD_ShopItem>();item.price=10;
                p.accProductIds=new[]{7,11};p.accItems=new[]{item,item};
                p.accRows=new GameObject[6];p.accPreviewDogs=new GameObject[6];p.accPreviewItems=new GameObject[12];
                for(int d=0;d<6;d++)
                {
                    p.accRows[d]=Go("Row"+d,p.tabPages[5].transform);p.accPreviewDogs[d]=Go("Dog"+d,p.accRows[d].transform);
                    for(int product=0;product<2;product++)p.accPreviewItems[product*6+d]=Go("Item"+product,p.accPreviewDogs[d].transform);
                }
                p.accConfirmRoot=Go("Confirm",p.tabPages[5].transform);p.accConfirmYes=Go("Yes",p.accConfirmRoot.transform);
                typeof(KD_TreatPouch).GetMethod("ApplyTab",flags).Invoke(p,null);
                p._Acc0();
                Check((int)typeof(KD_TreatPouch).GetField("_accProduct",flags).GetValue(p)==0,"unowned product can be previewed");
                for(int i=0;i<12;i++)Check(p.accPreviewItems[i].activeInHierarchy==(i<6),"selected item only "+i);
                p._Acc2();Check(p.accConfirmRoot.activeInHierarchy,"dog button opens purchase confirmation");
                Check(p.accessories.EquippedOf(2,0)==-1,"unowned preview leaves actual dog unequipped");
                Check(!p.accConfirmYes.activeSelf,"loading purchase unavailable");
                p._AccConfirmBuy();Check((int)typeof(KD_ShopItem).GetField("_dbgBought",flags).GetValue(item)==0,"cannot buy while loading");
                p._AccConfirmNo();Check(!p.accConfirmRoot.activeSelf,"cancel closes confirmation");
                p._Acc2();p._Tab0();Check(!p.accPreviewDogs[0].activeInHierarchy,"other tab hides miniature");
                p._Tab5();Check(!p.accConfirmRoot.activeSelf,"tab round trip discards confirmation");
                p._Acc2();p._OpenSettings();p._Tab5();Check(!p.accConfirmRoot.activeSelf,"settings round trip discards confirmation");
                p._Acc2();typeof(KD_TreatPouch).GetMethod("SetOpen",flags).Invoke(p,new object[]{false});
                Check(!p.accPreviewDogs[0].activeInHierarchy,"closed menu hides miniature");
                typeof(KD_TreatPouch).GetMethod("SetOpen",flags).Invoke(p,new object[]{true});
                Check(!p.accConfirmRoot.activeSelf,"reopening discards confirmation");
                p._AccBack();for(int d=0;d<6;d++)Check(!p.accPreviewDogs[d].activeSelf,"product list hides miniature "+d);
                p._Acc1();for(int i=0;i<12;i++)Check(p.accPreviewItems[i].activeInHierarchy==(i>=6),"next selection replaces accessory "+i);
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);log.AppendLine("RESULT pass="+pass+" fail="+fail);File.WriteAllText("Temp/dog-previews/states.txt",log.ToString());}
        }
        [MenuItem("Tools/KominkaHillside/Menu/25b Measure Skin")]
        public static void MeasureSkin()
        {
            var log=new StringBuilder();var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                foreach(var def in KD_DogDefs.All)
                {
                    var clone=Object.Instantiate(KD_SceneObjects.Find(def.DogName));SceneManager.MoveGameObjectToScene(clone,scene);
                    clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                    KD_DogPreviewEditor.SamplePose(clone,def.sleepLoop);
                    var rig=KD_DogRig.Resolve(clone);log.AppendLine(def.key+" head="+rig.head.position+" sourceScale="+clone.transform.localScale);
                    foreach(var skin in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        // Control condition isolates bone skinning from intentional facial morphs.
                        for(int shape=0;shape<skin.sharedMesh.blendShapeCount;shape++)skin.SetBlendShapeWeight(shape,0);
                        var a=new Mesh();var b=new Mesh();skin.BakeMesh(a,false);skin.BakeMesh(b,true);
                        var mesh=skin.sharedMesh;var weights=mesh.boneWeights;var verts=mesh.vertices;var bones=skin.bones;var bp=mesh.bindposes;
                        var expected=new Bounds();bool first=true;float errA=0,errB=0,errNoScale=0;
                        var av=a.vertices;var bv=b.vertices;var noScale=Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one);
                        for(int i=0;i<verts.Length;i++)
                        {
                            var w=weights[i];Vector3 v=Vector3.zero;
                            int[] bi={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] ws={w.weight0,w.weight1,w.weight2,w.weight3};
                            for(int j=0;j<4;j++)if(ws[j]>0)v+=(bones[bi[j]].localToWorldMatrix*bp[bi[j]]).MultiplyPoint3x4(verts[i])*ws[j];
                            if(first){expected=new Bounds(v,Vector3.zero);first=false;}else expected.Encapsulate(v);
                            errA=Mathf.Max(errA,Vector3.Distance(v,skin.transform.TransformPoint(av[i])));
                            errB=Mathf.Max(errB,Vector3.Distance(v,skin.transform.TransformPoint(bv[i])));
                            errNoScale=Mathf.Max(errNoScale,Vector3.Distance(v,noScale.MultiplyPoint3x4(av[i])));
                        }
                        log.AppendLine(skin.name+" scale="+skin.transform.lossyScale+" expected="+expected+" false="+a.bounds+" true="+b.bounds+" errFalse="+errA+" errTrue="+errB+" errNoScale="+errNoScale);
                        Object.DestroyImmediate(a);Object.DestroyImmediate(b);
                    }
                    Object.DestroyImmediate(clone);
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);File.WriteAllText("Temp/dog-previews/skin-measure.txt",log.ToString());}
        }
        [MenuItem("Tools/KominkaHillside/Menu/25a Render Dog Previews")]
        public static void Render()
        {
            var p=KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            var scene=EditorSceneManager.NewPreviewScene();var previous=RenderTexture.active;
            var rt=new RenderTexture(1368,840,24);Texture2D image=null;
            try
            {
                var clone=Object.Instantiate(p.panelRoot);SceneManager.MoveGameObjectToScene(clone,scene);
                foreach(var b in clone.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true))Object.DestroyImmediate(b);
                foreach(var b in clone.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true))Object.DestroyImmediate(b);
                clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.transform.localScale=Vector3.one*.001f;
                clone.SetActive(true);clone.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                for(int i=0;i<p.tabPages.Length;i++)clone.transform.Find(p.tabPages[i].name).gameObject.SetActive(i==KD_TreatPouch.TabAcc);
                if(p.settingsPage!=null)clone.transform.Find(p.settingsPage.name).gameObject.SetActive(false);
                var page=clone.transform.Find(p.tabPages[KD_TreatPouch.TabAcc].name);
                var camGo=new GameObject("PreviewCamera");SceneManager.MoveGameObjectToScene(camGo,scene);
                var cam=camGo.AddComponent<Camera>();cam.scene=scene;cam.transform.position=new Vector3(0,0,-2);
                cam.orthographic=true;cam.orthographicSize=.35f;cam.aspect=1368f/840;cam.targetTexture=rt;
                cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.14f,.16f,.18f);
                image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                for(int product=0;product<p.accProductIds.Length;product++)
                {
                    page.Find("Header").GetComponent<TextMeshProUGUI>().text=p.accNames[product];
                    for(int dog=0;dog<KD_DogDefs.All.Length;dog++)
                    {
                        var row=page.Find("Scroll/Viewport/Content/AccRow"+dog);row.gameObject.SetActive(true);
                        var label=row.Find("Label").GetComponent<TextMeshProUGUI>();label.text=KD_DogDefs.All[dog].key;
                        label.rectTransform.anchoredPosition=new Vector2(80,32);label.rectTransform.sizeDelta=new Vector2(320,36);
                        row.Find("Sub").GetComponent<TextMeshProUGUI>().text="";
                        var mini=row.Find("DogPreview");mini.gameObject.SetActive(true);
                        foreach(int id in p.accessories.productIds) mini.Find("Art/Item_"+id).gameObject.SetActive(p.accessories.SelectionContains(p.accProductIds[product], id));
                    }
                    for(int row=KD_DogDefs.All.Length;row<p.accRows.Length;row++) page.Find("Scroll/Viewport/Content/AccRow"+row).gameObject.SetActive(false);
                    Canvas.ForceUpdateCanvases();foreach(var text in clone.GetComponentsInChildren<TextMeshProUGUI>())text.ForceMeshUpdate();
                    cam.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
                    File.WriteAllBytes("Temp/dog-previews/accessory-"+product+".png",image.EncodeToPNG());
                }
                clone.SetActive(false);
                for(int d=0;d<KD_DogDefs.All.Length;d++)
                {
                    var ghost=Object.Instantiate(p.sleepPlacement.markerDogs[d]);SceneManager.MoveGameObjectToScene(ghost,scene);
                    ghost.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);ghost.SetActive(true);
                    Bounds bounds=new Bounds();bool first=true;
                    foreach(var r in ghost.GetComponentsInChildren<Renderer>()){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
                    cam.orthographicSize=Mathf.Max(bounds.size.y,bounds.size.z)*.75f;
                    cam.transform.position=bounds.center+new Vector3(1,.7f,1)*Mathf.Max(2,bounds.size.magnitude*2);
                    cam.transform.LookAt(bounds.center);cam.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
                    File.WriteAllBytes("Temp/dog-previews/sleep-"+d+".png",image.EncodeToPNG());Object.DestroyImmediate(ghost);
                }
            }
            finally{RenderTexture.active=previous;EditorSceneManager.ClosePreviewScene(scene);Object.DestroyImmediate(rt);if(image!=null)Object.DestroyImmediate(image);}
        }
}
}
