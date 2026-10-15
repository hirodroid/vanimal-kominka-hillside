using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using KominkaDogRun.Editor;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    // Original grounded choreography. Keys are authored against the measured mesh skeleton,
    // then written to its drivers. No purchased animation curves or network-root keys are copied.
    public static partial class KH_ParadeDanceEditor
    {
        public const string Dir = "Assets/KominkaHillside/Animations/Parade";
        public const string Evidence = "docs/plans/halloween-parade/evidence/dance-v2";
        public static readonly string[] Moves = { "Groove", "Reply", "Bow", "LookLeft", "LookRight", "Hop" };
        static readonly string[][] Legs = {
            new[]{"Bone.011_L","Bone.012_L","Bone.013_L","Bone.014_L"},
            new[]{"Bone.011_R","Bone.012_R","Bone.013_R","Bone.014_R"},
            new[]{"Bone.005_L","Bone.010_L","Bone.015_L","Bone.016_L"},
            new[]{"Bone.005_R","Bone.010_R","Bone.015_R","Bone.016_R"} };
        public static string PathFor(string breed,string move) => Dir+"/KH_Parade"+move+"_"+breed+".anim";
        public static float Duration(string move) => move=="Groove" ? 5f : 2.5f;
        class Rig : IDisposable
        {
            public GameObject dog; public KH_DogRig rig; public Transform[] bones;
            public Vector3[] pos; public Quaternion[] rot; public string[] paths;
            public Transform[][] joints,drivers; public Vector3[] feet; public Quaternion[] footRot;
            public PositionConstraint[] pc; public RotationConstraint[] rc;
            public float height,step,lift;
            public Rig(KD_DogDef def,Scene scene)
            {
                var src=KD_SceneObjects.Find(def.DogName);if(src==null)throw new Exception(def.DogName+" missing");
                dog=Object.Instantiate(src);SceneManager.MoveGameObjectToScene(dog,scene);
                dog.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);dog.SetActive(true);
                foreach(var a in dog.GetComponentsInChildren<Animator>(true))a.enabled=false;
                var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(def.idle.guid));
                idle.SampleAnimation(dog,0);rig=KH_DogRig.Resolve(dog,"KD_FetchAnchor_Mouth");
                if(!rig.Ok)throw new Exception(def.key+": "+string.Join(";",rig.problems));
                pc=dog.GetComponentsInChildren<PositionConstraint>(true);rc=dog.GetComponentsInChildren<RotationConstraint>(true);Apply();
                // Only the known authored driver bindings, not every helper / PhysBone under the armature.
                paths=AnimationUtility.GetCurveBindings(KD_CallPoseEditor.SitClip(def)).Where(b=>b.type==typeof(Transform)).Select(b=>b.path).Distinct().ToArray();
                bones=paths.Select(p=>dog.transform.Find(p)).ToArray();
                pos=bones.Select(t=>t.localPosition).ToArray();rot=bones.Select(t=>t.localRotation).ToArray();
                joints=Legs.Select(l=>l.Select(n=>rig.Bone(n)).ToArray()).ToArray();
                drivers=Legs.Select(l=>l.Select(n=>rig.Driver(n)).ToArray()).ToArray();
                feet=joints.Select(l=>l[3].position).ToArray();footRot=joints.Select(l=>l[3].rotation).ToArray();
                height=rig.Bone("Bone").position.y-feet.Min(p=>p.y);
                step=Mathf.Clamp(height*.28f,.055f,.16f);lift=Mathf.Clamp(height*.28f,.045f,.14f);
            }
            public void Apply()
            {
                foreach(var c in pc)if(c.constraintActive&&c.weight>.99f&&c.sourceCount==1)
                    c.transform.position=c.GetSource(0).sourceTransform.position+c.transform.parent.TransformVector(c.translationOffset);
                foreach(var c in rc)if(c.constraintActive&&c.weight>.99f&&c.sourceCount==1)
                    c.transform.rotation=c.GetSource(0).sourceTransform.rotation*Quaternion.Euler(c.rotationOffset);
            }
            public void Reset(){for(int i=0;i<bones.Length;i++){bones[i].localPosition=pos[i];bones[i].localRotation=rot[i];}Apply();}
            public void Rotate(string bone,Vector3 axis,float angle)
            {var d=rig.Driver(bone);d.rotation=Quaternion.AngleAxis(angle,axis)*d.rotation;Apply();}
            public float Solve(int leg,Vector3 target)
            {
                // CCD with small, bounded rotations; start from the standing reference each frame.
                for(int n=0;n<28;n++)
                {
                    for(int j=2;j>=0;j--)
                    {
                        var origin=joints[leg][j].position;
                        var from=joints[leg][3].position-origin;var to=target-origin;
                        // Elbow/knee and wrist/hock bend in the sagittal plane. Only the hip/shoulder swings sideways.
                        var q=j==0?Quaternion.FromToRotation(from,to):Quaternion.AngleAxis(Vector3.SignedAngle(Vector3.ProjectOnPlane(from,Vector3.right),Vector3.ProjectOnPlane(to,Vector3.right),Vector3.right),Vector3.right);
                        q=Quaternion.RotateTowards(Quaternion.identity,q,12f);
                        var driver=drivers[leg][j];driver.rotation=q*driver.rotation;
                        int index=Array.IndexOf(bones,driver);if(index>=0)driver.localRotation=Quaternion.RotateTowards(rot[index],driver.localRotation,j==0?35f:65f);
                        Apply();
                    }
                    if(Vector3.Distance(joints[leg][3].position,target)<.001f)break;
                }
                var correction=footRot[leg]*Quaternion.Inverse(joints[leg][3].rotation);
                drivers[leg][3].rotation=correction*drivers[leg][3].rotation;Apply();
                return Vector3.Distance(joints[leg][3].position,target);
            }
            public void Dispose(){if(dog!=null)Object.DestroyImmediate(dog);}
        }
        static float Smooth(float v)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(v));
        static float Pulse(float v)=>v<=0||v>=1?0:Mathf.Sin(v*Mathf.PI);
        static float Pose(Rig c,string move,float time)
        {
            c.Reset();float length=Duration(move),t=Mathf.Clamp(time,0,length);
            var targets=(Vector3[])c.feet.Clone();float envelope=Smooth(t/.22f)*Smooth((length-t)/.22f);
            if(move=="Groove")
            {
                // Four planted steps right, four back. Every supporting paw has a fixed world goal.
                for(int l=0;l<4;l++)
                {
                    int order=l==0?1:l==1?0:l==2?3:2;
                    float a=(t-order*.625f)/.3125f,b=(t-(4+order)*.625f)/.3125f;
                    targets[l].x+=c.step*(Smooth(a)-Smooth(b));
                    targets[l].y+=c.lift*(Pulse(a)+Pulse(b));
                }
                float mean=targets.Select((p,i)=>p.x-c.feet[i].x).Average();
                float beat=Pulse(Mathf.Repeat(t,.625f)/.625f);
                var shift=new Vector3(mean,-c.height*(.035f+.055f*beat)*envelope,0);
                c.rig.AnimationRoot.position+=shift;if(c.rig.SiblingTail!=null)c.rig.SiblingTail.position+=shift;c.Apply();
                float accent=Smooth((t-.8f)/.3f)-2f*Smooth((t-2.9f)/.3f)+Smooth((t-4.55f)/.3f);
                c.Rotate("Bone.002",Vector3.forward,-8f*accent);
                c.Rotate("Bone.003",Vector3.up,22f*accent);
                c.Rotate("Bone.004",Vector3.right,-10f*Mathf.Abs(accent));
            }
            else if(move=="Reply")
            {
                float a=t/.625f,b=(t-1.25f)/.625f;
                targets[1]+=new Vector3(0,c.lift*1.4f*Pulse(a),c.step*.7f*Pulse(a));
                targets[0]+=new Vector3(0,c.lift*1.4f*Pulse(b),c.step*.7f*Pulse(b));
                var shift=new Vector3(c.step*.2f*(Pulse(a)-Pulse(b)),-c.height*.035f*envelope,0);
                c.rig.AnimationRoot.position+=shift;if(c.rig.SiblingTail!=null)c.rig.SiblingTail.position+=shift;c.Apply();
                c.Rotate("Bone.003",Vector3.right,12f*(Pulse(a)+Pulse(b)));
                c.Rotate("Bone.004",Vector3.forward,12f*(Pulse(a)-Pulse(b)));
            }
            else if(move=="Hop")
            {
                float air=Pulse((t-.625f)/.625f),crouch=Pulse(t/.625f)+Pulse((t-1.25f)/.625f);
                float jump=Mathf.Clamp(c.height*.40f,.09f,.20f)*air;
                var shift=new Vector3(0,jump-c.height*.16f*crouch,0);
                c.rig.AnimationRoot.position+=shift;if(c.rig.SiblingTail!=null)c.rig.SiblingTail.position+=shift;c.Apply();
                c.Rotate("Bone.003",Vector3.right,-16f*air+10f*crouch);c.Rotate("Bone.004",Vector3.right,-8f*air);
                for(int l=0;l<4;l++){targets[l].y+=jump;if(l<2){targets[l].y+=c.lift*.65f*air;targets[l].z-=c.step*.4f*air;}}
            }
            else if(move=="Bow")
            {
                float bow=Pulse(t/length);var shift=new Vector3(0,-c.height*.1f*bow,0);
                c.rig.AnimationRoot.position+=shift;if(c.rig.SiblingTail!=null)c.rig.SiblingTail.position+=shift;c.Apply();
                c.Rotate("Bone.002",Vector3.right,10f*bow);c.Rotate("Bone.003",Vector3.right,17f*bow);
                c.Rotate("Bone.004",Vector3.right,-7f*bow);
            }
            else
            {
                float look=Smooth(t/.45f)*Smooth((length-t)/.45f)*(move=="LookLeft"?-1:1);
                c.Rotate("Bone.003",Vector3.up,32f*look);c.Rotate("Bone.004",Vector3.forward,-9f*look);
                c.Rotate("Bone.002",Vector3.up,6f*look);
            }
            if(c.rig.SiblingTail!=null){var tail=c.rig.SiblingTail;tail.rotation=Quaternion.AngleAxis(22f*Mathf.Sin(t*Mathf.PI/.625f)*envelope,Vector3.up)*tail.rotation;c.Apply();}
            float error=0;for(int l=0;l<4;l++)error=Mathf.Max(error,c.Solve(l,targets[l]));return error;
        }
        [MenuItem("Tools/KominkaHillside/Parade/20 - Build Dance Clips")]
        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit only");
            Directory.CreateDirectory(Dir);Directory.CreateDirectory(Evidence);
            var scene=EditorSceneManager.NewPreviewScene();var log=new StringBuilder();
            try
            {
                foreach(var def in KD_DogDefs.All)using(var c=new Rig(def,scene))foreach(var move in Moves)
                {
                    var clip=new AnimationClip{name="KH_Parade"+move+"_"+def.key,frameRate=32};
                    var curves=new AnimationCurve[c.bones.Length*7];for(int i=0;i<curves.Length;i++)curves[i]=new AnimationCurve();
                    int count=Mathf.RoundToInt(Duration(move)*32);float maxError=0;
                    for(int f=0;f<=count;f++)
                    {
                        float t=f/32f;maxError=Mathf.Max(maxError,Pose(c,move,t));
                        for(int i=0;i<c.bones.Length;i++)
                        {
                            var p=c.bones[i].localPosition;var q=c.bones[i].localRotation;
                            if(f>0){var old=new Quaternion(curves[i*7+3].keys[f-1].value,curves[i*7+4].keys[f-1].value,curves[i*7+5].keys[f-1].value,curves[i*7+6].keys[f-1].value);if(Quaternion.Dot(old,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);}
                            float[] v={p.x,p.y,p.z,q.x,q.y,q.z,q.w};for(int j=0;j<7;j++)curves[i*7+j].AddKey(t,v[j]);
                        }
                    }
                    for(int i=0;i<c.bones.Length;i++)for(int j=0;j<7;j++)
                    {
                        var curve=curves[i*7+j];var keys=curve.keys;
                        if(keys.All(k=>Mathf.Abs(k.value-keys[0].value)<1e-6f))curve=new AnimationCurve(new Keyframe(0,keys[0].value),new Keyframe(Duration(move),keys[0].value));
                        for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
                        AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(c.paths[i],typeof(Transform),j<3?"m_LocalPosition."+"xyz"[j]:"m_LocalRotation."+"xyzw"[j-3]),curve);
                    }
                    clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
                    string path=PathFor(def.key,move);var oldClip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if(oldClip==null)AssetDatabase.CreateAsset(clip,path);else{EditorUtility.CopySerialized(clip,oldClip);Object.DestroyImmediate(clip);EditorUtility.SetDirty(oldClip);}
                    log.AppendLine(def.key+" "+move+" IK goal max error="+maxError.ToString("F5")+"m height="+c.height.ToString("F3")+" step="+c.step.ToString("F3")+" twoTier="+c.rig.TwoTier);
                }
                AssetDatabase.SaveAssets();
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);File.WriteAllText(Evidence+"/build.txt",log.ToString());}
            Debug.Log("PARADE_DANCE_BUILD_OK\n"+log);
        }
    }
}
