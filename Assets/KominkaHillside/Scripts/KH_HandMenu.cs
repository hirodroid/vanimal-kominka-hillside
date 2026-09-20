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
            if (_eyeCooldown <= 0) { float eye = lp.GetAvatarEyeHeightAsMeters(); if (eye > .1f) _scale = eye / referenceEyeHeight; _eyeCooldown = 60; }
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
            Note(hand == 0 ? "左につなぎます…" : "右につなぎます…");
            Draw();
        }
        private void Detach(int dog)
        {
            if (manager == null || !manager._IsMine(dog)) { Note("自分がつないでいる犬だけ外せます"); Draw(); return; }
            manager._RequestDetach(dog);
            Note("外します…");
            Draw();
        }
        private void ChooseDestination(int destination)
        {
            if (manager == null) return;
            manager._ChooseDestination(destination);
            int mine = 0;
            for (int i = 0; i < manager.walkers.Length; i++) if (manager._IsMine(i)) mine++;
            Note(mine == 0 ? "行き先をおぼえました。つないだ犬に伝えます" : "つないでいる " + mine + " 匹に伝えました");
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
            if (!Trainer()) { Note(pass != null && pass._Configured() && !pass._Loaded() ? "購入情報を読み込み中です" : "トレーナー特典です"); Draw(); return; }
            if (activities.Mode != 0) { Note("開催中です"); Draw(); return; }
            if (GatherCount() == 0) { Note("集合できる犬がいません (つないでいる犬・遊んでいる犬は対象外)"); Draw(); return; }
            activities._Request(1, _shape, 0);
            Note("集合を頼みました…");
            Draw();
        }
        public void _GatherEnd()
        {
            if (activities == null || activities.Mode == 0) return;
            activities._Request(0, 0, 0);
            Note("終わります…");
            Draw();
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
        private void Toy(int row)
        {
            if (recall == null) return;
            int gate = recall._Gate(row);
            if (gate != KH_ToyRecall.Ok) { Note(ToyText(gate)); Draw(); return; }
            _toyRow = row;
            recall._Recall(row);
            Note("手元へ呼びます…");
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
            bool preview = visible && _tab == 1 && Trainer() && activities != null && activities.Mode == 0;
            if (markers != null) for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] == null) continue;
                markers[i].SetActive(preview);
                if (!preview) continue;
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (!Utilities.IsValid(lp)) continue;
                Vector3 p = lp.GetPosition() + lp.GetRotation() * activities.Offset(_shape, i);
                RaycastHit hit;
                if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out hit, 4f, groundMask, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
                markers[i].transform.position = p + Vector3.up * .02f;
                float d = activities.MarkerDiameter(i);
                markers[i].transform.localScale = new Vector3(d, .005f, d);
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
                else if (leashed)
                {
                    VRCPlayerApi holder = VRCPlayerApi.GetPlayerById(manager.walkers[i]);
                    state = (Utilities.IsValid(holder) ? holder.displayName : "だれか") + " が散歩中";
                }
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
            bool loading = pass != null && pass._Configured() && !pass._Loaded();
            int mode = activities.Mode;
            string status;
            if (!pass._Configured()) status = "集合は準備中です";
            else if (loading) status = "購入情報を読み込み中…";
            else if (!trainer) status = "集合はトレーナー特典です";
            else if (mode == 1)
            {
                int left = Mathf.Max(0, 60 - Mathf.FloorToInt(activities.Elapsed));
                VRCPlayerApi host = VRCPlayerApi.GetPlayerById(activities.HostId);
                status = (Utilities.IsValid(host) ? host.displayName : "だれか") + " の集合中 (" + activities.ParticipantCount + " 匹・あと " + left + " 秒)";
            }
            else if (mode != 0) status = "ほかの遊びを開催中です";
            else status = "集合できる犬: " + GatherCount() + " 匹 (つないでいる犬・遊んでいる犬は対象外)";
            if (gatherStatus != null) gatherStatus.text = status;
            if (gatherNote != null) gatherNote.text = NoteOrDefault(trainer ? "隊形を選び、足元の印を見て「ここに集合」。坂でも並びます。1 分で解散し、その場で自由行動に戻ります" : "古民家で買った特典はここでも使えます");
            for (int i = 0; i < 6; i++) Tint(formImages, i, i == _shape ? toggleOn : trainer ? actionNeutral : disabled);
            if (startImage != null) startImage.color = trainer && mode == 0 && GatherCount() > 0 ? actionPrimary : disabled;
            VRCPlayerApi lp = Networking.LocalPlayer;
            bool canEnd = mode != 0 && Utilities.IsValid(lp) && activities.HostId == lp.playerId;
            if (endImage != null) endImage.color = canEnd ? actionNeutral : disabled;
            if (buyImage != null) { buyImage.gameObject.SetActive(!trainer && !loading && pass._Configured()); buyImage.color = actionPrimary; }
        }
        private void DrawToys()
        {
            if (recall == null) return;
            for (int i = 0; i < 6; i++)
            {
                int gate = recall._Gate(i);
                string state = gate == KH_ToyRecall.Ok ? "呼べる" : gate == KH_ToyRecall.Held ? "だれかが持っている" : gate == KH_ToyRecall.DogBusy ? "犬が遊んでいる" : gate == KH_ToyRecall.Pending ? "呼んでいる途中" : gate == KH_ToyRecall.Cooldown ? "少し待つ" : "使えない";
                if (toyState != null && i < toyState.Length && toyState[i] != null) toyState[i].text = state;
                Tint(toyImages, i, gate == KH_ToyRecall.Ok ? actionPrimary : disabled);
            }
            int result = recall._LastResult();
            string fallback = "ボールは 1 個ずつ、目の前の地面に置きます";
            if (_toyRow >= 0 && recall._LastRow() == _toyRow && result >= 0)
            {
                fallback = result == 0 ? "目の前に置きました" : result == 1 ? "いまは呼べませんでした" : "うまくいきませんでした。もう一度どうぞ";
                if (Time.time >= _noteUntil) { _note = fallback; _noteUntil = Time.time + 3f; _toyRow = -1; }
            }
            if (toyNote != null) toyNote.text = NoteOrDefault(fallback);
        }
        private void Tint(Image[] images, int i, Color color)
        {
            if (images == null || i >= images.Length || images[i] == null) return;
            images[i].color = color;
        }
    }
}
