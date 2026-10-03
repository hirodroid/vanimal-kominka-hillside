using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-TRT-001 Phase 4b: おやつの**ルールの正本**。
    /// 設計は [IMP-TRT-001](../../../docs/development/issues/IMP-TRT-001-treat-system.md)
    /// の「Phase 4b の確定設計」。
    ///
    /// 一文で: **どの犬がどの枠のおやつへ向かうかを決め、食べ始めを配る。それだけ。**
    ///
    /// ## なぜ「出す側のルール」までここに置くのか
    ///
    /// 門番 (<see cref="DispenseGateWith"/>) と手順
    /// (<see cref="StepDispenseWith"/>) は**出す人のローカルな話**なので、
    /// 素直には <see cref="KD_TreatPouch"/> (手元メニュー) に置きたくなる。置かない理由:
    ///
    /// > **ルールが 1 箇所にあれば、UI の表示と実行の可否が乖離しようがない。**
    ///
    /// 手元メニューの文言も、実際に出せるかの判断も、**同じ関数を読む**。
    /// 「表示は出せると言っているのに押すと何も起きない」が構造的に作れなくなる
    /// (デバッグ盤が算術を 1 行も書き写していないのと同じ規律 — IMP-PRG-001)。
    /// **状態そのもの**は個人のものなので <see cref="KD_TreatPouch"/> が持つ。
    ///
    /// ## 権威 — 書き手を交差させない
    ///
    /// <code>
    /// KD_TreatSlot_{i}  枠の占有 / 種類 / 出した時刻   ← その時点の Owner (通常 giver)
    /// KD_TreatBox       どの犬がどの枠へ / 食べ始め     ← **マスター固定** (SetOwner を呼ばない)
    /// KD_Treat_{i}      位置と保持だけ                  ← Owner (= giver) + VRCObjectSync
    /// </code>
    ///
    /// 犬 4 匹の Owner は [IMP-NET-005](../../../docs/development/issues/IMP-NET-005-owner-authoritative-dog.md)
    /// でマスター固定になった。だから **「Claim の権威は犬の Owner」= 全部同じ 1 クライアント**で、
    /// 「2 匹が同フレームで同じおやつを Claim しうる」という**クライアント跨ぎの競合は存在しない。**
    /// 残るのは 1 パス内で 4 匹に枠を配る順序問題だけで、それが
    /// <see cref="ResolveClaimsWith"/> — **純関数なので Play を使わずに検査できる。**
    ///
    /// ## `outMs` が世代番号を兼ねる
    ///
    /// 枠は再利用される。犬が枠番号だけを覚えていると、
    /// **食べ終わった枠に別の人が次のおやつを出した瞬間、古い Claim がそれへ黙って継承される。**
    /// だから犬は `(枠, outMs)` の対で掴む。同時に **`outMs` の昇順が「先に出された順」**
    /// になるので、不変条件 4 (先着順・切り替えない) の入力にもそのまま使える。
    /// </summary>
    /// <remarks>
    /// SyncMode Manual・**専用 GameObject**。同期は 12 int (犬 4 × 3 本)。
    ///
    /// **`Networking.SetOwner` をこのオブジェクトに対して呼ばない。**
    /// マスター固定 = 呼ばないことが仕様で、`check-sync-anchors.js` が機械検査する
    /// (犬と同じ規律 — `KD_DogStopAnchor` が言語化している)。
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KD_TreatBox : UdonSharpBehaviour
    {
        // ============================================================
        // 枠の状態 (KD_TreatSlot が持つ値から導出される)
        // ============================================================

        /// <summary>誰も使っていない。</summary>
        public const int SlotFree = 0;
        /// <summary>枠は取れたが、**まだ消費が確定していない。** 犬は向かわない。</summary>
        public const int SlotReserved = 1;
        /// <summary>実体が出て消費も済んだ。**犬が向かえる唯一の状態。**</summary>
        public const int SlotOut = 2;
        /// <summary>定位置へ戻す途中 (Respawn の再試行中)。</summary>
        public const int SlotReturning = 3;

        // ============================================================
        // 出せるか / 出せないなら理由は何か
        //
        // **手元メニューの文言も実行の可否も、両方これを読む。**
        // 「無言で失敗しない」(完了条件) はこの一本化で担保する。
        // ============================================================

        public const int GateOk = 0;
        /// <summary>台帳の復元待ち。**0 個と区別する** — 「消えた」に見えるため。</summary>
        public const int GateLoading = 1;
        /// <summary>その種類の在庫が 0。</summary>
        public const int GateNoStock = 2;
        /// <summary>自分がもう 1 個出している (1 人 1 個)。</summary>
        public const int GateAlreadyOut = 3;
        /// <summary>枠が満杯 = **4 人目**。完了条件「無言で失敗しない」の主役。</summary>
        public const int GateSlotsFull = 4;

        // ---- ここから下は**押した瞬間には分からない失敗** ----
        //
        // 押す前の判定 (上の 5 つ) と混ぜない。**射程が違う** —
        // 「出せるか」は事前に一本化できるが、枠の取り合いと消費の失敗は
        // ネットワークの往復のあとにしか分からない (2026-08-24 Codex 指摘)。

        /// <summary>枠の取り合いに負けた / Ownership が来ない。**在庫は減らない。**</summary>
        public const int GateOwnerFailed = 5;
        /// <summary>実体は出たが消費に失敗した。**実体を回収して在庫も減らさない。**</summary>
        public const int GateCommitFailed = 6;
        /// <summary>犬が全員 催し (Live の観客 / 集合 / かけっこ / じゃれ / Candy 演目) に拘束されていて、出しても来ない
        /// (破壊的監査 v4 FUNC-07/BUG-01 — 来ない犬に枠を配り、在庫だけ減って 60 秒で回収されていた)。
        /// 押す前の判定。<see cref="DispenseGateWith"/> の純関数には混ぜず、呼び出し側が <see cref="AllDogsEngaged"/> で足す。
        /// 文言は「いま出せません」(WordSlotsFull) を共用する。</summary>
        public const int GateDogsBusy = 7;

        // ============================================================
        // 出す手順 (KD_TreatPouch が状態を持ち、遷移はここが決める)
        // ============================================================

        public const int DispIdle = 0;
        /// <summary>枠と実体の Ownership を要求して待っている。</summary>
        public const int DispReserving = 1;
        /// <summary>枠を確保した。実体を手元へ出しているところ。**まだ在庫は減っていない。**</summary>
        public const int DispReserved = 2;
        /// <summary>実体が出た。**在庫を減らす唯一の瞬間**の直後で、結果待ち。</summary>
        public const int DispCommitting = 3;
        /// <summary>出ている。次は出せない。</summary>
        public const int DispOut = 4;
        /// <summary>回収中。</summary>
        public const int DispRecalling = 5;

        // ---- あげる (IMP-TRT-003。手に吸着したおやつを Use で放す) ----

        /// <summary>何もしない。</summary>
        public const int GiveIgnore = 0;
        /// <summary>**押し下がりを覚える。** これが立っていない離しは捨てる。</summary>
        public const int GiveArm = 1;
        /// <summary>**放す = あげる。**</summary>
        public const int GiveRelease = 2;
        /// <summary>武装を落とす (吸着していないので、この Use は無関係)。</summary>
        public const int GiveDisarm = 3;

        public const int EvPress = 0;
        public const int EvOwnerGranted = 1;
        public const int EvOwnerFailed = 2;
        /// <summary>実体を出せた。**このイベントだけが在庫を減らす。**</summary>
        public const int EvPlaced = 3;
        public const int EvConsumeOk = 4;
        public const int EvConsumeFailed = 5;
        public const int EvRecalled = 6;
        public const int EvTimeout = 7;

        // ============================================================
        // 配線と同期状態 (Phase 4b Step 4)
        //
        // **マスター固定。`Networking.SetOwner` をこのオブジェクトに対して呼ばない。**
        // 犬と同じ規律で、`check-sync-anchors.js` が
        // 「`KD_TreatBox` への SetOwner が 0 件」を機械検査する。
        // だから **Owner == インスタンスマスター**が構造的に保たれ、
        // 「Claim を決める人」が 1 人しか居ない状態が壊れようがない。
        // ============================================================

        [Header("配線 (Tools/KominkaDogRun/Treat System が設定する)")]
        [Tooltip("枠 3 つ。**添字 = slotIndex**")]
        public KD_TreatSlot[] slots;

        [Tooltip("犬の名簿。null だと誰も食べに来ない (機能ごと無効)")]
        public KD_DogPack pack;

        [Header("調整")]
        [Tooltip("秒。食べている時間。終わりは dogEatMs + これ で全クライアントが同じ時刻を導出する")]
        public float eatDuration = DefaultEatDuration;

        [Tooltip("秒。**食べ終わった犬が次の枠を受け持てるまでの間。**\n" +
            "犬の返事 (KD_DogWanderController.treatThanksDuration) より長くすること — " +
            "返事の途中で次のおやつへ走り出すと「誰への返事か」が濁る (不変条件 4)。\n" +
            "Phase 1 の assignCooldown (世界に 1 個・全体で 1 本) の**犬ごと版**で、" +
            "Verify が犬 4 匹ぶん機械検査する")]
        public float thanksCooldown = DefaultThanksCooldown;

        const float DefaultEatDuration = 2.6f;
        const float DefaultThanksCooldown = 2.6f;

        /// <summary>実体がこの距離 (m) 以上動いた突き合わせは「まだ転がっている」。
        /// **食べ始めの床** — 投げられて転がっている物へ口を付けると、
        /// 食べポーズのまま景色が流れる。<see cref="ReconcileInterval"/> あたりの移動量なので、
        /// 0.04m / 0.25s ≒ 0.16 m/s (Phase 1 の restingSpeed 0.15 と同じ水準)。</summary>
        const float SettleStep = 0.04f;

        /// <summary>マスターの突き合わせ間隔 (秒)。毎フレーム全犬 × 全枠の距離を測らない。</summary>
        const float ReconcileInterval = 0.25f;

        // ---- 同期 12 int (犬 4 × 3 本) ----
        //
        // **配列は宣言でも Start でも初期化する。** 未初期化の同期配列があると
        // **Behaviour 全体の同期が止まる** (vrc-networking §5)。
        // さらに **既定値を 0 にしない** — `dogSlot` の 0 は「枠 0 を掴んでいる」に読めるので、
        // KD_ToyBox が踏んだ「既定値と最初の実値が同じで OnDeserialization が飛ばない」と
        // 同じ罠になる。-1 埋めが「誰も掴んでいない」。

        /// <summary>犬 d が掴んだ枠 (-1 = 無し)。</summary>
        [UdonSynced] private int[] dogSlot = new int[0];
        /// <summary>そのとき掴んだ枠の世代 (= `outMs`)。**枠の再利用を見抜く鍵。**</summary>
        [UdonSynced] private int[] dogSlotOutMs = new int[0];
        /// <summary>食べ始め (サーバー ms)。0 = まだ。**書くのはマスターだけ**
        /// (<see cref="_ReportArrived"/>)。食べ終わりは + eatDuration で全員が導出する。</summary>
        [UdonSynced] private int[] dogEatMs = new int[0];

        private int _dogCount;
        private int _slotCount;
        private float _nextReconcile;

        // 名簿は Start で 1 度だけ読んでローカルへ持つ (KD_DogWanderController と同じ扱い)
        private Transform[] _packRoots;
        private KD_DogFetchController[] _packFetch;
        private KD_DogWanderController[] _packWander;

        // 突き合わせ用の作業配列 (毎回 new しない — Udon VM)
        private int[] _slotState;
        private int[] _slotOutMs;
        private int[] _dogEligible;
        private int[] _distMm;
        private int[] _outSlot;
        private int[] _outOutMs;

        // ---- マスターのローカル観測 (同期しない) ----
        //
        // **どれも「マスターが自分の画面で見たこと」で、結果は同期変数で配る。**
        // 読むのが 1 人だけなので、クライアント間で割れようがない
        // (`vrc-networking` §6「決定的にならない選択は Owner だけが行う」)。

        /// <summary>枠 i の実体が前回の突き合わせで居た場所 (静止判定の基準)。</summary>
        private Vector3[] _slotPrevPos;
        /// <summary>枠 i の実体が落ち着いているか。**食べ始めの前提。**</summary>
        private bool[] _slotSettled;
        /// <summary>犬 d が次の枠を受け持てる時刻。**返事が終わるまで再任命しない。**</summary>
        private float[] _thanksHoldUntil;

        // ---- IMP-DOG-006 (2026-10-03): 接続中の犬をおやつで 1 匹ずつ呼ぶ。マスターのローカル (同期しない) ----
        // 割当の規則は純関数 BuildAllowWith (犬 × 枠の許可) と ResolveClaimsAllowWith。ここはその入力の作業配列と、
        // 「同じ人の接続犬を続けて呼ばない」ための呼んだ順の記録 (読むのはマスター 1 人で、結果は dogSlot として配る)
        private int[] _walkerOf;       // 犬 d をつないでいる playerId (-1 = 未接続)。KH_LeashManager.walkers の写し
        private int[] _slotGiver;      // 枠 i を出した playerId (-1 = 無し)
        private int[] _allow;          // 犬 d × 枠 i の許可 (Allow*)。添字 d * slotCount + i
        private int[] _lastCallGiver;  // 犬 d を最後に接続犬として呼んだ人 (-1 = 無し)
        private int[] _lastCallSeq;    // その呼び出しの通し番号 (大きいほど新しい)
        private int _callSeq;
        private KominkaHillside.KH_LeashManager _leashes;

        private void Start()
        {
            if (eatDuration <= 0.01f) eatDuration = DefaultEatDuration;
            // 0 で入ると返事の途中で次のおやつへ走り出す (破壊的監査 RC-1 の 3 規則)
            if (thanksCooldown <= 0.01f) thanksCooldown = DefaultThanksCooldown;

            _slotCount = slots != null ? slots.Length : 0;
            _dogCount = 0;
            if (pack != null)
            {
                _packRoots = pack.dogRoots;
                _packFetch = pack.dogFetch;
                _packWander = pack.dogWander;
                bool ok = _packRoots != null && _packFetch != null && _packWander != null
                    && _packRoots.Length == _packFetch.Length
                    && _packRoots.Length == _packWander.Length;
                if (ok) _dogCount = _packRoots.Length;
                else
                {
                    Debug.LogError("[KD_TreatBox] dog pack roster invalid - "
                        + "おやつは誰にも食べられない。Run Tools/KominkaDogRun/Dog Pack setup.");
                    _packRoots = null;
                    _packFetch = null;
                    _packWander = null;
                }
            }
            else
            {
                Debug.LogError("[KD_TreatBox] pack 未配線 - おやつは誰にも食べられない。");
            }

            dogSlot = NewFilled(_dogCount, -1);
            dogSlotOutMs = NewFilled(_dogCount, 0);
            dogEatMs = NewFilled(_dogCount, 0);

            _slotState = new int[_slotCount];
            _slotOutMs = new int[_slotCount];
            _dogEligible = new int[_dogCount];
            _distMm = new int[_dogCount * _slotCount];
            _outSlot = new int[_dogCount];
            _outOutMs = new int[_dogCount];

            _slotPrevPos = new Vector3[_slotCount];
            _slotSettled = new bool[_slotCount];
            _thanksHoldUntil = new float[_dogCount];

            // IMP-DOG-006: 接続の写しと呼んだ順の記録。リードの管理は犬の徘徊層が持つ参照を 1 つ借りる (全頭同じ KH_LeashManager)
            _walkerOf = NewFilled(_dogCount, -1);
            _slotGiver = NewFilled(_slotCount, -1);
            _allow = new int[_dogCount * _slotCount];
            _lastCallGiver = NewFilled(_dogCount, -1);
            _lastCallSeq = NewFilled(_dogCount, 0);
            _leashes = null;
            for (int d = 0; d < _dogCount && _leashes == null; d++)
                if (_packWander[d] != null) _leashes = _packWander[d].hillsideLeashes;
            for (int i = 0; i < _slotCount; i++)
                if (slots[i] != null) _slotPrevPos[i] = slots[i].BodyPosition();
        }

        private int[] NewFilled(int n, int value)
        {
            int[] a = new int[n];
            for (int i = 0; i < n; i++) a[i] = value;
            return a;
        }

        private void Update()
        {
            // IMP-DOG-036 S5: own fields and the 4 Hz clock first, IsOwner only when a reconcile is due (it was every frame on every
            // client). Non-Owners now advance the clock too, so a client that becomes the Owner (the master left) reconciles within
            // ReconcileInterval instead of on its first frame as Owner - the Owner's own cadence.
            if (_dogCount <= 0 || _slotCount <= 0) return;
            float now = Time.time;
            if (now < _nextReconcile) return;
            _nextReconcile = now + ReconcileInterval;
            // **マスターだけが配る。** SetOwner を呼ばないので Owner == マスター
            if (!Networking.IsOwner(gameObject)) return;
            Reconcile();
        }

        /// <summary>枠の状態を集めて <see cref="ResolveClaimsWith"/> を呼び、
        /// **変化があったときだけ**配る。
        ///
        /// **犬の足と演技はここでは動かさない。** 足は
        /// <see cref="KD_DogWanderController"/> の割り込み 1.4 が、
        /// 配られた `(枠, outMs)` を読んで動かす。</summary>
        private void Reconcile()
        {
            for (int i = 0; i < _slotCount; i++)
            {
                KD_TreatSlot s = slots[i];
                _slotState[i] = s == null ? SlotFree : s.State();
                _slotOutMs[i] = s == null ? 0 : s.OutMs();
                _slotGiver[i] = s == null ? -1 : s.GiverId();   // IMP-DOG-006: 出した人 (割当の許可が読む)

                // 実体が落ち着いたか (**食べ始めの前提**)。位置は状態の正本ではないので、
                // ここで見るのは「転がっている最中か」だけ
                Vector3 p = s == null ? Vector3.zero : s.BodyPosition();
                _slotSettled[i] = (p - _slotPrevPos[i]).sqrMagnitude <= SettleStep * SettleStep;
                _slotPrevPos[i] = p;
            }
            // IMP-DOG-034 B6: no treat out and no dog holding a claim or eating = the rest is a no-op. ClaimStillOut is false (no slot
            // is Out), so no thanks hold starts (dogEatMs all 0); ResolveClaimsWith gives every dog -1 / 0 (both passes need an Out
            // slot), which equals dogSlot / dogSlotOutMs, so nothing changes and nothing is sent. EligibleDog / DistMmOf only read.
            // The slot loop above still runs, so _slotSettled / _slotPrevPos stay exactly as before.
            if (NothingToResolve()) return;
            for (int d = 0; d < _dogCount; d++)
            {
                // **食べていた枠が消えた = 食べ終わった。返事の保持へ入れる** (不変条件 4)。
                //
                // **`ResolveClaimsWith` を呼ぶ前に立てるのが要点。** 後ろでやると、
                // 同じパスの中で「古い枠が消えたので別の枠を配る」が先に済んでしまい、
                // **返事の最中の犬に次のおやつが割り当たる** (保持が 1 拍遅れる)。
                if (dogEatMs[d] != 0 && !ClaimStillOut(d))
                    _thanksHoldUntil[d] = Time.time + thanksCooldown;

                _dogEligible[d] = EligibleDog(d) ? 1 : 0;
                for (int i = 0; i < _slotCount; i++) _distMm[d * _slotCount + i] = DistMmOf(d, i);
                _walkerOf[d] = WalkerOf(d);   // IMP-DOG-006 (synced walkers の写し)
            }

            // IMP-DOG-006: 誰の枠へどの犬を出してよいか (他人の接続犬は外す / 出した人に接続犬が居ればその中から / 続けて同じ犬を呼ばない)
            BuildAllowWith(_walkerOf, _slotGiver, _slotState, _dogEligible, _lastCallGiver, _lastCallSeq,
                _dogCount, _slotCount, _allow);
            ResolveClaimsAllowWith(dogSlot, dogSlotOutMs, _slotState, _slotOutMs,
                _dogEligible, _distMm, _allow, _dogCount, _slotCount, _outSlot, _outOutMs);

            bool changed = false;
            for (int d = 0; d < _dogCount; d++)
            {
                if (_outSlot[d] == dogSlot[d] && _outOutMs[d] == dogSlotOutMs[d]) continue;
                // IMP-DOG-006: 出した本人の接続犬へ新しく配った = その人の「呼んだ」1 回。次は別の接続犬を優先する
                int ns = _outSlot[d];
                if (ns >= 0 && ns < _slotCount && _walkerOf[d] >= 0 && _walkerOf[d] == _slotGiver[ns])
                {
                    _callSeq++;
                    _lastCallGiver[d] = _slotGiver[ns];
                    _lastCallSeq[d] = _callSeq;
                }
                dogSlot[d] = _outSlot[d];
                dogSlotOutMs[d] = _outOutMs[d];
                // **掴む対象が変わったら食べ始めも捨てる。** 残すと、次に掴んだ
                // 別のおやつが「もう食べ終わっている」ことになって出た瞬間に回収される
                dogEatMs[d] = 0;
                changed = true;
            }
            if (changed) RequestSerialization();
        }

        /// <summary>IMP-DOG-034 B6: この突き合わせで Out の枠が無く、どの犬も Claim も食べ始めも持っていない。</summary>
        private bool NothingToResolve()
        {
            for (int i = 0; i < _slotCount; i++) if (_slotState[i] == SlotOut) return false;
            for (int d = 0; d < _dogCount; d++)
                if (dogSlot[d] != -1 || dogSlotOutMs[d] != 0 || dogEatMs[d] != 0) return false;
            return true;
        }

        /// <summary>犬 d が掴んでいる枠が**まだ同じおやつのまま出ている**か
        /// (この突き合わせで集めた枠の状態だけを見る)。</summary>
        private bool ClaimStillOut(int d)
        {
            int i = dogSlot[d];
            if (i < 0 || i >= _slotCount) return false;
            if (_slotState[i] != SlotOut) return false;
            return _slotOutMs[i] == dogSlotOutMs[d];
        }

        /// <summary>その犬が今おやつを受けられるか (Fetch 中・撫でられ中・催促中・
        /// **返事の途中**は受けない)。
        ///
        /// **Phase 1 の `assignCooldown` の置き換えがこの「返事の途中」。**
        /// あちらは世界に 1 個のおやつを前提に全体で 1 本のクールダウンを置いていたが、
        /// 枠が 3 つになると全体を止める形は使えない (他人が出したおやつまで待たされる)。
        /// **犬ごとに保持する**ことで、返事を最後まで演じきることだけを守る。</summary>
        private bool EligibleDog(int d)
        {
            if (_packRoots == null) return false;
            // **食べ始めた犬は完食まで Claim を保持する** (破壊的監査 v2 RC-6)。
            // ここで false を返すと `ResolveClaimsWith` パス 1 が Claim ごと剥がし、
            // 枠は Out のまま残る — 撫でや Fetch の割り込みで食べが「無かったこと」になり、
            // 同じおやつが 2 回食べられ、回収は放置タイムアウト (60s) に化けていた。
            // 食べ時間は eatDuration (約 2.6s) で有界なので保持しても詰まらない
            if (dogEatMs[d] != 0 && ClaimStillOut(d)) return true;
            // 食べ終わって返事をしている最中 (**マスターのローカル時計**。
            // 読むのはマスターだけで、結果は dogSlot として配るので割れない)
            if (_thanksHoldUntil != null && Time.time < _thanksHoldUntil[d]) return false;
            // IMP-DOG-006 (review 2026-10-03): ここから下は手元メニューの予測 (AllDogsEngaged の接続犬) と同じ関数
            return DogCanTake(d);
        }

        /// <summary>IMP-DOG-006 (review 2026-10-03): 犬 d がいまおやつを受けられるか — **割当 (<see cref="EligibleDog"/>) と、押す人の
        /// 予測 (<see cref="AllDogsEngaged"/> の自分の接続犬) が同じこの関数を読む** (撫でられている接続犬 1 匹だけのとき、
        /// 予測が通って在庫が減り、割当が配らない、を作らない)。読むのは全クライアントが持つ値だけ (同期値と、同期値から全員が導く旗)。
        /// マスターのローカル時計 (返事の保持) と食べている途中の保持は EligibleDog 側に残す。</summary>
        private bool DogCanTake(int d)
        {
            if (_packRoots == null || _packWander == null || _packFetch == null || d < 0 || d >= _packRoots.Length) return false;
            // 催しに拘束されている犬には配らない (破壊的監査 v4 FUNC-07/BUG-01)。Live の観客 / 集合 / かけっこの追走 /
            // じゃれの 2 匹 / Candy 演目とその着地・退場。徘徊層の優先順ではどれも おやつ (1.4) より上なので、
            // 配っても一歩も来ず、枠は放置 60 秒で回収され在庫だけ減っていた。**`f.IsIdle` では弾けない** (dogState は Idle のまま)。
            // 拘束されていない犬 (撫でられて観客から外れた犬、かけっこの勝者決定後の犬) は従来どおり候補に残す
            if (_packWander[d] != null && _packWander[d].ShowEngaged) return false;
            // 裏山改変 (IMP-DOG-014): 集合中・フリスビー中の犬には配らない (HillsideDogBusy)。
            // どれも徘徊層がおやつより優先するので、配っても来ずに枠が放置 60 秒で回収され在庫だけ減る (上の ShowEngaged と同じ理由)。
            // IMP-DOG-006 (2026-10-03): リード接続中はもう除外しない。接続犬は散歩の途中でおやつへ寄れる
            // (KD_DogWanderController.HillsideTreatCall.cs)。誰の枠を受けてよいかは BuildAllowWith が枠ごとに決める。
            // 接続中でも来られない状態 (洗い場に呼ばれている・移動の切符・水の中) だけはここで外す (HillsideLeashTreatBlocked)
            if (HillsideDogBusy(_packWander[d])) return false;
            Transform root = _packRoots[d];
            if (root == null || !root.gameObject.activeInHierarchy) return false;
            KD_DogFetchController f = _packFetch[d];
            if (f == null || !f.IsIdle) return false;   // 仕事中
            if (f.PetActive) return false;              // 撫でられている犬を引き剥がさない (不変条件 4)
            KD_DogWanderController w = _packWander[d];
            if (w == null || w.IsNudging) return false; // 催促中 = 別のプレイヤーに関わっている
            // **そばで寝ている犬は最寄り候補から外す** (IMP-DOG-019 Phase 5 / §6 危険 5)。
            // 外さないと**おやつで占有が破れる** — 呼んだ人の横で寝ている犬が、
            // 別の人が出したおやつへ歩いて行ってしまう。
            // **`f.IsIdle` では弾けない**: 寝ている犬の `dogState` は Idle のまま
            // (犬の状態機械は徘徊層が持ち、Fetch は関与しない)
            if (w.IsSleepCalled) return false;
            return true;
        }

        /// <summary>犬 d から枠 i の実体までの距離 (mm)。</summary>
        private int DistMmOf(int d, int i)
        {
            if (_packRoots == null || _packRoots[d] == null) return int.MaxValue;
            KD_TreatSlot s = slots[i];
            if (s == null) return int.MaxValue;
            float m = Vector3.Distance(_packRoots[d].position, s.BodyPosition()); // sync: local-ok: 距離の比較だけ — 犬の位置は「どの犬へ配るか」の選択にしか入らず、読むのはマスター 1 人で結果は同期変数で配る
            if (m > 2000000f) return int.MaxValue;
            return (int)(m * 1000f);
        }

        // ============================================================
        // 読み取り口 (枠と、Step 5 の手元メニューが読む)
        // ============================================================

        /// <summary>枠 <paramref name="slotIdx"/> の世代 <paramref name="gen"/> を
        /// 掴んでいる犬。**世代が合わなければ -1** — 枠が再利用されたとき、
        /// 古い Claim が新しいおやつへ継承されるのを防ぐ。</summary>
        // net: callable-ok: 読み取りのみ
        public int DogOfSlot(int slotIdx, int gen)
        {
            if (dogSlot == null || dogSlotOutMs == null) return -1;
            for (int d = 0; d < dogSlot.Length; d++)
                if (dogSlot[d] == slotIdx && dogSlotOutMs[d] == gen) return d;
            return -1;
        }

        /// <summary>犬 d が掴んでいる枠 (-1 = 無し)。**犬の足がここを読む。**</summary>
        // net: callable-ok: 読み取りのみ
        public int ClaimedSlotOf(int dogIndex)
        {
            if (dogSlot == null || dogIndex < 0 || dogIndex >= dogSlot.Length) return -1;
            return dogSlot[dogIndex];
        }

        /// <summary>犬 d が掴んだときの世代 (= そのときの `outMs`)。</summary>
        // net: callable-ok: 読み取りのみ
        public int ClaimedGenOf(int dogIndex)
        {
            if (dogSlotOutMs == null || dogIndex < 0 || dogIndex >= dogSlotOutMs.Length) return 0;
            return dogSlotOutMs[dogIndex];
        }

        /// <summary>
        /// **犬 d が今向かってよい枠。向かえないなら null。**
        /// 犬の足 (<see cref="KD_DogWanderController"/> の割り込み 1.4) の唯一の入口。
        ///
        /// ここで 3 つを一度に確かめる — **1 つでも欠けると犬が別のおやつへ歩く:**
        /// <code>
        /// 掴んでいるか          dogSlot[d] &gt;= 0
        /// **世代が一致するか**  slot.OutMs() == dogSlotOutMs[d]   ← 枠の再利用を見抜く鍵
        /// 出ているか            slot.State() == SlotOut           ← Reserved へは向かわない
        /// </code>
        ///
        /// **Reserved (消費がまだ確定していない枠) を弾くのがここの主役。**
        /// 弾かないと「出す手続きの途中で犬が走り出し、手続きが失敗して枠が返る」が起きる。
        /// </summary>
        // net: callable-ok: 読み取りのみ — 呼ばれても状態が動かない
        public KD_TreatSlot LiveSlotOf(int dogIndex)
        {
            int i = ClaimedSlotOf(dogIndex);
            if (slots == null || i < 0 || i >= slots.Length) return null;
            KD_TreatSlot s = slots[i];
            if (s == null) return null;
            if (s.State() != SlotOut) return null;
            if (s.OutMs() != ClaimedGenOf(dogIndex)) return null;
            return s;
        }

        /// <summary>犬 d が**今まさに食べている**か (同期値から全クライアントが同じ結論を導く)。
        /// サーバー時刻は差で比較する (ラップ対策 — KD_DogStopAnchor と同じ)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool EatingNowOf(int dogIndex)
        {
            if (dogEatMs == null || dogIndex < 0 || dogIndex >= dogEatMs.Length) return false;
            if (dogEatMs[dogIndex] == 0) return false;
            int elapsed = Networking.GetServerTimeInMilliseconds() - dogEatMs[dogIndex];
            return elapsed < (int)(eatDuration * 1000f);
        }

        /// <summary>
        /// **犬が食べる間合いへ着いた** (<see cref="KD_DogWanderController"/> が毎フレーム呼ぶ)。
        ///
        /// **書くのはマスターだけ。** 犬 4 匹の Owner はマスター固定
        /// ([IMP-NET-005](../../../docs/development/issues/IMP-NET-005-owner-authoritative-dog.md))
        /// なので、**「他人の犬が届いた」を待つ必要が無い** — 実際に歩いている犬を
        /// 自分の画面で見ている人と、食べ始めを書く人が同じ 1 人になる
        /// (Phase 1 は「おやつの持ち主」が書いていたので、そこがずれていた)。
        ///
        /// 食べ**終わり**は同期しない — `dogEatMs + eatDuration` で全クライアントが導出する。
        /// ローカル専用なので `_` 始まり。
        /// </summary>
        public void _ReportArrived(int dogIndex)
        {
            if (!Networking.IsOwner(gameObject)) return;      // 書くのはマスターだけ
            if (dogEatMs == null || dogIndex < 0 || dogIndex >= dogEatMs.Length) return;
            if (dogEatMs[dogIndex] != 0) return;              // 冪等 — 2 度目は何もしない

            KD_TreatSlot s = LiveSlotOf(dogIndex);
            if (s == null) return;                            // 掴んでいない / 世代違い / Reserved
            if (s.BodyHeld()) return;                         // 手の中のものは食べない
            int i = ClaimedSlotOf(dogIndex);
            if (_slotSettled != null && i >= 0 && i < _slotSettled.Length && !_slotSettled[i])
                return;                                       // まだ転がっている

            int ms = Networking.GetServerTimeInMilliseconds();
            // 0 は「まだ食べていない」の意味に使っているので避ける (枠の outMs と同じ処置)
            dogEatMs[dogIndex] = ms == 0 ? 1 : ms;
            RequestSerialization();
        }

        /// <summary>犬 d の食べ始め (サーバー ms)。0 = まだ。</summary>
        // net: callable-ok: 読み取りのみ
        public int EatStartMsOf(int dogIndex)
        {
            if (dogEatMs == null || dogIndex < 0 || dogIndex >= dogEatMs.Length) return 0;
            return dogEatMs[dogIndex];
        }

        /// <summary>その枠のおやつを**今まさに食べている**か。</summary>
        // net: callable-ok: 読み取りのみ
        public bool EatingNowFor(int slotIdx, int gen)
        {
            int d = DogOfSlot(slotIdx, gen);
            if (d < 0 || dogEatMs[d] == 0) return false;
            int elapsed = Networking.GetServerTimeInMilliseconds() - dogEatMs[d];
            return elapsed < (int)(eatDuration * 1000f);
        }

        /// <summary>その枠のおやつを**食べ終わった**か。**回収の主たるトリガー。**
        /// サーバー時刻は差で比較する (ラップ対策 — KD_DogStopAnchor と同じ)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool EatFinishedFor(int slotIdx, int gen)
        {
            int d = DogOfSlot(slotIdx, gen);
            if (d < 0 || dogEatMs[d] == 0) return false;
            int elapsed = Networking.GetServerTimeInMilliseconds() - dogEatMs[d];
            return elapsed >= (int)(eatDuration * 1000f);
        }

        /// <summary>空いている枠の数。**<see cref="DispenseGateWith"/> の 4 番目の引数。**</summary>
        // net: callable-ok: 読み取りのみ
        public int FreeSlotCount()
        {
            if (slots == null) return 0;
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].State() == SlotFree) n++;
            return n;
        }

        /// <summary>出しても来られる犬が居ないか (<see cref="GateDogsBusy"/> の材料。破壊的監査 v4 FUNC-07/BUG-01)。
        /// **押す本人のクライアントで読む予測**であり、割当 (マスターの <see cref="EligibleDog"/>) の保証ではない —
        /// 拘束の判定 (`ShowEngaged`) は Live / 集合の同期値から導くが、`UpdateLive` はローカルの撫で (`IsPetting`) も読み、
        /// 行動の再選択には最大 0.1 秒の差がある。門番が通ってもマスター側で全犬が不適格 (撫で中・寝ている・観客) なら
        /// 従来どおり在庫は減り放置 60 秒で回収される (Codex レビュー 2026-09-19。消費を割当の受理まで遅らせる変更は別件)。
        /// 犬が 1 匹も有効でなければ false (従来どおり門番は他の理由に任せる)。
        ///
        /// IMP-DOG-006 (2026-10-03 ユーザー決定 2): **押す人が犬をつないでいれば、その接続犬だけが候補** (割当の
        /// <see cref="BuildAllowWith"/> と同じ規則)。接続犬が居るのに 1 匹も来られない (フリスビー / Fetch 中・洗い場に呼ばれている・
        /// 移動の切符・水の中) なら出させずに理由を出す (在庫は減らない)。未接続の犬へは回さない。
        /// 接続犬が 0 匹なら従来どおり全頭を見る。そのとき**他人の接続犬は来られない**側に数える (横取りしない)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool AllDogsEngaged()
        {
            if (_packRoots == null || _packWander == null) return false;
            int me = LocalPlayerId();
            int active = 0, mine = 0, mineReady = 0;
            bool anyFree = false;
            for (int d = 0; d < _packRoots.Length; d++)
            {
                Transform root = _packRoots[d];
                if (root == null || !root.gameObject.activeInHierarchy) continue;
                active++;
                KD_DogWanderController w = d < _packWander.Length ? _packWander[d] : null;
                if (w != null && me >= 0 && WalkerOf(d) == me)
                {
                    mine++;
                    // review 2026-10-03: the same test as the assignment (EligibleDog -> DogCanTake): petted, nudging, sleep-called,
                    // Fetch / disc, ShowEngaged, wash station / travel ticket / wet. Only the master-local thanks hold (2.6 s) is not
                    // predicted: the assignment then waits for it instead of refusing
                    if (DogCanTake(d)) mineReady++;
                    continue;
                }
                if (w == null || !(w.ShowEngaged || HillsideCauseOf(w, me) != HillsideBusyNone)) anyFree = true;   // 裏山改変: 割当 (EligibleDog) と同じ除外を予測にも
            }
            if (mine > 0) return mineReady == 0;   // IMP-DOG-006: 自分の接続犬の中に来られる犬が居るか
            return active > 0 && !anyFree;
        }

        /// <summary>裏山改変 (IMP-DOG-014): 裏山の状態で おやつを受けられない犬 (割当 = マスターの見方)。**割当 (<see cref="EligibleDog"/>) と
        /// 予測 (<see cref="AllDogsEngaged"/>) の両方が <see cref="HillsideCauseOf"/> を読む** — 条件を 2 か所に書き写さない。
        ///
        /// | 状態 | 読む先 | 理由 |
        /// |---|---|---|
        /// | フリスビー中 (Ready を含む) | KH_FrisbeeCatch.IsIdle でない | root と Animator をフリスビーが持つ (徘徊層の FrisbeeActive と同じ判定) |
        /// | リード接続中 (IMP-DOG-006 で変更) | KH_LeashManager.walkers (同期) と犬の HillsideLeashTreatBlocked | 割当では接続そのものは除外しない (誰の枠かは BuildAllowWith)。予測では他人の接続犬 = 来られない |
        /// | 集合中 | KH_LeashManager._IsGathering (KD_GroupActivities の同期値) | 徘徊層がおやつより優先する (ShowEngaged と重なるが裏山の口でも見る) |
        ///
        /// 参照は犬ごとの徘徊層が既に持つもの (hillsideLeashes / frisbeeCatch / dogIndex) を読むだけで、同期変数は増やさない。</summary>
        private bool HillsideDogBusy(KD_DogWanderController w) { return HillsideCauseOf(w, ViewerMaster) != HillsideBusyNone; }

        /// <summary>裏山改変 (IMP-DOG-014): 全頭が来られないときの主因 (手元メニューの理由文を分けるため)。</summary>
        public const int HillsideBusyNone = 0;
        /// <summary>IMP-DOG-006 以降: **他人がつないでいる**犬 (押す人に接続犬が居ないとき)。</summary>
        public const int HillsideBusyLeash = 1;
        public const int HillsideBusyGather = 2;
        public const int HillsideBusyFrisbee = 3;
        /// <summary>催し (ライブ・集合の同期値・かけっこ等の ShowEngaged) だけ、または裏山の理由が特定できない = 活動の終わりを待つ。</summary>
        public const int HillsideBusyOther = 4;
        /// <summary>IMP-DOG-006 (2026-10-03 ユーザー決定 2): **自分の接続犬が居るのに 1 匹も来られない** (出させずに理由を出す)。</summary>
        public const int HillsideBusyOwnLeash = 5;

        /// <summary>IMP-DOG-006: <see cref="HillsideCauseOf"/> の見方。マスターの割当 (誰の枠かは別に決める) = これ。0 以上は押す人の playerId。</summary>
        const int ViewerMaster = -3;

        /// <summary>犬 1 匹の裏山の忙しさ。順は フリスビー > リード > 集合。
        /// <paramref name="viewer"/> = <see cref="ViewerMaster"/> (割当): 接続中なら「接続中でも来られない状態」だけを忙しいとする。
        /// viewer = 押す人の playerId (予測): 他人の接続犬は <see cref="HillsideBusyLeash"/>、自分の接続犬で来られなければ <see cref="HillsideBusyOwnLeash"/>。</summary>
        private int HillsideCauseOf(KD_DogWanderController w, int viewer)
        {
            if (w == null) return HillsideBusyNone;
            if (w.frisbeeCatch != null && !w.frisbeeCatch.IsIdle) return HillsideBusyFrisbee;
            KominkaHillside.KH_LeashManager leashes = w.hillsideLeashes;
            if (leashes == null) return HillsideBusyNone;
            int i = w.dogIndex;
            if (i < 0) return HillsideBusyNone;
            if (leashes._IsLeashed(i))
            {
                // IMP-DOG-006: 接続犬も呼べる。来られないのは洗い場・移動の切符・水の中だけ (犬側の HillsideLeashTreatBlocked)
                if (viewer == ViewerMaster) return w.HillsideLeashTreatBlocked ? HillsideBusyLeash : HillsideBusyNone;
                if (leashes.walkers[i] != viewer) return HillsideBusyLeash;
                return w.HillsideLeashTreatBlocked ? HillsideBusyOwnLeash : HillsideBusyNone;
            }
            if (leashes._IsGathering(i)) return HillsideBusyGather;
            return HillsideBusyNone;
        }

        /// <summary>裏山改変 (IMP-DOG-014): 全頭が来られない (<see cref="AllDogsEngaged"/> が true) ときの主因 <c>HillsideBusy*</c>。
        /// 来られる犬が居れば <see cref="HillsideBusyNone"/>。IMP-DOG-006: 押す人に接続犬が居れば <see cref="HillsideBusyOwnLeash"/>。
        /// それ以外は 裏山の理由で忙しい犬がいちばん多い理由 (同数は リード > 集合 > フリスビー)。裏山の理由の犬が 1 匹も居なければ
        /// <see cref="HillsideBusyOther"/>。押す人のローカルの予測 (AllDogsEngaged と同じ性質)。</summary>
        // net: callable-ok: 読み取りのみ
        public int HillsideAllBusyCause()
        {
            if (!AllDogsEngaged()) return HillsideBusyNone;
            int me = LocalPlayerId();
            int leash = 0, gather = 0, frisbee = 0;
            for (int d = 0; d < _packRoots.Length; d++)
            {
                Transform root = _packRoots[d];
                if (root == null || !root.gameObject.activeInHierarchy) continue;
                if (me >= 0 && WalkerOf(d) == me) return HillsideBusyOwnLeash;   // AllDogsEngaged が自分の接続犬だけで決めた
                KD_DogWanderController w = d < _packWander.Length ? _packWander[d] : null;
                int c = HillsideCauseOf(w, me);
                if (c == HillsideBusyLeash) leash++;
                else if (c == HillsideBusyGather) gather++;
                else if (c == HillsideBusyFrisbee) frisbee++;
            }
            if (leash == 0 && gather == 0 && frisbee == 0) return HillsideBusyOther;
            if (leash >= gather && leash >= frisbee) return HillsideBusyLeash;
            if (gather >= frisbee) return HillsideBusyGather;
            return HillsideBusyFrisbee;
        }

        /// <summary>IMP-DOG-006: 犬 d をつないでいる playerId (-1 = 未接続 / 配線無し)。KH_LeashManager.walkers (同期) の読み取り。</summary>
        private int WalkerOf(int d)
        {
            KominkaHillside.KH_LeashManager leashes = _leashes;
            if (leashes == null && _packWander != null && d >= 0 && d < _packWander.Length && _packWander[d] != null)
                leashes = _packWander[d].hillsideLeashes;
            if (leashes == null || leashes.walkers == null || d < 0 || d >= leashes.walkers.Length) return -1;
            return leashes.walkers[d];
        }

        /// <summary>このクライアントの playerId (-2 = まだ居ない。walkers の -1 と区別する)。</summary>
        private int LocalPlayerId()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            return Utilities.IsValid(lp) ? lp.playerId : -2;
        }

        /// <summary>最初の空き枠の番号。無ければ -1。</summary>
        // net: callable-ok: 読み取りのみ
        public int FreeSlotIndex()
        {
            if (slots == null) return -1;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].State() == SlotFree) return i;
            return -1;
        }

        /// <summary>その人がもう 1 個出しているか (**1 人 1 個** —
        /// <see cref="GateAlreadyOut"/> の入力)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool PlayerHasSlot(int playerId)
        {
            if (slots == null || playerId < 0) return false;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].GiverId() == playerId) return true;
            return false;
        }

        // ============================================================
        // 純関数 (状態を見ない。Editor から自己テストできる)
        //
        // **Play でしか確かめられないものを、机上で確かめられる形へ移すのが目的。**
        // 台帳の算術 37 件 / 選択の 5 ケース / ルート沈みの 18 件と同じ手。
        // ============================================================

        /// <summary>
        /// 出せるか。出せないなら**理由**を返す (<c>Gate*</c>)。
        ///
        /// **理由の優先順位はここが正本**で、自己テストが全 32 組合せで固定する。
        /// 順序に意味がある — 「在庫が無い」と「枠が満杯」が同時に起きたとき、
        /// **自分の側の理由 (在庫) を先に出す**。他人の都合より自分の状態のほうが
        /// 次の行動につながる (買えばよい)。
        /// </summary>
        // net: callable-ok: 純関数 — 引数だけを見る
        public int DispenseGateWith(bool loaded, int stock, bool iAlreadyOut, int freeSlots)
        {
            if (!loaded) return GateLoading;
            if (iAlreadyOut) return GateAlreadyOut;
            if (stock <= 0) return GateNoStock;
            if (freeSlots <= 0) return GateSlotsFull;
            return GateOk;
        }

        /// <summary>
        /// **あげる (手のおやつを放す) 判定。** Use の 1 イベントに対して何をするかを返す。
        ///
        /// **時間ではなく操作であげる**ようにしたのが IMP-TRT-003 で、
        /// 難所は「**`[出す]` を押したトリガーが、そのまま『あげる』に化ける**」こと —
        /// VR の UI 押下はトリガーそのものなので、離しは吸着が始まった**後**に届く。
        ///
        /// <code>
        /// [出す] を押す (押し下がり) ── 吸着はまだ ──→ 離し が吸着後に届く  ← ここを捨てたい
        /// 吸着してから引く (押し下がり) ────────────→ 離し であげる
        /// </code>
        ///
        /// だから**吸着が始まった後の押し下がりを見た (`armed`) ときだけ離しで放す。**
        /// 猶予時間ではなく順序で塞ぐので、押し方の速さに依らない。
        ///
        /// 板が開いている間を丸ごと無視するのは Desktop 側の事情 —
        /// あちらの Use は左クリックで、**板のボタンを押すクリックと同じ**。
        /// (出したときは <c>KD_TreatPouch</c> が板を閉じるので、通常この道は塞がらない)
        /// </summary>
        // net: callable-ok: 純関数 — 引数だけを見る
        public int GiveStepWith(bool attaching, bool armed, bool panelOpen, bool isDown)
        {
            // 吸着していない = 手に何も無い。**この Use は自分と無関係**
            if (!attaching) return GiveDisarm;
            // 板を操作しているトリガー / クリックで落とさない
            if (panelOpen) return GiveIgnore;
            if (isDown) return GiveArm;
            // ★ 吸着後の押し下がりを見ていない離し = [出す] を押したトリガーの離し
            if (!armed) return GiveIgnore;
            return GiveRelease;
        }

        /// <summary>
        /// 出す手順を 1 歩進める。戻り値は**次の状態と「在庫を減らすか」を詰めた値**で、
        /// <see cref="DispenseNextOf"/> / <see cref="DispenseConsumesOf"/> で取り出す。
        ///
        /// **`out` を使わないのは Udon の都合ではなく、2 つに割ると
        /// 「次の状態」と「減らすか」がずれうるため** — 1 本の表から両方を導く。
        ///
        /// ## 二重消費を防ぐ 3 つの仕掛け
        ///
        /// 1. **在庫が減る辺はちょうど 1 本** (`DispReserved` + <see cref="EvPlaced"/>)
        /// 2. **その辺は状態を動かす** (`DispCommitting` へ) ので、
        ///    同じイベントがもう一度来ても 2 度目は成立しない。
        ///    **「辺が 1 本」だけでは実行回数を保証できない** (2026-08-24 Codex 指摘) ため、
        ///    呼び出し側は**状態が動いた呼び出しでだけ副作用を実行する**
        /// 3. **世代 (`evSeq`) が合わないイベントは捨てる** —
        ///    遅延イベントの重複や、古い試行の `OnOwnershipTransferred` の再通知を弾く
        ///
        /// <paramref name="ev"/> が <see cref="EvPress"/> のときだけ世代を見ない
        /// (押した瞬間が新しい世代の始まりなので、まだ番号が無い)。
        /// </summary>
        // net: callable-ok: 純関数 — 引数だけを見る
        public int StepDispenseWith(int state, int ev, int evSeq, int curSeq)
        {
            // 押す以外は、その試行のものでなければ何もしない (冪等化の要)
            if (ev != EvPress && evSeq != curSeq) return Pack(state, false);

            if (ev == EvPress)
            {
                // **出ている / 手続き中の連打を無視する。** 状態が動かないので副作用も起きない
                if (state != DispIdle) return Pack(state, false);
                return Pack(DispReserving, false);
            }

            if (state == DispReserving)
            {
                if (ev == EvOwnerGranted) return Pack(DispReserved, false);
                // **負けた / 来ない。在庫は 1 も減らない**
                if (ev == EvOwnerFailed || ev == EvTimeout) return Pack(DispIdle, false);
                return Pack(state, false);
            }

            if (state == DispReserved)
            {
                // ★ **在庫が減る唯一の辺。** 実体が出た後にしか来ない
                if (ev == EvPlaced) return Pack(DispCommitting, true);
                // 出せないまま時間切れ → 枠を返して終わり (減らさない)
                if (ev == EvTimeout || ev == EvOwnerFailed) return Pack(DispRecalling, false);
                return Pack(state, false);
            }

            if (state == DispCommitting)
            {
                if (ev == EvConsumeOk) return Pack(DispOut, false);
                // **減らせなかった → 実体を回収する。**「無料で出た」を残さない
                if (ev == EvConsumeFailed) return Pack(DispRecalling, false);
                if (ev == EvTimeout) return Pack(DispRecalling, false);
                return Pack(state, false);
            }

            if (state == DispOut)
            {
                if (ev == EvRecalled) return Pack(DispIdle, false);
                if (ev == EvTimeout) return Pack(DispRecalling, false);
                return Pack(state, false);
            }

            if (state == DispRecalling)
            {
                if (ev == EvRecalled) return Pack(DispIdle, false);
                return Pack(state, false);
            }

            return Pack(state, false);
        }

        private int Pack(int next, bool consume) { return (next << 1) | (consume ? 1 : 0); }

        /// <summary><see cref="StepDispenseWith"/> の戻り値から次の状態を取り出す。</summary>
        // net: callable-ok: 純関数
        public int DispenseNextOf(int packed) { return packed >> 1; }

        /// <summary><see cref="StepDispenseWith"/> の戻り値から「在庫を減らすか」を取り出す。</summary>
        // net: callable-ok: 純関数
        public bool DispenseConsumesOf(int packed) { return (packed & 1) != 0; }

        /// <summary>
        /// **どの犬がどの枠のおやつへ向かうかを、まとめて決める。**
        ///
        /// マスターだけが呼ぶ。距離を int(mm) で受けるので純関数のまま保てる
        /// (`Vector3` を受けると自己テストが Play 前提になる)。
        ///
        /// ## 2 パスで書く理由
        ///
        /// <code>
        /// パス 1  今の Claim のうち**まだ有効なもの**をそのまま残す  ← 先着順 (不変条件 4)
        /// パス 2  空いている Out の枠へ、**先に出された順**に犬を配る
        /// </code>
        ///
        /// **1 パスで「一番近い枠を選ぶ」と書くと、出現のたびに犬が向きを変える。**
        /// それは不変条件 4 (誰への返事かが読めること) が壊れた状態で、
        /// 二重 Claim を防ぐだけでは足りない (設計 6)。
        ///
        /// 結果は <paramref name="outSlot"/> / <paramref name="outOutMs"/> へ書く
        /// (戻り値にできないのは配列を 2 本返せないため)。
        /// </summary>
        /// <param name="prevSlot">犬 d が今掴んでいる枠 (-1 = 無し)</param>
        /// <param name="prevOutMs">それを掴んだときの枠の世代。**枠の再利用を見抜く鍵**</param>
        /// <param name="slotState">枠 i の状態 (<c>Slot*</c>)</param>
        /// <param name="slotOutMs">枠 i の世代 = 出された時刻。**昇順が先着**</param>
        /// <param name="dogEligible">犬 d が受けられるか (Fetch / 撫で / 催促中でなければ 1)</param>
        /// <param name="distMm">犬 d から枠 i までの距離 (mm)。添字は <c>d * slotCount + i</c></param>
        // net: callable-ok: 純関数 — 引数だけを見て書き換えるのは渡された出力配列だけ
        public void ResolveClaimsWith(
            int[] prevSlot, int[] prevOutMs,
            int[] slotState, int[] slotOutMs,
            int[] dogEligible, int[] distMm,
            int dogCount, int slotCount,
            int[] outSlot, int[] outOutMs)
        {
            // 許可の表なし = 全組が AllowNew (IMP-DOG-006 より前と同じ答え。古民家の自己テストはこの口を当てる)
            ResolveClaimsAllowWith(prevSlot, prevOutMs, slotState, slotOutMs, dogEligible, distMm, null,
                dogCount, slotCount, outSlot, outOutMs);
        }

        // ============================================================
        // IMP-DOG-006 (2026-10-03 ユーザー決定): 接続中の犬をおやつで 1 匹ずつ呼ぶ — 割当の許可
        // ============================================================

        /// <summary>犬 d は枠 i を掴めない (他人がつないでいる犬)。**パス 1 でも Claim を外す。**</summary>
        public const int AllowNone = 0;
        /// <summary>今の Claim は保ってよいが、新しくは配らない (出した人に接続犬が居るときの未接続犬 / 続けて呼ばない犬)。</summary>
        public const int AllowKeep = 1;
        /// <summary>新しく配ってよい。</summary>
        public const int AllowNew = 2;

        /// <summary>
        /// **誰の枠へどの犬を出してよいか** を犬 × 枠の表 <paramref name="allow"/> (添字 <c>d * slotCount + i</c>) に書く。純関数。
        ///
        /// 枠 i を出した人を g とする (<paramref name="slotGiver"/>)。
        /// <code>
        /// 犬 d を g 以外がつないでいる                  → AllowNone  (横取りしない。Claim も外す)
        /// 犬 d を g がつないでいる                      → AllowNew   (ただし下の「続けて呼ばない」)
        /// 犬 d は未接続、g に接続犬が 1 匹以上居る      → AllowKeep  (接続犬だけが候補。途中で外された犬は Claim を保つ)
        /// 犬 d は未接続、g に接続犬が居ない            → AllowNew   (従来どおり最寄りの犬)
        /// </code>
        /// **続けて呼ばない:** g が最後に呼んだ犬 (<paramref name="lastCallGiver"/> == g の中で <paramref name="lastCallSeq"/> 最大。
        /// 全頭から探し、その犬がいま g の接続犬でなければ誰も飛ばさない) は、g の接続犬で受けられる犬 (<paramref name="dogEligible"/>) がほかにも居るときだけ AllowKeep に下げる
        /// (「次のおやつで次の 1 匹」。1 匹しか来られなければ同じ犬が続けて来てよい)。
        /// 最寄りの選択そのものは <see cref="ResolveClaimsAllowWith"/> のパス 2 (距離・同距離は犬番号の小さい方) が行う。
        /// </summary>
        // net: callable-ok: 純関数 — 引数だけを見て書き換えるのは渡された出力配列だけ
        public void BuildAllowWith(int[] walker, int[] slotGiver, int[] slotState, int[] dogEligible,
            int[] lastCallGiver, int[] lastCallSeq, int dogCount, int slotCount, int[] allow)
        {
            if (allow == null || walker == null || slotGiver == null) return;
            if (dogCount > walker.Length) dogCount = walker.Length;
            if (slotCount > slotGiver.Length) slotCount = slotGiver.Length;
            if (dogCount * slotCount > allow.Length) return;
            for (int i = 0; i < slotCount; i++)
            {
                int g = slotGiver[i];
                int leashed = 0, ready = 0, lastDog = -1, lastSeq = 0;
                for (int d = 0; d < dogCount; d++)
                {
                    // g の「最後に呼んだ犬」は全頭から探す (今は外れている犬でもよい)。外された犬が最後なら、誰も飛ばさない —
                    // 2 つ前に呼んだ接続犬まで飛ばすと「最寄り」から離れる (Course/97c TC3 で確認)
                    int lg = lastCallGiver == null || d >= lastCallGiver.Length ? -1 : lastCallGiver[d];
                    int ls = lastCallSeq == null || d >= lastCallSeq.Length ? 0 : lastCallSeq[d];
                    if (g >= 0 && lg == g && (lastDog < 0 || ls > lastSeq)) { lastDog = d; lastSeq = ls; }
                    if (g < 0 || walker[d] != g) continue;
                    leashed++;
                    if (Eligible(dogEligible, d)) ready++;
                }
                if (lastDog >= 0 && walker[lastDog] != g) lastDog = -1;
                for (int d = 0; d < dogCount; d++)
                {
                    int w = walker[d];
                    int a;
                    if (w >= 0 && w != g) a = AllowNone;
                    else if (w >= 0) a = (d == lastDog && ready >= 2 && Eligible(dogEligible, d)) ? AllowKeep : AllowNew;
                    else a = leashed > 0 ? AllowKeep : AllowNew;
                    allow[d * slotCount + i] = a;
                }
            }
        }

        /// <summary>
        /// <see cref="ResolveClaimsWith"/> に許可の表を足したもの (IMP-DOG-006)。<paramref name="allow"/> が null なら全組 AllowNew で、
        /// 答えは許可なしの版と同じ。許可があるとき:
        /// パス 1 は AllowKeep 以上の組だけ Claim を保つ (他人の接続犬になった犬は外れる)、
        /// パス 2 は AllowNew の組だけを候補にし、**距離が測れない組 (int.MaxValue) は選ばない**。
        /// </summary>
        // net: callable-ok: 純関数 — 引数だけを見て書き換えるのは渡された出力配列だけ
        public void ResolveClaimsAllowWith(
            int[] prevSlot, int[] prevOutMs,
            int[] slotState, int[] slotOutMs,
            int[] dogEligible, int[] distMm, int[] allow,
            int dogCount, int slotCount,
            int[] outSlot, int[] outOutMs)
        {
            if (outSlot == null || outOutMs == null) return;
            if (dogCount > outSlot.Length) dogCount = outSlot.Length;
            if (dogCount > outOutMs.Length) dogCount = outOutMs.Length;
            if (slotState == null || slotOutMs == null) slotCount = 0;
            else
            {
                if (slotCount > slotState.Length) slotCount = slotState.Length;
                if (slotCount > slotOutMs.Length) slotCount = slotOutMs.Length;
            }
            if (slotCount < 0) slotCount = 0;
            if (dogCount < 0) dogCount = 0;

            // 枠が既に誰かのものか (このパスの中だけで使う。単射を保つのはこれ)
            bool[] taken = new bool[slotCount];

            for (int d = 0; d < dogCount; d++)
            {
                outSlot[d] = -1;
                outOutMs[d] = 0;
            }

            // ---- パス 1: 有効な Claim を残す (先着順。より近い枠が出ても変えない) ----
            for (int d = 0; d < dogCount; d++)
            {
                if (!Eligible(dogEligible, d)) continue;
                int s = prevSlot == null || d >= prevSlot.Length ? -1 : prevSlot[d];
                if (s < 0 || s >= slotCount) continue;
                if (slotState[s] != SlotOut) continue;               // 食べられた / 消えた / 未確定
                int gen = prevOutMs == null || d >= prevOutMs.Length ? 0 : prevOutMs[d];
                if (gen != slotOutMs[s]) continue;                   // 枠が再利用された = 別のおやつ
                if (AllowOf(allow, d, s, slotCount) < AllowKeep) continue;   // IMP-DOG-006: 他人がつないだ犬は Claim を外す
                // **壊れた入力で 2 匹が同じ枠を掴んでいたら、犬番号の小さい方が勝つ** —
                // どちらかに決まりさえすれば単射は保てる (破損からの決定的な復旧)
                if (taken[s]) continue;
                taken[s] = true;
                outSlot[d] = s;
                outOutMs[d] = slotOutMs[s];
            }

            // ---- パス 2: 空いている枠へ、先に出された順に配る ----
            //
            // **枠を主語にして回す** — 仕様の言葉が「犬は最寄りの有効なおやつへ向かう」なので
            // 犬を主語にしたくなるが、それだと 2 匹が同じ枠を選んだときの調停が要る。
            // 枠から見て最寄りの犬を採れば、調停なしで単射になる
            for (int guard = 0; guard < slotCount; guard++)
            {
                int bestSlot = -1;
                int bestOutMs = 0;
                for (int i = 0; i < slotCount; i++)
                {
                    if (taken[i]) continue;
                    if (slotState[i] != SlotOut) continue;
                    // 先に出された順。同時刻なら枠番号の小さい方 (決定的であることが要点)。
                    // **生の大小で比べない** — サーバー時刻は循環する (下記 EarlierMs)
                    if (bestSlot < 0 || EarlierMs(slotOutMs[i], bestOutMs))
                    {
                        bestSlot = i;
                        bestOutMs = slotOutMs[i];
                    }
                }
                if (bestSlot < 0) break;   // 配る先がもう無い

                int bestDog = -1;
                int bestDist = 0;
                for (int d = 0; d < dogCount; d++)
                {
                    if (!Eligible(dogEligible, d)) continue;
                    if (outSlot[d] >= 0) continue;                   // もう持っている
                    if (AllowOf(allow, d, bestSlot, slotCount) < AllowNew) continue;   // IMP-DOG-006
                    int dist = DistOf(distMm, d, bestSlot, slotCount);
                    if (allow != null && dist == int.MaxValue) continue;              // IMP-DOG-006: 測れない組は選ばない
                    if (bestDog < 0 || dist < bestDist)
                    {
                        bestDog = d;
                        bestDist = dist;
                    }
                }

                taken[bestSlot] = true;    // 手の空いた犬が居なくても、この枠はもう見ない
                if (bestDog < 0) continue;
                outSlot[bestDog] = bestSlot;
                outOutMs[bestDog] = slotOutMs[bestSlot];
            }
        }

        /// <summary>
        /// **`a` は `b` より先に出されたか。サーバー時刻の循環に耐える比較。**
        ///
        /// `GetServerTimeInMilliseconds()` は int なので **約 24.8 日で一周する。**
        /// 生の大小 (`a &lt; b`) で比べると、**ラップの前後で新旧が逆転する** —
        /// ラップ直後に出した枠 (小さな負値) が「いちばん先に出された」と読まれ、
        /// 先着順 (不変条件 4) が壊れる。
        ///
        /// **符号付きの差で見れば一周を跨いでも正しい** (差が 2^31 ms ≒ 24 日以内なら)。
        /// C# / Udon の int 減算はラップするので、それがそのまま使える。
        ///
        /// **同じファイルの食事時刻 (`EatingNowOf` など) は元から差分で比較していた** のに、
        /// Claim 順だけ生の大小のままだった (2026-08-24 Codex 指摘)。
        /// **時刻の比較はこの 1 本へ寄せる** — 2 通りの比較が混在すると、
        /// 片方だけ直したときに気づけない。
        /// </summary>
        // net: callable-ok: 純関数
        public bool EarlierMs(int a, int b) { return (a - b) < 0; }

        /// <summary>IMP-DOG-006: 犬 d × 枠 i の許可。表が無ければ AllowNew (許可なしの版と同じ答え)。</summary>
        private int AllowOf(int[] allow, int d, int slot, int slotCount)
        {
            if (allow == null) return AllowNew;
            int idx = d * slotCount + slot;
            if (idx < 0 || idx >= allow.Length) return AllowNone;
            return allow[idx];
        }

        private bool Eligible(int[] dogEligible, int d)
        {
            if (dogEligible == null || d >= dogEligible.Length) return false;
            return dogEligible[d] != 0;
        }

        private int DistOf(int[] distMm, int d, int slot, int slotCount)
        {
            if (distMm == null) return int.MaxValue;
            int idx = d * slotCount + slot;
            if (idx < 0 || idx >= distMm.Length) return int.MaxValue;
            return distMm[idx];
        }
    }
}
