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
        // DES-DOG-005 D-4 (M3): the disc's water states 11-13 move the dog through this controller's water legs (HillsideWater.cs)
        private bool FrisbeeWaterBusy { get { return frisbeeCatch != null && frisbeeCatch.IsWaterBusy; } }
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
        private int _routeChoice = -1; // loop directions can share the same destination anchor
        private int _routeResume = -1; // progression hint after returning to the holder
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
        private bool _poseSpotActive;       // 立ち寄り場所のポーズを今出しているか
        private int _poseSpotIndex = -1;    // その場所 (保持のしきい値はこの場所で測る)
        private float _alignUntil;          // 着いたあと向きを整える締切 (0 = まだ着いていない)
        private int _pauseMode = -1;        // 今の滞在の種類 (LeashDetour / LeashStopover)
        private bool _pauseEndedThisFrame;  // このフレーム EndPause を通ったか (割り込み検出の二重発火よけ)
        /// <summary>今この犬が足を止めている最中か (寄り道・立ち寄りのどちらでも)。**同じ人の犬どうしが
        /// 読む** — `leashMode` は割り込みで即座に別の値へ書き換わるので、それを見ていると
        /// 「もう歩いている」と誤読して次の犬が続けて止まる。この旗は <see cref="EndPause"/> でしか
        /// 下りないので、割り込みの後始末が済むまで立ったままになる。</summary>
        [HideInInspector] public bool pausing;
        // 足を止める一連の流れをどこまで通れたか。**見せ方の設計を数字で確かめるための計数** (Codex U)
        private int _detourRolls, _detourOk, _detourArrived, _detourPoseReq;
        private int _stopoverRolls, _stopoverOk, _stopoverArrived, _stopoverPoseReq;
        private bool _pausePosed;           // この滞在で一度ポーズを要求したか (立ち上がりだけ数える)
        private float _pauseHeld;           // その滞在でポーズを出していた合計時間 (s)
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
        // **姿勢のしきい値より内側で止まらせない**。止まる位置は候補より最大 DetourArrive だけ道側へ
        // 寄るので、候補の下限は 1.6 ではなく `PoseTrailEnter + DetourArrive` = 1.7 を使う
        // (TryDetour の floor。1.6 のまま 0.2 詰めると 1.4 になり、姿勢の判定 1.5 を割る)
        const float DetourArrive = 0.2f;        // ここまで近づいたら着いたとみなす (通過点の 0.35 とは別)
        const float DetourTrigger = 0.95f;      // 発生を許すリード長の倍率。**先行保持の帯 (0.78〜0.90×)
                                                // より外に取る** — ここを 0.75 にすると、ずっと先行している
                                                // 犬は一度も抽選されない (Codex 2)。行き先は手から 0.8× 以内
        const float DetourRollInterval = 4f;    // 抽選の間隔 (s)
        const float DetourQuiet = 12f;          // 同じ人の犬すべてに掛かる静穏間隔 (s)
        const float DetourModeCap = 12f;        // 寄り道 1 回の上限 (s)
        const float DetourReachCap = 6f;        // ここまでに着けなければ諦める (s)
        // ---- 寄り道と立ち寄りで共通の値 (Pause*)。切り上げの倍率だけは場面ごとに違う ----
        const float PauseBreakLeash = 0.8f;     // 寄り道で人が離れていくとき切り上げるリード長の倍率
        const float PauseExtend = 5f;           // 付き合ってくれたときの延長 (1 回だけ)
        const float PauseExtendLeash = 0.95f;   // 延長を許すリード長の倍率
        const float SniffMin = 3f, SniffMax = 5f;      // 嗅ぐ時間 (s)。人が気づいて足を止められる長さ
        const float SniffFaceToleranceDeg = 15f;       // 向き切ってから頭を下げる (Rest の飲む枝より厳しめ)
        const float SniffFaceMinDist = 0.25f;          // これより近い行き先は「もう向いている」とみなす
        const float PauseAlignCap = 3f;                // 向きを整えるのに使ってよい時間 (s)
        const float PauseAlignRate = 90f;              // 整えている間の旋回速度。その場で見る 30 deg/s では間に合わない
        const float PauseAlignGiveUpDeg = 45f;         // 締切が来てもこれだけずれていたら、眺める場所は諦める
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
        private int _stopoverPending = -1;      // 当たったが、まだ手が近づいていない場所
        private float _stopoverPendingUntil;    // その持ち越しの期限
        /// <summary>今この犬が滞在している立ち寄り場所 (-1 = 居ない)。**同じ人の犬どうしが読む** —
        /// 1 つの場所に二匹入らないため。`leashMode` と同じく **その犬の Owner の画面でだけ意味を持つ**。</summary>
        [HideInInspector] public int stopoverIndex = -1;

        // ---- IMP-DOG-010 の仮値 (立ち寄り。場所そのものは KH_WalkStopovers / Walking 4d) ----
        // **抽選も忘却も「手」を基準にする** (Codex D)。犬は先行しているので犬から測ると
        // 「犬の前に在る」と「手から 0.8x 以内」が両立しない — 2.4 m 先行した犬の前方の場所は
        // 手からも遠く、いつまでも抽選されない
        const float StopoverRange = 6f;         // 手からこの距離に入ったら抽選する
        const float StopoverForget = 8f;        // 手からここまで離れたら「通り過ぎた」= 次の通過でまた引ける
        const float StopoverBehind = 1f;        // 手より道沿いにこれだけ後ろへ下がったら、もう通り過ぎている (m)
        const float StopoverPendingCap = 8f;    // 当たりを持ち越せる時間 (s)。人が立ち止まっても腐らせる
        // 入場と切り上げは寄り道より緩い (1.0x)。**歩いている人は 4〜8 s も待ってくれない** —
        // 歩き続ける人には「一瞬の反応」が、足を止めてくれた人には 4〜8 s の全部が見える。
        // Play のチェックリストはこの 2 つを別々に評価する
        const float StopoverHandMax = 1f;       // 入場を許す、手からのリード長の倍率
        const float StopoverBreakLeash = 1f;    // 切り上げの倍率 (人が歩いていて離れていく向きのときだけ)
        const float StopoverArrive = 0.2f;      // 着いたとみなす距離 (寄り道と同じ — 場所の上で止まらせる)
        const float StopoverReachCap = 6f;      // ここまでに着けなければ諦める (s)
        const float StopoverModeCap = 14f;      // 立ち寄り 1 回の上限 (s)
        const float StopoverCooldown = 45f;     // 同じ犬が次に立ち寄れるまで (s)
        const float StopoverHoldMin = 4f, StopoverHoldMax = 8f; // ポーズを保つ時間 (s)。寄り道より長い
        const float StopoverPoseRadius = 0.7f;  // 姿勢の導出: 場所からこの距離で止まっていたら入る
        const float StopoverPoseHold = 0.9f;    // 一度出た姿勢はここまでは保つ
        const float StopoverPoseHandMin = 1f;   // 姿勢の導出: 手からこれだけ離れていたら入る
        const float StopoverPoseHandHold = 0.8f; // 一度出た姿勢はここまでは保つ

        public void _InvalidateLeashAnimation() { _leashAnimationDirty = true; }
        private bool _leashAnimationDirty;

        // Sound only. This UdonBehaviour is Sync None, so it can neither send nor receive network events
        // ("Unable to send network event ... SyncType 'None'", found by the IMP-WLD-001 travel harness T3 while the
        // holder stood still). The dog Owner relays through KH_LeashManager (Manual), which checks that the sender
        // owns this dog's root and then calls this local method on every client.
        public void _InviteBark() { Bark(); }

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
            if (pausing && leashMode != _pauseMode && !_pauseEndedThisFrame) EndPause("interrupted");
            _pauseEndedThisFrame = false;
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
                { _holderTracked = holderId; _routeNext = -1; _routeChoice = -1; _routeResume = -1; ResetHolderSpeed(); ResetPauseAndPoseState(); }
                TrackHolderSpeed(holderPos);
            }
            // **手放した側も畳む** (Codex D)。ローカルの滞在状態が残ったままだと、この画面では
            // その犬がいつまでも「立ち寄り中」に見え、同じ人の他の犬が場所を取れなくなる
            else if (_wasDogOwner) ResetPauseAndPoseState();
            _wasDogOwner = _dogOwner;
            Vector3 toHand = handPos - dogRoot.position; toHand.y = 0f;
            float handDist = toHand.magnitude;
            // Is the hand pulling away from the dog this frame? (the detour reads it to know when to look up)
            bool handOpening = handDist > _handDistPrev;
            _handDistPrev = handDist;
            float leash = hillsideLeashes.leashLength;
            int destNode = hillsideLeashes._DestinationNode(dogIndex);
            KominkaHillside.KH_WalkRoute route = hillsideLeashes.route;
            int routeChoice = hillsideLeashes.destinations[dogIndex];
            bool loopWalk = route != null && route._LoopDirection(routeChoice) != 0;
            if (destNode != _routeDest || routeChoice != _routeChoice)
            {
                // A new destination is a fresh start: leave rest/invite state and the stillness clock behind.
                _routeDest = destNode; _routeChoice = routeChoice; _routeNext = -1; _routeResume = -1; _arrived = false;
                _holderMovedAt = Time.time; _holderSample = holderPos; _inviteCount = 0; _nextInviteAt = 0f;
                if (leashMode == LeashRest || leashMode == LeashInvite) leashMode = LeashWalk;
                // 引き返すと「これから通る場所」も入れ替わる。前の向きで当てた立ち寄りは捨てる
                _stopoverPending = -1; _stopoverRolledFor = -1;
            }
            if (destNode >= 0 && !loopWalk)
            {
                float destDist = Horizontal(dogRoot.position, route._Position(destNode));
                if (destDist < route._Radius(destNode)) _arrived = true;
                else if (Horizontal(holderPos, route._Position(destNode)) > route._Radius(destNode) + 6f) _arrived = false;
            }
            else _arrived = false;

            SampleHolder(holderPos, loopWalk ? -1 : destNode, route);
            float stillFor = Time.time - _holderMovedAt;
            float restAfter = (_arrived ? 20f : hillsideLeashes.inviteAfter + hillsideLeashes.inviteInterval * hillsideLeashes.inviteLimit) + dogIndex * 2f;
            bool settling = stillFor > restAfter && handDist < 2.2f;
            // IMP-DOG-027: the leashed wash stop (HillsideWash.cs) is decided here, on every client and before LeashPose and the
            // non-Owner return, so every screen picks the same pose (head to the washer, no LieDown / sniff). false whenever
            // wash is null or not held, and then LeashPose and everything below run exactly as before.
            // IMP-DOG-035: the holder called their own leashed dog to a wash station: the station branch moves and holds it (every
            // client, before LeashPose and the non-Owner return), the leash follow stops until the release. False when not held
            // (or held for someone else, which the station never keeps), and then everything below runs as before.
            if (UpdateWashStation(true)) return true;
            bool washStop = WashLeashStop(holder, handDist, leash);
            LeashPose(holder, toHand, handDist, handPos, route, settling, washStop);
            if (!_dogOwner) { ApplyMoveAnim(); return true; }
            // map-and-travel §F.4: the leash owns the root from here (leashed, no water veto - the caller checks it -, Fetch /
            // disc not holding the dog, Owner), so an unfinished swim-height ease must not outlive it. Flags only, no height write.
            WaterClearFloat();
            if (UpdateHillsideTravel(holderPos, handDist, leash)) return true; // IMP-WLD-001: travel ticket first, before Return (HillsideTravel.cs)

            // The stopover badge means "standing at that spot right now" and nothing else. Rest, the
            // return and the off-trail branches below all take the dog away without asking, so the badge
            // is dropped here rather than at each of them - otherwise a sibling waits for a spot forever.
            if (leashMode != LeashStopover && stopoverIndex >= 0) stopoverIndex = -1;
            // The "which spot is this pass about" bookkeeping has to run on every Owner frame, not only
            // when the dog happens to reach the roll: the branches above return early, and a badge that
            // is never cleared means the spot is never offered again (Codex D).
            UpdateStopoverPass(handPos, route);

            // 1. Stretched too far: come back to the hand (run), teleport only after a real separation.
            if (leashMode == LeashReturn)
            {
                // Return also crosses nodes. Record them before choosing a fresh forward entry,
                // particularly at the shared gate stem where position alone cannot identify a lap.
                if (loopWalk && _routeNext >= 0 && Horizontal(dogRoot.position, route._Position(_routeNext)) < route._Radius(_routeNext))
                    _routeNext = route._Advance(_routeNext, routeChoice);
                if (handDist < leash * 0.6f)
                {
                    leashMode = LeashWalk;
                    // A second return while awaiting reentry must retain the last known progress.
                    if (!loopWalk) _routeResume = -1;
                    else if (_routeNext >= 0) _routeResume = _routeNext;
                    _routeNext = -1;
                }
            }
            else if (handDist > leash * 1.25f && TravelPending() && _travelAppliedReq != hillsideLeashes.travelReq[dogIndex])
            {
                // IMP-WLD-001 RC-01: 本人は TeleportTo したが Phase 4 (移動済み) がまだ届いていない数フレーム。ここで Return に入ると
                // 588 m 先の飼い主へ走り出す (T1 の Return 0 件で検出)。切符の期限 (15 s) までその場で待ち、切れたら通常の Return で追いつく。適用済み (完了の再送中) なら通常どおり追う
                Coast();
                return true;
            }
            else if (handDist > leash * 1.25f)
            {
                if (pausing) EndPause("cutoff"); // 滞在の記録は残して帰る
                leashMode = LeashReturn;
                // PCVR のチェック用 (IMP-DOG-009)。走っている人の後ろで戻りが周期的に出るなら、
                // 先行を止めている側 (lead-hold の帯) が狭すぎる
                Debug.Log("[KH_Leash] dog" + dogIndex + " Return handDist=" + handDist.ToString("F1")
                    + " holderSpeed=" + _holderSpeed.ToString("F1"));
            }
            if (leashMode == LeashReturn)
            {
                Vector3 back = handPos - toHand / Mathf.Max(handDist, 0.001f) * 0.9f;
                if (handDist > hillsideLeashes.catchUpDistance && Time.time >= _nextTeleportTry && !TravelPending()) // IMP-WLD-001: a pending ticket owns the move
                {
                    _nextTeleportTry = Time.time + 1f;
                    if (TeleportTo(back)) return true;
                }
                LeashGo(back, handDist > leash * 1.6f ? hillsideLeashes.sprintSpeed : hillsideLeashes.dashSpeed, 0.2f);
                return true;
            }

            // 1b. IMP-DOG-027: the holder washes the dog (their own stroke, standing, slack leash): stand still until they walk
            //     on. After the return (a stretched leash comes back first) and the travel ticket above, before rest and the rest.
            if (washStop) { WashLeashHold(); return true; }

            // 2. Holder has stood still for long (or all invitations are spent): settle down beside them until they move.
            if (stillFor > restAfter || (_inviteCount >= hillsideLeashes.inviteLimit && stillFor > 1f))
            {
                leashMode = LeashRest;
                if (handDist > 1.8f) LeashGo(handPos - toHand / handDist * 1.0f, walkSpeed, 0.2f); else Coast();
                return true;
            }

            // 3. No destination, holder off the trail, or no reachable entry: keep them company around the hand.
            if (destNode < 0 || _holderOffTrail) { _routeNext = -1; _routeResume = -1; FollowHand(handDist, leash, LeashFree); return true; }
            if (_arrived) { _routeNext = -1; FollowHand(handDist, leash, LeashArrived); return true; }
            if (_routeNext < 0 && Time.time >= _nextRouteEntry)
            {
                _routeNext = ChooseEntry(destNode, route);
                if (_routeNext < 0) _nextRouteEntry = Time.time + 3f; // nothing reachable: retry later, stay with the holder
            }
            if (_routeNext < 0) { FollowHand(handDist, leash, LeashFree); return true; }

            // 4. Advance along the routing table; arrival at the destination node ends the trip.
            Vector3 nodePos = route._Position(_routeNext);
            if (Horizontal(dogRoot.position, nodePos) < route._Radius(_routeNext) && (loopWalk || _routeNext != destNode))
            {
                _routeNext = route._Advance(_routeNext, routeChoice);
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
                        hillsideLeashes._RelayInviteBark(dogIndex); // via the Manual manager (this behaviour is Sync None)
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
                if (_eatPose) { _pauseHeld += Dt(); if (!_pausePosed) { _pausePosed = true; _detourPoseReq++; } }
                // 切り上げに固定の待ち時間は置かない — ポーズは足が動いた時点で ApplyMoveAnim が下ろす (Codex X2/D1)
                bool leaving = PauseShouldBreak(handDist, leash, PauseBreakLeash, handOpening);
                bool tooSlow = _alignUntil <= 0f && Time.time > _detourEnteredAt + DetourReachCap;
                if (leaving) EndPause("cutoff");
                else if (tooSlow) EndPause("abort");
                else if (Time.time > _modeUntil) EndPause("cap");
                else if (!atSpot) { LeashGo(_detourPoint, walkSpeed, 0.1f); return true; }
                else
                {
                    Coast();
                    // **向き切ってから頭を下げる** (Rest の飲む枝と同じ作法)。着いた瞬間に立てると
                    // 嗅ぐ先を横目に見たまま止まる。ただし鼻先の数 cm のために回らない —
                    // 行き過ぎた犬がその場で回り直しても意味が無い
                    Vector3 want = _detourPoint - dogRoot.position; want.y = 0f;
                    bool facing = Time.time >= _alignUntil
                        || want.sqrMagnitude <= SniffFaceMinDist * SniffFaceMinDist
                        || Vector3.Angle(dogRoot.forward, want) <= SniffFaceToleranceDeg;
                    if (!facing) { FaceTowardsRate(_detourPoint, PauseAlignRate); return true; }
                    if (_speed > stopSpeedThreshold) return true;   // 止まり切ってから数え始める
                    if (_sniffUntil <= 0f)
                        _sniffUntil = Time.time + SniffMin + Rand01((int)Time.time, 4211) * (SniffMax - SniffMin);
                    else if (Time.time >= _sniffUntil)
                    {
                        if (PauseCanExtend(handDist, leash, _sniffExtended))
                        { _sniffExtended = true; _sniffUntil = Time.time + PauseExtend; }
                        else EndPause("arrived");
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
                if (_detourRolls % 10 == 0) LogDetourFunnel();
                if (Rand01((int)Time.time, 4201) < chance && !SiblingPausedRecently()
                    && PickDetour(nodePos, handPos, leash, route))
                {
                    _detourOk++;
                    BeginPause(LeashDetour);
                    _detourEnteredAt = Time.time;
                    _modeUntil = Time.time + DetourModeCap; _sniffUntil = 0f; _sniffExtended = false;
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
                if (_eatPose || _wantSit)
                { _pauseHeld += Dt(); if (!_pausePosed) { _pausePosed = true; _stopoverPoseReq++; } }
                // 入場も切り上げも寄り道より緩い倍率で測る (歩いている人に付いていける幅)
                bool leaving = PauseShouldBreak(handDist, leash, StopoverBreakLeash, handOpening);
                bool tooSlow = _alignUntil <= 0f && Time.time > _stopoverEnteredAt + StopoverReachCap;
                if (gone || tooSlow) EndPause("abort");
                else if (leaving) EndPause("cutoff");
                else if (Time.time > _stopoverModeUntil) EndPause("cap");
                else if (!atSpot) { LeashGo(spot, runSpeed, 0.1f); return true; }
                else
                {
                    Coast();
                    // 見る先は場所ごとに決まっている (古民家・水場など)。**向き切ってから**ポーズを立てる
                    Vector3 look = spots._LookTarget(stopoverIndex);
                    Vector3 want = look - dogRoot.position; want.y = 0f;
                    float off = want.sqrMagnitude <= SniffFaceMinDist * SniffFaceMinDist
                        ? 0f : Vector3.Angle(dogRoot.forward, want);
                    bool expired = Time.time >= _alignUntil;
                    // **締切切れは「向けた」ことにしない** (Codex U)。眺める場所で明後日を向いて座ると
                    // 「何を見ているのか」が読めないので、そのときは座らずに切り上げる。
                    // 嗅ぐ場所は鼻先が地面なので、多少ずれていても成立する
                    if (expired && off > PauseAlignGiveUpDeg
                        && spots._Kind(stopoverIndex) == KominkaHillside.KH_WalkStopovers.KindSit)
                    { EndPause("align"); }
                    else if (!(expired || off <= SniffFaceToleranceDeg))
                    { FaceTowardsRate(look, PauseAlignRate); return true; }
                    else if (_speed > stopSpeedThreshold) return true;
                    else if (_stopoverUntil <= 0f)
                        _stopoverUntil = Time.time + StopoverHoldMin
                            + Rand01((int)Time.time, 4281) * (StopoverHoldMax - StopoverHoldMin);
                    else if (Time.time >= _stopoverUntil)
                    {
                        if (PauseCanExtend(handDist, leash, _stopoverExtended))
                        { _stopoverExtended = true; _stopoverUntil = Time.time + PauseExtend; }
                        else EndPause("arrived");
                    }
                    if (leashMode == LeashStopover) return true;
                }
            }
            else if (leashMode == LeashWalk && TryStopover(handPos, leash, route)) return true;

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
            KominkaHillside.KH_WalkRoute route, bool settling, bool washStop)
        {
            bool stopped = _speed <= stopSpeedThreshold;
            // **その場で回っている犬は「止まっている」ではない** (Codex 4)。足が止まっていても向きを
            // 変えている間に頭を下げさせると、回りながら嗅ぐ犬になる
            float yaw = dogRoot.eulerAngles.y;
            float dt = Dt();
            float yawRate = dt > 0.0001f ? Mathf.Abs(Mathf.DeltaAngle(_prevYaw, yaw)) / dt : 0f;
            _prevYaw = yaw;
            // 「止まっている」ではなく「止まって何秒経ったか」で決める — 通過中のわずかな停止では立たない
            if (!stopped || yawRate > PoseYawRateMax) _stoppedSince = -1f;
            else if (_stoppedSince < 0f) _stoppedSince = Time.time;
            // IMP-DOG-027: washed on the leash - stronger than settling (the holder standing to wash is not the holder resting).
            // The Owner's decision already includes "the holder is standing"; the others follow the observed stop.
            if (washStop && (_dogOwner || stopped)) { ClearPoseLatches(); WashLeashPose(); return; }
            if (settling && stopped) { ClearPoseLatches(); _wantLieDown = true; return; }
            if (!stopped) { ClearPoseLatches(); return; }
            bool settled = _stoppedSince >= 0f && Time.time - _stoppedSince >= PoseStoppedFor;
            // 立ち寄り場所の上で止まっているなら、**その場所の種類がポーズを決める** (道からの距離より優先)。
            // モード (leashMode) は見ない — 見ると Owner だけが正しく、他の画面が立ち姿になる
            KominkaHillside.KH_WalkStopovers spots = hillsideLeashes.stopovers;
            if (spots != null && settled && handDist >= (_poseSpotActive ? StopoverPoseHandHold : StopoverPoseHandMin))
            {
                // 一度その場所のポーズに入ったら、少し広い半径まではその場所で測り続ける
                int found = -1;
                if (_poseSpotActive && _poseSpotIndex >= 0 && _poseSpotIndex < spots.count
                    && Horizontal(dogRoot.position, spots._Position(_poseSpotIndex)) <= StopoverPoseHold)
                    found = _poseSpotIndex;
                else
                    for (int k = 0; k < spots.count; k++)
                        if (Horizontal(dogRoot.position, spots._Position(k)) <= StopoverPoseRadius) { found = k; break; }
                if (found >= 0)
                {
                    if (spots._Kind(found) == KominkaHillside.KH_WalkStopovers.KindSit) _wantSit = true;
                    else _eatPose = true;
                    _poseSpotActive = true; _poseSpotIndex = found;
                    _poseTrailActive = false;
                    return;
                }
            }
            _poseSpotActive = false; _poseSpotIndex = -1;
            // 嗅ぐ: 0.5 s 止まっていて、道の中心線から外れた所に居て、手からも離れていて、
            // その手はまだ道の上 (= 人は散歩を続けている)。Fetch / フリスビー中はここへ来ない (上で return 済み)。
            // **入るしきい値と保つしきい値を分ける** — 犬も人も少しずつ動くので、境目で姿勢が明滅する
            float trailNeed = _poseTrailActive ? PoseTrailHold : PoseTrailEnter;
            float handNeed = _poseTrailActive ? PoseHandHold : PoseHandMin;
            _poseTrailActive = route != null && settled
                && handDist >= handNeed
                && PoseTrailDogAtLeast(route, dogRoot.position, trailNeed)       // IMP-DOG-034 A3: 結果は走査と同じ (下のキャッシュ)
                && PoseTrailHandBelow(route, handPos, PoseTrailHalfWidth);
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

        // ---- IMP-DOG-034 A3: 嗅ぐ姿勢の判定に使う「道の中心線からの距離」のキャッシュ ----
        // _DistanceToTrail はリンクの走査 (≈ 650 extern) で、止まっているリード犬では毎フレーム 2 本 (犬と手) 引いていた。
        // 道は動かず、距離は水平距離なので、測った点から水平に m 動いた点の真の距離は [d - m, d + m] に入る。
        // **しきい値との大小がこの区間で決まるときだけキャッシュを使い、決まらないときは引き直す** — 比較の結果は毎フレーム
        // 走査した場合と同じ (ヒステリシスの状態が分かれない。Codex 指摘 2026-10-01: 古い距離をそのまま使うと境目を
        // 跨いで戻る動きを取りこぼし、姿勢が残り続ける)。リンクが見つからない 1e6 は距離ではないので、その間は
        // 0.25 m 動いたら引き直す。ResetPauseAndPoseState が無効化する
        const float PoseTrailCacheMove = 0.25f;
        private Vector3 _poseTrailDogAt, _poseTrailHandAt;
        private float _poseTrailDogDist, _poseTrailHandDist;
        private float _poseTrailDogTime = -1000f, _poseTrailHandTime = -1000f;   // < 0: 無効 (未計測)

        private bool PoseTrailDogAtLeast(KominkaHillside.KH_WalkRoute route, Vector3 p, float need)
        {
            if (_poseTrailDogTime >= 0f)
            {
                Vector3 moved = p - _poseTrailDogAt; moved.y = 0f;
                float m = moved.magnitude;
                float d = _poseTrailDogDist;
                if (d < 1e5f) { if (d - m >= need) return true; if (d + m < need) return false; }
                else if (m <= PoseTrailCacheMove) return true;   // 道の外 (リンク無し) のまま
            }
            _poseTrailDogDist = route._DistanceToTrail(p); _poseTrailDogAt = p; _poseTrailDogTime = Time.time;
            return _poseTrailDogDist >= need;
        }

        private bool PoseTrailHandBelow(KominkaHillside.KH_WalkRoute route, Vector3 p, float limit)
        {
            if (_poseTrailHandTime >= 0f)
            {
                Vector3 moved = p - _poseTrailHandAt; moved.y = 0f;
                float m = moved.magnitude;
                float d = _poseTrailHandDist;
                if (d < 1e5f) { if (d + m < limit) return true; if (d - m >= limit) return false; }
                else if (m <= PoseTrailCacheMove) return false;
            }
            _poseTrailHandDist = route._DistanceToTrail(p); _poseTrailHandAt = p; _poseTrailHandTime = Time.time;
            return _poseTrailHandDist < limit;
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
            float dt = Dt();
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
            bool loopWalk = route._LoopDirection(_routeChoice) != 0;
            if (loopWalk)
            {
                int ahead = route._LoopReentry(dogRoot.position, _routeChoice, _routeResume);
                if (ahead >= 0 && NavMesh.CalculatePath(from.position, route._Position(ahead), NavMesh.AllAreas, _leashPath)
                    && _leashPath.status == NavMeshPathStatus.PathComplete && PathLength(_leashPath) <= hillsideLeashes.entryMaxPath)
                { _routeResume = -1; _leashCorners = null; _nextLeashPath = 0f; return ahead; }
                // Follow the hand and retry the forward entry; nearest-node fallback can select
                // a passed node on the shared gate stem. Initial attachment still uses fallback.
                if (_routeResume >= 0 && _routeResume < route.count) return -1;
                _routeResume = -1;
            }
            int best = -1, nearest = -1; float bestScore = 1e9f, nearestLen = 1e9f;
            int cell = route._EntryCell(dogRoot.position);
            int start = cell < 0 ? 0 : route.entryCellStarts[cell];
            int end = cell < 0 ? route.count : route.entryCellStarts[cell + 1];
            for (int index = start; index < end; index++)
            {
                int i = cell < 0 ? index : route.entryCellNodes[index];
                if (route._Cost(i, destNode) >= 1e5f) continue;
                Vector3 p = route._Position(i);
                if (Horizontal(dogRoot.position, p) > 30f) continue;
                NavMeshHit to;
                if (!NavMesh.SamplePosition(p, out to, 2f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _leashPath) || _leashPath.status != NavMeshPathStatus.PathComplete) continue;
                float len = PathLength(_leashPath);
                if (len < nearestLen) { nearestLen = len; nearest = i; }
                if (len > hillsideLeashes.entryMaxPath) continue;
                float score = len + (loopWalk ? 0f : route._Cost(i, destNode));
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
            if (route == null || !route._AllowsDetour(handPos)) return false;
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

        /// <summary>Keeps the "pass" bookkeeping for the stopovers, **every Owner frame** (IMP-DOG-010 B).
        ///
        /// Everything here is measured from the **hand**, not from the dog (Codex D): a dog leading by
        /// 2.4 m is never both "in front of the dog" and "within reach of the hand", so measuring from the
        /// dog meant a leading dog never rolled at all. The holder's hand walks past the spot exactly once
        /// per lap, which is the event this is really about.
        ///
        /// Two badges fall off here: `_stopoverRolledFor` (this pass has been rolled for, win or lose -
        /// it is what makes the next lap a fresh chance) and `_stopoverPending` (the roll was won but the
        /// hand is still too far for the dog to go without pulling the leash; it is cashed in as soon as
        /// the hand is close, and expires if that never happens).</summary>
        private void UpdateStopoverPass(Vector3 handPos, KominkaHillside.KH_WalkRoute route)
        {
            KominkaHillside.KH_WalkStopovers spots = hillsideLeashes.stopovers;
            if (spots == null || route == null) { _stopoverRolledFor = -1; _stopoverPending = -1; return; }
            if (_stopoverPending >= 0 && Time.time > _stopoverPendingUntil) _stopoverPending = -1;
            // 追うものが無ければ道の向きも要らない。**_TrailDirection はリンク全走査**なので、
            // 何も持っていない大半のフレームでは引かない
            if (_stopoverRolledFor < 0 && _stopoverPending < 0) return;
            Vector3 dir = PassDirection(route, handPos);
            if (_stopoverRolledFor >= 0 && PassedBy(spots, _stopoverRolledFor, handPos, dir)) _stopoverRolledFor = -1;
            if (_stopoverPending >= 0 && PassedBy(spots, _stopoverPending, handPos, dir)) _stopoverPending = -1;
        }

        /// <summary>The direction the walk is going, at the hand, signed toward the destination through
        /// the next trail node. **`Vector3.zero` when there is no next node** - off the trail there is no
        /// "ahead" to speak of, and guessing one from the dog's own position would point backwards as
        /// often as forwards. Callers fall back to distance only.</summary>
        private Vector3 PassDirection(KominkaHillside.KH_WalkRoute route, Vector3 handPos)
        {
            if (_routeNext < 0) return Vector3.zero;
            Vector3 dir = route._TrailDirection(handPos);
            Vector3 toNode = route._Position(_routeNext) - handPos; toNode.y = 0f;
            if (Vector3.Dot(dir, toNode) < 0f) dir = -dir;
            return dir;
        }

        // その場所を手がもう通り過ぎたか (手から 8 m より遠い、または道沿いに 1 m 以上後ろ)。
        // 向きが決まらないとき (dir = zero) は距離だけで見る
        private bool PassedBy(KominkaHillside.KH_WalkStopovers spots, int k, Vector3 handPos, Vector3 dir)
        {
            if (k >= spots.count) return true;
            Vector3 to = spots._Position(k) - handPos; to.y = 0f;
            if (to.magnitude > StopoverForget) return true;
            return dir.sqrMagnitude > 0.0001f && Vector3.Dot(to, dir) <= -StopoverBehind;
        }

        /// <summary>Roll for the stopover the holder is walking up to, and go as soon as it is in reach.
        /// One roll per pass (`_stopoverRolledFor`); a won roll waits in `_stopoverPending` until the hand
        /// is within `StopoverHandMax`, because that is when the dog can cross to the spot without the
        /// leash going taut.</summary>
        private bool TryStopover(Vector3 handPos, float leash, KominkaHillside.KH_WalkRoute route)
        {
            KominkaHillside.KH_WalkStopovers spots = hillsideLeashes.stopovers;
            if (spots == null || spots.count <= 0 || route == null) return false;
            // IMP-DOG-034 A1: PassDirection (_TrailDirection = リンク全走査 ≈ 650 extern) は、向きを実際に使う所でだけ引く。
            // 以前は先頭で毎フレーム引いてから HolderMoving / クールダウンで捨てていた。PassDirection は route を読むだけの
            // 純関数 (書き込み無し) なので、引く位置を後ろへ移しても戻り値も副作用も変わらない
            // 当たりを持っているなら、あとは手が近づくのを待つだけ。**ここは人が歩いているかを問わない**
            // (Codex D) — 立ち止まって手を伸ばしてくれた人にこそ、犬は寄って行ってよい
            if (_stopoverPending >= 0)
            {
                Vector3 pendingSpot = spots._Position(_stopoverPending);
                if (Horizontal(pendingSpot, handPos) > leash * StopoverHandMax) return false;
                // 待っている間に引き返していることがある。持ち越しを使う直前にもう一度前後を見る
                Vector3 pendingDir = PassDirection(route, handPos);
                Vector3 toPending = pendingSpot - handPos; toPending.y = 0f;
                if (pendingDir.sqrMagnitude > 0.0001f && Vector3.Dot(toPending, pendingDir) <= -StopoverBehind)
                { _stopoverPending = -1; return false; }
                if (SiblingAtStopover(_stopoverPending) || SiblingPausedRecently()) return false;
                return EnterStopover(spots, _stopoverPending);
            }
            // ここから下は新しい抽選。**歩いている人の前を通るときだけ引く**
            if (!HolderMoving()) return false;
            // 犬ごとのクールダウンと、同じ人の犬全体の静穏間隔 (寄り道と共通)
            if (Time.time - _lastStopoverAt < StopoverCooldown || SiblingPausedRecently()) return false;
            // IMP-DOG-034 A1: 手から StopoverRange 以内に場所が 1 つも無ければ、下の走査は向きに関係なく best = -1 で
            // false を返す (副作用無し)。同じ距離の判定を先に安く回し、道の大半ではリンク走査を引かない
            bool anyInRange = false;
            for (int k = 0; k < spots.count; k++)
            {
                Vector3 near = spots._Position(k) - handPos; near.y = 0f;
                if (!(near.magnitude > StopoverRange)) { anyInRange = true; break; }
            }
            if (!anyInRange) return false;
            Vector3 dir = PassDirection(route, handPos);   // IMP-DOG-034 A1: ここで初めて使う
            if (dir.sqrMagnitude < 0.0001f) return false;   // 前後が決まらないなら「これから通る」も決まらない
            int best = -1; float bestDist = 1e6f;
            for (int k = 0; k < spots.count; k++)
            {
                Vector3 to = spots._Position(k) - handPos; to.y = 0f;
                float dist = to.magnitude;
                // 手から 6 m 以内で、道沿いに 1 m 以上は後ろへ下がっていない = これから通る場所
                if (dist > StopoverRange || Vector3.Dot(to, dir) <= -StopoverBehind) continue;
                if (dist < bestDist) { bestDist = dist; best = k; }
            }
            if (best < 0 || best == _stopoverRolledFor || best == _lastStopoverIndex || SiblingAtStopover(best))
                return false;
            _stopoverRolledFor = best; // 外れてもこの通過では引き直さない
            _stopoverRolls++;
            if (_stopoverRolls % 10 == 0) LogStopoverFunnel();
            if (Rand01((int)Time.time, 4271) >= hillsideLeashes.stopoverChance) return false;
            if (Horizontal(spots._Position(best), handPos) > leash * StopoverHandMax)
            {
                // 当たったが手はまだ遠い。持ち越して、近づいた瞬間に入る
                _stopoverPending = best; _stopoverPendingUntil = Time.time + StopoverPendingCap;
                return false;
            }
            return EnterStopover(spots, best);
        }

        private bool EnterStopover(KominkaHillside.KH_WalkStopovers spots, int k)
        {
            _stopoverOk++; _stopoverPending = -1;
            BeginPause(LeashStopover);
            stopoverIndex = k;
            _stopoverEnteredAt = Time.time; _stopoverModeUntil = Time.time + StopoverModeCap;
            _stopoverUntil = 0f; _stopoverExtended = false;
            _leashCorners = null; _nextLeashPath = 0f;
            Debug.Log("[KH_Leash] dog" + dogIndex + " stopover k=" + k + " kind=" + spots._Kind(k));
            return true;
        }

        /// <summary>The single exit from a detour or a stopover, whatever ended it - the dog lifted its
        /// head, it never got there, the holder walked on, or rest / the return / the invitation took the
        /// dog away (those arrive here as "interrupted", detected one frame later at the top of the
        /// update). The pack-wide quiet interval and this dog's own cooldowns all start here, so there is
        /// exactly one place that can forget to start them. The reason is logged so a Play round trip can
        /// be counted and read from the console. Which pause it is comes from <see cref="_pauseMode"/>,
        /// not from `leashMode`, because by the time an override is noticed `leashMode` is already
        /// something else.</summary>
        private void EndPause(string reason)
        {
            int mode = _pauseMode;
            bool stopover = mode == LeashStopover;
            Debug.Log("[KH_Leash] dog" + dogIndex + (stopover ? " stopover" : " detour") + " end reason=" + reason
                + " held=" + _pauseHeld.ToString("F1") + "s");
            if (stopover)
            {
                if (stopoverIndex >= 0) _lastStopoverIndex = stopoverIndex;
                _lastStopoverAt = Time.time;
                LogStopoverFunnel();
            }
            else LogDetourFunnel();
            stopoverIndex = -1;
            _sniffUntil = 0f; _sniffExtended = false;
            _stopoverUntil = 0f; _stopoverExtended = false;
            _alignUntil = 0f; _pausePosed = false; _pauseHeld = 0f;
            lastPauseEnd = Time.time;
            _nextDetourRoll = Time.time + DetourRollInterval;
            _pauseEndedThisFrame = true;
            pausing = false; _pauseMode = -1;
            // 割り込みで抜けた場合、モードはもう別のものに書き換わっている。そこへ戻さない
            if (leashMode == mode) leashMode = LeashWalk;
        }

        private void LogDetourFunnel()
        {
            Debug.Log("[KH_Leash] dog" + dogIndex + " detour funnel rolls=" + _detourRolls
                + " ok=" + _detourOk + " arrived=" + _detourArrived + " poseReq=" + _detourPoseReq);
        }

        private void LogStopoverFunnel()
        {
            Debug.Log("[KH_Leash] dog" + dogIndex + " stopover funnel rolls=" + _stopoverRolls
                + " ok=" + _stopoverOk + " arrived=" + _stopoverArrived + " poseReq=" + _stopoverPoseReq);
        }

        // 切り上げ: 紐が伸びていて、人が歩いていて、さらに離れていく向きのとき。倍率だけ場面で違う
        private bool PauseShouldBreak(float handDist, float leash, float ratio, bool handOpening)
        {
            return handDist >= leash * ratio && HolderMoving() && handOpening;
        }

        // 延長: 人が足を止めて付き合ってくれたなら 1 回だけ。伸ばす先は呼び出し側が自分の時計へ書く
        private bool PauseCanExtend(float handDist, float leash, bool alreadyExtended)
        {
            return !alreadyExtended && !HolderMoving() && handDist <= leash * PauseExtendLeash;
        }

        // 滞在に入るときの共通の初期化 (どちらのモードも同じ形で始める)
        private void BeginPause(int mode)
        {
            leashMode = mode; _pauseMode = mode; pausing = true;
            _alignUntil = 0f; _pausePosed = false; _pauseHeld = 0f;
        }

        /// <summary>Is another of this holder's dogs standing at that spot? `stopoverIndex`, `pausing` and
        /// `lastPauseEnd` are **local** values that only the dog's own Owner writes, so a copy on a client
        /// that does not own that dog is whatever it happened to be when ownership moved - stale, and
        /// quite possibly a spot the dog left long ago. Only the owned ones are believed (Codex D); the
        /// cost of not knowing about a dog someone else owns is two dogs at one spot, not a broken walk.
        /// One client owns all six in the normal case (nobody calls SetOwner on a dog), so this is the
        /// exact answer in practice and a safe guess in the middle of a hand-over.</summary>
        // 姿勢のラッチ (今出している姿勢) を全部下ろす。保持のしきい値は「今出している」前提の値なので、
        // 出していない状態へ戻るときは必ずここを通す
        private void ClearPoseLatches()
        {
            _poseTrailActive = false; _poseSpotActive = false; _poseSpotIndex = -1;
        }

        private bool SiblingStateTrusted(KD_DogWanderController other)
        {
            // 単一引数の overload (= ローカルプレイヤー) を使う。IsDogOwner と同じ軸 (dogRoot) で測る
            return other.dogRoot != null && Networking.IsOwner(other.dogRoot.gameObject);
        }

        private bool SiblingAtStopover(int spot)
        {
            if (pack == null || pack.dogWander == null) return false;
            for (int i = 0; i < pack.dogWander.Length; i++)
            {
                KD_DogWanderController other = pack.dogWander[i];
                if (i == dogIndex || other == null) continue;
                if (hillsideLeashes.walkers[i] != hillsideLeashes.walkers[dogIndex]) continue;
                if (!SiblingStateTrusted(other)) continue;
                if (other.stopoverIndex == spot) return true;
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
                if (i != dogIndex && !SiblingStateTrusted(other)) continue;
                if (Time.time - other.lastPauseEnd < DetourQuiet) return true;
                if (i == dogIndex) continue;
                // **`leashMode` ではなく `pausing` を見る** — 割り込みで抜けた犬の leashMode は
                // その場で別の値になるが、後始末 (EndPause) はまだ済んでいないことがある
                if (other.pausing || other.stopoverIndex >= 0) return true;
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
            // `pausing` で見るので、**割り込みの 1 フレーム後** (leashMode はもう別、後始末はまだ) でも
            // ここで必ず静穏間隔が始まる
            if (pausing || leashMode == LeashDetour || leashMode == LeashStopover)
            {
                if (_pauseMode < 0) _pauseMode = leashMode;
                EndPause("interrupted");
            }
            _sniffUntil = 0f; _sniffExtended = false; _modeUntil = 0f;
            _detourPoint = dogRoot.position; _detourEnteredAt = Time.time;
            // 姿勢の観測履歴 (止まっていた時間・向き・今出している姿勢) もここで捨てる
            _stoppedSince = -1f; _handDistPrev = 0f; _poseTrailActive = false;
            _poseTrailDogTime = -1000f; _poseTrailHandTime = -1000f;   // IMP-DOG-034 A3: 新しい散歩・Owner は距離を測り直す
            _prevYaw = dogRoot.eulerAngles.y;
            _nextDetourRoll = Time.time + DetourRollInterval;
            // 立ち寄りも同じ: 進行中の滞在は引き継がない。履歴 (_lastStopoverAt / _lastStopoverIndex) は
            // **このクライアントのぶんだけ**残す — 直後に同じ場所へ入り直すのを防ぐためで、
            // 新しい Owner へ渡るものではない (向こうは自分の -1000 / -1 から始める)
            stopoverIndex = -1; _stopoverRolledFor = -1; _stopoverPending = -1;
            _stopoverUntil = 0f; _stopoverExtended = false;
            _stopoverEnteredAt = Time.time; _stopoverModeUntil = 0f;
            _poseSpotActive = false; _poseSpotIndex = -1;
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
            _navStep = true; // IMP-DOG-020: NavMesh の移動は川の帯の判定から外す (HillsideWater.cs)
            StepToward(dogRoot.position + delta / distance * clearance, _leashCorner == _leashCorners.Length - 1 ? stop : 0.05f, speed);
            _navStep = false;
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
            // IMP-DOG-020: 完全経路の角までは NavMesh の移動として帯の判定から外す。最後の区間は NavMesh へ投影した終点ではなく
            // 元の goal (枠) へ直進するので、帯の判定を掛ける (枠が帯の中なら岸で止まる。Codex 2026-09-27 D-高)
            _navStep = !last;
            StepToward(last ? goal : _leashCorners[_leashCorner], 0f, speed);
            _navStep = false;
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
