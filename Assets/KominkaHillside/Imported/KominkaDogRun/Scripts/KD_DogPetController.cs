using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-PET-001: 撫で判定 (M2a) + 手への追従 (M2b)。
    /// IMP-PET-002: ゾーン別リアクション — Head = 手を追う / Cheek = 頬を押しつける /
    /// Chin = 顔を上へ向けて首元を預ける (core-concept §13)。
    ///
    /// 判定はローカル (不変条件 5)。ローカルプレイヤーの両手だけを見る。
    /// 「撫で」の成立は接触だけでは不十分で、接触したまま手が動いていること
    /// (vrc-animator-reactions §6)。ゾーンは最寄りのアンカーで決め、
    /// 切替にはヒステリシスを入れる。
    ///
    /// リアクションは全ゾーン共通で LateUpdate から
    /// 「Constraint のソース側 = アーマチュア側ボーン」へ追加回転を書く
    /// (VER-DOG-001 実測 / 設計書 §4)。目標回転だけがゾーンで変わる。
    /// Animator が書き戻さないフレーム (実測 179 中 57 回) があるため、
    /// 蓄積防止ガード (§4 手順 5) を省略してはいけない。ガードはボーンごとに独立。
    ///
    /// IMP-FET-001: Fetch 中 (dogState != Idle) は KD_DogFetchController が
    /// _SuppressPetting / _ResumePetting で撫でを抑制する (設計書 §1 —
    /// 無反応が「ボールに夢中」として演技になる)。抑制中も LateUpdate は回り続け、
    /// 追加回転は identity へ自然に減衰する (急に戻さない)。
    ///
    /// IMP-PET-004: 撫で成立中の尻尾フリフリ (アーマチュア Bone.018 へ書く。
    /// ko-gi- 側は Bone.019 が Constraint で追い、Bone.020 以降は PhysBone)。
    ///
    /// IMP-PET-005: 接触強度 (片手/両手) の一軸リアクション
    /// (仕様 = pet-reaction-design.md / 順序 = 2026-08-12-imp-pet-005-impl-plan.md)。
    /// - _intensity エンベロープ 1 本 (片手 = oneHandIntensity / 両手 = 1) を
    ///   尻尾・腰・耳・表情・ハートが係数として読む。段階の二重管理はしない
    /// - 両手 = 同一 (ローカル) プレイヤーの両手がゾーン内 + 片方が撫で/保持成立。
    ///   維持側にだけ猶予 (入る側は即時 — 返事の即応性を削らない)
    /// - 大喜び移行はランダムでも時間でもなく両手成立エッジ。ワンショットは
    ///   演出 (バースト/SE) だけで、状態はエンベロープが継続する
    /// - **顔近接 (親密) は削除した (2026-08-12 決定)**。頭の追従先を手からプレイヤーの顔へ
    ///   「置き換える」設計だったため、寄ると Follow Hand と頬すりすりが止まり、
    ///   プレイヤーには「反応が弱くなった」としか読めなかった (Play 実測 ×2)。
    ///   閾値調整では直らない (0.45m は常時発火・0.28m は体感不能)。
    ///   犬種ごとに顔の高さが違うため多頭化でチューニングが 3 倍になる点も棄却理由。
    ///   段階は片手/両手の一軸だけにする
    /// - 気づく導入 = 接触開始エッジからの部位別ディレイ / 余韻 = 接触終了エッジで
    ///   最後にプレイヤーを見る (新しい状態機械は作らない)
    /// - 腰の逆位相スイングは棄却 (2026-08-12 Play 実測): Bone.001 の子は前半身
    ///   (胸→首→頭) なので、書くと「お尻固定で前が揺れる」逆の絵になる。
    ///   お尻側を振るにはルート Bone しかなく、脚ごと回って足が滑るため不成立
    /// - **耳は Udon から動かさない (2026-08-12 決定)**。v1 (アーマチュア側へ書き
    ///   PB isAnimated=false) は Constraint と PB がボーンを取り合って発振、
    ///   v2 (PB ルートへ親を挿入) は PB が親回転を打ち消して不動、
    ///   v3 (同じ書き方 + PB isAnimated=true) も見た目が破綻、mimi BlendShape も不一致 —
    ///   Play 実測 ×4 でいずれも棄却した。耳は PhysBone に任せ、
    ///   **手で触れると揺れる**だけにする (PB の allowCollision/allowGrabbing は元から有効)。
    ///   喜びの段階は尻尾の振幅・速度 + 預け + 表情 (metozi/egao) + ハートで表現する
    ///
    /// IMP-NET-001 (2026-08-15): 撫での結果を他プレイヤーにも見せる。
    /// - **同期の器は KD_DogFetchController** (Manual)。ここは「成立した結果」を
    ///   エッジで渡すだけで、送信の判断は器が持つ (`ReconcilePetSync`)
    /// - **演出は同期変数から導出する。イベントで配らない** — `_PlayPetHearts` を
    ///   SendCustomNetworkEvent で叩く形にすると Stop の取りこぼしで出しっぱなしになり、
    ///   Late Joiner にも届かない (`vrc-networking` §6)。発火口はそのままで**呼び出し元だけ**変えた
    /// - LateUpdate は `_eff*` (実効状態) だけを読む。**ローカルの撫でが成立していれば
    ///   ローカルが勝つ** — 自分の画面では必ず自分の手へ返事が返る (不変条件 4)
    /// - リモートの手の位置は**同期しない**。VRChat がすでに同期しているアバターの IK から
    ///   近いほうの手を採る (`RemoteHandPos`) — 帯域ゼロで、しかも読むのは
    ///   **その画面が描いている手そのもの**なので、犬の位置がずれていても手を見ているように見える
    /// - 出しっぱなし防止は導出側の毎フレーム `Utilities.IsValid` が本体。
    ///   同期の解除が届かなくても ♡ と SE は必ず落ちる
    ///
    /// IMP-DOG-019 Phase 4 (2026-09-04): **寝たまま撫でられる。**
    /// - 判定・♡・撫でエフェクト・同期は**一切分岐しない** — ゾーンアンカーは骨の子なので
    ///   横倒しでも顔に付いて回り、`Sleep` 中に `Pet` を立てても Animator は
    ///   `寝なでなで` のまま (Phase 1 で出口を `Sleep == false` の 1 本に絞ってある)
    /// - 分岐するのは**立っている前提で書いた身体リアクションだけ** —
    ///   頭 (ゾーン別の到達ポーズ → 可動域を絞った LookTarget) ・尻尾 (振幅と速さを落とす) ・
    ///   余韻と外からの注視 (寝ている犬には出さない)
    /// - `IsAsleep` は毎フレーム `wanderController` から引き、触れていない睡眠中も閉眼を維持する
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_DogPetController : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/Pet System が設定する)")]
        public Animator dogAnimator;
        [Tooltip("ko-gi- 側 Bone.004 の子。表示中の頭の位置に追従する。" +
            "手を追う向きの基準点であり、ハートの位置基準でもある (BUG-PET-001: ここは動かさない)")]
        public Transform headAnchor;
        [Tooltip("BUG-PET-001: Head ゾーンの判定専用アンカー (頭の上面 = 頭頂〜おでこの中間)。" +
            "headAnchor は頭蓋の内側にあり、おでこが CHEEK に取られていた。null なら headAnchor へフォールバック")]
        public Transform headZoneAnchor;
        [Tooltip("ko-gi- 側 Bone.009_L の子 (左頬)")]
        public Transform cheekAnchorL;
        [Tooltip("ko-gi- 側 Bone.009_R の子 (右頬)")]
        public Transform cheekAnchorR;
        [Tooltip("ko-gi- 側 Bone.006 の子 (顎の下)")]
        public Transform chinAnchor;
        [Tooltip("アーマチュア側 Bone.004 = RotationConstraint のソース。追加回転の書き先")]
        public Transform sourceBone;
        public KD_SleepSlot companionSlot;
        public Vector3 chinRestVector; // Source head rotation space, metres; sampled from the authored clip.
        public Vector3 chinRestTarget; // Dog-local contact point; same point as the hand detector.
        public float chinRestRadius = .08f;
        public Transform dogRoot;
        [Tooltip("IMP-NET-001: 撫での同期の器 (同じ犬の KD_FetchSystem)。" +
            "null なら従来どおりローカル完結で動き、他人の撫では見えない")]
        public KD_DogFetchController fetchController;
        [Tooltip("IMP-DOG-019 Phase 4: 同じ犬の KD_WanderSystem。**読むのは IsAsleep だけ**。" +
            "null なら従来どおり (寝ていても立っている犬と同じ返事を返す = 首が捻れる)。" +
            "配線は Wander Setup が張る (fetchController を Fetch Setup が張るのと同じ向き)")]
        public KD_DogWanderController wanderController;

        [Header("撫で判定 (ローカル)")]
        [Tooltip("この距離まで手が近づいたら接触")]
        public float touchRadius = DefaultTouchRadius;
        [Tooltip("接触の解除はやや広く取る (境界でのちらつき防止)")]
        public float releaseRadius = DefaultTouchRadius * ReleaseRadiusRatio;
        [Tooltip("m/s。接触したままこれ以上手が動いていたら撫で")]
        public float minStrokeSpeed = 0.03f;
        [Tooltip("手が止まってから撫で解除までの猶予秒")]
        public float strokeGrace = DefaultStrokeGrace;
        [Tooltip("m。プレイヤーの頭からこれ以上離れた手の値は無効扱い (トラッキングロスト対策)")]
        public float handValidMaxDistance = DefaultHandValidMaxDistance;

        [Header("ゾーン判定 (IMP-PET-002)")]
        [Tooltip("ゾーン切替に要する距離差 (m)。境界でのちらつき防止")]
        public float zoneSwitchMargin = 0.03f;

        [Header("Head = 手を追う (M2b の核心。完成形 — 劣化させない)")]
        [Tooltip("頭を向けられる最大角度。超えたら自然に諦める")]
        public float maxFollowAngle = DefaultMaxFollowAngle;
        [Tooltip("頭より下の手を追うときの下向きピッチ上限 (度)。VR 第 1 回で首が変に曲がった対策")]
        public float maxDownPitch = 10f;
        [Tooltip("追いの速さ。遅れが生き物っぽさになる (瞬間追従にしない)")]
        public float followResponsiveness = DefaultFollowResponsiveness;
        [Tooltip("戻りの速さ。急に戻さない")]
        public float returnResponsiveness = DefaultReturnResponsiveness;
        [Tooltip("外から差し込まれた注視 (催促 / フェイント) へ頭を向ける速さ。" +
            "撫での追従 (followResponsiveness) より遅くする — 手を追うのは即応、" +
            "遠くのボールを目で追うのは少し遅い方が「見ている」に見える (IMP-FET-005)")]
        public float gazeResponsiveness = 4.5f;
        [Tooltip("dogRoot ローカルでの頭のニュートラル方向 (+Z が前)")]
        public Vector3 neutralHeadDir = new Vector3(0f, DefaultNeutralHeadDirY, DefaultNeutralHeadDirZ);

        [Header("Cheek = 頬を掌へ押しつける (core-concept §13)")]
        [Tooltip("頬を手へ押しつけるロール角 (度)。実機で逆に傾いたら負値にする")]
        public float cheekRollDeg = 18f;
        [Tooltip("押しつけ時にわずかに手の方へ向くヨー角 (度)")]
        public float cheekYawDeg = 8f;

        [Header("Chin = 顔を上へ向け首元を預ける (core-concept §13)")]
        [Tooltip("上を向くピッチ角 (度)。手の位置は追わず到達ポーズへ補間する")]
        public float chinPitchUpDeg = 22f;
        [Tooltip("手が左右にあるときわずかに向くヨー角 (度)")]
        public float chinYawDeg = 6f;

        [Header("撫でクリップ")]
        [Tooltip("なで1 で確定 (2026-08-10 PCVR 判定)。0 のまま使う")]
        public float petClipIndex = 0f;

        [Header("接触強度 (IMP-PET-005 — 片手/両手)")]
        [Tooltip("片手撫での強度係数 (無次元 0〜1)。両手 = 1。PCVR 体感で確定する (Phase 1 は仮)")]
        public float oneHandIntensity = DefaultOneHandIntensity;
        [Tooltip("強度の立ち上がりの速さ (大きいほど速い)。返事は早く")]
        public float intensityAttack = DefaultIntensityAttack;
        [Tooltip("強度の減衰の速さ (小さいほどゆっくり = 手を離した余韻)")]
        public float intensityRelease = DefaultIntensityRelease;
        [Tooltip("秒。両手成立を維持する猶予 (片手へ落ちる判定だけ遅らせる。入る側は即時)")]
        public float twoHandGrace = 0.5f;

        [Header("大喜び移行ワンショット (IMP-PET-005)")]
        [Tooltip("両手成立エッジで出すハートの個数 (heartFx 側)")]
        public int joyBurstCount = 6;
        [Tooltip("秒。移行演出 (バースト/SE) の連打防止。状態遷移自体は抑制しない")]
        public float joyBurstCooldown = 2f;
        [Tooltip("任意。移行時に一度だけ鳴らす小さな声/鼻息 (null なら音なし — 音は身体表現の補助)")]
        public AudioSource joyAudioSource;
        public AudioClip joyClip;

        // 顔近接 (親密) は 2026-08-12 に削除した — クラスコメント参照。段階は片手/両手の一軸だけ

        [Header("気づく導入と余韻 (IMP-PET-005 — 仕様 §4 / §10)")]
        [Tooltip("秒。撫で成立から尻尾が振り出すまで (視線 → 尻尾 → 頭寄せの順。耳は PhysBone 任せで制御しない)")]
        public float tailStartDelay = 0.15f;
        [Tooltip("秒。手を離した後「最後にプレイヤーを見る」余韻の長さ")]
        public float farewellDuration = 1.1f;
        [Tooltip("度。余韻の視線の最大角度")]
        public float farewellMaxAngle = 20f;

        [Header("寝ている犬の返事 (IMP-DOG-019 Phase 4)")]
        // **絵は `寝なでなで` が全部持っている** (購入元が「寝たまま撫でられる」用に焼いたクリップ。
        // Phase 1 で `Sleep` が真の間はどの State へも出ないようにしてある = `Pet` を立てても
        // 横倒しのまま)。だからここでやるのは**足すことではなく、立っている犬の前提で
        // 書いた 2 つを寝姿勢用に置き換えること**:
        //
        //   1. **頭の追従** — ゾーン別の到達ポーズ (頬すり / 顎上げ) も LookTarget も
        //      「犬は立っている」前提の dogRoot ローカル角で書いてある。実測で犬は
        //      右脇腹を下に約 45° 傾いて寝るので、そのまま足すと**首が捻れる**。
        //      寝ている間は可動域を `sleepFollowMaxAngle` まで狭めた LookTarget 1 本にして、
        //      「薄目を開けて手の方へ鼻先を寄せる」程度にする
        //   2. **尻尾** — 振り自体は既存の器がそのまま使える (振れ軸が `dogRoot.up` =
        //      ワールド上向きなので、横倒しの犬では**尻尾が床を掃く**動きになる。
        //      寝たまま尻尾だけ振るという DES-DOG-003 §4 の例そのもの)。
        //      振幅と速さだけ落として「寝ぼけた振り」にする — 全開で振ると起きて見える
        //
        // **♡ と撫でエフェクトは通常どおり出す** (設計原則: 身体リアクションが先、
        // パーティクルはその増幅。身体側 = 寝たままの絵 + 尻尾 + 寝息が先にある)
        [Tooltip("度。寝ている間の頭の可動域。**立っているとき (maxFollowAngle 40) より小さく** — " +
            "横倒しの首へ大きな角度を足すと捻れる。鼻先が少し動く程度でよい")]
        public float sleepFollowMaxAngle = DefaultSleepFollowMaxAngle;
        [Tooltip("寝ている間の尻尾の振幅・速さの倍率 (0〜1)。**1 にすると起きているのと同じ振り**に" +
            "なって「寝ている」が読めなくなる")]
        public float sleepTailScale = DefaultSleepTailScale;

        [Header("尻尾フリフリ (IMP-PET-004 / 005)")]
        [Tooltip("アーマチュア側 Bone.018 = ko-gi- 側 Bone.019 の Constraint ソース。追加回転の書き先")]
        public Transform tailSourceBone;
        [Tooltip("片側の振れ角 (度)。犬ローカル上軸まわりのヨー往復 (片手時 = この値のまま。PCVR 確定値を守る)")]
        public float tailWagAmplitudeDeg = 40f;
        [Tooltip("往復の速さ (Hz)。速すぎると PhysBone の先端が付いてこない (2026-08-11 PCVR: 3.5 は速すぎた)")]
        public float tailWagFrequency = 1.8f;
        [Tooltip("振り始め / 減衰の速さ。離すと自然に減衰する")]
        public float tailWagResponsiveness = DefaultTailWagResponsiveness;
        [Tooltip("両手時の振幅倍率 (片手 1.0 → 両手でこの倍率へ)")]
        public float twoHandTailAmpScale = 1.35f;
        [Tooltip("両手時の速度倍率 (上げすぎると PhysBone が付いてこない)")]
        public float twoHandTailFreqScale = 1.15f;
        [Tooltip("度。撫で中に尻尾の根元を持ち上げるピッチ (真上まで立てない — 興奮に化ける)")]
        public float tailBasePitchDeg = 15f;

        // 耳は Udon から動かさない (2026-08-12 決定 — クラスコメント参照)。
        // PhysBone に任せ、手で触れると揺れるだけにする

        [Header("表情 (IMP-PET-005 — metozi = 目を細める / egao = 笑顔)")]
        [Tooltip("karada の SkinnedMeshRenderer。null なら表情は動かない")]
        public SkinnedMeshRenderer faceRenderer;
        [Tooltip("metozi (目を閉じる) の BlendShape index。Setup が名前から解決する")]
        public int metoziIndex = 6;
        [Tooltip("egao (笑顔) の BlendShape index")]
        public int egaoIndex = 13;
        [Tooltip("強度 1.0 (両手) のときの metozi 値 (0〜100)。完全に閉じさせない")]
        public float metoziMax = 60f;
        [Tooltip("両手 (大喜び) のときの egao 値 (0〜100)")]
        public float egaoMax = 50f;

        [Header("撫でハート (IMP-PET-003 / 005)")]
        [Tooltip("撫で成立中に漂う ♡。ボーンの子に置かない (Setup が KD_PetSystem 直下へ作り、位置は LateUpdate で追従)")]
        public ParticleSystem heartFx;
        [Tooltip("2 種目の ♡ (中抜きハート。2026-08-11 ユーザー追加)。null なら 1 種で動く")]
        public ParticleSystem heartFx2;
        [Tooltip("頭アンカーからの上オフセット (m)")]
        public float heartOffsetY = 0.12f;
        [Tooltip("♡ の RateOverTime — 片手 (段階が量で読めるように 2→6。heartFx2 は半分)")]
        public float heartRateOneHand = 2f;
        [Tooltip("同 — 両手")]
        public float heartRateTwoHands = 6f;

        [Header("撫でエフェクトの候補 (IMP-PET-006)")]
        [Tooltip("品目ごとの ParticleSystem。**fxProductKeys と添字で対応する**。" +
                 "選ばれたものを heartFx へ差し替えるだけで、発火口の中身は変えない")]
        public ParticleSystem[] fxVariants;
        [Tooltip("同 — 2 枚目のレイヤー (無い品目は null でよい)")]
        public ParticleSystem[] fxVariants2;
        [Tooltip("品目ごとの2層目の発生量倍率。未設定は既存ハートと同じ0.5。小さなきらめきは2")]
        public float[] fxAccentRateMultipliers;
        [Tooltip("恒久 ID との対応 (KD_ShopItem.productKey)")]
        public string[] fxProductKeys;

        [Tooltip("IMP-PET-006 Phase 3: 撫で主に合わせてエフェクトを差し替える解決役。" +
                 "**null なら常に自分の選択のまま** (Phase 2 までの挙動)")]
        public KD_PetFxBox petFxBox;

        [Header("デバッグ表示 (Tools/KominkaDogRun/Pet System/4 で作成。公開前に 5 で撤去)")]
        [Tooltip("null なら何もしない。配線されているときだけモード/強度を表示する")]
        public TMPro.TextMeshPro debugText;
        [Tooltip("debugText の Transform (TMP 型の .transform は Udon 未公開のため別配線)")]
        public Transform debugTextTransform;
        [Tooltip("犬の足元からの表示高さ (m)")]
        public float debugTextHeight = 0.9f;

        // ゾーン (int で持つ。UdonSharp なので enum を増やさない)。
        // **public なのは KD_DogFetchController が petState の下位 2 ビットへこの値を
        // そのまま詰めるため** (IMP-NET-001)。2 ビットに収まること
        // (ZoneChin <= PetZoneMask) は Verify (Dog Unit/4) が機械検査する (IMP-INF-001 / D)
        public const int ZoneHead = 0;
        public const int ZoneCheekL = 1;
        public const int ZoneCheekR = 2;
        public const int ZoneChin = 3;

        // ================================================================ 既定値の正本
        // IMP-INF-001 / B の方針 (宣言の初期値と Start の 0 フォールバックが同じ定数を見る) を、
        // 破壊的監査 RC-1 でこのクラスへも広げた。**0 で死ぬ値と、大小関係が要る対だけ**を
        // 正本化する — 見え方の調整値 (cheekRollDeg 等) は 0 でも「効かない」だけなので対象外。
        const float DefaultTouchRadius = 0.14f;
        const float DefaultStrokeGrace = 0.6f;
        const float DefaultHandValidMaxDistance = 3f;
        const float DefaultMaxFollowAngle = 40f;
        const float DefaultFollowResponsiveness = 6f;
        const float DefaultReturnResponsiveness = 3f;
        const float DefaultOneHandIntensity = 0.55f;
        const float DefaultIntensityAttack = 6f;
        const float DefaultIntensityRelease = 1.5f;
        const float DefaultTailWagResponsiveness = 4f;
        /// <summary>寝ている間の頭の可動域 (度)。立ち (40) の 1/5。**0 で「追わない」ではなく
        /// 既定へ戻す** — 既存シーンへ後から足したフィールドは 0 で入るので、
        /// 0 を「意図した無効化」と読むと Phase 4 の返事が無言で消える。</summary>
        const float DefaultSleepFollowMaxAngle = 8f;
        /// <summary>寝ている間の尻尾の倍率。振ってはいるが「寝ぼけている」と読める量。</summary>
        const float DefaultSleepTailScale = 0.55f;
        /// <summary>解除半径を掴み半径から作るときの倍率。**必ず 1 より大きい** —
        /// 等しいと境界で毎フレーム掴み直しになり、速度サンプルが永久に立たない。</summary>
        const float ReleaseRadiusRatio = 1.43f; // 0.14 → 0.20 (確定値の比)
        // neutralHeadDir の既定値。**Vector3 は const にできないので、ここだけは
        // 宣言の初期値と二重に持つ** (成分を定数化してもコンストラクタ呼び出しは
        // 二重になるので、可読性を採った)。片方を動かしたらもう片方も動かすこと
        const float DefaultNeutralHeadDirY = 0.3f;
        const float DefaultNeutralHeadDirZ = 1f;

        VRCPlayerApi _localPlayer;
        Transform _sourceParent;
        bool _suppressed; // Fetch 中 (KD_DogFetchController が制御)

        /// <summary>撫でが成立しているか (KD_PhotoDirector が撫での一枚の判定に使う)。
        /// 抑制中は _SuppressPetting が _petting を落とすので false になる。</summary>
        public bool IsPetting { get { return _petting; } }

        /// <summary>両手の大喜びが成立しているか (IMP-NET-001 / 撮影が使える発火口)。</summary>
        public bool IsBigJoy { get { return _petting && _twoHands; } }

        // 撫で判定の状態
        // IMP-INF-022: PC/Quest 共通。判定だけ間引き、接触中の追従・演出は毎フレーム。
        const float ContactSampleInterval = 0.1f;
        const float FarContactSampleInterval = 0.25f;
        const float FarContactDistance = 4f;
        float _nextContactSampleTime;
        float _previousContactSampleTime;
        bool _petting;
        bool _handHeld;          // 追従対象の手を掴んでいるか
        bool _handIsRight;
        int _zone = ZoneHead;    // 追従対象の手が今どのゾーンにいるか
        bool _prevSampleValid;
        Vector3 _prevHandPos;
        float _lastStrokeTime;
        Vector3 _followHandPos;  // LateUpdate へ渡す手の位置
        bool _followHandValid;

        // IMP-PET-005: 接触強度・導入と余韻の状態
        bool _twoHands;           // 両手成立 (維持猶予込み)
        bool _otherHandIn;        // 反対の手がゾーン内か (ヒステリシス用)
        float _twoHandsHoldUntil; // 維持猶予の期限
        bool _prevBigJoy;         // 大喜び移行エッジ検出
        float _lastJoyBurstTime = -1000f;
        float _intensity;         // 強度エンベロープ (0 → oneHandIntensity → 1)
        Vector3 _playerHeadPos;   // 手の有効性判定と余韻の視線に使う
        bool _playerHeadValid;
        float _contactStartTime = -1000f; // 気づく導入の起点
        float _farewellUntil = -1000f;    // 余韻 (最後にプレイヤーを見る) の期限
        bool _wasTouching;
        bool _wasPetting;

        // 追加回転 (ワールド差分で持ち、書く直前にボーンローカルへ変換する)
        Quaternion _worldDelta = Quaternion.identity;

        bool _heartsPlaying; // ♡ の Play/Stop を一度だけ切り替えるガード

        // 蓄積防止ガード (設計書 §4 手順 5)。ボーンごとに独立した状態で持つ
        Quaternion _lastWritten = Quaternion.identity;
        Quaternion _lastBase = Quaternion.identity;
        bool _hasLast;

        // 尻尾 (IMP-PET-004)
        Transform _tailParent;
        float _tailPhase;
        float _tailWeight;
        Quaternion _tailLastWritten = Quaternion.identity;
        Quaternion _tailLastBase = Quaternion.identity;
        bool _tailHasLast;

        // デバッグ表示 (IMP-PET-005)。0.25s 間隔でだけ文字列を組む (毎フレームは組まない)
        float _nextDebugTime;
        string _dbgMode = "IDLE";
        float _lastHeartRate;
        float _heartAccentRateMultiplier = 0.5f;

        // 表情。書き込み中フラグ (止めるときに一度だけ 0 を書いてクリップへ返す)
        bool _metoziWriting;
        bool _egaoWriting;

        // ---- IMP-NET-001: 実効状態 (ローカルの撫で または リモートの撫での再現) ----
        // LateUpdate はこちらだけを読む。**ローカルが成立していればローカルが勝つ** —
        // 自分の画面では必ず自分の手へ返事が返る (不変条件 4 / IMP-PET-005 の既存挙動)
        bool _effPetting;
        bool _effTwoHands;
        int _effZone = ZoneHead;
        Vector3 _effFollowPos;
        bool _effFollowValid;
        bool _effRemote;      // リモートの撫でを再現中か (デバッグ表示と導入エッジに使う)
        bool _effWasPetting;
        // IMP-PET-006 Phase 3: 直近に解決した撫で主 (-1 = 自分 / 誰も。-2 = 未解決)。
        // **エフェクトの引き直しをこの変化だけに絞る**ための印
        int _fxPetterId = -2;

        // ---- IMP-FET-005: 外から差し込まれる注視 / 催促 (設計書 §10 決定 3) ----
        Vector3 _gazePos;
        float _gazeRoll;
        int _gazeFrame = -1;  // 差し込まれたフレーム。**そのフレームだけ有効**
        float _urge;
        int _urgeFrame = -1;
        float _urgeWeight;    // 尻尾用のエンベロープ (立ち上がり・落ちを滑らかにする)
        bool _petBoolWritten; // Animator へ書いた Pet Bool (変化時だけ書くためのキャッシュ)
        bool _petBoolOnce;

        // 同期へ送った (= fetchController へ渡した) ローカル状態。変化したときだけ渡す
        bool _pushedPetting;
        int _pushedZone = -1;
        bool _pushedTwoHands;

        // 同値判定の閾値。cos(θ/2) なので 0.999999 ≈ 0.16°。
        // 厳しすぎると「Animator が書き戻していない」を見逃して蓄積する方向に倒れるため、
        // 待機アニメの自然な 1 フレーム変化 (実測 1.43°) より十分小さい範囲で緩めてある
        const float PoseSameDot = 0.999999f;

        void Start()
        {
            _localPlayer = Networking.LocalPlayer;
            if (sourceBone != null) _sourceParent = sourceBone.parent;
            _lastStrokeTime = -1000f; // 初期値 0 だとワールド開始直後の接触が grace 内で撫で扱いになる

            // 配線漏れは無言で死なせず起動時に知らせる (vrc-udonsharp)
            if (dogAnimator == null || headAnchor == null || sourceBone == null || dogRoot == null)
            {
                Debug.LogError("[KD_DogPetController] wiring incomplete. Run Tools/KominkaDogRun/Pet System setup.");
                enabled = false;
                return;
            }
            if (cheekAnchorL == null || cheekAnchorR == null || chinAnchor == null)
            {
                // ゾーンアンカーが無くても Head だけで動く (NearestZone がフォールバックする)
                Debug.LogWarning("[KD_DogPetController] zone anchors incomplete - Head zone only.");
            }
            if (headZoneAnchor == null)
            {
                // BUG-PET-001: 無いと Head 判定が頭蓋の内側基準に戻り、おでこが CHEEK に取られる
                Debug.LogWarning("[KD_DogPetController] headZoneAnchor not wired - Head zone falls back to headAnchor (forehead will read as CHEEK).");
            }
            // ================================================================ 破壊的監査 RC-1
            // **このクラスにだけ 0 フォールバックが 1 つも無かった。**
            // Fetch / Wander / Pack は全部持っているのに、調整値が最も多く (25 本超)
            // しかも**開発優先順位 1「犬から返事が返ってくること」の本体**であるここが素通り。
            // Verify も配線しか見ないので、**機械検査を全 PASS したまま
            // 「1 匹だけ返事をしない犬」**が成立していた。
            //
            // 規則 2 (0 で機能が死ぬ値):
            //   touchRadius 0            → 手が永久に届かない = 撫でが成立しない
            //   handValidMaxDistance 0   → 全ての手が「ロスト」扱いで無効になる
            //   followResponsiveness 0   → 1 - exp(0) = 0 で _worldDelta が永久に identity
            //                              (= **頭が一切追わない**。M2b の核心が無言で死ぬ)
            //   returnResponsiveness 0   → 手を離しても追加回転が戻らない
            //   maxFollowAngle 0         → クランプが常に 0 で追従が潰れる
            //   intensityAttack/Release 0 → 強度エンベロープが 0 に固着 (尻尾も表情も ♡ も出ない)
            //   tailWagResponsiveness 0  → 尻尾の重みが立ち上がらない
            //   strokeGrace < 0          → 撫でが 1 フレームも維持されない
            //
            // 規則 3 (大小関係が要る対):
            //   releaseRadius <= touchRadius → **毎フレーム掴み直しになり `_prevSampleValid`
            //                                  が永久に立たず、速度サンプルが取れない**
            //                                  = 撫でが二度と成立しない (FUNC-06/BUG-06)
            //   oneHandIntensity >= 1     → bigT の分母 (1 - oneHandIntensity) が 0
            //   neutralHeadDir ≒ 0        → LookTarget が未定義回転をボーンへ直接書く
            //                               (FUNC-07/BUG-02。NaN はクランプも素通りする)
            if (touchRadius <= 0.001f || handValidMaxDistance <= 0.01f
                || followResponsiveness <= 0.01f || returnResponsiveness <= 0.01f
                || maxFollowAngle <= 0.01f || intensityAttack <= 0.01f
                || intensityRelease <= 0.01f || tailWagResponsiveness <= 0.01f
                || strokeGrace < 0f)
            {
                Debug.LogWarning("[KD_DogPetController] pet tuning value is 0 - falling back to defaults.");
                if (touchRadius <= 0.001f) touchRadius = DefaultTouchRadius;
                if (handValidMaxDistance <= 0.01f) handValidMaxDistance = DefaultHandValidMaxDistance;
                if (followResponsiveness <= 0.01f) followResponsiveness = DefaultFollowResponsiveness;
                if (returnResponsiveness <= 0.01f) returnResponsiveness = DefaultReturnResponsiveness;
                if (maxFollowAngle <= 0.01f) maxFollowAngle = DefaultMaxFollowAngle;
                if (intensityAttack <= 0.01f) intensityAttack = DefaultIntensityAttack;
                if (intensityRelease <= 0.01f) intensityRelease = DefaultIntensityRelease;
                if (tailWagResponsiveness <= 0.01f) tailWagResponsiveness = DefaultTailWagResponsiveness;
                if (strokeGrace < 0f) strokeGrace = DefaultStrokeGrace;
            }
            // IMP-DOG-019 Phase 4: 寝ている犬の返事の調整値も**後から足したので既存シーンでは 0**。
            //   sleepFollowMaxAngle 0 → 寝ている間は頭が一切動かない (薄目の返事が消える)
            //   sleepTailScale 0      → 寝ている間は尻尾が振れない = **追加した返事が丸ごと無音**
            // どちらも「機械検査は全 PASS のまま返事だけ消える」型なので、0 は既定へ倒す
            if (sleepFollowMaxAngle <= 0.01f || sleepTailScale <= 0.01f)
            {
                Debug.LogWarning("[KD_DogPetController] sleep reaction tuning value is 0 - falling back to defaults.");
                if (sleepFollowMaxAngle <= 0.01f) sleepFollowMaxAngle = DefaultSleepFollowMaxAngle;
                if (sleepTailScale <= 0.01f) sleepTailScale = DefaultSleepTailScale;
            }
            if (sleepFollowMaxAngle > maxFollowAngle)
            {
                // 大小関係が要る対: 寝ている犬のほうが首が回るのは絵として破綻する
                Debug.LogWarning("[KD_DogPetController] sleepFollowMaxAngle > maxFollowAngle - "
                    + "寝ている犬の首が起きているときより回る。maxFollowAngle へ切り詰める");
                sleepFollowMaxAngle = maxFollowAngle;
            }
            if (sleepTailScale > 1f) sleepTailScale = 1f;

            if (releaseRadius <= touchRadius)
            {
                // 解除は掴みより必ず広い (境界のちらつき防止がそもそもの目的)。
                // ここを黙って通すと「掴んだ次のフレームに離す」を延々と繰り返す
                Debug.LogWarning("[KD_DogPetController] releaseRadius <= touchRadius - "
                    + "毎フレーム掴み直しになり撫でが成立しない。touchRadius の "
                    + ReleaseRadiusRatio + " 倍へ広げる");
                releaseRadius = touchRadius * ReleaseRadiusRatio;
            }
            if (oneHandIntensity <= 0f || oneHandIntensity >= 1f)
            {
                Debug.LogWarning("[KD_DogPetController] oneHandIntensity は 0 < x < 1 でなければ "
                    + "両手の上乗せ (bigT) が計算できない - 既定値へ戻す");
                oneHandIntensity = DefaultOneHandIntensity;
            }
            if (neutralHeadDir.sqrMagnitude < 0.0001f)
            {
                // ゼロベクトルだと LookTarget の FromToRotation が未定義になり、
                // その結果 (NaN を含みうる) がそのまま sourceBone.localRotation へ入る
                Debug.LogWarning("[KD_DogPetController] neutralHeadDir がゼロベクトル - 既定値へ戻す");
                neutralHeadDir = new Vector3(0f, DefaultNeutralHeadDirY, DefaultNeutralHeadDirZ);
            }

            if (tailSourceBone != null) _tailParent = tailSourceBone.parent;
            else Debug.LogWarning("[KD_DogPetController] tailSourceBone not wired - tail wag disabled.");
            if (faceRenderer == null)
            {
                Debug.LogWarning("[KD_DogPetController] faceRenderer not wired - expression disabled.");
            }
            else if (faceRenderer.sharedMesh != null)
            {
                // index の範囲外は起動時に落としておく (メッシュ差し替え等で黙ってずれた場合の保険。
                // SetBlendShapeWeight の範囲外は実行時例外 = Behaviour が halted になる)
                int shapeCount = faceRenderer.sharedMesh.blendShapeCount;
                if (metoziIndex >= shapeCount) { metoziIndex = -1; Debug.LogWarning("[KD_DogPetController] metoziIndex out of range - disabled."); }
                if (egaoIndex >= shapeCount) { egaoIndex = -1; Debug.LogWarning("[KD_DogPetController] egaoIndex out of range - disabled."); }
            }
            else
            {
                // **sharedMesh が null なら表情ごと無効化する** (破壊的監査 v2 RC-13 —
                // 前回監査 2 FUNC が独立に見つけた S がトリアージの帳簿から落ちていた分)。
                // ここを素通りすると index が正のまま残り、LateUpdate の
                // SetBlendShapeWeight が実行時例外 = **Behaviour halted (撫で全体が死ぬ)**
                metoziIndex = -1;
                egaoIndex = -1;
                Debug.LogWarning("[KD_DogPetController] faceRenderer.sharedMesh is null - expression disabled.");
            }
            dogAnimator.SetFloat("PetIndex", petClipIndex);
        }

        /// <summary>Fetch 中の抑制 (IMP-FET-001)。撫で状態を即座に落とすが、
        /// 追加回転は LateUpdate の減衰に任せる (急に戻さない)。余韻の視線も出さない
        /// (ボールに夢中の演技を守る)。</summary>
        // ================================================================ IMP-FET-005
        // 外から演技を差し込む窓口 (設計書 §10 決定 3)。**契約は 4 つ:**
        //
        //   1. 呼ぶのは `Update` から。**`LateUpdate` からは呼ばない** —
        //      Unity は「全 Update → Animator → 全 LateUpdate」なので、Update から渡せば
        //      この Behaviour の LateUpdate に必ず間に合う。Udon の Behaviour 間の
        //      LateUpdate 順序は保証されないため、そこへ依存させない
        //   2. 叩くのは KD_DogWanderController の 1 箇所だけ。
        //      催促とフェイントのどちらを見せるかの調停は向こうが持つ
        //      (**優先度が呼び出し順で決まらない**)
        //   3. 寿命はそのフレームだけ。差し込まれなければ identity へ減衰する。
        //      Fetch が始まれば徘徊層が差し込みをやめるので、**中断処理が要らない**
        //   4. **ローカルの撫でには必ず負ける** — 自分の手への返事が最優先 (不変条件 4)

        /// <summary>このフレームだけ、指定のワールド座標を見る (催促 / フェイント)。
        /// <paramref name="rollDeg"/> は首をかしげる角度 (0 で無し)。</summary>
        public void _SetGaze(Vector3 worldPos, float rollDeg)
        {
            _gazePos = worldPos;
            _gazeRoll = rollDeg;
            _gazeFrame = Time.frameCount;
        }

        /// <summary>このフレームの催促の強さ (0〜1)。尻尾がこれで振れる。
        /// **表情と ♡ には効かない** — あれは撫でへの返事なので混ぜない (設計書 §10 決定 3)。</summary>
        public void _SetUrge(float level)
        {
            _urge = level < 0f ? 0f : (level > 1f ? 1f : level);
            _urgeFrame = Time.frameCount;
        }

        public void _SuppressPetting()
        {
            if (_suppressed) return;
            _suppressed = true;
            _petting = false;
            _handHeld = false;
            _prevSampleValid = false;
            _nextContactSampleTime = 0f;
            _followHandValid = false;
            _twoHands = false;
            _otherHandIn = false;
            _twoHandsHoldUntil = -1000f;
            _prevBigJoy = false;
            _wasTouching = false;
            _wasPetting = false;
            _farewellUntil = -1000f;
            if (dogAnimator != null) dogAnimator.SetBool("Pet", false);
            _petBoolWritten = false; // キャッシュも合わせる (直書きしたので)
            _petBoolOnce = true;
            // 抑制へ入った時点で「撫でていない」を器へ渡す (IMP-NET-001)。
            // Fetch が始まったのに ♡ が残る、を構造的に防ぐ
            PushLocalPet(false, _zone, false);
            _effPetting = false;
            _effRemote = false;
        }

        public void _ResumePetting()
        {
            if (!_suppressed) return;
            _suppressed = false;
            _nextContactSampleTime = 0f;
            _prevSampleValid = false;
        }

        /// <summary>♡ を出し始める (IMP-PET-003)。ローカルの撫で成立が呼ぶほか、
        /// IMP-NET-001 (リモートの撫で表示) が同じ演出を鳴らすための発火口。</summary>
        public void _PlayPetHearts()
        {
            if (heartFx == null || _heartsPlaying) return;
            FollowHeartEmitters(); // Position before Play, including FX selection/resume outside LateUpdate.
            _heartsPlaying = true;
            heartFx.Play();
            if (heartFx2 != null) heartFx2.Play();
        }

        /// <summary>
        /// IMP-PET-006: 撫でエフェクトを差し替える。**発火口の中身は変えない** —
        /// `heartFx` / `heartFx2` が指す先だけを変える。
        ///
        /// **`SetActive` の切り替えは要らない。** ParticleSystem は `Play()` されなければ
        /// 粒を出さないので、参照を差し替えるだけで足りる
        /// (おもちゃの Mesh は常時描画なので `SetActive` が要ったが、ここは事情が違う)。
        ///
        /// **必ず今の分を止めてから差し替える** — `_heartsPlaying` は「今出している」の
        /// 印なので、出しっぱなしのまま参照を変えると**古い粒が止められなくなる**。
        ///
        /// ローカル専用なので `_` 始まり。
        /// </summary>
        public void _SelectPetFx(string productKey)
        {
            if (fxVariants == null || fxProductKeys == null) return;
            int n = fxVariants.Length;
            if (fxProductKeys.Length < n) n = fxProductKeys.Length;
            for (int i = 0; i < n; i++)
            {
                if (fxProductKeys[i] != productKey) continue;
                if (fxVariants[i] == null) return;      // 器が無い品目は無視 (今の見た目を保つ)

                bool wasPlaying = _heartsPlaying;
                if (wasPlaying) _StopPetHearts();       // 古い粒の止め手を失わない
                heartFx = fxVariants[i];
                heartFx2 = (fxVariants2 != null && i < fxVariants2.Length) ? fxVariants2[i] : null;
                _heartAccentRateMultiplier = (fxAccentRateMultipliers != null && i < fxAccentRateMultipliers.Length)
                    ? Mathf.Clamp(fxAccentRateMultipliers[i], 0f, 2f) : 0.5f;
                // **レートのキャッシュも捨てる** (破壊的監査 v2 RC-15)。残すと
                // 「変わったときだけ書く」ガードが差し替え後の新しい PS への書き込みを
                // 飛ばし、バリアントの authored 値 (Corgi で 12/s) のまま出て
                // 「片手 < 両手」の量の段階 (仕様 §7) が逆転する
                _lastHeartRate = 0f;
                if (wasPlaying) _PlayPetHearts();       // 撫でている最中の切り替えでも途切れない
                return;
            }
        }

        /// <summary>♡ を止める (出た粒は寿命まで漂って消える)。</summary>
        public void _StopPetHearts()
        {
            if (heartFx == null || !_heartsPlaying) return;
            _heartsPlaying = false;
            heartFx.Stop();
            if (heartFx2 != null) heartFx2.Stop();
        }

        /// <summary>大喜び移行のワンショット (IMP-PET-005)。バースト + 任意の SE。
        /// 連打防止は演出側だけ — 状態 (大喜び) 自体はエンベロープが継続する。
        /// IMP-NET-001 がリモートの移行を鳴らす発火口にもなる。</summary>
        public void _PlayJoyBurst()
        {
            if (Time.time - _lastJoyBurstTime < joyBurstCooldown) return;
            _lastJoyBurstTime = Time.time;
            if (heartFx != null || heartFx2 != null) FollowHeartEmitters();
            if (heartFx != null) heartFx.Emit(joyBurstCount);
            if (heartFx2 != null) heartFx2.Emit(Mathf.Min(20, Mathf.FloorToInt(joyBurstCount * _heartAccentRateMultiplier)));
            if (joyAudioSource != null && joyClip != null) joyAudioSource.PlayOneShot(joyClip);
        }

        /// <summary>ローカルの撫で状態を同期の器へ渡す (IMP-NET-001)。
        /// **変化したときだけ** — Behaviour 跨ぎの呼び出しは Udon では高い。
        /// 送信するかどうかの判断は器 (KD_DogFetchController) 側の責務。</summary>
        void PushLocalPet(bool petting, int zone, bool twoHands)
        {
            if (fetchController == null) return;
            if (petting == _pushedPetting
                && (!petting || (zone == _pushedZone && twoHands == _pushedTwoHands))) return;
            _pushedPetting = petting;
            _pushedZone = zone;
            _pushedTwoHands = twoHands;
            fetchController._SetLocalPetState(petting, zone, twoHands);
        }

        /// <summary>撫でている人の手の位置を、その人のアバターから読む (IMP-NET-001)。
        ///
        /// **手の座標は同期しない** (不変条件 5 — 本人のクライアントでしか正確に取れない)。
        /// 代わりに **VRChat がすでに同期しているアバターの IK** から、犬の頭に近いほうの手を採る。
        /// 帯域はゼロで、しかも読むのは**この画面が描いている手そのもの**なので、
        /// 犬の位置が他人の画面とずれていても「手のほうを見ている」が必ず成立する
        /// (これが座標を送るより良い理由)。
        ///
        /// 手が取れないとき (トラッキングロスト等) は頭を見る — 起票時の当初案。</summary>
        bool RemoteHandPos(VRCPlayerApi p, out Vector3 pos)
        {
            Vector3 basePos = headAnchor.position;
            Vector3 l = p.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
            Vector3 r = p.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
            bool lv = l.sqrMagnitude > 0.0001f;
            bool rv = r.sqrMagnitude > 0.0001f;
            if (lv && rv)
            {
                pos = (Vector3.Distance(l, basePos) <= Vector3.Distance(r, basePos)) ? l : r;
                return true;
            }
            if (lv) { pos = l; return true; }
            if (rv) { pos = r; return true; }
            Vector3 h = p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            if (h.sqrMagnitude > 0.0001f) { pos = h; return true; }
            pos = basePos;
            return false;
        }

        /// <summary>実効状態を決める (IMP-NET-001)。LateUpdate はこの結果だけを読む。
        ///
        /// **ローカルの撫でが成立していればローカルが勝つ** — 自分の画面では必ず
        /// 自分の手へ返事が返る (不変条件 4)。他人が同じ犬に触れていても乱れない。
        ///
        /// 出しっぱなし防止の本体は**ここで毎フレーム `Utilities.IsValid` を見ること**
        /// (`vrc-networking` §6)。撫でていた人が抜けた同期が届かなくても、
        /// 導出が落ちるので ♡ と SE は必ず止まる。</summary>
        void ResolveEffective()
        {
            bool petting = _petting && !_suppressed;
            bool two = _twoHands;
            int zone = _zone;
            Vector3 follow = _followHandPos;
            bool followValid = _followHandValid;
            bool remote = false;

            if (!petting && !_suppressed && fetchController != null)
            {
                int pid = fetchController.PetPlayerId;
                bool mine = Utilities.IsValid(_localPlayer) && pid == _localPlayer.playerId;
                if (pid >= 0 && !mine)
                {
                    VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
                    if (Utilities.IsValid(p))
                    {
                        petting = true;
                        remote = true;
                        zone = fetchController.PetZone;
                        two = fetchController.PetTwoHands;
                        followValid = RemoteHandPos(p, out follow);
                    }
                }
            }

            // 気づく導入 (仕様 §4) の起点。ローカルぶんは Update の撫で成立エッジで
            // 取っているので、ここで取るのはリモート再現の立ち上がりだけ
            if (petting && !_effWasPetting && remote) _contactStartTime = Time.time;
            _effWasPetting = petting;

            // IMP-PET-006 Phase 3: **撫で主が変わったときだけ**エフェクトを解決する。
            //
            // 「見るのは全員、出せるのは買った人だけ」を成立させる要。
            // リモートの撫でならその人の PlayerObject から選択を引き、
            // 自分の撫で / 誰も撫でていないなら自分の選択へ戻す
            // (戻さないと、他人のエフェクトのまま自分が撫でることになる)。
            //
            // **毎フレーム引かない** — Udon では走査が高い (vrc-networking §9)
            int wantPetter = (petting && remote) ? fetchController.PetPlayerId : -1;
            if (wantPetter != _fxPetterId)
            {
                _fxPetterId = wantPetter;
                if (petFxBox != null) petFxBox._ApplyFxForPetter(this, wantPetter);
            }

            _effPetting = petting;
            _effTwoHands = two;
            _effZone = zone;
            _effFollowPos = follow;
            _effFollowValid = followValid;
            _effRemote = remote;

            // 大喜び移行のワンショット (IMP-PET-005)。**ローカルとリモートで 1 本にまとめる** —
            // 2 箇所から鳴らすと、ローカル ⇄ リモートが切り替わる 1 フレームで二重に鳴る
            bool bigJoy = petting && two;
            if (bigJoy && !_prevBigJoy) _PlayJoyBurst();
            _prevBigJoy = bigJoy;

            // 変化したときだけ書く。抑制中も毎フレーム呼ばれる経路になったので、
            // ここで畳まないと Fetch 中ずっと extern 呼び出しが乗る
            if (petting != _petBoolWritten || !_petBoolOnce)
            {
                _petBoolWritten = petting;
                _petBoolOnce = true;
                if (dogAnimator != null) dogAnimator.SetBool("Pet", petting);
            }
        }

        void Update()
        {
            if (_suppressed)
            {
                // 抑制中は判定ごと止める (状態は _SuppressPetting で落とし済み)。
                // リモート再現も止まる — 実効状態は毎フレーム決め直すので、
                // 抑制が解ければそのまま復帰する
                ResolveEffective();
                return;
            }

            if (!Utilities.IsValid(_localPlayer))
            {
                // プレイヤーが無効な間は撫で状態を残さない (古い手位置への追従を防ぐ)
                _localPlayer = Networking.LocalPlayer;
                if (_petting || _handHeld)
                {
                    _petting = false;
                    _handHeld = false;
                    _prevSampleValid = false;
                    _followHandValid = false;
                    _twoHands = false;
                    _otherHandIn = false;
                    _wasTouching = false;
                    dogAnimator.SetBool("Pet", false);
                }
                // 無効化前の状態を持ち越さない (復帰直後に古い余韻・猶予が再開しないように)
                _farewellUntil = -1000f;
                _twoHandsHoldUntil = -1000f;
                _prevBigJoy = false;
                _wasPetting = false;
                _playerHeadValid = false;
                _nextContactSampleTime = 0f;
                _prevSampleValid = false;
                // 自分が無効な間もリモートの撫では見えていてよい (相手のアバターは生きている)
                PushLocalPet(false, _zone, false);
                ResolveEffective();
                return;
            }

            float now = Time.time;
            bool sampledContact = false;
            if (now >= _nextContactSampleTime)
            {
                Vector3 headPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                _playerHeadPos = headPos;
                _playerHeadValid = headPos.sqrMagnitude > 0.0001f;
                if (!_handHeld && !_petting && IsFarFromContact(headPos))
                {
                    // 遠方では頭位置だけを読み、両手の取得と接触ゾーン探索を省く。
                    // 復帰時に遠方滞在中の移動量を「撫で速度」に加えない。
                    _nextContactSampleTime = now + FarContactSampleInterval;
                    _prevSampleValid = false;
                }
                else
                {
                    _nextContactSampleTime = now + ContactSampleInterval;
                    Vector3 leftPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
                    Vector3 rightPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
                    SampleLocalContact(now, headPos, leftPos, rightPos);
                    sampledContact = true;
                }
            }

            // 判定の間も現在の手へ滑らかに追従する。速度判定用の前回座標には触らない。
            if (!sampledContact && _handHeld)
            {
                Vector3 headPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                Vector3 handPos = _localPlayer.GetTrackingData(_handIsRight
                    ? VRCPlayerApi.TrackingDataType.RightHand : VRCPlayerApi.TrackingDataType.LeftHand).position;
                RefreshFollowHand(headPos, handPos);
            }

            // リモートの手追従・退出時の解除は判定間隔の外に残す。
            PushLocalPet(_petting, _zone, _twoHands);
            ResolveEffective();
        }

        bool IsFarFromContact(Vector3 headPos)
        {
            // 頭位置が不明なら間引きを強めない。有効な手が届く範囲も除外しない。
            if (headPos.sqrMagnitude <= 0.0001f) return false;
            float distance = Mathf.Max(FarContactDistance, handValidMaxDistance + releaseRadius);
            float sq = distance * distance;
            if ((headPos - HeadZonePos()).sqrMagnitude <= sq) return false;
            if (cheekAnchorL != null && (headPos - cheekAnchorL.position).sqrMagnitude <= sq) return false;
            if (cheekAnchorR != null && (headPos - cheekAnchorR.position).sqrMagnitude <= sq) return false;
            if (chinAnchor != null && (headPos - chinAnchor.position).sqrMagnitude <= sq) return false;
            return true;
        }

        void RefreshFollowHand(Vector3 headPos, Vector3 handPos)
        {
            _playerHeadPos = headPos;
            _playerHeadValid = headPos.sqrMagnitude > 0.0001f;
            _followHandValid = handPos.sqrMagnitude > 0.0001f &&
                (!_playerHeadValid || (handPos - headPos).sqrMagnitude <= handValidMaxDistance * handValidMaxDistance);
            if (_followHandValid) _followHandPos = handPos;
            else _prevSampleValid = false; // トラッキング再取得時の速度スパイクを防ぐ。
        }

        void SampleLocalContact(float now, Vector3 headPos, Vector3 leftPos, Vector3 rightPos)
        {

            // トラッキングデータの有効性 (IMP-PET-005): 原点付近や頭から異常に離れた値は
            // ロスト/未初期化として無効扱い。誤った両手成立・顔近接エッジを踏ませない
            _playerHeadValid = headPos.sqrMagnitude > 0.0001f;
            bool leftValid = leftPos.sqrMagnitude > 0.0001f &&
                (!_playerHeadValid || Vector3.Distance(leftPos, headPos) <= handValidMaxDistance);
            bool rightValid = rightPos.sqrMagnitude > 0.0001f &&
                (!_playerHeadValid || Vector3.Distance(rightPos, headPos) <= handValidMaxDistance);
            _playerHeadPos = headPos;

            // 追従対象の手: 掴んだら releaseRadius を出るまで維持する (左右の取り合いで揺れない)
            if (_handHeld && !(_handIsRight ? rightValid : leftValid))
            {
                _handHeld = false;
                _prevSampleValid = false;
            }
            if (_handHeld)
            {
                Vector3 held = _handIsRight ? rightPos : leftPos;
                int nearZone;
                float nearDist = NearestZone(held, out nearZone);
                if (nearDist > releaseRadius)
                {
                    _handHeld = false;
                    _prevSampleValid = false;
                }
                else if (nearZone != _zone)
                {
                    // ゾーン切替はヒステリシス付き: 現ゾーンよりはっきり近いときだけ移る
                    // (境界のちらつき防止。切替後の補間は _worldDelta の Slerp が滑らかにする)
                    if (nearDist + zoneSwitchMargin < DistToZone(held, _zone)) _zone = nearZone;
                }
            }
            if (!_handHeld)
            {
                int zoneL = ZoneHead; int zoneR = ZoneHead;
                float dl = 9999f; float dr = 9999f;
                if (leftValid) dl = NearestZone(leftPos, out zoneL);
                if (rightValid) dr = NearestZone(rightPos, out zoneR);
                bool rightCloser = dr <= dl;
                float best = rightCloser ? dr : dl;
                if (best <= touchRadius)
                {
                    _handHeld = true;
                    _handIsRight = rightCloser;
                    _zone = rightCloser ? zoneR : zoneL;
                    _prevSampleValid = false;
                }
            }

            bool touching = _handHeld;
            Vector3 handPos = _handIsRight ? rightPos : leftPos;

            // 「撫で」= 接触したまま手が動いていること。接触だけでは成立しない
            float sampleElapsed = now - _previousContactSampleTime;
            if (touching && _prevSampleValid && sampleElapsed > 0f)
            {
                float speed = Vector3.Distance(handPos, _prevHandPos) / sampleElapsed;
                if (speed >= minStrokeSpeed) _lastStrokeTime = now;
            }
            _prevHandPos = handPos;
            _previousContactSampleTime = now;
            _prevSampleValid = touching;

            _petting = touching && (now - _lastStrokeTime) <= strokeGrace;
            _followHandPos = handPos;
            _followHandValid = touching;

            // ---- IMP-PET-005: 導入と余韻のエッジ ----
            // 気づく導入の起点は「撫でとして成立した」エッジ (仕様 §4 — 触れているだけでは
            // 始めない。手を置いたまま後から撫で始めたときも、そこから順序が立ち上がる)
            if (_petting && !_wasPetting) _contactStartTime = now;
            _wasPetting = _petting;
            if (!touching && _wasTouching)
            {
                // 撫でとして成立していたときだけ「最後にプレイヤーを見る」余韻を出す
                // (かすっただけでは出さない — 因果の読みやすさ)
                if (now - _lastStrokeTime <= strokeGrace + 0.5f)
                    _farewellUntil = now + farewellDuration;
            }
            _wasTouching = touching;

            // ---- IMP-PET-005: 両手判定 (同一 = ローカルプレイヤーの左右のみ) ----
            // 反対の手が「いずれかのゾーン内」にあれば両手 (別ゾーンで可 — 頭 + 背中等)。
            // 片方 (追従対象の手) は撫で/保持として成立済み。維持側にだけ猶予を掛ける
            bool otherValid = _handIsRight ? leftValid : rightValid;
            Vector3 otherPos = _handIsRight ? leftPos : rightPos;
            if (touching && otherValid)
            {
                int dummy;
                float od = NearestZone(otherPos, out dummy);
                _otherHandIn = od <= (_otherHandIn ? releaseRadius : touchRadius);
            }
            else
            {
                _otherHandIn = false;
            }
            bool twoNow = _petting && _otherHandIn;
            if (twoNow) _twoHandsHoldUntil = now + twoHandGrace;
            _twoHands = twoNow || (_petting && now < _twoHandsHoldUntil);
        }

        void LateUpdate()
        {
            if (_sourceParent == null) return;

            float dt = Time.deltaTime;

            // ---- IMP-PET-005: 強度エンベロープ (段階の芯。時間蓄積ではなく現在の接触強度へ追従) ----
            // IMP-NET-001: ここから下は実効状態 (_eff*) だけを見る。ローカルの撫でと
            // リモートの撫での再現が、同じ 1 本の経路を通る
            float intensityTarget = 0f;
            if (_effPetting) intensityTarget = _effTwoHands ? 1f : oneHandIntensity;
            float resp2 = (intensityTarget > _intensity) ? intensityAttack : intensityRelease;
            _intensity = Mathf.Lerp(_intensity, intensityTarget, 1f - Mathf.Exp(-resp2 * dt));
            // 両手ぶんの正規化係数 (片手 = 0 / 両手 = 1)。表現の「上乗せ」はこれで掛ける
            float bigT = (oneHandIntensity < 0.999f)
                ? Mathf.Clamp01((_intensity - oneHandIntensity) / (1f - oneHandIntensity))
                : 0f;

            // IMP-FET-005: 外からの差し込みは**そのフレームだけ**有効 (窓口の契約 3)。
            // 差し込みが止まれば下の鎖から自然に落ちて identity へ減衰する
            bool gazeActive = _gazeFrame == Time.frameCount;
            float urgeNow = _urgeFrame == Time.frameCount ? _urge : 0f;

            // ---- IMP-DOG-019 Phase 4: 寝ているか ----
            // 閉眼は接触が無くても維持するため、毎フレーム読む。
            // 呼び出し中の移動ではなく、寝姿勢に入った IsAsleep を全リアクションで共有する。
            bool asleep = false;
            if (wanderController != null)
            {
                asleep = wanderController.IsAsleep;
            }

            // ---- 頭の目標回転 (ゾーン別 / 注視 / 余韻) ----
            Quaternion target = Quaternion.identity;
            float resp = returnResponsiveness;
            _dbgMode = _suppressed ? "FETCH" : "IDLE";
            bool stretch = wanderController != null && wanderController.IsStretching;
            bool chinRest = companionSlot != null && companionSlot.ChinHand != 0
                && wanderController != null && wanderController.IsCompanionReady;
            if (stretch) _dbgMode = "STRETCH";
            else if (chinRest)
            {
                // One head writer: apply to the animation driver before the two-tier rig constraints.
                // The dog stays on its selected spot; only the head follows within a small neck arc.
                _dbgMode = "CHIN_REST"; resp = followResponsiveness;
                var caller = VRCPlayerApi.GetPlayerById(companionSlot.LiveSleeperId);
                int handId = companionSlot.ChinHand;
                if (Utilities.IsValid(caller) && caller.IsUserInVR() && handId >= 1 && handId <= 2 && chinRestVector.sqrMagnitude > .00001f)
                {
                    Vector3 hand = caller.GetTrackingData(handId == 1 ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand).position;
                    Vector3 playerHead = caller.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                    Vector3 toward = hand - headAnchor.position;
                    if (hand.sqrMagnitude > .0001f && Vector3.Distance(hand, playerHead) < 2.5f
                        && Vector3.Distance(hand, dogRoot.TransformPoint(chinRestTarget)) <= chinRestRadius * 1.5f && toward.sqrMagnitude > .00001f)
                    {
                        Quaternion basePose = sourceBone.localRotation;
                        if (_hasLast && Mathf.Abs(Quaternion.Dot(basePose, _lastWritten)) > PoseSameDot) basePose = _lastBase;
                        Vector3 from = (_sourceParent.rotation * basePose) * chinRestVector;
                        target = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(from, toward), 25f);
                    }
                }
            }
            else if (_effPetting && _effFollowValid)
            {
                Quaternion dogRot = dogRoot.rotation;
                Vector3 dirLocal = Quaternion.Inverse(dogRot) * (_effFollowPos - headAnchor.position);
                if (dirLocal.sqrMagnitude > 0.0001f)
                {
                    // 寝ている間は追いの速さも上げない (returnResponsiveness のまま) —
                    // 手に即応する首は「起きている犬」の動きで、寝ている返事にならない
                    resp = asleep ? returnResponsiveness : followResponsiveness;
                    if (asleep)
                    {
                        // IMP-DOG-019 Phase 4: 横倒しのまま、手の方へ鼻先を寄せるだけ。
                        // **ゾーン別の到達ポーズ (頬すり / 顎上げ) はここでは使わない** —
                        // あれは dogRoot ローカルのオイラー角で「立っている犬の頬を手へ倒す」
                        // 形に書いてあり、45° 傾いた首へ足すと捻れる (Play で戻せない絵になる)。
                        // ♡ と撫でエフェクトは通常どおり出るので、返事が消えるわけではない
                        target = LookTarget(_effFollowPos, sleepFollowMaxAngle, true);
                        _dbgMode = "SLEEP"; // "R:" は下で付く (リモートの撫での再現)
                    }
                    else if (_effZone == ZoneCheekL || _effZone == ZoneCheekR)
                    {
                        // Cheek: 手の位置は追わず、手のある側へ頬を傾けて押しつける (§13「犬側から接触を返す」)
                        float side = (_effZone == ZoneCheekR) ? 1f : -1f; // 犬ローカル +X = 右
                        Quaternion deltaLocal = Quaternion.Euler(0f, side * cheekYawDeg, -side * cheekRollDeg);
                        target = dogRot * deltaLocal * Quaternion.Inverse(dogRot);
                        _dbgMode = (_effZone == ZoneCheekR) ? "CHEEK_R" : "CHEEK_L";
                    }
                    else if (_effZone == ZoneChin)
                    {
                        _dbgMode = "CHIN";
                        // Chin: 顔を上へ向けて首元を預ける到達ポーズへ補間 (手の位置は追わない —
                        // 追うと VR 第 1 回で実測した「首が変に曲がる」になる)
                        float yawSide = Mathf.Clamp(dirLocal.x * 4f, -1f, 1f);
                        Quaternion deltaLocal = Quaternion.Euler(-chinPitchUpDeg, yawSide * chinYawDeg, 0f);
                        target = dogRot * deltaLocal * Quaternion.Inverse(dogRot);
                    }
                    else
                    {
                        // Head: 手を追う (M2b の完成形)。下方向はピッチ上限で制限する
                        target = LookTarget(_effFollowPos, maxFollowAngle, true);
                        _dbgMode = "HEAD";
                    }
                    if (_effRemote) _dbgMode = "R:" + _dbgMode; // 他人の撫での再現 (IMP-NET-001)
                }
            }
            else if (gazeActive && !asleep)
            {
                // 寝ている犬へは差し込みを通さない (IMP-DOG-019 Phase 4)。徘徊層は寝てからは
                // 差し込まない作りだが、**片方だけの取り決めは黙って破れる**ので受け手でも閉じる —
                // 通すと「寝ているのにボールを目で追う犬」になり、占有が読めなくなる
                // IMP-FET-005: 催促 / フェイント。**撫でには必ず負ける** (この else if の位置が
                // その保証)。首をかしげるロールは頬すり (cheekRollDeg) と同じ作り方
                resp = gazeResponsiveness;
                target = LookTarget(_gazePos, maxFollowAngle, false);
                if (_gazeRoll > 0.01f || _gazeRoll < -0.01f)
                {
                    Quaternion dogRot2 = dogRoot.rotation;
                    Quaternion rollLocal = Quaternion.Euler(0f, 0f, -_gazeRoll);
                    target = (dogRot2 * rollLocal * Quaternion.Inverse(dogRot2)) * target;
                }
                _dbgMode = "GAZE";
            }
            else if (!_suppressed && !asleep && _playerHeadValid && Time.time < _farewellUntil)
            {
                // 余韻: 減衰しながら最後にプレイヤーを見る (仕様 §10 — 急に無表情へ戻さない)。
                // **寝ている犬は出さない** (IMP-DOG-019 Phase 4) — 手が離れたら顔を上げて
                // 見送る、は起きている犬の返事。寝ている犬はそのまま寝ているのが返事で、
                // 続きは寝息が持つ
                target = LookTarget(_playerHeadPos, farewellMaxAngle, false);
                _dbgMode = "FAREWELL";
            }

            // アイドル時はボーンへ触らない (Udon VM は遅い — 目標も現在値も identity なら
            // 書き込みごとスキップし、Animator にボーンを返す。再開時はガードの基準を取り直す)
            bool headActive = Quaternion.Angle(target, Quaternion.identity) > 0.01f ||
                              Quaternion.Angle(_worldDelta, Quaternion.identity) > 0.02f;
            if (headActive)
            {
                // 補間の遅れが「犬が追ってきている」感覚を作る (瞬間追従にしない)
                float t = 1f - Mathf.Exp(-resp * dt);
                _worldDelta = Quaternion.Slerp(_worldDelta, target, t);

                // 蓄積防止ガード (§4 手順 5): Animator が書き戻していないフレームでは
                // 自分の書き込みを土台にせず、前フレームの基準姿勢を使う
                Quaternion pose = sourceBone.localRotation;
                if (_hasLast && Mathf.Abs(Quaternion.Dot(pose, _lastWritten)) > PoseSameDot)
                {
                    pose = _lastBase;
                }
                _lastBase = pose;

                // ワールド差分をボーンローカルの追加回転へ変換して、Animator の出力に乗せる
                Quaternion worldPose = _sourceParent.rotation * pose;
                Quaternion additiveLocal = Quaternion.Inverse(worldPose) * _worldDelta * worldPose;
                Quaternion written = pose * additiveLocal;
                sourceBone.localRotation = written;
                _lastWritten = written;
                _hasLast = true;
            }
            else
            {
                _worldDelta = Quaternion.identity;
                _hasLast = false;
            }

            // ---- 撫でハート (IMP-PET-003 / 005) ----
            // ボーンの子にできない (PhysBone チェーンが切れる — 設計書 §3) ため、
            // 位置だけ毎フレーム追従させる
            if (heartFx != null)
            {
                // World-space particles retain their trajectory after emission stops.
                if (_effPetting || _heartsPlaying) FollowHeartEmitters();
                // ON/OFF は**同期変数からの導出**で決まる (IMP-NET-001)。
                // `_PlayPetHearts` を SendCustomNetworkEvent で叩く形にすると、
                // Stop の取りこぼしで出しっぱなしになり Late Joiner にも届かない
                // (vrc-networking §6)。呼び出し元を変えただけで、発火口はそのまま
                if (_effPetting && !_heartsPlaying) _PlayPetHearts();
                else if (!_effPetting && _heartsPlaying) _StopPetHearts();

                // 段階が量で読めるように RateOverTime を切り替える (片手 2 → 両手 6。
                // ユーザー指定 2026-08-12)。変わったときだけ書く
                float rate = 0f;
                if (_effPetting) rate = _effTwoHands ? heartRateTwoHands : heartRateOneHand;
                if (rate > 0f && rate != _lastHeartRate)
                {
                    _lastHeartRate = rate;
                    var em = heartFx.emission;
                    em.rateOverTime = rate;
                    if (heartFx2 != null)
                    {
                        var em2 = heartFx2.emission;
                        em2.rateOverTime = Mathf.Min(16f, rate * _heartAccentRateMultiplier);
                    }
                }
            }

            // ---- 部位別エンベロープ (気づく導入 = 接触開始からの部位別ディレイ。仕様 §4:
            // 視線 (頭・即時) → 尻尾 → 頭寄せ (追従補間の遅れが担う) の順。
            // 耳は Udon から動かさない = 導入の順序にも入らない) ----
            float sinceContact = Time.time - _contactStartTime;
            bool tailOn = _effPetting && sinceContact >= tailStartDelay;
            float envT = 1f - Mathf.Exp(-tailWagResponsiveness * dt);
            _tailWeight = Mathf.Lerp(_tailWeight, tailOn ? 1f : 0f, envT);
            // IMP-FET-005: 催促は**尻尾だけ**を動かす。`_intensity` へ混ぜると
            // 両手撫で用の egao が催促で出るのに tailOn は false のまま、という
            // ちぐはぐが起きる (設計書 §10 決定 3)
            _urgeWeight = Mathf.Lerp(_urgeWeight, urgeNow, envT);
            float wagWeight = _tailWeight > _urgeWeight ? _tailWeight : _urgeWeight;

            // ---- 尻尾 (IMP-PET-004 / 005): ヨー往復 + 撫で中のベース角ピッチ ----
            // なで1 は尻尾ボーンのカーブを持たない = 撫で中は Animator が書き戻さないため、
            // 頭と同じ蓄積防止ガードを尻尾用の状態で必ず通す
            if (_tailParent != null && (tailOn || wagWeight > 0.001f))
            {
                // IMP-DOG-019 Phase 4: 寝ている間は「寝ぼけた振り」へ落とす。
                // **振り自体は既存の器がそのまま乗る** — 振れ軸が dogRoot.up (ワールド上向き)
                // なので、横倒しの犬では尻尾が床を掃く動きになる = 「起き上がらずに
                // 尻尾だけ振る」(DES-DOG-003 §4) がそのまま絵になる。
                // 全開で振ると起きている犬と区別が付かないので、振幅・速さ・持ち上げを一律に絞る
                float sleepWag = asleep ? sleepTailScale : 1f;
                float freq = tailWagFrequency * Mathf.Lerp(1f, twoHandTailFreqScale, bigT) * sleepWag;
                _tailPhase += dt * freq * 2f * Mathf.PI;
                if (_tailPhase > 2f * Mathf.PI) _tailPhase -= 2f * Mathf.PI;
                // 片手 = 従来の振幅そのまま (PCVR 確定値を守る)。両手で振幅・速度を上乗せ。
                // 催促のぶんは wagWeight として同じ器に入る (bigT は撫で専用なので効かない)
                float wagDeg = Mathf.Sin(_tailPhase) * tailWagAmplitudeDeg * wagWeight
                    * Mathf.Lerp(1f, twoHandTailAmpScale, bigT) * sleepWag;
                // やや上向き (真上までは立てない)。寝ているときはさらに寝かせる —
                // 床に着いた尻尾を持ち上げると「起きようとしている」に見える
                float pitchDeg = tailBasePitchDeg * wagWeight * sleepWag;
                Quaternion wagWorld = Quaternion.AngleAxis(wagDeg, dogRoot.up)
                    * Quaternion.AngleAxis(pitchDeg, dogRoot.right);

                Quaternion tailPose = tailSourceBone.localRotation;
                if (_tailHasLast && Mathf.Abs(Quaternion.Dot(tailPose, _tailLastWritten)) > PoseSameDot)
                {
                    tailPose = _tailLastBase;
                }
                _tailLastBase = tailPose;
                Quaternion tailWorldPose = _tailParent.rotation * tailPose;
                Quaternion tailAdditive = Quaternion.Inverse(tailWorldPose) * wagWorld * tailWorldPose;
                Quaternion tailWritten = tailPose * tailAdditive;
                tailSourceBone.localRotation = tailWritten;
                _tailLastWritten = tailWritten;
                _tailHasLast = true;
            }
            else
            {
                // アイドル: 減衰し切ったらボーンへ触らない (Animator へ返す)。
                // 次の撫でを常に中央 (振れ角 0) + 取り直した基準から始める
                _tailPhase = 0f;
                _tailHasLast = false;
            }

            // 耳へは何も書かない (2026-08-12 決定)。PhysBone が手との接触で揺らす

            // ---- 表情 (IMP-PET-005): metozi = 目を細める / egao = 両手の笑顔。
            // LateUpdate 書きで Animator より後になる見込み (Phase 2 の PCVR 確認項目 —
            // ボーン実行順の実測をそのまま BlendShape に広げない)。
            // 睡眠中は撫での強度に関係なく閉眼。起きていて撫でてもいないときは
            // 最後の書き込みを解除し、クリップへ表情を返す ----
            if (faceRenderer != null && !stretch && !chinRest)
            {
                float metozi = asleep ? 100f : metoziMax * _intensity;
                if (metozi > 0.5f)
                {
                    if (metoziIndex >= 0) faceRenderer.SetBlendShapeWeight(metoziIndex, Mathf.Min(metozi, 100f));
                    _metoziWriting = true;
                }
                else if (_metoziWriting)
                {
                    if (metoziIndex >= 0) faceRenderer.SetBlendShapeWeight(metoziIndex, 0f);
                    _metoziWriting = false;
                }

                // 笑顔の目と閉眼を重ねない。寝たままの撫でには頭・尻尾・ハートで返事する。
                float egao = asleep ? 0f : egaoMax * bigT;
                if (egao > 0.5f)
                {
                    if (egaoIndex >= 0) faceRenderer.SetBlendShapeWeight(egaoIndex, Mathf.Min(egao, 100f));
                    _egaoWriting = true;
                }
                else if (_egaoWriting)
                {
                    if (egaoIndex >= 0) faceRenderer.SetBlendShapeWeight(egaoIndex, 0f);
                    _egaoWriting = false;
                }
            }

            // ---- デバッグ表示 (IMP-PET-005。配線されているときだけ。0.25s 間隔で組む) ----
            if (debugText != null && Time.time >= _nextDebugTime)
            {
                _nextDebugTime = Time.time + 0.25f;
                string hands = !_handHeld ? "NONE" : (_twoHands ? "TWO" : "ONE");
                // IMP-NET-001: net= は同期側の状態 (誰が撫でているか)。
                // **自分の hands/pet と食い違っていてよい** — 自分の画面は自分の撫でを優先する
                string net = "-";
                if (fetchController != null)
                {
                    int pid = fetchController.PetPlayerId;
                    net = pid < 0 ? "none" : ("p" + pid + (fetchController.PetTwoHands ? " TWO" : " ONE"));
                }
                debugText.text = "hands=" + hands + " pet=" + (_petting ? "Y" : "N")
                    + "\nmode=" + _dbgMode + " int=" + _intensity.ToString("F2")
                    + "\ntailW=" + _tailWeight.ToString("F2")
                    + " rate=" + _lastHeartRate.ToString("F0")
                    + "\nnet=" + net;
                if (debugTextTransform != null)
                {
                    Vector3 dp = dogRoot.position + Vector3.up * debugTextHeight;
                    debugTextTransform.position = dp;
                    if (_playerHeadValid)
                        debugTextTransform.rotation = Quaternion.LookRotation(dp - _playerHeadPos);
                }
            }
        }

        void FollowHeartEmitters()
        {
            Vector3 pos = headAnchor.position + Vector3.up * heartOffsetY;
            if (heartFx != null) heartFx.transform.position = pos;
            if (heartFx2 != null) heartFx2.transform.position = pos;
        }

        /// <summary>headAnchor から worldPos の方向へ頭を向けるワールド差分回転。
        /// maxAngle でクランプし、limitDownPitch なら下向きだけピッチ上限で制限する
        /// (手が下にあるとき首が変に曲がる対策 — VR 第 1 回)。</summary>
        Quaternion LookTarget(Vector3 worldPos, float maxAngle, bool limitDownPitch)
        {
            Quaternion dogRot = dogRoot.rotation;
            Vector3 dirLocal = Quaternion.Inverse(dogRot) * (worldPos - headAnchor.position);
            if (dirLocal.sqrMagnitude <= 0.0001f) return Quaternion.identity;
            if (limitDownPitch && dirLocal.y < 0f)
            {
                float horiz = Mathf.Sqrt(dirLocal.x * dirLocal.x + dirLocal.z * dirLocal.z);
                if (horiz < 0.01f)
                {
                    // 手がほぼ真下: 水平成分が消えると方向が潰れて identity へ
                    // 戻ってしまうため、前方へ最小の水平成分を足してから制限する
                    dirLocal.x = 0f;
                    dirLocal.z = 0.01f;
                    horiz = 0.01f;
                }
                float maxDrop = horiz * Mathf.Tan(maxDownPitch * Mathf.Deg2Rad);
                if (-dirLocal.y > maxDrop) dirLocal.y = -maxDrop;
            }
            Vector3 dir = dogRot * dirLocal;
            Vector3 neutral = dogRot * neutralHeadDir.normalized;
            Quaternion full = Quaternion.FromToRotation(neutral, dir.normalized);
            // 可動範囲でクランプ。超えたら上限で止まる = 無理な角度で追わない
            float ang = Quaternion.Angle(Quaternion.identity, full);
            return (ang > maxAngle)
                ? Quaternion.Slerp(Quaternion.identity, full, maxAngle / ang)
                : full;
        }

        /// <summary>Head ゾーンの判定点。追従ピボット (headAnchor) とは別 (BUG-PET-001)。</summary>
        Vector3 HeadZonePos()
        {
            return (headZoneAnchor != null) ? headZoneAnchor.position : headAnchor.position;
        }

        /// <summary>最も近いゾーンとその距離。未配線のアンカーは無視する (Head へフォールバック)。</summary>
        float NearestZone(Vector3 pos, out int zone)
        {
            zone = ZoneHead;
            float best = Vector3.Distance(pos, HeadZonePos());
            if (cheekAnchorL != null)
            {
                float d = Vector3.Distance(pos, cheekAnchorL.position);
                if (d < best) { best = d; zone = ZoneCheekL; }
            }
            if (cheekAnchorR != null)
            {
                float d = Vector3.Distance(pos, cheekAnchorR.position);
                if (d < best) { best = d; zone = ZoneCheekR; }
            }
            if (chinAnchor != null)
            {
                float d = Vector3.Distance(pos, chinAnchor.position);
                if (d < best) { best = d; zone = ZoneChin; }
            }
            return best;
        }

        float DistToZone(Vector3 pos, int zone)
        {
            if (zone == ZoneCheekL && cheekAnchorL != null) return Vector3.Distance(pos, cheekAnchorL.position);
            if (zone == ZoneCheekR && cheekAnchorR != null) return Vector3.Distance(pos, cheekAnchorR.position);
            if (zone == ZoneChin && chinAnchor != null) return Vector3.Distance(pos, chinAnchor.position);
            return Vector3.Distance(pos, HeadZonePos());
        }
    }
}
