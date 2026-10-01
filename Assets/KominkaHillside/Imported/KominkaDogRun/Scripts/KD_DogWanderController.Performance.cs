using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    public partial class KD_DogWanderController
    {
        const float BehaviorSelectionInterval = .1f;
        const int BehaviorSleep = 1, BehaviorLive = 2, BehaviorActivity = 3,
            BehaviorTreat = 4, BehaviorNudge = 5, BehaviorGreeting = 6,
            BehaviorRest = 7, BehaviorPaused = 8, BehaviorCalm = 9,
            BehaviorPlay = 10, BehaviorWander = 11;
        bool _behaviorSelectionValid;
        int _selectedBehavior, _behaviorSelectionSerial;
        float _nextBehaviorSelection;

        void ScheduleBehaviorSelection(float now)
        {
            // Fixed phases avoid drifting all six dogs back onto the same frame.
            // Missed ticks are skipped, never replayed in a catch-up loop.
            float phase = (Mathf.Max(0, dogIndex) % 6) * (BehaviorSelectionInterval / 6f);
            _nextBehaviorSelection = (Mathf.Floor((now - phase) / BehaviorSelectionInterval) + 1f)
                * BehaviorSelectionInterval + phase;
            _behaviorSelectionValid = true;
            _behaviorSelectionSerial++;
        }

        // IMP-DOG-034 A5: activities.Available(dogIndex) (他 Behaviour を 15 前後跨ぐ) を選び直し 1 回で 1 回だけ引く。
        // 選び直しでは UpdateActivity / UpdateGreeting(false) / UpdateGreeting(true) が順に引いていた。その間に走る
        // UpdateTreat / UpdateNudge / UpdateRest / UpdatePlay が Available の入力 (Leash・ディスク・集合・Fetch の Idle /
        // 撫で / 水の姿勢・Candy・撫で中・寝床・この犬の IsAsleep / IsResting) で変えうるのは _restPhase (UpdateRest の
        // EndRest) だけなので、休憩中かどうかが変わったら引き直す。それ以外は同じ tick で同じ値になる。
        // 無効化は UpdateTick の先頭と、継続 (ContinueSelectedBehavior) から選び直しへ落ちる所 (ScheduleBehaviorSelection の直前)
        bool _selfAvailableValid, _selfAvailableValue, _selfAvailableResting;

        bool SelfAvailable()
        {
            bool resting = _restPhase != RestPhaseNone;
            if (!_selfAvailableValid || _selfAvailableResting != resting)
            {
                _selfAvailableValue = activities.Available(dogIndex);
                _selfAvailableResting = resting; _selfAvailableValid = true;
            }
            return _selfAvailableValue;
        }

        bool ContinueSelectedBehavior()
        {
            switch (_selectedBehavior)
            {
                case BehaviorSleep: return UpdateSleep();
                case BehaviorLive: return UpdateLive();
                case BehaviorActivity: return UpdateActivity();
                case BehaviorTreat: return UpdateTreat();
                case BehaviorNudge: return UpdateNudge();
                case BehaviorGreeting: return UpdateGreeting(false);
                case BehaviorRest: return UpdateRest();
                case BehaviorWater: return UpdateWaterPlay(); // IMP-DOG-020
                case BehaviorPaused:
                    if (!_paused) return false;
                    Coast();
                    return true;
                case BehaviorCalm:
                    if (Time.time - _lastBusyTime >= postFetchCalm) return false;
                    Coast();
                    return true;
                case BehaviorPlay: return UpdatePlay(BehaviorServerTime());
                case BehaviorWander:
                    UpdateWander(BehaviorServerTime());
                    return true;
            }
            return false;
        }

        double BehaviorServerTime()
        {
            double time = Networking.GetServerTimeInSeconds();
            if (debugInstrument) _dbgServerTime = time;
            return time;
        }

        // IMP-INF-022: cache only deterministic plans, never live contact/movement decisions.
        bool _playScheduleValid;
        int _playScheduleCycle, _playScheduleCount, _playScheduleMaxDogs;
        float _playSchedulePeriod, _playScheduleMin, _playScheduleMax, _playScheduleBreak;
        float _playScheduleFaceChance;
        float _playCoreLength, _playWindowStart, _playWindowTotal;
        bool _playIsFace;
        int _playPartySize;
        int[] _playRankByDog, _playDogByRank;

        void EnsurePlaySchedule(int cycle, int count)
        {
            if (_playScheduleValid && _playScheduleCycle == cycle && _playScheduleCount == count
                && _playSchedulePeriod == playCyclePeriod && _playScheduleMin == playDurationMin
                && _playScheduleMax == playDurationMax && _playScheduleBreak == playBreakOff
                && _playScheduleFaceChance == playFaceChance && _playScheduleMaxDogs == playMaxDogs) return;

            _playScheduleCycle = cycle; _playScheduleCount = count;
            _playSchedulePeriod = playCyclePeriod; _playScheduleMin = playDurationMin;
            _playScheduleMax = playDurationMax; _playScheduleBreak = playBreakOff;
            _playScheduleFaceChance = playFaceChance; _playScheduleMaxDogs = playMaxDogs;
            _playCoreLength = Mathf.Lerp(playDurationMin, playDurationMax, PlayRand(cycle, PlayStreamCoreLen));
            _playWindowTotal = _playCoreLength + playBreakOff;
            _playWindowStart = PlayRand(cycle, PlayStreamWinStart) * Mathf.Max(playCyclePeriod - _playWindowTotal, 0f);
            _playIsFace = PlayRand(cycle, PlayStreamIsFace) < playFaceChance;
            int cap = Mathf.Clamp(playMaxDogs, 2, count);
            _playPartySize = _playIsFace ? 2 : 2 + (int)(PlayRand(cycle, PlayStreamPartySize) * (cap - 1));
            if (_playPartySize > cap) _playPartySize = cap;
            if (_playRoll == null || _playRoll.Length != count)
            {
                _playRoll = new float[count];
                _playRankByDog = new int[count];
                _playDogByRank = new int[count];
            }
            for (int i = 0; i < count; i++) _playRoll[i] = PlayRand(cycle, PlayStreamRollBase + i);
            for (int j = 0; j < count; j++)
            {
                int rank = 0;
                float value = _playRoll[j];
                for (int k = 0; k < count; k++)
                    if (_playRoll[k] < value || (_playRoll[k] == value && k < j)) rank++;
                _playRankByDog[j] = rank;
                _playDogByRank[rank] = j;
            }
            _playScheduleValid = true;
        }

        bool _wanderScheduleValid, _wanderTargetValid;
        int _wanderScheduleCycle, _wanderScheduleSalt, _wanderTargetHop, _wanderTargetSalt;
        float _wanderSchedulePeriod, _wanderScheduleMin, _wanderScheduleMax;
        float _wanderWindowLength, _wanderWindowStart, _wanderTargetRadius;
        Vector3 _wanderTargetOffset;

        void EnsureWanderSchedule(int cycle)
        {
            if (_wanderScheduleValid && _wanderScheduleCycle == cycle && _wanderScheduleSalt == seedSalt
                && _wanderSchedulePeriod == cyclePeriod && _wanderScheduleMin == wanderDurationMin
                && _wanderScheduleMax == wanderDurationMax) return;
            _wanderScheduleCycle = cycle; _wanderScheduleSalt = seedSalt;
            _wanderSchedulePeriod = cyclePeriod; _wanderScheduleMin = wanderDurationMin;
            _wanderScheduleMax = wanderDurationMax;
            _wanderWindowLength = Mathf.Lerp(wanderDurationMin, wanderDurationMax, Rand01(cycle, RandStreamWanderLen));
            float latest = Mathf.Max(cyclePeriod - _wanderWindowLength, 0f);
            _wanderWindowStart = Mathf.Lerp(latest * 0.25f, latest, Rand01(cycle, RandStreamWanderStart));
            _wanderScheduleValid = true;
        }

        Vector3 WanderTarget(int hop)
        {
            if (!_wanderTargetValid || _wanderTargetHop != hop || _wanderTargetSalt != seedSalt
                || _wanderTargetRadius != areaRadius)
            {
                float r = Mathf.Sqrt(Rand01(hop, RandStreamWaypointRadius)) * areaRadius;
                float angle = Rand01(hop, RandStreamWaypointAngle) * Mathf.PI * 2f;
                _wanderTargetOffset = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                _wanderTargetHop = hop; _wanderTargetSalt = seedSalt; _wanderTargetRadius = areaRadius;
                _wanderTargetValid = true;
            }
            // A moved/replaced center must take effect immediately without regenerating the offset.
            return areaCenter.position + _wanderTargetOffset;
        }

        bool _companionAnimationValid;
        Animator _companionAnimationTarget;
        bool _writtenSleep, _writtenCallReady;
        int _writtenCallPose;
        float _writtenCallTime;
        bool _breathVolumeValid;
        AudioSource _breathVolumeTarget;
        float _writtenBreathVolume;

        void OnEnable()
        {
            ResetPerformanceOutputs();
            _behaviorSelectionValid = false;
        }

        void ResetPerformanceOutputs()
        {
            _companionAnimationValid = false;
            _breathVolumeValid = false;
        }

        void ApplyCompanionAnimation(bool sleep, bool ready, int pose, float time)
        {
            if (!dogAnimator.isActiveAndEnabled) { _companionAnimationValid = false; return; }
            bool force = !_companionAnimationValid || _companionAnimationTarget != dogAnimator;
            if (force || sleep != _writtenSleep) dogAnimator.SetBool("Sleep", sleep);
            if (force || ready != _writtenCallReady) dogAnimator.SetBool("CallReady", ready);
            if (force || pose != _writtenCallPose) dogAnimator.SetInteger("CallPose", pose);
            if (force || time != _writtenCallTime) dogAnimator.SetFloat("CallTime", time);
            _writtenSleep = sleep; _writtenCallReady = ready;
            _writtenCallPose = pose; _writtenCallTime = time;
            _companionAnimationTarget = dogAnimator; _companionAnimationValid = true;
        }
    }
}
