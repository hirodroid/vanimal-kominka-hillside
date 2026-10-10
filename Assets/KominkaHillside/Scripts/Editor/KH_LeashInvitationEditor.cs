using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UdonSharpEditor;
using VRC.SDKBase;
using KominkaDogRun;

namespace KominkaHillside.Editor
{
    public static class KH_LeashInvitationEditor
    {
        const string Scene = "Assets/Scenes/VRCDefaultWorldScene.unity";
        const string Menu = "Tools/KominkaHillside/Course/Leash Invitation/";
        const string Root = "KH_LeashInvitations";
        static KH_LeashManager Manager()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != Scene)
                throw new InvalidOperationException("Open the hillside scene in Edit mode");
            return UnityEngine.Object.FindObjectsOfType<KH_LeashManager>(true).Single();
        }
        static void Report(string name, StringBuilder log)
        { Directory.CreateDirectory("Temp/KH_LeashInvitation");File.WriteAllText("Temp/KH_LeashInvitation/"+name+".txt",log.ToString());Debug.Log("[LeashInvitation] "+log); }

        [MenuItem(Menu+"1 Setup")]
        public static void Setup()
        {
            var m=Manager();var log=new StringBuilder();
            var stations=new Transform[3];
            for(int s=0;s<3;s++) stations[s]=m.stationSlots[Array.IndexOf(m.stationOf,s)].parent;
            var dogs=UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true).Where(w=>w.hillsideLeashes==m).OrderBy(w=>w.dogIndex).ToArray();
            if(dogs.Length!=6 || !dogs.Select(w=>w.dogIndex).SequenceEqual(Enumerable.Range(0,6)))throw new Exception("Six dogs required");
            var calls=UnityEngine.Object.FindObjectsOfType<KH_WashStationCall>(true);
            // Plan every point before touching the scene; no fallback through water, shelves or incomplete paths.
            var positions=new Vector3[18];
            float[] angles={-78,-47,-16,16,47,78};
            for(int s=0;s<3;s++)
            {
                var guide=stations[s].Find("WalkGuide/Canvas");if(guide==null)throw new Exception("Missing station guide");
                for(int d=0;d<6;d++)
                {
                    bool found=false;
                    for(int attempt=0;attempt<9 && !found;attempt++)
                    {
                        float radius=attempt/3==0?1.85f:attempt/3==1?2f:1.65f;
                        float offset=attempt%3==0?0:attempt%3==1?8:-8;
                        Vector3 raw=stations[s].position-Vector3.up*.8f+Quaternion.AngleAxis(angles[d]+offset,Vector3.up)*guide.forward*radius;
                        NavMeshHit hit;if(!NavMesh.SamplePosition(raw,out hit,.35f,NavMesh.AllAreas))continue;
                        if(!ClearOfWash(hit.position,calls))continue;
                        bool spaced=true;for(int j=0;j<d;j++)if(Flat(hit.position,positions[s*6+j])<.72f)spaced=false;
                        if(!spaced || Flat(hit.position,stations[s].position)>2.05f)continue;
                        positions[s*6+d]=hit.position;found=true;
                    }
                    if(!found)throw new Exception("No clear invitation spot at "+stations[s].name+" dog "+d);
                }
            }
            var root=GameObject.Find(Root);if(root==null)root=new GameObject(Root);
            var spots=new Transform[18];
            for(int i=0;i<18;i++)
            {
                string name="Station"+(i/6)+"_Dog"+(i%6);var t=root.transform.Find(name);
                if(t==null){t=new GameObject(name).transform;t.SetParent(root.transform,false);}
                t.position=positions[i];spots[i]=t;log.AppendLine(name+" "+t.position.ToString("F3"));
            }
            m.inviteStations=stations;m.inviteSpots=spots;m.inviteDogs=dogs;
            m.inviteRange=25f;m.inviteHolderRange=5f;m.inviteGrace=5f;
            m.inviteUntil=new double[3];m.inviteLookIds=new[]{-1,-1,-1};m.inviteDogStation=new int[6];m.inviteDogAt=new double[6];m.inviteHeldMask=0;
            UdonSharpEditorUtility.CopyProxyToUdon(m);EditorUtility.SetDirty(m);EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(m));
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());Report("setup",log);
        }
        static float Flat(Vector3 a,Vector3 b){a.y=0;b.y=0;return Vector3.Distance(a,b);}
        static bool ClearOfWash(Vector3 p,KH_WashStationCall[] calls)
        {return calls.All(c=>Flat(p,c.anchor!=null?c.anchor.position:c.transform.position)>=c.keepOutRadius+.3f);}

        [MenuItem(Menu+"2 Verify")]
        public static string Verify()
        {
            var m=Manager();var log=new StringBuilder();int count=0,fail=0;
            Action<bool,string> check=(ok,name)=>{count++;if(!ok)fail++;log.AppendLine((ok?"PASS ":"FAIL ")+name);};
            var u=UdonSharpEditorUtility.GetBackingUdonBehaviour(m);
            check(u.SyncMethod==Networking.SyncType.Manual,"manager retains Manual sync and existing Owner");
            check(m.inviteStations.Length==3 && m.inviteSpots.Length==18 && m.inviteDogs.Length==6,"3 stations / 18 distinct points / 6 dogs");
            check(m.inviteRange==25 && m.inviteHolderRange==5 && m.inviteGrace==5,"25m dogs / 5m holders / 5s grace");
            foreach(string field in new[]{"inviteStations","inviteSpots","inviteDogs"})
            {
                object stored;bool exists=u.publicVariables.TryGetVariableValue(field,out stored);
                var expected=(Array)typeof(KH_LeashManager).GetField(field).GetValue(m);var actual=stored as Array;
                bool same=exists && actual!=null && actual.Length==expected.Length;
                if(same)for(int i=0;i<expected.Length;i++)
                {
                    UnityEngine.Object want=(UnityEngine.Object)expected.GetValue(i);
                    if(want is UdonSharp.UdonSharpBehaviour)want=UdonSharpEditorUtility.GetBackingUdonBehaviour((UdonSharp.UdonSharpBehaviour)want);
                    if(want!=(UnityEngine.Object)actual.GetValue(i))same=false;
                }
                check(same,"backing refs "+field);
            }
            var calls=UnityEngine.Object.FindObjectsOfType<KH_WashStationCall>(true);
            for(int s=0;s<3;s++)for(int d=0;d<6;d++)
            {
                var spot=m.inviteSpots[s*6+d];NavMeshHit hit;
                check(spot!=null && NavMesh.SamplePosition(spot.position,out hit,.1f,NavMesh.AllAreas)
                    && ClearOfWash(spot.position,calls) && Flat(spot.position,m.inviteStations[s].position)<=2.05f,"station "+s+" dog "+d+" navigable / wash clear / within 2m");
                check(spot.GetComponents<Collider>().Length==0,"point has no input collider "+s+"/"+d);
            }
            for(int i=0;i<m.localClips.Length;i++)
                check(UdonSharpEditorUtility.GetBackingUdonBehaviour(m.localClips[i]).SyncMethod==Networking.SyncType.None,"clip "+i+" stays local");
            check(m.inviteHeldMask==0 && m.inviteDogStation.All(v=>v==0) && m.inviteUntil.All(v=>v==0),"saved state inactive; no invitations baked in");
            log.AppendLine("TOTAL "+count+" FAILED "+fail);Report("verify",log);return log.ToString();
        }

        [MenuItem(Menu+"3 Probe Transitions")]
        public static string Probe()
        {
            Manager();var log=new StringBuilder();int count=0,fail=0;
            Action<bool,string> check=(ok,name)=>{count++;if(!ok)fail++;log.AppendLine((ok?"PASS ":"FAIL ")+name);};
            var go=new GameObject("Invitation probe"){hideFlags=HideFlags.HideAndDontSave};
            var m=go.AddComponent<KH_LeashManager>();var f=BindingFlags.Instance|BindingFlags.NonPublic;
            Action<int,int,double> report=(id,mask,at)=>typeof(KH_LeashManager).GetMethod("RememberInvitation",f).Invoke(m,new object[]{id,mask,at});
            Func<double,int> mask=at=>(int)typeof(KH_LeashManager).GetMethod("InvitationReportedMask",f).Invoke(m,new object[]{at});
            Action<int,int,double> reduce=(held,confirmed,at)=>typeof(KH_LeashManager).GetMethod("ReduceInvitation",f).Invoke(m,new object[]{held,confirmed,at});
            try
            {
                report(1,1,100);report(2,1,100);check(mask(100)==1,"two players at same station are ORed");
                report(1,0,100.5);check(mask(100.5)==1,"one player releasing cannot dismiss the other");
                report(2,3,101);check(mask(101)==3,"one player can report combined clip mask");
                report(2,2,101.5);check(mask(101.5)==2,"updated mask replaces prior mask");
                check(mask(105)==0,"lost report expires after 3s");
                reduce(1,1,200);check(m.inviteUntil[0]==208,"held invitation has bounded fallback expiry");
                reduce(0,0,201);check(m.inviteUntil[0]==206,"last release starts exactly 5s grace");
                reduce(1,1,203);check(m.inviteUntil[0]==211,"pickup during grace renews invitation");
                reduce(1,0,204);check(m.inviteUntil[0]==211,"inherited state without reports never extends forever");
                reduce(2,2,205);check(m.inviteUntil[0]==210 && m.inviteUntil[1]==213,"stations have independent grace and renewal");
                report(9,4,300);typeof(KH_LeashManager).GetMethod("ForgetInvitation",f).Invoke(m,new object[]{9});check(mask(300)==0,"player departure removes only their contribution");
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
            log.AppendLine("TOTAL "+count+" FAILED "+fail+" (Edit mode; no network delivery or player simulation)");Report("transitions",log);return log.ToString();
        }
    }
}
