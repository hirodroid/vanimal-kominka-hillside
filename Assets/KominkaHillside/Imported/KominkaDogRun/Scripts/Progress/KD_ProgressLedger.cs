using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace KominkaDogRun
{
    // Product-key persistence. Ledger writes only monotonic totals, ownership and once-only bonus flags.
    // Points come from three sources (IMP-PRG-009): initial grant on first restore, the keeper NPC once,
    // and 1 pt per minute of stay. Gift codes (IMP-PRG-010) reuse the same once-only bonus path with
    // bonusKey "gift.{codeKey}". Every source adds to the same Earned total.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_ProgressLedger : UdonSharpBehaviour
    {
        public const int MaxPoints = 1000000;
        public const string ProductionNamespace = "KD_Commerce_v1";
        public const string TestNamespace = "KD_TestCommerce_v1";
        public string storageNamespace = ProductionNamespace;
        public KD_ShopItem[] products;
        public float minuteSeconds = 60f;
        public float tickSeconds = 15f;
        /// <summary>初期配布。復元直後に一度だけ Earned へ足す (0 で無効)。Progress Setup が 100 に正す。</summary>
        public int initialPoints = 100;
        /// <summary>おじいさんに話しかけたとき一度だけ (0 で無効)。呼ぶのは KD_ShopKeeper、書くのはここ。</summary>
        public int keeperPoints = 100;
        public const string BonusInitial = "initial";
        public const string BonusKeeper = "keeper";
        private bool _started, _restored, _catalogReady, _loaded;
        private bool _earnedValid, _spentValid;
        private int _earned, _spent, _pendingMinutes, _grantedMinutes, _version;
        private float _clockBase;
        private bool[] _owned, _ownValid, _stockValid, _stockSeeded;
        private int[] _stockBought, _stockEaten;
        /// <summary>デバッグ HUD が heap を文字列で読むためのフィールド名 (IMP-INF-019 N3)。改名をコンパイルで検出する。</summary>
        public const string VarOwned = nameof(_owned);
        public const string VarStockBought = nameof(_stockBought);
        public const string VarStockEaten = nameof(_stockEaten);
        public const string VarStockValid = nameof(_stockValid);
        private string[] _ownKeys, _boughtKeys, _eatenKeys;
        private int _dbgGrantCount, _dbgBuyCount, _dbgRejectCount;
        private float _dbgLastGrantAt;

        private void Start()
        {
            if (_started) return;
            _started = true;
            _clockBase = Time.time;
            _ConfigureCatalog(products);
            SendCustomEventDelayedSeconds(nameof(_Tick), Mathf.Max(tickSeconds, 1f));
        }
        public void _ConfigureCatalog(KD_ShopItem[] registered)
        {
            if (_catalogReady) return;
            if (registered == null || registered.Length == 0) return;
            if (storageNamespace != ProductionNamespace && storageNamespace != TestNamespace) return;
            for (int i = 0; i < registered.Length; i++)
            {
                var item = registered[i];
                if (item == null || item.ledger != this || !item.IsConfigured()) return;
                if (storageNamespace == ProductionNamespace && item.productKey.StartsWith("test.")) return;
                if (storageNamespace == TestNamespace && !item.productKey.StartsWith("test.")) return;
                for (int j = 0; j < i; j++) if (registered[j].productKey == item.productKey) return;
            }
            products = registered;
            int n = products.Length;
            _owned = new bool[n]; _ownValid = new bool[n]; _stockValid = new bool[n]; _stockSeeded = new bool[n];
            _stockBought = new int[n]; _stockEaten = new int[n];
            _ownKeys = new string[n]; _boughtKeys = new string[n]; _eatenKeys = new string[n];
            for (int i = 0; i < n; i++)
            {
                _ownKeys[i] = DataKey("Own", products[i].productKey);
                _boughtKeys[i] = DataKey("StockB", products[i].productKey);
                _eatenKeys[i] = DataKey("StockE", products[i].productKey);
            }
            _catalogReady = true;
            Restore();
        }
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal || _restored) return;
            _restored = true;
            Restore();
        }
        private void Restore()
        {
            if (!_restored || !_catalogReady || _loaded) return;
            var player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player)) return;
            string earnedKey = DataKey("Earned", ""), spentKey = DataKey("Spent", "");
            _earnedValid = ReadTotal(player, earnedKey, out _earned);
            _spentValid = ReadTotal(player, spentKey, out _spent);
            for (int i = 0; i < products.Length; i++)
            {
                _ownValid[i] = true; _stockValid[i] = true;
                var item = products[i];
                if (item.IsFree() || item.IsCE()) continue;
                if (item.IsStocked())
                {
                    _stockSeeded[i] = PlayerData.HasKey(player, _boughtKeys[i]);
                    int bought, eaten;
                    bool boughtOk = ReadTotal(player, _boughtKeys[i], out bought);
                    bool eatenOk = ReadTotal(player, _eatenKeys[i], out eaten);
                    _stockBought[i] = bought; _stockEaten[i] = eaten;
                    _stockValid[i] = boughtOk && eatenOk && (_stockSeeded[i] || !PlayerData.HasKey(player, _eatenKeys[i]));
                    if (!_stockValid[i]) Debug.LogError("[KD_Commerce] Invalid stock data: " + item.productKey);
                }
                else if (!item.consumable)
                {
                    bool has = PlayerData.HasKey(player, _ownKeys[i]);
                    // UdonSharp can omit the write-back for an extern out array element.
                    // Receive into a local, then explicitly assign the ownership cache (BUG-PRG-001).
                    bool owned = false;
                    _ownValid[i] = !has || PlayerData.TryGetBool(player, _ownKeys[i], out owned);
                    _owned[i] = owned;
                    if (!_ownValid[i]) Debug.LogError("[KD_Commerce] Invalid ownership type: " + item.productKey);
                }
            }
            _loaded = true; _version++;
            LogRestored(player);
            if (_earnedValid && _pendingMinutes > 0)
            {
                int pending = _pendingMinutes; _pendingMinutes = 0; Grant(pending);
            }
            _GrantBonusOnce(BonusInitial, initialPoints);
        }
        /// <summary>BUG-PRG-001 の切り分け用。復元直後 (付与前) の台帳値と、独立して読んだ rawOwned を 1 行で出す。
        /// 実機の output_log を <c>node scripts/prg-diagnose-log.js</c> で読む。原因が確定して Issue を閉じるときに外す (release-checklist §1)。</summary>
        private void LogRestored(VRCPlayerApi player)
        {
            string owned = "", rawOwned = "", stock = "";
            for (int i = 0; i < products.Length; i++)
            {
                var item = products[i];
                if (item.IsStocked()) { if (_stockSeeded[i] || _stockBought[i] > 0 || _stockEaten[i] > 0) stock += " " + item.productKey + "=" + _stockBought[i] + "/" + _stockEaten[i]; }
                else if (!item.IsFree() && !item.IsCE() && !item.consumable && PlayerData.HasKey(player, _ownKeys[i]))
                {
                    bool raw;
                    bool rawValid = PlayerData.TryGetBool(player, _ownKeys[i], out raw);
                    rawOwned += " " + item.productKey + "=" + (rawValid ? (raw ? "1" : "0") : "?");
                    owned += " " + item.productKey + "=" + (_ownValid[i] ? (_owned[i] ? "1" : "0") : "?");
                }
            }
            Debug.Log("[KD_Commerce] restored ns=" + storageNamespace + " earned=" + _earned + (_earnedValid ? "" : "?") + " spent=" + _spent + (_spentValid ? "" : "?")
                + " bonus.initial=" + (PlayerData.HasKey(player, DataKey("Bonus", BonusInitial)) ? "1" : "0")
                + " bonus.keeper=" + (PlayerData.HasKey(player, DataKey("Bonus", BonusKeeper)) ? "1" : "0")
                + " owned[" + owned.Trim() + "] rawOwned[" + rawOwned.Trim() + "] stock[" + stock.Trim() + "]");
        }
        private bool ReadTotal(VRCPlayerApi player, string key, out int value)
        {
            value = 0;
            if (!PlayerData.HasKey(player, key)) return true;
            if (!PlayerData.TryGetInt(player, key, out value))
            { Debug.LogError("[KD_Commerce] Invalid total type: " + key); return false; }
            value = ClampPoints(value);
            return true;
        }
        public void _Tick()
        {
            if (!_started) return;
            int elapsed = (int)((Time.time - _clockBase) / Mathf.Max(minuteSeconds, 1f));
            if (elapsed > _grantedMinutes)
            { int gain = elapsed - _grantedMinutes; _grantedMinutes = elapsed; Grant(gain); }
            SendCustomEventDelayedSeconds(nameof(_Tick), Mathf.Max(tickSeconds, 1f));
        }
        private void Grant(int minutes)
        {
            if (minutes <= 0) return;
            if (!_loaded || !_earnedValid) { _pendingMinutes = AddClamped(_pendingMinutes, minutes); return; }
            Earn(minutes);
        }
        private void Earn(int points)
        {
            _earned = AddClamped(_earned, points);
            PlayerData.SetInt(DataKey("Earned", ""), _earned);
            _version++; _dbgGrantCount++; _dbgLastGrantAt = Time.time;
        }
        /// <summary>一度きりのポイント付与 (初期配布 / おじいさん)。フラグ true → Earned の順に書く。
        /// 復元前・Earned 型異常・付与済み・0 以下では何も書かない。</summary>
        public bool _GrantBonusOnce(string bonusKey, int amount)
        {
            if (!_loaded || !_earnedValid || string.IsNullOrEmpty(bonusKey) || amount <= 0) return false;
            var player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player)) return false;
            string key = DataKey("Bonus", bonusKey);
            if (PlayerData.HasKey(player, key)) return false;
            PlayerData.SetBool(key, true);
            Earn(amount);
            return true;
        }
        public bool _GrantKeeperBonus() { return _GrantBonusOnce(BonusKeeper, keeperPoints); }
        /// <summary>ギフトコードの照合用 (IMP-PRG-010)。入力を「ASCII 英数字のみ・大文字」に正規化する —
        /// 大文字小文字とハイフンは区別しない。非 ASCII は捨てる。</summary>
        public string NormalizeCode(string raw) // net: callable-ok: pure
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string upper = raw.ToUpperInvariant(); string result = "";
            for (int i = 0; i < upper.Length; i++)
            {
                char c = upper[i];
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z')) result += upper.Substring(i, 1);
            }
            return result;
        }
        /// <summary>FNV-1a 32bit を 31bit に畳んだハッシュ (非負)。定義表のハッシュと同じ式 — Editor 側 (KD_GiftCodeDefinitions.Hash) と一致させる。
        /// uint を使わず long で回す (負の int→uint は実機で例外。vrc-udonsharp §1)。</summary>
        public int Fnv1a31(string text) // net: callable-ok: pure
        {
            long h = 2166136261L;
            if (text != null) for (int i = 0; i < text.Length; i++)
            {
                h = (h ^ (long)text[i]) * 16777619L;
                h = h & 0xFFFFFFFFL;
            }
            return (int)(h & 0x7FFFFFFFL);
        }
        public bool BonusReceived(string bonusKey) // net: callable-ok: read only
        {
            var player = Networking.LocalPlayer;
            return _loaded && !string.IsNullOrEmpty(bonusKey) && Utilities.IsValid(player)
                && PlayerData.HasKey(player, DataKey("Bonus", bonusKey));
        }
        private int ClampPoints(int value) { return Mathf.Clamp(value, 0, MaxPoints); }
        private int AddClamped(int current, int add)
        { int c = ClampPoints(current); return add <= 0 ? c : add >= MaxPoints - c ? MaxPoints : c + add; }
        private void Spend(int price)
        {
            _spent = AddClamped(_spent, price); PlayerData.SetInt(DataKey("Spent", ""), _spent);
            _version++; _dbgBuyCount++;
        }
        public bool _TryBuyUnlock(string productKey, int price)
        {
            if (!CanBuyUnlock(productKey, price)) return false;
            int i = IndexOfProduct(productKey);
            _owned[i] = true; PlayerData.SetBool(_ownKeys[i], true);
            Spend(price);
            Debug.Log("[KD_Commerce] bought " + productKey + " ns=" + storageNamespace + " price=" + price + " earned=" + _earned + " spent=" + _spent); // BUG-PRG-001 切り分け用 (LogRestored と対で外す)
            return true;
        }
        public bool _TryBuyStocked(string productKey, int price, int count, int cap)
        {
            if (!CanBuyStocked(productKey, price, count, cap)) return false;
            int i = IndexOfProduct(productKey);
            _stockBought[i] = AddClamped(_stockBought[i], count);
            PlayerData.SetInt(_boughtKeys[i], _stockBought[i]); _stockSeeded[i] = true;
            Spend(price); return true;
        }
        public bool _TryConsume(string productKey)
        {
            int i = IndexOfProduct(productKey);
            if (i < 0 || StockOf(productKey) <= 0) return false;
            _stockEaten[i] = AddClamped(_stockEaten[i], 1);
            PlayerData.SetInt(_eatenKeys[i], _stockEaten[i]); _version++; return true;
        }
        public bool _GrantInitialStockOnce(string productKey, int count)
        {
            int i = IndexOfProduct(productKey);
            if (!_loaded || i < 0 || !_stockValid[i] || _stockSeeded[i] || count <= 0) return false;
            var item = products[i];
            if (!item.IsStocked() || item.IsCE() || item.initialStock != count) return false;
            _stockBought[i] = AddClamped(_stockBought[i], count);
            PlayerData.SetInt(_boughtKeys[i], _stockBought[i]); _stockSeeded[i] = true; _version++; return true;
        }
        public void _NoteReject() { _dbgRejectCount++; }
        public bool Loaded() { return _loaded; } // net: callable-ok: read only
        public bool CatalogReady() { return _catalogReady; } // net: callable-ok: read only
        public int Points() { return BalanceOf(_earned, _spent); } // net: callable-ok: read only
        public int EarnedPoints() { return _earned; } // net: callable-ok: read only
        public int SpentPoints() { return _spent; } // net: callable-ok: read only
        public int Version() { return _version; } // net: callable-ok: read only
        public int OwnedCount() { int n=0; if (_owned!=null) for(int i=0;i<_owned.Length;i++) if(_owned[i] && _ownValid[i]) n++; return n; } // net: callable-ok: read only
        public string DataKey(string kind, string key) { return storageNamespace + "." + kind + (string.IsNullOrEmpty(key) ? "" : "." + key); } // net: callable-ok: key construction only
        public int IndexOfProduct(string key) // net: callable-ok: read only
        {
            if (string.IsNullOrEmpty(key) || products == null) return -1;
            for (int i=0;i<products.Length;i++) if(products[i]!=null && products[i].productKey==key) return i;
            return -1;
        }
        public bool IsUnlocked(string key) // net: callable-ok: read only
        { int i=IndexOfProduct(key); return _loaded && i>=0 && _ownValid[i] && _owned[i]; }
        public bool IsUnlockedFor(VRCPlayerApi player, string key) // net: callable-ok: read only
        {
            int i=IndexOfProduct(key);
            if (!Utilities.IsValid(player) || i<0 || products[i].consumable || products[i].IsCE() || products[i].IsFree()) return false;
            if(player.isLocal) return IsUnlocked(key);
            bool owned;
            return PlayerData.TryGetBool(player, DataKey("Own", key), out owned) && owned;
        }
        public bool CanAfford(int price) // net: callable-ok: read only
        { return _loaded && _earnedValid && _spentValid && price>0 && Points()>=price && _spent<=MaxPoints-price; }
        public bool CanBuyUnlock(string key,int price) // net: callable-ok: read only
        {
            int i=IndexOfProduct(key);
            return i>=0 && CanAfford(price) && _ownValid[i] && !_owned[i] && !products[i].consumable && !products[i].IsCE() && products[i].price==price;
        }
        public int Shortfall(int price) { return Mathf.Max(0,price-Points()); } // net: callable-ok: read only
        public int StockOf(string key) // net: callable-ok: read only
        { int i=IndexOfProduct(key); return _loaded && i>=0 && _stockValid[i] && products[i].IsStocked() ? StockOfWith(_stockBought[i],_stockEaten[i]) : 0; }
        public bool StockSeeded(string key) // net: callable-ok: read only
        { int i=IndexOfProduct(key); return _loaded && i>=0 && _stockValid[i] && _stockSeeded[i] && products[i].IsStocked(); }
        public bool CanBuyStocked(string key,int price,int count,int cap) // net: callable-ok: read only
        {
            int i=IndexOfProduct(key);
            if(i<0 || !CanAfford(price) || !_stockValid[i]) return false;
            var item=products[i];
            return item.IsStocked() && !item.IsCE() && item.price==price && item.packCount==count && item.stockCap==cap && _stockBought[i]<MaxPoints && CanBuyStockedWith(_earned,_spent,_stockBought[i],_stockEaten[i],price,count,cap);
        }
        public int BalanceOf(int earned,int spent) { return Mathf.Max(0,ClampPoints(earned)-ClampPoints(spent)); } // net: callable-ok: pure
        public int StockOfWith(int bought,int eaten) { return BalanceOf(bought,eaten); } // net: callable-ok: pure
        public bool CanBuyStockedWith(int earned,int spent,int bought,int eaten,int price,int count,int cap) // net: callable-ok: pure
        { return price>0 && count>0 && (cap<=0 || StockOfWith(bought,eaten)<cap) && BalanceOf(earned,spent)>=price; }
    }
}
