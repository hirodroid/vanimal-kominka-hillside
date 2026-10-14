using UdonSharp;
using UnityEngine;
using VRC.Economy;

namespace KominkaHillside
{
    // IMP-UI-008: the VAnimal group poster on the farmhouse wall (KH_WallPosters/GroupPoster/Interact), the same one Kominka Dog Run
    // shows (Data/Panfret/poster.png). Interact opens the VRChat group page for the player who pressed it. Kominka uses MimyLab's
    // GroupPageOpener (a third-party asset kept out of its repository); this is the same one call, written here so that nothing
    // third-party is copied. Local only: no synced variables, no ownership, no network events.
    // Store.OpenGroupPage is in the SDK's Udon wrapper (VRC.Udon.VRCWrapperModules: __OpenGroupPage__SystemString__SystemVoid,
    // next to OpenListing which KH_TrainerPass already uses). Whether the page opens in the real client is a Play / device check.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_GroupPoster : UdonSharpBehaviour
    {
        [Tooltip("The VRChat group id (grp_...). Written by Tools/KominkaHillside/Guide/20 (the same id as Kominka Dog Run's poster).")]
        public string groupId = "";

        public override void Interact()
        {
            if (string.IsNullOrEmpty(groupId))
            {
                Debug.LogError("[KH_GroupPoster] groupId is empty (run Guide/20)");
                return;
            }
            Store.OpenGroupPage(groupId);
        }
    }
}
