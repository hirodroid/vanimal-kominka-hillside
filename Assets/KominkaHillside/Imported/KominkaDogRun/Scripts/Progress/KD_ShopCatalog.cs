using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
namespace KominkaDogRun
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_ShopCatalog : UdonSharpBehaviour
    {
        public const string NoSelection = "";
        public KD_ProgressLedger ledger;
        public KD_ShopItem[] items;
        public string[] categoryKeys;
        public string[] defaultProducts;
        public UdonSharpBehaviour[] consumers;
        public string[] consumerEvents;
        private string[] _selected, _savedRaw, _selectionKeys;
        private bool[] _userChose, _selectionValid;
        private bool _restoreNotified, _loaded, _resolved, _retryScheduled;
        private int _version, _dbgSelectCount, _dbgFallbackCount;
        private string _changedCategory = "";
        private void Start() { _ResolveRestored(); }
        private bool EnsureArrays()
        {
            if (_selected != null) return true;
            if (ledger==null || categoryKeys==null || defaultProducts==null || categoryKeys.Length!=defaultProducts.Length || categoryKeys.Length==0 || items==null || items.Length==0) return false;
            if (items[0] == null) return false;
            for (int c=0;c<categoryKeys.Length;c++)
            {
                if (!items[0].ValidKey(categoryKeys[c],false)) return false;
                for(int prior=0;prior<c;prior++) if(categoryKeys[prior]==categoryKeys[c]) return false;
                if(!string.IsNullOrEmpty(defaultProducts[c]))
                {
                    int d=IndexOfProduct(defaultProducts[c]);
                    if(d<0 || items[d].categoryKey!=categoryKeys[c] || !items[d].IsStarter()) return false;
                }
            }
            for(int i=0;i<items.Length;i++) if(items[i]==null || IndexOfCategory(items[i].categoryKey)<0) return false;
            ledger._ConfigureCatalog(items);
            if (!ledger.CatalogReady()) return false;
            int n=categoryKeys.Length;
            _selected=new string[n]; _savedRaw=new string[n]; _selectionKeys=new string[n]; _userChose=new bool[n]; _selectionValid=new bool[n];
            for(int i=0;i<n;i++) { _selected[i]=""; _savedRaw[i]=""; _selectionKeys[i]=ledger.DataKey("Sel",categoryKeys[i]); _selectionValid[i]=true; }
            return true;
        }
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if(!Utilities.IsValid(player)||!player.isLocal||_restoreNotified) return;
            _restoreNotified=true; _ResolveRestored();
        }
        public void _ResolveRestored()
        {
            bool wired=EnsureArrays();
            if(!wired || !_restoreNotified || !ledger.Loaded())
            { ScheduleResolve(); return; }
            if(_resolved) return;
            if(!_loaded)
            {
                for(int c=0;c<categoryKeys.Length;c++)
                {
                    bool has=PlayerData.HasKey(Networking.LocalPlayer,_selectionKeys[c]);
                    string value="";
                    _selectionValid[c]=!has || PlayerData.TryGetString(Networking.LocalPlayer,_selectionKeys[c],out value);
                    _savedRaw[c]=_selectionValid[c] && has ? value : "";
                    if(!_selectionValid[c]) Debug.LogError("[KD_Commerce] Invalid selection type: "+categoryKeys[c]);
                }
                _loaded=true;
            }
            for(int i=0;i<items.Length;i++) if(items[i].initialStock>0) ledger._GrantInitialStockOnce(items[i].productKey,items[i].initialStock);
            for(int c=0;c<categoryKeys.Length;c++)
            {
                if(_userChose[c]) continue;
                _selected[c]=ResolveSelection(_savedRaw[c],categoryKeys[c],DefaultOf(categoryKeys[c]));
                if(_selected[c]!=_savedRaw[c]) _dbgFallbackCount++;
            }
            _resolved=true; _version++;
            // Restore only the local cursor. Shared toys must not change on late join.
        }
        private void ScheduleResolve()
        { if(_retryScheduled || _resolved) return; _retryScheduled=true; SendCustomEventDelayedSeconds(nameof(_RetryResolve),.5f); }
        public void _RetryResolve() { _retryScheduled=false; _ResolveRestored(); }
        public bool _RequestSelect(string productKey)
        {
            if(!EnsureArrays() || !_resolved || !ledger.Loaded()) return false;
            int i=IndexOfProduct(productKey); if(i<0) return false;
            var item=items[i]; int c=IndexOfCategory(item.categoryKey);
            if(c<0 || !_selectionValid[c] || !item.Owned()) return false;
            bool same=_selected[c]==productKey; _selected[c]=productKey; _userChose[c]=true;
            if(!same) { _dbgSelectCount++; _version++; PlayerData.SetString(_selectionKeys[c],productKey); }
            NotifyConsumers(item.categoryKey); return true;
        }
        public bool _RequestBuy(string productKey)
        {
            if(!_resolved) return false;
            int i=IndexOfProduct(productKey); if(i<0) return false;
            int c=IndexOfCategory(items[i].categoryKey); if(c<0 || !_selectionValid[c]) return false;
            if(!items[i]._Buy()) return false;
            _RequestSelect(productKey); return true;
        }
        private void NotifyConsumers(string category)
        {
            _changedCategory=category;
            if(consumers!=null && consumerEvents!=null)
                for(int i=0;i<consumers.Length && i<consumerEvents.Length;i++)
                    if(consumers[i]!=null && !string.IsNullOrEmpty(consumerEvents[i])) consumers[i].SendCustomEvent(consumerEvents[i]);
            _changedCategory="";
        }
        public int IndexOfCategory(string key) // net: callable-ok: read only
        { if(categoryKeys!=null) for(int i=0;i<categoryKeys.Length;i++) if(categoryKeys[i]==key) return i; return -1; }
        public string SelectedProductOf(string categoryKey) // net: callable-ok: read only
        { int c=IndexOfCategory(categoryKey); return _selected==null || c<0 ? NoSelection : _selected[c]; }
        public int SelectionVersion() { return _version; } // net: callable-ok: read only
        public bool Loaded() { return _loaded; } // net: callable-ok: read only
        public bool Resolved() { return _resolved; } // net: callable-ok: read only
        public string ChangedCategory() { return _changedCategory; } // net: callable-ok: read only
        public int ItemCount() { return items==null ? 0 : items.Length; } // net: callable-ok: read only
        public int IndexOfProduct(string key) // net: callable-ok: read only
        { if(string.IsNullOrEmpty(key)||items==null) return -1; for(int i=0;i<items.Length;i++) if(items[i]!=null && items[i].productKey==key) return i; return -1; }
        public string DefaultOf(string category) // net: callable-ok: read only
        {
            int c=IndexOfCategory(category); if(c<0 || defaultProducts==null || c>=defaultProducts.Length) return NoSelection;
            int i=IndexOfProduct(defaultProducts[c]);
            return i>=0 && items[i].categoryKey==category && items[i].Owned() ? items[i].productKey : NoSelection;
        }
        public string ResolveSelection(string savedKey,string category,string fallback) // net: callable-ok: read only
        { int i=IndexOfProduct(savedKey); return i>=0 && items[i].categoryKey==category && items[i].Owned() ? savedKey : fallback; }
    }
}
