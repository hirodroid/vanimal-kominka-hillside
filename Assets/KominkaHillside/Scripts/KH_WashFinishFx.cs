using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
namespace KominkaHillside
{
    // Cosmetic finishing has no wash cells, completion or photo-session side effects.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_WashFinishFx : UdonSharpBehaviour
    {
        public KH_WashTool[] tools;
        public Collider[] parts;
        public ParticleSystem brushSheen;
        public ParticleSystem fragrance;
        public AudioSource brushAudio;
        public AudioSource fragranceAudio;
        private float _nextSend;
        private float _nextShow;
        private int _pendingPlayer=-1,_pendingKind;
        private Vector3 _pendingPoint;
        private float _pendingUntil;
        public void _LocalFinish(int kind, Vector3 point)
        {
            if (Time.time < _nextSend) return;
            _nextSend = Time.time + (kind == 1 ? .22f : .8f);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetFinish), kind, point);
        }
        [NetworkCallable(maxEventsPerSecond: 5)]
        public void NetFinish(int kind, Vector3 point)
        {
            var p=NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p) || (kind!=1 && kind!=2) || Time.time < _nextShow || float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z)) return;
            if ((p.GetPosition()-point).sqrMagnitude>16f) return;
            _Receive(kind,point,p.playerId,true);
        }
        void _Receive(int kind,Vector3 point,int playerId,bool allowPending)
        {
            var p=VRCPlayerApi.GetPlayerById(playerId);if(!Utilities.IsValid(p)||(p.GetPosition()-point).sqrMagnitude>16f||!_NearBody(point))return;
            bool held=false;
            for(int i=0;tools!=null && i<tools.Length;i++) if(tools[i]!=null && tools[i].finishKind==kind && tools[i]._IsHeldBy(p.playerId)){held=true;break;}
            if(!held){if(allowPending&&_pendingPlayer<0){_pendingPlayer=playerId;_pendingKind=kind;_pendingPoint=point;_pendingUntil=Time.time+.8f;SendCustomEventDelayedSeconds(nameof(_RetryFinish),.1f);}return;}
            _pendingPlayer=-1;
            _nextShow=Time.time+.12f; _Show(kind,point);
        }
        public void _RetryFinish(){if(_pendingPlayer<0)return;if(Time.time>_pendingUntil){_pendingPlayer=-1;return;}_Receive(_pendingKind,_pendingPoint,_pendingPlayer,false);if(_pendingPlayer>=0)SendCustomEventDelayedSeconds(nameof(_RetryFinish),.1f);}
        public bool _NearBody(Vector3 point)
        {
            for(int i=0;parts!=null && i<parts.Length;i++) if(parts[i]!=null && parts[i].enabled && (parts[i].ClosestPoint(point)-point).sqrMagnitude<.0225f)return true;
            return false;
        }
        public void _Show(int kind,Vector3 point)
        {
            ParticleSystem fx=kind==1?brushSheen:fragrance;
            AudioSource sound=kind==1?brushAudio:fragranceAudio;
            if(fx!=null){fx.transform.position=point;fx.Emit(kind==1?3:10);}
            if(sound!=null){sound.transform.position=point;if(!sound.isPlaying)sound.Play();}
        }
    }
}
