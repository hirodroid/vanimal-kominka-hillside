using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    public partial class KD_LiveSystem
    {
        // Actual launch/recovery decisions are not derivable from the music clock.
        // 0 waiting, 1 launched, 2 landing requested, 3 landing, 4 grounded.
        [UdonSynced] private int dogMountState;
        [UdonSynced] private int dogMountOwner = -1;
        [UdonSynced] private int dogMountChangedMs;
        [UdonSynced] private Vector3 dogMountFrom;
        [UdonSynced] private Vector3 dogMountTo;
        [UdonSynced] private Quaternion dogMountRotation = Quaternion.identity;
        public int DogMountState { get { return dogMountState; } } // net: callable-ok: shared mount phase
        public int DogMountOwner { get { return dogMountOwner; } } // net: callable-ok: owner that committed this movement
        public int DogMountStamp { get { return dogMountChangedMs; } } // net: callable-ok: committed recovery revision
        public float DogMountElapsed { get { return (Networking.GetServerTimeInMilliseconds() - dogMountChangedMs) * .001f; } } // net: callable-ok: shared recovery clock
        public Vector3 DogMountFrom { get { return dogMountFrom; } } // net: callable-ok: committed takeoff/recovery origin
        public Vector3 DogMountTo { get { return dogMountTo; } } // net: callable-ok: committed surveyed landing
        public Quaternion DogMountRotation { get { return dogMountRotation; } } // net: callable-ok: recovery starting rotation
        public bool DogRecovering { get { return dogMountState == 2 || dogMountState == 3
            || (dogMountState == 4 && candyStopped && DogMountElapsed < 4f); } } // net: callable-ok: shared exclusion until grounded and support standing

        private bool MountPairReady(KD_CandyDogPerformance p)
        {
            if (p == null) return false;
            if (!p.useMount) return true;
            if (p.dogs == null || p.mountRider < 0 || p.mountRider >= p.dogs.Length
                || p.mountSupport < 0 || p.mountSupport >= p.dogs.Length) return false;
            var rider = p.dogs[p.mountRider]; var support = p.dogs[p.mountSupport];
            if (rider == null || support == null || rider.dogRoot == null || support.dogRoot == null
                || rider.fetchController == null || support.fetchController == null) return false;
            var owner = Networking.GetOwner(rider.dogRoot.gameObject);
            return Utilities.IsValid(owner) && Networking.GetOwner(support.dogRoot.gameObject) == owner
                && Networking.GetOwner(rider.fetchController.gameObject) == owner
                && Networking.GetOwner(support.fetchController.gameObject) == owner;
        }

        private void StopDogPerformance()
        {
            var p = DogPerformanceOf(liveId);
            if (p == null) return;
            if (p.useMount && !DogRecovering)
            {
                float t = Elapsed - NightInSeconds;
                // Also cover a launch report still in transit at cancellation.
                dogMountState = dogMountState == 1 || (t >= p.mountStart - .2f && t <= p.mountLand + .5f) ? 2 : 4;
                dogMountChangedMs = Networking.GetServerTimeInMilliseconds();
            }
            candyStopped = true;
        }

        public void _ReportDogMount(int epoch, int result, Vector3 position, Quaternion rotation)
        {
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            int stamp = Networking.GetServerTimeInMilliseconds();
            if (Networking.IsOwner(gameObject)) AcceptDogMount(Networking.LocalPlayer, epoch, result, position, rotation, stamp);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(DogMountReport), epoch, result, position, rotation, stamp);
        }

        [NetworkCallable(maxEventsPerSecond: 20)]
        public void DogMountReport(int epoch, int result, Vector3 position, Quaternion rotation, int stamp) // net: callable-ok: authenticated current rider/root+Fetch authority, epoch and transition guards
        { AcceptDogMount(NetworkCalling.CallingPlayer, epoch, result, position, rotation, stamp); }

        private void AcceptDogMount(VRCPlayerApi caller, int epoch, int result, Vector3 position, Quaternion rotation, int stamp)
        {
            var p = DogPerformanceOf(liveId);
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(caller) || epoch != candyEpoch
                || p == null || !p.useMount || p.dogs == null || p.dogs.Length != 6
                || p.mountRider < 0 || p.mountRider >= p.dogs.Length) return;
            var rider = p.dogs[p.mountRider];
            if (rider == null || rider.dogRoot == null || p.choreographySpace == null) return;
            // Launch requires the complete unit. Recovery belongs to the root's
            // current sole writer even while the ancillary Fetch ownership catches up.
            if (Networking.GetOwner(rider.dogRoot.gameObject) != caller
                || (result == 1 && (rider.fetchController == null || Networking.GetOwner(rider.fetchController.gameObject) != caller))) return;
            int age = Networking.GetServerTimeInMilliseconds() - stamp;
            if (age < -1000 || age > 3000 || !(position.sqrMagnitude < 1e8f)
                || !(Quaternion.Angle(rotation, Quaternion.identity) <= 180.01f)) return;
            if (result == 2)
            {
                if (!CandyReserved || candyPreparing) return;
                StopDogPerformance();
                startMs = Networking.GetServerTimeInMilliseconds() - Mathf.RoundToInt((NightInSeconds + DurationOf(liveId)) * 1000f);
            }
            else if (result == 1)
            {
                float t = (stamp - startMs) * .001f - NightInSeconds;
                if (dogMountState != 0 || !CandyReserved || candyPreparing || !MountPairReady(p)
                    || t < p.mountStart - .05f || t > p.mountStart + .5f
                    || Vector3.Distance(position, p.choreographySpace.TransformPoint(p.mountTakeoff)) > .08f) return;
                dogMountState = 1; dogMountFrom = position; dogMountRotation = rotation;
                dogMountOwner = caller.playerId; dogMountChangedMs = stamp;
            }
            else if (result == 3)
            {
                if ((dogMountState != 2 && dogMountState != 3) || Vector3.Distance(position, rider.dogRoot.position) > .75f) return;
                var takeoff = p.choreographySpace.TransformPoint(p.mountTakeoff);
                var landing = p.choreographySpace.TransformPoint(p.mountLanding);
                dogMountFrom = position; dogMountRotation = rotation;
                dogMountTo = Vector3.Distance(position, takeoff) < Vector3.Distance(position, landing) ? takeoff : landing;
                dogMountState = 3; dogMountOwner = caller.playerId; dogMountChangedMs = stamp;
            }
            else if (result == 4)
            {
                bool normal = dogMountState == 1 && Elapsed - NightInSeconds >= p.mountLand;
                bool recovery = dogMountState == 3 && DogMountElapsed >= 1.35f;
                var target = normal ? p.choreographySpace.TransformPoint(p.mountLanding) : dogMountTo;
                if ((!normal && !recovery) || Vector3.Distance(position, target) > .04f) return;
                dogMountState = 4; dogMountChangedMs = stamp;
            }
            else return;
            RequestSerialization();
        }

        private void UpdateMountAuthority()
        {
            if (!Networking.IsOwner(gameObject)) return;
            var p = DogPerformanceOf(liveId);
            if (p == null || !p.useMount || candyPreparing || p.dogs == null
                || p.mountRider < 0 || p.mountRider >= p.dogs.Length) return;
            var rider = p.dogs[p.mountRider];
            if (rider == null || rider.dogRoot == null) return;
            var owner = Networking.GetOwner(rider.dogRoot.gameObject);
            if (DogRecovering)
            {
                if (dogMountState == 3 && (!Utilities.IsValid(owner) || owner.playerId != dogMountOwner || DogMountElapsed > 5f))
                { dogMountState = 2; dogMountChangedMs = Networking.GetServerTimeInMilliseconds(); RequestSerialization(); }
                return;
            }
            if (!CandyReserved || dogMountState == 4) return;
            float t = Elapsed - NightInSeconds;
            if (!p.enabled || !p.gameObject.activeInHierarchy || !MountPairReady(p) || !p.dogs[p.mountSupport].dogRoot.gameObject.activeInHierarchy
                || !rider.dogRoot.gameObject.activeInHierarchy || !rider.enabled
                || (dogMountOwner >= 0 && (!Utilities.IsValid(owner) || owner.playerId != dogMountOwner))
                || (dogMountState == 0 && t > p.mountStart + 1f)
                || (dogMountState == 1 && t > p.mountLand + 1f))
            {
                StopDogPerformance();
                startMs = Networking.GetServerTimeInMilliseconds() - Mathf.RoundToInt((NightInSeconds + DurationOf(liveId)) * 1000f);
                RequestSerialization();
            }
        }
    }
}
