using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>IMP-WLD-001 段階 1: 地点ワープの切符を犬の側で適用する **唯一の場所** (設計書 §B.4 の手順 6)。
    ///
    /// 犬の Owner だけが、UpdateHillsideLeash の Return の判定より前に毎フレーム呼ぶ。条件は
    /// Phase==1・切符が 15 秒以内・walkers[i]==travelHolder[i]・飼い主が L から 6 m 以内 の 4 つ。成立したら:
    /// - 犬が手からリード長 × 1.25 より遠ければ FlagDiscontinuity + travelPoint に置き、向きを travelYaw にする
    /// - 近ければ位置は変えない
    /// - **どちらでも**経路の状態を初期化し、完了を Manager Owner へ報告する (destinations は変えない)
    ///
    /// Phase 1 の間は、同じ犬の Return による追従テレポートを止める (TravelPending。HillsideLeash の Return の枝)。
    /// 「適用済み」はこのクライアントが適用した reqId だけを覚える (同じ切符で 2 回置かないため)。
    /// Owner が替われば新しい Owner は覚えていないが、同期された Phase と距離で同じ判断をするので、手のそばの犬は動かさない。</summary>
    public partial class KD_DogWanderController
    {
        private int _travelAppliedReq;         // このクライアントが適用した要求 ID (0 = なし)
        private float _travelDoneSentAt = -1000f;
        private int _travelMoves;               // 切符で位置を変えた回数 (ハーネスの観測用。ローカル)
        private int _travelApplies;             // 適用 (経路の初期化) をした回数 (同上)

        const int TravelTicketLifeMs = 15000;
        const float TravelHolderNear = 6f;
        const float TravelDoneRetry = 1f;

        /// <summary>この犬に、今の飼い主あての準備中の切符があるか。Return の追従テレポートを止める条件。</summary>
        private bool TravelPending()
        {
            KominkaHillside.KH_LeashManager m = hillsideLeashes;
            if (m == null || m.travelPhase == null || dogIndex < 0 || dogIndex >= m.travelPhase.Length) return false;
            if (m.travelPhase[dogIndex] != KominkaHillside.KH_LeashManager.TravelPhaseReady) return false;
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
            if (Horizontal(holderPos, m.travelCenter[dogIndex]) > TravelHolderNear) return false;
            if (_travelAppliedReq == req)
            {
                // 適用は済んでいる。完了が Manager に届くまで 1 秒ごとに報告し直す (T6)
                if (Time.time - _travelDoneSentAt >= TravelDoneRetry)
                {
                    _travelDoneSentAt = Time.time;
                    m._ReportTravelDone(dogIndex, req);
                }
                return false;
            }
            _travelAppliedReq = req;
            _travelApplies++;
            bool moved = false;
            if (handDist > leash * 1.25f)
            {
                VRCObjectSync sync = dogRoot.GetComponent<VRCObjectSync>();
                if (sync != null) sync.FlagDiscontinuity();
                dogRoot.SetPositionAndRotation(m.travelPoint[dogIndex], Quaternion.Euler(0f, m.travelYaw[dogIndex], 0f)); // net: owner-only: called on the dog Owner path only
                _speed = 0f;
                _travelMoves++;
                moved = true;
            }
            // 経路の状態の初期化 (§B.4。位置を変えない場合も必ず)
            _routeNext = -1; _routeResume = -1; _arrived = false; _nextRouteEntry = 0f;
            ResetPauseAndPoseState();
            ResetHolderSpeed();
            _leashCorners = null; _nextLeashPath = 0f;
            _nextTeleportTry = Time.time + 1f;
            if (leashMode == LeashReturn) leashMode = LeashWalk;
            _travelDoneSentAt = Time.time;
            Debug.Log("[KH_Leash] dog" + dogIndex + " travel apply req=" + req + " moved=" + moved + " handDist=" + handDist.ToString("F1"));
            m._ReportTravelDone(dogIndex, req);
            if (moved) ApplyMoveAnim();
            return moved;
        }
    }
}
