using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    public static class KH_PCFoamAuditEditor
    {
        [Serializable] public class Shape
        {
            public string name; public float authoredWeight; public int frames, affectedVertices;
            public bool dynamic; public List<string> writers = new List<string>();
        }
        [Serializable] public class Dog
        {
            public string name, meshPath, bodyPath; public int vertices, animatorClips, dynamicAffected, eligible, legacyAnimations;
            public int[] eligibleByPart = new int[9]; public List<Shape> shapes = new List<Shape>();
        }
        [Serializable] public class Report { public string result, error;public List<Dog> dogs=new List<Dog>(); }

        internal static SkinnedMeshRenderer Body(KH_DogWash wash)
        {
            var shell=wash.shellRenderer;
            return shell.transform.parent.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(r=>r!=shell && r.sharedMesh==shell.sharedMesh && r.name=="karada");
        }

        internal static Dog Inspect(KH_DogWash wash, out bool[] excluded)
        {
            var body=Body(wash);var mesh=body.sharedMesh;
            var dog=new Dog{name=wash.name,meshPath=AssetDatabase.GetAssetPath(mesh),bodyPath=AnimationUtility.CalculateTransformPath(body.transform,null),vertices=mesh.vertexCount};
            var dynamicShapes=new HashSet<string>();var writers=new Dictionary<string,List<string>>();
            void Writer(string name,string detail,bool dynamic)
            {
                if(!writers.TryGetValue(name,out var list)){list=new List<string>();writers.Add(name,list);}list.Add(detail);
                if(dynamic)dynamicShapes.Add(name);
            }
            foreach(var animator in UnityEngine.Object.FindObjectsOfType<Animator>(true))
            {
                if(animator.runtimeAnimatorController==null || !body.transform.IsChildOf(animator.transform))continue;
                var clips=animator.runtimeAnimatorController.animationClips.Distinct().ToArray();dog.animatorClips+=clips.Length;
                foreach(var clip in clips)foreach(var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if(binding.type!=typeof(SkinnedMeshRenderer)||!binding.propertyName.StartsWith("blendShape."))continue;
                    var target=binding.path==""?animator.transform:animator.transform.Find(binding.path);
                    if(target!=body.transform)continue;
                    string name=binding.propertyName.Substring("blendShape.".Length);int index=mesh.GetBlendShapeIndex(name);
                    if(index<0)throw new InvalidOperationException("Bound unknown shape "+name);
                    var curve=AnimationUtility.GetEditorCurve(clip,binding);var keys=curve.keys;
                    float authored=body.GetBlendShapeWeight(index);
                    bool dynamic=keys.Any(k=>Mathf.Abs(k.value-authored)>.001f || (k.inTangent!=0&&!float.IsInfinity(k.inTangent)) || (k.outTangent!=0&&!float.IsInfinity(k.outTangent)));
                    Writer(name,AssetDatabase.GetAssetPath(clip)+" / "+clip.name+" ["+string.Join(",",keys.Select(k=>k.value.ToString("R")))+"]",dynamic);
                }
            }
            foreach(var pet in UnityEngine.Object.FindObjectsOfType<KD_DogPetController>(true))
            {
                if(pet.faceRenderer!=body)continue;
                // These are the only dog-body Udon SetBlendShapeWeight writes in the current source tree.
                foreach(string name in new[]{"metozi","egao"})if(mesh.GetBlendShapeIndex(name)>=0)Writer(name,"KD_DogPetController / "+pet.name,true);
            }
            dog.legacyAnimations=body.GetComponentsInParent<Animation>(true).Length;
            if(dog.legacyAnimations>0)throw new InvalidOperationException("Legacy animation needs audit: "+wash.name);
            excluded=new bool[mesh.vertexCount];var dp=new Vector3[mesh.vertexCount];var dn=new Vector3[mesh.vertexCount];var dt=new Vector3[mesh.vertexCount];
            for(int s=0;s<mesh.blendShapeCount;s++)
            {
                string name=mesh.GetBlendShapeName(s);var shape=new Shape{name=name,authoredWeight=body.GetBlendShapeWeight(s),frames=mesh.GetBlendShapeFrameCount(s),dynamic=dynamicShapes.Contains(name)};
                if(writers.TryGetValue(name,out var list))shape.writers=list;
                var affected=new bool[mesh.vertexCount];
                for(int f=0;f<shape.frames;f++)
                {
                    mesh.GetBlendShapeFrameVertices(s,f,dp,dn,dt);
                    for(int i=0;i<dp.Length;i++)affected[i]|=dp[i].sqrMagnitude>1e-16f||dn[i].sqrMagnitude>1e-16f;
                }
                shape.affectedVertices=affected.Count(b=>b);dog.shapes.Add(shape);
                if(shape.dynamic)for(int i=0;i<excluded.Length;i++)excluded[i]|=affected[i];
            }
            dog.dynamicAffected=excluded.Count(b=>b);var weights=mesh.boneWeights;
            for(int i=0;i<excluded.Length;i++)
            {
                int part=Mathf.Clamp(Mathf.RoundToInt(wash.boneToPart[weights[i].boneIndex0]),0,8);
                if(part==7)excluded[i]=true;
                if(!excluded[i]){dog.eligible++;dog.eligibleByPart[part]++;}
            }
            return dog;
        }

        [MenuItem("Tools/KominkaHillside/PC Foam/11 - Audit Bodies")]
        public static void Audit()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit required");
            var report=new Report();
            try
            {
                foreach(var wash in UnityEngine.Object.FindObjectsOfType<KH_DogWash>().OrderBy(w=>w.dogIndex))report.dogs.Add(Inspect(wash,out _));
                if(report.dogs.Count!=6)throw new InvalidOperationException("Expected six dogs");report.result="PASS";
            }
            catch(Exception ex){report.result="FAIL";report.error=ex.ToString();Debug.LogError(ex);}
            Directory.CreateDirectory("Temp/PCFoamProof");File.WriteAllText("Temp/PCFoamProof/body-audit.json",JsonUtility.ToJson(report,true));
        }
    }
}
