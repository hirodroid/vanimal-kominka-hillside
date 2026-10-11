using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
namespace KominkaHillside
{
    // IMP-DOG-046 / 047 (2026-10-11): one selection per category (0 foam / 1 water / 2 finish) plus the platform (3), per station.
    // All three tools can be out at once (Q1). A held check looks only at the requested category's pickup slots, so the platform and the
    // other categories never wait for a held tool (Q3). Reset clears every category nobody holds and the platform (Q2; code 3 = a held
    // tool stayed out). Item ids keep the 17-item numbering (new items take 17+); itemCategories[id] < 0 = not offered (dropped by
    // DES-DOG-008, or not built yet: Q4). Every array below is written by WashPremium/48 (owned by the code) and checked by 44.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_WashSelection : UdonSharpBehaviour
    {
        public KH_WashTool[] slots;
        public VRC_Pickup[] pickups;
        public BoxCollider[] pickupColliders;
        public int[] slotCategories;          // by slot: its category, -1 = no offered item uses it
        public Transform[] categoryHomes;     // 3: the foam / water / finish places on the tray (each slot's KH_WashTool.home)
        public GameObject[] models;           // by item id; null = not in the scene
        public int[] itemCategories;          // by item id: 0..2 tools, 3 platform, -1 not offered
        public int[] itemSlots;               // by item id: the pickup slot, -1 for platforms and items not offered
        public Transform[] nozzleAnchors;
        public Transform[] gripAnchors;
        // By item id: non-null = orientation Gun with this Exact Gun (the foam cannon, BUG-DOG-010; the water pistol and the hose,
        // IMP-DOG-049: their FBX *_GunHold), null = Grip with gripAnchors.
        // Per item, not per slot, so one slot can hold a watering can (Grip) and a water gun (Gun) (IMP-DOG-049).
        public Transform[] gunHolds;
        public Vector3[] colliderCenters;
        public Vector3[] colliderSizes;
        public float[] pulseDurations;        // by item id: KH_WashTool.pulseSeconds (0 toggle, > 0 one shot, KH_WashTool.PulseHold hold, KH_WashTool.PulseTilt tilt to pour)
        // By item id: KH_WashTool.sprayRange (the wash ray's reach, m). Per item because the water slot is shared by the watering can
        // (1.5), the hose (2.5) and the water pistol (4.0) (IMP-DOG-049). Declared null: WashPremium/48 / 49 write it (vrc-udonsharp §4).
        public float[] sprayRanges;
        public ParticleSystem[] itemSprays;
        public AudioClip[] itemSounds;
        public float[] bathDepths;            // by item id: the support's mount depth under the platform collider
        public Renderer[] freeTableRenderers;
        public KH_WashSupportVisual support;
        public float freeBaseY;
        public AudioSource appearSound;
        public ParticleSystem appearFx;
        public Transform toolHome;
        [UdonSynced] public int selectedFoam=-1;
        [UdonSynced] public int selectedWater=-1;
        [UdonSynced] public int selectedFinish=-1;
        [UdonSynced] public int selectedBath=-1;
        [UdonSynced] public int revision;
        [UdonSynced] public int resultPlayer=-1;
        [UdonSynced] public int resultCode;
        [UdonSynced] public int resultSerial;
        public const int Categories=3, CategoryBath=3;
        public const int ResultApplied=0, ResultHeld=1, ResultStale=2, ResultKeptHeld=3;
        private int[] _shownTools;   // by category: the item whose model is out (-1 none, -2 not yet applied)
        private int[] _slotItem;     // by slot: the item the pickup is configured for (-1 none, -2 not yet applied)
        private int _shownBath=-2, _seenRevision=-1;
        private float _lastRequest=-10f;
        private bool _initialized;
        private bool _receivedState;
        public int localResultSerial, localResultCode;
        void Start(){_Apply(false);SendCustomEventDelayedSeconds(nameof(_Poll),.25f);}
        public void _Poll(){_Apply(_initialized);SendCustomEventDelayedSeconds(nameof(_Poll),.25f);}
        public override void OnDeserialization(){bool animate=_receivedState;_Apply(animate);_receivedState=true;}
        public override void OnOwnershipTransferred(VRCPlayerApi player){_Apply(false);if(Networking.IsOwner(gameObject))RequestSerialization();}
        public int _Category(int item){return itemCategories!=null&&item>=0&&item<itemCategories.Length?itemCategories[item]:-1;}
        /// <summary>The synced selection of a category (3 = platform), -1 when none or not a valid offered item of that category.</summary>
        public int _Selected(int category){return _Valid(category==0?selectedFoam:category==1?selectedWater:category==2?selectedFinish:category==CategoryBath?selectedBath:-1,category);}
        /// <summary>The tool whose model is out here (differs from _Selected only while a held one waits to be put down).</summary>
        public int _ShownItem(int category){return _shownTools!=null&&category>=0&&category<Categories?_shownTools[category]:-1;}
        int _Valid(int item,int category)
        {
            if(item<0||category<0||models==null||item>=models.Length||models[item]==null||_Category(item)!=category)return -1;
            if(category<Categories&&(itemSlots==null||item>=itemSlots.Length||itemSlots[item]<0||slots==null||itemSlots[item]>=slots.Length))return -1;
            return item;
        }
        void SetSelected(int category,int item){if(category==0)selectedFoam=item;else if(category==1)selectedWater=item;else if(category==2)selectedFinish=item;else if(category==CategoryBath)selectedBath=item;}
        /// <summary>Held as far as this client sees: any pickup slot of that category (the outgoing and the incoming one).</summary>
        public bool _CategoryHeld(int category)
        {
            if(category<0||category>=Categories||slots==null||slotCategories==null)return false;
            for(int s=0;s<slots.Length&&s<slotCategories.Length;s++)if(slotCategories[s]==category&&slots[s]!=null&&slots[s]._HeldNow())return true;
            return false;
        }
        public void _Request(int item)
        {
            if(!Utilities.IsValid(Networking.LocalPlayer))return;
            SendCustomNetworkEvent(NetworkEventTarget.Owner,nameof(ReqSelect),item,revision);
        }
        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqSelect(int item,int expectedRevision)
        {
            var p=NetworkCalling.CallingPlayer;
            if(!Networking.IsOwner(gameObject)||!Utilities.IsValid(p)||item < -1||(p.GetPosition()-transform.position).sqrMagnitude>36f)return;
            // Q4: only offered items. Any id can arrive here; the board hiding an item is not the gate.
            int category=item<0?-1:_Category(item);
            if(item>=0&&_Valid(item,category)<0)return;
            if(Time.time-_lastRequest<.3f)return;
            _lastRequest=Time.time;resultPlayer=p.playerId;resultSerial++;
            resultCode=_Decide(item,expectedRevision);
            if(resultCode==ResultApplied||resultCode==ResultKeptHeld){revision++;_Apply(true);}
            RequestSerialization();Reply(p.playerId);
            // Only the pickup's current owner writes its position. No ownership stealing, no all-client teleport.
            if(resultCode==ResultApplied&&category>=0&&category<Categories)slots[itemSlots[item]].SendCustomNetworkEvent(NetworkEventTarget.Owner,"ReqPremiumReturn");
        }
        /// <summary>The Owner's decision on one request: writes the synced selection only, returns the result code. No network calls
        /// (WashPremium/44 runs it in Edit mode). item -1 = reset (Q2), a platform item never waits for a held tool (Q3).</summary>
        public int _Decide(int item,int expectedRevision)
        {
            int category=item<0?-1:_Category(item);
            if(category>=0&&category<Categories&&_CategoryHeld(category))return ResultHeld;
            if(expectedRevision!=revision)return ResultStale;
            if(item<0)
            {
                bool kept=false;
                for(int c=0;c<Categories;c++){if(!_CategoryHeld(c))SetSelected(c,-1);else if(_Selected(c)>=0)kept=true;}
                selectedBath=-1;
                return kept?ResultKeptHeld:ResultApplied;
            }
            SetSelected(category,item);
            return ResultApplied;
        }
        void Reply(int playerId){SendCustomNetworkEvent(NetworkEventTarget.All,nameof(NetSelectionResult),playerId,resultCode);}
        [NetworkCallable(maxEventsPerSecond: 4)]
        public void NetSelectionResult(int playerId,int code){var p=NetworkCalling.CallingPlayer;if(!Utilities.IsValid(p)||!Networking.IsOwner(p,gameObject)||!Utilities.IsValid(Networking.LocalPlayer)||Networking.LocalPlayer.playerId!=playerId)return;localResultCode=code;localResultSerial++;}
        bool _Ready()
        {
            if(models==null||slots==null||pickups==null||pickupColliders==null||slotCategories==null||itemCategories==null||itemSlots==null||categoryHomes==null)return false;
            int n=models.Length,k=slots.Length;
            if(n==0||k==0||pickups.Length!=k||pickupColliders.Length!=k||slotCategories.Length!=k||itemCategories.Length!=n||itemSlots.Length!=n||categoryHomes.Length!=Categories)return false;
            if(nozzleAnchors==null||nozzleAnchors.Length!=n||gripAnchors==null||gripAnchors.Length!=n||gunHolds==null||gunHolds.Length!=n||bathDepths==null||bathDepths.Length!=n)return false;
            if(sprayRanges==null||sprayRanges.Length!=n)return false;
            return colliderCenters!=null&&colliderCenters.Length==n&&colliderSizes!=null&&colliderSizes.Length==n&&pulseDurations!=null&&pulseDurations.Length==n&&itemSprays!=null&&itemSprays.Length==n&&itemSounds!=null&&itemSounds.Length==n;
        }
        public void _Apply(bool animate)
        {
            if(!_Ready())return;
            int n=models.Length,k=slots.Length;
            if(_shownTools==null||_shownTools.Length!=Categories){_shownTools=new int[Categories];for(int c=0;c<Categories;c++)_shownTools[c]=-2;}
            if(_slotItem==null||_slotItem.Length!=k){_slotItem=new int[k];for(int s=0;s<k;s++)_slotItem[s]=-2;}
            int bath=_Selected(CategoryBath);
            bool same=_initialized&&_seenRevision==revision&&_shownBath==bath;
            for(int c=0;c<Categories;c++)if(_shownTools[c]!=_Selected(c))same=false;
            if(same)return;
            int appearAt=-1;
            for(int c=0;c<Categories;c++)
            {
                int want=_Selected(c);
                if(want==_shownTools[c])continue;
                // A pickup may arrive after the owner's decision: only that category keeps its current model until it is put down;
                // the other categories and the platform change now (Q3). The first state received restores even when held.
                if(_initialized&&_receivedState&&_shownTools[c]>=-1&&_CategoryHeld(c))continue;
                if(want>=0)appearAt=c;
                _shownTools[c]=want;
            }
            for(int i=0;i<n;i++)
            {
                if(models[i]==null)continue;
                int c=_Category(i);
                bool on=c>=0&&c<Categories?_shownTools[c]==i:c==CategoryBath&&i==bath;
                if(models[i].activeSelf!=on)models[i].SetActive(on);
            }
            for(int s=0;s<k;s++)
            {
                int item=-1;
                for(int c=0;c<Categories;c++){int it=_shownTools[c];if(it>=0&&itemSlots[it]==s)item=it;}
                if(item==_slotItem[s])continue;
                _slotItem[s]=item;ConfigureSlot(s,item);
            }
            for(int i=0;freeTableRenderers!=null&&i<freeTableRenderers.Length;i++)if(freeTableRenderers[i]!=null)freeTableRenderers[i].enabled=bath<0;
            if(support!=null){float depth=bath>=0?bathDepths[bath]:0f;support.mountBelowCollider=depth;support.baseLocalY=freeBaseY-depth;support._RefreshVisual();}
            bool changed=_initialized&&(revision!=_seenRevision||appearAt>=0);
            if(animate&&changed)
            {
                Transform at=appearAt>=0&&categoryHomes[appearAt]!=null?categoryHomes[appearAt]:toolHome;
                if(appearSound!=null){if(at!=null)appearSound.transform.position=at.position;appearSound.Play();}
                if(appearFx!=null){if(at!=null)appearFx.transform.position=at.position;appearFx.Play();}
            }
            _shownBath=bath;_seenRevision=revision;_initialized=true;
        }
        void ConfigureSlot(int s,int item)
        {
            var t=slots[s];var p=pickups[s];var col=pickupColliders[s];
            if(t==null||p==null||col==null)return;
            bool active=item>=0;
            t._SetSelected(active);p.pickupable=active;col.enabled=active;
            if(!active){if(t.spray!=null)t.spray.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);t.spray=null;return;}
            t.nozzle=nozzleAnchors[item];t.spray=itemSprays[item];t.pulseSeconds=pulseDurations[item];t.sprayRange=sprayRanges[item];
            if(t.sprayAudio!=null)t.sprayAudio.clip=itemSounds[item];
            Transform gun=gunHolds[item];
            if(gun!=null){p.orientation=VRC_Pickup.PickupOrientation.Gun;p.ExactGun=gun;}
            else{p.orientation=VRC_Pickup.PickupOrientation.Grip;p.ExactGrip=gripAnchors[item];}
            col.center=colliderCenters[item];col.size=colliderSizes[item];
        }
    }
}
