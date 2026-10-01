using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-NET-003: 停止アンカー同期 — **犬が止まるたびに全画面を寄せる**。
    ///
    /// [DES-NET-001 §5-1](../../../docs/development/issues/archive/DES-NET-001-dog-sync-method.md)
    /// で採択した「結果整合性」の実装。骨格は 3 つだけ:
    ///
    ///   1. 各クライアントは**楽観的にローカル計算する** (今までどおり。ここは何も変えない)
    ///   2. 犬が止まったら Owner が**その場所を 1 回だけ送る**
    ///   3. 受信側は自分の犬とのずれを見て、**大きければ滑って寄せる / 極端ならスナップ**
    ///
    /// ## なぜ「止まった時だけ」なのか — ここが簡潔さの鍵
    ///
    /// 移動中の位置を送らないので、**移動中標本を扱うための装備が全部要らなくなる**
    /// (前方投影・時刻印の数学・補正帯設計・ヒステリシス — DES-NET-001 §5-2 に保留)。
    /// 伝送遅延 × 速度 (2.4m/s × 0.1〜0.3s = 0.24〜0.72m) のずれが標本に乗らないので、
    /// **素朴な減算でも「遅延」を「発散」と誤読しない。**
    /// 犬は 1 分に何度も止まるので、そのたびに全画面が寄る。
    ///
    /// ## なぜ Fetch へ相乗りしないのか
    ///
    /// Manual 同期は **Behaviour 内を一括シリアライズする**。
    /// KD_DogFetchController へ相乗りさせると、`petState` が変わるたびに
    /// **古い停止標本まで再放送される**。加えて Fetch の Ownership は
    /// 投げた人・撫でた人へ移り続けるので、**アンカーの正本が人について回る**ことになる。
    /// この Behaviour は専用の GameObject に置き、**Owner は誰でもよい**
    /// (決定論シムは全クライアント等価なので、正本が誰でも成立する)。
    /// **だから SetOwner を一度も呼ばない** — 呼ばないことが仕様。
    ///
    /// ## 置き場所 — 犬の子ではなくシーン直下の兄弟
    ///
    /// Issue の初稿は「dogRoot の子 GameObject」だったが、**dogRoot は購入アセットの
    /// プレハブインスタンス**なので、子を足すとプレハブ差分になる。
    /// 既存の `KD_FetchSystem_*` / `KD_WanderSystem_*` と同じく**シーン直下の兄弟**に置く
    /// (同一 GameObject へ同期方式を混在できない、という制約への答えも同じ)。
    ///
    /// ## 補正は無い (IMP-NET-005 Phase B、2026-09-13)
    ///
    /// 犬の位置は VRCObjectSync が配るので、Non-Owner が停止位置へ滑り寄せる補正はもう無い。
    /// ここに残っている仕事は「Owner が止まった位置を Manual 同期で流し、ずれを計測して記録する」ことと、
    /// 本当のテレポート (スナップ) だけ。旧方式 (Wander の StepToward に歩かせて寄せる) は
    /// DES-NET-002 で棄却し、実装は撤去した — 再導入しないこと。経緯は archive の IMP-NET-003 / IMP-NET-005。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KD_DogStopAnchor : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/Stop Anchor が設定する)")]
        public Transform dogRoot;
        [Tooltip("Fetch 中は照合しない (目標がボールとプレイヤー = 同期実体なので放っておいても寄る)")]
        public KD_DogFetchController fetchController;
        [Tooltip("補正の足を借りる先。null なら滑り寄せは無効になり、スナップだけが効く")]
        public KD_DogWanderController wanderController;
        [Tooltip("撫でている間は動かさないための問い合わせ先。null 可")]
        public KD_DogPetController petController;

        [Header("送信 (Owner だけが行う)")]
        [Tooltip("この速度 (m/s) 以下なら「止まっている」。**Wander / Fetch のデッドバンドと同じ値**")]
        public float stopSpeedThreshold = KD_DogWanderController.DefaultStopSpeedThreshold;
        [Tooltip("止まってからこれだけ経ったら 1 標本を採る (秒)。減速の尾を拾わないための間")]
        public float stopSettleTime = DefaultStopSettleTime;
        [Tooltip("最短送信間隔 (秒)。止まる・動くを短く繰り返しても送信が詰まらないようにする")]
        public float minSendInterval = DefaultMinSendInterval;
        [Tooltip("前回のアンカーからこれだけ (m) 離れていなければ送らない。" +
            "**止まっている間じゅう送らない**ための条件で、1 停止 = 1 標本にする")]
        public float minMoveToResend = DefaultMinMoveToResend;

        [Header("受信 (Owner 以外が行う)")]
        [Tooltip("ずれがこれ (m) を超えたら歩いて寄せる。DES-NET-001 §5-1 の確定値")]
        public float slideDistance = DefaultSlideDistance;
        [Tooltip("ずれがこれ (m) を超えたらスナップする。**入室直後の 1 回目も** " +
            "slideDistance を超えていればスナップする (本人に「前の絵」が無いので副作用ゼロ)")]
        public float snapDistance = DefaultSnapDistance;

        [Header("計器 (IMP-NET-003 §5。**未配線なら分岐 1 つぶんのコストしか無い**)")]
        [Tooltip("ずれの数値を出す板。**Stop Anchor/4 が付け、5 が外す** — " +
            "実機 2 人セッションで読む必要があるので、Editor 専用の HUD ではなく " +
            "ワールドに置く TextMeshPro にしてある (アップロード後も読める)")]
        public TMPro.TextMeshPro debugText;
        [Tooltip("debugText の Transform (TMP 型の .transform は Udon 未公開のため別配線)")]
        public Transform debugTextTransform;
        [Tooltip("犬の足元からの表示高さ (m)")]
        public float debugTextHeight = 1.25f;

        [Tooltip("計器の値を VRChat のログへも落とす (**Stop Anchor/4 が立て、5 が下ろす**)。\n" +
            "板は「今」しか見せないので、2 人セッション中に 4 匹ぶんを目で読んで書き写す作業が要る。\n" +
            "行が残れば `node scripts/parse-net-log.js` が数え直せるので、\n" +
            "旧方式との A/B が「見た瞬間の運」と記憶に依らなくなる。\n" +
            "**VRChat 側に `--enable-udon-debug-logging` が要る** (無いと 1 行も出ない)")]
        public bool logToConsole;

        // IMP-DOG-034 A7 (2026-10-01): production default is OFF. This behaviour has been a pure instrument since IMP-NET-005
        // Phase B (no position correction); no runtime Udon reads Drift / HasAnchor / the synced anchor fields (only the
        // instrument board, the [KDNET] log and the Editor setup/verify/release-check menus touch it). Off = Update returns at
        // once: no per-frame speed measurement and no per-stop RequestSerialization. The script, its wiring and its synced
        // fields stay so the measurement can be switched back on. Wiring the instruments (debugText or logToConsole, i.e.
        // Stop Anchor/4) also turns it on, so the existing 4/5 menus keep working without touching this flag.
        [Tooltip("IMP-DOG-034 A7: 停止アンカーの計測を回す (既定 false = 本番。計測専用で実行時に読む Udon は無い)。\n" +
            "debugText か logToConsole が配線されていれば、これが false でも計測は回る")]
        public bool measureDrift;
        bool _measuring; // Start で 1 回だけ決める (Update の入口を分岐 1 つにする)

        // ---- 既定値の正本 (IMP-INF-001 / B の方針。宣言と 0 フォールバックが同じ定数を見る) ----
        const float DefaultStopSettleTime = 0.25f;
        const float DefaultMinSendInterval = 1.0f;
        const float DefaultMinMoveToResend = 0.25f;
        const float DefaultSlideDistance = 0.5f;
        const float DefaultSnapDistance = 2.0f;

        /// <summary>1 フレームでこれ以上動いていたらワープとみなし、速度の測定をやり直す。
        /// **自分がスナップしたぶんを「移動」と読まない**ための保険でもある
        /// (KD_DogPack.teleportThreshold と同じ考え方・同じ値)。</summary>
        const float TeleportThreshold = 1.5f;

        /// <summary>計器の更新間隔 (秒)。毎フレーム文字列を組まない
        /// (KD_DogPetController のデバッグ表示と同じ作法)。</summary>
        const float DebugInterval = 0.25f;

        // ---- 文脈 (anchorContext) のビット詰め ----
        // **これは「古い標本を捨てる」ためではなく、計器で局面を切り分けるためのもの。**
        // 古さの排除は「動いている間は照合しない」で構造的に済んでいる (下の Evaluate 参照)。
        /// <summary>下位 3 ビット = KD_DogFetchController の dogState (0〜5)。</summary>
        public const int ContextStateMask = 7;
        /// <summary>じゃれ合い中に採った標本かどうか (DES-NET-001 §2 の主犯仮説の切り分け用)。</summary>
        public const int ContextPlayingBit = 8;

        // ---- 同期変数 3 本 (犬 1 匹あたり。4 匹で 12 本) ----
        // **増やした本数と理由は IMP-NET-003 の完了条件に記録する。**
        [UdonSynced] Vector3 anchorPos;   // 止まった場所 (ワールド座標)
        [UdonSynced] int anchorServerMs;  // 採取時刻 (サーバー時刻 ms — 全クライアント共通の物差し)
        [UdonSynced] int anchorContext;   // 局面 (上のビット詰め)

        Vector3 _prevPos;
        bool _prevValid;
        float _speed;
        float _stillSince = -1f;   // 止まり始めた時刻 (-1 = 動いている)
        float _lastSendTime = -1000f;
        bool _sentOnce;

        bool _hasAnchor;           // 標本を 1 度でも受け取ったか
        int _lastSeenMs;
        bool _joinSnapPending = true; // 入室後まだ 1 度も照合していない
        float _drift;              // 直近に測ったずれ (m)。計器が読む

        // ---- IMP-NET-005: 新方式のずれを旧方式と同じ物差しで数える ----
        // **旧・補正のしきい値 (slideDistance / snapDistance) を、そのまま計器の目盛りへ転用する。**
        // 補正は撤去したが、値の意味 (「これを超えたら人が気付くずれ」) は変わらないので、
        // 基準測定 (旧方式) と移行後で**同じ物差しの数字**を突き合わせられる。
        int _stops;                // 照合できた停止の回数 (母数)
        int _overSlide;            // そのうち slideDistance を超えた回数
        int _overSnap;             // そのうち snapDistance を超えた回数 (旧方式ならワープしていた)
        float _driftMax;           // 最大のずれ (m)
        bool _countedThisStop;     // 1 停止 = 1 標本 (送信側の minMoveToResend と同じ考え方)
        float _nextDebugTime;

        // ---- ログ (logToConsole のときだけ動く) ----
        bool _cfgLogged;           // しきい値の見出しを 1 回だけ出す
        int _sends;                // Owner 側: 送った回数 (帯域の当たりを付ける材料)
        int _lastOwnerId = -2;     // -1 が「Owner 不在」なので、初回を必ず出すため -2 始まり
        bool _lastSplit;
        int _lastReqSeen = -1;     // 投げ要求の結果 (変化したときだけ 1 行出す)

        /// <summary>直近に測った「自分の犬と Owner の停止位置のずれ」(m)。
        /// **計器と、将来の場面別しきい値の判定 (DES-NET-001 §5-2) が読む窓口。**
        /// 外から呼ばれても状態は動かない。</summary>
        // net: callable-ok: 読み取りのみ — 遠隔から呼ばれても状態が動かない
        public float Drift { get { return _drift; } }

        /// <summary>アンカーを 1 度でも受け取ったか (Late Joiner の判定に使う)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool HasAnchor { get { return _hasAnchor; } }

        void Start()
        {
            if (dogRoot == null)
            {
                Debug.LogError("[KD_DogStopAnchor] wiring incomplete. Run Tools/KominkaDogRun/Stop Anchor setup.");
                enabled = false;
                return;
            }

            // 0 で入ると無言で壊れる値 (破壊的監査 RC-1 の 3 規則)。
            //   stopSpeedThreshold 0 → 「止まった」が永久に成立せず、**1 標本も送らない**
            //   stopSettleTime < 0   → 減速の尾を標本にする
            //   minSendInterval 0    → 止まっている間じゅう送る (帯域の意味が消える)
            //   slideDistance 0      → 計器の下の目盛りが消え、全停止が「超えた」に数えられる
            //   snapDistance 0       → 計器の上の目盛りが消える (同上)
            if (stopSpeedThreshold <= 0f || stopSettleTime < 0f || minSendInterval <= 0f
                || minMoveToResend <= 0f || slideDistance <= 0.01f || snapDistance <= 0.01f)
            {
                Debug.LogWarning("[KD_DogStopAnchor] tuning value is 0 - falling back to defaults.");
                if (stopSpeedThreshold <= 0f) stopSpeedThreshold = KD_DogWanderController.DefaultStopSpeedThreshold;
                if (stopSettleTime < 0f) stopSettleTime = DefaultStopSettleTime;
                if (minSendInterval <= 0f) minSendInterval = DefaultMinSendInterval;
                if (minMoveToResend <= 0f) minMoveToResend = DefaultMinMoveToResend;
                if (slideDistance <= 0.01f) slideDistance = DefaultSlideDistance;
                if (snapDistance <= 0.01f) snapDistance = DefaultSnapDistance;
            }
            // 大小が逆だと計器の 2 つの目盛りが潰れて、区別が付かなくなる
            if (snapDistance <= slideDistance)
            {
                Debug.LogWarning("[KD_DogStopAnchor] snapDistance <= slideDistance - "
                    + "計器の目盛りが 1 段になる。slideDistance の 4 倍へ広げる");
                snapDistance = slideDistance * 4f;
            }

            if (wanderController == null)
                Debug.LogWarning("[KD_DogStopAnchor] wanderController 未配線 - "
                    + "計器の局面ビット (PLAY) が立たない。");

            _prevPos = dogRoot.position;
            // IMP-DOG-034 A7: measure only when asked for or when an instrument is wired (see measureDrift)
            _measuring = measureDrift || debugText != null || logToConsole;

            // **これが出ないなら launch option が効いていない。**
            // ワールド読み込み直後に必ず 1 行出るので、セッションを始める前の自己診断になる
            if (logToConsole) LogLine("boot key=" + gameObject.name);
        }

        /// <summary>ログ 1 行。**最初の 1 行の前にしきい値の見出しを出す** —
        /// 数字だけ残っても「何 m を超えた回数なのか」が後から分からなくなるため
        /// (旧方式のログと突き合わせるときに効く)。</summary>
        void LogLine(string body)
        {
            if (!_cfgLogged)
            {
                _cfgLogged = true;
                Debug.Log("[KDNET] cfg key=" + gameObject.name
                    + " slide=" + slideDistance.ToString("F2")
                    + " snap=" + snapDistance.ToString("F2")
                    + " stopSpeed=" + stopSpeedThreshold.ToString("F2")
                    + " settle=" + stopSettleTime.ToString("F2")
                    + " minSend=" + minSendInterval.ToString("F2")
                    + " resend=" + minMoveToResend.ToString("F2"));
            }
            Debug.Log("[KDNET] " + body);
        }

        public override void OnDeserialization()
        {
            // **この Behaviour は同期変数を 3 本しか持たないので、受信は常に新しい標本。**
            // それでも時刻で弾いておく — Manual 同期の再送や、Owner 交代直後の
            // 巻き戻った標本を「新しい」と読まないため。
            // **差で比較する** (int の絶対値比較だと ~24.8 日でサーバー時刻がラップしたときに
            // 全部を「古い」と判定して二度と寄らなくなる)
            if (_hasAnchor && anchorServerMs - _lastSeenMs <= 0) return;
            _lastSeenMs = anchorServerMs;
            _hasAnchor = true;
            // IMP-NET-005: 新しい標本 = 新しい停止。計器の母数を 1 つ進めてよい
            _countedThisStop = false;
        }

        void Update()
        {
            if (!_measuring) return; // IMP-DOG-034 A7: instrument off in production (nothing at runtime reads it)
            if (dogRoot == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0.0001f) return;

            // ---- 自分の犬が動いているかを測る (送信側・受信側の共通の材料) ----
            Vector3 pos = dogRoot.position;
            Vector3 moved = pos - _prevPos;
            moved.y = 0f;
            float m = moved.magnitude;
            // ワープ (スナップ / リスポーン) は速度に数えない
            _speed = (_prevValid && m <= TeleportThreshold) ? m / dt : 0f;
            _prevPos = pos;
            _prevValid = true;

            bool still = _speed <= stopSpeedThreshold;
            if (!still)
            {
                _stillSince = -1f;
                _countedThisStop = false; // 動き出したので、次に止まったら 1 標本として数える
            }
            else if (_stillSince < 0f) _stillSince = Time.time;

            if (Networking.IsOwner(gameObject)) UpdateSend(pos, still);
            else Evaluate(pos, still);

            UpdateDebug(pos);
        }

        /// <summary>Owner: 止まったら 1 回だけ送る。</summary>
        void UpdateSend(Vector3 pos, bool still)
        {
            if (!still || _stillSince < 0f) return;
            if (Time.time - _stillSince < stopSettleTime) return;
            if (Time.time - _lastSendTime < minSendInterval) return;

            // **前回のアンカーから動いていなければ送らない。**
            // これが「1 停止 = 1 標本」の実装で、止まっている間じゅう送らないことの保証。
            // 送った直後は anchorPos == pos なので、次のフレームからは必ずここで返る
            if (_sentOnce)
            {
                Vector3 d = pos - anchorPos;
                d.y = 0f;
                if (d.magnitude < minMoveToResend) return;
            }

            anchorPos = pos;
            anchorServerMs = Networking.GetServerTimeInMilliseconds();
            anchorContext = BuildContext();
            float gap = _sends > 0 ? Time.time - _lastSendTime : 0f;
            _sentOnce = true;
            _lastSendTime = Time.time;
            RequestSerialization();

            // Owner 側は「ずれ」を測れない (自分が正本) ので、残す価値があるのは**送信の頻度**。
            // Debug View 6 の Bps は目で読むしかないが、行数と gap があれば
            // 「4 匹でどれだけ送っているか」を後から数えられる (A4 の裏取り)
            _sends++;
            if (logToConsole)
                LogLine("send key=" + gameObject.name
                    + " n=" + _sends
                    + " gap=" + gap.ToString("F2")
                    + " ctx=" + anchorContext
                    + " pos=" + pos.x.ToString("F2") + "," + pos.z.ToString("F2"));
        }

        int BuildContext()
        {
            int c = 0;
            if (fetchController != null) c |= fetchController.DebugState & ContextStateMask;
            if (wanderController != null && wanderController.IsPlaying) c |= ContextPlayingBit;
            return c;
        }

        /// <summary>Owner 以外: ずれを測る。
        ///
        /// **IMP-NET-005 でここは「補正器」から「計器」になった。**
        /// 位置は `VRCObjectSync` が配るので寄せる仕事はもう無い —
        /// むしろこの補正 (スナップと滑り寄せ) が、実機 2 人で見えていた
        /// **ワープと滑りそのもの**だった (DES-NET-002)。
        ///
        /// 測るほうは残す。<see cref="_drift"/> は
        /// **新方式で実際どれだけずれているかを実機で読むための唯一の数値**で、
        /// 旧方式の基準測定と同じ物差しで比較できる。
        ///
        /// **照合するのは「自分の犬が止まっているとき」だけ。**
        /// アンカーは「止まった場所」なので、まだ歩いている自分と比べても意味が無い。
        /// この 1 行があるおかげで、**古い標本を時刻で捨てる仕掛けが要らない**。</summary>
        void Evaluate(Vector3 pos, bool still)
        {
            if (!_hasAnchor) return;

            // Fetch 中は測らない (目標がどちらも同期実体なので局面として別物)
            if (fetchController != null && !fetchController.IsIdle) return;
            if (!still) return;

            Vector3 d = anchorPos - pos;
            d.y = 0f;
            _drift = d.magnitude;

            // **1 停止につき 1 回だけ数える。** 止まっている間じゅう加算すると
            // 「fps が高い人ほど悪い数字が出る」計器になり、旧方式との比較に使えない
            if (!_countedThisStop)
            {
                _countedThisStop = true;
                _stops++;
                if (_drift > slideDistance) _overSlide++;
                if (_drift > snapDistance) _overSnap++;
                if (_drift > _driftMax) _driftMax = _drift;

                // **1 停止 = 1 行。** 板に出るのは累計だけなので、
                // 「どの局面で大きくずれたか」(state / play) は板を見ていても残らない。
                // 行にしておけば分布も局面別も後から出せる — セッション中の仕事は
                // 「読んで書き写す」から「あとで数える」へ移る
                if (logToConsole)
                    LogLine("stop key=" + gameObject.name
                        + " n=" + _stops
                        + " drift=" + _drift.ToString("F2")
                        + " max=" + _driftMax.ToString("F2")
                        + " gtSlide=" + _overSlide
                        + " gtSnap=" + _overSnap
                        + " state=" + (anchorContext & ContextStateMask)
                        + " play=" + ((anchorContext & ContextPlayingBit) != 0 ? 1 : 0)
                        + " ageMs=" + (Networking.GetServerTimeInMilliseconds() - anchorServerMs));
            }
            _joinSnapPending = false;
        }

        /// <summary>投げ要求の結果を実機で読める短い語にする (2026-08-23)。
        /// **数字だけだと板を見た瞬間に意味が引けない** ので語にしてある。</summary>
        string ReqTag(int r)
        {
            if (r == KD_DogFetchController.ReqAccepted) return "OK";
            if (r == KD_DogFetchController.ReqWaitingEvidence) return "waiting";
            if (r == KD_DogFetchController.ReqTimedOut) return "NO-THROW";
            if (r == KD_DogFetchController.ReqRejectNotIdle) return "busy";
            if (r == KD_DogFetchController.ReqRejectNotOwner) return "NOT-OWNER";
            if (r == KD_DogFetchController.ReqRejectSleeping) return "SLEEPING";
            return "-";
        }

        /// <summary>**Ownership の軸が揃っているか**を実機で目視するための表示 (IMP-NET-005)。
        ///
        /// 不変条件 8 は「Owner は常に dogRoot の Owner」で、`KD_StopAnchor_*` と
        /// dogRoot の Owner が食い違ったら計器に出す — 隠すのではなく晒す。
        /// 揃っていれば頭文字だけ、割れていたら `SPLIT!` と出す。</summary>
        string OwnerTag()
        {
            if (dogRoot == null) return "?";
            bool same = Networking.IsOwner(gameObject) == Networking.IsOwner(dogRoot.gameObject);
            if (!same) return "SPLIT!";
            VRCPlayerApi o = Networking.GetOwner(dogRoot.gameObject);
            return Utilities.IsValid(o) ? o.displayName : "(none)";
        }

        /// <summary>計器 (§5)。**未配線なら null 判定 1 つで抜ける。**
        ///
        /// 実機 2 人セッションで読む必要があるので、Editor 専用の頭上 HUD
        /// (KD_DogAnimDebugHud 等) ではなく**ワールドに置く TextMeshPro** にした —
        /// 素の MonoBehaviour はアップロードしたワールドでは動かない。
        /// 着脱は `Stop Anchor/4` と `5` が行う (KD_DogPetController のデバッグ表示と同じ作法)。</summary>
        void UpdateDebug(Vector3 pos)
        {
            if ((debugText == null && !logToConsole) || Time.time < _nextDebugTime) return;
            _nextDebugTime = Time.time + DebugInterval;

            bool owner = Networking.IsOwner(gameObject);

            // **Owner が変わった瞬間だけ 1 行残す** (D1〜D3 と不変条件 8)。
            // 板は「今」しか見せないので、目を離した隙に起きた SPLIT! も
            // マスター交代の時刻も残らない。**変化点だけ**なので行数は増えない。
            // 文字列ではなく playerId で比べる — 同名や表示名の揺れで空振りさせない
            if (logToConsole && dogRoot != null)
            {
                bool split = owner != Networking.IsOwner(dogRoot.gameObject);
                VRCPlayerApi po = Networking.GetOwner(dogRoot.gameObject);
                int oid = Utilities.IsValid(po) ? po.playerId : -1;
                if (oid != _lastOwnerId || split != _lastSplit)
                {
                    _lastOwnerId = oid;
                    _lastSplit = split;
                    // 表示名は空白を含みうるので**必ず行末**に置く (パーサが行末まで取る)
                    LogLine("owner key=" + gameObject.name
                        + " id=" + oid
                        + " split=" + (split ? 1 : 0)
                        + " role=" + (owner ? "owner" : "remote")
                        + " name=" + (Utilities.IsValid(po) ? po.displayName : "(none)"));
                }
            }

            // **投げの要求がどう処理されたかを、変化したときだけ 1 行残す** (2026-08-23)。
            // 実機で B2 (非マスターの投げで Fetch が始まらない) が落ちたとき、
            // 原因の切り分けにもう 1 セッション必要だった。**次はその場で読める。**
            // 0 なし / 1 非Owner / 2 非Idle / 3 証拠待ち / 4 猶予切れ (投げていない) / 5 成立
            int req = fetchController != null ? fetchController.DebugLastRequestResult : 0;
            if (logToConsole && req != _lastReqSeen)
            {
                _lastReqSeen = req;
                LogLine("req key=" + gameObject.name + " result=" + req);
            }

            if (debugText == null) return;

            string body;
            if (owner)
            {
                // Owner は「送った側」なので、ずれの測りようが無い (自分が正本)
                // **req はここにこそ出す** — `ReqStartFetch` を処理するのは Owner なので、
                // 非マスターが投げたときに何が起きたかは Owner の板にしか出ない
                body = "OWNER (送信側)\nsent=" + (_sentOnce ? "Y" : "N")
                    + " ctx=" + anchorContext
                    + "\nreq " + ReqTag(req);
            }
            else if (!_hasAnchor)
            {
                body = "no anchor yet";
            }
            else
            {
                int ageMs = Networking.GetServerTimeInMilliseconds() - anchorServerMs;
                // IMP-NET-005: **読みたいのは瞬間値ではなく累計。**
                // 「n 回止まったうち何回 0.5m / 2.0m を超えたか」が、旧方式の基準測定と
                // 突き合わせられる唯一の形 (瞬間の drift は見た瞬間の運で決まる)
                body = "drift " + _drift.ToString("F2") + " m  max " + _driftMax.ToString("F2")
                    + "\n>" + slideDistance.ToString("F1") + "m " + _overSlide
                    + " / >" + snapDistance.ToString("F1") + "m " + _overSnap
                    + " / " + _stops + " stops"
                    + "\nage " + (ageMs / 1000f).ToString("F1") + " s"
                    + "  state " + (anchorContext & ContextStateMask)
                    + ((anchorContext & ContextPlayingBit) != 0 ? " PLAY" : "")
                    + "\n" + (_speed <= stopSpeedThreshold ? "still" : "moving")
                    + "  owner " + OwnerTag();
            }
            debugText.text = body;

            if (debugTextTransform != null)
            {
                debugTextTransform.position = pos + Vector3.up * debugTextHeight;
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (Utilities.IsValid(lp))
                {
                    Vector3 to = debugTextTransform.position - lp.GetPosition();
                    to.y = 0f;
                    if (to.sqrMagnitude > 0.0001f)
                        debugTextTransform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
                }
            }
        }
    }
}
