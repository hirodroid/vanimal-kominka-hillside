using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-ENV-004: おじいさん NPC — 庭の持ち主。**場の旗印 + 説明・案内役。**
    /// 正本は [npc-shop-menu.md](../../../docs/development/design/npc-shop-menu.md) (v2.1)。
    ///
    /// **売り子ではない** — 購入は従来どおり棚の実物に触れて行う
    /// (初稿の「購入もメニューへ一本化」は 2026-09-01 に撤回。
    /// 実物タッチ → 真上に確認パネルの因果最短の操作を、
    /// Canvas 4 段のポインティングへ置き換えるのは後退だった)。
    /// このクラスが持つのは**説明メニューの開閉と描画だけ**で、
    /// 台帳・カタログ・棚には一切書き込まない (残高を読むだけ)。
    /// 唯一の例外が IMP-PRG-009 の「おじいさんから 100pt」— はじめて話しかけたときに
    /// 台帳へ <see cref="KD_ProgressLedger._GrantKeeperBonus"/> を**頼む**だけで、
    /// 書くのは台帳 (一度きりの判定・保存も台帳側)。
    ///
    /// ## 開閉の形
    ///
    /// <code>
    /// 開く   Interact のみ (近接ゲートは無い — 勝手に開く板は写真に写り込む)
    /// 向き   開く瞬間だけ Y 軸ビルボード。開いた後は空間に固定
    /// 閉じる [またね] または hideDistance 超過
    /// </code>
    ///
    /// ヒステリシスが要らないのは開く条件が距離ではないから
    /// (棚の撫でFXメニューは show/hide の 2 距離が要る — あちらは近接で開くため)。
    /// </summary>
    /// <remarks>
    /// SyncMode None。**メニューは完全にローカル** — 開いたクライアントだけに見え、
    /// 複数人が同時に話しかけてもそれぞれ自分のメニューを操作できる。
    ///
    /// 描き直しは 1 秒周期の `SendCustomEventDelayedSeconds` 自己再帰
    /// (<see cref="KD_ShopShelf"/> と同型)。root が非アクティブな Canvas は
    /// 自分でループを回せないので、常時アクティブな NPC 本体が駆動する。
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_ShopKeeper : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/Shop Keeper が設定する)")]
        [Tooltip("残高の読み取り先。読むだけ + はじめて話しかけたときの 100pt を台帳へ頼む (書くのは台帳)")]
        public KD_ProgressLedger ledger;

        [Tooltip("開く瞬間にビルボード回転を書く親。メニュー Canvas はこの子。" +
                 "**開いた後は触らない** (顔の前に付いて回る板は酔うし写真に写る)")]
        public Transform pivot;

        [Tooltip("メニュー Canvas の root。**既定は非アクティブ** — " +
                 "Photo System/3 の「想定外の active Canvas」検査の対象")]
        public GameObject menuRoot;

        [Tooltip("「もっている N pt」。残高が読めるのは棚の面・ポーチ・ここの 3 つだけ " +
                 "(常時 HUD は作らない — 危険 7)")]
        public TextMeshProUGUI balanceLabel;
        [Tooltip("表示エリアの題 (選択中の話題名)。挨拶のときは空")]
        public TextMeshProUGUI infoTitle;
        [Tooltip("表示エリアの本文。**1 枚を話題ごとに書き替えて使い回す** — " +
                 "本文は文言表 (KD_ShopKeeper.*) を KD_Language 経由で引く。const は ja の写しで、" +
                 "Setup がその字を焼き、Verify が一致を検査する (IMP-ENV-020)")]
        public TextMeshProUGUI infoBody;

        [Tooltip("表示エリアのスクロール。話題を切り替えるたびに先頭へ戻す " +
                 "(戻さないと前の話題のスクロール位置で次の本文が開く)。" +
                 "verticalNormalizedPosition の setter は Udon 公開を確認済み " +
                 "(2026-09-01 レジストリ + 対照群)")]
        public UnityEngine.UI.ScrollRect contentScroll;

        [Tooltip("左ナビ 8 ボタンの背景 Image (生成順 = _Open* の navIndex。IMP-INF-011 で過ごし方、IMP-ENV-020 で呼ぶことが加わり 8)。選択中だけ色を変える。" +
                 "Image.color の setter は Udon 公開を確認済み (2026-09-01)")]
        public UnityEngine.UI.Image[] navImages;
        [Tooltip("ナビの通常色 (Setup が書く)")]
        public Color navColor = new Color(0.30f, 0.27f, 0.22f);
        [Tooltip("ナビの選択中色 (Setup が書く)")]
        public Color navActiveColor = new Color(0.52f, 0.43f, 0.26f);

        [Header("音")]
        public AudioSource sfx;
        public AudioClip openClip;
        public AudioClip closeClip;

        [Header("挙動")]
        [Tooltip("この距離まで離れたら閉じる (m)。**Interact の proximity より大きくする** " +
                 "(Shop Keeper/3 - Verify が大小関係を検査する)")]
        public float hideDistance = 4.0f;

        [Tooltip("残高と距離を見る間隔 (秒)")]
        public float refreshSeconds = 1f;

        // ============================================================
        // 文言の宣言 (vrc-tmp-font §3)
        //
        // **表示の正本は UITranslations.json の KD_ShopKeeper.* (13 言語)。** この const は
        // その ja の写しで、役割は 2 つ: ①フォントは Static なので Setup がこの const 群を読んで
        // TryAddCharacters する (字形の宣言) ②Shop Keeper/3 - Verify が手動改行の行幅を測る。
        // **文言を直すときは文言表と const を同時に直す** — ズレると Shop Keeper/3、Menu Finish/3・5、
        // ビルドガードが止める (IMP-ENV-020)。
        // 「もっている」「よみこみ中」は KD_ShopShelf の const を借りる
        // (KD_TreatPouch と同じ借用規約 — 同じ語を 2 箇所に持たない)。
        // ============================================================

        /// <summary>Interact の表示文言。Setup が UdonBehaviour.interactText へ書く。</summary>
        public const string InteractWord = "おじいさんに話しかける";

        public const string WordGreeting =
            "よく来たね。\nここはわしの庭だよ。\n犬たちと好きに遊んでいきな。\n" +
            "知りたいことがあれば、\n何でも聞いておくれ。";

        // IMP-PRG-009: はじめて話しかけたときだけの挨拶。{0} は台帳の keeperPoints
        // (文言に数を焼かない — 額を変えたら板も追随する)
        public const string WordGreetingGift =
            "よく来たね。\nここはわしの庭だよ。\n\nはじめてだね。\nこれを持っていきな。\n" +
            "{0}ptだ。\n好きなものと交換しな。\n\n犬たちと好きに遊んでいきな。\n" +
            "知りたいことがあれば、\n何でも聞いておくれ。";

        // IMP-ENV-005: 説明は「聞けば全部わかる」詳細版 — 仕様は隠さず全部言う
        // (入り口の説明ボードが簡素版。文面のレビュー記録は
        //  docs/plans/archive/2026-09-01-imp-env-005-dialogue-draft.md と
        //  docs/plans/2026-09-18-shopkeeper-guidance-refresh.md (IMP-ENV-020 の再構成))。
        // 1 行 ≤ 16 全角 (本文 fontSize 26 / 有効幅 440px = viewport 460 − 左右 margin 10。Verify が実測で溢れを検査する。
        //  縦は制限なし — 本文はスクロールで収まる。規約が破れた行は wrap が折り返す
        //  = 欠けはしないが読みにくい (BUG-ENV-001 の安全網。免罪符にしない)
        public const string WordWays = "過ごし方";
        public const string InfoWays =
            "ここでの過ごし方だよ。\n" +
            "好きな順でいい。\n" +
            "\n" +
            "犬をなでる。\n" +
            "ボールを投げる。\n" +
            "おやつをあげる。\n" +
            "それだけでも、じゅうぶんだ。\n" +
            "\n" +
            "手元メニューの「呼ぶ」で、\n" +
            "好きな場所に犬を呼んで\n" +
            "そばで寝てもらえる。\n" +
            "くわしくは「呼ぶこと」だ。\n" +
            "\n" +
            "いい瞬間は写真になって、\n" +
            "縁側の板に並ぶ。\n" +
            "「写真のこと」で話すよ。\n" +
            "\n" +
            "「かざり」で犬におめかし、\n" +
            "「パーティクル」でなでた\n" +
            "ときの光を変えられる。\n" +
            "「ライブ」は誰かが開けば、\n" +
            "その場のみんなで見られる。\n" +
            "「あそぶ」には犬目線カメラと\n" +
            "トレーナーの遊びがある。\n" +
            "ライブやトレーナーの条件と\n" +
            "買い物は「おかいもの」で話すよ。\n" +
            "\n" +
            "となりの板では、犬に呼び名を\n" +
            "つけて、お気に入りを選べる。\n" +
            "\n" +
            "ぜんぶやらなくていい。\n" +
            "ながめているだけでも\n" +
            "いいもんだよ。";
        public const string WordHowTo = "メニュー";
        public const string WordPetting = "なでかた";
        public const string WordDogs = "犬のこと";
        public const string WordPhotos = "写真のこと";
        public const string WordShop = "おかいもの";
        public const string WordTreats = "おやつのこと";
        public const string WordBye = "またね";
        /// <summary>IMP-ENV-020: 8 話題目 (呼ぶ・姿勢と動作・呼べる人)。</summary>
        public const string WordCall = "呼ぶこと";

        public const string InfoHowTo =
            "まずは手元のメニューを\n" +
            "開いてみな。\n" +
            "VRは右スティックを下に\n" +
            "倒したままにすると出る。\n" +
            "DesktopはTキー、\n" +
            "スマホは画面にある\n" +
            "「メニュー」を押しな。\n" +
            "\n" +
            "上の項目は7つだ。\n" +
            "「かざり」犬に飾りをつける。\n" +
            "「おやつ」おやつを手元に出す。\n" +
            "「呼ぶ」犬をそばに呼び、\n" +
            "ポーズを選ぶ。\n" +
            "「おもちゃ」犬のボールを\n" +
            "手元へ戻す。\n" +
            "「ライブ」庭でライブを開く。\n" +
            "「パーティクル」なでたときの\n" +
            "光を選ぶ。\n" +
            "「あそぶ」犬目線カメラと、\n" +
            "トレーナーの遊び。\n" +
            "\n" +
            "上の「ポイント」を押すと、\n" +
            "ポイントの入手方法と\n" +
            "ギフトコードの入力があるよ。\n" +
            "「設定」には音量、暗さ、\n" +
            "言語、写真の解像度がある。\n" +
            "ラジオと距離フェードも\n" +
            "あるが、どちらもPCだけだ。\n" +
            "管理者なら、呼ぶとライブを\n" +
            "みんなに許す項目もある。\n" +
            "管理者は、この部屋の\n" +
            "オーナーのことだよ。\n" +
            "オーナーがいないときは\n" +
            "Masterが代わりをする。\n" +
            "「はじめに」も設定の\n" +
            "インフォメーションから\n" +
            "読み返せる。\n" +
            "\n" +
            "使い終わったら「閉じる」だ。\n" +
            "VRとスマホでは、開く操作を\n" +
            "もう一度すると、遠くに\n" +
            "置いてきても手元に戻る。\n" +
            "DesktopのTキーは\n" +
            "開く・閉じるの切り替えだ。";

        public const string InfoPetting =
            "犬は、頭のうえ、ほっぺた、\n" +
            "あごの下をなでてやれるよ。\n" +
            "片手でもよろこぶが、\n" +
            "両手だと大喜びで、\n" +
            "出てくる光の演出も変わる。\n" +
            "\n" +
            "光の演出は、手元メニューの\n" +
            "「パーティクル」で選べる。\n" +
            "無料のものもあるし、\n" +
            "ポイントで買うものや、\n" +
            "ライブの商品についてくる\n" +
            "ものもあるよ。";

        public const string InfoDogs =
            "ほうっておいても、犬たちは\n" +
            "追いかけっこをしたり、\n" +
            "じゃれ合ったりしている。\n" +
            "自然に始まるじゃれ合いは\n" +
            "誰でもただで見られるよ。\n" +
            "つかれたら水を飲んで、\n" +
            "伏せて休むんだよ。\n" +
            "ながめているだけでも\n" +
            "いいもんだよ。\n" +
            "\n" +
            "ボールは犬ごとに色と大きさが\n" +
            "きまっていて、投げると\n" +
            "その犬が持ってきてくれるよ。\n" +
            "Desktopはクリック長押しで\n" +
            "ためて、離すと投げられる。\n" +
            "VRで手に持ったまま\n" +
            "投げるふりをすると、\n" +
            "こちらを見たり、\n" +
            "吠えたりすることもあるよ。\n" +
            "\n" +
            "ボールが見つからなければ、\n" +
            "メニューの「おもちゃ」で\n" +
            "その犬の「手元へ」を押しな。\n" +
            "誰かが持っている間や、\n" +
            "犬がそのボールで遊んでいる\n" +
            "間は、戻せないよ。\n" +
            "\n" +
            "犬をそばに呼んで\n" +
            "寝てもらう話は「呼ぶこと」だ。\n" +
            "\n" +
            "となりの「庭の犬たち」の板で、\n" +
            "犬に呼び名をつけたり、\n" +
            "お気に入りを選んだりできるよ。\n" +
            "くわしくは、板の「？」を\n" +
            "見てみな。";

        public const string InfoPhotos =
            "犬と遊んでいると、\n" +
            "いい瞬間が写真になって、\n" +
            "縁側の写真板に並ぶよ。\n" +
            "寝室には、添い寝の写真だ。\n" +
            "写真は種類ごとに、\n" +
            "その枠がいっぱいになると\n" +
            "古いものから入れ替わるよ。\n" +
            "\n" +
            "写真はきみだけのもので、\n" +
            "ほかの人には別の写真が\n" +
            "見えている。\n" +
            "このワールドから出ると\n" +
            "写真は消えるよ。\n" +
            "気に入った一枚があったら、\n" +
            "PCやQuestでは、\n" +
            "VRChatのカメラを\n" +
            "写真に向けて撮りな。\n" +
            "スマホでは写真を押して\n" +
            "大きくしてから、\n" +
            "スクリーンショットだ。\n" +
            "フレームは無料で選べる。\n" +
            "\n" +
            "犬目線カメラは、\n" +
            "ポイントで買う道具だよ。\n" +
            "「あそぶ」から使えて、\n" +
            "映像は自分だけに見える。\n" +
            "ここの写真とは別のものだ。\n" +
            "\n" +
            "くわしい説明は、\n" +
            "写真板の「？」を見な。\n" +
            "撮影を止めたり、\n" +
            "画質を変えたりするなら、\n" +
            "隣の「解像度設定」だよ。";

        public const string InfoShop =
            "ポイントは、初めて来たときに\n" +
            "100ptもらえる。\n" +
            "わしと初めて話したときも、\n" +
            "一度だけ100ptあげるよ。\n" +
            "あとはここでのんびり\n" +
            "過ごしていれば、\n" +
            "1分ごとに1ptたまる。\n" +
            "なでても投げても増えない。\n" +
            "ギフトコードでも\n" +
            "ポイントを受け取れるよ。\n" +
            "ポイントはこの板と、\n" +
            "手元メニューで見られる。\n" +
            "コードは、手元メニューの\n" +
            "「ポイント」を押した先で\n" +
            "使えるよ。\n" +
            "\n" +
            "ここにあるものは4つに分かれる。\n" +
            "\n" +
            "無料で遊べるもの。\n" +
            "なでる、投げる、呼ぶ、\n" +
            "みんなが開いたライブや遊び。\n" +
            "\n" +
            "ポイントで買うもの。\n" +
            "おもちゃとおやつは、\n" +
            "棚の品物を押して、\n" +
            "確認画面から買えるよ。\n" +
            "ほかのポイント商品は、\n" +
            "手元メニューで買える。\n" +
            "おやつは使うたびに減るが、\n" +
            "ほかは一度買えば使える。\n" +
            "\n" +
            "VRChatのお店で買う\n" +
            "買い切りの商品。\n" +
            "おもちゃや飾りのセット、\n" +
            "ライブがある。\n" +
            "販売期間が限られる\n" +
            "商品もあるよ。\n" +
            "\n" +
            "月ごとの会員が、\n" +
            "ばにまるトレーナーだよ。\n" +
            "会員期間を買って使えて、\n" +
            "自動では更新されない。\n" +
            "「あそぶ」から、集合や\n" +
            "かけっこを開いたり、\n" +
            "空いている2匹を選んで\n" +
            "じゃれ合ってもらえる。\n" +
            "参加する人は会員でなくても\n" +
            "無料で楽しめるよ。\n" +
            "くわしくは「設定」の\n" +
            "インフォメーションを見な。\n" +
            "\n" +
            "ライブを開けるのは、\n" +
            "その作品を持つ管理者だよ。\n" +
            "管理者は部屋のオーナーで、\n" +
            "いないときはMasterだ。\n" +
            "「ライブ開催を全員に許可」\n" +
            "がONなら、ほかの人も\n" +
            "自分の持つ作品を開ける。\n" +
            "はじめからONになっている。\n" +
            "同時に開けるのは一本で、\n" +
            "終わったあともしばらくは\n" +
            "次のを開けない。\n" +
            "待ち時間は画面を見な。\n" +
            "見るのは誰でも無料だよ。\n" +
            "犬が出るライブもある。";

        public const string InfoTreats =
            "手元のメニューで「おやつ」を\n" +
            "選び、「出す」を押しな。\n" +
            "おやつが出るとメニューは\n" +
            "閉じるから、犬にあげておくれ。\n" +
            "\n" +
            "手に持っていると犬が寄ってきて、\n" +
            "手放すと、一番近くにいる犬が\n" +
            "食べてくれるよ。\n" +
            "一度に出せるのはひとつだけ。\n" +
            "はじめに5個持たせてあるから、\n" +
            "なくなったら棚で\n" +
            "ポイントと交換しな。";

        public const string InfoCall =
            "［呼び方］\n" +
            "手元メニューの「呼ぶ」で\n" +
            "犬を選び、ポーズを選んで、\n" +
            "場所と向きを決める。\n" +
            "場所を決める間は、\n" +
            "メニューが閉じるよ。\n" +
            "決めたあとも閉じたままで、\n" +
            "犬が歩いてくる。\n" +
            "自分が呼んだ犬を\n" +
            "自由にするなら、\n" +
            "「呼ぶ」でその犬を選び、\n" +
            "「自由にする」を押しな。\n" +
            "\n" +
            "［姿勢と動作］\n" +
            "ごろん、ふせ、おすわり、\n" +
            "うずくまるの4つは無料だ。\n" +
            "へそ天は、ポイントで買うと\n" +
            "ポーズで選べる。\n" +
            "お手とあご乗せも、\n" +
            "ポイントで買う動作だよ。\n" +
            "おすわりでお手、\n" +
            "ふせであご乗せだ。\n" +
            "VRでは、呼んだ本人が\n" +
            "犬の前に手を差し出して、\n" +
            "少しそのまま待ちな。\n" +
            "手を犬から遠ざけると\n" +
            "終わるよ。\n" +
            "Desktopとスマホでは、\n" +
            "犬の近くでメニューを開き、\n" +
            "「呼ぶ」の動作ボタンを\n" +
            "押しな。しばらくその姿で\n" +
            "応えてくれるよ。\n" +
            "伸びとあくびはセットで、\n" +
            "これもポイントで買う動作だ。\n" +
            "犬が着いてから、\n" +
            "どの端末でも「呼ぶ」の\n" +
            "ボタンで一回ずつ頼めるよ。\n" +
            "追加の姿勢や動作は、\n" +
            "一度買えば6犬種で使える。\n" +
            "トレーナーには含まれない。\n" +
            "\n" +
            "［呼べる人］\n" +
            "犬を呼べるのは、部屋の\n" +
            "オーナー (いなければMaster) か、\n" +
            "「呼ぶを全員に許可」が\n" +
            "ONのときの参加者だよ。\n" +
            "姿勢や動作を買っても、\n" +
            "呼ぶ許可は別に必要だ。\n" +
            "呼んだ犬のポーズを\n" +
            "変えられるのは、呼んだ本人だけ。\n" +
            "ほかの犬が空いていれば、\n" +
            "そっちを呼びな。";

        public KD_Language language;
        private string L(string source) { return language != null ? language._Text(source) : source; }

        private void Start()
        {
            // 欠けていても Interact が黙って何もしないだけなので、起動時に名指しする
            // (KD_BallResetButton と同じ保険)
            if (menuRoot == null || infoBody == null || contentScroll == null)
                Debug.LogError("[KD_ShopKeeper] wiring incomplete. " +
                    "Run Tools/KominkaDogRun/Shop Keeper setup.");
            CloseMenu();
            SendCustomEventDelayedSeconds(nameof(_Refresh), Mathf.Max(refreshSeconds, 0.25f));
        }

        // ============================================================
        // 操作 — 左ナビ固定 + 右表示エリアの 1 枚構成 (IMP-ENV-005 v2.4)。
        // ページ切り替えは無い。ボタンが表示エリアの中身を差し替えるだけ
        // ============================================================

        public override void Interact()
        {
            if (menuRoot == null) return;
            FacePlayer();
            _giftPending = true;
            ShowTopic(-1, "", "KD_ShopKeeper.WordGreeting");   // 開いた直後は挨拶 (どのナビも非選択)
            menuRoot.SetActive(true);
            _TryGift();
            RedrawBalance();
            Play(openClip);
        }

        /// <summary>IMP-PRG-009: はじめて話しかけたときの 100pt。台帳が復元前なら
        /// 板を開いている間 <see cref="_Refresh"/> が 1 秒ごとに再試行する
        /// (入室直後に話しかけた人が取りこぼさないように)。付与できたら挨拶を
        /// 贈り物版へ差し替える (残高の行も同時に増える = 因果が読める)。
        /// ローカル専用なので `_` 始まり。</summary>
        public void _TryGift()
        {
            if (!_giftPending || ledger == null || !ledger.Loaded()) return;
            _giftPending = false;
            if (!ledger._GrantKeeperBonus()) return;
            ShowTopic(-1, "", "KD_ShopKeeper.WordGreetingGift");
            RedrawBalance();
        }
        private bool _giftPending;

        /// <summary>[あそびかた]。ローカル専用なので `_` 始まり。</summary>
        public void _OpenWays() { ShowTopic(6, "KD_ShopKeeper.WordWays", "KD_ShopKeeper.InfoWays"); }

        public void _OpenHowTo() { ShowTopic(0, "KD_ShopKeeper.WordHowTo", "KD_ShopKeeper.InfoHowTo"); }

        /// <summary>[なでかた]。ローカル専用なので `_` 始まり。</summary>
        public void _OpenPetting() { ShowTopic(1, "KD_ShopKeeper.WordPetting", "KD_ShopKeeper.InfoPetting"); }

        /// <summary>[犬のこと]。ローカル専用なので `_` 始まり。</summary>
        public void _OpenDogs() { ShowTopic(2, "KD_ShopKeeper.WordDogs", "KD_ShopKeeper.InfoDogs"); }

        /// <summary>[写真のこと]。ローカル専用なので `_` 始まり。</summary>
        public void _OpenPhotos() { ShowTopic(3, "KD_ShopKeeper.WordPhotos", "KD_ShopKeeper.InfoPhotos"); }

        /// <summary>[おかいもの]。ローカル専用なので `_` 始まり。</summary>
        public void _OpenShop() { ShowTopic(4, "KD_ShopKeeper.WordShop", "KD_ShopKeeper.InfoShop"); }

        /// <summary>[おやつのこと]。ローカル専用なので `_` 始まり。</summary>
        public void _OpenTreatInfo() { ShowTopic(5, "KD_ShopKeeper.WordTreats", "KD_ShopKeeper.InfoTreats"); }

        /// <summary>[呼ぶこと] (IMP-ENV-020 で 8 話題目)。ローカル専用なので `_` 始まり。</summary>
        public void _OpenCall() { ShowTopic(7, "KD_ShopKeeper.WordCall", "KD_ShopKeeper.InfoCall"); }

        /// <summary>[またね]。ローカル専用なので `_` 始まり。</summary>
        public void _Close()
        {
            CloseMenu();
            Play(closeClip);
        }

        // ============================================================
        // 表示
        // ============================================================

        /// <summary>周期処理。**`Update` ではない** — 1 秒に 1 回だけ走る。
        /// 開いている間だけ、距離クローズと残高の追従を行う。
        /// ローカル専用なので `_` 始まり。</summary>
        public void _Refresh()
        {
            if (menuRoot != null && menuRoot.activeSelf)
            {
                var lp = Networking.LocalPlayer;
                if (lp != null &&
                    Vector3.Distance(lp.GetPosition(), transform.position) >= hideDistance)
                {
                    // 立ち去ったら黙って閉じる (音は鳴らさない — 本人はもう見ていない)
                    CloseMenu();
                }
                else
                {
                    _TryGift();
                    RedrawBalance();
                }
            }
            SendCustomEventDelayedSeconds(nameof(_Refresh), Mathf.Max(refreshSeconds, 0.25f));
        }

        /// <summary>開く瞬間の Y 軸ビルボード。
        /// **World Space Canvas の前面は -Z 側**なので、板をプレイヤーへ向けるのは
        /// `LookRotation(プレイヤー方向)` ではなく `LookRotation(板位置 - 読者位置)`
        /// (`vrc-udonsharp` §5)。水平成分だけ使い、上を向かせない。</summary>
        private void FacePlayer()
        {
            var lp = Networking.LocalPlayer;
            if (lp == null || pivot == null) return;
            Vector3 dir = pivot.position - lp.GetPosition();
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            pivot.rotation = Quaternion.LookRotation(dir);
        }

        /// <summary>表示エリアの中身を差し替える。<paramref name="navIndex"/> は
        /// 選択中ナビのハイライト先 (-1 = どれも選択しない = 挨拶)。</summary>
        private void ShowTopic(int navIndex, string title, string body)
        {
            _topicTitle = title;
            _topicBody = body;
            if (infoTitle != null) infoTitle.text = L(title);
            if (infoBody != null) infoBody.text = Body(body);
            if (navImages != null)
                for (int i = 0; i < navImages.Length; i++)
                    if (navImages[i] != null)
                        navImages[i].color = i == navIndex ? navActiveColor : navColor;
            // 1 = 先頭 (上端)。前の話題のスクロール位置を引きずらない
            if (contentScroll != null) contentScroll.verticalNormalizedPosition = 1f;
        }

        private void CloseMenu()
        {
            if (menuRoot != null) menuRoot.SetActive(false);
            _giftPending = false;   // 閉じたら再試行しない (次に話しかけたとき改めて)
        }

        /// <summary>本文の翻訳。贈り物の挨拶だけ {0} に台帳の額を入れる。</summary>
        private string Body(string key)
        {
            string text = L(key);
            if (key == "KD_ShopKeeper.WordGreetingGift" && ledger != null)
                text = text.Replace("{0}", ledger.keeperPoints.ToString());
            return text;
        }

        private string _topicTitle;
        private string _topicBody;
        public void _RefreshLanguage()
        {
            if (infoTitle != null && _topicTitle != null) infoTitle.text = L(_topicTitle);
            if (infoBody != null && _topicBody != null) infoBody.text = Body(_topicBody);
            RedrawBalance();
        }

        private void RedrawBalance()
        {
            if (balanceLabel == null) return;
            // **復元前に 0 と書かない** — 「消えた」に見える (KD_ShopShelf.Redraw と同じ)
            if (ledger == null) balanceLabel.text = L("KD_ShopShelf.WordNoLedger");
            else if (!ledger.Loaded()) balanceLabel.text = L("KD_ShopShelf.WordLoading") + "...";
            else balanceLabel.text =
                L("KD_ShopShelf.WordRemain") + " " + ledger.Points().ToString() + " pt";
        }

        private void Play(AudioClip clip)
        {
            if (sfx == null || clip == null) return;
            sfx.PlayOneShot(clip);
        }
    }
}
