using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    /// <summary>
    /// DES-DOG-004: 飾りの消費側。**共有の犬に着いている物の正本。**
    ///
    /// 一文で: **犬 x 装着枠 -> 品目 を同期変数で持ち、見た目は兄弟ノードの
    /// `SetActive` だけで作る。それだけ。**
    ///
    /// ## <see cref="KD_ToyBox"/> と同じ形にしてある
    ///
    /// 「世界に出ている共有物を切り替える」という性質が同じなので、機構をそろえた:
    ///
    /// <code>
    /// 自分が選ぶ → 手元メニュー (かざりタブ) が _Equip を叩く
    ///            → 専用台帳の Owner が要求を検証し、同期変数へ書く
    ///            → **見た目は自分も他人も、同期変数からしか作らない**
    ///
    /// 他人 / Late Joiner → OnDeserialization → 同期変数 → SetActive
    /// </code>
    ///
    /// `Renderer.material` も実行時に触らない (`vrc-udonsharp` §6)。
    /// 切り替えは **兄弟のうち 1 つだけ `SetActive(true)`** に統一する。
    ///
    /// ## おもちゃと違うところ
    ///
    /// | | おもちゃ | 飾り |
    /// |---|---|---|
    /// | 単位 | 世界に 1 つ (4 犬種まとめて 1 選択) | **犬ごと x 装着枠ごと** |
    /// | 取り合い | 後勝ち | **最後の変更者は即変更可 / 他人は 60 秒待つ** (DES-DOG-004 §1.1) |
    ///
    /// ## 犬の Ownership は取らない
    ///
    /// **書くのはこの GameObject の同期変数だけで、犬の Ownership には触らない。**
    /// [IMP-NET-005](../../../docs/development/issues/IMP-NET-005-owner-authoritative-dog.md)
    /// 不変条件 3 (撫でる人が犬の Ownership を取らない) と同じ理由 —
    /// 飾りを着けただけで犬の位置権威が移ると、犬の動きが変更者のクライアントに引きずられる。
    /// DES-DOG-004 §1.1 の「犬のネットワーク Ownership 取得とも結び付けない」もこれを指している。
    ///
    /// ## 60 秒はサーバー時計で測る
    ///
    /// ローカル時計だと、クライアントごとに「あと何秒」がずれて
    /// **押せるように見えるのに弾かれる**が起きる。
    /// <see cref="Networking.GetServerTimeInMilliseconds"/> を使い、
    /// 待ち時間そのものも同期変数から導出する (別途 tick を同期しない)。
    /// </summary>
    /// <remarks>
    /// SyncMode Manual・**専用 GameObject**。
    /// U# は同一 GameObject で同期方式を混在できない (`vrc-udonsharp` §2)。
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KD_AccessoryBox : UdonSharpBehaviour
    {
        /// <summary>何も着けていない / 変更者がいない。</summary>
        public const int Unset = -1;

        /// <summary>装着枠。**DES-DOG-004 論点 1 の 5 部位**。
        /// 今は 4 枠に品目があるが、**枠の数は仕様なので 5 のまま持つ** —
        /// 後から増やすと同期変数の添字がずれて、既に着けている人の見た目が飛ぶ。</summary>
        public const int SlotCount = 5;

        public const int SlotHead = 0;
        public const int SlotFace = 1;
        public const int SlotChest = 2;
        public const int SlotBody = 3;
        public const int SlotTail = 4;

        /// <summary>他人が変更できるようになるまでの待ち (ミリ秒)。
        /// **DES-DOG-004 §1.1 のユーザー決定 = 60 秒。** 予告も強制交代も作らない。</summary>
        public const int WaitMs = 60000;

        /// <summary>Owner からの確定応答を待つ上限 (秒)。過ぎたら結果不明を表示する。</summary>
        private const float PendingTimeoutSeconds = 5f;

        [Header("器 (Setup が入れる)")]
        [Tooltip("品目ごとの恒久 ID。**nodes / productSlots と添字で対応する**")]
        public int[] productIds;

        [Tooltip("品目がどの装着枠に入るか (Slot* の値)。productIds と同じ長さ")]
        public int[] productSlots;

        [Tooltip("品目 x 犬の見た目ノード。**productIds[i] の犬 d 番目 = nodes[i * dogCount + d]**。" +
                 "1 次元なのは U# が多次元配列を扱えないため (KD_ToyBox と同じ)")]
        public GameObject[] nodes;

        [Tooltip("犬の数。nodes の添字計算に使う")]
        public int dogCount = 6;

        /// <summary>犬 x 装着枠 -> 品目の恒久 ID。**共有の見た目の正本。**
        /// 添字は `dog * SlotCount + slot`。<see cref="Unset"/> = その枠は空。
        ///
        /// **既定は全部 Unset。** シーンの初期状態 (何も着けていない) と一致させておく —
        /// 0 を「何か着けている」にすると、最初の人が品目 0 を選んだ 1 回だけ値が変わらず、
        /// `OnDeserialization` が飛ばずに他人の画面だけ変わらない (KD_ToyBox が踏んだ形)。</summary>
        [UdonSynced] private int[] _slotProduct = new int[0];

        /// <summary>犬ごとの「最後に実際に見た目を変えた人」の playerId。
        /// <see cref="Unset"/> = まだ誰も変えていない (誰でも変えられる)。</summary>
        [UdonSynced] private int[] _changerId = new int[0];

        /// <summary>犬ごとの最後の変更時刻 (サーバー時計のミリ秒)。</summary>
        [UdonSynced] private int[] _changedMs = new int[0];

        // 適用済み。二重適用と無駄な SetActive を避ける
        private int[] _applied;

        // Temporary show layer only. Ordinary synced selection/cooldown is never rewritten.
        private bool _candyPerformanceOverlay;
        public GameObject[] candyPerformanceNodes = new GameObject[0];
        public void _SetCandyPerformanceOverlay(bool enabled)
        {
            _candyPerformanceOverlay = enabled;
            EnsureArrays();
            Apply();
        }

        // Sets refer to the same product nodes; they never duplicate meshes or entitlement.
        public KD_ShopItem[] productItems;
        public int[] setIds = new int[0];
        public int[] setMembers = new int[0]; // exactly three product IDs per set
        public KD_ShopItem[] setItems;
        // 裏山改変 (IMP-DOG-015 案 C、2026-09-24 ユーザー決定): 首輪と物理的に重なる「犬 × 品目」の組。添字をそろえた 2 本の配列
        // (U# は多次元配列・構造体を持てない)。正本は Editor の定数 1 か所 (KD_AccessorySetupEditor.HillsideCollarExcluded) で、Menu/6e が書く。
        // 単品はその犬に着けられない (Owner が拒否)。セットはその品だけ飛ばして残りの枠を着ける。
        public int[] blockedDogs = new int[0];
        public int[] blockedProducts = new int[0];

        /// <summary>裏山改変: 犬 dog にこの品目 (単品の恒久 ID) を着けられないか。セットの ID は常に false (中身ごとに見る)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool Blocked(int dog, int product)
        {
            if (blockedDogs == null || blockedProducts == null) return false;
            int n = blockedDogs.Length < blockedProducts.Length ? blockedDogs.Length : blockedProducts.Length;
            for (int i = 0; i < n; i++) if (blockedDogs[i] == dog && blockedProducts[i] == product) return true;
            return false;
        }

        public int requestState; // 0 idle, 1 pending, 2 accepted, 3 rejected, 4 result unknown
        public int requestDog = Unset;
        private int _requestToken;
        private float _requestDeadline;
        public int requestSelection;
        private int _requestClearSlot;
        private int _requestOwnerId;
        private int _requestSentMs;
        private bool _retryWaiting;

        private void Start()
        {
            EnsureArrays();
            Apply();
        }

        /// <summary>同期配列は Setup が長さを入れるが、**取りこぼしても落ちないように**ここでも作る。
        /// 長さが足りないだけで犬が丸ごと見えなくなるより、空で始めるほうが常にまし。</summary>
        private void EnsureArrays()
        {
            int n = dogCount * SlotCount;
            if (_slotProduct == null || _slotProduct.Length != n)
            {
                _slotProduct = new int[n];
                for (int i = 0; i < n; i++) _slotProduct[i] = Unset;
            }
            if (_changerId == null || _changerId.Length != dogCount)
            {
                _changerId = new int[dogCount];
                for (int i = 0; i < dogCount; i++) _changerId[i] = Unset;
            }
            if (_changedMs == null || _changedMs.Length != dogCount)
            {
                _changedMs = new int[dogCount];
            }
            if (_applied == null || _applied.Length != n)
            {
                _applied = new int[n];
                for (int i = 0; i < n; i++) _applied[i] = Unset;
            }
        }

        // ============================================================
        // 手元メニューからの操作
        // ============================================================

        /// <summary>犬 <paramref name="dog"/> へ品目を着ける。取り外しは _Clear で枠を指定する。
        ///
        /// **ローカル専用** — ネットワーク越しに呼ばれると他人の犬を勝手に着替えさせられる。</summary>
        public void _Equip(int dog, int product) { SendAppearance(dog, product, -1); }
        public void _EquipSet(int dog, int set) { SendAppearance(dog, set, -1); }
        public void _Clear(int dog, int slot) { SendAppearance(dog, Unset, slot); }

        private void SendAppearance(int dog, int selection, int clearSlot)
        {
            EnsureArrays();
            if (requestState == 1 || dog < 0 || dog >= dogCount || !CanChange(dog)) return;
            if (clearSlot < 0 && !SelectionOwned(selection)) return;
            if (clearSlot < 0 && Blocked(dog, selection)) return;   // 裏山改変: 首輪と重なる組は送らない (Owner も拒否する)
            var player = Networking.LocalPlayer;
            var owner = Networking.GetOwner(gameObject);
            if (!Utilities.IsValid(player) || !Utilities.IsValid(owner)) return;
            _requestToken = _requestToken == int.MaxValue ? 1 : _requestToken + 1;
            requestState = 1;
            requestDog = dog;
            requestSelection = selection; _requestClearSlot = clearSlot; _requestOwnerId = owner.playerId;
            _retryWaiting = false;
            _requestDeadline = Time.time + PendingTimeoutSeconds;
            int now = Networking.GetServerTimeInMilliseconds();
            _requestSentMs = now;
            if (Networking.IsOwner(gameObject)) HandleAppearance(player, dog, selection, clearSlot, now, owner.playerId, _requestToken);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestAppearance), dog, selection, clearSlot, now, owner.playerId, _requestToken);
            SendCustomEventDelayedSeconds(nameof(_PendingTimeout), PendingTimeoutSeconds);
        }

        [NetworkCallable(maxEventsPerSecond: 8)]
        // net: callable-ok: authenticated appearance request, only the dedicated ledger Owner writes
        public void RequestAppearance(int dog, int selection, int clearSlot, int sentMs, int ownerId, int token)
        {
            HandleAppearance(NetworkCalling.CallingPlayer, dog, selection, clearSlot, sentMs, ownerId, token);
        }

        private void HandleAppearance(VRCPlayerApi player, int dog, int selection, int clearSlot, int sentMs, int ownerId, int token)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(player)) return;
            if (Networking.LocalPlayer.playerId != ownerId) return;
            int age = Networking.GetServerTimeInMilliseconds() - sentMs;
            if (age < -1000 || age > 5000) return;
            EnsureArrays();
            bool ok = dog >= 0 && dog < dogCount;
            bool entitlementPending = false;
            int[] next = new int[SlotCount];
            if (ok)
            {
                for (int i = 0; i < SlotCount; i++) next[i] = _slotProduct[dog * SlotCount + i];
                ok = RemainingFor(dog, player.playerId) <= 0;
                if (ok)
                {
                    ok = PrepareSelection(next, selection, clearSlot, player, dog);
                    // PlayerData and the request travel separately. Retry only an explicitly
                    // uncommitted entitlement failure, with the original deadline and Owner.
                    entitlementPending = !ok && !player.isLocal && clearSlot < 0 && age < 3500;
                }
            }
            bool changed = false;
            if (ok)
            {
                for (int i = 0; i < SlotCount; i++) if (next[i] != _slotProduct[dog * SlotCount + i]) changed = true;
                if (changed)
                {
                    for (int i = 0; i < SlotCount; i++) _slotProduct[dog * SlotCount + i] = next[i];
                    _changerId[dog] = player.playerId;
                    _changedMs[dog] = Networking.GetServerTimeInMilliseconds();
                    RequestSerialization(); Apply();
                }
            }
            if (player.isLocal) requestState = ok ? 2 : 3;
            else SendCustomNetworkEvent(NetworkEventTarget.All, nameof(AppearanceReply), player.playerId, token, ok ? 2 : entitlementPending ? 5 : 3);
        }

        // Validate the whole selection before writing even one slot.
        private bool PrepareSelection(int[] next, int selection, int clearSlot, VRCPlayerApi player, int dog)
        {
            if (clearSlot >= 0)
            {
                if (selection != Unset || clearSlot >= SlotCount) return false;
                next[clearSlot] = Unset; return true;
            }
            int set = IndexOfSet(selection);
            if (set >= 0)
            {
                if (setMembers == null || setMembers.Length != setIds.Length * 3 || setItems == null || set >= setItems.Length || setItems[set] == null || !setItems[set].OwnedBy(player)) return false;
                int used = 0, applied = 0;
                for (int i = 0; i < 3; i++)
                {
                    int product = setMembers[set * 3 + i];
                    int slot = SlotOfProduct(product);
                    if (slot < 0 || slot >= SlotCount || (used & (1 << slot)) != 0 || !ProductOwnedBy(product, player)) return false;
                    used |= 1 << slot;
                    if (Blocked(dog, product)) continue;   // 裏山改変: 首輪と重なる品だけ飛ばす (その枠は今のまま)
                    next[slot] = product; applied++;
                }
                return applied > 0;
            }
            int singleSlot = SlotOfProduct(selection);
            if (singleSlot < 0 || singleSlot >= SlotCount || !ProductOwnedBy(selection, player)) return false;
            if (Blocked(dog, selection)) return false;   // 裏山改変: 首輪と重なる組は Owner が拒否 (未所持品の拒否と同じ経路)
            next[singleSlot] = selection; return true;
        }

        [NetworkCallable(maxEventsPerSecond: 16)]
        // net: callable-ok: addressed reply accepted only from the current ledger Owner
        public void AppearanceReply(int playerId, int token, int result)
        {
            var sender = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(sender) || !Networking.IsOwner(sender, gameObject)) return;
            var local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local) || local.playerId != playerId || token != _requestToken || requestState != 1) return;
            if (result == 5)
            {
                if (!_retryWaiting) { _retryWaiting = true; SendCustomEventDelayedSeconds(nameof(_RetryEntitlement), .5f); }
                return;
            }
            if (result == 2 || result == 3) requestState = result;
        }

        public void _RetryEntitlement()
        {
            if (!_retryWaiting || requestState != 1) return;
            _retryWaiting = false;
            var owner = Networking.GetOwner(gameObject);
            if (Time.time >= _requestDeadline || !Utilities.IsValid(owner) || owner.playerId != _requestOwnerId)
            { requestState = 4; return; }
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestAppearance), requestDog, requestSelection, _requestClearSlot, _requestSentMs, _requestOwnerId, _requestToken);
        }

        public void _PendingTimeout()
        {
            if (requestState != 1) return;
            float remaining = _requestDeadline - Time.time;
            if (remaining > .01f) { SendCustomEventDelayedSeconds(nameof(_PendingTimeout), remaining); return; }
            requestState = 4; // Never replay an uncertain request after an Owner change.
        }

        private bool ProductOwnedBy(int product, VRCPlayerApi player)
        {
            int i = IndexOfProduct(product);
            return i >= 0 && productItems != null && i < productItems.Length && productItems[i] != null && productItems[i].OwnedBy(player);
        }

        // net: callable-ok: local entitlement query; missing product wiring fails closed
        public bool SelectionOwned(int selection)
        {
            int set = IndexOfSet(selection);
            if (set >= 0) return setItems != null && set < setItems.Length && setItems[set] != null && setItems[set].Owned();
            int i = IndexOfProduct(selection);
            return i >= 0 && productItems != null && i < productItems.Length && productItems[i] != null && productItems[i].Owned();
        }
        // net: callable-ok: reads the fixed set membership
        public int IndexOfSet(int selection)
        {
            if (setIds != null) for (int i = 0; i < setIds.Length; i++) if (setIds[i] == selection) return i;
            return -1;
        }
        // net: callable-ok: determines which preview meshes belong to a selection
        public bool SelectionContains(int selection, int product)
        {
            int set = IndexOfSet(selection);
            if (set < 0) return selection == product;
            if (setMembers == null || setMembers.Length < (set + 1) * 3) return false;
            for (int i = 0; i < 3; i++) if (setMembers[set * 3 + i] == product) return true;
            return false;
        }
        // net: callable-ok: compares all three slots without changing the cooldown
        public bool SetEquipped(int dog, int selection)
        {
            int set = IndexOfSet(selection);
            if (set < 0 || setMembers == null || setMembers.Length < (set + 1) * 3) return false;
            for (int i = 0; i < 3; i++)
            {
                int product = setMembers[set * 3 + i];
                if (Blocked(dog, product)) continue;   // 裏山改変: その犬に着けない品は「着けた」の判定から外す
                if (EquippedOf(dog, SlotOfProduct(product)) != product) return false;
            }
            return true;
        }

        public override void OnDeserialization() { EnsureArrays(); Apply(); }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // The departed player's ID is still supplied by the callback. Preserve every outfit.
            if (!Networking.IsOwner(gameObject) || player == null) return;
            EnsureArrays();
            bool changed = false;
            for (int dog = 0; dog < dogCount; dog++)
            {
                if (_changerId[dog] != player.playerId) continue;
                _changerId[dog] = Unset; _changedMs[dog] = 0; changed = true;
            }
            if (changed) RequestSerialization();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // Covers either ordering of departure and automatic ledger Owner transfer.
            if (Utilities.IsValid(player) && player.isLocal)
                SendCustomEventDelayedSeconds(nameof(_ReleaseDepartedChangers), .5f);
        }

        public void _ReleaseDepartedChangers()
        {
            if (!Networking.IsOwner(gameObject)) return;
            EnsureArrays();
            bool changed = false;
            for (int dog = 0; dog < dogCount; dog++)
            {
                if (_changerId[dog] == Unset || Utilities.IsValid(VRCPlayerApi.GetPlayerById(_changerId[dog]))) continue;
                _changerId[dog] = Unset; _changedMs[dog] = 0; changed = true;
            }
            if (changed) RequestSerialization();
        }

        /// <summary>同期変数から見た目を作る。**兄弟のうち 1 つだけ active。**</summary>
        private void Apply()
        {
            if (nodes == null || productIds == null || productSlots == null) return;
            if (dogCount <= 0) return;
            if (productSlots.Length != productIds.Length) return;
            if (nodes.Length < productIds.Length * dogCount) return;

            // **毎回すべてのノードを同期変数から作り直す。**
            // 「変わっていない枠は飛ばす」最適化は入れない — 枠と品目の二重添字で
            // 飛ばし条件を間違えると、外したはずの飾りが残る / 着けたのに出ない が起きる。
            // `SetActive` は値が変わるときしか呼ばない
            for (int p = 0; p < productIds.Length; p++)
            {
                int slot = productSlots[p];
                if (slot < 0 || slot >= SlotCount) continue;
                // 裏山改変 IMP-PRG-002 (2026-10-14): 公開しない CE 品 (商品の Product を外した品) は、同期値が指していても着けない
                // (他人・旧 Owner の値でも既定 = 何も着けない に落とす)。恒久 ID と nodes の並びは変えない
                bool withheld = productItems != null && p < productItems.Length && productItems[p] != null && productItems[p].CeWithheld();
                for (int d = 0; d < dogCount; d++)
                {
                    var go = nodes[p * dogCount + d];
                    if (go == null) continue;
                    bool on = !withheld && !_candyPerformanceOverlay && _slotProduct[d * SlotCount + slot] == productIds[p];
                    // Shared fitted nodes stay dressed if ordinary selection deserializes during the show.
                    if (_candyPerformanceOverlay && candyPerformanceNodes != null)
                        for (int c = 0; c < candyPerformanceNodes.Length; c++)
                            if (candyPerformanceNodes[c] == go) { on = true; break; }
                    if (go.activeSelf != on) go.SetActive(on);
                }
            }
            for (int i = 0; i < _slotProduct.Length; i++) _applied[i] = _slotProduct[i];
        }

        // ============================================================
        // メニューが読む口 (どれも読み取りのみ)
        // ============================================================

        /// <summary>犬 <paramref name="dog"/> の枠に着いている品目 (<see cref="Unset"/> = 空)。</summary>
        // net: callable-ok: 読み取りのみ
        public int EquippedOf(int dog, int slot)
        {
            if (_slotProduct == null) return Unset;
            if (dog < 0 || dog >= dogCount) return Unset;
            if (slot < 0 || slot >= SlotCount) return Unset;
            if (dog * SlotCount + slot >= _slotProduct.Length) return Unset;
            return _slotProduct[dog * SlotCount + slot];
        }

        /// <summary>いま自分がこの犬の見た目を変えられるか。
        /// **本人 (最後の変更者) は待たない / 他人は最後の変更から 60 秒** (DES-DOG-004 §1.1)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool CanChange(int dog)
        {
            return RemainingMs(dog) <= 0;
        }

        /// <summary>変更できるようになるまでの残り (ミリ秒)。0 以下なら今すぐ変えられる。</summary>
        // net: callable-ok: 読み取りのみ
        public int RemainingMs(int dog)
        {
            var lp = Networking.LocalPlayer;
            return RemainingFor(dog, Utilities.IsValid(lp) ? lp.playerId : Unset);
        }

        private int RemainingFor(int dog, int playerId)
        {
            if (_changerId == null || _changedMs == null || dog < 0 || dog >= dogCount || dog >= _changerId.Length || dog >= _changedMs.Length) return 0;
            int changer = _changerId[dog];
            if (changer == Unset || changer == playerId) return 0;
            int elapsed = Networking.GetServerTimeInMilliseconds() - _changedMs[dog];
            // **サーバー時計が巻き戻ったように見えたら待たせない。**
            // 待たせる方向へ倒すと「永久に変えられない犬」になりうる
            if (elapsed < 0) return 0;
            int left = WaitMs - elapsed;
            return left > 0 ? left : 0;
        }

        /// <summary>「あと ○ 秒」の秒。0 なら今すぐ変えられる。</summary>
        // net: callable-ok: 読み取りのみ
        public int RemainingSeconds(int dog)
        {
            int ms = RemainingMs(dog);
            return ms <= 0 ? 0 : (ms + 999) / 1000;
        }

        /// <summary>器を持っている品目か。持っていなければ -1。</summary>
        // net: callable-ok: 読み取りのみ
        public int IndexOfProduct(int product)
        {
            if (productIds == null) return -1;
            for (int i = 0; i < productIds.Length; i++)
                if (productIds[i] == product) return i;
            return -1;
        }

        /// <summary>品目の装着枠。</summary>
        // net: callable-ok: 読み取りのみ
        public int SlotOfProduct(int product)
        {
            int i = IndexOfProduct(product);
            if (i < 0 || productSlots == null || i >= productSlots.Length) return -1;
            return productSlots[i];
        }
    }
}
