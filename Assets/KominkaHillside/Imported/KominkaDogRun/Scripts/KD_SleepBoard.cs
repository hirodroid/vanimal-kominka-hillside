using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-DOG-019 Phase 3a/3b: **そばで寝るモードの台帳**。設計の正本は
    /// [IMP-DOG-019](../../../docs/development/issues/IMP-DOG-019-sleep-beside-mode.md) §3。
    ///
    /// 一文で: **枠 4 つへの窓口と、開放フラグ 1 本と、誰が権限を持つかの判定。**
    ///
    /// <code>
    /// KD_SleepBoard          ← ここ。開放フラグ (bool) だけを持つ
    ///  └ KD_SleepSlot_{key}  犬 4 匹ぶん。sleeperId / calledMs
    /// </code>
    ///
    /// ## 読み手はここだけを見る
    ///
    /// 犬側 (Phase 2 の `KD_DogWanderController`) は <see cref="LiveSleeperOf"/> を
    /// 毎フレーム引いて、自分が指名されていれば割り込む。
    /// **`treatBox.LiveSlotOf(dogIndex)` とまったく同じ形** — 枠の実体を直接持たせず
    /// 台帳を 1 枚かませるのは、null / 範囲外を**読み手ごとに書かせない**ため。
    ///
    /// ## 権限もここに 1 箇所だけ置く (Phase 3b → IMP-NET-007 で部屋全体の正本)
    ///
    /// <see cref="CanControl"/> / <see cref="CanCall"/> が唯一の正本で、
    /// 枠 (`KD_SleepSlot.OnOwnershipRequest` / `_TryCall` / `_Release`) も
    /// メニュー (Phase 5) も、**ライブの開催許可 (`KD_LiveAccess`) と共有開催の終了
    /// (`KD_GroupActivities` / `KD_LiveSystem._StopFor`) も**ここへ問い合わせる
    /// ([インスタンスの管理権限](../../../docs/development/design/instance-permissions.md))。
    /// **判定の式を写した場所を作らない** — 写すと、オーナーの居ないインスタンスでの
    /// 挙動が場所ごとに割れる。参照が欠けた機能は Master 判定で埋めず「操作不可」に倒し、
    /// 欠落は Setup / Verify で数える。
    ///
    /// ## 台帳の Ownership は「切り替えたときだけ動く」(**マスター固定ではない**)
    ///
    /// **この GameObject の `SetOwner` を呼ぶのは開放フラグを書くときだけ。**
    /// 枠も BUG-NET-002 以降は Owner 固定で要求を調停し、台帳も**普段の操作では取らない** —
    /// 動くのは「権限者が開放を切り替えた」ときの 1 回だけで、
    /// そこ以外で Owner が移ると**開放フラグの書き手が増える**。
    ///
    /// `KD_TreatBox` のような**マスター固定ではない** (Codex 2026-09-04 指摘 4)。
    /// 権限者が非 Owner のまま切り替えたら台帳の Owner はその人へ移り、**戻さない** —
    /// 書き手は常に 1 人 (<see cref="CanControl"/> が true の人) なので戻す理由が無く、
    /// 戻すと `SetOwner` が 1 操作あたり 2 回に増えて競合の窓が広がる。
    ///
    /// **犬の Ownership は絶対に取らない** (IMP-NET-005 の不変条件 3)。
    /// </summary>
    /// <remarks>
    /// SyncMode Manual・**専用 GameObject** (U# は同一 GameObject で同期方式を混在できない)。
    /// 同期は bool 1 本。
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KD_SleepBoard : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/Sleep System が設定する)")]
        [Tooltip("犬 1 匹につき 1 つの寝る枠。**添字 = dogIndex** " +
            "(KD_DogPack.dogRoots と同じ並び)")]
        public KD_SleepSlot[] slots;

        [Tooltip("犬の名簿。**枠の数がここと一致していること**を Setup / Verify が数える")]
        public KD_DogPack pack;

        /// <summary>**開放フラグ。** オフならオーナー以外は呼べない (Issue §1 決定 2)。
        /// **既定は ON** (2026-09-22 ユーザー決定) — 部屋は開いた状態で始め、絞りたいときだけ管理者が設定ホームで切る。
        /// 同期はこの 1 本だけ — 誰が占有しているかは枠が持つ。</summary>
        [UdonSynced] private bool openToAll = true;

        /// <summary>Owner になってから書いてよくなるまでの打ち切り (秒)。
        /// <see cref="KD_SleepSlot"/> と同じ理屈 (`SetOwner` 直後の書き込みは捨てられうる)。</summary>
        const float PendingTimeoutSeconds = 1.0f;

        private bool _pendingValue;
        private bool _hasPending;

        /// <summary>保留の世代 (<see cref="KD_SleepSlot"/> と同じ形)。
        /// 撒いた <see cref="_PendingTimeout"/> は取り消せないので、
        /// **最後に撒いたぶんの着弾でだけ**保留を畳む。番号が無いと、
        /// 保留中にもう一度トグルしたとき **1 発目の打ち切りが新しい保留を捨てる。**</summary>
        private int _pendingGen;

        private int _timeoutGen;

        // ============================================================
        // 権限判定のキャッシュ (Phase 3b)
        //
        // **毎フレーム全員を走査しない。** CanControl はメニュー (Phase 5) が
        // 毎フレーム引く問い合わせなので、素直に書くと「フレーム × 人数」の走査が
        // Udon VM で回る (徘徊の近接判定を 0.25s へ間引いたのと同じ理由 —
        // KD_DogWanderController.NearestPlayerSqrDistance)。
        //
        // 走査で欲しいのは **「インスタンスオーナーが 1 人でも居るか」の 1 bit だけ**で、
        // これは人が出入りしたときにしか変わらない。だから
        //   1. OnPlayerJoined / OnPlayerLeft でキャッシュを無効化し
        //   2. それでも取りこぼしたときのために OwnerScanInterval で必ず測り直す
        // の 2 段にする。2 があるので、1 が VRChat 側の順序で外れても
        // **最大 0.5 秒で自己修復する** (OnPlayerLeft の時点では GetPlayers に
        // 去った人がまだ載っていることがある)。
        // ============================================================

        /// <summary>オーナー在席の測り直し間隔 (秒)。</summary>
        const float OwnerScanInterval = 0.5f;

        /// <summary>走査する人数の上限 (固定長。VRChat のインスタンス上限に合わせる)。
        /// `KD_DogWanderController.MaxPlayers` と同じ値。</summary>
        const int MaxPlayersScanned = 90;

        private VRCPlayerApi[] _players = new VRCPlayerApi[MaxPlayersScanned];

        /// <summary>**インスタンスオーナーが 1 人でも居るか** (Issue §6 危険 1)。
        /// これが false のときだけ Master へフォールバックする。</summary>
        private bool _ownerPresent;

        /// <summary>次に走査してよくなる時刻。-1 = 今すぐ測り直す (無効化の印)。</summary>
        private float _nextOwnerScan = -1f;

        // ============================================================
        // 読み取り (副作用なし。**全クライアントが同じ結論を出す**)
        // ============================================================

        /// <summary>オーナー以外も呼べる状態か。</summary>
        public bool IsOpenToAll { get { return openToAll; } }

        /// <summary>犬 <paramref name="dogIndex"/> の枠。null = 配線が欠けている / 範囲外。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public KD_SleepSlot SlotOf(int dogIndex)
        {
            if (slots == null || dogIndex < 0 || dogIndex >= slots.Length) return null;
            return slots[dogIndex];
        }

        /// <summary>犬 <paramref name="dogIndex"/> を**今まさに占有している人**の playerId。
        /// -1 = 誰も呼んでいない (呼んだ人が退室済みの場合も -1)。
        ///
        /// **犬側 (Phase 2) が毎フレーム引く唯一の口。** `treatBox.LiveSlotOf` と同型。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public int LiveSleeperOf(int dogIndex)
        {
            KD_SleepSlot s = SlotOf(dogIndex);
            if (s == null) return -1;
            return s.LiveSleeperId;
        }

        /// <summary>犬 <paramref name="dogIndex"/> が寝る指名を受けているか。
        ///
        /// **おもちゃタブの「呼ぶ」の条件に `∧ !IsSleeping` を足すのを忘れないこと**
        /// (Issue §6 危険 4 — 足さないと**押しても犬が来ないボタン**になる。
        /// 寝ている犬は `fetch.IsIdle` が真のままなので、Idle 判定では弾けない)。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public bool IsSleeping(int dogIndex)
        {
            return LiveSleeperOf(dogIndex) >= 0;
        }

        /// <summary>**この機能の権限を握っている人か** (Issue §1 決定 2 / §6 危険 1)。
        ///
        /// <code>
        /// インスタンスオーナーが 1 人でも居る → その人 (isInstanceOwner) だけ true
        /// 誰も居ない                          → Master (isMaster) だけ true
        /// </code>
        ///
        /// **`isInstanceOwner` は作成者が居ないインスタンスでは誰も true にならない**
        /// (公開インスタンスへ飛び込んだだけ / 作成者が退出済み)。単に
        /// `p.isInstanceOwner || p.isMaster` と書くと**作成者と Master の 2 人が
        /// 同時に権限を持つ**ので、フォールバックは「オーナー不在のときだけ」に閉じる —
        /// ユーザーの意図は「場を立てた人の権限であって、たまたま最古参の人の権限ではない」
        /// (DES-DOG-003 §2.1 ①)。
        ///
        /// 「オーナーが居るか」は全員走査でしか分からないので**キャッシュしている** (上の注記)。
        /// 走査の副作用はキャッシュだけで、**返す結論は全クライアントで同じ**。
        ///
        /// Udon 公開の確認 (2026-09-04。ノード定義レジストリ + 対照群):
        /// `VRCSDKBaseVRCPlayerApi.__get_isInstanceOwner__SystemBoolean` /
        /// `__get_isMaster__SystemBoolean` /
        /// `__GetPlayers__VRCSDKBaseVRCPlayerApiArray__VRCSDKBaseVRCPlayerApiArray` /
        /// `__GetPlayerCount__SystemInt32` はいずれも解決する。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public bool CanControl(VRCPlayerApi p)
        {
            if (!Utilities.IsValid(p)) return false;
            EnsureOwnerScan();
            return _ownerPresent ? p.isInstanceOwner : p.isMaster;
        }

        /// <summary><see cref="CanControl"/> の**確定直前用**。在席キャッシュを捨てて測り直してから答える。
        ///
        /// 同期値を書く直前 (保留の着地・Ownership 取得後・受信側の受理) に使う。
        /// 代行 Master の保留中にオーナーが戻ったとき、最大 0.5 秒古い在席結果で
        /// 旧管理者の値を公開しないため (IMP-NET-007)。UI の毎フレーム問い合わせには
        /// 使わない (キャッシュの意味が無くなる)。</summary>
        // net: callable-ok: 副作用はキャッシュの測り直しだけ。同期値は動かさない
        public bool CanControlNow(VRCPlayerApi p)
        {
            _nextOwnerScan = -1f;
            return CanControl(p);
        }

        /// <summary>**犬を呼んでよい人か。** オーナーは常に / それ以外は開放されているとき。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public bool CanCall(VRCPlayerApi p)
        {
            if (!Utilities.IsValid(p)) return false;
            return CanControl(p) || openToAll;
        }

        /// <summary>ローカルプレイヤーが権限を持っているか。
        /// **Phase 5 のメニューが [みんなにも開放] トグルと「取り上げる」を出す条件。**</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public bool IsLocalController { get { return CanControl(Networking.LocalPlayer); } }

        /// <summary>ローカルプレイヤーが犬を呼べるか。
        /// **Phase 5 のメニューが [そばで寝る] を出す / 押せるようにする条件。**</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public bool IsLocalAllowedToCall { get { return CanCall(Networking.LocalPlayer); } }

        // ============================================================
        // 書き込み (**ローカル専用。`_` はセキュリティの印**)
        // ============================================================

        /// <summary>開放フラグを書く。**台帳の Owner を取るのはここだけ。**
        ///
        /// 押せるのは <see cref="CanControl"/> が true の人だけ (Phase 3b で実判定を入れた)。
        /// **ここで弾くのはネットワーク越しの偽造対策ではない** — このメソッドは `_` 付き
        /// なので遠隔からは呼べない。守っているのは「非オーナーのクライアントで
        /// メニューのバグや将来の配線ミスから呼ばれても書けない」ことのほう。
        ///
        /// **既に寝ている犬は開放を切っても起きない。** 切るのは「これから呼べる人」の
        /// 制限であって、既に成立している占有を畳む操作ではない (畳みたければ
        /// 権限者が <see cref="KD_SleepSlot._Release"/> で 1 匹ずつ取り上げる)。</summary>
        public void _SetOpenToAll(bool value)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (!CanControlNow(lp)) return;   // 押した瞬間の在席で判定 (キャッシュの 0.5 秒を待たない)

            // **冪等の相手は「今の同期値」ではなく「この操作の結末」** (Codex 2026-09-04 指摘 2)。
            //
            // 移譲待ちの間、同期値はまだ古いままなので `openToAll == value` で比べると、
            // **保留中の値へ戻す操作 (オン → すぐオフ) が冪等と誤判定されて捨てられ**、
            // あとから保留のオンが公開される = 操作順と最終状態が逆転する
            bool target = _hasPending ? _pendingValue : openToAll;
            if (target == value) return;   // 冪等 (**移譲も要求しない** — 押すたびに Owner が動く形にしない)

            // **保留と相殺する**: 要求が今の同期値と同じなら、保留を捨てるだけで終わり。
            // 移譲は既に頼んであるが、着地したときに書くものが無いので害は無い
            // (OnOwnershipTransferred / _PendingTimeout はどちらも `_hasPending` を見る)
            if (_hasPending && value == openToAll) { _hasPending = false; return; }

            if (Networking.IsOwner(gameObject))
            {
                _hasPending = false;       // 今すぐ書ける = 保留を残さない (古い値が後から蘇らない)
                openToAll = value;
                RequestSerialization();
                return;
            }

            // **SetOwner の直後に同期変数へ書かない** (KD_SleepSlot と同じ処置)。
            // 保留がもう有るなら**上書きするだけ** — 最後の要求が最終状態になる
            _pendingValue = value;
            _hasPending = true;
            _pendingGen++;
            Networking.SetOwner(lp, gameObject);   // net: setowner-ok: 寝る台帳の開放フラグ。犬ではない
            SendCustomEventDelayedSeconds(nameof(_PendingTimeout), PendingTimeoutSeconds);
        }

        /// <summary>移譲の通知が来ないまま時間が過ぎたときの打ち切り。
        /// **古い世代の着弾は何もしない** (<see cref="_pendingGen"/>) —
        /// 保留を出し直すと打ち切りが 2 発以上飛び、1 発目が新しい保留を畳んでしまう。</summary>
        public void _PendingTimeout()
        {
            _timeoutGen++;                          // 着弾は必ず数える (発行数と対で見る)
            if (_timeoutGen < _pendingGen) return;  // 古い世代 — 新しい打ち切りが後から来る
            if (!_hasPending) return;
            _hasPending = false;
            PublishPending();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            if (!_hasPending) return;
            _hasPending = false;
            PublishPending();
        }

        /// <summary>保留していた開放フラグを書く。**書く直前に Object Owner と管理権限の両方を見直す**
        /// (IMP-NET-007)。代行 Master が保留している間にオーナーが戻れば、その保留は
        /// 権限失効で捨てる — 移譲の着地を「押した時点の権限」で通すと、戻ったオーナーの
        /// 操作より後から旧管理者の値が公開される。</summary>
        private void PublishPending()
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (!CanControlNow(Networking.LocalPlayer)) return;
            openToAll = _pendingValue;
            RequestSerialization();
        }

        // ============================================================
        // 名簿の出入り — **キャッシュを無効化するだけ** (ここでは走査しない)
        //
        // OnPlayerLeft の時点では GetPlayers に去った人がまだ載っていることがあるので、
        // ここで測ると誤った結論をキャッシュしうる。無効化だけしておき、
        // 実際の走査は次の問い合わせ + OwnerScanInterval に任せる。
        // ============================================================

        public override void OnPlayerJoined(VRCPlayerApi player) { _nextOwnerScan = -1f; }

        public override void OnPlayerLeft(VRCPlayerApi player) { _nextOwnerScan = -1f; }

        void Start()
        {
            int dogs = pack != null && pack.dogRoots != null ? pack.dogRoots.Length : -1;
            if (slots == null || dogs < 0 || slots.Length != dogs)
            {
                Debug.LogError("[KD_SleepBoard] slots (" + (slots == null ? -1 : slots.Length)
                    + ") が名簿 (" + dogs + ") と揃っていない - "
                    + "Run Tools/KominkaDogRun/Sleep System setup.");
                return;
            }
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    Debug.LogError("[KD_SleepBoard] slots[" + i + "] が null - "
                        + "この犬は呼べない。Run Tools/KominkaDogRun/Sleep System setup.");
                    continue;
                }
                // 添字と枠の自己申告がずれていると、**別の犬が寝る**
                if (slots[i].dogIndex != i)
                    Debug.LogError("[KD_SleepBoard] slots[" + i + "].dogIndex == "
                        + slots[i].dogIndex + " (添字と不一致) - 別の犬が寝る。Setup を流し直すこと");
            }
        }

        // ============================================================
        // 内部
        // ============================================================

        /// <summary>**インスタンスオーナーの在席**を測り直す (間引き付き)。
        /// 触るのはキャッシュ 2 本だけで、同期値も他の Behaviour も動かさない。</summary>
        private void EnsureOwnerScan()
        {
            if (_nextOwnerScan >= 0f && Time.time < _nextOwnerScan) return;
            _nextOwnerScan = Time.time + OwnerScanInterval;

            int count = VRCPlayerApi.GetPlayerCount();
            if (count > _players.Length) count = _players.Length;
            VRCPlayerApi.GetPlayers(_players);

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                VRCPlayerApi p = _players[i];
                if (!Utilities.IsValid(p)) continue;
                if (p.isInstanceOwner) { found = true; break; }
            }
            _ownerPresent = found;
        }
    }
}
