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
    ///   保留は 1 件。同じ送り主の再送は冪等 (通知の置き直し・期限の延長をしない)。前の通知を閉じてから 20 秒は code 2、
    ///   [しばらく受けない] で 10 分は code 3、「このインスタンスにいる間は受けない」は黙って code 0。
    /// - **申し込む人 R**: `_RequestJoin(相手)` で相手の箱へ `ReqJoin`。`AnsJoin` は送り主 == 相手 かつ seq が保留中のものと一致するときだけ採る。
    ///   承認なら、相手の速さの測定が 2 回そろうのを待って (§D.3 の「歩いている」) 着地点を決め、地点ワープと同じ切符・暗転
    ///   (KH_LeashManager._TravelBeginJoin) で自分が移動する。移動が終わるまで相手を覚え、途中で相手が退出したら取りやめる (§D.4)。
    ///   断り・時間切れの後、同じ相手へは 30 秒あける (相手ごとに覚える)。
    /// code: 0 断り・時間切れ / 1 承認 / 2 対応中 / 3 休止中。受け付けの ON/OFF (`accepting`) は PlayerData に保存する (ナイトモードと同じ作法)。
    /// 相手を動かす機能は無い。フレンド判定は使わない。
    /// 破壊監査 2026-09-28 RC-07: 「この人からは受けない」と「同じ相手へ 30 秒」の履歴は固定長の循環上書きで、在室中の相手の記録を
    /// 押し出していた (17 人目の拒否で最初の人の拒否が消える・9 人に断られると最初の相手の 30 秒が消える)。今は、同じ人の枠 → 空き枠 →
    /// 退室した人の枠 (30 秒の方は期限切れの枠も) の順に使い、それでも無ければ配列を 16 枠ずつ伸ばす。退室した人の枠は OnPlayerLeft でも空ける
    /// (playerId はインスタンス内で使い回されない。再入室した人を同じ人として覚える仕組みは入れない)。
    /// RC-06: 取消できるかは _JoinCancelable() 1 か所で決め、手元メニューの表示と押したときの処理の両方がそれを使う。</summary>
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
        public const int JoinWaiting = 10, JoinNotReady = 11, JoinMeasuring = 12, JoinLeft = 13, JoinCanceled = 14, JoinNoSafe = 15,
            JoinStarted = 16, JoinAgain = 17, JoinBusyMe = 18, JoinNotAccepting = 19, JoinStartFailed = 20;
        public const float AnswerSeconds = 20f, NoticeGap = 20f, PauseSeconds = 600f, SameTargetGap = 30f;
        const float SampleEvery = 0.5f, MeasureCap = 1.5f;

        // ---- 受け手 (Owner のローカル) ----
        private int _inFrom = -1, _inSeq;
        private float _inAt, _closedAt = -1000f, _pauseUntil;
        private int[] _blocked = new int[] {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1};   // 足りなければ 16 枠ずつ伸ばす
        // ---- 申し込む人 (Owner のローカル) ----
        private int _outTo = -1, _outSeq;
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
        public int _RequestJoin(int targetId)
        {
            if (!Networking.IsOwner(gameObject)) return JoinNotReady;
            if (_outTo >= 0 || _acceptFor >= 0 || _movingWith >= 0) return JoinBusyMe;
            if (manager != null && (manager._TravelWaiting() || manager._TravelFading())) return JoinBusyMe;
            VRCPlayerApi target = VRCPlayerApi.GetPlayerById(targetId);
            if (!Utilities.IsValid(target) || target.isLocal) return JoinLeft;
            if (DeniedRecently(targetId)) return JoinAgain;
            if (debugJoinLoopback <= 0)
            {
                KH_TravelMailbox box = _FindBox(target);
                if (box == null) return JoinNotReady;
                if (!box.accepting) return JoinNotAccepting;
                _outSeq++;
                box.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqJoin), _outSeq);
            }
            else _outSeq++;
            _outTo = targetId; _outAt = Time.time;
            StartSampling(target);
            Debug.Log("[KH_Join] request to player " + targetId + " seq=" + _outSeq + (debugJoinLoopback > 0 ? " (loopback)" : ""));
            Post(JoinWaiting, targetId);
            return JoinWaiting;
        }

        /// <summary>申し込み・承認後の移動を取り消す。返事待ちなら CancelJoin を送り、承認後 (切符待ち・暗転中) なら移動を取りやめる。ローカル専用。</summary>
        public void _CancelJoinRequest()
        {
            if (_outTo >= 0)
            {
                SendCancelTo(_outTo);
                Debug.Log("[KH_Join] cancel to player " + _outTo + " seq=" + _outSeq);
                int to = _outTo; _outTo = -1; _sampleId = -1;
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

        public int _WaitingFor() { return _outTo; }
        /// <summary>合流を取り消せるか: 返事待ち・承認後の速さの測定待ち・切符待ち・暗転中。本人が移動した後 (犬待ち) は含めない (RC-06)。
        /// 手元メニューは表示 ([取り消す]) と押したとき (_CancelJoinRequest へ回す) の両方でこれを使う。</summary>
        public bool _JoinCancelable()
        {
            if (_outTo >= 0 || _acceptFor >= 0) return true;
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
            Vector3 face = p - landing; face.y = 0f;
            float yaw = face.sqrMagnitude > 0.0001f ? Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg : 0f;
            int started = manager._TravelBeginJoin(landing, yaw);
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
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller) || caller.isLocal) return;
            if (_inFrom == caller.playerId)
            {
                // 同じ送り主の再送・新しい seq は冪等: 通知の置き直しも期限の延長もしない。新しい seq だけ覚える (返事が照合で通るように)
                if (seq > _inSeq) _inSeq = seq;
                return;
            }
            int code = -1;
            if (!accepting || Time.time < _pauseUntil) code = CodePaused;
            else if (Blocked(caller.playerId)) code = CodeNo;
            else if (_inFrom >= 0) code = CodeBusy;
            else if (Time.time - _closedAt < NoticeGap) code = CodeBusy;
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
        /// <summary>[このインスタンスにいる間は、この人から受けない]。今の申し込みには code 0。保存しない。ローカル専用。</summary>
        public void _BlockIncoming()
        {
            if (_inFrom < 0) return;
            Block(_inFrom);
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

        /// <summary>退室した人の枠を空ける (RC-07)。</summary>
        private void ForgetHistory(int id)
        {
            for (int i = 0; i < _blocked.Length; i++) if (_blocked[i] == id) _blocked[i] = -1;
            for (int i = 0; i < _deniedIds.Length; i++) if (_deniedIds[i] == id) _deniedIds[i] = -1;
        }

        private bool DeniedRecently(int id)
        {
            for (int i = 0; i < _deniedIds.Length; i++) if (_deniedIds[i] == id && Time.time - _deniedAts[i] < SameTargetGap) return true;
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
            int id = player.playerId;
            if (id == _outTo) { _outTo = -1; _sampleId = -1; Post(JoinLeft, id); }
            if (id == _acceptFor) { _acceptFor = -1; _sampleId = -1; Post(JoinLeft, id); }
            if (id == _movingWith) TargetLeftWhileMoving();
            if (id == _inFrom) ClearIncoming();
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
            if (_outTo < 0 && _inFrom < 0 && _sampleId < 0 && _acceptFor < 0 && _movingWith < 0) return;
            if (!Networking.IsOwner(gameObject)) return;
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
            if (_outTo >= 0)
            {
                if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(_outTo))) { int to = _outTo; _outTo = -1; _sampleId = -1; Post(JoinLeft, to); }
                else if (Time.time - _outAt > AnswerSeconds + 2f)
                {
                    // 時間切れは断りと同じ扱い (code 0)。相手側の保留も取り消しておく
                    SendCancelTo(_outTo);
                    int to = _outTo; _outTo = -1; _sampleId = -1;
                    Deny(to);
                    Debug.Log("[KH_Join] request to player " + to + " timed out");
                    Post(CodeNo, to);
                }
            }
            if (_inFrom >= 0 && Time.time - _inAt > AnswerSeconds) AnswerIncoming(CodeNo);
        }

        private void StartSampling(VRCPlayerApi target)
        {
            _sampleId = target.playerId; _samples = 0;
            _tPrev = target.GetPosition(); _tPrevAt = Time.time; _tVel = Vector3.zero;
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
