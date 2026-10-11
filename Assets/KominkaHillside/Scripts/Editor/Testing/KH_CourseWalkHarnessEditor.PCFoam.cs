using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/100v - Harness PC foam lifecycle")]
        public static void PCFoamHarness()
        {
            WashHarnessGroups(new[]{"pc-foam"});report.deadline=EditorApplication.timeSinceStartup+180;
            report.limitations="One local ClientSim. Six-dog initialization/reference check and one Shiba restore/shower/clear/re-emission case. Cells injected to make foam; contact uses actual _LocalContact. No ParticleSystem.Play/Simulate in harness. Not visual, performance, Udon-on-device, network, mobile, or release acceptance.";Save();
        }

        static void MakeWashPCFoamCases()
        {
            WsBind();var s=WsCase("PCF-restore-rinse-clear-reemit",45);
            SkinnedMeshRenderer volume=null;ParticleSystem runoff=null;Material material=null;int peak=0,emits=0;float previousGate=-1;
            void Observe()
            {
                peak=Mathf.Max(peak,runoff.particleCount);WsExpect(runoff.particleCount<=16,"PC runoff cap 16");
                WsExpect(material.GetTexture("_Mask")==Get<RenderTexture>(wsWash,"_cur"),"PC material follows the actual mask swap");
                float gate=Get<float>(wsWash,"_pcFoamNextEmit");
                if(gate>previousGate&&gate>0){if(previousGate>0)WsExpect(gate-previousGate>=1f/12f-.0002f,"PC runoff rate gate >= 1/12 s");previousGate=gate;emits++;}
                var particles=new ParticleSystem.Particle[16];int n=runoff.GetParticles(particles);
                for(int i=0;i<n;i++)WsExpect(particles[i].startSize>=.008f&&particles[i].startSize<=.025f&&particles[i].startLifetime>=.25f&&particles[i].startLifetime<=.6f,"PC particle world size/lifetime bounds");
            }
            void RestoreFoam()
            {
                var cells=(float[])Get<float[]>(wsWash,"_foam").Clone();var valid=Get<bool[]>(wsWash,"cellValid");
                for(int i=0;i<cells.Length;i++)cells[i]=valid==null||valid[i]?1:0;
                wsWash.SetProgramVariable("_foam",cells);wsWash.SendCustomEvent("_DebugCellsChanged");wsWash.SetProgramVariable("_restorePending",true);
                peak=0;emits=0;previousGate=-1;
            }
            s.Add(WsDo(()=>
            {
                var all=UnityEngine.Object.FindObjectsOfType<UdonBehaviour>().Where(u=>u.programSource!=null&&u.programSource.name=="KH_DogWash").ToArray();
                WsExpect(all.Length==6,"six runtime wash behaviours");
                foreach(var w in all)
                {
                    WsExpect(Get<bool>(w,"_pcFoamReady"),w.name+" PC initialized");
                    WsExpect(Get<Material>(w,"_pcFoamMat").GetTexture("_Mask")==Get<RenderTexture>(w,"_cur"),w.name+" initial shared mask");
                    WsExpect(Get<Renderer>(w,"shellRenderer").sharedMaterial.shader.name=="KominkaHillside/Wash Shell PC Thin",w.name+" thin material selected");
                }
                WsPlaceShiba();WsPlayerNear(1.3f);wsWash.SendCustomEvent("_DebugClear");
                volume=Get<SkinnedMeshRenderer>(wsWash,"pcFoamRenderer");runoff=Get<ParticleSystem>(wsWash,"pcFoamRunoff");material=Get<Material>(wsWash,"_pcFoamMat");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDrive(.5f,1,u=>WsPath(u/3f),WsShower,-1f,new WsTrace()));
            s.Add(WsDo(()=>{WsEnd(WsShower);WsExpect(runoff.particleCount==0&&!volume.enabled,"zero foam shower does not emit or enable volume");RestoreFoam();}));
            s.Add(WsWait(.8));
            s.Add(WsDo(()=>WsExpect(volume.enabled,"restored foam enables volume")));
            s.Add(WsDrive(1.5f,1,u=>{Observe();return WsPath(u/3f);},WsShower,-1f,new WsTrace()));
            s.Add(WsDo(()=>{Observe();WsEnd(WsShower);WsExpect(peak>0&&emits>0,"real particles emitted without harness simulation");WsNote("initial peak="+peak+" observed emission gates="+emits);}));
            s.Add(WsWait(1));
            s.Add(WsDo(()=>{WsExpect(runoff.particleCount==0,"particles expire after contact stops");wsWash.SendCustomEvent("_DebugClear");}));
            s.Add(WsWait(.8));
            s.Add(WsDo(()=>{WsExpect(!volume.enabled&&runoff.particleCount==0,"clear disables volume and clears runoff");RestoreFoam();}));
            s.Add(WsWait(.8));
            s.Add(WsDrive(1f,1,u=>{Observe();return WsPath(u/3f);},WsShower,-1f,new WsTrace()));
            s.Add(WsDo(()=>{Observe();WsEnd(WsShower);WsExpect(volume.enabled&&peak>0&&emits>0,"clear then restore permits real re-emission");WsNote("re-emission peak="+peak+" observed emission gates="+emits);wsWash.SendCustomEvent("_DebugClear");}));
            s.Add(WsWait(.8));
            s.Add(WsDo(()=>WsExpect(!volume.enabled&&runoff.particleCount==0,"final state cleared")));
        }
    }
}
