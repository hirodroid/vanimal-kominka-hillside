using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-CAM-001 / IMP-CAM-002: 「いつ・**誰を**・どこから撮るか」の決定
    /// (このワールドの撮影の本体)。
    /// 撮影はコミュニケーションを中断させない — 遊んでいたらいい瞬間が生まれていて、
    /// それをシステムが拾う (core-concept §19)。
    ///
    /// ## 撮る瞬間 4 つ (IMP-CAM-002 で 3 → 4)
    ///
    /// 1. **受け渡し** — 犬が完全に止まってボールを持って待っている絵 (dogState=Drop)。
    ///    起点は同期済みの dogState なので全クライアントが各自ローカルで撮る
    /// 2. **撫で** — 撫で成立が一定時間続いたらバースト 4 枚。撫で判定はローカルなので
    ///    撮れるのは本人だけ (自分のアルバムとして正しい)
    /// 3. **かけっこ** — Fetch の追跡、または徘徊の走り出し
    /// 4. **じゃれ合い** — 犬同士が遊んでいるところ (IMP-DOG-009 / 012)。
    ///    **IMP-CAM-002 で追加した。** 起票時は「撮る瞬間は変えない」方針だったが、
    ///    IMP-DOG-012 でじゃれ合いが 18〜28 秒・最大 4 匹になり、
    ///    ユーザーが唯一「見ていて楽しい」と名指しした出来事になった。
    ///    それが 1 枚も写らないアルバムは、このワールドの記録になっていない
    ///
    /// ## 多頭 (IMP-CAM-002)
    ///
    /// **Director は 1 個のまま。** 犬ごとに複製すると、クールダウン・スロット・
    /// 公平性が犬の数だけ分裂して「アルバム全体をどう配分するか」を誰も持たなくなる。
    /// 代わりに **犬を番号で持つ** — 配列の並びは <c>KD_DogDefs.All</c> =
    /// <c>KD_DogPack</c> の名簿と同じで、通知側 (<see cref="_OnDogDrop"/> 等) は
    /// 自分の <c>dogIndex</c> をそのまま渡す。番号が名簿と共通なので、
    /// 「fetch は犬 A・pet は犬 B」の取り違えを Verify が機械検査できる。
    ///
    /// **遅延撮影の確認先も犬ごと**でなければならない — 撮影直前に
    /// 「まだ走っているか」を見る先が固定だと、**ダックスの走りをコーギーの状態で
    /// 判定する**ことになる。
    ///
    /// ## 誰が撮られるか = 公平性 (IMP-CAM-002 決めたこと 4)
    ///
    /// クールダウンは全犬共通のまま (連写過多の抑制が目的)。**ただし競合したら
    /// 直近に撮られていない犬を採る。** 「先に来たイベントが勝つ」だとイベント順が
    /// 実質**名簿順に寄り**、アルバムがコーギーだらけになる。
    /// 予約は上書きしない — 4 匹が同時に走り出しても、1 フレーム 1 枚ずつ順に捌ける
    /// (カメラは 1 フレームに 1 枚しか撮れないため、待たせても数フレーム)。
    ///
    /// すべてローカル (BehaviourSyncMode.None、同期変数ゼロ — KanjiFind NET-01 思想)。
    /// カメラの cullingMask は PlayerLocal(10) ではなく MirrorReflection(18) で自分を写す
    /// (PlayerLocal だと自分が被写体のとき顔が欠ける — KanjiFind IMP-SC-003 §3.5 実測)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_PhotoDirector : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/Photo System が設定する)")]
        public KD_PhotoCapture photoCapture;
        public Camera photoCamera;
        public KD_GalleryController gallery;
        public KD_BedroomPhotoDirector bedroom;

        // ---- 犬 (IMP-CAM-002)。**5 本とも KD_DogDefs.All と同じ並び** ----
        // 番号は KD_DogPack の dogIndex と共通で、通知側がそのまま渡してくる。
        // 並びがずれると「ポメのイベントでラブを撮る」という、
        // Play では「なんか変」としか見えない壊れ方をする → Verify が機械検査する
        [Header("犬 (KD_DogDefs.All と同じ並び = KD_DogPack.dogIndex と同じ番号)")]
        public Transform[] dogRoots;
        [Tooltip("犬ごとの頭アンカー (KD_PetAnchor_Head を流用)。構図の基準であり、" +
            "体格 (= 距離の逆算) の実測点でもある")]
        public Transform[] dogHeadAnchors;
        [Tooltip("犬ごとの Fetch。受け渡しの相手と「まだ走っているか」を引く")]
        public KD_DogFetchController[] fetchControllers;
        [Tooltip("犬ごとの撫で。「今この犬が撫でられているか」を毎フレーム見る")]
        public KD_DogPetController[] petControllers;
        [Tooltip("犬ごとの徘徊。徘徊の走りと じゃれ合いの継続を引く。" +
            "配線は Wander System Setup が行う (作られる順の都合)")]
        public KD_DogWanderController[] wanderControllers;

        [Header("受け渡しの一枚")]
        [Tooltip("Drop に入ってから撮るまでの間 (完全に止まって待っている絵にする)")]
        public float dropShotDelay = 0.4f;
        // **旧 dropShotHeight (= 犬の頭からのカメラ高さ 0.7m) から改名した。**
        // IMP-CAM-002 で受け渡しに俯瞰を足したとき意味が「持ち上げの強さ」に変わったので、
        // 名前を変えて**シリアライズ済みの旧値を引き継がせない** — 同じ名前のままだと
        // Setup が調整値を保持する方針のせいで 0.7 が残り、
        // 「コードの既定を直したのに絵が変わらない」という気づけないずれになる
        [Tooltip("受け渡しの俯瞰でカメラを持ち上げる強さ (被写体からの持ち上げ)")]
        public float dropDroneHeight = 1.6f;

        [Header("撫でバースト (毎回 4 枚。構図は 9 種のプールから回す)")]
        [Tooltip("撫でがこの秒数続いたらバースト開始")]
        public float petShotAfterSec = 3f;
        [Tooltip("バーストの間隔 (秒)。1 回で 4 スロット使う")]
        public float petShotCooldown = 30f;
        [Tooltip("ドローンの高さ (被写体からの持ち上げ)")]
        public float petDroneHeight = 1.8f;

        // ================================================================ IMP-CAM-003
        // 「いい写真」の基準をここで決める。
        //
        // **距離を固定にしない。** 犬種で体高が倍近く違うので (ポメ / コーギー / ラブ)、
        // 固定距離では小型犬が豆粒になり大型犬が枠から溢れる。
        // **画面の縦を犬が何割占めるか (fill) を指定し、距離はそこから逆算する。**
        // これで構図の意図 (寄り / 標準 / 引き) が犬種に依存しなくなる。
        //
        // **枚数は増やさず絵の種類を増やす。** 撮影頻度を上げると、たまたま撮れた
        // 良い 1 枚が上書きで流れやすくなる。だからバーストは 4 枚のままにして、
        // 9 種のプールから毎回 4 つを回す。
        // **4 と 9 は互いに素**なのでバーストを重ねると全構図を巡る
        // (IMP-CAM-002 で 7 → 9 に増やしたが、互いに素の性質は保っている —
        //  8 にすると偶数番の構図しか出ない回ができる)。
        [Header("フレーミング (IMP-CAM-003 —「いい写真」の基準)")]
        [Tooltip("寄り: 犬 (の頭) が画面の縦を占める割合")]
        public float fillCloseUp = 0.62f;
        [Tooltip("標準: 犬が画面の縦を占める割合")]
        public float fillNormal = 0.4f;
        [Tooltip("引き: 犬が画面の縦を占める割合 (庭ごと入れる)")]
        public float fillWide = 0.16f;
        [Tooltip("角度の揺らぎ (度)。同じトリガーでも毎回同じ絵にしない")]
        public float angleJitter = 18f;
        [Tooltip("寄り引きの揺らぎ (割合)")]
        public float fillJitter = 0.12f;
        [Tooltip("カメラが壁・地面へめり込まないための余白 (m)")]
        public float cameraClearance = 0.18f;
        // **IMP-CAM-002 で追加。** じゃれ合いは 2 匹が 6m 離れて走るので、
        // 引き (fillWide) の逆算がそのままだと **約 50m 先**になり、
        // 古民家どころか世界の外から撮ることになる (遮蔽レイが引き戻すとしても、
        // 空へ向いた方向では戻らない)。**被写体が少しはみ出す方が、世界の外から撮るよりよい。**
        // 1 匹の引き 2.7m / 受け渡しの引き 8m はどれもこの下なので、既存の絵は変わらない
        [Tooltip("カメラが被写体から離れる上限 (m)。庭ごとの引きでもこれ以上は下がらない")]
        public float maxFrameDistance = 12f;
        // **LayerMask ではなく int** で持つ。Udon で LayerMask → int の暗黙変換に
        // 頼りたくないため (調整の正本は Setup 側の定数なので Inspector の
        // レイヤードロップダウンは要らない)
        [Tooltip("カメラを遮る地形のレイヤーマスク (Default + Environment)。プレイヤーと犬は入れない")]
        public int cameraBlockMask = 1;

        // 撫での構図プール (IMP-CAM-002 で 7 → 9)
        const int PetShotCount = 9;
        const int PetBurstLen = 4;
        const int ShotDogEye = 0;      // 犬視点・至近 (被写体はプレイヤー)
        const int ShotCloseUp = 1;     // 寄り (顔だけ)
        const int ShotFront = 2;       // 正面 (撫でている顔と犬)
        const int ShotBack = 3;        // 肩越し
        const int ShotDrone = 4;       // 俯瞰
        const int ShotLowAngle = 5;    // ローアングル (犬の目線の高さから見上げる)
        const int ShotWide = 6;        // 引き (庭ごと)
        const int ShotDogEyeOver = 7;  // 犬視点・引き (犬の頭ごしにプレイヤー)
        const int ShotProfile = 8;     // 真横 (手と犬の横顔)

        // ================================================================ スロットのカテゴリ
        // **IMP-CAM-002 で「構図」から「出来事」へ切り直した (2026-08-16 ユーザー決定)。**
        //
        // IMP-CAM-003 は「寄り / 目線 / 俯瞰 / ...」= 構図でカテゴリを切っていた。
        // 理由 (同じ絵で埋まらない) は正しかったが、**列に「この列は何が撮られるか」を
        // 書けない** — 「俯瞰」の列にはあらゆる出来事の俯瞰が混ざるため。
        // 出来事で切り直すと列にラベルが付けられる (物理レイアウトは IMP-GAL-004)。
        //
        // **切り直すと「同じ構図 4 枚で埋まる」危険が出来事側へ移る。**
        // → 各列に最低 2 構図を用意し、列の中で回す:
        //   受け渡し 4 / 撫で 6 / 犬視点 2 / かけっこ 2 / じゃれ合い 2 /
        //   庭ごと 4 (各出来事の引きが集まる)
        //
        // **壁に貼る文字は KD_PhotoSetupEditor.SlotGroupNames が正本**で、
        // ここの名前とは一致しない (「もってきた」「犬目線」)。
        // **コード側は仕組みを指す語のままにしてある** — GroupHandoff は Fetch の Drop
        // に対応する出来事で、表示の言い回しを変えるたびに定数名まで動かさない
        // **public なのは KD_PhotoSetupEditor (SlotGroupNames / ギャラリーの列構成) が
        // 同じ番号を読むため** (IMP-INF-001 / D — FleeTurnRad と同じ手法)。
        // Editor 側に 6 を書き写すと、カテゴリを増やしたとき片方だけ直して
        // 「列とスロットの対応が黙ってずれる」
        //
        // **IMP-CAM-005 (2026-09-12 ユーザー決定): 「自分が写る写真」を主役にする。**
        // 犬目線＋かけっこ、庭ごと＋じゃれ合いをそれぞれ 1 カテゴリへ畳み、
        // 空いた枠を もってきた / 撫で (どちらも自分が写る) へ回す。
        // 枠数は 8 / 8 / 4 / 4 (列 = 2 / 2 / 1 / 1。KD_PhotoSetupEditor.ColumnGroups が正本)。
        // 各 Shoot の振り分けは変えず、番号だけを畳んでいる — 定数名は出来事のまま残す。
        public const int GroupHandoff = 0;  // 受け渡し (表示「もってきた」) — 待っている犬と、投げた人
        public const int GroupPet = 1;      // 撫で                          — 撫でている手と犬
        public const int GroupDogEye = 2;   // 犬視点 (表示「犬目線」)       — 犬の目線から見上げた、あなた
        public const int GroupChase = 2;    // かけっこ (犬目線と同じ列)     — ボールを追って走る犬
        public const int GroupPlay = 3;     // じゃれ合い (庭ごとと同じ列)   — 犬同士が遊んでいるところ
        public const int GroupWide = 3;     // 庭ごと                        — 古民家と庭ごとの引き

        /// <summary>カテゴリ数。KD_GalleryController.slotGroupCount と Editor の
        /// SlotGroupNames.Length はこの値と一致しなければならない (Setup が配り、Verify が見る)。</summary>
        public const int GroupCount = 4;

        [Header("走る犬 (Fetch / 徘徊)")]
        public bool chaseShotEnabled = true;
        [Tooltip("追跡開始からの遅れ (走りが乗ってから)")]
        public float chaseShotDelay = 0.6f;
        [Tooltip("連写しない間隔 (秒)。**全犬共通** — 誰が撮られるかは公平性が決める")]
        public float chaseShotCooldown = 45f;
        [Tooltip("追跡ドローンの高さ (被写体からの持ち上げ)")]
        public float chaseShotHeight = 1.2f;

        [Header("じゃれ合い (IMP-CAM-002 — 4 つ目の撮る瞬間)")]
        public bool playShotEnabled = true;
        [Tooltip("じゃれ合い開始からの遅れ (秒)。**長め** — 走り出した直後は 2 匹が離れていて、" +
            "「じゃれ合っている絵」にならない。追いかけっこが噛み合うまで待つ")]
        public float playShotDelay = 2.5f;
        [Tooltip("連写しない間隔 (秒)。じゃれ合いは 18〜28 秒続くので、1 回の窓で数枚に収める")]
        public float playShotCooldown = 25f;
        [Tooltip("相手がこの距離より遠ければ「じゃれ合っている絵」にならないので諦める (m)")]
        public float playPairMaxDistance = 6f;

        [Header("予約 (IMP-CAM-002 — 4 匹が同時に来ても上書きしない)")]
        [Tooltip("撮り頃を過ぎた予約を捨てるまでの猶予 (秒)。" +
            "**予約は上書きせず順に捌く**が、待たせすぎるとその瞬間ではなくなる")]
        public float shotPendingLifetime = 1.5f;

        // 予約の種類 (RipeKind の戻り値)
        const int KindNone = 0;
        const int KindDrop = 1;
        const int KindPlay = 2;
        const int KindChase = 3;

        int _n;                    // 犬の数 (Start で確定)
        float[] _lastShotTime;     // **公平性の本体** — 犬ごとの最終撮影時刻
        bool[] _dropPending;
        float[] _dropTime;
        int[] _dropPlayerId;
        bool[] _chasePending;
        float[] _chaseTime;
        bool[] _playPending;
        float[] _playTime;
        float[] _petAccum;         // 犬ごとの撫で継続時間

        float _lastPetShot;
        float _lastChaseShot;
        float _lastPlayShot;
        int _burstStep = -1; // 撫でバーストの進行 (-1 = 停止中)。0..3 = バースト内の何枚目か
        int _burstRetries;
        int _burstOffset;    // バーストごとに構図プールの開始位置をずらす
        int _burstDog = -1;  // **バーストの被写体。開始時に捕まえて離さない** (下記)
        int _chaseVariant;   // かけっこの構図を回す
        int _dropVariant;    // 受け渡しの構図を回す
        int _playVariant;    // じゃれ合いの構図を回す

        const int DropVariantCount = 4;
        const int ChaseVariantCount = 3;
        const int PlayVariantCount = 3;

        // ================================================================ 裏山改変 (IMP-WLD-005)
        // 1) **毎フレームの見回りを間引く** (IMP-DOG-034 / 036 の方針: 常時動くものを増やさない)。
        //    予約の判定と撫での積算は tickInterval ごと (既定 0.1 s = 10 Hz)。撫での積算は実時間の差を足すので 3 秒の閾値は変わらない。
        //    受け渡し 0.4 s・走り 0.6 s・じゃれ合い 2.5 s の待ちは最大 tickInterval 遅れる (撮り頃の猶予 1.5 s の内)。
        //    撮影を止めている間 (OFF・ボード未設置) は KH_PhotoAlbum がこの Behaviour ごと無効にする (Update も来ない)
        [Header("裏山: 見回りの間隔")]
        [Tooltip("予約の判定と撫での積算の間隔 (秒)。0 以下なら毎フレーム (古民家と同じ)")]
        public float tickInterval = 0.1f;
        float _nextTick;
        float _lastTick = -1f;

        // 2) **フリスビーを取った瞬間** (新しいきっかけ 1 つ。設計 §4.1)。KH_FrisbeeCatch の同期された state が
        //    「跳んで取った」(StateJumpCarry) に入ったエッジで、各自の端末から _OnFrisbeeCatch が来る。少し待って犬の横から 1 枚
        [Header("裏山: フリスビーを取った瞬間")]
        public bool frisbeeShotEnabled = true;
        [Tooltip("取った瞬間からの遅れ (秒)")]
        public float frisbeeShotDelay = 0.1f;
        [Tooltip("連写しない間隔 (秒)。全犬共通")]
        public float frisbeeShotCooldown = 20f;
        float _lastFrisbeeShot = -1000f;
        int _frisbeeDog = -1;
        int _frisbeeRetries;

        // 裏山改変 (IMP-WLD-005): 無効にされている間 (KH_PhotoAlbum が OFF・見る場所なしで止めた / 配線不足で自分で止めた) も止める。
        // 受け渡し・集合写真・フリスビーの通知は Update を通らずに来るので、ここで弾かないと見えない写真を撮る
        private bool CapturePaused() { return !enabled || (photoCapture != null && photoCapture.settings != null && !photoCapture.settings.CaptureAllowed); }

        void Start()
        {
            _lastPetShot = -1000f;
            _lastChaseShot = -1000f;
            _lastPlayShot = -1000f;
            if (photoCapture == null || photoCamera == null || gallery == null
                || dogRoots == null || dogRoots.Length == 0)
            {
                Debug.LogError("[KD_PhotoDirector] wiring incomplete. Run Tools/KominkaDogRun/Photo System setup.");
                enabled = false;
                return;
            }
            _n = dogRoots.Length;
            // **並びがそろっていなければ動かさない。** ずれたまま動くと
            // 「ポメのイベントでラブを撮る」= Play では原因の分からない絵になる。
            // wanderControllers は Wander System Setup が埋めるので、
            // 長さだけそろっていれば中身 null は許す (その犬の徘徊の走りだけ撮れない)
            if (dogHeadAnchors == null || dogHeadAnchors.Length != _n
                || fetchControllers == null || fetchControllers.Length != _n
                || petControllers == null || petControllers.Length != _n
                || wanderControllers == null || wanderControllers.Length != _n)
            {
                Debug.LogError("[KD_PhotoDirector] dog arrays are not aligned (dogRoots=" + _n
                    + "). Run Tools/KominkaDogRun/Photo System setup, then Wander System setup.");
                enabled = false;
                return;
            }

            _lastShotTime = new float[_n];
            _dropPending = new bool[_n];
            _dropTime = new float[_n];
            _dropPlayerId = new int[_n];
            _chasePending = new bool[_n];
            _chaseTime = new float[_n];
            _playPending = new bool[_n];
            _playTime = new float[_n];
            _petAccum = new float[_n];
            for (int i = 0; i < _n; i++)
            {
                // **初期値をそろえない。** そろえると同点になり、公平性の比較が
                // 常に若い番号を採る = ワールド開始直後だけ名簿順の偏りが残る。
                // ゆらぎはローカルの見た目だけの話なので同期は要らない
                _lastShotTime[i] = -1000f + Random.Range(0f, 1f);
                _dropPlayerId[i] = -1;
            }
        }

        /// <summary>犬が Drop に入った (KD_DogFetchController.ApplyState が状態変化時に
        /// 全クライアントで呼ぶ)。少し待ってから撮る。
        /// <paramref name="dog"/> は名簿の番号 (呼び出し側の dogIndex をそのまま渡す)。</summary>
        public void _OnDogDrop(int dog)
        {
            if (CapturePaused()) return;
            if (!ValidDog(dog)) return;
            KD_DogFetchController f = fetchControllers[dog];
            if (f == null) return;
            _dropPending[dog] = true;
            _dropTime[dog] = Time.time + dropShotDelay;
            _dropPlayerId[dog] = f.AttentionPlayerId;
        }

        /// <summary>犬が走り出した (Fetch の Chase 突入、または徘徊の走り出し)。
        /// 走りが乗ってから追跡ドローンを 1 枚。</summary>
        public void _OnDogChase(int dog)
        {
            if (CapturePaused()) return;
            if (!chaseShotEnabled || !ValidDog(dog)) return;
            if (Time.time - _lastChaseShot < chaseShotCooldown) return;
            // **先着を上書きしない** (旧実装は pending が bool 1 個で、4 匹が同時に
            // 走り出すと後の 1 匹が前を黙って消していた)。犬ごとに持てば上書き自体が起きない
            if (_chasePending[dog]) return;
            _chasePending[dog] = true;
            _chaseTime[dog] = Time.time + chaseShotDelay;
        }

        /// <summary>犬同士のじゃれ合いが始まった (KD_DogWanderController が
        /// 遊びに入ったエッジで呼ぶ)。**判定した本人に通知させる** —
        /// 撮影側で同じ予定表を組み直すと、複製は黙ってズレる (IMP-DOG-013 と同じ理由)。</summary>
        public void _OnDogsPlay(int dog)
        {
            if (CapturePaused()) return;
            if (!playShotEnabled || !ValidDog(dog)) return;
            if (Time.time - _lastPlayShot < playShotCooldown) return;
            if (_playPending[dog]) return;
            _playPending[dog] = true;
            _playTime[dog] = Time.time + playShotDelay;
        }

        // ================================================================ 裏山改変 (IMP-WLD-005)

        /// <summary>予約と撫での積算を全部畳む (撮影を止めたとき)。配列が未確保 (Start 前) でも安全。</summary>
        void ClearPending()
        {
            _burstStep = -1;
            _frisbeeDog = -1;
            if (_dropPending == null || _chasePending == null || _playPending == null || _petAccum == null) return;
            for (int i = 0; i < _n; i++)
            {
                _dropPending[i] = false; _chasePending[i] = false; _playPending[i] = false; _petAccum[i] = 0f;
            }
        }

        /// <summary>KH_PhotoAlbum が撮影を止めるとこの Behaviour を無効にする。途中の撫でバーストや予約を残さない
        /// (無効の間は遅延イベントの続きが来ないことがあるので、再開したとき _burstStep が 0..3 のまま固まらないようにする)。</summary>
        void OnDisable()
        {
            ClearPending();
            _lastTick = -1f;
        }

        /// <summary>フリスビーを跳んで取った (KH_FrisbeeCatch が全クライアントで、同期された state のエッジから呼ぶ)。
        /// 少し待ってから犬の横から 1 枚。間隔は全犬共通。<paramref name="dog"/> は名簿の番号 (KH_FrisbeeCatch.dogIndex)。</summary>
        public void _OnFrisbeeCatch(int dog)
        {
            if (CapturePaused()) return;
            if (!frisbeeShotEnabled || !ValidDog(dog) || SleepingDog(dog)) return;
            if (Time.time - _lastFrisbeeShot < frisbeeShotCooldown) return;
            if (_frisbeeDog >= 0) return;   // 先着を上書きしない (待ちは 0.1 s なので次の取りは間隔が弾く)
            _frisbeeDog = dog;
            _frisbeeRetries = 0;
            if (frisbeeShotDelay > 0f) SendCustomEventDelayedSeconds(nameof(_FrisbeeShot), frisbeeShotDelay);
            else _FrisbeeShot();
        }

        /// <summary>フリスビーの 1 枚 (遅延イベント)。直前の撮影の 1 フレームと重なったら 2 フレームおきに 3 回まで撮り直す。</summary>
        public void _FrisbeeShot()
        {
            int dog = _frisbeeDog;
            if (dog < 0) return;
            if (CapturePaused() || !ValidDog(dog)) { _frisbeeDog = -1; return; }
            if (!ShootFrisbee(dog))
            {
                if (_frisbeeRetries < 3) { _frisbeeRetries++; SendCustomEventDelayedFrames(nameof(_FrisbeeShot), 2); return; }
                _frisbeeDog = -1;
                return;
            }
            _frisbeeDog = -1;
            _lastFrisbeeShot = Time.time;
            MarkShot(dog);
        }

        /// <summary>犬の真横から (投げた人がいる側を選ぶ)。ディスクは口の中なので被写体は犬の胴。標準の寄り。</summary>
        bool ShootFrisbee(int dog)
        {
            Transform root = dogRoots[dog];
            if (root == null) return false;
            Vector3 body = DogSubject(dog);
            Vector3 side = root.right;
            side.y = 0f;
            side = side.sqrMagnitude < 0.0001f ? Vector3.right : side.normalized;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp))
            {
                Vector3 toMe = lp.GetPosition() - root.position;
                toMe.y = 0f;
                if (Vector3.Dot(toMe, side) < 0f) side = -side;   // 自分の側から (自分の端末のアルバムなので)
            }
            return ShootFramed(root, body, Jitter(side + Vector3.up * 0.18f), DogSize(dog) * 1.2f, fillNormal, GroupChase);
        }

        void Update()
        {
            // 裏山改変 (IMP-WLD-005): tickInterval ごとに見る。撫での積算は前回からの実時間 (tickDt) を足す
            float now = Time.time;
            if (tickInterval > 0f && now < _nextTick) return;
            _nextTick = now + tickInterval;
            float tickDt = _lastTick < 0f ? Time.deltaTime : now - _lastTick;
            _lastTick = now;
            if (tickDt > 0.5f) tickDt = 0.5f;   // 無効にしていた間の時間を撫でとして数えない
            if (CapturePaused())
            {
                ClearPending();
                return;
            }

            // ---- 予約の裁定 (IMP-CAM-002 の公平性) ----
            // カメラは 1 フレームに 1 枚しか撮れないので、撮り頃の予約が複数あるときは
            // **直近に撮られていない犬**を採る。落選した予約は消さずに残る —
            // 次のフレームで撮られるだけなので、体感できる遅れにはならない
            int best = -1;
            int bestKind = KindNone;
            for (int i = 0; i < _n; i++)
            {
                if (SleepingDog(i))
                {
                    // IMP-DOG-019 Phase 5 / Codex レビュー 2 回目 (2026-09-04)。
                    // **候補から外すだけでは足りない。** `RipeKind` を回さない = 期限切れの
                    // 畳みも止まるので、予約は寝ている間ずっと生き残る。睡眠が
                    // `shotPendingLifetime` より短いと、**寝る前の瞬間 (受け渡し / 走り出し /
                    // じゃれ合い) が起きた直後に撮られる** — もうその絵は無い。
                    // **寝ている間は毎フレーム畳む**: 寝る前の予約も、寝ている間に来た予約も
                    // 同じ扱いになり、「起床後に古い予約が撮られない」が入口の数に依らず立つ
                    _dropPending[i] = false;
                    _playPending[i] = false;
                    _chasePending[i] = false;
                    continue;
                }
                int kind = RipeKind(i, now);
                if (kind == KindNone) continue;
                if (best >= 0 && _lastShotTime[i] >= _lastShotTime[best]) continue;
                best = i;
                bestKind = kind;
            }
            if (best >= 0) TakePending(best, bestKind, now);

            // ---- 撫での継続時間 (抑制中は IsPetting が false になるので自然に途切れる) ----
            // **犬ごとに数える。** 両手で 2 匹を同時に撫でられるので、
            // 1 本の積算にすると「どちらの犬の 3 秒か」が決まらない
            for (int i = 0; i < _n; i++)
            {
                KD_DogPetController pc = petControllers[i];
                // IMP-DOG-019 Phase 5: 寝ている犬は候補から外す。
                // **ここで積算ごと止めるのが要点** — 撮る直前だけで弾くと、
                // 寝ている間に溜まった 3 秒が起きた瞬間にバーストへ化ける
                if (pc != null && pc.IsPetting && !SleepingDog(i)) _petAccum[i] += tickDt;   // 裏山改変: 間引いた分の実時間
                else _petAccum[i] = 0f;
            }

            if (_burstStep < 0 && now - _lastPetShot >= petShotCooldown)
            {
                // 閾値を超えた犬のうち、直近に撮られていない犬を採る (公平性)
                int pick = -1;
                for (int i = 0; i < _n; i++)
                {
                    if (_petAccum[i] < petShotAfterSec) continue;
                    if (pick >= 0 && _lastShotTime[i] >= _lastShotTime[pick]) continue;
                    pick = i;
                }
                if (pick >= 0)
                {
                    // バースト開始。クールダウンは開始時に消費する
                    // (構図ごとの失敗は _BurstStep がリトライで拾う)
                    _lastPetShot = now;
                    _petAccum[pick] = 0f;
                    _burstStep = 0;
                    _burstRetries = 0;
                    // **被写体はここで確定して離さない。** バーストは数フレームに分けて
                    // 4 枚撮るので、毎回引き直すと**途中で別の犬へ手を移した瞬間に
                    // 構図が半分ずつ別の犬**になる (1 枚のアルバムとして破綻する)
                    _burstDog = pick;
                    MarkShot(pick); // 開始時点で公平性へ計上する (4 枚ぶんの権利を取っている)
                    // 前回の続きから撮る。4 と 9 は互いに素なのでバーストを重ねると全構図を巡る
                    _burstOffset = (_burstOffset + PetBurstLen) % PetShotCount;
                    _BurstStep();
                }
            }
        }

        /// <summary>その犬に「今撮るべき予約」があるか (無ければ KindNone)。
        /// 撮り頃を過ぎた予約はここで捨てる — 待たせすぎた受け渡しは
        /// もう「止まって待っている絵」ではない。</summary>
        int RipeKind(int i, float now)
        {
            if (_dropPending[i])
            {
                if (now >= _dropTime[i] + shotPendingLifetime) _dropPending[i] = false;
                else if (now >= _dropTime[i]) return KindDrop;
            }
            if (_playPending[i])
            {
                if (now >= _playTime[i] + shotPendingLifetime) _playPending[i] = false;
                else if (now >= _playTime[i]) return KindPlay;
            }
            if (_chasePending[i])
            {
                if (now >= _chaseTime[i] + shotPendingLifetime) _chasePending[i] = false;
                else if (now >= _chaseTime[i]) return KindChase;
            }
            return KindNone;
        }

        /// <summary>裁定で選ばれた 1 枚を撮る。**確認先はその犬のもの** —
        /// 固定の 1 匹で判定すると「ダックスの走りをコーギーの状態で判定する」になる。</summary>
        void TakePending(int dog, int kind, float now)
        {
            if (kind == KindDrop)
            {
                _dropPending[dog] = false;
                if (ShootDropScene(dog)) MarkShot(dog);
                return;
            }
            if (kind == KindPlay)
            {
                _playPending[dog] = false;
                // **クールダウンは撮る直前にもう一度見る。**
                // 通知の時点だけで見ると、4 匹がほぼ同時に遊び始めた回に
                // **4 枚まとめて撮ってしまう** (予約を犬ごとに持った副作用 —
                // 旧実装は pending が 1 本だったので構造的に起きなかった)。
                // 落ちた犬は諦める: 公平性がここまでに「一番撮られていない犬」を
                // 選んでいるので、残りを待たせて撮る価値は無い
                if (now - _lastPlayShot < playShotCooldown) return;
                // まだ遊んでいるときだけ (プレイヤーが近づいて中断していたら諦める)。
                // **相手も撮影の直前に選ぶ** — 途中参加・役の入れ替えがあるので、
                // 通知の時点で相手を決めると別の犬が写る
                KD_DogWanderController me = wanderControllers[dog];
                if (me == null || !me.IsPlaying) return;
                int other = NearestPlaymate(dog);
                if (other < 0) return;
                if (!ShootPlay(dog, other)) return;
                _lastPlayShot = now;
                MarkShot(dog);
                MarkShot(other); // **2 匹とも写っている** ので、両方を計上する
                return;
            }
            if (kind == KindChase)
            {
                _chasePending[dog] = false;
                // 上と同じ理由 — 4 匹が同時に走り出す回で連写しない
                if (now - _lastChaseShot < chaseShotCooldown) return;
                // まだ走っているときだけ (追い付いて Pickup に入っていたら諦める)。
                // 徘徊の走り (IMP-DOG-004) も同じ構図で拾う
                KD_DogFetchController f = fetchControllers[dog];
                KD_DogWanderController w = wanderControllers[dog];
                bool running = (f != null && f.IsChasing) || (w != null && w.IsRunning);
                if (!running || !ShootChase(dog)) return;
                _lastChaseShot = now;
                MarkShot(dog);
            }
        }

        /// <summary>じゃれ合いの相手 — 今も遊んでいて、一番近い別の犬。
        /// 遠すぎる相手は「じゃれ合っている絵」にならないので採らない。</summary>
        int NearestPlaymate(int dog)
        {
            Transform me = dogRoots[dog];
            if (me == null) return -1;
            Vector3 p = me.position;
            int best = -1;
            float bestSqr = playPairMaxDistance * playPairMaxDistance;
            for (int i = 0; i < _n; i++)
            {
                if (i == dog) continue;
                KD_DogWanderController w = wanderControllers[i];
                if (w == null || !w.IsPlaying) continue;
                Transform t = dogRoots[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                float d = (t.position - p).sqrMagnitude;
                if (d >= bestSqr) continue;
                bestSqr = d;
                best = i;
            }
            return best;
        }

        /// <summary>**そばで寝ている犬か** (IMP-DOG-019 Phase 5 / Issue §7 未決 4)。
        ///
        /// 公平性 (§4「最終撮影時刻が古い方を採る」) は「一番撮られていない犬」を選ぶので、
        /// **ずっと寝ている犬がその席を取り続ける** — 寝たまま撫でられる (Phase 4) ぶん
        /// 撫でバーストの候補にも入り続け、庭で遊んでいる犬がアルバムに出なくなる。
        /// **候補から外すのがマネージャー決定** (2026-09-04)。
        ///
        /// ⚠ **`IsPausedForPlayer` 経由で暗黙に外れていることに頼らない。**
        /// あちらは徘徊 (`_paused` / 休憩 / 催促) も含む広い述語で、ここでは
        /// 一度も読んでいない。**外れている根拠を grep できる 1 行にする。**</summary>
        bool SleepingDog(int i)
        {
            if (wanderControllers == null || i < 0 || i >= wanderControllers.Length) return false;
            KD_DogWanderController w = wanderControllers[i];
            return w != null && w.IsSleepCalled;
        }

        void MarkShot(int dog)
        {
            if (dog >= 0 && dog < _n) _lastShotTime[dog] = Time.time;
        }

        bool ValidDog(int dog)
        {
            return _n > 0 && dog >= 0 && dog < _n && dogRoots[dog] != null;
        }

        /// <summary>撫でバーストの 1 ステップ。カメラは 1 フレームに 1 枚しか撮れないため、
        /// 数フレームおきに自分を呼び直して 4 枚を順に撮る。
        /// **被写体 (_burstDog) はバースト開始時に確定していて、ここでは引き直さない。**</summary>
        public void _BurstStep()
        {
            if (CapturePaused()) { _burstStep = -1; return; }
            if (_burstStep < 0 || _burstStep >= PetBurstLen || !ValidDog(_burstDog))
            {
                _burstStep = -1;
                return;
            }

            bool ok = ShootPetShot(_burstDog, (_burstOffset + _burstStep) % PetShotCount);
            if (!ok && _burstRetries < 5)
            {
                // 直前の撮影の 1 フレームと重なった等。少し待って同じ構図を撮り直す
                _burstRetries++;
                SendCustomEventDelayedFrames("_BurstStep", 2);
                return;
            }

            _burstRetries = 0;
            _burstStep++;
            if (_burstStep >= PetBurstLen) { _burstStep = -1; return; }
            SendCustomEventDelayedFrames("_BurstStep", 3);
        }

        // ================================================================ 受け渡し

        /// <summary>受け渡し: 犬と投げた本人を収める。
        /// **IMP-CAM-002 で構図を 2 → 4 に増やした** — 引きを「庭ごと」の列へ出すと、
        /// 「受け渡し」の列が 1 構図しか持たなくなり、同じ絵 4 枚で埋まるため。</summary>
        bool ShootDropScene(int dog)
        {
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(_dropPlayerId[dog]);
            if (!Utilities.IsValid(p)) return false;
            Transform root = dogRoots[dog];
            if (root == null) return false;

            Vector3 dogHead = DogHead(dog);
            Vector3 playerChest = p.GetPosition() + Vector3.up * 1.1f;
            Vector3 mid = (dogHead + playerChest) * 0.5f;

            Vector3 axis = playerChest - dogHead;
            axis.y = 0f;
            if (axis.sqrMagnitude < 0.01f) axis = root.forward;
            axis.Normalize();
            Vector3 side = Vector3.Cross(axis, Vector3.up);

            // 2 人 (1 人と 1 匹) が入るので被写体は犬より大きい。間隔から見積もる
            float pairSize = Mathf.Max(Vector3.Distance(dogHead, playerChest) * 1.2f, DogSize(dog));

            bool ok;
            if (_dropVariant == 0)
            {
                // 横から: 受け渡しの関係が一番読める向き (誰への返事かが分かる)
                ok = ShootFramed(root, mid, Jitter(side + Vector3.up * 0.28f),
                    pairSize, fillNormal, GroupHandoff);
            }
            else if (_dropVariant == 1)
            {
                // 反対の横から、やや低く: 犬の高さで受け渡しを見る。
                // 同じ「横から」でも左右と高さが変わるので別の絵になる
                Vector3 dir = -side;
                dir.y = -0.12f;
                ok = ShootFramed(root, mid, Jitter(dir), pairSize, fillNormal, GroupHandoff);
            }
            else if (_dropVariant == 2)
            {
                // 俯瞰: 斜め上から 1 人と 1 匹を見下ろす (庭の広さも少し入る)
                Vector3 dir = (side * 0.7f - axis).normalized + Vector3.up * (dropDroneHeight / 1.6f);
                ok = ShootFramed(root, mid, Jitter(dir), pairSize, fillNormal, GroupHandoff);
            }
            else
            {
                // 引き: 庭ごと。二人が小さくても「古民家で遊んでいる絵」になる
                ok = ShootFramed(root, mid, Jitter(-side + Vector3.up * 0.5f),
                    pairSize, fillWide, GroupWide);
            }
            if (ok) _dropVariant = (_dropVariant + 1) % DropVariantCount;
            return ok;
        }

        // ================================================================ 撫で

        /// <summary>撫での 1 枚。shot は構図プールの番号 (Shot* 定数)。</summary>
        bool ShootPetShot(int dog, int shot)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return false;
            Transform root = dogRoots[dog];
            if (root == null) return false;

            VRCPlayerApi.TrackingData td = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 head = td.position;
            Vector3 fwd = td.rotation * Vector3.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 0.0001f ? root.forward : fwd.normalized;
            Vector3 side = Vector3.Cross(fwd, Vector3.up);

            Vector3 dogHead = DogHead(dog);
            Vector3 body = DogSubject(dog);
            float size = DogSize(dog);
            Vector3 mid = Vector3.Lerp(body, head, 0.45f); // 犬多めの中点

            if (shot == ShotDogEye)
            {
                // 犬視点・至近: 犬の頭のやや後ろ・低い位置から、撫でている自分を見上げる。
                // **被写体はプレイヤー**なので犬のフレーミングは通さない (至近が正しい)
                Vector3 back = dogHead - head;
                back.y = 0f;
                back = back.sqrMagnitude < 0.0001f ? -root.forward : back.normalized;
                return Shoot(dogHead + back * 0.35f + Vector3.up * 0.05f, head, GroupDogEye);
            }
            if (shot == ShotDogEyeOver)
            {
                // 犬視点・引き: もう少し下がって、**犬の後頭部ごしにプレイヤーの上半身**。
                // 「犬目線」の列に構図が 1 つしか無いと同じ絵 4 枚で埋まるため対にした。
                // 至近の ShotDogEye と違い「誰を見上げているか」が周りごと読める
                Vector3 back = dogHead - head;
                back.y = 0f;
                back = back.sqrMagnitude < 0.0001f ? -root.forward : back.normalized;
                Vector3 chest = head - Vector3.up * 0.35f;
                return Shoot(dogHead + back * 0.75f + Vector3.up * 0.12f, chest, GroupDogEye);
            }
            if (shot == ShotCloseUp)
            {
                // 寄り: 顔だけ。撫でている手が入るよう斜め前から。被写体は頭なので小さい
                Vector3 dir = Jitter(-root.forward + side * 0.4f + Vector3.up * 0.15f);
                return ShootFramed(root, dogHead, dir, size * 0.42f, fillCloseUp, GroupPet);
            }
            if (shot == ShotFront)
            {
                // 前から: 撫でている顔と犬を正面から (人と犬の両方が入る)
                float pairSize = Mathf.Max(Vector3.Distance(body, head) * 1.15f, size);
                return ShootFramed(root, mid, Jitter(fwd - Vector3.up * 0.08f), pairSize,
                    fillNormal, GroupPet);
            }
            if (shot == ShotBack)
            {
                // 後ろから: 肩越しに犬を見下ろす
                float pairSize = Mathf.Max(Vector3.Distance(body, head) * 1.1f, size);
                return ShootFramed(root, Vector3.Lerp(body, head, 0.2f),
                    Jitter(-fwd + Vector3.up * 0.32f), pairSize, fillNormal, GroupPet);
            }
            if (shot == ShotDrone)
            {
                // 俯瞰: 斜め上空から 1 人と 1 匹を見下ろす
                float pairSize = Mathf.Max(Vector3.Distance(body, head) * 1.25f, size);
                Vector3 dir = (-fwd + side).normalized + Vector3.up * (petDroneHeight / 2.4f);
                return ShootFramed(root, mid, Jitter(dir), pairSize, fillNormal, GroupPet);
            }
            if (shot == ShotLowAngle)
            {
                // ローアングル: 犬の目線の高さから見上げる。**犬が主役に見える構図**
                Vector3 dir = Jitter(-root.forward + side * 0.5f);
                dir.y = -0.12f; // わずかに下から (見上げる)
                return ShootFramed(root, dogHead, dir, size, fillNormal, GroupPet);
            }
            if (shot == ShotProfile)
            {
                // 真横: 犬の横顔と、そこへ伸びている手。**手と犬の関係が一番読める** —
                // 正面・肩越し・俯瞰はどれも人の側から見ているので、
                // 「撫で」の列にひとつ第三者の目を混ぜる
                Vector3 dir = Jitter(root.right + Vector3.up * 0.12f);
                float pairSize = Mathf.Max(size, Vector3.Distance(dogHead, body) * 2f);
                return ShootFramed(root, Vector3.Lerp(dogHead, body, 0.35f), dir,
                    pairSize, fillNormal, GroupPet);
            }
            // 引き: 庭ごと入れる。犬は小さいが「古民家で過ごしている絵」になる
            return ShootFramed(root, body,
                Jitter(-root.forward + side * 0.6f + Vector3.up * 0.35f),
                size, fillWide, GroupWide);
        }

        // ================================================================ かけっこ

        /// <summary>走る犬。俯瞰の追跡ドローン / ローアングル / 引きを回す。</summary>
        bool ShootChase(int dog)
        {
            Transform root = dogRoots[dog];
            if (root == null) return false;
            Vector3 body = DogSubject(dog);
            float size = DogSize(dog);
            Vector3 fwd = root.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 0.0001f ? Vector3.forward : fwd.normalized;
            Vector3 side = Vector3.Cross(fwd, Vector3.up);

            bool ok;
            if (_chaseVariant == 0)
            {
                // 追跡ドローン: 進行方向の先へ回り込んで振り返る (走ってくる絵)
                Vector3 dir = (fwd + side * 0.7f).normalized + Vector3.up * (chaseShotHeight / 2.0f);
                ok = ShootFramed(root, body, Jitter(dir), size, fillNormal, GroupChase);
            }
            else if (_chaseVariant == 1)
            {
                // ローアングル: 地面すれすれから。脚と土埃の高さで速さが出る
                Vector3 dir = (fwd + side * 0.4f).normalized;
                dir.y = -0.1f;
                ok = ShootFramed(root, body, Jitter(dir), size, fillNormal, GroupChase);
            }
            else
            {
                // 引き: 庭を横切っていく犬。**走りは引きの方が速さが読める**ことがある
                // (背景が流れないぶん、犬の位置が庭のどこかで分かる)
                Vector3 dir = (side - fwd * 0.3f).normalized + Vector3.up * 0.3f;
                ok = ShootFramed(root, body, Jitter(dir), size, fillWide, GroupWide);
            }
            if (ok) _chaseVariant = (_chaseVariant + 1) % ChaseVariantCount;
            return ok;
        }

        // ================================================================ じゃれ合い

        /// <summary>じゃれ合い (IMP-CAM-002)。**2 匹が入るフレーミング** —
        /// 受け渡し (人 + 犬) と同じ手で、2 匹の中点を狙い、間隔から被写体の大きさを見積もる。
        /// 体格の逆算 (DogSize) は犬ごとに効くので、ポメとラブの組でも枠から溢れない。</summary>
        bool ShootPlay(int a, int b)
        {
            Transform ra = dogRoots[a];
            Transform rb = dogRoots[b];
            if (ra == null || rb == null) return false;

            Vector3 pa = DogSubject(a);
            Vector3 pb = DogSubject(b);
            Vector3 mid = (pa + pb) * 0.5f;

            Vector3 axis = pb - pa;
            axis.y = 0f;
            if (axis.sqrMagnitude < 0.01f) axis = ra.forward;
            axis.Normalize();
            Vector3 side = Vector3.Cross(axis, Vector3.up);

            // 2 匹が入る = 間隔 + 大きい方の体格。追いかけっこは離れて走るので間隔が効く
            float pairSize = Mathf.Max(Vector3.Distance(pa, pb) * 1.25f,
                Mathf.Max(DogSize(a), DogSize(b)));

            bool ok;
            if (_playVariant == 0)
            {
                // 犬の目線の高さで、2 匹の並びを横から。**どっちが追ってどっちが逃げているかが読める**
                Vector3 dir = side;
                dir.y = -0.05f;
                ok = ShootFramed(ra, mid, Jitter(dir), pairSize, fillNormal, GroupPlay);
            }
            else if (_playVariant == 1)
            {
                // 俯瞰: 2 匹の位置関係が一番読める。走り回っている軌跡が絵になる
                Vector3 dir = (side * 0.8f - axis * 0.4f).normalized + Vector3.up * 0.95f;
                ok = ShootFramed(ra, mid, Jitter(dir), pairSize, fillNormal, GroupPlay);
            }
            else
            {
                // 引き: 庭ごと。犬が小さくても「庭で犬同士が遊んでいる」が読める
                ok = ShootFramed(ra, mid, Jitter(-side + Vector3.up * 0.45f),
                    pairSize, fillWide, GroupWide);
            }
            if (ok) _playVariant = (_playVariant + 1) % PlayVariantCount;
            return ok;
        }

        // ================================================================ 幾何

        Vector3 DogHead(int dog)
        {
            Transform h = dogHeadAnchors[dog];
            if (h != null) return h.position;
            Transform root = dogRoots[dog];
            return root == null ? Vector3.zero : root.position + Vector3.up * 0.3f;
        }

        /// <summary>被写体としての犬の中心 (胴のあたり)。頭だけ狙うと胴が切れる。</summary>
        Vector3 DogSubject(int dog)
        {
            Transform root = dogRoots[dog];
            if (root == null) return Vector3.zero;
            return Vector3.Lerp(root.position, DogHead(dog), 0.75f);
        }

        /// <summary>被写体としての犬のおおよその直径 (m)。**体格から実測する** —
        /// 犬種で体高が倍近く違うので (ポメ / コーギー / ラブ / ダックス)、距離を固定にすると
        /// 小型犬が豆粒になり大型犬が枠から溢れる。頭アンカーの高さ (= 体高) を
        /// ルートとの差で測り、その 1.5 倍を体長の目安にしている。
        /// **犬定義を参照しない**ので、犬が増えても調整値が増えない。</summary>
        float DogSize(int dog)
        {
            Transform root = dogRoots[dog];
            Transform h = dogHeadAnchors[dog];
            float y = (root != null && h != null) ? h.position.y - root.position.y : 0f;
            // **`y < 0.1f` だけでは NaN が素通りする** (NaN との比較は常に false)。
            // 否定形で書いて NaN も上限外も既定値へ落とす (Codex 指摘・中)。
            // 非有限値をここで止めないと、距離 → レイ → LookRotation まで伝播する
            if (!(y > 0.1f) || !(y < 2f)) y = 0.4f; // アンカー未配線・伏せている・異常値
            return y * 1.5f;
        }

        /// <summary>被写体が画面の縦を fill 割合だけ占める距離。カメラの FOV から逆算する。
        /// fill には揺らぎを入れる (同じ構図でも毎回同じ絵にしない)。
        /// **FOV と被写体サイズも必ず範囲へ丸める** — FOV が 0 付近だと距離が無限へ、
        /// 180 度付近だと 0 へ飛ぶ (Codex 指摘・中)。</summary>
        float FrameDistance(float size, float fill)
        {
            if (photoCamera == null) return 2f;
            // **上限は 2 匹 + プレイヤーが入る大きさまで許す** (じゃれ合いは離れて走る)。
            // 8m のままだと追いかけっこの引きが枠に収まらず既定値へ落ちていた
            if (!(size > 0.05f) || !(size < 14f)) size = 0.6f;
            fill = Mathf.Clamp(fill * (1f + Random.Range(-fillJitter, fillJitter)), 0.06f, 0.95f);
            float fov = Mathf.Clamp(photoCamera.fieldOfView, 10f, 120f);
            float halfFov = fov * 0.5f * Mathf.Deg2Rad;
            float d = (size / fill) * 0.5f / Mathf.Tan(halfFov);
            // 上限は世界の広さで決まる (上の maxFrameDistance のコメント)。
            // **下限も要る** — 逆算が near clip より手前へ来ると何も写らない
            float cap = !(maxFrameDistance > 0.5f) ? 12f : maxFrameDistance;
            return Mathf.Clamp(d, 0.3f, cap);
        }

        /// <summary>方向へ揺らぎを入れる (水平 ±angleJitter / 仰角はその 0.4 倍)。</summary>
        Vector3 Jitter(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return Vector3.forward;
            Vector3 axis = Vector3.Cross(dir, Vector3.up);
            if (axis.sqrMagnitude < 0.0001f) axis = Vector3.right; // 真上・真下を向いている
            Quaternion yaw = Quaternion.AngleAxis(Random.Range(-angleJitter, angleJitter), Vector3.up);
            Quaternion pitch = Quaternion.AngleAxis(
                Random.Range(-angleJitter * 0.4f, angleJitter * 0.4f), axis.normalized);
            return yaw * (pitch * dir);
        }

        /// <summary>被写体から見て dir の方向へ、fill を満たす距離だけ離して撮る。
        /// 壁抜け・地面へのめり込みはレイで詰める (撮影が体験の外へ出ないように)。</summary>
        bool ShootFramed(Transform root, Vector3 subject, Vector3 dir, float size, float fill, int group)
        {
            if (dir.sqrMagnitude < 0.0001f) dir = root == null ? Vector3.forward : -root.forward;
            dir = dir.normalized;
            Vector3 pos = subject + dir * FrameDistance(size, fill);
            return Shoot(ClearCameraPos(subject, pos, size), subject, group);
        }

        /// <summary>カメラ位置を地形の内側から救い出す。**壁の向こうから撮ると
        /// 壁だけの写真になる** ので、遮られていたら手前へ詰める。</summary>
        Vector3 ClearCameraPos(Vector3 subject, Vector3 pos, float size)
        {
            Vector3 d = pos - subject;
            float len = d.magnitude;
            if (len < 0.05f) return pos;
            Vector3 n = d / len;

            // 犬自身のコライダーを避けて少し外から撃つ
            float skip = Mathf.Min(size * 0.6f, len * 0.5f);
            float span = len - skip;
            RaycastHit hit;
            if (span > 0.01f && Physics.Raycast(subject + n * skip, n, out hit, span,
                    cameraBlockMask, QueryTriggerInteraction.Ignore))
                pos = hit.point - n * cameraClearance;

            // ================================================================ 破壊的監査 RC-12
            // 地面・縁側の床へ潜らない。
            //
            // **レイの起点をカメラの 2m 上に置いていたのが誤りだった。**
            // 古民家には庇・軒・縁側の天井があるので、カメラの真上 2m 以内にそれが
            // あると**それを「床」と読んでしまい**、`pos.y < down.point.y + clearance` が
            // 無条件に真になって**カメラが屋根の上まで跳ね上がる**。
            // 発火は偶発ではなく幾何的に決定的で、しかも**縁側で撫でているとき**
            // = いちばん撮りたい場所で必ず出る。
            //
            // 直し方は 2 つ重ねた:
            //   1. **起点を被写体の高さに紐づける** — 犬は天井の中に居ないので、
            //      被写体の少し上から撃てば「カメラの上の庇」を拾わない
            //   2. **被写体より高い面は床として採らない** — 起点をずらしても
            //      被写体とカメラの間に庇が挟まる配置は作れるので、面の高さでも弾く
            //
            // 起点を単純に下げなかったのは、**既に床下に居るカメラを救えなくなる**ため
            // (この処理の本来の目的はそちら)。
            RaycastHit down;
            Vector3 downOrigin = new Vector3(pos.x, subject.y + 0.5f, pos.z);
            if (Physics.Raycast(downOrigin, Vector3.down, out down, 5f,
                    cameraBlockMask, QueryTriggerInteraction.Ignore)
                && down.point.y <= subject.y + cameraClearance   // 被写体より高い面は「床」ではない
                && pos.y < down.point.y + cameraClearance)
                pos.y = down.point.y + cameraClearance;
            return pos;
        }

        /// <summary>撮影の実体。**スロット確保より先に撮れることを確認する** —
        /// 逆にすると、撮影が拒否されたのにスロットだけ消費されて
        /// 「黒い板が撮影済みとして表示される」(Codex 指摘・高)。</summary>
        public void _GroupPhoto(Vector3 center, float yaw)
        {
            if (photoCapture == null || gallery == null || photoCamera == null) return;
            if (CapturePaused()) return;   // 裏山改変 (IMP-WLD-005): OFF・見る場所なしでは集合写真も撮らない
            Vector3 subject = center + Vector3.up * .6f;
            Vector3 from = Quaternion.Euler(0, yaw, 0) * new Vector3(0, 2.2f, 6.5f);
            Shoot(ClearCameraPos(subject, center + from, 5f), subject, GroupWide);
        }

        bool Shoot(Vector3 pos, Vector3 lookAt, int group)
        {
            if (!photoCapture.CanCapture) return false; // 前の撮影の 1 フレーム中 (再入)

            // 寝室の出来事は屋外用の距離計算を使わず、別の4枚へ残す。
            if (bedroom != null && bedroom.room != null && bedroom.room.GainAt(lookAt) <= 0f)
            {
                bedroom.requestedSubject = lookAt;
                bedroom.requestedLow = false;
                return bedroom._TryCapture();
            }

            // 出来事のカテゴリごとの枠から取る — 同じ絵で埋まらないように
            RenderTexture rt = gallery._AcquireNextSlot(group);
            if (rt == null) return false;

            Vector3 dir = lookAt - pos;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            Transform ct = photoCamera.transform;
            ct.position = pos;
            ct.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            // 描き先は撮影用 RT 1 枚で、確定後にスロットへ色だけ写る (IMP-GAL-003)
            return photoCapture._CaptureTo(rt);
        }
    }
}
