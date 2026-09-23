using System.IO;
using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    public static class KD_PosePolishEditor
    {
        static MeshFilter[] PreviewMeshes(Transform root) => root.GetComponentsInChildren<MeshFilter>(true)
            .Where(f=>f.gameObject.activeSelf && f.GetComponent<MeshRenderer>()!=null && f.GetComponent<MeshRenderer>().enabled).ToArray();
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/5 - Update Controllers And Previews")]
        public static void Apply() => ApplyPoses(null,new[]{2,3,4});
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/All BellyUp/3 - Update Previews")]
        public static void ApplyAllBellyUp() => ApplyPoses(null,new[]{4});
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Curl Paws/3 - Update Previews")]
        public static void ApplyCurlPaws()
        {
            ApplyPoses("Lab",new[]{3});ApplyPoses("Samoyed",new[]{3});
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Curl Paws/2 - Render")]
        public static void RenderCurlPaws()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                foreach(var def in KD_DogDefs.All.Where(d=>d.key=="Lab"||d.key=="Samoyed"))
                    KD_CompanionMotionEditor.RenderClip(def,KD_CallPoseEditor.PoseClip(def,3),0,"Curl","Temp/pose-polish/curl-paws",scene);
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Corgi BellyUp/3 - Update Preview")]
        public static void ApplyCorgiBellyUp() => ApplyPoses("Corgi",new[]{4});
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Pome BellyUp/3 - Update Preview")]
        public static void ApplyPomeBellyUp() => ApplyPoses("Pome",new[]{4});
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Remaining BellyUp/3 - Update Previews")]
        public static void ApplyRemainingBellyUp() => ApplyPoses(null,new[]{4},true);
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Long Legs BellyUp/3 - Update Previews")]
        public static void ApplyLongLegBellyUp() => ApplyPoses(null,new[]{4},false,true);
        static void ApplyPoses(string breed,int[] poses,bool remainingBellyUp=false,bool longLegBellyUp=false)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");
            var c=KD_SceneObjects.Find("KD_CallPose").GetComponent<KD_CallPose>();
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_SleepPreview.mat");
            var scene=EditorSceneManager.NewPreviewScene();var log=new StringBuilder();
            try
            {
                for(int d=0;d<KD_DogDefs.All.Length;d++)
                {
                    var def=KD_DogDefs.All[d];
                    if(breed!=null && def.key!=breed)continue;
                    if(remainingBellyUp && !KD_CompanionMotionEditor.IsRemainingBellyUp(def.key))continue;
                    if(longLegBellyUp && !KD_CompanionMotionEditor.IsLongLegBellyUp(def.key))continue;
                    var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
                    foreach(string name in poses.Contains(2)?new[]{"Sit","CallSit"}:new string[0])
                    {
                        var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name==name).state;
                        state.motion=KD_CallPoseEditor.SitClip(def);EditorUtility.SetDirty(state);
                    }
                    if(poses.Contains(2))EditorUtility.SetDirty(controller);
                    foreach(int pose in poses)
                    {
                        var source=KD_SceneObjects.Find(def.DogName);var clone=Object.Instantiate(source);
                        SceneManager.MoveGameObjectToScene(clone,scene);
                        try
                        {
                            clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.SetActive(true);
                            KD_CallPoseEditor.PoseClip(def,pose).SampleAnimation(clone,0);KD_DogPreviewEditor.ApplyConstraints(clone);
                            var mini=c.miniatures[d*5+pose];var art=mini.transform.Find("Art");
                            KD_DogPreviewEditor.BakeBody(clone,art,def.key+"_call"+pose,null);
                            KD_DogPreviewEditor.MirrorBakedMeshes(art,c.placement.poseGhosts[d*5+pose].transform,material);
                            var filters=PreviewMeshes(art);var bounds=filters[0].sharedMesh.bounds;
                            foreach(var filter in filters.Skip(1))bounds.Encapsulate(filter.sharedMesh.bounds);
                            art.localPosition=-bounds.center;
                            var projected=new Bounds(Vector3.zero,Vector3.zero);
                            for(int corner=0;corner<8;corner++)
                                projected.Encapsulate(mini.transform.localRotation*Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                            mini.transform.localScale=Vector3.one*Mathf.Min(160/Mathf.Max(.1f,projected.size.x),130/Mathf.Max(.1f,projected.size.y));
                            log.AppendLine(def.key+" pose="+pose+" meshes="+filters.Length);
                        }
                        finally{Object.DestroyImmediate(clone);}
                    }
                }
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
                Directory.CreateDirectory("Temp/pose-polish");File.WriteAllText("Temp/pose-polish/applied.txt",log.ToString());
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/6 - Verify Integration")]
        public static void VerifyIntegration()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");
            var c=KD_SceneObjects.Find("KD_CallPose").GetComponent<KD_CallPose>();var log=new StringBuilder();int failures=0;
            void Check(bool ok,string label){log.AppendLine((ok?"PASS ":"FAIL ")+label);if(!ok)failures++;}
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
            for(int d=0;d<KD_DogDefs.All.Length;d++)
            {
                var def=KD_DogDefs.All[d];var ctrl=AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
                foreach(var pair in new[]{("Sit",2),("CallSit",2),("CallCurl",3),("CallBellyUp",4)})
                    Check(ctrl.layers[0].stateMachine.states.Single(s=>s.state.name==pair.Item1).state.motion==KD_CallPoseEditor.PoseClip(def,pair.Item2),def.key+" state "+pair.Item1);
                foreach(int pose in new[]{2,3,4})
                {
                    var mini=c.miniatures[d*5+pose].transform.Find("Art");var ghost=c.placement.poseGhosts[d*5+pose].transform;
                    var a=PreviewMeshes(mini);var b=PreviewMeshes(ghost);
                    Check(a.Length>0 && a.Length==b.Length,def.key+" preview count "+pose);
                    for(int i=0;i<Mathf.Min(a.Length,b.Length);i++)
                        Check(a[i].sharedMesh==b[i].sharedMesh && a[i].transform.localPosition==b[i].transform.localPosition && a[i].transform.localRotation==b[i].transform.localRotation && a[i].transform.localScale==b[i].transform.localScale,def.key+" shared preview "+pose+"/"+i);
                    // Independent of Apply/BakeBody: sample the current clip and compare every skin vertex.
                    var dog=Object.Instantiate(KD_SceneObjects.Find(def.DogName));SceneManager.MoveGameObjectToScene(dog,scene);
                    try
                    {
                        dog.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);dog.SetActive(true);
                        KD_CallPoseEditor.PoseClip(def,pose).SampleAnimation(dog,0);KD_DogPreviewEditor.ApplyConstraints(dog);
                        int meshIndex=0;float error=0;
                        foreach(var skin in dog.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            bool accessory=false;
                            for(var t=skin.transform;t!=dog.transform;t=t.parent)if(t.name.StartsWith("KD_Acc_"))accessory=true;
                            if(accessory||!skin.enabled||!skin.gameObject.activeInHierarchy)continue;
                            var sample=new Mesh();
                            try
                            {
                                skin.BakeMesh(sample,true);
                                Check(meshIndex<a.Length,def.key+" preview skin exists "+pose+"/"+meshIndex);
                                if(meshIndex>=a.Length)continue;
                                var expected=sample.vertices;var actual=a[meshIndex].sharedMesh.vertices;
                                Check(expected.Length==actual.Length,def.key+" vertex count "+pose+"/"+meshIndex);
                                for(int v=0;v<Mathf.Min(expected.Length,actual.Length);v++)
                                    error=Mathf.Max(error,Vector3.Distance(skin.transform.TransformPoint(expected[v]),actual[v]));
                            }
                            finally{Object.DestroyImmediate(sample);}
                            meshIndex++;
                        }
                        Check(meshIndex==a.Length && error<.0001f,def.key+" current clip vertices "+pose+" maxError="+error.ToString("F7"));
                    }
                    finally{Object.DestroyImmediate(dog);}
                }
            }
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
            log.AppendLine("RESULT dogs=6 poses=18 failures="+failures);
            Directory.CreateDirectory("Temp/pose-polish");File.WriteAllText("Temp/pose-polish/integration.txt",log.ToString());
            if(failures>0)throw new InvalidOperationException(log.ToString());
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/3 - Render")]
        public static void Render()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach(var def in KD_DogDefs.All)
                    foreach(var pose in new[]{"Curl","BellyUp","Sit"})
                    {
                        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/KominkaHillside/Imported/KominkaDogRun/Animations/Companion/KD_"+pose+"_"+def.key+".anim");
                        KD_CompanionMotionEditor.RenderClip(def,clip,0,pose,"Temp/pose-polish/after",scene);
                    }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Corgi BellyUp/2 - Render")]
        public static void RenderCorgiBellyUp() => RenderBellyUp("Corgi","Temp/pose-polish/corgi-revision");
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Pome BellyUp/2 - Render")]
        public static void RenderPomeBellyUp() => RenderBellyUp("Pome","Temp/pose-polish/pome-revision");
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/All BellyUp/2 - Render")]
        public static void RenderAllBellyUp()
        {
            foreach(var def in KD_DogDefs.All)RenderBellyUp(def.key,"Temp/pose-polish/mouth-revision");
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Remaining BellyUp/2 - Render")]
        public static void RenderRemainingBellyUp()
        {
            foreach(var def in KD_DogDefs.All)
                if(KD_CompanionMotionEditor.IsRemainingBellyUp(def.key))RenderBellyUp(def.key,"Temp/pose-polish/remaining-revision");
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Long Legs BellyUp/2 - Render")]
        public static void RenderLongLegBellyUp()
        {
            foreach(var def in KD_DogDefs.All)
                if(KD_CompanionMotionEditor.IsLongLegBellyUp(def.key))RenderBellyUp(def.key,"Temp/pose-polish/longlegs-revision");
        }
        static void RenderBellyUp(string breed,string output)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var def=KD_DogDefs.All.Single(d=>d.key==breed);
                var clip=KD_CallPoseEditor.PoseClip(def,4);
                KD_CompanionMotionEditor.RenderClip(def,clip,0,"BellyUp",output,scene);
                KD_CompanionMotionEditor.RenderClip(def,clip,0,"BellyUpFace",output,scene,true);
                var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(def.idle.guid));
                KD_CompanionMotionEditor.RenderClip(def,idle,0,"Idle",output,scene);
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/1 - Survey")]
        public static void Survey()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Edit mode only");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var def in KD_DogDefs.All)
                {
                    var sit = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(def.sit.guid));
                    foreach (float time in new[] { 0f, 2f, 8f, 16f })
                        KD_CompanionMotionEditor.RenderClip(def, sit, time, "Sit-" + time.ToString("F0"), "Temp/pose-polish/before", scene);
                    foreach (var pose in new[] { "Curl", "BellyUp" })
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/KominkaHillside/Imported/KominkaDogRun/Animations/Companion/KD_" + pose + "_" + def.key + ".anim");
                        KD_CompanionMotionEditor.RenderClip(def, clip, 0, pose, "Temp/pose-polish/before", scene);
                    }
                }
                File.WriteAllText("Temp/pose-polish/survey.txt", "survey-v1: 6 dogs x (4 sit times + 2 resting poses) x 3 views\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
