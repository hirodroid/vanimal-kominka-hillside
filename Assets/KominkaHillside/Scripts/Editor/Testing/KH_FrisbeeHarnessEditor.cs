using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// フリスビーの ClientSim 自動テスト台 (IMP-DOG-008 の繰り返し確認用)。
    ///
    /// 何をするか: プロトタイプシーン (KH_FrisbeePrototype) を単独で開き、Play へ入り、
    /// 固定投げ台 (KH_FrisbeeStand) の Interact を 6 プリセット x 6 投げ分だけ叩き、
    /// 毎回 [KH_Frisbee*] のコンソール行から成否を分類して JSON に残す。
    /// 1 投げの合否は 4 つ: 想定内の結果 (outcomeOk) / 一周を終えた (completed) /
    /// 動きが機械でない (motionOk) / 期待した終わり方 (expectOk)。
    ///
    /// 原則 (古民家ドッグランの KD_ClientSimHarnessEditor と同じ):
    ///  - 入口は固定 (メニュー 1 つ)。終了も固定 (完了・失敗・停止・時間切れのどれでも Edit モードへ戻る)。
    ///  - シーンは保存しない。保存シーンへコンポーネントを足さない。Editor アセンブリだけで動く。
    ///  - 結果はファイル。`START_REQUEST_ACCEPTED` は受付であって PASS ではない。
    ///  - 実行時の値は **ランタイムの UdonBehaviour** から読む (Play 中の U# プロキシは信用しない)。
    /// </summary>
    [InitializeOnLoad]
    public static class KH_FrisbeeHarnessEditor
    {
        const string Menu = "Tools/KominkaHillside/Frisbee/";
        const string Base = "Logs/kh-frisbee-harness";
        const string Journal = Base + "/journal.json";
        const string StopFlag = Base + "/stop.flag";
        const string OptionsPath = Base + "/options.json";
        const string Key = "KH.Frisbee.Harness.Run";

        // シーン内のオブジェクト名 (KH_FrisbeePrototypeEditor.BuildInto が作る名前)。
        // IMP-DOG-012: 試作シーンは犬種ごとのレーン 6 本。--breed=<Key> (既定 Lab) でレーンを選び、名前は
        // KH_FrisbeePrototypeEditor.LaneName から組む (Lab レーンは従来の名前のまま = 既存の記録と比べられる)。
        // 投げの開始位置・向きはレーンの投げ台と犬の立ち位置 (Lab と同じ相対配置を x へずらしたもの) で決まるので、
        // 投げ表と期待する終わり方は Lab の表をそのまま使う。
        const string DefaultBreed = "Lab";
        static string laneBreed = DefaultBreed;
        static string CatcherName { get { return "KH_FrisbeeCatch_" + laneBreed; } }
        static string ThrowerName { get { return KH_FrisbeePrototypeEditor.LaneName("KH_FrisbeeStand", laneBreed); } }
        static string RecallName { get { return KH_FrisbeePrototypeEditor.LaneName("KH_FrisbeeRecall", laneBreed); } }
        static string CyclerName { get { return KH_FrisbeePrototypeEditor.LaneName("KH_FrisbeeVariant", laneBreed); } }
        // プロトタイプの ClientSim の出現位置 (KH_FrisbeePrototypeEditor.BuildInto の Spawn)。レーンを選んだら
        // プレイヤーをそのレーンの同じ位置へ移す (犬は投げた人の足元へ持ち帰るので、Lab 以外のレーンで
        // 出現位置のままだと帰り道が 12-36 m 伸び、16 秒の窓に収まらない)。
        static readonly Vector3 SpawnPos = new Vector3(0f, 0f, -6f);

        const int StateIdle = 0, StateReady = 10;
        // IMP-DOG-017: beside の投げで犬を置く、投げ台からの横の距離 (よしよしの後の、手の届く距離)。
        const float BesideGap = 0.6f;
        const int VariantCount = 6;
        // 全体の番犬。下限 240 秒 (設計値)。36 投げの全面実行は 1 投げ 16 秒 + 片付けで 240 秒に収まらないので、
        // 計画した投げ数に応じて伸ばす。options.json の globalTimeoutSeconds で上書きできる。
        const double MinBudget = 240.0;
        const double BudgetPerThrow = 28.0;
        // Codex M1: 1 投げの窓は「Idle へ戻るまで」を含む。遠投 (far) は取ってから人の足元まで
        // 咥えて戻り、置いて Idle へ戻るまでに 12-14 秒かかるので、10 秒では「取れたのに未完」に
        // なっていた (それを ok にしていたのが M1 の指摘)。窓を 16 秒に広げ、完走も合否に入れる。
        const double ThrowBudget = 18.0;      // 1 投げ (投げてから片付けるまで) の上限 (秒)
        const double ThrowSettle = 16.0;      // 投げてから Idle へ戻るのを待つ上限 (秒)
        // 動きの質のしきい値は捕り手側の定数をそのまま使う (2 か所に数字を置かない)。
        const float MotionMaxYawRate = KH_FrisbeeCatch.MotionMaxYawRate;
        const float MotionMinSpeed = KH_FrisbeeCatch.MotionMinSpeed;
        const float MotionWindow = KH_FrisbeeCatch.MotionWindow;
        const float MotionMinPath = KH_FrisbeeCatch.MotionMinPath;

        // ---------------------------------------------------------------- netorder (BUG-DOG-003)
        // 投げの通知 (ネットワークイベント) と円盤の held (VRCPickup の同期) は別の経路で届くので、
        // 犬の Owner では「通知が先、まだ手の中」という順序が起こる。その順序だけを 1 クライアントで
        // 再現する mode。捕り手の debugHeldOverride (0 = 実値 / 1 = 保持扱い / -1 = 解放扱い) を
        // ハーネスが切り替えて作る。**実際の通信の到着順は再現しない** (それは 2 人実機でしか出ない)。
        //
        // 期限の秒は捕り手の定数をそのまま使う (2 か所に数字を置かない)。PendingHoldSeconds を
        // 変えたら、下の切り替え時刻も自動で追従する。
        const float NetHold = KH_FrisbeeCatch.PendingHoldSeconds;
        const int NetVariant = 0;             // netorder はプリセットの違いを見ない (順序だけ)
        const string NetShot = "flat";        // 跳んで取る基準の投げ 1 本だけを使う
        const double NetReleaseEarly = NetHold * 0.25;   // held-first: 期限のはるか手前で手を離す
        const double NetReleaseLate = NetHold * 1.3;     // expired:   期限を過ぎてから手を離す
        const double NetRegrabHold = NetHold * 0.5;      // regrab:    掴み直しを保つ長さ
        // 下の 3 つは保留の長さとは無関係の**観測窓**なので PendingHoldSeconds からは導かない。
        const double NetExpiredWatch = 2.0;   // 失効のあと「何も起きない」ことを見る窓
        const double NetRegrabSettle = 1.0;   // 取消のあと Idle/Ready へ落ち着くまでの猶予
        const double NetReadyWait = 3.0;      // 投げる前に Ready (state 10) になるのを待つ上限
        const double NetPlanWait = 4.0;       // regrab / steal-rethrow: plan= がここまでに出なければ打ち切る
        // steal-rethrow は「1 投げ目の追走 → 奪取 → 2 投げ目の一周」を 1 ケースで通すので、
        // 判定の窓と番犬をこの分だけ伸ばす (既定モードの ThrowSettle / ThrowBudget は触らない)。
        const double NetStealExtra = 6.0;

        // 持ち帰りの証拠に使う state (Codex M1)。捕り手の定数をそのまま参照する。
        const int StateJumpCarry = KH_FrisbeeCatch.StateJumpCarry, StatePickup = KH_FrisbeeCatch.StatePickup,
            StateReturn = KH_FrisbeeCatch.StateReturn, StateDrop = KH_FrisbeeCatch.StateDrop;
        const int NetStateCount = KH_FrisbeeCatch.StateReady + 1;

        struct NetCase { public string name; public bool holdBeforeThrow; public string expect; public string note; }

        static readonly NetCase[] NetCases =
        {
            new NetCase { name = "held-first", holdBeforeThrow = true,
                expect = "req hold -> req resolved -> throw -> plan=",
                note = "通知が先に着き、円盤はまだ手の中に見える。保留に入り、手を離した最初のフレームで受理される (これが直したい順序そのもの)" },
            new NetCase { name = "release-first", holdBeforeThrow = false,
                expect = "req accept path=local (hold 無し)",
                note = "対照。従来どおり手を離した後に通知が着く。既定モードの 4 条件 (outcome/completed/motion/expectKind) もここだけ合否に入れる" },
            new NetCase { name = "expired", holdBeforeThrow = true,
                expect = "req hold -> req expired、その後は静か",
                note = "手の中のまま期限を越える。保留は捨てられ、あとから手を離しても投球が蘇らないことを見る" },
            new NetCase { name = "regrab", holdBeforeThrow = false,
                expect = "req accept -> plan= -> req cancel reason=held-regrab",
                note = "受理して追いかけている最中に掴み直される。解放を 1 度見た後の手は新しい掴みとして勝ち、犬は追走をやめる" },
            // Codex U3: 追走中に相手が円盤を取得し、そのまま投げ直す。1 ケースで 2 投げぶん走る。
            new NetCase { name = "steal-rethrow", holdBeforeThrow = false,
                expect = "req accept(gen N) -> plan= -> req cancel reason=held-regrab|superseded (gen N) -> req hold(gen N+1) -> req resolved(gen N+1) -> throw -> plan= -> 持ち帰り",
                note = "追走中に相手が取って即投げ直す。古い投球は取り消され、新しい投球だけが保留から解決して一周まで行く (取消が新しい gen の受理より先に出ること)" },
        };

        // ---------------------------------------------------------------- 投げ表
        // expectKind (Codex M2): その投げが「どう終わったか」の期待。outcome (取れたか) だけでは
        // 「跳ばずに立って取った」回帰 (4 回目の v4) を落とせないので、終わり方も行ごとに書く。
        //   jump   = 跳んで取った (catch OK / MISS。踏み切りのある行)
        //   stand  = 立ったまま取った / 取り損ねた (stand catch ...)
        //   over   = 走り抜けざまに取った (overtake catch ...。round 5 から全プリセットで出うる)
        //   ground = 落ちたものを拾いに行った (plan=2)
        //   any    = 問わない
        //   a|b    = どれか (IMP-DOG-017: Lab の beside の jump|over|ground)
        // IMP-DOG-017 で足した列:
        //   place       "" = 犬をシーンの立ち位置へ戻す (従来)。"beside" / "chase" = 投げ台からの相対位置へ置く (PlaceDogFor)
        //   variant     -1 = --variants の各プリセットで流す (従来)。0 以上 = そのプリセットで 1 回だけ流す
        //   expectMode  "" = 問わない。跳んだ投げ (kind=jump) の takeoff / catch 行の mode= がこれであること
        //   labAllowed / labExpectKind / labExpectMode  Lab レーンだけの期待 (null = 上と同じ)
        struct Shot
        {
            public string name; public float speed, elevation, side; public string[] allowed; public string expectKind; public string note;
            public string place; public int variant; public string expectMode;
            public string[] labAllowed; public string labExpectKind, labExpectMode;
        }

        static readonly Shot[] Shots =
        {
            new Shot { variant = -1, name = "flat", speed = 7.5f, elevation = 10f, allowed = new[] { "caught" }, expectKind = "jump",
                note = "基準の投げ。跳んで取る (立って取ったら不合格)" },
            // ClientSim の Desktop 投げ (5.6-6.2 m/s・低い仰角、犬は 6.5-7.5 m 前) を再現する投げ。
            // ひねり (v4) が跳ばずに立って取る / 地上回収へ落ちる回帰は、この幾何でだけ出た。
            // 固定台では犬が 10.5 m 先なので全プリセットで「立って取る」幾何になる = 終わり方は問わない。
            new Shot { variant = -1, name = "mid",  speed = 5.8f, elevation = 8f,  allowed = new[] { "caught" }, expectKind = "any",
                note = "ClientSim の Desktop 投げの再現。低く短い。取れれば跳んでも立ってもよい (round 5: 追い抜き over も全プリセットで出うる。outcome は caught なので落ちない)" },
            new Shot { variant = -1, name = "lob",  speed = 6f,   elevation = 35f, allowed = new[] { "caught" }, expectKind = "jump",
                note = "山なり。滞空が長いので先回りして跳ぶ" },
            new Shot { variant = -1, name = "low",  speed = 5f,   elevation = 0f,  allowed = new[] { "caught", "ground" }, expectKind = "ground",
                note = "水平・低速。犬の手前に落ちるので拾いに行く" },
            new Shot { variant = -1, name = "far",  speed = 9f,   elevation = 15f, allowed = new[] { "caught" }, expectKind = "jump",
                note = "遠投。走って追いついて跳ぶ" },
            // round 5 (2026-09-22): 横へ 25 度ずらした投げ。固定台の +Z 一本槍では、真正面の幾何しか
            // 通っていなかった (回り込み・旋回・横からの踏み切りが 1 度も測られない)。取れるかどうかは
            // 基準にしない (allowed に ground を入れてある) — **etaErr を横の幾何でも採るための投げ**で、
            // 合否ではなく分布を見るために足した。
            new Shot { variant = -1, name = "side", speed = 7f,   elevation = 10f, side = 25f, allowed = new[] { "caught", "ground" }, expectKind = "any",
                note = "横 25 度。回り込みと横からの踏み切りを通す (取れ方は問わない)" },
            // IMP-DOG-017 (2026-09-26): 位置取りの投げ。固定台の立ち位置 (1.5, 0, 6.5) は落下点の横なので、
            // 「犬が落下点の後ろから追いつく」(B chase) が 1 度も測られていなかった。どちらもプリセット 1 つで 1 回。
            // beside: よしよしの後に投げる流れ (犬は投げ手の 0.6 m 横、投げ手を向く)。小型・中型は追いついて跳ぶ。
            // Lab は追い抜き (over) か地上回収 (ground) も許す (跳んだなら mode=chase を求める)。Lab 以外は跳ぶこと。
            new Shot { variant = KH_FrisbeeCatch.VariantNormal, place = "beside", name = "beside", speed = 6f, elevation = 10f,   // 6 m/s = PCVR 2026-09-25 のよしよし直後の投げの実測 (5.5-7.1、中央値 6.0)。7 だと小型犬の B は余裕 0 の際どい候補になる
                // 2026-09-26 速度を犬種別に下げた後 (Pome 8.0 / Corgi・Dachs 8.5): 6 m/s の投げでも小型犬は横からは物理的に届かない (solve=0)。
                // 全犬種 jump|over|ground を許し、跳んだなら mode=chase を求める。跳べる速さに戻すかは PCVR の判断 (IMP-DOG-017)。
                allowed = new[] { "caught", "ground" }, expectKind = "jump|over|ground", expectMode = "chase",
                labAllowed = new[] { "caught", "ground" }, labExpectKind = "jump|over|ground",
                note = "投げ手の横 0.6 m (投げ手向き) から 7 m/s。Lab 以外は追いついて跳ぶ (mode=chase)、Lab は over / ground も許す (跳ぶなら mode=chase)" },
            // chase: v3 (並走) の待機位置 (投げ台から前 ReadyAhead3・横 ReadyLateral3、投げる方向を向く) から。
            // 試作シーンには持ち手がいないので Ready に入らない — 置く位置でその幾何を作る。
            new Shot { variant = KH_FrisbeeCatch.VariantRunInBeside, place = "chase", name = "chase", speed = 7f, elevation = 10f,
                allowed = new[] { "caught" }, expectKind = "jump", expectMode = "chase",
                note = "v3 の待機位置 (前 4 m・横 2.5 m、投げる方向向き) から 7 m/s。全犬種で追いついて跳ぶ (mode=chase)" },
        };

        static readonly string[] NotCovered =
        {
            // Codex D1: 試作シーンの捕り手は playAreaRadius = 0 (広場無し) なので、DogMayGo は常に真。
            // 着地点が広場の外へ出る候補を落とす規則は、ここでは 1 度も働かない。
            "広場の縁 (playArea)。試作シーンの捕り手は playAreaRadius = 0 なので、着地点・踏み切り・待機の広場判定 (DogMayGo) は常に真になる。縁での着地・回復のはみ出しは本シーンの Verify (幾何) と実機 Play でしか見ていない",
            "Ready (持ち手が構えている間の待機位置)。プロトタイプに持ち手がいないので Ready へ入らず、走り込み系プリセット v1..v3 は待機位置の違いが出ない = ここでは v0 と同じ投げになる",
            // IMP-DOG-013: 猶予は Ready の頭にあり、撫では pet / fetch の配線が要る。試作シーンはどちらも無い。
            "持ち帰り後のよしよし (IMP-DOG-013)。readyGraceSeconds / readyGraceDistance は Ready に入って初めて働くので 1 度も通らず、撫での判定 (pet.IsPetting / fetch.PetActive) も試作シーンには撫で・Fetch の配線が無いので常に偽 = Drop の待ちも止まらない。猶予・撫で・Drop のタイマー停止は本シーンの Verify (値と状態の規則) と実機 Play でしか見ていない",
            "VR の手投げ・狙い (aim line / marker / 広場外の警告) は一切通らない。固定投げ台の +Z 方向の投げだけ",
            "Interact は UdonBehaviour.Interact() を直接呼ぶので、実際のレイキャスト・距離 (proximity)・InteractionText の当たり判定は通っていない",
            // BUG-DOG-003: --mode=netorder は「通知が先、held が後」という**順序**を 1 クライアントで作るだけ。
            // 実際の到着順・Owner 移譲・同期そのものは、ここでは 1 度も通らない。
            "他プレイヤー、Owner 移譲、同期 (単一 ClientSim クライアント)。--mode=netorder は debugHeldOverride で順序を作って保留の論理を通すが、通すのは 1 クライアント上の論理であって通信ではない。実際の到着順・Owner 移譲・IsHeld の同期は 2 人以上の実機でしか見ていない",
            // Codex U3/M4: netorder の holder は「円盤の Owner = 自分」を騙っているだけ。
            "保留中に相手が取得 → 弱く放す (別人の保持者・Owner) は単一クライアントでは再現できない。netorder の HolderNow() は円盤の Owner (= ローカルプレイヤー) を返すだけで、別人が持つ・別人へ Owner が移る・その人が弱く放すという経路は 1 度も通らない。2 人実機で確認する",
            "体験としての見え方 (間・迫力・不自然さ) は人の Play でしか判定できない",
            "実機ビルド (PCVR / Quest / iOS) と実クライアント",
        };

        // ---------------------------------------------------------------- レポート
        // mode: "" / "default" = 従来の 36 投げ。"netorder" = BUG-DOG-003 の順序 4 ケース x repeats。
        // IMP-DOG-012: breed = 投げる犬のレーン (既定 Lab)。mode "sixdogs" = 6 匹の負荷測定 (下の SixDogsSuite)。
        [Serializable] public class Options { public double globalTimeoutSeconds; public string variants = ""; public string shots = ""; public string mode = ""; public int repeats; public string breed = ""; }

        static bool IsNetOrder(Options o) { return o != null && o.mode == "netorder"; }
        static bool IsSixDogs(Options o) { return o != null && o.mode == "sixdogs"; }
        static int Repeats(Options o) { return o != null && o.repeats > 0 ? o.repeats : 2; }
        static string BreedOf(Options o) { return o != null && !string.IsNullOrEmpty(o.breed) ? o.breed : DefaultBreed; }

        // ---------------------------------------------------------------- sixdogs (IMP-DOG-012)
        // 6 匹が同時に追う区間の負荷を測る mode。各周で (1) Lab 1 匹だけ (参考値) → (2) 6 匹同時、の順に
        // flat を投げ、同じ長さの区間の frameTime・描画コール数と、計画器の候補探索の回数を並べる。
        // frameTime は上限を外して測る (Application.targetFrameRate = -1 / QualitySettings.vSyncCount = 0)。
        // 外した値は SessionState にも置き、どの終わり方でも戻して読み直した結果を frameCapRestored に残す
        // (QualitySettings は Play を抜けても戻らないことがある: vrc-performance)。
        const string SixShot = "flat";
        const int SixVariant = 0;
        const double SixFlightWindow = 4.0;   // 投げてからこの秒数を「追う区間」として frameTime を比べる (flat は 2 s 前後で取る)
        const double SixSettle = 30.0;        // 全員が Idle へ戻るまでの上限。6 匹が 1 人の足元へ最大 30 m 持ち帰るので既定の 16 s では足りない
        const double SixBudgetExtra = 4.0;    // 番犬は SixSettle + これ
        // 犬ごとの行は Console から犬を特定できない ([KH_FrisbeeCatch] の行に犬の名前が無い) ので、
        // 取れたか・一周したかは各捕り手の state を毎フレーム読んで決める。

        [Serializable]
        public class ThrowResult
        {
            public int index;
            public int variant;
            public int variantApplied = -1;
            public string shot;
            public float speed, elevationDeg;
            public string outcome = "none";      // caught / miss / ground / none / timeout / blocked
            public string[] allowed;
            // Codex M1: 「取れた」と「一周終わった」は別の列。取ってから持ち帰って置いて Idle へ戻るまでが
            // 1 投げで、そこまで行かなかった投げは ok にしない。
            public bool caught;                  // outcome == "caught"
            public bool completed;               // 窓の内に Idle へ戻った (= reachedIdle)
            public bool outcomeOk;               // outcome が allowed に入っている
            // Codex M2: 終わり方 (jump / stand / over / ground / "") と、その投げに期待した終わり方。
            public string kind = "";
            public string expectKind = "any";
            public bool expectOk = true;
            public string expectNote = "";
            public bool ok;
            // 動きの質 (IMP-DOG-008 round 4)。取れたかどうかとは別の合否。
            // mode = around / chase / front / stand / overtake / "" (跳ばず取らずに終わった投げ)。IMP-DOG-017 で
            // 旧 hook / run / wait から改名 (stand は旧 wait の立ち取り。旧 ModeStraight の "stand" とは別物)。
            public string mode = "";
            public float yawRateMax = -1f, speedMin = -1f, vTake = -1f, approachPath = -1f;
            public bool motionOk = true;
            public string motionNote = "no takeoff / catch line: motion not judged";
            public double seconds;
            public int endState = -1;
            public bool reachedIdle;
            // round 5: Eta の誤差 (実際 - 見積もり、秒)。正 = 見積もりが楽観的。合否には使わない
            // (足したのは「余裕を広げる」前に分布を見るため)。-99 = この投げでは採れなかった。
            public float etaErr = -99f;
            public string etaAt = "", etaMode = "", etaLine;
            // netorder (BUG-DOG-003)。既定モードでは netCase が "" のまま = この 4 列は意味を持たない。
            // netorder では **合否は netOk だけ**で、上の outcomeOk / completed / motionOk / expectOk は
            // 参考列として残る (release-first だけは従来の 4 条件も netOk に入れる)。
            public string netCase = "";
            public bool netOk;
            public string netNote = "";
            // Codex M1: 判定した区間で実際に通った state。Idle へ戻っただけを「一周した」と数えないための証拠。
            public string netStates = "";
            public bool netRoundTrip;
            public List<string> reqLines = new List<string>();
            public string dogStart, launchLine, planLine, takeoffLine, outcomeLine, note;
            public string place = "";            // IMP-DOG-017: "" = シーンの立ち位置 / beside / chase
            public string expectMode = "";       // IMP-DOG-017: 跳んだ投げに期待する mode ("" = 問わない)
            public List<string> lines = new List<string>();
        }

        [Serializable]
        public class VariantSummary
        {
            public int variant;
            public int caught, miss, ground, none, other;
            public int completed;   // Codex M1: そのプリセットで一周まで終わった投げの数
            public int jump, stand, over;   // round 5: 終わり方の内訳 (追い抜きが全プリセットへ広がったので)
        }

        /// <summary>round 5: Eta の誤差の分布。合否ではなく**較正のための観測**。
        /// 正 = 見積もりが楽観的 (実際のほうが遅い)。median が一貫して正なら
        /// HookEtaMargin を広げるのではなく Eta 自体に係数を入れる、という判断材料。</summary>
        [Serializable]
        public class EtaStats
        {
            public int samples;
            public float min, median, mean, max;
            public int optimistic;   // etaErr > 0 の本数
            public int over100ms;    // etaErr > 0.10 s の本数
            public string byMode = "";
            public string verdict = "NOT_MEASURED";
        }

        /// <summary>IMP-DOG-012 sixdogs: 1 区間の frameTime (ms、上限を外した値) と描画コール数 (UnityEditor.UnityStats)。</summary>
        [Serializable]
        public class FrameStats
        {
            public int frames;            // 採れたフレーム数
            public float seconds;
            public float meanMs, p95Ms, maxMs;
            public float meanBatches, maxBatches, meanDrawCalls, maxDrawCalls, meanSetPass;
        }

        [Serializable]
        public class SixDogResult
        {
            public string breed;
            public string kind = "";      // jump (JumpCarry を見た) / stand-or-over (咥えずに Return) / ground (Pickup) / ""
            public bool caught, completed, ok;
            public string states = "";
            public double seconds;
            public int planScans, planEvaluations, planHookTries;   // この区間での増分
        }

        [Serializable]
        public class SixDogsRound
        {
            public int round;
            public string label;          // "lab-only" (参考値) / "six"
            public int dogs;
            public FrameStats flight = new FrameStats();   // 投げてから SixFlightWindow 秒 (追う区間)。比率はこちらで出す
            public FrameStats cycle = new FrameStats();    // 投げてから全員が Idle へ戻るまで
            public int planScans, planEvaluations, planHookTries;   // 投げた犬の合計 (増分)
            public bool ok;
            public List<SixDogResult> perDog = new List<SixDogResult>();
        }

        [Serializable]
        public class SixDogsReport
        {
            public string frameCap = "NOT_TOUCHED";          // 外す前の値と外した値
            public string frameCapRestored = "NOT_APPLICABLE";
            public string shot = SixShot;
            public int variant = SixVariant;
            public string playerAt = "";
            public List<SixDogsRound> rounds = new List<SixDogsRound>();
            public string ratio = "NOT_MEASURED";            // six / lab-only (flight の平均 frameTime、候補探索の回数)
            public string[] cannotSay =
            {
                "試作シーン (平面・犬 6 匹・投げ台) の値。本シーン (地形・草木・遠景・他の犬の徘徊・リード・メニュー) との差は含まない",
                "Editor の Desktop Play で上限を外した frameTime。VR (解像度・2 眼・90 fps 予算) の予測には使えない。使ってよいのは比率と描画コール数だけ",
                "単一 ClientSim クライアント。犬の Owner が 1 人に集まった場合の負荷であって、Owner が分かれた実機の分担は測っていない",
                "描画コール数は UnityEditor.UnityStats の値。Game 画面の Statistics とは一致しない (vrc-performance: 計測系の食い違い)。同じ計測どうしの比だけを読む",
                "犬ごとの取り方 (jump / stand / over) の内訳と動きの質は、Console の行が犬を名乗らないので判定していない (state だけで取れた・一周したを見る)",
            };
        }

        [Serializable]
        public class Report
        {
            public string runId, status = "RUNNING", failure;
            // IMP-DOG-012: 投げたレーン (--breed)。sixdogs は全レーン。
            public string breed = DefaultBreed;
            public SixDogsReport sixdogs;
            // "default" = 36 投げの通し。"netorder" = BUG-DOG-003 の順序ケース (repeats 回)。
            public string mode = "default";
            public int repeats;
            // netorder が debugHeldOverride をどう戻したか。既定モードでは一度も書かない。
            public string overrideRestored = "NOT_APPLICABLE: default mode never writes debugHeldOverride";
            public string unity, sdk, scene, previousScene, sceneHashBefore, sceneHashAfter;
            public string started, ended, revision, dirty, sourceManifest;
            public bool sceneDirtyBefore, sceneDirtyAfterPlay, sceneFileUnchanged;
            public double deadline, budgetSeconds;
            public int throwsPlanned, throwsRun, throwsOk;
            public string injection = "毎回、犬をシーンの立ち位置へ戻してから投げる (円盤の recall だけでは犬が戻らないため)。IMP-DOG-017 の位置取りの投げ (beside / chase) だけは、立ち位置の代わりに投げ台からの相対位置 (dogStart に出る) へ置く。それ以外の注入はない";
            public string scope = "実行した投げだけの結果。notCovered は PASS でも NOT_TESTED のまま";
            public string buildTest = "NOT_TESTED", humanExperience = "NOT_TESTED";
            public string[] notCovered = NotCovered;
            public List<ThrowResult> throwResults = new List<ThrowResult>();
            public List<VariantSummary> perVariant = new List<VariantSummary>();
            public EtaStats eta = new EtaStats();
            public List<string> errors = new List<string>();
            public List<string> trace = new List<string>();
        }

        static Report report;
        static IEnumerator routine;
        static int lastFrame = -1;
        static bool ending;
        static bool closed;          // RestoreScene が journal を消した後 (もう書き戻さない)
        static bool pendingRestore;  // Play を抜けたので、あとはシーンを戻して記録を閉じるだけ
        static double throwDeadline;
        static double nextStopPoll;
        static readonly List<string> Lines = new List<string>();
        static UdonBehaviour catcher, thrower, recaller, cycler;
        // netorder: 捕り手の debugHeldOverride を書いたか / 今いくつか (全終了経路で 0 へ戻すため)。
        static bool overrideTouched;
        static bool overrideFailLogged;   // 後始末の失敗は 1 件だけ errors へ (再試行のたびに増やさない)
        static int overrideNow;
        static Transform dogRoot;
        static Vector3 dogHome;
        static Quaternion dogHomeRot;
        static string RunDir { get { return Base + "/" + report.runId; } }

        static KH_FrisbeeHarnessEditor()
        {
            EditorApplication.playModeStateChanged += Mode;
            EditorApplication.update += Tick;
            if (!string.IsNullOrEmpty(SessionState.GetString(Key, "")) && File.Exists(Journal))
            {
                report = JsonUtility.FromJson<Report>(File.ReadAllText(Journal));
                // ended が入っている journal は「判定は済んでいて、あとはシーンを戻すだけ」の状態。
                pendingRestore = SessionState.GetBool(Key + ".restore", false) || !string.IsNullOrEmpty(report.ended);
                Configure();
            }
        }

        static void Configure()
        {
            Application.logMessageReceived -= Log;
            Application.logMessageReceived += Log;
            ClientSimRuntimeLoader.BeginUnityTesting(new ClientSimSettings
            {
                enableClientSim = true,
                hideMenuOnLaunch = true,
                displayLogs = false,
                customLocalPlayerName = "KH Frisbee Harness",
                localPlayerIsMaster = true,
                initializationDelay = 0f,
                setTargetFrameRate = true,
                targetFrameRate = 60,
                spawnPlayer = true,
            });
        }

        // ---------------------------------------------------------------- 入口 / 出口
        [MenuItem(Menu + "7 Harness (ClientSim)")]
        public static void StartRun()
        {
            try { Begin(); }
            catch (Exception e)
            {
                if (report != null && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    SessionState.EraseString(Key);
                    SessionState.EraseString(Key + ".options");
                    if (File.Exists(Journal)) File.Delete(Journal);
                    report = null;
                }
                Blocked(e.Message);
                Debug.LogError("[KH Frisbee Harness] BLOCKED " + e.Message);
            }
        }

        [MenuItem(Menu + "8 Harness Stop")]
        public static void StopRun()
        {
            if (report == null)
            {
                // クラッシュ等で journal だけが残っている場合の後始末 (Edit モードでのみ)。
                if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Journal))
                {
                    var stale = JsonUtility.FromJson<Report>(File.ReadAllText(Journal));
                    stale.status = "BLOCKED"; stale.failure = "Interrupted run closed by an explicit stop";
                    stale.ended = DateTime.Now.ToString("o");
                    string json = JsonUtility.ToJson(stale, true);
                    Directory.CreateDirectory(Base + "/" + stale.runId);
                    File.WriteAllText(Base + "/" + stale.runId + "/report.json", json);
                    File.WriteAllText(Base + "/latest.json", json);
                    File.Delete(Journal);
                    SessionState.EraseString(Key);
                    Debug.Log("[KH Frisbee Harness] closed a stale journal as BLOCKED");
                    return;
                }
                Debug.Log("[KH Frisbee Harness] no run in progress");
                return;
            }
            report.status = "BLOCKED";
            report.failure = "Explicit stop";
            StopInternal();
        }

        static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Harness needs an idle Edit mode (playing / compiling / building).");
            if (EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Compile errors are present; fix them first.");
            if (File.Exists(Journal))
                throw new InvalidOperationException("A previous run did not finish. Inspect " + Journal + " and delete it once the editor is back in Edit mode.");
            string proto = KH_FrisbeePrototypeEditor.ScenePath;
            if (!File.Exists(proto))
                throw new InvalidOperationException("Prototype scene missing: " + proto + " (run Frisbee/4 Build Scene).");

            // 開いているシーンは保存も破棄もしない。未保存なら何もせず BLOCKED。
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isDirty)
                    throw new InvalidOperationException("Open scene has unsaved changes: " + (string.IsNullOrEmpty(s.path) ? s.name : s.path) + ". Save or discard it yourself first.");
            }
            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path))
                throw new InvalidOperationException("The active scene has never been saved; the harness could not put it back.");

            Directory.CreateDirectory(Base);
            if (File.Exists(StopFlag)) File.Delete(StopFlag);
            var options = ReadOptions();
            // IMP-DOG-012: the lane must be one the prototype builder makes (the scene itself is checked in Play).
            if (Array.IndexOf(KH_FrisbeePrototypeEditor.LaneBreeds, BreedOf(options)) < 0)
                throw new InvalidOperationException("Unknown --breed '" + BreedOf(options) + "' (lanes: " + string.Join(",", KH_FrisbeePrototypeEditor.LaneBreeds) + ").");
            int lanes = KH_FrisbeePrototypeEditor.LaneBreeds.Length;
            int planned = IsNetOrder(options)
                ? NetCases.Length * Repeats(options)
                : IsSixDogs(options) ? Repeats(options) * (1 + lanes)   // 各周: Lab 1 匹 + 6 匹同時
                : Variants(options).Length * Plan(options).Count(s => s.variant < 0) + Plan(options).Count(s => s.variant >= 0);

            report = new Report
            {
                runId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                mode = IsNetOrder(options) ? "netorder" : IsSixDogs(options) ? "sixdogs" : "default",
                repeats = IsNetOrder(options) || IsSixDogs(options) ? Repeats(options) : 0,
                breed = IsSixDogs(options) ? string.Join(",", KH_FrisbeePrototypeEditor.LaneBreeds) : BreedOf(options),
                sixdogs = IsSixDogs(options) ? new SixDogsReport() : null,
                unity = Application.unityVersion,
                sdk = SdkVersion(),
                scene = proto,
                previousScene = active.path,
                sceneHashBefore = HashFile(proto),
                started = DateTime.Now.ToString("o"),
                revision = Git("rev-parse HEAD"),
                dirty = Git("status --porcelain"),
                sourceManifest = HashDirectory("Assets/KominkaHillside/Scripts"),
                sceneDirtyBefore = false,
                throwsPlanned = planned,
                budgetSeconds = options.globalTimeoutSeconds > 0
                    ? options.globalTimeoutSeconds
                    : Math.Max(MinBudget, planned * BudgetPerThrow + 30.0),
            };
            report.deadline = EditorApplication.timeSinceStartup + report.budgetSeconds;
            report.trace.Add(IsNetOrder(options)
                ? "mode=netorder cases=" + string.Join(",", NetCases.Select(n => n.name).ToArray()) +
                  " repeats=" + Repeats(options) + " variant=" + NetVariant + " shot=" + NetShot +
                  " PendingHoldSeconds=" + NetHold.ToString("F2", CultureInfo.InvariantCulture) +
                  " budget=" + report.budgetSeconds + "s" + " breed=" + BreedOf(options)
                : IsSixDogs(options)
                ? "mode=sixdogs lanes=" + string.Join(",", KH_FrisbeePrototypeEditor.LaneBreeds) + " repeats=" + Repeats(options) +
                  " shot=" + SixShot + " variant=" + SixVariant + " flightWindow=" + SixFlightWindow + "s settle=" + SixSettle + "s budget=" + report.budgetSeconds + "s"
                : "breed=" + BreedOf(options) + " variants=" + string.Join(",", Variants(options).Select(v => v.ToString()).ToArray()) +
                  " shots=" + string.Join(",", Plan(options).Select(s => s.name).ToArray()) +
                  " budget=" + report.budgetSeconds + "s");
            SessionState.SetString(Key, report.runId);
            SessionState.SetString(Key + ".options", JsonUtility.ToJson(options));
            Directory.CreateDirectory(RunDir);
            Save();

            try
            {
                if (active.path != proto || SceneManager.sceneCount != 1)
                    EditorSceneManager.OpenScene(proto, OpenSceneMode.Single);
                Lines.Clear();
                ending = false;
                Configure();
                EditorApplication.EnterPlaymode();
            }
            catch
            {
                // Play へ入る前に転んだときは、開いたままにせず記録を閉じてから投げ直す。
                SessionState.EraseString(Key);
                SessionState.EraseString(Key + ".options");
                if (File.Exists(Journal)) File.Delete(Journal);
                report = null;
                throw;
            }
        }

        static void Mode(PlayModeStateChange state)
        {
            if (report == null || string.IsNullOrEmpty(SessionState.GetString(Key, ""))) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { routine = Suite(); lastFrame = -1; }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                // 外から Play を止められた経路は StopInternal を通らないので、ここでも override を戻す
                // (まだ isPlaying なので書き戻せる最後の機会)。
                RestoreOverride();
                RestoreFrameCap();   // IMP-DOG-012 sixdogs: still playing, so both values can be written back
                if (report.status == "RUNNING")
                {
                    report.status = "BLOCKED";
                    // 自分で止めたなら理由は入っている。空なら外から止まった
                    // (手動 Stop / スクリプト再コンパイル / Unity 側の都合)。
                    Why("Play ended before the suite finished (external Stop, a script recompile, or Unity itself). compiling=" +
                        EditorApplication.isCompiling + " throws=" + report.throwResults.Count + "/" + report.throwsPlanned);
                }
                Save();
            }
            if (state == PlayModeStateChange.EnteredEditMode) Finish();
        }

        static void Tick()
        {
            if (pendingRestore)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                pendingRestore = false;
                SessionState.SetBool(Key + ".restore", false);
                RestoreScene();
                return;
            }
            if (report == null || ending || string.IsNullOrEmpty(SessionState.GetString(Key, ""))) return;

            if (EditorApplication.timeSinceStartup > nextStopPoll)
            {
                nextStopPoll = EditorApplication.timeSinceStartup + 0.5;
                if (File.Exists(StopFlag))
                {
                    try { File.Delete(StopFlag); } catch { }
                    report.status = "BLOCKED"; report.failure = "Stop requested by the runner";
                    StopInternal(); return;
                }
            }
            if (EditorApplication.timeSinceStartup > report.deadline ||
                (throwDeadline > 0 && EditorApplication.timeSinceStartup > throwDeadline + 5) ||
                (EditorApplication.isPlaying && EditorApplication.isPaused))
            {
                report.status = "BLOCKED";
                report.failure = EditorApplication.isPaused ? "Editor paused; no automatic resume"
                    : EditorApplication.timeSinceStartup > report.deadline ? "Global watchdog (" + report.budgetSeconds + "s)"
                    : "One throw did not finish inside its budget";
                StopInternal(); return;
            }
            // Editor の MonoBehaviour を置かずに進める: ゲームのフレームが 1 つ進むごとに 1 歩。
            if (EditorApplication.isPlaying && routine != null && Time.frameCount != lastFrame)
            {
                lastFrame = Time.frameCount;
                Step();
            }
        }

        static void Step()
        {
            try
            {
                if (!routine.MoveNext())
                {
                    Summarise();
                    // netorder は前提 (debugHeldOverride の有無) を満たさないとき、自分で BLOCKED を置いて
                    // 抜ける。その判定は上書きしない。既定モードはここへ来る時点で必ず RUNNING なので、
                    // この条件は既定の合否を変えない。
                    if (report.status == "RUNNING")
                        report.status = report.errors.Count == 0
                            && report.throwResults.Count == report.throwsPlanned
                            && report.throwsOk == report.throwResults.Count
                            && report.throwResults.Count > 0 ? "PASS" : "FAIL";
                    StopInternal();
                }
            }
            catch (Exception e)
            {
                Summarise();
                report.status = e is TimeoutException ? "BLOCKED" : "FAIL";
                report.failure = e.ToString();
                StopInternal();
            }
        }

        static void StopInternal()
        {
            routine = null;
            // netorder の後始末はここが最後の関所。停止フラグ・番犬・例外・正常終了のどれもここを通る。
            // **Udon を止める前に**戻す (下で UdonBehaviour を無効化すると書き込めなくなる)。
            RestoreOverride();
            RestoreFrameCap();
            if (!ending && EditorApplication.isPlaying)
            {
                // Udon を先に止めてから Play を抜ける (終了順で SDK 側が先に消えるのを避ける)。
                foreach (var u in Resources.FindObjectsOfTypeAll<UdonBehaviour>())
                    if (u != null && u.gameObject.scene.IsValid() && u.enabled) u.enabled = false;
            }
            ending = true;
            Save();
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
            else Finish();
        }

        static void Finish()
        {
            try
            {
                // 最後の関所。ここまで来て PENDING のままなら「戻せなかった」と記録に残す
                // (すでに戻してあれば何もしない)。
                RestoreOverride();
                RestoreFrameCap();   // after a domain reload only the SessionState copy is left
                ClientSimRuntimeLoader.EndUnityTesting();
                Application.logMessageReceived -= Log;
                Summarise();
                if (report.status == "RUNNING") { report.status = "BLOCKED"; Why("Run ended without a verdict"); }
                if (report.errors.Count > 0 && report.status == "PASS") report.status = "FAIL";
                // Codex M2: 後始末を確かめられていない実行は PASS にしない。
                // 既定モードは "NOT_APPLICABLE..." のままなのでここを通らない。
                if (report.status == "PASS" && report.overrideRestored != null
                    && !report.overrideRestored.StartsWith("OK:") && !report.overrideRestored.StartsWith("NOT_APPLICABLE"))
                {
                    report.status = "FAIL";
                    Why("debugHeldOverride was not confirmed back at 0: " + report.overrideRestored);
                }
                report.ended = DateTime.Now.ToString("o");
                Save();
            }
            catch (Exception e)
            {
                report.status = "BLOCKED";
                report.failure += "\nFinish: " + e;
                Save();
            }
            // シーンの復帰は playModeStateChanged の中では行わない。EditorApplication.update (Tick) へ回す。
            // delayCall は使わない: Play 退出まわりのドメイン再読み込みで登録が消え、シーンが戻らないまま
            // journal が残る状態を実測した (2026-09-21)。update は [InitializeOnLoad] が毎回つなぎ直し、
            // 予約自体も SessionState に置くので、再読み込みをまたいでも必ず片付く。
            pendingRestore = true;
            SessionState.SetBool(Key + ".restore", true);
        }

        static void RestoreScene()
        {
            if (report == null) return;
            try
            {
                var active = SceneManager.GetActiveScene();
                report.sceneDirtyAfterPlay = active.isDirty;
                // Play へ入るだけで SDK が NetworkID を復旧し、シーンが dirty になる。保存はしない。
                // EditorSceneManager.OpenScene は確認を出さずに未保存の変更を捨てて開き直すので、
                // 「元のシーンへ戻す」と「Play が付けた dirty を捨てる」を同じ 1 回で済ませる。
                // 実行前は全シーンが未変更であることを確認済み (Begin)。ここで捨てるのは Play 由来の差分だけ。
                if (!string.IsNullOrEmpty(report.previousScene) && File.Exists(report.previousScene) &&
                    (active.path != report.previousScene || active.isDirty))
                    EditorSceneManager.OpenScene(report.previousScene, OpenSceneMode.Single);
                report.sceneHashAfter = File.Exists(report.scene) ? HashFile(report.scene) : "MISSING";
                report.sceneFileUnchanged = report.sceneHashAfter == report.sceneHashBefore;
                if (!report.sceneFileUnchanged)
                {
                    report.errors.Add("Prototype scene file changed during the run: " + report.scene);
                    if (report.status == "PASS") report.status = "FAIL";
                }
                report.trace.Add("scene restored to " + SceneManager.GetActiveScene().path +
                    " (dirty after play=" + report.sceneDirtyAfterPlay + ", reopened from disk without saving)");
                closed = true;
                Save();
                Debug.Log("[KH Frisbee Harness] " + report.status + " " + RunDir + "/report.json");
                if (File.Exists(Journal)) File.Delete(Journal);
                SessionState.EraseString(Key);
                SessionState.EraseString(Key + ".options");
                SessionState.SetBool(Key + ".restore", false);
                report = null; ending = false; closed = false; routine = null; throwDeadline = 0;
                overrideTouched = false; overrideFailLogged = false; overrideNow = 0;
                catcher = thrower = recaller = cycler = null;
                laneBreed = DefaultBreed;
                Lines.Clear();
            }
            catch (Exception e)
            {
                report.status = "BLOCKED";
                report.failure += "\nScene restore: " + e;
                Save();
                Debug.LogError("[KH Frisbee Harness] scene restore failed; the journal is kept. " + e.Message);
            }
        }

        static void Blocked(string why)
        {
            Directory.CreateDirectory(Base);
            var r = new Report
            {
                runId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-blocked",
                status = "BLOCKED",
                failure = why,
                unity = Application.unityVersion,
                started = DateTime.Now.ToString("o"),
                ended = DateTime.Now.ToString("o"),
            };
            Directory.CreateDirectory(Base + "/" + r.runId);
            string json = JsonUtility.ToJson(r, true);
            File.WriteAllText(Base + "/" + r.runId + "/report.json", json);
            File.WriteAllText(Base + "/latest.json", json);
        }

        // ---------------------------------------------------------------- 本体
        static IEnumerator Suite()
        {
            double until = EditorApplication.timeSinceStartup + 30;
            while (Networking.LocalPlayer == null || LocalPlayer() == null)
            {
                if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("ClientSim local player did not appear within 30 s");
                yield return null;
            }
            report.trace.Add("local player ready: " + Networking.LocalPlayer.displayName);
            var settle = Wait(1.5); while (settle.MoveNext()) yield return null;

            // IMP-DOG-012: the lane (--breed) and the sixdogs mode. The options are read here (not after the
            // lookups below, as before) because they decide which objects to look up.
            var options = JsonUtility.FromJson<Options>(SessionState.GetString(Key + ".options", "{}")) ?? new Options();
            if (IsSixDogs(options))
            {
                var six = SixDogsSuite(options);
                while (six.MoveNext()) yield return null;
                yield break;
            }
            laneBreed = BreedOf(options);
            string missingLane = MissingLaneObjects(laneBreed);
            if (missingLane != null)
            {
                report.status = "BLOCKED";
                report.failure = "Prototype lane '" + laneBreed + "' is not built (" + missingLane + "). Run Tools/KominkaHillside/Frisbee/4 Build Scene after Frisbee/2 has made this breed's clips (IMP-DOG-012).";
                yield break;
            }
            if (laneBreed != DefaultBreed)
            {
                // The dog carries the disc back to whoever threw it, so the thrower stands where the Lab lane's
                // spawn is, shifted with the lane. The Lab lane is left exactly as before (no teleport at all).
                var at = SpawnPos + new Vector3(KH_FrisbeePrototypeEditor.LaneOffsetX(laneBreed), 0f, 0f);
                Networking.LocalPlayer.TeleportTo(at, Quaternion.identity);
                var moved = Wait(0.5); while (moved.MoveNext()) yield return null;
                report.trace.Add("lane " + laneBreed + ": player teleported to " + at.ToString("F1") + " (now " + Networking.LocalPlayer.GetPosition().ToString("F1") + ")");
            }

            catcher = Find(CatcherName);
            thrower = Find(ThrowerName);
            recaller = Find(RecallName);
            cycler = Find(CyclerName);
            if (!Has(catcher, "state") || !Has(catcher, "variant"))
                throw new InvalidOperationException("KH_FrisbeeCatch no longer exposes 'state' / 'variant'; the harness reads those symbols.");

            // 犬の立ち位置。円盤を戻すだけでは犬は戻らないので (咥えて人の足元まで運んで置く)、
            // 2 投げ目以降が別の距離からの投げになってしまう。毎回この位置へ戻してから投げる。
            object value;
            if (catcher.TryGetProgramVariable("dogRoot", out value)) dogRoot = value as Transform;
            if (dogRoot == null) throw new InvalidOperationException("KH_FrisbeeCatch.dogRoot is not wired; the harness cannot put the dog back.");
            dogHome = dogRoot.position; dogHomeRot = dogRoot.rotation;
            report.trace.Add("lane " + laneBreed + " catcher=" + CatcherName + " stand=" + ThrowerName + " dog home = " + dogHome.ToString("F2") + " " + dogHomeRot.eulerAngles.ToString("F0"));

            if (IsNetOrder(options))
            {
                var net = NetOrderSuite(options);
                while (net.MoveNext()) yield return null;
                yield break;
            }
            var shots = Plan(options);
            int index = 0;
            foreach (int v in Variants(options))
                foreach (var shot in shots)
                {
                    if (shot.variant >= 0) continue;   // IMP-DOG-017: 位置取りの投げは下で 1 回だけ
                    var one = RunThrow(index++, v, shot);
                    while (one.MoveNext()) yield return null;
                    Save();
                }
            // IMP-DOG-017: 自分のプリセットを持つ投げ (beside / chase)。--variants に関係なく、--shots で選ばれていれば 1 回。
            foreach (var shot in shots)
            {
                if (shot.variant < 0) continue;
                var one = RunThrow(index++, shot.variant, shot);
                while (one.MoveNext()) yield return null;
                Save();
            }
        }

        // ---------------------------------------------------------------- lanes / sixdogs (IMP-DOG-012)
        /// <summary>このレーンの 4 つ (捕り手・投げ台・recall・切替台) のうち、Play 中のシーンに無いものの名前。全部あれば null。</summary>
        static string MissingLaneObjects(string breed)
        {
            string keep = laneBreed; laneBreed = breed;
            try
            {
                var missing = new List<string>();
                foreach (string n in new[] { CatcherName, ThrowerName, RecallName, CyclerName })
                    if (TryFind(n) == null) missing.Add(n);
                return missing.Count == 0 ? null : string.Join(", ", missing.ToArray());
            }
            finally { laneBreed = keep; }
        }

        static UdonBehaviour TryFind(string goName)
        {
            foreach (var u in Resources.FindObjectsOfTypeAll<UdonBehaviour>())
                if (u != null && u.gameObject.scene.IsValid() && u.gameObject.name == goName) return u;
            return null;
        }

        /// <summary>上限を外す。外す前の値は SessionState にも置く (Play 退出のドメイン再読み込みで static は消える)。</summary>
        static void UncapFrameRate()
        {
            int tfr = Application.targetFrameRate, vs = QualitySettings.vSyncCount;
            SessionState.SetInt(Key + ".tfr", tfr);
            SessionState.SetInt(Key + ".vsync", vs);
            SessionState.SetBool(Key + ".cap", true);
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            if (report != null && report.sixdogs != null)
                report.sixdogs.frameCap = "before targetFrameRate=" + tfr + " vSyncCount=" + vs + " (quality level " + QualitySettings.GetQualityLevel()
                    + ") -> measured with targetFrameRate=" + Application.targetFrameRate + " vSyncCount=" + QualitySettings.vSyncCount;
        }

        /// <summary>sixdogs の後始末。どの終了経路 (完了・BLOCKED・停止・番犬・例外・外からの Stop) も通る。
        /// Play 中なら targetFrameRate と vSyncCount の両方、Edit モードでは QualitySettings の vSyncCount だけ戻す
        /// (targetFrameRate は Play を抜ければ ClientSim の設定ごと消える値、vSyncCount はプロジェクト設定に残りうる値)。
        /// 戻したら読み直し、確かめられなければ errors に足して PASS にしない。</summary>
        static void RestoreFrameCap()
        {
            if (!SessionState.GetBool(Key + ".cap", false)) return;
            int tfr = SessionState.GetInt(Key + ".tfr", -1), vs = SessionState.GetInt(Key + ".vsync", 0);
            string outcome;
            try
            {
                QualitySettings.vSyncCount = vs;
                bool playing = EditorApplication.isPlaying;
                if (playing) Application.targetFrameRate = tfr;
                bool ok = QualitySettings.vSyncCount == vs && (!playing || Application.targetFrameRate == tfr);
                outcome = (ok ? "OK: " : "FAILED: ") + "vSyncCount=" + QualitySettings.vSyncCount + " (want " + vs + ")"
                    + (playing ? " targetFrameRate=" + Application.targetFrameRate + " (want " + tfr + ")" : " (edit mode: targetFrameRate left to Unity)");
                if (ok) SessionState.SetBool(Key + ".cap", false);
            }
            catch (Exception e) { outcome = "FAILED: " + e.Message; }
            if (report == null) return;
            if (report.sixdogs != null) report.sixdogs.frameCapRestored = outcome;
            if (!outcome.StartsWith("OK:") && report.errors.Count < 50)
                report.errors.Add("[KH Frisbee Harness] frame cap was not confirmed restored: " + outcome);
        }

        struct SixLane
        {
            public string breed;
            public UdonBehaviour catcher, thrower, recaller, cycler;
            public Transform dogRoot;
            public Vector3 home; public Quaternion homeRot;
        }

        /// <summary>IMP-DOG-012 --mode=sixdogs。各周で Lab 1 匹 (参考値) → 6 匹同時 の順に flat を投げ、
        /// 同じ区間の frameTime (上限を外した値) と描画コール数、計画器のカウンタ (_planScans /
        /// _planEvaluations / _planHookTries の増分) を並べる。1 匹ごとの合否は state だけで決める
        /// (取れた = JumpCarry を見た、または Pickup を経ずに Return を見た / 一周 = 動いた後に Idle)。</summary>
        static IEnumerator SixDogsSuite(Options options)
        {
            var breeds = KH_FrisbeePrototypeEditor.LaneBreeds;
            var lanes = new SixLane[breeds.Length];
            var missing = new List<string>();
            for (int i = 0; i < breeds.Length; i++)
            {
                string m = MissingLaneObjects(breeds[i]);
                if (m != null) { missing.Add(breeds[i] + ": " + m); continue; }
                laneBreed = breeds[i];
                lanes[i].breed = breeds[i];
                lanes[i].catcher = Find(CatcherName); lanes[i].thrower = Find(ThrowerName);
                lanes[i].recaller = Find(RecallName); lanes[i].cycler = Find(CyclerName);
            }
            laneBreed = DefaultBreed;
            if (missing.Count > 0)
            {
                report.status = "BLOCKED";
                report.failure = "Prototype lanes are not built (" + string.Join(" | ", missing.ToArray()) + "). Run Tools/KominkaHillside/Frisbee/4 Build Scene (IMP-DOG-012).";
                yield break;
            }
            foreach (var l in lanes)
                if (!Has(l.catcher, "_planEvaluations") || !Has(l.catcher, "_planScans") || !Has(l.catcher, "state"))
                {
                    report.status = "BLOCKED";
                    report.failure = l.breed + ": KH_FrisbeeCatch does not expose _planScans / _planEvaluations (the IMP-DOG-012 planner counters). Land the runtime side first.";
                    yield break;
                }
            if (!Shots.Any(s => s.name == SixShot)) { report.status = "BLOCKED"; report.failure = "The shot table has no '" + SixShot + "' shot."; yield break; }
            Shot shot = Shots.First(s => s.name == SixShot);
            for (int i = 0; i < lanes.Length; i++)
            {
                object value;
                if (lanes[i].catcher.TryGetProgramVariable("dogRoot", out value)) lanes[i].dogRoot = value as Transform;
                if (lanes[i].dogRoot == null) throw new InvalidOperationException(lanes[i].breed + ": KH_FrisbeeCatch.dogRoot is not wired.");
                lanes[i].home = lanes[i].dogRoot.position; lanes[i].homeRot = lanes[i].dogRoot.rotation;
            }
            int lab = Array.IndexOf(breeds, DefaultBreed);
            // The dogs carry the disc back to the thrower (the local player). Stand at the middle of the lanes so no
            // lane's walk back is more than half the row; the Lab-only reference uses the same spot.
            float mid = 0f; foreach (var b in breeds) mid += KH_FrisbeePrototypeEditor.LaneOffsetX(b); mid /= breeds.Length;
            var playerAt = SpawnPos + new Vector3(mid, 0f, 0f);
            Networking.LocalPlayer.TeleportTo(playerAt, Quaternion.identity);
            report.sixdogs.playerAt = playerAt.ToString("F1");
            // Used by the shared helpers (Recall / WaitIdle / override restore) - the Lab lane, as in the default mode.
            catcher = lanes[lab].catcher; thrower = lanes[lab].thrower; recaller = lanes[lab].recaller; cycler = lanes[lab].cycler;
            dogRoot = lanes[lab].dogRoot; dogHome = lanes[lab].home; dogHomeRot = lanes[lab].homeRot;

            UncapFrameRate();
            report.trace.Add("sixdogs: " + report.sixdogs.frameCap + " player at " + playerAt.ToString("F1"));
            var warm = Wait(1.0); while (warm.MoveNext()) yield return null;

            int index = 0;
            for (int r = 0; r < Repeats(options); r++)
            {
                foreach (bool six in new[] { false, true })
                {
                    int[] set = six ? Enumerable.Range(0, lanes.Length).ToArray() : new[] { lab };
                    var round = new SixDogsRound { round = r + 1, label = six ? "six" : "lab-only", dogs = set.Length };
                    report.sixdogs.rounds.Add(round);
                    var recs = new ThrowResult[set.Length];

                    // 1. Everyone quiet, preset v0, every dog (thrown or not) back on its spot.
                    for (int k = 0; k < set.Length; k++)
                    {
                        var l = lanes[set[k]];
                        recs[k] = new ThrowResult
                        {
                            index = index++, variant = SixVariant, shot = shot.name, speed = shot.speed, elevationDeg = shot.elevation,
                            allowed = new[] { "caught" }, expectKind = "any",
                            note = "sixdogs round " + (r + 1) + " " + round.label + " lane " + l.breed,
                            motionNote = "not judged in sixdogs: the [KH_FrisbeeCatch] lines do not name the dog",
                        };
                        report.throwResults.Add(recs[k]);
                        report.throwsRun++;
                        var pre = EnsureVariantOn(l.catcher, l.cycler, SixVariant, recs[k]); while (pre.MoveNext()) yield return null;
                    }
                    for (int i = 0; i < lanes.Length; i++) lanes[i].dogRoot.SetPositionAndRotation(lanes[i].home, lanes[i].homeRot);
                    var calm = Wait(0.5); while (calm.MoveNext()) yield return null;

                    // 2. Counters before, then every stand in the set fires in the same frame.
                    var before = new int[set.Length, 3];
                    for (int k = 0; k < set.Length; k++)
                    {
                        var c = lanes[set[k]].catcher;
                        before[k, 0] = ReadInt(c, "_planScans"); before[k, 1] = ReadInt(c, "_planEvaluations"); before[k, 2] = ReadIntOr(c, "_planHookTries", 0);
                        lanes[set[k]].thrower.SetProgramVariable("speed", shot.speed);
                        lanes[set[k]].thrower.SetProgramVariable("elevationDeg", shot.elevation);
                        lanes[set[k]].thrower.SetProgramVariable("sideDeg", shot.side);
                    }
                    int mark = Lines.Count;
                    double start = EditorApplication.timeSinceStartup;
                    throwDeadline = start + SixSettle + SixBudgetExtra;
                    for (int k = 0; k < set.Length; k++) lanes[set[k]].thrower.Interact();

                    // 3. Observe: per-frame frame time and draw counts, per-dog states, until every thrown dog is back
                    //    in Idle after having moved (or the settle window runs out).
                    var flightMs = new List<float>(); var cycleMs = new List<float>();
                    var flightB = new List<float>(); var cycleB = new List<float>();
                    var flightD = new List<float>(); var cycleD = new List<float>(); var flightS = new List<float>(); var cycleS = new List<float>();
                    var seen = new bool[set.Length, NetStateCount];
                    var engaged = new bool[set.Length]; var done = new bool[set.Length]; var doneAt = new double[set.Length];
                    int sampledFrame = -1;
                    while (EditorApplication.timeSinceStartup < start + SixSettle)
                    {
                        double now = EditorApplication.timeSinceStartup;
                        if (Time.frameCount != sampledFrame)
                        {
                            sampledFrame = Time.frameCount;
                            float ms = Time.unscaledDeltaTime * 1000f;
                            float b = UnityStats.batches, d = UnityStats.drawCalls, sp = UnityStats.setPassCalls;
                            cycleMs.Add(ms); cycleB.Add(b); cycleD.Add(d); cycleS.Add(sp);
                            if (now - start <= SixFlightWindow) { flightMs.Add(ms); flightB.Add(b); flightD.Add(d); flightS.Add(sp); }
                        }
                        bool all = true;
                        for (int k = 0; k < set.Length; k++)
                        {
                            if (done[k]) continue;
                            int st = ReadInt(lanes[set[k]].catcher, "state");
                            if (st >= 0 && st < NetStateCount) seen[k, st] = true;
                            if (st != StateIdle && st != StateReady) engaged[k] = true;
                            if (engaged[k] && st == StateIdle) { done[k] = true; doneAt[k] = now - start; }
                            else all = false;
                        }
                        // Keep sampling for the whole flight window even when everyone is done early, so the
                        // lab-only and the six window always cover the same seconds.
                        if (all && now - start > SixFlightWindow) break;
                        yield return null;
                    }
                    double took = EditorApplication.timeSinceStartup - start;
                    round.flight = Stats(flightMs, flightB, flightD, flightS, Math.Min(took, SixFlightWindow));
                    round.cycle = Stats(cycleMs, cycleB, cycleD, cycleS, took);

                    // 4. Per dog: caught / completed from the states, and the planner's work in this window.
                    round.ok = true;
                    for (int k = 0; k < set.Length; k++)
                    {
                        var l = lanes[set[k]];
                        var dog = new SixDogResult { breed = l.breed };
                        dog.planScans = ReadInt(l.catcher, "_planScans") - before[k, 0];
                        dog.planEvaluations = ReadInt(l.catcher, "_planEvaluations") - before[k, 1];
                        dog.planHookTries = ReadIntOr(l.catcher, "_planHookTries", 0) - before[k, 2];
                        bool jumpCarry = seen[k, StateJumpCarry], pickup = seen[k, StatePickup], ret = seen[k, StateReturn] || seen[k, StateDrop];
                        dog.kind = jumpCarry ? "jump" : pickup ? "ground" : ret ? "stand-or-over" : "";
                        dog.caught = jumpCarry || (ret && !pickup);
                        dog.completed = done[k] && ret;
                        dog.ok = dog.caught && dog.completed;
                        dog.seconds = Math.Round(done[k] ? doneAt[k] : took, 2);
                        var got = new List<string>(); for (int s = 0; s < NetStateCount; s++) if (seen[k, s]) got.Add(s.ToString());
                        dog.states = got.Count == 0 ? "(none)" : string.Join(",", got.ToArray());
                        round.perDog.Add(dog);
                        round.planScans += dog.planScans; round.planEvaluations += dog.planEvaluations; round.planHookTries += dog.planHookTries;
                        round.ok &= dog.ok;

                        var rec = recs[k];
                        rec.seconds = dog.seconds;
                        rec.endState = ReadInt(l.catcher, "state");
                        rec.reachedIdle = rec.endState == StateIdle;
                        rec.outcome = dog.caught ? "caught" : pickup ? "ground" : engaged[k] ? (done[k] ? "none" : "timeout") : "none";
                        rec.kind = dog.kind == "stand-or-over" ? "" : dog.kind;
                        rec.caught = dog.caught; rec.completed = dog.completed;
                        rec.outcomeOk = dog.caught; rec.expectOk = true; rec.expectNote = "expectKind=any (sixdogs measures load; how each dog caught is not attributed)";
                        rec.outcomeLine = "states=" + dog.states + " kind=" + (dog.kind.Length == 0 ? "(none)" : dog.kind) + " planEvaluations=" + dog.planEvaluations;
                        rec.dogStart = l.home.ToString("F2");
                        rec.ok = dog.ok;
                    }
                    // The lines of a six-dog window cannot be split per dog; they are kept once, on the round's first record.
                    for (int i = mark; i < Lines.Count; i++) recs[0].lines.Add(Lines[i]);
                    report.throwsOk = report.throwResults.Count(t => t.ok);
                    report.trace.Add("sixdogs r" + (r + 1) + " " + round.label + ": flight mean " + round.flight.meanMs.ToString("F2", CultureInfo.InvariantCulture)
                        + " ms max " + round.flight.maxMs.ToString("F2", CultureInfo.InvariantCulture) + " ms batches " + round.flight.meanBatches.ToString("F0", CultureInfo.InvariantCulture)
                        + " | planEvaluations " + round.planEvaluations + " scans " + round.planScans + " hookTries " + round.planHookTries + " | ok=" + round.ok);

                    // 5. Tidy: every thrown disc back on its stand.
                    throwDeadline = 0;
                    for (int k = 0; k < set.Length; k++) lanes[set[k]].recaller.Interact();
                    var back = Wait(0.5); while (back.MoveNext()) yield return null;
                    for (int k = 0; k < set.Length; k++) { var idle = WaitIdleOn(lanes[set[k]].catcher, 4); while (idle.MoveNext()) yield return null; }
                    Save();
                }
            }
            SummariseSixDogs();
            RestoreFrameCap();
        }

        static FrameStats Stats(List<float> ms, List<float> batches, List<float> draws, List<float> setPass, double seconds)
        {
            var f = new FrameStats { frames = ms.Count, seconds = (float)Math.Round(seconds, 2) };
            if (ms.Count == 0) return f;
            var sorted = ms.OrderBy(v => v).ToList();
            f.meanMs = ms.Average(); f.maxMs = sorted[sorted.Count - 1];
            f.p95Ms = sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * 0.95) - 1)];
            f.meanBatches = batches.Average(); f.maxBatches = batches.Max();
            f.meanDrawCalls = draws.Average(); f.maxDrawCalls = draws.Max();
            f.meanSetPass = setPass.Average();
            return f;
        }

        /// <summary>6 匹 / 1 匹 の比。flight 区間の平均 frameTime と、候補探索の回数 (6 匹は合計、1 匹は Lab)。</summary>
        static void SummariseSixDogs()
        {
            if (report == null || report.sixdogs == null || report.mode != "sixdogs") return;
            var solo = report.sixdogs.rounds.Where(x => x.label == "lab-only").ToList();
            var six = report.sixdogs.rounds.Where(x => x.label == "six").ToList();
            if (solo.Count == 0 || six.Count == 0) { report.sixdogs.ratio = "NOT_MEASURED: need both a lab-only and a six round"; return; }
            float soloMs = solo.Average(x => x.flight.meanMs), sixMs = six.Average(x => x.flight.meanMs);
            float soloMax = solo.Max(x => x.flight.maxMs), sixMax = six.Max(x => x.flight.maxMs);
            double soloEval = solo.Average(x => (double)x.planEvaluations), sixEval = six.Average(x => (double)x.planEvaluations);
            float soloB = solo.Average(x => x.flight.meanBatches), sixB = six.Average(x => x.flight.meanBatches);
            var ic = CultureInfo.InvariantCulture;
            report.sixdogs.ratio = "flight mean frameTime six/lab = " + sixMs.ToString("F2", ic) + "/" + soloMs.ToString("F2", ic) + " ms (x"
                + (soloMs > 0f ? (sixMs / soloMs).ToString("F2", ic) : "-") + "), max " + sixMax.ToString("F2", ic) + "/" + soloMax.ToString("F2", ic)
                + " ms; planEvaluations six(total)/lab = " + sixEval.ToString("F0", ic) + "/" + soloEval.ToString("F0", ic) + " (x"
                + (soloEval > 0 ? (sixEval / soloEval).ToString("F2", ic) : "-") + "); flight batches six/lab = "
                + sixB.ToString("F0", ic) + "/" + soloB.ToString("F0", ic) + " (UnityStats)";
        }

        // ---------------------------------------------------------------- netorder (BUG-DOG-003)
        /// <summary>通知が先で held が後、という順序だけを 1 クライアントで作る mode。
        /// 捕り手の debugHeldOverride を投げの前後で切り替え、[KH_FrisbeeCatch] req ... の行の
        /// **順序**で合否を出す。既定モードの 4 条件は参考列として残すだけ (release-first を除く)。</summary>
        static IEnumerator NetOrderSuite(Options options)
        {
            // 前提: 切り替えは捕り手の debugHeldOverride だけで作る。無ければ何も測れないので BLOCKED
            // (FAIL ではない - 測れていないのであって、順序が壊れていると分かったわけではない)。
            if (!Has(catcher, "debugHeldOverride"))
            {
                report.status = "BLOCKED";
                report.failure = "KH_FrisbeeCatch does not expose 'debugHeldOverride'. --mode=netorder drives that symbol "
                    + "to fake the held / released order (BUG-DOG-003); land the runtime side first, then re-run.";
                report.overrideRestored = "NOT_APPLICABLE: the symbol is missing, so nothing was ever written";
                report.trace.Add("netorder BLOCKED: the live KH_FrisbeeCatch has no debugHeldOverride");
                yield break;
            }
            if (!Shots.Any(s => s.name == NetShot))
            {
                report.status = "BLOCKED";
                report.failure = "The shot table has no '" + NetShot + "' shot; every netorder case throws it.";
                yield break;
            }
            overrideNow = ReadInt(catcher, "debugHeldOverride");
            // 0 以外で始まっていたら (シーン側の置き忘れ) それも「戻すべきもの」として扱う。
            if (overrideNow != 0) overrideTouched = true;
            report.overrideRestored = "PENDING: netorder is running";
            report.trace.Add("netorder: debugHeldOverride starts at " + overrideNow
                + ", PendingHoldSeconds=" + NetHold.ToString("F2", CultureInfo.InvariantCulture)
                + " -> release-early=" + NetReleaseEarly.ToString("F2", CultureInfo.InvariantCulture)
                + "s release-late=" + NetReleaseLate.ToString("F2", CultureInfo.InvariantCulture)
                + "s regrab-hold=" + NetRegrabHold.ToString("F2", CultureInfo.InvariantCulture)
                + "s (all derived from the constant, never typed twice)");

            int index = 0;
            for (int r = 0; r < Repeats(options); r++)
                foreach (var c in NetCases)
                {
                    var one = RunNetThrow(index++, c, r);
                    while (one.MoveNext()) yield return null;
                    Save();
                }
            RestoreOverride();
        }

        static IEnumerator RunThrow(int index, int variant, Shot shot)
        {
            var rec = new ThrowResult
            {
                index = index, variant = variant, shot = shot.name,
                speed = shot.speed, elevationDeg = shot.elevation,
                allowed = shot.allowed, expectKind = shot.expectKind, note = shot.note,
                place = shot.place ?? "", expectMode = shot.expectMode ?? "",
            };
            // IMP-DOG-017: Lab だけ期待が違う投げ (beside)。
            bool labLane = laneBreed == DefaultBreed;
            if (labLane && shot.labAllowed != null) rec.allowed = shot.labAllowed;
            if (labLane && shot.labExpectKind != null) rec.expectKind = shot.labExpectKind;
            if (labLane && shot.labExpectMode != null) rec.expectMode = shot.labExpectMode;
            report.throwResults.Add(rec);
            report.throwsRun++;

            // 1. 落ち着くまで待ち、プリセットを合わせる (犬が動いている間は要求が保留されるため)
            var pre = EnsureVariant(variant, rec); while (pre.MoveNext()) yield return null;
            if (rec.variantApplied != variant)
            {
                rec.outcome = "blocked"; rec.ok = false;
                rec.note = "preset " + variant + " could not be selected (stuck on " + rec.variantApplied + ")";
                yield break;
            }

            // 2. 犬を元の立ち位置へ戻す (注入)。これをしないと 2 投げ目以降は犬が人の足元に
            //    いる状態からの投げになり、同じ投げの比較にならない。
            //    IMP-DOG-017: 位置取りの投げ (beside / chase) は、立ち位置の代わりに投げ台からの相対位置へ置く。
            var home = string.IsNullOrEmpty(shot.place) ? PlaceDogHome(rec) : PlaceDogFor(shot.place, rec);
            while (home.MoveNext()) yield return null;
            var settle = Wait(0.3); while (settle.MoveNext()) yield return null;

            // 3. 投げの値を固定投げ台へ書き、Interact で投げる。ここから 1 投げ分の時計を回す
            //    (プリセット合わせは投げそのものではないので、全体の番犬に任せる)。
            double start = EditorApplication.timeSinceStartup;
            throwDeadline = start + ThrowBudget;
            thrower.SetProgramVariable("speed", shot.speed);
            thrower.SetProgramVariable("elevationDeg", shot.elevation);
            thrower.SetProgramVariable("sideDeg", shot.side);
            int mark = Lines.Count;
            thrower.Interact();

            // 4. 犬が動き出すのを少し待ってから、Idle へ戻るか 10 秒経つまで
            bool engaged = false;
            double limit = Math.Min(start + ThrowSettle, throwDeadline);
            while (EditorApplication.timeSinceStartup < limit)
            {
                int st = ReadInt(catcher, "state");
                if (st != StateIdle && st != StateReady) engaged = true;
                if (engaged && st == StateIdle) break;
                yield return null;
            }
            rec.seconds = Math.Round(EditorApplication.timeSinceStartup - start, 2);
            rec.endState = ReadInt(catcher, "state");
            rec.reachedIdle = rec.endState == StateIdle;
            for (int i = mark; i < Lines.Count; i++) rec.lines.Add(Lines[i]);
            Classify(rec, engaged);
            JudgeMotion(rec);
            JudgeKind(rec);
            // 1 投げの合否は 4 つ全部:
            //   outcomeOk  想定内の結果だったか (round 1)
            //   completed  その一周が終わったか = 窓の内に Idle へ戻ったか (Codex M1)
            //   motionOk   動きが機械でないか (round 4)
            //   expectOk   期待した終わり方だったか (Codex M2)
            rec.caught = rec.outcome == "caught";
            rec.completed = rec.reachedIdle;
            rec.outcomeOk = rec.allowed.Contains(rec.outcome);
            rec.ok = rec.outcomeOk && rec.completed && rec.motionOk && rec.expectOk;
            if (rec.ok) report.throwsOk++;

            // 5. 次の投げのために円盤を台へ戻す (犬の現在の追いかけも終わる)。
            //    片付けは投げの判定が済んだあとなので、1 投げの時計は先に止める。
            throwDeadline = 0;
            var tidy = Recall(); while (tidy.MoveNext()) yield return null;
        }

        /// <summary>手順 1 (既定モードと netorder で共有)。落ち着くまで待ってからプリセットを合わせる。
        /// 犬が動いている間は切り替え要求が保留されるので、毎回 Idle を待ってから回す。</summary>
        static IEnumerator EnsureVariant(int variant, ThrowResult rec)
        {
            var on = EnsureVariantOn(catcher, cycler, variant, rec); while (on.MoveNext()) yield return null;
        }

        /// <summary>EnsureVariant の本体。sixdogs (IMP-DOG-012) はレーンごとの捕り手と切替台で呼ぶ。</summary>
        static IEnumerator EnsureVariantOn(UdonBehaviour c, UdonBehaviour cyc, int variant, ThrowResult rec)
        {
            var idle = WaitIdleOn(c, 4); while (idle.MoveNext()) yield return null;
            int guard = 0;
            while (ReadInt(c, "variant") != variant && guard++ < VariantCount + 2)
            {
                cyc.Interact();
                var w = Wait(0.35); while (w.MoveNext()) yield return null;
                idle = WaitIdleOn(c, 2); while (idle.MoveNext()) yield return null;
            }
            rec.variantApplied = ReadInt(c, "variant");
        }

        /// <summary>手順 2 (共有)。唯一の注入 = 犬をシーンの立ち位置へ戻す。</summary>
        static IEnumerator PlaceDogHome(ThrowResult rec)
        {
            if (dogRoot == null) yield break;
            dogRoot.SetPositionAndRotation(dogHome, dogHomeRot);
            yield return null;
            rec.dogStart = dogRoot.position.ToString("F2");
        }

        /// <summary>IMP-DOG-017 の位置取りの投げ。投げ台 (固定台は常に +Z へ投げる) を投げ手とみなし、
        /// 犬をその相対位置へ置く。横はシーンの立ち位置がある側 (+x) — 実際のワールドでも Ready は
        /// 「犬のいる側」へ取るので、同じ側に置く。高さは立ち位置と同じ (平面)。
        ///   beside: 投げ台の横 BesideGap (0.6 m)、投げ台の方を向く (よしよしの後に投げる流れ)
        ///   chase:  v3 の Ready = 前 ReadyAhead3 (4 m)・横 ReadyLateral3 (2.5 m)、投げる方向 (+Z) を向く</summary>
        static IEnumerator PlaceDogFor(string place, ThrowResult rec)
        {
            if (dogRoot == null || thrower == null) yield break;
            Vector3 stand = thrower.transform.position; stand.y = dogHome.y;
            Vector3 fwd = Vector3.forward;
            float sideSign = dogHome.x - stand.x >= 0f ? 1f : -1f;
            Vector3 side = Vector3.right * sideSign;
            Vector3 at; Quaternion rot;
            if (place == "beside")
            {
                at = stand + side * BesideGap;
                rot = Quaternion.LookRotation(-side, Vector3.up);
            }
            else if (place == "chase")
            {
                at = stand + fwd * KH_FrisbeeCatch.ReadyAhead3 + side * KH_FrisbeeCatch.ReadyLateral3;
                rot = Quaternion.LookRotation(fwd, Vector3.up);
            }
            else throw new InvalidOperationException("Unknown dog placement '" + place + "' (beside / chase)");
            dogRoot.SetPositionAndRotation(at, rot);
            yield return null;
            rec.dogStart = dogRoot.position.ToString("F2") + " yaw=" + dogRoot.eulerAngles.y.ToString("F0") + " (" + place + ")";
        }

        /// <summary>手順 5 (共有)。次の投げのために円盤を台へ戻す (犬の現在の追いかけも終わる)。</summary>
        static IEnumerator Recall()
        {
            recaller.Interact();
            var back = Wait(0.4); while (back.MoveNext()) yield return null;
            var idle = WaitIdle(4); while (idle.MoveNext()) yield return null;
        }

        /// <summary>netorder の 1 ケース。既定モードの RunThrow と同じ手順 (Idle 待ち → プリセット →
        /// 犬を定位置へ → 固定台の Interact → 観測 → recall) を通し、投げの前後で
        /// debugHeldOverride を切り替えて「通知が先、held が後」を作る。</summary>
        static IEnumerator RunNetThrow(int index, NetCase c, int repeat)
        {
            Shot shot = Shots.First(s => s.name == NetShot);
            var rec = new ThrowResult
            {
                index = index, variant = NetVariant, shot = shot.name,
                speed = shot.speed, elevationDeg = shot.elevation,
                allowed = shot.allowed, expectKind = shot.expectKind,
                netCase = c.name,
                note = "netorder " + c.name + " (repeat " + (repeat + 1) + "). expect: " + c.expect + " — " + c.note,
            };
            report.throwResults.Add(rec);
            report.throwsRun++;
            var notes = new List<string>();

            // 1. プリセット合わせの前に override を 0 へ。1 のままだと犬は Ready から Idle へ戻らず、
            //    WaitIdle が毎回空振りする。
            SetOverride(0);
            var pre = EnsureVariant(NetVariant, rec); while (pre.MoveNext()) yield return null;
            if (rec.variantApplied != NetVariant)
            {
                rec.outcome = "blocked"; rec.ok = false; rec.netOk = false;
                rec.netNote = "preset " + NetVariant + " could not be selected (stuck on " + rec.variantApplied + ")";
                yield break;
            }

            // 2. 犬を立ち位置へ (既定モードと同じ唯一の注入)
            var home = PlaceDogHome(rec); while (home.MoveNext()) yield return null;
            var settle = Wait(0.3); while (settle.MoveNext()) yield return null;

            // 3. 投げの前に「まだ手の中」を作る。Interact の瞬間に 1 なら ReqCatch は保留へ入る。
            //    Ready まで待つのは本番に近い文脈にするためで、**順序の判定には要らない** (試作シーンに
            //    持ち手がいないので、standoffWhenHeld や CanPlay の都合で入らないことがありうる)。
            if (c.holdBeforeThrow)
            {
                SetOverride(1);
                double readyUntil = EditorApplication.timeSinceStartup + NetReadyWait;
                while (EditorApplication.timeSinceStartup < readyUntil && ReadInt(catcher, "state") != StateReady)
                    yield return null;
                if (ReadInt(catcher, "state") != StateReady)
                    notes.Add("dog did not reach Ready within " + NetReadyWait + " s (state=" + ReadInt(catcher, "state")
                        + "); the case still runs — only the pending order is judged");
            }

            // 4. 投げる。steal-rethrow だけは 2 投げぶん走るので窓を伸ばす。
            double extra = c.name == "steal-rethrow" ? NetStealExtra : 0.0;
            double start = EditorApplication.timeSinceStartup;
            throwDeadline = start + ThrowBudget + extra;
            thrower.SetProgramVariable("speed", shot.speed);
            thrower.SetProgramVariable("elevationDeg", shot.elevation);
            thrower.SetProgramVariable("sideDeg", shot.side);
            int mark = Lines.Count;
            thrower.Interact();

            // 5. 観測。行と state を毎フレーム拾いながら、ケースごとに override を切り替える。
            //    seen[] は「判定する区間で実際に通った state」。steal-rethrow は 2 投げ目の受理で
            //    数え直す (1 投げ目の追走を 2 投げ目の一周の証拠にしない)。
            bool engaged = false;
            var seen = new bool[NetStateCount];
            int scan = mark, firstPlan = -1, expiredAt = -1, cancelAt = -1, stealResolvedAt = -1;
            bool stolen = false, phase2Engaged = false;
            double release = c.name == "held-first" ? start + NetReleaseEarly
                : c.name == "expired" ? start + NetReleaseLate : -1;
            double regrabUntil = -1, watchUntil = -1, cancelTime = -1, settledAt = -1, stealRelease = -1;
            double planWait = start + NetPlanWait;
            double limit = Math.Min(start + ThrowSettle + extra, throwDeadline);

            while (EditorApplication.timeSinceStartup < limit)
            {
                double now = EditorApplication.timeSinceStartup;
                int st = ReadInt(catcher, "state");
                if (st != StateIdle && st != StateReady) engaged = true;
                if (st >= 0 && st < seen.Length) seen[st] = true;
                for (; scan < Lines.Count; scan++)
                {
                    string l = Lines[scan];
                    if (firstPlan < 0 && l.Contains("] plan=")) firstPlan = scan;
                    if (expiredAt < 0 && l.Contains("] req expired")) expiredAt = scan;
                    if (cancelAt < 0 && l.Contains("] req cancel") && l.Contains("reason=held-regrab"))
                    { cancelAt = scan; cancelTime = now; }
                    if (stolen && stealResolvedAt < 0 && l.Contains("] req resolved"))
                    {
                        // 2 投げ目が受理された瞬間から、持ち帰りの証拠を数え直す。
                        stealResolvedAt = scan;
                        for (int k = 0; k < seen.Length; k++) seen[k] = false;
                    }
                }

                if (c.name == "held-first")
                {
                    // 期限のはるか手前で手を離す → 次のフレームで保留が解決するはず。
                    if (overrideNow == 1 && now >= release) SetOverride(0);
                    if (engaged && st == StateIdle) break;
                }
                else if (c.name == "release-first")
                {
                    if (engaged && st == StateIdle) break;
                }
                else if (c.name == "expired")
                {
                    // 期限を過ぎてから手を離し、**そのあと 2 秒**「何も起きない」ことを見る。
                    if (overrideNow == 1 && now >= release)
                    {
                        if (expiredAt < 0) notes.Add("no 'req expired' line yet when the hand was released at "
                            + NetReleaseLate.ToString("F2", CultureInfo.InvariantCulture) + " s");
                        SetOverride(0);
                        watchUntil = now + NetExpiredWatch;
                    }
                    if (watchUntil > 0 && now >= watchUntil) break;
                }
                else if (c.name == "steal-rethrow")
                {
                    // 追走が始まったら (accept + plan=) 相手が取って即投げ直す。
                    // override を先に立ててから Interact する: 投げ台は実値の IsHeldNow を見るので
                    // 投げは通り、捕り手は「まだ手の中」に見えて保留に入る。
                    if (!stolen)
                    {
                        if (firstPlan >= 0 && FindLine(Lines, "] req accept", null, mark) >= 0)
                        {
                            SetOverride(1);
                            thrower.Interact();
                            stolen = true;
                            stealRelease = now + NetReleaseEarly;
                        }
                        else if (now > planWait)
                        { notes.Add("no 'req accept' + 'plan=' within " + NetPlanWait + " s: the first throw never started a chase"); break; }
                    }
                    else
                    {
                        if (overrideNow == 1 && now >= stealRelease) SetOverride(0);   // 相手が投げて手を離した
                        if (stealResolvedAt >= 0)
                        {
                            if (st != StateIdle && st != StateReady) phase2Engaged = true;
                            if (phase2Engaged && st == StateIdle) break;   // 2 投げ目の一周が終わった
                        }
                        else if (now > stealRelease + NetPlanWait)
                        { notes.Add("the second launch was never resolved within " + NetPlanWait + " s of the release"); break; }
                    }
                }
                else   // regrab
                {
                    if (firstPlan < 0)
                    {
                        if (now > planWait)
                        { notes.Add("no 'plan=' within " + NetPlanWait + " s of the throw: the dog never started a chase"); break; }
                    }
                    else
                    {
                        if (overrideNow == 0 && regrabUntil < 0) { SetOverride(1); regrabUntil = now + NetRegrabHold; }
                        if (overrideNow == 1 && now >= regrabUntil) SetOverride(0);
                        if (cancelAt >= 0)
                        {
                            if (settledAt < 0 && (st == StateIdle || st == StateReady)) settledAt = now;
                            if (overrideNow == 0 && (settledAt >= 0 || now > cancelTime + NetRegrabSettle)) break;
                        }
                        else if (now > regrabUntil + NetRegrabSettle)
                        { notes.Add("no 'req cancel reason=held-regrab' line after the re-grab"); break; }
                    }
                }
                yield return null;
            }

            rec.seconds = Math.Round(EditorApplication.timeSinceStartup - start, 2);
            rec.endState = ReadInt(catcher, "state");
            rec.reachedIdle = rec.endState == StateIdle;
            for (int i = mark; i < Lines.Count; i++) rec.lines.Add(Lines[i]);
            foreach (string l in rec.lines) if (l.Contains("[KH_FrisbeeCatch] req ")) rec.reqLines.Add(l);

            // netorder は Ready で終わるのが正しいケース (expired) を含むので、「Idle へ戻らなかった」
            // だけで timeout にはしない。
            Classify(rec, engaged, true);
            JudgeMotion(rec);
            JudgeKind(rec);
            rec.caught = rec.outcome == "caught";
            rec.completed = rec.reachedIdle;
            rec.outcomeOk = shot.allowed.Contains(rec.outcome);
            bool watchedFull = watchUntil > 0 && EditorApplication.timeSinceStartup >= watchUntil - 0.1;
            JudgeNetOrder(rec, c, engaged, watchedFull, cancelTime, settledAt, seen, notes);
            // Codex M2: netorder の合否は netOk だけ。outcomeOk / completed / motionOk / expectOk は
            // 参考列のまま残す (release-first だけ、その 4 条件も netOk の一部として使う)。
            rec.ok = rec.netOk;
            if (rec.ok) report.throwsOk++;

            // 6. 片付け。円盤を戻す前に override を必ず 0 へ。
            throwDeadline = 0;
            SetOverride(0);
            var tidy = Recall(); while (tidy.MoveNext()) yield return null;
        }

        /// <summary>netorder の合否。req の行の**順序**だけで決める。
        /// 行は `[KH_FrisbeeCatch] req <動詞>` と `reason=<理由>` を別々の部分文字列として見るので、
        /// 列を揃える空白 (req cancel   reason=...) を跨いでも当たる。</summary>
        static void JudgeNetOrder(ThrowResult rec, NetCase c, bool engaged, bool watchedFull,
            double cancelTime, double settledAt, bool[] seen, List<string> notes)
        {
            var L = rec.lines;
            int hold = FindLine(L, "] req hold", null, 0);
            int accept = FindLine(L, "] req accept", null, 0);
            int plan = FindLine(L, "] plan=", null, 0);
            string trip;
            rec.netRoundTrip = RoundTrip(seen, L, out trip);
            rec.netStates = StatesOf(seen);
            switch (c.name)
            {
                case "held-first":
                {
                    int resolved = FindLine(L, "] req resolved", null, hold < 0 ? 0 : hold + 1);
                    int thrown = FindLine(L, "[KH_FrisbeeCatch] throw ", null, resolved < 0 ? 0 : resolved + 1);
                    int planned = FindLine(L, "] plan=", null, thrown < 0 ? 0 : thrown + 1);
                    // 投げ遅れの分だけ幾何がずれるので、flat でも地上回収に落ちるのは許す (取れ方は問わない)。
                    // Codex M1: ただし「Idle へ戻った」だけでは一周の証拠にしない (下の RoundTrip)。
                    bool landed = rec.outcome == "caught" || rec.outcome == "ground";
                    rec.netOk = hold >= 0 && resolved > hold && thrown > resolved && planned > thrown
                        && landed && rec.completed && rec.netRoundTrip;
                    notes.Add("hold@" + hold + " resolved@" + resolved + " throw@" + thrown + " plan@" + planned
                        + " outcome=" + rec.outcome + " completed=" + rec.completed + " roundTrip=" + trip);
                    break;
                }
                case "release-first":
                {
                    int local = FindLine(L, "] req accept", "path=local", 0);
                    // 対照なので、ここだけは既定モードの 4 条件もそのまま合否に入れる。
                    rec.netOk = accept >= 0 && local == accept && hold < 0
                        && rec.outcomeOk && rec.completed && rec.motionOk && rec.expectOk && rec.netRoundTrip;
                    notes.Add("accept@" + accept + " path=local:" + (local >= 0) + " hold@" + hold
                        + " (must be -1) legacy outcomeOk=" + rec.outcomeOk + " completed=" + rec.completed
                        + " motionOk=" + rec.motionOk + " expectOk=" + rec.expectOk + " roundTrip=" + trip);
                    break;
                }
                case "steal-rethrow":
                {
                    // 1 投げ目 (gen N) を受理して追走 → 相手が取って投げ直す (gen N+1) → 古い方が取り消され、
                    // 新しい方だけが保留から解決して一周まで行く。gen で 2 本を区別する。
                    int genA = accept < 0 ? -1 : GenOf(L[accept]);
                    int plan1 = FindLine(L, "] plan=", null, accept < 0 ? 0 : accept + 1);
                    int holdB = FindLineGenNot(L, "] req hold", genA, plan1 < 0 ? 0 : plan1 + 1);
                    int genB = holdB < 0 ? -1 : GenOf(L[holdB]);
                    // 取消の理由は、奪取が生きた投球に追いついたか (superseded) 先に手が見えたか (held-regrab) で変わる。
                    int cancelA = FindLineGen(L, "] req cancel", "reason=held-regrab", genA, plan1 < 0 ? 0 : plan1 + 1);
                    if (cancelA < 0) cancelA = FindLineGen(L, "] req cancel", "reason=superseded", genA, plan1 < 0 ? 0 : plan1 + 1);
                    int resolvedB = holdB < 0 ? -1 : FindLineGen(L, "] req resolved", null, genB, holdB + 1);
                    int thrownB = FindLine(L, "[KH_FrisbeeCatch] throw ", null, resolvedB < 0 ? 0 : resolvedB + 1);
                    int plan2 = FindLine(L, "] plan=", null, thrownB < 0 ? 0 : thrownB + 1);
                    rec.netOk = accept >= 0 && genA >= 0 && plan1 > accept
                        && holdB > plan1 && genB >= 0 && genB != genA
                        && cancelA > plan1 && resolvedB > holdB && cancelA < resolvedB   // 古い投球の取消が新しい投球の受理より先
                        && thrownB > resolvedB && plan2 > thrownB
                        && rec.netRoundTrip;
                    notes.Add("gen " + genA + " accept@" + accept + " plan@" + plan1 + " cancel@" + cancelA
                        + " -> gen " + genB + " hold@" + holdB + " resolved@" + resolvedB + " throw@" + thrownB
                        + " plan@" + plan2 + " (cancel before resolved="
                        + (cancelA >= 0 && resolvedB >= 0 && cancelA < resolvedB) + ") roundTrip=" + trip);
                    break;
                }
                case "expired":
                {
                    int expired = FindLine(L, "] req expired", null, hold < 0 ? 0 : hold + 1);
                    // 失効のあとに投球が蘇らないこと。resolved / throw / plan= のどれが出ても不合格。
                    bool quiet = expired >= 0
                        && FindLine(L, "] req resolved", null, expired + 1) < 0
                        && FindLine(L, "[KH_FrisbeeCatch] throw ", null, expired + 1) < 0
                        && FindLine(L, "] plan=", null, expired + 1) < 0;
                    rec.netOk = hold >= 0 && expired > hold && quiet && !engaged && watchedFull;
                    notes.Add("hold@" + hold + " expired@" + expired + " quiet-after-expiry=" + quiet
                        + " leftIdleOrReady=" + engaged + " watched " + NetExpiredWatch + " s after the release="
                        + watchedFull + " endState=" + rec.endState);
                    break;
                }
                case "regrab":
                {
                    int cancel = FindLine(L, "] req cancel", "reason=held-regrab", plan < 0 ? 0 : plan + 1);
                    double delay = settledAt >= 0 && cancelTime > 0 ? settledAt - cancelTime : -1;
                    bool settled = delay >= 0 && delay <= NetRegrabSettle;
                    rec.netOk = accept >= 0 && plan > accept && cancel > plan && settled;
                    notes.Add("accept@" + accept + " plan@" + plan + " cancel(held-regrab)@" + cancel
                        + " back to Idle/Ready " + (delay < 0 ? "never" : delay.ToString("F2", CultureInfo.InvariantCulture) + " s")
                        + " after the cancel (limit " + NetRegrabSettle + " s) endState=" + rec.endState);
                    break;
                }
                default:
                    // 表にケースを足したのに判定を書き忘れた場合。黙って合格にしない。
                    rec.netOk = false;
                    notes.Add("netCase '" + c.name + "' has no rule in JudgeNetOrder");
                    break;
            }
            rec.netNote = string.Join(" | ", notes.ToArray());
        }

        static int FindLine(List<string> lines, string a, string b, int from)
        {
            for (int i = Math.Max(0, from); i < lines.Count; i++)
                if (lines[i].Contains(a) && (b == null || lines[i].Contains(b))) return i;
            return -1;
        }

        /// <summary>gen= が一致する行だけを探す (steal-rethrow は 2 本の投球を gen で分ける)。</summary>
        static int FindLineGen(List<string> lines, string a, string b, int gen, int from)
        {
            for (int i = Math.Max(0, from); i < lines.Count; i++)
                if (lines[i].Contains(a) && (b == null || lines[i].Contains(b)) && GenOf(lines[i]) == gen) return i;
            return -1;
        }

        /// <summary>gen= が「その値ではない」行を探す (新しい世代の最初の 1 本を取る)。</summary>
        static int FindLineGenNot(List<string> lines, string a, int gen, int from)
        {
            for (int i = Math.Max(0, from); i < lines.Count; i++)
            {
                if (!lines[i].Contains(a)) continue;
                int g = GenOf(lines[i]);
                if (g >= 0 && g != gen) return i;
            }
            return -1;
        }

        /// <summary>req の行の gen= を読む。読めなければ -1。</summary>
        static int GenOf(string line)
        {
            int v;
            return int.TryParse(Field(line, " gen="), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : -1;
        }

        /// <summary>持ち帰りの証拠 (Codex M1)。「Idle へ戻った」(completed) だけでは、
        /// plan=2 のまま取り消されて Idle に落ちた投げも通ってしまう。
        /// 咥えた state (JumpCarry / Pickup) か `catch OK` の行、**かつ** 返す state (Return / Drop) を
        /// 実際に見たことを求める。state は毎フレーム読んでいるので、通ったなら必ずどれかに当たる。</summary>
        static bool RoundTrip(bool[] seen, List<string> lines, out string why)
        {
            bool carried = seen[StateJumpCarry] || seen[StatePickup];
            bool caughtLine = FindLine(lines, "catch OK", null, 0) >= 0;
            bool returned = seen[StateReturn] || seen[StateDrop];
            bool ok = returned && (carried || caughtLine);
            why = (ok ? "OK" : "MISSING") + " states=" + StatesOf(seen)
                + " carried(" + StateJumpCarry + "/" + StatePickup + ")=" + carried
                + " catchOK=" + caughtLine
                + " returned(" + StateReturn + "/" + StateDrop + ")=" + returned;
            return ok;
        }

        static string StatesOf(bool[] seen)
        {
            var got = new List<string>();
            for (int i = 0; i < seen.Length; i++) if (seen[i]) got.Add(i.ToString());
            return got.Count == 0 ? "(none)" : string.Join(",", got.ToArray());
        }

        /// <summary>捕り手の debugHeldOverride を書く (0 = 実値 / 1 = 保持扱い / -1 = 解放扱い)。
        /// 書いた事実を覚えておき、どの終わり方でも RestoreOverride が 0 に戻せるようにする。</summary>
        static void SetOverride(int value)
        {
            if (catcher == null || !EditorApplication.isPlaying || overrideNow == value) return;
            catcher.SetProgramVariable("debugHeldOverride", value);
            overrideNow = value;
            if (value != 0) overrideTouched = true;
        }

        /// <summary>netorder の後始末。debugHeldOverride を 0 へ戻し、**読み直して 0 を確かめてから**
        /// 記録する。正常終了・BLOCKED・停止フラグ・番犬・例外のすべてが StopInternal を通るので、そこから呼ぶ。
        /// 値は Play 中の Udon ヒープにしか無く (ハーネスはシーンを保存しない) Play を抜ければ消えるが、
        /// 「抜けたから大丈夫」を理由にしない — 抜ける前に戻し、確かめた結果を記録する。
        ///
        /// Codex M2: 確かめられなかった実行を PASS にしない。読み直しが 0 でない・例外・Play が終わっていた
        /// のいずれでも errors に 1 件足し (= FAIL)、overrideTouched を**下ろさない**ので
        /// 後続の終了経路 (ExitingPlayMode / Finish) がもう一度試す。</summary>
        static void RestoreOverride()
        {
            if (report == null) return;
            if (!overrideTouched)
            {
                // 一度も 1 / -1 を書いていない = 戻すものが無い (既定モードは常にこちら)。
                if (report.mode == "netorder" && report.overrideRestored != null && report.overrideRestored.StartsWith("PENDING"))
                    report.overrideRestored = "OK: netorder never wrote debugHeldOverride (nothing to restore)";
                return;
            }
            string outcome;
            try
            {
                if (catcher == null || !EditorApplication.isPlaying)
                {
                    outcome = "NOT_RESTORED_IN_SESSION: play mode had already ended, so the harness could not write "
                        + "debugHeldOverride back to 0 and read it back. (The value lives only in the play-mode Udon heap "
                        + "and the harness never saves the scene, but this run did not confirm it.)";
                }
                else
                {
                    catcher.SetProgramVariable("debugHeldOverride", 0);
                    overrideNow = 0;
                    object v;
                    int now = catcher.TryGetProgramVariable("debugHeldOverride", out v) && v is int ? (int)v : -99;
                    outcome = now == 0
                        ? "OK: debugHeldOverride=0, read back from the live UdonBehaviour"
                        : "FAILED: debugHeldOverride read back as " + now + " after the restore";
                }
            }
            catch (Exception e) { outcome = "FAILED: " + e.Message; }
            report.overrideRestored = outcome;
            if (outcome.StartsWith("OK:")) { overrideTouched = false; return; }
            // 失敗: touched を下ろさない (次の終了経路で再試行する) + PASS を落とす。
            if (!overrideFailLogged)
            {
                overrideFailLogged = true;
                report.errors.Add("[KH Frisbee Harness] debugHeldOverride was not confirmed back at 0: " + outcome);
            }
        }

        /// <summary>拾った行から 1 投げの outcome / kind を決める。</summary>
        /// <param name="readyEndsOk">netorder 用。Ready で終わるのが正しいケース (expired) があるので、
        /// 「Idle へ戻らなかった」だけで timeout にはしない。既定モードは false のまま。</param>
        static void Classify(ThrowResult rec, bool engaged, bool readyEndsOk = false)
        {
            string ok = null, miss = null;
            foreach (string l in rec.lines)
            {
                if (l.Contains("catch OK")) ok = l;
                if (l.Contains("catch MISS")) miss = l;
                if (l.Contains("] plan=")) rec.planLine = l;
                if (l.Contains("] takeoff ")) rec.takeoffLine = l;
                if (l.Contains("[KH_Frisbee] launch ")) rec.launchLine = l;
                // round 5: 1 投げにつき最後に成立したプラン 1 本ぶんを採る (計画が差し替わったら上書き)。
                float ee;
                if (l.Contains(" etaErr=") && TryNum(l, " etaErr=", out ee))
                { rec.etaLine = l; rec.etaErr = ee; rec.etaAt = Field(l, " at="); rec.etaMode = Field(l, " mode="); }
            }
            if (ok != null) { rec.outcome = "caught"; rec.outcomeLine = ok; rec.kind = KindOf(ok); return; }
            if (miss != null) { rec.outcome = "miss"; rec.outcomeLine = miss; rec.kind = KindOf(miss); return; }
            if (rec.planLine != null && rec.planLine.Contains("plan=2 ")) { rec.outcome = "ground"; rec.outcomeLine = rec.planLine; rec.kind = "ground"; return; }
            if (!rec.reachedIdle && !(readyEndsOk && rec.endState == StateReady))
            { rec.outcome = "timeout"; rec.outcomeLine = "state=" + rec.endState + " when the " + ThrowSettle + " s window ran out"; return; }
            rec.outcome = "none";
            rec.outcomeLine = rec.launchLine == null ? "no launch line: the throw never left the stand"
                : engaged ? "the dog engaged but logged no catch / miss / ground line"
                : "the disc flew but the dog never left Idle";
        }

        /// <summary>キャッチの行から「どう終わったか」を読む (Codex M2)。
        /// `stand catch ...` は立ったまま、`overtake catch ...` は走り抜けざま、素の `catch OK/MISS` は
        /// 跳んで (UpdateJump の中でしか出ない行)。</summary>
        static string KindOf(string line)
        {
            if (line.Contains("stand catch")) return "stand";
            if (line.Contains("overtake catch")) return "over";
            return "jump";
        }

        /// <summary>期待した終わり方か (Codex M2)。v4 が flat / lob / far で跳ばずに立って取る回帰は
        /// outcome では caught なので落ちない。行ごとに書いた expectKind で落とす。
        /// 結果が想定外のときは outcomeOk が既に落としているので、ここでは何も足さない。</summary>
        static void JudgeKind(ThrowResult rec)
        {
            if (string.IsNullOrEmpty(rec.expectKind) || rec.expectKind == "any")
            { rec.expectOk = true; rec.expectNote = "expectKind=any"; return; }
            // IMP-DOG-017: "over|ground" のような候補の並び。
            if (!rec.expectKind.Split('|').Contains(rec.kind))
            {
                rec.expectOk = false;
                rec.expectNote = "kind=" + (string.IsNullOrEmpty(rec.kind) ? "(none)" : rec.kind) + " but this shot expects " + rec.expectKind;
                return;
            }
            // IMP-DOG-017: 跳んだ投げは取り方 (mode) も見る。beside / chase は「追いついて跳ぶ」(chase) の投げ。
            if (!string.IsNullOrEmpty(rec.expectMode) && rec.kind == "jump")
            {
                string mode = !string.IsNullOrEmpty(rec.mode) ? rec.mode
                    : Field(rec.takeoffLine ?? rec.outcomeLine ?? "", " mode=");
                if (mode != rec.expectMode)
                {
                    rec.expectOk = false;
                    rec.expectNote = "kind=jump but mode=" + (string.IsNullOrEmpty(mode) ? "(none)" : mode) + ", this shot expects mode=" + rec.expectMode;
                    return;
                }
                rec.expectOk = true; rec.expectNote = "kind=jump mode=" + mode + " == expected"; return;
            }
            rec.expectOk = true; rec.expectNote = "kind=" + rec.kind + " in expectKind=" + rec.expectKind;
        }

        /// <summary>動きの質 (round 4)。「取れた」だけでは PCVR 3 の「機械っぽい」を落とせないので、
        /// 踏み切り / キャッチの行に残る mode と直前 1 秒の最大旋回速度・最小速度で別に合否を出す。
        /// 減速せずに跳ぶのが既定なので、速度 0 を許すのは around (回り込んで立ち止まってから跳ぶ) と
        /// stand (立ち取り) だけ (IMP-DOG-017: 旧 wait から改名。早く着くことを不合格にしなくなった)。
        /// 旋回の急さはどの mode でも許さない。</summary>
        static void JudgeMotion(ThrowResult rec)
        {
            // 判定に使えるのは踏み切り / キャッチの行だけ。plan= の行にも mode= は載るが数値が無いので、
            // 地上回収 (ground) のように跳ばずに終わった投げは「判定していない」(motionOk 既定 true) のまま。
            string line = rec.outcomeLine != null && rec.outcomeLine.Contains(" yawRateMax=") ? rec.outcomeLine
                : rec.takeoffLine != null && rec.takeoffLine.Contains(" yawRateMax=") ? rec.takeoffLine : null;
            if (line == null)
            {
                // Codex M3: 跳んだのに数字が無い = 判定できない、であって合格ではない。
                // 踏み切りの行があった (= 跳んだ) / キャッチの行が跳躍だった のどちらかなら不合格にする。
                if (rec.kind == "jump" || rec.kind == "stand" || rec.kind == "over" || rec.takeoffLine != null)
                {
                    rec.motionOk = false;
                    rec.motionNote = "kind=" + rec.kind + " but no takeoff / catch line carried yawRateMax= : "
                        + (rec.takeoffLine ?? rec.outcomeLine ?? "(no line)");
                }
                return;   // 跳びも取りもしなかった投げ (ground / none) は既定どおり「判定していない」
            }
            rec.mode = Field(line, " mode=");
            rec.yawRateMax = Num(line, " yawRateMax=");
            rec.speedMin = Num(line, " speedMin=");
            rec.vTake = Num(line, " vTake=");
            rec.approachPath = Num(line, " path=");
            // Codex M3: 3 つのうち 1 つでも読めなければ不合格。既定の true のまま通していたのが指摘。
            if (rec.yawRateMax < 0f || rec.speedMin < 0f || rec.vTake < 0f)
            { rec.motionOk = false; rec.motionNote = "motion numbers missing (yawRateMax / speedMin / vTake) from: " + line; return; }
            if (rec.yawRateMax > MotionMaxYawRate)
            { rec.motionOk = false; rec.motionNote = "yawRateMax " + rec.yawRateMax.ToString("F0") + " > " + MotionMaxYawRate + " deg/s (snap turn)"; return; }
            // 助走が 1 秒ぶんの距離 (1.5 m/s x 1 s) に満たない投げは速度で判定しない。円盤が犬のすぐ横に
            // 落ちてくる投げでは、どれだけ生き物らしく動いても直前 1 秒を走り続けることは幾何的にできない。
            if (rec.approachPath >= 0f && rec.approachPath < MotionMinPath)
            {
                rec.motionOk = true;
                rec.motionNote = "mode=" + rec.mode + " yawRateMax=" + rec.yawRateMax.ToString("F0")
                    + " (speed not judged: approach was only " + rec.approachPath.ToString("F1") + " m)";
                return;
            }
            if (rec.mode != "around" && rec.mode != "stand" && rec.speedMin < MotionMinSpeed)
            { rec.motionOk = false; rec.motionNote = "mode=" + rec.mode + " but speedMin " + rec.speedMin.ToString("F2") + " < " + MotionMinSpeed + " m/s (slowed to a stop before taking off)"; return; }
            rec.motionOk = true;
            rec.motionNote = "mode=" + rec.mode + " yawRateMax=" + rec.yawRateMax.ToString("F0") + " speedMin=" + rec.speedMin.ToString("F2");
        }

        static string Field(string line, string key)
        {
            int at = line.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return "";
            at += key.Length;
            int end = line.IndexOf(' ', at);
            return end < 0 ? line.Substring(at) : line.Substring(at, end - at);
        }

        static float Num(string line, string key)
        {
            string s = Field(line, key);
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : -1f;
        }

        /// <summary>Num と違い「読めなかった」と「負の値」を混同しない (etaErr は負になりうる)。</summary>
        static bool TryNum(string line, string key, out float v)
        {
            return float.TryParse(Field(line, key), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }


        static void Summarise()
        {
            report.perVariant.Clear();
            foreach (var group in report.throwResults.GroupBy(t => t.variant).OrderBy(g => g.Key))
            {
                var c = new VariantSummary { variant = group.Key };
                foreach (var t in group)
                {
                    if (t.outcome == "caught") c.caught++;
                    else if (t.outcome == "miss") c.miss++;
                    else if (t.outcome == "ground") c.ground++;
                    else if (t.outcome == "none") c.none++;
                    else c.other++;
                    if (t.completed) c.completed++;
                    if (t.kind == "jump") c.jump++;
                    else if (t.kind == "stand") c.stand++;
                    else if (t.kind == "over") c.over++;
                }
                report.perVariant.Add(c);
            }
            report.throwsOk = report.throwResults.Count(t => t.ok);
            SummariseEta();
            SummariseSixDogs();   // IMP-DOG-012 (no-op outside sixdogs)
        }

        /// <summary>round 5: etaErr の分布をまとめる。**合否には一切関わらない** —
        /// 「余裕 (HookEtaMargin) を広げる」のか「Eta に係数を入れる」のかを、症状ではなく数字で決めるため。</summary>
        static void SummariseEta()
        {
            var s = report.throwResults.Where(t => t.etaErr > -90f).Select(t => t.etaErr).OrderBy(v => v).ToList();
            var e = report.eta;
            e.samples = s.Count;
            if (s.Count == 0) { e.verdict = "NOT_MEASURED: どの投げでも etaErr 行が出なかった"; return; }
            e.min = s[0]; e.max = s[s.Count - 1];
            e.median = s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) * 0.5f;
            e.mean = s.Sum() / s.Count;
            e.optimistic = s.Count(v => v > 0f);
            e.over100ms = s.Count(v => v > 0.10f);
            e.byMode = string.Join(" ", report.throwResults.Where(t => t.etaErr > -90f)
                .GroupBy(t => string.IsNullOrEmpty(t.etaMode) ? "?" : t.etaMode)
                .OrderBy(g => g.Key)
                .Select(g => g.Key + "=" + g.Count() + "/" + (g.Sum(t => t.etaErr) / g.Count()).ToString("F2", CultureInfo.InvariantCulture))
                .ToArray());
            // しきい値は「1 フレーム 2 つぶん」ではなく「HookEtaMargin (0.15 s) を食い潰すか」で置く。
            // median がそこを超えて楽観的なら、余裕を広げても次の幾何でまた足りなくなる = Eta 側の問題。
            e.verdict = e.median > 0.15f ? "OPTIMISTIC: median " + e.median.ToString("F2", CultureInfo.InvariantCulture) + " s > HookEtaMargin 0.15 — Eta に係数 (EtaSafety) を入れることを検討する"
                : e.median > 0.05f ? "SLIGHTLY_OPTIMISTIC: median " + e.median.ToString("F2", CultureInfo.InvariantCulture) + " s — 余裕の内。様子見"
                : e.median < -0.15f ? "PESSIMISTIC: median " + e.median.ToString("F2", CultureInfo.InvariantCulture) + " s — 候補を必要以上に捨てている可能性"
                : "OK: median " + e.median.ToString("F2", CultureInfo.InvariantCulture) + " s — 見積もりと実際がほぼ一致";
        }

        // ---------------------------------------------------------------- 小道具
        /// <summary>まだ理由が入っていないときだけ理由を入れる。JsonUtility は null 文字列を "" にして返すので
        /// `??` では判定できない (journal を読み直した後の report は failure が "" になっている)。</summary>
        static void Why(string reason)
        {
            if (string.IsNullOrEmpty(report.failure)) report.failure = reason;
        }

        static IEnumerator Wait(double seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until) yield return null;
        }

        static IEnumerator WaitIdle(double seconds) { return WaitIdleOn(catcher, seconds); }

        static IEnumerator WaitIdleOn(UdonBehaviour c, double seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until && ReadInt(c, "state") != StateIdle) yield return null;
        }

        static UdonBehaviour Find(string goName)
        {
            foreach (var u in Resources.FindObjectsOfTypeAll<UdonBehaviour>())
                if (u != null && u.gameObject.scene.IsValid() && u.gameObject.name == goName) return u;
            throw new InvalidOperationException("No runtime UdonBehaviour on a GameObject named " + goName);
        }

        static bool Has(UdonBehaviour u, string symbol) { object _; return u.TryGetProgramVariable(symbol, out _); }

        /// <summary>IMP-DOG-017: 回り込みのループ探索 (_planHookTries) は計画器の書き直しで無くなりうるので、
        /// 無ければ既定値 (増分 0) として読む。_planScans / _planEvaluations は従来どおり必須。</summary>
        static int ReadIntOr(UdonBehaviour u, string symbol, int fallback)
        {
            object value;
            return u.TryGetProgramVariable(symbol, out value) && value is int ? (int)value : fallback;
        }

        static int ReadInt(UdonBehaviour u, string symbol)
        {
            object value;
            if (!u.TryGetProgramVariable(symbol, out value))
                throw new InvalidOperationException("Missing live Udon symbol: " + u.gameObject.name + "." + symbol);
            return value is int ? (int)value : -1;
        }

        static ClientSimPlayer LocalPlayer()
        {
            return Resources.FindObjectsOfTypeAll<ClientSimPlayer>().FirstOrDefault(p => p.Player != null && p.Player.isLocal);
        }

        static int[] Variants(Options o)
        {
            if (!string.IsNullOrEmpty(o.variants))
            {
                var picked = o.variants.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
                    .Select(s => { int n; return int.TryParse(s, out n) ? n : -1; })
                    .Where(n => n >= 0 && n < VariantCount).Distinct().ToArray();
                if (picked.Length > 0) return picked;
            }
            return Enumerable.Range(0, VariantCount).ToArray();
        }

        static Shot[] Plan(Options o)
        {
            if (!string.IsNullOrEmpty(o.shots))
            {
                var names = o.shots.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                var picked = Shots.Where(s => names.Contains(s.name)).ToArray();
                if (picked.Length > 0) return picked;
            }
            return Shots;
        }

        static Options ReadOptions()
        {
            try
            {
                if (!File.Exists(OptionsPath)) return new Options();
                var o = JsonUtility.FromJson<Options>(File.ReadAllText(OptionsPath));
                return o ?? new Options();
            }
            catch { return new Options(); }
        }

        static void Log(string message, string stack, LogType type)
        {
            if (report == null) return;
            if (message != null && message.StartsWith("[KH_Frisbee") && Lines.Count < 4000) Lines.Add(message);
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && report.errors.Count < 50)
            {
                report.errors.Add(message + "\n" + stack);
                if (report.status == "PASS") report.status = "FAIL";
            }
        }

        static void Save()
        {
            if (report == null) return;
            Directory.CreateDirectory(RunDir);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(RunDir + "/report.json", json);
            File.WriteAllText(Base + "/latest.json", json);
            // journal は「実行中の記録」ではなく「ドメイン再読み込みをまたぐ唯一の記憶」。
            // Play を抜けるときにもドメインは作り直されるので、status が RUNNING でなくなった後も
            // 書き続ける (ここを止めると、静的コンストラクタが古い journal を読み直して
            // 最後の投げと停止理由を丸ごと失う)。消すのは RestoreScene だけ。
            if (!closed) File.WriteAllText(Journal, json);
        }

        static string SdkVersion()
        {
            try
            {
                string text = File.ReadAllText("Packages/com.vrchat.worlds/package.json");
                int i = text.IndexOf("\"version\"", StringComparison.Ordinal);
                int a = text.IndexOf('"', text.IndexOf(':', i)) + 1, b = text.IndexOf('"', a);
                return text.Substring(a, b - a);
            }
            catch { return "unknown"; }
        }

        static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }

        static string HashDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return "ABSENT";
            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal);
            using (var sha = SHA256.Create())
            {
                var all = new List<byte>();
                foreach (string f in files) all.AddRange(sha.ComputeHash(File.ReadAllBytes(f)));
                return BitConverter.ToString(sha.ComputeHash(all.ToArray())).Replace("-", "");
            }
        }

        static string Git(string args)
        {
            try
            {
                using (var p = new System.Diagnostics.Process())
                {
                    p.StartInfo = new System.Diagnostics.ProcessStartInfo("git",
                        "-c safe.directory=\"" + Path.GetFullPath(".").Replace('\\', '/') + "\" " + args)
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    p.Start();
                    string output = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(10000)) { p.Kill(); return "TIMEOUT"; }
                    return output.Trim();
                }
            }
            catch { return "UNKNOWN"; }
        }
    }
}
