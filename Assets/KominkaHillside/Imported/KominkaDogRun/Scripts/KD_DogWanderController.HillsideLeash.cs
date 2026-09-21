using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    // Hillside-only adaptation. Existing Owner-only locomotion and animation remain the single writer.
    //
    // IMP-DOG-003 "free walk with a destination": the dog knows the trail and wants to go, the holder
    // sets the pace through the leash. Decisions run on the dog Owner; position reaches everyone through
    // VRCObjectSync. Poses that others must see (LookAt, LieDown) are derived on every client from
    // synced facts (dog pose, holder position/stillness) so Owner and non-Owner screens agree.
    public partial class KD_DogWanderController
    {
        public KominkaHillside.KH_LeashManager hillsideLeashes;
        // IMP-DOG-005: the disc catcher moves the root (Ready and engaged); Wander yields exactly as it does for Fetch.
        public KominkaHillside.KH_FrisbeeCatch frisbeeCatch;
        private bool FrisbeeActive { get { return frisbeeCatch != null && !frisbeeCatch.IsIdle; } }
        // Fetch and the disc catcher own the Animator (bools and playback speed) while they run. Wander's
        // own writes must stand back for exactly as long, or the two fight over Animator.speed (IMP-DOG-009).
        private bool AnimatorYielded() { return (fetchController != null && !fetchController.IsIdle) || FrisbeeActive; }
        // Read by sibling dogs of the same holder to stagger invitations. Meaningful on the dog Owner only.
        [HideInInspector] public int leashMode;
        [HideInInspector] public float leashInviteEndedAt = -1000f; // pack-wide quiet gap between invitations
        const int LeashWalk = 0, LeashWait = 1, LeashReturn = 2, LeashDetour = 3, LeashInvite = 4,
            LeashRest = 5, LeashFree = 6, LeashArrived = 7;

        private NavMeshPath _leashPath;
        private Vector3[] _leashCorners;
        private int _leashCorner;
        private float _nextLeashPath;
        private Vector3 _leashGoal;
        private bool _wasLeashed;
        private int _routeNext = -1;   // next trail node; -1 = not on the trail
        private int _routeDest = -1;
        private float _nextRouteEntry;
        private float _nextHolderSample;
        private Vector3 _holderSample;      // where the holder was when they last counted as moving
        private float _holderMovedAt;
        private bool _arrived;              // held until the destination changes or the holder leaves its zone
        private float _nextTeleportTry;
        private bool _holderOffTrail;
        private float _modeUntil;
        private float _sniffUntil;
        private Vector3 _detourPoint;
        private float _nextDetourRoll;
        private int _inviteCount;
        private float _nextInviteAt;
        private bool _inviteBarked;
        // IMP-DOG-009: how fast the holder is actually moving right now. Owner only, never synced; the 1 Hz
        // SampleHolder below is a different question (has the holder stood still long enough to rest/invite?)
        // and stays as it was. This one picks which of the four speed tiers the dog walks at.
        private float _holderSpeed;         // EMA of the holder's horizontal body speed (m/s)
        private Vector3 _holderPrevPos;
        private bool _holderPrevValid;
        private int _holderTier;            // 0 still / 1 walking / 2 running (hysteresis)
        private int _holderTracked = -1;    // playerId the history belongs to; a new holder starts from zero
        private bool _leashYielded;         // last frame handed the root/Animator to Fetch or the disc
        private bool _wasDogOwner;          // Owner edge: a client that just took over has no history

        // ---- IMP-DOG-009 の仮値 (どれも Play で決める。速さの段そのものは KH_LeashManager 側) ----
        const float HolderSpeedTau = 0.4f;       // 速さの平滑化の時定数 (s)
        const float HolderJumpMeters = 1f;       // 1 フレームでこれ以上飛んだら不連続 (テレポート・リスポーン)
        const float HolderRunEnter = 3f, HolderRunExit = 2.4f;    // 人の走り 4 m/s と歩き 2 m/s の間で切る
        const float HolderMoveEnter = 0.7f, HolderMoveExit = 0.4f; // 「歩き出した」/「止まった」
        const float LeadHoldGain = 2f;           // lead-hold: リード端までの余裕 1 m あたりの上乗せ (m/s / m)
        const float LeadHoldMax = 1.1f;          // lead-hold を続けるリード長の倍率の上限 (復帰の 1.25 倍の内側)
        const float TurnSlowDeg = 60f;           // 体がこれだけ向き違うなら歩きまで落とす (横滑り対策)
        const float TurnEaseDeg = 30f;           // 中間は走りで頭打ち

        // IMP-DOG-010: 寄り道が「分かる」ための状態。姿勢は全クライアントが位置から導出するので同期は増やさない。
        private float _detourEnteredAt;     // 寄り道へ入った時刻 (到着の締切の起点)
        private bool _sniffExtended;        // 延長を使ったか (1 回だけ)
        private float _handDistPrev;        // 前フレームの手との距離 (人が離れていく向きかの判定)
        private float _stoppedSince = -1f;  // 足が止まった時刻 (-1 = 動いている)。非 Owner も観測 _speed から持てる
        private int _detourOk, _detourFail; // 行き先の候補が成立した / しなかった回数 (Console で成立率を見る)
        /// <summary>この犬が最後に足を止め終わった時刻 (寄り道・立ち寄り)。**同じ人の犬どうしが読む** —
        /// 次々と止まると散歩にならないので、群れ全体で静穏間隔を置く (Codex X4)。</summary>
        [HideInInspector] public float lastPauseEnd = -1000f;

        // ---- IMP-DOG-010 の仮値 (寄り道。どれも Play で決める。リード長 3 m で成立する幾何) ----
        const float DetourAheadMin = 0.5f, DetourAheadMax = 2f;   // 手を道へ落とした点から道沿いに前へ
        const float DetourSideMin = 1.8f, DetourSideMax = 2.3f;   // そこから道と直角に外す
        const float DetourTrailMin = 1.6f;      // 道の中心線からこれだけ離れないと、進路の揺れ (±1.1 m) と区別できない
        const float DetourHandMax = 0.8f;       // 手からリード長のこの倍率まで (寄り道の途中で紐が張らない)
        const float DetourPathMax = 5f;         // 行き先までの経路長の上限
        const float DetourArrive = 0.35f;       // ここまで近づいたら着いたとみなす
        const float DetourTrigger = 0.75f;      // 発生を許すリード長の倍率 (先行していても起きる)
        const float DetourRollInterval = 4f;    // 抽選の間隔 (s)
        const float DetourQuiet = 12f;          // 同じ人の犬すべてに掛かる静穏間隔 (s)
        const float DetourModeCap = 12f;        // 寄り道 1 回の上限 (s)
        const float DetourReachCap = 6f;        // ここまでに着けなければ諦める (s)
        const float DetourBreakLeash = 0.8f;    // 人が離れていくとき切り上げるリード長の倍率
        const float SniffMin = 3f, SniffMax = 5f;      // 嗅ぐ時間 (s)。人が気づいて足を止められる長さ
        const float SniffExtend = 5f;                  // 付き合ってくれたときの延長 (1 回だけ)
        const float SniffExtendLeash = 0.95f;          // 延長を許すリード長の倍率
        const float SniffFaceToleranceDeg = 15f;       // 向き切ってから頭を下げる (Rest の飲む枝より厳しめ)
        const float SniffFaceMinDist = 0.25f;          // これより近い行き先は「もう向いている」とみなす
        const float PoseStoppedFor = 0.5f;      // 姿勢の導出: これだけ連続で止まっていること
        const float PoseHandMin = 1.2f;         // 姿勢の導出: 手からこれだけ離れていること

        public void _InvalidateLeashAnimation() { _leashAnimationDirty = true; }
        private bool _leashAnimationDirty;

        // Sound only, but only the dog Owner (the one deciding invitations) may ring it.
        [NetworkCallable(maxEventsPerSecond: 3)]
        public void LeashInviteBark()
        {
            if (dogRoot == null || NetworkCalling.CallingPlayer != Networking.GetOwner(dogRoot.gameObject)) return;
            Bark();
        }

        private bool UpdateHillsideLeash()
        {
            if (hillsideLeashes == null || dogRoot == null) return false;
            if (!hillsideLeashes._IsLeashed(dogIndex))
            {
                // Unleashing also gives the run clip back its normal playback speed: off the leash the dog
                // never exceeds runSpeed, and Fetch may take over before the next wander frame (Codex D1).
                if (_wasLeashed) { _wasLeashed = false; _speed = 0f; _nextLeashPath = 0f; _routeNext = -1; leashMode = LeashFree; ResetHolderSpeed(); ReleaseAnimatorSpeed(); }
                return false;
            }
            if (!_wasLeashed)
            {
                _leashAnimationDirty = true; _routeNext = -1; _routeDest = -1; _inviteCount = 0; _nextInviteAt = 0f;
                _holderMovedAt = Time.time; _nextHolderSample = 0f; _holderOffTrail = false; _arrived = false; leashMode = LeashWalk;
                ResetHolderSpeed(); _holderTracked = hillsideLeashes.walkers[dogIndex]; _leashYielded = false;
                ResetLeashDecisions();
            }
            _wasLeashed = true;
            // Fetch cancels on its own owner tick. Never write the root concurrently with it.
            // The disc does the same (an airborne dog lands before the leash moves it; KH_FrisbeeCatch yields).
            // Both own the Animator too, so the hand-over gets cleaned up once, on the edge (Codex D2).
            if (AnimatorYielded())
            {
                if (!_leashYielded) { _leashYielded = true; YieldAnimator(); }
                return true;
            }
            if (_leashYielded) { _leashYielded = false; ResetHolderSpeed(); } // came back: the pace estimate is stale
            _wantSleep = false; _paused = false;
            _lastBusyTime = Time.time;
            VRCPlayerApi holder = VRCPlayerApi.GetPlayerById(hillsideLeashes.walkers[dogIndex]);
            if (!Utilities.IsValid(holder)) { if (_dogOwner) _speed = 0f; ApplyMoveAnim(); return true; }

            Vector3 handPos = hillsideLeashes._HandPosition(holder, hillsideLeashes.hands[dogIndex]);
            Vector3 holderPos = holder.GetPosition();
            // The pace comes from the body, not the hand: a hand waved beside a standing player is not walking.
            if (_dogOwner)
            {
                int holderId = hillsideLeashes.walkers[dogIndex];
                // A client that has just taken the dog over did not make the decision that is in progress
                // and has no history behind it, so it starts the walk from scratch (Codex D1).
                if (!_wasDogOwner || holderId != _holderTracked)
                { _holderTracked = holderId; ResetHolderSpeed(); ResetLeashDecisions(); }
                TrackHolderSpeed(holderPos);
            }
            _wasDogOwner = _dogOwner;
            Vector3 toHand = handPos - dogRoot.position; toHand.y = 0f;
            float handDist = toHand.magnitude;
            // Is the hand pulling away from the dog this frame? (the detour reads it to know when to look up)
            bool handOpening = handDist > _handDistPrev;
            _handDistPrev = handDist;
            float leash = hillsideLeashes.leashLength;
            int destNode = hillsideLeashes._DestinationNode(dogIndex);
            KominkaHillside.KH_WalkRoute route = hillsideLeashes.route;
            if (destNode != _routeDest)
            {
                // A new destination is a fresh start: leave rest/invite state and the stillness clock behind.
                _routeDest = destNode; _routeNext = -1; _arrived = false;
                _holderMovedAt = Time.time; _holderSample = holderPos; _inviteCount = 0; _nextInviteAt = 0f;
                if (leashMode == LeashRest || leashMode == LeashInvite) leashMode = LeashWalk;
            }
            if (destNode >= 0)
            {
                float destDist = Horizontal(dogRoot.position, route._Position(destNode));
                if (destDist < route._Radius(destNode)) _arrived = true;
                else if (Horizontal(holderPos, route._Position(destNode)) > route._Radius(destNode) + 6f) _arrived = false;
            }
            else _arrived = false;

            SampleHolder(holderPos, destNode, route);
            float stillFor = Time.time - _holderMovedAt;
            float restAfter = (_arrived ? 20f : hillsideLeashes.inviteAfter + hillsideLeashes.inviteInterval * hillsideLeashes.inviteLimit) + dogIndex * 2f;
            bool settling = stillFor > restAfter && handDist < 2.2f;
            LeashPose(holder, toHand, handDist, handPos, route, settling);
            if (!_dogOwner) { ApplyMoveAnim(); return true; }

            // 1. Stretched too far: come back to the hand (run), teleport only after a real separation.
            if (leashMode == LeashReturn) { if (handDist < leash * 0.6f) { leashMode = LeashWalk; _routeNext = -1; } }
            else if (handDist > leash * 1.25f)
            {
                leashMode = LeashReturn;
                // PCVR のチェック用 (IMP-DOG-009)。走っている人の後ろで戻りが周期的に出るなら、
                // 先行を止めている側 (lead-hold の帯) が狭すぎる
                Debug.Log("[KH_Leash] dog" + dogIndex + " Return handDist=" + handDist.ToString("F1")
                    + " holderSpeed=" + _holderSpeed.ToString("F1"));
            }
            if (leashMode == LeashReturn)
            {
                Vector3 back = handPos - toHand / Mathf.Max(handDist, 0.001f) * 0.9f;
                if (handDist > hillsideLeashes.catchUpDistance && Time.time >= _nextTeleportTry)
                {
                    _nextTeleportTry = Time.time + 1f;
                    if (TeleportTo(back)) return true;
                }
                LeashGo(back, handDist > leash * 1.6f ? hillsideLeashes.sprintSpeed : hillsideLeashes.dashSpeed, 0.2f);
                return true;
            }

            // 2. Holder has stood still for long (or all invitations are spent): settle down beside them until they move.
            if (stillFor > restAfter || (_inviteCount >= hillsideLeashes.inviteLimit && stillFor > 1f))
            {
                leashMode = LeashRest;
                if (handDist > 1.8f) LeashGo(handPos - toHand / handDist * 1.0f, walkSpeed, 0.2f); else Coast();
                return true;
            }

            // 3. No destination, holder off the trail, or no reachable entry: keep them company around the hand.
            if (destNode < 0 || _holderOffTrail) { _routeNext = -1; FollowHand(handDist, leash, LeashFree); return true; }
            if (_arrived) { _routeNext = -1; FollowHand(handDist, leash, LeashArrived); return true; }
            if (_routeNext < 0 && Time.time >= _nextRouteEntry)
            {
                _routeNext = ChooseEntry(destNode, route);
                if (_routeNext < 0) _nextRouteEntry = Time.time + 3f; // nothing reachable: retry later, stay with the holder
            }
            if (_routeNext < 0) { FollowHand(handDist, leash, LeashFree); return true; }

            // 4. Advance along the routing table; arrival at the destination node ends the trip.
            Vector3 nodePos = route._Position(_routeNext);
            if (Horizontal(dogRoot.position, nodePos) < route._Radius(_routeNext) && _routeNext != destNode)
            {
                _routeNext = route._Next(_routeNext, destNode);
                if (_routeNext < 0) { FollowHand(handDist, leash, LeashFree); return true; }
                nodePos = route._Position(_routeNext);
            }
            if (Horizontal(dogRoot.position, nodePos) > 40f) { _routeNext = -1; FollowHand(handDist, leash, LeashFree); return true; }

            // 5. Invitation: holder stopped while the dog waits ahead -> come back, bark once, look, then lead again.
            if (leashMode == LeashInvite)
            {
                if (stillFor < 1f) leashMode = LeashWalk;
                else if (handDist > 1.2f && !_inviteBarked) LeashGo(handPos - toHand / handDist * 0.9f, runSpeed * 0.8f, 0.2f);
                else
                {
                    Coast(); FaceTowards(holderPos);
                    if (!_inviteBarked)
                    {
                        _inviteBarked = true; _modeUntil = Time.time + 2.5f;
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(LeashInviteBark));
                    }
                    if (Time.time >= _modeUntil)
                    {
                        _inviteCount++; leashInviteEndedAt = Time.time;
                        _nextInviteAt = Time.time + hillsideLeashes.inviteInterval * (1f + 0.3f * _inviteCount);
                        leashMode = LeashWalk;
                    }
                }
                if (leashMode == LeashInvite) return true;
            }
            else if (stillFor > hillsideLeashes.inviteAfter + dogIndex * 1.3f && handDist > leash * 0.55f && DogAhead(nodePos, toHand)
                && _inviteCount < hillsideLeashes.inviteLimit && Time.time >= _nextInviteAt && !SiblingInviting())
            {
                leashMode = LeashInvite; _inviteBarked = false;
                return true;
            }

            // 6. Detour: leave the trail for the verge, sniff there, and pick the walk up again.
            //    The dog must visibly step off the path, stay long enough for the holder to notice, and
            //    look up by itself when the holder walks on (never be dragged off the spot).
            if (leashMode == LeashDetour)
            {
                // 切り上げ: 紐が伸びていて、人が歩いていて、さらに離れていく向きのときだけ。
                // 固定の待ち時間は置かない — ポーズは足が動いた時点で ApplyMoveAnim が下ろす (Codex X2/D1)
                bool leaving = handDist >= leash * DetourBreakLeash && HolderMoving() && handOpening;
                bool tooSlow = _sniffUntil <= 0f && Time.time > _detourEnteredAt + DetourReachCap;
                if (leaving || tooSlow || Time.time > _modeUntil) EndDetour();
                else if (Horizontal(dogRoot.position, _detourPoint) > DetourArrive && _sniffUntil <= 0f)
                { LeashGo(_detourPoint, walkSpeed, 0.1f); return true; }
                else
                {
                    Coast();
                    // **向き切ってから頭を下げる** (Rest の飲む枝と同じ作法)。着いた瞬間に立てると
                    // 嗅ぐ先を横目に見たまま止まる。ただし鼻先の数 cm のために回らない —
                    // 行き過ぎた犬がその場で 180° 回ると 30 deg/s では到着の締切を食い潰す
                    Vector3 want = _detourPoint - dogRoot.position; want.y = 0f;
                    bool facing = want.sqrMagnitude <= SniffFaceMinDist * SniffFaceMinDist
                        || Vector3.Angle(dogRoot.forward, want) <= SniffFaceToleranceDeg;
                    if (!facing) { FaceTowards(_detourPoint); return true; }
                    if (_speed > stopSpeedThreshold) return true;   // 止まり切ってから数え始める
                    if (_sniffUntil <= 0f)
                        _sniffUntil = Time.time + SniffMin + Rand01((int)Time.time, 4211) * (SniffMax - SniffMin);
                    else if (Time.time >= _sniffUntil)
                    {
                        // 人が足を止めて付き合ってくれたなら 1 回だけ延長する
                        if (!_sniffExtended && !HolderMoving() && handDist <= leash * SniffExtendLeash)
                        { _sniffExtended = true; _sniffUntil = Time.time + SniffExtend; }
                        else EndDetour();
                    }
                    if (leashMode == LeashDetour) return true;
                }
            }
            else if (leashMode == LeashWalk && handDist < leash * DetourTrigger && HolderMoving()
                && Time.time >= _nextDetourRoll)
            {
                _nextDetourRoll = Time.time + DetourRollInterval;
                // 犬ごとの差は値だけで作る (行動の種類は増やさない)
                float chance = hillsideLeashes.detourChance * (0.7f + 0.2f * (dogIndex % 4));
                if (Rand01((int)Time.time, 4201) < chance && !SiblingPausedRecently()
                    && PickDetour(nodePos, handPos, leash, route))
                {
                    leashMode = LeashDetour; _detourEnteredAt = Time.time;
                    _modeUntil = Time.time + DetourModeCap; _sniffUntil = 0f; _sniffExtended = false;
                    return true;
                }
            }

            // 7. Lead toward the next node; stop at the end of the leash when ahead of the holder.
            Vector3 nodeDir = nodePos - dogRoot.position; nodeDir.y = 0f;
            bool ahead = DogAhead(nodePos, toHand);
            float waitAt = leash * (0.78f + 0.04f * (dogIndex % 4));
            // Only a standing holder makes the dog stop and wait. While they walk or run the dog keeps
            // leading and trims its pace by the slack it has left (lead-hold) — stopping in front of a
            // running holder is what used to hand them the 1.25x leash end and start a return loop.
            bool leadHold = ahead && HolderMoving() && handDist >= leash * 0.45f && handDist <= leash * LeadHoldMax;
            if (ahead && handDist >= waitAt && !leadHold)
            {
                leashMode = LeashWait;
                Coast();
                if (_speed <= stopSpeedThreshold) FaceTowards(holderPos);
                return true;
            }
            leashMode = LeashWalk;
            float side = Mathf.Sin(Time.time * 0.2f + dogIndex * 1.3f) * 1.1f;
            Vector3 goal = nodePos + Vector3.Cross(Vector3.up, nodeDir.normalized) * side;
            float speed;
            if (!ahead && handDist > 1.2f)
                // Behind the holder: overtaking has to beat the pace they are actually keeping.
                speed = HolderRunning() ? hillsideLeashes.sprintSpeed
                    : (HolderMoving() ? hillsideLeashes.dashSpeed : runSpeed);
            else if (leadHold)
                // Match the holder and add just enough to keep the slack; never past the end of the leash.
                speed = Mathf.Clamp(_holderSpeed + LeadHoldGain * (waitAt - handDist), walkSpeed, hillsideLeashes.sprintSpeed);
            else if (handDist < leash * 0.45f && HolderMoving())
                speed = hillsideLeashes.dashSpeed; // plenty of slack and the holder is going: get out in front
            else speed = walkSpeed;
            LeashGo(goal, speed, 0.3f);
            return true;
        }

        // Every client: LookAt when stopped facing the holder, LieDown when settling, and the sniff pose
        // (Pickup, IMP-DOG-010) when the dog stands still out on the verge. All three derive from facts
        // every client already has — the dog's position/rotation through VRCObjectSync, the holder's own
        // position, the static trail — so nobody sees a pose the Owner did not choose and no synced
        // variable is added. **The Owner uses the same derivation**, not leashMode, so the screens cannot
        // disagree about which pose is up; the cost is that the pose follows the position updates rather
        // than the decision (an approximate display was chosen over another synced variable, Codex D1).
        // Bool は一度に 1 つ (設計書 §5): 伏せ > 嗅ぐ > 対面 の順で 1 つだけ立てて抜ける。
        private void LeashPose(VRCPlayerApi holder, Vector3 toHand, float handDist, Vector3 handPos,
            KominkaHillside.KH_WalkRoute route, bool settling)
        {
            bool stopped = _speed <= stopSpeedThreshold;
            // 「止まっている」ではなく「止まって何秒経ったか」で決める — 通過中のわずかな停止では立たない
            if (!stopped) _stoppedSince = -1f;
            else if (_stoppedSince < 0f) _stoppedSince = Time.time;
            if (settling && stopped) { _wantLieDown = true; return; }
            if (!stopped) return;
            // 嗅ぐ: 0.5 s 止まっていて、道の中心線から外れた所に居て、手からも離れていて、
            // その手はまだ道の上 (= 人は散歩を続けている)。Fetch / フリスビー中はここへ来ない (上で return 済み)
            if (route != null && _stoppedSince >= 0f && Time.time - _stoppedSince >= PoseStoppedFor
                && handDist >= PoseHandMin
                && route._DistanceToTrail(dogRoot.position) >= DetourTrailMin
                && route._DistanceToTrail(handPos) < hillsideLeashes.trailOffDistance)
            {
                _eatPose = true; // ApplyMoveAnim が Animator の Pickup へ落とす (IMP-TRT-001 の流用)
                return;
            }
            Vector3 forward = dogRoot.forward; forward.y = 0f;
            if (handDist < 0.8f || Vector3.Angle(forward, toHand) > 40f) return;
            _look = true;
            if (petController == null) return;
            petController._SetGaze(holder.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position, 0f);
            petController._SetUrge(0.5f);
        }

        // 1 Hz: did the holder move, and are they still near the trail (with hysteresis)?
        private void SampleHolder(Vector3 holderPos, int destNode, KominkaHillside.KH_WalkRoute route)
        {
            if (Time.time < _nextHolderSample) return;
            bool first = _nextHolderSample <= 0f;
            _nextHolderSample = Time.time + 1f;
            // Displacement from where stillness began, so a slow stroll still counts as moving.
            if (first || Vector3.Distance(holderPos, _holderSample) > 0.6f)
            {
                _holderMovedAt = Time.time; _inviteCount = 0; _nextInviteAt = 0f; _holderSample = holderPos;
            }
            if (route == null) { _holderOffTrail = true; return; }
            float trailDist = route._DistanceToTrail(holderPos);
            bool nearDest = destNode >= 0 && Horizontal(holderPos, route._Position(destNode)) < route._Radius(destNode) + 2f;
            if (_holderOffTrail) { if (trailDist < hillsideLeashes.trailOnDistance || nearDest) _holderOffTrail = false; }
            else if (trailDist > hillsideLeashes.trailOffDistance && !nearDest) _holderOffTrail = true;
        }

        private void FollowHand(float handDist, float leash, int mode)
        {
            leashMode = mode;
            // Three tiers around the hand: sprint from outside the leash, run while it is taut, walk on slack.
            float speed = handDist > leash * 1.2f ? hillsideLeashes.sprintSpeed
                : (handDist > leash * 0.7f ? hillsideLeashes.dashSpeed : walkSpeed);
            LeashGo(hillsideLeashes._WalkingTarget(dogIndex), speed, 0.2f);
        }

        // ---- IMP-DOG-009: the holder's own pace ----

        private bool HolderRunning() { return _holderTier >= 2; }
        private bool HolderMoving() { return _holderTier >= 1; }

        private void ResetHolderSpeed()
        {
            _holderPrevValid = false; _holderSpeed = 0f; _holderTier = 0;
        }

        /// <summary>Owner only, every frame. Teleports, respawns and station warps arrive as one huge step,
        /// so those only move the reference point — letting them into the average would read as "running"
        /// for the next second and send every dog off at full speed.</summary>
        private void TrackHolderSpeed(Vector3 holderPos)
        {
            float dt = Time.deltaTime;
            if (dt <= 0.0001f) return;
            holderPos.y = 0f;
            if (!_holderPrevValid) { _holderPrevPos = holderPos; _holderPrevValid = true; return; }
            float delta = Vector3.Distance(holderPos, _holderPrevPos);
            _holderPrevPos = holderPos;
            if (delta > HolderJumpMeters) return;
            _holderSpeed = Mathf.Lerp(_holderSpeed, delta / dt, Mathf.Min(1f, dt / HolderSpeedTau));

            // Hysteresis both ways: a pace hovering on a threshold must not flip the dog's speed every frame.
            int tier = _holderTier;
            if (_holderSpeed >= HolderRunEnter) tier = 2;
            else if (tier == 2 && _holderSpeed < HolderRunExit) tier = _holderSpeed >= HolderMoveExit ? 1 : 0;
            if (tier < 2)
            {
                if (_holderSpeed >= HolderMoveEnter) { if (tier < 1) tier = 1; }
                else if (tier == 1 && _holderSpeed < HolderMoveExit) tier = 0;
            }
            if (tier != _holderTier)
            {
                Debug.Log("[KH_Leash] dog" + dogIndex + " holder tier " + _holderTier + "->" + tier
                    + " v=" + _holderSpeed.ToString("F1"));
                _holderTier = tier;
            }
        }

        /// <summary>Rising edge of the hand-over to Fetch / the disc (Codex D2). They own the root and the
        /// Animator from here, so leave nothing of ours for them to fight: our own pose bool, the playback
        /// speed we scaled, and the walk state (detour point, timers, pace history) that will be stale by
        /// the time the dog comes back.</summary>
        private void YieldAnimator()
        {
            // Pickup は Fetch がくわえポーズで自分で使う Bool。**こちらが立てた分だけ**下ろす
            if (_wasEatPose && dogAnimator != null) dogAnimator.SetBool("Pickup", false);
            _eatPose = false; _wasEatPose = false;
            // Sit は散歩の側しか書かない Bool。立てたままだと座った犬がボールを追う
            if (_wasSit && dogAnimator != null) dogAnimator.SetBool("Sit", false);
            _wantSit = false; _wasSit = false;
            ReleaseAnimatorSpeed();
            ResetLeashDecisions();
            ResetHolderSpeed();
        }

        // Nearest reachable node that still leads to the destination. Path length, not straight distance,
        // so a dog below a switchback enters the lower leg instead of climbing across the slope.
        private int ChooseEntry(int destNode, KominkaHillside.KH_WalkRoute route)
        {
            if (route == null || route.count <= 0) return -1;
            if (_leashPath == null) _leashPath = new NavMeshPath();
            NavMeshHit from;
            if (!NavMesh.SamplePosition(dogRoot.position, out from, 2f, NavMesh.AllAreas)) return -1;
            int best = -1, nearest = -1; float bestScore = 1e9f, nearestLen = 1e9f;
            for (int i = 0; i < route.count; i++)
            {
                if (route._Cost(i, destNode) >= 1e5f) continue;
                Vector3 p = route._Position(i);
                if (Horizontal(dogRoot.position, p) > 30f) continue;
                NavMeshHit to;
                if (!NavMesh.SamplePosition(p, out to, 2f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _leashPath) || _leashPath.status != NavMeshPathStatus.PathComplete) continue;
                float len = PathLength(_leashPath);
                if (len < nearestLen) { nearestLen = len; nearest = i; }
                if (len > hillsideLeashes.entryMaxPath) continue;
                float score = len + route._Cost(i, destNode);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            _leashCorners = null; _nextLeashPath = 0f;
            return best >= 0 ? best : nearest;
        }

        /// <summary>Where to go and sniff. **Measured from the trail centreline, not from the dog**
        /// (IMP-DOG-010 / Codex X1): the old point was a diagonal step from wherever the dog stood, which
        /// stayed inside the path's own width and read as the walk wobbling. Here the hand is dropped onto
        /// the centreline, a point 0.5-2.0 m further along the walk is taken, and the verge is reached by
        /// stepping perpendicular 1.8-2.3 m — far enough to read as "off the path", close enough that a
        /// 3 m leash still reaches. The side the dog is already on is tried first so it does not cross in
        /// front of the holder; the other side is the fallback.</summary>
        private bool PickDetour(Vector3 nodePos, Vector3 handPos, float leash, KominkaHillside.KH_WalkRoute route)
        {
            if (route == null) return false;
            Vector3 onTrail = route._ProjectToTrail(handPos);
            Vector3 dir = route._TrailDirection(handPos);
            Vector3 toNode = nodePos - onTrail; toNode.y = 0f;
            if (Vector3.Dot(dir, toNode) < 0f) dir = -dir; // 「前」は次の通過点の側
            Vector3 basePoint = onTrail + dir * (DetourAheadMin
                + Rand01((int)Time.time, 4241) * (DetourAheadMax - DetourAheadMin));
            Vector3 perp = Vector3.Cross(Vector3.up, dir);
            Vector3 toDog = dogRoot.position - basePoint; toDog.y = 0f;
            float near = Vector3.Dot(perp, toDog) < 0f ? -1f : 1f;
            float side = DetourSideMin + Rand01((int)Time.time, 4251) * (DetourSideMax - DetourSideMin);
            if (TryDetour(basePoint + perp * (near * side), handPos, leash, route)) return true;
            return TryDetour(basePoint - perp * (near * side), handPos, leash, route);
        }

        // One candidate: off the trail, within reach of the hand, on the NavMesh and actually walkable to.
        // The NavMesh may shove the sample back onto the path, so the distance is re-checked after it.
        private bool TryDetour(Vector3 cand, Vector3 handPos, float leash, KominkaHillside.KH_WalkRoute route)
        {
            bool ok = false;
            NavMeshHit hit, from;
            if (route._DistanceToTrail(cand) >= DetourTrailMin
                && Horizontal(cand, handPos) <= leash * DetourHandMax
                && NavMesh.SamplePosition(cand, out hit, 1f, NavMesh.AllAreas)
                && route._DistanceToTrail(hit.position) >= DetourTrailMin
                && Horizontal(hit.position, handPos) <= leash * DetourHandMax
                && NavMesh.SamplePosition(dogRoot.position, out from, 2f, NavMesh.AllAreas))
            {
                if (_leashPath == null) _leashPath = new NavMeshPath();
                if (NavMesh.CalculatePath(from.position, hit.position, NavMesh.AllAreas, _leashPath)
                    && _leashPath.status == NavMeshPathStatus.PathComplete
                    && PathLength(_leashPath) <= DetourPathMax)
                {
                    _detourPoint = hit.position; _leashCorners = null; _nextLeashPath = 0f;
                    ok = true;
                }
            }
            // Codex: the geometry has to be shown to work on the real hillside, not argued about.
            // Owner only (this is only reached from the Owner's roll); one line per 10 candidates.
            if (ok) _detourOk++; else _detourFail++;
            if ((_detourOk + _detourFail) % 10 == 0)
                Debug.Log("[KH_Leash] dog" + dogIndex + " detour candidates ok=" + _detourOk + " fail=" + _detourFail);
            return ok;
        }

        // Every exit from the detour comes through here, so the quiet interval always starts when the dog
        // lifts its head - whatever ended it (Wave 3's stopovers will stamp the same field).
        private void EndDetour()
        {
            leashMode = LeashWalk;
            _sniffUntil = 0f; _sniffExtended = false;
            lastPauseEnd = Time.time;
            _nextDetourRoll = Time.time + DetourRollInterval;
        }

        // Codex X4: one holder's dogs stopping one after another is not a walk any more. The quiet interval
        // covers this dog too, so it is also the per-dog cooldown.
        private bool SiblingPausedRecently()
        {
            if (pack == null || pack.dogWander == null) return false;
            for (int i = 0; i < pack.dogWander.Length; i++)
            {
                if (pack.dogWander[i] == null) continue;
                if (hillsideLeashes.walkers[i] != hillsideLeashes.walkers[dogIndex]) continue;
                if (Time.time - pack.dogWander[i].lastPauseEnd < DetourQuiet) return true;
            }
            return false;
        }

        // Anything the Owner alone was deciding: dropped on attach and when this client takes the dog over.
        private void ResetLeashDecisions()
        {
            if (leashMode == LeashDetour) leashMode = LeashWalk;
            _sniffUntil = 0f; _sniffExtended = false; _modeUntil = 0f;
            _detourPoint = dogRoot.position; _detourEnteredAt = Time.time;
            _stoppedSince = -1f; _handDistPrev = 0f;
            _nextDetourRoll = Time.time + DetourRollInterval;
        }

        private bool SiblingInviting()
        {
            if (pack == null || pack.dogWander == null) return false;
            for (int i = 0; i < pack.dogWander.Length; i++)
            {
                if (i == dogIndex || pack.dogWander[i] == null) continue;
                if (hillsideLeashes.walkers[i] != hillsideLeashes.walkers[dogIndex]) continue;
                if (pack.dogWander[i].leashMode == LeashInvite) return true;
                if (Time.time - pack.dogWander[i].leashInviteEndedAt < hillsideLeashes.inviteInterval * 0.5f) return true; // quiet gap for the holder
            }
            return false;
        }

        // Recover only to a baked, reachable surface after a large separation/teleport.
        private bool TeleportTo(Vector3 goal)
        {
            if (_leashPath == null) _leashPath = new NavMeshPath();
            NavMeshHit from, to;
            if (!NavMesh.SamplePosition(dogRoot.position, out from, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(goal, out to, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _leashPath) || _leashPath.status != NavMeshPathStatus.PathComplete) return false;
            VRCObjectSync sync = dogRoot.GetComponent<VRCObjectSync>();
            if (sync != null) sync.FlagDiscontinuity();
            dogRoot.position = to.position; // net: owner-only: _dogOwner checked by the caller
            _speed = 0f; _leashCorners = null; _nextLeashPath = 0f; _routeNext = -1;
            ApplyMoveAnim();
            return true;
        }

        // Walks toward goal along the baked NavMesh. The path is refreshed every 0.35 s or when the goal moves.
        private void LeashGo(Vector3 goal, float speed, float stop)
        {
            if (Time.time >= _nextLeashPath || Vector3.Distance(goal, _leashGoal) > 0.6f)
            {
                _nextLeashPath = Time.time + 0.35f; _leashGoal = goal;
                if (_leashPath == null) _leashPath = new NavMeshPath();
                NavMeshHit from, to;
                _leashCorners = null;
                if (NavMesh.SamplePosition(dogRoot.position, out from, 2f, NavMesh.AllAreas)
                    && NavMesh.SamplePosition(goal, out to, 2f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _leashPath)
                    && _leashPath.status != NavMeshPathStatus.PathInvalid)
                { _leashCorners = _leashPath.corners; _leashCorner = 1; }
            }
            if (_leashCorners == null || _leashCorners.Length < 2) { Coast(); return; }
            while (_leashCorner < _leashCorners.Length - 1 && Vector3.Distance(dogRoot.position, _leashCorners[_leashCorner]) < 0.35f) _leashCorner++;
            Vector3 target = _leashCorners[_leashCorner];
            Vector3 delta = target - dogRoot.position; delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 0.15f) { Coast(); return; }
            float clearance = WallClearance(dogRoot.position, delta / distance, distance);
            if (clearance < 0.05f) { Coast(); return; }
            // IMP-DOG-009: StepToward points the movement at the target instantly but turns the body only at
            // turnSpeedDegPerSec, so at 7 m/s the dog side-slips through a corner. Hold the pace back until
            // the body has come round. The 0.35 m corner advance and the shared decel/turn constants stay.
            Vector3 forward = dogRoot.forward; forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
            {
                float turn = Vector3.Angle(forward, delta / distance);
                if (turn > TurnSlowDeg) { if (speed > walkSpeed) speed = walkSpeed; }
                else if (turn > TurnEaseDeg && speed > hillsideLeashes.dashSpeed) speed = hillsideLeashes.dashSpeed;
            }
            StepToward(dogRoot.position + delta / distance * clearance, _leashCorner == _leashCorners.Length - 1 ? stop : 0.05f, speed);
        }

        // Gathering (KD_GroupActivities mode 1) on the hillside: follow a complete NavMesh path to the slot.
        // Returns false when no complete path exists so the caller walks straight (the agreed "pass through" fallback).
        private float _nextGatherPath;
        private bool _gatherPathOk;
        private bool GatherStep(Vector3 goal, float speed)
        {
            if (!_dogOwner) { ApplyMoveAnim(); return true; }
            if (Time.time >= _nextGatherPath || Vector3.Distance(goal, _leashGoal) > 0.6f)
            {
                _nextGatherPath = Time.time + 0.5f; _leashGoal = goal;
                if (_leashPath == null) _leashPath = new NavMeshPath();
                NavMeshHit from, to;
                _leashCorners = null; _gatherPathOk = false;
                if (NavMesh.SamplePosition(dogRoot.position, out from, 2f, NavMesh.AllAreas)
                    && NavMesh.SamplePosition(goal, out to, 2f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _leashPath)
                    && _leashPath.status == NavMeshPathStatus.PathComplete)
                { _leashCorners = _leashPath.corners; _leashCorner = 1; _gatherPathOk = true; }
            }
            if (!_gatherPathOk || _leashCorners == null || _leashCorners.Length < 2) return false;
            while (_leashCorner < _leashCorners.Length - 1 && Vector3.Distance(dogRoot.position, _leashCorners[_leashCorner]) < 0.35f) _leashCorner++;
            bool last = _leashCorner == _leashCorners.Length - 1;
            StepToward(last ? goal : _leashCorners[_leashCorner], 0f, speed);
            return true;
        }

        private float PathLength(NavMeshPath path)
        {
            Vector3[] c = path.corners;
            float len = 0f;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            return len;
        }

        // The dog is between the holder and the next node (node and hand lie in opposite directions).
        private bool DogAhead(Vector3 nodePos, Vector3 toHand)
        {
            Vector3 nodeDir = nodePos - dogRoot.position; nodeDir.y = 0f;
            return Vector3.Dot(nodeDir, toHand) < 0f;
        }

        private float Horizontal(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
