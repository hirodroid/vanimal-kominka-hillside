using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-ENV-011 / IMP-DOG-035 (docs/plans/2026-10-01-wash-call-and-height.md §4): the board on a wash station's shelf.
    // Local only: every button is a String Persistent Listener (SendCustomEvent) wired by the builder to one handler here,
    // which calls the station's local entry (KH_WashStationCall._Call / _Release / _Lift, KH_WashPersonalFloor._Adjust).
    // The Call decides and syncs; nothing here is synced and there is no network event.
    //
    // IMP-DOG-037 (plan §7): each dog button shows whether that dog can be called now and, if not, why - from
    // KH_WashStationCall._Availability, the same rule the Owner accepts by. Three looks (colour + the second line's words, never
    // the colour alone): callable / at this station (on its way, on the stand, or my call waiting) / not now + the reason. The
    // range rule is written under the status line. Pressing a dog that cannot come shows the reason at once (and the request
    // still goes to the Owner, whose refusal names the same reason). Every word comes from KD_Language (13 languages, the
    // keys wash.*, dog names shared with the hand menu); without it the builder's Japanese stays.
    //
    // IMP-DOG-039 / IMP-UI-003 (docs/plans/2026-10-03-hand-wash.md §7, §13.11): the 4-step picture guide above the board (a
    // sibling `Guide`, read only) takes its words from here too (W 24..37). Steps ② ③ and the bottom line depend on the device:
    // VR shows "let go of the bottle" / "both hands" (picture cells guideUvVr2 / guideUvVr3), Desktop / phone "take the sponge" /
    // "scrub with the sponge" (guideUvFlat2 / guideUvFlat3). Written at Start and on each language load; the guide may be
    // missing (older scene) - every field is skipped when null.
    //
    // Cost: the refresh runs every refreshSeconds only while the local player is within showMeters of the board (otherwise one
    // distance check per farCheckSeconds); a label is written only when its text or look changed.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WashStationBoard : UdonSharpBehaviour
    {
        public KH_WashStationCall call;
        public KH_WashPersonalFloor personalFloor;
        [Tooltip("文言の正本 (KD_TreatPouch.language)。null なら words / dogNames (ビルダーが書く日本語) のまま")]
        public KominkaDogRun.KD_Language language;
        public TextMeshProUGUI statusText;   // line 1: what is happening now
        public TextMeshProUGUI ruleText;     // the call range rule
        public TextMeshProUGUI heightText;   // the stand's and my floor's height
        [Tooltip("犬のボタンの背景 Image (dogIndex の順、6)")] public Image[] dogButtons;
        [Tooltip("犬のボタンの 1 行目 (名前)")] public TextMeshProUGUI[] dogNameLabels;
        [Tooltip("犬のボタンの 2 行目 (状態・理由)")] public TextMeshProUGUI[] dogStateLabels;
        [Tooltip("おわり・台 上へ・台 下へ・自分 上へ・自分 下へ のラベル (この順)")] public TextMeshProUGUI[] buttonLabels;
        [Tooltip("犬の名前のキー (dogIndex の順)")] public string[] dogNameKeys;
        [Tooltip("犬の名前 (日本語。language が無い時)")] public string[] dogNames;
        [Tooltip("文言のキー (W* の順)")] public string[] wordKeys;
        [Tooltip("文言 (日本語。language が無い時、キーが無い時)")] public string[] words;
        public Color colorCallable = new Color(0.35f, 0.50f, 0.30f);
        public Color colorHere = new Color(0.30f, 0.42f, 0.52f);
        public Color colorUnavailable = new Color(0.20f, 0.18f, 0.16f);
        public Color inkName = Color.white;
        public Color inkNameDim = new Color(0.78f, 0.75f, 0.70f);
        public Color inkState = new Color(0.92f, 0.92f, 0.88f);
        public Color inkReason = new Color(0.84f, 0.68f, 0.32f);
        [Tooltip("断られた知らせを出す秒数")]
        public float refuseShowSeconds = 3f;
        public float refreshSeconds = 0.25f;
        public float showMeters = 12f;
        [Tooltip("遠い時に近づいたかを見る間隔 (秒)")]
        public float farCheckSeconds = 1f;
        [Tooltip("押した犬を「呼んでいます」と出す上限 (秒)。受理・拒否・おわりで先に消える")]
        public float myPickSeconds = 8f;
        [Tooltip("図解のラベル: Step1・Sub1・Step2・Sub2・Step3・Sub3・Step4・Sub4・Help (この順、9)。null なら図解なし")]
        public TextMeshProUGUI[] guideLabels;
        [Tooltip("図解の絵 ①②③④ (4)。②③ の uvRect を端末で替える")]
        public RawImage[] guidePics;
        [Tooltip("② の絵の区画 (VR: ボトルをはなす)")]
        public Rect guideUvVr2 = new Rect(0.25f, 0.5f, 0.25f, 0.5f);
        [Tooltip("② の絵の区画 (Desktop / スマホ: スポンジを持つ)")]
        public Rect guideUvFlat2 = new Rect(0f, 0f, 0.25f, 0.5f);
        [Tooltip("③ の絵の区画 (VR: 両手で洗う)")]
        public Rect guideUvVr3 = new Rect(0.5f, 0.5f, 0.25f, 0.5f);
        [Tooltip("③ の絵の区画 (Desktop / スマホ: スポンジでこする)")]
        public Rect guideUvFlat3 = new Rect(0.25f, 0f, 0.25f, 0.5f);
        [Tooltip("ハーネス専用: true で図解を VR の表示にする (_ApplyGuide で反映)。通常は false")]
        public bool debugVr;

        // Word indices (wordKeys / words; the builder writes both in this order)
        public const int WPick = 0, WRule = 1, WCalling = 2, WHere = 3, WNameReason = 4, WBusy = 5, WHeights = 6;
        public const int WStCallable = 7, WStComing = 8, WStHere = 9, WStCalling = 10, WStLeashedOther = 11, WStOtherStation = 12,
            WStTooFar = 13, WStFetch = 14, WStFrisbee = 15, WStGathering = 16, WStSleepCall = 17, WStWashedOther = 18;
        public const int WEnd = 19, WStandUp = 20, WStandDown = 21, WMeUp = 22, WMeDown = 23;
        public const int WGuideStep1 = 24, WGuideStep1Sub = 25, WGuideStep2 = 26, WGuideStep2Sub = 27, WGuideStep2Flat = 28,
            WGuideStep2FlatSub = 29, WGuideStep3 = 30, WGuideStep3Sub = 31, WGuideStep3Flat = 32, WGuideStep3FlatSub = 33,
            WGuideStep4 = 34, WGuideStep4Sub = 35, WGuideHelp = 36, WGuideHelpFlat = 37;
        public const int WordCount = 38;
        // Looks of a dog button
        public const int LookCallable = 0, LookHere = 1, LookUnavailable = 2;

        private float _nextAt;
        private string _shown = "";
        private int _seenRefuse = -1;
        private float _refuseUntil = -100f;
        private string _refuseText = "";
        // Codex review 7: _PendingDog() is the Call Owner's local value, so a non-Owner who pressed a busy dog saw nothing for
        // up to callWaitSeconds. The dog I pressed is kept here until it is held, refused, ended, or myPickSeconds pass.
        private int _myPick = -1;
        private float _myPickUntil;
        private int _myPickSerial;
        // IMP-DOG-037: the words in the current language, and what each dog button shows (written only on change)
        private string[] _w;
        private string[] _names;
        private string _lang = "?";
        private bool _wordsDirty = true;
        private int[] _dogState;     // _Availability per dog at the last refresh (harness read-out)
        private int[] _dogLook;      // Look* per dog
        private int[] _dogWord;      // W* shown on the second line per dog
        private string _shownRule = "", _shownHeights = "";
        private float _shownRange = -1f;
        // VER-ENV-002 C wave 2: the inputs of the last built height line / status line. The same inputs and the same words (LoadWords
        // clears both keys) give the same string, so the ToString / Replace work is skipped; the label writes were already change-only.
        private bool _heightsKeyValid;
        private float _heightsStand, _heightsMe;
        private int _stWord = -1, _stDog = -1;
        private string _stText = "";
        // VER-ENV-002 C wave 2 (exception, display only): within showMeters the refresh waits QuietRefreshSeconds instead of
        // refreshSeconds while the last refresh found nothing in progress here (no held dog, no pending call, no press of mine, no
        // refusal on show). Every press still sets _nextAt = 0 (the next frame refreshes), and a busy board keeps refreshSeconds.
        // Only a passive change (another player leashes / throws for / gathers a dog, a remote lift) can show up to 0.25 s later.
        const float QuietRefreshSeconds = 0.5f;
        private bool _quiet;

        public void _Dog0() { DoCall(0); }
        public void _Dog1() { DoCall(1); }
        public void _Dog2() { DoCall(2); }
        public void _Dog3() { DoCall(3); }
        public void _Dog4() { DoCall(4); }
        public void _Dog5() { DoCall(5); }
        public void _End() { _myPick = -1; if (call != null) call._Release(); _nextAt = 0f; }
        public void _LiftUp() { if (call != null) call._Lift(1); _nextAt = 0f; }
        public void _LiftDown() { if (call != null) call._Lift(-1); _nextAt = 0f; }
        public void _MeUp() { if (personalFloor != null) personalFloor._Adjust(1); _nextAt = 0f; }
        public void _MeDown() { if (personalFloor != null) personalFloor._Adjust(-1); _nextAt = 0f; }

        void Start()
        {
            int n = dogButtons != null ? dogButtons.Length : 0;
            _dogState = new int[n]; _dogLook = new int[n]; _dogWord = new int[n];
            for (int i = 0; i < n; i++) { _dogState[i] = -1; _dogLook[i] = -1; _dogWord[i] = -1; }
            _ApplyGuide();   // the builder's Japanese until the first LoadWords
        }

        // ================= words =================

        string W(int i)
        {
            if (_w != null && i >= 0 && i < _w.Length && !string.IsNullOrEmpty(_w[i])) return _w[i];
            return words != null && i >= 0 && i < words.Length ? words[i] : "";
        }

        string DogName(int dog)
        {
            if (_names != null && dog >= 0 && dog < _names.Length && !string.IsNullOrEmpty(_names[dog])) return _names[dog];
            return dogNames != null && dog >= 0 && dog < dogNames.Length ? dogNames[dog] : dog.ToString();
        }

        /// <summary>The current language's words (KD_Language._Text; a key it does not know comes back as the key itself, then the
        /// builder's Japanese is used). Runs when the language changed, i.e. once after Start and on each switch.</summary>
        void LoadWords()
        {
            int n = words != null ? words.Length : 0;
            _w = new string[n];
            for (int i = 0; i < n; i++)
            {
                string key = wordKeys != null && i < wordKeys.Length ? wordKeys[i] : "";
                string t = language != null && !string.IsNullOrEmpty(key) ? language._Text(key) : "";
                _w[i] = string.IsNullOrEmpty(t) || t == key ? words[i] : t;
            }
            int m = dogNames != null ? dogNames.Length : 0;
            _names = new string[m];
            for (int i = 0; i < m; i++)
            {
                string key = dogNameKeys != null && i < dogNameKeys.Length ? dogNameKeys[i] : "";
                string t = language != null && !string.IsNullOrEmpty(key) ? language._Text(key) : "";
                _names[i] = string.IsNullOrEmpty(t) || t == key ? dogNames[i] : t;
            }
            // the fixed button labels and the dog names (the second lines follow on the next refresh: everything is redrawn)
            for (int i = 0; buttonLabels != null && i < buttonLabels.Length; i++)
                if (buttonLabels[i] != null) buttonLabels[i].text = W(WEnd + i);
            for (int i = 0; dogNameLabels != null && i < dogNameLabels.Length; i++)
                if (dogNameLabels[i] != null) dogNameLabels[i].text = DogName(i);
            for (int i = 0; _dogWord != null && i < _dogWord.Length; i++) { _dogWord[i] = -1; _dogLook[i] = -1; }
            _shownRange = -1f; _shownHeights = ""; _shown = "";
            _heightsKeyValid = false; _stWord = -1; _stDog = -1;   // VER-ENV-002 C wave 2: the words changed, rebuild both lines
            _wordsDirty = false;
            _ApplyGuide();
        }

        /// <summary>IMP-DOG-039 / IMP-UI-003: writes the picture guide's labels and pictures ② ③'s cells for this device (VR: let go
        /// of the bottle / both hands, otherwise take the sponge / scrub with it; debugVr forces VR for the harness). Local only; the
        /// harness flips debugVr and calls this again.</summary>
        public void _ApplyGuide()
        {
            bool vr = debugVr;
            if (!vr)
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                vr = Utilities.IsValid(lp) && lp.IsUserInVR();
            }
            SetGuideLabel(0, WGuideStep1);
            SetGuideLabel(1, WGuideStep1Sub);
            SetGuideLabel(2, vr ? WGuideStep2 : WGuideStep2Flat);
            SetGuideLabel(3, vr ? WGuideStep2Sub : WGuideStep2FlatSub);
            SetGuideLabel(4, vr ? WGuideStep3 : WGuideStep3Flat);
            SetGuideLabel(5, vr ? WGuideStep3Sub : WGuideStep3FlatSub);
            SetGuideLabel(6, WGuideStep4);
            SetGuideLabel(7, WGuideStep4Sub);
            SetGuideLabel(8, vr ? WGuideHelp : WGuideHelpFlat);
            if (guidePics == null) return;
            if (guidePics.Length > 1 && guidePics[1] != null) guidePics[1].uvRect = vr ? guideUvVr2 : guideUvFlat2;
            if (guidePics.Length > 2 && guidePics[2] != null) guidePics[2].uvRect = vr ? guideUvVr3 : guideUvFlat3;
        }

        void SetGuideLabel(int slot, int word)
        {
            if (guideLabels == null || slot >= guideLabels.Length || guideLabels[slot] == null) return;
            string t = W(word);
            if (!string.IsNullOrEmpty(t)) guideLabels[slot].text = t;   // no word yet (the builder has not written it): keep the label
        }

        // ================= press =================

        void DoCall(int dog)
        {
            if (call != null)
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                int a = Utilities.IsValid(lp) ? call._Availability(dog, lp.playerId) : KH_WashStationCall.AvCallable;
                if (a == KH_WashStationCall.RefuseLeashedByOther || a == KH_WashStationCall.RefuseOtherStation || a == KH_WashStationCall.RefuseTooFar)
                {
                    // IMP-DOG-037: the reason at once on this screen. The request still goes: the Owner applies the same rule and is
                    // the one that counts (its refusal shows the same words; if this screen was a moment behind, it accepts)
                    _myPick = -1;
                    ShowRefusal(dog, StateWord(a));
                }
                else
                {
                    _myPick = dog;
                    _myPickUntil = Time.time + myPickSeconds;
                    _myPickSerial = call._RefuseSerial();
                }
                call._Call(dog);
            }
            _nextAt = 0f;
        }

        void ShowRefusal(int dog, int word)
        {
            _refuseText = W(WNameReason).Replace("{0}", DogName(dog)).Replace("{1}", W(word));
            _refuseUntil = Time.time + refuseShowSeconds;
        }

        /// <summary>The second line's word for an availability (_Availability), or -1 when it is not a "cannot / not now" state.</summary>
        int StateWord(int a)
        {
            if (a == KH_WashStationCall.RefuseLeashedByOther) return WStLeashedOther;
            if (a == KH_WashStationCall.RefuseOtherStation) return WStOtherStation;
            if (a == KH_WashStationCall.RefuseTooFar) return WStTooFar;
            if (a == KH_WashStationCall.AvFetch) return WStFetch;
            if (a == KH_WashStationCall.AvFrisbee) return WStFrisbee;
            if (a == KH_WashStationCall.AvGathering) return WStGathering;
            if (a == KH_WashStationCall.AvSleepCall) return WStSleepCall;
            if (a == KH_WashStationCall.AvWashedByOther) return WStWashedOther;
            return -1;
        }

        // ================= refresh =================

        void Update()
        {
            if (Time.time < _nextAt) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { _nextAt = Time.time + farCheckSeconds; return; }
            Vector3 d = lp.GetPosition() - transform.position; d.y = 0f;
            if (d.sqrMagnitude > showMeters * showMeters) { _nextAt = Time.time + farCheckSeconds; return; }
            _nextAt = Time.time + refreshSeconds;
            if (statusText == null || call == null) return;

            string lang = language != null ? language.CurrentLanguage : "";
            if (lang != _lang) { _lang = lang; _wordsDirty = true; }
            if (_wordsDirty) LoadWords();

            RefreshDogs(lp.playerId);
            string s = StatusLine();
            if (s != _shown) { _shown = s; statusText.text = s; }
            RefreshRule();
            RefreshHeights();
            if (_quiet && QuietRefreshSeconds > refreshSeconds) _nextAt = Time.time + QuietRefreshSeconds;
        }

        void RefreshDogs(int pid)
        {
            int n = _dogState != null ? _dogState.Length : 0;
            int held = call._HeldDog();
            bool arrived = call._Arrived();
            for (int i = 0; i < n; i++)
            {
                int a = call._Availability(i, pid);
                _dogState[i] = a;
                int look, word;
                if (a == KH_WashStationCall.AvHeldHere) { look = LookHere; word = i == held && arrived ? WStHere : WStComing; }
                else if (a == KH_WashStationCall.AvCallable) { look = i == _myPick ? LookHere : LookCallable; word = i == _myPick ? WStCalling : WStCallable; }
                else if (a == KH_WashStationCall.RefuseInvalid) { look = LookUnavailable; word = -1; }
                else { look = LookUnavailable; word = StateWord(a); }
                if (look == _dogLook[i] && word == _dogWord[i]) continue;
                _dogLook[i] = look; _dogWord[i] = word;
                if (dogButtons != null && i < dogButtons.Length && dogButtons[i] != null)
                    dogButtons[i].color = look == LookCallable ? colorCallable : look == LookHere ? colorHere : colorUnavailable;
                if (dogNameLabels != null && i < dogNameLabels.Length && dogNameLabels[i] != null)
                    dogNameLabels[i].color = look == LookUnavailable ? inkNameDim : inkName;
                if (dogStateLabels != null && i < dogStateLabels.Length && dogStateLabels[i] != null)
                {
                    dogStateLabels[i].text = word >= 0 ? W(word) : "";
                    dogStateLabels[i].color = look == LookUnavailable ? inkReason : inkState;
                }
            }
        }

        string StatusLine()
        {
            string head = W(WPick);
            // a refusal (KH_WashStationCall._RefuseSerial moved) is shown for refuseShowSeconds
            int serial = call._RefuseSerial();
            if (_seenRefuse < 0) _seenRefuse = serial;   // what was refused before I came is not news
            else if (serial != _seenRefuse)
            {
                _seenRefuse = serial;
                int why = call._RefusedReason();
                int rd = call._RefusedDog();
                if (why == KH_WashStationCall.RefuseBusy)
                {
                    // the wait ran out: name what keeps the dog busy when this screen still sees it, else the general words
                    int now = rd >= 0 && _dogState != null && rd < _dogState.Length ? _dogState[rd] : -1;
                    int word = StateWord(now);
                    ShowRefusal(rd, word >= 0 ? word : WBusy);
                }
                else if (why == KH_WashStationCall.RefuseLeashedByOther || why == KH_WashStationCall.RefuseTooFar || why == KH_WashStationCall.RefuseOtherStation)
                    ShowRefusal(rd, StateWord(why));
            }
            int dog = call._HeldDog();
            int pending = call._PendingDog();
            if (_myPick >= 0 && (dog == _myPick || serial != _myPickSerial || Time.time > _myPickUntil)) _myPick = -1;   // accepted / refused / timed out
            bool refusing = Time.time < _refuseUntil;
            _quiet = !refusing && dog < 0 && pending < 0 && _myPick < 0;   // VER-ENV-002 C wave 2: see QuietRefreshSeconds
            if (refusing) head = _refuseText;
            else if (dog >= 0 && dog != _myPick) head = StatusName(call._Arrived() ? WHere : WCalling, dog);
            else if (_myPick >= 0) head = StatusName(WCalling, _myPick);
            else if (pending >= 0) head = StatusName(WCalling, pending);
            return head;
        }

        /// <summary>VER-ENV-002 C wave 2: W(word) with {0} = the dog's name, rebuilt only when the word or the dog changed
        /// (LoadWords clears the key, so a language switch rebuilds it).</summary>
        string StatusName(int word, int dog)
        {
            if (word == _stWord && dog == _stDog) return _stText;
            _stWord = word; _stDog = dog;
            _stText = W(word).Replace("{0}", DogName(dog));
            return _stText;
        }

        void RefreshRule()
        {
            if (ruleText == null) return;
            float r = call.callRange;
            if (r == _shownRange) return;
            _shownRange = r;
            string t = r > 0f ? W(WRule).Replace("{0}", Mathf.RoundToInt(r).ToString()) : "";
            if (t != _shownRule) { _shownRule = t; ruleText.text = t; }
        }

        void RefreshHeights()
        {
            if (heightText == null) return;
            float stand = call._LiftMeters();
            float me = personalFloor != null ? personalFloor._TargetHeight() : 0f;
            if (_heightsKeyValid && stand == _heightsStand && me == _heightsMe) return;   // VER-ENV-002 C wave 2: same numbers, same words
            _heightsKeyValid = true; _heightsStand = stand; _heightsMe = me;
            string t = W(WHeights).Replace("{0}", stand.ToString("F1")).Replace("{1}", me.ToString("F1"));
            if (t != _shownHeights) { _shownHeights = t; heightText.text = t; }
        }
    }
}
