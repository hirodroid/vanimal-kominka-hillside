using UdonSharp;
using UnityEngine;
using VRC.Economy;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>商品 1 つ。入手方法は price / ceListingId から導く (無料 / Point / 消耗 / CE)。
    ///
    /// **CE (IMP-FET-010)**: 所有の正本は VRChat の Store。台帳には書かない。
    /// 判定は「入室時」— 一度所有を観測したプレイヤーは滞在中ずっと所有扱い
    /// (`OnPurchaseExpired` で剥がさない。business-strategy §13.3「期限の判定は入室時」)。
    /// 退室で忘れ、再入室時に `OnPurchasesLoaded` で見直す。
    /// CE イベントは全 UdonBehaviour に届くので、商品ごとに自分の Product だけ見る
    /// (ClientSim は旧 `OnPurchaseConfirmed` しか送らないので Multiple と両方受ける。
    /// 二重に来ても集合に足すだけなので数が狂わない)。
    ///
    /// 裏山改変 (IMP-DOG-056): インスタンス単位の所有 (洗い場のセット) を <see cref="CeInstanceOwnership"/> で返す。
    /// Unknown (誰かの購入情報が未着) / Owned (在室者に所有者が居る) / None (全員の分が届いて所有者 0)。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_ShopItem : UdonSharpBehaviour
    {
        [Header("台帳")]
        public KD_ProgressLedger ledger;

        [Header("品目")]
        public int price = 10;

        public bool consumable = false;

        public string productKey = "";

        [Header("表示")]
        public string displayName = "";
        public string displayNameKey;

        public bool menuOnly = false;

        [Header("カテゴリと入手方法 (IMP-PRG-002)")]
        public string categoryKey = "";

        public int packCount = 0;

        public int initialStock = 0;

        public int stockCap = 0;

        [Tooltip("Store.OpenListing に渡す Listing ID (prod_…)。Product ID とは別物。空 = CE ではない")]
        public string ceListingId = "";
        [Tooltip("所有判定用の Udon Product アセット (Product ID を持つ)。Listing ID とは別物。ce-listings.md が台帳")]
        public VRC.Economy.UdonProduct ceProduct;
        [Tooltip("表示用の Credits 額。実際の価格は Storefront の Listing が正本")]
        public int ceCredits = 0;
        private bool _cePurchasesLoaded;
        private bool _ceOwnedLocal;
        private int[] _ceOwnerIds = new int[0];
        private int _ceOwnerCount;
        // 裏山改変 (IMP-DOG-056): インスタンス単位の所有 (契約は IMP-DOG-056 §2.1)。
        // 在室者ごとの「購入情報が届いた」(OnPurchasesLoaded の player。自分も入る) と、3 状態の結果のキャッシュ
        public const int InstanceUnknown = 0;
        public const int InstanceOwned = 1;
        public const int InstanceNone = 2;
        private int[] _ceLoadedIds = new int[0];
        private int _ceLoadedCount;
        private int _ceInstCache;
        private bool _ceInstCacheValid;
        private int _ceLeftId = -1;
        // Unknown も短く覚える (ボードが 4 洗い場 × 4 Hz で聞くので、毎回の走査を避ける)。下の無効化はどれも即座に効く
        public const float CeUnknownRecheckSeconds = 1f;
        private float _ceUnknownUntil;
        private VRCPlayerApi[] _cePlayers = new VRCPlayerApi[0]; // 走査用。人数が増えた時だけ伸ばす
        private int _dbgStoreOpenCount;
        public const string VarCeOwnedLocal = nameof(_ceOwnedLocal);
        public const string VarCePurchasesLoaded = nameof(_cePurchasesLoaded);
        // net: callable-ok: validates the authenticated requester's existing entitlement
        public bool OwnedBy(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !Registered()) return false;
            if (IsCE()) return CeOwnedBy(player);
            if (IsFree()) return true;
            return !consumable && ledger.IsUnlockedFor(player, productKey);
        }

        // ============================================================
        // CE — 所有は Store が正本。判定は入室時 (滞在中は剥がさない)
        // ============================================================

        public override void OnPurchasesLoaded(IProduct[] products, VRCPlayerApi player)
        {
            if (!IsCE() || ceProduct == null || !Utilities.IsValid(player)) return;
            if (player.isLocal) _cePurchasesLoaded = true;
            // 裏山改変 (IMP-DOG-056): 誰の分でも「届いた」を記録する (products が null でも読込は済んでいる = 何も持っていない)
            NoteCeLoaded(player.playerId);
            _ceInstCacheValid = false;
            if (products == null) return;
            for (int i = 0; i < products.Length; i++)
                if (products[i] != null && products[i].ID == ceProduct.ID) { NoteCeOwner(player); return; }
        }

        public override void OnPurchaseConfirmed(IProduct product, VRCPlayerApi player, bool purchasedNow)
        {
            _ceInstCacheValid = false; // 裏山改変 (IMP-DOG-056)
            NoteCePurchase(product, player);
        }

        public override void OnPurchaseConfirmedMultiple(IProduct product, VRCPlayerApi player, bool purchasedNow, int quantity)
        {
            _ceInstCacheValid = false; // 裏山改変 (IMP-DOG-056)
            NoteCePurchase(product, player);
        }

        public override void OnPurchaseExpired(IProduct product, VRCPlayerApi player)
        {
            // 期限の判定は入室時 — 滞在中に切れても使い切れる (§13.3)。次の入室で見直す
        }

        // 裏山改変 (IMP-DOG-056): 入室者の購入情報はまだ届いていない (Unknown になりうる) ので、キャッシュを捨てる
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            _ceInstCacheValid = false;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // 裏山改変 (IMP-DOG-056): 「所有者 0 なら何もしない」の早期 return を外し、読込記録の削除とキャッシュの無効化を先に行う。
            // 退室の通知中は GetPlayers がまだ退室者を返しうるので、その id を CeInstanceOwnership の走査から外す
            // (Store の肯定で退室者を所有者に戻して Owned をキャッシュしないため。id はインスタンス内で再利用されない)
            if (!Utilities.IsValid(player)) return;
            int id = player.playerId;
            _ceLeftId = id;
            _ceInstCacheValid = false;
            for (int i = 0; i < _ceLoadedCount; i++)
            {
                if (_ceLoadedIds[i] != id) continue;
                _ceLoadedIds[i] = _ceLoadedIds[_ceLoadedCount - 1];
                _ceLoadedCount--;
                break;
            }
            for (int i = 0; i < _ceOwnerCount; i++)
            {
                if (_ceOwnerIds[i] != id) continue;
                _ceOwnerIds[i] = _ceOwnerIds[_ceOwnerCount - 1];
                _ceOwnerCount--;
                return;
            }
        }

        private void NoteCePurchase(IProduct product, VRCPlayerApi player)
        {
            if (!IsCE() || ceProduct == null || product == null || !Utilities.IsValid(player)) return;
            if (product.ID != ceProduct.ID) return;
            NoteCeOwner(player);
        }

        private void NoteCeOwner(VRCPlayerApi player)
        {
            int id = player.playerId;
            if (HasCeOwner(id)) return;
            if (_ceOwnerCount >= _ceOwnerIds.Length)
            {
                var grown = new int[_ceOwnerIds.Length + 8];
                for (int i = 0; i < _ceOwnerCount; i++) grown[i] = _ceOwnerIds[i];
                _ceOwnerIds = grown;
            }
            _ceOwnerIds[_ceOwnerCount++] = id;
            if (player.isLocal) _ceOwnedLocal = true;
            _ceInstCacheValid = false; // 裏山改変 (IMP-DOG-056): 所有者が増えたら 3 状態を数え直す
        }

        // 裏山改変 (IMP-DOG-056): 在室者ごとの「購入情報が届いた」の集合 (_ceOwnerIds と同じ手作りの伸長)
        private void NoteCeLoaded(int id)
        {
            if (HasCeLoaded(id)) return;
            if (_ceLoadedCount >= _ceLoadedIds.Length)
            {
                var grown = new int[_ceLoadedIds.Length + 8];
                for (int i = 0; i < _ceLoadedCount; i++) grown[i] = _ceLoadedIds[i];
                _ceLoadedIds = grown;
            }
            _ceLoadedIds[_ceLoadedCount++] = id;
        }

        private bool HasCeLoaded(int id)
        {
            for (int i = 0; i < _ceLoadedCount; i++) if (_ceLoadedIds[i] == id) return true;
            return false;
        }

        /// <summary>裏山改変 (IMP-DOG-056 §2.1): インスタンス単位の所有 (洗い場のセット。1 人が持てば全員が使える)。
        /// CE でない / ceProduct 未配線 → None。在室者を走査し、観測済みの所有者が居るか、観測の無い在室者に Store が肯定すれば Owned
        /// (自分については購入情報の読込後だけ Store に聞く)。所有者が居ず、在室者全員の購入情報が届いていれば None、
        /// 誰かの分が未着なら Unknown (「居ない」と断定しない)。Owned / None はキャッシュし、購入情報・購入通知・入退室・所有者の追加で捨てる。
        /// Unknown は <see cref="CeUnknownRecheckSeconds"/> 秒だけキャッシュする (同じ無効化で即座に捨てる。
        /// 通知を伴わない変化 = Store の肯定が先に立つ場合だけ、最長その秒数遅れて拾う)。</summary>
        // net: callable-ok: 読み取りのみ (キャッシュの更新はローカルの観測だけ)
        public int CeInstanceOwnership()
        {
            if (!IsCE() || ceProduct == null) return InstanceNone;
            if (_ceInstCacheValid && (_ceInstCache != InstanceUnknown || Time.time < _ceUnknownUntil)) return _ceInstCache;
            int n = VRCPlayerApi.GetPlayerCount();
            if (n <= 0) return InstanceUnknown;
            if (_cePlayers.Length < n) _cePlayers = new VRCPlayerApi[n + 8];
            VRCPlayerApi.GetPlayers(_cePlayers);
            bool allLoaded = true;
            int seen = 0;
            // 先頭 n 件だけ見る (配列が長いとき、後ろには前回の在室者が残りうる)
            for (int i = 0; i < n; i++)
            {
                VRCPlayerApi p = _cePlayers[i];
                if (!Utilities.IsValid(p)) continue;
                int id = p.playerId;
                if (id == _ceLeftId) continue;
                seen++;
                bool local = p.isLocal;
                bool loaded = local ? _cePurchasesLoaded : HasCeLoaded(id);
                bool owns = HasCeOwner(id);
                if (!owns && (!local || _cePurchasesLoaded) && Store.DoesPlayerOwnProduct(p, ceProduct))
                {
                    NoteCeOwner(p);
                    owns = true;
                }
                if (owns)
                {
                    _ceInstCache = InstanceOwned;
                    _ceInstCacheValid = true;
                    return InstanceOwned;
                }
                if (!loaded) allLoaded = false;
            }
            if (seen == 0) return InstanceUnknown;
            if (!allLoaded)
            {
                _ceInstCache = InstanceUnknown;
                _ceInstCacheValid = true;
                _ceUnknownUntil = Time.time + CeUnknownRecheckSeconds;
                return InstanceUnknown;
            }
            _ceInstCache = InstanceNone;
            _ceInstCacheValid = true;
            return InstanceNone;
        }

        private bool HasCeOwner(int id)
        {
            for (int i = 0; i < _ceOwnerCount; i++) if (_ceOwnerIds[i] == id) return true;
            return false;
        }

        /// <summary>そのプレイヤーが所有しているか。観測済みならそれを返し、
        /// 未観測なら Store に一度だけ聞いて、所有なら覚える (以後 Store を叩かない)。
        /// 自分については購入情報のロード前は必ず false (Start 直後に判定しない — KanjiFind の教訓)。</summary>
        private bool CeOwnedBy(VRCPlayerApi player)
        {
            if (ceProduct == null || !Utilities.IsValid(player)) return false;
            if (HasCeOwner(player.playerId)) return true;
            if (player.isLocal && !_cePurchasesLoaded) return false;
            if (!Store.DoesPlayerOwnProduct(player, ceProduct)) return false;
            NoteCeOwner(player);
            return true;
        }

        /// <summary>Storefront の購入画面を開く。**台帳は動かさない** (所有は Store から戻ってくる)。
        /// ローカル専用なので `_` 始まり。</summary>
        public void _OpenStore()
        {
            // 裏山改変 IMP-PRG-002 (2026-10-14): 公開しない CE 行 (Product を外した行) は移植元の Listing を開かない
            if (!IsCE() || CeWithheld()) return;
            _dbgStoreOpenCount++;
            Store.OpenListing(ceListingId);
        }

        // net: callable-ok: 読み取りのみ
        public bool CePurchasesLoaded() { return _cePurchasesLoaded; }

        [Header("買えたときの通知 (省略可)")]
        public UdonSharpBehaviour listener;

        public string boughtEvent = "_OnShopItemBought";

        public string listenerKeyField = "boughtProductKey";
