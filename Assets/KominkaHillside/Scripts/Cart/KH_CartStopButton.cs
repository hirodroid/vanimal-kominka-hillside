using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M1: 肘掛け前端のボタン (子 GameObject、BoxCollider、Interact)。kind 0 = 停止して降りる、1 = 合流の受付切替。
    /// このカートの乗員 (受付はマスター) の画面でだけ Interact を出す (KH_LeashCollar._SetReleasable と同じく DisableInteractive と
    /// Collider の両方を切る)。文言は実行時に KD_Language から入れる。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_CartStopButton : UdonSharpBehaviour
    {
        public KH_CartManager manager;
        public KominkaDogRun.KD_Language language;
        public int kind;   // 0 = 停止 → manager._Leave()、1 = 受付切替 → manager._ToggleAccepting()

        private int _on = -1;          // -1 = まだ書いていない
        private bool _accepting = true;
        private Collider _hit;

        void Start()
        {
            _RefreshText();
            if (_on < 0) _SetOn(false);   // manager が先に書いていればそのまま (Start の順は保証されない)
        }

        public override void Interact()
        {
            if (manager == null) return;
            if (kind == 0) manager._Leave();
            else manager._ToggleAccepting();
        }

        public void _SetOn(bool on)
        {
            int want = on ? 1 : 0;
            if (want == _on) return;
            _on = want;
            DisableInteractive = !on;
            if (_hit == null) _hit = GetComponent<Collider>();
            if (_hit != null) _hit.enabled = on;
        }

        public void _SetAccepting(bool on)
        {
            if (on == _accepting) return;
            _accepting = on;
            _RefreshText();
        }

        public void _RefreshText()
        {
            if (language == null) return;
            if (kind == 0) InteractionText = language._Text("cart.stop");
            else InteractionText = language._Text("cart.accepting") + ": " + language._Text(_accepting ? "cart.accepting.on" : "cart.accepting.off");
        }
    }
}
