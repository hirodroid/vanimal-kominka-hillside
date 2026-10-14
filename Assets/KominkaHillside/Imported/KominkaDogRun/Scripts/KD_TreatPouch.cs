using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-TRT-001 Phase 4b Step 5: **手元メニュー (おやつポーチ)。**
    /// 設計は [IMP-TRT-001](../../../docs/development/issues/IMP-TRT-001-treat-system.md)
    /// の「Phase 4b の確定設計」— 手元メニューの節。
    ///
    /// 一文で: **呼べば出る板。そこから 1 個出す。**
    ///
    /// ## 板だけ。**ポーチの実物は持たない** (2026-09-01 ユーザー決定)
    ///
    /// 初版は腰の左脇に革のポーチを提げ、それを触って開く形だった。
    /// **VR でも Desktop でも邪魔なだけだった** — 常に視界の下端に居座るのに、
    /// 触って開く導線は VR では手のひら、Desktop ではキー (T) に置き換わっていて、
    /// **実物は「開く理由」を 1 つも担っていなかった。**
    /// いまは**板が出る / 消えるだけ**で、閉じている間は世界に何も無い。
    /// 当たりも持たない (**体に追従する当たりはプレイヤーを押しのける** — 下記)。
    ///
    /// ## 開き方 (2026-09-03 に作り直した — IMP-TRT-002)
    ///
    /// <code>
    /// VR       右スティックを下へ倒したまま openHoldSeconds → 開く / 手元へ呼び戻す
    /// Desktop  desktopToggleKey (既定 T。2026-09-15 まで Tab)
    /// 共通     開いた場所へ据わる。明示的な閉鎖と、おやつ成功・添い寝指定開始時の閉鎖
    /// </code>
    ///
    /// 旧方式 (左手に追従し、**手のひらが自分を向いたら開く**) は
    /// **実機で「出しにくい」が結論**だったので調整値ごと落とした:
    /// ①`palmAxis` は「実機で決める」と書いたまま既定のままだった
    /// ②向き × 保持 × 頭からの距離の 3 条件が重なる
    /// ③**開く条件と閉じる条件が同じ**なので、板を読もうと手を動かすと閉じる。
    /// **狙って出せることを、覚えなくてよさより上に置いた。**
    ///
    /// ## シーンに 1 個。同期は 0 本
    ///
    /// 漢字間違い探しの `KF_CrownMarker` と同型で、**画面は同期せず、条件と結果だけを同期する。**
    /// 各クライアントがこの 1 個を**自分のローカルプレイヤーへ追従させる**ので、
    /// 「他人のポーチが腰にぶら下がって見える」が構造的に起こらない。
    /// 世界へ伝わるのは枠 (<see cref="KD_TreatSlot"/>) の同期状態だけ。
    ///
    /// ## 表示と実行の可否は**同じ関数**から作る
    ///
    /// 行のボタンを出すかどうかも、押したときに実際に出せるかも、
    /// **両方 <see cref="KD_TreatBox.DispenseGateWith"/> の理由コードを読む。**
    /// 「表示は出せると言っているのに押すと何も起きない」が構造的に作れなくなる
    /// (デバッグ盤が算術を 1 行も書き写していないのと同じ規律 — IMP-PRG-001)。
    ///
    /// > **⚠ `KD_ShopItem.Owned()` を可否の入力に混ぜない。**
    /// > 在庫つき消耗品の `Owned()` は**「一度でも入手したか」**を返す (使い切っても
    /// > 選んだ種類を失わないための定義 — shop-foundation §4)。
    /// > 「所持している = 出せる」と読むと**在庫 0 でも出せてしまう。**
    /// > 実際 Step 3b で棚がこれを踏んで「消耗品という型が成立しない」まで行った。
    ///
    /// ## 状態はここが持ち、遷移は <see cref="KD_TreatBox.StepDispenseWith"/> が決める
    ///
    /// <code>
    /// Idle → Reserving → Reserved → Committing → Out → (回収) → Idle
    ///          枠と実体の      枠へ書く      ★在庫を        犬が来られる
    ///          Ownership 待ち  + 手へ吸着     1 個減らす
    /// </code>
    ///
    /// **副作用は「状態が実際に動いた呼び出し」でだけ実行する。**
    /// 遷移表の「在庫が減る辺は 1 本」だけでは**実行回数を保証しない** (2026-08-24 Codex 指摘) —
    /// 保証するのは ①その辺が必ず状態を動かすこと ②ここが動いたときだけ叩くこと
    /// ③世代 (<see cref="_curSeq"/>) の合わないイベントを捨てること、の 3 つ。
    ///
    /// ## Ownership の移譲は自分では受け取れない
    ///
    /// `OnOwnershipTransferred` は**移った先のオブジェクト**でしか発火しない。
    /// 枠と実体は別 GameObject なので、出す側であるここには届かない。
    /// だから <see cref="KD_TreatSlot"/> が <see cref="_OnSlotOwnerChanged"/> を叩き、
    /// あわせて <see cref="Poll"/> が `Networking.IsOwner` を見る (通知の取りこぼしの床)。
    /// **どちらの経路も「両方を持っているか」を自分で確かめてから先へ進む** —
    /// 移譲前に書いた値は捨てられうる (`KD_ToyBox` が手本)。
    /// </summary>
    /// <remarks>
    /// SyncMode None・**同期変数 0 本** (Verify が機械検査する)。
    ///
    /// **この GameObject に Collider を付けない。** 触って開く導線はもう無く、
    /// 体に追従する当たりは**物理でプレイヤーを押しのける** (2026-08-24 Play で実測)。
    /// Verify が「Collider が無いこと」を機械検査する。
    /// 板は **World Space Canvas** で、`VRCUiShape` / `GraphicRaycaster` /
    /// **layer = Default** が要件 (`vrc-udonsharp` §5。"UI" だとポインタが効かない)。
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public partial class KD_TreatPouch : UdonSharpBehaviour
    {
        // ============================================================
        // 文言の宣言 (vrc-tmp-font §3)
        //
        // **フォントは Static なので、ここに無い字は豆腐になる。**
        // Treat の Setup がこの const 群を直接読んで TryAddCharacters するので、
        // **ここを直せばフォントも追随する** (二重管理にならない)。
        // 「もっている」「あと」「よみこみ中」は KD_ShopShelf の const を借りる —
        // 棚と手元で同じ概念に別の字を当てない。
        // ============================================================

        public const string WordSettings = "設定";
        public const string WordDogVoice = "犬の声";
        public const string WordVoiceOn = "ON";
        public const string WordVoiceOff = "OFF";
        public const string WordVoiceKinds = "吠え声・はぁはぁ・寝息";
        /// <summary>2 節 (犬の声 / BGM) 共通の断り書き。**他人の耳は変わらない。**</summary>
        public const string WordSoundScope = "自分に聞こえる音量を変えます";
        public const string WordVoiceVolume = "音量";
        // IMP-ENV-009: BGM。**犬の声と同じ形**で同じ面に並べる (音の設定の面を 2 つ作らない)
        public const string WordBgm = "BGM";
        public const string WordBgmNote = "ライブ中は自動で止まります";
        public const string WordVolumeSettings = "音量設定";
        public const string WordBackSettings = "設定へ戻る";
        public const string WordLiveVolume = "ライブ";
        public const string WordOutside = "外";
        public const string WordBedroom = "寝室";
        public const string WordBedroomNote = "各場所の音量を個別に設定・0%で無音";
        public const string WordVolumeScope = "自分だけ・BGMはライブ中に停止";
        public GameObject settingsPage;
        public GameObject settingsHome;
        public GameObject volumeSettingsPage;
        public KD_SleepAudio sleepAudio;
        public UnityEngine.UI.Slider liveVolumeSlider;
        public UnityEngine.UI.Slider bedroomLiveVolumeSlider;
        public UnityEngine.UI.Slider bedroomBgmVolumeSlider;
        public UnityEngine.UI.Slider bedroomVoiceVolumeSlider;
        public TextMeshProUGUI bedroomBgmVolumeValueLabel;
        public TextMeshProUGUI bedroomVoiceVolumeValueLabel;
        private float _appliedVoiceGain = -1f;
        public TextMeshProUGUI liveVolumeValueLabel;
        public TextMeshProUGUI bedroomLiveVolumeValueLabel;
        private bool _volumeSettingsOpen;
        public GameObject distanceFadeSettingsPage;
        public KD_DistanceFadeSettings distanceFadeSettings;
        private bool _distanceFadeSettingsOpen;
        public UnityEngine.UI.Image settingsButtonImage;
        public UnityEngine.UI.Slider dogVoiceSlider;
        public TextMeshProUGUI dogVoiceValueLabel;
        public TextMeshProUGUI dogVoiceToggleLabel;
        public UnityEngine.UI.Image dogVoiceToggleImage;
        public KD_DogWanderController[] voiceDogs;
        public AudioSource[] dogVoiceSources;
        public float[] dogVoiceBaseVolumes;
        // IMP-ENV-009: BGM の設定。**倍率と ON/OFF を渡すだけ** — 実際の音量は
        // KD_BgmPlayer がライブの掛け金と掛け合わせて毎フレーム作り直す
        public KD_BgmPlayer bgmPlayer;
        public UnityEngine.UI.Slider bgmSlider;
        public TextMeshProUGUI bgmValueLabel;
        public TextMeshProUGUI bgmToggleLabel;
        public UnityEngine.UI.Image bgmToggleImage;
        private bool _settingsOpen;
        private bool _voiceEnabled = true;
        private float _voiceVolume = 0.5f;
        private bool _bgmEnabled = true;
        private float _bgmVolume = 0.5f;

        public const string WordTitle = "おやつを出す";
        public const string WordToyTitle = "ボールを手元へ";
        public const string WordFxTitle = "なでたときの演出";
        public const string WordSleepTitle = "そばに呼ぶ";
        public const string WordLiveTitle = "ライブをひらく";
        public const string WordAccTitle = "犬のかざり";
        public const string WordApplied = "切り替えました";
        public const string WordLivePending = "開催を受け付けています...";
        public const string WordLiveRejected = "開催できませんでした。もう一度お試しください";
        // P5 (2026-09-16): 呼ぶタブから設定ホーム (ライブ開催許可の隣) へ移した。文言も「〜を全員に許可: ON/OFF」で揃える
        public const string WordOpenOn = "呼ぶを全員に許可: ON";
        public const string WordOpenOff = "呼ぶを全員に許可: OFF";
        public TextMeshProUGUI sectionTitleLabel;
        public TextMeshProUGUI feedbackLabel;
        public UnityEngine.UI.Image[] fxButtonImages;
        public Color equipOnColor = new Color(0.30f, 0.42f, 0.52f);
        public TextMeshProUGUI[] rowHintLabels;
        public TextMeshProUGUI openToAllLabel;
        public AudioSource uiAudio;
        public AudioClip uiSuccessClip;
        public AudioClip uiRejectClip;
        private string _feedbackText = "";
        private float _feedbackUntil;
        private int _feedbackTab;
        private bool _awaitingLive;

        public const string WordDispense = "出す";

        // ---- タブ (IMP-TRT-004) ----
        //
        // **手元メニューは「持っている物を使う場所」。** 買う場所は棚のまま
        // (hand-menu §0 / §9 — 購入の一本化は 2026-09-03 に「しない」で確定した)。

        /// <summary>タブの見出し。**動詞ではなく物の名前**にする —
        /// 「出す / 呼ぶ / つける」は行のボタンが持っており、
        /// タブは面の切り替えであって操作ではない。</summary>
        public const string WordTabTreat = "おやつ";
        public const string WordTabToy = "おもちゃ";
        public const string WordTabPetFx = "パーティクル";
        /// <summary>4 タブ目 (IMP-DOG-019 Phase 5)。**動詞ではなく物の名前**という
        /// 上の規律に合わせて「呼ぶ」ではなく「ねる」— 行のボタンが動詞を持つ。</summary>
        public const string WordTabSleep = "呼ぶ";

        // ---- ねるタブ (IMP-DOG-019 Phase 5) ----
        //
        // **犬 4 匹ぶんの行 ×1。並びはおもちゃタブと同じ dogIndex 順**で、
        // 行頭の丸も同じボールの色にする — 同じ板の中で「どの犬か」の手がかりを
        // タブごとに変えない (識別層の主役は色と大きさ — IMP-FET-009)。

        /// <summary>空いている犬の行のボタン。**押すと自分のそばへ呼んで寝かせる。**</summary>
        public const string WordSleepCall = "ポーズを選ぶ";
        /// <summary>解除。**自分の解除にも権限者の「取り上げ」にも同じ字を使う** —
        /// 起きる犬から見れば同じことが起きるので、押し手で言葉を変えない。</summary>
        public const string WordSleepWake = "自由にする";
        /// <summary>自分が寝かせている行の状態。</summary>
        public const string WordSleepingSelf = "あなたのそばにいる";
        /// <summary>他人が寝かせている行の状態 (名前 + これ)。
        /// **占有を外から読めるようにする口をこの板に決めた** (DES-DOG-003 §2.1 ⑥)。</summary>
        public const string WordSleepingSuffix = "さんのそばにいる";
        /// <summary>名前を出せないときの代わり (**表示名の字がアトラスに無い**場合)。
        /// 下の <see cref="AsciiNameOrNull"/> を参照。</summary>
        public const string WordSleepingSomeone = "だれかのそばにいる";
        /// <summary>権限者だけに出るトグル。**点灯 = 開放中。**</summary>
        public const string WordSleepOpenToAll = "みんなにも開放";
        /// <summary>権限も開放も無いときの理由。**押せない理由を無言にしない**
        /// (おもちゃタブの理由テキストと同じ作法 — hand-menu §3-2)。</summary>
        public const string WordSleepOwnerOnly = "オーナーだけが呼べます (設定で全員に開放できます)";

        // ---- ライブタブ (IMP-PRG-004 Phase 4) ----
        public const string WordTabLive = "ライブ";
        /// <summary>所有している作品の行のボタン。押すと開催し、状態を板に表示する。</summary>
        public const string WordLiveHost = "開催";
        /// <summary>`〇〇さんが開催中  残り N 秒`。</summary>
        public const string WordLiveHostingSuffix = "さんが開催中";
        public const string WordLiveHostingSomeone = "だれかが開催中";
        public const string WordLiveRemain = "残り";
        /// <summary>再生完了後の 60 秒待機 (DES-PRG-002 §1.1)。「次の開催まで あと N 秒」。</summary>
        public const string WordLiveWait = "次の開催まで あと";
        public const string WordLiveSeconds = "秒";
        public const string WordLiveReady = "開催できます";
        /// <summary>P7 (2026-09-16): 未所持の行のボタン。押すと詳細の面 (買う前に雰囲気が分かる)。</summary>
        public const string WordLiveMore = "くわしく";
        public const string WordLiveSeeFx = "パーティクルで見る";
        public const string WordLiveSeeAcc = "かざりで見る";
        /// <summary>IMP-PRG-005: Pack の一部の行に付ける札。同じ商品を指す行が 2 つ以上あれば付く
        /// (行の値段が単曲価格に見えないように — Codex 指摘 2026-09-06)。ASCII なので焼き済み。</summary>
        public const string WordLivePack = "[Pack]";
        /// <summary>表示名を切り詰めたときの印。**枠を溢れさせない** (vrc-tmp-font §4)。</summary>
        public const string NameEllipsis = "…";

        // ---- かざりタブ (DES-DOG-004) ----
        //
        // **6 タブ目。見出しは 3 字** — 板は 760px のままで 6 枚を 1 段に収めるため、
        // タブ 1 枚が 120px しかない (28pt × 4 字 = 112px が上限)。
        //
        // 構成は **2 ページ (品目 → 犬)**。他のタブが「犬 6 行 1 枚」で足りるのに対し、
        // 飾りは **犬 6 × 枠 5 × 品目** の三次元なので 1 枚に平らには載らない。
        // 先に飾りを選び、次にどの子へ着けるかを選ぶ (2026-09-07 ユーザー決定)。

        /// <summary>タブの見出し。**動詞ではなく物の名前**という規律に合わせて
        /// 「きせる」ではなく「かざり」。</summary>
        public const string WordTabAcc = "かざり";
        /// <summary>7 枚目のタブ (IMP-INF-006 P2、2026-09-16)。犬目線カメラ (Point) とばにまるトレーナー (月額) の 2 群。
        /// 面の中身は KD_ActivitiesMenu が描く。</summary>
        public const string WordTabPlay = "あそぶ";

        /// <summary>1 ページ目の行のボタン。押すと 2 ページ目 (犬を選ぶ) へ。</summary>
        public const string WordAccChoose = "えらぶ";
        /// <summary>2 ページ目から戻る。</summary>
        public const string WordAccBack = "もどる";
        /// <summary>行の [ギフト] の Editor 既定文 (IMP-PRG-012)。実行時は文言表 product.gift。</summary>
        public const string WordGift = "ギフト";
        /// <summary>2 ページ目、着けられる犬の行のボタン。</summary>
        public const string WordAccPut = "つける";
        /// <summary>2 ページ目、既にその飾りが着いている行のボタン。</summary>
        public const string WordAccTakeOff = "はずす";
        /// <summary>待ち時間中の行。`あと N 秒`。**押せない理由を無言にしない**
        /// (おもちゃタブの理由テキストと同じ作法 — hand-menu §3-2)。</summary>
        public const string WordAccWaitPrefix = "あと";
        public const string WordAccWaitSuffix = "秒";
        /// <summary>同じ枠に別の飾りが着いている行。**置き換わることを先に言う** —
        /// 王冠と麦わら帽子は同じ頭枠で排他 (ACC-005)。</summary>
        public const string WordAccReplaces = "とりかえ";
        /// <summary>まだ持っていない飾り。**買う口はここに作らない** —
        /// 実物が棚に並ぶので「買う = 棚」のまま (hand-menu §9)。</summary>
        public const string WordAccNotOwned = "棚で買える";

        // ---- おもちゃタブ (IMP-FET-011) ----

        /// <summary>行のボタン。**「戻す」ではなく「呼ぶ」** —
        /// 初期位置へ戻すのをやめて、押した人の目の前へ来るようにしたので、
        /// 文言のほうも実際に起きることへ合わせる。</summary>
        public const string WordRecall = "手元へ";
        /// <summary>誰かが持っている。**手から奪わない** (旧リセットボタンと同じ)。</summary>
        public const string WordHeldByOther = "だれかが持っている";
        /// <summary>犬が追っている / くわえて帰っている。**旧リセットは Chase 中を
        /// 素通ししていた**が、こちらは `IsIdle` で見るので追跡を横から折れない。</summary>
        public const string WordDogBusy = "犬が遊んでいる";

        // ---- なでタブ (IMP-PET-007) ----

        /// <summary>所持している行のボタン。</summary>
        public const string WordEquip = "つける";

        /// <summary>未所持で残高が足りる行のボタン。**押しても即決済しない** —
        /// 下の確認帯が開くだけ (2 段階購入は棚と同じ規律)。
        ///
        /// > ⚠ **ここだけは「購入をメニューへ入れない」の例外。**
        /// > 撫でエフェクトの `KD_ShopItem` は `menuOnly` で **Collider を持たない**ので、
        /// > 廃止した近接メニューが**唯一の購入口**だった。だから「口が 2 本になる」に
        /// > あたらず、**廃止した板の仕事をそのまま引き継いでいるだけ**。
        /// > 2026-09-03 ユーザー決定 (実装中に穴が判明して諮った)。</summary>
        public const string WordBuyHere = "買う";
        public const string WordPutAway = "しまう";
        /// <summary>枠が満杯 = **4 人目**。完了条件「無言で失敗しない」の主役。</summary>
        public const string WordSlotsFull = "いま出せません";
        public const string WordAlreadyOut = "もう出している";
        public const string WordNoStock = "おやつがありません";
        /// <summary>在庫 0 だが残高は足りている。**「あと N pt」と別の文言にする** —
        /// 待てば解決するのか、棚へ行けば解決するのかが逆 (棚の `WordEnough` と同じ整理)。</summary>
        public const string WordBuyAtShelf = "棚で買える";
        /// <summary>Ownership 待ち。**待っている間を無言にしない。**</summary>
        public const string WordDispensing = "出しています";
        public const string WordHolding = "「閉じる」を押してからあげられます";
        public const string WordRecalling = "もどしています";
        /// <summary>枠の取り合いに負けた / 移譲が来ない。**在庫は減っていない。**</summary>
        public const string WordSlotLost = "枠を取れなかった";
        /// <summary>実体は出たが消費に失敗した。**実体は回収され、在庫も減っていない。**</summary>
        public const string WordCommitFailed = "出せなかった";
        /// <summary>Desktop 用の表示・呼び戻しの案内。**VR では出さない** (スティックの案内は
        /// おじいさんの説明側に置く。板の中に操作説明を積み上げない)。</summary>
        public const string WordToggleHint = "で開く・閉じる";
        /// <summary>板の右上。**[しまう] とは別物** — こちらは板を消すだけで枠に触らない。</summary>
        public const string WordClose = "閉じる";
        /// <summary>IMP-TRT-006: Touch (スマホ) 用の画面端の起動ボタンの字。**タブの見出しと同じく物の名前**
        /// (開く・手元へ呼び戻す入口として名前を表示する)。</summary>
        public const string WordTouchLauncher = "メニュー";

        // ============================================================
        // 配線
        // ============================================================

        [Header("配線 (Tools/KominkaDogRun/Treat System が設定する)")]
        [Tooltip("ルールの正本。**門番も遷移表もここから読む。** null だと何も出せない")]
        public KD_TreatBox box;

        [Tooltip("台帳。復元済みかと残高、そして**在庫を 1 個減らす唯一の口**")]
        public KD_ProgressLedger ledger;

        [Tooltip("おやつ 4 種。**添字 = 行番号**。KD_ShopCatalog の部分集合")]
        public KD_ShopItem[] items;

        [Header("板 (既定は非アクティブ)")]
        [Tooltip("開閉する板の root。**閉じたまま保存すること** — " +
                 "開いたまま保存すると入室した瞬間から板が浮く")]
        public GameObject panelRoot;
        public KD_HintDirector hints;
        public KD_CallPose callPose;

        // ============================================================
        // ようこそ (IMP-INF-020 案 1 + 2、2026-09-17)
        //
        // **新しい UI は作らない。この板を入場時に庭の決まった位置へ開いた状態で置き、最初の面だけ「ようこそ」にする。**
        // 出す条件は KD_HintDirector が決める (復元後、案内を離れた印が無い人だけ)。板はここで置き方と面を持つ。
        // 面を離れる操作 (タブ / 設定 / [閉じる] / [あとで] / [メニューを見る] / 本人の呼び出し) で印を立て、以後この面は出ない。
        // 「庭に置いた」だけでは印を立てない (見なかった人には次回も出す)。
        // ============================================================

        [Header("ようこそ (IMP-INF-020)")]
        [Tooltip("庭に開いた状態で置く位置と向き (KD_Places/KD_WelcomeSpot)。**板 (panelRoot) の中心がここへ来る**。" +
                 "向きは「板の +Z = 読者から板へ向かう方向」(PlaceInFront と同じ)。位置は人が Inspector で詰める")]
        public Transform welcomeSpot;

        [Tooltip("初回だけ出す「ようこそ」の面 (Page_Welcome)。表示中はタブの面と設定の面を隠し、タブは非選択で見せる")]
        public GameObject welcomePage;

        [Tooltip("ようこその足元の 1 行 (今の端末の呼び方 + もう一度読む場所)。入力方式が変わると描き直す")]
        public TextMeshProUGUI welcomeFooterLabel;

        /// <summary>ようこその面を出している。ローカル状態。</summary>
        private bool _welcome;
        /// <summary>この滞在で庭に置いた (二度は置かない)。</summary>
        private bool _welcomeShown;
        /// <summary>庭に据えたまま (ようこそを離れても板は庭に残る)。閉じるか手元へ呼び戻すと false。
        /// KD_HintDirector が「集中中」を距離で判定するのはこの間だけ (Codex 指摘 2026-09-17)。</summary>
        private bool _gardenPlaced;
        /// <summary>自分の操作 (右スティック / T / 画面の [メニュー]) で開いた・呼び戻した。
        /// **庭に置いた板を触っただけでは立たない** — 手元ヒント (KD_HintDirector) が消える条件はこちら。</summary>
        private bool _selfOpened;

        [Tooltip("「もっている N pt」。復元前は「よみこみ中」")]
        public TextMeshProUGUI balanceLabel;

        [Tooltip("行の文言 (品名 + 残り個数 + 在庫の理由)。**items と添字で対応**")]
        public TextMeshProUGUI[] rowLabels;

        [Tooltip("行の [出す] ボタン。**出せない行では隠す** — " +
                 "押しても無反応なボタンは、壊れているのか仕様なのかが読めない")]
        public GameObject[] rowButtons;

        [Tooltip("**理由コードの出し先。** 未配線だと理由があっても無言になるので、" +
                 "Verify が配線を機械検査する")]
        public TextMeshProUGUI statusLabel;

        [Tooltip("Desktop 用の「T で開く・閉じる」の案内。**VR では自動で隠す**")]
        public TextMeshProUGUI closeHintLabel;

        [Tooltip("[しまう]。**出している間だけ出す。** 犬が来なくても枠が返せる " +
                 "(回収は本番と同じ KD_TreatSlot._Release() を通る)")]
        public GameObject putAwayButton;

        [Tooltip("[とじる] (板の右上)。**常時出す。** [しまう] と違い枠にも在庫にも触らない — " +
                 "押しても出したおやつは手に残る")]
        public GameObject closeButton;

        [Header("Touch: 画面端の起動ボタン (IMP-TRT-006)")]
        [Tooltip("Screen-Space Overlay の Canvas (ボタン 1 個)。**既定は非アクティブ**で、" +
                 "入力方式が Touch のときだけ実行時に出す。VR / Desktop では出さない。\n" +
                 "押すと _Toggle() — 既存の板を開く・手元へ呼び戻す")]
        public GameObject touchLauncher;
        public RectTransform touchLauncherButton;
        public const float TouchLauncherEdgeMargin = 24f;
        private bool _hasScreenOrientation;
        private bool _portraitScreen;
        private bool _launcherLayoutSet;
        private bool _lastLauncherPortrait;


        [Tooltip("開発用: ClientSim は入力方式を Generic としか返さないので、Touch の見た目を " +
                 "エディタで確かめるときだけ true。**公開時は false** (Verify が数える)")]
        public bool debugForceTouch;

        // ============================================================
        // タブ (IMP-TRT-004)
        //
        // **完全ローカル / 同期なし / 保存なし。** 既定は必ず「おやつ」で、
        // 入室直後に別タブが開いていると**いま唯一ある機能が一段奥に隠れる**。
        //
        // ⚠ **選択中を `Button.interactable` で表さない。**
        // Avatar Idle Arena `IMP-UI-009` が踏んだ罠 — この板は `interactable = false` を
        // 「今は押せない」の意味でおもちゃ行に使うので、同じ見た目へ真逆の意味
        // (「今これが選ばれている」) を乗せると読み方が壊れる。
        // **選択中は色 (点灯) と形 (下線) の 2 つで表す。**
        // ============================================================

        public const int TabTreat = 0;
        public const int TabToy = 1;
        public const int TabPetFx = 2;
        // 裏山改変: タブ 2 は さんぽ (計画書 D-2)。撫でFX は かざり (TabAcc) に合流し、描画は KD_TreatPouch.Walk.cs。
        // TabPetFx は移植コードのコンパイル互換のため残すが、裏山のタブ切替では使わない (復帰条件なし)。
        public const int TabWalk = 2;
        /// <summary>4 タブ目 (IMP-DOG-019 Phase 5)。**hand-menu §9 の却下
        /// 「タブを 4 つ目 (設定 / 写真) へ広げる」には当たらない** —
        /// あれが却下したのは「使う」以外を持ち込むことで、
        /// そばで寝るのは**持っている物ではないが自分の操作に閉じた「使う」**
        /// (呼ぶ / 起こすは自分の周りだけで完結する)。
        /// **縦は 520px のまま。**タブの横幅を詰めて 4 枚を 1 段に収める。</summary>
        public const int TabSleep = 3;
        /// <summary>5 タブ目 (IMP-PRG-004 Phase 4)。ParticleLive を**開催する**場所 —
        /// 「持っている物を使う」(hand-menu §0) に当たる。単品 1 曲と 5 曲 Pack はここで買う (IMP-PRG-005)
        /// (`menuOnly` で実物が無く、売り場が 1 つしかない = 撫でエフェクトと同じ例外)。
        /// **縦は 520px のまま。**タブを 140px に詰めて 5 枚を 1 段に収める。</summary>
        public const int TabLive = 4;
        /// <summary>6 タブ目 (DES-DOG-004)。共有の犬へ**自分が持っている飾りを着ける**場所。
        ///
        /// ⚠ **hand-menu §9 の却下「おもちゃの選択もタブへ入れる」を部分的に撤回して置いている。**
        /// あの却下の理由は「世界に出る共有物を切り替える操作は棚が正しい /
        /// 手元メニューは自分の操作に閉じる」で、飾りは**まさに共有の犬の見た目を全員に対して変える**。
        /// それでもここへ置くのは、①飾りは棚に実物を並べても「どの犬に着けるか」を選べず
        /// 導線が棚で閉じない ②[DES-DOG-004 §1.1](../../../docs/development/issues/DES-DOG-004-accessories.md)
        /// の 60 秒ルールが**共有物を全員が触る前提で設計されている** = 「自分の操作に閉じない」ことが
        /// 仕様に織り込み済み、の 2 つ (2026-09-07 ユーザー決定)。
        /// **買う口はここに作らない** — 撫でエフェクトと違い飾りは棚に実物が並ぶので、
        /// 「買う = 棚 / 使う = 手元」は保たれる。
        /// **縦は 520px のまま。**タブを 120px に詰めて 6 枚を 1 段に収める。</summary>
        public const int TabAcc = 5;
        /// <summary>あそぶ (IMP-INF-006 P2、2026-09-16)。2026-09-11〜16 は上段の [あそぶ] から開く設定の面だった。</summary>
        public const int TabPlay = 6;
        public const int TabCount = 7;

        [Header("タブ (添字 = Tab*)")]
        [Tooltip("タブごとの中身。**表示中の 1 枚だけ active**。" +
                 "残高と [とじる] はこの外に置く (どのタブでも読める)")]
        public GameObject[] tabPages;

        [Tooltip("タブ見出しの背景。**選択中だけ点灯色にする** (色は Udon 公開済みの " +
                 "Image.color setter で書く)。角丸にはしない — タブは押せる面ではなく見出し")]
        public UnityEngine.UI.Image[] tabImages;

        [Tooltip("タブの下線。**選択中だけ active**。色だけだと明度差が弱い環境で読めない")]
        public GameObject[] tabUnderlines;

        [Tooltip("選択中のタブの色")]
        public Color tabOnColor = new Color(0.42f, 0.36f, 0.26f);

        [Tooltip("選択中でないタブの色")]
        public Color tabOffColor = new Color(0.20f, 0.18f, 0.16f);

        // ============================================================
        // ライブタブ (IMP-PRG-004 Phase 4)
        //
        // **台帳を読むだけ。** 開催できるか (`CanStart`) ・誰が開催中か ・残り秒は
        // `KD_LiveSystem` が正本で、板は問い合わせて描くだけ。
        // 所有は `KD_ShopItem.Owned()` (Point の買い切り。単品 1 曲 + 5 曲 Pack — IMP-PRG-005)。
        // ============================================================

        [Header("ライブ (IMP-PRG-004 Phase 4)")]
        [Tooltip("Live の台帳。**開催・残り秒・待機はここが正本**。null ならタブは暗いまま")]
        public KD_LiveSystem live;

        [Tooltip("作品ごとの商品 (menuOnly)。**添字 = liveId** (= KD_LiveSystem.stageDirectors の添字)。" +
                 "Treat Setup が KD_DogDefs.LiveShows の productKey でカタログから引く。**Pack の行は同じ商品を指す**")]
        public KD_ShopItem[] liveItems;

        [Tooltip("行に出す曲名 (KD_DogDefs.LiveShows.title)。**添字 = liveId**。空なら商品名で代用")]
        public string[] liveTitles;
        public string[] liveTitleKeys;

        [Tooltip("行の曲名")]
        public TextMeshProUGUI[] liveLabels;

        [Tooltip("行の 2 行目 ([Pack] / 値段 / あと N pt)。おもちゃタブの理由ラベルと同じ置き方")]
        public TextMeshProUGUI[] liveSubLabels;

        [Tooltip("行のボタン ([開催] か [買う])。未所持で買えないときは隠す")]
        public GameObject[] liveButtons;
        public UnityEngine.UI.Button[] liveButtonComponents;
        public UnityEngine.UI.Image[] liveButtonImages;
        public TextMeshProUGUI[] liveButtonLabels;
        [Tooltip("行の [ギフト] (IMP-PRG-012)。所有済みの買い切り CE (Pack / キャンディ / パンプキン) の一覧の行だけ出す。押すと贈る確認カード → 同じ Listing を開く")]
        public GameObject[] liveGiftButtons;
        public TextMeshProUGUI[] liveGiftLabels;

        [Tooltip("状態の 1 行 (〇〇さんが開催中 残り N 秒 / 次の開催まで あと N 秒)")]
        public TextMeshProUGUI liveStatusLabel;

        // ---- P7 / P8 (IMP-INF-006、2026-09-16): 一覧の行 = 商品、未所持は詳細の面 ----
        [Tooltip("一覧の行 = 商品 (KD_DogDefs.LiveProducts の順 = 安い → 高い、CE は最後。Pack は 1 行)。開催する作品 (liveId) は liveItems から引く")]
        public KD_ShopItem[] liveProducts;
        [Tooltip("詳細の面 (Page_Live/LiveDetail)。買う前に雰囲気・見どころ・曲・同梱が分かる。試聴は置かない")]
        public GameObject liveDetailRoot;
        public TextMeshProUGUI liveDetailTitle, liveDetailMeta, liveDetailMood, liveDetailScene, liveDetailSongs, liveDetailFx, liveDetailAcc, liveDetailNote, liveDetailActionLabel, liveDetailPosterLabel;
        public GameObject liveDetailBack, liveDetailAction, liveDetailSongsRow, liveDetailFxRow, liveDetailAccRow;
        [Tooltip("詳細の参考画像 (liveProducts と同じ添字。Textures/LiveThumbs/<productKey>.png。無ければ色面 + 準備中)")]
        public Sprite[] liveThumbs;
        public UnityEngine.UI.Image liveDetailPosterImage;
        public UnityEngine.UI.Button liveDetailActionButton;
        public UnityEngine.UI.Image liveDetailActionImage;
        // ---- P10 (IMP-INF-006、2026-09-16): ヘッダとタブの間のインフォメーション帯。イベントがあるときだけ 1 件 ----
        [Tooltip("インフォメーション帯 (EventBand)。ハロウィン限定ライブの販売中だけ出て、押すとパンプキン・パレードの詳細へ")]
        public GameObject eventBandRoot;
        public TextMeshProUGUI eventBandLabel;
        private int _livePage;           // LivePageList / LivePageDetail / LivePageSongs
        private int _liveProduct = -1;   // 詳細・曲を選ぶ面で見ている商品 (liveProducts の添字)

        [Tooltip("2 段階購入の確認帯 (なでタブと同じ型)")]
        public GameObject liveConfirmRoot;
        public TextMeshProUGUI liveConfirmLabel;
        public GameObject liveConfirmYes;
        public GameObject liveConfirmNo;

        private int _liveConfirmRow = -1;
        /// <summary>贈る確認を開いている商品 (liveProducts の添字)。購入の確認 (_liveConfirmRow) とは別に持つ — 所有で閉じない (IMP-PRG-012)。</summary>
        private int _liveGiftRow = -1;

        // ============================================================
        // かざりタブ (DES-DOG-004)
        //
        // **台帳を読むだけ。** 何が着いているか・誰が最後に変えたか・あと何秒かは
        // <see cref="KD_AccessoryBox"/> が正本で、板は問い合わせて描く。
        // 所有は `KD_ShopItem.Owned()` (無料 / Point / CE の三層とも同じ口)。
        //
        // 商品一覧 → 所持セットの装着方法 → セット内の品目 → 犬。
        // `_accProduct >= 0` は犬選択、`_accSetMenu` はセットの中を選ぶ階層。
        // ページはローカル状態で、同期も保存もしない。
        // ============================================================

        [Header("かざり (DES-DOG-004)")]
        [Tooltip("飾りの台帳。**着いている物・60 秒の待ちはここが正本**。null ならタブは暗いまま")]
        public KD_AccessoryBox accessories;

        [Tooltip("飾りごとの商品。**添字 = 行番号 = accProductIds の添字**。所有判定に使う")]
        public KD_ShopItem[] accItems;

        [Tooltip("単品またはセットの恒久 ID。添字 = メニュー行番号。セットは KD_AccessoryBox.setIds に対応")]
        public int[] accProductIds;

        [Tooltip("飾りの表示名 (王冠 / 麦わら帽子 …)。**添字 = 行番号**")]
        public string[] accNames;
        public string[] accNameKeys;

        [Tooltip("飾りの装着枠名 (頭 / 胸・首元 / 尻尾)。行の 2 行目に出す。**添字 = 行番号**")]
        public string[] accSlotNames;
        public string[] accSlotNameKeys;

        [Tooltip("1 ページ目 = 飾りの行 / 2 ページ目 = 犬の行。**同じ 6 行を使い回す**")]
        public GameObject[] accRows;
        public TextMeshProUGUI[] accRowLabels;
        public TextMeshProUGUI[] accRowSubLabels;
        public GameObject[] accButtons;
        public UnityEngine.UI.Button[] accButtonComponents;
        public TextMeshProUGUI[] accButtonLabels;
        [Tooltip("1 ページ目の行の [ギフト] (IMP-PRG-012)。所有済みの買い切り CE セット (王様 / 夏 / キャンディ / パンプキン) だけ出す。2 ページ目 (犬) では全部隠す")]
        public GameObject[] accGiftButtons;
        public TextMeshProUGUI[] accGiftLabels;

        [Tooltip("2 ページ目の見出し (選んだ飾りの名前) と [もどる]")]
        public GameObject accBackButton;
        public TextMeshProUGUI accHeaderLabel;

        /// <summary>2 ページ目で選んでいる飾りの行番号。**-1 = 1 ページ目。**
        /// ローカル状態 — 同期も保存もしない。</summary>
        private int _accProduct = -1;
        private int _accSet = -1; // Catalog index of the owned set being explored.
        private int _accSetMenu; // 0 catalog/dogs, 1 equip mode, 2 this set's three members.
        // IMP-DOG-026: display-only meshes. Product-major, then dog, like accessories.nodes.
        public GameObject[] accPreviewDogs;
        public GameObject[] accPreviewItems;
        public GameObject accConfirmRoot;
        public TextMeshProUGUI accConfirmLabel;
        public GameObject accConfirmYes;
        private bool _accConfirm;

        // ============================================================
        // おもちゃタブ (IMP-FET-011)
        //
        // **待機中のボールだけ呼べる。新しい状態は 1 つも作らない** —
        // `!ball.IsHeldNow ∧ fetch.IsIdle` で既存の同期値 2 つの AND から導出する。
        // ============================================================

        [Header("おもちゃ (添字 = 行番号。KD_DogPack の dogIndex 順)")]
        [Tooltip("犬ごとのボール。**IsHeldNow を読むだけ** — Fetch のコードは 1 行も変えない")]
        public KD_BallController[] balls;

        [Tooltip("犬ごとの Fetch。**IsIdle を読むだけ**。" +
                 "null だと「犬が遊んでいるか」が分からないので行を出さない")]
        public KD_DogFetchController[] toyFetchers;

        [Tooltip("行に出す名前 (「コーギーのボール」等)。**balls と添字で対応**")]
        public string[] toyNames;
        public string[] toyNameKeys;

        [Tooltip("行の名前 (犬の名前)。**「のボール」は付けない** — " +
                 "タブが「おもちゃ」なので要らないし、340px の行に理由まで入れると溢れる")]
        public TextMeshProUGUI[] toyLabels;

        [Tooltip("押せない理由。**名前とは別のラベル**にしてある (幅が足りない)。" +
                 "押せるときは空文字")]
        public TextMeshProUGUI[] toyReasonLabels;

        [Tooltip("行の [呼ぶ]。**隠さない** — おやつと違い 4 行あること自体が情報で、" +
                 "隠すと並びが動いて押し間違える。押せないときは暗くする")]
        public GameObject[] toyButtons;

        [Tooltip("[呼ぶ] の Button。**押せないときに interactable を落とす。** " +
                 "タブでは絶対に使わない (意味が 2 つになる)")]
        public UnityEngine.UI.Button[] toyButtonComponents;

        [Tooltip("[呼ぶ] の背景。押せないときに暗くする")]
        public UnityEngine.UI.Image[] toyButtonImages;

        [Tooltip("押せるときの [呼ぶ] の色")]
        public Color recallOnColor = new Color(0.35f, 0.50f, 0.30f);

        [Tooltip("押せないときの [呼ぶ] の色。**消灯であって不可視ではない**")]
        public Color recallOffColor = new Color(0.22f, 0.22f, 0.22f);

        [Tooltip("m。呼んだボールを目の前のどれだけ先へ置くか。" +
                 "**近いと足元に落ち、遠いと壁の中へ入る** — 実機で詰める")]
        public float recallDistance = DefaultRecallDistance;

        [Tooltip("秒。同じボールを連打で呼べない間隔 (旧リセットボタンと同じ 2.5s)")]
        public float recallCooldown = DefaultRecallCooldown;

        // ============================================================
        // ねるタブ (IMP-DOG-019 Phase 5)
        //
        // **台帳を読んで、枠へ呼ぶ / 返すだけ。** 犬にも Ownership にも触らない
        // (取ってよいのは枠と台帳だけで、それは `_TryCall` / `_Release` /
        // `_SetOpenToAll` の中にある — IMP-NET-005 の不変条件 3)。
        //
        // **同期変数は 1 本も増えない。** 出す絵はすべて枠の同期値からの導出で、
        // この板は `SyncMode None` のまま (hand-menu §7)。
        // ============================================================

        [Header("そばで寝る (IMP-DOG-019。添字 = 行番号 = dogIndex)")]
        [Tooltip("寝る台帳。**権限も占有もここが正本** — 判定の式をこの板へ写さない " +
                 "(写すとオーナー不在のインスタンスでの挙動が場所ごとに割れる)。" +
                 "null なら 4 行とも「よみこみ中」で暗いまま")]
        public KD_SleepBoard sleepBoard;
        public KD_SleepPlacement sleepPlacement;

        [Tooltip("行の名前 (犬の名前)。**おもちゃタブと同じ toyNameKeys を出す** — " +
                 "同じ犬の呼び名を板の中で 2 つ持たない")]
        public TextMeshProUGUI[] sleepLabels;

        [Tooltip("行の状態 (`いっしょに寝ている` / `〇〇さんと寝ている`)。" +
                 "**名前とは別のラベル**にしてあり、行の 2 行目を丸ごと使う " +
                 "(名前が入るので、おもちゃの理由ラベルより広い枠が要る)")]
        public TextMeshProUGUI[] sleepStatusLabels;

        [Tooltip("行のボタン。**隠さない** — おもちゃタブと同じで、" +
                 "4 行あること自体が「どの犬が空いているか」の一覧になっている")]
        public GameObject[] sleepButtons;

        [Tooltip("行のボタンの Button。**押せないときに interactable を落とす**")]
        public UnityEngine.UI.Button[] sleepButtonComponents;

        [Tooltip("行のボタンの背景。押せないときに暗くする。" +
                 "**点灯 / 消灯の色はおもちゃ行と共有する** " +
                 "(recallOnColor / recallOffColor — 同じ板の中で「押せる / 押せない」に " +
                 "2 つの色体系を作らない)")]
        public UnityEngine.UI.Image[] sleepButtonImages;

        [Tooltip("行のボタンの文言。**[一緒に寝る] と [起こす] を切り替える先**")]
        public TextMeshProUGUI[] sleepButtonLabels;

        [Tooltip("[呼ぶを全員に許可] トグル (P5: 設定ホーム)。**隠さず、権限が無い人には押せない** (openToAllButtonComponent.interactable)。" +
                 "点灯 = 開放中 (`KD_SleepBoard.IsOpenToAll`)。2026-09-16 までは呼ぶタブで権限者にだけ出していた")]
        public GameObject openToAllButton;
        [Tooltip("P5: 設定ホームの [呼ぶを全員に許可]。権限が無い人には押せない (隠さない — ライブ開催許可と同じ見せ方)")]
        public UnityEngine.UI.Button openToAllButtonComponent;

        [Tooltip("[みんなにも開放] の背景。**点灯 / 消灯で今の状態を出す** — " +
                 "押せるものなので角丸 + 枠 (hand-menu §5)")]
        public UnityEngine.UI.Image openToAllImage;

        [Tooltip("押せない理由 (`オーナーだけが呼べます`)。" +
                 "**4 行が暗いだけだと理由が読めない**ので、板の中に 1 行置く")]
        public TextMeshProUGUI sleepNoteLabel;

        [Tooltip("開放中の [みんなにも開放] の色")]
        public Color openOnColor = new Color(0.62f, 0.50f, 0.24f);

        [Tooltip("開放していないときの色。**消灯であって不可視ではない**")]
        public Color openOffColor = new Color(0.30f, 0.28f, 0.24f);

        // ============================================================
        // なでタブ (IMP-PET-007)
        //
        // **切り替えるだけ。買わない。** 押すとカタログの選択が変わり、
        // 既存の `KD_PetFxBox._OnShopSelectionChanged` がそのまま走る。
        // ============================================================

        [Header("撫でエフェクト (添字 = 行番号)")]
        [Tooltip("選択の口。**`_RequestSelect` を叩くだけ** — 購入経路は持たない")]
        public KD_ShopCatalog catalog;

        [Tooltip("いま自分に付いているものを読む先 (「使用中」の判定)")]
        public KD_PetFxBox petFxBox;

        [Tooltip("撫でエフェクト 6 種。**棚と同じ KD_ShopItem を指す** — 値段の正本を 2 つにしない")]
        public KD_ShopItem[] fxItems;

        [Tooltip("行の文言 (品名 + 状態)")]
        public TextMeshProUGUI[] fxLabels;
        public TextMeshProUGUI[] fxSubLabels;

        [Tooltip("行のボタン。所持していれば [つける] / 買えるなら [買う] / " +
                 "どちらでもなければ**隠す** (押しても何も起きないボタンを出さない)。" +
                 "品目は常に 6 行あるので、隠しても並びは動かない")]
        public GameObject[] fxButtons;

        [Tooltip("行のボタンの文言。**[つける] と [買う] を切り替える**")]
        public TextMeshProUGUI[] fxButtonLabels;

        [Header("撫でエフェクトの確認帯 (2 段階購入)")]
        [Tooltip("**押した瞬間には払わない。** 帯が開いて [購入]/[やめる] を選ぶまで pt は減らない " +
                 "— 棚の確認パネルと同じ規律 (売り場の形は違ってよいが確認の型は 1 つ)")]
        public GameObject fxConfirmRoot;

        [Tooltip("「キラキラ  16pt」。**値段はここだけが出す** (行は状態を出す)")]
        public TextMeshProUGUI fxConfirmLabel;

        [Tooltip("[購入]。**押した行の品を買う唯一の口**")]
        public GameObject fxConfirmYes;

        [Tooltip("[やめる]")]
        public GameObject fxConfirmNo;

        // ============================================================
        // 置き場所
        // ============================================================

        [Header("置き場所 (VR / Desktop 共通)")]
        [Tooltip("この目線高さ (m) を 1.0 倍とみなす。**ずらしはすべてこの比で伸縮する**")]
        public float referenceEyeHeight = DefaultEyeHeight;

        [Tooltip("開いたとき、**板をこの距離 (m) だけ目の前へ据えて世界座標で固定する**。" +
                 "身長で伸縮。**追従させると照準が合わない** (下の PlaceInFront のコメント)")]
        public float panelDistance = DefaultPanelDistance;

        [Tooltip("据えるときの目線からの高さのずらし (m)。負 = 少し下")]
        public float panelHeightOffset = -0.05f;

        // 裏山改変 (IMP-UI-004 案 B、2026-10-08): 板の下端が地面に埋まらない高さへ持ち上げる
        [Tooltip("板の下端と地面の間に必ず空ける高さ (m)。**身長の比では伸縮しない**。" +
                 "据えるときに下端がこれより地面に近ければ、その差だけ板を上げる (下げる方向には動かさない)")]
        public float panelGroundClearance = DefaultPanelGroundClearance;

        [Tooltip("板の下端より下へはみ出す要素がある面のための余白 (m、ワールド)。" +
                 "2026-10-08 時点の板 (1140 × 700 px) は、どの面も板の矩形の内側に収まっているので 0")]
        public float panelBottomExtra = 0f;

        [Tooltip("持ち上げたときだけ、板の上端を頭の方へ倒す上限 (度)。見上げても読めるように。" +
                 "**0 で無効** (持ち上げても垂直のまま)。持ち上げていないときは常に垂直")]
        public float panelTiltMax = DefaultPanelTiltMax;

        [Tooltip("地面として扱うレイヤー。**Default + Environment** (犬・フリスビー・写真の地面レイと同じ流儀。" +
                 "板自身 (17 Walkthrough)・犬 (14)・プレイヤーには当たらない)。trigger は無視する")]
        public LayerMask panelGroundMask = (1 << 0) | (1 << 11);

        [Header("Desktop: キーで表示")]
        [Tooltip("このキーで開く / 手元へ呼び戻す (既定 = T)。**Desktop の唯一の導線** — " +
                 "**Tab は使えない** — VRChat 2026.3.3 (2026-09 Open Beta) から Desktop の Tab 長押しが " +
                 "HUD 操作 / Player・Portal・Prop・Print 選択に割り当てられた (2026-09-15 に T へ変更)。" +
                 "F も不可 (右クリック + F = 投げる)。Q / E は寝床配置の回転が使う。\n" +
                 "触る実物は持たない (ポーチは 2026-09-01 に廃止)。\n" +
                 "**`Input.GetKeyDown` は Udon に公開されている** — 2026-08-24 に " +
                 "ノード登録で実測 (`UnityEngineInput.__GetKeyDown__UnityEngineKeyCode__SystemBoolean`)。\n" +
                 "**VR では読まない** — あちらは右スティックで開く")]
        public KeyCode desktopToggleKey = KeyCode.T;

        // ---- VR: 右スティック下の長押し (2026-09-03 ユーザー決定。IMP-TRT-002) ----
        //
        // **狙って出せることを最優先にした。** 姿勢の条件を並べるのをやめ、
        // 「倒す → 待つ → 出る」の 1 本にする (Avatar Idle Arena と同じ操作感)。

        [Header("VR: 右スティック長押し")]
        [Tooltip("**倒す向き。** -1 = 下 (既定) / +1 = 上。" +
                 "実機で逆だったらここだけ入れ替える (判定式は 1 本のまま)")]
        public int openStickSign = DefaultStickSign;

        [Tooltip("倒し量のしきい値 (0〜1)。**深めにする** — 浅いと旋回や手ブレで誤爆する")]
        public float openStickThreshold = DefaultStickThreshold;

        [Tooltip("秒。この時間だけ倒し続けたら開閉する。**1 回の長押しで 1 回だけ**")]
        public float openHoldSeconds = DefaultOpenHold;

        [Header("調整")]
        [Tooltip("板を描き直す間隔 (秒)。周期が要るのは残高が時間で増えるから")]
        public float refreshSeconds = DefaultRefresh;

        [Tooltip("秒。Ownership の移譲を待つ上限。**過ぎても在庫は減らない**")]
        public float ownerTimeout = DefaultOwnerTimeout;

        [Tooltip("秒。手続きの途中で固まったときに枠を返すまで")]
        public float stepTimeout = DefaultStepTimeout;

        const float DefaultEyeHeight = 1.6f;
        /// <summary>**0.80 → 0.95 (IMP-TRT-004)。** 板を 640 → 760px へ広げたぶん、
        /// 見込み角が現行を超えないよう距離を伸ばす — 0.95m 先の 0.76m は水平 43.5° で、
        /// 旧 (0.80m 先の 0.64m = 43.6°) をわずかに下回る。**面積は 1.19 倍。**</summary>
        const float DefaultPanelDistance = 0.95f;
        /// <summary>呼んだおもちゃを置く距離 (m)。**足元から積むので高さは含まない。**</summary>
        const float DefaultRecallDistance = 1.2f;
        /// <summary>連打の抑制 (秒)。**旧 `KD_BallResetButton.cooldown` と同じ値。**</summary>
        const float DefaultRecallCooldown = 2.5f;
        const float DefaultRefresh = KD_ShopItem.ConfirmRefreshSeconds;
        const float DefaultOwnerTimeout = 5f;
        const float DefaultStepTimeout = 8f;
        const int DefaultStickSign = -1;
        const float DefaultStickThreshold = 0.8f;
        const float DefaultOpenHold = 0.5f;
        /// <summary>裏山改変 (IMP-UI-004): 板の下端と地面の間の余白 (m)。</summary>
        const float DefaultPanelGroundClearance = 0.10f;
        /// <summary>裏山改変 (IMP-UI-004): 持ち上げたときに頭へ倒す上限 (度)。</summary>
        const float DefaultPanelTiltMax = 30f;
        /// <summary>裏山改変 (IMP-UI-004): 地面のレイの始点を頭の高さからどれだけ上に置くか (m)。</summary>
        const float PanelGroundRayUp = 0.5f;
        /// <summary>裏山改変 (IMP-UI-004): 地面のレイの長さ (m)。</summary>
        const float PanelGroundRayLength = 6f;
        /// <summary>裏山改変 (IMP-UI-004): 頭より上に当たった時、撃ち直すレイの始点を当たり点からどれだけ下げるか (m)。</summary>
        const float PanelGroundRetryGap = 0.02f;

        /// <summary>状態のポーリング間隔 (秒)。毎フレーム Ownership を問い合わせない。</summary>
        const float PollInterval = 0.2f;

        /// <summary>再入したイベントを回す上限。**無限ループの床** (状態機械は数歩で収束する)。</summary>
        const int MaxChainedEvents = 8;

        /// <summary>ReserveSettle 明けを待つ再試行の間隔 (秒)。settle 0.3s に対して
        /// 2 回目 (約 0.4s) までに必ず明ける長さ (破壊的監査 v2 RC-2)。</summary>
        const float ReserveRetryDelay = 0.2f;

        // ============================================================
        // 状態 (**全部このクライアントのもの。1 本も同期しない**)
        // ============================================================

        private int _state;            // KD_TreatBox.Disp*
        /// <summary>試行の世代。**押すたびに増える** — 遅れて届いた前回のイベントを弾く。</summary>
        private int _curSeq = 1;
        private int _placedSeq;
        private string _wantProduct = "";
        private int _slotIndex = -1;
        /// <summary>Out にしたときの `outMs`。**枠の再利用を見抜く鍵** (犬側と同じ扱い)。</summary>
        private int _slotGen;
        /// <summary>ReserveSettle 待ち再試行の世代 (古い遅延イベントを弾く)。</summary>
        private int _reserveRetrySeq;
        /// <summary>最後の拒否理由 (<c>KD_TreatBox.Gate*</c>)。</summary>
        private int _reason;
        private float _stateSince;
        private float _nextPoll;

        // 再入の潰し。**U# は既定で再帰できない** (ローカル変数の格納先が固定なので
        // 入れ子呼び出しで壊れる)。副作用から次のイベントを投げたいときは積む
        private bool _firing;
        private int _queuedEv = -1;
        private int _queuedSeq;

        private int _eyeCooldown;
        private float _scale = 1f;
        /// <summary>裏山改変 (IMP-UI-004): 最後に <see cref="PlaceInFront"/> が板を持ち上げた量 (m)。0 = 持ち上げていない。</summary>
        private float _lastLift;

        /// <summary>いま開いているタブ (<c>Tab*</c>)。**同期も保存もしない。**
        /// 既定は左端の <see cref="TabAcc"/>。</summary>
        // 裏山改変: 既定は さんぽ (TabWalk。計画書 D-2。復帰条件なし)
        private int _tab = TabWalk;
        public UnityEngine.UI.ScrollRect[] tabScrolls;

        // ---- おもちゃタブのキャッシュ (Start で 1 回だけ引く) ----
        //
        // **毎フレーム GetComponent しない** (Udon は通常 C# の 200〜1000 倍遅い)。
        // ボールはプレハブインスタンスなので、シーン側からその子を引くのは安全な向き
        // (`vrc-udonsharp` §3 — 逆向きだけが実機ビルドで null 化する)。

        private VRCObjectSync[] _ballSyncs;
        private Rigidbody[] _ballBodies;
        /// <summary>行ごとの最後に呼んだ時刻。**連打の抑制**。</summary>
        private float[] _recallAt;

        /// <summary>確認帯を開いている撫でエフェクトの行 (-1 = 閉じている)。
        /// **タブを離れても畳まない** — 戻ってきたときに続きから選べるほうが素直。</summary>
        private int _fxConfirmRow = -1;

        /// <summary>右スティックの縦 (VR)。**イベントはこれを更新するだけ** —
        /// 倒したまま静止すると次のイベントが来ないので、経過は <see cref="LateUpdate"/> が測る。</summary>
        private float _stickY;
        /// <summary>しきい値を越え始めた時刻 (-1 = 越えていない)。**長押しの判定用。**</summary>
        private float _stickSince = -1f;
        /// <summary>この倒しで既に開閉したか。**倒しっぱなしで連打しないための掛け金。**</summary>
        private bool _stickLatched;

        public KD_Language language;
        public void _RefreshLanguage() { Redraw(); }
        public GameObject[] extraSettingsPages;
        private GameObject _extraSettingsPage;
        private string _extraSettingsTitle;
        public void _OpenExtraSettings(GameObject page, string title)
        {
            _OpenSettings(); _extraSettingsPage = page; _extraSettingsTitle = title; ApplyTab(); Redraw();
        }
        private string L(string source) { return language != null ? language._Text(source) : source; }

        private void Start()
        {
            _voiceFollowsPosition = sleepAudio != null;   // IMP-DOG-036 S5
            ApplyDogVoiceSettings();
            ApplyBgmSettings();
            // 0 で入ると無言で壊れる値 (破壊的監査 RC-1 の 3 規則)。
            // **改名したフィールドはシーンに 0 で入る**ので、ここが既定値の出どころになる
            // (2026-09-03 の IMP-TRT-002 で desktopPanelDistance → panelDistance 等へ改名した)
            if (referenceEyeHeight <= 0.1f) referenceEyeHeight = DefaultEyeHeight;
            // 0 で入ると板が顔の中に生えて**読めないまま操作もできない**
            if (panelDistance <= 0.2f) panelDistance = DefaultPanelDistance;
            if (refreshSeconds <= 0.05f) refreshSeconds = DefaultRefresh;
            if (ownerTimeout <= 0.5f) ownerTimeout = DefaultOwnerTimeout;
            if (stepTimeout <= 1f) stepTimeout = DefaultStepTimeout;
            // 0 で入ると**倒していなくても常に開く** (しきい値 0 を越え続ける)
            if (openStickThreshold <= 0.05f || openStickThreshold > 1f)
                openStickThreshold = DefaultStickThreshold;
            // 0 で入ると**触れた瞬間に開く** = 旋回のたびに板が出る
            if (openHoldSeconds <= 0.05f) openHoldSeconds = DefaultOpenHold;
            // 0 で入ると**どちらへ倒しても開かない** (符号が消える)
            if (openStickSign != 1 && openStickSign != -1) openStickSign = DefaultStickSign;
            // 0 で入ると**呼んだおもちゃが自分の足の中に生えて**掴めない
            if (recallDistance <= 0.3f) recallDistance = DefaultRecallDistance;
            // 0 で入ると連打が素通りする (Ownership の取り合いが毎フレーム走る)
            if (recallCooldown <= 0.05f) recallCooldown = DefaultRecallCooldown;

            // **おもちゃの部品はここで 1 回だけ引く。** Udon の呼び出しは重いので
            // 毎フレーム GetComponent しない。取れなくても他のタブは動く
            CacheBalls();

            // 初回は左端の「かざり」(IMP-INF-008)。以後は選択を保持する。
            // 裏山改変: 初回は左端の「さんぽ」(計画書 D-2。復帰条件なし)
            _tab = TabWalk;
            ApplyTab();

            // **既定は閉じている。** 開いたまま保存すると入室した瞬間から板が浮くし、
            // 撮影の許可レイヤーに active な Canvas が増える (BUG-CAM-001 と同型)
            // (復元が Start より先に来て庭へ置いた直後なら閉じない — IMP-INF-020。Start 同士の順は保証されない)
            if (panelRoot != null && panelRoot.activeSelf && !_welcome) panelRoot.SetActive(false);

            // 裏山改変: 未配線の検査は段階表 (計画書 D-1) で「意図的な未導入」と「欠落」を分ける。移動先 KD_TreatPouch.Hillside.cs。hillsideStage 1 (台帳) / 2 (おやつ・かざり・呼ぶ) で段階表どおり必須へ戻る
            HillsideStartGuard();

            _state = KD_TreatBox.DispIdle;
            _stateSince = Time.time;
            SendCustomEventDelayedSeconds(nameof(_Refresh), refreshSeconds);
        }

        // ============================================================
        // 開閉
        // ============================================================

        /// <summary>開く / 手元へ呼び戻す。自動閉鎖はおやつ成功・添い寝指定開始時。
        /// VR = 右スティック下の長押し (<see cref="StepStickHold"/>) /
        /// Desktop = <see cref="desktopToggleKey"/> (既定 T) /
        /// 板の右上の [とじる] (<see cref="_Close"/>)。
        ///
        /// **開くと板はその場に据わる** (<see cref="PlaceInFront"/>)。追従したままでは
        /// 押せない — Desktop は見る = 身体ごと回るので、板が照準から逃げる。
        /// **VR も 2026-09-03 に据え置きへそろえた** (ユーザー決定) —
        /// 手に付いていると、右上の [とじる] を狙うたびに板が動く。
        ///
        /// > `Interact()` は**もう持たない。** 触る実物 (ポーチ) を無くしたので、
        /// > 当たりごと外してある (体に追従する当たりはプレイヤーを押しのける)。</summary>
        private void OpenOrRecall()
        {
            _introMenuRevision++;
            if (sleepPlacement != null && sleepPlacement.Active) return;
            if (panelRoot == null) return;
            // 自分の操作で開けた = 呼び方を知っている。手元ヒントはここで消える (IMP-INF-020 案 4)
            MarkSelfOpened();
            if (!panelRoot.activeSelf) SetOpen(true);
            else
            {
                // VR/Touchの起動操作は手元へ呼び戻す。Desktopキーは別の開閉入口。
                // 庭に置いた「ようこそ」を自分で呼び戻したときは通常の面へ (呼び方を知っている人に呼び方の案内は要らない)
                if (_welcome) { LeaveWelcome(); ApplyTab(); }
                _gardenPlaced = false;
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (Utilities.IsValid(lp)) PlaceInFront(lp);
                Redraw();
            }
        }

        // ============================================================
        // ようこそ (IMP-INF-020 案 1 + 2)
        // ============================================================

        /// <summary>庭へ開いた状態で置く。**KD_HintDirector が復元後に一度だけ呼ぶ** (案内を離れた印が無い人だけ)。
        /// 自分で既に開けた人・位置指定中・二度目は何もしない。ローカル専用。</summary>
        public void _ShowWelcomeInGarden()
        {
            if (_welcomeShown || _selfOpened || welcomeSpot == null || panelRoot == null || welcomePage == null) return;
            if (sleepPlacement != null && sleepPlacement.Active) return;
            if (panelRoot.activeSelf) return;
            _welcomeShown = true;
            _welcome = true;
            _gardenPlaced = true;
            panelRoot.SetActive(true);
            ApplyTab();
            // PlaceInFront と同じ置き方 — root を据えてから、板が目標からずれたぶん root を戻す
            transform.rotation = welcomeSpot.rotation;
            transform.position = welcomeSpot.position;
            transform.position += welcomeSpot.position - panelRoot.transform.position;
            Redraw();
        }

        /// <summary>ようこその [あとで]。板を閉じる。案内を離れた印を立てる。</summary>
        public void _WelcomeLater() { _Close(); }

        /// <summary>ようこその [メニューを見る]。通常の面 (初回はかざり) へ。案内を離れた印を立てる。</summary>
        public void _WelcomeMenu()
        {
            _introMenuRevision++;
            if (!_welcome) return;
            LeaveWelcome();
            if (BoardContinuePending()) return;   // 裏山改変 IMP-UI-008: ようこそ中に看板の札を押していたら、その面へ (KD_TreatPouch.Board.cs)
            ApplyTab();
            Redraw();
        }

        /// <summary>ようこその面を離れる。**印はここでだけ立つ** — 庭に置いただけでは立てない。</summary>
        private void LeaveWelcome()
        {
            if (!_welcome) return;
            _welcome = false;
            if (welcomePage != null && welcomePage.activeSelf) welcomePage.SetActive(false);
            if (hints != null) hints._OnGuideLeft();
        }

        private void MarkSelfOpened()
        {
            if (_selfOpened) return;
            _selfOpened = true;
            if (hints != null) hints._OnMenuOpened();
        }

        /// <summary>ようこその面を庭 (または目の前) に出しているか。KD_HintDirector が「集中中」の判定に読む。</summary>
        // net: callable-ok: 読み取りのみ
        public bool WelcomeShowing() { return _welcome && panelRoot != null && panelRoot.activeSelf; }

        /// <summary>庭に据えたまま開いているか (ようこそを離れた後も含む)。KD_HintDirector が距離で「集中中」を決める。</summary>
        // net: callable-ok: 読み取りのみ
        public bool GardenPlaced() { return _gardenPlaced && panelRoot != null && panelRoot.activeSelf; }

        /// <summary>自分の操作で一度でも開けたか。手元ヒントを消す条件 (KD_HintDirector)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool SelfOpened() { return _selfOpened; }

        /// <summary>板の右上の [とじる]。**[しまう] とは別物** —
        /// こちらは板を消すだけで、枠にも在庫にも触らない
        /// (出したおやつは手に残り、あげるのは自分のトリガー — IMP-TRT-003)。
        /// ローカル専用なので `_` 始まり。</summary>
        public void _Close()
        {
            _introMenuRevision++;
            if (sleepPlacement != null && sleepPlacement.Active) sleepPlacement._CancelHidden();
            LeaveWelcome();
            SetOpen(false);
        }

        // Placement owns the selection from here; hiding its parent menu must not release it.
        public void _HideForPlacement() { SetOpen(false); }

        public void _CallFailed() { _feedbackTab = TabSleep; Feedback("call.unavailable", false); Redraw(); }

        public void _DesktopToggle()
        {
            if (sleepPlacement != null && sleepPlacement.Active) { _Close(); return; }
            // 庭に置いた「ようこそ」が開いている間の T は閉じずに手元へ呼び戻す — 見えていない板を閉じても「何も起きない」に見える
            if (panelRoot != null && panelRoot.activeSelf && !_welcome) { MarkSelfOpened(); _Close(); }
            else OpenOrRecall();
        }

        /// <summary>IMP-TRT-006: Touch の起動ボタンの onClick。**開閉の 4 つ目の入口** —
        /// VR = 右スティック下長押し / Desktop = キー / 板の [とじる] / **Touch = 画面端の [メニュー]**。
        /// ローカル専用なので `_` 始まり (付けないとネットワーク経由で誰でも開閉できる)。</summary>
        public void _Toggle() { OpenOrRecall(); }

        // ============================================================
        // 入力方式 (IMP-TRT-006)
        //
        // **「VR か否か」の 2 分岐は Touch を Desktop 扱いにする穴だった** — Touch は
        // `Input.GetKeyDown(desktopToggleKey)` に落ちてキーボードが無いので永久に開かなかった。
        // 表示 (VR / 2D) と入力装置 (VRController / KeyboardMouse / Touch / ...) は別の軸
        // (IMP-INF-004 の Facade と同じ 2 軸)。ここで実装するのは 3 組合せだけ:
        //   VR + VRController → スティック / 2D + KeyboardMouse → desktopToggleKey (T) / 2D + Touch → 起動ボタン
        //
        // 入力方式は**途中で変わる** (スマホに Bluetooth コントローラを繋ぐ等) ので、
        // OnInputMethodChanged で受け、取りこぼしの床として LateUpdate でも定期的に読む。
        // Start で 1 回だけ見る作りにしない。
        //
        // enum の等値比較が Udon で落ちる報告 (VRChat Feedback) があるので **int に落として比べる**
        // (VRCInputMethod.Touch = 15。2026-09-06 Editor 実測)。
        // ============================================================

        /// <summary>最後に使われた入力方式 (int)。-1 = まだ知らない (= 従来どおり Desktop 扱い)</summary>
        private int _inputMethod = -1;
        private int _inputPollCooldown;
        /// <summary>入力方式の定期読みの間隔 (フレーム)。イベントが正なので間引いてよい</summary>
        private const int InputPollFrames = 90;

        public override void OnInputMethodChanged(VRCInputMethod inputMethod)
        {
            _inputMethod = (int)inputMethod;
            ApplyInputMode();
        }

        /// <summary>今の入力が Touch か。<see cref="debugForceTouch"/> はエディタ確認用の上書き。</summary>
        private bool IsTouch()
        {
            return debugForceTouch || _inputMethod == (int)VRCInputMethod.Touch;
        }

        /// <summary>写真ビューア (BUG-CAM-003 案 B) が開いている間は起動ボタンを隠す。
        /// スマホの「持ち帰り」は**端末のスクリーンショット**なので、Screen-Space Overlay は
        /// RenderTexture に写らなくても**画面には居る** = そのまま撮ると写る (Codex レビュー 2026-09-06)。</summary>
        private bool _launcherSuppressed;

        /// <summary>KD_PhotoViewer が開いた瞬間に呼ぶ。ローカル専用。</summary>
        public void _SuppressLauncher() { _launcherSuppressed = true; ApplyInputMode(); }
        /// <summary>KD_PhotoViewer が閉じた瞬間に呼ぶ。ローカル専用。</summary>
        public void _ReleaseLauncher() { _launcherSuppressed = false; ApplyInputMode(); }

        /// <summary>起動ボタンの出し入れ。**Touch かつ非 VR、かつ写真ビューアが閉じているときだけ出す。**
        /// VR では Screen-Space Canvas そのものが成立しないので必ず隠す。
        /// 何度呼んでもよい (変化があるときだけ SetActive する)。</summary>
        public override void OnScreenUpdate(VRC.SDK3.Platform.ScreenUpdateData data)
        {
            _hasScreenOrientation = true;
            _portraitScreen = data.orientation == VRC.SDKBase.Platform.VRCOrientation.Portrait;
            ApplyTouchLauncherLayout();
        }

        private void ApplyTouchLauncherLayout()
        {
            if (touchLauncher == null || touchLauncherButton == null) return;
            // 入室時イベントより先に入力判定が来た場合はCanvasの現在寸法を使う。
            // 以後はVRChatの端末向き通知を正本にする（描画解像度から端末向きを推定し直さない）。
            Rect canvasRect = ((RectTransform)touchLauncher.transform).rect;
            bool portrait = _hasScreenOrientation ? _portraitScreen : canvasRect.height > canvasRect.width;
            if (_launcherLayoutSet && portrait == _lastLauncherPortrait) return;
            _launcherLayoutSet = true;
            _lastLauncherPortrait = portrait;
            Vector2 anchor = portrait ? new Vector2(1f, 0.5f) : new Vector2(0.5f, 1f);
            touchLauncherButton.anchorMin = anchor;
            touchLauncherButton.anchorMax = anchor;
            touchLauncherButton.pivot = anchor;
            touchLauncherButton.anchoredPosition = portrait
                ? new Vector2(-TouchLauncherEdgeMargin, 0f)
                : new Vector2(0f, -TouchLauncherEdgeMargin);
        }

        private void ApplyInputMode()
        {
            if (touchLauncher == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            bool show = Utilities.IsValid(lp) && !lp.IsUserInVR() && IsTouch() && !_launcherSuppressed;
            ApplyTouchLauncherLayout();
            if (touchLauncher.activeSelf != show) touchLauncher.SetActive(show);
            // 案内文言 (T で開閉) も入力方式に追従させる — 板が開いていれば描き直す
            if (panelRoot != null && panelRoot.activeSelf) Redraw();
        }

        /// <summary>取りこぼしの床: イベントが来なくても最後に使われた入力方式を読み直す。
        /// 表示条件は毎回評価し直す (入力方式が同じでも <see cref="debugForceTouch"/> を
        /// Play 中に切り替えたら追従する — Codex レビュー 2026-09-06)。</summary>
        private void PollInputMethod()
        {
            if (_inputPollCooldown > 0) { _inputPollCooldown--; return; }
            _inputPollCooldown = InputPollFrames;
            _inputMethod = (int)InputManager.GetLastUsedInputMethod();
            ApplyInputMode();
        }

        /// <summary>板が開いているか。**「あげる」判定がこれを読む** —
        /// 板を操作しているトリガー / クリックで手のおやつを落とさないため (IMP-TRT-003)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool PanelOpen() { return (panelRoot != null && panelRoot.activeSelf) || (sleepPlacement != null && sleepPlacement.Active); }

        private int _panelClosedFrame = -1;
        // net: callable-ok: 入力ゲートの読み取りのみ。閉じるボタンのUSEを配布に持ち越さない。
        public bool TreatInputBlocked() { return PanelOpen() || Time.frameCount <= _panelClosedFrame + 1; }

        private void SetOpen(bool open)
        {
            if (!open && callPose != null && (sleepPlacement == null || !sleepPlacement.Active)) callPose._Dismiss();
            if (panelRoot == null) return;
            if (panelRoot.activeSelf == open) return;
            panelRoot.SetActive(open);
            // hints._OnMenuOpened() はここでは呼ばない — 「開いた」と「自分の操作で開けた」は別 (MarkSelfOpened。IMP-INF-020)
            if (!open) { _panelClosedFrame = Time.frameCount; _gardenPlaced = false; }
            if (!open) { _fxConfirmRow = -1; _liveConfirmRow = -1; _liveGiftRow = -1; _livePage = LivePageList; _liveProduct = -1; _accConfirm = false; _accGiftRow = -1; HillsideResetTreatConfirm(); HideConfirmAll(); return; }   // 裏山改変: おやつの確認 (IMP-DOG-014) も閉じるときに捨てる

            // 初回はかざり。同じ滞在中は直前のタブを保ち、繰り返し操作を短くする。
            ApplyTab();

            // **開いた瞬間に据える (VR / Desktop 共通)。** 以後どこも板を動かさないので、
            // ここで決めた場所が閉じるまでの置き場所になる
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp)) PlaceInFront(lp);
            Redraw();
        }

        // ============================================================
        // タブ (IMP-TRT-004)
        //
        // ボタンごとに専用のイベントを持つのは、Udon がパラメータ付きの呼び出しを
        // 信頼できないため (`vrc-udonsharp` §7)。タブは 3 枚固定なので素直に並べる。
        // ============================================================

        public void _Tab0() { SetTab(TabTreat); }
        public void _Tab1() { SetTab(TabToy); }
        public void _Tab2() { SetTab(TabWalk); }   // 裏山改変: タブ 2 は さんぽ (計画書 D-2)
        public void _Tab3() { SetTab(TabSleep); }
        public void _Tab4() { SetTab(TabLive); }
        public void _Tab5() { SetTab(TabAcc); }
        public void _Tab6() { SetTab(TabPlay); }

        public void _OpenSettings()
        {
            _introMenuRevision++;
            LeaveWelcome();   // ヘッダの [設定] / ポイント ▶ も「案内を離れた」
            _extraSettingsPage = null;
            _settingsOpen = true;
            _volumeSettingsOpen = false;
            _distanceFadeSettingsOpen = false;
            _fxConfirmRow = -1;
            _liveConfirmRow = -1; _liveGiftRow = -1;
            _accConfirm = false; _accGiftRow = -1;
            HillsideResetTreatConfirm();   // 裏山改変: おやつの確認 (IMP-DOG-014) も遷移で捨てる
            HideConfirmAll();
            ApplyTab();
            Redraw();
        }


        private void SetTab(int tab) { SetTabCore(tab, false); }

        /// <summary>force = 同じタブでも確認の解除・あそぶのホーム復帰まで初期化する (裏山改変 IMP-UI-008: 看板の札から開くとき。
        /// 古民家の OpenFromBoard → ShowTab と同じ。KD_TreatPouch.Board.cs)。</summary>
        private void SetTabCore(int tab, bool force)
        {
            _introMenuRevision++;
            if (tab < 0 || tab >= TabCount) return;
            if (!force && _tab == tab && !_settingsOpen && !_welcome) return;
            LeaveWelcome();   // ようこそからタブを押した = 案内を離れた
            _settingsOpen = false;
            _tab = tab;
            _fxConfirmRow = -1;
            _liveConfirmRow = -1; _liveGiftRow = -1;
            _livePage = LivePageList; _liveProduct = -1;   // P7: ライブは一覧から
            _accConfirm = false; _accGiftRow = -1;
            HillsideResetTreatConfirm();   // 裏山改変: おやつの確認 (IMP-DOG-014) も遷移で捨てる
            HideConfirmAll();
            ApplyTab();
            // P2: あそぶの面は KD_ActivitiesMenu が描く。タブを開くたびにホームへ戻す (Codex 指摘 2026-09-17: _OpenSettings に置いていた)
            if (_tab == TabPlay && activitiesMenu != null) activitiesMenu._Open();
            Redraw();
        }

        /// <summary>**表示中の 1 枚だけを active にし、見出しへ色と下線を当てる。**
        ///
        /// 選択中を `interactable` で表さないのが要点 — この板は `interactable = false` を
        /// 「今は押せない」の意味でおもちゃ行に使うので、同じ見た目が 2 義になる
        /// (Avatar Idle Arena `IMP-UI-009` が実際に踏んだ)。</summary>
        private void ApplyTab()
        {
            _accPreviewReady = false; // Reconcile once when reopening or changing pages, not on each periodic redraw.
            // ようこそ (IMP-INF-020): この面だけ出し、タブの面も設定の面も隠す。タブは見せて非選択 (中に何があるかの約束)
            if (welcomePage != null && welcomePage.activeSelf != _welcome) welcomePage.SetActive(_welcome);
            if (callPose != null && (_settingsOpen || _welcome || _tab != TabSleep)) callPose._Dismiss();
            if (settingsPage != null) settingsPage.SetActive(_settingsOpen && !_welcome);
            if (settingsHome != null) settingsHome.SetActive(!_volumeSettingsOpen && !_distanceFadeSettingsOpen && _extraSettingsPage == null);
            if (extraSettingsPages != null) for (int i = 0; i < extraSettingsPages.Length; i++)
                if (extraSettingsPages[i] != null) extraSettingsPages[i].SetActive(_settingsOpen && extraSettingsPages[i] == _extraSettingsPage);
            if (volumeSettingsPage != null) volumeSettingsPage.SetActive(_volumeSettingsOpen);
            if (distanceFadeSettingsPage != null) distanceFadeSettingsPage.SetActive(_distanceFadeSettingsOpen);
            if (settingsButtonImage != null)
                settingsButtonImage.color = _settingsOpen ? tabOnColor : tabOffColor;
            if (tabPages != null)
            {
                for (int i = 0; i < tabPages.Length; i++)
                {
                    GameObject page = tabPages[i];
                    if (page == null) continue;
                    bool on = !_settingsOpen && !_welcome && (i == _tab);
                    if (page.activeSelf != on) page.SetActive(on);
                }
            }
            if (tabImages != null)
            {
                for (int i = 0; i < tabImages.Length; i++)
                {
                    UnityEngine.UI.Image img = tabImages[i];
                    if (img == null) continue;
                    img.color = (!_settingsOpen && !_welcome && i == _tab) ? tabOnColor : tabOffColor;
                }
            }
            if (tabUnderlines != null)
            {
                for (int i = 0; i < tabUnderlines.Length; i++)
                {
                    GameObject bar = tabUnderlines[i];
                    if (bar == null) continue;
                    bool on = !_settingsOpen && !_welcome && (i == _tab);
                    if (bar.activeSelf != on) bar.SetActive(on);
                }
            }
        }

        /// <summary>いま開いているタブ。デバッグ盤と Verify が読む。</summary>
        // net: callable-ok: 読み取りのみ
        public int CurrentTab() { return _tab; }

        /// <summary>**開いたときの据え方 — 目の前へ置いて、そこで固定する (VR / Desktop 共通)。**
        ///
        /// Desktop には「身体と別に視線を動かす」が無い。**見る = 身体ごと回る**なので、
        /// 腰に付いて回る板は**照準を合わせようとした瞬間に同じだけ逃げる** —
        /// これが 2026-09-01 の Play で出た「追従してきて選択できない」。
        /// 追従をやめれば、振り向く → 板は残る → カーソルが乗る、になる。
        ///
        /// **VR も同じ扱いにした** (2026-09-03 ユーザー決定)。左手に付いていると、
        /// 右上の [とじる] や行の [出す] を狙うたびに板そのものが動く。
        /// 開く操作が手の姿勢から独立した (右スティック) ので、手に付ける理由も無くなった。
        ///
        /// 向きは頭のヨーだけを使う (上下は捨てる) — **板は常に立っている**方が読める。
        /// 位置は板 (<see cref="panelRoot"/>) が目標へ来るように**逆算して root を置く** —
        /// 板は root の子で決め打ちのずらしを持っており、root をそのまま置くと
        /// そのぶん斜め上へずれる。
        ///
        /// **裏山改変 (IMP-UI-004 案 B、2026-10-08): 板の下端が地面に埋まらない高さへ上げる。**
        /// 低い姿勢・小さいアバター・登り斜面で下のボタンが地面に埋まっていた。据える時にだけ
        /// 下端の真下へレイを下ろし、下端が「地面 + <see cref="panelGroundClearance"/>」より低ければその差だけ上げる
        /// (下げる方向には動かさない)。上げたときだけ、見上げて読めるよう上端を頭の方へ
        /// <see cref="panelTiltMax"/> まで倒し、前へ出た下端でもう一度同じ判定をして足りない分だけ上げる。
        /// 上げていないときは今までどおり垂直。レイは 1 判定につき最大 2 本 (屋根・枝の下の撃ち直し)、
        /// 据える 1 回で最大 4 本。毎フレームは撃たない。
        /// どちらも据えた後は固定 (追従しない。2026-09-03 の決定)。</summary>
        private void PlaceInFront(VRCPlayerApi lp)
        {
            if (panelRoot == null) return;

            VRCPlayerApi.TrackingData head =
                lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

            Vector3 fwd = head.rotation * Vector3.forward;
            fwd.y = 0f;
            // 真下 / 真上を向いていると水平成分が消える。**そのときは身体の向きで代替する**
            if (fwd.sqrMagnitude < 0.0001f)
            {
                fwd = lp.GetRotation() * Vector3.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            }
            fwd = fwd.normalized;

            Vector3 want = head.position
                + fwd * (panelDistance * _scale)
                + Vector3.up * (panelHeightOffset * _scale);

            // 板の面はプレイヤーへ向ける (Canvas の正面は +Z の裏)
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            // **子のずらしは読まずに実測で消す。** 一度置いてから、板が want からどれだけ
            // ずれたかぶん root を戻す — ずらしの値や scale を書き写さずに済む
            transform.position = want;
            transform.position += want - panelRoot.transform.position;

            // 裏山改変 (IMP-UI-004): 下端が地面に埋まるなら、その差だけ上げる (据える時だけ。毎フレームは測らない)
            float feetY = lp.GetPosition().y;
            float lift = PanelGroundLift(head.position, feetY);
            _lastLift = lift;
            if (lift <= 0f) return;
            want.y += lift;

            // 上げたときだけ、上端を頭の方へ倒す。倒すと下端は (1 − cos) × 半高だけ上がるが、
            // 同時に sin × 半高だけ前へ出る (30° で約 0.175 m)。登り斜面ではその先の地面が高いので、
            // 倒して据え直した後にもう一度下端の真下を測って、足りない分だけさらに上げる (下の 2 回目)
            Vector3 face = fwd;
            float tiltMax = Mathf.Min(panelTiltMax, 80f);   // 90° 近くは LookRotation が上向きと平行になって崩れる
            if (tiltMax > 0f)
            {
                Vector3 toPanel = want - head.position;   // 頭 → 板。上向き (y > 0) なら見上げている
                float horiz = Mathf.Sqrt(toPanel.x * toPanel.x + toPanel.z * toPanel.z);
                float pitch = Mathf.Clamp(Mathf.Atan2(toPanel.y, horiz) * Mathf.Rad2Deg, 0f, tiltMax);
                if (pitch > 0f)
                {
                    float rad = pitch * Mathf.Deg2Rad;
                    face = fwd * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);
                }
            }
            transform.rotation = Quaternion.LookRotation(face, Vector3.up);
            transform.position = want;
            transform.position += want - panelRoot.transform.position;

            // 2 回目: 倒して前へ出た下端の真下で同じ判定をする。傾きは変えずに、足りない分だけ上げる
            float more = PanelGroundLift(head.position, feetY);
            if (more <= 0f) return;
            _lastLift = lift + more;
            want.y += more;
            transform.position = want;
            transform.position += want - panelRoot.transform.position;
        }

        /// <summary>裏山改変 (IMP-UI-004): いま据わっている板の下端を
        /// 「地面 + <see cref="panelGroundClearance"/>」まで上げるのに要る量 (m)。上げなくてよければ 0。
        ///
        /// 板の下端は panelRoot の RectTransform.rect の下辺の中点を TransformPoint で世界へ移して取る
        /// (板の大きさ・scale を書き写さない。傾けた板なら前へ出た位置がそのまま入る)。
        /// 地面 = max(その下端の点の真下へ下ろしたレイの当たり点, 足元)。レイの始点は頭の高さ + 0.5 m、長さ 6 m、
        /// <see cref="panelGroundMask"/> (Default + Environment) で trigger は無視する。
        /// **頭より高い当たりは地面とみなさない** — 軒・枝・岩の上面に当たって板が頭上へ逃げるのを防ぐ。
        /// その時は当たり点の 0.02 m 下から同じ長さでもう 1 本だけ撃ち直す (最大 2 本。屋根・枝の下の段差を拾う)。
        /// それでも取れなければ足元だけを使う。</summary>
        private float PanelGroundLift(Vector3 headPos, float feetY)
        {
            RectTransform rt = (RectTransform)panelRoot.transform;
            Rect r = rt.rect;
            Vector3 bottom = rt.TransformPoint(new Vector3(0f, r.yMin, 0f));
            float bottomY = bottom.y - panelBottomExtra;

            float ground = feetY;
            RaycastHit hit;
            Vector3 origin = new Vector3(bottom.x, headPos.y + PanelGroundRayUp, bottom.z);
            if (Physics.Raycast(origin, Vector3.down, out hit, PanelGroundRayLength, panelGroundMask, QueryTriggerInteraction.Ignore))
            {
                float hy = hit.point.y;
                if (hy > headPos.y)
                {
                    // 頭より上 (屋根・枝) に当たった。その少し下から 1 本だけ撃ち直す
                    origin = new Vector3(bottom.x, hy - PanelGroundRetryGap, bottom.z);
                    if (Physics.Raycast(origin, Vector3.down, out hit, PanelGroundRayLength, panelGroundMask, QueryTriggerInteraction.Ignore))
                        hy = hit.point.y;
                    else
                        hy = feetY;
                }
                if (hy <= headPos.y && hy > ground) ground = hy;
            }

            float lift = ground + panelGroundClearance - bottomY;
            return lift > 0f ? lift : 0f;
        }

        /// <summary>裏山改変 (IMP-UI-004): 最後に据えた時に板を持ち上げた量 (m)。0 = 持ち上げていない。ハーネスが読む
        /// (変数名で読むなら <c>_lastLift</c>)。</summary>
        // net: callable-ok: 読み取りのみ
        public float LastLift() { return _lastLift; }

        // ============================================================
        // 入力 — **板は誰も動かさない。開くときに据えるだけ**
        //
        // VR = 右スティック下の長押し / Desktop = キー。どちらも開いた場所へ据わる。
        // 「左手に追従して手のひらの向きで開く」は 2026-09-03 に廃止した (IMP-TRT-002) —
        // 実機で出しにくく、かつ**開く条件と閉じる条件が同じ**だったので
        // 板を読もうと手を動かすと閉じていた。
        // ============================================================

        private void LateUpdate()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;

            // 身長スケールは毎フレーム引かない (アバター変更に追従できれば十分)
            if (_eyeCooldown <= 0)
            {
                float eye = lp.GetAvatarEyeHeightAsMeters();
                if (eye > 0.1f) _scale = eye / referenceEyeHeight;
                _eyeCooldown = 60;
            }
            _eyeCooldown--;

            // IMP-TRT-006: 入力方式の取りこぼしの床 (イベントが正。ここは間引いた読み直し)
            PollInputMethod();

            if (lp.IsUserInVR()) { StepStickHold(); return; }

            // Desktop: one key-down toggles the menu, including while a text field has focus.
            // **VR ではキーを、Desktop ではスティックを読まない** —
            // Desktop の InputLookVertical は**マウスの縦移動**なので、
            // 読むと下を向いた瞬間に板が出る。
            // Touch でもキーは読み続ける (Bluetooth キーボードなら開ける。害は無い) —
            // Touch の本線は画面端の起動ボタン (_Toggle。IMP-TRT-006)
            if (Input.GetKeyDown(desktopToggleKey)) _DesktopToggle();
        }

        /// <summary>**VR の開閉 — 右スティックを下へ倒したまま <see cref="openHoldSeconds"/>。**
        ///
        /// 入力そのもの (<see cref="InputLookVertical"/>) は値が変わったときにしか来ないので、
        /// **経過はここで測る。** 倒したまま静止しても時間は進む。
        ///
        /// <see cref="_stickLatched"/> は**1 回の長押しで 1 回だけ**開閉させるための掛け金。
        /// 無いと、倒しっぱなしの間 <see cref="openHoldSeconds"/> ごとに開閉を繰り返す。</summary>
        private void StepStickHold()
        {
            if (sleepPlacement != null && sleepPlacement.Active)
            {
                _stickSince = -1f;
                _stickLatched = true; // 終了後も一度中立へ戻してから呼び戻す
                return;
            }
            bool down = _stickY * openStickSign >= openStickThreshold;
            if (!down)
            {
                _stickSince = -1f;
                _stickLatched = false;
                return;
            }
            if (_stickLatched) return;
            if (_stickSince < 0f) { _stickSince = Time.time; return; }
            if (Time.time - _stickSince < openHoldSeconds) return;
            _stickLatched = true;
            OpenOrRecall();
        }

        /// <summary>右スティックの縦 (VR) / マウスの縦 (Desktop)。
        /// **値を控えるだけ**で、判定は <see cref="StepStickHold"/> が行う。
        ///
        /// **Desktop では控えもしない** — マウスの縦移動が入ってくるので、
        /// 拾うと下を向いた瞬間に板が出る。</summary>
        public override void InputLookVertical(float value, VRC.Udon.Common.UdonInputEventArgs args)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || !lp.IsUserInVR()) return;
            _stickY = value;
        }

        // ============================================================
        // 突き合わせ (0.2s おき)
        // ============================================================

        private void Update()
        {
            RefreshVoiceForPosition();
            PollSharedRequests();
            if (Time.time < _nextPoll) return;
            _nextPoll = Time.time + PollInterval;
            Poll();
        }

        private void RefreshVoiceForPosition()
        {
            // IMP-DOG-036 S5: without sleepAudio (this world: the builder writes null) the gain is _voiceVolume, and every write of
            // _voiceVolume / _voiceEnabled already ends in ApplyDogVoiceSettings, so the per-frame compare can never differ.
            // With sleepAudio the gain follows the player's position (sleepAudio's own clock, no notice), so it is still compared.
            if (!_voiceFollowsPosition) return;
            if (_appliedVoiceGain != CurrentVoiceGain()) ApplyDogVoiceSettings();
        }
        private bool _voiceFollowsPosition;   // IMP-DOG-036 S5: sleepAudio was wired at Start (an Inspector reference; nothing assigns it at run time)

        private void Poll()
        {
            if (box == null) return;
            if (_state == KD_TreatBox.DispIdle) return;

            if (_state == KD_TreatBox.DispReserving)
            {
                // **移譲の通知を取りこぼしても進める床。** 通知 (_OnSlotOwnerChanged) は
                // 早い経路であって唯一の経路ではない
                if (OwnsBoth(_slotIndex)) Fire(KD_TreatBox.EvOwnerGranted, _curSeq);
                else if (Time.time - _stateSince > ownerTimeout)
                    Fire(KD_TreatBox.EvTimeout, _curSeq);
                return;
            }

            if (_state == KD_TreatBox.DispOut || _state == KD_TreatBox.DispRecalling)
            {
                // **枠が自分のものでなくなったこと**だけを見る。犬が食べても /
                // 放置タイムアウトでも / [しまう] でも、戻り道は 1 本しかない
                if (!MineNow()) { Fire(KD_TreatBox.EvRecalled, _curSeq); return; }
                if (_state == KD_TreatBox.DispRecalling && Time.time - _stateSince > stepTimeout)
                {
                    // Respawn の再試行と同じで、効くまで叩き直す
                    ReleaseSlot();
                    _stateSince = Time.time;
                }
                return;
            }

            // Reserved / Committing は数フレームで抜ける区間。
            // **ここで固まると枠が返るまで次が 1 個も出せない**ので床を置く
            if (Time.time - _stateSince > stepTimeout) Fire(KD_TreatBox.EvTimeout, _curSeq);
        }

        // ============================================================
        // 操作
        //
        // ボタンごとに専用のイベントを持つのは、Udon がパラメータ付きの
        // 呼び出しを信頼できないため (vrc-udonsharp §7)。行数は固定なので素直に並べる。
        // ============================================================

        public void _Row0() { Press(0); }
        public void _Row1() { Press(1); }
        public void _Row2() { Press(2); }
        public void _Row3() { Press(3); }

        private void Press(int row)
        {
            if (box == null) return;
            KD_ShopItem it = ItemAt(row);
            if (it == null) return;

            // **押す前の判定は門番に一本化する。** ここで条件を書き写すと、
            // 表示と実行の可否が乖離しうる形になる
            int gate = GateFor(it);
            if (gate != KD_TreatBox.GateOk) { _reason = gate; Redraw(); return; }

            // **新しい試行 = 新しい世代。** 前回の遅延イベントはここで無効になる
            _curSeq++;
            _wantProduct = it.productKey;
            _reason = KD_TreatBox.GateOk;
            Fire(KD_TreatBox.EvPress, _curSeq);

            // 成功してDispOutへ入れば閉じる。処理中・失敗時は理由を読むため表示を保つ。
            Redraw();
        }

        /// <summary>[しまう]。**犬が来なくても往復が閉じるための口。**
        ///
        /// `EvRecalled` を直接投げない — あれは「回収が終わった」の意味なので、
        /// 投げると枠を返さないまま Idle へ落ちて**以後 1 個も出せなくなる。**
        /// `Out → Recalling` を通して <see cref="KD_TreatSlot._Release"/> を叩く
        /// (**本番の犬もこの同じ口で戻す**ので、テスト専用の経路にならない)。
        /// ローカル専用なので `_` 始まり。</summary>
        public void _PutAway()
        {
            if (_state != KD_TreatBox.DispOut) return;
            Fire(KD_TreatBox.EvTimeout, _curSeq);
        }

        /// <summary>枠か実体の Ownership が動いた (<see cref="KD_TreatSlot"/> が知らせる)。
        ///
        /// **移譲イベントは移った先のオブジェクトでしか発火しない**ので、
        /// 出す側であるここには届かない。知らせを受けても**自分で両方を確かめる** —
        /// 片方だけ来た時点で書くと、もう片方への書き込みが捨てられる。
        /// ローカル専用なので `_` 始まり。</summary>
        public void _OnSlotOwnerChanged()
        {
            if (_state != KD_TreatBox.DispReserving) return;
            if (!OwnsBoth(_slotIndex)) return;
            Fire(KD_TreatBox.EvOwnerGranted, _curSeq);
        }

        /// <summary>実体が出た 1 フレーム後。**在庫が減るのはこのイベントだけ。**
        /// ローカル専用なので `_` 始まり。</summary>
        public void _OnPlaced() { Fire(KD_TreatBox.EvPlaced, _placedSeq); }

        /// <summary>ReserveSettle 明けの予約再試行 (破壊的監査 v2 RC-2)。
        /// `EnterState(DispReserved)` の予約部分と同じことを、settle が明けるまで
        /// 繰り返す。世代 (`_reserveRetrySeq`) と状態の両方で古い予約を遮断する。
        /// ローカル専用なので `_` 始まり。</summary>
        public void _RetryReserve()
        {
            if (_state != KD_TreatBox.DispReserved) return;
            if (_reserveRetrySeq != _curSeq) return;
            KD_TreatSlot slot = SlotAt(_slotIndex);
            if (slot == null) { Fire(KD_TreatBox.EvOwnerFailed, _curSeq); return; }
            if (!slot._TryReserve(_wantProduct))
            {
                if (slot.ReserveSettling())
                {
                    SendCustomEventDelayedSeconds(nameof(_RetryReserve), ReserveRetryDelay);
                    return;
                }
                Fire(KD_TreatBox.EvOwnerFailed, _curSeq);
                return;
            }
            if (slot.body != null) slot.body._AttachToHand();
            _placedSeq = _curSeq;
            SendCustomEventDelayedFrames(nameof(_OnPlaced), 1);
        }

        /// <summary>周期的に描き直す。ローカル専用なので `_` 始まり。</summary>
        public void _Refresh()
        {
            PollLiveRequest();
            Redraw();
            SendCustomEventDelayedSeconds(nameof(_Refresh), Mathf.Max(refreshSeconds, 0.1f));
        }

    }
}
