using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KominkaHillside.Editor
{
    // Show 004 only: baked Transform choreography, no runtime MonoBehaviour or new Udon loop.
    public static class KH_ParadeStoryCast
    {
        const string Folder="Assets/KominkaHillside/Data/HalloweenParade/Companions";
        const string Input="docs/plans/halloween-parade/companions.json";
        [Serializable] public class Window {public float start,end;}
        [Serializable] public class Sample {public float t,yaw;public float[] p;}
        [Serializable] public class Actor {public string id,type;public int side;public Window[] windows;public Sample[] samples;}
        [Serializable] public class Slot {public int canonical;public float[] p;}
        [Serializable] public class Plan {public string schema;public float duration,sampleStep;public Actor[] actors;public Slot[] finale;}
        static Vector3 V(float[] p)=>new Vector3(p[0],p[1],p[2]);
        static void Guard(){if(EditorApplication.isPlayingOrWillChangePlaymode||UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Saved Edit scene required");}
        static AnimationCurve Linear(IEnumerable<Keyframe> keys){var c=new AnimationCurve(keys.ToArray());for(int i=0;i<c.length;i++){AnimationUtility.SetKeyLeftTangentMode(c,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(c,i,AnimationUtility.TangentMode.Linear);}return c;}
        static Transform Node(string name,Transform parent,Vector3 p){var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=p;return t;}
        static Transform MeshPart(string meshName,string name,Transform parent,Vector3 p,Material material)
        {
            var mesh=AssetDatabase.LoadAllAssetsAtPath(Folder+"/"+meshName+".fbx").OfType<Mesh>().Single();
            var t=Node(name,parent,p);t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=t.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;return t;
        }
        static float Ground(Transform stage,Vector3 local,float clearance)
        {
            var world=stage.TransformPoint(local);var hits=Physics.RaycastAll(world+Vector3.up*5,Vector3.down,12,~0,QueryTriggerInteraction.Ignore);
            foreach(var hit in hits.OrderBy(h=>h.distance))
            {
                string n=hit.collider.name.ToLowerInvariant();
                if(hit.collider is TerrainCollider||n.Contains("ground")||n.Contains("terrain")||n.Contains("coursemesh"))return stage.InverseTransformPoint(hit.point).y+clearance;
            }
            throw new Exception("No ground beneath companion at "+world);
        }
        [MenuItem("Tools/KominkaHillside/Parade/30 - Build Story Cast")]
        public static void Build()
        {
            Guard();var p=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();var stage=p.stageRoot.transform;var director=p.director;var timeline=(TimelineAsset)director.playableAsset;
            var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(Input));if(plan.schema!="kh-parade-companions/1"||plan.actors.Length!=18)throw new Exception("Unexpected cast plan");
            // Complete inputs and terrain before touching the saved cast/clip. Build curves in memory.
            string[] expected={"TreeBody","TreeArmL","TreeArmR","TreeFootL","TreeFootR","LampBody","LampHead","LampFootL","LampFootR","HatBody","BatBody","BatWingL","BatWingR"};
            foreach(string meshName in expected)if(AssetDatabase.LoadAllAssetsAtPath(Folder+"/"+meshName+".fbx").OfType<Mesh>().Count()!=1)throw new Exception("Missing/ambiguous mesh "+meshName);
            var grounded=new Dictionary<string,Vector3[]>();
            foreach(var actor in plan.actors){var points=actor.samples.Select(s=>V(s.p)).ToArray();if(actor.type=="Tree"||actor.type=="Lamp")for(int i=0;i<points.Length;i++)points[i].y=Ground(stage,points[i],actor.type=="Tree"?.20f:.25f);grounded.Add(actor.id,points);}
            string clipPath=Folder+"/KH_StoryCast.anim";var savedAnimation=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);var animation=new AnimationClip{name="KH_StoryCast",frameRate=32};
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/KH_Companions.mat");
            if(material==null){material=new Material(Shader.Find("KominkaHillside/Halloween Story Prop"));AssetDatabase.CreateAsset(material,Folder+"/KH_Companions.mat");}
            material.shader=Shader.Find("KominkaHillside/Halloween Story Prop");material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/KH_Companions_Atlas.png");material.SetColor("_Color",Color.white);EditorUtility.SetDirty(material);
            var root=Node("StoryCast_Build",stage,Vector3.zero);int renderers=0;var bindings=new List<KeyValuePair<Actor,GameObject>>();
            try{
            foreach(var actor in plan.actors)
            {
                var a=Node(actor.id,root,Vector3.zero);var body=Node("Pose",a,Vector3.zero);string basePath="StoryCast/"+actor.id;float size=actor.type=="Bat"?.7f:actor.type=="Tree"?1.18f:actor.type=="Hat"?.65f:1.05f;a.localScale=Vector3.one*size;
                MeshPart(actor.type+"Body","Body",body,Vector3.zero,material);
                if(actor.type=="Tree")for(int side=-1;side<=1;side+=2){string label=side<0?"L":"R";MeshPart("TreeArm"+label,"Arm"+label,body,new Vector3(side*.32f,.72f,0),material);MeshPart("TreeFoot"+label,"Foot"+label,a,new Vector3(side*.36f,.1f,0),material);}
                if(actor.type=="Lamp"){MeshPart("LampHead","Head",body,new Vector3(.04f,1.12f,0),material);for(int side=-1;side<=1;side+=2){string label=side<0?"L":"R";MeshPart("LampFoot"+label,"Foot"+label,a,new Vector3(side*.20f,.08f,0),material);}}
                if(actor.type=="Bat")for(int side=-1;side<=1;side+=2){string label=side<0?"L":"R";MeshPart("BatWing"+label,"Wing"+label,body,new Vector3(side*.065f,0,0),material);}
                var positions=grounded[actor.id];
                for(int axis=0;axis<3;axis++){int k=axis;animation.SetCurve(basePath,typeof(Transform),"m_LocalPosition."+"xyz"[axis],Linear(actor.samples.Select((s,i)=>new Keyframe(s.t,positions[i][k]))));}
                animation.SetCurve(basePath,typeof(Transform),"localEulerAnglesRaw.y",Linear(actor.samples.Select(s=>new Keyframe(s.t,s.yaw))));
                // Each part has a different response/phase. A lamp steps; a stump strikes its drum; bats flap.
                Curve(animation,basePath+"/Pose","m_LocalPosition.y",t=>actor.type=="Bat"?0:actor.type=="Hat"?.06f*Mathf.Sin(t*5+actor.side):Walking(actor,t)?.045f*Mathf.Abs(Mathf.Sin(t*Mathf.PI/.625f)):0);
                Curve(animation,basePath+"/Pose","localEulerAnglesRaw.z",t=>actor.type=="Hat"?HatTilt(t,actor.side):actor.type=="Tree"?4*Mathf.Sin(t*5+actor.side)*Awake(t):actor.type=="Lamp"?3*Mathf.Sin(t*5+actor.side)*Awake(t):0);
                Curve(animation,basePath+"/Pose","localEulerAnglesRaw.x",t=>actor.type=="Hat"?HatBow(t,actor.side):t<10?25*(1-Smooth(t/10)):t>215?25*Smooth((t-215)/4):0);
                for(int side=-1;side<=1;side+=2)
                {
                    int s=side;string label=side<0?"L":"R";
                    if(actor.type=="Tree"||actor.type=="Lamp")
                    {
                        float x=actor.type=="Tree"?.22f:.12f,y=actor.type=="Tree"?.1f:.08f;
                        Curve(animation,basePath+"/Foot"+label,"m_LocalPosition.y",t=>y+(Walking(actor,t)?Mathf.Max(0,Mathf.Sin(t*Mathf.PI/.625f+s*Mathf.PI/2))*.13f:0));
                        Curve(animation,basePath+"/Foot"+label,"m_LocalPosition.z",t=>Walking(actor,t)?.13f*Mathf.Sin(t*Mathf.PI/.625f+s*Mathf.PI/2):0);
                        if(actor.type=="Tree")Curve(animation,basePath+"/Pose/Arm"+label,"localEulerAnglesRaw.x",t=>t<12?0:t<200?18+28*Mathf.Sin(t*Mathf.PI/.625f+s*Mathf.PI/2)+((t>=170&&t<185)?55*Smooth((t-170)/10):0):0);
                    }
                    if(actor.type=="Bat"){float phase=int.Parse(actor.id.Substring(3))*.73f;Curve(animation,basePath+"/Pose/Wing"+label,"localEulerAnglesRaw.z",t=>s*18*Mathf.Sin(t*Mathf.PI*2/.416667f+phase),.03125f);Curve(animation,basePath+"/Pose/Wing"+label,"localEulerAnglesRaw.y",t=>s*48*Mathf.Sin(t*Mathf.PI*2/.416667f+phase),.03125f);}
                }
                if(actor.type=="Lamp")
                {
                    Curve(animation,basePath+"/Pose/Head","localEulerAnglesRaw.x",t=>t<10?35*(1-Smooth((t-2)/6)):t>215?35*Smooth((t-215)/4):t>=165&&t<200?-25*Smooth((t-165)/8):6*Mathf.Sin(t*5-.6f));
                    Curve(animation,basePath+"/Pose/Head","localEulerAnglesRaw.y",t=>t>=150&&t<165?actor.side*(15+Mathf.Floor((t-150)/2.5f)*5):0);
                }
                // Visible rest gesture then a small scale fade only in the final second.
                if(actor.type!="Bat")for(int axis=0;axis<3;axis++)Curve(animation,basePath,"m_LocalScale."+"xyz"[axis],t=>size*(1-.95f*Smooth((t-219)/1)));
                bindings.Add(new KeyValuePair<Actor,GameObject>(actor,a.gameObject));a.gameObject.SetActive(false);renderers+=a.GetComponentsInChildren<Renderer>(true).Length;
            }
            KH_ParadePurchasedAssets.ApplyCast(root);
            }catch{UnityEngine.Object.DestroyImmediate(root.gameObject);UnityEngine.Object.DestroyImmediate(animation);throw;}
            var old=stage.Find("StoryCast");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);root.name="StoryCast";
            foreach(var track in timeline.GetOutputTracks().Where(t=>t.name.StartsWith("KH_Story_")).ToArray()){director.ClearGenericBinding(track);timeline.DeleteTrack(track);}
            if(savedAnimation==null)AssetDatabase.CreateAsset(animation,clipPath);else{EditorUtility.CopySerialized(animation,savedAnimation);UnityEngine.Object.DestroyImmediate(animation);animation=savedAnimation;}
            foreach(var binding in bindings){var activation=timeline.CreateTrack<ActivationTrack>(null,"KH_Story_"+binding.Key.id);activation.postPlaybackState=ActivationTrack.PostPlaybackState.Inactive;foreach(var window in binding.Key.windows){var clip=activation.CreateDefaultClip();clip.start=window.start;clip.duration=window.end-window.start;}director.SetGenericBinding(activation,binding.Value);}
            var movement=timeline.CreateTrack<AnimationTrack>(null,"KH_Story_Choreography");movement.trackOffset=TrackOffset.ApplySceneOffsets;var timelineClip=movement.CreateClip(animation);timelineClip.start=0;timelineClip.duration=220;
            var playable=(AnimationPlayableAsset)timelineClip.asset;playable.removeStartOffset=false;playable.applyFootIK=false;
            KH_ParadePurchasedAssets.AnimateLanterns(root);
            director.SetGenericBinding(movement,stage.GetComponent<Animator>());EditorUtility.SetDirty(animation);EditorUtility.SetDirty(timeline);EditorUtility.SetDirty(director);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(stage.gameObject.scene);
            Debug.Log("PARADE_STORY_CAST_BUILT actors="+plan.actors.Length+" renderers="+renderers+" animationBindings="+AnimationUtility.GetCurveBindings(animation).Length);
        }
        static void Curve(AnimationClip clip,string path,string property,Func<float,float> f,float step=.125f){int count=Mathf.RoundToInt(220/step);clip.SetCurve(path,typeof(Transform),property,Linear(Enumerable.Range(0,count+1).Select(i=>new Keyframe(i*step,f(i*step)))));}
        static float Smooth(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
        static float Awake(float t)=>Smooth(t/10)*(1-Smooth((t-200)/5));
        static bool Walking(Actor a,float t)=>t>=(a.type=="Tree"?80:40)&&t<147&&!(t>=55&&t<80);
        static float HatTilt(float t,int side)=>t>=65&&t<70?side*(50*Mathf.Sin(Mathf.PI*Smooth((t-65)/5))):t>=205?0:side*8*Mathf.Sin(t*Mathf.PI/.625f+.7f);
        static float HatBow(float t,int side){float cue=side<0?60.3125f:62.8125f;float bow=t>=cue&&t<cue+2?30*Mathf.Sin(Mathf.PI*(t-cue)/2):0;return bow+(t<10?25*(1-Smooth((t-3)/7)):t>=200&&t<205?30*Mathf.Sin(Mathf.PI*(t-200)/5):t>215?35*Smooth((t-215)/4):0);}
        [MenuItem("Tools/KominkaHillside/Parade/34 - Apply Story Finale")]
        public static void ApplyFinale()
        {
            Guard();var p=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(Input));
            if(plan.finale==null||plan.finale.Length!=6)throw new Exception("Six explicit finale slots required");var targets=new Vector3[6];
            foreach(var slot in plan.finale){var target=p.stageRoot.transform.TransformPoint(V(slot.p));UnityEngine.AI.NavMeshHit hit;if(!UnityEngine.AI.NavMesh.SamplePosition(target,out hit,.2f,UnityEngine.AI.NavMesh.AllAreas))throw new Exception("Finale NavMesh "+slot.canonical);targets[slot.canonical]=target;}
            for(int i=0;i<6;i++)for(int j=i+1;j<6;j++){var delta=targets[i]-targets[j];delta.y=0;if(delta.magnitude<1.1f)throw new Exception("Finale root spacing "+i+"/"+j);}
            for(int i=0;i<6;i++)p.parade.finaleSlots[i].position=targets[i];EditorSceneManager.MarkSceneDirty(p.gameObject.scene);Debug.Log("PARADE_STORY_FINALE_APPLIED six, small dogs moved forward");
        }
        [MenuItem("Tools/KominkaHillside/Parade/31 - Verify Story Cast")]
        public static void Verify()
        {
            var p=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();var root=p.stageRoot.transform.Find("StoryCast");var timeline=(TimelineAsset)p.director.playableAsset;
            var errors=new List<string>();if(root==null||root.childCount!=18)errors.Add("18 actor roots required");
            if(root!=null)foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true)){var f=r.GetComponent<MeshFilter>();if(f==null||f.sharedMesh==null||f.sharedMesh.vertexCount==0)errors.Add("mesh "+r.name);if(r.sharedMaterial==null||r.sharedMaterial.mainTexture==null||!r.sharedMaterial.shader.isSupported)errors.Add("material "+r.name);}
            var tracks=timeline.GetOutputTracks().Where(t=>t.name.StartsWith("KH_Story_")).ToArray();if(tracks.Length!=19)errors.Add("19 cast tracks required");
            foreach(var track in tracks)if(p.director.GetGenericBinding(track)==null)errors.Add("binding "+track.name);
            if(root!=null&&(root.GetComponentsInChildren<ParticleSystem>(true).Length>0||root.GetComponentsInChildren<Collider>(true).Length>0))errors.Add("cast must not change physics or PS restoration");
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/KH_StoryCast.anim");if(clip==null||clip.hasRootCurves||AnimationUtility.GetCurveBindings(clip).Any(b=>string.IsNullOrEmpty(b.path)))errors.Add("child curves only");
            Directory.CreateDirectory("docs/plans/halloween-parade/evidence/story-v4");File.WriteAllText("docs/plans/halloween-parade/evidence/story-v4/cast-wiring.txt",DateTime.UtcNow.ToString("o")+"\n"+(errors.Count==0?"PASS: 18 actors,19 tracks; child curves; meshes/materials/bindings present; no Collider/new PS. Edit structure only.":string.Join("\n",errors)));
            if(errors.Count>0)throw new Exception(string.Join(";",errors));Debug.Log("PARADE_STORY_CAST_VERIFIED");
        }
        [MenuItem("Tools/KominkaHillside/Parade/33 - Verify Story Motion")]
        public static void VerifyMotion()
        {
            Guard();var p=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(Input));
            var stage=UnityEngine.Object.Instantiate(p.stageRoot);stage.name="KH_StoryMotionCheck";stage.hideFlags=HideFlags.HideAndDontSave;stage.SetActive(true);var director=stage.GetComponent<PlayableDirector>();
            var lines=new List<string>();int checkedPoses=0;float worst=0,minFoot=float.PositiveInfinity,maxFoot=float.NegativeInfinity;
            try
            {
                foreach(var output in director.playableAsset.outputs){var original=p.director.GetGenericBinding(output.sourceObject);Transform t=original is GameObject g?g.transform:original is Component c?c.transform:null;if(t==null)continue;var path=AnimationUtility.CalculateTransformPath(t,p.stageRoot.transform);var target=string.IsNullOrEmpty(path)?stage.transform:stage.transform.Find(path);if(target==null)throw new Exception("Binding clone path "+path);director.SetGenericBinding(output.sourceObject,original is GameObject?(UnityEngine.Object)target.gameObject:target.GetComponent(original.GetType()));}
                foreach(var a in stage.GetComponentsInChildren<AudioSource>(true)){a.mute=true;a.playOnAwake=false;}
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/KH_StoryCast.anim");
                foreach(var actor in plan.actors)
                {
                    var t=stage.transform.Find("StoryCast/"+actor.id);string path="StoryCast/"+actor.id;
                    var curves=new AnimationCurve[3];for(int axis=0;axis<3;axis++)curves[axis]=AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalPosition."+"xyz"[axis]));
                    var times=actor.windows.SelectMany(w=>new[]{Mathf.Max(0,w.start-.01f),w.start+.01f,w.end-.01f,Mathf.Min(220,w.end+.01f)}).Concat(new[]{32.8f,65.8f,100.8f,190.8f,219.8f,220f}).Distinct().ToArray();
                    foreach(float time in times)
                    {
                        director.time=time;director.Evaluate();bool active=actor.windows.Any(w=>time>=w.start&&time<w.end);
                        if(t.gameObject.activeSelf!=active)throw new Exception(actor.id+" activation at "+time);
                        if(!active)continue;var expected=new Vector3(curves[0].Evaluate(time),curves[1].Evaluate(time),curves[2].Evaluate(time));float error=Vector3.Distance(t.localPosition,expected);worst=Mathf.Max(worst,error);if(error>.005f)throw new Exception(actor.id+" pose at "+time+" error="+error);checkedPoses++;
                        if((actor.type=="Tree"||actor.type=="Lamp")&&time>=25&&time<215)
                        {
                            float lowest=float.PositiveInfinity;
                            foreach(string foot in new[]{"FootL","FootR"}){var f=t.Find(foot);var mesh=f.GetComponent<MeshFilter>().sharedMesh;foreach(var vertex in mesh.vertices){var w=f.TransformPoint(vertex);var l=stage.transform.InverseTransformPoint(w);float floor=Ground(stage.transform,l,0);lowest=Mathf.Min(lowest,w.y-stage.transform.TransformPoint(new Vector3(l.x,floor,l.z)).y);}}
                            minFoot=Mathf.Min(minFoot,lowest);maxFoot=Mathf.Max(maxFoot,lowest);
                        }
                    }
                }
                lines.Add("PASS: activation boundary and first Evaluate root poses="+checkedPoses+" maxPositionError="+worst);
                lines.Add("Sampled minimum foot vertex / terrain gap across both feet: min="+minFoot+" max="+maxFoot+" metres. Off-path slope/foot articulation visual acceptance remains separate.");
                if(minFoot<-.10f||maxFoot>.20f)throw new Exception("Foot support outside .10m penetration/.20m hover investigation bounds: "+minFoot+","+maxFoot);
                lines.Add("Limits: Edit cloned Timeline; not runtime Animator/dog/VR/network acceptance.");
            }
            catch(Exception e){lines.Add("FAIL: "+e);throw;}
            finally{director.Stop();UnityEngine.Object.DestroyImmediate(stage);lines.Add("sceneDirty="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty);File.WriteAllText("docs/plans/halloween-parade/evidence/story-v4/motion-wiring.txt",DateTime.UtcNow.ToString("o")+"\n"+string.Join("\n",lines));}
            Debug.Log("PARADE_STORY_MOTION_VERIFIED poses="+checkedPoses);
        }
    }
}
