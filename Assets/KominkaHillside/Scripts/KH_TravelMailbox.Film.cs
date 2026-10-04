using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    /// <summary>IMP-WLD-008 W4: 他の人を撮る許可 (docs/plans/2026-10-03-video-camera.md §5・§13・§14。§14 > §13 > §5)。F = 撮る人、S = 撮られる人、seq = F の申し込みの番号。
    ///
    /// **許可の正本は S の箱の同期された表** (filmGrantIds / filmGrantSeqs、4 枠。S だけが書く)。F の filmTargetId / filmSeq は「使っています」の申告だけ。
    /// 返事のイベント (AnsJoin) は表示と回の状態のためだけで、撮影を始める条件にしない (§5.1・§13.2)。
    /// - F の回の状態 (ローカル): pending (ReqFilm を送った) → grant-in-transit (AnsJoin(seq, 1)) → in-use (S の表に (F, seq) が届いた。申告を同期) → ended。
    ///   transit は 10 s で表が届かなければ ended。ended の seq は二度と開かない (回の記録を消すので、遅れた承認・古い表は当たらない)。
    /// - 撮る人の端末の門 <see cref="_FilmGate"/>: 回が in-use・相手が今の回の相手・相手の箱の Owner が相手本人・表に (自分, 回の seq) がある。
    ///   動画カメラの TryPersonPose (KH_VideoCamera.Subjects.cs) が描く回ごとに呼ぶ。
    /// - S の表の書き方 (§13.5): 同じ F の行 → 空き行 → 無ければ断る (code 2)。掃除は 10 秒ごと: F の退室・F の申告が別の相手 / 別の seq になってから 10 s・
    ///   書いてから 20 s たっても申告がそろわない行。書いた直後の行は消さない。
    /// - 受付の切り替え filmAccepting は合流の accepting とは別 (初期 ON、PlayerData KH_TravelJoin.filmAccepting に保存、ナイトモードと同じ作法)。
    ///   受付 OFF・休止・ブロックは新しい申し込みだけに効く。使用中の許可を消すのは S の [やめてもらう] (_FilmRevokeRow) だけ。
    /// - ネットワークの口: ReqFilm (F → S)・EndFilm (F → S)・FilmRevoked (S → F)。どれも最初に IsOwner を見て、送り主は NetworkCalling.CallingPlayer だけを信じ、
    ///   seq と回の相手で照合する。届く順番には頼らない (§13.9)。
    /// - 改造したクライアントは防げない (§13.1)。S の「撮影中」は F が同期した申告を表示しているだけ。</summary>
    public partial class KH_TravelMailbox
    {
        [UdonSynced] public bool filmAccepting = true;
        [UdonSynced] public int[] filmGrantIds = new int[] { -1, -1, -1, -1 };
        [UdonSynced] public int[] filmGrantSeqs = new int[] { 0, 0, 0, 0 };
        [UdonSynced] public int filmTargetId = -1;
        [UdonSynced] public int filmSeq;

        public const string FilmSaveKey = "KH_TravelJoin.filmAccepting";
        public const int FilmSlots = 4;
        public const int FilmNone = 0, FilmPending = 1, FilmTransit = 2, FilmInUse = 3;
        // 撮影の結果 (撮影カメラが文言に変える)。0〜27 は合流と同じ意味で使う (CodeNo / CodeBusy / CodePaused / JoinWaiting / JoinLeft ...)
        public const int FilmGrantedCode = 28, FilmInUseCode = 29, FilmRevokedBy = 30, FilmNoTable = 31, FilmEnded = 32;
        public const float FilmTransitSeconds = 10f, FilmSweepEvery = 10f, FilmUnstartedSeconds = 20f, FilmMismatchSeconds = 10f, FilmCheckEvery = 0.25f;

        [HideInInspector] public int filmResultSerial, filmResultCode, filmResultTarget = -1;
        // ハーネス: 折り返し (debugJoinLoopback > 0) のとき、送るはずだった EndFilm / FilmRevoked を数える
        [HideInInspector] public int debugFilmSentCount, debugFilmSentSeq, debugFilmSentKind;

        // ---- F (撮る人) の回 (ローカル) ----
        private int _filmPhase, _filmTarget = -1, _filmSeqLocal;
        private float _filmPhaseAt, _filmNextCheck, _filmNextFind;
        private KH_TravelMailbox _filmBox;   // 相手 (S) の箱。申し込みのときに 1 回探す (毎回は探さない。vrc-networking §9)
        private bool _filmChosen;
        // ---- S (撮られる人) の行 (ローカルの添え書き。表そのものは同期変数) ----
        private float[] _rowAt = new float[] { 0f, 0f, 0f, 0f };
        private float[] _rowMismatchSince = new float[] { -1f, -1f, -1f, -1f };
        private bool[] _rowStarted = new bool[] { false, false, false, false };
        private KH_TravelMailbox[] _rowBoxes = new KH_TravelMailbox[4];
        private int _filmRows;
        private float _filmNextSweep;

        // ============================================================
        // F: 申し込み・回・門
        // ============================================================

        /// <summary>撮影の許可を申し込む (撮影カメラの [依頼する] から。§14.3 の確認の後だけ)。戻り値は _Request と同じ結果コード。ローカル専用。</summary>
        public int _FilmRequest(int targetId) { return _Request(targetId, KindFilm); }

        public int _FilmPhase() { return _filmPhase; }
        public int _FilmTarget() { return _filmPhase != FilmNone ? _filmTarget : -1; }
        public int _FilmSeqNow() { return _filmSeqLocal; }
        /// <summary>返事待ちの残り秒数 (返事待ちでなければ 0)。</summary>
        public float _FilmWaitLeft() { return _filmPhase == FilmPending && _outTo >= 0 && _outKind == KindFilm ? _WaitLeft() : 0f; }

        /// <summary>撮る人の端末の門 (§5.5・§13)。回が in-use で相手が pid、相手の箱の Owner が pid 本人、その表に (自分, 回の seq) がある。
        /// 動画カメラが描く回ごとに呼ぶ (箱は申し込みのときに探してある。ここでは探さない)。ローカル専用。</summary>
        public bool _FilmGate(int pid)
        {
            if (_filmPhase != FilmInUse || _filmTarget != pid || pid < 0) return false;
            return FilmTableHasMe(pid);
        }

        /// <summary>今の回を終える (× / 対象の変更 / 撮影カメラの [取り消す] / 人以外のモードで 60 s)。pending なら CancelJoin、transit・in-use なら EndFilm を S へ送り、
        /// 申告を消す。後から届く承認より優先する (§13.3)。合流・呼ぶの保留は触らない。ローカル専用。</summary>
        public void _FilmEnd()
        {
            if (!Networking.IsOwner(gameObject) || _filmPhase == FilmNone) return;
            int to = _filmTarget;
            if (_filmPhase == FilmPending)
            {
                if (_outTo >= 0 && _outKind == KindFilm) { SendCancelTo(_outTo); ResetOut(); }   // ResetOut が回も終える
                else FilmClear();
            }
            else
            {
                SendFilmEvent(to, nameof(EndFilm), _filmSeqLocal, null);
                FilmClear();
            }
            Debug.Log("[KH_Film] session with player " + to + " ended by me");
            PostFilm(FilmEnded, to);
        }

        /// <summary>一覧の段階で申し込めるか (送らない。§14.3「確認まで進ませてから断らない」)。JoinWaiting = 申し込める。
        /// box は相手の箱 (呼ぶ側が一覧を作ったときに探したもの。null = 準備中)。ローカル専用。</summary>
        public int _FilmCanAsk(int pid, KH_TravelMailbox box)
        {
            if (!Networking.IsOwner(gameObject)) return JoinNotReady;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
            if (!Utilities.IsValid(p) || p.isLocal) return JoinLeft;
            // 自分の撮影の返事待ちは数えない ([依頼する] がまず今の回を終えるので、別の人へ申し込める。§13.3 の対象変更)
            bool ownFilm = _outTo >= 0 && _outKind == KindFilm;
            if ((_outTo >= 0 && !ownFilm) || _acceptFor >= 0 || _movingWith >= 0 || _goFrom >= 0 || _goPressed) return JoinBusyMe;
            if (manager != null && (manager._TravelWaiting() || manager._TravelFading())) return JoinBusyMe;
            if (_inFrom == pid) return AnswerFirst;
            if (DeniedRecently(pid)) return JoinAgain;
            if (debugJoinLoopback > 0) return JoinWaiting;
            if (!Utilities.IsValid(box)) return JoinNotReady;
            if (!box.filmAccepting) return JoinNotAccepting;
            return JoinWaiting;
        }

        /// <summary>同じ相手へまた申し込めるまでの秒数 (断り・時間切れの 30 秒。無ければ 0)。</summary>
        public float _FilmAgainLeft(int pid)
        {
            float left = 0f;
            for (int i = 0; i < _deniedIds.Length; i++)
                if (_deniedIds[i] == pid) left = Mathf.Max(left, SameTargetGap - (Time.time - _deniedAts[i]));
            return Mathf.Max(0f, left);
        }

        void FilmBegin(int target, int seq)
        {
            _filmPhase = FilmPending; _filmTarget = target; _filmSeqLocal = seq; _filmPhaseAt = Time.time; _filmNextCheck = 0f; _filmNextFind = 0f;
        }

        /// <summary>回を終える (ended)。申告を出していたら消して送る。回の記録 (相手・箱) は残さない。</summary>
        void FilmClear()
        {
            _filmPhase = FilmNone; _filmTarget = -1; _filmBox = null;
            if (filmTargetId >= 0 && Networking.IsOwner(gameObject)) { filmTargetId = -1; RequestSerialization(); }
            // every end of a session passes here (revoke, table gone, transit timeout, leave, _FilmEnd, a pending reset): the camera detaches
            // the person's picture now, not at its next render tick (Codex review 1)
            if (_filmListener != null) _filmListener.SendCustomEvent("_OnFilmEnded");
        }

        private UdonSharpBehaviour _filmListener;

        /// <summary>The video camera registers itself (local only; never a network target).</summary>
        public void _FilmSetListener(UdonSharpBehaviour listener) { _filmListener = listener; }

        /// <summary>撮影の返事 (AnsJoin が送り主・seq を照合した後)。code 1 は grant-in-transit (表が届くまで撮らない)。それ以外は ended。</summary>
        private void HandleFilmAnswer(int code)
        {
            int to = _outTo;
            // 保留枠は空ける。回は続く (ResetOut は回も終えるので使わない)
            _outTo = -1; _outPhase = 0; _outKind = KindJoin; _sampleId = -1;
            if (code == CodeYes && _filmPhase == FilmPending && _filmTarget == to)
            {
                _filmPhase = FilmTransit; _filmPhaseAt = Time.time; _filmNextCheck = 0f;
                Debug.Log("[KH_Film] player " + to + " said yes to seq " + _filmSeqLocal + "; waiting for the grant table");
                PostFilm(FilmGrantedCode, to);
                FilmCheck();
                return;
            }
            FilmClear();
            if (code == CodeNo) Deny(to);
            PostFilm(code, to);
        }

        /// <summary>F の回を 4 Hz で見る (Update から。回が無ければ呼ばれない)。transit: 表が届いたら in-use (申告を同期)、10 s で届かなければ ended。
        /// in-use: 表から (自分, seq) が消えた・Owner が違う・相手が退室 → ended。</summary>
        void FilmCheck()
        {
            if (_filmPhase != FilmTransit && _filmPhase != FilmInUse) return;
            int to = _filmTarget;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(to);
            if (!Utilities.IsValid(p)) { FilmClear(); PostFilm(JoinLeft, to); return; }
            if (!Utilities.IsValid(_filmBox) && Time.time >= _filmNextFind) { _filmNextFind = Time.time + 1f; _filmBox = _FindBox(p); }
            bool granted = FilmTableHasMe(to);
            if (_filmPhase == FilmTransit)
            {
                if (granted)
                {
                    _filmPhase = FilmInUse; _filmPhaseAt = Time.time;
                    filmTargetId = to; filmSeq = _filmSeqLocal; RequestSerialization();   // 申告 (S の「撮影中」の表示と掃除が読む)
                    Debug.Log("[KH_Film] grant table of player " + to + " has seq " + _filmSeqLocal + ": in use");
                    PostFilm(FilmInUseCode, to);
                    return;
                }
                if (Time.time - _filmPhaseAt > FilmTransitSeconds)
                {
                    // RC-08 と遅延の期限 (§13.7): 承認は来たが表が届かない。S に行があれば消してもらう
                    SendFilmEvent(to, nameof(EndFilm), _filmSeqLocal, null);
                    FilmClear();
                    Debug.Log("[KH_Film] grant table of player " + to + " never showed seq; ended");
                    PostFilm(FilmNoTable, to);
                }
                return;
            }
            if (!granted)
            {
                FilmClear();
                Debug.Log("[KH_Film] grant of player " + to + " is gone; ended");
                PostFilm(FilmRevokedBy, to);
            }
        }

        /// <summary>相手の箱の Owner が相手本人 (構成の異常を拒む追加の検査。§13.1) で、その表に (自分, 回の seq) がある。</summary>
        bool FilmTableHasMe(int pid)
        {
            if (!Utilities.IsValid(_filmBox)) return false;
            VRCPlayerApi o = Networking.GetOwner(_filmBox.gameObject);
            if (!Utilities.IsValid(o) || o.playerId != pid) return false;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return false;
            int me = lp.playerId;
            int[] ids = _filmBox.filmGrantIds, seqs = _filmBox.filmGrantSeqs;
            if (ids == null || seqs == null) return false;
            int n = Mathf.Min(ids.Length, seqs.Length);
            for (int k = 0; k < n; k++) if (ids[k] == me && seqs[k] == _filmSeqLocal) return true;
            return false;
        }

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void FilmRevoked(int seq) // net: callable-ok: only the subject of the current film session ends that session's own seq (sender == session target, seq == session seq)
        {
            if (!Networking.IsOwner(gameObject)) return;
            FilmReceiveRevoked(NetworkCalling.CallingPlayer, seq);
        }

        /// <summary>S の取り消し (§13.3): 送り主 = その回の S、seq = 回の seq、回が pending / transit / in-use のときだけ ended。古い取り消しで新しい回を止めない。</summary>
        void FilmReceiveRevoked(VRCPlayerApi caller, int seq)
        {
            if (!Utilities.IsValid(caller) || caller.isLocal) return;
            if (_filmPhase == FilmNone || caller.playerId != _filmTarget || seq != _filmSeqLocal) { Debug.Log("[KH_Film] revoke from player " + (Utilities.IsValid(caller) ? caller.playerId : -1) + " seq=" + seq + " ignored"); return; }
            int to = _filmTarget;
            if (_filmPhase == FilmPending && _outTo == to && _outKind == KindFilm) { _outTo = -1; _outPhase = 0; _outKind = KindJoin; }   // 保留枠も空ける (後から届く承認は送り主が合わず捨てられる)
            FilmClear();
            Debug.Log("[KH_Film] player " + to + " revoked seq " + seq);
            PostFilm(FilmRevokedBy, to);
        }

        /// <summary>ハーネス専用: 人形 (debugIncomingFrom) からの FilmRevoked(debugIncomingSeq)。ローカル専用。</summary>
        public void _DebugFilmRevokedNow()
        {
            if (debugJoinLoopback <= 0 || !Networking.IsOwner(gameObject)) return;
            FilmReceiveRevoked(VRCPlayerApi.GetPlayerById(debugIncomingFrom), debugIncomingSeq);
        }

        // ============================================================
        // S: 受信・許可の表・取り消し・掃除
        // ============================================================

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void ReqFilm(int seq) // net: callable-ok: requester = CallingPlayer; only raises a local notice on the owner (a grant row is written only by the owner's own [いいよ])
        {
            if (!Networking.IsOwner(gameObject)) return;
            Receive(NetworkCalling.CallingPlayer, seq, KindFilm);
        }

        [NetworkCallable(maxEventsPerSecond: 2)]
        public void EndFilm(int seq) // net: callable-ok: a filmer can only drop its own grant row (row id == sender, row seq == seq)
        {
            if (!Networking.IsOwner(gameObject)) return;
            FilmReceiveEnd(NetworkCalling.CallingPlayer, seq);
        }

        void FilmReceiveEnd(VRCPlayerApi caller, int seq)
        {
            if (!Utilities.IsValid(caller) || caller.isLocal) return;
            if (FilmDropRow(caller.playerId, seq)) Debug.Log("[KH_Film] player " + caller.playerId + " ended seq " + seq + "; row removed");
        }

        /// <summary>ハーネス専用: 人形 (debugIncomingFrom) からの EndFilm(debugIncomingSeq)。ローカル専用。</summary>
        public void _DebugFilmEndNow()
        {
            if (debugJoinLoopback <= 0 || !Networking.IsOwner(gameObject)) return;
            FilmReceiveEnd(VRCPlayerApi.GetPlayerById(debugIncomingFrom), debugIncomingSeq);
        }

        /// <summary>通知板 (撮影の形) の [いいよ]。世代・種類・0.5 秒の守りを通ったら、表に (F, seq) を書いて送り、その後で表示用の AnsJoin(seq, 1) を返す (§5.3-4)。
        /// 書く行は同じ F の行 → 空き行。無ければ code 2 (他の人の許可は追い出さない。§13.5)。ローカル専用。</summary>
        public void _AnswerFilmYesGen(int gen)
        {
            if (!Accepts(gen, KindFilm)) return;
            int from = _inFrom, seq = _inSeq;
            VRCPlayerApi f = VRCPlayerApi.GetPlayerById(from);
            if (!Utilities.IsValid(f)) { ClearIncoming(); return; }
            int slot = FilmSlotFor(from);
            if (slot < 0) { AnswerIncoming(CodeBusy); return; }
            filmGrantIds[slot] = from; filmGrantSeqs[slot] = seq;
            _rowAt[slot] = Time.time; _rowStarted[slot] = false; _rowMismatchSince[slot] = -1f; _rowBoxes[slot] = _FindBox(f);
            FilmCountRows();
            _filmNextSweep = Time.time + FilmSweepEvery;   // 書いた直後の行は消さない (最初の掃除は 10 秒後)
            RequestSerialization();
            Debug.Log("[KH_Film] granted player " + from + " seq=" + seq + " (row " + slot + ")");
            AnswerIncoming(CodeYes);   // 表示用の返事を送り、通知を閉じる
            if (notice != null) notice._ShowFilmGranted(from);
        }

        /// <summary>通知板 (撮影の形) の [いまはだめ]。表は書かない。ローカル専用。</summary>
        public void _AnswerFilmNoGen(int gen) { if (Accepts(gen, KindFilm)) AnswerIncoming(CodeNo); }

        /// <summary>手元メニューの [やめてもらう] (§13.8)。その行を消して送り、F へ FilmRevoked(seq)。ローカル専用。</summary>
        public void _FilmRevokeRow(int k)
        {
            if (!Networking.IsOwner(gameObject) || k < 0 || k >= FilmSlots || filmGrantIds[k] < 0) return;
            int f = filmGrantIds[k], seq = filmGrantSeqs[k];
            KH_TravelMailbox box = _rowBoxes[k];
            ClearRow(k);
            FilmCountRows();
            RequestSerialization();
            Debug.Log("[KH_Film] revoked player " + f + " seq=" + seq);
            SendFilmEvent(f, nameof(FilmRevoked), seq, box);
        }

        public int _FilmRowId(int k) { return k >= 0 && k < FilmSlots ? filmGrantIds[k] : -1; }
        public int _FilmRowCount() { return _filmRows; }

        /// <summary>その行の F が (自分, 行の seq) を撮っていると申告しているか (両方とも同期された値。§5.6・§13.8)。申告を表示しているだけで、実際に描いているかは分からない。</summary>
        public bool _FilmRowActive(int k)
        {
            if (k < 0 || k >= FilmSlots || filmGrantIds[k] < 0) return false;
            KH_TravelMailbox box = _rowBoxes[k];
            if (!Utilities.IsValid(box)) return false;
            VRCPlayerApi o = Networking.GetOwner(box.gameObject);
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(o) || !Utilities.IsValid(lp) || o.playerId != filmGrantIds[k]) return false;
            return box.filmTargetId == lp.playerId && box.filmSeq == filmGrantSeqs[k];
        }

        /// <summary>同じ F の行 → 空き行。どちらも無ければ -1。</summary>
        int FilmSlotFor(int from)
        {
            for (int k = 0; k < FilmSlots; k++) if (filmGrantIds[k] == from) return k;
            for (int k = 0; k < FilmSlots; k++) if (filmGrantIds[k] < 0) return k;
            return -1;
        }

        bool FilmHasRoom(int from) { return FilmSlotFor(from) >= 0; }

        /// <summary>保留中の申し込みを、同じ送り主の別の種類で置き換えるときの自動の返事 (§13.6)。-1 = 置き換えてよい。
        /// 撮影へ: 撮影の受け付け・休止・枠。撮影から合流・呼ぶへ: 合流の受け付け・休止。合流 ⇄ 呼ぶは今までどおり (検査しない)。</summary>
        int FilmSwapCode(int from, int newKind)
        {
            if (newKind == KindFilm)
            {
                if (!filmAccepting || Time.time < _pauseUntil) return CodePaused;
                if (!FilmHasRoom(from)) return CodeBusy;
                return -1;
            }
            if (_inKind == KindFilm && (!accepting || Time.time < _pauseUntil)) return CodePaused;
            return -1;
        }

        /// <summary>(from, seq) の行だけを消す。消したら送る。</summary>
        bool FilmDropRow(int from, int seq)
        {
            if (!Networking.IsOwner(gameObject)) return false;
            bool changed = false;
            for (int k = 0; k < FilmSlots; k++)
                if (filmGrantIds[k] == from && filmGrantSeqs[k] == seq) { ClearRow(k); changed = true; }
            if (changed) { FilmCountRows(); RequestSerialization(); }
            return changed;
        }

        void ClearRow(int k)
        {
            filmGrantIds[k] = -1; filmGrantSeqs[k] = 0;
            _rowBoxes[k] = null; _rowStarted[k] = false; _rowMismatchSince[k] = -1f; _rowAt[k] = 0f;
        }

        void FilmCountRows()
        {
            int n = 0;
            for (int k = 0; k < FilmSlots; k++) if (filmGrantIds[k] >= 0) n++;
            _filmRows = n;
        }

        /// <summary>S の掃除 (10 秒ごと、行があるときだけ。§13.5)。</summary>
        void FilmSweep()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            bool changed = false;
            float now = Time.time;
            for (int k = 0; k < FilmSlots; k++)
            {
                if (filmGrantIds[k] < 0) continue;
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(filmGrantIds[k]);
                if (!Utilities.IsValid(p)) { ClearRow(k); changed = true; continue; }
                if (!Utilities.IsValid(_rowBoxes[k])) _rowBoxes[k] = _FindBox(p);
                if (_FilmRowActive(k)) { _rowStarted[k] = true; _rowMismatchSince[k] = -1f; continue; }
                if (!_rowStarted[k])
                {
                    if (now - _rowAt[k] >= FilmUnstartedSeconds) { Debug.Log("[KH_Film] row " + k + " (player " + filmGrantIds[k] + ") never started; removed"); ClearRow(k); changed = true; }
                    continue;
                }
                if (_rowMismatchSince[k] < 0f) _rowMismatchSince[k] = now;
                else if (now - _rowMismatchSince[k] >= FilmMismatchSeconds) { Debug.Log("[KH_Film] row " + k + " (player " + filmGrantIds[k] + ") no longer declared; removed"); ClearRow(k); changed = true; }
            }
            if (changed) { FilmCountRows(); RequestSerialization(); }
        }

        // ============================================================
        // 共通
        // ============================================================

        /// <summary>Update から (撮影に関わっているときだけ)。F の回は 4 Hz、S の掃除は 10 秒ごと。</summary>
        void FilmUpdate()
        {
            if (_filmPhase == FilmTransit || _filmPhase == FilmInUse)
            {
                if (Time.time >= _filmNextCheck) { _filmNextCheck = Time.time + FilmCheckEvery; FilmCheck(); }
            }
            if (_filmRows > 0 && Time.time >= _filmNextSweep) { _filmNextSweep = Time.time + FilmSweepEvery; FilmSweep(); }
        }

        /// <summary>退室: S の側はその人の行を消す。F の側はその人との回 (transit / in-use) を終える (pending は保留枠の処理が終える)。</summary>
        void FilmPlayerLeft(int id)
        {
            bool changed = false;
            for (int k = 0; k < FilmSlots; k++) if (filmGrantIds[k] == id) { ClearRow(k); changed = true; }
            if (changed) { FilmCountRows(); RequestSerialization(); }
            if (_filmPhase != FilmNone && _filmTarget == id) { FilmClear(); PostFilm(JoinLeft, id); }
        }

        /// <summary>EndFilm / FilmRevoked を相手の箱へ。box が無ければ探す。折り返し (ハーネス) では送らずに数える。</summary>
        void SendFilmEvent(int to, string evt, int seq, KH_TravelMailbox box)
        {
            if (debugJoinLoopback > 0) { debugFilmSentCount++; debugFilmSentSeq = seq; debugFilmSentKind = evt == nameof(FilmRevoked) ? 1 : 0; return; }
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(to);
            if (!Utilities.IsValid(p)) return;
            if (!Utilities.IsValid(box)) box = to == _filmTarget && Utilities.IsValid(_filmBox) ? _filmBox : _FindBox(p);
            if (Utilities.IsValid(box)) box.SendCustomNetworkEvent(NetworkEventTarget.Owner, evt, seq);
        }

        private void PostFilm(int code, int target)
        {
            filmResultCode = code; filmResultTarget = target; filmResultSerial++;
        }

        // ---- 受け付けの ON/OFF (同期 1 本。PlayerData に保存。合流の accepting と同じ作法) ----

        /// <summary>「撮影の申し込みを受ける」の切替。新規の受け付けだけ止める (今の許可は消さない)。ローカル専用。</summary>
        public void _ToggleFilmAccepting()
        {
            if (!Networking.IsOwner(gameObject)) return;
            filmAccepting = !filmAccepting; _filmChosen = true;
            RequestSerialization();
            if (_restored) PlayerData.SetBool(FilmSaveKey, filmAccepting);
        }

        void FilmRestored(VRCPlayerApi player)
        {
            if (_filmChosen) { PlayerData.SetBool(FilmSaveKey, filmAccepting); return; }
            bool saved = true;
            if (PlayerData.TryGetBool(player, FilmSaveKey, out saved) && saved != filmAccepting) { filmAccepting = saved; RequestSerialization(); }
        }

        // OnOwnershipRequest で拒む案 (§5.7) は入れていない: PlayerObject の Owner は本人から移らない (§13.1) うえ、実機で Player Object の生成が
        // 所有の要求を通るかを確かめられず、拒むと受付箱 (合流・呼ぶを含む) の Owner が本人にならない恐れがある。代わりに読む側が毎回 GetOwner を照合する。
    }
}
