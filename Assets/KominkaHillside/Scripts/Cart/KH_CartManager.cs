using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M1 おさんぽカートの唯一の権威 (契約: docs/plans/auto-walk-cart/implementation.md)。
    ///
    /// - 既定の Owner のまま (SetOwner しない)。乗り場パネル・停止ボタンの要求は `[NetworkCallable]` の Req* で受け、
    ///   要求者は `NetworkCalling.CallingPlayer` で決める (引数の playerId は持たない)。Owner 自身の要求は Receive* を直接呼ぶ。
    /// - 状態は同期配列 (宣言時に初期化・長さ固定)。変更は Commit (version++ → RequestSerialization → ApplyState)。
    ///   Owner には自分の書き込みの OnDeserialization が来ないので、Commit の中でローカルにも反映する。
    /// - 動きは決定論 (KH_CartManager.Motion.cs): 錨 (groupAnchorDist, groupAnchorMs, groupDir) と共通速度から各クライアントが
    ///   毎フレーム周回上の距離 → 姿勢を計算する。VRCObjectSync は使わない。
    /// - 降車の全経路 (停止ボタン・ジャンプ・ワープ/合流・リスポーン・到着・座れなかった) は LeaveCore 1 本を通る。
    ///
    /// ログは `[KH_Cart] <事象> key=value ...` の 1 行 (start / join / leave / arrive / handover / free / reject / accepting /
    /// board / seated / unseated / unboard / local-leave / owner)。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public partial class KH_CartManager : UdonSharpBehaviour
    {
        public const int MaxCarts = 8, MaxGroups = 8, SpotCount = 5;
        // 要求の結果 (ローカル API の戻り値。Owner からは返さない)
        public const int CartOk = 0, CartBusy = 1, CartFull = 2, CartClosed = 3, CartHere = 4, CartUnavailable = 5;
        public const int CartIdle = 0, CartRiding = 1, CartEnding = 2;
        public const int GroupFree = 0, GroupRunning = 1, GroupEnding = 2;

        // 参照 (非同期。既定は null — Setup が配線する。vrc-udonsharp §4: 参照配列に同じ長さの既定値を持たせない)
        public KH_WalkRoute route;
        public KH_LeashManager leash;
        public KominkaDogRun.KD_Language language;
        public KH_TravelFade fade;
        public KH_CartSeat[] carts;            // MaxCarts 台
        public KH_CartStationPanel[] panels;   // 5 枚 (spot 順)
        public Transform parking;              // 待機カートを置く場所 (地中 / 範囲外)

        [Header("設定 (M1 の既定。Setup は既定のまま)")]
        public float speed = 1.3f, gap = 3f, lateral = .6f, arriveSlack = .5f, smoothTime = .25f, pitchLimit = 8f, lookAhead = 1.5f;
        public int departDelayMs = 3000;

        // 同期 (すべて宣言時に初期化、長さ固定。vrc-networking §5)。「なし」は cartRider = 0、cartGroup / groupMaster = -1
        [UdonSynced] public int[] cartState   = new int[8];   // 0 Idle / 1 Riding / 2 Ending (到着停止へ向かう)
        [UdonSynced] public int[] cartRider   = new int[8];   // playerId、0 = 空
        [UdonSynced] public int[] cartGroup   = new int[] {-1,-1,-1,-1,-1,-1,-1,-1};
        [UdonSynced] public int[] cartOrder   = new int[8];   // グループ内の順 (0 = 先頭)
        [UdonSynced] public int[] cartSpot    = new int[8];   // 開始した乗り場
        [UdonSynced] public int[] groupState  = new int[8];   // 0 Free / 1 Running / 2 Ending
        [UdonSynced] public int[] groupMaster = new int[] {-1,-1,-1,-1,-1,-1,-1,-1};   // cart index
        [UdonSynced] public int[] groupDest   = new int[8];   // 0..5
        [UdonSynced] public int[] groupDir    = new int[8];   // +1 / -1 (loopDistances が増える向きが +1)
        [UdonSynced] public bool[] groupAccepting = new bool[8];
        [UdonSynced] public float[] groupAnchorDist = new float[8];   // 先頭 (order 0) の経路距離 [0, loopLength)
        [UdonSynced] public int[] groupAnchorMs   = new int[8];       // Networking.GetServerTimeInMilliseconds() の値
        [UdonSynced] public int[] groupCount  = new int[8];
        [UdonSynced] public int version;                              // 変更ごとに +1 (パネルの再描画と検査用)
        // レビュー 2026-10-11 #3/#4: 空車の再利用猶予 (解放したサーバー時刻 ms、0 = 猶予なし) とグループの世代 (作成ごとに +1)。
        // Owner のローカルでなく同期に置くので、Owner 交代後も猶予と世代の照合が保たれる
        [UdonSynced] public int[] cartFreedMs = new int[8];
        [UdonSynced] public int[] groupGen    = new int[8];
        // M1.1: 座席の調整 (乗っている本人だけが ReqSeat で変える。降車で 0)。高さ 1 段 = 5 cm、向き 1 段 = 15°、どちらも -6..+6
        [UdonSynced] public int[] cartSeatSteps = new int[8];
        [UdonSynced] public int[] cartYawSteps  = new int[8];
        [UdonSynced] public int[] cartFwdSteps  = new int[8];   // 前後 (1 段 = 5 cm、+ が前、-6..+6。2026-10-12 追加要望)
        // M2 交通 (KH_CartManager.Traffic.cs)。Owner が書く
        [UdonSynced] public int[] groupLane = new int[8];                                         // 0 千鳥 / 1 一列・右寄せ / 2 一列・中央
        [UdonSynced] public float[] groupHoldDist = new float[] {-1f,-1f,-1f,-1f,-1f,-1f,-1f,-1f}; // 先頭の距離の上限 (−1 = なし)
        [UdonSynced] public int[] groupHoldSinceMs = new int[8];                                  // 待機を始めたサーバー時刻

        // M2 交通の地形 (非同期。既定 null — Cart/1 Build が軌道から焼く。null なら交通なし)
        public int[] trackClass;                     // 区間 i: 0 広い (千鳥可) / 1 一列なら双方通れる / 2 一列でも通れない
        public float[] narrowFrom, narrowTo;         // class 2 の連続区間 (軌道距離)
        public float[] waitBefore, waitAfter;        // 各区間の待機点 (順方向から入る側 / 逆方向から入る側)

        [Header("M2 調整値 (Data/Cart/tuning.json から Build が書く)")]
        public float approach = 25f, holdTimeoutSec = 60f, laneOffset = 0.55f, seatStep = 0.05f, yawStep = 15f;
        public float groundSmoothTime = 0.08f;       // 接地の高さの平滑化 (M2 接地 v3。既存の smoothTime はピッチに使う)

        // M1.1: カート専用の軌道 (非同期。既定 null — Setup (Cart/1) が route の節 0 を除いた閉路から書く。null なら route を使う)
        public Vector3[] trackPositions;
        public float[] trackDistances;
        public float trackLength;
        public bool[] trackDetour;                   // 区間 i (trackPositions[i] → [i+1]) で横ずれ可か
        public float[] noLateralFrom, noLateralTo;   // 追加で横ずれ 0 にする距離範囲 (ベンチ・門枠)
        public float[] spotTrackDistance;            // 乗り場 0..4 の開始距離
        public float[] destTrackDistance;            // 行き先 0..5 の到着距離 (周回 2/3 は -1)

        // ---- ローカル (同期しない) ----
        private int _myCart = -1;        // ApplyState が決める「自分が cartRider のカート」(-1 = なし)
        private int _leaveCart = -1;     // 降車を送ったカート (ReqLeave が反映されるまで。二重呼び出し・OnStationExited の再入を止める)
        private float _leaveSentAt;
        private int _leaveTries;
        private bool _leaveSendNow;      // 遅れた割当の返却を次の LocalTick で送る
        private int _seatedCart = -1;    // OnStationEntered/Exited で知る「ローカルプレイヤーが実際に座っているカート」
        private int _unboardCart = -1;   // 最後に ExitStation したカートと時刻 (重ね打ちを止める)
        private float _unboardAt = -1000f;
        private float _boardAt;          // 割当を受けて着座を始めた時刻 (座れなかったときの見張り)
        private int _fadeCart = -1;      // 合流の暗転を待っているカート
        private float _fadeAt;
        // 保留中の要求 (レビュー #1)。_pendingKind != 0 の間だけ有効。時間切れ・_LeaveForTravel・リスポーンで取り消す (_pendingKind = 0)。
        // 自分への新しい割当は、有効な保留要求と乗り場・グループ (合流は世代も) が合うときだけ着座し、それ以外は ReqLeave で返す
        private int _pendingKind;        // 0 なし / 1 出発 / 2 合流 (2 秒の返事待ち)
        private float _pendingAt;
        private int _pendingSpot = -1, _pendingGroup = -1, _pendingGen = -1;
        private int _reqSeq;             // 要求のローカル連番 (ログ用。割当は ID を運ばないので照合は乗り場・グループ・世代で行う)
        private float _nextOwnerTick, _nextMaintenance, _nextLocalSlow, _exitPickAt;
        private string _lang = "";
        private int _panelVersion = -1, _panelDest = -2;   // パネルを最後に書いたときの version と自分の行き先
        private bool _started;

        public const string KeyStarted = "cart.result.started", KeyJoined = "cart.result.joined", KeyLeft = "cart.result.left",
            KeyFull = "cart.result.full", KeyClosed = "cart.result.closed", KeyHere = "cart.result.here", KeyBusy = "cart.result.busy";
        // 行き先の呼び名 (行き先の値 0..5 の順。KH_CourseMenuEditor の Destinations/Keys を値の順に並べ替えたもの)
        private string[] _destKeys = { "KD_TreatPouch.WordWalkGate", "KD_TreatPouch.WordWalkPlaza", "course.walk.forward", "course.walk.reverse", "course.walk.upper", "course.walk.river" };

        void Start()
        {
            EnsureLocals();
            EnsureRoute();
            _started = true;
            // vrc-udonsharp §6: Start の順は保証されないので、末尾で現在の状態から表示を作り直す (冪等)
            ApplyState();
            for (int c = 0; c < MaxCarts; c++) if (!_shown[c]) ParkCart(c);
        }

        public override void OnDeserialization() { ApplyState(); }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            Debug.Log("[KH_Cart] owner player=" + (Utilities.IsValid(player) ? player.playerId : -1) + " version=" + version);
        }

        // Owner だけ: 退出した人のカートを解放する (Owner 交代と重なった取りこぼしは OwnerTick の見張りが拾う)
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(player)) return;
            bool changed = false;
            for (int c = 0; c < MaxCarts; c++)
                if (cartState[c] != CartIdle && cartRider[c] == player.playerId) { RemoveCart(c, "left"); changed = true; }
            if (changed) Commit();
        }

        // リスポーンは降車優先。座ったままの ExitStation はリスポーン先を exit 位置で上書きしうるので、ここでは station に触れない
        public override void OnPlayerRespawn(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            CancelPending("respawn");
            if (_IsRiding()) LeaveCore("respawn", false);
        }

        void Update()
        {
            if (!_started) return;
            MotionTick();
            LocalTick();
            if (Networking.IsOwner(gameObject) && Time.time >= _nextOwnerTick)
            {
                _nextOwnerTick = Time.time + 0.25f;
                OwnerTick();
            }
        }

        // ============================================================
        // 問い合わせ (ローカル)
        // ============================================================

        public bool _IsRiding() { return _myCart >= 0 && _leaveCart != _myCart; }
        public int _MyCart() { return _IsRiding() ? _myCart : -1; }
        public int _MyGroup() { int c = _MyCart(); return c >= 0 ? cartGroup[c] : -1; }
        public bool _IsMaster()
        {
            int c = _MyCart();
            if (c < 0) return false;
            int g = cartGroup[c];
            return g >= 0 && g < MaxGroups && groupMaster[g] == c;
        }
        public int _GroupDestination(int g) { return ValidGroup(g) ? groupDest[g] : -1; }
        public bool _GroupAccepting(int g) { return ValidGroup(g) && groupAccepting[g]; }
        public int _GroupState(int g) { return ValidGroup(g) ? groupState[g] : GroupFree; }
        public int _GroupCount(int g) { return ValidGroup(g) ? groupCount[g] : 0; }
        public int _GroupGen(int g) { return ValidGroup(g) ? groupGen[g] : -1; }
        /// <summary>M2: 自分のグループが交通で待っているか (操作帯の「すれ違い待ち」)。</summary>
        public bool _MyGroupWaiting() { int g = _MyGroup(); return ValidGroup(g) && groupHoldDist[g] >= 0f; }

        // ============================================================
        // 犬用 API (M2。KD_DogWanderController.HillsideLeash が呼ぶ。ローカルの計算だけで副作用なし)
        // ============================================================

        /// <summary>その人がカートに乗っているか (Riding / Ending)。</summary>
        public bool _IsPlayerRiding(int playerId) { return RiderCart(playerId) >= 0; }

        /// <summary>その人のカートの前方 0.9 m、横 slot 0: −0.45 / 1: +0.45 / 2: 0 (進行方向の右が +)。一列走行 (groupLane != 0) では横を半分に。
        /// カートが無い・まだ表示していなければ Vector3.zero。slot は 0..2 に収める。</summary>
        public Vector3 _DogAnchor(int playerId, int slot)
        {
            int c = RiderCart(playerId);
            if (c < 0 || _cartT == null || _shown == null || _cartT[c] == null || !_shown[c]) return Vector3.zero;
            Transform t = _cartT[c];
            Vector3 f = t.forward; f.y = 0f;
            f = f.sqrMagnitude < 0.0001f ? Vector3.forward : f.normalized;
            Vector3 r = new Vector3(f.z, 0f, -f.x);
            float side = slot <= 0 ? -DogSide : (slot == 1 ? DogSide : 0f);
            int g = cartGroup[c];
            if (ValidGroup(g) && groupLane[g] != LaneStagger) side *= 0.5f;
            return t.position + f * DogAhead + r * side;
        }

        /// <summary>その人のカートの水平の前向き (単位)。カートが無ければ Vector3.zero。</summary>
        public Vector3 _CartForward(int playerId)
        {
            int c = RiderCart(playerId);
            if (c < 0 || _cartT == null || _shown == null || _cartT[c] == null || !_shown[c]) return Vector3.zero;
            Vector3 f = _cartT[c].forward; f.y = 0f;
            return f.sqrMagnitude < 0.0001f ? Vector3.zero : f.normalized;
        }

        const float DogAhead = 0.9f, DogSide = 0.45f;
        public int _GroupMasterPlayerId(int g)
        {
            if (!ValidGroup(g)) return 0;
            int m = groupMaster[g];
            return m >= 0 && m < MaxCarts ? cartRider[m] : 0;
        }
        /// <summary>自分のさんぽの行き先 (犬の散歩設定と共用)。leash が無ければ -1。</summary>
        public int _MyDestination() { return leash != null ? leash._MyChoice() : -1; }
        /// <summary>行き先の値 0..5 の文言キー (範囲外は "")。</summary>
        public string _DestinationKey(int destination)
        {
            return destination >= 0 && destination < _destKeys.Length ? _destKeys[destination] : "";
        }

        private bool ValidGroup(int g) { return g >= 0 && g < MaxGroups; }
        private bool ValidCart(int c) { return c >= 0 && c < MaxCarts; }

        // ============================================================
        // 反映 (Owner の Commit と OnDeserialization の共通口)
        // ============================================================

        private void Commit()
        {
            version++;
            RequestSerialization();
            ApplyState();
        }

        /// <summary>同期状態から、自分の着座・降車、全カートの可視と駐車、停止ボタン、全パネルを作り直す。冪等。</summary>
        private void ApplyState()
        {
            ApplyCore();
            RefreshPanels(true);
        }

        private void ApplyCore()
        {
            EnsureLocals();
            VRCPlayerApi lp = Networking.LocalPlayer;
            int me = Utilities.IsValid(lp) ? lp.playerId : -1;
            int mine = -1;
            if (me > 0)
                for (int c = 0; c < MaxCarts; c++)
                    if (cartState[c] != CartIdle && cartRider[c] == me) { mine = c; break; }

            int now = Networking.GetServerTimeInMilliseconds();
            // 可視と駐車 (自分の着座より先に姿勢を合わせる)
            for (int c = 0; c < MaxCarts; c++)
            {
                bool active = CartActive(c);
                if (active && !_shown[c])
                {
                    _shown[c] = true; _parked[c] = false; _snap[c] = true; _placedAt[c] = 0f; _seg[c] = -1;
                    if (carts != null && c < carts.Length && carts[c] != null) carts[c]._SetVisible(true);
                }
                else if (active)
                {
                    // 錨の打ち直し・隊列の詰めで目標が跳んだ分を、表示側でなめらかに追う (Motion の _err)
                    if (!_snap[c])
                    {
                        float jump = WrapSigned(TargetDistance(c, now) - _dLast[c]);
                        if (Mathf.Abs(jump) > SnapJump) _snap[c] = true; else _err[c] = jump;
                    }
                }
                else if (_shown[c])
                {
                    _shown[c] = false;
                    _parkAt[c] = Time.time + ParkDelay;   // 降りる人の ExitStation が先に済むよう、少しその場に止めてから駐車
                }
            }

            if (mine != _myCart)
            {
                int prev = _myCart;
                _myCart = mine;
                if (prev >= 0)
                {
                    if (_fadeCart == prev) { _fadeCart = -1; if (fade != null) fade._Release(); }
                    // 自分で降りた (LeaveCore) なら station の扱いはそちらで決めた (リスポーンは触れない)。Owner に外されたときだけここで降ろす
                    if (_seatedCart == prev && _leaveCart != prev) UnboardCart(prev);
                    Debug.Log("[KH_Cart] released cart=" + prev + " player=" + me);
                }
                _leaveCart = -1; _leaveTries = 0;
                if (mine >= 0)
                {
                    if (!PendingMatches(mine))
                    {
                        // 取り消した・時間切れの要求への遅れた割当: 座らずに返す (レビュー #1)
                        Debug.Log("[KH_Cart] late-assign returned cart=" + mine + " group=" + cartGroup[mine] + " spot=" + cartSpot[mine]
                            + " pendingKind=" + _pendingKind + " req=" + _reqSeq);
                        // 送信は次の LocalTick (Owner 自身だと SendLeave → Commit → ApplyCore の入れ子になり、Udon のローカル変数を壊すため)
                        _leaveCart = mine; _leaveSentAt = Time.time; _leaveTries = 0; _leaveSendNow = true;
                    }
                    else
                    {
                        bool join = _pendingKind == 2;
                        ShowResult(_pendingSpot, join ? KeyJoined : KeyStarted);
                        _pendingKind = 0;
                        _boardAt = Time.time;
                        if (join) BeginJoinBoard(mine); else BoardNow(mine, "direct");
                    }
                }
            }
            // 自分の座っていたカートが他人に割り当て直された (解放直後の再利用) なら、運ばれる前に降りる
            if (_seatedCart >= 0 && _seatedCart != _myCart && cartState[_seatedCart] != CartIdle) UnboardCart(_seatedCart);

            // 停止・受付ボタンは自分のカートの乗員にだけ出す
            for (int c = 0; carts != null && c < carts.Length && c < MaxCarts; c++)
            {
                if (carts[c] == null) continue;
                bool isMine = c == _myCart && _leaveCart != c;
                int g = cartGroup[c];
                bool master = isMine && ValidGroup(g) && groupMaster[g] == c && groupState[g] == GroupRunning;
                carts[c]._SetRider(isMine, master, ValidGroup(g) && groupAccepting[g]);   // 操作帯の _Refresh とドックの開始/停止も中で
                carts[c]._ApplySeat(cartSeatSteps[c], cartYawSteps[c], cartFwdSteps[c]);
            }
        }

        private bool CartActive(int c)
        {
            if (cartState[c] == CartIdle) return false;
            int g = cartGroup[c];
            return ValidGroup(g) && groupState[g] != GroupFree;
        }

        private void RefreshPanels(bool all)
        {
            if (panels == null) return;
            _panelVersion = version;
            _panelDest = _MyDestination();
            VRCPlayerApi lp = Networking.LocalPlayer;
            Vector3 me = Utilities.IsValid(lp) ? lp.GetPosition() : Vector3.zero;
            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null) continue;
                if (!all && Utilities.IsValid(lp) && (panels[i].transform.position - me).sqrMagnitude > PanelNear * PanelNear) continue;
                panels[i]._Refresh();
            }
        }

        private void ShowResult(int spot, string key)
        {
            if (panels == null || spot < 0 || spot >= panels.Length || panels[spot] == null) return;
            panels[spot]._ShowResult(key);
        }

        // ============================================================
        // ローカルの毎フレーム (着座の見張り・到着・降車の再送・返事待ち・暗転・言語)
        // ============================================================

        private void LocalTick()
        {
            if (_fadeCart >= 0) TickJoinFade();

            if (_pendingKind != 0 && Time.time - _pendingAt > PendingTimeout)
            {
                // 空車なし等で Owner が黙って断った (結果コードは返らない。契約の既知の制約)
                string key = KeyFull;
                if (_pendingKind == 2 && (!ValidGroup(_pendingGroup) || groupState[_pendingGroup] != GroupRunning || !groupAccepting[_pendingGroup])) key = KeyClosed;
                Debug.Log("[KH_Cart] request timeout req=" + _reqSeq + " kind=" + _pendingKind + " spot=" + _pendingSpot + " group=" + _pendingGroup + " result=" + key);
                ShowResult(_pendingSpot, key);
                _pendingKind = 0;   // 取消。この後に届く割当は late-assign として返す
            }

            if (_leaveSendNow)
            {
                _leaveSendNow = false;
                if (_leaveCart >= 0 && _leaveCart == _myCart) SendLeave();
            }

            if (_IsRiding())
            {
                int c = _myCart;
                // 到着: 自分のカートが目的地距離に達したら、自分で降りる (1 回だけ。LeaveCore が _leaveCart で止める)
                int g = cartGroup[c];
                if (ValidGroup(g) && IsDestRun(g) && _shown[c] && Mathf.Abs(_err[c]) < 0.5f)
                {
                    int now = Networking.GetServerTimeInMilliseconds();
                    if (TravelOf(c, now) >= Remaining(g) - arriveSlack) { LeaveCore("arrive", true); return; }
                }
                // 座れなかった (他の Station に座っていた・UseStation が通らなかった): 3 秒で割当を返す
                if (_seatedCart != c && _fadeCart < 0 && Time.time - _boardAt > BoardTimeout)
                {
                    Debug.Log("[KH_Cart] board failed cart=" + c);
                    LeaveCore("board-failed", false);
                    return;
                }
                // ジャンプで降りても立てる側へ出られるよう、座っている間は exit 側を選び直しておく
                if (_seatedCart == c && Time.time >= _exitPickAt && carts != null && c < carts.Length && carts[c] != null)
                {
                    _exitPickAt = Time.time + 0.5f;
                    carts[c]._PickExit();
                }
            }

            // 降車の要求が Owner に届かなかった (Owner 交代など) ときの再送
            if (_leaveCart >= 0 && _leaveCart == _myCart && Time.time - _leaveSentAt > LeaveResend && _leaveTries < 5)
            {
                _leaveTries++;
                _leaveSentAt = Time.time;
                Debug.Log("[KH_Cart] leave resend cart=" + _leaveCart + " try=" + _leaveTries);
                SendLeave();
            }

            // 座っているカートが自分の乗車でなくなったのに座ったまま (取りこぼし・リスポーン後の居残り) なら、1.5 秒おきに降ろす
            // (降車を送ってから 1.5 秒は待つ: リスポーンが自分で station から外すのを先に受ける)
            if (_seatedCart >= 0 && _seatedCart != _MyCart() && Time.time - _unboardAt > 1.5f && Time.time - _leaveSentAt > 1.5f)
                UnboardCart(_seatedCart);

            if (Time.time < _nextLocalSlow) return;
            _nextLocalSlow = Time.time + 1f;
            string lang = language != null ? language.CurrentLanguage : "";
            bool langChanged = lang != _lang;
            _lang = lang;
            if (langChanged)
            {
                for (int c = 0; carts != null && c < carts.Length; c++) if (carts[c] != null) carts[c]._RefreshText();
            }
            // 安全網: 取りこぼした反映を 1 秒ごとに突き合わせる (冪等。他 Behaviour の呼び出しは変化した所だけ書く)
            ApplyCore();
            // パネルは状態 (version)・言語・自分の行き先が変わったときだけ書き直す (2026-10-12 コスト削減。文字列連結・_Text・displayName を毎回しない)
            int dest = _MyDestination();
            if (langChanged || dest != _panelDest || version != _panelVersion) RefreshPanels(true);
        }

        // ============================================================
        // 着座と降車 (ローカル)
        // ============================================================

        private void BoardNow(int c, string how)
        {
            if (carts == null || c >= carts.Length || carts[c] == null) return;
            PlaceCart(c, Networking.GetServerTimeInMilliseconds(), 0f);   // 割当の直後は駐車位置にいるので、先に姿勢を合わせる
            carts[c]._Board();
            Debug.Log("[KH_Cart] board cart=" + c + " how=" + how + " order=" + cartOrder[c] + " group=" + cartGroup[c]);
        }

        // 合流: 短く暗転して着座 (KH_TravelFade の既存 API。ワープの暗転中・未配線なら暗転なし)
        private void BeginJoinBoard(int c)
        {
            if (fade == null || fade._Busy()) { BoardNow(c, "join-direct"); return; }
            fade._Begin();
            _fadeCart = c; _fadeAt = Time.time;
        }

        private void TickJoinFade()
        {
            int c = _fadeCart;
            if (c != _myCart || !_IsRiding()) { _fadeCart = -1; if (fade != null) fade._Release(); return; }
            if (fade == null) { _fadeCart = -1; BoardNow(c, "join-direct"); return; }
            bool black = fade._IsBlack();
            if (!black && fade._Busy() && Time.time - _fadeAt < 4f) return;
            _fadeCart = -1;
            _boardAt = Time.time;
            BoardNow(c, black ? "join-fade" : "join-fade-timeout");
            fade._Release();
        }

        // ExitStation は OnStationExited が届くまで 1 秒に 1 回まで (Owner 自身の降車では LeaveCore → Commit → ApplyCore が同じフレームで重なる)
        private void UnboardCart(int c)
        {
            if (carts == null || c < 0 || c >= carts.Length || carts[c] == null) return;
            if (_unboardCart == c && Time.time - _unboardAt < 1f) return;
            _unboardCart = c; _unboardAt = Time.time;
            carts[c]._Unboard();
        }

        // KH_CartSeat から (ローカルプレイヤーだけ)
        public void _OnSeated(int c)
        {
            if (!ValidCart(c)) return;   // 配線ミスの index で配列外 → halted を起こさない
            _seatedCart = c;
            _exitPickAt = 0f;
            Debug.Log("[KH_Cart] seated cart=" + c);
        }

        public void _OnUnseated(int c)
        {
            if (!ValidCart(c)) return;
            if (_seatedCart == c) _seatedCart = -1;
            Debug.Log("[KH_Cart] unseated cart=" + c + " riding=" + _IsRiding());
            // ジャンプ等で自分から降りた: 割当を返す (station には触れない。冪等)
            if (_IsRiding() && _myCart == c) LeaveCore("exit", false);
        }

        // ---- 調整値 (コードの定数。Play で決める) ----
        const float SnapJump = 15f;          // 目標がこれより跳んだら表示も跳ばす (それ以下はなめらかに追う)
        const float ParkDelay = 1.0f;        // 空いたカートを駐車へ移すまでの間 (降りる人の ExitStation を先に済ませる)
        const float PendingTimeout = 2f;     // 出発・合流の返事待ち (契約: 2 秒で「空いているカートがありません」)
        const float BoardTimeout = 3f;       // 割当から着座までの見張り
        const float LeaveResend = 3f;        // 降車の要求の再送間隔
        const float PanelNear = 30f;         // 1 秒ごとの更新をこの距離のパネルに限る
        const float HereDistance = 15f;      // 現在地が目的地とみなす経路距離 (KH_LeashManager の TravelHereDistance と同じ)
    }
}
