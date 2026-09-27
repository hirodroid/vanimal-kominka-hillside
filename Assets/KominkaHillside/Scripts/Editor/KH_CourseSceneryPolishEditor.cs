using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    public static class KH_CourseSceneryPolishEditor
    {
        [Serializable] public class Placement
        {public string path;public Vector3 position;public float ground,oldGround,delta,routeDistance;public int renderers,colliders;}
        [Serializable] public class Report
        {public string utc;public List<Placement> placements=new List<Placement>();public int barkVertices,coloredBarkVertices;public Color barkColorMin,barkColorMax;}
        const string PolishDir="Temp/des-env-002/scenery-polish";
        const string BarkPath=KH_CourseGroundEditor.Dir+"/Scenery/KH_CourseBark.mat";
        static readonly string[] MovedPaths={"KH_World/KH_BoardTrees/KH_BoardTree_08","KH_World/KH_BoardTrees/KH_BoardTree_09","KH_World/KH_BoardTrees/KH_BoardTree_14","KH_World/KH_Shrubs/KH_Shrub_11"};
        [Serializable] public class Move
        {public string path;public Vector3 before,after;public float rootOffset,routeDistance;}
        [Serializable] public class Applied
        {public string utc;public List<Move> moves=new List<Move>();public bool navAndOcclusionRefreshRequired=true;}
        [MenuItem("Tools/KominkaHillside/Course/50 - Correct Course Bark and Floating Plants")]
        public static void Apply()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if(Directory.Exists(PolishDir))throw new InvalidOperationException("Existing polish backup preserved; first application only");
            var old=AssetDatabase.LoadAssetAtPath<TerrainData>(KH_TerrainPrototypeConfig.TerrainDataPath);
            var terrain=UnityEngine.Object.FindObjectsOfType<Terrain>();var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            var bark=AssetDatabase.LoadAssetAtPath<Material>(BarkPath);
            if(bark==null||!bark.HasProperty("_VertexTint")||ShaderUtil.ShaderHasError(bark.shader))throw new InvalidOperationException("Bark shader not ready");
            var report=new Applied{utc=DateTime.UtcNow.ToString("o")};var transforms=new List<Transform>();
            foreach(string path in MovedPaths)
            {
                var go=GameObject.Find(path);if(go==null)throw new InvalidOperationException("Missing "+path);
                Vector3 p=go.transform.position;float oldGround=old.GetInterpolatedHeight(p.x/old.size.x,p.z/old.size.z);
                float offset=p.y-oldGround;
                if(Mathf.Abs(offset-(path.Contains("BoardTree")?-.2f:-.1f))>.02f)throw new InvalidOperationException("Plant was independently adjusted: "+path);
                var move=new Move{path=path,before=p,rootOffset=offset};
                if(route._DistanceToTrail(p)<6.2f)
                {
                    Vector3 on=route._ProjectToTrail(p),away=p-on;away.y=0;
                    if(away.sqrMagnitude<.01f)throw new InvalidOperationException("Ambiguous plant side");
                    var clear=on+away.normalized*6.3f;p.x=clear.x;p.z=clear.z;
                }
                p.y=Ground(terrain,p)+offset;move.after=p;move.routeDistance=route._DistanceToTrail(p);
                if(move.routeDistance<6.2f)throw new InvalidOperationException("Plant would obstruct course: "+path);
                transforms.Add(go.transform);report.moves.Add(move);
            }
            Directory.CreateDirectory(PolishDir);File.Copy(KH_CourseGroundEditor.ScenePath,PolishDir+"/before.unity");File.Copy(BarkPath,PolishDir+"/before-bark.mat");
            File.Copy(KH_CourseOcclusionEditor.DataPath,PolishDir+"/before-occlusion.asset");
            float beforeTint=bark.GetFloat("_VertexTint");var scene=EditorSceneManager.GetActiveScene();
            try
            {
                Undo.RecordObject(bark,"Correct bark vertex tint");bark.SetFloat("_VertexTint",0);EditorUtility.SetDirty(bark);
                for(int i=0;i<transforms.Count;i++)
                {Undo.RecordObject(transforms[i],"Ground copied plant");transforms[i].position=report.moves[i].after;EditorUtility.SetDirty(transforms[i]);if(PrefabUtility.IsPartOfPrefabInstance(transforms[i]))PrefabUtility.RecordPrefabInstancePropertyModifications(transforms[i]);}
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))throw new IOException("Polish save failed");
                File.WriteAllText(PolishDir+"/applied.json",JsonUtility.ToJson(report,true));
            }
            catch
            {
                bark.SetFloat("_VertexTint",beforeTint);EditorUtility.SetDirty(bark);
                for(int i=0;i<transforms.Count;i++){transforms[i].position=report.moves[i].before;EditorUtility.SetDirty(transforms[i]);if(PrefabUtility.IsPartOfPrefabInstance(transforms[i]))PrefabUtility.RecordPrefabInstancePropertyModifications(transforms[i]);}
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))Debug.LogError("[KH Scenery Polish] Rollback not saved; backup retained");
                throw;
            }
            Debug.Log("[KH Scenery Polish] Corrected bark and four plants; refresh course NavMesh and occlusion before acceptance");
        }
        [MenuItem("Tools/KominkaHillside/Course/51 - Verify Course Scenery Polish")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report=JsonUtility.FromJson<Applied>(File.ReadAllText(PolishDir+"/applied.json"));
            var terrain=UnityEngine.Object.FindObjectsOfType<Terrain>();var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            if(report.moves.Count!=4||AssetDatabase.LoadAssetAtPath<Material>(BarkPath).GetFloat("_VertexTint")!=0)throw new InvalidOperationException("Polish missing");
            foreach(var move in report.moves)
            {
                var go=GameObject.Find(move.path);if(go==null)throw new InvalidOperationException("Missing plant");var p=go.transform.position;
                if(Vector3.Distance(p,move.after)>.001f||Mathf.Abs(p.y-Ground(terrain,p)-move.rootOffset)>.002f||route._DistanceToTrail(p)<6.2f)throw new InvalidOperationException("Placement mismatch: "+move.path);
            }
            Debug.Log("[KH Scenery Polish] SAVED_PLACEMENT_OK plants=4 barkVertexTint=0; Nav/PVS require separate verification");
        }
        [MenuItem("Tools/KominkaHillside/Course/49 - Survey Course Scenery Placement")]
        public static void Survey()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var old=AssetDatabase.LoadAssetAtPath<TerrainData>(KH_TerrainPrototypeConfig.TerrainDataPath);
            var terrains=UnityEngine.Object.FindObjectsOfType<Terrain>();
            var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),barkColorMin=Color.white,barkColorMax=Color.black};
            foreach(string name in new[]{"KH_Trees","KH_BoardTrees","KH_Shrubs"})
            {
                var parent=GameObject.Find("KH_World/"+name);if(parent==null)throw new InvalidOperationException("Missing "+name);
                foreach(Transform child in parent.transform)
                {
                    Vector3 p=child.position;float ground=Ground(terrains,p);
                    float oldGround=old.GetInterpolatedHeight(p.x/old.size.x,p.z/old.size.z);
                    report.placements.Add(new Placement{path="KH_World/"+name+"/"+child.name,position=p,ground=ground,oldGround=oldGround,delta=ground-oldGround,
                        routeDistance=route._DistanceToTrail(p),renderers=child.GetComponentsInChildren<Renderer>(true).Length,colliders=child.GetComponentsInChildren<Collider>(true).Length});
                }
            }
            var bark=GameObject.Find("KH_CourseScenery/Trees/Tree_0/Near");if(bark==null)throw new InvalidOperationException("New tree required");
            foreach(var filter in bark.GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<Renderer>();if(renderer==null||renderer.sharedMaterial.name!="KH_CourseBark")continue;
                foreach(Color c in filter.sharedMesh.colors)
                {
                    report.barkVertices++;if(Mathf.Abs(c.r-c.g)>.05f||Mathf.Abs(c.g-c.b)>.05f)report.coloredBarkVertices++;
                    report.barkColorMin=new Color(Mathf.Min(report.barkColorMin.r,c.r),Mathf.Min(report.barkColorMin.g,c.g),Mathf.Min(report.barkColorMin.b,c.b),1);
                    report.barkColorMax=new Color(Mathf.Max(report.barkColorMax.r,c.r),Mathf.Max(report.barkColorMax.g,c.g),Mathf.Max(report.barkColorMax.b,c.b),1);
                }
            }
            File.WriteAllText("Temp/des-env-002/scenery-placement.json",JsonUtility.ToJson(report,true));
            Debug.Log("[KH Course Scenery Placement] surveyed="+report.placements.Count+" coloredBarkVertices="+report.coloredBarkVertices+"/"+report.barkVertices);
        }
        static float Ground(Terrain[] terrains,Vector3 p)
        {
            foreach(var terrain in terrains)
            {var t=terrain.transform.position;var s=terrain.terrainData.size;if(p.x>=t.x&&p.x<=t.x+s.x&&p.z>=t.z&&p.z<=t.z+s.z)return t.y+terrain.terrainData.GetInterpolatedHeight((p.x-t.x)/s.x,(p.z-t.z)/s.z);}
            throw new InvalidOperationException("No Terrain at "+p);
        }
    }
}
