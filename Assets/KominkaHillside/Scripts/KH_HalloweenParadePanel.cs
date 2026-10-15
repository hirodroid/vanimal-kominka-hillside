using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using KominkaDogRun;

namespace KominkaHillside
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_HalloweenParadePanel : UdonSharpBehaviour
    {
        public KH_HalloweenParade parade;
        public KH_HalloweenParadePlayback playback;
        public KD_Language language;
        public string[] languageCodes, words;
        public int wordCount=35;
        public GameObject surface, resumeSurface;
        public string[] dogNameKeys, dogNames;
        public TextMeshProUGUI title, body, progress;
        public Button startButton, requestButton, cancelButton, approveButton, declineButton;
        public TextMeshProUGUI[] buttonLabels;
        public int[] buttonWords;
        private bool _confirm;
        private int _shownRequest=-1;
        private int _lang;
        private string _lastText="";
        private string _lastLanguage="";
        private void Start(){if(surface!=null)surface.SetActive(false);if(resumeSurface!=null)resumeSurface.SetActive(false);SendCustomEventDelayedSeconds(nameof(_Tick),.25f);}
        public void _Tick()
        {
            if(parade!=null)
            {
                if(parade.LocalConsentRequired&&_shownRequest!=parade.RequestId)
                {_shownRequest=parade.RequestId;_confirm=false;_Open();}
                string current=language!=null?language.CurrentLanguage:"en";
                if((surface!=null&&surface.activeSelf)||current!=_lastLanguage)_Refresh();
            }
            SendCustomEventDelayedSeconds(nameof(_Tick),.25f);
        }
        public void _Open()
        {
            if(surface==null||parade==null)return;
            PlaceSurface(surface);
            if(resumeSurface!=null)resumeSurface.SetActive(false);
            surface.SetActive(true);_Refresh();
        }
        private void PlaceSurface(GameObject target)
        {
            var p=Networking.LocalPlayer;
            if(Utilities.IsValid(p))
            {
                var h=p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                Vector3 f=h.rotation*Vector3.forward;f.y=0;if(f.sqrMagnitude<.01f)f=p.GetRotation()*Vector3.forward;
                f.Normalize();
                target.transform.SetPositionAndRotation(h.position+f*1.35f+Vector3.down*.12f,Quaternion.LookRotation(f));
            }
        }
        public void _Close(){if(surface!=null)surface.SetActive(false);_confirm=false;if(resumeSurface!=null)resumeSurface.SetActive(false);}
        public void _Start(){if(parade==null||!parade._LocalCanStart())return;_confirm=true;_Refresh();}
        public void _Send(){if(!_confirm||parade==null)return;_confirm=false;parade._RequestStart();_Refresh();}
        public void _Approve(){if(parade!=null)parade._Approve();_Refresh();}
        public void _Decline(){if(parade!=null)parade._Decline();_Refresh();}
        public void _Cancel(){_confirm=false;if(parade!=null&&parade.Phase!=0)parade._Cancel();_Refresh();}
        public void _Reduced(){if(playback!=null)playback._ToggleReduced();_Refresh();}
        public void _Mute(){if(playback!=null)playback._ToggleMute();_Refresh();}
        public void _Refresh()
        {
            if(parade==null)return;
            string code=language!=null?language.CurrentLanguage:"en";_lastLanguage=code;_lang=0;
            for(int i=0;languageCodes!=null&&i<languageCodes.Length;i++)if(languageCodes[i]==code){_lang=i;break;}
            int phase=parade.Phase;bool needs=parade.LocalConsentRequired;
            var p=Networking.LocalPlayer;bool host=Utilities.IsValid(p)&&p.playerId==parade.HostId;
            int message=19;
            if(_confirm&&phase==0)message=1;
            else if(phase==1)message=needs?2:(Utilities.IsValid(p)&&parade._AcceptedUser(p.playerId)?3:4);
            else if(phase>=2&&phase<=4)message=5;
            else if(phase==5)message=6;
            else if(phase==6)message=32;
            else if(parade.ResultCode==11)message=22;
            else if(parade.ResultCode==7)message=7;
            else if(parade.ResultAfterCommit)message=parade.ShowStarted?23:8;
            else if(parade.ResultCode==1||parade.ResultCode==2)message=18;
            else if(parade.ResultCode==8)message=21;
            else if(parade.ResultCode==5)message=31;
            else if(parade.ResultCode!=0)message=9;
            string text=Word(message);
            if(message==22)
            {
                text+="\n"+Word(25)+" ";bool comma=false;
                for(int i=0;i<6;i++)if((parade.RecoveryFailedMask&(1<<i))!=0)
                {
                    if(comma)text+=", ";comma=true;
                    string key=dogNameKeys!=null&&i<dogNameKeys.Length?dogNameKeys[i]:"";
                    string name=language!=null?language._Text(key):key;
                    text+=name!=key?name:(dogNames!=null&&i<dogNames.Length?dogNames[i]:(i+1).ToString());
                }
            }
            if(text!=_lastText){_lastText=text;if(body!=null)body.text=text;}
            if(title!=null)title.text=Word(needs?20:0);
            if(progress!=null)progress.text=phase==1?Word(33).Replace("{0}",parade.PendingCount.ToString()).Replace("{1}",parade.ConsentRemainingSeconds.ToString()):phase==5?Word(34).Replace("{0}",Mathf.Max(0,Mathf.CeilToInt(parade.durationSeconds-parade.Elapsed)).ToString()):"";
            for(int i=0;buttonLabels!=null&&buttonWords!=null&&i<buttonLabels.Length&&i<buttonWords.Length;i++)if(buttonLabels[i]!=null)
            {
                int index=buttonWords[i];
                if(playback!=null&&index==16)index=playback.reducedEffects?27:26;
                if(playback!=null&&index==17)index=playback.userMuted?29:28;
                if(index==24&&!needs)index=30;
                buttonLabels[i].text=Word(index);
                bool selected=playback!=null&&((buttonWords[i]==16&&playback.reducedEffects)||(buttonWords[i]==17&&playback.userMuted));
                buttonLabels[i].color=selected?new Color(1f,.75f,.35f):new Color(.96f,.94f,.88f);
            }
            if(startButton!=null)startButton.gameObject.SetActive(!_confirm&&parade._LocalCanStart());
            if(requestButton!=null)requestButton.gameObject.SetActive(_confirm&&parade._LocalCanStart());
            if(cancelButton!=null)cancelButton.gameObject.SetActive(_confirm||(phase>=1&&phase<=5&&host));
            if(approveButton!=null)approveButton.gameObject.SetActive(needs);
            if(declineButton!=null)declineButton.gameObject.SetActive(needs);
        }
        private string Word(int n){int i=_lang*wordCount+n;return words!=null&&i>=0&&i<words.Length?words[i]:"";}
    }
}