// デバッグ盤が読む値 (盤側で判定を組み直さない)
        private int _dbgBought;
        private int _dbgRejected;
        /// <summary>デバッグ HUD が heap を文字列で読むためのフィールド名 (IMP-INF-019 N3)。</summary>
        public const string VarDbgBought = nameof(_dbgBought);
        public const string VarDbgRejected = nameof(_dbgRejected);

        // ============================================================
        // 購入
        // ============================================================

        [Header("棚 (省略可)")]
        public KD_ShopShelf shelf;

        public override void Interact()
        {
            // **棚があるなら判断は棚がする。** ここで買ってしまうと 2 段の確認が成立しない
            if (shelf != null) { shelf._OnItemTouched(productKey); return; }
            _Buy();
        }

        public bool _Buy()
        {
            if (!Registered()) return false;
            if (ledger == null)
            {
                // **黙って無反応にしない。** 未配線は Play で最も気づきにくい壊れ方で、
                // しかも「残高が足りない」と見分けが付かない
                Debug.LogError("[KD_ShopItem] ledger が未配線: " + gameObject.name);
                return false;
            }

            if (IsFree())
            {
                // **無料品は買う対象ではない。** 最初から所持しているものとして棚に並ぶので、
                // ここへ来るのは配線ミス。台帳の「0 円は拒否」に任せると
                // 「残高が足りない」と区別が付かない
                Debug.LogError("[KD_ShopItem] 無料品に _Buy() が来た (棚の配線ミス): "
                    + gameObject.name);
                return false;
            }

            bool ok;
            if (IsCE())
            {
                // CE の所有は CE 側が正本。台帳は 1 pt も動かさない (二重保存しない)
                Debug.LogError("[KD_ShopItem] CE 品の購入は Store 経由 (IMP-FET-010)。"
                    + "台帳を叩いてはいけない: " + gameObject.name);
                return false;
            }
            else if (packCount > 0)
            {
                ok = ledger._TryBuyStocked(productKey, price, packCount, stockCap);
            }
            else
            {
                ok = ledger._TryBuyUnlock(productKey, price);
            }

            if (!ok)
            {
                _dbgRejected++;
                ledger._NoteReject();
                return false;
            }

            _dbgBought++;

            if (listener != null)
            {
                // Publish the product key before notifying the listener.
                if (listenerKeyField != null && listenerKeyField.Length > 0)
                    listener.SetProgramVariable(listenerKeyField, productKey);
                listener.SendCustomEvent(boughtEvent);
            }
            return true;
        }

        // ============================================================
        // 表示用 (棚が読む。棚側で判定を組み直さない)
        // ============================================================

        // ============================================================
        // 入手方法は**フィールドを持たずに導く** (IMP-PRG-002)
        //
        // `acquisition (0=初期 / 1=Point / 2=CE)` を足すと
        // 「CE なのに price が pt」のような**表現できる不正状態が増える**。
        // 必要な情報そのもの (値段と Listing ID) の有無で判定すればずれようがない。
        // ============================================================

        // net: callable-ok: 読み取りのみ
        public bool IsFree() { return price <= 0 && !IsCE(); }

        // net: callable-ok: 読み取りのみ
        public bool IsStarter() { return IsFree() || (initialStock > 0 && !IsCE()); }

        // net: callable-ok: 読み取りのみ
        public bool IsCE() { return ceListingId != null && ceListingId.Length > 0; }

        // net: callable-ok: 読み取りのみ
        public bool IsStocked() { return packCount > 0; }

        /// <summary>裏山改変 IMP-PRG-002 (2026-10-14): 今回の公開版で出さない CE 行か。Setup (KD_ProductDefinitions.Apply) が
        /// UdonProduct を刺さなかった CE 行 = Listing はあるが Product が無い。所有は付かず (CeOwnedBy)、購入画面も開かない (_OpenStore)。
        /// フラグは持たず、既にある 2 つの値から導く (IMP-PRG-002 の「入手方法を導く」と同じ規律)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool CeWithheld() { return IsCE() && ceProduct == null; }

        // net: callable-ok: 読み取りのみ — 遠隔から呼ばれても状態が動かない
        public bool Owned()
        {
            if (!Registered()) return false;
            if (IsCE()) return CeOwnedBy(Networking.LocalPlayer);
            if (IsFree()) return true;              // 無料品は最初から所持
            if (IsStocked()) return ledger.StockSeeded(productKey);
            return ledger.IsUnlocked(productKey);
        }

        // net: callable-ok: 読み取りのみ
        public int Stock()
        {
            if (ledger == null || !IsStocked()) return 0;
            return ledger.StockOf(productKey);
        }

        // net: callable-ok: 読み取りのみ
        public bool CanBuy()
        {
            if (!Registered()) return false;
            if (IsFree() || IsCE()) return false;   // どちらも台帳の購入経路に乗らない
            if (IsStocked()) return ledger.CanBuyStocked(productKey, price, packCount, stockCap);
            if (consumable) return ledger.CanAfford(price);
            return ledger.CanBuyUnlock(productKey, price);
        }

        public const int ConfirmLoading = 0;
        public const int ConfirmFull = 1;
        public const int ConfirmInUse = 2;
        public const int ConfirmOwned = 3;
        public const int ConfirmSpecial = 4;
        public const int ConfirmShort = 5;
        public const int ConfirmBuy = 6;
        public const float ConfirmRefreshSeconds = .5f;
        // net: callable-ok: shared purchase presentation policy; never spends or selects
        public int ConfirmationState(bool inUse)
        {
            if (ledger == null || !ledger.Loaded()) return ConfirmLoading;
            // CE は Store の購入情報が届くまで「読み込み中」(未所持と誤って購入画面へ送らない)
            if (IsCE() && !_cePurchasesLoaded) return ConfirmLoading;
            if (IsStocked())
            {
                if (stockCap > 0 && Stock() >= stockCap) return ConfirmFull;
                return CanBuy() ? ConfirmBuy : ConfirmShort;
            }
            if (inUse) return ConfirmInUse;
            if (Owned()) return ConfirmOwned;
            if (IsCE()) return ConfirmSpecial;
            return CanBuy() ? ConfirmBuy : ConfirmShort;
        }

        // net: callable-ok: 読み取りのみ
        public int Shortfall()
        {
            if (ledger == null) return price;
            if (IsFree() || IsCE()) return 0;
            // **在庫つきは「所持済み」でも足りないことがある** — 買い切りと違って
            // 二度目以降も買うので、Owned() で 0 を返すと「あと N pt」が出なくなる
            if (!IsStocked() && Owned()) return 0;
            return ledger.Shortfall(price);
        }

        // net: callable-ok: 読み取りのみ
        public bool IsConfigured()
        {
            if (ledger == null) return false;
            // **恒久 ID は全商品が持つ** (無料品も消耗品も。選択の保存に使うため)
            if (!ValidKey(productKey, true)) return false;
            if (!ValidKey(categoryKey, false)) return false;
            if (price < 0 || price > KD_ProgressLedger.MaxPoints || packCount < 0 || initialStock < 0 || stockCap < 0) return false;
            if (packCount > KD_ProgressLedger.MaxPoints || initialStock > KD_ProgressLedger.MaxPoints || stockCap > KD_ProgressLedger.MaxPoints) return false;
            if (consumable != IsStocked()) return false;
            if (!IsStocked() && (initialStock != 0 || stockCap != 0)) return false;

            if (IsCE())
            {
                // **CE と無料 / 消耗は排他** (2026-08-23 Codex 指摘)。
                // 「入手方法を price と ceListingId から導く」ようにしたので、
                // 両方が同時に立っている状態は**どちらとも読めてしまう**。
                // 値段は Credits なので pt としての price は持たない
                return price == 0 && packCount == 0 && initialStock == 0 && !consumable;
            }
            if (IsFree())
            {
                // 無料品に在庫や初期付与を付けない (どちらの型なのか読めなくなる)
                return price == 0 && !consumable && packCount == 0 && initialStock == 0;
            }
            if (price <= 0) return false;                // 有料なのに値段が無い
            // consumable と packCount がちぐはぐだと、購入の分岐が意図と変わる
            if (IsStocked() && !consumable) return false;
            return true;
        }
        // ASCII key grammar; no runtime registration of unknown keys.
        public bool ValidKey(string key, bool dotted) // net: callable-ok: pure validation
        {
            if (string.IsNullOrEmpty(key)) return false;
            bool start=true, dot=false;
            for(int i=0;i<key.Length;i++)
            {
                char c=key[i];
                if(c=='.') { if(!dotted || start) return false; start=true; dot=true; continue; }
                if(c>='a' && c<='z') { start=false; continue; }
                if(start || !((c>='0' && c<='9') || c=='_')) return false;
            }
            return !start && (!dotted || dot);
        }
        private bool Registered()
        {
            if (!IsConfigured()) return false;
            if (ledger.storageNamespace != KD_ProgressLedger.ProductionNamespace && ledger.storageNamespace != KD_ProgressLedger.TestNamespace) return false;
            if (productKey.StartsWith("test.") != (ledger.storageNamespace == KD_ProgressLedger.TestNamespace)) return false;
            int i = ledger.IndexOfProduct(productKey);
            return i >= 0 && ledger.products[i] == this;
        }
    }
}
