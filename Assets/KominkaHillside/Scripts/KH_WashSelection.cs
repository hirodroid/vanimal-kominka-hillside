using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
using VRC.Economy;
namespace KominkaHillside
{
    // IMP-DOG-046 / 047 (2026-10-11): one selection per category (0 foam / 1 water / 2 finish) plus the platform (3), per station.
    // All three tools can be out at once (Q1). A held check looks only at the requested category's pickup slots, so the platform and the
    // other categories never wait for a held tool (Q3). Reset clears every category nobody holds and the platform (Q2; code 3 = a held
    // tool stayed out). Item ids keep the 17-item numbering (new items take 17+); itemCategories[id] < 0 = not offered (dropped by
    // DES-DOG-008, or not built yet: Q4). Every array below is written by WashPremium/48 (owned by the code) and checked by 44.
    // IMP-DOG-056 (2026-10-14, contract after the Codex consultation): the purchase gate.
    //  - One gate, on the Owner: ReqSelect asks _SetState(category) and _Decide refuses (Locked / Pending); the board never gates.
    //  - Ownership has three states per set (KD_ShopItem.CeInstanceOwnership: Unknown / Owned / None): 1 owner in the instance = everyone.
    //  - Back to free per station: a None category stays until _StationIdle (no tool of this station held, no dog on the stand, 60 s).
    //  - The guarantee holds at the Owner's observation: a purchase the Owner has not received yet cannot be ordered against a revert.
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
        // IMP-DOG-044 (2026-10-11): by item id, the stand's lowest height (m) while that platform is selected here (0 = none needed: the
        // ground under it is not higher than its bottom). Written by WashPremium/50 (measured per station), checked by 44. Declared null and
        // optional (not in _Ready): a scene before 50 behaves as before. _Apply writes it to call.minLift on every client.
        public float[] bathMinLifts;
        public KH_WashStationCall call;       // this station's stand (WashPremium/50 wires it); null = no minimum height
        // IMP-ENV-013 (2026-10-12): by item id, true = while this platform is selected the stand keeps its own 1.3 x 0.7 size (the pool,
        // IMP-DOG-044: its rim would flatten if scaled per dog). Written by Wash/22, checked by Wash/23. Declared null and optional (not in
        // _Ready): no array = every platform scales with the dog. _Apply writes _KeepsSize(selectedBath) to call.standKeep on every client.
        public bool[] bathKeepsSize;
        public Renderer[] freeTableRenderers;
        public KH_WashSupportVisual support;
        public float freeBaseY;
        public AudioSource appearSound;
        public ParticleSystem appearFx;
        public Transform toolHome;
        // IMP-DOG-056: by category (0 foam / 1 water / 2 finish / 3 station) the set's KD_ShopItem (KH_Dogs/KD_ShopCatalog/KD_ShopItem_Wash_*),
        // and this station's reset board (its tools = every tool of this station, the free ones too). Declared null, written by
        // WashPremium/54, checked by 44. Not in _Ready: an unwired scene still applies selections, the gate only treats every set as None.
        public KominkaDogRun.KD_ShopItem[] setItems;
        public KH_WashStationReset reset;
        // Edit-mode checks only (WashPremium/44): >= 0 replaces the seconds since this station was last seen busy (as debugHeldOverride).
        public float debugIdleSecondsOverride=-1f;
        [UdonSynced] public int selectedFoam=-1;
        [UdonSynced] public int selectedWater=-1;
        [UdonSynced] public int selectedFinish=-1;
        [UdonSynced] public int selectedBath=-1;
        [UdonSynced] public int revision;
        [UdonSynced] public int resultPlayer=-1;
        [UdonSynced] public int resultCode;
        [UdonSynced] public int resultSerial;
        public const int Categories=3, CategoryBath=3;
        public const int ResultApplied=0, ResultHeld=1, ResultStale=2, ResultKeptHeld=3, ResultLocked=4, ResultPending=5;
        // IMP-DOG-056 §2.3: the end of a wash = nothing of this station held and no dog on the stand for this long; the Owner re-checks
        // every RevertPollSeconds while a revert waits (or a set's state is still Unknown).
        public const float IdleGraceSeconds=60f, RevertPollSeconds=2f;
        private int[] _shownTools;   // by category: the item whose model is out (-1 none, -2 not yet applied)
        private int[] _slotItem;     // by slot: the item the pickup is configured for (-1 none, -2 not yet applied)
        private int _shownBath=-2, _seenRevision=-1;
        private float _lastRequest=-10f;
        private bool _initialized;
        private bool _receivedState;
        public int localResultSerial, localResultCode;
        // 2026-10-12 (VER-ENV-002 perf): _Poll runs only while it has work: a category whose new tool waits for the held one to be put
        // down (_shownTools[c] != _Selected(c), left so only by the held-wait `continue` in _Apply), or before the first apply. Otherwise
        // _Apply returns at `same` and the tick did nothing. Every runtime _Apply caller (Start, _Poll, OnDeserialization,
        // OnOwnershipTransferred, ReqSelect) re-arms it through PollIfPending; the drop on a remote client is seen only by this poll
        // (no OnDrop there), so it keeps the 0.25 s cadence while a wait exists. Never scheduled from _Apply itself (Edit mode calls it).
        private bool _polling;   // a _Poll is scheduled (one at a time)
        // IMP-DOG-056 (Owner only): _revertPending = a None category is kept until the station is idle; _revertPolling = a _SlowPoll is
        // scheduled (one at a time, as _polling). Delayed events do not follow an ownership transfer: the new Owner re-arms it.
        private bool _revertPending, _revertPolling, _warnedUnwired;
        private float _lastBusyAt=-1000f;
        void Start(){_Apply(false);PollIfPending();}
        public void _Poll(){_polling=false;_Apply(_initialized);PollIfPending();}
        void PollIfPending(){if(_polling||!_PollPending())return;_polling=true;SendCustomEventDelayedSeconds(nameof(_Poll),.25f);}
        /// <summary>Would the next _Poll tick do anything: not applied yet, or a held-wait category (see _Apply).</summary>
        bool _PollPending()
        {
            if(!_initialized||_shownTools==null)return true;
            for(int c=0;c<Categories;c++)if(_shownTools[c]!=_Selected(c))return true;
            return false;
        }
        public override void OnDeserialization(){bool animate=_receivedState;_Apply(animate);_receivedState=true;PollIfPending();}
        public override void OnOwnershipTransferred(VRCPlayerApi player){_Apply(false);if(Networking.IsOwner(gameObject))RequestSerialization();PollIfPending();_ReevaluateLater();}
        // IMP-DOG-056 §2.3: every trigger schedules the same _Reevaluate (_SlowPoll, <= RevertPollSeconds later) instead of reverting inside
        // the event: KD_ShopItem receives the same event (the leaver dropped, the purchase noted) in an undefined order, so a state read here
        // could still be the old one. A leave is scheduled whatever the state reads now (the leaver may still count as an owner).
        public override void OnPlayerLeft(VRCPlayerApi player){_ReevaluateLater();}
        public override void OnPurchasesLoaded(IProduct[] products,VRCPlayerApi player){_ReevaluateLater();}
        public override void OnPurchaseConfirmed(IProduct product,VRCPlayerApi player,bool purchasedNow){_ReevaluateLater();}
        public override void OnPurchaseConfirmedMultiple(IProduct product,VRCPlayerApi player,bool purchasedNow,int quantity){_ReevaluateLater();}
        /// <summary>IMP-ENV-013: does platform `bath` keep the stand at its own size (-1 / out of range / no array: no). Pure (Wash/23).</summary>
        public bool _KeepsSize(int bath){return bath>=0&&bathKeepsSize!=null&&bath<bathKeepsSize.Length&&bathKeepsSize[bath];}
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
            // IMP-DOG-056: the set of the requested category, as this Owner observes the instance (not the requester's own purchase). Reset
            // (-1) never asks: going back to free is always allowed.
            int setState=category==-1?KominkaDogRun.KD_ShopItem.InstanceOwned:_SetState(category);
            resultCode=_Decide(item,expectedRevision,setState);
            if(resultCode==ResultApplied||resultCode==ResultKeptHeld){revision++;_Apply(true);PollIfPending();}
            RequestSerialization();Reply(p.playerId);
            // Only the pickup's current owner writes its position. No ownership stealing, no all-client teleport.
            if(resultCode==ResultApplied&&category>=0&&category<Categories)slots[itemSlots[item]].SendCustomNetworkEvent(NetworkEventTarget.Owner,"ReqPremiumReturn");
        }
        /// <summary>The Owner's decision on one request: writes the synced selection only, returns the result code. No network calls and no
        /// Store (WashPremium/44 runs it in Edit mode). item -1 = reset (Q2), a platform item never waits for a held tool (Q3).
        /// IMP-DOG-056: setState = the requested category's set (KD_ShopItem.Instance*). Order: Held, Stale, reset (whatever setState),
        /// Unknown -> Pending, anything but Owned -> Locked, apply. A refusal leaves the selection and revision as they were.</summary>
        public int _Decide(int item,int expectedRevision,int setState)
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
            if(setState==KominkaDogRun.KD_ShopItem.InstanceUnknown)return ResultPending;
            if(setState!=KominkaDogRun.KD_ShopItem.InstanceOwned)return ResultLocked;
            SetSelected(category,item);
            return ResultApplied;
        }
        /// <summary>IMP-DOG-056: the set of a category (0..3) in this instance, as this client observes it (KD_ShopItem.Instance*). Not wired
        /// (no setItems / that entry null) = None: never free by accident (44 checks the wiring; one warning here).</summary>
        public int _SetState(int category)
        {
            if(setItems==null||category<0||category>=setItems.Length||setItems[category]==null)
            {
                if(!_warnedUnwired){_warnedUnwired=true;Debug.LogWarning("[KH_WashSelection] "+gameObject.name+": setItems not wired for category "+category.ToString()+" (run WashPremium/54); the set counts as not owned");}
                return KominkaDogRun.KD_ShopItem.InstanceNone;
            }
            return setItems[category].CeInstanceOwnership();
        }
        /// <summary>IMP-DOG-056 (board, every client): category c is still out although nobody here owns its set (its owner left): it stays until
        /// the wash ends, then the Owner puts it back to free. Derived from the synced selection and this client's own observation, so a
        /// non-Owner board shows it too (the Owner's _revertPending flag never leaves the Owner).</summary>
        public bool _RevertPendingFor(int category){return _Selected(category)>=0&&_SetState(category)==KominkaDogRun.KD_ShopItem.InstanceNone;}
        public bool _RevertPending(){for(int c=0;c<=CategoryBath;c++)if(_RevertPendingFor(c))return true;return false;}
        bool _AnySelected(){for(int c=0;c<=CategoryBath;c++)if(_Selected(c)>=0)return true;return false;}
        /// <summary>IMP-DOG-056 §2.3: the end of a wash at this station (one function, to be replaced if the user decides otherwise):
        /// (a) no tool of this station held now (reset.tools: the free and the premium ones; slots when no reset is wired), (b) no dog on the
        /// stand now, (c) IdleGraceSeconds since the latest of: each tool's last seen hold (KH_WashTool._LastHeldAt, 2 Hz grain: a use between
        /// the Owner's 2 s polls counts), the dog last seen on the stand / the wait's start (_lastBusyAt, the poll's grain: a stay is long).
        /// Edit mode: debugIdleSecondsOverride >= 0 replaces the seconds since that latest time.</summary>
        public bool _StationIdle()
        {
            float now=Time.time;
            bool busy=false;
            if(call!=null&&call._HeldDog()>=0){busy=true;_lastBusyAt=now;}
            float last=_lastBusyAt;
            KH_WashTool[] tools=reset!=null?reset.tools:slots;
            for(int i=0;tools!=null&&i<tools.Length;i++)
            {
                KH_WashTool t=tools[i];if(t==null)continue;
                if(t._HeldNow())busy=true;
                float h=t._LastHeldAt();if(h>last)last=h;
            }
            if(busy)return false;
            float idleFor=debugIdleSecondsOverride>=0f?debugIdleSecondsOverride:now-last;
            return idleFor>=IdleGraceSeconds;
        }
        /// <summary>IMP-DOG-056 §2.3 (Owner only; anyone else returns at once): puts each selected category whose set nobody owns (None) back to
        /// free once the station is idle, through the same path as an applied request (revision, _Apply, serialization; late joiners restore
        /// from the synced values). The state is read again right before the revert (a purchase noted since). Keeps a slow poll while a None
        /// category waits for the idle station or a set is still Unknown; stops when everything is resolved. Never touches the request reply
        /// (resultPlayer / resultCode / resultSerial).</summary>
        public void _Reevaluate()
        {
            if(!Networking.IsOwner(gameObject))return;
            bool changed=false,waitIdle=false,waitState=false;int idle=-1;   // idle: -1 not asked yet, 0 busy, 1 idle
            for(int c=0;c<=CategoryBath;c++)
            {
                if(_Selected(c)<0)continue;
                int st=_SetState(c);
                if(st==KominkaDogRun.KD_ShopItem.InstanceOwned)continue;
                if(st!=KominkaDogRun.KD_ShopItem.InstanceNone){waitState=true;continue;}
                if(idle<0){if(!_revertPending)_lastBusyAt=Time.time;idle=_StationIdle()?1:0;}   // a new wait starts the 60 s now
                if(idle==0){waitIdle=true;continue;}
                st=_SetState(c);   // Codex 2: once more, right before the revert
                if(st==KominkaDogRun.KD_ShopItem.InstanceOwned)continue;
                if(st!=KominkaDogRun.KD_ShopItem.InstanceNone){waitState=true;continue;}
                SetSelected(c,-1);changed=true;
            }
            _revertPending=waitIdle;
            if(changed){revision++;_Apply(true);RequestSerialization();PollIfPending();}
            if(waitIdle||waitState)_ArmSlowPoll();
        }
        public void _SlowPoll(){_revertPolling=false;_Reevaluate();}
        void _ArmSlowPoll(){if(_revertPolling)return;_revertPolling=true;SendCustomEventDelayedSeconds(nameof(_SlowPoll),RevertPollSeconds);}
        /// <summary>A trigger (leave, purchase information, ownership): the Owner re-evaluates on the next slow poll if anything is out.</summary>
        void _ReevaluateLater(){if(Networking.IsOwner(gameObject)&&_AnySelected())_ArmSlowPoll();}
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
            // IMP-ENV-013: before any model is shown, so a platform that keeps the stand's size (the pool) is never drawn squeezed: the stand
            // is put at its own size now (Codex review 2026-10-12). Leaving it (keep -> false) eases in KH_WashStationCall.StandStep as usual.
            if(call!=null){bool keep=_KeepsSize(bath);if(call.standKeep!=keep){call.standKeep=keep;if(keep)call._StandSnapNow();}}
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
            if(call!=null)call.minLift=bath>=0&&bathMinLifts!=null&&bath<bathMinLifts.Length?bathMinLifts[bath]:0f;   // IMP-DOG-044: derived here from the synced selectedBath (not synced itself)
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
