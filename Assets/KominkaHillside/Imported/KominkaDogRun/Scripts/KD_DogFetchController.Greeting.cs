using VRC.SDKBase;

namespace KominkaDogRun
{
    public partial class KD_DogFetchController
    {
        // Negative target IDs have no thrown object. Idle remains pettable/fetchable.
        // -2/-3: once-per-visit welcome (IMP-DOG-033). -4/-5: casual drop-by (IMP-DOG-035).
        private const int GreetingWalkingTarget = -2, GreetingArrivedTarget = -3;
        private const int DropByWalkingTarget = -4, DropByArrivedTarget = -5;
        public bool GreetingActive { get { return IsIdle && petState < 0 && attentionPlayerId >= 0
            && targetObjectId <= GreetingWalkingTarget && targetObjectId >= DropByArrivedTarget; } }
        public bool GreetingArrived { get { return GreetingActive
            && (targetObjectId == GreetingArrivedTarget || targetObjectId == DropByArrivedTarget); } }
        public bool GreetingIsDropBy { get { return GreetingActive
            && (targetObjectId == DropByWalkingTarget || targetObjectId == DropByArrivedTarget); } }
        public void _BeginGreeting(int player) { Begin(player, GreetingWalkingTarget); }
        public void _BeginDropBy(int player) { Begin(player, DropByWalkingTarget); }
        void Begin(int player, int walkingTarget)
        {
            if (!IsDogOwner() || !IsIdle || petState >= 0 || !Utilities.IsValid(VRCPlayerApi.GetPlayerById(player))) return;
            attentionPlayerId = player; targetObjectId = walkingTarget; RequestSerialization();
        }
        public void _ArriveGreeting()
        {
            if (!IsDogOwner() || !GreetingActive || GreetingArrived) return;
            targetObjectId = targetObjectId == DropByWalkingTarget ? DropByArrivedTarget : GreetingArrivedTarget;
            RequestSerialization();
        }
        public void _EndGreeting()
        {
            if (!IsDogOwner() || !IsIdle || targetObjectId > GreetingWalkingTarget || targetObjectId < DropByArrivedTarget) return;
            targetObjectId = -1;
            // PetSync may already have taken over attentionPlayerId in this frame.
            if (petState < 0) attentionPlayerId = -1;
            RequestSerialization();
        }
    }
}
