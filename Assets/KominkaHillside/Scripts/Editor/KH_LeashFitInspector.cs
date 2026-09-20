using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    // Read-only evidence for attachment choice. Never changes the purchased rig.
    public static class KH_LeashFitInspector
    {
        [MenuItem("Tools/KominkaHillside/Walking/Inspect Neck Skin")]
        public static void Inspect()
        {
            var manager=Object.FindObjectOfType<KH_LeashManager>();
            if(Application.isPlaying || manager==null) throw new System.InvalidOperationException("Edit mode hillside required");
            var log=new StringBuilder();
            for(int i=0;i<manager.dogRoots.Length;i++)
            {
                var dog=manager.dogRoots[i]; var collar=manager.collarAnchors[i].parent;
                log.AppendLine("DOG "+dog.name+" collarParent="+collar.parent.name);
                foreach(var skin in dog.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(!skin.gameObject.activeInHierarchy || skin.sharedMesh==null || skin.sharedMesh.boneWeights.Length==0)continue;
                    var vertices=SkinWorldVertices(skin); var weights=skin.sharedMesh.boneWeights;
                    log.AppendLine("LBS skinScale="+skin.transform.lossyScale+" firstDog="+dog.InverseTransformPoint(vertices[0])+" collarDog="+dog.InverseTransformPoint(collar.position));
                    var scores=new float[skin.bones.Length]; int near=0;
                    for(int v=0;v<vertices.Length;v++)
                    {
                        var p=collar.InverseTransformPoint(vertices[v]);
                        if(Mathf.Abs(p.z)>.024f || p.magnitude>.25f)continue;
                        near++;var w=weights[v];
                        scores[w.boneIndex0]+=w.weight0;scores[w.boneIndex1]+=w.weight1;
                        scores[w.boneIndex2]+=w.weight2;scores[w.boneIndex3]+=w.weight3;
                    }
                    log.AppendLine("SKIN "+skin.name+" asset="+AssetDatabase.GetAssetPath(skin.sharedMesh)+" nearbyVertices="+near);
                    for(int b=0;b<skin.bones.Length;b++)if(scores[b]>.01f)
                        log.AppendLine("  "+b+" "+skin.bones[b].name+" parent="+skin.bones[b].parent.name+" weight="+scores[b].ToString("F2")+" posDog="+dog.InverseTransformPoint(skin.bones[b].position).ToString("F4"));
                }
                var anim=dog.GetComponentInChildren<Animator>();
                if(anim!=null&&anim.runtimeAnimatorController!=null)
                    foreach(var clip in anim.runtimeAnimatorController.animationClips.Distinct())log.AppendLine("CLIP "+clip.name+" "+clip.length+" "+AssetDatabase.GetAssetPath(clip));
            }
            Directory.CreateDirectory("Temp/KH_LeashRelease");File.WriteAllText("Temp/KH_LeashRelease/neck-skin.txt",log.ToString());
        }
        public static Vector3[] SkinWorldVertices(SkinnedMeshRenderer skin)
        {
            var mesh=skin.sharedMesh;var result=mesh.vertices;var weights=mesh.boneWeights;
            var matrices=new Matrix4x4[skin.bones.Length];var bind=mesh.bindposes;
            for(int i=0;i<matrices.Length;i++)matrices[i]=skin.bones[i].localToWorldMatrix*bind[i];
            for(int i=0;i<result.Length;i++)
            {
                var p=result[i];var w=weights[i];
                result[i]=matrices[w.boneIndex0].MultiplyPoint3x4(p)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(p)*w.weight1+
                    matrices[w.boneIndex2].MultiplyPoint3x4(p)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(p)*w.weight3;
            }
            return result;
        }
    }
}
