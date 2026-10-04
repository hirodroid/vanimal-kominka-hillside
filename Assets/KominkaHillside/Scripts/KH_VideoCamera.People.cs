using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008 W4: filming another person (design §5, §13, §14; §14 wins). The F side of the permission, in the camera's terms.
    ///
    /// - The person picker (じぶん mode only): [人: name] opens a list of the instance (me first, then the others by playerId, frozen while open;
    ///   Quest / phone players can be chosen too, §14.1). The list reads names and ids only: **no position of another player** (the camera files
    ///   read another player's pose only in TryPersonPose; so the list is not in distance order like the join list, design §7.2).
    ///   A player who cannot be asked now (not accepting, not ready, the 30 s wait, my own pending join / call) is dimmed and shows why; pressing
    ///   it shows the reason and never opens the confirmation (§14.3).
    /// - The confirmation (§14.3): 「(S) さんに撮影の許可を依頼しますか?」[依頼する] [やめる]. Local only: nothing is sent and no sequence number is
    ///   used while it is up. [依頼する] is ignored for 0.5 s after it appears, re-checks that S is still here and askable, ends the current session
    ///   (a target change, §13.3) and only then calls my mailbox's _FilmRequest. [やめる] goes back to the list. Choosing myself needs no permission.
    ///   Closing the monitor, another mode or S leaving closes it.
    /// - Following another person (PersonFollow): TryPersonPose on every tick. Without the permission the mode frames the dog (犬追従 of the
    ///   selected dog) and the picker says why. When the permission ends, the picture is detached at once, every cached pose is dropped and the
    ///   dog framing starts with a cut (no 3 s hold: that is for a lost dog only, §13.3). The same happens right before enabling the camera
    ///   if the session is no longer in use (the re-check before drawing).
    /// - Ending the session: × / しまう, choosing another person or myself, [取り消す] / [やめる] on the picker, or 60 s in another mode (§5.4).
    /// - おまかせ (W5) does not exist yet. When it comes, it may use only a person whose session is already in use and never opens the
    ///   confirmation or sends a request by itself (§14.3).
    /// Texts are Japanese constants (13 languages in W5). Names use the display-name font (Video/4 wires it).
    /// </summary>
    public partial class KH_VideoCamera
    {
        public const string WordPerson = "人: ";
        public const string WordPersonTitle = "撮る人を選ぶ";
        public const string WordPersonClose = "とじる";
        public const string WordAskConfirm = "{0} さんに撮影の許可を依頼しますか?";
        public const string WordAskNote = "{0} さんの画面に通知が出ます";
        public const string WordAskYes = "依頼する";
        public const string WordAskNo = "やめる";
        public const string WordCancel = "取り消す";
        public const string WordStopPerson = "撮るのをやめる";
        public const string WordWaitPermit = "{0} さんの返事を待っています ({1})";
        public const string WordChecking = "{0} さんの許可を確かめています";
        public const string WordWaiting = "返事を待っています";
        public const string WordFilmingPerson = "{0} さんを撮影中";
        public const string WordNoPermit = "{0} さんの許可がないので、犬だけを撮っています";
        public const string WordDeclined = "今は撮影できないようです";
        public const string WordPaused = "今は撮影の申し込みを受けていないようです";
        public const string WordBusy = "相手はいま別の申し込みに対応中です";
        public const string WordNotAccepting = "撮影の申し込みを受けていません";
        public const string WordNotReady = "準備中";
        public const string WordAgain = "あと {0} 秒は申し込めません";
        public const string WordBusyMe = "返事を待っている申し込みがあります";
        public const string WordAnswerFirst = "相手からの申し込みに先に答えてください";
        public const string WordLeft = "相手が退室しました";
        public const string WordRevoked = "{0} さんが撮影の許可を取り消しました";
        public const string WordNoTable = "許可を確かめられませんでした";
        public const string WordCrossYield = "相手からの申し込みを先に表示します";

        [Header("人を撮る (W4。Video/4 が作って配線する)")]
        public GameObject personButton, personCancel;
        public TMP_Text personLabel, personCancelLabel;
        public GameObject personListRoot;
        public TMP_Text personListTitle, personListReason, personPageLabel;
        public TextMeshProUGUI[] personCellLabels;   // TextMeshProUGUI[] (the array type the menu already uses in Udon)
        public Image[] personCellImages;
        public GameObject personConfirmRoot;
        public TMP_Text personConfirmLabel, personConfirmNote;
        public Color personDimColor = new Color(0.24f, 0.21f, 0.19f);

        public const int PeoplePerPage = 8;
        const int PeopleMax = 100;
        const float AskGuard = 0.5f, AwayEndSeconds = 60f, NoteSeconds = 4f;

        private int _person = -1;          // -1 = じぶん; otherwise the playerId of the person chosen for じぶん mode
        private bool _personLive;          // the last tick framed that person (with the permission)
        private int[] _peopleIds = new int[PeopleMax];
        private KH_TravelMailbox[] _peopleBoxes = new KH_TravelMailbox[PeopleMax];
        private int _peopleCount, _peoplePage;
        private bool _listOpen;
        private string _listReason = "";
        private int _confirmId = -1;
        private float _confirmAt;
        private string _personNote = "";
        private float _personNoteUntil;
        private int _filmSeenSerial = -1;
        private float _awaySince = -1f;
        private KH_TravelMailbox _myBox;

        /// <summary>My own mailbox (the PlayerObject owned by me). Found once; null until it exists.</summary>
        KH_TravelMailbox PersonMyBox()
        {
            if (Utilities.IsValid(_myBox)) return _myBox;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return null;
            GameObject[] objs = Networking.GetPlayerObjects(lp);
            if (objs == null) return null;
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] == null) continue;
                KH_TravelMailbox b = objs[i].GetComponent<KH_TravelMailbox>();
                if (b != null) { _myBox = b; return b; }
            }
            return null;
        }

        // ================================================================ the follow (じぶん mode with another person)

        /// <summary>じぶん mode with another person: that person's framing while the permission is in use, else the selected dog's framing.</summary>
        bool PersonFollow(float dt)
        {
            if (!TryPersonPose(_person))
            {
                if (_personLive) PersonDrop();   // the permission ended on this tick: detach, forget, cut (never hold the person's picture)
                return DogFollowOf(_dog, dt);
            }
            if (!_personLive) { NewSubject(); _personLive = true; }   // the person's first tick: a new subject (cut, picture attached after its own render)
            Vector3 head = _personHead;
            Vector3 h = Heading(_personFeet, _personFwd, dt);
            int own = PersonDogsCentre(_person, head, selfDogMax);
            _wantPos = SelfFollowPos(head, h, selfSideDeg, own > 0 ? selfDistanceWithDog : selfDistance, selfLift) + LeadOffset(_hVel, posTau);
            _wantLook = SelfFollowLook(head, _ownCentre, own, selfLookToDogs, selfChestDrop) + LeadOffset(_hVel, lookTau);
            _wantFov = selfFov;
            _subject = head; _subjectSize = 0.5f;
            return true;
        }

        /// <summary>The dogs leashed by that person (KH_LeashManager.walkers, synced) within maxDist of `from`; centre into _ownCentre.</summary>
        int PersonDogsCentre(int pid, Vector3 from, float maxDist)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            int[] w = manager != null ? manager.walkers : null;
            if (w != null && dogRoots != null)
                for (int i = 0; i < dogRoots.Length && i < w.Length; i++)
                {
                    if (w[i] != pid || !DogValid(i)) continue;
                    Vector3 c = DogSubject(i);
                    if ((c - from).magnitude > maxDist) continue;
                    sum += c; n++;
                }
            _ownCentre = n > 0 ? sum / n : from;
            return n;
        }

        /// <summary>The permission ended (or the pre-render re-check failed): forget the person's pose, detach the picture, start over with a cut.</summary>
        void PersonDrop()
        {
            _personLive = false;
            _personHead = Vector3.zero; _personFwd = Vector3.zero; _personFeet = Vector3.zero;
            NewSubject();
        }

        /// <summary>The re-check right before enabling the camera (§13.3): still in use for that person (no pose read here).</summary>
        bool PersonStillUsable()
        {
            KH_TravelMailbox box = PersonMyBox();
            return box != null && box._FilmGate(_person);
        }

        /// <summary>Choose the person for じぶん mode (-1 = me). A different person is a new subject.</summary>
        void PersonSet(int id)
        {
            if (id == _person) return;
            _person = id;
            _personLive = false;
            if (_open && _mode == ModeSelfFollow) NewSubject();
        }

        // ================================================================ the picker, the list, the confirmation (local only)

        /// <summary>[人: name]: opens the list of the instance (snapshot now; frozen while open).</summary>
        public void _PersonList()
        {
            if (!_open) return;
            PersonSnapshot();
            _peoplePage = 0; _listOpen = true; _confirmId = -1; _listReason = "";
            DrawPerson();
        }

        public void _PersonListClose() { _listOpen = false; _listReason = ""; DrawPerson(); }
        public void _PersonPagePrev() { PersonPage(-1); }
        public void _PersonPageNext() { PersonPage(1); }
        public void _PersonPick0() { PersonPick(0); }
        public void _PersonPick1() { PersonPick(1); }
        public void _PersonPick2() { PersonPick(2); }
        public void _PersonPick3() { PersonPick(3); }
        public void _PersonPick4() { PersonPick(4); }
        public void _PersonPick5() { PersonPick(5); }
        public void _PersonPick6() { PersonPick(6); }
        public void _PersonPick7() { PersonPick(7); }

        /// <summary>[依頼する] (§14.3): 0.5 s guard, S still here and askable, the current session ends (a target change), then the request.</summary>
        public void _PersonAskYes()
        {
            if (!_open || _confirmId < 0) return;
            if (Time.time - _confirmAt < AskGuard) return;   // a quick double press must not skip the confirmation
            int id = _confirmId;
            KH_TravelMailbox box = PersonMyBox();
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(id);
            if (box == null || !Utilities.IsValid(p) || p.isLocal) { _confirmId = -1; PersonNote(WordLeft); DrawPerson(); return; }
            KH_TravelMailbox theirs = box._FindBox(p);   // look again at the press (a leave in between)
            int can = box._FilmCanAsk(id, theirs);
            if (can != KH_TravelMailbox.JoinWaiting) { _confirmId = -1; _listOpen = true; _listReason = AskText(can, id); DrawPerson(); return; }
            box._FilmEnd();   // the old session (if any) ends before the new request
            int code = box._FilmRequest(id);
            _confirmId = -1;
            _filmSeenSerial = box.filmResultSerial;
            if (code == KH_TravelMailbox.JoinWaiting) { PersonSet(id); _listOpen = false; }
            else { _listOpen = true; _listReason = AskText(code, id); }
            DrawPerson();
        }

        /// <summary>[やめる]: nothing is sent; back to the list.</summary>
        public void _PersonAskNo()
        {
            if (_confirmId < 0) return;
            _confirmId = -1; _listOpen = true; _listReason = "";
            DrawPerson();
        }

        /// <summary>[取り消す] (waiting / checking) or [撮るのをやめる] (in use): the session ends; じぶん frames me again.</summary>
        public void _PersonCancel()
        {
            KH_TravelMailbox box = PersonMyBox();
            if (box != null) box._FilmEnd();
            PersonSet(-1);
            DrawPerson();
        }

        void PersonPage(int step)
        {
            if (!_listOpen) return;
            int pages = Mathf.Max(1, (_peopleCount + PeoplePerPage - 1) / PeoplePerPage);
            _peoplePage = ((_peoplePage + step) % pages + pages) % pages;
            DrawPerson();
        }

        void PersonPick(int cell)
        {
            if (!_open || !_listOpen) return;
            int idx = _peoplePage * PeoplePerPage + cell;
            if (idx < 0 || idx >= _peopleCount) return;
            int id = _peopleIds[idx];
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(id);
            if (!Utilities.IsValid(p)) { _listReason = WordLeft; DrawPerson(); return; }
            KH_TravelMailbox box = PersonMyBox();
            if (p.isLocal)
            {
                // myself: no permission, no confirmation; the current session (another person) ends
                if (box != null) box._FilmEnd();
                PersonSet(-1);
                _listOpen = false;
                DrawPerson();
                return;
            }
            if (box != null && box._FilmTarget() == id) { _listOpen = false; DrawPerson(); return; }   // already asked / filming that person
            if (box == null) { _listReason = WordNotReady; DrawPerson(); return; }
            if (!Utilities.IsValid(_peopleBoxes[idx])) _peopleBoxes[idx] = box._FindBox(p);
            int can = box._FilmCanAsk(id, _peopleBoxes[idx]);
            if (can != KH_TravelMailbox.JoinWaiting) { _listReason = AskText(can, id); DrawPerson(); return; }   // never open the confirmation to refuse later
            _confirmId = id; _confirmAt = Time.time; _listOpen = false; _listReason = "";
            DrawPerson();
        }

        /// <summary>Me first, then everyone else by playerId. Names and ids only.</summary>
        void PersonSnapshot()
        {
            _peopleCount = 0;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            int n = VRCPlayerApi.GetPlayerCount();
            VRCPlayerApi[] all = new VRCPlayerApi[n];
            VRCPlayerApi.GetPlayers(all);
            _peopleIds[_peopleCount++] = lp.playerId;
            int last = -1;
            while (_peopleCount < PeopleMax)
            {
                int best = -1;
                for (int i = 0; i < n; i++)
                {
                    if (!Utilities.IsValid(all[i]) || all[i].isLocal) continue;
                    int id = all[i].playerId;
                    if (id > last && (best < 0 || id < best)) best = id;
                }
                if (best < 0) break;
                _peopleIds[_peopleCount++] = best;
                last = best;
            }
            KH_TravelMailbox box = PersonMyBox();
            for (int i = 0; i < PeopleMax; i++)
                _peopleBoxes[i] = i > 0 && i < _peopleCount && box != null ? box._FindBox(VRCPlayerApi.GetPlayerById(_peopleIds[i])) : null;
        }

        /// <summary>2 Hz while open (PostLateUpdate): the confirmation's S still here, film results, the person leaving, 60 s in another mode, the labels.</summary>
        void PersonTick(float now)
        {
            KH_TravelMailbox box = PersonMyBox();
            if (_confirmId >= 0 && !Utilities.IsValid(VRCPlayerApi.GetPlayerById(_confirmId))) { _confirmId = -1; PersonNote(WordLeft); }
            if (box != null && box.filmResultSerial != _filmSeenSerial)
            {
                _filmSeenSerial = box.filmResultSerial;
                PersonResult(box.filmResultCode, box.filmResultTarget);
            }
            if (_person >= 0 && !Utilities.IsValid(VRCPlayerApi.GetPlayerById(_person))) { PersonSet(-1); PersonNote(WordLeft); }
            int phase = box != null ? box._FilmPhase() : KH_TravelMailbox.FilmNone;
            if (_mode != ModeSelfFollow && phase != KH_TravelMailbox.FilmNone)
            {
                if (_awaySince < 0f) _awaySince = now;
                else if (now - _awaySince > AwayEndSeconds) { box._FilmEnd(); PersonSet(-1); _awaySince = -1f; }
            }
            else _awaySince = -1f;
            if (_listOpen)
                for (int c = 0; c < PeoplePerPage; c++)
                {
                    int idx = _peoplePage * PeoplePerPage + c;
                    if (idx <= 0 || idx >= _peopleCount || Utilities.IsValid(_peopleBoxes[idx]) || box == null) continue;
                    VRCPlayerApi p = VRCPlayerApi.GetPlayerById(_peopleIds[idx]);
                    if (Utilities.IsValid(p)) _peopleBoxes[idx] = box._FindBox(p);   // "not ready" rows only, visible page only
                }
            DrawPerson();
        }

        /// <summary>A film result from my mailbox (filmResultSerial): a short note on the picker.</summary>
        void PersonResult(int code, int target)
        {
            string name = PersonName(target);
            if (code == KH_TravelMailbox.CodeNo) PersonNote(WordDeclined);
            else if (code == KH_TravelMailbox.CodeBusy) PersonNote(WordBusy);
            else if (code == KH_TravelMailbox.CodePaused) PersonNote(WordPaused);
            else if (code == KH_TravelMailbox.FilmRevokedBy) PersonNote(WordRevoked.Replace("{0}", name));
            else if (code == KH_TravelMailbox.FilmNoTable) PersonNote(WordNoTable);
            else if (code == KH_TravelMailbox.CrossYield) PersonNote(WordCrossYield);
            else if (code == KH_TravelMailbox.JoinLeft) { PersonNote(WordLeft); if (target == _person) PersonSet(-1); }
        }

        void PersonNote(string s) { _personNote = s; _personNoteUntil = Time.time + NoteSeconds; }

        string PersonName(int id)
        {
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(id);
            return Utilities.IsValid(p) ? p.displayName : "?";
        }

        /// <summary>Why a person cannot be asked now (the list and the press).</summary>
        string AskText(int code, int id)
        {
            if (code == KH_TravelMailbox.JoinNotAccepting) return WordNotAccepting;
            if (code == KH_TravelMailbox.JoinNotReady) return WordNotReady;
            if (code == KH_TravelMailbox.JoinAgain)
            {
                KH_TravelMailbox box = PersonMyBox();
                return WordAgain.Replace("{0}", box != null ? Mathf.CeilToInt(box._FilmAgainLeft(id)).ToString() : "30");
            }
            if (code == KH_TravelMailbox.JoinBusyMe) return WordBusyMe;
            if (code == KH_TravelMailbox.AnswerFirst) return WordAnswerFirst;
            if (code == KH_TravelMailbox.JoinLeft) return WordLeft;
            return WordBusyMe;
        }

        /// <summary>Close the list / confirmation (another mode, a shot change). Nothing is sent.</summary>
        void PersonUiClose()
        {
            _listOpen = false; _confirmId = -1; _listReason = "";
        }

        /// <summary>× / しまう: the session ends, じぶん is me again, the list / confirmation close.</summary>
        void PersonCloseAll()
        {
            KH_TravelMailbox box = PersonMyBox();
            if (box != null) box._FilmEnd();
            _person = -1; _personLive = false; _awaySince = -1f;
            _personHead = Vector3.zero; _personFwd = Vector3.zero; _personFeet = Vector3.zero;
            PersonUiClose();
            _personNote = "";
            if (personListRoot != null) personListRoot.SetActive(false);
            if (personConfirmRoot != null) personConfirmRoot.SetActive(false);
        }

        // ================================================================ drawing

        void DrawPerson()
        {
            bool pick = _open && _mode == ModeSelfFollow;
            if (personButton != null && personButton.activeSelf != pick) personButton.SetActive(pick);
            KH_TravelMailbox box = PersonMyBox();
            int phase = box != null ? box._FilmPhase() : KH_TravelMailbox.FilmNone;
            bool cancel = pick && _person >= 0 && phase != KH_TravelMailbox.FilmNone && box._FilmTarget() == _person;
            if (personCancel != null && personCancel.activeSelf != cancel) personCancel.SetActive(cancel);
            if (cancel && personCancelLabel != null) personCancelLabel.text = phase == KH_TravelMailbox.FilmInUse ? WordStopPerson : WordCancel;
            if (pick && personLabel != null)
            {
                string who = _person < 0 ? WordModeSelf : PersonName(_person);
                string line;
                if (Time.time < _personNoteUntil && _personNote.Length > 0) line = _personNote;
                else if (_person < 0) line = "";
                else if (phase == KH_TravelMailbox.FilmPending && box._FilmTarget() == _person)
                    line = WordWaitPermit.Replace("{0}", who).Replace("{1}", Mathf.CeilToInt(box._FilmWaitLeft()).ToString());
                else if (phase == KH_TravelMailbox.FilmTransit && box._FilmTarget() == _person) line = WordChecking.Replace("{0}", who);
                else if (_personLive) line = WordFilmingPerson.Replace("{0}", who);
                else line = WordNoPermit.Replace("{0}", who);
                personLabel.text = WordPerson + who + (line.Length > 0 ? "\n" + line : "");
            }
            // the list
            bool list = _open && _listOpen;
            if (personListRoot != null && personListRoot.activeSelf != list) personListRoot.SetActive(list);
            if (list)
            {
                if (personListTitle != null) personListTitle.text = WordPersonTitle;
                if (personListReason != null) personListReason.text = _listReason;
                int pages = Mathf.Max(1, (_peopleCount + PeoplePerPage - 1) / PeoplePerPage);
                if (personPageLabel != null) personPageLabel.text = (_peoplePage + 1).ToString() + "/" + pages.ToString();
                for (int c = 0; personCellLabels != null && c < personCellLabels.Length && c < PeoplePerPage; c++)
                {
                    int idx = _peoplePage * PeoplePerPage + c;
                    bool on = idx < _peopleCount;
                    Image img = personCellImages != null && c < personCellImages.Length ? personCellImages[c] : null;
                    if (img != null && img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
                    if (!on || personCellLabels[c] == null) continue;
                    int id = _peopleIds[idx];
                    VRCPlayerApi p = VRCPlayerApi.GetPlayerById(id);
                    string text; bool ok = true;
                    if (!Utilities.IsValid(p)) { text = "-\n" + WordLeft; ok = false; }
                    else if (p.isLocal) text = WordModeSelf;
                    else
                    {
                        text = p.displayName;
                        int can = box != null ? box._FilmCanAsk(id, _peopleBoxes[idx]) : KH_TravelMailbox.JoinNotReady;
                        if (box != null && box._FilmTarget() == id) text += "\n" + (phase == KH_TravelMailbox.FilmInUse ? WordFilming : WordWaiting);
                        else if (can != KH_TravelMailbox.JoinWaiting) { text += "\n" + AskText(can, id); ok = false; }
                    }
                    if (personCellLabels[c].text != text) personCellLabels[c].text = text;
                    if (img != null) img.color = ok ? modeOffColor : personDimColor;
                }
            }
            // the confirmation
            bool confirm = _open && _confirmId >= 0;
            if (personConfirmRoot != null && personConfirmRoot.activeSelf != confirm) personConfirmRoot.SetActive(confirm);
            if (confirm)
            {
                string who = PersonName(_confirmId);
                if (personConfirmLabel != null) personConfirmLabel.text = WordAskConfirm.Replace("{0}", who);
                if (personConfirmNote != null) personConfirmNote.text = WordAskNote.Replace("{0}", who);
            }
        }
    }
}
