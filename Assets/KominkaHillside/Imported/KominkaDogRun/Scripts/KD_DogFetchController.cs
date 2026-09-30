using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-FET-001: Fetch の主状態 FSM (設計書 §1 / §2)。
    ///
    ///   Idle → ChaseStart → Chase → Pickup → Return → Drop → Idle
    ///
    /// 同期は 4 変数 (dogState / attentionPlayerId / targetObjectId / petState、Manual)。
    /// 画面 (毎フレームの移動・アニメ・ボールの口元表示) は同期せず、
    /// 各クライアントが同期変数から導出する (不変条件 5)。
    /// 状態遷移の決定 (到着判定・タイマー) は Owner だけが行う。
    ///
    /// IMP-NET-001 (2026-08-15): 撫での結果もここへ相乗りさせた。**この Behaviour が
    /// 「犬の対人状態」の唯一の器**で、Fetch 中は投げた本人・Idle 中は撫でている本人を
    /// `attentionPlayerId` が指す。撫での演出 (♡ / SE / 表情 / 尻尾) は
    /// **イベントで配らず、各クライアントが `petState` から導出する** —
    /// イベント配布は Stop の取りこぼしで出しっぱなしになり、Late Joiner にも届かない
    /// (`vrc-networking` §6「演出をどう配るか」)。
    ///
    /// 撫で (KD_DogPetController) は dogState != Idle の間は抑制する —
    /// 無反応が「ボールに夢中」として演技になる (設計書 §1)。
    ///
    /// ## IMP-NET-005 (2026-08-22): Owner 権威 + VRCObjectSync へ移行
    ///
    /// **ここの記述は方式ごと入れ替わった。** 旧方式 —
    /// 「非 Owner の投げは SetOwner → OnOwnershipTransferred で完了を確認してから書く」
    /// 「犬の位置は同期しない (移動で自然に収束する)」— は、
    /// 実機 2 人で滑り・ワープが多く DES-NET-002 で方式ごと棄却した。
    ///
    /// - **Owner は常に dogRoot の Owner** (<see cref="IsDogOwner"/>)。自分の gameObject は見ない
    /// - **犬側で SetOwner を一度も呼ばない。** 投げも撫でも Owner への Network Event
    ///   (<see cref="ReqStartFetch"/> / <see cref="ReqPetBegin"/> / <see cref="ReqPetEnd"/>) で伝える
    /// - **位置と回転は Owner だけが書き、VRCObjectSync が配る。**
    ///   非 Owner は観測した Transform 差分から _speed を作って脚だけ選ぶ
    /// - **送り主の playerId は Udon では取れない**ので、Owner が観測できる事実で認証する
    ///   (投げ = ボールが実際に飛んでいる / 撫で = その人の手が犬にある)
    /// - OnPlayerLeft: 対象が抜けたら Owner が Idle へ戻す
    /// - <see cref="OnOwnershipTransferred"/> へ来るのは**マスター交代のときだけ**になった
    /// - Late Joiner は Start / OnDeserialization の ApplyState で表示を復元
    ///   (位置は VRCObjectSync が入室時に配る)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public partial class KD_DogFetchController : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/Fetch System が設定する)")]
        public Transform dogRoot;
        public KominkaHillside.KH_LeashManager hillsideLeashes;
        // IMP-DOG-005: a thrown ball does not interrupt a disc chase / jump / carry (Ready does not block).
        public KominkaHillside.KH_FrisbeeCatch frisbeeCatch;
        public Animator dogAnimator;
        [Tooltip("ko-gi- 側 Bone.006 (顎) の子。くわえたボールの表示位置")]
        public Transform mouthAnchor;
        public KD_BallController ballController;
        [Tooltip("Fetch 中に抑制する撫でコントローラ")]
        public KD_DogPetController petController;
        [Tooltip("受け渡しの瞬間を撮る (IMP-CAM-001)。null 可")]
        public KD_PhotoDirector photoDirector;
        [Tooltip("そばで寝るモードの台帳 (IMP-DOG-019 Phase 5)。**読むだけ。** " +
            "寝ている犬は投擲に反応しない。null 可 (従来どおり全部の投擲へ反応する)")]
        public KD_SleepBoard sleepBoard;

        [Tooltip("IMP-PRG-004 Phase 2: Live の台帳。**Live 中は投擲に反応しない** (観客として集まっている " +
            "犬をボールで連れ出さない)。null なら従来どおり。読むだけで同期変数も Ownership も動かない")]
        public KD_LiveSystem liveSystem;
        public KD_GroupActivities activities;
        [UdonSynced] private int raceBallIndex = -1;
        [UdonSynced] private int completedRaceRound;
        private int _boundRaceBall = -2;
        public int CompletedRaceRound { get { return completedRaceRound; } } // net: callable-ok: read-only race receipt

        private void BindRaceBall()
        {
            if (_boundRaceBall == raceBallIndex) return;
            KD_BallController ball = raceBallIndex >= 0 && activities != null && raceBallIndex < activities.balls.Length ? activities.balls[raceBallIndex] : ballController;
            if (ball == null) return;
            // A remote can receive Return -> Idle+home in one snapshot. Release the OLD pickup before rebinding.
            if (_boundRaceBall >= 0 && _ballPickup != null && (activities == null || !activities.BallClaimed(_boundRaceBall, dogIndex)))
                _ballPickup.pickupable = true;
            _boundRaceBall = raceBallIndex;
            _ballT = ball.transform; _ballRb = ball.GetComponent<Rigidbody>();
            _ballPickup = ball.GetComponent<VRC_Pickup>(); _ballSync = ball.GetComponent<VRCObjectSync>();
            _ballObsValid = false;
        }

        [Header("FSM タイミング (秒)")]
        [Tooltip("投げに気付いてから走り出すまでの間 (振り向く時間)")]
        public float chaseStartDuration = 0.35f;
        [Tooltip("頭を下げてくわえる時間")]
        public float pickupDuration = 1.0f;
        [Tooltip("IMP-FET-014: 到着してから、くわえたまま立って待つ秒数 (受け渡しの「間」)。" +
                 "この間は**投げた本人だけ**が口からボールを掴める。取られなければ経過後に頭を下げて置く。" +
                 "旧 1.0 (立って待つだけ) → 5.0 (フレンド要望)")]
        public float dropWaitDuration = DefaultDropWaitDuration;
        [Tooltip("頭を下げ始めてからボールを放すまでの秒数 (頭が下がり切るのは約 0.5s)")]
        public float dropDelay = 0.8f;
        [Tooltip("ボールを放してから Idle へ戻るまでの秒数")]
        public float dropDuration = 0.9f;
        [Tooltip("この秒数追い付けなければ諦めて Idle へ戻る (届かない場所へ投げた等)")]
        public float chaseTimeout = DefaultChaseTimeout;
        [Tooltip("帰還がこの秒数で終わらなければ諦めて置く。**Chase にあって Return に無かった** " +
            "(破壊的監査 RC-2)。プレイヤーの走行 4m/s は犬の runSpeed 2.4m/s より速いので、" +
            "逃げ続けられると着地点に永久に追い付かない。その間ボールは kinematic + " +
            "pickupable=false + リセットボタンも IsCarrying で拒否 = **復帰手段がゼロ**になる。" +
            "returnSettleTimeout (足元へ着いてから測る) では届かないので、Chase と同じ絶対値を持つ")]
        public float returnTimeout = DefaultReturnTimeout;
        // IMP-NET-005: `ownershipWaitTimeout` はここにあったが**削除した**。
        // Ownership を投げ・撫でで移さなくなった (DES-NET-002 不変条件 3) ので、
        // 「移譲の確認を待つ」という状態そのものが存在しない。残すと
        // 「まだ移譲で成立させている」と読める死んだ設定になる。
        [Tooltip("ボールが 1 フレームでこれ以上動いたらリセットとみなして諦める (IMP-FET-002。投げの最速でも 1 フレーム 1m 未満)")]
        public float ballTeleportThreshold = DefaultBallTeleportThreshold;

        [Header("移動 (スクリプト駆動 — クリップは rootCurves=0)")]
        public float runSpeed = KD_DogWanderController.DefaultRunSpeed;
        public float walkSpeed = KD_DogWanderController.DefaultWalkSpeed;
        public float turnSpeedDegPerSec = DefaultTurnSpeedDegPerSec;
        [Tooltip("静止中の旋回速度 (deg/s)。**IMP-FET-008 の「最後に人を向く」専用**。" +
            "足を止めたまま速く回すと接地した足が地面を擦り「滑っている」ように見えるので、" +
            "その場旋回はここまで落とす。**KD_DogWanderController.idleTurnSpeedDegPerSec と " +
            "同じ値・同じ理由** (向こうは じゃれ合いの対面用。実測 2026-08-14 で 60 では " +
            "まだ滑って見えたため 30)。**片方だけ直さないこと** — 同じ Animator を叩く同じ犬で、" +
            "「静止中にどれだけの速さなら擦って見えないか」は 1 つしか無い")]
        public float idleTurnSpeedDegPerSec = KD_DogWanderController.DefaultIdleTurnSpeedDegPerSec;
        [Tooltip("ボールへの到着半径 (水平距離)。鼻先が +0.29m なので、それより少し先で止まると「届く前にくわえた」ように見える")]
        public float chaseArriveRadius = DefaultChaseArriveRadius;
        [Tooltip("投げた本人の手前で止まる距離。0.55 = ほぼ足元 (IMP-FET-003 確定値。0.2 は近すぎた)")]
        public float returnStopDistance = DefaultReturnStopDistance;
        [Tooltip("帰還でこの距離より遠い間は走る。近づいたら歩きに落とす。" +
            "1.2 = ほぼ足元まで走って最後の 0.65m だけ歩く (IMP-FET-004。3.0 は走りが早く終わりすぎた)")]
        public float returnRunDistance = 1.2f;

        [Header("群れ (IMP-DOG-008 — 帰還の着地点を「点」から「扇」へ)")]
        [Tooltip("犬の名簿。Tools/KominkaDogRun/Dog Pack が設定する。" +
            "null なら 1 匹版と同じ振る舞い (着地点はプレイヤーへの直線上)")]
        public KD_DogPack pack;
        [Tooltip("名簿の中での自分の番号。Setup が設定する。-1 = 未配線")]
        public int dogIndex = -1;
        [Tooltip("隣のスロットからどれだけ余分に開くか。1.0 = 体が触れる限界ぴったり")]
        public float returnSpread = 1.15f;
        [Tooltip("**IMP-NET-004 以降ランタイム未使用** (扇スロットが同期値由来になり" +
            "「先客の実位置」を読まなくなった)。Verify の互換のため残してある")]
        public float slotNeighborRange = 2.0f;
        [Tooltip("着地点を探して回り込み続ける最大秒数。超えたら足元まで来ている限り置く " +
            "(先客が動き続けても Fetch が終わらなくならないための保険)。" +
            "**測り始めるのは足元へ着いてから** — 帰り道の長さを食わない")]
        public float returnSettleTimeout = 4f;

        [Header("加減速 (滑り対策 — 2026-08-12 Play フィードバック)")]
        // 速度を階段状 (0 ⇄ runSpeed) に切り替えると、Animator の 0.25s ブレンドと
        // 噛み合わずに滑る。止まる過程・出る過程そのものを作り、Run/Walk は
        // 「今どれだけ動いているか」から導出する (状態からではなく)
        [Tooltip("発進の加速度 (m/s^2)。小さいほど脚が付いてくるが、反応が鈍く見える")]
        public float acceleration = KD_DogWanderController.DefaultAcceleration;
        [Tooltip("停止の減速度 (m/s^2)。到着点の手前からこの減速で 0 になるよう自動で速度を絞る。" +
            "36 = 走り 2.4m/s から 0.08m で止まる (IMP-FET-004「急に止まる感じでいい」。" +
            "5 だと低速で這う区間が 0.58m 残り、そこで足が滑って見えた)。" +
            "12 でもまだ 0.24m / 0.20s あり、うち Walk クリップの区間は 0.14s = 半歩未満。" +
            "半歩は目にアニメーションとして読めず「静止ポーズになったのにまだ進む」に " +
            "見えたため上げた。24 でも滑りが残ったので 36 へ " +
            "(2026-08-14 Play フィードバック 2 巡目)。Wander 側と揃える")]
        public float deceleration = KD_DogWanderController.DefaultDeceleration;
        [Tooltip("この速度以下は「完全に止まった」とみなす。歩き/走りのアニメはここで切り、" +
            "**移動もここで止める** (デッドバンド)。表示だけの閾値にすると、Animator が " +
            "静止ポーズなのに数 cm/s で這い続けて滑って見える " +
            "(2026-08-14 の計測でこれが滑りの本体と判明)")]
        public float stopSpeedThreshold = KD_DogWanderController.DefaultStopSpeedThreshold;
        [Tooltip("走りアニメに切り替える速度 (walkSpeed と runSpeed の間)")]
        public float runAnimSpeed = KD_DogWanderController.DefaultRunAnimSpeed;
        [Tooltip("実速度が 0 になってから静止ポーズ (Idle / Pickup) へ落とすまでの保持時間 (秒)。" +
            "足が止まるより先にポーズへ入ると「静止ポーズなのにまだ進む」= 滑って見えるため、" +
            "アニメの側を後ろへずらす。0 で無効 (従来どおり即座に落とす)。Wander 側と揃える")]
        public float stopAnimHold = 0.1f;

        [Header("接地 (wildlife-area WA_Animal の方式)")]
        [Tooltip("接地判定に使うレイヤー。既定は Default + Environment。地形のレイヤーが違うならここを直す")]
        public LayerMask groundLayerMask = (1 << 0) | (1 << 11);
        [Tooltip("レイ開始の高さ (現在位置からの上方オフセット)。古民家は屋根・縁側があるため低め — 大きくすると屋根の下で天井を拾って犬が屋根に乗る")]
        public float groundRayOffset = 0.5f;
        [Tooltip("レイの距離 (下向き)")]
        public float groundRayDistance = 4f;

        // 主状態 (int で持つ。UdonSharp なので enum を増やさない)
        const int StateIdle = 0;
        const int StateChaseStart = 1;
        const int StateChase = 2;
        const int StatePickup = 3;
        const int StateReturn = 4;
        const int StateDrop = 5;

        /// <summary>着地点を探す時間を測り始める帯 (プレイヤーからこの距離 + returnStopDistance)。
        /// 打ち切りで置く位置の上限でもある。</summary>
        const float ReturnSettleBand = 0.25f;

        /// <summary><see cref="returnTimeout"/> の既定値 (秒)。**宣言の初期値と Start の
        /// 0 フォールバックが同じ正本を見る** (IMP-INF-001 / B の方針)。
        /// 20 = 庭の端 (約 20m) から走って帰る 8.3s に、回り込みと減速の余裕を足した値。
        /// 正常な帰還がこれに触ることは無い — 触ったら「追い付けていない」が確定している。</summary>
        public const float DefaultReturnTimeout = 20f;

        // ---- 既定値の正本 (IMP-INF-001 / B の方針を Fetch 固有の値へも広げた) ----
        // **宣言の初期値と Start の 0 フォールバックが同じ定数を見る。**
        // 破壊的監査 RC-1: 下の 6 本は**どちらの検査にも入っていなかった** —
        // 既存シーンの UdonBehaviour へ後から足したフィールドは 0 で入るので、
        // 「Verify は全 PASS なのに犬が無言で Fetch を完了できない」が成立していた。
        // 脚の速さ (runSpeed / walkSpeed) は Wander と同じ犬の同じ脚なので向こうの正本を使う。
        const float DefaultChaseArriveRadius = 0.32f;
        /// <summary>IMP-FET-015: 12 → 16。壁の外の受け皿 (外側 3m) まで庭の対角から走る 35m ÷ 2.4m/s ≒ 14.6s に余裕を足した値。</summary>
        public const float DefaultChaseTimeout = 16f;
        const float DefaultTurnSpeedDegPerSec = 360f; // Wander の 300 とは別 (Fetch は振り向きが速い)
        const float DefaultBallTeleportThreshold = 2f;
        const float DefaultReturnStopDistance = 0.55f;
        /// <summary>くわえて待つ秒数の既定 (IMP-FET-014)。5 秒 = 「取らないなら落としてもいい」の長さ (ユーザー指定)。</summary>
        const float DefaultDropWaitDuration = 5f;

        /// <summary>Return → Drop の到着判定 (m)。**向き先の切り替え (facePlayer) も同じ値を
        /// 使う** — 別の値にすると「着地したのに 1 フレームあさってを向く」隙間ができる。
        /// 旧: 2 箇所の裸の 0.05f をコメントで「同じ値」と結んでいた (IMP-INF-001 / C)。</summary>
        const float ReturnArriveEpsilon = 0.05f;

        // ---- petState のビット詰め (IMP-NET-001 / IMP-INF-001 D) ----
        // 下位 2 ビット = ゾーン (KD_DogPetController.Zone* の値がそのまま入る)、
        // その上の 1 ビット = 両手成立。**Zone* が 2 ビットに収まることは
        // Verify (Dog Unit/4) が機械検査する** — ここは KD_DogPetController と
        // クラスを跨いだ約束で、型では守れないため検査で守る。
        //
        // **IMP-NET-005 (2026-08-22): 今この詰め方は使っていない。**
        // 送り主が取れないので Owner はゾーンを知りようがなく、petState は 0 / -1 の
        // 2 値になった (他人の画面では既定ポーズ。撫でている本人の画面は完全ローカルで不変)。
        // **定数と読み出し (PetZone / PetTwoHands) は残してある** — Phase B で Owner が
        // GetBonePosition からゾーンを導出できるようになったら、そのまま戻せるため。
        public const int PetZoneMask = 3;
        public const int PetTwoHandsBit = 4;

        // 同期は 4 変数 (設計書 §2 + IMP-NET-001)。安易に増やさない (計画書のリスク欄)
        [UdonSynced] int dogState = StateIdle;
        // 犬が今相手にしている人。Fetch 中 = 投げた本人 / Idle 中 = 撫でている本人。-1 = 誰も
        [UdonSynced] int attentionPlayerId = -1;
        [UdonSynced] int targetObjectId = -1;    // Fetch 対象の投擲物 (Phase 1 はボール 1 個 = 0)

        /// <summary>IMP-NET-001: 撫での成立状態。**-1 = 撫でていない。**
        /// それ以外は下位 2 ビット = ゾーン (0 Head / 1 CheekL / 2 CheekR / 3 Chin)、
        /// ビット 2 = 両手成立。**1 変数に詰めているのは、増やすたびに
        /// 「画面を同期しない」という不変条件 5 が緩む方向へ効くため。**
        ///
        /// 送るのは「成立した結果 (Zone + 誰から)」だけで、手の座標は送らない。
        /// リモート側の手の位置は **VRChat がすでに同期しているアバターの IK から読む** —
        /// 帯域ゼロで、しかも**受信クライアントが描いている手の位置そのもの**なので、
        /// 犬の位置がずれていても「その画面では手を見ている」が必ず成立する。</summary>
        [UdonSynced] int petState = -1;

        Transform _ballT;
        Rigidbody _ballRb;
        VRC_Pickup _ballPickup;
        VRCObjectSync _ballSync;
        float _stateEnteredTime; // Owner のタイムアウト判定に使う
        bool _ballReleased;      // Drop 中に一度だけ放すためのガード (毎フレーム放すと速度が消され続けて浮く)
        Vector3 _prevBallPos;    // テレポート検知 (IMP-FET-002)。追跡系状態でのみ有効
        bool _prevBallValid;
        int _lastAppliedState = -1; // ApplyState の変化検出 (Drop 突入の撮影通知に使う)
        bool _stateAppliedOnce;     // Late Joiner の初回適用を「遷移」と誤検出しないためのガード
        float _speed;               // 実移動速度 (m/s)。Run/Walk アニメはこれから導出する
        float _stopHoldUntil;       // 実速度 0 のあともこの時刻まで脚を動かし続ける
        bool _heldRunning;          // 保持中に続ける脚 (Run か Walk か)
        bool _animMoving;           // Animator へ書いた移動状態 (保持ぶんを含む。HUD が読む)

        // 名簿は Start で 1 度だけ読んでローカルへ持つ (Udon の Behaviour 跨ぎアクセスは高い)
        Transform[] _packRoots;
        float[] _packRadius;
        // IMP-NET-004: 扇スロットの集合判定に他犬の同期値 (IsCarrying / AttentionPlayerId) を
        // 読むための参照。**位置 (ローカル値) はもう読まない**
        KD_DogFetchController[] _packFetch;
        float _selfRadius;
        float _settleStartTime = -1f; // 足元へ着いた時刻 (Return 中のみ有効。-1 = まだ着いていない)

        // IMP-NET-001: ローカルの撫で判定の写し (KD_DogPetController が変化時だけ書く)。
        // 同期変数への反映は Update の突き合わせで行う — 「変わったときだけ送る」を
        // 1 箇所に閉じ込めるため (呼び出し側が送信の責任を持たない)
        bool _localPetActive;
        int _localPetZone;
        bool _localPetTwoHands;

        // ---- IMP-NET-005: Owner 権威 (DES-NET-002) ----
        bool _dogOwner = true;      // 今フレームの判定結果。**Update 冒頭で 1 回だけ引く**
        Vector3 _obsPrevPos;        // 非 Owner が実速度を測るための前フレーム位置
        bool _obsPrevValid;
        bool _petRequestSent;       // 非 Owner が ReqPetBegin を送った (終了で ReqPetEnd を送り始める)
        float _petRequestTime;      // 直近に送った時刻 (弾かれたときの再要求に使う)
        float _petEndSentTime = -1000f; // 非 Owner が直近に ReqPetEnd を送った時刻 (幽霊撫での再送に使う)
        // ---- Owner 側: ReqPetEnd の確認窓 (破壊的監査 v2 RC-10 = 幽霊撫で) ----
        // End が来ても手が犬の傍にある間は真偽が分からない。**本物の撫では
        // ReqPetBegin が 1.5s ごとに届き続ける (keep-alive)** ので、
        // End の後この窓の間 Begin が来なければ「本当に終わった」と判断して畳む
        bool _petEndPending;
        float _petEndPendingTime;
        int _petEndPendingFor = -1; // 保留が指す撫で主 (途中で撫で主が代わったら畳まない)
        float _petKeepAliveTime = -1000f; // 成立中に ReqPetBegin が届いた直近時刻
        bool _ownerSplitWarned;     // Ownership の軸が割れたことを 1 回だけ警告した
        VRCPlayerApi[] _players = new VRCPlayerApi[MaxPlayersScanned];

        // ---- 投擲の証拠を「観測」で採る (2026-08-23 の B2 NG 修正) ----
        Vector3 _ballObsPrevPos;
        bool _ballObsValid;
        float _ballLastMovingTime = -1000f; // 最後にボールが飛んでいるのを見た時刻 (2 標本目以降のみ)
        float _ballPrevMoveTime = -1000f;   // 直前に「動いた」を見た時刻 (1 標本だけでは証拠にしない)
        bool _fetchReqPending;              // 要求は来たが、まだ証拠が届いていない
        float _fetchReqTime;
        int _fetchReqKind;                  // 保留中の要求の種類 (0 = 投げ / 1 = 弱い放し。IMP-FET-015)
        int _lastReqResult;                 // 計器が読む (Req* の定数)
        // ---- IMP-FET-015: 弱い放しの証拠 = 「持たれていた → 放された」の遷移 (IsHeld は同期された保持状態) ----
        bool _ballHeldPrev;                 // 前フレームの IsHeld (Owner が Idle 中に観測)
        float _ballReleaseTime = -1000f;    // 直近に人が放した時刻。Fetch が始まる (TransitionTo) と消費される

        /// <summary>撫で開始要求が成立しないときに再送する間隔 (秒)。
        /// **帯域ではなく「片道で終わらせない」ための値**なので秒オーダーでよい。</summary>
        const float PetRequestRetryInterval = 1.5f;

        /// <summary>投擲とみなす最低速度 (m/s)。`ReqStartFetch` の認証に使う。
        ///
        /// **投げ手側の判定 (`KD_BallController.throwSpeedThreshold` 1.2) と同じ値にする**
        /// (破壊的監査 v2 RC-5)。以前は「伝播遅延で鈍った本物を通す」つもりで低くしていたが、
        /// 向きが逆 — `VRCObjectSync` の位置は数 Hz でまとめて届くので、観測される
        /// 見かけの速度は実速度より**高く**出る (数フレームぶんの移動 ÷ 1 フレームの dt)。
        /// 低くする理由は無く、低いと「投げ手が投げていないと捨てた転がり」を Owner が受理する。</summary>
        const float MinThrowSpeed = 1.2f;

        /// <summary>「動いた」2 標本を投擲の証拠とみなす間隔の上限 (秒)。
        ///
        /// **1 標本では証拠にしない** (破壊的監査 v2 RC-5) — リセットボタンの Respawn や
        /// 同期の補正は 1 回きりの瞬間移動で、投げたボールは飛んでいる間じゅう
        /// 数 Hz の更新が続く。「短い間隔で 2 回動いた」を要求するだけで、
        /// テレポート 1 発が投げの証拠に化ける経路が全部消える。</summary>
        const float ThrowSampleWindow = 0.6f;

        /// <summary>ReqPetEnd を受けてから「本当に終わったか」を確かめる窓 (秒)。
        /// 本物の撫でなら keep-alive (`ReqPetBegin` 1.5s 間隔) がこの窓の間に必ず届く。</summary>
        const float PetEndConfirmWindow = 2.5f;

        /// <summary>「最近まで飛んでいた」とみなす時間 (秒)。
        /// ボールが着地して止まってから要求が届いても成立させるための幅。</summary>
        const float ThrowEvidenceWindow = 2.0f;

        /// <summary>弱い放し (IMP-FET-015) の証拠が有効な時間 (秒)。放されてからこの間に届いた要求だけ成立する。
        /// 放した本人の通知は窓 (6 フレーム) + dropNoticeDelay (1s) の後に来るので、伝播の遅れを足しても十分。</summary>
        const float DropEvidenceWindow = 6f;

        /// <summary>要求が先に届いたときに、投擲の証拠を待つ猶予 (秒)。
        /// **Network Event と `VRCObjectSync` は別経路なので到着順が決まっていない。**
        /// これが無いと、要求が一瞬早いだけで正当な投げが捨てられる (2026-08-23 の B2 NG)。</summary>
        const float FetchRequestGrace = 1.5f;

        // ---- 計器 (2026-08-23)。**次に B2 が落ちたら理由が実機で読める**ようにする ----
        // 板と VRChat ログの両方に出る (KD_DogStopAnchor が読む)
        public const int ReqNone = 0;
        public const int ReqRejectNotOwner = 1;   // 犬の Owner でない端末へ届いた
        public const int ReqRejectNotIdle = 2;    // 既に Fetch 中
        public const int ReqWaitingEvidence = 3;  // 要求は来た。ボールが飛んだ証拠を待っている
        public const int ReqTimedOut = 4;         // 猶予内に証拠が来なかった (= 投げていない要求)
        public const int ReqAccepted = 5;         // 成立
        public const int ReqRejectSleeping = 6;   // そばで寝ている (IMP-DOG-019 Phase 5)
        public const int ReqRejectBusy = 7;       // おやつ対応中 (弱い放しだけが負ける。IMP-FET-015)

        /// <summary>手が犬に触れているとみなす距離 (m。dogRoot からの直線距離)。
        /// dogRoot は足元なので、頭を撫でる手までの実距離ぶん余裕を持たせてある。</summary>
        const float PetHandRadius = 1.2f;

        /// <summary>1 フレームでこれ以上動いていたらワープとみなし、速度に数えない。
        /// `KD_DogPack.teleportThreshold` / `KD_DogStopAnchor` と同じ値・同じ考え方。</summary>
        const float ObservedTeleportThreshold = 1.5f;

        /// <summary>撫での要求を「誰から」へ解決するときに見る最大距離 (m)。
        /// **撫では手が犬に触れる距離でしか成立しない**ので、この内側の最寄り = 撫でた人でよい。
        /// 大きめに取ってあるのは、受信側で犬の位置が少し遅れているぶんの余裕。</summary>
        const float PetAttributeRadius = 2.5f;

        /// <summary>最寄り判定で走査するプレイヤーの上限 (KD_DogWanderController と同じ作法)。</summary>
        const int MaxPlayersScanned = 32;

        /// <summary>犬が今相手にしているプレイヤー (KD_PhotoDirector が構図に使う)。</summary>
        public int AttentionPlayerId { get { return attentionPlayerId; } }

        /// <summary>追跡中か (KD_PhotoDirector の追跡ドローンが撮影直前に確認する)。</summary>
        public bool IsChasing { get { return dogState == StateChase; } }

        /// <summary>Fetch していないか (KD_DogWanderController が徘徊してよい条件)。</summary>
        public bool IsIdle { get { return dogState == StateIdle; } }

        // ---- デバッグ HUD 用 (KD_DogDebugHud が読む。表示専用で挙動には効かない) ----
        public float DebugSpeed { get { return _speed; } }

        /// <summary>直近の `ReqStartFetch` の結果 (Req* の定数)。**計器が読む窓口。**
        /// 2026-08-23 に B2 (非マスターの投げ) が落ちたとき、原因の切り分けに
        /// もう 1 セッション必要だったので、**次は実機でその場で読めるようにした。**</summary>
        // net: callable-ok: 読み取りのみ — 遠隔から呼ばれても状態が動かない
        public int DebugLastRequestResult { get { return _lastReqResult; } }
        public bool DebugAnimMoving { get { return _animMoving; } }
        public int DebugState { get { return dogState; } }

        /// <summary>くわえて帰ってきている最中か。
        ///
        /// **手元メニューの「呼ぶ」は これではなく <see cref="IsIdle"/> で見る** (IMP-FET-011) —
        /// `IsCarrying` だけだと Chase / Pickup 中が素通りして、犬の追跡を横から折れてしまう。
        /// ここが残っているのは Fetch 内部と撮影が使うため。</summary>
        public bool IsCarrying { get { return dogState == StateReturn || dogState == StateDrop || dogState == StateWaterReturn || dogState == StateWaterExit; } }

        // ---- IMP-NET-001: 撫での同期状態 (KD_DogPetController が毎フレーム導出する) ----
        // **Idle 以外では必ず「撫でていない」を返す** — Fetch が始まった瞬間に
        // リモート表示が止まる保証をここで持つ (受信の取りこぼしに依存しない)。

        /// <summary>誰かが撫でているか。</summary>
        public bool PetActive { get { return dogState == StateIdle && petState >= 0; } }

        /// <summary>撫でている本人の playerId。-1 = 誰も撫でていない。</summary>
        public int PetPlayerId { get { return (dogState == StateIdle && petState >= 0) ? attentionPlayerId : -1; } }

        /// <summary>撫でられているゾーン (KD_DogPetController の Zone* と同じ番号)。</summary>
        public int PetZone { get { return petState >= 0 ? (petState & PetZoneMask) : 0; } }

        /// <summary>両手 (大喜び) が成立しているか。</summary>
        public bool PetTwoHands { get { return petState >= 0 && (petState & PetTwoHandsBit) != 0; } }

        void Start()
        {
            // 配線漏れは無言で死なせず起動時に知らせる (vrc-udonsharp)
            if (dogRoot == null || dogAnimator == null || mouthAnchor == null || ballController == null)
            {
                Debug.LogError("[KD_DogFetchController] wiring incomplete. Run Tools/KominkaDogRun/Fetch System setup.");
                enabled = false;
                return;
            }

            // 既存シーンの UdonBehaviour へ後から足したフィールドが 0 のまま入る事故を
            // 無言で通さない。加減速 0 は brakeSpeed = √(2·0·d) = 0 で犬が完全に固まり、
            // runAnimSpeed 0 は常に走りアニメになる (歩きが出なくなる)
            // IMP-FET-008: idleTurnSpeedDegPerSec が 0 だと**帰りの向き直りだけが黙って死ぬ**
            // (犬は動き続けるので気づけない = 「誰も居ないところへ帰る」が再発したまま残る)
            if (acceleration <= 0.01f || deceleration <= 0.01f
                || stopSpeedThreshold <= 0f || runAnimSpeed <= 0f
                || idleTurnSpeedDegPerSec <= 0.01f)
            {
                // 既定値の正本は KD_DogWanderController.Default* (IMP-INF-001 / B) —
                // 同じ犬の足なので Fetch 側に別の数字を持たない。宣言の初期値も同じ正本を参照する
                Debug.LogWarning("[KD_DogFetchController] locomotion tuning value is 0 - falling back to defaults.");
                if (acceleration <= 0.01f) acceleration = KD_DogWanderController.DefaultAcceleration;
                if (deceleration <= 0.01f) deceleration = KD_DogWanderController.DefaultDeceleration;
                if (stopSpeedThreshold <= 0f) stopSpeedThreshold = KD_DogWanderController.DefaultStopSpeedThreshold;
                if (runAnimSpeed <= 0f) runAnimSpeed = KD_DogWanderController.DefaultRunAnimSpeed;
                if (idleTurnSpeedDegPerSec <= 0.01f) idleTurnSpeedDegPerSec = KD_DogWanderController.DefaultIdleTurnSpeedDegPerSec;
            }

            // **破壊的監査 RC-1: FSM の距離とタイムアウトは、どちらの検査にも入っていなかった。**
            // 上の locomotion 検査 (加減速・アニメ閾値) はあるのに、Fetch が「終わる」ための
            // 値が丸ごと素通りしていた。0 のまま入ったときの壊れ方はどれも**無言**で、
            // しかも Verify は配線しか見ないので機械検査を全 PASS する:
            //
            //   chaseArriveRadius 0  → dist <= 0 が成立せず、くわえに入れない
            //   chaseTimeout 0       → 追跡へ入った瞬間に諦める (投げても即やめる)
            //   returnTimeout 0      → 帰還へ入った瞬間に諦めて置く (**新設フィールドなので
            //                          既存シーンでは必ず 0 で入る** — この検査が無いと
            //                          RC-2 の修正がそのまま Fetch を壊す)
            //   turnSpeedDegPerSec 0 → 振り向かないまま走り出す
            //   runSpeed / walkSpeed 0 → 一歩も動かない
            //   ballTeleportThreshold 0 → **ボールが 1mm でも動けばリセット扱いで諦める**
            //   returnStopDistance 0 → 扇の半径が 0 = 全員がプレイヤーの中心へ重なる
            //   dropWaitDuration 0   → 到着した瞬間に頭を下げる = 口から取る間が無い (IMP-FET-014)
            if (chaseArriveRadius <= 0.01f || chaseTimeout <= 0.01f || returnTimeout <= 0.01f
                || turnSpeedDegPerSec <= 0.01f || runSpeed <= 0.01f || walkSpeed <= 0.01f
                || ballTeleportThreshold <= 0.01f
                || returnStopDistance <= 0.01f
                || dropWaitDuration <= 0.01f)
            {
                Debug.LogWarning("[KD_DogFetchController] FSM tuning value is 0 - falling back to defaults.");
                if (chaseArriveRadius <= 0.01f) chaseArriveRadius = DefaultChaseArriveRadius;
                if (chaseTimeout <= 0.01f) chaseTimeout = DefaultChaseTimeout;
                if (returnTimeout <= 0.01f) returnTimeout = DefaultReturnTimeout;
                if (turnSpeedDegPerSec <= 0.01f) turnSpeedDegPerSec = DefaultTurnSpeedDegPerSec;
                if (runSpeed <= 0.01f) runSpeed = KD_DogWanderController.DefaultRunSpeed;
                if (walkSpeed <= 0.01f) walkSpeed = KD_DogWanderController.DefaultWalkSpeed;
                if (ballTeleportThreshold <= 0.01f) ballTeleportThreshold = DefaultBallTeleportThreshold;
                if (returnStopDistance <= 0.01f) returnStopDistance = DefaultReturnStopDistance;
                if (dropWaitDuration <= 0.01f) dropWaitDuration = DefaultDropWaitDuration;
            }

            _ballT = ballController.transform;
            _ballRb = ballController.GetComponent<Rigidbody>();
            _ballPickup = ballController.GetComponent<VRC_Pickup>();
            _ballSync = ballController.GetComponent<VRCObjectSync>();
            _stateEnteredTime = Time.time;

            // 配置が浮いていても開始時に接地させる。
            // IMP-NET-005: 非 Owner が書くと `VRCObjectSync` が配ってきた正しい位置を、
            // 入室直後の 1 フレームだけ自前のレイで上書きしてしまう
            if (IsDogOwner()) dogRoot.position = SnapToGround(dogRoot.position); // net: owner-only: IsDogOwner()

            // プレハブ内 → シーンの参照は Inspector に持たせられないため実行時に配線する
            // (vrc-udonsharp §3。逆方向のこのフィールド側は Inspector 配線で問題ない)
            ballController.fetchController = this;

            if (petController == null)
                Debug.LogWarning("[KD_DogFetchController] petController not wired - pet suppression disabled.");

            // IMP-DOG-008: 名簿。壊れていたら「扇」だけ黙って切り、Fetch 自体は 1 匹版として動かす
            // (犬が返事を返さなくなるより、重なるほうがまだ軽い — 不変条件 1)
            if (pack != null)
            {
                _packRoots = pack.dogRoots;
                _packRadius = pack.dogRadius;
                _packFetch = pack.dogFetch; // IMP-NET-004: 集合判定用 (要素 null は使用側で弾く)
                bool ok = _packRoots != null && _packRadius != null
                    && _packFetch != null
                    && _packRoots.Length == _packRadius.Length
                    && _packRoots.Length == _packFetch.Length
                    && dogIndex >= 0 && dogIndex < _packRoots.Length;
                // **全員分の半径を検査する** (Codex 指摘)。1 件でも 0 / 負だと、
                // その相手に対する必要角が 0 になって「先客が居ないのと同じ」に化ける。
                // Setup を通さずシーンを複製した場合に無言でそうなるのを防ぐ
                if (ok)
                {
                    for (int i = 0; i < _packRadius.Length; i++)
                    {
                        if (_packRadius[i] > 0.001f) continue;
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    _selfRadius = _packRadius[dogIndex];
                }
                else
                {
                    Debug.LogWarning("[KD_DogFetchController] dog pack roster invalid (dogIndex="
                        + dogIndex + ") - 帰還の扇を無効化する。Run Tools/KominkaDogRun/Dog Pack setup.");
                    _packRoots = null;
                    _packRadius = null;
                    _packFetch = null;
                }
            }

            ApplyState(); // Late Joiner: Start 時点の同期値から表示を復元する
        }

        // ---- 入力 (ローカルイベント → Owner の決定) ----

        /// <summary>ボールが投げられた (投げた本人のクライアントでだけ呼ばれる)。
        ///
        /// **IMP-NET-005: Ownership を移さない** (DES-NET-002 不変条件 3)。
        /// 非 Owner は Owner へ開始を要求するだけ。旧方式は「投げた人が Fetch システムの
        /// Owner になる」だったが、dogRoot に `VRCObjectSync` が載った以上それをやると
        /// **「状態を決める人」と「位置を書く人」が割れる**。</summary>
        public void _OnBallThrown()
        {
            if (dogState != StateIdle) return; // 追跡中の別投擲へは切り替えない (設計書 §2)
            if (SleepCalled()) return;         // 寝ている犬は投擲に反応しない (IMP-DOG-019 Phase 5)

            if (IsDogOwner())
            {
                int id = ThrowerPlayerId();
                if (id >= 0) StartFetch(id);
                return;
            }
            // 文字列で渡す (U# の nameof は Udon 側のエクスポート名と一致する保証を持たない)
            SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqStartFetch");
        }

        /// <summary>IMP-FET-015: 投げにならなかった放し (弱い放し)。放した本人のクライアントでだけ呼ばれる
        /// (KD_BallController._NoticeWeakDrop)。犬は足元のボールへ寄ってくわえ、放した人へ差し出す (小さい Fetch)。
        /// 投げと同じく Ownership は移さず、Owner へ要求するだけ。Owner 側の認証は「持たれていた → 放された」の観測。</summary>
        public void _OnBallDropped()
        {
            if (dogState != StateIdle) return;
            if (SleepCalled()) return;
            if (IsDogOwner()) { RequestDroppedFetch(); return; }
            SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqStartFetchDropped");
        }

        /// <summary>弱い放しの要求 (Owner 側)。**保留中の投擲要求を降格しない** — 投擲の証拠待ちの間に別人の放しの通知が届いても、投擲が勝つ (Codex 指摘)。</summary>
        void RequestDroppedFetch()
        {
            if (_fetchReqPending && _fetchReqKind == 0) return;
            _fetchReqKind = 1;
            TryStartFetchFromRequest(); // 証拠待ちなら保留 (猶予は投げと同じ FetchRequestGrace)、拒否なら計器へ書いて畳む
        }

        /// <summary>Owner への「弱い放し」の開始要求 (IMP-FET-015)。ReqStartFetch と同じ型で、証拠だけが違う
        /// (飛んだ形跡ではなく「人が持っていて放した」の観測)。おやつ対応中の犬は反応しない。</summary>
        // net: callable-ok: Owner 権威への遷移要求 (DES-NET-002 不変条件 3)。Owner / dogState / 放しの実体を自分で検査する
        public void ReqStartFetchDropped()
        {
            if (!IsDogOwner()) { _lastReqResult = ReqRejectNotOwner; return; }
            if (dogState != StateIdle) { _lastReqResult = ReqRejectNotIdle; return; }
            if (SleepCalled()) { _lastReqResult = ReqRejectSleeping; return; }
            RequestDroppedFetch();
        }

        /// <summary>Owner への Fetch 開始要求 (IMP-NET-005 A5)。
        ///
        /// **Owner でなければ何もせず、`dogState` も投げ主も自分で検査する**ので、
        /// 誰がいつ呼んでも「Idle の犬が、ボールを持っていた人へ走り出す」以上のことは起きない。
        /// </summary>
        // net: callable-ok: Owner 権威への遷移要求 (DES-NET-002 不変条件 3)。Owner / dogState / 投擲の実体を自分で検査する
        public void ReqStartFetch()
        {
            if (!IsDogOwner()) { _lastReqResult = ReqRejectNotOwner; return; }
            if (dogState != StateIdle) { _lastReqResult = ReqRejectNotIdle; return; }
            // 寝ている犬は投擲に反応しない (IMP-DOG-019 Phase 5)。
            // **ここで弾かないと `_fetchReqPending` が立ち、証拠が揃った時点で
            // `ReqAccepted` を計器へ書いてから何も起きない** = 計器が嘘をつく
            if (SleepCalled()) { _lastReqResult = ReqRejectSleeping; return; }
            _fetchReqKind = 0;

            // **要求そのものを信用しない** (Codex レビュー 2026-08-22)。
            // これはネットワーク越しに誰でも呼べるので、検査が無いと
            // 「地面に転がったままのボールで Fetch を延々と起動して犬を占有する」ができる。
            //
            // **ただし、正当な投げを 1 回でも弾いてはいけない** (2026-08-23 実機で B2 が NG)。
            // 要求 (Network Event) とボールの動き (`VRCObjectSync`) は**別々の経路で届く**ので、
            // どちらが先に着くかは決まっていない。要求が先に着いただけで捨てると、
            // 「非マスターが投げても始まらない」になる。**証拠が来るまで少しだけ待つ。**
            if (TryStartFetchFromRequest()) return;
            _fetchReqPending = true;
            _fetchReqTime = Time.time;
            _lastReqResult = ReqWaitingEvidence;
        }

        /// <summary>保留中の要求に投擲の証拠が揃ったら成立させる。成立したら true。
        /// 要求が来た瞬間と、猶予の間の毎フレームから呼ばれる。</summary>
        bool TryStartFetchFromRequest()
        {
            bool dropped = _fetchReqKind == 1;
            if (dropped)
            {
                // 弱い放しはおやつに負ける (IMP-FET-015 の優先表)。保留も残さない
                if (TreatBusy()) { _fetchReqPending = false; _lastReqResult = ReqRejectBusy; return false; }
                if (!BallLooksDropped())
                {
                    // 証拠がまだ届いていなければ猶予を取る (投げと同じ。Network Event と Pickup の同期は別経路)
                    if (!_fetchReqPending) { _fetchReqPending = true; _fetchReqTime = Time.time; _lastReqResult = ReqWaitingEvidence; }
                    return false;
                }
            }
            else if (!BallLooksThrown()) return false;
            int id = ThrowerPlayerId();
            if (id < 0) return false;
            // **保留の間に犬が寝ることがある** (Codex レビュー 2 回目 2026-09-04)。
            // `ReqStartFetch` の入口で弾いても、そこを通ったあと [一緒に寝る] が通れば
            // ここへ来る。書く前に見ないと `StartFetch` の睡眠ガードで遷移は止まるのに
            // **計器だけが「成立」を示す** — 保留を畳んで理由を書くのはここ
            if (SleepCalled())
            {
                _fetchReqPending = false;
                _lastReqResult = ReqRejectSleeping;
                return false;
            }
            // **遷移できたときだけ計器へ書く。** 入口が増えても「計器が嘘をつく」が
            // 再発しないように、成立の判定は `StartFetch` の戻り値へ一本化する
            if (!StartFetch(id)) return false;   // 保留は残す (猶予切れで ReqTimedOut になる)
            _fetchReqPending = false;
            _lastReqResult = ReqAccepted;
            return true;
        }

        /// <summary>ボールが実際に投げられた直後に見える状態か (Owner 側の認証)。
        ///
        /// 持たれていない かつ **最近まで飛んでいた**。
        /// 転がっているだけのボールや皿の上のボールでは成立しない。
        ///
        /// ## ⚠ ここは 1 度間違えた (2026-08-23 実機で B2 が NG)
        ///
        /// 最初の実装は <c>_ballRb.velocity</c> を見ていたが、
        /// **ボールの Rigidbody を持っているのは投げた人であって、犬の Owner ではない。**
        /// 犬の Owner 側ではボールの Transform を `VRCObjectSync` が動かしていて
        /// Rigidbody は物理シミュレーションしていない (だから `ForceKinematicOnRemote` がある)。
        /// つまり velocity は常に 0 で、**非マスターの投げが 1 回も通らなかった。**
        ///
        /// **認証は、認証する側が観測できる信号でやる。**
        /// 犬の `_speed` を「シミュレーションではなく観測」へ直したのと同じ話で、
        /// ボールでも同じ間違いをしていた。今は
        /// <see cref="ObserveBall"/> が Transform の差分から飛んでいたことを見ている。</summary>
        /// <summary>IMP-FET-015: 人が持っていて放した直後か (Owner 側の認証)。
        /// 持たれていない かつ 最近 IsHeld が true → false になった。犬が置いたボール (IsHeld を経ない) や
        /// 転がったままのボールでは成立しない。Fetch が始まると TransitionTo が消費する (同じ放しで二度起動しない)。</summary>
        bool BallLooksDropped()
        {
            if (_ballPickup == null) return false;
            if (_ballPickup.IsHeld) return false;
            return Time.time - _ballReleaseTime < DropEvidenceWindow;
        }

        /// <summary>おやつに関わっている最中か (接近・食事・食後の返事)。全クライアントが同期値から同じ結論になる。</summary>
        bool TreatBusy()
        {
            return pack != null && pack.dogWander != null && dogIndex >= 0 && dogIndex < pack.dogWander.Length
                && pack.dogWander[dogIndex] != null && pack.dogWander[dogIndex].IsTreatBusy;
        }

        bool BallLooksThrown()
        {
            if (_ballPickup != null && _ballPickup.IsHeld) return false;
            if (_ballT == null) return true; // 測れないなら通す (配線前でも Fetch は成立させる)
            return Time.time - _ballLastMovingTime < ThrowEvidenceWindow;
        }

        /// <summary>ボールが飛んでいたことを**観測**で記録する (Owner が Idle 中に毎フレーム)。
        ///
        /// `VRCObjectSync` が配ってくる Transform の差分なので、
        /// **ボールを誰が持っていても、犬の Owner 側で正しく読める。**
        /// テレポート (リセットボタン / Respawn) は
        /// <see cref="ballTeleportThreshold"/> で除く — あれは「投げた」ではない。</summary>
        void ObserveBall()
        {
            if (_ballT == null) { _ballObsValid = false; return; }
            Vector3 p = _ballT.position;
            float dt = Time.deltaTime;
            if (_ballObsValid && dt > 0.0001f)
            {
                float m = (p - _ballObsPrevPos).magnitude;
                // **上限を付けない** (2026-08-23 の実機 4 人で発覚)。
                //
                // 最初は `m <= ballTeleportThreshold` (2m) で「テレポートを除く」つもりだったが、
                // **犬の Owner から見るとボールは他人の持ち物 = `VRCObjectSync` 経由**で、
                // 位置は毎フレームではなく数 Hz でまとめて届く。強く投げたボールは
                // 更新の合間に 2m 以上進むので、**検出したい「速い動き」そのものが
                // テレポート扱いで捨てられていた** (計器のログで req result=4 が 6 回中 2 回)。
                //
                // そもそも上限は要らない。塞ぎたかったのは
                // **「転がったまま静止しているボールで Fetch を起動する」**ことで、
                // 静止しているボールは一度も動かない = 下限だけで足りる。
                //
                // **ただし 1 標本では証拠にしない** (破壊的監査 v2 RC-5)。
                // リセットの Respawn・Late Join の初期同期は 1 回きりの瞬間移動で、
                // これを証拠に数えると「リセットを押した人へ犬が届ける」が起きる。
                // 飛んでいるボールは更新が続くので、短い間隔の 2 標本目から数える
                if (m / dt > MinThrowSpeed)
                {
                    if (Time.time - _ballPrevMoveTime < ThrowSampleWindow)
                        _ballLastMovingTime = Time.time;
                    _ballPrevMoveTime = Time.time;
                }
            }
            _ballObsPrevPos = p;
            _ballObsValid = true;
        }

        /// <summary>投げた本人の playerId。**ボールの Owner から引く。**
        ///
        /// `SendCustomNetworkEvent` は送り主を教えてくれないが、`VRCPickup` は
        /// **掴んだ人へボールの Ownership を移す**ので、Owner 側は
        /// `Networking.GetOwner(ball)` を見るだけで「誰が投げたか」が分かる。
        /// **新しい同期変数を 1 本も足さずに設計原則 4 (誰への返事かが分かること) が保たれる。**
        ///
        /// 投げた直後に別の人がボールを掴み直すと取り違えうるが、その場合は
        /// 「掴まれたので諦める」(<see cref="GiveUp"/>) が先に成立するので害は無い。</summary>
        int ThrowerPlayerId()
        {
            if (ballController == null) return -1;
            VRCPlayerApi o = Networking.GetOwner(ballController.gameObject);
            return Utilities.IsValid(o) ? o.playerId : -1;
        }

        /// <summary>そばで寝る指名を受けているか (IMP-DOG-019 Phase 5)。
        ///
        /// **寝ている犬は投擲に反応しない** — 割り込みの順位では寝るが最上位で、
        /// 他人が投げたボールで占有が破れてはいけない (Issue §4 Phase 5)。
        /// 入力は枠の同期値だけなので**全クライアントで同じ結論**になり、
        /// 「投げた人には走って見えて、呼んだ人には寝て見える」が起きない。
        ///
        /// ⚠ **これは `dogState` では代用できない。** 寝ている犬の `dogState` は
        /// Idle のまま (寝る絵を出すのは徘徊層) なので、
        /// 上の `dogState != StateIdle` ガードは 1 件も弾かない。
        ///
        /// **撫での同期経路 (`ReqPetBegin` 等) はここを通らない** — 寝たまま撫でられるのは
        /// Phase 4 の仕様なので、止めるのは Fetch の開始だけに閉じる。</summary>
        bool SleepCalled()
        {
            return sleepBoard != null && sleepBoard.IsSleeping(dogIndex);
        }

        /// <summary>Live の粒が流れている間か (IMP-PRG-004 Phase 2)。同期値の導出なので全員一致。
        /// 寝ると同じく **Fetch の開始だけ**を止める (Live 開始時に走っていた Fetch は最後まで済ませる)。</summary>
        bool LiveWatching()
        {
            return CandyBusy() || (liveSystem != null && liveSystem.IsPlaying) || (activities != null && activities.Mode == 1);
        }

        /// <summary>自分の徘徊層 (名簿の同じ番号)。未配線なら null。</summary>
        KD_DogWanderController MyWander()
        {
            if (pack == null || pack.dogWander == null || dogIndex < 0 || dogIndex >= pack.dogWander.Length) return null;
            return pack.dogWander[dogIndex];
        }

        /// <summary>Candy 演目 (集合・騎乗) とその着地・退場に拘束されているか (`CandyReserved`)。
        /// この間は **dogState が Idle のまま**なので、撫での受理 (`ReqPetBegin`) と
        /// `ApplyState` の撫で再開がこれを見る (破壊的監査 v4 FUNC-02/BUG-01)。全クライアントが同じ Live 同期から導くので一致する。</summary>
        bool CandyBusy()
        {
            KD_DogWanderController w = MyWander();
            return w != null && w.CandyReserved;
        }

        /// <summary>Fetch を始める。**遷移できたときだけ true。**
        ///
        /// 戻り値を持つのは計器のため (Codex レビュー 2 回目 2026-09-04) —
        /// 呼び出し側が「呼んだ = 成立した」と決め打つと、ここのガードで止めた分だけ
        /// **`_lastReqResult` が嘘をつく**。増える入口も同じ戻り値で守られる。</summary>
        bool StartFetch(int playerId)
        {
            if (WaterHoldsThrow(playerId)) return false;   // IMP-DOG-020: in the water, walk out first (HillsideWater.cs)
            if (hillsideLeashes != null && hillsideLeashes._IsLeashed(dogIndex)) return false;
            if (frisbeeCatch != null && frisbeeCatch.IsEngaged) return false;
            // IMP-DOG-027: 洗われ中 (最後のストロークから holdSeconds。同期値) は投げに反応しない。断り方はリードと同じ
            // (false を返すだけ。ReqStartFetch の保留は残り、猶予切れで ReqTimedOut)。wash の無い犬は常に false で素通り
            KD_DogWanderController washed = MyWander();
            if (washed != null && washed.WashHeld) return false;
            // **Fetch が始まる唯一の口。** `_OnBallThrown` (ローカル) と
            // `ReqStartFetch` (ネットワーク経由) の両方がここを通るので、
            // 判定を 1 箇所に置けば経路ごとの書き分けが要らない
            if (SleepCalled()) return false;
            if (LiveWatching()) return false;   // Live 中は観客 (IMP-PRG-004 Phase 2)
            if (activities != null && (activities.Mode == 2 || activities.BallClaimed(dogIndex, dogIndex))) return false;

            petState = -1;                // Fetch が撫でを引き取る (IMP-NET-001。attentionPlayerId も投げた本人で上書きされる)
            attentionPlayerId = playerId; // ChaseStart で投げた本人を代入 (設計書 §2)
            targetObjectId = 0;
            TransitionTo(StateChaseStart);
            return true;
        }

        void Update()
        {
            BindRaceBall();
            // ---- IMP-NET-005: Owner 権威。**ルートを書くのは Owner だけ** ----
            // 判定は常に dogRoot の Owner (不変条件 8)。非 Owner は
            // `VRCObjectSync` が配ってくる実位置の差分から _speed を作る
            _dogOwner = IsDogOwner();
            WaterThrowTick();   // IMP-DOG-020: a throw held while the dog was in the water (HillsideWater.cs)
            // Cross-object delivery can race with a throw. Once a leash is observed it wins,
            // and the existing release path restores carried-ball physics before walking.
            if (_dogOwner && hillsideLeashes != null && hillsideLeashes._IsLeashed(dogIndex) && !IsIdle)
            {
                GiveUp();
            }
            if (!_dogOwner) ObserveSpeed();
            WarnIfOwnerSplit();

            if (dogState == StateIdle)
            {
                _locomotionOutputValid = false; // Wander may write the shared Animator while idle.
                if (_dogOwner && activities != null && activities.RaceWinner == dogIndex && activities.RaceRound != completedRaceRound)
                {
                    completedRaceRound = activities.RaceRound;
                    // IMP-DOG-027 (Codex 4-2): the race winner's own path to ChaseStart honours the wash gate as StartFetch does.
                    // A refused round is marked completed like any other refusal here, so the race ends instead of retrying
                    KD_DogWanderController raceWander = MyWander();
                    if (activities.Available(dogIndex) && activities.RaceBall >= 0 && (raceWander == null || !raceWander.WashHeld))
                    {
                        raceBallIndex = activities.RaceBall; BindRaceBall();
                        petState = -1; attentionPlayerId = activities.RaceThrower; targetObjectId = 0;
                        TransitionTo(StateChaseStart); return;
                    }
                    RequestSerialization();
                }
                bool claimedElsewhere = activities != null && activities.BallClaimed(dogIndex, dogIndex) && raceBallIndex < 0;
                // 出しっぱなし保険 (vrc-networking §6「ON にした人が居なくなる」):
                // Release の SetKinematic(false) が Ownership 未確定で無視された場合、
                // ボールが永久に kinematic のまま残る (Codex 指摘・高)。
                // Idle 中に実態と突き合わせ、効くまで毎フレーム再試行する
                if (!claimedElsewhere && _dogOwner && _ballRb != null && _ballRb.isKinematic
                    && (_ballPickup == null || !_ballPickup.IsHeld))
                    ReleaseBall();

                if (_dogOwner && raceBallIndex >= 0 && (_ballRb == null || !_ballRb.isKinematic))
                { raceBallIndex = -1; BindRaceBall(); RequestSerialization(); }

                // ---- 投擲の証拠を観測し、保留中の要求を成立させる (Owner だけ) ----
                if (_dogOwner)
                {
                    ObserveBall();
                    // IMP-FET-015: 「持たれていた → 放された」を観測する (弱い放しの証拠)
                    bool heldNow = _ballPickup != null && _ballPickup.IsHeld;
                    if (_ballHeldPrev && !heldNow) _ballReleaseTime = Time.time;
                    _ballHeldPrev = heldNow;
                    // **期限を先に見る** (破壊的監査 v2 RC-5) — 成立チェックが先だと、
                    // 期限切れの古い要求が「たまたま今動いたボール」で成立してしまう
                    if (_fetchReqPending && Time.time - _fetchReqTime > FetchRequestGrace)
                    {
                        _fetchReqPending = false;
                        _lastReqResult = ReqTimedOut;
                    }
                    if (_fetchReqPending) TryStartFetchFromRequest();
                }
                else
                {
                    _ballObsValid = false; // Owner でない間の観測は当てにしない
                    _ballHeldPrev = false; _ballReleaseTime = -1000f;
                }

                // 撫でが成立するのは Idle 中だけ (IMP-NET-001)
                ReconcilePetSync();
                return;
            }

            if (WaterFetchTick()) return;   // IMP-DOG-020 step 6: water states 6-8 and the entry from Chase (HillsideWater.cs)

            // 目標位置: 追跡系はボール、帰還系は投げた本人。
            // stopDist (止まるべき点) は状態が変わっても同じ値を使う — Pickup / Drop は
            // 「そこへ向かって減速し切る」区間であって、瞬間停止ではない
            Vector3 target;
            float desiredSpeed = 0f;
            float stopDist;
            float distToPlayer = 0f;      // 帰還系のみ。走り/歩きの切り替えと着地判定に使う
            Vector3 toPlayerFlat = Vector3.zero; // 帰還系のみ。**着地点ではなく人へ向く**ための方向 (IMP-FET-008)
            if (dogState == StateChaseStart || dogState == StateChase || dogState == StatePickup)
            {
                if (_ballT == null) return;
                target = _ballT.position;
                stopDist = chaseArriveRadius * 0.7f; // 到着判定より少し奥まで詰める
                if (dogState == StateChase) desiredSpeed = runSpeed;
            }
            else // Return / Drop
            {
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(attentionPlayerId);
                if (!Utilities.IsValid(p))
                {
                    // 対象プレイヤーが居ない (退室など)。Owner が Idle へ戻す
                    // (Phase 2 で OnPlayerLeft による即時検知を足す。ここは毎フレームの保険)
                    if (_dogOwner) GiveUp();
                    return;
                }
                // IMP-DOG-008 / IMP-NET-004: 目標は「プレイヤー」ではなく
                // 「プレイヤーを中心とする半径 returnStopDistance の円周上の扇スロット」。
                // スロットは同期値 (帰還中の犬の集合 + プレイヤーの forward) だけから導出する
                // ので全クライアントで同じ絶対座標になる (ReturnSpot 参照)。
                // 自分の位置は目標の導出に使わない — 使うのは間合い判定 (insideStop) と
                // 走り/歩きの切り替えだけ (どちらも目標ではなく足の側)
                Vector3 playerPos = p.GetPosition();
                Vector3 fromPlayer = dogRoot.position - playerPos;
                fromPlayer.y = 0f;
                distToPlayer = fromPlayer.magnitude;
                toPlayerFlat = -fromPlayer; // 犬 → 人 (水平)
                target = ReturnSpot(p, playerPos);
                stopDist = 0f; // 着地点そのものが「止まるべき点」
                if (dogState == StateReturn) desiredSpeed = walkSpeed; // 遠ければ下で Run へ上げる
            }

            // 旋回と移動は水平 (XZ) で計算し、高さは移動後に地面へスナップする
            // (wildlife-area WA_Animal の方式 — 起伏のあるマップでも接地する)
            Vector3 pos = dogRoot.position;
            Vector3 to = target - pos;
            to.y = 0f;
            float dist = to.magnitude;

            // 帰還は遠い間は走り、近づいたら歩きへ (全クライアントが同じ距離から同じ結論になる)。
            // **判定はプレイヤーまでの距離** — 着地点までの距離で測ると、扇でずれた分だけ
            // 走りに落ちる境界が動いてしまう (IMP-FET-004 で詰めた値を保つ)
            if (dogState == StateReturn && distToPlayer > returnRunDistance) desiredSpeed = runSpeed;

            // プレイヤーの方が寄ってきて既に間合いの内側に居るなら、**後ずさりして取り直さない**。
            // 1 匹版は remain = dist - returnStopDistance が負 → 0 で「その場に止まる」だったので、
            // 着地点方式でも同じ見え方を保つ (着地点は体の後ろにあるため、素直に追うと
            // プレイヤーに背を向けて歩き去ってしまう)
            bool insideStop = (dogState == StateReturn || dogState == StateDrop)
                && distToPlayer <= returnStopDistance;
            if (insideStop) desiredSpeed = 0f;

            // 止まるべき点までの残り。ここが 0 になる時点で速度も 0 にする
            float remain = dist - stopDist;
            if (remain < 0f) remain = 0f;

            // 到着点でちょうど 0 になる速度 (v = √(2ad)) で目標速度を頭打ちにする。
            // 「完全に止まってから足を止める」には、まず止まる過程が要る
            float brakeSpeed = Mathf.Sqrt(2f * deceleration * remain);
            if (desiredSpeed > brakeSpeed) desiredSpeed = brakeSpeed;

            // 実速度を目標へ寄せる (発進は加速度、減速は減速度)。
            // IMP-NET-005: 非 Owner の _speed は Update 冒頭の ObserveSpeed が
            // 観測値で埋めているので、ここで上書きしない
            if (_dogOwner)
            {
                float rate = (desiredSpeed > _speed ? acceleration : deceleration) * Time.deltaTime;
                _speed = Mathf.MoveTowards(_speed, desiredSpeed, rate);
                if (remain <= 0f) _speed = 0f; // 端数で微動しない
            }

            // **デッドバンド (2026-08-14 Play 計測)。滑りの本体はここだった。**
            // brakeSpeed = √(2ad) は到着点のごく手前で 0.03〜0.04 に落ち着き、_speed > 0 の間は
            // 位置が書かれ続ける。Animator へ「止まった」と書いたあとも数 cm/s で這うため、
            // 静止ポーズのまま滑って見えていた。√ の中に a が居るので**減速度を上げると
            // この速度はむしろ上がる** (12 → 24 → 36 が効かなかった理由)。
            //
            // 不変条件: Animator へ「止まった」と書く速度域では、位置も書かない。
            // desiredSpeed も閾値以下のときだけ畳むのは発進を殺さないため
            // (高フレームレートでは acceleration * deltaTime が閾値を下回る)
            if (_dogOwner && _speed <= stopSpeedThreshold && desiredSpeed <= stopSpeedThreshold)
                _speed = 0f;

            // ================================================================ IMP-FET-008
            // **向き先は「進んでいる先」ではなく「誰への返事か」で決める。**
            //
            // 帰り道の間は進行方向 (着地点) を向く。**止まる段に入ったら投げた本人を向く** —
            // IMP-DOG-008 で着地点を「点」から「扇」へ変えた結果、位置はずらせたが
            // 向きが着地点のまま取り残され、「誰も居ないところへ帰った」ように見えていた
            // (扇のスロットは先客 1 匹で約 59 度ずれる = 多頭ほどあさってを向く)。
            // これは見た目の粗さではなく**設計原則 4「誰への返事かが分かること」の問題**。
            //
            // **BUG-DOG-001 の不変条件とは衝突しない。** あの不変条件の意味は
            // 「静止中に回すな」ではなく「**足が擦って見える速さで回すな**」で、
            // 答えは KD_DogWanderController.FaceTowards が既に持っている
            // (対面のために静止中の旋回を禁止せず idleTurnSpeedDegPerSec へ落としている)。
            // ここでも同じ処し方を採る。**Pickup は対象外のまま** — くわえる瞬間に
            // 向きを変える理由が無く、静止ポーズの最中に回る絵だけが増える。
            bool facePlayer = dogState == StateDrop
                || (dogState == StateReturn && (insideStop || dist <= ReturnArriveEpsilon));
            // ReturnArriveEpsilon は Return → Drop の到着判定と同じ値。着地点へ着いた瞬間から
            // 向き先が切り替わる (Drop へ落ちる 1 フレームぶんの隙間を作らない)
            bool turning = facePlayer
                || dogState == StateChaseStart || dogState == StateChase
                || (dogState == StateReturn && !insideStop); // 間合いの内側では着地点 (体の後ろ) を向かない
            // IMP-NET-005: 向きも `VRCObjectSync` が配る
            if (turning && _dogOwner)
            {
                Vector3 faceDir = facePlayer ? toPlayerFlat : to;
                float faceLen = faceDir.magnitude;
                if (faceLen > 0.02f)
                {
                    // 足が動いている間は通常の速さでよい。止まってからは擦って見える速さで回さない。
                    //
                    // **破壊的監査 RC-4a: ここは facePlayer だけを見ていた。**
                    // ChaseStart は desiredSpeed = 0 のまま turnSpeedDegPerSec (360deg/s) で
                    // 回るので、**待機ポーズのまま 0.35s × 360 = 最大 126 度その場旋回する** —
                    // BUG-DOG-001 の「原因 2: 旋回は移動と別にゲートが要る」と同じ形が
                    // Fetch 側だけ塞がれずに残っていた (Wander 側は _StepToward で塞いである)。
                    // 判定を「今フレーム足が動いているか」へ寄せると、facePlayer も
                    // ChaseStart も同じ 1 つの規則で処理される
                    float turnRate = (_speed <= stopSpeedThreshold)
                        ? idleTurnSpeedDegPerSec : turnSpeedDegPerSec;
                    Quaternion look = Quaternion.LookRotation(faceDir / faceLen, Vector3.up);
                    dogRoot.rotation = Quaternion.RotateTowards(dogRoot.rotation, look, // net: owner-only: turning && _dogOwner
                        turnRate * Time.deltaTime);
                }
            }
            // IMP-NET-005: 位置を書くのは Owner だけ
            bool riverBlocked = false; // IMP-DOG-020: 川の帯へ入る歩は進めず、下の Owner 判定で GiveUp する
            if (_dogOwner && _speed > 0f && dist > 0.0001f)
            {
                float step = _speed * Time.deltaTime;
                if (step > remain) step = remain;
                if (_BlockedByRiver(pos, pos + (to / dist) * Mathf.Max(step, RiverProbe)))
                {
                    _speed = 0f;
                    riverBlocked = true;
                }
                else
                {
                    pos += (to / dist) * step;
                    dogRoot.position = SnapToGround(pos); // net: owner-only: _dogOwner
                }
            }

            UpdateLocomotionAnim();

            // ---- ここから Owner だけの状態遷移判定 ----
            if (!_dogOwner) return;
            // IMP-DOG-021: a Chase stopped at the band while its ball is still coming down waits for it (HillsideWater.RiverBlockWait)
            if (riverBlocked) { if (RiverBlockWait()) return; GiveUpAtRiver(); return; }
            float elapsed = Time.time - _stateEnteredTime;

            // 誰かがボールを掴んだら諦める (pickupable=false が届く前の一瞬に
            // 非 Owner が掴む race の対策。IsHeld は同期された保持状態なので Owner から見える)。
            // **Return / Drop も対象** (破壊的監査 v2 RC-12) — 咥えて帰る最中に
            // 掴まれると、全クライアントの口元貼り付けと保持者の手がボールを
            // 奪い合い続ける。掴まれた時点でボールは保持者のものとして手放す
            if (BallHeld())
            {
                if (_claimNext >= 0) Debug.Log("[KD_DogFetchController] give-up dog=" + dogIndex + " state=" + dogState + " reason=held-while-claiming");
                GiveUp();
                return;
            }
            // RC-17 (破壊監査 2026-09-28): while the ball is in the mouth it must stay ours and frozen (Owner.cs HoldTick)
            if ((dogState == StateReturn || (dogState == StateDrop && !_ballReleased)) && HoldTick()) return;

            // リセットボタン等でボールが 1 フレームに大きく動いたら諦める (IMP-FET-002 —
            // ボールが消えたのに追い続けない)。テレポート検知なのでネットワークイベント
            // 不要で Late Joiner 問題も出ない。追跡系 (ボールが自由な状態) だけ監視する —
            // Return/Drop 中は LateUpdate が毎フレーム口元へ動かすため検知に使えない
            if (dogState == StateChaseStart || dogState == StateChase || dogState == StatePickup)
            {
                Vector3 ballPos = _ballT.position;
                if (_prevBallValid && (ballPos - _prevBallPos).sqrMagnitude
                        > ballTeleportThreshold * ballTeleportThreshold)
                {
                    _prevBallValid = false;
                    GiveUp();
                    return;
                }
                _prevBallPos = ballPos;
                _prevBallValid = true;
            }
            else
            {
                _prevBallValid = false;
            }

            if (dogState == StateChaseStart)
            {
                if (elapsed >= chaseStartDuration) TransitionTo(StateChase);
            }
            else if (dogState == StateChase)
            {
                if (dist <= chaseArriveRadius) { TransitionTo(StatePickup); return; }
                if (elapsed > chaseTimeout) GiveUp();
            }
            else if (dogState == StatePickup)
            {
                // RC-17: Return only once the ball is ours and frozen (BeginCarry waits here, asks once a second, gives up after 2 s)
                if (elapsed >= pickupDuration) BeginCarry(StateReturn);
            }
            else if (dogState == StateReturn)
            {
                // dist は「着地点まで」。
                //
                // 着地点が逃げ続ける場合 (IMP-NET-004 以降はプレイヤーが回り続ける /
                // 他犬の帰還でスロットがずれ直る) に Fetch が終わらなくならないよう打ち切る。
                // **測り始めるのは足元へ着いてから** (Codex 指摘) — Return 突入から数えると、
                // 遠くから帰るときに回り込みへ使える時間がほとんど残らない
                // **絶対タイムアウト (破壊的監査 RC-2)。** 下の settle 打ち切りは
                // 「足元へ着いてから」しか武装しないので、そもそも足元へ着けないケース
                // (プレイヤーが走って逃げ続ける = 4m/s > runSpeed 2.4m/s) を拾えない。
                // **Drop ではなく GiveUp** — 追い付けていない以上「受け渡し」の絵にならず、
                // その場でボールを放すほうが復帰手段 (掴み直し・リセット) が全部戻る
                // IMP-DOG-021 (E-3): after a river exit the limit is the path to the thrower / run speed + slack when that is longer
                if (elapsed > Mathf.Max(returnTimeout, _returnTimeoutExtra)) { GiveUp(); return; }

                bool nearPlayer = distToPlayer <= returnStopDistance + ReturnSettleBand;
                if (nearPlayer && _settleStartTime < 0f) _settleStartTime = Time.time;
                if (dist <= ReturnArriveEpsilon || insideStop
                    || (nearPlayer && _settleStartTime >= 0f
                        && Time.time - _settleStartTime > returnSettleTimeout))
                    TransitionTo(StateDrop);
            }
            else if (dogState == StateDrop)
            {
                // 完全停止 → dropWait くわえたまま待つ (投げた本人が口から取れる。IMP-FET-014)
                //   → 頭を下げ始めて dropDelay 後に放す → dropDuration 後 Idle。
                // 口から取られた場合は上の IsHeld → GiveUp が受け渡しの完了になる
                if (!_ballReleased && elapsed >= dropWaitDuration + dropDelay)
                {
                    ReleaseBall();
                    _ballReleased = true;
                }
                if (elapsed >= dropWaitDuration + dropDelay + dropDuration)
                {
                    // 未放出のまま Idle へ入るとボールが kinematic のまま浮き続けるため必ず放す
                    if (!_ballReleased) { ReleaseBall(); _ballReleased = true; }
                    TransitionTo(StateIdle);
                }
            }
        }

        /// <summary>移動アニメを **実速度から** 決める (2026-08-12 Play フィードバック)。
        ///
        /// 状態遷移で切り替えていたときは、まだ減速し切っていない犬の脚が先に止まり
        /// 「滑って」見えた。速度が stopSpeedThreshold を割るまで歩き/走りを続け、
        /// 完全に止まってからくわえ/置きのポーズ (静止画) へ入る。
        /// 全クライアントが同じ導出をする (速度も同期変数から導く量)。
        ///
        /// IMP-FET-004: ここで Bool を落としても、Animator 側が 0.25s かけてブレンドすると
        /// 結局「動いているのに脚が待機へ寄る」ため滑って見えた。Walk/Run の出入りは
        /// AC_KD_Dog 側でブレンド 0 (カット) にしてある — **この関数と対で意味を持つ**。</summary>
        bool _locomotionOutputValid;
        Animator _locomotionOutputTarget;
        bool _writtenRun, _writtenWalk, _writtenPickup;

        void OnEnable() { ResetLocomotionOutput(); }

        void ResetLocomotionOutput() { _locomotionOutputValid = false; }

        void WriteLocomotionAnimation(bool running, bool walking, bool pickup)
        {
            if (!dogAnimator.isActiveAndEnabled) { _locomotionOutputValid = false; return; }
            bool force = !_locomotionOutputValid || _locomotionOutputTarget != dogAnimator;
            if (force || running != _writtenRun) dogAnimator.SetBool("Run", running);
            if (force || walking != _writtenWalk) dogAnimator.SetBool("Walk", walking);
            if (force || pickup != _writtenPickup) dogAnimator.SetBool("Pickup", pickup);
            _writtenRun = running; _writtenWalk = walking; _writtenPickup = pickup;
            _locomotionOutputTarget = dogAnimator; _locomotionOutputValid = true;
        }

        void UpdateLocomotionAnim()
        {
            if (dogAnimator == null) return;

            bool moving = _speed > stopSpeedThreshold;
            bool running = moving && _speed >= runAnimSpeed;

            // 停止後の保持 (2026-08-14 Play フィードバック 3 巡目)。
            // **足が止まるより先に静止ポーズへ入らせない。** 減速距離を 12 → 24 → 36
            // (停止 0.24m → 0.08m) と詰めても滑って見えたので、距離ではなく順序の問題として扱う。
            // 実速度が 0 になっても stopAnimHold の間は直前の脚を続ける。
            // Pickup / Drop のポーズも同じ moving を見るので、まとめて後ろへずれる
            if (moving) _stopHoldUntil = Time.time + stopAnimHold;
            else if (Time.time < _stopHoldUntil)
            {
                moving = true;
                running = _heldRunning;
            }
            _heldRunning = running;
            _animMoving = moving;

            // Keep deriving speed/hold every frame; write only changed output below.

            // くわえ (Pickup) と置き (Drop の dropWait 明け) は完全停止が条件。
            // Drop は「到着して立って待つ」間を挟んでから頭を下げる (受け渡しの間)
            bool pose = !moving
                && (dogState == StatePickup
                    || (dogState == StateDrop && (Time.time - _stateEnteredTime) >= dropWaitDuration));
            WriteLocomotionAnimation(running, moving && !running, pose);
        }

        /// <summary>
        /// IMP-DOG-008 / IMP-NET-004: 帰還の着地点。**プレイヤーを中心とする半径
        /// returnStopDistance の円周上の扇スロット** — 「点」ではなく「扇」。
        ///
        /// IMP-NET-004 で方位を**同期値のみの導出**へ差し替えた (DES-NET-001 §5 A5):
        ///
        ///   - **スロットの割り当て**: 「今この人へ帰っている犬の集合」を
        ///     <see cref="IsCarrying"/> + <see cref="AttentionPlayerId"/> (どちらも
        ///     同期変数由来) から作り、dogIndex 順に体格の必要角
        ///     (`2·asin((r隣 + r自) / 2R)`) で前から詰める。
        ///     旧 `_BearingAdjust` は**先客の実位置**で押しのけ量を決めており、
        ///     クライアント間で「押しのけるか」の結論が割れて別スロットへ着地した
        ///     (先客 1 匹で約 59 度 = 0.55m。受け渡しの「一匹分ずれる」の主因)
        ///   - **方位の基準**: プレイヤーの **forward** (VRChat が同期するアバターの向き)。
        ///     「犬が今いる方角」(ローカル) は使わない。リモートアバターの補間差 (F3) は
        ///     残るが有界で、プレイヤーが止まれば全クライアントで同じ点になる
        ///
        /// 扇は**プレイヤーの正面**に左右対称へ開く — ボールを持った犬が目の前へ
        /// 並んで来るので、設計原則 4 (誰への返事か) にもむしろ寄る。
        /// プレイヤーが回ればスロットも回るが、追い続けない保険は既存の
        /// returnSettleTimeout と insideStop (間合いの内側では取り直さない) が持つ。
        /// 集合の出入り (他犬の Fetch 開始/終了) でスロットがずれ直るのは意図どおり —
        /// 全クライアントが同じ同期値から同じずれ直りを見る。
        /// </summary>
        Vector3 ReturnSpot(VRCPlayerApi p, Vector3 playerPos)
        {
            // 方位の基準 = プレイヤーの forward (水平)。アバターのルートは直立なので
            // 水平成分が消えることは実質無いが、保険で world forward へ落とす
            Vector3 fwd = p.GetRotation() * Vector3.forward;
            fwd.y = 0f;
            float fl = fwd.magnitude;
            Vector3 dir = fl > 0.01f ? fwd / fl : Vector3.forward;
            float bearing = Mathf.Atan2(dir.z, dir.x);

            if (_packFetch != null)
            {
                // 「今この人へ帰っている犬」を dogIndex 順で詰める。自分は必ず入っている
                // (ここへ来るのは Return / Drop の間だけ)。入力は同期値と体格 (静的) のみ
                float twoR = 2f * Mathf.Max(returnStopDistance, 0.05f);
                float total = 0f; // 扇の全幅 (rad)
                float mine = 0f;  // 扇の端から自分のスロットまで (rad)
                int prev = -1;    // 直前に詰めた犬 (必要角は隣り合う 2 匹の体格から)
                int n = _packFetch.Length;
                for (int j = 0; j < n; j++)
                {
                    bool returning;
                    if (j == dogIndex)
                    {
                        returning = true;
                    }
                    else
                    {
                        // activeInHierarchy は見ない (Codex 指摘) — ローカル状態なので
                        // クライアント間で割れうる。IsCarrying / AttentionPlayerId は
                        // 同期変数由来なので、無効化された犬が Return のまま残っても
                        // **全クライアントが同じように含める** = スロットは割れない
                        // (null はシーン静的なので決定論を壊さない)
                        KD_DogFetchController f = _packFetch[j];
                        returning = f != null
                            && f.IsCarrying && f.AttentionPlayerId == attentionPlayerId;
                    }
                    if (!returning) continue;
                    if (prev >= 0)
                        total += 2f * Mathf.Asin(Mathf.Min(1f,
                            (_packRadius[prev] + _packRadius[j]) * returnSpread / twoR));
                    if (j == dogIndex) mine = total;
                    prev = j;
                }
                bearing += mine - total * 0.5f; // 扇全体を forward の左右対称に置く
            }

            return playerPos + new Vector3(Mathf.Cos(bearing), 0f, Mathf.Sin(bearing)) * returnStopDistance;
        }

        /// <summary>下向きレイで地面の高さへスナップする。外れたら高さを変えない
        /// (穴・池の縁で犬を落とさない — wildlife-area と同じ振る舞い)。</summary>
        Vector3 SnapToGround(Vector3 pos)
        {
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * groundRayOffset, Vector3.down, out hit,
                    groundRayDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;
            return pos;
        }

        void LateUpdate()
        {
            // くわえ表示: Return の間 + Drop の放すまで (dropDelay) はボールを口元へ
            // (表示ローカル、状態は同期変数から導出)。
            // VRCObjectSync の書き込みより後に走るため、このフレームの描画はこちらが勝つ。
            // 非 Owner の起点は自分の OnDeserialization 時刻 — 数フレームのずれは許容
            bool carrying = dogState == StateReturn || dogState == StateWaterReturn || dogState == StateWaterExit   // IMP-DOG-020
                || (dogState == StateDrop && (Time.time - _stateEnteredTime) < dropWaitDuration + dropDelay);
            // IMP-FET-014: 口から掴まれたら**手が勝つ**。Owner の Idle が届くまでの数フレーム、
            // 貼り付けを続けると保持者の手と口元がボールを奪い合う (IsHeld は同期された保持状態)
            if (carrying && _ballPickup != null && _ballPickup.IsHeld) carrying = false;
            if (carrying && _ballT != null && mouthAnchor != null)
                _ballT.position = mouthAnchor.position;
        }
    }
}
