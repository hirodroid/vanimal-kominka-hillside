using UdonSharp;
using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    /// <summary>IMP-WLD-001 段階 1: 地点ワープと接続犬の同行 (設計書 docs/plans/2026-09-27-map-and-travel.md §B・§D.3〜D.6)。
    ///
    /// 三者の役割:
    /// - **要求する人 (ローカル)**: `_TravelBegin` で門番 → 着地点の検査 5 つ → 要求 ID 付きの切符を要求 → 確認できたら自分を TeleportTo。
    /// - **Manager Owner**: `ReqTravel` / `CancelTravel` / `ReqTravelDone` で切符 (同期配列 7 本) を書く唯一の人。
    /// - **犬の Owner**: KD_DogWanderController.HillsideTravel.cs が、飼い主が着地点に着いたのを見てから 1 回だけ適用し、完了を報告する。
    ///
    /// ログは `[KH_Leash] ReqTravel ...` / `[KH_Leash] travel ...` (BUG-DOG-004 と同じ書式)。</summary>
    public partial class KH_LeashManager
    {
        // ---- 地点 (Course/80 が周回路のノードから解決して書く。並びは固定) ----
        public const int SpotGate = 0, SpotPlaza = 1, SpotUpper = 2, SpotBridgeUp = 3, SpotRiver = 4, SpotBridgeDown = 5, SpotCount = 6;
        // ---- 切符の Phase ----
        public const int TravelPhaseNone = 0, TravelPhaseReady = 1, TravelPhaseDone = 2, TravelPhaseCanceled = 3;
        // ---- 要求の結果 (手元メニューが文言に変える)。0〜9 は押した瞬間、20〜 は待ちの後 ----
        public const int TravelStarted = 0, TravelBusy = 1, TravelCooling = 2, TravelBall = 3, TravelCrowded = 4,
            TravelUnavailable = 5, TravelAlreadyHere = 6, TravelArrived = 20, TravelChanged = 21, TravelNoAnswer = 22,
            TravelStayed = 23, TravelCanceledByMe = 24, TravelWaitingDogs = 25, TravelBlocked = 26;

        // ---- 切符 (長さ 6。宣言時に初期化 — 同期配列を未初期化にすると Behaviour 全体の同期が止まる) ----
        [UdonSynced] public int[] travelReq = new int[] {0,0,0,0,0,0};            // 要求 ID (playerId * 65536 + seq)。0 = 無し
        [UdonSynced] public int[] travelHolder = new int[] {-1,-1,-1,-1,-1,-1};   // 切符を出した人。適用の瞬間に walkers と照合する
        [UdonSynced] public Vector3[] travelCenter = new Vector3[6];               // 飼い主の着地点 L
        [UdonSynced] public Vector3[] travelPoint = new Vector3[6];                // その犬の配置点
        [UdonSynced] public float[] travelYaw = new float[6];                      // 着地したときの向き (度)
        [UdonSynced] public int[] travelAtMs = new int[6];                         // 準備した時刻 (GetServerTimeInMilliseconds)
        [UdonSynced] public int[] travelPhase = new int[] {0,0,0,0,0,0};          // 0 無し / 1 準備 / 2 完了 / 3 取消

        [Header("Travel spots (IMP-WLD-001). Course/80 が書く。手で変えない")]
        public string[] spotIds;
        public Vector3[] spotPositions;
        public float[] spotYaws;
        public bool[] spotWarpEnabled;   // 初期公開は upper と river だけ true (決定 2026-09-27)
        public int[] spotNodes;          // 解決したノード (代わりの点の「前後のノード」に使う)
        // 川の中心線 (KH_CourseGroundEditor.RiverAt を z 方向に riverStep m ごとに標本化) と橋の区域。共通の検査 4 に使う
        public float[] travelRiverXs;
        public float travelRiverZ0;
        public float travelRiverStep = 2f;
        public float[] travelBridgeZs;
        public float[] travelBridgeXs;

        [Header("Travel tuning (設計書 §D.5。Play で決める)")]
        public float travelCooldownOk = 10f;
        public float travelCooldownFail = 3f;
        public float travelResendAfter = 4f;
        public float travelGiveUpAfter = 8f;

        [Header("Travel harness hooks: シーンでは必ず 0 (Course/81 Verify とビルドの門番が止める)")]
        public int debugDropTravel;          // >0 の間、Owner は ReqTravel を捨てる (T2)
        public float debugDelayTravel;       // >0 なら次の要求 1 本の書き込みをこの秒数遅らせる (T3・T9)。1 回で 0 に戻る
        public int debugDropTravelDone;      // >0 なら完了報告をこの数だけ捨てる (T6)
        public int debugSkipTravelTeleport;  // >0 なら確認の後の TeleportTo をこの回数だけ飛ばす (T4・T5)
        public int debugForgeTravelMask;     // 送る dogMask にこのビットを足す (T8 の偽装)

        // ---- 要求する人 (ローカル。同期しない) ----
        private int _tvState;                // 0 なし / 1 確認待ち / 2 本人は着いた・犬を待っている (レビュー X6)
        private float _tvArrivedAt;
        const float TravelDogsWait = 15f;    // 犬の適用期限と同じ。過ぎたら待つのをやめて「着きました」
        private int _tvSeq, _tvReq, _tvMask, _tvSpot;
        private Vector3 _tvCenter;
        private float _tvYaw;
        private float _tvSentAt;
        private bool _tvResent;
        private float _tvCooldownUntil;
        private Vector3 _tvLanding;
        private int _tvBusyRejects;          // 処理中に押された回数 (T11 の観測用)
        /// <summary>待ちの後に出た結果。手元メニューは serial の変化を見て文言を出す。</summary>
        [HideInInspector] public int travelResultSerial;
        [HideInInspector] public int travelResultCode;
        [HideInInspector] public int travelResultSpot = -1;

        // ---- Manager Owner (ローカル。Owner が替わると引き継がれない) ----
        private int[] _tvCanceled = new int[] {0,0,0,0,0,0,0,0};
        private int _tvCanceledAt;
        private int _dbgDelayReq, _dbgDelayCaller, _dbgDelayMask;
        private Vector3 _dbgDelayCenter;
        private float _dbgDelayYaw;

        // ---- 共通の検査の数値 (§D.3)。Editor の Verify (KH_CourseTravelEditor) も同じ定数を使う ----
        public const int TravelGroundMask = (1 << 0) | (1 << 11);   // KH_CourseRouteEditor の足場と同じ
        public const float TravelRayUp = 2f, TravelRayLength = 6f, TravelNavReach = 0.5f, TravelSlopeMinY = 0.94f;
        public const float TravelHeadLow = 0.55f, TravelHeadHigh = 1.65f, TravelHeadRadius = 0.25f; // 地面 +0.3〜+1.9 m の円柱
        public const float TravelRiverClear = 10f, TravelRiverWindow = 12f, TravelBridgeHalfZ = 8f, TravelBridgeHalfX = 18f;
        public const float TravelPeopleGap = 1.0f;
        const float TravelTicketBlockMs = 5000f;               // 同じ犬への新しい要求 ID を捨てる間隔 (§B.5)
        const float TravelExpireMs = 20000f;                   // Phase 1 をこの時間で片付ける (犬は 15 秒で見なくなる)
        const float TravelHereDistance = 15f;                  // これより近い地点へはワープしない

        // ============================================================
        // 要求する人 (手元メニューから)
        // ============================================================

        /// <summary>その地点へワープを始める。戻り値は結果コード (TravelStarted なら確認待ちに入った)。ローカル専用。</summary>
        public int _TravelBegin(int spot)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return TravelUnavailable;
            if (_tvState == 1) { _tvBusyRejects++; return TravelBusy; }
            if (Time.time < _tvCooldownUntil) return TravelCooling;
            if (!_TravelWarpable(spot)) return TravelUnavailable;
            if (TravelFlat(lp.GetPosition(), spotPositions[spot]) < TravelHereDistance) return TravelAlreadyHere;
            int mask = _TravelMyMask();
            if (TravelAnyChasing(mask)) return TravelBall;
            if (!TravelFindLanding(spot, lp.playerId)) { _tvCooldownUntil = Time.time + travelCooldownFail; return TravelCrowded; }
            float yaw = spotYaws[spot];
            if (mask == 0)
            {
                // 犬がいなければ切符は出さない (§B.1 の 0 匹)
                TravelTeleport(lp, _tvLanding, yaw);
                _tvCooldownUntil = Time.time + travelCooldownOk;
                return TravelArrived;
            }
            _tvSeq = (_tvSeq % 65535) + 1;
            _tvReq = lp.playerId * 65536 + _tvSeq;
            _tvMask = mask; _tvSpot = spot; _tvCenter = _tvLanding; _tvYaw = yaw;
            _tvState = 1; _tvResent = false; _tvSentAt = Time.time;
            TravelSend();
            return TravelStarted;
        }

        /// <summary>確認待ちを自分で取りやめる。ローカル専用 (段階 2 で暗転中の取消にも使う)。</summary>
        public void _TravelCancel()
        {
            if (_tvState != 1) return;
            TravelSendCancel();
            TravelFinish(TravelCanceledByMe, travelCooldownFail);
        }

        public bool _TravelWaiting() { return _tvState == 1; }
        /// <summary>確認待ちの残り (取りやめまで。秒)。待っていなければ 0。</summary>
        public float _TravelWaitLeft() { return _tvState == 1 ? Mathf.Max(0f, travelGiveUpAfter - (Time.time - _tvSentAt)) : 0f; }
        public bool _TravelAwaitingDogs() { return _tvState == 2; }
        public int _TravelWaitingSpot() { return _tvState == 1 ? _tvSpot : -1; }
        public float _TravelCooldownLeft() { return Mathf.Max(0f, _tvCooldownUntil - Time.time); }

        public bool _TravelWarpable(int spot)
        {
            return spot >= 0 && spotPositions != null && spot < spotPositions.Length && spotYaws != null && spot < spotYaws.Length
                && spotWarpEnabled != null && spot < spotWarpEnabled.Length && spotWarpEnabled[spot];
        }

        /// <summary>自分の接続犬 (ビット i = 犬 i)。</summary>
        public int _TravelMyMask()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return 0;
            int mask = 0;
            for (int i = 0; i < walkers.Length; i++) if (walkers[i] == lp.playerId) mask |= 1 << i;
            return mask;
        }

        /// <summary>毎フレーム (Update の先頭)。確認・再送・取りやめ・接続の変化を見る。</summary>
        private void TravelTick()
        {
            if (_tvState == 2) { TravelTickDogs(); return; }
            if (_tvState != 1) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { _tvState = 0; return; }
            if (_TravelMyMask() != _tvMask)
            {
                Debug.Log("[KH_Leash] travel req=" + _tvReq + " stopped: leashed dogs changed");
                TravelSendCancel();
                TravelFinish(TravelChanged, travelCooldownFail);
                return;
            }
            if (TravelConfirmed())
            {
                _tvState = 0;
                if (debugSkipTravelTeleport > 0)
                {
                    debugSkipTravelTeleport--;
                    Debug.Log("[KH_Leash] travel req=" + _tvReq + " confirmed; TeleportTo skipped (debugSkipTravelTeleport)");
                    _tvCooldownUntil = Time.time + travelCooldownOk;
                    TravelPost(TravelStayed);
                    return;
                }
                // 共通の検査 5 (人) を移動の直前にもう一度 (レビュー U/D 1)。確認待ちは最大 8 秒あり、その間に人が来ることがある。
                // 別の候補へ切り替えると犬の切符も新しい要求で書き直しになるので、初期公開は取りやめだけにする
                if (!TravelPeopleClear(_tvCenter))
                {
                    Debug.Log("[KH_Leash] travel req=" + _tvReq + " stopped: landing occupied");
                    TravelSendCancel();
                    TravelFinish(TravelBlocked, travelCooldownFail);
                    return;
                }
                TravelTeleport(lp, _tvCenter, _tvYaw);
                Debug.Log("[KH_Leash] travel req=" + _tvReq + " confirmed; teleported to spot " + _tvSpot);
                _tvCooldownUntil = Time.time + travelCooldownOk;
                _tvState = 2; _tvArrivedAt = Time.time;
                TravelPost(TravelWaitingDogs);
                return;
            }
            float age = Time.time - _tvSentAt;
            if (!_tvResent && age >= travelResendAfter)
            {
                _tvResent = true;
                Debug.Log("[KH_Leash] travel req=" + _tvReq + " resend");
                TravelSend();
            }
            if (age >= travelGiveUpAfter)
            {
                Debug.Log("[KH_Leash] travel req=" + _tvReq + " gave up (no ticket)");
                TravelSendCancel();
                TravelFinish(TravelNoAnswer, travelCooldownFail);
            }
        }

        /// <summary>本人は着いた。全頭の Phase 2 (犬の Owner が適用した) か 15 秒で「着きました」。接続が変わっても待つのをやめる。</summary>
        private void TravelTickDogs()
        {
            bool done = true;
            for (int i = 0; i < travelReq.Length; i++)
            {
                if ((_tvMask & (1 << i)) == 0) continue;
                if (walkers[i] != (Utilities.IsValid(Networking.LocalPlayer) ? Networking.LocalPlayer.playerId : -1)) continue;
                if (travelReq[i] != _tvReq || travelPhase[i] != TravelPhaseDone) { done = false; break; }
            }
            if (!done && Time.time - _tvArrivedAt < TravelDogsWait) return;
            _tvState = 0;
            TravelPost(TravelArrived);
        }

        private bool TravelConfirmed()
        {
            for (int i = 0; i < travelReq.Length; i++)
            {
                if ((_tvMask & (1 << i)) == 0) continue;
                if (travelReq[i] != _tvReq) return false;
                if (travelPhase[i] != TravelPhaseReady && travelPhase[i] != TravelPhaseDone) return false;
            }
            return true;
        }

        private void TravelFinish(int code, float cooldown)
        {
            _tvState = 0;
            _tvCooldownUntil = Time.time + cooldown;
            TravelPost(code);
        }

        private void TravelPost(int code)
        {
            travelResultCode = code; travelResultSpot = _tvSpot; travelResultSerial++;
        }

        private void TravelSend()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            int sendMask = _tvMask | debugForgeTravelMask;
            if (Networking.IsOwner(gameObject)) ReceiveTravel(lp.playerId, _tvReq, sendMask, _tvCenter, _tvYaw);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqTravel), _tvReq, sendMask, _tvCenter, _tvYaw);
        }

        private void TravelSendCancel()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (Networking.IsOwner(gameObject)) CancelFrom(lp.playerId, _tvReq);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(CancelTravel), _tvReq);
        }

        private void TravelTeleport(VRCPlayerApi lp, Vector3 point, float yaw)
        {
            // 4 引数版で向きの扱いと lerpOnRemote を明示する (設計書 事実 3・§D.1)
            lp.TeleportTo(point, Quaternion.Euler(0f, yaw, 0f), VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint, false);
        }

        private bool TravelAnyChasing(int mask)
        {
            for (int i = 0; i < walkers.Length; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                if (fetchers != null && i < fetchers.Length && fetchers[i] != null && !fetchers[i].IsIdle) return true;
                if (frisbeeCatchers != null && i < frisbeeCatchers.Length && frisbeeCatchers[i] != null && !frisbeeCatchers[i].IsIdle) return true;
            }
            return false;
        }

        // ============================================================
        // 着地点 (設計書 §D.3。共通の検査 5 つ)
        // ============================================================

        /// <summary>地点の基準点 → 半径 1.5 m の 6 点 (開始角 playerId × 137.5°) → 半径 3 m の 12 点 → 前後のノード (±1〜5)。
        /// 通った点を _tvLanding に置く。全部だめなら false (移動しない)。</summary>
        private bool TravelFindLanding(int spot, int playerId)
        {
            Vector3 c = spotPositions[spot];
            if (_TravelLandingOk(c)) return true;
            float start = playerId * 137.5f; // Sin/Cos take any angle (no float % needed in Udon)
            for (int k = 0; k < 6; k++)
            {
                float a = (start + k * 60f) * Mathf.Deg2Rad;
                if (_TravelLandingOk(c + new Vector3(Mathf.Sin(a) * 1.5f, 0f, Mathf.Cos(a) * 1.5f))) return true;
            }
            for (int k = 0; k < 12; k++)
            {
                float a = (start + k * 30f) * Mathf.Deg2Rad;
                if (_TravelLandingOk(c + new Vector3(Mathf.Sin(a) * 3f, 0f, Mathf.Cos(a) * 3f))) return true;
            }
            if (route != null && route.count > 0 && spotNodes != null && spot < spotNodes.Length && spotNodes[spot] >= 0)
            {
                int node = spotNodes[spot];
                for (int d = 1; d <= 5; d++)
                {
                    if (_TravelLandingOk(route._Position((node + d) % route.count))) return true;
                    if (_TravelLandingOk(route._Position((node - d + route.count) % route.count))) return true;
                }
            }
            return false;
        }

        /// <summary>共通の検査 5 つ。1 足場 (真下の Raycast + NavMesh 0.5 m) / 2 傾斜 (法線 y ≥ 0.94) / 3 頭上 (CheckCapsule 半径 0.25、
        /// 地面 +0.3〜+1.9 m) / 4 水 (川の中心から 10 m 以上・橋の区域の外) / 5 人 (ほかのプレイヤーから 1.0 m 以上)。
        /// 通ったら足元を _tvLanding に置く。Editor の Verify (Course/81) は 1〜4 を同じ数値で行う。</summary>
        public bool _TravelLandingOk(Vector3 p)
        {
            RaycastHit hit;
            if (!Physics.Raycast(p + Vector3.up * TravelRayUp, Vector3.down, out hit, TravelRayLength, TravelGroundMask, QueryTriggerInteraction.Ignore)) return false;
            if (hit.normal.y < TravelSlopeMinY) return false;
            Vector3 foot = hit.point;
            NavMeshHit nav;
            if (!NavMesh.SamplePosition(foot, out nav, TravelNavReach, NavMesh.AllAreas)) return false;
            if (Physics.CheckCapsule(foot + Vector3.up * TravelHeadLow, foot + Vector3.up * TravelHeadHigh, TravelHeadRadius, TravelGroundMask, QueryTriggerInteraction.Ignore)) return false;
            if (!_TravelWaterOk(foot)) return false;
            if (!TravelPeopleClear(foot)) return false;
            _tvLanding = foot;
            return true;
        }

        /// <summary>共通の検査 4。川の中心線から 10 m 以上、かつ橋の区域 (川の中心から x±18 m・橋から z±8 m) の外。
        /// 川のデータが無ければ安全と言えないので false。</summary>
        public bool _TravelWaterOk(Vector3 p)
        {
            if (travelRiverXs == null || travelRiverXs.Length == 0 || travelRiverStep <= 0f) return false;
            int k0 = Mathf.Max(0, Mathf.FloorToInt((p.z - TravelRiverWindow - travelRiverZ0) / travelRiverStep));
            int k1 = Mathf.Min(travelRiverXs.Length - 1, Mathf.CeilToInt((p.z + TravelRiverWindow - travelRiverZ0) / travelRiverStep));
            for (int k = k0; k <= k1; k++)
            {
                float dx = p.x - travelRiverXs[k];
                float dz = p.z - (travelRiverZ0 + k * travelRiverStep);
                if (dx * dx + dz * dz < TravelRiverClear * TravelRiverClear) return false;
            }
            if (travelBridgeZs != null && travelBridgeXs != null)
                for (int b = 0; b < travelBridgeZs.Length && b < travelBridgeXs.Length; b++)
                    if (Mathf.Abs(p.z - travelBridgeZs[b]) <= TravelBridgeHalfZ && Mathf.Abs(p.x - travelBridgeXs[b]) <= TravelBridgeHalfX) return false;
            return true;
        }

        private bool TravelPeopleClear(Vector3 p)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            int count = VRCPlayerApi.GetPlayerCount();
            if (count <= 1) return true;
            VRCPlayerApi[] players = new VRCPlayerApi[count];
            VRCPlayerApi.GetPlayers(players);
            for (int i = 0; i < players.Length; i++)
            {
                VRCPlayerApi other = players[i];
                if (!Utilities.IsValid(other) || (Utilities.IsValid(lp) && other.playerId == lp.playerId)) continue;
                if (TravelFlat(other.GetPosition(), p) < TravelPeopleGap) return false;
            }
            return true;
        }

        private float TravelFlat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        // ============================================================
        // Manager Owner (切符を書く唯一の人)
        // ============================================================

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void ReqTravel(int reqId, int dogMask, Vector3 center, float yaw) // net: callable-ok: CallingPlayer, reqId owner and dogMask checked against walkers
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            ReceiveTravel(caller.playerId, reqId, dogMask, center, yaw);
        }

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void CancelTravel(int reqId) // net: callable-ok: only the requester's own reqId is cancelled
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            CancelFrom(caller.playerId, reqId);
        }

        // 1 回の移動で最大 6 匹ぶん届くので 6/s (ReqAttach と同じ)。設計書 §B.5 の 2/s からの変更。取りこぼしは犬の側が 1 秒ごとに再送する
        [NetworkCallable(maxEventsPerSecond: 6)]
        public void ReqTravelDone(int dog, int reqId) // net: callable-ok: sender must own that dog's root; reqId must match
        {
            if (!Networking.IsOwner(gameObject) || dog < 0 || dog >= travelReq.Length || dogRoots == null || dog >= dogRoots.Length || dogRoots[dog] == null) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            VRCPlayerApi dogOwner = Networking.GetOwner(dogRoots[dog].gameObject);
            if (!Utilities.IsValid(caller) || !Utilities.IsValid(dogOwner) || caller.playerId != dogOwner.playerId) return;
            DoneFrom(dog, reqId);
        }

        /// <summary>犬の Owner が呼ぶ (KD_DogWanderController.HillsideTravel.cs)。自分が Manager Owner なら直接書く。</summary>
        public void _ReportTravelDone(int dog, int reqId)
        {
            if (Networking.IsOwner(gameObject)) DoneFrom(dog, reqId);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqTravelDone), dog, reqId);
        }

        /// <summary>試験用の遅延 (debugDelayTravel) の書き込み。ローカル専用。</summary>
        public void _DebugDelayedTravel()
        {
            int req = _dbgDelayReq;
            _dbgDelayReq = 0;
            if (req == 0) return;
            Debug.Log("[KH_Leash] ReqTravel req=" + req + " delayed write now (debugDelayTravel)");
            WriteTravel(_dbgDelayCaller, req, _dbgDelayMask, _dbgDelayCenter, _dbgDelayYaw);
        }

        private void ReceiveTravel(int callerId, int reqId, int dogMask, Vector3 center, float yaw)
        {
            if (reqId != 0 && reqId == _dbgDelayReq)
            {
                Debug.Log("[KH_Leash] ReqTravel from player " + callerId + " req=" + reqId + " held (debugDelayTravel)");
                return;
            }
            if (debugDelayTravel > 0f && _dbgDelayReq == 0)
            {
                _dbgDelayReq = reqId; _dbgDelayCaller = callerId; _dbgDelayMask = dogMask; _dbgDelayCenter = center; _dbgDelayYaw = yaw;
                Debug.Log("[KH_Leash] ReqTravel from player " + callerId + " req=" + reqId + " delayed " + debugDelayTravel.ToString("F1") + "s (debugDelayTravel)");
                SendCustomEventDelayedSeconds(nameof(_DebugDelayedTravel), debugDelayTravel);
                debugDelayTravel = 0f;
                return;
            }
            if (debugDropTravel > 0)
            {
                Debug.Log("[KH_Leash] ReqTravel from player " + callerId + " req=" + reqId + " dropped (debugDropTravel)");
                return;
            }
            WriteTravel(callerId, reqId, dogMask, center, yaw);
        }

        private void WriteTravel(int callerId, int reqId, int dogMask, Vector3 center, float yaw)
        {
            if (!Networking.IsOwner(gameObject)) return;
            string head = "[KH_Leash] ReqTravel from player " + callerId + " req=" + reqId;
            if (reqId <= 0 || reqId / 65536 != callerId) { Debug.Log(head + " rejected: reqId"); return; }
            if (TravelWasCanceled(reqId)) { Debug.Log(head + " rejected: cancelled earlier"); return; }
            // レビュー D 2: _tvCanceled は Owner のローカルで 8 件だけ。Owner 交代や押し出しの後でも古い要求を通さないよう、
            // 同期済みの配列だけで判定する — その人の犬に載っている要求 ID (Phase を問わず) より古い番号は捨てる。
            // seq は 16 bit で回るので、差が半周 (32768) 未満のときだけ「新しい」とみなす。
            // なお取消済みの要求が遅れて Phase 1 になっても、本人は L へ行かないので犬は動かず、15 秒で期限切れになる
            if (TravelOlderThanTicket(callerId, reqId)) { Debug.Log(head + " rejected: older than the ticket on this player's dogs"); return; }
            int mine = 0, dogs = 0;
            for (int i = 0; i < walkers.Length; i++) if (walkers[i] == callerId) { mine |= 1 << i; dogs++; }
            if (dogMask == 0 || dogMask != mine) { Debug.Log(head + " rejected: dogMask=" + dogMask + " leashed=" + mine); return; }
            if (!TravelCenterOk(center)) { Debug.Log(head + " rejected: landing " + center); return; }
            bool resend = true;
            for (int i = 0; i < travelReq.Length; i++) if ((dogMask & (1 << i)) != 0 && travelReq[i] != reqId) resend = false;
            if (resend) { Debug.Log(head + " dogs=" + dogs + " resend (unchanged)"); RequestSerialization(); return; }
            int now = Networking.GetServerTimeInMilliseconds();
            for (int i = 0; i < travelReq.Length; i++)
            {
                if ((dogMask & (1 << i)) == 0 || travelReq[i] == 0 || travelReq[i] == reqId) continue;
                if (now - travelAtMs[i] < TravelTicketBlockMs) { Debug.Log(head + " rejected: dog" + i + " ticket younger than 5s"); return; }
            }
            for (int i = 0; i < travelReq.Length; i++)
            {
                if ((dogMask & (1 << i)) == 0) continue;
                travelReq[i] = reqId;
                travelHolder[i] = callerId;
                travelCenter[i] = center;
                travelPoint[i] = TravelPlacement(i, callerId, center, yaw);
                travelYaw[i] = yaw;
                travelAtMs[i] = now;
                travelPhase[i] = TravelPhaseReady;
            }
            Debug.Log(head + " dogs=" + dogs + " phase=1 center=" + center);
            RequestSerialization();
        }

        private void CancelFrom(int callerId, int reqId)
        {
            if (reqId <= 0 || reqId / 65536 != callerId) return;
            _tvCanceled[_tvCanceledAt] = reqId;
            _tvCanceledAt = (_tvCanceledAt + 1) % _tvCanceled.Length;
            bool changed = false;
            for (int i = 0; i < travelReq.Length; i++)
                if (travelReq[i] == reqId && travelPhase[i] == TravelPhaseReady) { travelPhase[i] = TravelPhaseCanceled; changed = true; }
            Debug.Log("[KH_Leash] CancelTravel from player " + callerId + " req=" + reqId + " changed=" + changed);
            if (changed) RequestSerialization();
        }

        private void DoneFrom(int dog, int reqId)
        {
            if (dog < 0 || dog >= travelReq.Length) return;
            if (debugDropTravelDone > 0)
            {
                debugDropTravelDone--;
                Debug.Log("[KH_Leash] travel done dog" + dog + " req=" + reqId + " dropped (debugDropTravelDone)");
                return;
            }
            if (travelReq[dog] != reqId || travelPhase[dog] != TravelPhaseReady) return;
            travelPhase[dog] = TravelPhaseDone;
            Debug.Log("[KH_Leash] travel done dog" + dog + " req=" + reqId);
            RequestSerialization();
        }

        private bool TravelOlderThanTicket(int callerId, int reqId)
        {
            int seq = reqId - callerId * 65536;
            for (int i = 0; i < travelReq.Length; i++)
            {
                int held = travelReq[i];
                if (held == 0 || held == reqId || held / 65536 != callerId || walkers[i] != callerId) continue;
                int ahead = (seq - (held - callerId * 65536) + 65536) % 65536;
                if (ahead == 0 || ahead >= 32768) return true;
            }
            return false;
        }

        private bool TravelWasCanceled(int reqId)
        {
            for (int i = 0; i < _tvCanceled.Length; i++) if (_tvCanceled[i] == reqId) return true;
            return false;
        }

        /// <summary>§B.5: 偽装された L で他人の犬を変な所へ置かない。NavMesh 1.0 m・コースの範囲・川床でない。</summary>
        private bool TravelCenterOk(Vector3 p)
        {
            if (p.x < 0f || p.x > 600f || p.z < 0f || p.z > 720f) return false;
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(p, out hit, 1f, NavMesh.AllAreas)) return false;
            return _TravelWaterOk(p);
        }

        /// <summary>§B.1: 散歩中の目標 (_WalkingTarget) と同じ左右と段の近似。基準は着地点 + 飼い主の向き。揺らぎは入れない。</summary>
        private Vector3 TravelPlacement(int dog, int callerId, Vector3 center, float yaw)
        {
            int slot = 0;
            for (int i = 0; i < dog; i++) if (walkers[i] == callerId && hands[i] == hands[dog]) slot++;
            float side = hands[dog] == 0 ? -1f : 1f;
            Vector3 offset = new Vector3(side * (0.75f + slot * 0.45f), 0f, 0.5f + slot * 0.35f);
            Vector3 want = center + Quaternion.Euler(0f, yaw, 0f) * offset;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(want, out hit, 1f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - center.y) <= 0.6f) return hit.position;
            want = Vector3.Lerp(center, want, 0.5f);
            if (NavMesh.SamplePosition(want, out hit, 1f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - center.y) <= 0.6f) return hit.position;
            return center;
        }

        /// <summary>解除したら、その犬の切符を消す (§B.2 の 4)。Release から呼ぶ。</summary>
        private void ClearTravel(int dog)
        {
            if (dog < 0 || dog >= travelReq.Length) return;
            travelReq[dog] = 0; travelHolder[dog] = -1; travelPhase[dog] = TravelPhaseNone;
        }

        /// <summary>Owner の保守 (0.5 秒ごと)。20 秒 (TravelExpireMs) を過ぎた準備の切符を Phase 0 に片付ける。犬は 15 秒
        /// (TravelTicketLifeMs) で見なくなるので、その後の片付け。変わったら true。</summary>
        private bool ExpireTravel()
        {
            bool changed = false;
            int now = Networking.GetServerTimeInMilliseconds();
            for (int i = 0; i < travelPhase.Length; i++)
            {
                if (travelPhase[i] != TravelPhaseReady || now - travelAtMs[i] < TravelExpireMs) continue;
                travelPhase[i] = TravelPhaseNone;
                Debug.Log("[KH_Leash] travel expired dog" + i + " req=" + travelReq[i]);
                changed = true;
            }
            return changed;
        }
    }
}
