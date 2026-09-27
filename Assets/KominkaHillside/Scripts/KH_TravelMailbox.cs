using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    /// <summary>IMP-WLD-001 段階 3: 合流の受付箱 (設計書 §A.4・§A.7・§B.5・§D.4・§D.5)。
    ///
    /// `VRCPlayerObject` に載せる (入室ごとに複製され、本人が Owner)。同期は `accepting` 1 本だけ (Manual)。
    /// 1 つの箱が本人の 2 つの役割を持つ (どちらも **Owner の端末でだけ** 動く):
    /// - **受け手 T**: `ReqJoin(seq)` を受け、通知板 (KH_TravelNotice) を出す。答えは申し込んだ人 R の箱へ `AnsJoin(seq, code)`。
    ///   保留は 1 件。前の通知を閉じてから 20 秒は code 2、[しばらく受けない] で 10 分は code 3、「この人からは受けない」は黙って code 0。
    /// - **申し込む人 R**: `_RequestJoin(相手)` で相手の箱へ `ReqJoin`。`AnsJoin` は送り主 == 相手 かつ seq が保留中のものと一致するときだけ採る。
    ///   承認なら着地点を §D.3 で決めて、地点ワープと同じ切符・暗転 (KH_LeashManager._TravelBeginJoin) で自分が移動する。
    /// code: 0 断り・時間切れ / 1 承認 / 2 対応中 / 3 休止中。受け付けの ON/OFF (`accepting`) は PlayerData に保存する (ナイトモードと同じ作法)。
    /// 相手を動かす機能は無い。フレンド判定は使わない。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_TravelMailbox : UdonSharpBehaviour
    {
        [UdonSynced] public bool accepting = true;

        [Header("Wiring (Tools/KominkaHillside/Course/85 writes these on the template)")]
        public KH_LeashManager manager;
        public KH_TravelNotice notice;

        [Header("Harness hook: シーンでは必ず 0 (Course/81 とビルドの門番が止める)")]
        [Tooltip(">0 なら相手の箱を使わずに待ち状態へ入り、_DebugAnswerNow で答えを模す (ClientSim の人形は Udon を動かさない)")]
        public int debugJoinLoopback;
        public int debugAnswerCode;

        public const string SaveKey = "KH_TravelJoin.accepting";
        public const int CodeNo = 0, CodeYes = 1, CodeBusy = 2, CodePaused = 3;
        // 申し込む側の結果 (手元メニューが文言に変える)
        public const int JoinWaiting = 10, JoinNotReady = 11, JoinLeft = 13, JoinCanceled = 14, JoinNoSafe = 15,
            JoinStarted = 16, JoinAgain = 17, JoinBusyMe = 18, JoinNotAccepting = 19;
        public const float AnswerSeconds = 20f, NoticeGap = 20f, PauseSeconds = 600f, SameTargetGap = 30f;

        // ---- 受け手 (Owner のローカル) ----
        private int _inFrom = -1, _inSeq;
        private float _inAt, _closedAt = -1000f, _pauseUntil;
        private int[] _blocked = new int[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1};
        private int _blockedAt;
        // ---- 申し込む人 (Owner のローカル) ----
        private int _outTo = -1, _outSeq;
        private float _outAt;
        private int _deniedTo = -1;
        private float _deniedAt = -1000f;
        private Vector3 _tPrev, _tVel;
        private float _tPrevAt;
        [HideInInspector] public int joinResultSerial, joinResultCode, joinResultTarget = -1, joinReason = -1;
        private bool _restored, _chosen;

        // ============================================================
        // 申し込む人 R
        // ============================================================

        /// <summary>相手へ合流を申し込む。戻り値は結果コード (JoinWaiting なら返事待ち)。ローカル専用。</summary>
        public int _RequestJoin(int targetId)
        {
            if (!Networking.IsOwner(gameObject)) return JoinNotReady;
            if (_outTo >= 0) return JoinBusyMe;
            if (manager != null && (manager._TravelWaiting() || manager._TravelFading())) return JoinBusyMe;
            VRCPlayerApi target = VRCPlayerApi.GetPlayerById(targetId);
            if (!Utilities.IsValid(target) || target.isLocal) return JoinLeft;
            if (_deniedTo == targetId && Time.time - _deniedAt < SameTargetGap) return JoinAgain;
            _outSeq++;
            if (debugJoinLoopback <= 0)
            {
                KH_TravelMailbox box = _FindBox(target);
                if (box == null) return JoinNotReady;
                if (!box.accepting) return JoinNotAccepting;
                box.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqJoin), _outSeq);
            }
            _outTo = targetId; _outAt = Time.time;
            _tPrev = target.GetPosition(); _tPrevAt = Time.time; _tVel = Vector3.zero;
            Debug.Log("[KH_Join] request to player " + targetId + " seq=" + _outSeq + (debugJoinLoopback > 0 ? " (loopback)" : ""));
            Post(JoinWaiting, targetId);
            return JoinWaiting;
        }

        /// <summary>申し込みを取り消す。ローカル専用。</summary>
        public void _CancelJoinRequest()
        {
            if (_outTo < 0) return;
            VRCPlayerApi target = VRCPlayerApi.GetPlayerById(_outTo);
            if (debugJoinLoopback <= 0 && Utilities.IsValid(target))
            {
                KH_TravelMailbox box = _FindBox(target);
                if (box != null) box.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(CancelJoin), _outSeq);
            }
            Debug.Log("[KH_Join] cancel to player " + _outTo + " seq=" + _outSeq);
            int to = _outTo; _outTo = -1;
            Post(JoinCanceled, to);
        }

        public int _WaitingFor() { return _outTo; }
        public float _WaitLeft() { return _outTo >= 0 ? Mathf.Max(0f, AnswerSeconds - (Time.time - _outAt)) : 0f; }

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void AnsJoin(int seq, int code) // net: callable-ok: sender must be the pending target and seq the pending one
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller) || caller.playerId != _outTo || seq != _outSeq) return;
            HandleAnswer(code);
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
            _outTo = -1;
            Debug.Log("[KH_Join] answer from player " + to + " seq=" + _outSeq + " code=" + code);
            if (code != CodeYes)
            {
                if (code == CodeNo) { _deniedTo = to; _deniedAt = Time.time; }
                Post(code, to);
                return;
            }
            VRCPlayerApi target = VRCPlayerApi.GetPlayerById(to);
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(target) || !Utilities.IsValid(lp) || manager == null) { Post(JoinLeft, to); return; }
            Vector3 p = target.GetPosition();
            int reason = manager._TravelFindJoinLanding(p, target.GetRotation(), _tVel, lp.GetPosition());
            joinReason = reason;
            if (reason < 0) { Debug.Log("[KH_Join] no safe landing near player " + to); Post(JoinNoSafe, to); return; }
            Vector3 landing = manager._TravelLastLanding();
            Vector3 face = p - landing; face.y = 0f;
            float yaw = face.sqrMagnitude > 0.0001f ? Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg : 0f;
            int started = manager._TravelBeginJoin(landing, yaw);
            Debug.Log("[KH_Join] accepted by player " + to + " reason=" + reason + " landing=" + landing + " travel=" + started);
            Post(JoinStarted, to);
        }

        // ============================================================
        // 受け手 T
        // ============================================================

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void ReqJoin(int seq) // net: callable-ok: requester = CallingPlayer; only raises a local notice on the owner
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller) || caller.isLocal) return;
            int code = -1;
            if (!accepting || Time.time < _pauseUntil) code = CodePaused;
            else if (Blocked(caller.playerId)) code = CodeNo;
            else if (_inFrom >= 0 && _inFrom != caller.playerId) code = CodeBusy;
            else if (_inFrom < 0 && Time.time - _closedAt < NoticeGap) code = CodeBusy;
            if (code >= 0) { Debug.Log("[KH_Join] request from player " + caller.playerId + " auto code=" + code); Reply(caller, seq, code); return; }
            _inFrom = caller.playerId; _inSeq = seq; _inAt = Time.time;
            Debug.Log("[KH_Join] request from player " + caller.playerId + " seq=" + seq + " -> notice");
            if (notice != null) notice._Show(this);
        }

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void CancelJoin(int seq) // net: callable-ok: only the pending requester can withdraw its own seq
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller) || caller.playerId != _inFrom || seq != _inSeq) return;
            Debug.Log("[KH_Join] request from player " + _inFrom + " withdrawn");
            ClearIncoming();
        }

        public int _IncomingFrom() { return _inFrom; }
        public float _IncomingLeft() { return _inFrom >= 0 ? Mathf.Max(0f, AnswerSeconds - (Time.time - _inAt)) : 0f; }

        /// <summary>通知板の [いいよ] / [いまはだめ]。ローカル専用。</summary>
        public void _AnswerYes() { AnswerIncoming(CodeYes); }
        public void _AnswerNo() { AnswerIncoming(CodeNo); }
        /// <summary>[しばらく受けない (10 分)]。今の申し込みには code 3。保存しない。ローカル専用。</summary>
        public void _PauseIncoming() { _pauseUntil = Time.time + PauseSeconds; AnswerIncoming(CodePaused); }
        /// <summary>[この人からはこの滞在中は受けない]。今の申し込みには code 0。保存しない。ローカル専用。</summary>
        public void _BlockIncoming()
        {
            if (_inFrom < 0) return;
            _blocked[_blockedAt] = _inFrom; _blockedAt = (_blockedAt + 1) % _blocked.Length;
            AnswerIncoming(CodeNo);
        }

        private void AnswerIncoming(int code)
        {
            if (!Networking.IsOwner(gameObject) || _inFrom < 0) return;
            VRCPlayerApi from = VRCPlayerApi.GetPlayerById(_inFrom);
            if (Utilities.IsValid(from)) Reply(from, _inSeq, code);
            Debug.Log("[KH_Join] answered player " + _inFrom + " code=" + code);
            ClearIncoming();
        }

        private void ClearIncoming()
        {
            _inFrom = -1; _closedAt = Time.time;
            if (notice != null) notice._Hide();
        }

        private void Reply(VRCPlayerApi to, int seq, int code)
        {
            KH_TravelMailbox box = _FindBox(to);
            if (box != null) box.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(AnsJoin), seq, code);
        }

        private bool Blocked(int id)
        {
            for (int i = 0; i < _blocked.Length; i++) if (_blocked[i] == id) return true;
            return false;
        }

        // ============================================================
        // 受け付けの ON/OFF (同期 1 本。PlayerData に保存)
        // ============================================================

        /// <summary>「合流の申し込みを受ける」の切替。ローカル専用。</summary>
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
            if (player.playerId == _outTo) { int to = _outTo; _outTo = -1; Post(JoinLeft, to); }
            if (player.playerId == _inFrom) ClearIncoming();
        }

        void Update()
        {
            if (_outTo < 0 && _inFrom < 0) return;
            if (!Networking.IsOwner(gameObject)) return;
            if (_outTo >= 0)
            {
                VRCPlayerApi target = VRCPlayerApi.GetPlayerById(_outTo);
                if (!Utilities.IsValid(target)) { int to = _outTo; _outTo = -1; Post(JoinLeft, to); }
                else
                {
                    // 相手の速さ (0.5 秒ごとの位置の差)。§D.3 の「歩いている」の判定に使う
                    if (Time.time - _tPrevAt >= 0.5f)
                    {
                        Vector3 now = target.GetPosition();
                        _tVel = (now - _tPrev) / Mathf.Max(0.001f, Time.time - _tPrevAt);
                        _tPrev = now; _tPrevAt = Time.time;
                    }
                    if (Time.time - _outAt > AnswerSeconds + 2f)
                    {
                        // 時間切れは断りと同じ扱い (code 0)。相手側の保留も取り消しておく
                        _CancelJoinRequestSilently();
                    }
                }
            }
            if (_inFrom >= 0 && Time.time - _inAt > AnswerSeconds) AnswerIncoming(CodeNo);
        }

        private void _CancelJoinRequestSilently()
        {
            VRCPlayerApi target = VRCPlayerApi.GetPlayerById(_outTo);
            if (debugJoinLoopback <= 0 && Utilities.IsValid(target))
            {
                KH_TravelMailbox box = _FindBox(target);
                if (box != null) box.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(CancelJoin), _outSeq);
            }
            int to = _outTo; _outTo = -1;
            _deniedTo = to; _deniedAt = Time.time;
            Debug.Log("[KH_Join] request to player " + to + " timed out");
            Post(CodeNo, to);
        }

        private void Post(int code, int target)
        {
            joinResultCode = code; joinResultTarget = target; joinResultSerial++;
        }

        /// <summary>その人の受付箱。入室直後はまだ作られていないことがある (null = 準備中)。</summary>
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
