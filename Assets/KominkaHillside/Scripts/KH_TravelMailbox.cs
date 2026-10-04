using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    /// <summary>IMP-WLD-001 段階 3: 合流の受付箱 (設計書 §A.4・§A.7・§B.5・§D.4・§D.5)。IMP-WLD-002: 呼ぶ (設計書 §F 第 2 版)。
    ///
    /// `VRCPlayerObject` に載せる (入室ごとに複製され、本人が Owner)。同期は `accepting` 1 本だけ (Manual)。
    /// 1 つの箱が本人の 2 つの役割を持つ (どちらも **Owner の端末でだけ** 動く):
    /// - **受け手 T**: `ReqJoin(seq)` (合流) / `ReqCall(seq)` (呼ぶ) を受け、共通の <see cref="Receive"/> で判定して通知板 (KH_TravelNotice) を出す。
    ///   答えは申し込んだ人 R の箱へ `AnsJoin(seq, code)`。保留は 1 件。送り主ごとに「見た最大の seq」を覚え、それ以下の要求・取消は捨てる
    ///   (古い seq・取消の後に遅れて届いた要求が復活しない。§F.3)。同じ送り主の同じ種類の新しい seq は引き継ぎ、種類が違えば差し替え (世代 +1)。
    ///   承認は (送り主, seq, 種類, 世代) に固定し、世代が変わってから 0.5 秒は押下を受けない。前の通知を閉じてから 20 秒は code 2、
    ///   [しばらく受けない] で 10 分は code 3、「このインスタンスにいる間は受けない」は黙って code 0。
    ///   呼ぶの [行く]: R の速さの測定がそろってから着地点を決め、地点ワープと同じ切符・暗転 (KH_LeashManager._TravelBeginJoin) で**自分が**移動する。
    ///   始められたら code 4 (準備中)、本人が移動したら code 1 (到着)、移動の前に止まったら code 5。始められなければ返事を送らず保留のまま理由を出す。
    /// - **申し込む人 R**: `_RequestJoin` / `_RequestCall` で相手の箱へ送る。`AnsJoin` は送り主 == 相手 かつ seq が保留中のものと一致するときだけ採る。
    ///   合流: 承認なら、相手の速さの測定が 2 回そろうのを待って (§D.3 の「歩いている」) 着地点を決め、自分が移動する。移動が終わるまで相手を覚え、
    ///   途中で相手が退出したら取りやめる (§D.4)。断り・時間切れの後、同じ相手へは 30 秒あける (相手ごとに覚える)。
    ///   呼ぶ: R は動かない。返事待ち 22 秒と到着待ち 20 秒の 2 段の期限。無応答は「結果を確認できませんでした」(30 秒に入れない)。
    /// 交差 (同じ 2 人が互いに申し込む): 確定した移動が優先、どちらも未確定なら playerId の小さい方が勝ち、大きい側は自分の申し込みを失効させる (§F.3)。
    /// code: 0 断り・時間切れ / 1 承認 (合流)・到着 (呼ぶ) / 2 対応中 / 3 休止中 / 4 準備中 (呼ぶ) / 5 移動の前に止まった (呼ぶ)。
    /// 受け付けの ON/OFF (`accepting`) は合流と呼ぶの両方に効き、PlayerData に保存する (ナイトモードと同じ作法)。OFF は新規の受け付けだけ止める。
    /// 相手を動かす機能は無い。フレンド判定は使わない。
    /// 破壊監査 2026-09-28 RC-07: 「この人からは受けない」と「同じ相手へ 30 秒」の履歴は、同じ人の枠 → 空き枠 → 退室した人の枠
    /// (30 秒の方は期限切れの枠も) の順に使い、それでも無ければ配列を 16 枠ずつ伸ばす。退室した人の枠は OnPlayerLeft でも空ける。
    /// RC-06: 取消できるかは _JoinCancelable() 1 か所で決め、手元メニューの表示と押したときの処理の両方がそれを使う。
    /// IMP-WLD-008 W4 (動画カメラの設計書 §5・§13・§14): 3 つめの種類 KindFilm (撮影の許可)。申し込み・照合・seq・保留 1 件・通知の間隔・休止・ブロック・
    /// 交差は合流と同じ処理を通る。許可そのものは KH_TravelMailbox.Film.cs の同期された表 (filmGrantIds / filmGrantSeqs) に持ち、返事のイベントは表示用。
    /// 撮影の種類は合流の取り消し (_JoinCancelable / _WaitingFor)・結果 (joinResult*) に出さない (撮影は filmResult*)。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public partial class KH_TravelMailbox : UdonSharpBehaviour
    {
        [UdonSynced] public bool accepting = true;

        [Header("Wiring (Tools/KominkaHillside/Course/85 writes these on the template)")]
        public KH_LeashManager manager;
        public KH_TravelNotice notice;

        [Header("Harness hook: シーンでは必ず 0 (Course/81 とビルドの門番が止める)")]
        [Tooltip(">0 なら相手の箱を使わずに待ち状態へ入り、_DebugAnswerNow で答えを模す。T の返事も相手へ送らず debugLastReply* に書く (ClientSim の人形は Udon を動かさない)")]
        public int debugJoinLoopback;
        public int debugAnswerCode;
        [Tooltip("_DebugIncomingNow / _DebugIncomingCancelNow の送り主 (人形の playerId)・種類 (0 合流 / 1 呼ぶ)・seq")]
        public int debugIncomingFrom, debugIncomingKind, debugIncomingSeq;
        [Tooltip(">0 なら交差の playerId の大小を逆にする (ClientSim ではローカルが 1 なので、大きい側の失効はこれでしか通らない)")]
        public int debugCrossInvert;
        [HideInInspector] public int debugReplyCount, debugLastReplySeq, debugLastReplyCode = -1;

        public const string SaveKey = "KH_TravelJoin.accepting";
        public const int KindJoin = 0, KindCall = 1, KindFilm = 2;
        public const int CodeNo = 0, CodeYes = 1, CodeBusy = 2, CodePaused = 3, CodePreparing = 4, CodeCallFailed = 5;
        // 申し込む側・呼ばれた側の結果 (手元メニューが文言に変える)
        public const int JoinWaiting = 10, JoinNotReady = 11, JoinMeasuring = 12, JoinLeft = 13, JoinCanceled = 14, JoinNoSafe = 15,
            JoinStarted = 16, JoinAgain = 17, JoinBusyMe = 18, JoinNotAccepting = 19, JoinStartFailed = 20,
            CallComing = 21, CallArrived = 22, CallNotComing = 23, CallUnknown = 24, CallCanceledBy = 25, CrossYield = 26, AnswerFirst = 27;
        // [行く] を始められなかった理由 (通知板の理由の行)。KH_LeashManager.Travel* の 1〜6 はそのまま使う
        public const int GoFailNone = -1, GoFailNoSafe = 100, GoFailMoving = 101;
        public const float AnswerSeconds = 20f, NoticeGap = 20f, PauseSeconds = 600f, SameTargetGap = 30f, ArriveSeconds = 20f, InputGuard = 0.5f;
        const float SampleEvery = 0.5f, MeasureCap = 1.5f;

        // ---- 受け手 (Owner のローカル) ----
        private int _inFrom = -1, _inSeq, _inKind, _inGen;
        private float _inAt, _inGenAt = -1000f, _closedAt = -1000f, _pauseUntil;
        private int[] _blocked = new int[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1};   // 足りなければ 16 枠ずつ伸ばす
        private int[] _seenIds = new int[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1};   // 送り主ごとの見た最大の seq (§F.3)
        private int[] _seenSeqs = new int[16];
        // 呼ばれた人の [行く]
        private bool _goPressed;              // 押した。R の速さの測定がそろうのを待っている
        private float _goPressAt;
        private int _goFail = GoFailNone;     // 最後に始められなかった理由 (通知板の理由の行)
        private int _goFrom = -1, _goSeq, _goSerial;   // 呼ばれて移動中 (切符待ち・暗転中)。Manager の結果の serial が変わるまで
        // 呼んだ人 R の速さ (受け手の保留中、0.5 秒ごと)
        private int _cId = -1, _cSamples;
        private Vector3 _cPrev, _cVel;
        private float _cPrevAt;
        // ---- 申し込む人 (Owner のローカル) ----
        private int _outTo = -1, _outSeq, _outKind, _outPhase;   // _outPhase: 0 返事待ち / 1 呼ぶの到着待ち (code 4 の後)
        private float _outAt;
        private int[] _deniedIds = new int[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1};   // 断り・時間切れの相手 (相手ごとに 30 秒)。人数の上限 16 を収める
        private float[] _deniedAts = new float[16];
        const int HistoryGrow = 16;
        // 相手の速さ (0.5 秒ごと)。承認の後もそろうまで測る
        private int _sampleId = -1, _samples;
        private Vector3 _tPrev, _tVel;
        private float _tPrevAt;
        private int _acceptFor = -1;          // 承認を受けたが、速さの測定がそろうのを待っている相手
        private float _acceptAt;
        private int _movingWith = -1;         // 承認された移動 (切符待ち・暗転中) の相手。退出したら取りやめる
        [HideInInspector] public int joinResultSerial, joinResultCode, joinResultTarget = -1, joinReason = -1, joinTravelCode;
        private bool _restored, _chosen;

        // ============================================================
        // 申し込む人 R
        // ============================================================

        /// <summary>相手へ合流を申し込む。戻り値は結果コード (JoinWaiting なら返事待ち)。ローカル専用。</summary>
        public int _RequestJoin(int targetId) { return _Request(targetId, KindJoin); }
        /// <summary>相手を呼ぶ (相手が [行く] を押したら、相手が自分で来る)。ローカル専用。</summary>
        public int _RequestCall(int targetId) { return _Request(targetId, KindCall); }

        /// <summary>合流・呼ぶの共通の入口。ローカル専用。</summary>
        public int _Request(int targetId, int kind)
        {
            if (!Networking.IsOwner(gameObject)) return JoinNotReady;
            if (kind != KindJoin && kind != KindCall && kind != KindFilm) return JoinBusyMe;
            if (kind == KindFilm && _filmPhase != FilmNone) return JoinBusyMe;   // 撮影の回は 1 つ。対象を変えるときはカメラが先に _FilmEnd する (§13.3)
            if (_outTo >= 0 || _acceptFor >= 0 || _movingWith >= 0 || _goFrom >= 0 || _goPressed) return JoinBusyMe;
            if (manager != null && (manager._TravelWaiting() || manager._TravelFading())) return JoinBusyMe;
            VRCPlayerApi target = VRCPlayerApi.GetPlayerById(targetId);
            if (!Utilities.IsValid(target) || target.isLocal) return JoinLeft;
            if (_inFrom == targetId) return AnswerFirst;   // 相手からの申し込みに先に答える (§F.4)
            if (DeniedRecently(targetId)) return JoinAgain;
            if (debugJoinLoopback <= 0)
            {
                KH_TravelMailbox box = _FindBox(target);
                if (box == null) return JoinNotReady;
                if (kind == KindFilm ? !box.filmAccepting : !box.accepting) return JoinNotAccepting;   // 撮影は撮影の受け付け (合流とは別の切り替え)
                _outSeq++;
                box.SendCustomNetworkEvent(NetworkEventTarget.Owner, kind == KindCall ? nameof(ReqCall) : kind == KindFilm ? nameof(ReqFilm) : nameof(ReqJoin), _outSeq);
                if (kind == KindFilm) _filmBox = box;
            }
            else { _outSeq++; if (kind == KindFilm) _filmBox = _FindBox(target); }
            _outTo = targetId; _outKind = kind; _outPhase = 0; _outAt = Time.time;
            if (kind == KindJoin) StartSampling(target);
            if (kind == KindFilm) FilmBegin(targetId, _outSeq);
            Debug.Log("[KH_Join] request to player " + targetId + " seq=" + _outSeq + " kind=" + kind + (debugJoinLoopback > 0 ? " (loopback)" : ""));
            PostKind(kind == KindFilm, JoinWaiting, targetId);
            return JoinWaiting;
        }

        /// <summary>申し込み・承認後の移動を取り消す。返事待ち (呼ぶの到着待ちを含む) なら CancelJoin を送り、
        /// 合流の承認後 (切符待ち・暗転中) なら移動を取りやめる。ローカル専用。</summary>
        public void _CancelJoinRequest()
        {
            if (_outTo >= 0 && _outKind != KindFilm)   // 撮影の申し込みはここでは取り消さない (撮影カメラの [取り消す] → _FilmEnd。§13.6)
            {
                SendCancelTo(_outTo);
                Debug.Log("[KH_Join] cancel to player " + _outTo + " seq=" + _outSeq + " kind=" + _outKind);
                int to = _outTo; ResetOut();
                Post(JoinCanceled, to);
                return;
            }
            if (_acceptFor >= 0) { int to = _acceptFor; _acceptFor = -1; _sampleId = -1; Post(JoinCanceled, to); return; }
            if (_movingWith >= 0 && manager != null && manager._TravelCancelable())
            {
                int to = _movingWith; _movingWith = -1;
                manager._TravelCancel();
                Post(JoinCanceled, to);
            }
        }

        /// <summary>合流・呼ぶの返事待ちの相手 (無ければ -1)。撮影の申し込みは含めない (撮影は _FilmTarget / _FilmPhase。§13.6)。</summary>
        public int _WaitingFor() { return _outKind == KindFilm ? -1 : _outTo; }
        /// <summary>送った申し込みの種類 (KindJoin / KindCall)。返事待ちでなければ KindJoin。</summary>
        public int _OutKind() { return _outTo >= 0 ? _outKind : KindJoin; }
        /// <summary>0 返事待ち / 1 呼ぶの到着待ち。</summary>
        public int _OutPhase() { return _outTo >= 0 ? _outPhase : 0; }
        /// <summary>合流を取り消せるか: 返事待ち (呼ぶの到着待ちを含む)・承認後の速さの測定待ち・切符待ち・暗転中。本人が移動した後 (犬待ち) は含めない (RC-06)。
        /// 手元メニューは表示 ([取り消す]) と押したとき (_CancelJoinRequest へ回す) の両方でこれを使う。</summary>
        public bool _JoinCancelable()
        {
            if ((_outTo >= 0 && _outKind != KindFilm) || _acceptFor >= 0) return true;   // 撮影の返事待ちは合流の [取り消す] にしない (§13.6)
            return _movingWith >= 0 && manager != null && manager._TravelCancelable();
        }
        /// <summary>取消の表示に出す残り秒数 (返事待ち = 返事の期限、測定待ち = 測定の上限、切符待ち = 確認の期限、暗転中 = 0)。</summary>
        public float _CancelLeft()
        {
            if (_outTo >= 0) return _WaitLeft();
            if (_acceptFor >= 0) return Mathf.Max(0f, MeasureCap - (Time.time - _acceptAt));
            return manager != null ? manager._TravelWaitLeft() : 0f;
        }
        /// <summary>承認された後、移動が終わるまでの相手 (測定待ち・切符待ち・暗転中)。無ければ -1。</summary>
        public int _MovingWith() { return _acceptFor >= 0 ? _acceptFor : _movingWith; }
        public float _WaitLeft()
        {
            if (_outTo < 0) return 0f;
            return Mathf.Max(0f, (_outPhase == 1 ? ArriveSeconds : AnswerSeconds) - (Time.time - _outAt));
        }

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void AnsJoin(int seq, int code) // net: callable-ok: sender must be the pending target and seq the pending one
        {
            if (!Networking.IsOwner(gameObject)) return;
            ReceiveAnswer(NetworkCalling.CallingPlayer, seq, code);
        }

        /// <summary>The answer check (sender = the pending target, seq = the pending seq). AnsJoin and the harness hook share it.</summary>
        private void ReceiveAnswer(VRCPlayerApi caller, int seq, int code)
        {
            if (!Utilities.IsValid(caller) || caller.playerId != _outTo || seq != _outSeq) return;
            HandleAnswer(code);
        }

        /// <summary>ハーネス専用: 人形 debugIncomingFrom からの AnsJoin(debugIncomingSeq, debugAnswerCode) を、送り主と seq の照合ごと通す。ローカル専用。</summary>
        public void _DebugAnswerFromNow()
        {
            if (debugJoinLoopback <= 0 || !Networking.IsOwner(gameObject)) return;
            ReceiveAnswer(VRCPlayerApi.GetPlayerById(debugIncomingFrom), debugIncomingSeq, debugAnswerCode);
        }

        /// <summary>ハーネス専用: 人形の答えを模す (debugJoinLoopback > 0 のときだけ)。ローカル専用。</summary>
        public void _DebugAnswerNow()
        {
            if (debugJoinLoopback <= 0 || _outTo < 0) return;
            HandleAnswer(debugAnswerCode);
        }

        private void HandleAnswer(int code)
        {
            int to = _outTo;
            Debug.Log("[KH_Join] answer from player " + to + " seq=" + _outSeq + " kind=" + _outKind + " phase=" + _outPhase + " code=" + code);
            if (_outKind == KindCall) { HandleCallAnswer(code); return; }
            if (_outKind == KindFilm) { HandleFilmAnswer(code); return; }   // 撮影: 表示と回の状態だけ (許可は相手の表。KH_TravelMailbox.Film.cs)
            // 合流: 返事待ちの間に始めた相手の速さの測定は、承認のときだけ続ける (_sampleId は残す)
            _outTo = -1; _outPhase = 0; _outKind = KindJoin;
            if (code != CodeYes)
            {
                _sampleId = -1;
                if (code == CodeNo) Deny(to);
                Post(code, to);
                return;
            }
            // 速さの測定が 2 回そろうまで待つ (速い承認でも「歩いている」を取りこぼさない。最大 1.5 秒)
            _acceptFor = to; _acceptAt = Time.time;
            Post(JoinMeasuring, to);
            if (_samples >= 2) FinishAccept();
        }

        /// <summary>呼ぶの返事 (§F.4)。準備中 (4) は待ちを続け、到着 (1)・中止 (5)・断り (0)・対応中 (2)・休止 (3) で終わる。</summary>
        private void HandleCallAnswer(int code)
        {
            int to = _outTo;
            if (code == CodePreparing)
            {
                if (_outPhase != 0) return;   // 重複・追い越しの 2 通目は捨てる
                _outPhase = 1; _outAt = Time.time;
                Post(CallComing, to);
                return;
            }
            if (code == CodeYes) { ResetOut(); Post(CallArrived, to); return; }
            if (code == CodeCallFailed) { ResetOut(); Post(CallNotComing, to); return; }   // 開始後の中止は 30 秒に入れない
            if (code == CodeNo) { ResetOut(); Deny(to); Post(CallNotComing, to); return; }
            if (code == CodeBusy || code == CodePaused) { ResetOut(); Post(code, to); return; }
        }

        private void ResetOut()
        {
            if (_outKind == KindFilm && _filmPhase == FilmPending) FilmClear();   // 撮影の返事待ちは保留枠と一緒に終わる (ended。同じ seq は開かない)
            _outTo = -1; _outPhase = 0; _outKind = KindJoin; _sampleId = -1;
        }

        private void FinishAccept()
        {
            int to = _acceptFor;
            _acceptFor = -1; _sampleId = -1;
            VRCPlayerApi target = VRCPlayerApi.GetPlayerById(to);
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(target) || !Utilities.IsValid(lp) || manager == null) { Post(JoinLeft, to); return; }
            Vector3 p = target.GetPosition();
            int reason = manager._TravelFindJoinLanding(p, target.GetRotation(), _tVel, lp.GetPosition());
            joinReason = reason;
            if (reason < 0) { Debug.Log("[KH_Join] no safe landing near player " + to); Post(JoinNoSafe, to); return; }
            Vector3 landing = manager._TravelLastLanding();
            int started = manager._TravelBeginJoin(landing, YawToward(landing, p));
            Debug.Log("[KH_Join] accepted by player " + to + " reason=" + reason + " landing=" + landing + " speed=" + _tVel.magnitude.ToString("F1") + " travel=" + started);
            if (started != KH_LeashManager.TravelStarted) { joinTravelCode = started; Post(JoinStartFailed, to); return; }
            _movingWith = to;
            Post(JoinStarted, to);
        }

        // ============================================================
        // 受け手 T
        // ============================================================

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void ReqJoin(int seq) // net: callable-ok: requester = CallingPlayer; only raises a local notice on the owner
        {
            if (!Networking.IsOwner(gameObject)) return;
            Receive(NetworkCalling.CallingPlayer, seq, KindJoin);
        }

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void ReqCall(int seq) // net: callable-ok: requester = CallingPlayer; only raises a local notice on the owner (the owner moves only by its own [go])
        {
            if (!Networking.IsOwner(gameObject)) return;
            Receive(NetworkCalling.CallingPlayer, seq, KindCall);
        }

        /// <summary>ハーネス専用: 人形 (debugIncomingFrom) からの要求を、ネットワークの入口と同じ処理へ入れる。ローカル専用。</summary>
        public void _DebugIncomingNow()
        {
            if (debugJoinLoopback <= 0 || !Networking.IsOwner(gameObject)) return;
            Receive(VRCPlayerApi.GetPlayerById(debugIncomingFrom), debugIncomingSeq, debugIncomingKind);
        }

        /// <summary>ハーネス専用: 人形からの取消 (seq = debugIncomingSeq)。ローカル専用。</summary>
        public void _DebugIncomingCancelNow()
        {
            if (debugJoinLoopback <= 0 || !Networking.IsOwner(gameObject)) return;
            ReceiveCancel(VRCPlayerApi.GetPlayerById(debugIncomingFrom), debugIncomingSeq);
        }

        /// <summary>合流・呼ぶの共通の受信 (§F.3)。照合 → 終わった seq の破棄 → 引き継ぎ・差し替え → 自動の返事 → 交差 → 保留。</summary>
        private void Receive(VRCPlayerApi caller, int seq, int kind)
        {
            if (!Utilities.IsValid(caller) || caller.isLocal) return;
            if (kind != KindJoin && kind != KindCall && kind != KindFilm) return;
            int from = caller.playerId;
            if (seq <= SeenSeq(from)) { Debug.Log("[KH_Join] request from player " + from + " seq=" + seq + " kind=" + kind + " dropped (seen)"); return; }
            MarkSeen(from, seq);
            if (_inFrom == from)
            {
                if (kind == _inKind)
                {
                    // 同じ相手・同じ行為への同意を続ける: seq だけ引き継ぐ (通知の置き直し・期限の延長はしない)
                    _inSeq = seq;
                    return;
                }
                // 撮影が絡む差し替えは、新しい種類の受付と枠を確かめる (§13.6: 合流の保留中に、撮影を受けていない相手へ撮影が来たら断る)。
                // 断るときは古い保留も閉じる (送り主は保留を 1 つしか持たないので、古い seq はもう使われない)
                int swap = FilmSwapCode(from, kind);
                if (swap >= 0)
                {
                    Debug.Log("[KH_Join] request from player " + from + " seq=" + seq + " kind=" + kind + " replaces kind " + _inKind + " but auto code=" + swap);
                    Reply(caller, seq, swap);
                    ClearIncoming();
                    return;
                }
                // 種類の差し替え: 古い操作対象を無効にし、新しい世代から受ける
                _inSeq = seq; _inKind = kind; _inAt = Time.time; NewGen(); _goPressed = false; _goFail = GoFailNone;
                if (kind == KindCall) StartCallSampling(caller); else _cId = -1;
                Debug.Log("[KH_Join] request from player " + from + " seq=" + seq + " kind=" + kind + " replaces the pending one (gen " + _inGen + ")");
                if (notice != null) notice._Show(this);
                return;
            }
            int code = -1;
            if ((kind == KindFilm ? !filmAccepting : !accepting) || Time.time < _pauseUntil) code = CodePaused;   // 受付の切り替えは種類ごと、休止は共通 (§13.6)
            else if (Blocked(from)) code = CodeNo;
            else if (ConfirmedWith(from)) code = CodeBusy;
            else if (kind == KindFilm && !FilmHasRoom(from)) code = CodeBusy;   // 許可の表 4 枠が他の人で埋まっている (§5.2・§13.5)
            else if (_inFrom >= 0) code = CodeBusy;
            else if (Time.time - _closedAt < NoticeGap) code = CodeBusy;
            else if (_outTo == from)
            {
                // 未確定同士の交差: playerId の小さい方が勝つ (§F.3)
                VRCPlayerApi lp = Networking.LocalPlayer;
                int me = Utilities.IsValid(lp) ? lp.playerId : 0;
                bool iWin = me < from;
                if (debugCrossInvert > 0) iWin = !iWin;
                if (iWin) code = CodeBusy;
                else YieldOutgoing();
            }
            if (code >= 0) { Debug.Log("[KH_Join] request from player " + from + " seq=" + seq + " kind=" + kind + " auto code=" + code); Reply(caller, seq, code); return; }
            _inFrom = from; _inSeq = seq; _inKind = kind; _inAt = Time.time; NewGen();
            _goPressed = false; _goFail = GoFailNone;
            if (kind == KindCall) StartCallSampling(caller); else _cId = -1;
            Debug.Log("[KH_Join] request from player " + from + " seq=" + seq + " kind=" + kind + " -> notice (gen " + _inGen + ")");
            if (notice != null) notice._Show(this);
        }

        /// <summary>送り主との移動が確定している (送り主の呼ぶで移動中・送り主への合流が承認済み・送り主から準備中を受けた呼ぶ)。</summary>
        private bool ConfirmedWith(int id)
        {
            if (_goFrom == id || _acceptFor == id || _movingWith == id) return true;
            return _outTo == id && _outKind == KindCall && _outPhase == 1;
        }

        /// <summary>交差で負けた: 自分の申し込みをその場で失効させる (30 秒に入れない)。</summary>
        private void YieldOutgoing()
        {
            int to = _outTo;
            bool film = _outKind == KindFilm;
            SendCancelTo(to);
            Debug.Log("[KH_Join] crossing with player " + to + ": my request seq=" + _outSeq + " yields");
            ResetOut();
            PostKind(film, CrossYield, to);
        }

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void CancelJoin(int seq) // net: callable-ok: only the pending requester (or the caller of an in-progress call move) can withdraw its own seq
        {
            if (!Networking.IsOwner(gameObject)) return;
            ReceiveCancel(NetworkCalling.CallingPlayer, seq);
        }

        private void ReceiveCancel(VRCPlayerApi caller, int seq)
        {
            if (!Utilities.IsValid(caller) || caller.isLocal) return;
            int from = caller.playerId;
            if (seq > SeenSeq(from)) MarkSeen(from, seq);   // この seq の要求が後から届いても復活させない
            FilmDropRow(from, seq);   // 撮影: 返事待ちのうちに取り消した申し込みに [いいよ] が行き違っていたら、その行 (送り主の行・同じ seq だけ) を消す
            if (from == _inFrom && seq == _inSeq)
            {
                Debug.Log("[KH_Join] request from player " + _inFrom + " withdrawn (seq " + seq + ")" + (_goPressed ? " while measuring after [go]" : ""));
                bool measuring = _goPressed;   // [行く] の後の測定待ちなら、取り消されたことを本人に出す (ClearIncoming が控えを消す前に)
                ClearIncoming();
                if (measuring) Post(CallCanceledBy, from);
                return;
            }
            if (from == _goFrom && seq == _goSeq)
            {
                // 取消が勝つのは、本人が移動する前にここで処理したときだけ (§F.3)
                if (manager != null && manager._TravelCancelable())
                {
                    Debug.Log("[KH_Join] call from player " + from + " withdrawn before the move; cancelling");
                    _goFrom = -1;
                    manager._TravelCancel();
                    Post(CallCanceledBy, from);
                }
            }
        }

        public int _IncomingFrom() { return _inFrom; }
        public int _IncomingKind() { return _inKind; }
        public int _IncomingGen() { return _inGen; }
        public int _GoFail() { return _goFail; }
        public bool _GoPressed() { return _goPressed; }
        public float _IncomingLeft() { return _inFrom >= 0 ? Mathf.Max(0f, AnswerSeconds - (Time.time - _inAt)) : 0f; }

        /// <summary>通知板の [いいよ] / [いまはだめ] (合流だけ)。gen は通知板が描いた世代。ローカル専用。</summary>
        public void _AnswerYesGen(int gen) { if (Accepts(gen, KindJoin)) AnswerIncoming(CodeYes); }
        public void _AnswerNoGen(int gen) { if (Accepts(gen, KindJoin)) AnswerIncoming(CodeNo); }
        /// <summary>通知板の [行く] / [今は行かない] (呼ぶだけ)。ローカル専用。</summary>
        public void _AnswerGoGen(int gen) { if (Accepts(gen, KindCall)) StartGo(); }
        public void _AnswerNotNowGen(int gen) { if (Accepts(gen, KindCall) && !_goPressed) AnswerIncoming(CodeNo); }
        // 今の世代への押下 (互換の入口。通知板は世代つきを使う)
        public void _AnswerYes() { _AnswerYesGen(_inGen); }
        public void _AnswerNo() { _AnswerNoGen(_inGen); }
        public void _AnswerGo() { _AnswerGoGen(_inGen); }
        public void _AnswerNotNow() { _AnswerNotNowGen(_inGen); }
        /// <summary>[しばらく受けない (10 分)]。今の申し込みには code 3。保存しない。ローカル専用。</summary>
        public void _PauseIncoming() { if (_goPressed) return; _pauseUntil = Time.time + PauseSeconds; AnswerIncoming(CodePaused); }
        /// <summary>[このインスタンスにいる間は、この人から受けない]。今の申し込みには code 0。保存しない。ローカル専用。</summary>
        public void _BlockIncoming()
        {
            if (_inFrom < 0 || _goPressed) return;
            Block(_inFrom);
            AnswerIncoming(CodeNo);
        }

        /// <summary>押下が今の保留に向いているか: 世代・種類が一致し、世代が変わってから 0.5 秒たった (§F.2)。</summary>
        private bool Accepts(int gen, int kind)
        {
            if (!Networking.IsOwner(gameObject) || _inFrom < 0) return false;
            if (gen != _inGen || kind != _inKind) { Debug.Log("[KH_Join] press ignored: gen " + gen + "/" + _inGen + " kind " + kind + "/" + _inKind); return false; }
            if (Time.time - _inGenAt < InputGuard) { Debug.Log("[KH_Join] press ignored: within " + InputGuard + " s of the change"); return false; }
            return true;
        }

        private void NewGen() { _inGen++; _inGenAt = Time.time; }

        private void AnswerIncoming(int code)
        {
            if (!Networking.IsOwner(gameObject) || _inFrom < 0) return;
            VRCPlayerApi from = VRCPlayerApi.GetPlayerById(_inFrom);
            if (Utilities.IsValid(from)) Reply(from, _inSeq, code);
            Debug.Log("[KH_Join] answered player " + _inFrom + " seq=" + _inSeq + " kind=" + _inKind + " code=" + code);
            ClearIncoming();
        }

        private void ClearIncoming()
        {
            _inFrom = -1; _closedAt = Time.time; _goPressed = false; _cId = -1;
            if (notice != null) notice._Hide();
        }

        // ---- 呼ばれた人の [行く] (§F.4) ----

        private void StartGo()
        {
            if (_goPressed) return;   // 二重押下
            if (_acceptFor >= 0 || _movingWith >= 0 || _goFrom >= 0) { _goFail = GoFailMoving; return; }
            _goPressed = true; _goPressAt = Time.time; _goFail = GoFailNone;
            Debug.Log("[KH_Join] go pressed for player " + _inFrom + " seq=" + _inSeq + " samples=" + _cSamples);
            if (_cSamples >= 2) FinishGo();
        }

        private void FinishGo()
        {
            _goPressed = false;
            VRCPlayerApi r = VRCPlayerApi.GetPlayerById(_inFrom);
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(r) || !Utilities.IsValid(lp) || manager == null) { ClearIncoming(); return; }
            Vector3 p = r.GetPosition();
            int reason = manager._TravelFindJoinLanding(p, r.GetRotation(), _cVel, lp.GetPosition());
            if (reason < 0) { _goFail = GoFailNoSafe; Debug.Log("[KH_Join] go: no safe landing near player " + _inFrom); return; }
            Vector3 landing = manager._TravelLastLanding();
            int before = manager.travelResultSerial;
            int started = manager._TravelBeginJoin(landing, YawToward(landing, p));
            if (started != KH_LeashManager.TravelStarted) { _goFail = started; Debug.Log("[KH_Join] go: could not start (travel=" + started + ")"); return; }
            int from = _inFrom, seq = _inSeq;
            // 自分の申し込み (別の相手への返事待ち) は、移動を始めたここで取り消す
            if (_outTo >= 0)
            {
                int outTo = _outTo; bool outFilm = _outKind == KindFilm;
                SendCancelTo(_outTo); Debug.Log("[KH_Join] go: my request to player " + _outTo + " withdrawn"); ResetOut();
                if (outFilm) PostFilm(JoinCanceled, outTo);   // 撮影の返事待ちも取り消した (撮影カメラの表示用)
            }
            joinReason = reason;
            _goFrom = from; _goSeq = seq; _goSerial = before;
            _inFrom = -1; _closedAt = Time.time; _cId = -1;
            if (notice != null) notice._Hide();
            Reply(r, seq, CodePreparing);
            Debug.Log("[KH_Join] go: started toward player " + from + " seq=" + seq + " reason=" + reason + " landing=" + landing + " speed=" + _cVel.magnitude.ToString("F1"));
            Post(JoinStarted, from);
            CheckGo();   // 暗転の板が無い・犬がいないときは、ここで移動が終わっている
        }

        /// <summary>呼ばれて移動中: Manager の結果 (本人の移動 = 到着、移動の前の中止 = 5) を 1 回だけ R へ返す。</summary>
        private void CheckGo()
        {
            if (_goFrom < 0 || manager == null) return;
            if (manager.travelResultSerial == _goSerial)
            {
                if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(_goFrom)))
                {
                    int gone = _goFrom; _goFrom = -1;
                    if (manager._TravelCancelable()) { Debug.Log("[KH_Join] caller " + gone + " left before the move; cancelling"); manager._TravelCancel(); }
                    Post(JoinLeft, gone);
                }
                return;
            }
            int code = manager.travelResultCode;
            int to = _goFrom; _goFrom = -1;
            bool moved = code == KH_LeashManager.TravelWaitingDogs || code == KH_LeashManager.TravelArrived;
            VRCPlayerApi r = VRCPlayerApi.GetPlayerById(to);
            if (Utilities.IsValid(r)) Reply(r, _goSeq, moved ? CodeYes : CodeCallFailed);
            Debug.Log("[KH_Join] go: " + (moved ? "moved" : "stopped before the move (travel=" + code + ")") + " for player " + to + " seq=" + _goSeq);
        }

        private void Reply(VRCPlayerApi to, int seq, int code)
        {
            if (debugJoinLoopback > 0)
            {
                debugReplyCount++; debugLastReplySeq = seq; debugLastReplyCode = code;
                return;
            }
            KH_TravelMailbox box = _FindBox(to);
            if (box != null) box.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(AnsJoin), seq, code);
        }

        private bool Blocked(int id)
        {
            for (int i = 0; i < _blocked.Length; i++) if (_blocked[i] == id) return true;
            return false;
        }

        /// <summary>在室中の個別拒否 (RC-07)。同じ人 → 空き枠 → 退室した人の枠の順に使い、在室中の人の記録は上書きしない。無ければ伸ばす。</summary>
        private void Block(int id)
        {
            int slot = -1;
            for (int i = 0; i < _blocked.Length; i++) if (_blocked[i] == id) return;
            for (int i = 0; i < _blocked.Length && slot < 0; i++) if (_blocked[i] < 0) slot = i;
            for (int i = 0; i < _blocked.Length && slot < 0; i++) if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(_blocked[i]))) slot = i;
            if (slot < 0)
            {
                int n = _blocked.Length;
                int[] grown = new int[n + HistoryGrow];
                for (int i = 0; i < grown.Length; i++) grown[i] = i < n ? _blocked[i] : -1;
                _blocked = grown; slot = n;
            }
            _blocked[slot] = id;
        }

        /// <summary>断り・時間切れの 30 秒 (RC-07)。同じ人 → 空き枠 → 期限切れか退室した人の枠の順に使う。無ければ伸ばす。</summary>
        private void Deny(int id)
        {
            for (int i = 0; i < _deniedIds.Length; i++) if (_deniedIds[i] == id) { _deniedAts[i] = Time.time; return; }
            int slot = -1;
            for (int i = 0; i < _deniedIds.Length && slot < 0; i++) if (_deniedIds[i] < 0) slot = i;
            for (int i = 0; i < _deniedIds.Length && slot < 0; i++)
                if (Time.time - _deniedAts[i] >= SameTargetGap || !Utilities.IsValid(VRCPlayerApi.GetPlayerById(_deniedIds[i]))) slot = i;
            if (slot < 0)
            {
                int n = _deniedIds.Length;
                int[] ids = new int[n + HistoryGrow]; float[] ats = new float[n + HistoryGrow];
                for (int i = 0; i < ids.Length; i++) { ids[i] = i < n ? _deniedIds[i] : -1; ats[i] = i < n ? _deniedAts[i] : 0f; }
                _deniedIds = ids; _deniedAts = ats; slot = n;
            }
            _deniedIds[slot] = id; _deniedAts[slot] = Time.time;
        }

        /// <summary>送り主ごとの見た最大の seq (無ければ 0。R の seq は 1 から)。</summary>
        private int SeenSeq(int id)
        {
            for (int i = 0; i < _seenIds.Length; i++) if (_seenIds[i] == id) return _seenSeqs[i];
            return 0;
        }

        /// <summary>見た seq を進める。同じ人 → 空き枠 → 退室した人の枠の順に使い、無ければ伸ばす (RC-07 と同じ作法)。</summary>
        private void MarkSeen(int id, int seq)
        {
            for (int i = 0; i < _seenIds.Length; i++) if (_seenIds[i] == id) { if (seq > _seenSeqs[i]) _seenSeqs[i] = seq; return; }
            int slot = -1;
            for (int i = 0; i < _seenIds.Length && slot < 0; i++) if (_seenIds[i] < 0) slot = i;
            for (int i = 0; i < _seenIds.Length && slot < 0; i++) if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(_seenIds[i]))) slot = i;
            if (slot < 0)
            {
                int n = _seenIds.Length;
                int[] ids = new int[n + HistoryGrow]; int[] seqs = new int[n + HistoryGrow];
                for (int i = 0; i < ids.Length; i++) { ids[i] = i < n ? _seenIds[i] : -1; seqs[i] = i < n ? _seenSeqs[i] : 0; }
                _seenIds = ids; _seenSeqs = seqs; slot = n;
            }
            _seenIds[slot] = id; _seenSeqs[slot] = seq;
        }

        /// <summary>退室した人の枠を空ける (RC-07)。</summary>
        private void ForgetHistory(int id)
        {
            for (int i = 0; i < _blocked.Length; i++) if (_blocked[i] == id) _blocked[i] = -1;
            for (int i = 0; i < _deniedIds.Length; i++) if (_deniedIds[i] == id) _deniedIds[i] = -1;
            for (int i = 0; i < _seenIds.Length; i++) if (_seenIds[i] == id) { _seenIds[i] = -1; _seenSeqs[i] = 0; }
        }

        private bool DeniedRecently(int id)
        {
            for (int i = 0; i < _deniedIds.Length; i++) if (_deniedIds[i] == id && Time.time - _deniedAts[i] < SameTargetGap) return true;
            return false;
        }

        // ============================================================
        // 受け付けの ON/OFF (同期 1 本。PlayerData に保存)
        // ============================================================

        /// <summary>「合流・呼び出しを受ける」の切替。新規の受け付けだけ止める (今の保留は閉じない)。ローカル専用。</summary>
        public void _ToggleAccepting()
        {
            if (!Networking.IsOwner(gameObject)) return;
            accepting = !accepting; _chosen = true;
            RequestSerialization();
            if (_restored) PlayerData.SetBool(SaveKey, accepting);
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal || !Networking.IsOwner(gameObject)) return;
            _restored = true;
            FilmRestored(player);   // 撮影の受け付け (別のキー KH_TravelJoin.filmAccepting)。合流の分は下で今までどおり
            if (_chosen) { PlayerData.SetBool(SaveKey, accepting); return; }
            bool saved = true;
            if (PlayerData.TryGetBool(player, SaveKey, out saved) && saved != accepting) { accepting = saved; RequestSerialization(); }
        }

        // ============================================================
        // 共通
        // ============================================================

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(player)) return;
            int id = player.playerId;
            if (id == _outTo) { bool film = _outKind == KindFilm; ResetOut(); PostKind(film, JoinLeft, id); }
            FilmPlayerLeft(id);   // 撮影: その人の許可の行 (被写体の側)・その人との撮影の回 (撮る人の側) をはっきり消す (§13.5)
            if (id == _acceptFor) { _acceptFor = -1; _sampleId = -1; Post(JoinLeft, id); }
            if (id == _movingWith) TargetLeftWhileMoving();
            if (id == _inFrom) ClearIncoming();
            if (id == _goFrom)
            {
                _goFrom = -1;
                if (manager != null && manager._TravelCancelable()) { Debug.Log("[KH_Join] caller " + id + " left before the move; cancelling"); manager._TravelCancel(); }
                Post(JoinLeft, id);
            }
            ForgetHistory(id);
        }

        /// <summary>§D.4: 承認の後、切符待ち・暗転中に相手が退出したら取りやめる (明るく戻して、その場に残る)。移動した後なら何もしない。</summary>
        private void TargetLeftWhileMoving()
        {
            int to = _movingWith;
            _movingWith = -1;
            if (manager != null && manager._TravelCancelable())
            {
                Debug.Log("[KH_Join] target " + to + " left before the move; cancelling");
                manager._TravelCancel();
                Post(JoinLeft, to);
            }
        }

        void Update()
        {
            if (_outTo < 0 && _inFrom < 0 && _sampleId < 0 && _acceptFor < 0 && _movingWith < 0 && _goFrom < 0 && _cId < 0 && _filmPhase == FilmNone && _filmRows == 0) return;
            if (!Networking.IsOwner(gameObject)) return;
            FilmUpdate();   // 撮影の回 (4 Hz) と許可の表の掃除 (10 秒ごと)。撮影に関わっていなければ上の行で戻る
            // 相手の速さ (0.5 秒ごとの位置の差)。§D.3 の「歩いている」の判定に使う
            if (_sampleId >= 0 && Time.time - _tPrevAt >= SampleEvery)
            {
                VRCPlayerApi st = VRCPlayerApi.GetPlayerById(_sampleId);
                if (Utilities.IsValid(st))
                {
                    Vector3 now = st.GetPosition();
                    _tVel = (now - _tPrev) / Mathf.Max(0.001f, Time.time - _tPrevAt);
                    _tPrev = now; _tPrevAt = Time.time; _samples++;
                }
            }
            // 呼んだ人 R の速さ (受け手の保留中)
            if (_cId >= 0 && Time.time - _cPrevAt >= SampleEvery)
            {
                VRCPlayerApi ct = VRCPlayerApi.GetPlayerById(_cId);
                if (Utilities.IsValid(ct))
                {
                    Vector3 now = ct.GetPosition();
                    _cVel = (now - _cPrev) / Mathf.Max(0.001f, Time.time - _cPrevAt);
                    _cPrev = now; _cPrevAt = Time.time; _cSamples++;
                }
            }
            if (_acceptFor >= 0)
            {
                if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(_acceptFor))) { int to = _acceptFor; _acceptFor = -1; _sampleId = -1; Post(JoinLeft, to); }
                else if (_samples >= 2 || Time.time - _acceptAt > MeasureCap) FinishAccept();
            }
            if (_movingWith >= 0)
            {
                if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(_movingWith))) TargetLeftWhileMoving();
                else if (manager == null || !manager._TravelCancelable()) _movingWith = -1;   // 移動した (または取りやめた)
            }
            if (_goFrom >= 0) CheckGo();
            if (_outTo >= 0)
            {
                if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(_outTo))) { int to = _outTo; bool film = _outKind == KindFilm; ResetOut(); PostKind(film, JoinLeft, to); }
                else if (_outKind == KindJoin && Time.time - _outAt > AnswerSeconds + 2f)
                {
                    // 合流の時間切れは断りと同じ扱い (code 0)。相手側の保留も取り消しておく
                    SendCancelTo(_outTo);
                    int to = _outTo; ResetOut();
                    Deny(to);
                    Debug.Log("[KH_Join] request to player " + to + " timed out");
                    Post(CodeNo, to);
                }
                else if (_outKind == KindCall && Time.time - _outAt > (_outPhase == 1 ? ArriveSeconds : AnswerSeconds + 2f))
                {
                    // 呼ぶの無応答は断りと区別する (30 秒に入れない。§F.4)
                    SendCancelTo(_outTo);
                    int to = _outTo; int phase = _outPhase; ResetOut();
                    Debug.Log("[KH_Join] call to player " + to + " unconfirmed (phase " + phase + ")");
                    Post(CallUnknown, to);
                }
                else if (_outKind == KindFilm && Time.time - _outAt > AnswerSeconds + 2f)
                {
                    // 撮影の時間切れ (22 s。§13.2): 断りと同じ扱い (同じ相手へ 30 秒)。相手側の保留も取り消す。回は ResetOut が終える
                    SendCancelTo(_outTo);
                    int to = _outTo; ResetOut();
                    Deny(to);
                    Debug.Log("[KH_Join] film request to player " + to + " timed out");
                    PostFilm(CodeNo, to);
                }
            }
            if (_inFrom >= 0)
            {
                if (_goPressed)
                {
                    if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(_inFrom))) ClearIncoming();
                    else if (_cSamples >= 2 || Time.time - _goPressAt > MeasureCap) FinishGo();
                }
                else if (Time.time - _inAt > AnswerSeconds) AnswerIncoming(CodeNo);
            }
        }

        private void StartSampling(VRCPlayerApi target)
        {
            _sampleId = target.playerId; _samples = 0;
            _tPrev = target.GetPosition(); _tPrevAt = Time.time; _tVel = Vector3.zero;
        }

        private void StartCallSampling(VRCPlayerApi caller)
        {
            _cId = caller.playerId; _cSamples = 0;
            _cPrev = caller.GetPosition(); _cPrevAt = Time.time; _cVel = Vector3.zero;
        }

        private float YawToward(Vector3 from, Vector3 to)
        {
            Vector3 face = to - from; face.y = 0f;
            return face.sqrMagnitude > 0.0001f ? Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg : 0f;
        }

        private void SendCancelTo(int id)
        {
            if (debugJoinLoopback > 0) return;
            VRCPlayerApi target = VRCPlayerApi.GetPlayerById(id);
            if (!Utilities.IsValid(target)) return;
            KH_TravelMailbox box = _FindBox(target);
            if (box != null) box.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(CancelJoin), _outSeq);
        }

        private void Post(int code, int target)
        {
            joinResultCode = code; joinResultTarget = target; joinResultSerial++;
        }

        /// <summary>結果を種類で分ける (§13.6): 撮影は filmResult* (撮影カメラが読む)、合流・呼ぶは joinResult* (手元メニューが読む)。</summary>
        private void PostKind(bool film, int code, int target)
        {
            if (film) PostFilm(code, target); else Post(code, target);
        }

        /// <summary>その人の受付箱。入室直後はまだ作られていないことがある (null = 準備中)。呼ぶのは入退室・未取得のときだけにする。</summary>
        public KH_TravelMailbox _FindBox(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return null;
            GameObject[] objs = Networking.GetPlayerObjects(player);
            if (objs == null) return null;
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] == null) continue;
                KH_TravelMailbox box = objs[i].GetComponent<KH_TravelMailbox>();
                if (box != null) return box;
            }
            return null;
        }
    }
}
