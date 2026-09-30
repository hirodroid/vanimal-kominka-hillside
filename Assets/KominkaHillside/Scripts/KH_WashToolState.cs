using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-DOG-025: the one synced bit of a wash tool (the shower is spraying). It lives on a child GameObject because
    // the tool itself carries VRCObjectSync (Continuous) and one GameObject cannot mix sync modes (vrc-networking §6).
    // A Pickup does not move this child's ownership, so the holder takes it before writing.
    //
    // The holder's wish is kept locally (_want) and written only while this client is the Owner: at once when it
    // already is, otherwise when OnOwnershipTransferred confirms the transfer (vrc-networking §4: do not rely on the
    // write right after SetOwner). The holder shows its own spray from the input without waiting (KH_WashTool).
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_WashToolState : UdonSharpBehaviour
    {
        public KH_WashTool washTool;
        [UdonSynced] public bool spraying;

        private bool _want;
        private bool _pending;

        // Local only: the holder of the tool calls this (Use down / up, drop), or the Owner clears a spray whose
        // holder is gone.
        public void _SetSpraying(bool value)
        {
            _want = value;
            if (Networking.IsOwner(gameObject))
            {
                _pending = false;
                Write();
                return;
            }
            if (spraying == value && !_pending) return;   // nothing to change: do not take the ownership for it
            // A request is already on its way: the newest wish is written when it lands. Repeated SetOwner calls in
            // a short time fight and roll back (vrc-networking §4), so tapping Use asks once. There is no automatic
            // retry: a request that did not land (someone else took it) is asked again only by a call made 3 s or
            // more after it (the next Use down / up or drop).
            if (_pending && Time.time - _askedAt < 3f) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            _pending = true;
            _askedAt = Time.time;
            Networking.SetOwner(lp, gameObject); // net: setowner-ok: tool state only (the holder writes); written after the transfer is confirmed
        }

        private float _askedAt = -100f;

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!_pending) return;
            if (!Networking.IsOwner(gameObject)) return;   // someone else took it; the wish stays pending until the next call
            _pending = false;
            Write();
        }

        void Write()
        {
            if (spraying == _want) return;
            spraying = _want;                              // net: owner-only
            RequestSerialization();
            if (washTool != null) washTool._OnStateChanged();
        }

        public override void OnDeserialization()
        {
            if (washTool != null) washTool._OnStateChanged();
        }
    }
}
