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
    /// 二重に来ても集合に足すだけなので数が狂わない)。</summary>
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
            if (products == null) return;
            for (int i = 0; i < products.Length; i++)
                if (products[i] != null && products[i].ID == ceProduct.ID) { NoteCeOwner(player); return; }
        }

        public override void OnPurchaseConfirmed(IProduct product, VRCPlayerApi player, bool purchasedNow)
        {
            NoteCePurchase(product, player);
        }

        public override void OnPurchaseConfirmedMultiple(IProduct product, VRCPlayerApi player, bool purchasedNow, int quantity)
        {
            NoteCePurchase(product, player);
        }

        public override void OnPurchaseExpired(IProduct product, VRCPlayerApi player)
        {
            // 期限の判定は入室時 — 滞在中に切れても使い切れる (§13.3)。次の入室で見直す
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || _ceOwnerCount == 0) return;
            int id = player.playerId;
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
            if (!IsCE()) return;
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
