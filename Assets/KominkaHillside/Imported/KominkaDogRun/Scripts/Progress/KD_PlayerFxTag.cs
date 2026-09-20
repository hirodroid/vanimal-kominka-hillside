using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    // PlayerObject carries selection only. Readers validate the authenticated player and ownership.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KD_PlayerFxTag : UdonSharpBehaviour
    {
        public const string Unset = "";
        [UdonSynced] private string _selectedFx = Unset;
        public void _Publish(string productId)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (_selectedFx == productId) return;   // 同じ値なら送らない
            _selectedFx = productId;
            RequestSerialization();
        }
        // net: callable-ok: 読み取りのみ — 遠隔から呼ばれても状態が動かない
        public string SelectedFx() { return _selectedFx; }
    }
}
