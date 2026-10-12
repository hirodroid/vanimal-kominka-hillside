using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M1: 要求 (ローカル API → Owner) と裁定 (Owner)。
    ///
    /// 流れは KH_LeashManager.Travel.cs の TravelSend と同じ: Owner 自身なら Receive* を直接呼び、それ以外は
    /// `SendCustomNetworkEvent(Owner, Req*)`。Req* は `NetworkCalling.CallingPlayer` を要求者とし、引数は範囲を検査してから使う。
    /// ローカル側は同じ判定 (CanStart / CanJoin) で先に結果コードを返す (Owner からは返さない。2 秒の返事待ちで代用)。
    ///
    /// 裁定の表 (Owner。拒否は何も書かずログだけ):
    /// | 要求 | 受ける条件 | 拒否 (結果コード) |
    /// |---|---|---|
    /// | ReqStart(spot, dest) | 経路と乗り場が有効・要求者が未乗車・現在地≠目的地・空車と空きグループがある | Unavailable / Busy / Here / Full |
    /// | ReqJoin(spot, g, gen) | (先に到着を確定) g が Running かつ受付中・世代 gen が一致・要求者が未乗車・空車がある | Unavailable / Closed (世代違いはログ reason=gen) / Busy / Full |
    /// | ReqLeave() | 要求者が乗っている (Riding / Ending) | 乗っていなければ何もしない |
    /// | ReqAccepting(on) | 要求者のカートがそのグループのマスターで、グループが Running | 何もしない |</summary>
    public partial class KH_CartManager
    {
        // ============================================================
        // ローカル API (乗り場パネル・停止ボタン・KH_LeashManager から)
        // ============================================================

        /// <summary>この乗り場から出発する。行き先は犬の散歩設定 (leash._MyChoice())。戻り値は結果コード (CartOk なら返事待ちに入った)。</summary>
        public int _Start(int spot)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || leash == null) return CartUnavailable;
            if (_IsRiding() || _leaveCart >= 0 || _pendingKind != 0) return CartBusy;
            int destination = leash._MyChoice();
            int code = CanStart(lp.playerId, spot, destination);
            if (code != CartOk) return code;
            _reqSeq++;
            _pendingKind = 1; _pendingAt = Time.time; _pendingSpot = spot; _pendingGroup = -1; _pendingGen = -1; _pendingCart = -1;
            Debug.Log("[KH_Cart] request req=" + _reqSeq + " kind=start spot=" + spot + " dest=" + destination);
            if (Networking.IsOwner(gameObject)) ReceiveStart(lp.playerId, spot, destination);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqStart), spot, destination);
            return CartOk;
        }

        /// <summary>走行中のグループに合流する。gen はパネルが表示したときのグループの世代 (解散後の同じ番号の別グループへ合流しない)。戻り値は結果コード。</summary>
        public int _Join(int spot, int group, int gen)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return CartUnavailable;
            if (_IsRiding() || _leaveCart >= 0 || _pendingKind != 0) return CartBusy;
            int code = CanJoin(lp.playerId, spot, group, gen);
            if (code != CartOk) return code;
            _reqSeq++;
            _pendingKind = 2; _pendingAt = Time.time; _pendingSpot = spot; _pendingGroup = group; _pendingGen = gen; _pendingCart = -1;
            Debug.Log("[KH_Cart] request req=" + _reqSeq + " kind=join spot=" + spot + " group=" + group + " gen=" + gen);
            if (Networking.IsOwner(gameObject)) ReceiveJoin(lp.playerId, spot, group, gen);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqJoin), spot, group, gen);
            return CartOk;
        }

        /// <summary>M3: その場に残ったカート cart から出発する (カート上メニュー)。行き先は犬の散歩設定。戻り値は結果コード。</summary>
        public int _StartFrom(int cart)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || leash == null) return CartUnavailable;
            if (_IsRiding() || _leaveCart >= 0 || _pendingKind != 0) return CartBusy;
            int destination = leash._MyChoice();
            int code = CanStartFrom(lp.playerId, cart, destination);
            if (code != CartOk) return code;
            _reqSeq++;
            _pendingKind = 3; _pendingAt = Time.time; _pendingSpot = -1; _pendingCart = cart; _pendingGroup = -1; _pendingGen = -1;
            Debug.Log("[KH_Cart] request req=" + _reqSeq + " kind=start-from cart=" + cart + " dest=" + destination);
            if (Networking.IsOwner(gameObject)) ReceiveStartFrom(lp.playerId, cart, destination);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqStartFrom), cart, destination);
            return CartOk;
        }

        /// <summary>M3: その場に残ったカート cart に乗って、走行中のグループの末尾へ合流する (暗転で移動)。戻り値は結果コード。</summary>
        public int _JoinFrom(int cart, int group, int gen)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return CartUnavailable;
            if (_IsRiding() || _leaveCart >= 0 || _pendingKind != 0) return CartBusy;
            int code = CanJoinFrom(lp.playerId, cart, group, gen);
            if (code != CartOk) return code;
            _reqSeq++;
            _pendingKind = 4; _pendingAt = Time.time; _pendingSpot = -1; _pendingCart = cart; _pendingGroup = group; _pendingGen = gen;
            Debug.Log("[KH_Cart] request req=" + _reqSeq + " kind=join-from cart=" + cart + " group=" + group + " gen=" + gen);
            if (Networking.IsOwner(gameObject)) ReceiveJoinFrom(lp.playerId, cart, group, gen);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqJoinFrom), cart, group, gen);
            return CartOk;
        }

        /// <summary>自分が乗っていれば、即ローカルで降りて (station から出る) 降車を要求する。二重呼び出しに耐える。</summary>
        public void _Leave() { LeaveCore("request", true); }

        /// <summary>KH_LeashManager の _TravelBegin / _TravelBeginJoin の門を通った後から。保留中の要求も取り消し、乗っていれば _Leave と同じ (暗転は呼び元)。</summary>
        public void _LeaveForTravel()
        {
            CancelPending("travel");
            LeaveCore("travel", true);
        }

        /// <summary>保留中の出発・合流を取り消す。この後に届いた割当は着座せずに返す (late-assign)。</summary>
        private void CancelPending(string reason)
        {
            if (_pendingKind == 0) return;
            Debug.Log("[KH_Cart] request canceled req=" + _reqSeq + " kind=" + _pendingKind + " reason=" + reason);
            _pendingKind = 0;
        }

        /// <summary>新しく自分に来た割当 c が、有効な保留要求の答えか (出発: 自分がマスターの 1 台目・同じ乗り場 / 合流: 同じ乗り場・グループ・世代)。</summary>
        private bool PendingMatches(int c)
        {
            if (_pendingKind == 0) return false;
            int g = cartGroup[c];
            if (!ValidGroup(g)) return false;
            // M3: カートからの出発・合流は「そのカート自体」が割り当てられたか
            if (_pendingKind == 3) return c == _pendingCart && groupMaster[g] == c && cartOrder[c] == 0;
            if (_pendingKind == 4) return c == _pendingCart && g == _pendingGroup && groupGen[g] == _pendingGen;
            if (cartSpot[c] != _pendingSpot) return false;
            if (_pendingKind == 1) return groupMaster[g] == c && cartOrder[c] == 0;
            return g == _pendingGroup && groupGen[g] == _pendingGen;
        }

        /// <summary>自分がマスターなら受付を切り替える。</summary>
        public void _ToggleAccepting()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || !_IsMaster()) return;
            int g = cartGroup[_myCart];
            bool on = !groupAccepting[g];
            if (Networking.IsOwner(gameObject)) ReceiveAccepting(lp.playerId, on);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqAccepting), on);
        }

        // ---- 座席の調整 (M1.1。乗っている本人だけ。高さ 5 cm/段・向き 15°/段、どちらも -6..+6) ----
        public void _SeatUp() { SendSeat(1, 0, 0); }
        public void _SeatDown() { SendSeat(-1, 0, 0); }
        public void _TurnLeft() { SendSeat(0, -1, 0); }
        public void _TurnRight() { SendSeat(0, 1, 0); }
        public void _SeatForward() { SendSeat(0, 0, 1); }
        public void _SeatBack() { SendSeat(0, 0, -1); }

        private void SendSeat(int dHeight, int dYaw, int dFwd)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || !_IsRiding()) return;
            if (Networking.IsOwner(gameObject)) ReceiveSeat(lp.playerId, dHeight, dYaw, dFwd);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqSeat), dHeight, dYaw, dFwd);
        }

        /// <summary>BUG-WLD-001: 着座の高さの自動補正 (段数の絶対値) を送る。KH_CartSeat._MeasureHip から (ローカルの乗員だけ、着座ごとに 1 回)。</summary>
        public void _SendSeatAuto(int steps)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || !_IsRiding()) return;
            if (Networking.IsOwner(gameObject)) ReceiveSeatAuto(lp.playerId, steps);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqSeatAuto), steps);
        }

        /// <summary>降車の 1 本道。exitStation = false はすでに station の外 (ジャンプ) か、station に触れたくない (リスポーン) とき。</summary>
        private void LeaveCore(string reason, bool exitStation)
        {
            if (!_IsRiding()) return;   // 乗っていない・降車を送り済み (OnStationExited からの再入もここで止まる)
            int c = _myCart;
            _leaveCart = c; _leaveSentAt = Time.time; _leaveTries = 0;
            if (_fadeCart == c) { _fadeCart = -1; if (fade != null) fade._Release(); }
            Debug.Log("[KH_Cart] local-leave cart=" + c + " group=" + cartGroup[c] + " reason=" + reason + " seated=" + (_seatedCart == c));
            if (exitStation && _seatedCart == c)
            {
                // Codex M3 (2): 途中の停止は、カートが駐車枠 (狭い区間なら出口 + 3 m) へ着いて横に寄せ切ってから外側へ降ろす (PlaceParked)。
                // ワープ・合流の移動 (travel) はすぐ降ろす (この後に暗転と TeleportTo が続く)
                // 到着 (arrive) はカートをプールへ戻すので、その場ですぐ降ろす (カートは降りたのを確かめてから地中へ: MotionTick の ParkCart)
                if (reason == "request") { _deferExitCart = c; _deferExitUntil = Time.time + DeferExitMax; }
                else UnboardCart(c);
            }
            ApplyCore();   // 停止ボタンを先に消す
            SendLeave();
        }

        private void SendLeave()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (Networking.IsOwner(gameObject)) ReceiveLeave(lp.playerId);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqLeave));
        }

        // ============================================================
        // ネットワークの入口 (Owner だけが処理する)
        // ============================================================

        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqStart(int spot, int destination) // net: callable-ok: CallingPlayer で照合 (引数は範囲検査。他人の乗車は作れない)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            ReceiveStart(caller.playerId, spot, destination);
        }

        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqJoin(int spot, int group, int gen) // net: callable-ok: CallingPlayer で照合 (受付中で世代が一致するグループの末尾に自分を足すだけ)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            ReceiveJoin(caller.playerId, spot, group, gen);
        }

        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqStartFrom(int cart, int destination) // net: callable-ok: CallingPlayer で照合 (Parked のカートだけ。乗員は要求者)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            ReceiveStartFrom(caller.playerId, cart, destination);
        }

        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqJoinFrom(int cart, int group, int gen) // net: callable-ok: CallingPlayer で照合 (Parked のカートで、受付中・世代一致のグループの末尾へ)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            ReceiveJoinFrom(caller.playerId, cart, group, gen);
        }

        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqLeave() // net: callable-ok: CallingPlayer で照合 (自分のカートだけを解放する)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            ReceiveLeave(caller.playerId);
        }

        [NetworkCallable(maxEventsPerSecond: 4)]
        public void ReqAccepting(bool on) // net: callable-ok: CallingPlayer で照合 (自分がマスターのグループだけ)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            ReceiveAccepting(caller.playerId, on);
        }

        [NetworkCallable(maxEventsPerSecond: 6)]
        public void ReqSeat(int dHeight, int dYaw, int dFwd) // net: callable-ok: CallingPlayer で照合 (自分が乗っているカートの座席だけ。1 回 ±1 段まで)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            ReceiveSeat(caller.playerId, dHeight, dYaw, dFwd);
        }

        [NetworkCallable(maxEventsPerSecond: 6)]
        public void ReqSeatAuto(int steps) // net: callable-ok: CallingPlayer で照合 (自分が乗っているカートの座席だけ。-SeatAutoMax..+SeatAutoMax に丸める)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            ReceiveSeatAuto(caller.playerId, steps);
        }

        // ============================================================
        // 裁定 (Owner)
        // ============================================================

        private void ReceiveSeat(int pid, int dHeight, int dYaw, int dFwd)
        {
            int c = RiderCart(pid);
            if (c < 0) return;
            int h = Mathf.Clamp(cartSeatSteps[c] + Mathf.Clamp(dHeight, -1, 1), -SeatStepsMax, SeatStepsMax);
            int y = Mathf.Clamp(cartYawSteps[c] + Mathf.Clamp(dYaw, -1, 1), -SeatStepsMax, SeatStepsMax);
            int f = Mathf.Clamp(cartFwdSteps[c] + Mathf.Clamp(dFwd, -1, 1), -SeatStepsMax, SeatStepsMax);
            if (h == cartSeatSteps[c] && y == cartYawSteps[c] && f == cartFwdSteps[c]) return;
            cartSeatSteps[c] = h;
            cartYawSteps[c] = y;
            cartFwdSteps[c] = f;
            Debug.Log("[KH_Cart] seat cart=" + c + " player=" + pid + " height=" + h + " yaw=" + y + " fwd=" + f);
            Commit();
        }

        // BUG-WLD-001: 着座の高さの自動補正 (乗員だけ。絶対値で上書き)
        private void ReceiveSeatAuto(int pid, int steps)
        {
            int c = RiderCart(pid);
            if (c < 0) return;
            int a = Mathf.Clamp(steps, -SeatAutoMax, SeatAutoMax);
            if (a == cartSeatAuto[c]) return;
            cartSeatAuto[c] = a;
            Debug.Log("[KH_Cart] seat-auto apply cart=" + c + " player=" + pid + " steps=" + a);
            Commit();
        }

        private void ReceiveStart(int pid, int spot, int destination)
        {
            int code = CanStart(pid, spot, destination);
            int c = code == CartOk ? ReclaimCandidate() : -1;
            int g = code == CartOk ? PickFreeGroup() : -1;
            if (code == CartOk && (c < 0 || g < 0)) code = CartFull;
            if (code != CartOk)
            {
                Debug.Log("[KH_Cart] reject start player=" + pid + " spot=" + spot + " dest=" + destination + " code=" + code);
                return;
            }
            LogReclaim(c);
            BeginGroup(pid, c, g, SpotStartDistance(spot), destination, spot, "start");
            Commit();
        }

        /// <summary>1 人グループを作って走り出す (乗り場からとカートからの出発の共通部分)。Commit は呼び元。</summary>
        private void BeginGroup(int pid, int c, int g, float start, int destination, int spot, string tag)
        {
            int dir = StartDirection(start, destination);
            groupGen[g] = (groupGen[g] % 1000000) + 1;   // 作成ごとに +1 (古い合流要求を見分ける)
            groupState[g] = GroupRunning;
            groupMaster[g] = c;
            groupDest[g] = destination;
            groupDir[g] = dir;
            groupAccepting[g] = true;
            groupCount[g] = 1;
            groupLane[g] = 0; groupHoldDist[g] = -1f; groupHoldSinceMs[g] = 0; groupHoldBanMs[g] = 0;   // M2: 交通の状態は空から
            groupAnchorDist[g] = Wrap(start);
            groupAnchorMs[g] = Networking.GetServerTimeInMilliseconds() + departDelayMs;
            TakeCart(c, pid, g, 0, spot);
            Debug.Log("[KH_Cart] " + tag + " cart=" + c + " group=" + g + " gen=" + groupGen[g] + " player=" + pid + " spot=" + spot + " start=" + start + " basis=" + (_useTrack ? "track" : "route") + " dest=" + destination
                + " dir=" + dir + " anchor=" + groupAnchorDist[g] + " remaining=" + (IsDestRun(g) ? Remaining(g) : -1f));
            TrafficStartGate(g, start, dir, Networking.GetServerTimeInMilliseconds());   // Codex M2 (3): 狭い区間内の乗り場は対向が抜けるまで出発保留
        }

        /// <summary>カート c を乗員 pid に割り当てる (Idle・Parked どちらからでも。置いてあった印は消す)。</summary>
        private void TakeCart(int c, int pid, int g, int order, int spot)
        {
            cartState[c] = CartRiding;
            cartRider[c] = pid;
            cartGroup[c] = g;
            cartOrder[c] = order;
            cartSpot[c] = spot;
            cartParkDist[c] = 0f;
            cartParkedMs[c] = 0;
            cartParkDir[c] = 0;
        }

        private void LogReclaim(int c)
        {
            if (!ValidCart(c) || cartState[c] != CartParked) return;
            int parkedFor = Networking.GetServerTimeInMilliseconds() - cartParkedMs[c];
            Debug.Log("[KH_Cart] reclaim cart=" + c + " parkedFor=" + (parkedFor * 0.001f).ToString("F1") + "s at=" + cartParkDist[c]);
        }

        private void ReceiveJoin(int pid, int spot, int group, int gen)
        {
            // レビュー #2: 先頭がもう着いているのに OwnerTick が未判定なら、ここで Ending を確定してから裁定する (到着後の合流を通さない)
            bool settled = ValidGroup(group) && SettleArrival(group, Networking.GetServerTimeInMilliseconds());
            if (ValidGroup(group) && groupState[group] != GroupFree && groupGen[group] != gen)
            {
                Debug.Log("[KH_Cart] reject join player=" + pid + " spot=" + spot + " group=" + group + " reason=gen want=" + gen + " now=" + groupGen[group]);
                if (settled) Commit();
                return;
            }
            int code = CanJoin(pid, spot, group, gen);
            int c = code == CartOk ? ReclaimCandidate() : -1;
            if (code == CartOk && c < 0) code = CartFull;
            if (code != CartOk)
            {
                Debug.Log("[KH_Cart] reject join player=" + pid + " spot=" + spot + " group=" + group + " gen=" + gen + " code=" + code);
                if (settled) Commit();
                return;
            }
            LogReclaim(c);
            AddToGroup(pid, c, group, spot, "join");
            Commit();
        }

        /// <summary>カート c をグループの末尾に加える (乗り場からとカートからの合流の共通部分)。Commit は呼び元。</summary>
        private void AddToGroup(int pid, int c, int group, int spot, string tag)
        {
            int order = CountMembers(group);   // 順は 0..count-1 に詰めてある (RemoveCart)。末尾は anchor - dir*order*gap に現れる
            TakeCart(c, pid, group, order, spot);
            groupCount[group] = order + 1;
            Debug.Log("[KH_Cart] " + tag + " cart=" + c + " group=" + group + " player=" + pid + " spot=" + spot + " order=" + order + " count=" + groupCount[group]);
        }

        // ---- M3: その場に残ったカートからの出発・合流 (Owner) ----

        private void ReceiveStartFrom(int pid, int cart, int destination)
        {
            int code = CanStartFrom(pid, cart, destination);
            int g = code == CartOk ? PickFreeGroup() : -1;
            if (code == CartOk && g < 0) code = CartFull;
            if (code != CartOk)
            {
                Debug.Log("[KH_Cart] reject start-from player=" + pid + " cart=" + cart + " dest=" + destination + " code=" + code);
                return;
            }
            BeginGroup(pid, cart, g, cartParkDist[cart], destination, -1, "start-from");
            Commit();
        }

        private void ReceiveJoinFrom(int pid, int cart, int group, int gen)
        {
            bool settled = ValidGroup(group) && SettleArrival(group, Networking.GetServerTimeInMilliseconds());
            int code = CanJoinFrom(pid, cart, group, gen);
            if (code != CartOk)
            {
                Debug.Log("[KH_Cart] reject join-from player=" + pid + " cart=" + cart + " group=" + group + " gen=" + gen + " code=" + code
                    + (ValidGroup(group) && groupGen[group] != gen ? " reason=gen" : ""));
                if (settled) Commit();
                return;
            }
            AddToGroup(pid, cart, group, -1, "join-from");   // カートは暗転で隊列の末尾へ移る (乗り場からの合流と同じ)
            Commit();
        }

        private void ReceiveLeave(int pid)
        {
            int c = RiderCart(pid);
            if (c < 0) { Debug.Log("[KH_Cart] leave ignored player=" + pid + " (not riding)"); return; }
            RemoveCart(c, "request");
            Commit();
        }

        private void ReceiveAccepting(int pid, bool on)
        {
            int c = RiderCart(pid);
            if (c < 0) return;
            int g = cartGroup[c];
            if (!ValidGroup(g) || groupMaster[g] != c || groupState[g] != GroupRunning || groupAccepting[g] == on) return;
            groupAccepting[g] = on;
            Debug.Log("[KH_Cart] accepting group=" + g + " on=" + on + " player=" + pid);
            Commit();
        }

        // ============================================================
        // 判定 (ローカルの先判定と Owner の裁定で同じものを使う)
        // ============================================================

        private int CanStart(int pid, int spot, int destination)
        {
            if (!EnsureRoute()) return CartUnavailable;
            float start = SpotStartDistance(spot);
            bool loop = destination == _loopFwd || destination == _loopRev;
            if (start < 0f || destination < 0 || destination >= _destKeys.Length || (!loop && DestDistance(destination) < 0f)) return CartUnavailable;
            if (RiderCart(pid) >= 0) return CartBusy;
            if (IsHere(start, destination)) return CartHere;
            if (ReclaimCandidate() < 0 || PickFreeGroup() < 0) return CartFull;
            return CartOk;
        }

        private int CanJoin(int pid, int spot, int group, int gen)
        {
            if (!EnsureRoute() || spot < 0 || spot >= SpotCount) return CartUnavailable;
            if (!ValidGroup(group) || groupState[group] != GroupRunning || !groupAccepting[group] || groupGen[group] != gen || CountMembers(group) <= 0) return CartClosed;
            if (RiderCart(pid) >= 0) return CartBusy;
            if (ReclaimCandidate() < 0) return CartFull;
            return CartOk;
        }

        private int CanStartFrom(int pid, int cart, int destination)
        {
            if (!EnsureRoute()) return CartUnavailable;
            if (!ValidCart(cart) || cartState[cart] != CartParked) return CartClosed;   // 先に誰かが使った
            bool loop = destination == _loopFwd || destination == _loopRev;
            if (destination < 0 || destination >= _destKeys.Length || (!loop && DestDistance(destination) < 0f)) return CartUnavailable;
            if (RiderCart(pid) >= 0) return CartBusy;
            if (IsHere(cartParkDist[cart], destination)) return CartHere;
            if (PickFreeGroup() < 0) return CartFull;
            return CartOk;
        }

        private int CanJoinFrom(int pid, int cart, int group, int gen)
        {
            if (!EnsureRoute()) return CartUnavailable;
            if (!ValidCart(cart) || cartState[cart] != CartParked) return CartClosed;
            if (!ValidGroup(group) || groupState[group] != GroupRunning || !groupAccepting[group] || groupGen[group] != gen || CountMembers(group) <= 0) return CartClosed;
            if (RiderCart(pid) >= 0) return CartBusy;
            return CartOk;
        }

        /// <summary>M3 (Probe 用、副作用なし): Parked ならそのカートからの出発距離 (cartParkDist)、それ以外は −1。</summary>
        public float ParkedStartDistance(int cart)
        {
            return ValidCart(cart) && cartState[cart] == CartParked ? cartParkDist[cart] : -1f;
        }

        /// <summary>M3 (Probe 用、副作用なし): 乗り場の出発・合流に使うカート。Idle の空車 (解放から ReuseDelayMs の猶予外) の番号の小さい方 → 無ければ
        /// cartParkedMs が今より ReclaimGraceMs 以上前の Parked のうち最古 → 無ければ −1。</summary>
        public int ReclaimCandidate()
        {
            int idle = PickFreeCart();
            if (idle >= 0) return idle;
            int now = Networking.GetServerTimeInMilliseconds();
            int best = -1, bestAge = -1;
            for (int c = 0; c < MaxCarts; c++)
            {
                if (cartState[c] != CartParked) continue;
                if (carts != null && (c >= carts.Length || carts[c] == null)) continue;
                int age = now - cartParkedMs[c];
                if (age < ReclaimGraceMs) continue;   // 降りたばかり (まだ座っているかもしれない)
                if (age > bestAge) { bestAge = age; best = c; }
            }
            return best;
        }

        /// <summary>その人が乗っているカート (Riding / Ending)。無ければ -1。</summary>
        private int RiderCart(int pid)
        {
            if (pid <= 0) return -1;
            for (int c = 0; c < MaxCarts; c++) if (IsRidingState(cartState[c]) && cartRider[c] == pid) return c;
            return -1;
        }

        /// <summary>空車。解放から ReuseDelayMs 以内の車 (降りた人がまだ座っているかもしれない) は使わない。</summary>
        private int PickFreeCart()
        {
            int now = Networking.GetServerTimeInMilliseconds();
            for (int c = 0; c < MaxCarts; c++)
            {
                if (cartState[c] != CartIdle || cartRider[c] != 0) continue;
                if (carts != null && (c >= carts.Length || carts[c] == null)) continue;   // 配線されていない台は使わない
                if (InReuseDelay(c, now)) continue;   // レビュー #3: 猶予中は候補から外す (代わりに使うこともしない)
                return c;
            }
            return -1;
        }

        /// <summary>解放から ReuseDelayMs 未満 (同期の cartFreedMs。0 = 猶予なし)。int の差なのでサーバー時刻の桁あふれにも耐える。</summary>
        private bool InReuseDelay(int c, int now)
        {
            int freed = cartFreedMs[c];
            if (freed == 0) return false;
            int dt = now - freed;
            return dt >= 0 && dt < ReuseDelayMs;
        }

        private int PickFreeGroup()
        {
            for (int g = 0; g < MaxGroups; g++) if (groupState[g] == GroupFree) return g;
            return -1;
        }

        /// <summary>乗り場 spot (0..4) の周回ノード (KH_LeashManager.spotNodes)。無効なら -1。</summary>
        private int SpotNode(int spot)
        {
            if (spot < 0 || spot >= SpotCount || leash == null) return -1;
            int[] nodes = leash.spotNodes;
            if (nodes == null || spot >= nodes.Length) return -1;
            int node = nodes[spot];
            return node >= 0 && node < _nodeCount ? node : -1;
        }

        private int DestNode(int destination)
        {
            if (_destNodes == null || destination < 0 || destination >= _destNodes.Length) return -1;
            int node = _destNodes[destination];
            return node >= 0 && node < _nodeCount ? node : -1;
        }

        /// <summary>周回 (順/逆) はその向き。それ以外は KH_WalkRoute._Next と同じ短い側 (forward <= 半周 なら +1)。距離は軌道 (無ければ route)。</summary>
        private int StartDirection(float start, int destination)
        {
            if (destination == _loopFwd) return 1;
            if (destination == _loopRev) return -1;
            float to = DestDistance(destination);
            if (to < 0f || start < 0f) return 1;
            float forward = Wrap(to - start);
            return forward <= _L * .5f ? 1 : -1;
        }

        /// <summary>現在地が目的地か。周回は常に false。乗り場と行き先の点は一致しないことがあるので、経路距離 HereDistance 以内を「ここ」とみなす。</summary>
        private bool IsHere(float start, int destination)
        {
            if (destination == _loopFwd || destination == _loopRev) return false;
            float to = DestDistance(destination);
            if (to < 0f || start < 0f) return false;
            float diff = Mathf.Abs(WrapSigned(to - start));
            return diff < HereDistance;
        }

        public const int SeatStepsMax = 6;   // 座席の段数の上限 (高さ ±30 cm、向き ±90°)
        public const int SeatAutoMax = 20;   // BUG-WLD-001: 着座の高さの自動補正の上限 (±1 m。KH_CartSeat.SeatAutoMax と同じ)
        public const int ReclaimGraceMs = 3000;   // M3: 置いてから回収できるまでの猶予 (Probe が参照)
        const int ReuseDelayMs = 3000;   // 解放したカートを他の人へ回すまでの猶予 (サーバー時刻。cartFreedMs で同期)
    }
}
