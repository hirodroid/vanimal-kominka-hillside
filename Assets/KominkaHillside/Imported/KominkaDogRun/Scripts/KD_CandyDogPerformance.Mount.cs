using UnityEngine;

namespace KominkaDogRun
{
    public partial class KD_CandyDogPerformance
    {
        public bool useMount;
        public int mountRider = 3;
        public int mountSupport;
        public Transform mountAnchor;
        public Vector3 mountAttachmentPosition;
        public Quaternion mountAttachmentRotation = Quaternion.identity;
        public Vector3 mountTakeoff;
        public Vector3 mountSeat;
        public Vector3 mountLanding;
        public Vector3 mountSupportPosition;
        public float mountSupportYaw = 180;
        public float mountRiderYaw;
        public float mountStart = 104;
        public float mountTouch = 105.4f;
        public float mountLeave = 108;
        public float mountLand = 109.4f;
        public float mountAscentArc = .55f;
        public float mountDescentArc = .5f;
        public int mountAnimatorLayer = -1;

        private bool MountValid()
        {
            if (!useMount) return true;
            if ((!previewSceneOnly && liveSystem == null) || !useChoreography || choreographySpace == null || dogs == null || dogs.Length != 6
                || mountRider < 0 || mountRider >= 6 || mountSupport < 0 || mountSupport >= 6 || mountRider == mountSupport
                || dogs[mountRider] == null || dogs[mountSupport] == null || dogs[mountSupport].dogRoot == null
                || mountAnchor == null || !mountAnchor.IsChildOf(dogs[mountSupport].dogRoot) || mountAnimatorLayer < 0
                || !(mountStart > 0 && mountTouch > mountStart && mountLeave > mountTouch && mountLand > mountLeave && mountLand < duration)
                || mountAscentArc <= 0 || mountDescentArc <= 0) return false;
            return true; // Editor gate validates curves, floor, support, controller and all other dog routes.
        }

        private void ConfigureMount()
        {
            if (!useMount || dogs == null || mountRider < 0 || mountRider >= dogs.Length
                || mountSupport < 0 || mountSupport >= dogs.Length || choreographySpace == null) return;
            var rider = dogs[mountRider]; var support = dogs[mountSupport];
            if (rider == null) return;
            rider.candyMountSupport = support;
            if (support != null) support.candyMountRider = rider;
            rider.candyMountAnchor = mountAnchor;
            rider.candyMountAttachmentPosition = mountAttachmentPosition; rider.candyMountAttachmentRotation = mountAttachmentRotation;
            rider.candyMountTakeoff = choreographySpace.TransformPoint(mountTakeoff);
            rider.candyMountSeat = choreographySpace.TransformPoint(mountSeat);
            rider.candyMountLanding = choreographySpace.TransformPoint(mountLanding);
            rider.candyMountSupportPosition = choreographySpace.TransformPoint(mountSupportPosition);
            rider.candyMountSupportYaw = choreographySpace.rotation * Quaternion.Euler(0, mountSupportYaw, 0);
            rider.candyMountYaw = choreographySpace.rotation * Quaternion.Euler(0, mountRiderYaw, 0);
            rider.candyMountStart = mountStart; rider.candyMountTouch = mountTouch; rider.candyMountLeave = mountLeave; rider.candyMountLand = mountLand;
            rider.candyMountAscentArc = mountAscentArc; rider.candyMountDescentArc = mountDescentArc;
            rider.candyMountLayer = mountAnimatorLayer; rider.candyMountClock = 0;
            rider.candyMountLive = previewSceneOnly ? null : liveSystem;
            rider.candyMountEpoch = previewSceneOnly ? -1 : liveSystem.CandyEpoch;
            rider.candyMountArmed = true;
        }

        private bool UpdateMountClock(float t)
        {
            if (!useMount) return true;
            var rider = dogs[mountRider];
            if (rider.candyMountFailed)
            {
                if (previewSceneOnly) Fail("Mount support, takeoff or clock validation failed; the pair is recovering before normal movement resumes.");
                return false;
            }
            rider.candyMountClock = t;
            return true;
        }
    }
}
