using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using KominkaDogRun.Editor;

namespace KominkaHillside.Editor
{
    public static partial class KH_ParadeDanceEditor
    {
        [MenuItem("Tools/KominkaHillside/Parade/21 - Bind Dance States")]
        public static void BindStates()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit only");
            var log=new StringBuilder();
            foreach(var def in KD_DogDefs.All)
            {
                var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
                var sm=controller.layers[0].stateMachine;
                for(int i=0;i<Moves.Length;i++)
                {
                    string name="Parade"+Moves[i];int pose=10+i;
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(PathFor(def.key,Moves[i]));if(clip==null)throw new Exception("Build clips first");
                    var state=sm.states.Select(x=>x.state).FirstOrDefault(s=>s.name==name)??sm.AddState(name,new Vector3(1500,800+i*80,0));
                    state.motion=clip;state.timeParameterActive=true;state.timeParameter="CallTime";
                    foreach(var t in sm.anyStateTransitions.Where(t=>t.destinationState==state).ToArray())sm.RemoveAnyStateTransition(t);
                    var enter=sm.AddAnyStateTransition(state);enter.hasExitTime=false;enter.hasFixedDuration=true;enter.duration=.16f;enter.canTransitionToSelf=false;
                    enter.AddCondition(AnimatorConditionMode.If,0,"CallReady");enter.AddCondition(AnimatorConditionMode.Equals,pose,"CallPose");
                    foreach(var t in state.transitions.ToArray())state.RemoveTransition(t);
                    foreach(var moving in new[]{"Run","Walk"})
                    {
                        var target=sm.states.Select(x=>x.state).Single(s=>s.name==moving);
                        var tr=state.AddTransition(target);tr.hasExitTime=false;tr.hasFixedDuration=true;tr.duration=0;
                        tr.AddCondition(AnimatorConditionMode.IfNot,0,"CallReady");tr.AddCondition(AnimatorConditionMode.If,0,moving);
                    }
                    var exit=state.AddTransition(sm.defaultState);exit.hasExitTime=false;exit.hasFixedDuration=true;exit.duration=.16f;exit.AddCondition(AnimatorConditionMode.IfNot,0,"CallReady");
                    log.AppendLine(def.key+" "+name+" CallPose="+pose+" normalized=CallTime");
                }
                EditorUtility.SetDirty(controller);
            }
            AssetDatabase.SaveAssets();Directory.CreateDirectory(Evidence);File.WriteAllText(Evidence+"/states.txt",log.ToString());Debug.Log("PARADE_DANCE_STATES_APPLIED\n"+log);
        }
        [MenuItem("Tools/KominkaHillside/Parade/24 - Verify Dance Scene")]
        public static void VerifyScene()
        {
            var p=UnityEngine.Object.FindObjectOfType<KH_HalloweenParade>();if(p==null)throw new Exception("Missing parade");
            var log=new StringBuilder();int errors=0;float minDistance=99,maxSpeed=0,maxNavOffset=0;string closest="";
            foreach(var def in KD_DogDefs.All)
            {
                var c=AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);var sm=c.layers[0].stateMachine;
                for(int i=0;i<Moves.Length;i++)
                {
                    var states=sm.states.Where(x=>x.state.name=="Parade"+Moves[i]).ToArray();
                    if(states.Length!=1){errors++;continue;}var s=states[0].state;
                    var entries=sm.anyStateTransitions.Where(t=>t.destinationState==s).ToArray();
                    bool ok=s.motion==AssetDatabase.LoadAssetAtPath<AnimationClip>(PathFor(def.key,Moves[i]))&&s.timeParameterActive&&s.timeParameter=="CallTime"&&entries.Length==1&&entries[0].conditions.Any(x=>x.parameter=="CallPose"&&x.threshold==10+i)&&entries[0].conditions.Any(x=>x.parameter=="CallReady"&&x.mode==AnimatorConditionMode.If)&&s.transitions.Length==3&&s.transitions.All(tr=>tr.conditions.Any(x=>x.parameter=="CallReady"&&x.mode==AnimatorConditionMode.IfNot));
                    if(!ok)errors++;log.AppendLine(def.key+" "+s.name+" wiring="+ok);
                }
            }
            for(int i=0;i<6;i++)
            {
                float dogSpeed=0,nav=0,height=0;
                UnityEngine.AI.NavMeshHit branchStart,branchEnd,obstacle;
                bool branchOk=UnityEngine.AI.NavMesh.SamplePosition(p.routePoints[p.routePoints.Length-2].position,out branchStart,.2f,UnityEngine.AI.NavMesh.AllAreas)
                    &&UnityEngine.AI.NavMesh.SamplePosition(p.finaleSlots[i].position,out branchEnd,.2f,UnityEngine.AI.NavMesh.AllAreas)
                    &&!UnityEngine.AI.NavMesh.Raycast(branchStart.position,branchEnd.position,out obstacle,UnityEngine.AI.NavMesh.AllAreas);
                if(!branchOk)errors++;log.AppendLine("dog="+i+" finaleBranchWalkable="+branchOk);
                for(float t=25;t<150;t+=.05f)
                {
                    Vector3 a=p._DogTargetAt(t,i),b=p._DogTargetAt(t+.05f,i);dogSpeed=Mathf.Max(dogSpeed,Vector3.Distance(a,b)/.05f);
                    if(t>=55&&t<=80||t>=115){UnityEngine.AI.NavMeshHit hit;if(!UnityEngine.AI.NavMesh.SamplePosition(a,out hit,.2f,UnityEngine.AI.NavMesh.AllAreas)){errors++;log.AppendLine("NavMesh missing dog="+i+" t="+t);break;}var offset=a-hit.position;height=Mathf.Max(height,Mathf.Abs(offset.y));offset.y=0f;nav=Mathf.Max(nav,offset.magnitude);}
                    for(int j=i+1;j<6;j++){var delta=a-p._DogTargetAt(t,j);delta.y=0;if(delta.magnitude<minDistance){minDistance=delta.magnitude;closest="dogs "+i+"/"+j+" at "+t.ToString("F2");}}
                }
                maxSpeed=Mathf.Max(maxSpeed,dogSpeed);maxNavOffset=Mathf.Max(maxNavOffset,nav);
                log.AppendLine("dog="+i+" targetMaxSpeed="+dogSpeed.ToString("F3")+" walkSpeed="+p.dogs[i].wander.walkSpeed.ToString("F3")+" navHorizontalOffset="+nav.ToString("F3")+" navHeightDifference="+height.ToString("F3")+" (runtime StepToward uses SnapToGround)");
                if(dogSpeed>p.dogs[i].wander.walkSpeed+.05f||nav>.12f)errors++;
            }
            if(minDistance<1f)errors++;
            log.AppendLine("minimum root separation="+minDistance.ToString("F3")+"m "+closest+" (not body/terrain clearance acceptance); errors="+errors);
            File.WriteAllText(Evidence+"/scene.txt",log.ToString());if(errors>0)throw new Exception("Dance scene errors="+errors+"; see "+Evidence+"/scene.txt");Debug.Log("PARADE_DANCE_SCENE_OK\n"+log);
        }
    }
}
