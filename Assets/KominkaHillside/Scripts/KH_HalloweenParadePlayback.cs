using UdonSharp;
using UnityEngine;
using UnityEngine.Playables;
using VRC.SDKBase;
using KominkaDogRun;

namespace KominkaHillside
{
    // Local playback reads the authoritative show clock. It never writes dog or consent state.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public partial class KH_HalloweenParadePlayback : UdonSharpBehaviour
    {
        public KH_HalloweenParade parade;
        public PlayableDirector director;
        public GameObject stageRoot;
        public AudioSource music;
        public KD_BgmPlayer bgmPlayer;
        public KH_NightMode nightMode;
        public Renderer[] detailRenderers;
        public GameObject[] costumes;
        public ParticleSystem[] effects;
        public float[] effectStarts, effectEnds;
        public Vector3[] audienceRoute;
        public float fullVolumeDistance=14f, silentDistance=28f, baseVolume=.55f;
        public float userVolume=1f;
        public bool userMuted;
        public bool reducedEffects;
        private bool _playing;
        private int _request=-1;
        private float _gain;
        private float _nextRange;
        private bool _lastReduced;
        private bool _costumesVisible;
        // net: callable-ok: personal audio state only
        public bool IsAudible { get { return _playing && _gain>.01f && PersonalVolume>.001f; } }
        // Hillside has one BGM volume slider and no live/bedroom rows. Its gain also controls parade music.
        // BGM UserEnabled controls background music only; parade has its own independent mute.
        public float PersonalVolume { get { return userMuted||bgmPlayer==null?0f:Mathf.Clamp01(userVolume)*bgmPlayer.UserVolume; } }

        private void Start()
        {
            if(stageRoot!=null)stageRoot.SetActive(false);
            if(music!=null){music.playOnAwake=false;music.loop=false;music.spatialBlend=0;music.Stop();}
            _lastReduced=!reducedEffects;
        }
        private void Update()
        {
            // An Udon update can arrive after OnDisable. Never restart a disabled playback.
            bool wired=enabled&&gameObject.activeInHierarchy&&parade!=null&&director!=null&&stageRoot!=null&&music!=null;
            float elapsed=wired?parade.Elapsed:0f;
            bool want=wired&&parade.Phase==5&&elapsed>=0f&&elapsed<parade.durationSeconds;
            if(nightMode!=null)nightMode.paradeDarkness=want?NightEnvelope(elapsed,parade.durationSeconds):0f;
            SetPlazaHidden(want);
            bool costumeWanted=wired&&(parade.Phase==4||parade.Phase==5);
            if(costumeWanted!=_costumesVisible){_costumesVisible=costumeWanted;SetCostumes(costumeWanted);}
            if(parade==null||director==null||stageRoot==null||music==null)return;
            if(!want)
            {
                if(_playing){director.Stop();music.Stop();stageRoot.SetActive(false);_playing=false;}
                _gain=0f;return;
            }
            if(!_playing||_request!=parade.RequestId)
            {
                director.Stop();music.volume=0f;stageRoot.SetActive(true);director.time=elapsed;director.Play();director.Evaluate();RestoreParticles(elapsed);
                _request=parade.RequestId;_playing=true;_nextRange=0f;
            }
            // Only correct measurable clock drift, not every-frame seeks (which reset particle history).
            if(Time.time>=_nextRange)
            {
                _nextRange=Time.time+.25f;
                if(Mathf.Abs((float)director.time-elapsed)>.35f){director.time=elapsed;director.Evaluate();RestoreParticles(elapsed);}
                _gain=AudienceGain();
            }
            float personalVolume=PersonalVolume;
            music.mute=personalVolume<=0f;
            music.volume=Mathf.MoveTowards(music.volume,baseVolume*personalVolume*_gain,Time.deltaTime);
            if(_lastReduced!=reducedEffects)
            {
                _lastReduced=reducedEffects;
                for(int i=0;detailRenderers!=null&&i<detailRenderers.Length;i++)if(detailRenderers[i]!=null)detailRenderers[i].enabled=!reducedEffects;
            }
        }
        // Pure shared-clock envelope: late join and seek see the current lighting cue.
        public static float NightEnvelope(float elapsed,float duration)
        {
            if(elapsed<0f||elapsed>=duration)return 0f;
            return .72f*Mathf.SmoothStep(0f,1f,Mathf.Min(elapsed/4f,(duration-elapsed)/5f));
        }
        private void RestoreParticles(float elapsed)
        {
            // Seeded Local single bursts, including shaped festival banks. No world-history modules.
            for(int i=0;effects!=null&&effectStarts!=null&&effectEnds!=null&&i<effects.Length&&i<effectStarts.Length&&i<effectEnds.Length;i++)
            {
                var ps=effects[i];if(ps==null)continue;
                ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                if(elapsed<effectStarts[i]||elapsed>=effectEnds[i])continue;
                ps.Simulate(elapsed-effectStarts[i],false,true,false);ps.Play(false);
            }
        }
        private float AudienceGain()
        {
            var player=Networking.LocalPlayer;
            if(!Utilities.IsValid(player)||audienceRoute==null||audienceRoute.Length<2)return 0f;
            Vector3 p=player.GetPosition();float best=1000000f;
            for(int i=1;i<audienceRoute.Length;i++)
            {
                Vector3 a=audienceRoute[i-1],d=audienceRoute[i]-a;float l=d.sqrMagnitude;
                float t=l>.0001f?Mathf.Clamp01(Vector3.Dot(p-a,d)/l):0f;
                best=Mathf.Min(best,(p-a-d*t).sqrMagnitude);
            }
            return 1f-Mathf.InverseLerp(fullVolumeDistance,Mathf.Max(fullVolumeDistance+.1f,silentDistance),Mathf.Sqrt(best));
        }
        public void _ToggleReduced(){reducedEffects=!reducedEffects;}
        public void _ToggleMute(){userMuted=!userMuted;}
        private void SetCostumes(bool shown){for(int i=0;costumes!=null&&i<costumes.Length;i++)if(costumes[i]!=null)costumes[i].SetActive(shown);}
        private void OnDisable()
        {
            SetPlazaHidden(false);
            if(nightMode!=null)nightMode.paradeDarkness=0f;
            if(director!=null)director.Stop();if(music!=null)music.Stop();
            if(stageRoot!=null)stageRoot.SetActive(false);_playing=false;_gain=0f;
            SetCostumes(false);_costumesVisible=false;
        }
    }
}
