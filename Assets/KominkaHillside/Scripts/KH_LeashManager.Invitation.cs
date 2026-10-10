using UdonSharp;
using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    public partial class KH_LeashManager
    {
        // IMP-DOG-045: local pickups report an OR per player, never take ownership of dogs or the manager.
        public Transform[] inviteStations, inviteSpots; // 3 stations, then station * 6 + dog
        public KominkaDogRun.KD_DogWanderController[] inviteDogs;
        public float inviteRange = 25f, inviteHolderRange = 5f, inviteGrace = 5f;
        [UdonSynced] public double[] inviteUntil = new double[3];
        [UdonSynced] public int[] inviteLookIds = new int[] { -1, -1, -1 };
        [UdonSynced] public int[] inviteDogStation = new int[6]; // 0 none, otherwise station + 1
        [UdonSynced] public double[] inviteDogAt = new double[6];
        [UdonSynced] public int inviteHeldMask;
        private const float InviteHeartbeat = 1f, InviteReportTtl = 3f;
        private int[] _inviteIds = new int[80], _inviteMasks = new int[80];
        private double[] _inviteSeen = new double[80];
        private int _inviteLocalMask;
        private float _inviteNextSend, _inviteSendZerosUntil;
        private double _inviteRebuildUntil;
        private NavMeshPath _invitePath;

        public int _InviteStationAt(Vector3 point)
        {
            if (inviteStations == null) return -1;
            int best = -1; float distance = 4f; // the home slot must actually belong to this rack (2 m)
            for (int i = 0; i < inviteStations.Length; i++)
            {
                Vector3 d = point - inviteStations[i].position; d.y = 0f;
                if (d.sqrMagnitude < distance) { distance = d.sqrMagnitude; best = i; }
            }
            return best;
        }

        // Pickup callbacks schedule the next ordinary maintenance immediately. IsHeld is read after the SDK callback finishes.
        public void _InvitationChanged() { _nextMaintenance = 0f; _inviteNextSend = 0f; }

        private int InvitationMask(VRCPlayerApi player, int mask)
        {
            if (!Utilities.IsValid(player) || inviteStations == null) return 0;
            int valid = 0;
            for (int i = 0; i < inviteStations.Length; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                Vector3 d = player.GetPosition() - inviteStations[i].position;
                // Vertical limit prevents a visitor on another level from activating a rack below them.
                if (Mathf.Abs(d.y) > 3f) continue;
                d.y = 0f;
                if (d.sqrMagnitude <= inviteHolderRange * inviteHolderRange) valid |= 1 << i;
            }
            return valid;
        }

        [NetworkCallable(maxEventsPerSecond: 40)]
        public void ReportInvitation(int mask)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi sender = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(sender)) return;
            RememberInvitation(sender.playerId, InvitationMask(sender, mask), Networking.GetServerTimeInSeconds());
        }

        private void RememberInvitation(int id, int mask, double now)
        {
            int slot = -1;
            for (int i = 0; i < _inviteIds.Length; i++)
            {
                if (_inviteIds[i] == id) { slot = i; break; }
                if (slot < 0 && (_inviteMasks[i] == 0 || now - _inviteSeen[i] > InviteReportTtl)) slot = i;
            }
            if (slot < 0) return;
            _inviteIds[slot] = id; _inviteMasks[slot] = mask; _inviteSeen[slot] = now;
        }

        private void ForgetInvitation(int id)
        {
            for (int i = 0; i < _inviteIds.Length; i++) if (_inviteIds[i] == id) _inviteMasks[i] = 0;
            _nextMaintenance = 0f;
        }

        private int InvitationReportedMask(double now)
        {
            int mask = 0;
            for (int i = 0; i < _inviteMasks.Length; i++)
            {
                if (now - _inviteSeen[i] > InviteReportTtl) _inviteMasks[i] = 0;
                mask |= _inviteMasks[i];
            }
            return mask;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            _inviteNextSend = 0f; _inviteSendZerosUntil = Time.time + InviteReportTtl;
            if (!Networking.IsOwner(gameObject)) return;
            for (int i = 0; i < _inviteMasks.Length; i++) _inviteMasks[i] = 0;
            // The last synced bounded expiry remains valid while current holders re-report to the new Owner.
            _inviteRebuildUntil = Networking.GetServerTimeInSeconds() + InviteReportTtl;
            _nextMaintenance = 0f;
        }

        private void InvitationTick()
        {
            if (inviteStations == null || inviteStations.Length != 3 || inviteDogs == null) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;
            double now = Networking.GetServerTimeInSeconds();
            int localMask = 0;
            for (int i = 0; i < localClips.Length; i++)
            {
                KH_LeashClip clip = localClips[i];
                if (clip == null || !clip.enabled || !clip.gameObject.activeInHierarchy || clip.pickup == null || !clip.pickup.IsHeld) continue;
                int station = clip._InvitationStation();
                if (station >= 0 && station < inviteStations.Length) localMask |= 1 << station;
            }
            localMask = InvitationMask(local, localMask);
            bool change = localMask != _inviteLocalMask;
            if (change) _inviteSendZerosUntil = Time.time + InviteReportTtl;
            _inviteLocalMask = localMask;
            if (change || (Time.time >= _inviteNextSend && (localMask != 0 || Time.time < _inviteSendZerosUntil)))
            {
                _inviteNextSend = Time.time + InviteHeartbeat;
                if (Networking.IsOwner(gameObject)) RememberInvitation(local.playerId, localMask, now);
                else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReportInvitation), localMask);
            }
            if (!Networking.IsOwner(gameObject)) return;
            for (int i = 0; i < _inviteMasks.Length; i++)
            {
                if (_inviteMasks[i] == 0) continue;
                _inviteMasks[i] = InvitationMask(VRCPlayerApi.GetPlayerById(_inviteIds[i]), _inviteMasks[i]);
            }
            int held = InvitationReportedMask(now);
            // During handover retain only the inherited state; it still expires even if reports never arrive.
            int confirmed = held;
            if (now < _inviteRebuildUntil) held |= inviteHeldMask;
            bool changed = ReduceInvitation(held, confirmed, now);
            for (int s = 0; s < inviteStations.Length; s++)
            {
                int look = -1;
                for (int i = 0; i < _inviteMasks.Length; i++)
                    if ((_inviteMasks[i] & (1 << s)) != 0) { look = _inviteIds[i]; break; }
                if (inviteLookIds[s] != look) { inviteLookIds[s] = look; changed = true; }
            }
            for (int dog = 0; dog < inviteDogs.Length; dog++)
            {
                int previous = inviteDogStation[dog] - 1;
                int chosen = -1;
                if (inviteDogs[dog] != null && _IntroDogAvailable(dog) && inviteDogs[dog]._InvitationAvailable())
                {
                    if (previous >= 0 && now < inviteUntil[previous] && InvitationInRange(dog, previous)) chosen = previous;
                    else
                    {
                        float best = float.MaxValue;
                        for (int s = 0; s < inviteStations.Length; s++)
                        {
                            if ((held & (1 << s)) == 0 || !InvitationInRange(dog, s)) continue;
                            float distance = Vector3.Distance(dogRoots[dog].position, inviteStations[s].position);
                            if (distance < best && InvitationPath(dog, s)) { best = distance; chosen = s; }
                        }
                    }
                }
                if (chosen == previous) continue;
                inviteDogStation[dog] = chosen + 1;
                inviteDogAt[dog] = now + .25 + (dog % 6) * .15;
                changed = true;
            }
            if (changed) RequestSerialization();
        }

        // No SDK access: this transition is also exercised by Edit-mode probes.
        private bool ReduceInvitation(int held, int confirmed, double now)
        {
            bool changed = false;
            for (int s = 0; s < inviteUntil.Length; s++)
            {
                bool on = (held & (1 << s)) != 0;
                if (on && (confirmed & (1 << s)) != 0 && inviteUntil[s] - now < inviteGrace + InviteReportTtl - InviteHeartbeat)
                { inviteUntil[s] = now + InviteReportTtl + inviteGrace; changed = true; }
                else if (!on && (inviteHeldMask & (1 << s)) != 0)
                { inviteUntil[s] = System.Math.Min(inviteUntil[s], now + inviteGrace); changed = true; }
            }
            if (held != inviteHeldMask) { inviteHeldMask = held; changed = true; }
            return changed;
        }

        private bool InvitationInRange(int dog, int station)
        {
            Vector3 delta = dogRoots[dog].position - inviteStations[station].position; delta.y = 0f;
            return delta.sqrMagnitude <= inviteRange * inviteRange;
        }
        private bool InvitationPath(int dog, int station)
        {
            if (_invitePath == null) _invitePath = new NavMeshPath();
            NavMeshHit from, to;
            Vector3 goal = inviteSpots[station * inviteDogs.Length + dog].position;
            return NavMesh.SamplePosition(dogRoots[dog].position, out from, .75f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(goal, out to, .15f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _invitePath)
                && _invitePath.status == NavMeshPathStatus.PathComplete;
        }
    }
}
