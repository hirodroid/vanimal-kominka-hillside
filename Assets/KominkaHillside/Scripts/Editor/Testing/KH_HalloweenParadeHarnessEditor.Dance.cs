using System;
using System.IO;
using System.Text;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    public static partial class KH_HalloweenParadeHarnessEditor
    {
        static Camera danceCamera,followCamera;static RenderTexture danceRT;static Texture2D danceTexture;
        static float nextCapture;static int observedCue;
        static bool[] sawDance=new bool[6];
        static bool[] cueSeen=new bool[36];
        static readonly float[] CueStarts={60,62.5f,65,67.5f,70,75};
        static readonly float[] DanceCues={55,57.5f,59.9f,60.35f,62.85f,65.35f,68.1f,70.35f,75.35f,77.5f,80,82};
        static string CaptureDir=>Dir+"/"+report.runId+"/dance";
        static void AdvanceDance()
        {
            if(stage==0)
            {
                if(Time.time-playAt>20)throw new InvalidOperationException("Client startup timeout");
                if(Time.time-playAt<3||!Utilities.IsValid(Networking.LocalPlayer))return;
                Resolve();BeginCase(false);observedCue=0;nextCapture=55f;sawDance=new bool[6];cueSeen=new bool[36];ToStage(1);return;
            }
            ObservePhase();int phase=Phase();
            Need(Get<int>(manager,"requestId")==report.normalRequest,"Request replaced");
            if(stage==1)
            {
                Need(phase!=0&&phase!=6&&phase!=7,"Preparation failed");if(phase!=5)return;
                Need(Get<int>(manager,"endedMask")==63&&Get<int>(manager,"placedMask")==63,"Missing actual ACKs");
                Directory.CreateDirectory(CaptureDir+"/following");File.WriteAllText(CaptureDir+"/frames.csv","frame,showSeconds\n");
                File.WriteAllText(CaptureDir+"/dogs.csv","seconds,dog,targetDistance,headingDegrees,speed,dancePose,danceTime,callReady,callPose,callTime,stateHash,transition\n");
                File.WriteAllText(CaptureDir+"/cues.csv","cueStart,dog,firstObserved,latency,expectedPose,callTime,poseClock\n");
                var go=new GameObject("KH_ParadeEvidenceCamera");danceCamera=go.AddComponent<Camera>();danceCamera.enabled=false;
                danceCamera.transform.SetPositionAndRotation(report.captureEye,Quaternion.LookRotation(report.captureTarget-report.captureEye));
                danceCamera.fieldOfView=62;danceCamera.nearClipPlane=.05f;danceCamera.farClipPlane=180;danceCamera.allowHDR=false;danceCamera.useOcclusionCulling=false;
                danceRT=new RenderTexture(1280,720,24);danceTexture=new Texture2D(1280,720,TextureFormat.RGB24,false);danceCamera.targetTexture=danceRT;
                followCamera=UnityEngine.Object.Instantiate(danceCamera);followCamera.name="KH_ParadeFollowingEvidence";followCamera.transform.SetPositionAndRotation(report.captureFollowEye,Quaternion.LookRotation(report.captureTarget-report.captureFollowEye));
                ToStage(10);return;
            }
            if(stage==10)
            {
                Need(phase==5,"Show ended before sample");float elapsed=Vm<float>(manager,"get_Elapsed");
                ObserveCueWindows(elapsed);
                for(int i=0;i<6;i++)
                {
                    var anim=Get<Animator>(wanders[i],"dogAnimator");int pose=Get<int>(wanders[i],"paradeDancePose");
                    if(elapsed>=60&&elapsed<77.5f&&pose>=10&&anim.GetBool("CallReady")&&anim.GetInteger("CallPose")==pose&&!anim.IsInTransition(0))
                    {
                        int index=pose-10;
                        if(index<KH_ParadeDanceEditor.Moves.Length&&anim.GetCurrentAnimatorStateInfo(0).IsName("Parade"+KH_ParadeDanceEditor.Moves[index]))sawDance[i]=true;
                    }
                }
                if(observedCue<DanceCues.Length&&elapsed>=DanceCues[observedCue]){ObserveDance(elapsed);observedCue++;}
                if(elapsed>=nextCapture&&elapsed<80f)
                {
                    var old=RenderTexture.active;try{CaptureDanceFrame(danceCamera,CaptureDir);CaptureDanceFrame(followCamera,CaptureDir+"/following");}finally{RenderTexture.active=old;}
                    File.AppendAllText(CaptureDir+"/frames.csv",report.capturedFrames+","+elapsed.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+"\n");report.capturedFrames++;nextCapture=elapsed+1f/24f;
                }
                if(elapsed<82)return;
                Note("Dance observations="+string.Join(",",sawDance.Select(v=>v.ToString()).ToArray())+" frames="+report.capturedFrames);
                manager.SendCustomEvent("_Cancel");Need(Phase()==6,"Cancel did not restore");ToStage(11);return;
            }
            if(stage==11)
            {
                if(Time.time-stageAt>35)throw new Exception("Dance cancellation recovery timeout");
                if(phase!=0){Need(phase==6,"Unexpected restore phase");return;}
                Need(Get<int>(manager,"restoredMask")==63,"Not all dogs restored");ToStage(12);return;
            }
            if(stage==12)
            {
                if(Time.time-stageAt<1)return;
                for(int i=0;i<6;i++)
                {
                    var anim=Get<Animator>(wanders[i],"dogAnimator");
                    Need(!Get<bool>(wanders[i],"paradeActive")&&Get<int>(wanders[i],"paradeDancePose")==0&&!Get<bool>(pets[i],"_suppressed")&&!anim.GetBool("CallReady"),"Dance state leaked after cancel dog="+i);
                }
                Need(sawDance.All(v=>v),"Not all six dogs reached actual parade Animator states in representative section; see dogs.csv");
                Need(report.capturedFrames>100,"Capture did not cover representative motion");
                report.normalPassed=true;End("PASS","Actual start, 0-82 s, six Animator dance states and cancel/restore. Visual/entertainment acceptance remains separate.");
            }
        }
        static void ObserveDance(float time)
        {
            for(int i=0;i<6;i++)
            {
                var root=Get<Transform>(dogs[i],"root");var target=Get<Vector3>(wanders[i],"paradeTarget");var face=Get<Vector3>(wanders[i],"paradeFacing");var anim=Get<Animator>(wanders[i],"dogAnimator");
                string line=time.ToString("F3")+","+i+","+Vector3.Distance(root.position,target).ToString("F4")+","+Vector3.Angle(root.forward,face).ToString("F2")+","+Get<float>(wanders[i],"_speed").ToString("F3")+","+Get<int>(wanders[i],"paradeDancePose")+","+Get<float>(wanders[i],"paradeDanceTime").ToString("F3")+","+anim.GetBool("CallReady")+","+anim.GetInteger("CallPose")+","+anim.GetFloat("CallTime").ToString("F3")+","+anim.GetCurrentAnimatorStateInfo(0).shortNameHash+","+anim.IsInTransition(0)+"\n";
                File.AppendAllText(CaptureDir+"/dogs.csv",line);
            }
        }
        static void ObserveCueWindows(float time)
        {
            for(int c=0;c<CueStarts.Length;c++)
            {
                float cue=CueStarts[c];if(time<cue+.25f)continue;
                for(int i=0;i<6;i++)
                {
                    int key=c*6+i;if(cueSeen[key])continue;
                    Need(time<=cue+.9f,"Scheduled cue window missed cue="+cue+" dog="+i+" observed="+time);
                    int rank=Get<int[]>(manager,"processionRanks")[i];bool left=rank%2==0;
                    int expected=c==0?(left?11:13):c==1?(left?14:11):c==2?(i==1?11:(left?14:13)):c==3?(i==0?12:i==1?11:(left?14:13)):c==4?10:15;
                    var anim=Get<Animator>(wanders[i],"dogAnimator");float clock=Get<float>(wanders[i],"paradePoseTime"),callTime=anim.GetFloat("CallTime");
                    bool phaseOk=i!=1||(c!=2&&c!=3)||Mathf.Abs(callTime-(c==2?(clock-65f)/1.25f:.125f))<.025f;
                    if(Get<int>(wanders[i],"paradeDancePose")==expected&&anim.GetBool("CallReady")&&anim.GetInteger("CallPose")==expected&&phaseOk
                        &&anim.GetCurrentAnimatorStateInfo(0).IsName("Parade"+KH_ParadeDanceEditor.Moves[expected-10])&&!anim.IsInTransition(0))
                    {
                        cueSeen[key]=true;File.AppendAllText(CaptureDir+"/cues.csv",cue+","+i+","+time.ToString("F3")+","+(time-cue).ToString("F3")+","+expected+","+callTime.ToString("F3")+","+clock.ToString("F3")+"\n");
                    }
                }
            }
        }
        static void CaptureDanceFrame(Camera camera,string directory)
        {camera.Render();RenderTexture.active=danceRT;danceTexture.ReadPixels(new Rect(0,0,1280,720),0,0);danceTexture.Apply();File.WriteAllBytes(directory+"/"+report.capturedFrames.ToString("D4")+".jpg",danceTexture.EncodeToJPG(90));}
        static void DisposeDanceCapture()
        {
            if(danceCamera!=null)UnityEngine.Object.Destroy(danceCamera.gameObject);
            if(followCamera!=null)UnityEngine.Object.Destroy(followCamera.gameObject);
            if(danceRT!=null){danceRT.Release();UnityEngine.Object.Destroy(danceRT);}if(danceTexture!=null)UnityEngine.Object.Destroy(danceTexture);
            danceCamera=null;followCamera=null;danceRT=null;danceTexture=null;
        }
    }
}
