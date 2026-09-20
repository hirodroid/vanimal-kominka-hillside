using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-TRT-001 Phase 4b: **枠 1 つぶんの状態**。設計の正本は
    /// [IMP-TRT-001](../../../docs/development/issues/IMP-TRT-001-treat-system.md)
    /// の「Phase 4b の確定設計」。
    ///
    /// 一文で: **誰が / 何を / いつ出したかを持つ。それだけ。**
    ///
    /// ## 3 つとも導出しない
    ///
    /// <code>
    /// giverId   出した人の playerId (-1 = Free)  ← **GetOwner から導出しない**
    /// product   おやつの種類 = 恒久 ID (20..23)   ← **KD_PlayerFxTag から導出しない**
    /// outMs     出した時刻 (サーバー ms)          ← **位置から導出しない**
    /// </code>
    ///
    /// 導出案が 3 つとも壊れる理由は Issue の表にある。要点だけ言えば、
    /// Owner は「現在の所有者」であって「出した人」ではなく (Pickup / Respawn 用の移譲 /
    /// 退室で必ず意味が変わる)、札は上書きされ、位置は予約中と物理ドリフトを区別できない。
    ///
    /// ## 書けるのはその Owner だけ
    ///
    /// **すべての書き込みが `Networking.IsOwner(gameObject)` の下にある。**
    /// これは単一書き手の規則で、**退室時の自動移譲がそのまま責任の引き継ぎになる** —
    /// giver が居なくなったらマスターが Owner になり、下の突き合わせが片付ける。
    ///
    /// ## `outMs` が世代番号を兼ねる
    ///
    /// 枠は再利用される。犬が枠番号だけを覚えていると、**食べ終わった枠に別の人が
    /// 次のおやつを出した瞬間、古い Claim がそれへ黙って継承される。**
    /// だから犬は `(枠, outMs)` の対で掴む (<see cref="KD_TreatBox.ResolveClaimsWith"/>)。
    /// 同時に `outMs` の昇順が「先に出された順」= 先着順の入力にもなる。
    ///
    /// ## `Returning` だけ同期しない — 理由を書いておく
    ///
    /// 状態表の 5 つのうち `Free` / `Reserved` / `Out` は上の 3 int から一意に決まるが、
    /// `Returning` (Respawn の再試行中) は決まらない。**同期変数を 4 本目に増やさなかった。**
    ///
    /// 増やさなくてよいのは、**`Free` と `Returning` が他人から見て完全に同じ振る舞い**
    /// だから — どちらも Claim 対象外・掴めない・メッシュは全部 false。
    /// 違うのは「Owner がまだ再試行を続けるか」だけで、それは Owner のローカルな話。
    /// 他人に伝える必要が無い値を同期変数にすると、**帯域ではなく整合の面が増える。**
    /// </summary>
    /// <remarks>
    /// SyncMode Manual・**専用 GameObject** (U# は同一 GameObject で同期方式を混在できない)。
    /// 同期は 3 int × 3 枠 = 9 int。
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KD_TreatSlot : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/Treat System が設定する)")]
        [Tooltip("枠番号 (0..2)。KD_TreatBox.slots の添字と一致していること")]
        public int slotIndex;

        [Tooltip("この枠の実体 (KD_Treat_{i})。位置と保持だけを持つ")]
        public KD_TreatController body;

        [Tooltip("ルールの正本。**食べ終わったか**をここへ問い合わせる。" +
            "null でも枠は壊れない (放置タイムアウトだけで回る)")]
        public KD_TreatBox box;

        [Tooltip("手元メニュー (KD_TreatPouch)。**移譲が済んだことを知らせる先。**\n" +
            "Ownership の移譲イベントは**移った先のオブジェクト**でしか発火しないので、" +
            "出す側 (ポーチ) は自分では受け取れない。" +
            "null でも壊れない — ポーチ側は IsOwner のポーリングでも進む (通知は早い経路)")]
        public KD_TreatPouch pouch;

        [Header("調整")]
        [Tooltip("秒。予約したまま実体を出せない状態がこれだけ続いたら枠を返す。" +
            "**手元メニューの手順 (Step 5) が落ちても枠が永久に埋まらないための床**")]
        public float reserveTimeout = DefaultReserveTimeout;

        [Tooltip("秒。**置かれたまま**誰も食べに来ないとこれだけで回収する。\n" +
            "**手にある間は数えない** (吸着中 / 握られている間) — " +
            "持ち歩いて犬を探している時間を「放置」と呼ばないため (IMP-TRT-003)")]
        public float abandonTimeout = DefaultAbandonTimeout;

        [Tooltip("秒。**出してからの上限。** 手に持ったままでもこれを過ぎたら回収する。\n" +
            "枠は 3 つしかない共有資源なので、上限そのものは外さない " +
            "(2026-09-03 ユーザー決定: 持っている間は延長する)")]
        public float holdAbandonTimeout = DefaultHoldAbandonTimeout;

        const float DefaultReserveTimeout = 20f;
        const float DefaultAbandonTimeout = 60f;
        const float DefaultHoldAbandonTimeout = 180f;

        /// <summary>突き合わせの間隔 (秒)。毎フレーム回さない (Udon VM)。</summary>
        const float TickInterval = 0.25f;

        // ---- 同期 3 本。**これが枠の正本** ----
        [UdonSynced] private int giverId = -1;
        [UdonSynced] private string product = "";
        [UdonSynced] private int outMs;

        /// <summary>Respawn が効くまでの再試行中か (**Owner のローカル状態**。上の注記を参照)。</summary>
        private bool _returning;

        /// <summary>予約に入った時刻 (Owner のローカル時計)。移譲で引き継がれないので、
        /// Owner になった側が観測した時点から測り直す — **タイムアウトが目的なので
        /// 早すぎるより遅すぎる方が安全**。</summary>
        private float _reservedSince = -1f;

        /// <summary>実体が**手を離れた**時刻 (Owner のローカル時計。-1 = まだ手にある)。
        ///
        /// **置かれてからの時間で測るため**に要る (出してからの時間ではない) —
        /// 持ち歩いた末に置いたおやつが、置いた瞬間に消えないようにする。
        /// `_reservedSince` と同じくローカル時計なので、**マスターへ移った直後の 1 回だけ
        /// 0 から数え直す** (最大でもう一度 `abandonTimeout` ぶん延びる)。
        /// giver の退室は `GiverStillHere()` が即座に畳むので、この穴が効くのは
        /// マスターが交代した場合だけ。</summary>
        private float _groundSince = -1f;

        /// <summary>いつ Owner になったか。**移譲直後の予約を止めるのに使う** —
        /// 同期前の Free を見て 2 人が同時に予約する隙を閉じる
        /// (2026-08-24 Codex 指摘。詳細は <see cref="_TryReserve"/>)。</summary>
        private float _ownedSince = -1000f;

        /// <summary>Owner になってから予約してよくなるまで (秒)。
        /// **1 往復ぶん**。長くすると「出す」が鈍くなり、短くすると隙が残る。
        /// ポーチは <see cref="ReserveSettling"/> が true の間、遅延再試行で待つ
        /// (破壊的監査 v2 RC-2 — 以前このコメントは「0.2s おきに再試行する」と
        /// 書いていたが実装が存在せず、settle がそのまま初回失敗になっていた)。</summary>
        const float ReserveSettle = 0.3f;

        private float _nextTick;

        // ============================================================
        // 読み取り (誰が読んでもよい。状態は動かない)
        // ============================================================

        /// <summary>枠の状態 (<c>KD_TreatBox.Slot*</c>)。**定数の正本は
        /// <see cref="KD_TreatBox"/> で、ここでは作り直さない。**</summary>
        // net: callable-ok: 読み取りのみ — 遠隔から呼ばれても状態が動かない
        public int State()
        {
            if (giverId < 0) return _returning ? KD_TreatBox.SlotReturning : KD_TreatBox.SlotFree;
            if (outMs == 0) return KD_TreatBox.SlotReserved;
            return KD_TreatBox.SlotOut;
        }

        /// <summary>出した人の playerId。-1 = 誰も使っていない。
        /// **`pickupable` はこれと自分の playerId の一致から導出する** (横取り防止の主役)。</summary>
        // net: callable-ok: 読み取りのみ
        public int GiverId() { return giverId; }

        /// <summary>出ているおやつの恒久 ID (20..23)。-1 = 無し。</summary>
        // net: callable-ok: 読み取りのみ
        public string Product() { return product; }

        /// <summary>出した時刻 (サーバー ms)。**0 = まだ確定していない (Reserved)。**
        /// 世代番号を兼ねる。</summary>
        // net: callable-ok: 読み取りのみ
        public int OutMs() { return outMs; }

        /// <summary>実体の現在位置 (VRCObjectSync が運ぶ同期実体 —
        /// 犬の移動目標に使ってよい。BUG-NET-001 の教訓)。</summary>
        // net: callable-ok: 読み取りのみ
        public Vector3 BodyPosition()
        {
            return body != null ? body.transform.position : transform.position;
        }

        /// <summary>実体が今**誰かに持たれている**か (`VRC_Pickup.IsHeld` は同期される)。
        ///
        /// **持ち主が誰かは返さない。** 「出した人」の正本は <see cref="GiverId"/> だけで、
        /// Owner から導出すると Pickup / Respawn / 退室の移譲で意味が変わる
        /// (Issue「導出で済ませる案は 3 つとも成立しない」の 1 行目)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool BodyHeld()
        {
            return body != null && body.IsHeldNow;
        }

        /// <summary>予約が **ReserveSettle 待ちで一時的に**断られる状態か。
        ///
        /// 破壊的監査 v2 RC-2: 移譲成立の通知は `OnOwnershipTransferred` と
        /// **同一フレーム**でポーチへ届くため、`_TryReserve` は必ず settle 未経過で
        /// false を返す。呼ぶ側はこれを「負けた」と読み違えて手順ごと畳んでいた
        /// (= 非マスターの初回「出す」がほぼ確実に失敗)。
        /// **一時的な false (少し待てば通る) と恒久的な false (枠を取られた) を
        /// 区別する口**がこれ。ポーチは true の間だけ遅延再試行する。</summary>
        // net: callable-ok: 読み取りのみ — 遠隔から呼ばれても状態が動かない
        public bool ReserveSettling()
        {
            return Networking.IsOwner(gameObject)
                && State() == KD_TreatBox.SlotFree
                && Time.time - _ownedSince < ReserveSettle;
        }

        // ============================================================
        // 書き込み (**すべて Owner ゲートの下**)
        // ============================================================

        /// <summary>枠を予約する。**まだ消費していないので犬は来ない** (`outMs == 0`)。
        /// Owner でなければ何もしないで false — 呼ぶ側が移譲を待ってから叩く。</summary>
        public bool _TryReserve(string wantProduct)
        {
            if (!Networking.IsOwner(gameObject)) return false;
            // Returning (再試行中) も断る。**実体が定位置へ戻る前に次を出すと、
            // 出した瞬間に Respawn が走って手から消える**
            if (State() != KD_TreatBox.SlotFree) return false;
            // **Owner になった直後は予約しない** (2026-08-24 Codex 指摘)。
            //
            // 2 人が**同期前の同じ Free 枠**を選び、A→B の順に Ownership を取ると、
            // B のローカルにはまだ A の予約が届いていないので `State()` は Free に見える。
            // → **両方が予約に成功し、2 人分の在庫が減って最後の書き込みだけが残る。**
            //
            // `OnOwnershipRequest` (下) が「占有中なら他人へ渡さない」を守るが、
            // **どちらもまだ予約していない瞬間に要求が重なると拒否できない** —
            // その隙をここで閉じる。移譲が済んでから 1 往復ぶん待てば、
            // 先に予約した側の値が届いて `State()` が Free でなくなる。
            // **待っている間に負けたと分かる**ので、在庫は 1 も減らない
            if (Time.time - _ownedSince < ReserveSettle) return false;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return false;

            if (pouch == null || pouch.catalog == null || body == null) return false;
            int itemIndex = pouch.catalog.IndexOfProduct(wantProduct);
            int meshIndex = body.IndexOfProduct(wantProduct);
            if (itemIndex < 0 || meshIndex < 0 || body.meshNodes == null || meshIndex >= body.meshNodes.Length || body.meshNodes[meshIndex] == null) return false;
            var item = pouch.catalog.items[itemIndex];
            if (item.categoryKey != "treat" || !item.IsStocked() || !item.Owned() || item.Stock() <= 0) return false;

            giverId = lp.playerId;
            product = wantProduct;
            outMs = 0;
            _reservedSince = Time.time;
            RequestSerialization();
            return true;
        }

        /// <summary>消費が確定した。**ここで初めて犬が来られる** (`Reserved` → `Out`)。
        ///
        /// **`outMs` が 0 にならないことを保証する** — 0 は「まだ確定していない」の意味に
        /// 使っているので、サーバー時刻がちょうど 0 のときに枠が永久に Reserved のまま
        /// 固まる (`KD_TreatBox._ReportArrived` の食べ始めも同じ処置)。</summary>
        public bool _CommitOut()
        {
            if (!Networking.IsOwner(gameObject)) return false;
            if (giverId < 0) return false;
            if (outMs != 0) return true;   // 冪等 — 2 度目は何もしない

            int ms = Networking.GetServerTimeInMilliseconds();
            outMs = ms == 0 ? 1 : ms;
            RequestSerialization();
            return true;
        }

        /// <summary>**回収の唯一の口。** 手元メニュー (Step 5) も犬 (Step 6) もここを呼ぶ。
        ///
        /// 実行者は**そのとき枠を Owner として持っている人**で、それは
        /// 通常 giver・giver 退室後はマスター (VRChat の自動移譲がそのまま責任を運ぶ)。</summary>
        public void _Release()
        {
            if (!Networking.IsOwner(gameObject)) return;
            giverId = -1;
            product = "";
            outMs = 0;
            _reservedSince = -1f;
            RequestSerialization();

            // **同期変数を先に落としてから実体を戻す。** 逆順にすると、
            // Respawn で実体が定位置へ着いた一瞬だけ「Out なのに皿の上」が見える
            _returning = true;
            RespawnBody();
        }

        // ============================================================
        // 突き合わせ (Owner だけ。0.25s おき)
        // ============================================================

        private void Update()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + TickInterval;

            if (!Networking.IsOwner(gameObject))
            {
                // 移譲で Owner が変わったときに古い計測を持ち越さない
                _reservedSince = -1f;
                _groundSince = -1f;
                return;
            }

            int st = State();

            if (st == KD_TreatBox.SlotFree || st == KD_TreatBox.SlotReturning)
            {
                // Respawn が効くまで再試行する (Phase 1 の _respawnPending と同じ作法)
                if (!_returning) return;
                if (body == null || body.AtHome()) _returning = false;
                else RespawnBody();
                return;
            }

            if (st == KD_TreatBox.SlotReserved)
            {
                if (_reservedSince < 0f) _reservedSince = Time.time;
                if (Time.time - _reservedSince > reserveTimeout) _Release();
                return;
            }

            // ---- Out ----
            _reservedSince = -1f;

            // **giver が退室した** → 移譲でここ (マスター) が Owner になっている
            if (!GiverStillHere()) { _Release(); return; }

            // 食べ終わった
            if (box != null && box.EatFinishedFor(slotIndex, outMs)) { _Release(); return; }

            // ---- 放置の回収 (2026-09-03 に 2 本立てへ。IMP-TRT-003) ----
            //
            // 旧実装は `outMs` からの経過だけで測っていたので、**手に持ったままの時間も
            // 放置に数えていた** — あげるトリガーを自分で引く形にすると、
            // 「犬を探して歩いている間に手から消える」になる。
            //
            //   置かれている間だけ  abandonTimeout      (60s)   ← 犬が来ない / 届かない所に落ちた
            //   出してからの上限    holdAbandonTimeout  (180s)  ← 持ったまま忘れられた枠を返す
            //
            // 上限を残すのは**枠が 3 つしかない共有資源**だから。
            if (box == null || !box.EatingNowFor(slotIndex, outMs))
            {
                bool inHand = body != null && body.InHandNow();
                if (inHand) _groundSince = -1f;
                else if (_groundSince < 0f) _groundSince = Time.time;

                if (!inHand && Time.time - _groundSince > abandonTimeout) { _Release(); return; }

                int elapsed = Networking.GetServerTimeInMilliseconds() - outMs;
                if (elapsed > (int)(holdAbandonTimeout * 1000f)) _Release();
            }
        }

        private void Start()
        {
            // 0 で入ると無言で壊れる値 (破壊的監査 RC-1 の 3 規則):
            //   reserveTimeout 0      → 予約した瞬間に枠が返る (何も出せない)
            //   abandonTimeout 0      → 出した瞬間に回収される
            //   holdAbandonTimeout 0  → **手に持った瞬間に消える** (既存シーンでは 0 で入る)
            if (reserveTimeout <= 1f) reserveTimeout = DefaultReserveTimeout;
            if (abandonTimeout <= 1f) abandonTimeout = DefaultAbandonTimeout;
            // 上限が「置いてからの猶予」より短いと、上限のほうが先に効いて
            // **持っていても持っていなくても同じ**になる (2 本立てにした意味が消える)
            if (holdAbandonTimeout <= abandonTimeout)
                holdAbandonTimeout = Mathf.Max(DefaultHoldAbandonTimeout, abandonTimeout * 3f);

            if (body == null)
                Debug.LogError("[KD_TreatSlot] body 未配線 (slot " + slotIndex + ") - "
                    + "枠は出せない。Run Tools/KominkaDogRun/Treat System setup.");
        }

        /// <summary>この枠の Owner が変わった。**出す側へ知らせるためだけに要る。**
        ///
        /// 出す側 (<see cref="KD_TreatPouch"/>) は別 GameObject なので、この枠の
        /// 移譲イベントを自分では受け取れない。**「移譲を待ってから書く」を
        /// 成立させているのがこの 1 本**で、待たずに書いた値は捨てられうる。</summary>
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;
            // **いつ Owner になったかを覚える** — 直後の予約を止めるのに使う
            // (`_TryReserve` の `ReserveSettle`)。同期前の Free を見て
            // 2 人が同時に予約するのを防ぐ
            _ownedSince = Time.time;

            // **`_returning` の推定復元** (破壊的監査 v2 RC-9a)。
            // `_returning` はローカルで移譲に乗らない。旧 Owner が Respawn 再試行の
            // 途中で居なくなると「枠は Free・実体は出しっぱなし」が残り、
            // 次の予約でその実体が部屋を横切って手へ飛んでくる。
            // 引き継いだ側は「Free なのに実体が家に居ない」から回収中を推定して再開する
            // (State() が Returning になるので、戻り切るまで予約も入らない)
            if (giverId < 0 && body != null && !body.AtHome()) _returning = true;

            if (pouch != null) pouch._OnSlotOwnerChanged();
        }

        /// <summary>
        /// **占有中の枠を他人へ渡さない。** 2026-08-24 Codex 指摘で追加。
        ///
        /// ## 横取り防止の主役はこちら。`pickupable` ではない
        ///
        /// 以前は「横取り防止の主役は `pickupable`、`OnOwnershipRequest` は二重の保険」
        /// と整理していたが、**守る対象が 2 つあるのに片方の防具しか見ていなかった**:
        ///
        /// <code>
        /// 実体 (KD_Treat_i)  掴まれること   ← pickupable が守る
        /// 枠   (KD_TreatSlot) 予約されること ← **これが守る**
        /// </code>
        ///
        /// `pickupable` は実体を掴めなくするだけで、**枠の Ownership 要求は素通し**だった。
        /// 素通しだと、他人が枠を取って `_TryReserve` に成功し、
        /// **giver がすり替わって「誰への返事か」まで壊れる。**
        ///
        /// **ここで拒否できるのは自分が今 Owner のときだけ** (要求は現 Owner が判断する)。
        /// 誰も予約していない瞬間に要求が重なる隙は塞げないので、
        /// そこは `_TryReserve` の `ReserveSettle` が受け持つ。**2 段で守る。**
        /// </summary>
        public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner)
        {
            // 空いている枠は誰が取ってもよい (それが「先着で貸し出す」ということ)
            if (giverId < 0) return true;
            // 占有中は**出した本人にだけ**返す (拾い直し・回収のため)
            if (newOwner != null && newOwner.playerId == giverId) return true;
            // **出した人が居なくなっていたら誰かに片付けてもらう必要がある** —
            // ここで拒み続けると、退室した人の枠が永久に塞がる
            if (!GiverStillHere()) return true;
            return false;
        }

        private bool GiverStillHere()
        {
            if (giverId < 0) return false;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(giverId);
            return Utilities.IsValid(p);
        }

        /// <summary>
        /// 実体を定位置へ戻す。**戻せたときだけ `Returning` を畳む。**
        ///
        /// ## 移譲は非同期なので、1 回目は効かない前提で書く
        ///
        /// 旧版は「**Ownership を取ってから呼ぶ** (移譲確認は待たない)」と書いて
        /// `SetOwner()` の直後に呼んでいたが、**移譲が済む前の呼び出しは非 Owner の
        /// 書き込みなので効かない。** それを「戻した」と扱うと `_returning` が畳まれ、
        /// **枠が塞がったまま誰も片付けなくなる。**
        ///
        /// → `_RespawnHome()` が**自分で `IsOwner` を見て bool を返す**ようにした
        /// (2026-08-24 Codex 指摘)。ここは Owner でなければ**移譲を要求して帰る** —
        /// 効かない書き込みを投げない。
        ///
        /// **`Returning` を畳むのはここではない。** 畳むのは上の突き合わせで
        /// **実体が実際に定位置へ着いたことを確認したとき** (`body.AtHome()`)。
        /// 「Respawn を呼べた」より「着いた」のほうが強い確認なので、そちらに任せる
        /// (Fetch の「Idle 中の実態突き合わせ」と同じ作法)。
        /// </summary>
        private void RespawnBody()
        {
            if (body == null) { _returning = false; return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (!Networking.IsOwner(body.gameObject))
            {
                Networking.SetOwner(lp, body.gameObject); // net: setowner-ok: おやつの実体。犬ではないので Owner 固定の対象外
                return;   // **移譲を待つ。Returning は維持したまま次の tick で再試行**
            }
            body._RespawnHome();
        }
    }
}
