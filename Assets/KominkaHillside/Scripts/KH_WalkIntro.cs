using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using KominkaDogRun;

namespace KominkaHillside
{
    // IMP-UI-006: observes the existing authority; never requests a leash or moves a tool.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WalkIntro : UdonSharpBehaviour
    {
        public const string ConnectedKey = "KH_WalkIntro.connected.v1";
        public const string HiddenKey = "KH_WalkIntro.hidden.v1";
        public KH_LeashManager manager;
        public KD_TreatPouch pouch;
        public KD_Language language;
        public Transform entrance, menuTarget, arrow, pickupRing, messageRoot;
        public GameObject messagePanel, closePanel, settingsPage;
        public CanvasGroup messageFade, destinationMark;
        public TMP_Text messageLabel, closeLabel, settingsEntryLabel, replayLabel, autoLabel, backLabel, settingsNote;
        public Transform[] collarRings;
        public float enterRadius = 10f, leaveRadius = 12f, rackRadius = 2f;
        public float readyDelay = 2f, successSeconds = 5f;
        private const float DestinationSeconds = 5f;

        // Display state is deliberately separate from completion: hidden clients still observe success.
        public const int Quiet = 0, Take = 1, Attach = 2, Pending = 3, Success = 4, NoDog = 5, NoTool = 6, Menu = 7, MenuAttach = 8;
        private bool _restored, _completed, _sessionSuccess, _hidden, _choseHidden;
        private bool _destinationShown, _destinationPresented;
        private int _successMenuRevision;
        private bool _dismissed, _replay, _inside, _engaged;
        // VER-ENV-002 (2026-10-12): set only where Tick has just hidden everything because the intro is over for this
        // player; cleared by every entry that can bring it back. While set, Tick keeps only the language and inside reads.
        private bool _resting;
        private int _state, _successDog = -1, _seenMask, _hintDog = -1;
        private float _readyAt = -1f, _successUntil, _nextTick;
        private Transform _arrowTarget;
        private Vector3 _messageHome;
        private string _shownKey, _shownLanguage;

        private void Start()
        {
            _messageHome = messageRoot.position;
            Hide();
            RefreshLabels();
            _OnLeashChanged(); // does not reset data that arrived before Start
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            bool saved = false;
            PlayerData.TryGetBool(player, ConnectedKey, out saved);
            _completed = _completed || saved || _sessionSuccess;
            _resting = false;
            if (!_choseHidden) { bool off = false; PlayerData.TryGetBool(player, HiddenKey, out off); _hidden = off; }
            _restored = true;
            if (_sessionSuccess && !saved) PlayerData.SetBool(ConnectedKey, true);
            if (_choseHidden) PlayerData.SetBool(HiddenKey, _hidden);
            RefreshLabels();
        }

        public void _OnLeashChanged()
        {
            _resting = false; // also reached from Tick; a resting Tick does not call this
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local) || manager == null || manager.walkers == null) return;
            int mask = 0;
            for (int i = 0; i < manager.walkers.Length; i++)
            {
                if (manager.walkers[i] != local.playerId) continue;
                mask |= 1 << i;
            }
            ObserveMask(mask);
        }

        // Same event path for authority writes and received snapshots; separate from SDK reads for Edit-mode probes.
        private void ObserveMask(int mask)
        {
            int firstNew = -1;
            for (int i = 0; i < manager.walkers.Length; i++)
                if ((mask & (1 << i)) != 0 && (_seenMask & (1 << i)) == 0 && firstNew < 0) firstNew = i;
            _seenMask = mask;
            if (mask != 0)
            {
                _sessionSuccess = true;
                if (!_completed && _restored) PlayerData.SetBool(ConnectedKey, true);
                _completed = true;
                if (firstNew >= 0 && _engaged && !_dismissed && _state != Success)
                {
                    _successDog = firstNew; _successUntil = Time.time + successSeconds + DestinationSeconds; _destinationShown = false; _destinationPresented = false; _successMenuRevision = pouch._IntroMenuRevision();
                    _state = Success; _replay = false;
                }
                if (firstNew >= 0 && !_engaged) _replay = false;
            }
            if (_state == Success && (_successDog < 0 || (mask & (1 << _successDog)) == 0)) Finish();
        }

        public void _Close() { _dismissed = true; _replay = false; Finish(); }
        public void _OpenSettings() { RefreshLabels(); pouch._OpenExtraSettings(settingsPage, L("title")); }
        public void _BackSettings() { pouch._OpenSettings(); }
        public void _ToggleAuto()
        {
            _hidden = !_hidden; _choseHidden = true; _resting = false;
            if (_restored) PlayerData.SetBool(HiddenKey, _hidden);
            if (_hidden && !_replay) Finish();
            RefreshLabels();
        }
        public void _Replay()
        {
            _dismissed = false; _replay = true; _engaged = false; _state = Quiet; _resting = false;
            _readyAt = Time.time;
            // An already connected walker has nothing to reconnect. Replay shows the closing hint only.
            for (int i = 0; i < manager.walkers.Length; i++) if (manager._IsMine(i))
            { _engaged = true; _successDog = i; _successUntil = Time.time + successSeconds + DestinationSeconds; _destinationShown = false; _destinationPresented = false; _successMenuRevision = pouch._IntroMenuRevision(); _state = Success; _replay = false; break; }
            _nextTick = 0f;
        }

        private void Update()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local) || manager == null) return;
            if (Time.time >= _nextTick) { _nextTick = Time.time + .15f; Tick(local); }
        }

        public override void PostLateUpdate()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local) || manager == null) return;
            if (_state == Quiet) return;
            Vector3 eye = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            // Eligibility is sampled; visible geometry follows the actual moving anchors every frame.
            for (int i = 0; i < collarRings.Length; i++)
                if (collarRings[i] != null && collarRings[i].gameObject.activeSelf)
                { collarRings[i].position = manager.collarAnchors[i].position; Face(collarRings[i], eye); }
            if (_state == Attach && _hintDog >= 0) messageRoot.position = HintPosition(_hintDog, eye);
            if (_state == Success && _successDog >= 0)
            {
                bool destination = _destinationPresented && SuccessHint(_successUntil - Time.time) == "destination";
                bool marked = destination && pouch.panelRoot.activeInHierarchy && pouch.tabPages[KD_TreatPouch.TabWalk].activeInHierarchy;
                if (destinationMark != null)
                {
                    SetVisible(destinationMark.gameObject, marked);
                    destinationMark.alpha = .7f + .3f * (.5f + .5f * Mathf.Sin(Time.time * 4f));
                }
                if (marked)
                {
                    RectTransform panel = (RectTransform)pouch.panelRoot.transform;
                    messageRoot.position = panel.TransformPoint(new Vector3(panel.rect.center.x, panel.rect.yMax + 120f, 0f));
                }
                else messageRoot.position = HintPosition(_successDog, eye);
            }
            if (arrow.gameObject.activeSelf && _arrowTarget != null)
            {
                UpdateTakeCue(eye, .5f + .5f * Mathf.Sin(Time.time * (2f * Mathf.PI / 1.2f)));
            }
            if (messagePanel.activeSelf)
            {
                Face(messageRoot, eye);
                messageFade.alpha = _state == Success ? Mathf.Clamp01((_successUntil - Time.time) / .7f) : 1f;
            }
        }

        private void Tick(VRCPlayerApi local)
        {
            // Resting: the rest of this Tick would end in Pause() on already hidden visuals (Quiet, not engaged). Leash
            // changes still arrive through the manager's _OnLeashChanged event (attach, release and OnDeserialization).
            bool resting = _resting && _state == Quiet && !_engaged && !_replay && (_completed || _hidden || _dismissed);
            if (!resting) _OnLeashChanged();
            string lang = language != null ? language.CurrentLanguage : "ja";
            if (lang != _shownLanguage) { _shownLanguage = lang; _shownKey = null; RefreshLabels(); }
            Vector3 offset = local.GetPosition() - entrance.position; offset.y = 0;
            _inside = offset.sqrMagnitude <= (_inside ? leaveRadius * leaveRadius : enterRadius * enterRadius);
            if (resting) return;
            if (!_restored || !manager._IntroStateReady()) { Pause(); return; }
            if (_readyAt < 0f) _readyAt = Time.time + readyDelay;
            if (_state == Success)
            {
                if (Time.time >= _successUntil) { Finish(); return; }
                messageRoot.position = manager.dogRoots[_successDog].position + Vector3.up * 1.05f;
                string key = SuccessHint(_successUntil - Time.time);
                if (key == "destination" && !_destinationShown)
                {
                    _destinationShown = true; // exactly once, including when another placement prevents opening
                    if (pouch._IntroMenuRevision() == _successMenuRevision)
                        _destinationPresented = pouch._IntroShowDestination(manager.collarAnchors[_successDog].position);
                }
                Show(key, false);
                return;
            }
            if (Time.time < _readyAt || (!_replay && (_completed || _hidden || _dismissed)) || !_inside)
            { Pause(); _resting = !_replay && (_completed || _hidden || _dismissed); return; }
            _engaged = true;
            messageRoot.position = _messageHome;
            bool pending = pouch != null && pouch._IntroAttachPending();
            bool held = false;
            int hands = 0;
            KH_LeashClip nearest = null; float nearestDistance = float.MaxValue;
            for (int i = 0; i < manager.localClips.Length; i++)
            {
                KH_LeashClip clip = manager.localClips[i];
                if (clip == null || clip.pickup == null) continue;
                if (clip._IntroPending()) pending = true;
                if (clip.pickup.IsHeld) { held = true; hands |= 1 << clip._IntroHand(); }
                else if (!clip._IntroPending() && clip.pickup.pickupable && clip.gameObject.activeInHierarchy)
                {
                    Vector3 rack = clip.transform.position - entrance.position; rack.y = 0;
                    float d = Vector3.Distance(local.GetPosition(), clip.transform.position);
                    if (clip.transform == _arrowTarget) d = -1f; // keep the same available tool while the visitor approaches
                    if (rack.sqrMagnitude <= rackRadius * rackRadius && d < nearestDistance) { nearest = clip; nearestDistance = d; }
                }
            }
            if (pending) { _state = Pending; Show("pending", true); return; }
            int candidate = -1; float bestDistance = float.MaxValue; bool entranceDog = false;
            for (int i = 0; i < manager.dogRoots.Length; i++)
            {
                if (manager._IntroDogAvailable(i))
                {
                    Vector3 fromRack = manager.dogRoots[i].position - entrance.position; fromRack.y = 0;
                    if (fromRack.sqrMagnitude <= leaveRadius * leaveRadius) entranceDog = true;
                }
                bool can = ((hands == 0 || (hands & 1) != 0) && manager._CanIntroAttach(i, 0)) ||
                           ((hands == 0 || (hands & 2) != 0) && manager._CanIntroAttach(i, 1));
                if (i < collarRings.Length && collarRings[i] != null)
                    SetVisible(collarRings[i].gameObject, held && can);
                if (!can) continue;
                float d = Vector3.Distance(local.GetPosition(), manager.dogRoots[i].position);
                if (i == _hintDog) d = -1f; // keep a still-eligible target instead of switching text between passing dogs
                if (d < bestDistance) { bestDistance = d; candidate = i; }
                if (held && i < collarRings.Length && collarRings[i] != null)
                {
                    // Tip proximity emphasizes the target without changing the clip's radius or dwell.
                    float size = 1f;
                    for (int j = 0; j < manager.localClips.Length; j++)
                        if (manager.localClips[j] != null && manager.localClips[j].pickup != null && manager.localClips[j].pickup.IsHeld &&
                            Vector3.Distance(manager.localClips[j].tip.position, manager.collarAnchors[i].position) < .4f) size = 1.35f;
                    collarRings[i].localScale = Vector3.one * size;
                }
            }
            if (candidate < 0 && !entranceDog) { _state = NoDog; Show("noDog", true); return; }
            if (held)
            {
                _state = Attach;
                _hintDog = candidate;
                Show(candidate < 0 ? "approach" : "attach", true); return;
            }
            // Menu visibility never replaces the physical pickup/collar cues.
            if (nearest == null) { _state = NoTool; Show("noTool", true); return; }
            _state = Take; PointAt(nearest.transform); Show("take", true);
        }

        private string SuccessHint(float remaining) { return remaining > DestinationSeconds ? "success" : "destination"; }

        private void PointAt(Transform target) { _arrowTarget = target; }
        private void UpdateTakeCue(Vector3 eye, float wave)
        {
            // A single fixed ring identifies the actual pickup; only the arrow bounces above it.
            pickupRing.position = _arrowTarget.position;
            Face(pickupRing, eye);
            arrow.position = pickupRing.position + Vector3.up * (.13f + .10f * wave);
            arrow.localScale = Vector3.one * (1f + .08f * wave);
            Face(arrow, eye);
        }
        private void Show(string key, bool close)
        {
            if (_shownKey != key) { messageLabel.text = L(key); _shownKey = key; }
            bool take = _state == Take && _arrowTarget != null;
            SetVisible(arrow.gameObject, take); SetVisible(pickupRing.gameObject, take);
            if (_state != Attach) HideRings();
            SetVisible(messagePanel, true); SetVisible(closePanel, close);
        }
        private string L(string key) { return language != null ? language._Text("walkIntro." + key) : key; }
        private void RefreshLabels()
        {
            if (closeLabel != null) closeLabel.text = L("close");
            if (settingsEntryLabel != null) settingsEntryLabel.text = L("title");
            if (replayLabel != null) replayLabel.text = L("replay");
            if (autoLabel != null) autoLabel.text = L(_hidden ? "autoOff" : "autoOn");
            if (backLabel != null) backLabel.text = L("back");
            if (settingsNote != null) settingsNote.text = L("note");
        }
        private void Face(Transform target, Vector3 eye)
        {
            Vector3 forward = target.position - eye; forward.y = 0f;
            if (forward.sqrMagnitude > .001f) target.rotation = Quaternion.LookRotation(forward);
        }
        private Vector3 HintPosition(int dog, Vector3 eye)
        {
            Vector3 toward = manager.dogRoots[dog].position - eye; toward.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, toward.normalized);
            // Keep the target's face/collar and the dogs directly behind it out of the sentence.
            return manager.dogRoots[dog].position + Vector3.up * 1.05f - right * .95f;
        }
        private void HideVisuals()
        {
            if (destinationMark != null) SetVisible(destinationMark.gameObject, false);
            SetVisible(arrow.gameObject, false); SetVisible(pickupRing.gameObject, false);
            SetVisible(messagePanel, false); SetVisible(closePanel, false); HideRings();
        }
        private void HideRings() { for (int i = 0; i < collarRings.Length; i++) if (collarRings[i] != null) SetVisible(collarRings[i].gameObject, false); }
        private void SetVisible(GameObject target, bool visible) { if (target.activeSelf != visible) target.SetActive(visible); }
        private void Hide() { HideVisuals(); _state = Quiet; }
        private void Pause() { Hide(); _engaged = false; }
        private void Finish() { _engaged = false; _replay = false; Hide(); }
    }
}
