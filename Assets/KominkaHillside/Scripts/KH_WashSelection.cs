using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
namespace KominkaHillside
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_WashSelection : UdonSharpBehaviour
    {
        public KH_WashTool[] slots;
        public VRC_Pickup[] pickups;
        public BoxCollider[] pickupColliders;
        public GameObject[] models;
        public Transform[] nozzleAnchors;
        public Transform[] gripAnchors;
        public int[] itemSlots;
        public Vector3[] colliderCenters;
        public Vector3[] colliderSizes;
        public float[] pulseDurations;
        public ParticleSystem[] itemSprays;
        public AudioClip[] itemSounds;
        public Renderer[] freeTableRenderers;
        public KH_WashSupportVisual support;
        public float freeBaseY;
        public AudioSource appearSound;
        public ParticleSystem appearFx;
        public Transform toolHome;
        [UdonSynced] public int selectedTool=-1;
        [UdonSynced] public int selectedBath=-1;
        [UdonSynced] public bool duckOn;
        [UdonSynced] public int revision;
        [UdonSynced] public int resultPlayer=-1;
        [UdonSynced] public int resultCode;
        [UdonSynced] public int resultSerial;
        private int _shownTool=-2, _shownBath=-2, _seenRevision=-1;
        private bool _shownDuck;
        private float _lastRequest=-10f;
        private bool _initialized;
        private bool _receivedState;
        public int localResultSerial, localResultCode;
        void Start(){_Apply(false);SendCustomEventDelayedSeconds(nameof(_Poll),.25f);}
        public void _Poll(){_Apply(_initialized);SendCustomEventDelayedSeconds(nameof(_Poll),.25f);}
        public override void OnDeserialization(){bool animate=_receivedState;_Apply(animate);_receivedState=true;}
        public override void OnOwnershipTransferred(VRCPlayerApi player){_Apply(false);if(Networking.IsOwner(gameObject))RequestSerialization();}
        public bool _Busy()
        {for(int i=0;slots!=null&&i<slots.Length;i++)if(slots[i]!=null&&slots[i]._HeldNow())return true;return false;}
        public void _Request(int item)
        {
            if(!Utilities.IsValid(Networking.LocalPlayer))return;
            SendCustomNetworkEvent(NetworkEventTarget.Owner,nameof(ReqSelect),item,revision,!duckOn);
        }
        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqSelect(int item,int expectedRevision,bool desiredDuck)
        {
            var p=NetworkCalling.CallingPlayer;
            if(!Networking.IsOwner(gameObject)||!Utilities.IsValid(p)||item < -1||item>16 || (p.GetPosition()-transform.position).sqrMagnitude>36f)return;
            if(Time.time-_lastRequest<.3f)return;
            _lastRequest=Time.time;resultPlayer=p.playerId;resultSerial++;
            if(_Busy()){resultCode=1;Reply(p.playerId);RequestSerialization();return;}
            if(expectedRevision!=revision){resultCode=2;Reply(p.playerId);RequestSerialization();return;}
            resultCode=0;
            if(item<0){selectedTool=-1;selectedBath=-1;duckOn=false;}
            else if(item<13)selectedTool=item;
            else if(item<16)selectedBath=item;
            else duckOn=desiredDuck;
            revision++;_Apply(true);RequestSerialization();Reply(p.playerId);
            // Only the pickup's current owner writes its position. No ownership stealing, no all-client teleport.
            if(item>=0&&item<13)slots[itemSlots[item]].SendCustomNetworkEvent(NetworkEventTarget.Owner,"ReqPremiumReturn");
        }
        void Reply(int playerId){SendCustomNetworkEvent(NetworkEventTarget.All,nameof(NetSelectionResult),playerId,resultCode);}
        [NetworkCallable(maxEventsPerSecond: 4)]
        public void NetSelectionResult(int playerId,int code){var p=NetworkCalling.CallingPlayer;if(!Utilities.IsValid(p)||!Networking.IsOwner(p,gameObject)||!Utilities.IsValid(Networking.LocalPlayer)||Networking.LocalPlayer.playerId!=playerId)return;localResultCode=code;localResultSerial++;}
        public void _Apply(bool animate)
        {
            if(models==null||models.Length!=17||slots==null||slots.Length!=7)return;
            if(selectedTool < -1 || selectedTool >12 || selectedBath < -1 || selectedBath >15 || (selectedBath>=0&&selectedBath<13))return;
            if(_initialized&&_seenRevision==revision&&_shownTool==selectedTool&&_shownBath==selectedBath&&_shownDuck==duckOn)return;
            // A pickup may arrive after the owner's decision. Keep its current model until it is put down.
            if(_initialized && _receivedState && _Busy())return;
            for(int i=0;i<13;i++)if(models[i]!=null)models[i].SetActive(i==selectedTool);
            int activeSlot=selectedTool>=0?itemSlots[selectedTool]:-1;
            for(int s=0;s<slots.Length;s++)
            {
                bool active=s==activeSlot;slots[s]._SetSelected(active);pickups[s].pickupable=active;pickupColliders[s].enabled=active;
                if(!active&&slots[s].spray!=null)slots[s].spray.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if(selectedTool>=0)
            {
                var t=slots[activeSlot];t.nozzle=nozzleAnchors[selectedTool];t.spray=itemSprays[selectedTool];t.pulseSeconds=pulseDurations[selectedTool];
                t.sprayAudio.clip=itemSounds[selectedTool];pickups[activeSlot].ExactGrip=gripAnchors[selectedTool];
                pickupColliders[activeSlot].center=colliderCenters[selectedTool];pickupColliders[activeSlot].size=colliderSizes[selectedTool];
            }
            for(int i=13;i<16;i++)models[i].SetActive(i==selectedBath);
            models[16].SetActive(duckOn);
            for(int i=0;freeTableRenderers!=null&&i<freeTableRenderers.Length;i++)if(freeTableRenderers[i]!=null)freeTableRenderers[i].enabled=selectedBath<0;
            if(support!=null){float depth=selectedBath==13?.038f:selectedBath==14?.034f:selectedBath==15?.049f:0f;support.mountBelowCollider=depth;support.baseLocalY=freeBaseY-depth;support._RefreshVisual();}
            bool changed=_initialized&&revision!=_seenRevision;
            if(animate&&changed){if(appearSound!=null)appearSound.Play();if(appearFx!=null)appearFx.Play();}
            _shownTool=selectedTool;_shownBath=selectedBath;_shownDuck=duckOn;_seenRevision=revision;_initialized=true;
        }
    }
}
