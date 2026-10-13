using UdonSharp;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-DOG-050: the leash pattern a player chose (one per player, IMP-DOG-048 answer 2), carried to everyone else.
    //
    //   what        a VRCPlayerObject template (KH_Dogs/KH_CollarTag). VRChat clones it for every player on join and that
    //               player owns the clone, so the owner is always the player whose choice it carries. Same shape as
    //               KD_PlayerFxTag (petting effects); a separate object because that tag's Verify pins exactly one synced
    //               variable and each GameObject can hold only one sync mode.
    //   state       [UdonSynced] string _selected = the product key from the local catalog's "collar" selection
    //               (PlayerData Sel.collar is the persistent source; this is only the broadcast copy). "" = not published yet.
    //   trust       readers never trust the value: KH_CollarLeash checks that the key is a collar product and that the
    //               holder owns it (KD_ShopItem.OwnedBy reads that player's ledger) before drawing it, else the free default.
    //   leaving     the clone is destroyed with its player; the rope of a dog that player held is hidden by the leash anyway.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_CollarTag : UdonSharpBehaviour
    {
        public const string Unset = "";
        public const string ObjectName = "KH_CollarTag";

        [UdonSynced] private string _selected = Unset;

        /// <summary>Owner (= the player of this clone) only. Sends only when the value changes.</summary>
        public void _Publish(string productKey)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (productKey == null) productKey = Unset;
            if (_selected == productKey) return;
            _selected = productKey;
            RequestSerialization();
        }

        // net: callable-ok: read only
        public string Selected() { return _selected; }
    }
}
