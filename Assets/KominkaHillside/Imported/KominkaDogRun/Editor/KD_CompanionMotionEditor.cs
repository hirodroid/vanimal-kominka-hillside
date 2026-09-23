using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    // Authoring only: outputs clips for the existing Generic rigs without changing purchased assets.
    public static class KD_CompanionMotionEditor
    {
        const string DirectoryPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/Companion";
        const string Output = "Temp/companion-motions";
        static readonly string[] Names = { "Curl", "BellyUp", "ChinRest", "StretchYawn", "Sit" };
        static AnimationClip Load(KD_ClipRef r) => AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(r.guid));
        static string PathFor(KD_DogDef d, int pose) => DirectoryPath + "/KD_" + Names[pose] + "_" + d.key + ".anim";
        static Transform Driver(KD_DogRig rig, string name)
        {
            var mesh = rig.Bone(name, null);
            if (mesh == null) throw new InvalidOperationException("Missing bone " + name);
            var driver = rig.WriteBone(mesh, name);
            if (driver == null) throw new InvalidOperationException(string.Join(";", rig.problems));
            return driver;
        }
        static void Rotate(KD_DogRig rig, string name, Vector3 axis, float angle)
        {
            var t = Driver(rig, name); t.rotation = Quaternion.AngleAxis(angle, axis) * t.rotation;
        }
        static void Aim(GameObject dog,KD_DogRig rig,string joint,string child,Vector3 direction)
        {
            KD_DogPreviewEditor.ApplyConstraints(dog);
            var from=rig.Bone(child,null).position-rig.Bone(joint,null).position;
            var driver=Driver(rig,joint);
            driver.rotation=Quaternion.FromToRotation(from,direction)*driver.rotation;
            KD_DogPreviewEditor.ApplyConstraints(dog);
        }
        static void Face(KD_DogRig rig, float closed, float jaw)
        {
            if (rig.face != null && rig.metoziIndex >= 0) rig.face.SetBlendShapeWeight(rig.metoziIndex, closed);
            if (rig.face != null && rig.egaoIndex >= 0) rig.face.SetBlendShapeWeight(rig.egaoIndex, 0);
            if (jaw != 0) Rotate(rig, "Bone.006", Vector3.right, jaw);
        }
        static void CurlForelegs(GameObject dog,KD_DogRig rig,KD_DogDef def)
        {
            var standing=Clone(def,dog.scene);
            try
            {
                Load(def.idle).SampleAnimation(standing,0);KD_DogPreviewEditor.ApplyConstraints(standing);
                var reference=KD_DogRig.Resolve(standing);
                var forward=Vector3.ProjectOnPlane(rig.nose.position-rig.head.position,Vector3.up).normalized;
                var lateral=Vector3.Cross(Vector3.up,forward);
                var underChin=(rig.head.position+rig.nose.position)*.5f;
                var shoulderMid=(rig.Bone("Bone.011_L",null).position+rig.Bone("Bone.011_R",null).position)*.5f;
                foreach(string side in new[]{"L","R"})
                {
                    string name="Bone.014_"+side;
                    var shoulder=rig.Bone("Bone.011_"+side,null);
                    float sign=Mathf.Sign(Vector3.Dot(shoulder.position-shoulderMid,lateral));
                    var target=underChin-lateral*sign*rig.headScale*.20f;
                    target.y=rig.Bone(name,null).position.y+(side=="L"?rig.headScale*.12f:0);
                    // Bring the forelegs around with the face; swap their sides slightly beneath the chin.
                    Foot(dog,rig,side,target,-forward*.6f+lateral*sign,Mathf.Min(shoulder.position.y,target.y)+rig.bodyScale*.015f);
                    var toeDirection=(forward-lateral*sign*.35f).normalized;
                    var desired=Quaternion.FromToRotation(Vector3.forward,toeDirection)*reference.Bone(name,null).rotation;
                    var driver=Driver(rig,name);
                    driver.rotation=desired*Quaternion.Inverse(rig.Bone(name,null).rotation)*driver.rotation;
                    KD_DogPreviewEditor.ApplyConstraints(dog);
                }
            }
            finally{Object.DestroyImmediate(standing);}
        }
        static void PitchHead(GameObject dog,KD_DogRig rig,float angle)
        {
            KD_DogPreviewEditor.ApplyConstraints(dog);
            var forward=rig.nose.position-rig.head.position;forward.y=0;
            var axis=Vector3.Cross(Vector3.up,forward).normalized;
            Rotate(rig,"Bone.004",axis,angle);KD_DogPreviewEditor.ApplyConstraints(dog);
        }
        static void Foot(GameObject dog,KD_DogRig rig,string side,Vector3 target,Vector3? bendDirection=null,float? elbowHeight=null)
        {
            KD_DogPreviewEditor.ApplyConstraints(dog);
            var shoulder=rig.Bone("Bone.011_"+side,null);
            var elbow=rig.Bone("Bone.012_"+side,null);
            var wrist=rig.Bone("Bone.014_"+side,null);
            float upper=Vector3.Distance(shoulder.position,elbow.position);
            float lower=Vector3.Distance(elbow.position,wrist.position);
            var direction=(target-shoulder.position).normalized;
            float distance=Mathf.Clamp(Vector3.Distance(target,shoulder.position),Mathf.Abs(upper-lower)+.0001f,upper+lower-.0001f);
            var bend=Vector3.ProjectOnPlane(bendDirection??(elbow.position-shoulder.position),direction);
            if(bend.sqrMagnitude<.000001f)bend=Vector3.ProjectOnPlane(Vector3.back,direction);
            float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
            float radius=Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            var center=shoulder.position+direction*along;
            var bendUnit=bend.normalized;
            if(elbowHeight.HasValue && radius>.0001f)
            {
                // Pick the low part of the IK elbow circle so folding under the chin does not raise the elbow.
                var vertical=Vector3.ProjectOnPlane(Vector3.up,direction).normalized;
                var horizontal=Vector3.Cross(direction,vertical).normalized;
                if(vertical.y>.001f)
                {
                    float v=Mathf.Clamp((elbowHeight.Value-center.y)/(radius*vertical.y),-.98f,.98f);
                    bendUnit=vertical*v+horizontal*Mathf.Sqrt(1-v*v)*(Vector3.Dot(bend,horizontal)<0?-1:1);
                }
            }
            var desiredElbow=center+bendUnit*radius;
            var desiredWrist=shoulder.position+direction*distance;
            var upperDriver=Driver(rig,"Bone.011_"+side);
            upperDriver.rotation=Quaternion.FromToRotation(elbow.position-shoulder.position,desiredElbow-shoulder.position)*upperDriver.rotation;
            KD_DogPreviewEditor.ApplyConstraints(dog);
            var lowerDriver=Driver(rig,"Bone.012_"+side);
            lowerDriver.rotation=Quaternion.FromToRotation(wrist.position-elbow.position,desiredWrist-elbow.position)*lowerDriver.rotation;
            KD_DogPreviewEditor.ApplyConstraints(dog);
        }
        static void Shape(GameObject dog, KD_DogRig rig, string breed, int pose, float strength, float yawn)
        {
            var root = rig.srcSkelRoot.Find("Bone");
            if (root == null) throw new InvalidOperationException("No animation root");
            if (pose == 0)
            {
                // Authoring values, not physiological joint limits. Share the curve across the trunk.
                bool roundedTrunk=breed=="Lab"||breed=="Samoyed";
                float lumbar=roundedTrunk?60:breed=="Dachs"?48:breed=="Pome"?42:50;
                float chest=roundedTrunk?60:breed=="Dachs"?24:breed=="Pome"?22:28;
                Rotate(rig,"Bone.001",Vector3.up,lumbar);
                Rotate(rig,"Bone.002",Vector3.up,chest);
                KD_DogPreviewEditor.ApplyConstraints(dog);
                var headTarget=rig.Bone("Bone.005_R",null).position+Vector3.right*(rig.headScale*.8f);
                var neckDirection=headTarget+Vector3.right*(rig.headScale*1.5f)-rig.Bone("Bone.003",null).position;
                neckDirection.y=-rig.headScale*.18f;
                if(roundedTrunk)
                {
                    // Continue the trunk's arc instead of aiming the neck straight back at the hips.
                    var trunkForward=Vector3.ProjectOnPlane(rig.Bone("Bone.003",null).position-rig.Bone("Bone.002",null).position,Vector3.up).normalized;
                    neckDirection=Quaternion.AngleAxis(60,Vector3.up)*trunkForward;
                    neckDirection.y=-.10f;
                }
                Aim(dog,rig,"Bone.003","Bone.004",neckDirection);
                foreach(var side in new[]{"L","R"})
                {
                    var back=rig.Bone("Bone.005_"+side,null).position-rig.Bone("Bone.011_"+side,null).position;
                    back.y=-rig.bodyScale*.08f;
                    Aim(dog,rig,"Bone.011_"+side,"Bone.012_"+side,back);
                    var front=rig.head.position-rig.Bone("Bone.012_"+side,null).position;
                    front.y=-rig.bodyScale*.03f;
                    Aim(dog,rig,"Bone.012_"+side,"Bone.013_"+side,front);
                    Aim(dog,rig,"Bone.013_"+side,"Bone.014_"+side,front);
                    var knee=rig.Bone("Bone.011_"+side,null).position-rig.Bone("Bone.005_"+side,null).position;
                    knee.y=-rig.bodyScale*.06f;
                    Aim(dog,rig,"Bone.005_"+side,"Bone.010_"+side,knee);
                    var hock=rig.Bone("Bone",null).position-rig.Bone("Bone.010_"+side,null).position;
                    hock.y=-rig.bodyScale*.04f;
                    Aim(dog,rig,"Bone.010_"+side,"Bone.015_"+side,hock);
                    knee.y=-rig.bodyScale*.02f;
                    Aim(dog,rig,"Bone.015_"+side,"Bone.016_"+side,knee);
                }
                KD_DogPreviewEditor.ApplyConstraints(dog);
                var headDriver=Driver(rig,"Bone.004");
                var towardBelly=headTarget-rig.head.position;
                towardBelly.y=-rig.headScale*.3f;
                if(roundedTrunk)
                {
                    towardBelly=Quaternion.AngleAxis(45,Vector3.up)*Vector3.ProjectOnPlane(neckDirection,Vector3.up).normalized;
                    towardBelly.y=-.20f;
                }
                var tuck=Quaternion.FromToRotation(rig.nose.position-rig.head.position,towardBelly);
                headDriver.rotation=tuck*headDriver.rotation;
                PitchHead(dog,rig,2);
                var tail=rig.srcSkelRoot.Find("Bone.018")??rig.Bone("Bone.018",null);
                bool curledTail=breed=="Shiba"||breed=="Samoyed"||breed=="Pome";
                tail.rotation=Quaternion.AngleAxis(-65,Vector3.up)*tail.rotation;
                KD_DogPreviewEditor.ApplyConstraints(dog);
                var t19=rig.Bone("Bone.019",null);var t20=rig.Bone("Bone.020",null);var t21=rig.Bone("Bone.021",null);
                if(curledTail&&t19!=null&&t20!=null&&t21!=null)
                {
                    var normal=Vector3.Cross(t20.position-t19.position,t21.position-t20.position);
                    if(normal.sqrMagnitude>1e-10f)tail.rotation=Quaternion.FromToRotation(normal,normal.y>=0?Vector3.up:Vector3.down)*tail.rotation;
                }
                tail.rotation=Quaternion.AngleAxis(-85,Vector3.up)*tail.rotation;
                Face(rig,100,0);
            }
            else if (pose == 1)
            {
                // Keep the native standing joint chain: independent world-space aims twisted the short limbs.
                var roll=Quaternion.AngleAxis(170,Vector3.forward);
                root.rotation=roll*root.rotation;
                var tail=rig.srcSkelRoot.Find("Bone.018");
                if(tail!=null)
                {
                    tail.position=root.position+roll*(tail.position-root.position);
                    tail.rotation=roll*tail.rotation;
                }
                KD_DogPreviewEditor.ApplyConstraints(dog);
                if((breed=="Pome"||breed=="Shiba"||breed=="Samoyed") && tail!=null)
                {
                    // Lay the curled tail beside the pelvis, keeping it from lifting the back off the floor.
                    tail.rotation=Quaternion.FromToRotation(rig.Bone("Bone.020",null).position-rig.Bone("Bone.019",null).position,new Vector3(-.6f,0,-1))*tail.rotation;
                    KD_DogPreviewEditor.ApplyConstraints(dog);
                    var a=rig.Bone("Bone.019",null);var b=rig.Bone("Bone.020",null);var c=rig.Bone("Bone.021",null);
                    var normal=Vector3.Cross(b.position-a.position,c.position-b.position);
                    if(normal.sqrMagnitude>1e-10f)tail.rotation=Quaternion.FromToRotation(normal,normal.y>=0?Vector3.up:Vector3.down)*tail.rotation;
                }
                // Extend from the hips as intact chains instead of folding each short segment back.
                foreach(var side in new[]{"L","R"})Rotate(rig,"Bone.005_"+side,Vector3.right,-35);
                // Distribute the inward fold along the foreleg; turn the pads toward the belly.
                var forelegAxis=roll*Vector3.right;
                foreach(var side in new[]{"L","R"})
                {
                    Rotate(rig,"Bone.011_"+side,forelegAxis,15);
                    Rotate(rig,"Bone.012_"+side,forelegAxis,20);
                    Rotate(rig,"Bone.013_"+side,forelegAxis,25);
                    Rotate(rig,"Bone.014_"+side,forelegAxis,30);
                    if(IsLongLegBellyUp(breed))
                    {
                        // Keep shoulder-to-elbow unchanged; bring the distal foreleg toward the abdomen.
                        Rotate(rig,"Bone.012_"+side,forelegAxis,40);
                        // Spread from the hips in opposite directions without twisting individual leg segments.
                        Rotate(rig,"Bone.005_"+side,Vector3.forward,side=="R"?22:-22);
                    }
                }
                // Keep the head near the back support plane, with the cheek resting to one side.
                Aim(dog,rig,"Bone.003","Bone.004",new Vector3(.12f,.06f,1));
                Aim(dog,rig,"Bone.004","Bone.007",new Vector3(.20f,.06f,1));
                Rotate(rig,"Bone.004",new Vector3(.20f,.06f,1).normalized,-60);
                Face(rig,75,0);
            }
            else if (pose == 4)
            {
                if(breed=="Dachs")
                {
                    var tail=rig.srcSkelRoot.Find("Bone.018");
                    if(tail!=null)tail.rotation=Quaternion.AngleAxis(45,Vector3.right)*tail.rotation;
                    KD_DogPreviewEditor.ApplyConstraints(dog);
                }
                // The purchased 17-second motion changes posture. Author a stable seated hold.
                // Straighten the carpal chain before solving planted forepaws, keeping bone lengths.
                foreach(var side in new[]{"L","R"})
                {
                    var paw=rig.Bone("Bone.014_"+side,null);
                    var target=paw.position;
                    if(breed=="Samoyed")target.x=Mathf.Lerp(target.x,rig.Bone("Bone.011_"+side,null).position.x,.85f);
                    var rotation=Driver(rig,"Bone.014_"+side).rotation;
                    Aim(dog,rig,"Bone.013_"+side,"Bone.014_"+side,
                        rig.Bone("Bone.013_"+side,null).position-rig.Bone("Bone.012_"+side,null).position);
                    Foot(dog,rig,side,target);
                    Driver(rig,"Bone.014_"+side).rotation=rotation;
                    KD_DogPreviewEditor.ApplyConstraints(dog);
                }
                if(breed=="Samoyed")PitchHead(dog,rig,6);
                Face(rig,8,0);
            }
            else if (pose == 2)
            {
                Rotate(rig,"Bone.003",Vector3.right,12);
                PitchHead(dog,rig,12);
                Face(rig,40,0);
            }
            else
            {
                var feet=new[]{rig.Bone("Bone.014_L",null).position,rig.Bone("Bone.014_R",null).position};
                var paws=new[]{Driver(rig,"Bone.014_L").rotation,Driver(rig,"Bone.014_R").rotation};
                Rotate(rig,"Bone.001",Vector3.right,24*strength);
                Rotate(rig,"Bone.002",Vector3.right,8*strength);
                Rotate(rig,"Bone.003",Vector3.right,-12*strength);
                Rotate(rig,"Bone.004",Vector3.right,-15*strength);
                KD_DogPreviewEditor.ApplyConstraints(dog);
                foreach (var side in new[]{"L","R"})
                {
                    var foot=feet[side=="L"?0:1];
                    var shoulder=rig.Bone("Bone.011_"+side,null).position;
                    var elbow=rig.Bone("Bone.012_"+side,null).position;
                    var wrist=rig.Bone("Bone.014_"+side,null).position;
                    float reach=.95f*(Vector3.Distance(shoulder,elbow)+Vector3.Distance(elbow,wrist));
                    float drop=shoulder.y-foot.y;
                    float forward=Mathf.Sqrt(Mathf.Max(0,reach*reach-drop*drop));
                    var target=Vector3.Lerp(foot,new Vector3(foot.x,foot.y,shoulder.z+forward),strength);
                    Foot(dog,rig,side,target);
                    Driver(rig,"Bone.014_"+side).rotation=paws[side=="L"?0:1];
                    KD_DogPreviewEditor.ApplyConstraints(dog);
                }
                Face(rig,Mathf.Lerp(15*strength,90,yawn),28*yawn);
            }
            KD_DogPreviewEditor.ApplyConstraints(dog);
            // Place the body on the selected surface, without moving the GameObject used for networking.
            var bounds = BodyBounds(dog);
            var groundOffset=Vector3.up*(.003f-bounds.min.y);
            root.position += groundOffset;
            var siblingTail=rig.srcSkelRoot.Find("Bone.018");
            if(siblingTail!=null && (pose==0||pose==1||pose==4))siblingTail.position+=groundOffset;
            KD_DogPreviewEditor.ApplyConstraints(dog);
        }
        static IEnumerable<SkinnedMeshRenderer> Bodies(GameObject dog)
        {
            return dog.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy
                && !Ancestors(r.transform,dog.transform).Any(t=>t.name.StartsWith("KD_Acc_")));
        }
        static IEnumerable<Transform> Ancestors(Transform t,Transform root)
        { for(var p=t;p!=null && p!=root;p=p.parent)yield return p; }
        static Bounds BodyBounds(GameObject dog)
        {
            bool first=true;var bounds=new Bounds();
            foreach(var r in Bodies(dog))
            {
                var mesh=new Mesh();
                try
                {
                    r.BakeMesh(mesh,true);
                    foreach(var v in mesh.vertices)
                    {
                        var p=r.transform.TransformPoint(v);
                        if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
                    }
                }
                finally{Object.DestroyImmediate(mesh);}
            }
            if(first)throw new InvalidOperationException("No visible body");
            return bounds;
        }
        static GameObject Clone(KD_DogDef def,Scene scene)
        {
            var source=KD_SceneObjects.Find(def.DogName);
            var clone=Object.Instantiate(source);SceneManager.MoveGameObjectToScene(clone,scene);
            clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.SetActive(true);
            return clone;
        }
        static void Key(AnimationClip clip,EditorCurveBinding binding,float time,float value)
        {
            var curve=AnimationUtility.GetEditorCurve(clip,binding)??new AnimationCurve();
            curve.AddKey(new Keyframe(time,value,0,0));AnimationUtility.SetEditorCurve(clip,binding,curve);
        }
        static void Capture(AnimationClip clip,GameObject dog,KD_DogRig rig,float time)
        {
            foreach(var t in rig.srcSkelRoot.GetComponentsInChildren<Transform>(true))
            {
                string path=AnimationUtility.CalculateTransformPath(t,dog.transform);
                var q=t.localRotation;var p=t.localPosition;
                for(int i=0;i<4;i++)Key(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[i]),time,q[i]);
                for(int i=0;i<3;i++)Key(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalPosition."+"xyz"[i]),time,p[i]);
            }
            foreach(var r in Bodies(dog))
            {
                string path=AnimationUtility.CalculateTransformPath(r.transform,dog.transform);
                for(int i=0;i<r.sharedMesh.blendShapeCount;i++)
                    Key(clip,EditorCurveBinding.FloatCurve(path,typeof(SkinnedMeshRenderer),"blendShape."+r.sharedMesh.GetBlendShapeName(i)),time,r.GetBlendShapeWeight(i));
            }
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Companion Motions/1 - Build Clips")]
        public static void Build()
        {
            BuildPoses(new[]{0,1,2,3});
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/2 - Build Three Poses")]
        public static void BuildPolished()
        {
            BuildPoses(new[]{0,1,4});
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Corgi BellyUp/1 - Build Clip")]
        public static void BuildCorgiBellyUp() => BuildPoses(new[]{1},"Corgi");
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Pome BellyUp/1 - Build Clip")]
        public static void BuildPomeBellyUp() => BuildPoses(new[]{1},"Pome");
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/All BellyUp/1 - Build Clips")]
        public static void BuildAllBellyUp() => BuildPoses(new[]{1});
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Remaining BellyUp/1 - Build Clips")]
        public static void BuildRemainingBellyUp() => BuildPoses(new[]{1},null,true);
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Long Legs BellyUp/1 - Build Clips")]
        public static void BuildLongLegBellyUp() => BuildPoses(new[]{1},null,false,true);
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/Curl Paws/1 - Build Clips")]
        public static void BuildCurlPaws()
        {
            BuildPoses(new[]{0},"Lab");BuildPoses(new[]{0},"Samoyed");
        }
        internal static bool IsLongLegBellyUp(string breed) => breed=="Lab"||breed=="Shiba"||breed=="Samoyed";
        internal static bool IsRemainingBellyUp(string breed) => breed=="Lab"||breed=="Dachs"||breed=="Shiba"||breed=="Samoyed";
        static void BuildPoses(int[] poses,string breed=null,bool remainingBellyUp=false,bool longLegBellyUp=false)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");
            Directory.CreateDirectory(DirectoryPath);Directory.CreateDirectory(Output);
            var scene=EditorSceneManager.NewPreviewScene();var log=new StringBuilder();
            try
            {
                foreach(var def in KD_DogDefs.All)foreach(int pose in poses)
                {
                    if(breed!=null && def.key!=breed)continue;
                    if(remainingBellyUp && !IsRemainingBellyUp(def.key))continue;
                    if(longLegBellyUp && !IsLongLegBellyUp(def.key))continue;
                    string path=PathFor(def,pose);var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    var result=new AnimationClip();
                    try
                    {
                    result.name="KD_"+Names[pose]+"_"+def.key;result.frameRate=30;
                    var baseClip=pose==4?Load(def.sit):pose==3||pose==1?Load(def.idle):KD_CallPoseEditor.PoseClip(def,1);
                    var times=pose==3?Enumerable.Range(0,25).Select(i=>i*.25f).ToArray():new[]{0f,2.5f,5f};
                    for(int frame=0;frame<times.Length;frame++)
                    {
                        var dog=Clone(def,scene);
                        try
                        {
                            baseClip.SampleAnimation(dog,0);KD_DogPreviewEditor.ApplyConstraints(dog);
                            var rig=KD_DogRig.Resolve(dog);
                            float time=times[frame];
                            float strength=pose!=3?1:time<1.5f?Mathf.SmoothStep(0,1,time/1.5f):time>4.5f?Mathf.SmoothStep(1,0,(time-4.5f)/1.5f):1;
                            float yawn=pose!=3?0:time<2.7f?Mathf.SmoothStep(0,1,(time-1.8f)/.9f):Mathf.SmoothStep(1,0,(time-3.4f)/.8f);
                            Shape(dog,rig,def.key,pose,strength,yawn);
                            if(pose!=3 && frame==1)
                            {
                                Rotate(rig,"Bone.002",Vector3.right,pose==4?.12f:pose==2?.5f:.22f);
                                KD_DogPreviewEditor.ApplyConstraints(dog);
                            }
                            if(pose==0 && (def.key=="Lab"||def.key=="Samoyed"))CurlForelegs(dog,rig,def);
                            // Preserve clip-specific constraint weights and other authored settings.
                            string skeletonPath=AnimationUtility.CalculateTransformPath(rig.srcSkelRoot,dog.transform);
                            foreach(var b in AnimationUtility.GetCurveBindings(baseClip))
                            {
                                if(b.type==typeof(SkinnedMeshRenderer))continue;
                                if(b.type==typeof(Transform) && (pose==2||pose==3))continue;
                                if(b.type==typeof(Transform) && (b.path==skeletonPath||b.path.StartsWith(skeletonPath+"/",StringComparison.Ordinal)))continue;
                                if(b.path.Length==0)throw new InvalidOperationException("Base clip animates network root: "+baseClip.name);
                                Key(result,b,times[frame],AnimationUtility.GetEditorCurve(baseClip,b).Evaluate(0));
                            }
                            Capture(result,dog,rig,times[frame]);
                        }
                        finally{Object.DestroyImmediate(dog);}
                    }
                    result.EnsureQuaternionContinuity();
                    foreach(var binding in AnimationUtility.GetCurveBindings(result))
                    {
                        var curve=AnimationUtility.GetEditorCurve(result,binding);
                        for(int k=1;k<curve.length-1;k++)curve.SmoothTangents(k,0);
                        AnimationUtility.SetEditorCurve(result,binding,curve);
                    }
                    var settings=AnimationUtility.GetAnimationClipSettings(result);
                    settings.loopTime=pose!=3;AnimationUtility.SetAnimationClipSettings(result,settings);EditorUtility.SetDirty(result);
                    log.AppendLine(path+" length="+result.length+" curves="+AnimationUtility.GetCurveBindings(result).Length);
                    if(existing==null){AssetDatabase.CreateAsset(result,path);result=null;}
                    else {EditorUtility.CopySerialized(result,existing);EditorUtility.SetDirty(existing);}
                    }
                    finally{if(result!=null)Object.DestroyImmediate(result);}
                }
                AssetDatabase.SaveAssets();File.WriteAllText(Output+"/build.txt",log.ToString());
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Companion Motions/2 - Render")]
        public static void Render()
        {
            Directory.CreateDirectory(Output);var scene=EditorSceneManager.NewPreviewScene();
            try{foreach(var d in KD_DogDefs.All)for(int p=0;p<4;p++)RenderOne(d,p,scene);}
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Companion Motions/3 - Verify Clips")]
        public static void Verify()
        {
            VerifyPoses(new[]{0,1,2,3},"verify.txt");
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Pose Polish/4 - Verify Clips")]
        public static void VerifyPolished()
        {
            VerifyPoses(new[]{0,1,4},"verify-polished.txt");
        }
        static void VerifyPoses(int[] poses,string reportName)
        {
            Directory.CreateDirectory(Output);var log=new StringBuilder();int failures=0,count=0;
            void Check(bool ok,string message){log.AppendLine((ok?"PASS ":"FAIL ")+message);if(!ok)failures++;}
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                foreach(var def in KD_DogDefs.All)foreach(int pose in poses)
                {
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(PathFor(def,pose));string label=def.key+"/"+Names[pose];
                    Check(clip!=null,label+" exists");if(clip==null)continue;count++;
                    Check(Mathf.Abs(clip.length-(pose==3?6:5))<.01f,label+" duration");
                    Check(clip.isLooping==(pose!=3),label+" looping");
                    var dog=Clone(def,scene);
                    try
                    {
                        foreach(var b in AnimationUtility.GetCurveBindings(clip))
                        {
                            var curve=AnimationUtility.GetEditorCurve(clip,b);
                            Check(b.path.Length>0 && dog.transform.Find(b.path)!=null,label+" binding "+b.path+"/"+b.propertyName);
                            Check(curve.keys.All(k=>!float.IsNaN(k.value)&&!float.IsInfinity(k.value)),label+" finite curve");
                            if(pose!=3)Check(Mathf.Abs(curve.Evaluate(0)-curve.Evaluate(clip.length))<.001f,label+" loop curve endpoints");
                        }
                        float minY=100,maxY=-100,pawDrift=0;Bounds first=new Bounds(),last=new Bounds();
                        Vector3 left=Vector3.zero,right=Vector3.zero;
                        var rig=KD_DogRig.Resolve(dog);
                        const int samples=48;
                        for(int i=0;i<=samples;i++)
                        {
                            clip.SampleAnimation(dog,clip.length*i/samples);KD_DogPreviewEditor.ApplyConstraints(dog);
                            var bounds=BodyBounds(dog);minY=Mathf.Min(minY,bounds.min.y);maxY=Mathf.Max(maxY,bounds.min.y);if(i==0)first=bounds;if(i==samples)last=bounds;
                            if(pose==4)
                            {
                                var l=rig.Bone("Bone.014_L",null).position;var r=rig.Bone("Bone.014_R",null).position;
                                if(i==0){left=l;right=r;}
                                pawDrift=Mathf.Max(pawDrift,Vector3.Distance(left,l),Vector3.Distance(right,r));
                            }
                        }
                        Check(minY>=-.015f,label+" lowest surface Y="+minY.ToString("F4"));
                        if(pose!=3)Check(maxY<=.015f && maxY-minY<=.01f,label+" support height range="+minY.ToString("F4")+".."+maxY.ToString("F4"));
                        if(pose==4)Check(pawDrift<.005f,label+" forepaw drift="+pawDrift.ToString("F4"));
                        Check(Vector3.Distance(first.center,last.center)<.001f && Vector3.Distance(first.size,last.size)<.001f,label+" matching endpoints");
                    }
                    finally{Object.DestroyImmediate(dog);}
                }
                Check(count==6*poses.Length,"expected motion clip count");log.AppendLine("RESULT clips="+count+" failures="+failures);
                File.WriteAllText(Output+"/"+reportName,log.ToString());if(failures>0)Debug.LogError("Companion motion verification failures="+failures);
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void RenderOne(KD_DogDef def,int pose,Scene scene)
        {
            RenderClip(def, AssetDatabase.LoadAssetAtPath<AnimationClip>(PathFor(def,pose)), pose==3?2.7f:0, Names[pose], Output, scene);
        }
        internal static void RenderClip(KD_DogDef def,AnimationClip clip,float time,string label,string output,Scene scene,bool faceClose=false)
        {
            Directory.CreateDirectory(output);
            var owned=new List<Object>();
            T Own<T>(T value) where T:Object {owned.Add(value);return value;}
            try
            {
            var dog=Own(Clone(def,scene));
            clip.SampleAnimation(dog,time);KD_DogPreviewEditor.ApplyConstraints(dog);
            {
                var rig=KD_DogRig.Resolve(dog);var report=new StringBuilder();
                foreach(string name in new[]{"Bone","Bone.001","Bone.002","Bone.003","Bone.004","Bone.007","Bone.005_R","Bone.010_R","Bone.015_R","Bone.016_R","Bone.011_R","Bone.012_R","Bone.013_R","Bone.014_R","Bone.018","Bone.019","Bone.020"})
                {
                    var bone=rig.Bone(name,null);if(bone!=null)report.AppendLine(name+" pos="+bone.position.ToString("F4")+" rot="+bone.rotation.eulerAngles.ToString("F1"));
                }
                report.AppendLine("noseAim="+(rig.nose.position-rig.head.position).normalized.ToString("F4"));
                File.WriteAllText(output+"/"+def.key+"-"+label+"-bones.txt",report.ToString());
            }
            var display=Own(new GameObject("MotionRender"));SceneManager.MoveGameObjectToScene(display,scene);
            var mat=Own(new Material(Shader.Find("Standard")));mat.color=new Color(.64f,.43f,.23f);mat.SetFloat("_Glossiness",.12f);
            var bounds=BodyBounds(dog);
            if(faceClose)
            {
                var faceRig=KD_DogRig.Resolve(dog);
                bounds=new Bounds((faceRig.head.position+faceRig.nose.position)*.5f,Vector3.one*faceRig.headScale*2.6f);
            }
            foreach(var r in Bodies(dog))
            {
                var mesh=Own(new Mesh());r.BakeMesh(mesh,true);
                var v=mesh.vertices;var n=mesh.normals;var nm=r.transform.localToWorldMatrix.inverse.transpose;
                for(int i=0;i<v.Length;i++)v[i]=r.transform.TransformPoint(v[i]);
                for(int i=0;i<n.Length;i++)n[i]=nm.MultiplyVector(n[i]).normalized;
                mesh.vertices=v;mesh.normals=n;mesh.RecalculateBounds();
                var obj=new GameObject("Body");obj.transform.SetParent(display.transform,false);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterials=faceClose?r.sharedMaterials:Enumerable.Repeat(mat,mesh.subMeshCount).ToArray();
            }
            dog.SetActive(false);
            var camGo=Own(new GameObject("Camera"));SceneManager.MoveGameObjectToScene(camGo,scene);var cam=camGo.AddComponent<Camera>();cam.enabled=false;cam.scene=scene;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.18f,.20f,.22f);cam.orthographic=true;cam.useOcclusionCulling=false;
            cam.orthographicSize=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z)*.64f;cam.nearClipPlane=.01f;cam.farClipPlane=20;
            var lightGo=Own(new GameObject("Light"));SceneManager.MoveGameObjectToScene(lightGo,scene);var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;lightGo.transform.rotation=Quaternion.Euler(45,-35,0);
            var floor=Own(GameObject.CreatePrimitive(PrimitiveType.Plane));SceneManager.MoveGameObjectToScene(floor,scene);floor.transform.localScale=Vector3.one*.3f;
            var floorMat=Own(new Material(Shader.Find("Standard")));floorMat.color=new Color(.30f,.32f,.33f);floor.GetComponent<Renderer>().sharedMaterial=floorMat;
            var rt=Own(new RenderTexture(640,540,24));var tex=Own(new Texture2D(640,540,TextureFormat.RGB24,false));var previous=RenderTexture.active;
            try
            {
                for(int angle=0;angle<3;angle++)
                {
                    cam.transform.position=bounds.center+(angle==0?new Vector3(1.4f,1.2f,2f):angle==1?new Vector3(2f,.35f,0):new Vector3(0,2.8f,.35f));
                    cam.transform.LookAt(bounds.center);cam.aspect=640f/540f;
                    // Explicit matrices keep repeated Edit-mode renders independent of the last camera view.
                    cam.worldToCameraMatrix=Matrix4x4.Scale(new Vector3(1,1,-1))*cam.transform.worldToLocalMatrix;
                    float halfHeight=cam.orthographicSize,halfWidth=halfHeight*cam.aspect;
                    cam.projectionMatrix=Matrix4x4.Ortho(-halfWidth,halfWidth,-halfHeight,halfHeight,cam.nearClipPlane,cam.farClipPlane);
                    cam.cullingMatrix=cam.projectionMatrix*cam.worldToCameraMatrix;
                    cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
                    tex.ReadPixels(new Rect(0,0,640,540),0,0);tex.Apply();File.WriteAllBytes(output+"/"+def.key+"-"+label+"-"+angle+".png",tex.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active=previous;cam.targetTexture=null;
            }
            }
            finally{for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)Object.DestroyImmediate(owned[i]);}
        }
    }
}
