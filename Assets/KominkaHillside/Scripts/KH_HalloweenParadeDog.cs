using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using KominkaDogRun;

namespace KominkaHillside
{
    // Attach beside the dog's existing Manual state (not on its Continuous ObjectSync root).
    // Root ownership is never changed. Every destructive action is authorized by a committed request.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_HalloweenParadeDog : UdonSharpBehaviour
    {
        public KH_HalloweenParade parade;
        public int dogIndex;
        public Transform root, startSlot, recoverySlot;
        public KD_DogWanderController wander;
        public KD_DogFetchController fetch;
        public KH_FrisbeeCatch frisbee;
        public KH_DogWash wash;
        [UdonSynced] private int endedRequest, placedRequest;
        public int cleanCount, warpCount;
        private float _nextReport;
        private bool _engaged;
        private bool _terminalObserved;
        private int[] _users = new int[80];
        public bool Isolated { get { return parade != null && parade._DogIsolated(dogIndex); } }
        public bool BlocksNewUses { get { return parade != null && parade._BlocksDog(dogIndex); } }
        public bool Committed { get { return parade != null && parade.Committed; } }

        public bool _ReadyForRequest()
        {
            if (parade == null || root == null || startSlot == null || wander == null || fetch == null || wash == null
                || !root.gameObject.activeInHierarchy || !wander.enabled || !fetch.enabled || !wash.enabled
                || !wash._HasState()) return false;
            VRCPlayerApi owner = Networking.GetOwner(root.gameObject);
            if (!Utilities.IsValid(owner) || Networking.GetOwner(gameObject) != owner || Networking.GetOwner(fetch.gameObject) != owner
                || Networking.GetOwner(wash.gameObject) != owner || (frisbee != null && Networking.GetOwner(frisbee.gameObject) != owner)) return false;
            // An active show or an airborne trick is not a safe reservation point.
            return !wander.CandyReserved;
        }
        private int UserAdd(int count, int id)
        {
            if (id < 0) return count;
            for (int i = 0; i < count; i++) if (_users[i] == id) return count;
            if (count < _users.Length) _users[count++] = id;
            return count;
        }
        public int[] _UserIds()
        {
            for (int i = 0; i < _users.Length; i++) _users[i] = -1;
            if (parade == null) return _users;
            int n = 0;
            if (parade.leashes != null && parade.leashes.walkers != null && dogIndex < parade.leashes.walkers.Length)
                n = UserAdd(n, parade.leashes.walkers[dogIndex]);
            if (parade.washStations != null) for (int i = 0; i < parade.washStations.Length; i++)
                if (parade.washStations[i] != null && parade.washStations[i]._HeldDog() == dogIndex) n = UserAdd(n, parade.washStations[i]._CallerId());
            if (fetch != null)
            {
                if (!fetch.IsIdle || fetch.PetActive) n = UserAdd(n, fetch.AttentionPlayerId);
                if (fetch.ballController != null) { VRCPlayerApi held = fetch.ballController.HolderPlayer; if (Utilities.IsValid(held)) n = UserAdd(n, held.playerId); }
            }
            if (frisbee != null)
            {
                if (frisbee.IsEngaged) n = UserAdd(n, frisbee.ThrowerId);
                if (frisbee.frisbee != null) { VRCPlayerApi held = frisbee.frisbee.HolderPlayer; if (Utilities.IsValid(held)) n = UserAdd(n, held.playerId); }
            }
            if (parade.sleepBoard != null) n = UserAdd(n, parade.sleepBoard.LiveSleeperOf(dogIndex));
            if (parade.treatBox != null) { KD_TreatSlot s = parade.treatBox.LiveSlotOf(dogIndex); if (s != null) n = UserAdd(n, s.GiverId()); }
            if (parade.activities != null && parade.activities.Busy) n = UserAdd(n, parade.activities.HostId);
            // Canonical exact set: never use a hash that could silently collide.
            for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++)
                if (_users[j] < _users[i]) { int temp = _users[i]; _users[i] = _users[j]; _users[j] = temp; }
            return _users;
        }
        public string _UsageKey()
        {
            int[] ids = _UserIds(); string key = "";
            for (int i = 0; i < ids.Length && ids[i] >= 0; i++) key += ids[i] + ",";
            if (parade != null && parade.washStations != null) for (int i = 0; i < parade.washStations.Length; i++)
                if (parade.washStations[i] != null && parade.washStations[i]._HeldDog() == dogIndex) key += "w" + i + ":" + parade.washStations[i]._HeldMs() + ",";
            KD_SleepSlot slot = parade != null && parade.sleepBoard != null ? parade.sleepBoard.SlotOf(dogIndex) : null;
            key += "s" + (slot != null && slot.LiveSleeperId >= 0 ? slot.CalledMs : 0);
            return key;
        }
        private void Update()
        {
            if (parade == null || wander == null || root == null) return;
            int phase = parade.Phase, id = parade.RequestId;
            if (phase == KH_HalloweenParade.EndUses) wander._SuppressPettingForParade();
            bool owner = Networking.IsOwner(root.gameObject);
            // Rendering/motion intent is reconstructed from the state on all clients. Only Wander's root Owner moves.
            bool occupy = phase >= KH_HalloweenParade.Place && phase <= KH_HalloweenParade.Playing;
            if (occupy)
            {
                _engaged = true;
                wander.paradeActive = true;
                wander.paradeTarget = phase == KH_HalloweenParade.Playing ? parade._DogTarget(dogIndex) : startSlot.position;
                wander.paradeFacing = phase == KH_HalloweenParade.Playing ? parade._DogFacing(dogIndex) : startSlot.forward;
                wander.paradePoseTime = parade.Elapsed;
            }
            else if (_engaged || phase == KH_HalloweenParade.Restore)
            {
                wander._EndParadePose(); _engaged = false;
            }
            // A terminal failure never runs old cleanup against newly accepted uses on healthy dogs.
            if (phase == KH_HalloweenParade.RecoveryFailed)
            {
                wander._EndParadePose();
                if (!_terminalObserved) { _terminalObserved = true; if (fetch != null) fetch._RefreshPettingAfterParade(); }
                return;
            }
            if (!owner || Time.time < _nextReport || phase < KH_HalloweenParade.Reserve || phase == KH_HalloweenParade.Playing) return;
            _nextReport = Time.time + .5f;
            if (!Networking.IsOwner(gameObject)) return; // do not assume child ownership follows the root
            if (phase == KH_HalloweenParade.Restore)
            {
                if (!_ReadyForRequest()) return;
                if (parade.treatBox != null && parade.treatBox.EatingNowOf(dogIndex)) return;
                if (!wash._CleanForParade(id)) return;
                if (!fetch.IsIdle || fetch.PetActive) fetch._EndForParade();
                if (frisbee != null && !frisbee.IsIdle) frisbee._EndForParade();
                // Never replay placement after Playing, including a new Owner with old per-dog receipts.
                // A preparation failure may instead need to remove the dog from a raised wash stand.
                if (!parade.ShowStarted && placedRequest != id)
                {
                    if (!wander._PlaceForParade(recoverySlot != null ? recoverySlot : startSlot)) return;
                    placedRequest = id; RequestSerialization();
                }
                parade._ReportDog(dogIndex, id, phase, fetch.IsIdle && (frisbee == null || frisbee.IsIdle) && !wash._WashHold());
                return;
            }
            if (phase == KH_HalloweenParade.Reserve)
            {
                parade._ReportDog(dogIndex, id, phase, _ReadyForRequest() && _UsageKey() == parade._ExpectedUsage(dogIndex));
                return;
            }
            if (phase == KH_HalloweenParade.EndUses)
            {
                if (endedRequest != id)
                {
                    if (!_ReadyForRequest()) { parade._ReportDog(dogIndex, id, phase, false); return; }
                    // A consumed treat completes its existing bounded eating path, with no refund/re-consumption.
                    if (parade.treatBox != null && parade.treatBox.EatingNowOf(dogIndex)) return;
                    if (!wash._CleanForParade(id)) return;
                    if (wash._ParadeCleaned(id)) cleanCount++;
                    fetch._EndForParade();
                    if (frisbee != null) frisbee._EndForParade();
                    endedRequest = id; RequestSerialization();
                    Debug.Log("[KH_ParadeDog] dog=" + dogIndex + " request=" + id + " ended cleanCount=" + cleanCount);
                }
                parade._ReportDog(dogIndex, id, phase, fetch.IsIdle && (frisbee == null || frisbee.IsIdle));
            }
            else if (phase == KH_HalloweenParade.Place)
            {
                if (endedRequest != id) { parade._ReportDog(dogIndex, id, phase, false); return; }
                if (placedRequest != id)
                {
                    if (!wander._PlaceForParade(startSlot)) { parade._ReportDog(dogIndex, id, phase, false); return; }
                    placedRequest = id; warpCount++; RequestSerialization();
                    Debug.Log("[KH_ParadeDog] dog=" + dogIndex + " request=" + id + " placed warpCount=" + warpCount);
                }
                bool placed = Vector3.Distance(root.position, startSlot.position) <= .2f && Quaternion.Angle(root.rotation, startSlot.rotation) < 10f;
                parade._ReportDog(dogIndex, id, phase, placed);
            }
        }
        public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner) { return false; }
    }
}
