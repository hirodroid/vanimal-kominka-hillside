using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-UI-013: the ばにまるトレーナー board in front of the gate (KH_GuideBoards/Trainer, built by Guide/1). The face is read-only;
    // this behaviour sits on its operation strip (Trainer/Actions) and drives the parts that change:
    //   - the membership line (Status) and the one button (join, or "joined" and disabled for a member), from KH_TrainerPass as the hand
    //     menu's 「あそぶ」 judges it (_Configured / _Loaded / _OwnedBy(LocalPlayer)) and from KD_Language.CurrentLanguage;
    //   - [join] opens the trainer Listing (KH_TrainerPass._OpenStore). 2026-10-16 user decision: the pass cannot be gifted, so a member's
    //     button only says so (no gift path).
    // The cove's URL is a read-only copy field on the strip (no portal: the cove is a Private world, 2026-10-16 user decision); no code here.
    // Local only: no synced variables, no ownership, no network events (the `_` prefix keeps every public event off the network).
    // One _Tick chain: Start arms it once and every _Tick re-arms it exactly once (never two outstanding, never none). Near the board the
    // body runs every NearTickSeconds; farther than NearMeters it runs about every FarBodySeconds. A press re-reads at once (Poke).
    // The texts are rewritten only when the state or the language changed.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_TrainerBoard : UdonSharpBehaviour
    {
        public const int StateUnconfigured = 0, StateLoading = 1, StateOff = 2, StateOn = 3;

        // the dynamic words (UITranslations.json, 13 languages); the ja fallback is used while a key is missing from KD_Language's tables
        public const string KeyLoading = "guide.trainer.state.loading", KeyOff = "guide.trainer.state.off", KeyOn = "guide.trainer.state.on",
            KeyUnconfigured = "guide.trainer.state.unconfigured", KeyJoin = "guide.trainer.join", KeyJoined = "guide.trainer.joined";
        public const string JaLoading = "確認中…", JaOff = "未加入", JaOn = "ここでも、入り江でも使えます", JaUnconfigured = "準備中",
            JaJoin = "トレーナーになる", JaJoined = "加入済み";

        [Tooltip("KH_Dogs/KH_TrainerPass (the same Product / Listing as the hand menu)")]
        public KH_TrainerPass pass;
        public KominkaDogRun.KD_Language language;
        public TextMeshProUGUI status, storeLabel;
        public Button storeButton;

        const float NearMeters = 12f, NearTickSeconds = 0.5f, FarBodySeconds = 2f;
        private float _farUntil;
        private int _shownState = -1;
        private string _shownLanguage = "?";

        private void Start()
        {
            Poll();
            SendCustomEventDelayedSeconds(nameof(_Tick), NearTickSeconds);
        }

        public void _Tick()
        {
            if (Time.time >= _farUntil)
            {
                Poll();
                _farUntil = IsFar() ? Time.time + FarBodySeconds - 0.05f : 0f;
            }
            SendCustomEventDelayedSeconds(nameof(_Tick), NearTickSeconds);
        }

        void Poke() { _farUntil = 0f; }

        bool IsFar()
        {
            var lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return true;
            Vector3 d = lp.GetPosition() - transform.position; d.y = 0f;
            return d.sqrMagnitude > NearMeters * NearMeters;
        }

        void Poll()
        {
            int state = ReadState();
            string code = language != null ? language.CurrentLanguage : "ja";
            if (state == _shownState && code == _shownLanguage) return;
            _shownState = state; _shownLanguage = code;
            _Render(state, code);
        }

        int ReadState()
        {
            if (pass == null) return StateUnconfigured;
            bool configured = pass._Configured();
            bool loaded = configured && pass._Loaded();
            bool owned = loaded && pass._OwnedBy(Networking.LocalPlayer);
            return _StateFrom(configured, loaded, owned);
        }

        /// <summary>The state from what KH_TrainerPass reports (pure: Guide/2 checks it in Edit mode).</summary>
        public int _StateFrom(bool configured, bool loaded, bool owned)
        {
            if (!configured) return StateUnconfigured;
            if (!loaded) return StateLoading;
            return owned ? StateOn : StateOff;
        }

        /// <summary>Writes the membership line and the button for a state in a language (Guide/2 calls it in Edit mode for ja / en and restores
        /// the scene values afterwards). The button opens the purchase page only while checking or not a member.</summary>
        public void _Render(int state, string code)
        {
            bool on = state == StateOn;
            if (status != null)
                status.text = state == StateUnconfigured ? Tx(KeyUnconfigured, JaUnconfigured, code)
                    : state == StateLoading ? Tx(KeyLoading, JaLoading, code)
                    : on ? Tx(KeyOn, JaOn, code) : Tx(KeyOff, JaOff, code);
            if (storeLabel != null) storeLabel.text = on ? Tx(KeyJoined, JaJoined, code) : Tx(KeyJoin, JaJoin, code);
            if (storeButton != null) storeButton.interactable = _ButtonOn(state);
        }

        /// <summary>The button opens the purchase page: checking or not a member (not unconfigured, not a member already).</summary>
        public bool _ButtonOn(int state) { return state == StateLoading || state == StateOff; }

        string Tx(string key, string ja, string code)
        {
            if (language == null) return ja;
            string s = language._TextForLanguage(key, code);
            return s == null || s.Length == 0 || s == key ? ja : s;
        }

        // ---------------------------------------------------------------- the button (Trainer/Actions/StoreButton)

        /// <summary>[join]: the trainer Listing. Opening it is never taken as a purchase (the Store events bring ownership back). A member's
        /// button is disabled; the state is re-read here too, so a press that slips through does nothing.</summary>
        public void _OpenStore()
        {
            Poke();
            if (pass == null || !_ButtonOn(ReadState())) return;
            pass._OpenStore();
        }
    }
}
