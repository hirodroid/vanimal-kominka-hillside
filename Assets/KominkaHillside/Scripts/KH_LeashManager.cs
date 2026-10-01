using UdonSharp;
using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // One authority arbitrates all six dogs, so concurrent requests cannot exceed a hand's limit.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public partial class KH_LeashManager : UdonSharpBehaviour
    {
        // Scene references are allocated by Build. Do not give them same-length program defaults:
        // U#'s array serializer can mutate that default without creating a per-behaviour override.
        public Transform[] dogRoots;
        public Transform[] collarAnchors;
        public KominkaDogRun.KD_DogFetchController[] fetchers;
        public Transform[] roamCenters;
        public KH_LeashClip[] localClips;
        public KH_FrisbeeCatch[] frisbeeCatchers; // per dog, null where the dog has no disc yet (IMP-DOG-005)
        public KH_WalkRoute route;
        public KominkaDogRun.KD_GroupActivities activities;
        // IMP-DOG-016: dogs called over from the Call tab (KD_SleepBoard) cannot be leashed until released there.
        // Wired by Menu/6 Setup Sleep (and Walking/3 Build keeps it). null = no exclusion (before stage 2).
        public KominkaDogRun.KD_SleepBoard sleepBoard;
        // Stopover spots along the trail. Wired by Build; the dogs only consult it while walking.
        public KH_WalkStopovers stopovers;
        // Leash stations: one slot per tool side. stationSlots[i] belongs to station stationOf[i], side slotHand[i].
        public Transform[] stationSlots;
        public int[] slotHand;
        public int[] stationOf;
        [UdonSynced] public int[] walkers = new int[] {-1,-1,-1,-1,-1,-1};
        [UdonSynced] public int[] hands = new int[] {0,0,0,0,0,0}; // 0 left, 1 right; not SDK enum values.
        [UdonSynced] public Vector3[] releasedHomes = new Vector3[6];
        [UdonSynced] private bool homesInitialized;
        // Per-dog destination (index into route.destinationNodes). Written from the attaching player's own
        // choice, so newly attached dogs inherit what that player already picked.
        [UdonSynced] public int[] destinations = new int[] {1,1,1,1,1,1};
        [Header("Tuning: provisional until Play")]
        public float leashLength = 3f;
        public float attachReach = 4f;
        // BUG-DOG-002: handle placement, shared by all six collars so there is one place to tune.
        [Tooltip("持ち手の輪の中心から握り位置までの距離 (m)。輪のローカル +Y 方向。0.062 で輪の後ろ側の帯が拳に入り、輪と金具は手の前へ出る")]
        public float handleGripOffset = 0.062f;
        [Tooltip("紐の軸まわりの輪の面の傾き (度)。右手の値で、左手は符号を反転して左右の感覚をそろえる。輪が縦/横に見えるときは ±90 単位で回す")]
        public float handleRollDegrees = 0f;
        // Walking pace, upper two of the four tiers (IMP-DOG-009). The lower two (walkSpeed 1.1 / runSpeed 2.4)
        // live on each KD_DogWanderController because they are the dog's own legs; these two are the walk's
        // own tiers, so the whole pack shares one number. Prototype values until Play: Build writes them,
        // Verify pins them, and walkSpeed < runSpeed < dashSpeed < sprintSpeed must hold.
        public float dashSpeed = 4.5f;    // 走り: overtaking a walking holder, coming back inside 1.6x leash
        public float sprintSpeed = 7.2f;  // 全力: recovery from beyond 1.6x leash, keeping up with a running holder
        public float catchUpDistance = 10f;
        public int defaultDestination = 1;
        [Header("Free walk (IMP-DOG-003, provisional)")]
        public float trailOffDistance = 6f;    // holder farther than this from the trail: dog keeps them company instead
        public float trailOnDistance = 4f;     // ... and rejoins the trail once the holder is closer than this
        public float entryMaxPath = 12f;       // longest NavMesh path accepted when entering the trail
        public float inviteAfter = 8f;         // holder still this long at leash end before the dog comes back to invite
        public float inviteInterval = 14f;     // base spacing between invitations (grows each time)
        public int inviteLimit = 3;            // after this many, the dog settles down until the holder moves
        public float detourChance = 0.25f;     // per 4 s roll while walking with slack
        public float stopoverChance = 0.5f;    // chance the dog takes a stopover spot it passes (provisional)
        private float _nextMaintenance;
        private int _myChoice = -1;            // this player's destination choice for the stay; -1 = not chosen yet
        private NavMeshPath _stationPath;

        public bool _IsLeashed(int dog)
        {
            return dog >= 0 && dog < walkers.Length && walkers[dog] >= 0;
        }

        public bool _IsMine(int dog)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            return Utilities.IsValid(local) && _IsLeashed(dog) && walkers[dog] == local.playerId;
        }

        public int _MyCount(int hand)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return 0;
            int count = 0;
            for (int i = 0; i < walkers.Length; i++) if (walkers[i] == local.playerId && hands[i] == hand) count++;
            return count;
        }

        public int _MyChoice() { return _myChoice < 0 ? defaultDestination : _myChoice; }

        // Gathering dogs cannot be leashed; a leashed dog is never gathered (KD_GroupActivities.Available).
        // Both authorities live on this GameObject, so their Owner decides both in sequence.
        public bool _IsGathering(int dog)
        {
            return activities != null && activities.Mode == 1 && activities._IsParticipant(dog);
        }

        // Called by KD_GroupActivities (same Owner) the moment a gathering ends: the spot each participant
        // reached, or was at when it ended early, becomes its roaming home.
        public void _OnGatherEnded(int participantMask)
        {
            if (!Networking.IsOwner(gameObject)) return;
            bool changed = false;
            for (int i = 0; i < walkers.Length; i++)
            {
                if (walkers[i] >= 0 || (participantMask & (1 << i)) == 0 || dogRoots[i] == null) continue;
                NavMeshHit hit;
                releasedHomes[i] = NavMesh.SamplePosition(dogRoots[i].position, out hit, 2f, NavMesh.AllAreas) ? hit.position : dogRoots[i].position;
                changed = true;
            }
            if (changed) { ApplyHomes(); RequestSerialization(); }
        }

        public Vector3 _HandPosition(VRCPlayerApi player, int hand)
        {
            if (!Utilities.IsValid(player)) return Vector3.zero;
            if (player.IsUserInVR()) return player.GetTrackingData(hand == 0
                ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand).position;
            // Desktop/touch has no tracked hands: explicit left/right body anchors.
            float height = Mathf.Clamp(player.GetAvatarEyeHeightAsMeters(), 0.5f, 2.5f);
            return player.GetPosition() + player.GetRotation() * new Vector3(hand == 0 ? -0.23f : 0.23f, height * 0.6f, 0.32f);
        }

        public int _DestinationNode(int dog)
        {
            if (route == null || dog < 0 || dog >= destinations.Length) return -1;
            return route._DestinationNode(destinations[dog]);
        }

        // The player's choice for this stay; also applied to every dog they are leashing right now.
        public void _ChooseDestination(int destination)
        {
            if (!Utilities.IsValid(Networking.LocalPlayer) || route == null || route._DestinationNode(destination) < 0) return;
            _myChoice = destination;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqDestination), destination);
        }

        [NetworkCallable(maxEventsPerSecond: 6)]
        public void ReqDestination(int destination)
        {
            if (!Networking.IsOwner(gameObject) || route == null || route._DestinationNode(destination) < 0) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            bool changed = false;
            int mine = 0;
            for (int i = 0; i < walkers.Length; i++)
            {
                if (walkers[i] != caller.playerId) continue;
                mine++;
                if (destinations[i] != destination) { destinations[i] = destination; changed = true; }
            }
            // BUG-DOG-004: the owner-side trail for "my choice did not reach my dogs" reports (playtest 2026-09-26).
            Debug.Log("[KH_Leash] ReqDestination from player " + caller.playerId + " dest=" + destination + " dogs=" + mine + " changed=" + changed);
            if (changed) RequestSerialization();
        }

        // ---- 誘いの一声 (IMP-DOG-003) の中継 ----
        // 犬の UdonBehaviour (KD_WanderSystem_*) は Sync None なので自分ではネットワークイベントを送れない。
        // 犬の Owner がここ (Manual) から全員へ送り、受け手は送り主が dogRoot の Owner かを確かめて犬のローカルの鳴き声を鳴らす
        public void _RelayInviteBark(int dog)
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(LeashInviteBark), dog);
        }

        [NetworkCallable(maxEventsPerSecond: 6)]
        public void LeashInviteBark(int dog) // net: callable-ok: sound only; sender must own that dog's root
        {
            if (dog < 0 || dogRoots == null || dog >= dogRoots.Length || dogRoots[dog] == null) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            VRCPlayerApi owner = Networking.GetOwner(dogRoots[dog].gameObject);
            if (!Utilities.IsValid(caller) || !Utilities.IsValid(owner) || caller.playerId != owner.playerId) return;
            if (fetchers == null || dog >= fetchers.Length || fetchers[dog] == null) return;
            KominkaDogRun.KD_DogPack pack = fetchers[dog].pack;
            if (pack == null || pack.dogWander == null || dog >= pack.dogWander.Length || pack.dogWander[dog] == null) return;
            pack.dogWander[dog]._InviteBark();
        }

        public Vector3 _WalkingTarget(int dog)
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(walkers[dog]);
            if (!Utilities.IsValid(player)) return dogRoots[dog].position;
            int slot = 0;
            for (int i = 0; i < dog; i++) if (walkers[i] == walkers[dog] && hands[i] == hands[dog]) slot++;
            float side = hands[dog] == 0 ? -1f : 1f;
            // Slow variation within distinct slots, not a formation locked to one point.
            float time = (float)(Networking.GetServerTimeInSeconds() % 10000.0);
            // Beside and slightly ahead of the hand, never queued behind the walker.
            Vector3 offset = new Vector3(side * (0.75f + slot * 0.45f), 0f,
                0.5f + slot * 0.35f + Mathf.Sin(time * 0.35f + dog * 1.7f) * 0.25f);
            offset = Vector3.ClampMagnitude(offset, Mathf.Max(0.5f, leashLength - 0.6f));
            Vector3 target = _HandPosition(player, hands[dog]) + player.GetRotation() * offset;
            target.y = player.GetPosition().y;
            return target;
        }

        public void _RequestAttach(int dog, int hand)
        {
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqAttach), dog, hand, _MyChoice());
        }

        [NetworkCallable(maxEventsPerSecond: 6)]
        public void ReqAttach(int dog, int hand, int destination)
        {
            if (!Networking.IsOwner(gameObject) || dog < 0 || dog >= dogRoots.Length || hand < 0 || hand > 1) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller) || dogRoots[dog] == null || _IsLeashed(dog)) return;
            if (Vector3.Distance(caller.GetPosition(), dogRoots[dog].position) > attachReach) return;
            // A carrying/chasing dog must finish its current Fetch before attachment; a gathering dog stays in formation.
            if (fetchers[dog] == null || !fetchers[dog].IsIdle || _IsGathering(dog)) return;
            if (_InWater(dog)) { fetchers[dog]._AskWaterExitFor(caller.playerId); return; }   // IMP-DOG-020: after landing (KH_LeashManager.Water.cs)
            if (frisbeeCatchers != null && dog < frisbeeCatchers.Length && frisbeeCatchers[dog] != null && frisbeeCatchers[dog].IsEngaged) return;
            // IMP-DOG-016: a dog waiting where it was called stays out of the walk until its caller (or the owner) lets it roam.
            if (sleepBoard != null && sleepBoard._IsCalled(dog)) return;
            // IMP-DOG-027: a dog being washed (a stroke within holdSeconds, synced) takes no new leash; ask again once the wash
            // has paused. Same silent refusal as the other gates (the menu's pending request times out). Only this new-attach
            // path: a dog already leashed is washed on its leash (KD_DogWanderController.HillsideWash.cs).
            if (WashHeld(dog)) return;
            int count = 0;
            for (int i = 0; i < walkers.Length; i++) if (walkers[i] == caller.playerId && hands[i] == hand) count++;
            if (count >= 3) return;
            walkers[dog] = caller.playerId;
            hands[dog] = hand;
            destinations[dog] = route != null && route._DestinationNode(destination) >= 0 ? destination : defaultDestination;
            Debug.Log("[KH_Leash] ReqAttach dog" + dog + " by player " + caller.playerId + " hand=" + hand + " dest=" + destinations[dog]); // BUG-DOG-004 trail
            RequestSerialization();
        }

        // IMP-DOG-027: the dog's wash hold through its wander layer (fetchers[dog].pack, as LeashInviteBark). Unwired = false.
        private bool WashHeld(int dog)
        {
            KominkaDogRun.KD_DogPack pack = fetchers[dog].pack;
            if (pack == null || pack.dogWander == null || dog >= pack.dogWander.Length || pack.dogWander[dog] == null) return false;
            return pack.dogWander[dog].WashHeld;
        }

        public void _RequestDetach(int dog)
        {
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqDetach), dog);
        }

        // Releasing your own dog needs no proximity (menu assist); nobody can release another player's dog.
        [NetworkCallable(maxEventsPerSecond: 6)]
        public void ReqDetach(int dog)
        {
            if (!Networking.IsOwner(gameObject) || dog < 0 || dog >= walkers.Length) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller) || walkers[dog] != caller.playerId) return;
            Release(dog);
            RequestSerialization();
        }

        private void Release(int dog)
        {
            walkers[dog] = -1;
            ClearTravel(dog); // IMP-WLD-001: a released dog carries no travel ticket (KH_LeashManager.Travel.cs)
            if (dogRoots[dog] != null) releasedHomes[dog] = dogRoots[dog].position;
            ApplyHomes();
        }

        private void ApplyHomes()
        {
            if (!homesInitialized) return;
            for (int i = 0; i < roamCenters.Length; i++)
                if (roamCenters[i] != null && walkers[i] < 0) roamCenters[i].position = releasedHomes[i];
        }

        // ---- Leash stations ----
        // Nearest station (by walkable path) with a free slot of the tool's side. Ties resolve by slot order.
        public Vector3 _ReturnSlot(Vector3 from, int hand, KH_LeashClip self)
        {
            int best = -1; float bestCost = 1e9f;
            if (stationSlots != null)
            {
                if (_stationPath == null) _stationPath = new NavMeshPath();
                for (int i = 0; i < stationSlots.Length; i++)
                {
                    if (stationSlots[i] == null || slotHand[i] != hand || SlotTaken(i, self)) continue;
                    float cost = Vector3.Distance(from, stationSlots[i].position);
                    NavMeshHit a, b;
                    if (NavMesh.SamplePosition(from, out a, 2f, NavMesh.AllAreas) && NavMesh.SamplePosition(stationSlots[i].position, out b, 2f, NavMesh.AllAreas)
                        && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, _stationPath) && _stationPath.status == NavMeshPathStatus.PathComplete)
                    {
                        Vector3[] c = _stationPath.corners; cost = 0f;
                        for (int k = 1; k < c.Length; k++) cost += Vector3.Distance(c[k - 1], c[k]);
                    }
                    else cost += 1000f; // unreachable stations only as a last resort
                    if (cost < bestCost - 0.01f) { bestCost = cost; best = i; }
                }
            }
            return best >= 0 ? stationSlots[best].position : from;
        }

        private bool SlotTaken(int slot, KH_LeashClip self)
        {
            for (int i = 0; i < localClips.Length; i++)
            {
                KH_LeashClip clip = localClips[i];
                if (clip == null || clip == self || clip.pickup == null || clip.pickup.IsHeld) continue;
                if (Vector3.Distance(clip.transform.position, stationSlots[slot].position) < 0.1f) return true;
            }
            return false;
        }

        public override void OnDeserialization() { ApplyHomes(); }
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject)) return;
            bool changed = false;
            for (int i = 0; i < walkers.Length; i++) if (walkers[i] == player.playerId) { Release(i); changed = true; }
            if (changed) RequestSerialization();
        }

        // IMP-DOG-034 C2-4: straight leash when this client's fps is low (user decision 2026-10-01, IMP-DOG-034
        // "決定: 2026-10-01 (ユーザー) — 段階 3 C-2 の採否", C2-4). Decided once per minute so it never flips often:
        // the average fps of the last 60 s window < 30 -> straight, >= 40 -> full rope, in between -> keep.
        // Local only (no sync, no events): each client judges its own fps and only its own drawing changes.
        const float LightRopeWindow = 60f;     // s, one decision per window (user: 判定は 1 分ごと)
        const float LightRopeOnBelow = 30f;    // fps, average below this -> straight rope
        const float LightRopeOffAt = 40f;      // fps, average at or above this -> full rope again
        // 0 = auto (default), 1 = always the full rope, 2 = always straight. After changing it, call
        // _ApplyLightRopeMode() (or _LightRopeAuto / _LightRopeFull / _LightRopeStraight, which set and apply);
        // the once-a-minute tick also applies it. No per-frame check of this field.
        public int lightRopeMode;
        private int _lrFrames;
        private float _lrWindowStart, _lrNextAt;   // _lrNextAt 0: the first Update opens the first window
        private bool _lrPartial;   // the last write did not reach every collar: write again whatever the remembered state
        private bool _lrStarted, _lrAuto, _lrOn;   // auto decision / what the collars currently have (both start OFF)
        private KH_LeashCollar[] _lrCollars;
        private void LightRopeTick(float now)
        {
            if (!_lrStarted) { _lrStarted = true; _lrWindowStart = now; _lrFrames = 0; _lrNextAt = now + LightRopeWindow; return; }
            float elapsed = now - _lrWindowStart;
            float fps = elapsed > 0f ? _lrFrames / elapsed : 0f;
            if (fps < LightRopeOnBelow) _lrAuto = true;
            else if (fps >= LightRopeOffAt) _lrAuto = false;
            _lrWindowStart = now; _lrFrames = 0; _lrNextAt = now + LightRopeWindow;
            _ApplyLightRopeMode();
        }
        public void _LightRopeAuto() { lightRopeMode = 0; _ApplyLightRopeMode(); }
        public void _LightRopeFull() { lightRopeMode = 1; _ApplyLightRopeMode(); }
        public void _LightRopeStraight() { lightRopeMode = 2; _ApplyLightRopeMode(); }
        // Writes lightRope to all six collars together, and only when the wanted state differs from the last write.
        public void _ApplyLightRopeMode()
        {
            bool on = lightRopeMode == 2 || (lightRopeMode == 0 && _lrAuto);
            if (on == _lrOn && !_lrPartial) return;
            // Same lookup as KH_LeashCollar.PeerCollar: anchor = child "ClipAnchor" of the collar object.
            int n = collarAnchors != null ? collarAnchors.Length : 0;
            if (_lrCollars == null || _lrCollars.Length != n) _lrCollars = new KH_LeashCollar[n];
            bool all = true;
            for (int i = 0; i < n; i++)
            {
                // A collar that could not be found is looked up again on every apply (at most once a minute).
                if (_lrCollars[i] == null && collarAnchors[i] != null && collarAnchors[i].parent != null)
                    _lrCollars[i] = collarAnchors[i].parent.GetComponent<KH_LeashCollar>();
                if (_lrCollars[i] != null) _lrCollars[i].lightRope = on;
                else all = false;
            }
            // Remember what was written; a partial write is repeated on the next tick or mode change (Codex 2026-10-02).
            _lrOn = on; _lrPartial = !all;
            if (!all) Debug.LogWarning("[KH_LeashManager] light rope: a collar was not found; will retry");
        }

        private void Update()
        {
            TravelTick(); // IMP-WLD-001: the requester's wait runs every frame (KH_LeashManager.Travel.cs)
            // IMP-DOG-034 C2-4: per frame only a counter, one unscaled-time read and one compare.
            _lrFrames++;
            float lrNow = Time.unscaledTime;
            if (lrNow >= _lrNextAt) LightRopeTick(lrNow);
            if (Time.time < _nextMaintenance) return;
            _nextMaintenance = Time.time + 0.5f;
            if (!Networking.IsOwner(gameObject)) { ApplyHomes(); return; }
            bool changed = false;
            if (!homesInitialized)
            {
                for (int i = 0; i < roamCenters.Length; i++) if (roamCenters[i] != null) releasedHomes[i] = roamCenters[i].position;
                homesInitialized = true; changed = true;
            }
            // Also repairs connections whose holder left during automatic ownership transfer.
            for (int i = 0; i < walkers.Length; i++)
                if (walkers[i] >= 0 && !Utilities.IsValid(VRCPlayerApi.GetPlayerById(walkers[i]))) { Release(i); changed = true; }
            if (ExpireTravel()) changed = true;
            if (changed) RequestSerialization();
            ApplyHomes();
        }
    }
}
