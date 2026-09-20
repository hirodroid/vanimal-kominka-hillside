using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaHillside
{
    // Hand menu for the hillside: same opening gestures, board and look as KominkaDogRun's KD_TreatPouch
    // (VR right stick down held / Desktop T / Touch launcher; the board sits where it was opened until
    // [閉じる]), with three hillside tabs: さんぽ (attach/detach + destination), あそぶ (trainer gathering),
    // おもちゃ (call one ball). Everything here is local; shared state is requested through the
    // authorities (KH_LeashManager, KD_GroupActivities) and read back for display.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_HandMenu : UdonSharpBehaviour
    {
        public KH_LeashManager manager;
        public KominkaDogRun.KD_GroupActivities activities;
        public KH_TrainerPass pass;
        public KH_ToyRecall recall;
        public Transform[] dogRoots;
        public GameObject panelRoot;
        public GameObject launcher;            // Touch only: screen-space [メニュー]
        public GameObject[] pages;             // 0 walk, 1 gather, 2 toys
        public Image[] tabImages;
        public GameObject[] tabUnderlines;
        public TextMeshProUGUI titleText;
        [Header("さんぽ")]
        public TextMeshProUGUI[] walkState;    // 6
        public Image[] walkLeft, walkRight, walkOff;
        public Image destPlazaImage, destGateImage;
        public TextMeshProUGUI destStatus;
        [Header("あそぶ")]
        public TextMeshProUGUI gatherStatus, gatherNote;
        public Image[] formImages;             // 6
        public Image startImage, endImage, buyImage;
        public GameObject[] markers;           // 6 ground discs previewing the chosen formation
        [Header("おもちゃ")]
        public TextMeshProUGUI[] toyState;     // 6
        public Image[] toyImages;
        public Image[] discImages;             // 6 (IMP-DOG-005)
        public TextMeshProUGUI toyNote;
        [Header("Look (KominkaDogRun tokens)")]
        public Color actionPrimary = new Color(.35f, .50f, .30f);
        public Color actionEquip = new Color(.30f, .42f, .52f);
        public Color actionNeutral = new Color(.34f, .30f, .28f);
        public Color disabled = new Color(.22f, .22f, .22f);
        public Color tabOn = new Color(.42f, .36f, .26f);
        public Color tabOff = new Color(.20f, .18f, .16f);
        public Color toggleOn = new Color(.62f, .50f, .24f);
        [Header("Opening")]
        public float panelDistance = .95f;
        public float panelHeightOffset = -.05f;
        public float referenceEyeHeight = 1.6f;
        public float openHoldSeconds = .6f;
        public float openStickThreshold = .6f;
        public KeyCode desktopToggleKey = KeyCode.T;
        public LayerMask groundMask = (1 << 0) | (1 << 11);
        [Header("Text")]
        public string[] dogNames;              // 6, Japanese display names (Build writes them)
        public string[] formationNames;        // 6
        private int _tab;
        private int _shape;
        private float _stickY, _stickSince = -1f;
        private bool _stickLatched;
        private int _inputMethod = -1;
        private int _inputPoll;
        private float _scale = 1f;
        private int _eyeCooldown;
        private float _nextDraw;
        private float _noteUntil;
        private string _note = "";
        private int _toyRow = -1;
        private int _toyKind = 0;
        // Requests are confirmed against the shared state (attach/detach/destination) or the authority's reply (gather).
        private int _pendingKind;              // 0 none, 1 attach, 2 detach, 3 destination, 4 gather start, 5 gather end
        private int _pendingDog, _pendingHand, _pendingDest;
        private float _pendingAt;
        private float _nextMarkers;

        // ---- opening ----
        public void _Toggle() { OpenOrRecall(); }
        public void _Close() { if (panelRoot != null) panelRoot.SetActive(false); }
        private void OpenOrRecall()
        {
            if (panelRoot == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (!panelRoot.activeSelf) panelRoot.SetActive(true);
            PlaceInFront(lp);
            Draw();
        }
        private void PlaceInFront(VRCPlayerApi lp)
        {
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 fwd = head.rotation * Vector3.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) { fwd = lp.GetRotation() * Vector3.forward; fwd.y = 0f; }
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd = fwd.normalized;
            Vector3 want = head.position + fwd * (panelDistance * _scale) + Vector3.up * (panelHeightOffset * _scale);
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            transform.position = want;
            transform.position += want - panelRoot.transform.position;
        }
        public override void InputLookVertical(float value, VRC.Udon.Common.UdonInputEventArgs args)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || !lp.IsUserInVR()) return;
            _stickY = value;
        }
        public override void OnInputMethodChanged(VRCInputMethod inputMethod) { _inputMethod = (int)inputMethod; ApplyInputMode(); }
        private void ApplyInputMode() { if (launcher != null) launcher.SetActive(_inputMethod == (int)VRCInputMethod.Touch); }
        private void LateUpdate()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            // Distance follows eye height like KominkaDogRun, clamped so tiny/huge avatars keep the board usable.
            if (_eyeCooldown <= 0) { float eye = lp.GetAvatarEyeHeightAsMeters(); if (eye > .1f) _scale = Mathf.Clamp(eye / referenceEyeHeight, .75f, 1.5f); _eyeCooldown = 60; }
            _eyeCooldown--;
            if (_inputPoll <= 0) { _inputMethod = (int)InputManager.GetLastUsedInputMethod(); ApplyInputMode(); _inputPoll = 90; }
            _inputPoll--;
            if (lp.IsUserInVR()) { StepStickHold(); return; }
            if (Input.GetKeyDown(desktopToggleKey)) { if (panelRoot != null && panelRoot.activeSelf) _Close(); else OpenOrRecall(); }
        }
        private void StepStickHold()
        {
            bool down = -_stickY >= openStickThreshold;
            if (!down) { _stickSince = -1f; _stickLatched = false; return; }
            if (_stickLatched) return;
            if (_stickSince < 0f) { _stickSince = Time.time; return; }
            if (Time.time - _stickSince < openHoldSeconds) return;
            _stickLatched = true;
            OpenOrRecall();
        }

        // ---- tabs ----
        public void _Tab0() { _tab = 0; Draw(); }
        public void _Tab1() { _tab = 1; Draw(); }
        public void _Tab2() { _tab = 2; Draw(); }

        // ---- さんぽ ----
        public void _Left0() { Attach(0, 0); } public void _Left1() { Attach(1, 0); } public void _Left2() { Attach(2, 0); }
        public void _Left3() { Attach(3, 0); } public void _Left4() { Attach(4, 0); } public void _Left5() { Attach(5, 0); }
        public void _Right0() { Attach(0, 1); } public void _Right1() { Attach(1, 1); } public void _Right2() { Attach(2, 1); }
        public void _Right3() { Attach(3, 1); } public void _Right4() { Attach(4, 1); } public void _Right5() { Attach(5, 1); }
        public void _Off0() { Detach(0); } public void _Off1() { Detach(1); } public void _Off2() { Detach(2); }
        public void _Off3() { Detach(3); } public void _Off4() { Detach(4); } public void _Off5() { Detach(5); }
        public void _DestPlaza() { ChooseDestination(1); }
        public void _DestGate() { ChooseDestination(0); }

        // Reasons mirror the authority's rules so a rejection never comes as a surprise (the Owner re-judges anyway).
        private int AttachGate(int dog, int hand)
        {
            if (manager == null || dog < 0 || dog >= manager.walkers.Length) return 9;
            if (manager._IsLeashed(dog)) return manager._IsMine(dog) ? 1 : 2; // 1 mine, 2 someone else's
            if (manager._IsGathering(dog)) return 3;
            if (manager.fetchers[dog] == null || !manager.fetchers[dog].IsIdle) return 4;
            if (manager._MyCount(hand) >= 3) return 5;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (dogRoots == null || dogRoots[dog] == null || !Utilities.IsValid(lp)) return 9;
            if (Vector3.Distance(lp.GetPosition(), dogRoots[dog].position) > manager.attachReach) return 6;
            return 0;
        }
        private void Attach(int dog, int hand)
        {
            int gate = AttachGate(dog, hand);
            if (gate != 0) { Note(GateText(gate, hand)); Draw(); return; }
            manager._RequestAttach(dog, hand);
            Pending(1, dog, hand, -1);
            Note(hand == 0 ? "左につなぎます…" : "右につなぎます…");
            Draw();
        }
        private void Detach(int dog)
        {
            if (manager == null || !manager._IsMine(dog)) { Note("自分がつないでいる犬だけ外せます"); Draw(); return; }
            manager._RequestDetach(dog);
            Pending(2, dog, -1, -1);
            Note("外します…");
            Draw();
        }
        private void ChooseDestination(int destination)
        {
            if (manager == null) return;
            manager._ChooseDestination(destination);
            int mine = 0;
            for (int i = 0; i < manager.walkers.Length; i++) if (manager._IsMine(i)) mine++;
            if (mine == 0) Note("行き先をおぼえました。つないだ犬に伝えます");
            else { Pending(3, -1, -1, destination); Note("つないでいる " + mine + " 匹に伝えます…"); }
            Draw();
        }
        private string GateText(int gate, int hand)
        {
            if (gate == 1) return "もうつないでいます";
            if (gate == 2) return "ほかの人がつないでいます";
            if (gate == 3) return "集合中は待ってください";
            if (gate == 4) return "ボールで遊んでいます";
            if (gate == 5) return (hand == 0 ? "左手" : "右手") + "は 3 匹までです";
            if (gate == 6) return "もう少し近づいてください (4 m)";
            return "いまは使えません";
        }

        // ---- あそぶ ----
        public void _Form0() { _shape = 0; Draw(); } public void _Form1() { _shape = 1; Draw(); } public void _Form2() { _shape = 2; Draw(); }
        public void _Form3() { _shape = 3; Draw(); } public void _Form4() { _shape = 4; Draw(); } public void _Form5() { _shape = 5; Draw(); }
        public void _GatherStart()
        {
            if (activities == null) return;
            if (!Trainer()) { Note(pass != null && pass._Configured() && !pass._Loaded() ? "購入情報を読み込み中です" : "集合はトレーナー特典です。購入画面からどうぞ"); Draw(); return; }
            if (activities.Mode != 0) { Note("開催中です"); Draw(); return; }
            if (GatherCount() == 0) { Note("集合できる犬がいません (つないでいる犬・遊んでいる犬は対象外)"); Draw(); return; }
            activities._Request(1, _shape, 0);
            Pending(4, -1, -1, -1);
            Note("集合を頼みました…");
            Draw();
        }
        public void _GatherEnd()
        {
            if (activities == null || activities.Mode == 0) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || activities.HostId != lp.playerId) { Note("終われるのは集合を始めた人です"); Draw(); return; }
            activities._Request(0, 0, 0);
            Pending(5, -1, -1, -1);
            Note("終わります…");
            Draw();
        }
        private void Pending(int kind, int dog, int hand, int dest) { _pendingKind = kind; _pendingDog = dog; _pendingHand = hand; _pendingDest = dest; _pendingAt = Time.time; }
        // Resolves the outstanding request from shared state; a request nobody answered within 4 s is reported as unknown.
        private void PollPending()
        {
            if (_pendingKind == 0) return;
            bool done = false, ok = false; string text = "";
            if (_pendingKind == 1) { ok = manager._IsMine(_pendingDog) && manager.hands[_pendingDog] == _pendingHand; done = ok; text = _pendingHand == 0 ? "左手につなぎました" : "右手につなぎました"; }
            else if (_pendingKind == 2) { ok = !manager._IsLeashed(_pendingDog); done = ok; text = "外しました"; }
            else if (_pendingKind == 3)
            {
                bool all = true, any = false;
                for (int i = 0; i < manager.walkers.Length; i++) if (manager._IsMine(i)) { any = true; if (manager.destinations[i] != _pendingDest) all = false; }
                ok = any && all; done = ok; text = _pendingDest == 1 ? "広場へ向かいます" : "古民家側へ帰ります";
            }
            else if (_pendingKind == 4 || _pendingKind == 5)
            {
                int state = activities.LocalRequestState; // 1 waiting, 2 accepted, 3 rejected, 4 timed out
                if (state == 2) { done = true; ok = true; text = _pendingKind == 4 ? "集合を始めました" : "集合を終えました"; }
                else if (state == 3) { done = true; text = _pendingKind == 4 ? "ここには並べません (斜面が急すぎる・物がある・犬がいない・開催中)" : "終えられませんでした"; }
                else if (state == 4) { done = true; text = "応答がありませんでした"; }
            }
            if (!done && Time.time - _pendingAt > 4f) { done = true; text = "結果を確認できませんでした。表示を見てもう一度どうぞ"; }
            if (!done) return;
            _pendingKind = 0;
            Note(text);
        }
        public void _Buy() { if (pass != null) pass._OpenStore(); }
        private bool Trainer() { return pass != null && pass._Configured() && pass._OwnedBy(Networking.LocalPlayer); }
        private int GatherCount()
        {
            int n = 0;
            for (int i = 0; i < 6; i++) if (activities.Available(i)) n++;
            return n;
        }

        // ---- おもちゃ ----
        public void _Toy0() { Toy(0); } public void _Toy1() { Toy(1); } public void _Toy2() { Toy(2); }
        public void _Toy3() { Toy(3); } public void _Toy4() { Toy(4); } public void _Toy5() { Toy(5); }
        private void Toy(int row) { Toy(row, KH_ToyRecall.KindBall); }
        public void _Disc0() { Toy(0, KH_ToyRecall.KindDisc); } public void _Disc1() { Toy(1, KH_ToyRecall.KindDisc); } public void _Disc2() { Toy(2, KH_ToyRecall.KindDisc); }
        public void _Disc3() { Toy(3, KH_ToyRecall.KindDisc); } public void _Disc4() { Toy(4, KH_ToyRecall.KindDisc); } public void _Disc5() { Toy(5, KH_ToyRecall.KindDisc); }
        private void Toy(int row, int kind)
        {
            if (recall == null) return;
            int gate = recall._GateToy(row, kind);
            if (gate != KH_ToyRecall.Ok) { Note(gate == KH_ToyRecall.Unwired && kind == KH_ToyRecall.KindDisc ? "この子のフリスビーはまだです" : ToyText(gate)); Draw(); return; }
            _toyRow = row; _toyKind = kind;
            recall._RecallToy(row, kind);
            Note("目の前へ呼びます…");
            Draw();
        }
        private string ToyText(int gate)
        {
            if (gate == KH_ToyRecall.Held) return "だれかが持っています";
            if (gate == KH_ToyRecall.DogBusy) return "犬が遊んでいます";
            if (gate == KH_ToyRecall.Cooldown) return "少し待ってください";
            if (gate == KH_ToyRecall.Pending) return "呼んでいる途中です";
            return "いまは使えません";
        }

        // ---- drawing ----
        private void Note(string text) { _note = text; _noteUntil = Time.time + 4f; }
        private void Update()
        {
            bool visible = panelRoot != null && panelRoot.activeInHierarchy;
            if (visible) PollPending();
            bool preview = visible && _tab == 1 && Trainer() && activities != null && activities.Mode == 0;
            // Markers: 10 Hz, only for dogs that would join, only where ground exists (a missing disc = no slot there).
            if (markers != null && (!preview || Time.time >= _nextMarkers))
            {
                _nextMarkers = Time.time + .1f;
                VRCPlayerApi lp = Networking.LocalPlayer;
                for (int i = 0; i < markers.Length; i++)
                {
                    if (markers[i] == null) continue;
                    bool show = preview && Utilities.IsValid(lp) && activities.Available(i);
                    Vector3 p = Vector3.zero; RaycastHit hit;
                    if (show)
                    {
                        p = lp.GetPosition() + lp.GetRotation() * activities.Offset(_shape, i);
                        if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out hit, 4f, groundMask, QueryTriggerInteraction.Ignore)) p.y = hit.point.y; else show = false;
                    }
                    markers[i].SetActive(show);
                    if (!show) continue;
                    markers[i].transform.position = p + Vector3.up * .02f;
                    float d = activities.MarkerDiameter(i);
                    markers[i].transform.localScale = new Vector3(d, .005f, d);
                }
            }
            if (!visible || Time.time < _nextDraw) return;
            _nextDraw = Time.time + .25f;
            Draw();
        }
        private void Draw()
        {
            if (panelRoot == null || !panelRoot.activeInHierarchy) return;
            for (int i = 0; i < pages.Length; i++) if (pages[i] != null) pages[i].SetActive(i == _tab);
            for (int i = 0; i < tabImages.Length; i++) if (tabImages[i] != null) tabImages[i].color = i == _tab ? tabOn : tabOff;
            if (tabUnderlines != null) for (int i = 0; i < tabUnderlines.Length; i++) if (tabUnderlines[i] != null) tabUnderlines[i].SetActive(i == _tab);
            if (_tab == 0) DrawWalk(); else if (_tab == 1) DrawGather(); else DrawToys();
        }
        private string NoteOrDefault(string fallback) { return Time.time < _noteUntil ? _note : fallback; }
        private void DrawWalk()
        {
            if (manager == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            int mine = 0, plaza = 0, gate = 0;
            for (int i = 0; i < 6; i++)
            {
                bool leashed = manager._IsLeashed(i);
                bool isMine = manager._IsMine(i);
                string state;
                if (isMine) { state = manager.hands[i] == 0 ? "左手につないでいる" : "右手につないでいる"; mine++; if (manager.destinations[i] == 1) plaza++; else gate++; }
                else if (leashed) state = "ほかの人が散歩中"; // no display names: the static font cannot render arbitrary glyphs
                else if (manager._IsGathering(i)) state = "集合中";
                else if (manager.fetchers[i] != null && !manager.fetchers[i].IsIdle) state = "ボールで遊び中";
                else
                {
                    float d = dogRoots != null && dogRoots[i] != null && Utilities.IsValid(lp) ? Vector3.Distance(lp.GetPosition(), dogRoots[i].position) : 999f;
                    state = d > manager.attachReach ? "自由行動 (" + Mathf.RoundToInt(d) + " m 先)" : "近くにいる";
                }
                if (walkState != null && i < walkState.Length && walkState[i] != null) walkState[i].text = state;
                bool canLeft = AttachGate(i, 0) == 0, canRight = AttachGate(i, 1) == 0;
                Tint(walkLeft, i, canLeft ? actionEquip : disabled);
                Tint(walkRight, i, canRight ? actionEquip : disabled);
                Tint(walkOff, i, isMine ? actionNeutral : disabled);
            }
            int choice = manager._MyChoice();
            if (destPlazaImage != null) destPlazaImage.color = choice == 1 ? toggleOn : actionNeutral;
            if (destGateImage != null) destGateImage.color = choice == 0 ? toggleOn : actionNeutral;
            string dest;
            if (mine == 0) dest = "つないでいる犬はいません。行き先は次につなぐ犬に伝わります";
            else if (plaza > 0 && gate > 0) dest = "行き先がまざっています (広場 " + plaza + " / 古民家側 " + gate + ")";
            else dest = mine + " 匹: " + (plaza > 0 ? "広場へ" : "古民家側へ");
            if (destStatus != null) destStatus.text = NoteOrDefault(dest);
        }
        private void DrawGather()
        {
            if (activities == null) return;
            bool trainer = Trainer();
            bool configured = pass != null && pass._Configured();
            bool loading = configured && !pass._Loaded();
            int mode = activities.Mode;
            string status;
            VRCPlayerApi me = Networking.LocalPlayer;
            bool hosting = mode != 0 && Utilities.IsValid(me) && activities.HostId == me.playerId;
            if (mode == 1)
            {
                int left = Mathf.Max(0, 60 - Mathf.FloorToInt(activities.Elapsed));
                status = (hosting ? "あなた" : "ほかの人") + "の集合中 (" + activities.ParticipantCount + " 匹・あと " + left + " 秒)";
            }
            else if (mode != 0) status = "ほかの遊びを開催中です";
            else if (!configured) status = "集合は準備中です";
            else if (loading) status = "購入情報を読み込み中…";
            else if (!trainer) status = "集合はトレーナー特典です";
            else status = "集合できる犬: " + GatherCount() + " 匹 (つないでいる犬・遊んでいる犬は対象外)";
            if (gatherStatus != null) gatherStatus.text = status;
            if (gatherNote != null) gatherNote.text = NoteOrDefault(trainer ? "隊形を選び、足元の印を見て「ここに集合」。印が出ない枠はそこに並べません。坂でも並びます。1 分で解散し、その場で自由行動に戻ります" : "古民家で買った特典はここでも使えます");
            int lit = mode == 1 ? activities.Formation : _shape;
            for (int i = 0; i < 6; i++) Tint(formImages, i, i == lit ? toggleOn : trainer && mode == 0 ? actionNeutral : disabled);
            if (startImage != null) startImage.color = trainer && mode == 0 && GatherCount() > 0 ? actionPrimary : disabled;
            if (endImage != null) endImage.color = hosting ? actionNeutral : disabled;
            if (buyImage != null) { buyImage.gameObject.SetActive(!trainer && !loading && configured); buyImage.color = actionPrimary; }
        }
        private void DrawToys()
        {
            if (recall == null) return;
            for (int i = 0; i < 6; i++)
            {
                int gate = recall._Gate(i);
                int discGate = recall._GateToy(i, KH_ToyRecall.KindDisc);
                string state = "ボール: " + ShortState(gate) + " ／ フリスビー: " + (discGate == KH_ToyRecall.Unwired ? "まだ" : ShortState(discGate));
                if (toyState != null && i < toyState.Length && toyState[i] != null) toyState[i].text = state;
                Tint(toyImages, i, gate == KH_ToyRecall.Ok ? actionPrimary : disabled);
                Tint(discImages, i, discGate == KH_ToyRecall.Ok ? actionPrimary : disabled);
            }
            int result = recall._LastResult();
            string fallback = "おもちゃは 1 つずつ、目の前の地面に置きます。フリスビーは持つと軌道が見え、Use で飛びます";
            if (_toyRow >= 0 && recall._LastRow() == _toyRow && recall._LastKind() == _toyKind && result >= 0)
            {
                fallback = result == 0 ? "目の前に置きました" : result == 1 ? "いまは呼べませんでした" : "うまくいきませんでした。もう一度どうぞ";
                if (Time.time >= _noteUntil) { _note = fallback; _noteUntil = Time.time + 3f; _toyRow = -1; }
            }
            if (toyNote != null) toyNote.text = NoteOrDefault(fallback);
        }
        private string ShortState(int gate)
        {
            if (gate == KH_ToyRecall.Ok) return "呼べる";
            if (gate == KH_ToyRecall.Held) return "持たれ中";
            if (gate == KH_ToyRecall.DogBusy) return "遊び中";
            if (gate == KH_ToyRecall.Pending) return "呼び中";
            if (gate == KH_ToyRecall.Cooldown) return "少し待つ";
            return "使えない";
        }
        private void Tint(Image[] images, int i, Color color)
        {
            if (images == null || i >= images.Length || images[i] == null) return;
            images[i].color = color;
        }
    }
}
