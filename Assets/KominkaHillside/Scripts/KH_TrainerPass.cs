using UdonSharp;
using UnityEngine;
using VRC.Economy;
using VRC.SDKBase;

namespace KominkaHillside
{
    // Trainer entitlement shared with KominkaDogRun: same Product (ownership) and Listing (purchase page).
    // Ownership is judged once purchases have loaded; the store is the source of truth, nothing is stored here.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_TrainerPass : UdonSharpBehaviour
    {
        public UdonProduct product;
        public string listingId = "";
        private bool _loaded;
        private int[] _owners = new int[0];
        private int _ownerCount;

        public bool _Loaded() { return _loaded; }
        public bool _Configured() { return product != null && listingId != null && listingId.Length > 0; }

        // net: callable-ok: read-only entitlement of an authenticated player
        public bool _OwnedBy(VRCPlayerApi player)
        {
            if (product == null || !Utilities.IsValid(player)) return false;
            if (Has(player.playerId)) return true;
            if (player.isLocal && !_loaded) return false;
            if (!Store.DoesPlayerOwnProduct(player, product)) return false;
            Note(player);
            return true;
        }

        public void _OpenStore() { if (_Configured()) Store.OpenListing(listingId); }

        public override void OnPurchasesLoaded(IProduct[] products, VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.isLocal) _loaded = true;
            if (products == null || product == null) return;
            for (int i = 0; i < products.Length; i++) if (products[i] != null && products[i].ID == product.ID) Note(player);
        }
        public override void OnPurchaseConfirmed(IProduct p, VRCPlayerApi player, bool purchasedNow) { if (p != null && product != null && p.ID == product.ID) Note(player); }
        public override void OnPurchaseConfirmedMultiple(IProduct p, VRCPlayerApi player, bool purchasedNow, int quantity) { if (p != null && product != null && p.ID == product.ID) Note(player); }
        // Expiry is judged on entry (same policy as KominkaDogRun); mid-stay expiry is not revoked here.
        public override void OnPurchaseExpired(IProduct p, VRCPlayerApi player) { }
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return;
            for (int i = 0; i < _ownerCount; i++) if (_owners[i] == player.playerId) { _owners[i] = _owners[_ownerCount - 1]; _ownerCount--; return; }
        }

        private bool Has(int id) { for (int i = 0; i < _ownerCount; i++) if (_owners[i] == id) return true; return false; }
        private void Note(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || Has(player.playerId)) return;
            if (_ownerCount >= _owners.Length)
            {
                int[] grown = new int[_owners.Length + 8];
                for (int i = 0; i < _ownerCount; i++) grown[i] = _owners[i];
                _owners = grown;
            }
            _owners[_ownerCount++] = player.playerId;
        }
    }
}
