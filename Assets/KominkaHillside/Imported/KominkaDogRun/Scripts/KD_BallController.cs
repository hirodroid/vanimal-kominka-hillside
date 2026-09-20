using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-FET-001: Fetch のボール (KD_Ball プレハブに載る)。
    ///
    /// 役割は「投げの検出」だけ。投げの検出はローカル (Drop + 1 フレーム後の速度)。
    /// 状態は一切持たない — Fetch の状態は KD_DogFetchController の同期変数が正本。
    ///
    /// IMP-FET-007: Desktop はここで投げを自前で作る。VR 側のコードパスは通らない
    /// (VR は素の Pickup のまま = 不変条件 6「Desktop 対応のために PCVR を落とさない」)。
    ///
    /// 同じ GameObject に VRCObjectSync (Continuous 固定) が居るため NoVariableSync
    /// (Manual を載せると SDK ビルド検証でエラー — vrc-networking §6 WashDog 実測)。
    ///
    /// fetchController はプレハブ内からシーンへの外部参照なので Inspector で配線しない
    /// (実機ビルドで null 化する — vrc-udonsharp §3)。KD_DogFetchController.Start() が
    /// 実行時に配線する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class KD_BallController : UdonSharpBehaviour
    {
        [Tooltip("実行時に KD_DogFetchController.Start() が配線する。Inspector で設定しない")]
        [HideInInspector] public KD_DogFetchController fetchController;
        [HideInInspector] public KD_GroupActivities activities;
        [HideInInspector] public int activityBallIndex;

        [Tooltip("m/s。Drop 後の検出ウィンドウ内の速度がこれ以上なら「投げ」。検出されないときはここを下げる (実測値はコンソールの [KD_Ball] ログに出る)")]
        public float throwSpeedThreshold = 1.2f;

        /// <summary>IMP-FET-015: 壁の外の受け皿 (KD_BallCatch_*) の名前の頭。Editor 側の KD_DogDefs.BallCatchName はこれを参照する。</summary>
        public const string CatchNamePrefix = "KD_BallCatch";
        /// <summary>弱い放し (投げにならなかった Drop) を犬へ知らせるまでの秒数の既定。
        /// 投げ検出の窓 (6 フレーム) が閉じてから数える。持ち直せば取り消し。</summary>
        public const float DefaultDropNoticeDelay = 1f;
        [Tooltip("秒。IMP-FET-015: 投げにならなかった放し (ポンと落とす) を、この秒数後にまだ持たれていなければ「弱い放し」として犬へ知らせる。" +
            "犬は寄ってくわえ、放した人へ差し出す (小さい Fetch)。持ち直せば取り消し。0 で無効")]
        public float dropNoticeDelay = DefaultDropNoticeDelay;

        [Tooltip("Drop 後に速度を見張るフレーム数。ClientSim の投げは AddForce (次の FixedUpdate で反映) のため 1 フレームでは取りこぼす")]
        public int throwCheckFrames = 6;

        [Tooltip("最後にこのボールを投げたプレイヤー (このクライアント視点)。KD_DogFetchController が読む")]
        [HideInInspector] public int lastThrowerPlayerId = -1;
        [HideInInspector] public KD_HintDirector hints; // 実行時にシーン側から接続

        [Header("Desktop の投げ (IMP-FET-007)")]
        [Tooltip("秒。Use を押している時間がこれで最大チャージ")]
        public float desktopMaxChargeSeconds = 1.0f;

        [Tooltip("m/s。チャージ 0 (押してすぐ離す) のときの初速")]
        public float desktopMinThrowSpeed = 3.5f;

        [Tooltip("m/s。チャージ最大のときの初速")]
        public float desktopMaxThrowSpeed = 9.0f;

        [Tooltip("視線方向へ足す上向き成分。0 で水平、大きいほど山なり")]
        public float desktopThrowRise = 0.35f;

        [Header("投げるふり (IMP-FET-005 / 設計書 §10 決定 5)")]
        [Tooltip("m/s。持ったまま振ってこれを超えたら「振り」の候補。投げ検出 (throwSpeedThreshold) " +
            "より高く取る — 低いと歩きながら持っているだけで暴発する")]
        public float feintSwingSpeed = 2.5f;

        [Tooltip("m/s。これを超える移動はリセットボタン等の瞬間移動とみなして捨てる。" +
            "**下限だけの判定は必ずテレポートを拾う** (Codex 指摘)")]
        public float feintMaxSpeed = 14f;

        [Tooltip("秒。振ってからこれだけ待って、まだ持たれていればフェイント成立。" +
            "この間に Drop が来れば本番の投げが勝つ (順序で排他する)。" +
            "**遅れて出るのは演技として正しい** — 犬も一瞬遅れて気づく")]
        public float feintConfirmDelay = 0.3f;

        [Tooltip("秒。フェイント成立からこの間は次を受け付けない (連打で演技が重ならないため)")]
        public float feintCooldown = 2.5f;

        [Tooltip("0〜1。フェイント成立時に犬が吠える確率。**抽選するのは持ち主のクライアントだけ**で、" +
            "結果はイベントの種類で配る (設計書 §10 決定 6)")]
        public float barkChance = 0.35f;

        // ---- フェイントの観測結果 (KD_DogWanderController が読む。同期変数ではない) ----
        [Tooltip("最後にフェイントが成立した時刻 (Time.time)。負 = まだ一度も無い")]
        [HideInInspector] public float feintTime = -1000f;
        [Tooltip("そのとき「飛んだはずの方向」(水平成分主体の単位ベクトル)")]
        [HideInInspector] public Vector3 feintDir = Vector3.forward;
        [Tooltip("その回に吠えるか (持ち主のクライアントが抽選した結果)")]
        [HideInInspector] public bool feintBark;

        Rigidbody _rb;
        VRC_Pickup _pickup;
        VRCObjectSync _sync;
        int _checksRemaining;
        float _maxObservedSpeed;

        bool _isDesktop;
        bool _charging;
        float _chargeStartTime;
        bool _hasPendingThrow;
        Vector3 _pendingThrowVelocity;
        bool _weakDropPending;     // IMP-FET-015: 投げにならなかった Drop の通知待ち (持ち直し・投げで取り消す)
        float _weakDropDue;        // その通知の期限 (Time.time)。古い遅延イベントが新しい放しを早期に確定しないため (Codex 指摘)

        // ---- フェイント (IMP-FET-005) ----
        Vector3 _heldPrevPos;      // 前フレームのボール位置 (持たれている間だけ有効)
        bool _heldPrevValid;
        Vector3 _observedDir;      // このクライアントが観測した最後の振りの向き
        float _observedTime = -1000f;
        bool _swingPending;        // 振りを見つけて confirmDelay を待っている
        float _swingTime;
        Vector3 _swingDir;
        float _lastFeintTime = -1000f;

        /// <summary>今このボールが誰かに持たれているか (同期された保持状態)。</summary>
        public bool IsHeldNow { get { return _pickup != null && _pickup.IsHeld; } }

        /// <summary>持ち主。持たれていなければ null。
        ///
        /// **`IsHeld` で必ずゲートする** — 放置中の Owner は「最後に投げた人」であって
        /// 持ち主ではない。掴んだ直後は移譲が伝播しておらず旧 Owner を指しうるので、
        /// 読み手 (KD_DogWanderController) 側で**安定するまで待つ** (設計書 §10 決定 4)。</summary>
        public VRCPlayerApi HolderPlayer
        {
            get
            {
                if (_pickup == null || !_pickup.IsHeld) return null;
                VRCPlayerApi p = Networking.GetOwner(gameObject);
                return Utilities.IsValid(p) ? p : null;
            }
        }

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            _sync = (VRCObjectSync)GetComponent(typeof(VRCObjectSync));

            VRCPlayerApi lp = Networking.LocalPlayer;
            _isDesktop = Utilities.IsValid(lp) && !lp.IsUserInVR();

            // AutoHold が無いと、Desktop は掴み入力を離した瞬間に ForceDrop
            // (throwHoldDuration = 0) へ落ちて投げの力が一切乗らない。AutoHold を立てると
            // Drop 入力が独立し、Use をチャージに使えるようになる。
            //
            // AutoHold は同期されないローカル設定なので、書き換わるのは Desktop の
            // クライアントだけ。VR 側は値も挙動も変わらない。
            // 掴まれた後に書き換えたときの実機挙動は未検証 → Start() で確定させる
            if (_isDesktop && _pickup != null) _pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
        }

        // ================================================================ IMP-FET-015
        // 壁の外の受け皿。帯 (KD_BallCatch_*、isTrigger) に入ったら**ボールの Owner だけ**が水平速度を殺し、
        // その場に落とす。物理の壁で跳ね返さない (空中で跳ねる絵を出さない。KD_ToyPhysics は
        // bounceCombine=Maximum なので反発 0 の壁でも 0.6 で跳ねる)。落ちた先 (壁の外 3m) の地面は
        // テレインが平らに続いている。犬は壁を通過してそこまで拾いに行く。
        // 非 Owner の Rigidbody は VRCObjectSync が kinematic にしているので、書いても意味が無い上に
        // Owner の位置と食い違う。Stay で毎ステップ殺すのは、帯の中を転がって出ないようにするため。

        void OnTriggerEnter(Collider other) { CatchIfOutside(other); }
        void OnTriggerStay(Collider other) { CatchIfOutside(other); }

        void CatchIfOutside(Collider other)
        {
            if (other == null || _rb == null || _rb.isKinematic) return;
            if (!other.gameObject.name.StartsWith(CatchNamePrefix)) return;
            if (!Networking.IsOwner(gameObject)) return;
            if (_pickup != null && _pickup.IsHeld) return;
            Vector3 v = _rb.velocity;
            if (v.x == 0f && v.z == 0f && v.y <= 0f) return; // 落下中 (縦だけ) は触らない
            v.x = 0f; v.z = 0f;
            if (v.y > 0f) v.y = 0f; // 上向きも殺す (帯の中で上へ抜けない。水平 0 でも見る — Codex 指摘)
            _rb.velocity = v;
            _rb.angularVelocity = Vector3.zero;
        }

        public override void OnPickup()
        {
            _charging = false;
            _hasPendingThrow = false;
            _weakDropPending = false; // 持ち直した = 弱い放しではない (IMP-FET-015)
            _heldPrevValid = false; // 掴む前の位置と比べない (手元へワープする分を振りと読む)
            _swingPending = false;
            // IMP-FET-014: 犬がくわえていたボールを口から受け取った場合、同期された kinematic は
            // Return で犬の Owner が true にしたまま。**このままだと投げても力が乗らず浮く**
            // (kinematic の Pickup には投擲力が乗らない)。Pickup が Ownership を本人へ移しているので、
            // 掴んだ本人がここで dynamic へ戻す。犬側の「Idle 中に kinematic のまま放置されていたら放す」
            // 保険はそのまま (こちらが効かなかったときの復帰手段)
            if (_sync != null)
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (Utilities.IsValid(lp) && !Networking.IsOwner(gameObject))
                    Networking.SetOwner(lp, gameObject); // net: setowner-ok: ボール自身 (掴んだ本人)。犬とは別軸
                _sync.SetKinematic(false);
            }
            // OnPickup は掴んだ本人のクライアントでだけ発火するローカルイベント
            if (fetchController != null) fetchController._OnBallHeldLocal();
        }

        // ================================================================ IMP-FET-005
        // 投げるふりの検出 (設計書 §10 決定 5)。
        //
        // **判定するのは持ち主のクライアントだけ**で、成立を one-shot のネットワーク
        // イベントで全員へ配る。全クライアントが自分でボールを見る方式は捨てた —
        // リモートのボール位置は補間でピークが鈍って「他の人の画面では犬が無反応」になり、
        // Desktop の持ち主が視点を回すと**観測している側で誤爆**する。
        //
        // 位置差分で測る: **持たれている間の `_rb.velocity` は当てにならない**
        // (手が Transform を動かしているだけで、物理は動いていない)。
        // 差分を経過時間で割るので、Update と LateUpdate のどちらで測っても意味は変わらない。

        void Update()
        {
            if (_pickup == null) return;
            bool held = _pickup.IsHeld;
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;

            if (held && _heldPrevValid && dt > 0.0001f)
            {
                Vector3 moved = pos - _heldPrevPos;
                float speed = moved.magnitude / dt;
                // 上限は瞬間移動 (リセットボタン・リスポーン) を捨てるため。
                // **下限だけの判定は必ずテレポートを拾う**
                if (speed >= feintSwingSpeed && speed <= feintMaxSpeed)
                {
                    // 向きは**全クライアントが観測する** — 受信側が「どっちへ」を採るのに使う
                    _observedDir = moved / moved.magnitude;
                    _observedTime = Time.time;

                    // 振りの起点になれるのは持ち主本人の VR クライアントだけ
                    if (!_swingPending && !_isDesktop && Networking.IsOwner(gameObject)
                        && Time.time - _lastFeintTime > feintCooldown)
                    {
                        _swingPending = true;
                        _swingTime = Time.time;
                        _swingDir = _observedDir;
                    }
                }
            }
            _heldPrevPos = pos;
            _heldPrevValid = held;

            if (!_swingPending) return;
            if (Time.time - _swingTime < feintConfirmDelay) return;

            _swingPending = false;
            // **まだ持たれていること**がフェイントの定義。離していたら本番の投げで、
            // そちらは _CheckThrow が既に処理している (順序で排他する)
            if (!_pickup.IsHeld || !Networking.IsOwner(gameObject)) return;

            _lastFeintTime = Time.time;
            _swingDir.y = 0f;
            if (_swingDir.sqrMagnitude > 0.0001f) _observedDir = _swingDir.normalized;

            // 吠えるかどうかは**持ち主のクライアントが抽選し、イベントの種類で配る**
            // (引数を持てないので 1 ビットをイベント名に載せる)
            bool bark = Random.Range(0f, 1f) < barkChance;
            SendCustomNetworkEvent(NetworkEventTarget.All, bark ? "FeintHappenedBark" : "FeintHappened");
        }

        // **`_` を付けていないのは意図的。** VRChat はアンダースコア始まりのメソッドを
        // ネットワークイベントの対象にしない (それがローカル専用にするための規約)。
        // ここは全員へ配る必要があるので付けられない。
        // 悪用されても起きるのは「犬が 1 回吠えて視線を動かす」だけで、状態は動かない。

        /// <summary>フェイントが成立した (全クライアントで実行)。</summary>
        public void FeintHappened() { ApplyFeint(false); } // net: callable-ok: 意図したネットワークイベント (上のコメント参照)

        /// <summary>フェイントが成立し、その回は吠える (全クライアントで実行)。</summary>
        public void FeintHappenedBark() { ApplyFeint(true); } // net: callable-ok: 意図したネットワークイベント (上のコメント参照)

        void ApplyFeint(bool bark)
        {
            feintTime = Time.time;
            feintBark = bark;
            // 「どっちへ」は各クライアントが自分の観測から採る。補間でピークを取り
            // 逃していたら**持ち主の頭の向き**で代用する (アバターの向きは同期済み)
            Vector3 dir = Vector3.zero;
            if (Time.time - _observedTime < 1f) dir = _observedDir;
            if (dir.sqrMagnitude < 0.0001f)
            {
                VRCPlayerApi holder = HolderPlayer;
                if (holder != null)
                {
                    VRCPlayerApi.TrackingData head = holder.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                    dir = head.rotation * Vector3.forward;
                }
            }
            dir.y = 0f;
            feintDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        }

        public override void OnPickupUseDown()
        {
            if (!_isDesktop) return;
            _charging = true;
            _chargeStartTime = Time.time;
        }

        public override void OnPickupUseUp()
        {
            // Drop の途中でも UseUp は飛んでくる (SDK が Drop の前に必ず呼ぶ)。
            // Use を押していないのに投げが成立しないよう _charging で守る
            if (!_isDesktop || !_charging) return;
            _charging = false;

            if (_pickup == null || !_pickup.IsHeld) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;

            // Desktop の頭の回転 = カメラの向き。画面中央へ投げる
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 dir = (head.rotation * Vector3.forward) + (Vector3.up * desktopThrowRise);
            if (dir.sqrMagnitude < 0.0001f) return;

            float charge = Mathf.Clamp01((Time.time - _chargeStartTime) / Mathf.Max(0.01f, desktopMaxChargeSeconds));
            float speed = Mathf.Lerp(desktopMinThrowSpeed, desktopMaxThrowSpeed, charge);

            _pendingThrowVelocity = dir.normalized * speed;
            _hasPendingThrow = true;

            // Drop() → OnDrop → 1 フレーム後に _CheckThrow。速度の代入はそこで行う
            // (クライアントが Drop の中で手の移動ぶんの速度を書くので、それより後で上書きする)
            _pickup.Drop();
        }

        public override void OnDrop()
        {
            // 離した = 本番の投げ。保留中の振りは捨てる (順序で排他する)
            _swingPending = false;
            _heldPrevValid = false;
            // 投げ速度は Drop の瞬間にはまだ乗っていない。ClientSim は AddForce で投げるため
            // 反映は次の FixedUpdate — フレーム間隔 (60fps=16.7ms) より物理ステップ (20ms) が
            // 長く、1 フレーム後の 1 回読みでは取りこぼす。数フレームの窓で見張る
            _checksRemaining = throwCheckFrames;
            _maxObservedSpeed = 0f;
            _weakDropPending = false;
            SendCustomEventDelayedFrames("_CheckThrow", 1);
        }

        /// <summary>IMP-FET-015: 投げにならなかった Drop (弱い放し)。窓が閉じてから dropNoticeDelay 後に
        /// まだ持たれていなければ、放した本人のクライアントから犬へ知らせる (投げと同じ Owner 権威の型)。
        /// かけっこ中 (activities Mode 2) は投擲だけが対象なので何もしない。</summary>
        public void _NoticeWeakDrop()
        {
            if (!_weakDropPending) return;
            // 放す → 持ち直す → また放す、で最初の遅延イベントが新しい放しを早期に確定しないよう、期限前なら何もしない
            // (新しい放しの分の遅延イベントが後から来る)
            if (Time.time < _weakDropDue - .02f) return;
            _weakDropPending = false;
            if (fetchController == null || _pickup == null || _pickup.IsHeld) return;
            // 放した後に別の人が掴んで投げると Ownership はその人へ移る。自分の放しはもう合図ではない
            if (!Networking.IsOwner(gameObject)) return;
            if (activities != null && activities.Mode == 2) return;
            fetchController._OnBallDropped();
        }

        public void _CheckThrow()
        {
            if (fetchController == null || _rb == null) return;
            // 検出ウィンドウ内に再取得されたら投げではない
            // (残存速度で誤判定して「手に持ったボールを犬が追う」事故になる — Codex 指摘)
            if (_pickup != null && _pickup.IsHeld) { _hasPendingThrow = false; return; }

            if (_hasPendingThrow)
            {
                // IMP-FET-007: Desktop の自前の初速。Drop 内の速度書き込みより後に上書きする
                _rb.velocity = _pendingThrowVelocity;
                _hasPendingThrow = false;
            }

            float speed = _rb.velocity.magnitude;
            if (speed > _maxObservedSpeed) _maxObservedSpeed = speed;

            if (speed >= throwSpeedThreshold)
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (!Utilities.IsValid(lp)) return;

                // OnDrop はローカルイベントなので、投げたのは必ずこのクライアントの本人
                lastThrowerPlayerId = lp.playerId;
                if (hints != null) hints._OnLocalThrow();
                Debug.Log("[KD_Ball] throw detected v=" + speed.ToString("F2"));
                if (activities != null && activities.Mode == 2) activities._BallThrown(activityBallIndex);
                else fetchController._OnBallThrown();
                return;
            }

            _checksRemaining--;
            if (_checksRemaining > 0)
            {
                SendCustomEventDelayedFrames("_CheckThrow", 1);
                return;
            }
            // 閾値調整用: 投げたつもりで無反応のときはこの実測値を見て threshold を下げる
            Debug.Log("[KD_Ball] drop ignored (max v=" + _maxObservedSpeed.ToString("F2") +
                " < threshold " + throwSpeedThreshold + ")");
            // IMP-FET-015: 投げではない放し → 少し待ってから犬へ (持ち直しの猶予)
            if (dropNoticeDelay > 0f)
            {
                _weakDropPending = true; _weakDropDue = Time.time + dropNoticeDelay;
                SendCustomEventDelayedSeconds("_NoticeWeakDrop", dropNoticeDelay);
            }
        }
    }
}
