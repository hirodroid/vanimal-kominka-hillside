using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-DOG-004: 放置時の徘徊 — Idle の下位状態 (設計書 §1。主状態 FSM は増やさない)。
    ///
    /// 同期変数ゼロ (方式 (b) — Issue の設計検討で確定):
    /// 全クライアントが共有のサーバー時刻から同じ結論を導出する。
    ///   - いつうろつくか: サーバー時刻をサイクル (cyclePeriod 秒) で割り、
    ///     サイクル番号を種にした LCG で「徘徊窓の開始・長さ」を決める
    ///   - どこへ行くか: 窓内の経過時間から目標点番号を割り、
    ///     (サイクル, 目標点) を種にした LCG で庭の円内の点を選ぶ
    ///   - 割り込み: 投げ = 同期済み dogState (Fetch が Idle でない間は完全停止)。
    ///     接近 = 同期済みプレイヤー位置 (半径内に誰か居たら立ち止まる「人懐こさ」)
    /// 位置そのものは同期しない — Fetch と同じく目標へ歩くことで自然に収束する。
    ///
    /// 徘徊は「時刻で決まったレール」を歩くだけで、状態を持たない。
    /// 途中で止められても (接近・投げ)、レールに追い付けなくても、
    /// 次の目標点番号からまた同じレールに乗る。
    ///
    /// 収束の根拠 (接近パーズ等でクライアント間の位置が割れたとき):
    /// 目標点へ到着したクライアントはそこで停止する = **到着が座標の一致点**になる。
    /// 最悪ホップ (円の直径 2R = 6m) でも走り 2.4m/s で約 2.9s < waypointPeriod 4.5s
    /// なので、各目標点でほぼ確実に到着 = 一致が起きる (Verify が機械検査する)。
    /// Late Joiner も現在の目標点へ歩くだけで 1 ホップ以内に合流する。
    ///
    /// Animator の Run/Walk は Fetch が Idle の間しか書かない
    /// (Fetch は非 Idle の間 3 つの Bool を毎フレーム書くため衝突しない)。
    /// Run/Walk は状態ではなく **実速度** から決める — 加減速を入れた上で
    /// 「完全に止まってから足を止める」(2026-08-12 Play フィードバック)。
    ///
    /// ## IMP-DOG-009: じゃれ合い (2026-08-14)
    ///
    /// **徘徊と同じ層に置いた。** 別 Behaviour にすると「どちらが犬を動かすか」の
    /// 調停が要るうえ、加減速・接地・Animator 導出を二重に持つことになる。
    /// ここに置けば割り込み順 (**投げ &gt; 接近 &gt; じゃれ合い &gt; 徘徊**) が
    /// Update の並びそのもので保証される — Issue の「プレイヤーが関わったら即やめる」は
    /// **じゃれ合い側に中断処理を書かずに**満たされる。
    ///
    /// 予定表は徘徊と同じ**サーバー時刻の決定論レール**で、同期変数は 1 つも増やさない:
    ///   - いつ: 共有サイクル (playCyclePeriod) の中に窓が 1 回
    ///   - 誰と誰が: 名簿の全ペアから 1 組を LCG で選ぶ (1 サイクルに 1 組だけ)
    ///   - 型: 追いかけっこ / 対面
    ///   - どちらの役か: 窓内の経過時間を roleSwapPeriod で割った区間の偶奇
    ///
    /// **この予定表の乱数に <see cref="seedSalt"/> を混ぜてはいけない。**
    /// seedSalt は「犬ごとに徘徊レールをずらす」ためのもので、混ぜると
    /// 2 匹が別々の予定表を見て「両方が追いかける役」「片方だけ遊んでいる」になる。
    /// 徘徊用 <see cref="Rand01"/> と じゃれ合い用 <see cref="PlayRand"/> を
    /// **別関数に分けてあるのはこのため** (同じ関数を使い回すと事故る)。
    /// 同じ理由で、じゃれ合いのパラメータは**全犬で同じ値でなければならない** —
    /// Primary の犬を正本として Dog Pack Setup が複製し、Verify が一致を機械検査する。
    ///
    /// ## IMP-NET-002: じゃれ合いの共有目標化 (2026-08-19)
    ///
    /// じゃれ合いの 3 パターン (追う役 B9 / 対面 B10 / 離れる B11) だけが
    /// **他犬のローカル位置を目標にしており**、位置ずれを保存・増幅していた
    /// (DES-NET-001 §2)。目標の導出をすべて共有値 (サーバー時刻・名簿の静的設定・順位)
    /// へ差し替えた — 同期変数は 1 本も増えていない。規律は 2 行に要約できる:
    ///
    ///   - **目標 (どこへ向かうか) にローカル値を混ぜない** — 自分の位置・進行方向・
    ///     他犬の実位置が 1 箇所でも入ると、そこからずれが全目標へ再侵入する
    ///   - **相手の実位置を見てよいのは減速 (<see cref="ContactSpeedCap"/>) と視線だけ**
    ///
    /// 再発は規律ではなく検査で防ぐ — `node scripts/check-sync-anchors.js` が
    /// StepToward の引数とローカル位置参照を機械検査する (`// sync: local-ok:` で許可)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public partial class KD_DogWanderController : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/Wander System が設定する)")]
        public Transform dogRoot;
        public Animator dogAnimator;
        public KD_DogFetchController fetchController;
        [Tooltip("徘徊範囲の中心 (庭)。半径とあわせて家の中へ入らない範囲にする")]
        public Transform areaCenter;
        [Tooltip("撮影 (IMP-CAM-001)。徘徊の走りと じゃれ合いを通知する。null 可。" +
            "**IMP-CAM-002 で全犬が繋がる** — かつては Primary (コーギー) だけで、" +
            "他の 3 匹は 1 枚も撮られなかった")]
        public KD_PhotoDirector photoDirector;

        [Header("徘徊のスケジュール (全クライアント共通の決定論導出)")]
        [Tooltip("IMP-DOG-005: 犬ごとにレールをずらす種 (Setup が犬定義から設定する)。" +
            "0 = 1 匹版と同一。決定論方式は変えない — 同じ種なら全クライアントで同じ徘徊になる")]
        public int seedSalt = 0;
        [Tooltip("1 サイクルの長さ (秒)。この中に徘徊窓が 1 回入る — 残りが待機")]
        public float cyclePeriod = DefaultCyclePeriod;
        [Tooltip("徘徊窓の最短 (秒)")]
        public float wanderDurationMin = DefaultWanderDurationMin;
        [Tooltip("徘徊窓の最長 (秒)")]
        public float wanderDurationMax = DefaultWanderDurationMax;
        [Tooltip("目標点 1 つあたりの時間 (秒)。早く着いたら残りは立ち止まる (匂いを嗅ぐ間)")]
        public float waypointPeriod = DefaultWaypointPeriod;
        [Tooltip("Fetch (Drop) 明けにおとなしくしている秒数")]
        public float postFetchCalm = DefaultPostFetchCalm;

        [Header("範囲と移動 (Fetch と同じ資産)")]
        [Tooltip("徘徊範囲の半径 (m)。areaCenter とあわせて庭に収める")]
        public float areaRadius = DefaultAreaRadius;
        [Tooltip("目標がこの距離より遠ければ走る (「走り回ってる」の成分)")]
        public float runDistance = 2.5f;
        public float runSpeed = DefaultRunSpeed;
        public float walkSpeed = DefaultWalkSpeed;
        public float turnSpeedDegPerSec = DefaultTurnSpeedDegPerSec;
        [Tooltip("静止中の旋回速度 (deg/s)。**FaceTowards (じゃれ合いの対面) 専用** — " +
            "移動を伴う旋回は _speed が 0 の間そもそも走らない。" +
            "足を止めたまま回すと接地した足が地面を擦り「滑っている」ように見えるので、" +
            "その場旋回はここまで落とす。実測 (2026-08-14) では 60 でもまだ滑って見えたので 30。" +
            "下げすぎると相手を見ていないように見えるので、対面の成立とのトレードオフ")]
        public float idleTurnSpeedDegPerSec = DefaultIdleTurnSpeedDegPerSec;
        [Tooltip("目標点への到着半径")]
        public float arriveRadius = DefaultArriveRadius;

        [Header("加減速 (滑り対策 — KD_DogFetchController と同じ方式)")]
        // 速度を階段状に切り替えると Animator の 0.25s ブレンドと噛み合わず滑る。
        // 到着点の手前から v=√(2ad) で速度を絞り、完全に止まってから足を止める。
        // 到着点そのものは変わらない (クライアント間の一致点は保たれる)
        [Tooltip("発進の加速度 (m/s^2)")]
        public float acceleration = DefaultAcceleration;
        [Tooltip("停止の減速度 (m/s^2)。36 = 走り 2.4m/s から 0.08m で止まる。" +
            "Fetch 側と揃える (IMP-FET-004 — Walk/Run のブレンドを 0 にしたので、" +
            "5 のままだと低速で這う区間で足が滑って見える。両者は同じ Animator を叩く)。" +
            "12 では止まりきるまでに 0.24m / 0.20s あり、うち Walk クリップに割り当たるのは " +
            "0.14s = 半歩未満。半歩は目にアニメーションとして読めないため " +
            "「静止ポーズになったのにまだ進む」に見えた。24 でも滑りが残ったので " +
            "36 まで上げた (2026-08-14 Play フィードバック 2 巡目)")]
        public float deceleration = DefaultDeceleration;
        [Tooltip("この速度以下は「完全に止まった」とみなす — アニメだけでなく **移動も止める** " +
            "デッドバンド。表示だけの閾値にすると、Animator が静止ポーズなのに数 cm/s で " +
            "這い続けて滑って見える (2026-08-14 の計測でこれが滑りの本体と判明)")]
        public float stopSpeedThreshold = DefaultStopSpeedThreshold;
        [Tooltip("走りアニメに切り替える速度 (walkSpeed と runSpeed の間)")]
        public float runAnimSpeed = DefaultRunAnimSpeed;
        [Tooltip("走りクリップの再生速度の上限倍率 (実速度 / runSpeed)。IMP-DOG-009 で散歩の速さを 4 段階に " +
            "したので、runSpeed の数倍で走る場面ができた。倍率を掛けないと足が滑って見え、上限を外すと " +
            "こま送りに見える。Play で決める仮値")]
        public float runAnimScaleMax = DefaultRunAnimScaleMax;
        [Tooltip("実速度が 0 になってから静止ポーズへ落とすまでの保持時間 (秒)。" +
            "足が止まるより先にポーズへ入ると「静止ポーズなのにまだ進む」= 滑って見えるため、" +
            "アニメの側を後ろへずらす。0 で無効 (従来どおり即座に落とす)。Fetch 側と揃える")]
        public float stopAnimHold = DefaultStopAnimHold;

        [Header("人懐こさ (プレイヤー接近で立ち止まる)")]
        [Tooltip("この距離に誰か居たら立ち止まる (撫でられる距離)")]
        public float pauseRadius = 1.5f;
        [Tooltip("再開はこの距離まで離れてから (ヒステリシス)")]
        public float resumeRadius = 2.1f;

        [Header("じゃれ合い (IMP-DOG-009 — 犬同士が遊ぶ。同期変数ゼロ)")]
        [Tooltip("犬の名簿。Tools/KominkaDogRun/Dog Pack が設定する。" +
            "null なら じゃれ合いは起きない (1 匹版と同じ振る舞い)")]
        public KD_DogPack pack;
        [Tooltip("名簿の中での自分の番号。Setup が設定する。-1 = 未配線")]
        public int dogIndex = -1;
        [Tooltip("じゃれ合いのサイクル (秒)。この中に窓が 1 回入る。" +
            "**全犬で同じ値でなければならない** (Primary の犬が正本。Dog Pack Setup が複製する)")]
        public float playCyclePeriod = DefaultPlayCyclePeriod;
        [Tooltip("じゃれ合い本体の最短 (秒)。**IMP-DOG-012 で 12 → 18** " +
                 "(ユーザー要望「もっと長いことやっててほしい」)")]
        public float playDurationMin = DefaultPlayDurationMin;
        [Tooltip("じゃれ合い本体の最長 (秒)。**IMP-DOG-012 で 20 → 28**。" +
                 "playCyclePeriod > これ + playBreakOff でなければならない (Verify が見る)")]
        public float playDurationMax = DefaultPlayDurationMax;
        [Tooltip("対面になる確率。残りが追いかけっこ。**追いかけっこの方が見ていて楽しい**と" +
                 "ユーザーが名指ししたので低めに置いてある (IMP-DOG-012)")]
        public float playFaceChance = 0.25f;
        [Tooltip("1 回のじゃれ合いに参加する最大頭数 (IMP-DOG-012)。" +
                 "**2 にすると従来どおり 1 組だけ**。対面は頭数によらず必ず 2 匹")]
        public int playMaxDogs = DefaultPlayMaxDogs;
        [Tooltip("3 匹目以降が**途中参加**してくる時間帯 (本体長に対する比)。" +
                 "0.5 = 前半のうちに入ってくる。0 にすると全員が頭からそろう")]
        public float playJoinWindow = 0.5f;
        [Tooltip("追いかけっこで追う役と逃げる役が入れ替わる周期 (秒)")]
        public float roleSwapPeriod = DefaultRoleSwapPeriod;
        [Tooltip("逃げる役が次の逃げ先へ切り替える周期 (秒)。**Dog Pack Setup が導出する — " +
            "手で触らない。** 庭 (areaRadius) を横切る 1 ダッシュを走り切って少し止まれる長さ。" +
            "短いと逃げ先で止まる間が消え、追う役と同速のまま一定距離で並走し続ける " +
            "(じゃれ合いのリズムが出ない)。この値は逃げる犬しか使わないので犬ごとに違ってよい")]
        public float fleeHopPeriod = DefaultFleeHopPeriod;
        [Tooltip("本体のあとに続く「離れる」時間 (秒)。**やめ方も演技にする** — " +
            "相手から視線を外して歩いて離れる。0 にすると対面のまま固まって窓が終わる")]
        public float playBreakOff = 2.0f;
        [Tooltip("鼻を突き合わせる間合い = (半径 + 半径) × これ。" +
            "**KD_DogPack.spacing より大きくする** — 小さいと押し出しと綱引きになる")]
        public float playContactSpacing = DefaultPlayContactSpacing;
        [Tooltip("逃げ先の最小半径 (areaRadius 比)。中心付近を除くほど 1 ダッシュが長くなる")]
        public float playFleeInnerRatio = DefaultPlayFleeInnerRatio;

        [Header("投げる前の演技 (IMP-FET-005 — 設計書 §10)")]
        [Tooltip("撫でコントローラ。頭の注視と尻尾はここへ差し込む " +
            "(**追加回転を書く主体を増やさない** — 設計書 §10 決定 3)。null なら首と尻尾の演技だけ無効")]
        public KD_DogPetController petController;
        [Tooltip("吠え声を鳴らす AudioSource (犬の口元)。Wander Setup が作る。null なら無音")]
        public AudioSource barkSource;
        [Tooltip("この犬の吠え声。**体格クラスは犬定義が正本** — 実測体格から導出しない " +
            "(ダックスは体幹がコーギー並みだが小型犬。設計書 §10 決定 6)")]
        public AudioClip barkClip;

        [Tooltip("催促でプレイヤーの手前どれだけで止まるか (m)。実際の間合いはこれ + 体の半径。" +
            "**近すぎると VR でボールを振る空間が無くなり、投げたボールが犬に当たる**")]
        public float nudgeStandoff = DefaultNudgeStandoff;
        [Tooltip("秒。持ち主がこれだけ安定してから動き出す。**Ownership 移譲の伝播遅れで " +
            "旧 Owner (= 最後に投げた人) を指しうる** — 0.2 秒遅れて気づく方が、" +
            "間違った人へ寄るよりずっと良い (設計書 §10 決定 4)")]
        public float holderStableTime = DefaultHolderStableTime;
        [Tooltip("催促で登れる段差の高さ (m)。壁判定のレイをこの高さから水平に打つので、" +
            "**これより低い段差 (縁側など) は障害物にならず、高いものは壁になる。** " +
            "縁側へ上がってほしくないなら縁側より低くする。\n" +
            "**実効的に登れる高さは これ − nudgeWallRadius** (球の下端で当たる)。" +
            "実測: 庭 y=-0.40 / 縁側床 y=0.00 で段差 0.40m。0.45 だと 0.33 までしか登れず縁側の下で止まった " +
            "(2026-09-04 Play: 部屋の中から呼ぶと来ない) → 0.6 へ。登る動作は無いので段差はぽんと乗る")]
        public float nudgeStepHeight = DefaultNudgeStepHeight;
        [Tooltip("壁判定の球の半径 (m)。犬の体幅より少し小さく取る")]
        public float nudgeWallRadius = DefaultNudgeWallRadius;
        [Tooltip("催促で頭を上下に振る速さ (Hz)。0 で振らない")]
        public float nudgeBobFrequency = 1.6f;
        [Tooltip("催促で頭を上下に振る幅 (m。注視先を上下へずらす量)")]
        public float nudgeBobAmplitude = 0.18f;

        [Tooltip("秒。フェイントで「飛んだはずの方向」を目で追う時間")]
        public float feintTrackDuration = DefaultFeintTrackDuration;
        [Tooltip("秒。そのあと持ち主を見返している時間。**ここが「なげてないじゃん」の山場**")]
        public float feintReturnDuration = DefaultFeintReturnDuration;
        [Tooltip("m。「飛んだはずの点」をボールからどれだけ先に置くか")]
        public float feintGazeDistance = 4f;
        [Tooltip("m。その点をどれだけ上へ置くか (投げたボールは山なりに飛ぶ)")]
        public float feintGazeRise = 1.2f;
        [Tooltip("度。見返すときに首をかしげる角度 (頬すりの cheekRollDeg と同じ作り)")]
        public float feintTiltDeg = 14f;
        [Tooltip("吠え声のピッチ揺らぎ (±これ)。**同じ犬はクリップを 1 本しか持たない**ので、" +
            "続けて吠えたときに同じ音にならないようにする。全クライアントで一致させる必要は無い " +
            "(同時に聞き比べられない)")]
        public float barkPitchJitter = 0.07f;

        [Header("はぁはぁの息 (IMP-DOG-015)")]
        // **吠えの barkSource には相乗りできない。** あちらは PlayOneShot の単発、
        // こちらは loop の持続で、同じ AudioSource だと片方が片方を切る。
        // 器の作り方 (口元へ子を作る / VRCSpatialAudioSource を付ける) は吠えと同じ。
        [Tooltip("呼吸音を鳴らす AudioSource (犬の口元)。Wander Setup が作る。null なら無音")]
        public AudioSource breathSource;
        [System.NonSerialized] public float localVoiceGain = 1f;
        private float _breathLevel;

        // 個人の設定を出力段だけに掛け、犬の呼吸状態とフェードを保つ。
        public void _ApplyVoiceVolume()
        {
            if (breathSource == null) return;
            float volume = _breathLevel * Mathf.Clamp01(localVoiceGain);
            if (!_breathVolumeValid || _breathVolumeTarget != breathSource || volume != _writtenBreathVolume)
            {
                breathSource.volume = volume;
                _writtenBreathVolume = volume;
                _breathVolumeTarget = breathSource;
                _breathVolumeValid = true;
            }
        }
        [Tooltip("運動後の呼吸音。**犬種ごとに違う** (2026-09-04 — 4 匹が同じ息をするのが " +
            "Play で聞き分けられた)。吠え (barkClipGuid) と同じく KD_DogDefs の " +
            "犬定義 breathClipGuid から配る。音源が 3 本しか無いので小型犬 2 匹は共有する。" +
            "**寝ている間は breathSleepClip へ差し替わる**")]
        public AudioClip breathClip;
        [Tooltip("IMP-DOG-023: はぁはぁの追加候補。鳴り始めに犬種の breathClip と等確率で選び、再生中は保持する。未設定なら従来音源のみ。")]
        public AudioClip breathAlternateClip;
        [Tooltip("この速度 (m/s) 以上で動いていたら「走った」と数える。" +
            "歩き 1.1 と走り 2.4 の間に置く — 歩いただけで息が上がると生き物感が嘘になる")]
        public float breathRunSpeed = DefaultBreathRunSpeed;
        [Tooltip("走った累計がこの秒数を超えたら息が上がる")]
        public float breathTriggerRunTime = DefaultBreathTriggerRunTime;
        [Tooltip("最後に走ってからこの秒数で落ち着く。**走っている間は毎フレーム延長される**ので、" +
            "走り続けている間はずっと鳴る")]
        public float breathCalmDuration = DefaultBreathCalmDuration;
        [Tooltip("走っていない間に「走った累計」が戻る速さの倍率。" +
            "1 で走った時間と同じ速さで回復する")]
        public float breathRecoverScale = DefaultBreathRecoverScale;
        [Tooltip("鳴り始め / 鳴り終わりのフェード秒数。**0 だとブツ切りになる**")]
        public float breathFadeSeconds = DefaultBreathFadeSeconds;
        [Tooltip("呼吸音の音量。**4 匹同時でもうるさくないこと**が完了条件 (IMP-DOG-015 危険 1)。" +
            "距離減衰は VRCSpatialAudioSource の Near / Far が持つ")]
        public float breathVolume = DefaultBreathVolume;

        [Header("そばに居る間の息づかい (IMP-DOG-017)")]
        // 走り (IMP-DOG-015) は「走った後」しか鳴らず、近寄ると犬が立ち止まって走る要因が
        // 消えるため、「近くに居る瞬間」と「息が上がっている瞬間」がほぼ重ならなかった。
        // 近接判定は人懐こさの _paused をそのまま読む — 音はローカル演出なので
        // 各クライアント自分基準の距離で正しい (IMP-NET-001)。
        [Tooltip("人がそば (pauseRadius) に居る間のアイドル呼吸の音量。" +
            "**走り (breathVolume) より小さくする** — 走った息と区別が付かないと嘘になる。" +
            "撫でている間は消える (顔を寄せる距離で loop が鳴り続けるとうるさい。2026-08-28 ユーザー判断)")]
        public float breathIdleVolume = DefaultBreathIdleVolume;
        [Tooltip("アイドル呼吸のフェード秒数。**走り (breathFadeSeconds) より長くする** — " +
            "近づいた瞬間にスイッチが入ったと読ませない")]
        public float breathIdleFadeSeconds = DefaultBreathIdleFadeSeconds;

        [Header("寝息 (IMP-DOG-019 Phase 4)")]
        // **寝ている犬にとって息づかいは「返事そのもの」** (DES-DOG-003 §4.1 — 寝たまま
        // 撫でられる + 寝息 が乗らないなら機能ごと出さない)。だから近接呼吸 (IMP-DOG-017) と
        // 2 点だけ性質が違う:
        //   1. **撫でていても消さない。** IMP-DOG-017 で消しているのは「顔を寄せる距離で
        //      loop がうるさい」からだが、寝ている犬は消すと返事が丸ごと無くなる
        //   2. **`_paused` に依存しない。** 呼んだ人が 1.5m の外側へ立っても寝息は続く
        //      (寝ているのは事実なので、人の距離で息が止まるのは嘘になる)
        [Tooltip("そばで寝ている間 (IsAsleep) の呼吸音の音量。**breathIdleVolume 以下**にする — " +
            "起きている犬より大きい寝息は嘘になる (Start が上回っていたら切り詰める)。" +
            "**撫でていても消えない** — 寝ている犬にとってはこれが返事 (DES-DOG-003 §4.1)")]
        public float breathSleepVolume = DefaultBreathSleepVolume;
        [Tooltip("寝ている間 (IsAsleep) に鳴らす**寝息そのものの音源** (2026-09-04)。" +
            "KD_DogDefs.SleepBreathClipGuid から配る (音源が 1 本なので全犬種で共通)。" +
            "**null なら従来どおり breathClip をピッチ (breathSleepPitch) で代用する** — " +
            "無言で寝息が消えることはない。走り由来の息 (runPant) が勝っている間は " +
            "breathClip のまま (寝る直前まで走っていた息なので)")]
        public AudioClip breathSleepClip;
        [Tooltip("**寝息クリップ (breathSleepClip) が無いときの代用**のピッチ (1 = そのまま)。" +
            "breathClip を下げて「寝ている息」に寄せるための値で、" +
            "**breathSleepClip が入っていれば使わない** (音源そのものが寝息なので 1 のまま鳴らす)。" +
            "走り由来の息 (runPant) が勝っている間も 1 のまま — 下げると走り呼吸がスローに化ける")]
        public float breathSleepPitch = DefaultBreathSleepPitch;

        [Header("休憩 (IMP-DOG-014 Phase 1 — 走った後に伏せて休む)")]
        // **疲労値・体力ゲージは作らない** (却下表の「体調」と同型)。
        // 「走った後は休む」という**時間的なリズム**として持つ。
        // 入る条件は はぁはぁ (IMP-DOG-015) と**同じ「走った累計」を共有する** —
        // だから「伏せて、はぁはぁ言って、落ち着く」が自然に一続きになる。
        [Tooltip("走った累計がこの秒数を超えたら休憩へ入りたくなる。" +
            "**呼吸 (breathTriggerRunTime) より長くする** — 息が上がるより先に伏せると順序が逆に見える")]
        public float restTriggerRunTime = DefaultRestTriggerRunTime;
        [Tooltip("休憩の最短 (秒)")]
        public float restDurationMin = DefaultRestDurationMin;
        [Tooltip("休憩の最長 (秒)。**幅を持たせるのが 4 匹が揃って見えないための本体**")]
        public float restDurationMax = DefaultRestDurationMax;
        [Tooltip("止まってからこの秒数だけ立って息を整えてから伏せる。" +
            "**走ったまま伏せると「急に畳まれた」ように見える**")]
        public float restSettleTime = DefaultRestSettleTime;
        [Tooltip("休憩を終えてから次に休憩へ入れるまでの秒数。連続で伏せ続けないための間")]
        public float restCooldown = DefaultRestCooldown;
        [Tooltip("**人が近づいたら起き上がるか。** true = 近寄ると立つ (置物に見えない)。" +
            "false = 伏せたまま撫でられる。**既定は false** (IMP-DOG-017 / 2026-08-28 ユーザー判断 — " +
            "「反応しない」はアイドル呼吸が返事で埋める。立った犬を撫でたければ Fetch へ誘えばよい)")]
        public bool restWakesOnApproach = false;
        [Tooltip("IMP-DOG-016: **徘徊の窓の外で伏せるか。** false にすると従来どおり立って待つ。" +
            "「休み」を立ち尽くしで表していたのが「アイドルしている時間が多すぎる」の正体だった。" +
            "**早着の匂いを嗅ぐ間 (IMP-DOG-011) と、人が近いときの立ち止まりは対象外**")]
        public bool idleLieDown = true;

        [Tooltip("水場 (IMP-DOG-014 Phase 2)。Wander Setup が配線する。" +
            "**null なら「その場で伏せる」だけになる** — 水場が無くてもサイクルは成立する")]
        public Transform waterPlace;
        [Tooltip("水場のどれだけ手前で止まるか (m)。実際の間合いはこれ + 体の半径")]
        public float drinkStandoff = DefaultDrinkStandoff;
        [Tooltip("水を飲んでいる秒数。頭を下げたポーズで止まっている時間")]
        public float drinkDuration = DefaultDrinkDuration;
        [Tooltip("水場のどこを見るか (水場の原点からの高さ m)。**モデルのピボットは台の底**なので、" +
            "0 のままだと犬が地面を睨む。器の水面あたりに合わせる")]
        public float drinkGazeRise = DefaultDrinkGazeRise;
        [Tooltip("水場・落ち着く場所への到着半径 (m)。**徘徊の arriveRadius とは別物** — " +
            "あちらを使うとその半径ぶん手前で止まり、器からずれて飲む")]
        public float drinkArriveRadius = DefaultDrinkArriveRadius;
        [Tooltip("飲み終わってから落ち着く場所まで移動する距離 (m)。" +
            "**水場で寝転がらせない** — 水場は通る場所・眺める場所であって寝る場所ではない")]
        public float restMoveDistance = DefaultRestMoveDistance;
        [Tooltip("水場へ向かい始めてこの秒数で着かなければ諦めてその場で伏せる。" +
            "**Fetch の returnTimeout と同じ保険** — 届かない目標を永久に追わせない")]
        public float waterApproachTimeout = DefaultWaterApproachTimeout;
        [Tooltip("水場がこれより遠ければ寄らずにその場で伏せる (m)。" +
            "庭の端から端まで歩かせると休憩ではなく移動になる")]
        public float waterMaxDistance = DefaultWaterMaxDistance;

        [Header("おやつ (IMP-TRT-001 — 設計書 §11 / Phase 4b)")]
        [Tooltip("おやつのルールの正本 (KD_TreatBox)。Treat System Setup が全犬へ同じものを配線する。" +
            "null ならおやつ機能ごと無効 (従来どおりの犬)。\n" +
            "**どのおやつへ向かうかはここが配る** — 犬は (枠, outMs) の対を読むだけで、" +
            "自分では選ばない (選ぶのはマスター 1 人)")]
        public KD_TreatBox treatBox;
        [Tooltip("m。おやつを食べる間合い (犬のルートから)。Fetch のボール到着 " +
            "(chaseArriveRadius 0.32) と同じ考え方 — 口がおやつへ届く距離")]
        public float treatArriveRadius = DefaultTreatArriveRadius;
        [Tooltip("秒。食べ終わったあと、出した人を見ている時間 (返事 — 完了条件 3)")]
        public float treatThanksDuration = DefaultTreatThanksDuration;

        [Header("そばで寝る (IMP-DOG-019 Phase 2 — 呼んだ人の横で寝る)")]
        [Tooltip("寝る枠の台帳 (KD_SleepBoard)。Sleep System Setup が全犬へ同じものを配線する。" +
            "**null なら寝るモードごと無効** (従来どおりの犬)。\n" +
            "犬はここから **占有者 (LiveSleeperOf) を読むだけ** — 犬側の同期変数は 1 本も増えない " +
            "(おやつの treatBox と同じ形)")]
        public KD_SleepBoard sleepBoard;
        [Tooltip("m。呼んだ人の中心から犬のルートまでの距離。**Fetch の returnStopDistance と同じ 0.55** — " +
            "2026-09-04 Play で「ボールを持ってきたときぐらい寄ってほしい」(ユーザー)。\n" +
            "体格ぶんの余白は足さない (頭を人へ向けて寝るので、体は人から遠ざかる側へ伸びる)")]
        public float sleepStopDistance = DefaultSleepStopDistance;
        [Tooltip("扇の詰め方 (Fetch の returnSpread 1.15 と同型)。" +
            "隣り合う 2 匹の必要角 = 2·asin((r隣 + r自) × これ / 2R)")]
        public float sleepSpread = DefaultSleepSpread;

        [Header("ライブの観客 (IMP-PRG-004 Phase 2 — 集まって伏せて見る)")]
        [Tooltip("Live の台帳 (KD_LiveSystem)。Live System Setup が全犬へ同じものを配線する。" +
            "**null なら観客参加ごと無効**。犬は **IsPlaying を読むだけ** — 犬側の同期変数は 1 本も増えない")]
        public KD_LiveSystem liveSystem;
        public KD_GroupActivities activities;
        private bool _groupReady;
        private bool _groupCalled;   // 今フレーム集合 (mode 1) に参加しているか。歩いている間も含む (InFormation が読む)
        private bool _groupSit;
        private int _lastFormation = -1;
        public bool IsGroupReady { get { return _groupReady; } } // net: callable-ok: read-only arrival
        /// <summary>隊形に参加している (集合 / Live の観客。枠へ歩いている間も含む)。KD_DogPack がこの犬どうしの押し出しを外す —
        /// 隊形は体を密着させるので、押し出しが枠への到着を阻み (到着半径の外へ押し戻される)、着いた後も枠へ戻る歩きと往復してしまう (2026-09-13)</summary>
        public bool InFormation { get { return _groupCalled || _liveCalled; } } // net: callable-ok: read-only
        private bool _activityBusy;  // 今フレーム かけっこの追走 (mode 2) / じゃれ (mode 3 の指定 2 匹) に拘束されているか
        /// <summary>催し (Live の観客 / 集合 / かけっこの追走 / じゃれの 2 匹 / Candy 演目とその着地・退場) に拘束されているか。
        /// **dogState は Idle のまま**なので Fetch の IsIdle では分からない。おやつの割当 (KD_TreatBox) が
        /// 「来ない犬に枠を配って在庫だけ減る」を避けるために読む (破壊的監査 v4 RC-01)。
        /// 撫でを止めるのは Candy 演目 (CandyReserved) だけ — 通常ライブの観客は撫でられる契約 (UpdateLive) を変えない</summary>
        public bool ShowEngaged { get { return _groupCalled || _liveCalled || _activityBusy || CandyReserved; } } // net: callable-ok: read-only
        public bool IsResting { get { return _restPhase != RestPhaseNone; } } // net: callable-ok: read-only rest
        [Tooltip("集合地点 (KD_LiveDogSpot)。**forward = ステージの向き** (Setup がステージの回転を写す)。" +
            "犬は right 方向に dogIndex 順で横一列に並ぶ")]
        public Transform liveSpot;
        [Tooltip("m。隣の犬との中心距離")]
        public float liveSpacing = DefaultLiveSpacing;

        [Header("接地 (KD_DogFetchController と同じ値にする)")]
        public LayerMask groundLayerMask = (1 << 0) | (1 << 11);
        public float groundRayOffset = 0.5f;
        public float groundRayDistance = 4f;

        VRCPlayerApi[] _players = new VRCPlayerApi[MaxPlayers];
        float _lastBusyTime;     // Fetch が Idle でなかった最後の時刻 (ローカル観測)
        bool _paused;            // 接近による停止 (ヒステリシス付き)

        // ---- IMP-FET-005 ----
        KD_BallController _ball;  // 自分のボール (Start で Fetch から 1 度だけ取る)
        bool _nudging;            // このフレーム催促しているか
        int _holderId = -1;       // 観測中の持ち主 (安定待ちを含む)
        float _holderSince;       // その持ち主を見始めた時刻
        float _barkedFeint = -1000f; // 吠え済みのフェイント時刻 (1 回のフェイントで 1 度だけ)

        // ---- IMP-TRT-001: おやつ (毎フレーム決め直す系は _nudging と同じ作法) ----
        bool _treatBusy;          // このフレームおやつに関わっているか (IsPausedForPlayer が読む)
        bool _eatPose;            // このフレーム食べポーズ (Animator の Pickup) を出すか
        bool _wasEatPose;         // Animator へ書いた食べポーズ (変化したときだけ書き直す)
        bool _treatEating;        // 同期値で「自分が食べ始めた」を観測済みか (エッジ検出用)
        Vector3 _treatEatPos;     // 食べ点 (食べ始めに 1 度だけ確定 — 皿へ戻っても頭が追わない)
        float _thanksUntil = -1f; // 返事 (出した人を見る) の終了時刻
        int _thanksPlayerId = -1; // 返事の相手 (食べ始めに出した人を確定しておく)
        bool _wasMoving;         // Animator へ書いた移動状態 (変化したときだけ書き直す)
        bool _wasRunning;        // 走り始めの検出 (追跡ドローンへ 1 回通知)
        float _stopHoldUntil;    // 実速度 0 のあともこの時刻まで脚を動かし続ける
        bool _heldRunning;       // 保持中に続ける脚 (Run か Walk か)
        float _wroteAnimSpeed = 1f; // Animator へ書いた再生速度 (IMP-DOG-009。変化したときだけ書き直す)
        float _speed;            // 実移動速度 (m/s)。Run/Walk アニメはこれから導出する
        Vector3 _moveDir = Vector3.forward; // 惰性で止まるときに進む向き (最後の進行方向)
        float _nextProximityCheck; // 接近判定の間引き (Udon VM で毎フレーム全員分は重い)
        float _nearestSqr = float.MaxValue;
        int _arrivedHop = int.MinValue; // 到着済みの目標点番号 (IMP-DOG-008)

        // ---- IMP-NET-005: Owner 権威 (DES-NET-002) ----
        // IMP-NET-005 Phase B (2026-09-13): 停止アンカー補正 (_SetStopAnchorCorrection / UpdateStopAnchorCorrection、割り込み 1.2)
        // は撤去した。位置は VRCObjectSync が配るので、Non-Owner が犬を寄せる仕事はもう無い (DES-NET-002 不変条件 1)。
        bool _dogOwner = true;   // 今フレームの判定結果。**Update 冒頭で 1 回だけ引く**
        Vector3 _obsPrevPos;     // 非 Owner が実速度を測るための前フレーム位置
        bool _obsPrevValid;

        // ---- IMP-DOG-015: はぁはぁ (同期変数 0 本。入力は _speed だけ) ----
        float _breathRunAccum;   // 走った累計 (秒)。走っていない間は戻る
        float _breathUntil;      // この時刻まで息が上がっている
        float _breathPitchWritten = 1f; // breathSource へ書いたピッチ (変化時だけ書く。IMP-DOG-019 Phase 4)
        // breathSource へ書いたクリップ (変化時だけ差し替える。2026-09-04)。
        // **AudioSource.clip を毎フレーム読まないための控え** — ピッチと同じ作法で、
        // 参照が違うときだけ Stop → clip → Play する (毎フレーム書くと再生位置が巻き戻り続ける)
        AudioClip _breathClipWritten;
        AudioClip _pantClip;

        // ---- IMP-DOG-014: 休憩 (同期変数 0 本。走った累計を はぁはぁ と共有する) ----
        float _restUntil = -1f;   // この時刻まで休憩中 (-1 = 休んでいない)
        float _restStillSince;    // 休憩へ入って足が止まった時刻 (restSettleTime の起点)
        float _restReadyAt;       // 次に休憩へ入れる時刻 (restCooldown)
        int _restCount;           // 何度目の休憩か (乱数のストリーム。犬ごとに seedSalt が混ざる)
        bool _wantLieDown;        // 今フレーム伏せていたいか。**下ろしてから立てる**
        bool _wasLieDown;         // Animator へ書いた値 (変化したときだけ書く)
        // IMP-DOG-010: 立ち寄り先で「座って眺める」。伏せと同じ作法 (下ろしてから立てる / 書くのは LateUpdate)。
        // 伏せは「歩き続けると起こされる」ので立ち寄りでは使わない — 座るはその代わり。
        // **最初に立てるのは Wave 3 の立ち寄り (LeashPose の場所ルール) で、それ以前の書き手は無い**
        bool _wantSit;            // 今フレーム座っていたいか
        bool _wasSit;             // Animator へ書いた値 (変化したときだけ書く)
        int _restPhase;           // RestPhase* (水場へ → 飲む → 伏せる)
        float _idleLieSince = -1f; // IMP-DOG-016: 窓の外で足が止まった時刻 (-1 = 動いている)
        float _drinkUntil;        // 飲み終わる時刻
        float _waterStartTime;    // 水場へ向かい始めた時刻 (諦めの基準)
        Vector3 _restSpot;        // 落ち着く場所 (飲み終わってから少し移動する先)
        float _restMoveStart;     // その移動を始めた時刻 (諦めの基準)
        Vector3 _restFaceTarget;  // 座ったときに向く先 (犬 / プレイヤー / 庭)

        // ---- IMP-DOG-019 Phase 2: そばで寝る (同期変数 0 本。入力は枠の同期値だけ) ----
        bool _wantSleep;      // 今フレーム寝ていたいか。**下ろしてから立てる** (_wantLieDown と同じ作法)
        bool _wasSleep;       // Animator へ書いた値 (書くのは LateUpdate の 1 箇所)
        bool _sleepCalled;    // 今フレーム寝る指名を受けているか (IsPausedForPlayer が読む)
        bool _sleepFrozen;    // 寝床へ着いた = **スロットを取り直さない** (Return の insideStop と同型)
        public float placedSleepLiftRadius = 0.4f;
        public float placedSleepLiftSpeed = 0.5f;
        bool _placedSleepActive;
        bool _liveCalled;     // 今フレーム観客として呼ばれているか (IsPausedForPlayer が読む。IMP-PRG-004)
        bool _liveFrozen;     // 集合地点へ着いた = スロットを取り直さない (寝床の凍結と同型)
        int _sleeperId = -1;  // 観測中の占有者 (呼び直し / 交代の検出)
        int _sleepCalledMs;   // その呼び出しの時刻 (同期値。変わったら並び直す)

        /// <summary>1 フレームでこれ以上動いていたらワープとみなし、速度に数えない。
        /// `KD_DogPack.teleportThreshold` / `KD_DogStopAnchor` と同じ値・同じ考え方
        /// (`VRCObjectSync` の補正・Late Join・リスポーンを「移動」と読まないため)。</summary>
        const float ObservedTeleportThreshold = 1.5f;
        const float ObservedTeleportSpeed = 12f;   // IMP-DOG-036 S1: m/s over the observation interval (sprint 7.2 m/s, with margin)

        // IMP-DOG-009: 名簿は Start で 1 度だけ読んでローカルへ持つ
        // (Udon の Behaviour 跨ぎアクセスは高い — KD_DogFetchController と同じ扱い)
        Transform[] _packRoots;
        float[] _packRadius;
        KD_DogFetchController[] _packFetch;
        KD_DogWanderController[] _packWander;
        float _selfRadius;

        // IMP-NET-002: 他犬の共有レール (徘徊 waypoint / 逃げホップ) を自分で再現するための
        // 静的設定のキャッシュ。fleeHopPeriod / playFleeInnerRatio / areaRadius は
        // **犬ごとに違いうる** (Dog Pack Setup がその犬の庭から導出する) ので、
        // 自分の値で他犬の目標を導出すると全クライアント一致が壊れる。
        // どれもシーンに焼かれた静的設定なので、全クライアントで同じ値が読める
        int[] _packSalt;
        float[] _packCyclePeriod;
        float[] _packWanderMin;
        float[] _packWanderMax;
        float[] _packWaypointPeriod;
        float[] _packAreaRadius;
        Transform[] _packAreaCenter;
        float[] _packFleeHop;
        float[] _packFleeInner;
        bool _look;        // このフレームに LookAt (対面) を立てるか。毎フレーム決め直す
        bool _wasLooking;  // Animator へ書いた LookAt (変化したときだけ書き直す)
        bool _playing;     // このフレームに じゃれ合っているか (_look と同じく毎フレーム決め直す)
        bool _playingPrev; // 前フレームの _playing (休憩 1.8 はじゃれ合い 3 より先に判定されるため — 破壊的監査 v2 RC-7)
        bool _wasPlaying;  // 撮影へ通知したエッジの記憶 (IMP-CAM-002)

        // IMP-NET-003: 停止アンカーの補正。**KD_DogStopAnchor から押し込まれる** —
        // 毎フレーム向こうへ問い合わせると Behaviour 跨ぎの読みが常時乗るので、
        // 変化したときだけ渡してもらう (_PushLocalPet と同じ形)

        const float ProximityCheckInterval = 0.25f;

        // ================================================================ 既定値の正本 (IMP-INF-001 / B)
        //
        // 「宣言の初期値」と「Start の 0 フォールバック」が別々の数字を持っていたため、
        // IMP-DOG-012 で宣言側だけ 12→18 / 20→28 / 2.4→3.0 に動き、フォールバックが
        // 旧値のまま黙って残った (このリファクタリングで発見)。以後は両方がここを参照する。
        // **接地系 (groundRayOffset 等) の宣言初期値はフォールバックが無いので対象外** —
        // シーン値の一致は Verify (Dog Unit/4) が機械検査する。

        /// <summary>移動 (locomotion) の既定値。**KD_DogFetchController も同じ値を参照する**
        /// (同じ犬の同じ Animator を叩くので、値は 1 つしか無い — BUG-DOG-001)。
        /// シーン値の一致は Verify (Dog Unit/4) が機械検査する。</summary>
        public const float DefaultAcceleration = 6f;
        public const float DefaultDeceleration = 36f;
        public const float DefaultStopSpeedThreshold = 0.05f;
        public const float DefaultRunAnimSpeed = 1.7f;
        public const float DefaultIdleTurnSpeedDegPerSec = 30f;
        /// <summary>脚の速さ。**同じ犬の同じ脚なので Fetch と 1 つの値を共有する**
        /// (破壊的監査 RC-1 でどちらの 0 検査にも入っていないと分かったので正本化した)。</summary>
        public const float DefaultRunSpeed = 2.4f;
        public const float DefaultWalkSpeed = 1.1f;
        /// <summary>走りクリップの再生速度の上限倍率 (IMP-DOG-009)。
        /// **KH_FrisbeeCatch とは共有しない** — あちらは追走用に自前の `maxRunAnimScale` を持ち、
        /// Idle へ戻るとき `Animator.speed` を 1 に戻す。`KD_DogFetchController` は再生速度を一度も書かない。
        /// Verify (Dog Unit/4) の共有定数の一致検査もこの値を見ない。散歩側だけの仮値。</summary>
        public const float DefaultRunAnimScaleMax = 2.5f;

        // ---- はぁはぁの息 (IMP-DOG-015)。**どれも Play で耳と目で決める値** ----
        /// <summary>歩き 1.1 と走り 2.4 の間。**歩いただけで息が上がると嘘になる。**</summary>
        const float DefaultBreathRunSpeed = 1.6f;
        const float DefaultBreathTriggerRunTime = 2.5f;
        const float DefaultBreathCalmDuration = 8f;
        const float DefaultBreathRecoverScale = 1f;
        const float DefaultBreathFadeSeconds = 0.8f;
        const float DefaultBreathVolume = 0.5f;

        // ---- そばに居る間の息づかい (IMP-DOG-017)。**Play で耳で決める値** ----
        /// <summary>走り (0.5) より控えめ。**走った息と区別が付かないと嘘になる。**</summary>
        const float DefaultBreathIdleVolume = 0.3f;
        /// <summary>走り (0.8s) より遅い。**「近づいたらスイッチが入った」と読ませない。**</summary>
        const float DefaultBreathIdleFadeSeconds = 1.5f;

        // ---- 寝息 (IMP-DOG-019 Phase 4)。**Play で耳で決める値** ----
        /// <summary>近接 (0.3) より小さい。**寝ている犬の息が起きている犬より大きいのは嘘。**
        /// 撫でている最中も鳴り続けるので、消さないぶん控えめに置く。</summary>
        const float DefaultBreathSleepVolume = 0.22f;
        /// <summary>1 より下げて「寝ている息」にする。下げすぎると唸り声に化けるので 0.85。</summary>
        const float DefaultBreathSleepPitch = 0.85f;

        // ---- 休憩 (IMP-DOG-014)。**幅は wildlife-area の restDurationMin/Max に倣う** ----
        // (先行実装 com.virtualcampers.wildlife-area は 5〜15 秒。あちらは野生動物の
        //  短い休みで、こちらは「遊んだ後の犬」なのでもう少し長く取る)
        const float DefaultRestTriggerRunTime = 4f;   // 呼吸 (2.5) より長い = 息が先に上がる
        const float DefaultRestDurationMin = 8f;
        const float DefaultRestDurationMax = 20f;
        const float DefaultRestSettleTime = 0.6f; // 1.2 は伏せる前の立ちが長すぎた (2026-08-23)
        const float DefaultRestCooldown = 25f;
        /// <summary>器の**半径** (m)。実測 0.288 x 0.151 x 0.300 = 直径 0.3m・高さ 0.15m。
        /// 立つ距離は これ + 体の半径 なので、**頭が器の上に来る**。
        /// **0.35 は器の直径より大きく、犬が離れて立って器 1 個ぶんずれて見えていた**
        /// (2026-08-23。実測せず勘で置いた値だった)。</summary>
        const float DefaultDrinkStandoff = 0.15f;
        const float DefaultDrinkDuration = 4f;
        const float DefaultDrinkArriveRadius = 0.12f; // 器に届く近さ。徘徊の 0.25 では遠すぎた
        const float DefaultRestMoveDistance = 1.6f;
        /// <summary>落ち着く場所の向きの散らし (rad)。**厳密でなくてよい** ので広めに取る。</summary>
        const float RestMoveSpreadRad = 1.6f;
        /// <summary>水場の扇の 1 スロットぶんの角度 (rad)。器が小さいので**狭く**取る —
        /// 広げると隣の犬が器から外れて「並んで飲んでいる」に戻る。</summary>
        const float DrinkFanStepRad = 0.45f;
        /// <summary>器の方を向けたとみなす角度 (度)。ここまで来たら頭を下げてよい。</summary>
        const float DrinkFaceToleranceDeg = 20f;
        const float DefaultDrinkGazeRise = 0.08f; // 台 + 器の高さ (scale 0.3 で置いた実測に合わせる)
        const float DefaultWaterApproachTimeout = 12f;
        const float DefaultWaterMaxDistance = 12f;

        // ---- 休憩の段階 (IMP-DOG-014)。**主状態 FSM は増やさない** — 徘徊層の内側の話 ----
        const int RestPhaseNone = 0;
        const int RestPhaseToWater = 1; // 水場へ歩いていく
        const int RestPhaseDrink = 2;   // 頭を下げて飲む
        const int RestPhaseMove = 3;    // 飲み終わって、落ち着く場所へ少し移動する
        const int RestPhaseLie = 4;     // 伏せて休む
        /// <summary>移動中の旋回速度。**Fetch は 360 で別値** (振り向きが速い)。
        /// 静止中の旋回は <see cref="DefaultIdleTurnSpeedDegPerSec"/> が両者共通。</summary>
        const float DefaultTurnSpeedDegPerSec = 300f;
        const float DefaultArriveRadius = 0.25f;
        /// <summary>実速度 0 のあとも脚を続ける時間 (秒)。**滑って見える正体はここ** —
        /// 短いと止まった瞬間にアイドルへ入り、まだ惰性で動いている体の下で足だけ止まって見える
        /// (2026-08-23 ユーザー報告「アイドルアニメーションに結構早く入るから滑って見える」)。</summary>
        const float DefaultStopAnimHold = 0.35f;

        // 徘徊レール (他犬のレール再現 (IMP-NET-002) の欠損補完も同じ値を使う)
        const float DefaultCyclePeriod = 45f;
        const float DefaultWanderDurationMin = 10f;
        const float DefaultWanderDurationMax = 20f;
        const float DefaultWaypointPeriod = 4.5f;
        const float DefaultPostFetchCalm = 12f;
        /// <summary>徘徊円の半径。**本来はシーンが正本** (庭の形なので犬の性質ではない) —
        /// ここの値は「0 で入って犬が原点に張り付く」のを防ぐためだけの床。</summary>
        const float DefaultAreaRadius = 3.0f;

        // じゃれ合い (IMP-DOG-012 確定値)
        const float DefaultPlayCyclePeriod = 50f;
        const float DefaultPlayDurationMin = 18f;
        const float DefaultPlayDurationMax = 28f;
        const int DefaultPlayMaxDogs = 4;
        const float DefaultRoleSwapPeriod = 5f;
        const float DefaultFleeHopPeriod = 3.0f;
        const float DefaultPlayContactSpacing = 1.3f;
        /// <summary>逃げ先の最小半径 (areaRadius 比)。**「ちょっと走って止まる」の主因はここ。**
        /// `fleeHopPeriod` は**最長の弦**を走り切れる長さで導出されるので、
        /// 内側まで逃げ先に許すと平均の弦が短くなり (0.55 で最長の 72%)、
        /// **早く着いた犬が残り時間ずっと立っている**。0.80 で 84% まで上がる。</summary>
        const float DefaultPlayFleeInnerRatio = 0.90f;
        /// <summary>Dog Pack Setup が 4 匹へ配るための公開版 (導出値の正本はここ 1 つ)。
        /// **0.55 → 0.80 → 0.90。** 平均の弦が最長の 72% → 84% → 89% になり、
        /// 「早く着いて残り時間ずっと立っている」が減る (2026-08-23 実測 立 21〜33%)。</summary>
        public const float DefaultPlayFleeInnerRatioPublic = 0.90f;
        const float MaxPlayFleeInnerRatio = 0.95f; // 1 に近いと逃げ先が円周の 1 点に退化する

        // 催促 / フェイント (IMP-FET-005)
        const float DefaultNudgeStandoff = 1.1f;
        const float DefaultHolderStableTime = 0.2f;
        const float DefaultFeintTrackDuration = 0.9f;
        const float DefaultFeintReturnDuration = 1.5f;
        const float DefaultNudgeStepHeight = 0.6f;   // 2026-09-04 Play: 0.45 → 0.6 (縁側 0.40m を越えるため。下の Tooltip)
        const float DefaultNudgeWallRadius = 0.12f;

        // おやつ (IMP-TRT-001)
        const float DefaultTreatArriveRadius = 0.32f; // Fetch の chaseArriveRadius と同じ根拠
        const float DefaultTreatThanksDuration = 1.6f;

        // ---- そばで寝る (IMP-DOG-019 Phase 2)。**どれも Play で目で決める値** ----
        /// <summary>人の体の縁からの余白 (m)。**実際の間合いはこれ + 体の半径。**
        /// Fetch の `returnStopDistance` 0.55 は「ボールを置ける距離」で、こちらは
        /// **横倒しになった体が人と重ならない距離**なので別値 (DES-DOG-003 §3.2 の変更点 1)。</summary>
        const float DefaultSleepStopDistance = 0.55f;
        /// <summary>扇の詰め方。Fetch の `returnSpread` と同じ 1.15 から入る。</summary>
        const float DefaultSleepSpread = 1.15f;
        /// <summary>観客の並びの間隔 (m)。6 匹で 4 m の横一列 (ラブ・サモエドの体幅 0.4 m 級が重ならない)。
        /// IMP-PRG-004 Phase 2。Play で詰める。</summary>
        const float DefaultLiveSpacing = 0.8f;
        /// <summary>寝姿勢へ入る前に、この角度まで向きを合わせる (度)。
        /// 水を飲む前の `DrinkFaceToleranceDeg` (20°) より厳しくするのは、
        /// **「人と平行に寝ている」がこの向きそのもの**だから。</summary>
        const float SleepFaceToleranceDeg = 12f;
        /// <summary>**遅れて入ってきた人のための合流路** (ms)。
        ///
        /// 到着の判定は「今フレームの扇スロットの近くで足が止まっている」なので、
        /// **犬が寝たあとに呼んだ人が向きを変えるとスロットが動き**、
        /// あとから入ってきたクライアントはその式では二度と真にならない
        /// (= その人の画面でだけ犬が立って見える)。凍結は各クライアントのラッチなので、
        /// 到着の瞬間に居合わせなかった人は合流できない。
        ///
        /// そこで**同期値だけで書ける第 2 の入口**を置く: 枠がまだ生きていて、
        /// 呼ばれてからこれだけ経っていて、犬が止まっている = **もう着いている**。
        /// 枠側 (Phase 3b) の「諦める床」が `arriveTimeout` 30s で着けない枠を解放するので、
        /// **それより十分後ろ**に置けば「着けていないのに寝る」は起きない
        /// (床が効かなかったときだけ、壁際で寝る絵になる — 立ち尽くすよりはよい)。</summary>
        const int SleepSettleGraceMs = 45000;

        // ================================================================ 乱数ストリーム台帳 (IMP-INF-001 / C)
        //
        // Rand01 (徘徊 — seedSalt 入り) と PlayRand (じゃれ合い — salt 無し) の第 2 引数は
        // 「どの用途の乱数列か」を分ける名前空間。**番号がぶつかると 2 つの判断が相関し、
        // Play では「たまに変」としか見えない壊れ方をする** — ここに全ストリームを一覧する。
        // ベース番号 + 可変項 (犬番号 / 順位 / 区間 / ホップ) のものは使用帯を書く。
        //
        // ## Rand01 系 (徘徊。犬ごとに別レール)
        const int RandStreamWanderLen = 1;      // 徘徊窓の長さ
        const int RandStreamWanderStart = 2;    // 徘徊窓の開始位置
        const int RandStreamWaypointRadius = 3; // 目標点の半径 (hop が第 1 引数)
        const int RandStreamWaypointAngle = 4;  // 目標点の方位 (hop が第 1 引数)
        /// <summary>休憩の長さ (IMP-DOG-014)。**`Rand01` は `seedSalt` を混ぜる**ので、
        /// 同じ回数目の休憩でも犬ごとに長さが変わる — これが危険 3
        /// 「4 匹が一斉に伏せて庭が止まる」への答え。
        /// (じゃれ合いの `PlayRand` と違い、ここは**ずれてほしい**ので `Rand01` でよい)</summary>
        const int RandStreamRestLen = 5;
        const int RandStreamRestMoveAngle = 6; // 落ち着く場所の向き
        const int RandStreamRestFace = 7;      // 座ったときに誰を向くか
        //
        // ## PlayRand 系 (じゃれ合い。全犬共通)
        const int PlayStreamCoreLen = 1;        // 本体の長さ
        const int PlayStreamWinStart = 2;       // 窓の開始位置
        const int PlayStreamIsFace = 4;         // 型 (対面か追いかけっこか)
        const int PlayStreamFleeBaseAngle = 5;  // 逃げ先の基準方位
        const int PlayStreamPartySize = 6;      // 参加頭数
        const int PlayStreamFaceAxis = 40;      // 対面の退化ケースの軸
        const int PlayStreamBreakAxisBase = 50; // 離れるの退化ケースの軸 (+dogIndex。使用帯 50〜59)
        const int PlayStreamRollBase = 100;     // 参加抽選 (+dogIndex。使用帯 100〜109)
        const int PlayStreamJoinBase = 200;     // 途中参加の時刻 (+順位。使用帯 200〜209)
        const int PlayStreamFleeSlotBase = 300; // 逃げ役の抽選 (+区間番号。使用帯 300〜)
        const int PlayStreamFleeJitterBase = 700;  // 逃げ先の揺らぎ (+ホップ。使用帯 700〜799)
        const int PlayStreamFleeRadiusBase = 800;  // 逃げ先の半径 (+ホップ。使用帯 800〜899)
        const int PlayStreamChaseAxisBase = 900;   // 回り込みの退化ケースの軸 (+ホップ。使用帯 900〜)

        // LCG の定数一式 (wildlife-area WA_Animal と同型)。
        // **使うのは Rand01Salt と PlayRand の 2 実装だけ** — 関数を分けた判断は正しい
        // (salt を混ぜる/混ぜないの要件が正反対。各関数のコメント参照) が、
        // 定数まで 2 重に書くと片方だけ直したとき「別の乱数列」になって黙ってズレるため、
        // 定数だけここへ一本化した
        const long RandHashA = 73856093L;
        const long RandHashB = 19349663L;
        const long RandHashSalt = 83492791L;
        const long RandLcgMul = 1664525L;
        const long RandLcgAdd = 1013904223L;
        const long RandMask31 = 0x7FFFFFFF; // 全て非負に保つ (uint の % は Udon 未公開 — vrc-udonsharp §1)
        const int RandTopShift = 7;         // 上位 24bit を採る (float 丸めで 1.0 に到達しない — Codex 指摘)
        const float RandTopScale = 16777216f; // 2^24

        /// <summary>徘徊の目標点番号の合成 (hop = cycleIdx * これ + waypointIdx)。
        /// サイクルをまたいで hop が重ならないための桁ずらし。
        /// **Update (自分のレール) と WanderAnchorAt (他犬のレール再現) の両方が使う** —
        /// 片方だけ変えると、じゃれ合いのアンカーが徘徊と別の点を指す。</summary>
        const int WaypointHopStride = 31;

        /// <summary>プレイヤー配列の上限。VRChat のインスタンス上限 (~80 + マージン)。</summary>
        const int MaxPlayers = 90;

        /// <summary>逃げ先の方位を毎ホップ進める角度 (rad)。黄金角 137.5°。
        /// **乱数だけで選ぶと隣り合う逃げ先が近くなる回があり、そこで「ダッシュ」が消える。**
        /// 一定の大角度を足すことで、どのホップも庭を横切る長さになることを保証する。
        ///
        /// **public const にしてあるのは KD_DogPackSetupEditor が読むため** —
        /// Editor 側に同じ数値を書くと、片方だけ直したときに
        /// 「Verify は通るのに走り切れない」という気づけないずれになる (Codex 指摘)。</summary>
        /// <summary>対面で「回る / 止まる」を切り替える周期 (秒)。サーバー時刻から導く。</summary>
        const float FaceCyclePeriod = 3.0f;
        /// <summary>そのうち止まって鼻を突き合わせる時間 (秒)。**型を残すための間**。</summary>
        const float FacePauseSec = 0.8f;
        /// <summary>回り込む角速度 (rad/s)。速いと「ぐるぐる回る」で不自然になる。</summary>
        const float FaceOrbitRadPerSec = 0.5f;
        /// <summary>回り込む速さ (walkSpeed 比)。**歩きよりゆっくり**。</summary>
        const float FaceOrbitSpeedScale = 0.45f;

        public const float FleeTurnRad = 2.39996f;

        /// <summary>逃げ先の方位に足す揺らぎの幅 (rad)。規則的に見えないための遊び。
        /// **連続ホップ間の角度差は FleeTurnRad ± これになる** ので、
        /// 走り切れるかの検査はこれを含めた最悪値で見なければならない (Codex 指摘)。</summary>
        public const float FleeJitterRad = 0.6f;

        /// <summary>対面で「着いた」とみなす余裕 (m)。狙いの間合い contact に対する遊び。
        /// 大きくすると鼻の間合いがそのぶん広がるので小さく取る。
        /// 減速が v=√(2ad) で 0 に入るため、小さくてもガタつかない。</summary>
        const float FaceSettleSlack = 0.06f;

        /// <summary>追う役同士を扇に散らす角 (rad ≒ 40°)。IMP-NET-002 —
        /// 回り込み点は全追う役で同じ P から導くため、散らさないと同一点へ団子になる。
        /// 順位由来のオフセットなので全クライアントで同じ散り方になる。
        /// 大きくしすぎると「回り込み」を通り越して逃げ役の行く手を塞ぐ絵になる。</summary>
        const float ChaseFanRad = 0.7f;

        /// <summary>徘徊で走っているか (KD_PhotoDirector の追跡ドローンが撮影直前に確認する)。</summary>
        public bool IsRunning { get { return _wasMoving && _wasRunning; } }

        /// <summary>
        /// 今フレーム じゃれ合っているか (IMP-CAM-002 — 撮影の 4 つ目の瞬間)。
        ///
        /// **「離れる」(窓の最後の演技) は含めない。** 撮りたいのは追いかけっこと対面で、
        /// 離れていく背中は「じゃれ合っている絵」にならない。
        ///
        /// **撮影側で予定表を組み直させないための窓口。** 同じ判定を撮影側に複製すると
        /// 黙ってズレる (IMP-DOG-013 で計器を「判定した本人」に持たせたのと同じ理由)。
        /// 撮影は開始のエッジで予約し、**撮る直前にもう一度ここを読んで**
        /// 「まだ遊んでいるか」を確認する (プレイヤーが近づけば即やめるため)。
        /// </summary>
        public bool IsPlaying { get { return _playing; } }

        // ---- デバッグ HUD 用 (KD_DogDebugHud が読む。表示専用で挙動には効かない) ----
        public float DebugSpeed { get { return _speed; } }
        public bool DebugAnimMoving { get { return _wasMoving; } }
        public bool DebugPaused { get { return _paused; } }

        /// <summary>
        /// プレイヤーが近くに居るので立ち止まっているか (人懐こさ)。
        ///
        /// **じゃれ合いの相手がこれを読む** (IMP-DOG-009 / Codex 指摘)。
        /// 読まないと「A にプレイヤーが近づいて A は止まったのに、B は A を追い続ける」
        /// = **プレイヤーの居るところへ B が突っ込んでくる**という絵になり、
        /// 「プレイヤーが関わったら即やめる」がペアとして成立しない。
        ///
        /// 相手の判定をそのまま読むので、**ヒステリシス (pauseRadius / resumeRadius) まで
        /// 含めて相手と完全に一致する** — 自分で相手の周りを測り直すと、
        /// ヒステリシスの帯で 2 匹の結論が割れる。
        ///
        /// Fetch 中は false (立ち止まっているのではなく仕事中)。
        ///
        /// **IMP-FET-005 で意味を広げた: 「プレイヤーに関わっているので遊びに応じない」。**
        /// 催促中 (<see cref="_nudging"/>) も true を返す — 返さないと、相手は催促中の犬を
        /// 「遊べる相手」と判断して**プレイヤーの居るところへ突っ込んでくる**。
        /// これは §8 で Codex 指摘により塞いだ穴と同じ形なので、塞ぎ方も同じにした。
        ///
        /// **`_paused` の鮮度は別に保証している** (催促の枝より前で毎フレーム更新する) —
        /// 覆いは意味の拡張、更新は鮮度の保証で、別の話。
        ///
        /// なお `_nudging` は Pickup の伝播に依存するので、**一致はクライアント間で
        /// 数百 ms 遅れる**。じゃれ合いは「止まる場所」で収束するため、割れるのは
        /// 遷移の瞬間だけ — ここを厳密にするには同期変数が要り、増やさない方が価値が高い。
        ///
        /// **IMP-TRT-001 でさらに広げた: おやつ対応中 (<see cref="_treatBusy"/>) も true。**
        /// こちらは招待が同期変数 (eaterDogIndex) 由来なので、催促よりむしろ割れにくい。
        ///
        /// **破壊的監査 v2 RC-7 でさらに広げた: 休憩中 (<see cref="_restPhase"/>) も true。**
        /// おやつは映るのに休憩だけ漏れていた非対称。漏れていると、じゃれ合いの相手が
        /// 水を飲みに離脱した犬を「遊べる相手」と読み続け、**居ない犬の幻影レールを
        /// 最大 48 秒追う** (その絵を撮影が Play カテゴリで撮る)。
        /// 休憩はローカル導出なのでクライアント間で数フレーム割れうるが、
        /// じゃれ合いの中断は「相手が止まった」の観測で収束する — 催促と同じ扱い。
        ///
        /// **IMP-DOG-019 Phase 2 でさらに広げた: 寝るモード中 (<see cref="_sleepCalled"/>) も true。**
        /// 呼ばれて向かっている間も、寝ている間も「仕事中」— じゃれ合いの相手・撮影・
        /// おやつの選定が、**呼んだ人のところに居る犬を空いている犬と読まない**ようにする。
        /// 入力が枠の同期値なので、催促よりむしろ割れにくい (おやつと同じ性質)。
        /// </summary>
        public bool IsPausedForPlayer { get { return _paused || _nudging || _treatBusy || _sleepCalled || _liveCalled || _greetingActive || _restPhase != RestPhaseNone; } }
        /// <summary>おやつに関わっている最中か (接近・見つめ・食事・食後の返事)。IMP-FET-015 の弱い放しがこれに負ける。同期値からの導出。</summary>
        public bool IsTreatBusy { get { return _treatBusy; } } // net: callable-ok: read-only

        /// <summary>ライブの観客として集まっている / 伏せて見ている (IMP-PRG-004 Phase 2)。</summary>
        public bool IsLiveWatching { get { return _liveCalled; } }

        /// <summary>今フレーム催促しているか (デバッグ HUD / 将来の撮影用)。</summary>
        public bool IsNudging { get { return _nudging; } }

        // ---- そばで寝る (IMP-DOG-019 Phase 2)。**Phase 4 / 5 が読む窓口** ----

        /// <summary>**寝るモード中か** (枠が生きている = 呼ばれてから解除されるまで)。
        /// 向かっている途中も true。
        ///
        /// **おもちゃタブの「呼ぶ」の条件へ `∧ !IsSleepCalled` を足すのはここ**
        /// (Issue §6 危険 4 — 寝ている犬は `fetch.IsIdle` が真のままなので、
        /// 足さないと**押しても犬が来ないボタン**になる)。
        /// 同じ判定は <see cref="KD_SleepBoard.IsSleeping"/> からも引ける — 台帳を持っている
        /// 相手はそちらでよい。ここは**犬を持っている相手のための同じ答え**。</summary>
        // net: callable-ok: 読み取りのみ — 遠隔から呼ばれても状態が動かない
        public bool IsSleepCalled { get { return _sleepCalled; } }

        /// <summary>**もう寝ているか** (寝床へ着いて `Sleep` を立てている)。
        /// 向かっている途中は false。Phase 4 の「寝たまま撫でられる」はここを見る。</summary>
        // net: callable-ok: 読み取りのみ — 遠隔から呼ばれても状態が動かない
        public bool IsAsleep { get { return IsCompanionReady && !IsStretching && (CompanionPose == 0 || CompanionPose == 3 || CompanionPose == 4); } }
        public bool IsStretching { get { KD_SleepSlot s = sleepBoard == null ? null : sleepBoard.SlotOf(dogIndex); return IsCompanionReady && s != null && s.StretchActive; } }
        public bool IsCompanionReady { get { return _sleepCalled && _wantSleep; } }
        public int CompanionPose
        {
            get
            {
                KD_SleepSlot slot = sleepBoard == null ? null : sleepBoard.SlotOf(dogIndex);
                return slot == null ? 0 : slot.PoseId;
            }
        }

        /// <summary>今この犬を呼んでいる人の playerId (-1 = 誰も呼んでいない)。
        /// **`〇〇さんと寝ている` の名前はここから引く** (`VRCPlayerApi.GetPlayerById`)。</summary>
        // net: callable-ok: 読み取りのみ — 遠隔から呼ばれても状態が動かない
        public int SleeperPlayerId { get { return _sleepCalled ? _sleeperId : -1; } }

        // ---- デバッグ計器 (IMP-DOG-013) ----
        //
        // **「なぜ今じゃれ合っていないか」は外から観測できない。**
        // UpdatePlay は 7 箇所で return false するが、外から見るとどれも「立っている」に
        // 見えるだけで区別が付かない。HUD 側で同じ判定を組み直す手もあるが、
        // **複製は黙ってズレる**ので、判定した本人に記録させる。
        //
        // 既定 false でシップする。**KD_DogPlayDebugHud が Play 中に
        // SetProgramVariable で立てる**ので、シーンには false のまま置いておく
        // (公開ワールドで計器のコストを 1 命令も払わない)。
        [Tooltip("じゃれ合い / 徘徊の判定理由を記録する。**シーンでは false のまま**。" +
                 "Play 中に KD_DogPlayDebugHud が立てる (IMP-DOG-013)")]
        public bool debugInstrument = false;

        /// <summary>_dbgReason の値。**HUD が名前で表示するのでここが正本**
        /// (HUD 側に数字を書き写さない)。</summary>
        public const int DbgNone = 0;
        public const int DbgFetch = 1;         // 自分が Fetch 中 — じゃれ合い判定まで来ない
        public const int DbgPaused = 2;        // 自分の近くにプレイヤーが居る (人懐こさ)
        public const int DbgCalm = 3;          // Drop 明けの静穏 (postFetchCalm)
        public const int DbgNoPack = 4;        // 名簿が無い / 壊れている (1 匹版として動作)
        public const int DbgAlone = 5;         // 犬が 1 匹しか居ない
        public const int DbgWindowClosed = 6;  // じゃれ合いの窓が開いていない ← 既定はこれ
        public const int DbgNotMyTurn = 7;     // 窓は開いているが今回の組に自分が入っていない
        public const int DbgOtherGone = 8;     // 相手が非アクティブ
        public const int DbgOtherFetch = 9;    // 相手が Fetch 中
        public const int DbgOtherPaused = 10;  // 相手のところにプレイヤーが居る (Codex 指摘の塞ぎ)
        public const int DbgPlaying = 11;      // じゃれ合っている
        public const int DbgNotJoinedYet = 12; // 参加するが**まだ入る時刻でない** (IMP-DOG-012 の途中参加)
        public const int DbgNudge = 13;        // 自分のボールが持たれている = 催促中 (IMP-FET-005)
        public const int DbgTreat = 15;        // おやつに呼ばれている / 食べている (IMP-TRT-001)
        public const int DbgSleep = 16;        // そばで寝るに呼ばれている / 寝ている (IMP-DOG-019)
        public const int DbgLive = 17;         // ライブの観客として集まっている / 伏せて見ている (IMP-PRG-004)

        int _dbgReason;
        double _dbgServerTime;   // 犬が実際に使った時刻。**HUD の時計もこれを使う** (ズレようがない)
        int _dbgCycle = -1;      // じゃれ合いのサイクル番号 = 2 人で突き合わせるときの照合キー
        float _dbgPhase;         // サイクル内の経過 (秒)
        float _dbgWinStart;      // 窓の開始 (サイクル内の秒)
        float _dbgWinCore;       // 本体の長さ
        float _dbgWinTotal;      // 本体 + 離れる
        int _dbgPairA = -1;      // IMP-DOG-012: 今の区間の逃げ役 (旧: 組の片方)
        int _dbgPairB = -1;      // IMP-DOG-012: 自分の順位 (旧: 組のもう片方)
        int _dbgPartySize = -1;  // その窓の参加頭数。**盤の検算はこれを突き合わせる**

        /// <summary>参加抽選の値を 1 フレーム分ためる作業領域 (IMP-DOG-012)。
        /// **毎フレーム確保し直さない** — 順位の計算で n 回引くので、
        /// LCG を n^2 回呼ばないためにここへ置く。</summary>
        float[] _playRoll;
        // **組は窓が開いたときしか更新されない。_dbgCycle は毎フレーム更新される。**
        // 鮮度が違うものを 1 つの番号で照合すると、窓外の犬の古い組を
        // 現在のサイクルのものとして読んでしまう (2026-08-16 の Play で偽 MISMATCH を出した)。
        // **組の鮮度は組と一緒に記録する。**
        int _dbgPairCycle = -1;
        int _dbgType;            // 0=なし 1=追いかけっこ 2=対面 3=離れる
        int _dbgRole;            // 0=なし 1=追う 2=逃げる
        int _dbgWanderState;     // 0=窓外 1=目標へ移動中 2=早着で待機 (匂いを嗅ぐ間)
        float _dbgWanderPhase;
        float _dbgWanderStart;
        float _dbgWanderLen;
        int _dbgWaypoint = -1;

        void Start()
        {
            if (dogRoot == null || dogAnimator == null || fetchController == null || areaCenter == null)
            {
                Debug.LogError("[KD_DogWanderController] wiring incomplete. Run Tools/KominkaDogRun/Wander System setup.");
                enabled = false;
                return;
            }
            // 既存シーンの UdonBehaviour へ後から足したフィールドが 0 のまま入る事故を
            // 無言で通さない (理由は KD_DogFetchController.Start の同じ検査)
            if (acceleration <= 0.01f || deceleration <= 0.01f
                || stopSpeedThreshold <= 0f || runAnimSpeed <= 0f
                || runAnimScaleMax <= 0.01f
                || idleTurnSpeedDegPerSec <= 0.01f)
            {
                Debug.LogWarning("[KD_DogWanderController] locomotion tuning value is 0 - falling back to defaults.");
                if (acceleration <= 0.01f) acceleration = DefaultAcceleration;
                if (deceleration <= 0.01f) deceleration = DefaultDeceleration;
                if (stopSpeedThreshold <= 0f) stopSpeedThreshold = DefaultStopSpeedThreshold;
                if (runAnimSpeed <= 0f) runAnimSpeed = DefaultRunAnimSpeed;
                // 0 のままだと Mathf.Clamp の上限が下限を下回り、走りクリップが 1 倍に張り付く
                if (runAnimScaleMax <= 0.01f) runAnimScaleMax = DefaultRunAnimScaleMax;
                // 0 のままだと「相手の方を向く」が永久に完了せず、対面が成立しない
                if (idleTurnSpeedDegPerSec <= 0.01f) idleTurnSpeedDegPerSec = DefaultIdleTurnSpeedDegPerSec;
            }

            // **破壊的監査 RC-1: 徘徊そのものの値が、上の locomotion 検査に入っていなかった。**
            // とくに cyclePeriod / waypointPeriod は**割り算の分母**で、0 だと
            // `(int)(serverTime / 0)` から窓判定が永久に偽になり、**犬は無警告で
            // 二度とうろつかなくなる** (ログすら出ないので Play でも原因に辿り着けない)。
            // waypointPeriod 0 は hop が int.MinValue へ落ち、_arrivedHop の初期値と衝突して
            // 「そのサイクルは動かない」も踏む。
            // 脚 (runSpeed / walkSpeed / turnSpeedDegPerSec) と arriveRadius は Fetch 側と同じ理由。
            //
            // これが入ったので、下の名簿の写し (IMP-NET-002) の「本人側に 0 フォールバックが
            // 無い」という注記も解消される — 鏡写しが本人の値と必ず一致するようになった。
            if (cyclePeriod <= 0.01f || waypointPeriod <= 0.01f || postFetchCalm < 0f
                || areaRadius <= 0.01f || arriveRadius <= 0.001f
                || runSpeed <= 0.01f || walkSpeed <= 0.01f || turnSpeedDegPerSec <= 0.01f)
            {
                Debug.LogWarning("[KD_DogWanderController] wander tuning value is 0 - falling back to defaults.");
                if (cyclePeriod <= 0.01f) cyclePeriod = DefaultCyclePeriod;
                if (waypointPeriod <= 0.01f) waypointPeriod = DefaultWaypointPeriod;
                if (postFetchCalm < 0f) postFetchCalm = DefaultPostFetchCalm;
                if (areaRadius <= 0.01f) areaRadius = DefaultAreaRadius;
                if (arriveRadius <= 0.001f) arriveRadius = DefaultArriveRadius;
                if (runSpeed <= 0.01f) runSpeed = DefaultRunSpeed;
                if (walkSpeed <= 0.01f) walkSpeed = DefaultWalkSpeed;
                if (turnSpeedDegPerSec <= 0.01f) turnSpeedDegPerSec = DefaultTurnSpeedDegPerSec;
            }
            if (wanderDurationMin <= 0.01f) wanderDurationMin = DefaultWanderDurationMin;
            if (wanderDurationMax < wanderDurationMin) wanderDurationMax = wanderDurationMin;

            // IMP-DOG-012: **後から足したフィールドなので、既存シーンでは 0 で入る。**
            // playMaxDogs 0 は Clamp(0,2,n) で 2 になるだけだが、
            // **「多頭化したのに 2 匹のままで、しかも無言」**という一番気付きにくい壊れ方をする。
            // playJoinWindow は 0 でも成立する (全員が頭からそろう) ので既定へ落とさない
            if (playMaxDogs < 2)
            {
                Debug.LogWarning("[KD_DogWanderController] playMaxDogs < 2 - falling back to "
                    + DefaultPlayMaxDogs
                    + " (多頭のじゃれ合いが 2 匹のまま無言で縮む。Dog Pack Setup を流し直すこと)");
                playMaxDogs = DefaultPlayMaxDogs;
            }

            // IMP-DOG-009: じゃれ合いの調整値も 0 のまま入ると無言で壊れる。
            // playCyclePeriod 0 は 0 除算、playBreakOff 0 は「対面のまま固まって窓が終わる」、
            // playContactSpacing 0 は相手に重なりに行って押し出しと綱引きになる
            if (playCyclePeriod <= 0.01f || playDurationMax <= 0.01f || roleSwapPeriod <= 0.01f
                || fleeHopPeriod <= 0.01f || playContactSpacing <= 0.01f)
            {
                // かつてここは 12f / 20f / 2.4f の手書きで、IMP-DOG-012 が宣言側だけ
                // 18 / 28 / 3.0 へ動かしたあとも旧値のまま残っていた (IMP-INF-001 で発見)。
                // 正本を Default* に一本化したので、この形のずれは構造的に起きない
                Debug.LogWarning("[KD_DogWanderController] play tuning value is 0 - falling back to defaults.");
                if (playCyclePeriod <= 0.01f) playCyclePeriod = DefaultPlayCyclePeriod;
                if (playDurationMin <= 0.01f) playDurationMin = DefaultPlayDurationMin;
                if (playDurationMax <= 0.01f) playDurationMax = DefaultPlayDurationMax;
                if (roleSwapPeriod <= 0.01f) roleSwapPeriod = DefaultRoleSwapPeriod;
                if (fleeHopPeriod <= 0.01f) fleeHopPeriod = DefaultFleeHopPeriod;
                if (playContactSpacing <= 0.01f) playContactSpacing = DefaultPlayContactSpacing;
            }
            if (playDurationMax < playDurationMin) playDurationMax = playDurationMin;
            if (playBreakOff < 0f) playBreakOff = 0f;
            if (playFleeInnerRatio < 0f) playFleeInnerRatio = 0f;
            if (playFleeInnerRatio > MaxPlayFleeInnerRatio) playFleeInnerRatio = MaxPlayFleeInnerRatio;

            // IMP-DOG-009: 名簿。壊れていたら じゃれ合いだけ黙って切り、徘徊は 1 匹版として続ける
            // (KD_DogFetchController の扇と同じ方針 — 犬が返事を返さなくなるより軽い)
            if (pack != null)
            {
                _packRoots = pack.dogRoots;
                _packRadius = pack.dogRadius;
                _packFetch = pack.dogFetch;
                _packWander = pack.dogWander;
                bool ok = _packRoots != null && _packRadius != null
                    && _packFetch != null && _packWander != null
                    && _packRoots.Length == _packRadius.Length
                    && _packRoots.Length == _packFetch.Length
                    && _packRoots.Length == _packWander.Length
                    && dogIndex >= 0 && dogIndex < _packRoots.Length;
                // 全員分の半径を検査する (KD_DogFetchController.Start と同じ理由) —
                // 1 件でも 0 だと、その相手との間合いが 0 になって重なりに行く
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

                    // IMP-NET-002: 他犬の共有レール再現に要る静的設定を写す。
                    // **相手の Start に補正があるフィールドは同じ変換を鏡写しにする** —
                    // Start の実行順は不定だが、同じ変換なら「読んだのが補正前でも補正後でも
                    // 同じ値」になり、相手が実際に使う値と一致する
                    // (補正の無いフィールドの例外は下の各コメント)
                    int m = _packRoots.Length;
                    _packSalt = new int[m];
                    _packCyclePeriod = new float[m];
                    _packWanderMin = new float[m];
                    _packWanderMax = new float[m];
                    _packWaypointPeriod = new float[m];
                    _packAreaRadius = new float[m];
                    _packAreaCenter = new Transform[m];
                    _packFleeHop = new float[m];
                    _packFleeInner = new float[m];
                    for (int i = 0; i < m; i++)
                    {
                        KD_DogWanderController w = _packWander[i];
                        if (w == null)
                        {
                            // 名簿の欠損 (Codex 指摘)。この犬は PlayBlockReason が常に弾くので
                            // 遊び相手にはならないが、アンカー計算が添字で触る可能性はある。
                            // ここで例外を出すと **Start ごと止まり徘徊まで死ぬ** ので、
                            // 決定論な既定値で埋めて続行する (null かどうかはシーン静的 =
                            // 全クライアントで同じ埋め方になる)
                            _packSalt[i] = 0;
                            _packCyclePeriod[i] = DefaultCyclePeriod;
                            _packWanderMin[i] = DefaultWanderDurationMin;
                            _packWanderMax[i] = DefaultWanderDurationMax;
                            _packWaypointPeriod[i] = DefaultWaypointPeriod;
                            _packAreaRadius[i] = areaRadius;
                            _packAreaCenter[i] = areaCenter;
                            _packFleeHop[i] = DefaultFleeHopPeriod;
                            _packFleeInner[i] = DefaultPlayFleeInnerRatio;
                            continue;
                        }
                        _packSalt[i] = w.seedSalt;
                        // cyclePeriod / waypointPeriod は**本人側にも 0 フォールバックが入った**
                        // (破壊的監査 RC-1。それまでは 0 だと本人が 0 除算で無言停止する構成で、
                        // ここの既定値だけが保険だったため「鏡写しが本人と一致しない」注記が付いていた)。
                        // 同じ Default* を見る鏡写しになったので、その不一致は構造的に消えている。
                        // **三項が残っているのは Start の実行順が不定だから** — 相手の Start より
                        // 先にここを読んでも、同じ変換を通すので同じ値になる
                        _packCyclePeriod[i] = w.cyclePeriod > 0.01f ? w.cyclePeriod : DefaultCyclePeriod;
                        _packWanderMin[i] = w.wanderDurationMin;
                        _packWanderMax[i] = w.wanderDurationMax;
                        _packWaypointPeriod[i] = w.waypointPeriod > 0.01f ? w.waypointPeriod : DefaultWaypointPeriod;
                        _packAreaRadius[i] = w.areaRadius;
                        _packAreaCenter[i] = w.areaCenter != null ? w.areaCenter : areaCenter;
                        // fleeHopPeriod / playFleeInnerRatio は本人 Start の補正の鏡写し
                        // (同じ Default* を参照するので、正本が動いても鏡は割れない)
                        _packFleeHop[i] = w.fleeHopPeriod > 0.01f ? w.fleeHopPeriod : DefaultFleeHopPeriod;
                        float inner = w.playFleeInnerRatio;
                        if (inner < 0f) inner = 0f;
                        if (inner > MaxPlayFleeInnerRatio) inner = MaxPlayFleeInnerRatio;
                        _packFleeInner[i] = inner;
                    }
                }
                else
                {
                    Debug.LogWarning("[KD_DogWanderController] dog pack roster invalid (dogIndex="
                        + dogIndex + ") - じゃれ合いを無効化する。Run Tools/KominkaDogRun/Dog Pack setup.");
                    _packRoots = null;
                    _packRadius = null;
                    _packFetch = null;
                    _packWander = null;
                }
            }

            // IMP-FET-005: 自分のボールは Fetch から 1 度だけ取る
            // (Udon の Behaviour 跨ぎアクセスは高い — 名簿を Start でローカルへ写すのと同じ理由)。
            // **プレハブ内 → シーンの参照ではない**ので実行時解決の必要は無く、
            // Fetch が Inspector で持っているものをそのまま受け取るだけ
            _ball = fetchController.ballController;
            if (_ball == null)
                Debug.LogWarning("[KD_DogWanderController] ballController が取れない - 催促 (IMP-FET-005) は無効。");
            if (petController == null)
                Debug.LogWarning("[KD_DogWanderController] petController 未配線 - 催促の首と尻尾が出ない。Run Tools/KominkaDogRun/Wander System setup.");

            // 既存シーンへ後から足したフィールドは 0 で入る。
            // nudgeStandoff 0 は**犬が鼻先まで来てボールを振れなくなる**、
            // holderStableTime 0 は**移譲の伝播前に別人へ寄る**、
            // feint 系 0 は演技が一瞬で終わって「騙された」が読めない
            if (nudgeStandoff <= 0.01f || holderStableTime <= 0f
                || feintTrackDuration <= 0.01f || feintReturnDuration <= 0.01f
                || nudgeStepHeight <= 0.01f || nudgeWallRadius <= 0.001f)
            {
                Debug.LogWarning("[KD_DogWanderController] nudge/feint tuning value is 0 - falling back to defaults.");
                if (nudgeStandoff <= 0.01f) nudgeStandoff = DefaultNudgeStandoff;
                if (holderStableTime <= 0f) holderStableTime = DefaultHolderStableTime;
                if (feintTrackDuration <= 0.01f) feintTrackDuration = DefaultFeintTrackDuration;
                if (feintReturnDuration <= 0.01f) feintReturnDuration = DefaultFeintReturnDuration;
                if (nudgeStepHeight <= 0.01f) nudgeStepHeight = DefaultNudgeStepHeight;
                if (nudgeWallRadius <= 0.001f) nudgeWallRadius = DefaultNudgeWallRadius;
            }

            // IMP-TRT-001: 既存シーンへ後から足したフィールドは 0 で入る。
            // treatArriveRadius 0 は「永久に届かない」= 犬がおやつへ重なりに行き、
            // treatThanksDuration 0 は返事 (完了条件 3) が 1 フレームで消える
            if (treatArriveRadius <= 0.01f || treatThanksDuration <= 0.01f)
            {
                Debug.LogWarning("[KD_DogWanderController] treat tuning value is 0 - falling back to defaults.");
                if (treatArriveRadius <= 0.01f) treatArriveRadius = DefaultTreatArriveRadius;
                if (treatThanksDuration <= 0.01f) treatThanksDuration = DefaultTreatThanksDuration;
            }

            // IMP-DOG-019 Phase 2: **後から足したフィールドは既存シーンで 0 で入る。**
            //   sleepStopDistance 0 → 扇の半径が 0 = **4 匹が呼んだ人へ重なりに行く**
            //                         (さらに twoR が 0 になり必要角が全部 180° へ飽和する)
            //   sleepSpread 0      → 隣り合う犬の必要角が 0 = **全員が同じ方位に重なる**
            if (sleepStopDistance <= 0.01f || sleepSpread <= 0.01f)
            {
                Debug.LogWarning("[KD_DogWanderController] sleep tuning value is 0 - falling back to defaults.");
                if (sleepStopDistance <= 0.01f) sleepStopDistance = DefaultSleepStopDistance;
                if (sleepSpread <= 0.01f) sleepSpread = DefaultSleepSpread;
            }

            // IMP-DOG-015 / IMP-DOG-014: **後から足したフィールドは既存シーンで 0 で入る。**
            // ここが無いと 0 の意味がそのまま効いて、無言で壊れる:
            //   breathRunSpeed 0     → 止まっていても「走っている」= 永久にはぁはぁ言う
            //   breathTriggerRunTime 0 → 入室した瞬間から息が上がっている
            //   breathCalmDuration 0 → 一瞬で止まり、鳴っているのが分からない
            //   restTriggerRunTime 0 → **走っていなくても常に伏せる** (庭が止まる)
            //   restDurationMin/Max 0 → 伏せた瞬間に起きるのを延々と繰り返す
            //   restCooldown 0       → 休憩明けに即また伏せる (休憩がループする)
            //   breathRecoverScale 0 → 息の借金が永久に減らない (一度走ると休み続ける)
            //   ↑ この 2 本はフォールバック行だけあって**条件式に入っていなかった**
            //     (破壊的監査 v2 RC-18 = 前回 A-3 と同じ規則の残穴)
            //   breathIdleVolume 0     → IMP-DOG-017 が丸ごと無音 (無言で機能が消える)
            //   breathIdleFadeSeconds 0 → フェード速度がゼロ除算ガードの 0.01 に落ちて実質ブツ切り
            if (breathRunSpeed <= 0.01f || breathTriggerRunTime <= 0.01f
                || breathCalmDuration <= 0.01f || breathFadeSeconds <= 0.001f
                || breathVolume <= 0.001f || breathRecoverScale <= 0f
                || breathIdleVolume <= 0.001f || breathIdleFadeSeconds <= 0.001f
                || restTriggerRunTime <= 0.01f || restDurationMin <= 0.01f
                || restDurationMax <= 0.01f || restSettleTime <= 0.001f
                || restCooldown <= 0.01f)
            {
                Debug.LogWarning("[KD_DogWanderController] breath/rest tuning value is 0 - falling back to defaults.");
                if (breathRunSpeed <= 0.01f) breathRunSpeed = DefaultBreathRunSpeed;
                if (breathTriggerRunTime <= 0.01f) breathTriggerRunTime = DefaultBreathTriggerRunTime;
                if (breathCalmDuration <= 0.01f) breathCalmDuration = DefaultBreathCalmDuration;
                if (breathFadeSeconds <= 0.001f) breathFadeSeconds = DefaultBreathFadeSeconds;
                if (breathVolume <= 0.001f) breathVolume = DefaultBreathVolume;
                if (breathRecoverScale <= 0f) breathRecoverScale = DefaultBreathRecoverScale;
                if (breathIdleVolume <= 0.001f) breathIdleVolume = DefaultBreathIdleVolume;
                if (breathIdleFadeSeconds <= 0.001f) breathIdleFadeSeconds = DefaultBreathIdleFadeSeconds;
                if (restTriggerRunTime <= 0.01f) restTriggerRunTime = DefaultRestTriggerRunTime;
                if (restDurationMin <= 0.01f) restDurationMin = DefaultRestDurationMin;
                if (restDurationMax <= 0.01f) restDurationMax = DefaultRestDurationMax;
                if (restSettleTime <= 0.001f) restSettleTime = DefaultRestSettleTime;
                if (restCooldown <= 0.01f) restCooldown = DefaultRestCooldown;
            }

            // IMP-DOG-019 Phase 4: 寝息も**後から足したフィールドなので既存シーンでは 0**。
            //   breathSleepVolume 0 → **寝ている返事が丸ごと無音** (Issue §4.1 の完成条件が
            //                         「機械検査は全 PASS のまま」消える。IMP-DOG-017 と同じ型)
            //   breathSleepPitch 0  → AudioSource が進まない = 鳴っているのに聞こえない
            if (breathSleepVolume <= 0.001f || breathSleepPitch <= 0.01f)
            {
                Debug.LogWarning("[KD_DogWanderController] sleep breath tuning value is 0 - falling back to defaults.");
                if (breathSleepVolume <= 0.001f) breathSleepVolume = DefaultBreathSleepVolume;
                if (breathSleepPitch <= 0.01f) breathSleepPitch = DefaultBreathSleepPitch;
            }
            if (breathSleepVolume > breathIdleVolume)
            {
                // 大小関係が要る対 (releaseRadius <= touchRadius と同じ扱い)。
                // 寝息が近接呼吸より大きいと「寝たほうが元気」に聞こえる
                Debug.LogWarning("[KD_DogWanderController] breathSleepVolume > breathIdleVolume - "
                    + "寝息のほうが大きいと嘘になる。breathIdleVolume へ切り詰める");
                breathSleepVolume = breathIdleVolume;
            }
            //   drinkDuration 0 → 着いた瞬間に伏せる = 飲む絵が 1 フレームで消える
            //   waterApproachTimeout 0 → 一歩も歩かずに諦める (水場へ永久に行かない)
            //   waterMaxDistance 0 → 水場が常に「遠すぎる」= Phase 2 が丸ごと死ぬ
            if (drinkStandoff <= 0.001f || drinkDuration <= 0.01f || drinkGazeRise <= 0.001f
                || drinkArriveRadius <= 0.001f || restMoveDistance <= 0.01f
                || waterApproachTimeout <= 0.01f || waterMaxDistance <= 0.01f)
            {
                Debug.LogWarning("[KD_DogWanderController] water tuning value is 0 - falling back to defaults.");
                if (drinkStandoff <= 0.001f) drinkStandoff = DefaultDrinkStandoff;
                if (drinkDuration <= 0.01f) drinkDuration = DefaultDrinkDuration;
                if (drinkGazeRise <= 0.001f) drinkGazeRise = DefaultDrinkGazeRise;
                if (drinkArriveRadius <= 0.001f) drinkArriveRadius = DefaultDrinkArriveRadius;
                if (restMoveDistance <= 0.01f) restMoveDistance = DefaultRestMoveDistance;
                if (waterApproachTimeout <= 0.01f) waterApproachTimeout = DefaultWaterApproachTimeout;
                if (waterMaxDistance <= 0.01f) waterMaxDistance = DefaultWaterMaxDistance;
            }
            // 大小が逆だと Lerp が縮んで「幅を持たせる」意図が消える (4 匹が揃って見える)
            if (restDurationMax < restDurationMin) restDurationMax = restDurationMin;
            // **息が上がるより先に伏せると順序が逆に見える** ので、必ず呼吸を先にする
            if (restTriggerRunTime <= breathTriggerRunTime)
                restTriggerRunTime = breathTriggerRunTime * 1.6f;

            // 参加直後を「Drop 明け」扱いにしない (Time.time はクライアントごとに基準が
            // 違うため、0 のままだと初回の静穏時間が人によって変わる — Codex 指摘)
            _lastBusyTime = Time.time - postFetchCalm;
        }

        // IMP-DOG-034: Update() itself is in KD_DogWanderController.Tick.cs (the fps-adaptive skip of an idle dog);
        // this is the old Update body, run on every executed frame
        void UpdateTick()
        {
            _greetingActive = false;
            _groupReady = false;
            _groupCalled = false;
            _groupSit = false;
            // LookAt は「今フレームそう決めた」ときだけ立つ。既定は下ろす —
            // 早期 return するどの経路を通っても対面ポーズが残らない (出しっぱなし防止)
            _look = false;
            // じゃれ合いも同じ形で毎フレーム決め直す (IMP-CAM-002)。
            // **下ろしてから立てる**ので、どの早期 return を通っても
            // 「遊んでいることになったまま」が残らない = 撮影が古い状態で撮りに来ない
            _playingPrev = _playing; // 前フレームの結論 (休憩の入場ゲートが読む — 破壊的監査 v2 RC-7)
            _playing = false;
            // IMP-FET-005: 催促も同じ作法で毎フレーム決め直す。
            // **注視と尻尾はここで下ろさなくてよい** — 差し込まなければ撫で層側で
            // そのフレームのうちに失効する (設計書 §10 決定 3 の契約 3)
            _nudging = false;
            // IMP-TRT-001: おやつも同じ作法 (毎フレーム下ろしてから立てる)
            _treatBusy = false;
            _eatPose = false;
            // IMP-DOG-014: 伏せも同じ作法。**どの早期 return を通っても伏せが残らない**
            // (Animator への書き込みは LateUpdate が 1 箇所で面倒を見る)
            _wantLieDown = false;
            // IMP-DOG-010: 座るも同じ作法 (立ち寄りの枝だけが立てる。伏せとは同時に立たない)
            _wantSit = false;
            // IMP-DOG-019: そばで寝るも同じ作法。**寝る枝だけが立てる**
            // (伏せ (LieDown) と同時に立てない — Bool は一度に 1 つの規約)
            _wantSleep = false;
            _sleepCalled = false;
            // IMP-PRG-004: ライブの観客も同じ作法 (観客の枝だけが立てる)
            _liveCalled = false;
            _activityBusy = false; // かけっこ・じゃれも同じ作法 (UpdateActivity の拘束する枝だけが立てる)
            _washActive = false;   // IMP-DOG-025: 洗われ中も同じ作法 (UpdateHillsideWash だけが立てる。HillsideWash.cs)
            _selfAvailableValid = false; _wetMemoValid = false;   // IMP-DOG-034 A5: tick 内の使い回しはこの tick の分だけ (Performance.cs / HillsideWater.cs)
            _heldStationMemoFrame = -1; _travelMemoFrame = -1;    // IMP-DOG-034 C1-1a / C1-1d: 同上 (HillsideWash.cs / HillsideTravel.cs)
            WashErrandTick();     // IMP-DOG-027: 集合・呼び戻し・ワープの受理のエッジで洗浄の保持を解く (wash が null なら即 return)

            // ---- IMP-NET-005: Owner 権威。**ルートを書くのは Owner だけ** ----
            //
            // **判断・レール・NotifyPlay は全員が回したままにする。** 止めてしまうと
            // 撮影の通知エッジ (`_OnDogsPlay` / `_OnDogChase`) が非 Owner 側で死に、
            // その人の画面ではカメラが二度と遊びを撮らなくなる。
            // 止めるのは Transform の書き込みと加減速だけで、非 Owner の `_speed` は
            // **`VRCObjectSync` が配ってくる実位置の差分**で埋める (下記)。
            bool wasOwner = _dogOwner;
            _dogOwner = IsDogOwner();
            if (_dogOwner != wasOwner) _behaviorSelectionValid = false;
            if (!_dogOwner) ObserveSpeed();
            WaterTick(); // IMP-DOG-020: 水遊びの姿勢・しぶき・リード解除の観測 (HillsideWater.cs)。早期 return より前
            // RC-14: a leash first seen while the dog is in the water is not taken up (WaterLeashTick, HillsideWater.cs)
            if (!_waterLeashVeto && UpdateHillsideLeash()) { _behaviorSelectionValid = false; NotifyPlay(); return; }

            _candyTicked = UpdateCandyPerformance();   // IMP-DOG-034: remembered for TickBusy (Tick.cs)
            if (_candyTicked) { _behaviorSelectionValid = false; NotifyPlay(); return; }

            // Complete any live landing above even if Fetch disappeared. Once grounded,
            // wait without halting the Udon VM so repairing the reference can resume AI.
            if (fetchController == null) { _behaviorSelectionValid = false; Coast(); NotifyPlay(); return; }

            // ---- 割り込み 1: Fetch 中は何もしない (dogState は同期済み — 全員一致) ----
            // IMP-DOG-005: フリスビー中 (Ready を含む。root と Animator を KH_FrisbeeCatch が書く) も同じ扱い
            if (!fetchController.IsIdle || FrisbeeActive)
            {
                _behaviorSelectionValid = false;
                _lastBusyTime = Time.time;
                // 投げが成立したらフェイントの演技は捨てる (本番が始まった)
                _holderId = -1;
                // おやつの演技も捨てる (食べかけでも本番の仕事が勝つ。
                // Animator の Pickup は Fetch が非 Idle の間ずっと自分で書くので触らない)。
                // **返事 (_thanksUntil) も意図して捨てる** (Codex 指摘でここに明記) —
                // Fetch のあとに返事を残すと「ボールを届けた相手」と「おやつの返事の相手」が
                // 別人になり得て、誰への返事か読めなくなる (不変条件 4)
                _treatEating = false;
                _thanksUntil = -1f;
                _wasEatPose = false;
                // **休憩も畳む** (破壊的監査 v2 RC-7)。畳まないと Fetch の間じゅう
                // 休憩 FSM が凍結され、Drop 直後に古いタイマーのまま再開して
                // postFetchCalm を素通りで即伏せる。EndRest 経由なので
                // クールダウンと借金の清算はどの出口とも同じ
                if (_restPhase != RestPhaseNone) EndRest();
                // **寝床の凍結も解く** (IMP-DOG-019 Phase 2)。Fetch はボールを追って犬を
                // どこへでも連れて行くので、凍結を残すと **戻らずにその場で寝る**
                // (凍結は「もうスロットを取り直さない」の意味なので、動かされたら無効)。
                // 枠 (sleepBoard) は生きたままなので、Idle に戻れば並び直して寝に入る。
                // **寝ている犬が投擲に反応しないこと自体は Phase 5** — ここはその前の保険
                _sleepFrozen = false;
                // 観客の凍結も同じ理由で解く (IMP-PRG-004 Phase 2)。Fetch 明けに並び直す
                _liveFrozen = false;
                // Animator は Fetch 側が毎フレーム書いている。こちらの書き込み状態だけ畳む。
                // ただし LookAt は Fetch が触らない Bool なので、ここで自分で下ろす
                // IMP-DOG-020: Fetch の水中の状態 (6〜8) は、足を _WaterFetchStep (HillsideWater.cs) が動かしている。
                // ここで毎フレーム _speed を 0 にすると、1 フレーム分の加速しか残らず這うように進む (run 20260927-095400)
                // DES-DOG-005 D-4 (M3): the disc's water states (KH_FrisbeeCatch 11-13) borrow the same legs, on every client
                if (!fetchController.IsWaterFetching && !FrisbeeWaterBusy)
                {
                    _wasMoving = false;
                    _wasRunning = false;
                    _speed = 0f;
                }
                // 再生速度もここで返す (IMP-DOG-009 / Codex D1)。**リードの引き渡しだけでは足りない** —
                // 速い散歩の途中でリードを外し、次の更新より前に Fetch が始まると、
                // Fetch は `Animator.speed` を一度も書かないので倍率が上がったまま残る。
                // 毎フレーム通るが、実物が自分の書いた値でなければ何も書かない
                ReleaseAnimatorSpeed();
                // 仕事中は「立ち止まっている」ではない。じゃれ合いの相手が IsPausedForPlayer を
                // 読むので、古い true を残さない
                _paused = false;
                if (_wasLooking)
                {
                    dogAnimator.SetBool("LookAt", false);
                    _wasLooking = false;
                }
                if (debugInstrument) _dbgReason = DbgFetch;
                NotifyPlay();
                return;
            }

            // ---- 近接の観測 (割り込み 2 の材料)。**催促より前に必ず通す** ----
            // 全員分の距離計算は 0.25s おきに間引く (Udon VM で毎フレームは重い — Codex 指摘)。
            //
            // IMP-FET-005: 催促の枝は下で return するので、**判定と `_paused` の更新を
            // 枝より前に済ませておく**。ここを飛び越すと `_paused` が催促の間ずっと
            // 古いままになり、じゃれ合いの相手が古い値で判断する
            // (コード上の警告「古い値を残すと相手の判断が狂う」がそのまま当たる)
            if (Time.time >= _nextProximityCheck)
            {
                _nextProximityCheck = Time.time + ProximityCheckInterval;
                _nearestSqr = NearestPlayerSqrDistance();
            }
            bool wasPaused = _paused;
            _paused = _paused ? _nearestSqr < resumeRadius * resumeRadius
                              : _nearestSqr < pauseRadius * pauseRadius;
            if (_paused != wasPaused) _behaviorSelectionValid = false;

            // IMP-DOG-025: 洗われ中はその場で止まって洗う人を見る (HillsideWash.cs)。選択済みの行動の継続より前
            // (後ろだと 0.1 秒の継続に遮られる。設計書 §1-5)。wash が null・洗われていない時は何も触らず false
            if (UpdateHillsideWash()) { NotifyPlay(); return; }

            // Select priority at 10 Hz; the selected branch still drives motion and
            // frame-scoped gaze/tail injections every frame. Ended branches reselect now.
            if (_behaviorSelectionValid && Time.time < _nextBehaviorSelection && ContinueSelectedBehavior())
            {
                NotifyPlay();
                return;
            }
            _selfAvailableValid = false;   // IMP-DOG-034 A5: 継続側が引いた値は選び直しへ持ち込まない (Performance.cs)
            ScheduleBehaviorSelection(Time.time);

            // IMP-DOG-020: 水遊び (HillsideWater.cs)。水の中では人の用事 (呼ぶ・ライブ・集合・おやつ) より先に出水し、
            // 用事は上陸してから受ける (設計 B-1「受理は上陸後」)。陸では何もせず下へ流す
            if (UpdateWaterPlay()) { _selectedBehavior = BehaviorWater; NotifyPlay(); return; }

            // ---- 割り込み 1.1: そばで寝る (IMP-DOG-019 Phase 2) ----
            //
            // **人の用事の中でいちばん強い。** 呼んだ人の横へ行って寝る間は、
            // 停止アンカーの補正 (1.2) ・おやつ (1.4) ・催促 (1.5) ・休憩 (1.8) ・
            // 人懐こさ (2) ・じゃれ合い・徘徊のどれも起こさない
            // (Fetch (1) にだけ負ける — 仕事中の犬は先に仕事を終える。
            //  **寝ている犬が投擲に反応しないこと自体は Phase 5** が Fetch 側へ入れる)。
            //
            // **近接の観測 (_paused) はこの上で毎フレーム済ませてある** —
            // 寝ている間もアイドル呼吸 (IMP-DOG-017) が鳴り、Phase 4 の寝息がそこへ乗る
            if (UpdateSleep())
            {
                _selectedBehavior = BehaviorSleep;
                if (debugInstrument) _dbgReason = DbgSleep;
                NotifyPlay(); // 早期 return では必ず呼ぶ (じゃれ合いの撮影エッジを殺さない)
                return;
            }

            // ---- 割り込み 1.15: ライブの観客 (IMP-PRG-004 Phase 2) ----
            //
            // **そばで寝る (1.1) にだけ負ける** — 寝ている犬はそのまま (2026-09-06 ユーザー決定)。
            // 撫でられている犬は UpdateLive の中で除外する (撫でが終わった瞬間に呼ばれる)。
            // おやつ (1.4) より前なのは「集まって見ている」を人の用事で崩さないため
            // (投擲は Fetch 側 (LiveWatching) が Live 中は受け付けない)
            if (UpdateLive())
            {
                _selectedBehavior = BehaviorLive;
                if (debugInstrument) _dbgReason = DbgLive;
                NotifyPlay(); // 早期 return では必ず呼ぶ (じゃれ合いの撮影エッジを殺さない)
                return;
            }

            if (UpdateActivity()) { _selectedBehavior = BehaviorActivity; NotifyPlay(); return; }

            // ---- 割り込み 1.4: おやつ (IMP-TRT-001 — 設計書 §11) ----
            // **催促より前** (食べ物は遊びの誘いに勝つ。選定が仕事中の犬を避けるので、
            // 重なるのは「招待の後で自分のボールが持たれた」ときだけ — 食べてから催促へ戻る)。
            // **人懐こさより前** に置く理由は催促と同じ (pauseRadius で止められて寄れない)
            if (UpdateTreat())
            {
                _selectedBehavior = BehaviorTreat;
                if (debugInstrument) _dbgReason = DbgTreat;
                NotifyPlay(); // 早期 return では必ず呼ぶ (§10 セルフレビュー穴 1)
                return;
            }

            // ---- 割り込み 1.5: 催促 (IMP-FET-005 — 人懐こさの強い版) ----
            // **人懐こさより前に置くことが「寄ってくる」の実装そのもの。**
            // 後ろだと pauseRadius (1.5m) で止められて近寄れない
            if (UpdateNudge())
            {
                _selectedBehavior = BehaviorNudge;
                if (debugInstrument) _dbgReason = DbgNudge;
                NotifyPlay(); // 早期 return では必ず呼ぶ (呼ばないと じゃれ合いの撮影エッジが死ぬ)
                return;
            }


            // Continue an accepted welcome before proximity/rest can stop it short of the person.
            if (UpdateGreeting(false)) { _selectedBehavior = BehaviorGreeting; NotifyPlay(); return; }

            // ---- 割り込み 1.8: 休憩 (IMP-DOG-014 Phase 1) ----
            // **人の用事 (Fetch / おやつ / 催促) には全部負ける** ので、ここに置く。
            // 接近 (_paused) に勝つか負けるかは `restWakesOnApproach` で切り替えられる —
            // 「近寄ったら立つ (置物に見えない)」と「伏せたまま撫でられる (絵が良い)」の
            // どちらが良いかは Play で決める (IMP-DOG-014 未決定 1)
            if (UpdateRest())
            {
                _selectedBehavior = BehaviorRest;
                if (debugInstrument) _dbgReason = DbgCalm;
                NotifyPlay(); // 早期 return では必ず呼ぶ (じゃれ合いの撮影エッジを殺さない)
                return;
            }

            // ---- 割り込み 2: 誰かが近くに居たら立ち止まる (人懐こさ。同期済み位置から導出) ----
            //
            // IMP-DOG-009: **この判定を じゃれ合いより前に置くことが「即やめる」の実装。**
            // じゃれ合い側に中断処理を書かないので、遊びに状態が増えても中断は壊れない。
            // **静穏 (postFetchCalm) より前へ置く**のは、静穏中も _paused を最新に保つため —
            // じゃれ合いの相手がこの値を読むので、古い値を残すと相手の判断が狂う
            if (_paused)
            {
                // A nearby player can be greeted, but an existing play session is never cut short for it.
                if (!_playingPrev && Time.time - _lastBusyTime >= postFetchCalm && UpdateGreeting(true)) { _selectedBehavior = BehaviorGreeting; NotifyPlay(); return; }
                _selectedBehavior = BehaviorPaused;
                Coast();
                if (debugInstrument) _dbgReason = DbgPaused;
                NotifyPlay();
                return;
            }

            // Drop 明けはしばらくおとなしい (完了条件 2)。観測基準なのでクライアント間で
            // 数百 ms ずれるが、レール (目標点) は共通なので位置は発散しない。
            // **接近判定の後ろでよい** — どちらも Coast() で止まるだけなので順序で挙動は変わらない
            if (Time.time - _lastBusyTime < postFetchCalm)
            {
                _selectedBehavior = BehaviorCalm;
                Coast();
                if (debugInstrument) _dbgReason = DbgCalm;
                NotifyPlay();
                return;
            }

            // サーバー時刻はラップで負になり得るため、割る側で floor 相当に補正する
            // (int キャストの 0 方向切り捨てのままだと負領域で phase < 0 になり
            // 徘徊が永久に止まる — Codex 指摘)。じゃれ合いも同じ補正を使う
            double serverTime = Networking.GetServerTimeInSeconds();
            // HUD の時計もこれを読む。**HUD 側で時刻を取り直さない** —
            // 取り直すと「HUD が指す瞬間」と「犬が判断した瞬間」が別物になり、
            // 予定表の突き合わせが 1 フレームぶんズレて嘘の不一致が出る
            if (debugInstrument) _dbgServerTime = serverTime;

            // ---- じゃれ合い (IMP-DOG-009)。徘徊より優先し、プレイヤー系の割り込みには必ず負ける ----
            bool played = UpdatePlay(serverTime);
            // **UpdatePlay が false を返す経路は 8 本ある** (名簿無し / 窓外 / 今回は不参加 /
            // まだ入る時刻でない / 相手が使えない …)。どれを通っても _playing は
            // 先頭で false に落ちているので、ここ 1 箇所で降りたことを拾える
            NotifyPlay();
            if (played) { _selectedBehavior = BehaviorPlay; return; }

            if (UpdateGreeting(true)) { _selectedBehavior = BehaviorGreeting; return; }

            _selectedBehavior = BehaviorWander;
            UpdateWander(serverTime);
        }

        void UpdateWander(double serverTime)
        {

            // ---- スケジュール: サーバー時刻 → サイクル → 徘徊窓 (全クライアント同一) ----
            int cycleIdx = (int)(serverTime / cyclePeriod);
            float phase = (float)(serverTime - (double)cycleIdx * cyclePeriod);
            if (phase < 0f)
            {
                cycleIdx -= 1;
                phase += cyclePeriod;
            }

            EnsureWanderSchedule(cycleIdx);
            float wanderLen = _wanderWindowLength;
            float windowStart = _wanderWindowStart;
            if (debugInstrument)
            {
                _dbgWanderPhase = phase;
                _dbgWanderStart = windowStart;
                _dbgWanderLen = wanderLen;
            }
            if (phase < windowStart || phase >= windowStart + wanderLen)
            {
                Coast(); // 窓の外 = Rest (待機ローテは Controller の Idle が回している)

                // IMP-DOG-016: **突っ立って待たない。** 足が止まったら伏せる。
                // 「休み」を立ち尽くしで表していたのが「アイドルしてる時間が多すぎる」の正体で、
                // 実測では休 82〜100% の犬が居た (2026-08-23 ユーザー)。
                //
                // **ここだけを伏せにする。** 目標点への早着 (匂いを嗅ぐ間) は
                // IMP-DOG-011 が意図して作った間なので触らない。人が近いとき (_paused) も
                // 立ち止まって見るのが人懐こさそのものなので触らない
                if (idleLieDown)
                {
                    if (_speed > stopSpeedThreshold) _idleLieSince = -1f;
                    else if (_idleLieSince < 0f) _idleLieSince = Time.time;
                    // 足が止まってから伏せる (BUG-DOG-001。休憩と同じ間を使う)
                    if (_idleLieSince >= 0f && Time.time - _idleLieSince >= restSettleTime)
                        _wantLieDown = true;
                }
                if (debugInstrument)
                {
                    _dbgWanderState = 0;
                    _dbgWaypoint = -1;
                }
                return;
            }

            // ---- 目標点: 窓内の経過時間から番号を割る (時刻のレール — 状態を持たない) ----
            int waypointIdx = (int)((phase - windowStart) / waypointPeriod);
            int hop = cycleIdx * WaypointHopStride + waypointIdx;
            if (debugInstrument) _dbgWaypoint = waypointIdx;

            // IMP-DOG-008: 一度その目標点へ着いたら、押し出されても歩いて戻らない。
            // 戻ると「犬同士が押し合いながら同じ点を取り合う」振動になる。
            // 到着点がクライアント間の一致点である性質は保たれる (着いた場所で止まるのは同じ)
            if (_arrivedHop == hop)
            {
                Coast();
                // **IMP-DOG-011 はこの状態の割合を測るのが本題** —
                // 「窓は開いているのに立っている」= 匂いを嗅ぐ間。計算上 63%
                if (debugInstrument) _dbgWanderState = 2;
                return;
            }

            Vector3 target = WanderTarget(hop);

            Vector3 to = target - dogRoot.position;
            to.y = 0f;
            if (to.magnitude <= arriveRadius)
            {
                _arrivedHop = hop;
                Coast(); // 早着。次の目標点番号まで立ち止まる (匂いを嗅ぐ間)
                if (debugInstrument) _dbgWanderState = 2;
                return;
            }

            if (debugInstrument) _dbgWanderState = 1;
            StepToward(target, arriveRadius, to.magnitude > runDistance ? runSpeed : walkSpeed);
        }

        /// <summary>止まる (減速し切るまでは惰性で進む)。目標点への到着は上の brakeSpeed で
        /// 既に速度 0 になっているため、ここで実際に滑るのは割り込み (接近・窓外) のときだけ。</summary>
        void Coast()
        {
            if (LegRecording()) _legKind = LegCoast;   // IMP-DOG-036 S1/S2: replayed by RunLegs until the next decision (Tick.cs)
            // IMP-NET-005: 非 Owner は加減速も位置も書かない
            // (_speed は Update 冒頭の ObserveSpeed が観測値で埋めている)
            if (_dogOwner && _speed > 0f)
            {
                float dt = MoveDt();   // IMP-DOG-036: one frame - the legs move every frame (Tick.cs)
                _speed = Mathf.MoveTowards(_speed, 0f, deceleration * dt);
                // ここは目標速度が常に 0 なので無条件に畳んでよい (StepToward と同じ不変条件)
                if (_speed <= stopSpeedThreshold) _speed = 0f;
                if (_speed > 0f)
                {
                    Vector3 coastNext = dogRoot.position + _moveDir * (_speed * dt);
                    // IMP-DOG-020: 惰性でも川の帯へは入らない (HillsideWater.cs)
                    if (_BlockedByRiver(dogRoot.position, coastNext)) _speed = 0f;
                    else dogRoot.position = SnapToGround(coastNext); // net: owner-only: _dogOwner (惰性)
                }
            }
            ApplyMoveAnim();
        }

        /// <summary>目標へ 1 フレーム分進む (旋回 → 加減速 → 接地スナップ → アニメ)。
        /// 徘徊もじゃれ合いも同じ足を使う — 加減速の詰め方 (IMP-FET-004) を 1 箇所に保つため。
        ///
        /// <paramref name="stopDist"/> は目標のどれだけ手前で止まるか。
        /// 到着点でちょうど 0 になる速度 (v = √(2ad)) で頭打ちにしてから加減速するので、
        /// **止まる過程が必ずある** (これが無いと脚のアニメだけ先に止まって滑って見える)。</summary>
        void StepToward(Vector3 target, float stopDist, float desired)
        {
            // IMP-DOG-036 S1/S2: record the step for RunLegs (not when called from inside another mover: GatherStep / LeashGo)
            // IMP-DOG-036 S1/S2 fix 1: a step turns the dog itself, so a face recorded earlier in this decision was superseded by it
            if (LegRecording()) { _legKind = LegStep; _legTarget = target; _legStop = stopDist; _legSpeed = desired; _legNav = _navStep; _legFaceKind = LegNone; }
            _legDepth++;
            StepTowardBody(target, stopDist, desired);
            _legDepth--;
        }

        void StepTowardBody(Vector3 target, float stopDist, float desired)
        {
            Vector3 pos = dogRoot.position;
            Vector3 to = target - pos;
            to.y = 0f;
            float dist = to.magnitude;
            float remain = dist - stopDist;
            if (dist <= 0.0001f || remain <= 0f)
            {
                Coast(); // 既に間合いの内側。惰性ぶんだけ進んで止まる
                return;
            }

            // net: owner-only: IMP-NET-005。**ここから下は「足」であって「判断」ではない。**
            // 非 Owner は加減速も旋回も前進も書かず、観測した _speed のまま脚だけ選ぶ。
            // 目標の算出 (呼び出し元) は全員が回したままなので、じゃれ合い・徘徊の
            // 撮影通知エッジは非 Owner 側でも今までどおり出る
            if (!_dogOwner)
            {
                ApplyMoveAnim();
                return;
            }

            // IMP-DOG-020: 直線移動は川の帯の手前で止まる (HillsideWater.cs。NavMesh の移動は対象外)。
            // 加速より前に見るので、止まっている間は _speed が 0 のまま (足踏みに見えない)
            float dt = MoveDt();   // IMP-DOG-036: one frame - the legs move every frame (Tick.cs)
            if (_BlockedByRiver(pos, pos + to / dist * Mathf.Max(_speed * dt, RiverProbe)))
            {
                _speed = 0f;
                ApplyMoveAnim();
                return;
            }

            float brakeSpeed = Mathf.Sqrt(2f * deceleration * remain);
            if (desired > brakeSpeed) desired = brakeSpeed;
            float rate = (desired > _speed ? acceleration : deceleration) * dt;
            _speed = Mathf.MoveTowards(_speed, desired, rate);

            // **デッドバンド (2026-08-14 Play 計測)。滑りの本体はここだった。**
            // brakeSpeed = √(2ad) は目標のごく手前 (remain がミクロン単位) で 0.03〜0.04 に
            // 落ち着く。_speed > 0 である限り位置は書かれ続けるので、Animator へ「止まった」と
            // 書いたあとも数 cm/s で這い続け、静止ポーズのまま滑って見えていた。
            // しかも √ の中に a が居るため、**減速度を上げるとこの速度はむしろ上がる**
            // (12 → 24 → 36 が効かなかったのはこれが理由)。
            //
            // 不変条件: Animator へ「止まった」と書く速度域では、位置も書かない。
            // desired も閾値以下のときだけ畳むのは、発進を殺さないため — 高フレームレートでは
            // acceleration * deltaTime が閾値を下回るので、無条件に畳むと二度と動き出せない
            if (_speed <= stopSpeedThreshold && desired <= stopSpeedThreshold) _speed = 0f;

            _moveDir = to / dist;

            // **旋回も止める (2026-08-14 Play 計測 2 巡目)。**
            // デッドバンドで這いは止まったが、旋回がここで速度と無関係に走っていたため
            // 「移動 0 なのに 20〜57 deg/s で回る」= 静止ポーズのまま足が地面を擦る、が残った。
            // じゃれ合いで相手に寄ると距離が小さくなり、相手のわずかな移動で方位が大きく振れる
            // ので特に目立つ。位置と同じ不変条件を向きにも適用する:
            // **Animator へ「止まった」と書く速度域では、位置も向きも書かない。**
            // (KD_DogFetchController が Pickup/Drop 中の旋回を禁止しているのと同じ処置)
            if (_speed > 0f)
            {
                Quaternion look = Quaternion.LookRotation(_moveDir, Vector3.up);
                dogRoot.rotation = Quaternion.RotateTowards(dogRoot.rotation, look, // net: owner-only: 上の早期 return が非 Owner を弾く
                    turnSpeedDegPerSec * dt);
            }

            if (_speed > 0f)
            {
                float step = _speed * dt;
                if (step > remain) step = remain;
                // IMP-DOG-020: 加速後の実際の歩幅でもう一度見る (上の先読みは加速前の速さ。低 fps の加速中に越えないため)
                if (_BlockedByRiver(pos, pos + _moveDir * step)) _speed = 0f;
                else dogRoot.position = SnapToGround(pos + _moveDir * step); // net: owner-only: 上の早期 return が非 Owner を弾く
            }

            ApplyMoveAnim();
        }

        // ================================================================ IMP-DOG-015

        /// <summary>はぁはぁの息。**Update ではなく LateUpdate で回す。**
        ///
        /// <see cref="Update"/> には早期 return が 8 本あり、そのどれを通っても
        /// 呼ばれなければ「走ったのに息が上がらない」「止まったのに鳴り続ける」が出る。
        /// このファイルは同じ罠を <see cref="NotifyPlay"/> で一度踏んでいて、
        /// あちらは「全部の return の直前で呼ぶ」で解いたが、**呼び忘れが増えるだけ**なので
        /// こちらは最初から出口を 1 つにする。
        ///
        /// ## 同期変数は 1 本も増やしていない (完了条件)
        ///
        /// 入力は <see cref="_speed"/> と、IMP-DOG-017 の近接 (<see cref="_paused"/>) ・
        /// 撫で中 (<see cref="KD_DogPetController.IsPetting"/>)。後の 2 つは元からローカル判定 —
        /// 音はローカル演出なので、各クライアント自分基準の距離・自分の手で正しい。
        /// **IMP-NET-005 で `_speed` は全クライアントで
        /// 「その画面で犬が実際に動いている速さ」になった** (Owner はシミュレーション値、
        /// 非 Owner は `VRCObjectSync` が配る実位置の差分)。だから
        /// 「走ったか」は各クライアントがローカルで正しく導出できる —
        /// `IMP-NET-001` の「演出は同期変数から導出する。イベントで配らない」に沿う。
        ///
        /// ## 音が出るのは「息が上がっている間」で、鳴らし方は loop
        ///
        /// 吠え (`PlayOneShot` の単発) とは別の <see cref="breathSource"/> を使う。
        /// 同じ AudioSource だと `PlayOneShot` と `loop` が噛み合わない。</summary>
        void LateUpdate()
        {
            // IMP-DOG-034: a frame whose Update was skipped (idle dog, Tick.cs) does nothing here. Nothing below has to run
            // every frame for an idle dog: the greeting finish and the hips bob only act in busy states (TickBusy), the
            // Animator Bools / companion values are write-on-change of flags that did not change (the stretch's running
            // CallTime counts as busy), and the breath fades integrate the carried time through _dt on the next tick.
            // IMP-DOG-036: on a legs frame only the Sit fold can have changed (ApplyMoveAnim lowers _wantSit when the legs move);
            // write it as the decision frame would. The rest keeps the decision's values (flags unchanged), the breath integrates
            // the whole interval on the next decision (_dt).
            if (!_tickedThisFrame)
            {
                if (dogAnimator != null && _wantSit != _wasSit) { _wasSit = _wantSit; dogAnimator.SetBool("Sit", _wantSit); }
                return;
            }
            FinishInterruptedGreeting();
            WaterHipsBobLate();   // IMP-DOG-020 step 7: the swim bob on a single-tier rig's hips, after the Animator (HillsideWater.cs)
            float dt = _dt;       // IMP-DOG-034: this frame's Update dt (+ the skipped frames' time)
            if (dt <= 0f) return;

            // ---- IMP-DOG-014: 伏せを Animator へ書く ----
            // **Update の 9 本の早期 return のどれを通っても、ここが 1 回だけ面倒を見る。**
            // Bool は一度に 1 つの規約 (設計書 §5) — 伏せている間は移動系が立たないが、
            // それは Coast() が実速度 0 で Run/Walk を下ろすことで自然に成立している
            if (dogAnimator != null && _wantLieDown != _wasLieDown)
            {
                _wasLieDown = _wantLieDown;
                dogAnimator.SetBool("LieDown", _wantLieDown);
            }

            // ---- IMP-DOG-010: 座る (立ち寄り先で眺める) を Animator へ書く ----
            // **伏せと同じ 1 箇所主義。** 座る枝は _wantLieDown / _wantSleep を立てないので、
            // 2 つの Bool が同時に立つことはない。LookAt / Pickup は ApplyMoveAnim 側が抑える
            if (dogAnimator != null && _wantSit != _wasSit)
            {
                _wasSit = _wantSit;
                dogAnimator.SetBool("Sit", _wantSit);
            }

            // ---- IMP-DOG-019 Phase 2: そばで寝るを Animator へ書く ----
            // **伏せ (LieDown) と同じ 1 箇所主義。** 寝る枝は _wantLieDown を立てないので、
            // 2 つの Bool が同時に立つことはない (Bool は一度に 1 つの規約)。
            // Animator 側は `Sleep` が立った瞬間にどの State からでも SleepIn へ抜け、
            // 出口は `Sleep == false → Idle` の 1 本だけ (Phase 1)
            if (dogAnimator != null)
            {
                _wasSleep = _wantSleep;
                // IMP-DOG-034 C1-4: CompanionPose and the line below looked the same slot up twice (sleepBoard.SlotOf = slots[dogIndex],
                // a static array wired by Build); the reference is now taken once and kept (LateSleepSlot). PoseId is still read every
                // frame (it can be non-zero with no live caller, so "nobody called" cannot skip it). Same values as before.
                KD_SleepSlot slot = LateSleepSlot();
                int pose = slot == null ? 0 : slot.PoseId;
                bool stretch = IsCompanionReady && slot != null && slot.StretchActive;
                int animation = pose == 3 ? 4 : pose == 4 ? 5 : pose;
                if (pose == 2 && slot != null && slot.PawHeld) animation = 3;
                if (pose == 1 && slot != null && slot.ChinHand != 0) animation = 6;
                if (stretch) animation = 7;
                // IMP-DOG-028: 洗われ中の足上げ (CallPaw = 3)。優先は最後 (Sit / LieDown / 集合 / 寝床の呼び出しが立っていれば出さない)。
                // 洗われていない犬は WashPawPose が false (_washActive が立たない) なので、下の 2 式は従来と同じ値になる
                // _wasMoving: the Walk / Run this frame's ApplyMoveAnim wrote (incl. its stop hold) - no static pose while the legs move
                bool washPaw = WashPawPose && !_wasMoving && !_wantSit && !_wantLieDown && !_groupSit && !IsCompanionReady;
                ApplyCompanionAnimation(_wantSleep && pose == 0 && !stretch,
                    washPaw || _groupSit || (IsCompanionReady && (pose != 0 || stretch)),
                    washPaw ? 3 : (_groupSit ? 2 : animation), stretch ? slot.StretchTime : 0f);
            }

            if (breathSource == null) return;

            // **Fetch 中は Wander の _speed が 0 に畳まれている** (割り込み 1)。
            // 走っているのは Fetch の足なので、両方見て大きい方を採る —
            // でないと「ボールを追って全力で走ったのに息が上がらない」になる
            float sp = _speed;
            if (fetchController != null && fetchController.DebugSpeed > sp)
                sp = fetchController.DebugSpeed;

            // IMP-DOG-034 C1-4: the breath is silent and stays silent this frame. _breathSilent records that the last pass ended with
            // everything at rest (level 0, run accumulator 0, no clip held, pitch 1 and volume 0 written, the source seen stopped),
            // and nothing else plays breathSource (playOnAwake off, only this block calls Play). With no run (sp below
            // breathRunSpeed), no run pant left, not asleep beside someone (IsCompanionReady false) and not _paused (no idle pant),
            // the code below would compute target 0, keep every value and write nothing; only its reads are skipped
            // (_breathLevel / _writtenBreathVolume are checked again: a harness may set the level and call _ApplyVoiceVolume directly)
            if (_breathSilent && sp < breathRunSpeed && Time.time >= _breathUntil && !IsCompanionReady && !_paused
                && _breathLevel == 0f && _writtenBreathVolume == 0f) return;
            _breathSilent = false;

            if (sp >= breathRunSpeed)
            {
                _breathRunAccum += dt;
                // 走り続けている間は毎フレーム延長される = 走っている間ずっと鳴る
                if (_breathRunAccum >= breathTriggerRunTime)
                    _breathUntil = Time.time + breathCalmDuration;
            }
            else
            {
                _breathRunAccum -= dt * breathRecoverScale;
                if (_breathRunAccum < 0f) _breathRunAccum = 0f;
            }

            // ---- IMP-DOG-017: そばに居る間のアイドル呼吸 ----
            // 走り (IMP-DOG-015) だけだと「近くに居る瞬間」と「息が上がっている瞬間」が
            // ほぼ重ならず、実質聞こえなかった。近接は人懐こさの _paused をそのまま読む —
            // 犬が立ち止まってこちらを見る「気づき」と同じ判定なので、因果がそこに乗る。
            // Fetch 仕事中は _paused が false に畳まれるので、勝手に鳴らない。
            // 撫でている間は消す (顔を寄せる距離で loop が鳴り続けるとうるさい)。
            // 走り由来の息はそのまま残る — Fetch 直後に撫でても息は上がったまま。
            //
            // ---- IMP-DOG-019 Phase 4: 寝息 ----
            // **寝ている間はアイドル呼吸の 2 つの前提が両方とも外れる:**
            //   1. 撫でていると消す → 消さない。**寝ている犬にとって息が返事そのもの**
            //      (DES-DOG-003 §4.1)。顔を寄せてもうるさくならないよう音量で解く
            //      (breathSleepVolume <= breathIdleVolume は Start が保証する)
            //   2. `_paused` に依存する → しない。呼んだ人が 1.5m の外へ立っても
            //      犬は寝たままなので、そこで息が止まると嘘になる
            // 走り由来の息は寝た瞬間まで残ってよい (寝てからは速度 0 なので自然に積まれない)。
            // **`IsAsleep` は自分のフィールドなので Behaviour 跨ぎのコストはゼロ。**
            bool runPant = Time.time < _breathUntil;
            bool sleepPant = IsAsleep;
            bool idlePant = !sleepPant && _paused
                && !(petController != null && petController.IsPetting);

            // 走りが常に勝つ (大きい方が鳴る)。フェードで出入りする (0 だとブツ切りになる)
            float targetVolume = runPant
                ? breathVolume
                : (sleepPant ? breathSleepVolume : (idlePant ? breathIdleVolume : 0f));
            float fadeSeconds = runPant ? breathFadeSeconds : breathIdleFadeSeconds;
            float rate = breathVolume / (fadeSeconds > 0.01f ? fadeSeconds : 0.01f);
            _breathLevel = Mathf.MoveTowards(_breathLevel, targetVolume, rate * dt);
            _ApplyVoiceVolume();

            // ---- 2026-09-04: 寝ている間は**寝息そのものの音源**へ差し替える ----
            // 専用の寝息音源が在るなら、ピッチを下げる代用よりそちらを鳴らす。
            // **走り由来の息が勝っている間は差し替えない** — 音量もピッチも runPant を
            // 優先しているので、クリップだけ寝息にすると 3 つの判断が割れる
            bool sleepClipWins = sleepPant && !runPant && breathSleepClip != null;
            AudioClip wantClip = SelectBreathClip(sleepClipWins, targetVolume > 0.001f);

            // 寝息は少しピッチを落とす。**変わったときだけ書く** (毎フレームの extern を増やさない)。
            // 走り呼吸が勝っている間は 1 のまま — 走った息をスローにすると別の生き物になる。
            // **寝息クリップが在るときも 1 のまま** — 音源そのものが寝息なので、
            // さらに下げると「寝ている息をスローにした音」になる (代用の二重掛け)
            float wantPitch = (sleepPant && !runPant && !sleepClipWins) ? breathSleepPitch : 1f;
            if (wantPitch != _breathPitchWritten)
            {
                _breathPitchWritten = wantPitch;
                breathSource.pitch = wantPitch;
            }

            bool want = targetVolume > 0.001f;
            if (want)
            {
                // **クリップ未設定でも無音で動く** (Phase 1 の完了条件)
                if (wantClip != null)
                {
                    if (wantClip != _breathClipWritten)
                    {
                        // 欲しいものと違うときだけ差し替える。Stop してから書くのは、
                        // 鳴っている最中に clip を替えると再生位置が残るため
                        _breathClipWritten = wantClip;
                        if (breathSource.isPlaying) breathSource.Stop();
                        breathSource.clip = wantClip;
                        breathSource.loop = true;
                        breathSource.Play();
                    }
                    else if (!breathSource.isPlaying)
                    {
                        breathSource.loop = true;
                        breathSource.Play();
                    }
                }
            }
            else
            {
                bool playing = breathSource.isPlaying;
                if (playing && _breathLevel <= 0.001f)
                {
                    breathSource.Stop();
                    playing = false;
                }
                // IMP-DOG-034 C1-4: at rest -> the next frames may skip this block (see the gate above)
                _breathSilent = !playing && _breathLevel == 0f && _breathRunAccum == 0f && _pantClip == null
                    && _breathPitchWritten == 1f && _breathVolumeValid && _writtenBreathVolume == 0f && !runPant;
            }
        }

        // ---- IMP-DOG-034 C1-4 ----
        private bool _breathSilent;            // the breath block ended at rest (cleared by OnEnable / ResetPerformanceOutputs)
        private KD_SleepSlot _lateSleepSlot;   // sleepBoard.SlotOf(dogIndex): slots is a static array, so the reference never changes

        KD_SleepSlot LateSleepSlot()
        {
            if (_lateSleepSlot != null) return _lateSleepSlot;
            if (sleepBoard == null) return null;
            _lateSleepSlot = sleepBoard.SlotOf(dogIndex);   // null (unwired / out of range) is asked again next frame, as before
            return _lateSleepSlot;
        }

        // ローカルな音色の抽選だけ。動きの乱数・Owner状態には関与しない。
        // フェードアウトが終わるまで候補を保持し、近接判定が揺れても音源を切り刻まない。
        AudioClip SelectBreathClip(bool sleepClipWins, bool audible)
        {
            if (sleepClipWins)
            {
                _pantClip = null;
                return breathSleepClip;
            }
            if (!audible)
            {
                AudioClip fadingClip = _pantClip;
                if (_breathLevel <= 0.001f) _pantClip = null;
                return fadingClip;
            }
            if (_pantClip == null)
                _pantClip = breathClip == null ? breathAlternateClip
                    : breathAlternateClip == null ? breathClip
                    : (Random.Range(0, 2) == 0 ? breathClip : breathAlternateClip);
            return _pantClip;
        }

        // ================================================================ IMP-NET-005

        /// <summary>犬ユニットの Owner か。**見るのは常に dogRoot の Owner**
        /// (DES-NET-002 不変条件 8)。
        ///
        /// 自分の <c>gameObject</c> (`KD_WanderSystem_*`) を見てはいけない —
        /// dogRoot に `VRCObjectSync` が載ったことで Ownership の軸が 2 本になり、
        /// 「状態を決める人」と「位置を書く人」が割れると却下理由 (iv) が形を変えて再発する。
        /// 軸を割らないための規律は「**犬側で SetOwner を一度も呼ばない**」の 1 行で、
        /// それによって dogRoot も兄弟もインスタンスマスターに揃ったまま一緒に移る。</summary>
        bool IsDogOwner()
        {
            return dogRoot != null && Networking.IsOwner(dogRoot.gameObject);
        }

        /// <summary>非 Owner の実速度を、**観測した Transform 差分**から測る (IMP-NET-005 A2)。
        ///
        /// Run/Walk は元々「状態ではなく実速度から決める」が方針なので、
        /// <see cref="_speed"/> の入力をシミュレーション値から観測値へ差し替えるだけで
        /// <see cref="ApplyMoveAnim"/> は 1 行も変わらない。
        /// シミュレーション値のまま残すと、**位置はネットワーク由来なのに脚は自前の予測**
        /// という食い違いが出て、BUG-DOG-001 の「静止ポーズのまま滑る」が非 Owner 側で再発する。
        ///
        /// ワープ (`VRCObjectSync` の補正・Late Join・リスポーン) を速度に数えないのは
        /// <see cref="KD_DogPack.teleportThreshold"/> / <c>KD_DogStopAnchor</c> と同じ作法。</summary>
        void ObserveSpeed()
        {
            Vector3 pos = dogRoot.position;
            float dt = Dt();   // IMP-DOG-034: the time since the last observation (skipped frames included)
            if (_obsPrevValid && dt > 0.0001f)
            {
                Vector3 moved = pos - _obsPrevPos;
                moved.y = 0f;
                float m = moved.magnitude;
                // IMP-DOG-036 S1: the observation interval is a decision interval now (up to 0.25 s), over which a sprinting leashed
                // dog (7.2 m/s) covers 1.8 m. The old 1.5 m stays the floor (unchanged for per-frame decisions); longer intervals
                // allow ObservedTeleportSpeed x dt
                bool warped = m > Mathf.Max(ObservedTeleportThreshold, ObservedTeleportSpeed * dt);
                _speed = warped ? 0f : m / dt;
                // IMP-DOG-010: ワープの直後を「ずっと止まっていた」と読ませない。姿勢はこちらの
                // 連続停止時間から導出されるので、飛んだ先で即座に嗅ぐポーズが出てしまう
                if (warped) _stoppedSince = -1f;
            }
            else
            {
                _speed = 0f;
            }
            _obsPrevPos = pos;
            _obsPrevValid = true;
        }

        Vector3 GroundAfterPlacedSleep(Vector3 pos)
        {
            _dbgWriter = DbgWGround;   // IMP-DOG-036 step probe: every caller writes the result to the root (sleep / station release)
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * groundRayOffset, Vector3.down, out hit,
                    Mathf.Infinity, groundLayerMask, QueryTriggerInteraction.Ignore))
                return new Vector3(pos.x, hit.point.y, pos.z);
            // 地中指定や床の無い場所からは、共有の徘徊中心へ戻す。
            return SnapToGround(areaCenter != null ? areaCenter.position : pos);
        }

        Vector3 SnapToGround(Vector3 pos)
        {
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * groundRayOffset, Vector3.down, out hit,
                    groundRayDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;
            return pos;
        }

        /// <summary>LCG ベースの決定論乱数 [0,1)。wildlife-area WA_Animal と同型。
        /// uint の % は Udon 未公開・負の int→uint キャストは実機で例外のため、
        /// long 演算 + 31bit マスクで全て非負に保つ (vrc-udonsharp §1)。
        /// 上位 24bit を使うことで float 丸めでも 1.0 に到達しない (Codex 指摘)。</summary>
        /// <summary>
        /// 時刻のレールから決定論的に 0〜1 を作る (状態を持たない = 同期不要)。
        ///
        /// IMP-DOG-005: `seedSalt` を混ぜて犬ごとに別のレールにする。
        /// **`seedSalt == 0` のときは XOR 項が 0 になり 1 匹版と恒等に一致する**
        /// (コーギーの徘徊が従来と変わらないことの根拠)。
        /// </summary>
        float Rand01(int a, int b)
        {
            return Rand01Salt(a, b, seedSalt);
        }

        /// <summary>
        /// <see cref="Rand01"/> の種指定版 (IMP-NET-002)。**他犬の徘徊レールを再現する**ために
        /// 種を引数化した — <see cref="WanderAnchorAt"/> が名簿でキャッシュした
        /// <see cref="_packSalt"/> を渡す。自分のレールは従来どおり Rand01 で引く
        /// (salt == seedSalt なら恒等に一致する = 既存の徘徊は 1 bit も変わらない)。
        /// </summary>
        float Rand01Salt(int a, int b, int salt)
        {
            long h = (long)a * RandHashA ^ (long)b * RandHashB ^ (long)salt * RandHashSalt;
            h = (h * RandLcgMul + RandLcgAdd) & RandMask31;
            return (h >> RandTopShift) / RandTopScale;
        }

        /// <summary>
        /// じゃれ合いの予定表用の決定論乱数 [0,1)。**<see cref="seedSalt"/> を混ぜない。**
        ///
        /// 徘徊は「犬ごとに別のレール」が要件なので salt を混ぜるが、じゃれ合いは逆で
        /// **2 匹が同じ予定表を見ること**が要件 (誰と誰が・どの型で・どちらの役か)。
        /// ここで salt を混ぜると、片方だけが遊びに来る / 両方が追う役になる、という
        /// **Play では「たまに変」としか見えない壊れ方**をする。
        /// 混同を構造的に避けるため関数ごと分けてある — <see cref="Rand01"/> と統合しないこと。
        /// </summary>
        float PlayRand(int a, int b)
        {
            long h = (long)a * RandHashA ^ (long)b * RandHashB;
            h = (h * RandLcgMul + RandLcgAdd) & RandMask31;
            return (h >> RandTopShift) / RandTopScale;
        }
    }
}
