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
            LeashRest = 5, LeashFree = 6, LeashArrived = 7, LeashStopover = 8;

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
        private float _prevYaw;             // 前フレームの向き (その場旋回を「止まっている」に数えない)
        private bool _poseTrailActive;      // 道端の嗅ぐポーズを今出しているか (しきい値のヒステリシス)
        private float _alignUntil;          // 着いたあと向きを整える締切 (0 = まだ着いていない)
        private int _prevLeashMode;         // 前フレームのモード (割り込みで抜けたのを拾う)
        private bool _pauseEndedThisFrame;  // このフレーム EndPause を通ったか (割り込み検出の二重発火よけ)
        // 足を止める一連の流れをどこまで通れたか。**見せ方の設計を数字で確かめるための計数** (Codex U)
        private int _detourRolls, _detourOk, _detourArrived, _detourPosed;
        private int _stopoverRolls, _stopoverOk, _stopoverArrived, _stopoverPosed;
        private bool _pausePosed;           // この滞在で一度ポーズが出たか (立ち上がりだけ数える)
        /// <summary>この犬の直前の寄り道・立ち寄りが終わった時刻。**中断・失敗も含む** (着けなかった、
        /// 人に切り上げられた、休憩や帰還に割り込まれた、のどれでもここが動く)。**同じ人の犬どうしが読む** —
        /// 次々と止まると散歩にならないので群れ全体で静穏間隔を置く (Codex X4)。
        /// ローカル値なので **Owner が変わると引き継がれない** (引き継いだ側は自分の -1000 から数え直す)。</summary>
        [HideInInspector] public float lastPauseEnd = -1000f;

        // ---- IMP-DOG-010 の仮値 (寄り道。どれも Play で決める。リード長 3 m で成立する幾何) ----
        const float DetourAheadMin = 0.5f, DetourAheadMax = 2f;   // 手を道へ落とした点から道沿いに前へ
        const float DetourSideMin = 1.8f, DetourSideMax = 2.3f;   // そこから道と直角に外す
        const float DetourTrailMin = 1.6f;      // 道の中心線からこれだけ離れないと、進路の揺れ (±1.1 m) と区別できない
        const float DetourHandMax = 0.8f;       // 手からリード長のこの倍率まで (寄り道の途中で紐が張らない)
        const float DetourPathMax = 5f;         // 行き先までの経路長の上限
        // **姿勢のしきい値より内側で止まらせない** (Codex 1)。候補は道から 1.6 m 以上、そこへ 0.2 m まで
        // 寄ってから止まるので、止まった位置は最悪でも 1.4 m — 姿勢の判定 (1.5 / 保持 1.3) が通る
        const float DetourArrive = 0.2f;        // ここまで近づいたら着いたとみなす (通過点の 0.35 とは別)
        const float DetourTrigger = 0.95f;      // 発生を許すリード長の倍率。**先行保持の帯 (0.78〜0.90×)
                                                // より外に取る** — ここを 0.75 にすると、ずっと先行している
                                                // 犬は一度も抽選されない (Codex 2)。行き先は手から 0.8× 以内
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
        const float PauseAlignCap = 3f;                // 向きを整えるのに使ってよい時間。過ぎたら整ったものとして進む
        const float DetourChanceBase = 0.7f;           // 犬ごとの差は値だけで作る (行動の種類は増やさない)
        const float DetourChanceSpread = 0.2f;
        const int DetourChanceGroups = 4;
        // ---- 姿勢の導出のしきい値。**入るときと出るときで別** — 境目で毎フレーム出たり消えたりさせない ----
        const float PoseStoppedFor = 0.5f;      // これだけ連続で止まっていること
        const float PoseYawRateMax = 20f;       // その場で回っている間は「止まっている」に数えない (deg/s)
        const float PoseTrailEnter = 1.5f;      // 道の中心線からこれだけ離れていたら嗅ぐ姿勢に入る
        const float PoseTrailHold = 1.3f;       // 一度出た姿勢はここまでは保つ
        const float PoseTrailHalfWidth = 3f;    // 「手はまだ道の上」とみなす幅。道を離れた判定 (6 m) より狭い
        const float PoseHandMin = 1.2f;         // 手からこれだけ離れていたら姿勢に入る
        const float PoseHandHold = 1f;          // 一度出た姿勢はここまでは保つ

        // IMP-DOG-010 B: 立ち寄り (道沿いの決まった場所)。場所は静的データ、選ぶのは Owner、姿勢は位置から
        // 導出 — 寄り道と同じく同期変数は 1 つも増えない。
        private float _stopoverEnteredAt;       // その場所へ向かい始めた時刻 (到着の締切の起点)
        private float _stopoverModeUntil;       // 立ち寄り 1 回の上限
        private float _stopoverUntil;           // ポーズを保つ終了時刻 (0 = まだ着いていない)
        private bool _stopoverExtended;         // 延長を使ったか (1 回だけ)
        private float _lastStopoverAt = -1000f; // この犬が最後に立ち寄った時刻 (犬ごとのクールダウン)
        private int _lastStopoverIndex = -1;    // 直前に立ち寄った場所 (続けて同じ場所へは行かない)
        private int _stopoverRolledFor = -1;    // この通過で抽選を済ませた場所 (通り過ぎたら外れる)
        /// <summary>今この犬が滞在している立ち寄り場所 (-1 = 居ない)。**同じ人の犬どうしが読む** —
        /// 1 つの場所に二匹入らないため。`leashMode` と同じく **その犬の Owner の画面でだけ意味を持つ**。</summary>
        [HideInInspector] public int stopoverIndex = -1;

        // ---- IMP-DOG-010 の仮値 (立ち寄り。場所そのものは KH_WalkStopovers / Walking 4d) ----
        const float StopoverRange = 6f;         // 犬からこの距離に入ったら抽選する
        const float StopoverForget = 8f;        // ここまで離れたら「通り過ぎた」= 次の通過でまた引ける
        const float StopoverHandMax = 0.8f;     // 手からリード長のこの倍率まで (寄り道と同じ理由)
        const float StopoverArrive = 0.2f;      // 着いたとみなす距離 (寄り道と同じ — 場所の上で止まらせる)
        const float StopoverReachCap = 6f;      // ここまでに着けなければ諦める (s)
        const float StopoverModeCap = 14f;      // 立ち寄り 1 回の上限 (s)
        const float StopoverCooldown = 45f;     // 同じ犬が次に立ち寄れるまで (s)
        const float StopoverHoldMin = 4f, StopoverHoldMax = 8f; // ポーズを保つ時間 (s)。寄り道より長い
        const float StopoverPoseRadius = 0.7f;  // 姿勢の導出: 場所からこの距離で止まっていること
        const float StopoverPoseHandMin = 1f;   // 姿勢の導出: 手からこれだけ離れていること

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
                if (_wasLeashed) { _wasLeashed = false; _speed = 0f; _nextLeashPath = 0f; _routeNext = -1;
                    ResetPauseAndPoseState(); leashMode = LeashFree; ResetHolderSpeed(); ReleaseAnimatorSpeed(); }
                return false;
            }
            if (!_wasLeashed)
            {
                _leashAnimationDirty = true; _routeNext = -1; _routeDest = -1; _inviteCount = 0; _nextInviteAt = 0f;
                _holderMovedAt = Time.time; _nextHolderSample = 0f; _holderOffTrail = false; _arrived = false; leashMode = LeashWalk;
                ResetHolderSpeed(); _holderTracked = hillsideLeashes.walkers[dogIndex]; _leashYielded = false;
                ResetPauseAndPoseState();
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
            // Rest, the return, the off-trail branch and the invitation all take the dog out of a detour or
            // a stopover without asking, and every one of them leaves through its own `return`. So the end
            // of a pause is detected here, at the top of the next frame, by comparing modes - one place
            // instead of a stamp at each override, which is what was missed before (Codex 3).
            if ((_prevLeashMode == LeashDetour || _prevLeashMode == LeashStopover)
                && leashMode != _prevLeashMode && !_pauseEndedThisFrame)
                EndPause(_prevLeashMode, "interrupted");
            _pauseEndedThisFrame = false;
            _prevLeashMode = leashMode;
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
                { _holderTracked = holderId; ResetHolderSpeed(); ResetPauseAndPoseState(); }
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

            // The stopover badge means "standing at that spot right now" and nothing else. Rest, the
            // return and the off-trail branches below all take the dog away without asking, so the badge
            // is dropped here rather than at each of them - otherwise a sibling waits for a spot forever.
            if (leashMode != LeashStopover && stopoverIndex >= 0) stopoverIndex = -1;

            // 1. Stretched too far: come back to the hand (run), teleport only after a real separation.
            if (leashMode == LeashReturn) { if (handDist < leash * 0.6f) { leashMode = LeashWalk; _routeNext = -1; } }
            else if (handDist > leash * 1.25f)
            {
                if (leashMode == LeashStopover) EndPause(LeashStopover, "cutoff"); // 立ち寄りの記録は残して帰る
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
            // 足を止めている最中の犬に催促を上書きさせない (Codex U) — 嗅いでいる犬が急に
            // 人を呼びに戻ると、見ている側には「何をしていたのか」が読めない
            else if (leashMode != LeashDetour && leashMode != LeashStopover
                && stillFor > hillsideLeashes.inviteAfter + dogIndex * 1.3f && handDist > leash * 0.55f && DogAhead(nodePos, toHand)
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
                // 到着 → 向きを整える → 嗅ぐ、の 3 段。締切は段ごとに分ける (Codex 4) —
                // ひとつの締切にまとめると、向きを整えている間に「着けなかった」と判定される
                bool atSpot = _sniffUntil > 0f || Horizontal(dogRoot.position, _detourPoint) <= DetourArrive;
                if (atSpot && _alignUntil <= 0f) { _alignUntil = Time.time + PauseAlignCap; _detourArrived++; }
                if (!_pausePosed && _eatPose) { _pausePosed = true; _detourPosed++; }
                // 切り上げ: 紐が伸びていて、人が歩いていて、さらに離れていく向きのときだけ。
                // 固定の待ち時間は置かない — ポーズは足が動いた時点で ApplyMoveAnim が下ろす (Codex X2/D1)
                bool leaving = handDist >= leash * DetourBreakLeash && HolderMoving() && handOpening;
                bool tooSlow = _alignUntil <= 0f && Time.time > _detourEnteredAt + DetourReachCap;
                if (leaving) EndPause(LeashDetour, "cutoff");
                else if (tooSlow) EndPause(LeashDetour, "abort");
                else if (Time.time > _modeUntil) EndPause(LeashDetour, "cap");
                else if (!atSpot) { LeashGo(_detourPoint, walkSpeed, 0.1f); return true; }
                else
                {
                    Coast();
                    // **向き切ってから頭を下げる** (Rest の飲む枝と同じ作法)。着いた瞬間に立てると
                    // 嗅ぐ先を横目に見たまま止まる。ただし鼻先の数 cm のために回らない —
                    // 行き過ぎた犬がその場で 180° 回ると 30 deg/s では整え切れない
                    Vector3 want = _detourPoint - dogRoot.position; want.y = 0f;
                    bool facing = Time.time >= _alignUntil
                        || want.sqrMagnitude <= SniffFaceMinDist * SniffFaceMinDist
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
                        else EndPause(LeashDetour, "arrived");
                    }
                    if (leashMode == LeashDetour) return true;
                }
            }
            else if (leashMode == LeashWalk && handDist < leash * DetourTrigger && HolderMoving()
                && Time.time >= _nextDetourRoll)
            {
                _nextDetourRoll = Time.time + DetourRollInterval;
                float chance = hillsideLeashes.detourChance
                    * (DetourChanceBase + DetourChanceSpread * (dogIndex % DetourChanceGroups));
                _detourRolls++;
                if (_detourRolls % 10 == 0)
                    Debug.Log("[KH_Leash] dog" + dogIndex + " detour funnel rolls=" + _detourRolls
                        + " ok=" + _detourOk + " arrived=" + _detourArrived + " posed=" + _detourPosed);
                if (Rand01((int)Time.time, 4201) < chance && !SiblingPausedRecently()
                    && PickDetour(nodePos, handPos, leash, route))
                {
                    _detourOk++;
                    leashMode = LeashDetour; _detourEnteredAt = Time.time;
                    _modeUntil = Time.time + DetourModeCap; _sniffUntil = 0f; _sniffExtended = false;
                    _alignUntil = 0f; _pausePosed = false;
                    return true;
                }
            }

            // 6b. Stopover: a fixed spot beside the trail the dog likes to drop in on (IMP-DOG-010 B).
            //     Same manners as the detour - go, face what there is to look at, hold the pose long enough
            //     to be noticed, and lift its head by itself when the holder walks on. The destination of
            //     the walk never changes: this is a detour whose point was placed by hand.
            if (leashMode == LeashStopover)
            {
                KominkaHillside.KH_WalkStopovers spots = hillsideLeashes.stopovers;
                bool gone = spots == null || stopoverIndex < 0 || stopoverIndex >= spots.count;
                Vector3 spot = gone ? dogRoot.position : spots._Position(stopoverIndex);
                bool atSpot = !gone && (_stopoverUntil > 0f || Horizontal(dogRoot.position, spot) <= StopoverArrive);
                if (atSpot && _alignUntil <= 0f) { _alignUntil = Time.time + PauseAlignCap; _stopoverArrived++; }
                if (!_pausePosed && (_eatPose || _wantSit)) { _pausePosed = true; _stopoverPosed++; }
                bool leaving = handDist >= leash * DetourBreakLeash && HolderMoving() && handOpening;
                bool tooSlow = _alignUntil <= 0f && Time.time > _stopoverEnteredAt + StopoverReachCap;
                if (gone || tooSlow) EndPause(LeashStopover, "abort");
                else if (leaving) EndPause(LeashStopover, "cutoff");
                else if (Time.time > _stopoverModeUntil) EndPause(LeashStopover, "cap");
                else if (!atSpot) { LeashGo(spot, runSpeed, 0.1f); return true; }
                else
                {
                    Coast();
                    // 見る先は場所ごとに決まっている (古民家・水場など)。**向き切ってから**ポーズを立てる
                    Vector3 look = spots._LookTarget(stopoverIndex);
                    Vector3 want = look - dogRoot.position; want.y = 0f;
                    bool facing = Time.time >= _alignUntil
                        || want.sqrMagnitude <= SniffFaceMinDist * SniffFaceMinDist
                        || Vector3.Angle(dogRoot.forward, want) <= SniffFaceToleranceDeg;
                    if (!facing) { FaceTowards(look); return true; }
                    if (_speed > stopSpeedThreshold) return true;
                    if (_stopoverUntil <= 0f)
                        _stopoverUntil = Time.time + StopoverHoldMin
                            + Rand01((int)Time.time, 4281) * (StopoverHoldMax - StopoverHoldMin);
                    else if (Time.time >= _stopoverUntil)
                    {
                        // 人が足を止めて付き合ってくれたなら 1 回だけ延長する (寄り道と同じ)
                        if (!_stopoverExtended && !HolderMoving() && handDist <= leash * SniffExtendLeash)
                        { _stopoverExtended = true; _stopoverUntil = Time.time + SniffExtend; }
                        else EndPause(LeashStopover, "arrived");
                    }
                    if (leashMode == LeashStopover) return true;
                }
            }
            else if (leashMode == LeashWalk && HolderMoving() && TryStopover(nodePos, handPos, leash)) return true;

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
        // position, the static trail — so no synced variable is added. **The Owner uses the same
        // derivation**, not leashMode, so every screen runs the same rule on its own copy of the facts.
        // That makes the screens near-identical, **not identical**: each client sees the dog's position
        // and the holder's at its own moment, so a dog on a threshold can hold the pose a little longer
        // on one screen than another, and a client that has not yet received a position update shows the
        // pose late. An approximate display was chosen over another synced variable (Codex D1); the
        // thresholds below carry hysteresis so the disagreement stays a delay and not a flicker.
        // Bool は一度に 1 つ (設計書 §5): 伏せ > 立ち寄り場所 (嗅ぐ / 座る) > 道端で嗅ぐ > 対面 の順で
        // 1 つだけ立てて抜ける。立ち寄り場所が道からの距離より強いのは、場所ごとに何をするかが決まって
        // いるから (座る場所で嗅がれると「眺めている」に見えない)。
        private void LeashPose(VRCPlayerApi holder, Vector3 toHand, float handDist, Vector3 handPos,
            KominkaHillside.KH_WalkRoute route, bool settling)
        {
            bool stopped = _speed <= stopSpeedThreshold;
            // **その場で回っている犬は「止まっている」ではない** (Codex 4)。足が止まっていても向きを
            // 変えている間に頭を下げさせると、回りながら嗅ぐ犬になる
            float yaw = dogRoot.eulerAngles.y;
            float dt = Time.deltaTime;
            float yawRate = dt > 0.0001f ? Mathf.Abs(Mathf.DeltaAngle(_prevYaw, yaw)) / dt : 0f;
            _prevYaw = yaw;
            // 「止まっている」ではなく「止まって何秒経ったか」で決める — 通過中のわずかな停止では立たない
            if (!stopped || yawRate > PoseYawRateMax) _stoppedSince = -1f;
            else if (_stoppedSince < 0f) _stoppedSince = Time.time;
            if (settling && stopped) { _poseTrailActive = false; _wantLieDown = true; return; }
            if (!stopped) { _poseTrailActive = false; return; }
            bool settled = _stoppedSince >= 0f && Time.time - _stoppedSince >= PoseStoppedFor;
            // 立ち寄り場所の上で止まっているなら、**その場所の種類がポーズを決める** (道からの距離より優先)。
            // モード (leashMode) は見ない — 見ると Owner だけが正しく、他の画面が立ち姿になる
            KominkaHillside.KH_WalkStopovers spots = hillsideLeashes.stopovers;
            if (spots != null && settled && handDist >= StopoverPoseHandMin)
            {
                for (int k = 0; k < spots.count; k++)
                {
                    if (Horizontal(dogRoot.position, spots._Position(k)) > StopoverPoseRadius) continue;
                    if (spots._Kind(k) == KominkaHillside.KH_WalkStopovers.KindSit) _wantSit = true;
                    else _eatPose = true;
                    _poseTrailActive = false;
                    return;
                }
            }
            // 嗅ぐ: 0.5 s 止まっていて、道の中心線から外れた所に居て、手からも離れていて、
            // その手はまだ道の上 (= 人は散歩を続けている)。Fetch / フリスビー中はここへ来ない (上で return 済み)。
            // **入るしきい値と保つしきい値を分ける** — 犬も人も少しずつ動くので、境目で姿勢が明滅する
            float trailNeed = _poseTrailActive ? PoseTrailHold : PoseTrailEnter;
            float handNeed = _poseTrailActive ? PoseHandHold : PoseHandMin;
            _poseTrailActive = route != null && settled
                && handDist >= handNeed
                && route._DistanceToTrail(dogRoot.position) >= trailNeed
                && route._DistanceToTrail(handPos) < PoseTrailHalfWidth;
            if (_poseTrailActive)
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
            // LookAt も Fetch が触らない Bool なので、こちらが下ろさないと対面ポーズのまま仕事へ行く
            // (Update の Fetch 枝では下ろしているのに、リードからの引き渡しでは抜けていた)
            if (_wasLooking && dogAnimator != null) dogAnimator.SetBool("LookAt", false);
            _look = false; _wasLooking = false;
            ReleaseAnimatorSpeed();
            ResetPauseAndPoseState();
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
            // 止まる位置は候補より最大 DetourArrive だけ道側へ寄る。**そこで姿勢のしきい値を割らない
            // ところまでしか候補にしない** — 1.6 の候補へ 0.2 詰めて止まると 1.4 になり、せっかく道端へ
            // 出たのに嗅ぐ姿勢が一度も出ない (Codex 1 の余裕をここで保証する)
            float floor = Mathf.Max(DetourTrailMin, PoseTrailEnter + DetourArrive);
            NavMeshHit hit, from;
            if (route._DistanceToTrail(cand) >= floor
                && Horizontal(cand, handPos) <= leash * DetourHandMax
                && NavMesh.SamplePosition(cand, out hit, 1f, NavMesh.AllAreas)
                && route._DistanceToTrail(hit.position) >= floor
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
            return ok;
        }

        /// <summary>Roll for the stopover the dog is about to walk past (IMP-DOG-010 B).
        ///
        /// **Once per pass per spot.** The dog is walking, so without the `_stopoverRolledFor` badge it
        /// would roll again every frame it spends beside the spot and always end up going. The badge falls
        /// off once the spot is behind it or 8 m away, which is what makes the next lap a new chance.
        /// The spot has to be ahead along the walk (never turn back), close to the dog, and close enough
        /// to the hand that going there does not pull the leash - with a 3 m leash that last one is the
        /// binding condition, so the roll happens as the holder draws level with the spot.</summary>
        private bool TryStopover(Vector3 nodePos, Vector3 handPos, float leash)
        {
            KominkaHillside.KH_WalkStopovers spots = hillsideLeashes.stopovers;
            if (spots == null || spots.count <= 0) return false;
            Vector3 ahead = nodePos - dogRoot.position; ahead.y = 0f;   // 進行方向 = 次の通過点の側
            if (ahead.sqrMagnitude < 0.0001f) return false;
            if (_stopoverRolledFor >= 0 && _stopoverRolledFor < spots.count)
            {
                Vector3 old = spots._Position(_stopoverRolledFor) - dogRoot.position; old.y = 0f;
                if (old.magnitude > StopoverForget || Vector3.Dot(old, ahead) <= 0f) _stopoverRolledFor = -1;
            }
            // 犬ごとのクールダウンと、同じ人の犬全体の静穏間隔 (寄り道と共通)
            if (Time.time - _lastStopoverAt < StopoverCooldown || SiblingPausedRecently()) return false;
            int best = -1; float bestDist = 1e6f;
            for (int k = 0; k < spots.count; k++)
            {
                Vector3 to = spots._Position(k) - dogRoot.position; to.y = 0f;
                float dist = to.magnitude;
                if (dist > StopoverRange || Vector3.Dot(to, ahead) <= 0f) continue;
                if (Horizontal(spots._Position(k), handPos) > leash * StopoverHandMax) continue;
                if (dist < bestDist) { bestDist = dist; best = k; }
            }
            if (best < 0 || best == _stopoverRolledFor || best == _lastStopoverIndex || SiblingAtStopover(best))
                return false;
            _stopoverRolledFor = best; // 外れてもこの通過では引き直さない
            _stopoverRolls++;
            if (_stopoverRolls % 10 == 0)
                Debug.Log("[KH_Leash] dog" + dogIndex + " stopover funnel rolls=" + _stopoverRolls
                    + " ok=" + _stopoverOk + " arrived=" + _stopoverArrived + " posed=" + _stopoverPosed);
            if (Rand01((int)Time.time, 4271) >= hillsideLeashes.stopoverChance) return false;
            _stopoverOk++;
            leashMode = LeashStopover; stopoverIndex = best;
            _stopoverEnteredAt = Time.time; _stopoverModeUntil = Time.time + StopoverModeCap;
            _stopoverUntil = 0f; _stopoverExtended = false;
            _alignUntil = 0f; _pausePosed = false;
            _leashCorners = null; _nextLeashPath = 0f;
            Debug.Log("[KH_Leash] dog" + dogIndex + " stopover k=" + best + " kind=" + spots._Kind(best));
            return true;
        }

        /// <summary>The single exit from a detour or a stopover, whatever ended it - the dog lifted its
        /// head, it never got there, the holder walked on, or rest / the return / the invitation took the
        /// dog away (those arrive here as "interrupted", detected one frame later at the top of the
        /// update). The pack-wide quiet interval and this dog's own cooldowns all start here, so there is
        /// exactly one place that can forget to start them. The reason is logged so a Play round trip can
        /// be counted and read from the console.</summary>
        private void EndPause(int mode, string reason)
        {
            bool stopover = mode == LeashStopover;
            Debug.Log("[KH_Leash] dog" + dogIndex + (stopover ? " stopover" : " detour") + " end reason=" + reason);
            if (stopover)
            {
                if (stopoverIndex >= 0) _lastStopoverIndex = stopoverIndex;
                _lastStopoverAt = Time.time;
            }
            stopoverIndex = -1;
            _sniffUntil = 0f; _sniffExtended = false;
            _stopoverUntil = 0f; _stopoverExtended = false;
            _alignUntil = 0f; _pausePosed = false;
            lastPauseEnd = Time.time;
            _nextDetourRoll = Time.time + DetourRollInterval;
            _pauseEndedThisFrame = true;
            // 割り込みで抜けた場合、モードはもう別のものに書き換わっている。そこへ戻さない
            if (leashMode == mode) leashMode = LeashWalk;
        }

        // One dog per spot among the same holder's dogs. Like SiblingInviting this reads a value the other
        // dog's Owner decided, so it is exact on the client that owns both and advisory otherwise; the cost
        // of being wrong is two dogs at one spot, not a broken walk.
        private bool SiblingAtStopover(int spot)
        {
            if (pack == null || pack.dogWander == null) return false;
            for (int i = 0; i < pack.dogWander.Length; i++)
            {
                if (i == dogIndex || pack.dogWander[i] == null) continue;
                if (hillsideLeashes.walkers[i] != hillsideLeashes.walkers[dogIndex]) continue;
                if (pack.dogWander[i].stopoverIndex == spot) return true;
            }
            return false;
        }

        // Codex X4: one holder's dogs stopping one after another is not a walk any more. The quiet interval
        // covers this dog too, so it is also the per-dog cooldown - and a sibling that is stopped **right
        // now** counts as well, not only one that finished recently (a 12 s sniff would otherwise let the
        // next dog start its own before the first has moved).
        private bool SiblingPausedRecently()
        {
            if (pack == null || pack.dogWander == null) return false;
            for (int i = 0; i < pack.dogWander.Length; i++)
            {
                KD_DogWanderController other = pack.dogWander[i];
                if (other == null) continue;
                if (hillsideLeashes.walkers[i] != hillsideLeashes.walkers[dogIndex]) continue;
                if (Time.time - other.lastPauseEnd < DetourQuiet) return true;
                if (i == dogIndex) continue;
                if (other.leashMode == LeashDetour || other.leashMode == LeashStopover) return true;
            }
            return false;
        }

        /// <summary>Drops the two things a new Owner (or a freshly attached leash) cannot inherit: the
        /// **pause in progress** - detour or stopover, with its target, timers and badges - and the
        /// **pose observation history** (how long the dog has been standing still). It deliberately does
        /// **not** touch Invite, Rest or Return: those rebuild themselves from the holder's stillness clock
        /// and the leash length, which every client measures for itself.</summary>
        private void ResetPauseAndPoseState()
        {
            if (leashMode == LeashDetour || leashMode == LeashStopover) EndPause(leashMode, "interrupted");
            _sniffUntil = 0f; _sniffExtended = false; _modeUntil = 0f;
            _detourPoint = dogRoot.position; _detourEnteredAt = Time.time;
            // 姿勢の観測履歴 (止まっていた時間・向き・今出している姿勢) もここで捨てる
            _stoppedSince = -1f; _handDistPrev = 0f; _poseTrailActive = false;
            _prevYaw = dogRoot.eulerAngles.y;
            _nextDetourRoll = Time.time + DetourRollInterval;
            // 立ち寄りも同じ: 進行中の滞在は引き継がない。履歴 (_lastStopoverAt / _lastStopoverIndex) は
            // 残す — 引き継いだ側がその場でもう一度同じ場所へ入るのを防ぐ
            stopoverIndex = -1; _stopoverRolledFor = -1;
            _stopoverUntil = 0f; _stopoverExtended = false;
            _stopoverEnteredAt = Time.time; _stopoverModeUntil = 0f;
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
