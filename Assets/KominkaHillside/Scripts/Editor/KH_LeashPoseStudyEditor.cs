using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Animations;

namespace KominkaHillside.Editor
{
    // Samples clips on disposable preview-scene clones, never on the live dogs and never enters Play.
    public static class KH_LeashPoseStudyEditor
    {
        static IEnumerator<bool> study;
        static double nextTick;
        [MenuItem("Tools/KominkaHillside/Walking/Production 5 Pose Study")]
        public static void Run()
        {
            if(study!=null)throw new InvalidOperationException("Pose study already running");
            study=Sample().GetEnumerator();nextTick=0;EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<nextTick)return;
            nextTick=EditorApplication.timeSinceStartup+.08;
            try{if(!study.MoveNext()){study.Dispose();study=null;EditorApplication.update-=Tick;}}
            catch(Exception e){study.Dispose();study=null;EditorApplication.update-=Tick;Debug.LogException(e);}
            EditorApplication.QueuePlayerLoopUpdate();
        }
        static IEnumerable<bool> Sample()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Edit mode only");
            var m=UnityEngine.Object.FindObjectOfType<KH_LeashManager>();
            var report=new StringBuilder("Editor animation sampling, not a Play/VR result.\n");
            string output="Temp/KH_LeashRelease/Poses";Directory.CreateDirectory(output);
            foreach(var dog in m.dogRoots)
            foreach(string pose in new[]{"rest","aruku","hasiru","Sit","huse","taimen","PickupPose","taiki"})
            {
                var preview=EditorSceneManager.NewPreviewScene();GameObject clone=null;
                try
                {
                    clone=UnityEngine.Object.Instantiate(dog.gameObject);SceneManager.MoveGameObjectToScene(clone,preview);
                    clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                    var animator=clone.GetComponentInChildren<Animator>();animator.enabled=true;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();
                    var transforms=clone.GetComponentsInChildren<Transform>(true);
                    var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();var scales=transforms.Select(t=>t.localScale).ToArray();
                    var clips=animator.runtimeAnimatorController.animationClips.Distinct().ToArray();
                    var collar=clone.GetComponentInChildren<KH_LeashCollar>();
                    var skin=clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s=>s.name=="karada");
                    var neck=transforms.First(t=>t.name=="Bone.003");var head=transforms.First(t=>t.name=="Bone.004");
                    report.AppendLine(dog.name+" neck="+AnimationUtility.CalculateTransformPath(neck,clone.transform)+" skin3="+AnimationUtility.CalculateTransformPath(skin.bones[3],clone.transform)+" animator="+animator.name);
                    var rest=KH_LeashFitInspector.SkinWorldVertices(skin);
                    var selected=Enumerable.Range(0,rest.Length).Where(i=>{var p=collar.transform.InverseTransformPoint(rest[i]);return Mathf.Abs(p.z)<.018f&&p.magnitude<.22f;}).ToArray();
                    var neckRest=selected.Select(i=>neck.InverseTransformPoint(rest[i])).ToArray();var headRest=selected.Select(i=>head.InverseTransformPoint(rest[i])).ToArray();
                    var lightGo=new GameObject("PreviewLight");SceneManager.MoveGameObjectToScene(lightGo,preview);var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;light.transform.rotation=Quaternion.Euler(45,-30,0);
                    var cameraGo=new GameObject("PreviewCamera");SceneManager.MoveGameObjectToScene(cameraGo,preview);var camera=cameraGo.AddComponent<Camera>();camera.scene=preview;camera.nearClipPlane=.01f;camera.farClipPlane=10;camera.fieldOfView=45;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.19f,.21f,.23f);
                    var rt=new RenderTexture(768,640,24);camera.targetTexture=rt;
                    try
                    {
                        {
                            var clip=pose=="rest"?null:clips.FirstOrDefault(c=>c.name.IndexOf(pose,StringComparison.OrdinalIgnoreCase)>=0);
                            if(pose=="huse"&&clip==null)clip=clips.FirstOrDefault(c=>c.name.Contains("LieDown"));
                            if(pose!="rest"&&clip==null){report.AppendLine(dog.name+" "+pose+" NOT_SAMPLED missing clip");continue;}
                            float time=clip==null?0:clip.length*(pose=="Sit"||pose=="huse"||pose=="PickupPose"?.95f:.35f);
                            if(clip!=null)
                            {
                                clip.SampleAnimation(clone,time);
                            }
                            animator.enabled=false;
                            ApplyDogConstraints(clone);
                            var moved=KH_LeashFitInspector.SkinWorldVertices(skin);float neckError=0,headError=0;
                            for(int j=0;j<selected.Length;j++){neckError+=Vector3.Distance(neck.TransformPoint(neckRest[j]),moved[selected[j]]);headError+=Vector3.Distance(head.TransformPoint(headRest[j]),moved[selected[j]]);}
                            int changed=Enumerable.Range(0,transforms.Length).Count(j=>Quaternion.Angle(transforms[j].localRotation,rotations[j])>.05f);
                            report.AppendLine(dog.name+" "+pose+" clip="+(clip==null?"rest":clip.name)+" t="+time.ToString("F3")+" samples="+selected.Length+" changed="+changed+" neckMeanMm="+(1000*neckError/Mathf.Max(1,selected.Length)).ToString("F2")+" headMeanMm="+(1000*headError/Mathf.Max(1,selected.Length)).ToString("F2"));
                            var focus=collar.transform.position+Vector3.up*.01f;camera.transform.position=focus+new Vector3(.50f,.18f,.65f);camera.transform.LookAt(focus);
                            var frozen=new List<GameObject>();var bakedMeshes=new List<Mesh>();var visibleSkins=clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>s.enabled&&s.gameObject.activeInHierarchy).ToArray();
                            foreach(var s in visibleSkins)
                            {
                                var baked=new Mesh();s.BakeMesh(baked,true);bakedMeshes.Add(baked);
                                // Independent scale check with blendshapes zeroed on the disposable clone only.
                                var savedWeights=Enumerable.Range(0,s.sharedMesh.blendShapeCount).Select(s.GetBlendShapeWeight).ToArray();
                                for(int bi=0;bi<savedWeights.Length;bi++)s.SetBlendShapeWeight(bi,0);
                                var control=new Mesh();s.BakeMesh(control,true);var expected=KH_LeashFitInspector.SkinWorldVertices(s);var actual=control.vertices;
                                float maxDifference=0;for(int vi=0;vi<actual.Length;vi++)maxDifference=Mathf.Max(maxDifference,Vector3.Distance(s.transform.TransformPoint(actual[vi]),expected[vi]));
                                report.AppendLine(dog.name+" "+pose+" bakeScale "+s.name+" maxMm="+(maxDifference*1000).ToString("F3"));
                                UnityEngine.Object.DestroyImmediate(control);for(int bi=0;bi<savedWeights.Length;bi++)s.SetBlendShapeWeight(bi,savedWeights[bi]);
                                var go=new GameObject("FrozenPose");SceneManager.MoveGameObjectToScene(go,preview);// Bake placement into vertices: a 100x renderer Transform also scales fur extrusion on MeshRenderer.
                                var matrix=s.transform.localToWorldMatrix;var bv=baked.vertices;var bn=baked.normals;var bt=baked.tangents;var nm=matrix.inverse.transpose;
                                for(int vi=0;vi<bv.Length;vi++)bv[vi]=matrix.MultiplyPoint3x4(bv[vi]);
                                for(int vi=0;vi<bn.Length;vi++)bn[vi]=nm.MultiplyVector(bn[vi]).normalized;
                                for(int vi=0;vi<bt.Length;vi++){var tv=matrix.MultiplyVector(new Vector3(bt[vi].x,bt[vi].y,bt[vi].z)).normalized;bt[vi]=new Vector4(tv.x,tv.y,tv.z,bt[vi].w);}
                                baked.vertices=bv;baked.normals=bn;baked.tangents=bt;baked.RecalculateBounds();
                                go.AddComponent<MeshFilter>().sharedMesh=baked;go.AddComponent<MeshRenderer>().sharedMaterials=s.sharedMaterials;frozen.Add(go);s.enabled=false;
                            }
                            camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;var texture=new Texture2D(768,640,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,768,640),0,0);texture.Apply();RenderTexture.active=previous;
                            File.WriteAllBytes(output+"/"+dog.name+"-"+pose+".png",texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
                            foreach(var s in visibleSkins)s.enabled=true;foreach(var go in frozen)UnityEngine.Object.DestroyImmediate(go);foreach(var mesh in bakedMeshes)UnityEngine.Object.DestroyImmediate(mesh);
                            yield return true;
                        }
                    }
                    finally{camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
                }
                finally{EditorSceneManager.ClosePreviewScene(preview);}
            }
            File.WriteAllText(output+"/report.txt",report.ToString());
        }
        // Imported dog-rig preview contract from KominkaDogRun/Editor/KD_DogPreviewEditor.ApplyConstraints.
        // Only full-weight, single-source position/rotation constraints are supported here.
        static void ApplyDogConstraints(GameObject clone)
        {
            foreach(var t in clone.GetComponentsInChildren<Transform>(true))
            {
                var p=t.GetComponent<PositionConstraint>();
                if(p!=null&&p.constraintActive&&p.weight>.99f&&p.sourceCount>0&&p.GetSource(0).weight>.99f)
                {
                    if(p.sourceCount!=1||p.translationAxis!=(Axis.X|Axis.Y|Axis.Z))throw new InvalidOperationException("Unsupported preview position constraint");
                    t.position=p.GetSource(0).sourceTransform.position+(t.parent==null?p.translationOffset:t.parent.TransformVector(p.translationOffset));
                }
                var r=t.GetComponent<RotationConstraint>();
                if(r!=null&&r.constraintActive&&r.weight>.99f&&r.sourceCount>0&&r.GetSource(0).weight>.99f)
                {
                    if(r.sourceCount!=1||r.rotationAxis!=(Axis.X|Axis.Y|Axis.Z))throw new InvalidOperationException("Unsupported preview rotation constraint");
                    t.rotation=r.GetSource(0).sourceTransform.rotation*Quaternion.Euler(r.rotationOffset);
                }
            }
        }
    }
}
