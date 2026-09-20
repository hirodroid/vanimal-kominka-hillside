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
    public class KH_LeashManager : UdonSharpBehaviour
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
        public float followSpeed = 4.8f;
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
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
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
            for (int i = 0; i < walkers.Length; i++)
                if (walkers[i] == caller.playerId && destinations[i] != destination) { destinations[i] = destination; changed = true; }
            if (changed) RequestSerialization();
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
            if (frisbeeCatchers != null && dog < frisbeeCatchers.Length && frisbeeCatchers[dog] != null && frisbeeCatchers[dog].IsEngaged) return;
            int count = 0;
            for (int i = 0; i < walkers.Length; i++) if (walkers[i] == caller.playerId && hands[i] == hand) count++;
            if (count >= 3) return;
            walkers[dog] = caller.playerId;
            hands[dog] = hand;
            destinations[dog] = route != null && route._DestinationNode(destination) >= 0 ? destination : defaultDestination;
            RequestSerialization();
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

        private void Update()
        {
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
            if (changed) RequestSerialization();
            ApplyHomes();
        }
    }
}
