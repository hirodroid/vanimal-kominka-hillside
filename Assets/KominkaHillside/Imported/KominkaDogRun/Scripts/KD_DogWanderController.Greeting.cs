using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    public partial class KD_DogWanderController
    {
        public KD_DogBook dogBook;
        // IMP-DOG-035: casual drop-by. One lottery per wander cycle while the dog is free.
        // Intervals are enforced from the visit tags, so an owner change keeps the same spacing.
        public float dropByChance = .35f;
        public float dropByDogInterval = 240f;
        public float dropByPlayerInterval = 600f;
        public float dropByAfterGreeting = 180f;
        private bool _greetingActive;
        private int _greetingPlayer = -1;
        private int _dropByCycle = int.MinValue;
        private float _greetingStarted, _nextGreetingPick, _nextGreetingAck;
        private float _greetingArrivalAt = -1f;
        public const int DbgGreeting = 18;
        public const int DbgDropBy = 19;

        bool UpdateGreeting(bool mayStart)
        {
            if (dogBook == null || fetchController == null || activities == null) return false;
            bool active = fetchController.GreetingActive;
            if (!SelfAvailable()) return false;   // IMP-DOG-034 A5: tick 内で 1 回 (Performance.cs)
            if (!active && mayStart && _dogOwner && Time.time >= _nextGreetingPick)
            {
                _nextGreetingPick = Time.time + 2f;
                int tick = Networking.GetServerTimeInMilliseconds() / 2000;
                int player = dogBook.PendingPlayer(dogIndex, Rand01(tick, 91), cyclePeriod);
                // 裏山改変 (2026-10-02 ユーザー要望「洗い場には他の犬は勝手に近寄ってこない」): 洗い場の円の中に居る人へは
                // 迎えに行かない (2 s 後にまた引く。人が円を出たら迎える)。HillsideStationLeave.cs
                if (player >= 0) { if (!KeepOutPlayer(player)) fetchController._BeginGreeting(player); }
                else TryDropBy();
                active = fetchController.GreetingActive;
            }
            if (!active) return false;
            bool dropBy = fetchController.GreetingIsDropBy;
            int id = fetchController.AttentionPlayerId;
            var person = VRCPlayerApi.GetPlayerById(id);
            var tag = dogBook.TagFor(id);
            if (!Utilities.IsValid(person)) return false;
            // A missing cache entry must not cancel a newly transferred greeting.
            if (tag != null && tag.FavoriteDog != dogIndex) return false;
            if (!dropBy && tag != null && tag.Greeted && tag.GreetedDog != dogIndex) return false;
            if (id != _greetingPlayer)
            {
                _greetingPlayer = id; _greetingStarted = Time.time; _nextGreetingAck = 0f; _greetingArrivalAt = -1f;
            }
            int nowMs = Networking.GetServerTimeInMilliseconds();
            // A drop-by mark younger than the visit window is this visit, whoever is watching and whenever they began.
            int doneMs = 0;
            if (tag != null) doneMs = dropBy ? tag.VisitedMs(dogIndex) : tag.GreetedMs;
            bool done = tag != null && (dropBy
                ? doneMs != 0 && nowMs - doneMs < KD_PlayerDogTag.VisitWindowMs
                : tag.Greeted);
            if (!fetchController.GreetingArrived && done) return false;
            if (!fetchController.GreetingArrived && Time.time - _greetingStarted > 35f) return false;
            if (fetchController.GreetingArrived)
            {
                if (_greetingArrivalAt < 0f) _greetingArrivalAt = Time.time;
                if (!done && Time.time - _greetingArrivalAt > 15f) return false;
            }
            if (fetchController.GreetingArrived && done && nowMs - doneMs >= 9000) return false;

            Vector3 playerPos = person.GetPosition();
            Vector3 goal = SleepSpot(person, playerPos);
            // 裏山改変 (2026-10-02): 向かう途中で相手が洗い場の円へ入ったら、そこへは行かない (このあいさつは終わり、後でまた引く)
            if (!fetchController.GreetingArrived && KeepOutAt(goal) >= 0) return false;
            _greetingActive = true;
            if (debugInstrument) _dbgReason = dropBy ? DbgDropBy : DbgGreeting;
            Vector3 dogPos = dogRoot.position;
            Vector3 delta = goal - dogPos; delta.y = 0f;
            float distance = delta.magnitude;
            if (!fetchController.GreetingArrived)
            {
                if (distance <= arriveRadius && _speed <= stopSpeedThreshold
                    && Mathf.Abs(playerPos.y - dogPos.y) < 1.5f)
                {
                    Coast(); FaceTowards(playerPos);
                    Vector3 facing = playerPos - dogPos; facing.y = 0f;
                    if (_dogOwner && Vector3.Angle(dogRoot.forward, facing) < 25f) fetchController._ArriveGreeting();
                }
                else
                {
                    // Unlike the explicit call action, a spontaneous welcome does not pass through walls.
                    float clear = distance > .001f ? WallClearance(dogPos, delta / distance, distance) : 0f;
                    Vector3 target = clear < distance ? dogPos + delta.normalized * clear : goal;
                    StepToward(target, 0f, walkSpeed);
                }
            }
            else
            {
                // Stay where the greeting landed; do not follow someone around indefinitely.
                _look = true; Coast(); FaceTowards(playerPos);
            }
            // Notify in the arrival frame, before a pet/Fetch can take over on the next frame.
            if (_dogOwner && fetchController.GreetingArrived && tag != null && !done && Time.time >= _nextGreetingAck)
            {
                _nextGreetingAck = Time.time + 1f;
                tag.SendCustomNetworkEvent(NetworkEventTarget.Owner, dropBy ? "MarkVisited" : "MarkGreeted", dogIndex);
            }
            if (petController != null)
            {
                petController._SetGaze(person.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position, 0f);
                petController._SetUrge(.55f);
            }
            return true;
        }
        // One deterministic lottery per wander cycle. A cycle spent busy is simply skipped,
        // so a dog that is petted, playing or resting never accumulates pending visits.
        void TryDropBy()
        {
            if (dropByChance <= 0f) return;
            double serverTime = Networking.GetServerTimeInSeconds();
            int cycleIdx = (int)(serverTime / cyclePeriod);
            if (serverTime - (double)cycleIdx * cyclePeriod < 0) cycleIdx -= 1;
            if (cycleIdx == _dropByCycle) return;
            _dropByCycle = cycleIdx;
            if (Rand01(cycleIdx, 92) >= dropByChance) return;
            int player = dogBook.DropByPlayer(dogIndex, Rand01(cycleIdx, 93), dropByDogInterval, dropByPlayerInterval, dropByAfterGreeting);
            // 裏山改変 (2026-10-02): 洗い場の円の中に居る人へは立ち寄らない (この周期の抽選は使い切り)
            if (player >= 0 && !KeepOutPlayer(player)) fetchController._BeginDropBy(player);
        }
        void FinishInterruptedGreeting()
        {
            if (_greetingActive) return;
            if (_greetingPlayer >= 0)
            {
                _greetingPlayer = -1;
                _nextGreetingPick = Time.time + Mathf.Max(45f, cyclePeriod * 2f);
            }
            // Safe even if PetSync/Fetch already took the target. Only greeting markers are cleared.
            if (_dogOwner && fetchController != null) fetchController._EndGreeting();
        }
    }
}
