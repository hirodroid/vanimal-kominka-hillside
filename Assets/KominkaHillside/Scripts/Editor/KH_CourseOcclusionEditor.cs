using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // Native camera-based culling: no runtime Udon, player-distance switches or disabled colliders.
    // Configuration, asynchronous bake, and acceptance/save are separate operations.
    [InitializeOnLoad]
    public static class KH_CourseOcclusionEditor
    {
        const string Root="KH_CourseOcclusion";
        const string Key="KH.CourseOcclusion.Bake";
        const string Evidence="Temp/des-env-002/occlusion-bake.json";
        public const string DataPath="Assets/Scenes/KH_RiverLoopCourse/OcclusionCullingData.asset";
        const float Timeout=600;
        [Serializable] public class BakeReport
        {public string status,utc,reason,scene,backupDirectory,previousAssetStamp;public bool running,assetUpdated;public long dataBytes;public double elapsedSeconds;}
        [Serializable] public class Verification
        {public string status,utc,scene,dataPath;public long dataBytes;public int errors,terrains,areas,meshOccluders;public bool referenceCameraEnabled;public string[] findings;}
        static KH_CourseOcclusionEditor(){EditorApplication.update+=Watch;}
        static void Mark(UnityEngine.Object obj)
        {EditorUtility.SetDirty(obj);if(PrefabUtility.IsPartOfPrefabInstance(obj))PrefabUtility.RecordPrefabInstancePropertyModifications(obj);}
        static void Flags(GameObject go,StaticEditorFlags flags){GameObjectUtility.SetStaticEditorFlags(go,flags);Mark(go);}
        static IEnumerable<GameObject> Objects(Scene scene)
        {return scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject);}
        [MenuItem("Tools/KominkaHillside/Course/34 - Configure Course Occlusion")]
        public static void Configure()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var scene=SceneManager.GetActiveScene();
            if(GameObject.Find(Root)!=null||StaticOcclusionCulling.umbraDataSize>0||File.Exists(DataPath)||UnityEngine.Object.FindObjectsOfType<OcclusionArea>(true).Length!=0)throw new InvalidOperationException("Existing occlusion preserved; first configuration only");
            var terrains=UnityEngine.Object.FindObjectsOfType<Terrain>();
            if(terrains.Length!=20||terrains.Any(t=>!AssetDatabase.GetAssetPath(t.terrainData).StartsWith(KH_CourseGroundEditor.Dir+"/")))throw new InvalidOperationException("Twenty owned course terrains required");
            var scenery=GameObject.Find("KH_CourseScenery");var extension=GameObject.Find("KH_CourseExtension");
            var night=UnityEngine.Object.FindObjectOfType<KH_NightMode>();
            if(scenery==null||extension==null||night==null||night.overlayRenderer==null||night.skyHolder==null)throw new InvalidOperationException("Course scenery and night required");
            var objects=Objects(scene).ToArray();var oldFlags=objects.Select(GameObjectUtility.GetStaticEditorFlags).ToArray();
            var nightRenderers=new[]{night.overlayRenderer,night.skyHolder};var oldDynamic=nightRenderers.Select(r=>r.allowOcclusionWhenDynamic).ToArray();
            Directory.CreateDirectory("Temp/des-env-002");File.Copy(scene.path,"Temp/des-env-002/before-course-occlusion.unity",true);GameObject root=null;
            try
            {
                // Old building prefab flags include moving doors. For this bake, only terrain hides other objects.
                for(int i=0;i<objects.Length;i++)if((oldFlags[i]&StaticEditorFlags.OccluderStatic)!=0)
                    Flags(objects[i],oldFlags[i]&~StaticEditorFlags.OccluderStatic);
                root=new GameObject(Root);
                foreach(var t in terrains)
                {
                    Flags(t.gameObject,GameObjectUtility.GetStaticEditorFlags(t.gameObject)|StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic);
                    var go=new GameObject("ViewVolume_"+t.name);go.transform.SetParent(root.transform,false);
                    var b=t.terrainData.bounds;go.transform.position=t.transform.position+b.center+Vector3.up*7;
                    var area=go.AddComponent<OcclusionArea>();area.center=Vector3.zero;area.size=new Vector3(t.terrainData.size.x+4,b.size.y+18,t.terrainData.size.z+4);
                    var serialized=new SerializedObject(area);var isView=serialized.FindProperty("m_IsViewVolume");
                    if(isView==null||isView.propertyType!=SerializedPropertyType.Boolean)throw new InvalidOperationException("OcclusionArea view-volume field missing");
                    isView.boolValue=true;serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach(var parent in new[]{scenery,extension})foreach(var r in parent.GetComponentsInChildren<Renderer>(true))
                    Flags(r.gameObject,(GameObjectUtility.GetStaticEditorFlags(r.gameObject)|StaticEditorFlags.OccludeeStatic)&~StaticEditorFlags.OccluderStatic);
                foreach(var r in nightRenderers)
                {r.allowOcclusionWhenDynamic=false;Mark(r);Flags(r.gameObject,GameObjectUtility.GetStaticEditorFlags(r.gameObject)&~(StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic));}
                EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new IOException("Occlusion configuration save failed");
            }
            catch
            {
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                for(int i=0;i<objects.Length;i++)if(objects[i]!=null)Flags(objects[i],oldFlags[i]);
                for(int i=0;i<nightRenderers.Length;i++){nightRenderers[i].allowOcclusionWhenDynamic=oldDynamic[i];Mark(nightRenderers[i]);}
                EditorSceneManager.MarkSceneDirty(scene);
                try{if(!EditorSceneManager.SaveScene(scene))Debug.LogError("[KH Course Occlusion] Configuration restored in memory; rollback not saved");}
                catch(Exception e){Debug.LogError("[KH Course Occlusion] Rollback save failed: "+e.Message);}
                throw;
            }
            Debug.Log("[KH Course Occlusion] Configured twenty terrain occluders and view volumes; no bake yet");
        }
        [MenuItem("Tools/KominkaHillside/Course/35 - Start Course Occlusion Bake")]
        public static void StartBake()
        {StartBake(false);}
        [MenuItem("Tools/KominkaHillside/Course/52 - Rebake Owned Course Occlusion")]
        public static void Rebake()
        {StartBake(true);}
        static void StartBake(bool replace)
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if(SessionState.GetBool(Key,false)||StaticOcclusionCulling.isRunning||GameObject.Find(Root)==null)throw new InvalidOperationException("Prepared idle course required");
            if(!replace&&(StaticOcclusionCulling.umbraDataSize>0||File.Exists(DataPath)))throw new InvalidOperationException("Existing data preserved; use explicit owned rebake");
            SessionState.SetString(Key+".previousAssetStamp","");SessionState.SetString(Key+".backup","");
            if(replace)
            {
                Verify(); // Includes the saved scene's owned data reference and terrain/volume flags.
                if(!File.Exists(DataPath)||!File.Exists(DataPath+".meta"))throw new InvalidOperationException("Owned bake asset missing");
                string backup="Temp/des-env-002/occlusion-backups/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");Directory.CreateDirectory(backup);
                File.Copy(KH_CourseGroundEditor.ScenePath,backup+"/before.unity");File.Copy(DataPath,backup+"/before.asset");File.Copy(DataPath+".meta",backup+"/before.asset.meta");
                SessionState.SetString(Key+".backup",backup);
                SessionState.SetString(Key+".previousAssetStamp",File.GetLastWriteTimeUtc(DataPath).Ticks.ToString());
            }
            SessionState.SetFloat(Key+".start",(float)EditorApplication.timeSinceStartup);
            SessionState.SetFloat(Key+".finishedSince",-1);
            SessionState.SetInt(Key+".scene",SceneManager.GetActiveScene().handle);
            SessionState.SetBool(Key,true);SessionState.SetBool(Key+".cancel",false);SessionState.SetBool(Key+".ready",false);
            Write("STARTING","");
            try
            {
                if(!StaticOcclusionCulling.GenerateInBackground())throw new InvalidOperationException("Unity refused occlusion bake");
                Write("RUNNING","");
            }
            catch(Exception e)
            {
                if(StaticOcclusionCulling.isRunning){SessionState.SetBool(Key+".cancel",true);StaticOcclusionCulling.Cancel();}
                else SessionState.SetBool(Key,false);
                Write("FAILED",e.Message);throw;
            }
        }
        static void Watch()
        {
            if(!SessionState.GetBool(Key,false))return;
            bool sameScene=SceneManager.sceneCount==1&&SceneManager.GetActiveScene().path==KH_CourseGroundEditor.ScenePath&&SceneManager.GetActiveScene().handle==SessionState.GetInt(Key+".scene",-1);
            double elapsed=EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".start",0);
            if(!sameScene||EditorApplication.isPlayingOrWillChangePlaymode||elapsed>Timeout)
            {SessionState.SetBool(Key+".cancel",true);if(StaticOcclusionCulling.isRunning)StaticOcclusionCulling.Cancel();}
            if(StaticOcclusionCulling.isRunning){SessionState.SetFloat(Key+".finishedSince",-1);return;}
            if(elapsed<2)return;
            bool cancelled=SessionState.GetBool(Key+".cancel",false);
            // Native generation can finish before its asset import updates umbraDataSize.
            float finishedSince=SessionState.GetFloat(Key+".finishedSince",-1);
            if(finishedSince<0){SessionState.SetFloat(Key+".finishedSince",(float)EditorApplication.timeSinceStartup);return;}
            double settled=EditorApplication.timeSinceStartup-finishedSince;
            if(!cancelled&&sameScene&&(EditorApplication.isUpdating||settled<3||((StaticOcclusionCulling.umbraDataSize<=0||!AssetUpdated())&&settled<30)))return;
            bool ready=!cancelled&&sameScene&&StaticOcclusionCulling.umbraDataSize>0&&File.Exists(DataPath)&&AssetUpdated();
            SessionState.SetBool(Key+".ready",ready);SessionState.SetBool(Key,false);
            Write(ready?"BAKED_UNSAVED":cancelled?"CANCELLED":"FAILED",ready?"Save and verify separately; no scene auto-save":"No scene auto-save. Configuration and any partial data retained for inspection.");
        }
        static void Write(string status,string reason)
        {
            var r=new BakeReport{status=status,utc=DateTime.UtcNow.ToString("o"),reason=reason,scene=SceneManager.GetActiveScene().path,running=StaticOcclusionCulling.isRunning,dataBytes=StaticOcclusionCulling.umbraDataSize,elapsedSeconds=EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".start",0),backupDirectory=SessionState.GetString(Key+".backup",""),previousAssetStamp=SessionState.GetString(Key+".previousAssetStamp",""),assetUpdated=AssetUpdated()};
            // Reporting must never disable the watchdog for a still-running native job.
            try{File.WriteAllText(Evidence,JsonUtility.ToJson(r,true));}
            catch(Exception e){Debug.LogError("[KH Course Occlusion] Evidence output failed; bake control remains active: "+e.Message);}
        }
        static bool AssetUpdated()
        {
            string previous=SessionState.GetString(Key+".previousAssetStamp","");
            return previous==""||File.Exists(DataPath)&&File.GetLastWriteTimeUtc(DataPath).Ticks.ToString()!=previous;
        }
        [MenuItem("Tools/KominkaHillside/Course/36 - Save Completed Course Occlusion Bake")]
        public static void SaveBake()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||BuildPipeline.isBuildingPlayer||StaticOcclusionCulling.isRunning||SessionState.GetBool(Key,false)||SessionState.GetBool(Key+".cancel",false)||SceneManager.sceneCount!=1||scene.path!=KH_CourseGroundEditor.ScenePath||scene.handle!=SessionState.GetInt(Key+".scene",-1)||SessionState.GetFloat(Key+".start",-1)<0)throw new InvalidOperationException("Completed owned bake required");
            // A late-imported completed asset is authoritative even if the earlier observation timed out.
            if(StaticOcclusionCulling.umbraDataSize<=0||AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DataPath)==null||!AssetUpdated())throw new InvalidOperationException("Fresh baked asset missing; existing pre-rebake data is not completion evidence");
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Baked scene save failed");
            SessionState.SetBool(Key+".ready",false);Write("SAVED","Saved owned bake; separate verification and render comparison required");
        }
        [MenuItem("Tools/KominkaHillside/Course/37 - Verify Course Occlusion")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);var scene=SceneManager.GetActiveScene();var errors=new List<string>();
            var report=new Verification{utc=DateTime.UtcNow.ToString("o"),scene=scene.path,dataPath=DataPath,dataBytes=StaticOcclusionCulling.umbraDataSize};
            var root=GameObject.Find(Root);var terrains=UnityEngine.Object.FindObjectsOfType<Terrain>();report.terrains=terrains.Length;
            report.areas=root==null?0:root.GetComponentsInChildren<OcclusionArea>().Length;
            if(report.terrains!=20||report.areas!=20)errors.Add("Terrain/volume count mismatch");
            if(root!=null)foreach(var area in root.GetComponentsInChildren<OcclusionArea>())
            {var isView=new SerializedObject(area).FindProperty("m_IsViewVolume");if(isView==null||!isView.boolValue)errors.Add("Inactive view volume");}
            foreach(var t in terrains)if((GameObjectUtility.GetStaticEditorFlags(t.gameObject)&(StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic))!=(StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic)||!t.drawHeightmap||!t.GetComponent<TerrainCollider>().enabled)errors.Add("Terrain flags or collider mismatch");
            foreach(var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true))if((GameObjectUtility.GetStaticEditorFlags(r.gameObject)&StaticEditorFlags.OccluderStatic)!=0)report.meshOccluders++;
            if(report.meshOccluders!=0)errors.Add("Only terrain should occlude in this bake");
            var night=UnityEngine.Object.FindObjectOfType<KH_NightMode>();
            if(night==null||night.overlayRenderer.allowOcclusionWhenDynamic||night.skyHolder.allowOcclusionWhenDynamic)errors.Add("Night overlay/holder should bypass occlusion");
            var descriptor=UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
            report.referenceCameraEnabled=descriptor!=null&&descriptor.ReferenceCamera!=null&&descriptor.ReferenceCamera.GetComponent<Camera>().useOcclusionCulling;
            if(!report.referenceCameraEnabled)errors.Add("Reference camera occlusion disabled");
            string guid=AssetDatabase.AssetPathToGUID(DataPath);
            if(report.dataBytes<=0||string.IsNullOrEmpty(guid)||!File.ReadAllText(scene.path).Contains("m_OcclusionCullingData: {fileID: 36300000, guid: "+guid))errors.Add("Saved scene does not reference owned baked data");
            report.errors=errors.Count;report.findings=errors.ToArray();report.status=errors.Count==0?"OCCLUSION_DATA_OK":"FAIL";
            File.WriteAllText("Temp/des-env-002/full-course-occlusion.json",JsonUtility.ToJson(report,true));
            if(errors.Count>0)throw new InvalidOperationException(string.Join("; ",errors));
            Debug.Log("[KH Course Occlusion] "+report.status+" bytes="+report.dataBytes);
        }
    }
}
