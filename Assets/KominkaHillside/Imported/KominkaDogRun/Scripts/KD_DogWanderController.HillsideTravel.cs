using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>IMP-WLD-001 段階 1: 地点ワープの切符を犬の側で適用する **唯一の場所** (設計書 §B.4 の手順 6)。
    ///
    /// 犬の Owner だけが、UpdateHillsideLeash の Return の判定より前に毎フレーム呼ぶ。条件は
    /// Phase==4 (本人が移動済み)・切符が 15 秒以内・walkers[i]==travelHolder[i]・飼い主が L から 6 m 以内 の 4 つ。成立したら:
    /// - 犬が手からリード長 × 1.25 より遠ければ FlagDiscontinuity + travelPoint に置き、向きを travelYaw にする
    /// - 近ければ位置は変えない
    /// - **どちらでも**経路の状態を初期化し、完了を Manager Owner へ報告する (destinations は変えない)
    ///
    /// Phase 1・4 の間は、同じ犬の Return による追従テレポートを止める (TravelPending。HillsideLeash の Return の枝)。
    /// 「適用済み」はこのクライアントが適用した reqId だけを覚える (同じ切符で 2 回置かないため)。
    /// Owner が替われば新しい Owner は覚えていないが、同期された Phase と距離で同じ判断をするので、手のそばの犬は動かさない。
    /// 破壊監査 2026-09-28: RC-01 = Phase 1 (準備) だけでは本人の移動前なので適用しない (以前は 6 m の距離だけで適用し、合流先が
    /// 3〜5 m のときに本人より先に跳んだ)。RC-02 = 適用済みの完了の再送は距離の判定より前 (以前は本人が 6 m より離れると再送が止まり、
    /// Phase が完了にならずに残った)。要求 ID・接続先・Phase・期限の検査は再送にも残す。</summary>
    public partial class KD_DogWanderController
    {
        private int _travelAppliedReq;         // このクライアントが適用した要求 ID (0 = なし)
        private float _travelDoneSentAt = -1000f;
        private int _travelMoves;               // 切符で位置を変えた回数 (ハーネスの観測用。ローカル)
        private int _travelApplies;             // 適用 (経路の初期化) をした回数 (同上)

        const int TravelTicketLifeMs = 15000;
        const float TravelHolderNear = 6f;
        const float TravelDoneRetry = 1f;

        // IMP-DOG-034 C1-1d: TravelPending は 1 tick に 2〜4 回呼ばれる (WashYieldsToErrand・UpdateHillsideTravel・Return の枝・
        // WashLeashStop)。中身は manager の同期配列と GetServerTimeInMilliseconds の読みだけの純関数なので、自分の tick の中では
        // 最初の答えを使い回す。無効化: UpdateTick の先頭、manager の切符・接続を書き換え得る呼び出しの直後 (_ReportTravelDone、
        // 旧 RC-14 の _RequestWaterRelease。IMP-DOG-053 で退役)。差: 期限 15 s の境目が同じ tick の 2 回目の呼び出しの間 (同一フレーム内の ms) に来た場合だけ、
        // その tick は最初の答えで揃う (1 フレーム以内)。tick の外 (他 Behaviour からの呼び出し) では従来どおり毎回読む
        private int _travelMemoFrame = -1;
        private bool _travelMemo;

        private bool TravelPending()
        {
            if (_inTick && _travelMemoFrame == Time.frameCount) return _travelMemo;
            bool pending = TravelPendingRead();
            if (_inTick) { _travelMemo = pending; _travelMemoFrame = Time.frameCount; }
            return pending;
        }

        /// <summary>この犬に、今の飼い主あての準備中の切符があるか。Return の追従テレポートを止める条件。</summary>
        private bool TravelPendingRead()
        {
            KominkaHillside.KH_LeashManager m = hillsideLeashes;
            if (m == null || m.travelPhase == null || dogIndex < 0 || dogIndex >= m.travelPhase.Length) return false;
            int ph = m.travelPhase[dogIndex];
            if (ph != KominkaHillside.KH_LeashManager.TravelPhaseReady && ph != KominkaHillside.KH_LeashManager.TravelPhaseMoved) return false;
            if (m.travelHolder[dogIndex] < 0 || m.travelHolder[dogIndex] != m.walkers[dogIndex]) return false;
            int age = Networking.GetServerTimeInMilliseconds() - m.travelAtMs[dogIndex];
            return age >= -2000 && age <= TravelTicketLifeMs;
        }

        /// <summary>犬の Owner が毎フレーム呼ぶ。位置を変えたら true (このフレームの残りは飛ばす)。</summary>
        private bool UpdateHillsideTravel(Vector3 holderPos, float handDist, float leash)
        {
            if (!TravelPending()) return false;
            KominkaHillside.KH_LeashManager m = hillsideLeashes;
            int req = m.travelReq[dogIndex];
            bool moved4 = m.travelPhase[dogIndex] == KominkaHillside.KH_LeashManager.TravelPhaseMoved;
            if (_travelAppliedReq == req)
            {
                // 適用は済んでいる。完了が Manager に届くまで 1 秒ごとに報告し直す (T6)。本人がもう着地点から離れていても続ける (RC-02)
                if (moved4 && Time.time - _travelDoneSentAt >= TravelDoneRetry)
                {
                    _travelDoneSentAt = Time.time;
                    m._ReportTravelDone(dogIndex, req);
                    _travelMemoFrame = -1;   // IMP-DOG-034 C1-1d: the manager Owner may have moved the phase to Done just now
                }
                return false;
            }
            if (!moved4) return false;   // RC-01: 本人の移動済み (Phase 4) より前は適用しない
            if (Horizontal(holderPos, m.travelCenter[dogIndex]) > TravelHolderNear) return false;
            _travelAppliedReq = req;
            _travelApplies++;
            bool moved = false;
            if (handDist > leash * 1.25f)
            {
                VRCObjectSync sync = dogRoot.GetComponent<VRCObjectSync>();
                if (sync != null) sync.FlagDiscontinuity();
                _dbgWriter = DbgWTravel; dogRoot.SetPositionAndRotation(m.travelPoint[dogIndex], Quaternion.Euler(0f, m.travelYaw[dogIndex], 0f)); // net: owner-only: called on the dog Owner path only
                _speed = 0f;
                _travelMoves++;
                moved = true;
            }
            // 経路の状態の初期化 (§B.4。位置を変えない場合も必ず)
            _routeNext = -1; _routeResume = -1; _arrived = false; _nextRouteEntry = 0f;
            ResetPauseAndPoseState();
            ResetHolderSpeed();
            _leashCorners = null; _nextLeashPath = 0f;
            // §F.4: an unfinished swim-height ease left by a leash taken up on the bank (HillsideWater.cs).
            // 2026-10-11 の決定 (DES-DOG-010) で上書き (IMP-DOG-053 #6): only when the ticket moved the dog (to the land at travelPoint;
            // the leash's river follow ends there and the pose is refreshed for the new place). A ticket that leaves a dog in the water
            // where it is (near the hand) keeps its float: dropping it would sink a swimming dog to the riverbed
            if (moved)
            {
                LeashRiverReset("ticket");
                WaterClearFloat();
                WaterPoseRefresh();
            }
            _nextTeleportTry = Time.time + 1f;
            if (leashMode == LeashReturn) leashMode = LeashWalk;
            _travelDoneSentAt = Time.time;
            Debug.Log("[KH_Leash] dog" + dogIndex + " travel apply req=" + req + " moved=" + moved + " handDist=" + handDist.ToString("F1"));
            m._ReportTravelDone(dogIndex, req);
            _travelMemoFrame = -1;   // IMP-DOG-034 C1-1d: see TravelPending
            if (moved) ApplyMoveAnim();
            return moved;
        }
    }
}
