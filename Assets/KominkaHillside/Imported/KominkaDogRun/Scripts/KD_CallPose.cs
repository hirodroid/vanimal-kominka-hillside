using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaDogRun
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_CallPose : UdonSharpBehaviour
    {
        public const string SelectionWaiting = "犬を確認中";
        public const string SelectionBusy = "ほかの人が選択中";
        public KD_TreatPouch pouch;
        public KD_SleepBoard board;
        public KD_SleepPlacement placement;
        public KD_Language language;
        public KD_DogWanderController[] dogs;
        public Vector3[] pawTargets;
        public float[] pawRadii;
        public Vector3[] chinTargets;
        public float[] chinRadii;
        public GameObject panel;
        public GameObject[] dogListParts;
        public GameObject[] miniatures;
        public Button[] poseButtons;
        public Button placeButton;
        public Button applyButton;
        public Button releaseButton;
        public Button pawButton;
        public Button stretchButton;
        public TextMeshProUGUI title;
        public TextMeshProUGUI status;
        public KD_ShopItem[] actionProducts;
        // P13 (2026-09-16): 確認は板の手前のカード (KD_TreatPouch.confirmRoot)。専用パネルは持たない
        public GameObject[] purchaseControls;
        private int _purchase = -1;
        private int _dog = -1;
        private int _pose;
        private float _nextTick;
        private float[] _dwell = new float[6];
        private float[] _outside = new float[6];
        private float[] _desktopUntil = new float[6];
        private int[] _hand = new int[6];
        private Vector3[] _previousHand = new Vector3[6];
        private bool[] _sampled = new bool[6];
        private int[] _interactionPose = new int[6];
        private int[] _interactionCall = new int[6];
        public bool PageOpen { get { return _dog >= 0 && panel != null && panel.activeSelf; } }

        private string L(string key) { return language != null ? language._Text(key) : key; }

        public void _Open(int dog)
        {
            if (board == null || dog < 0 || dogs == null || dog >= dogs.Length) return;
            KD_SleepSlot slot = board.SlotOf(dog);
            if (slot == null || (slot.LiveSleeperId >= 0 && !slot.IsSleeperLocal)) return;
            if (!slot.IsSleeperLocal && !board.IsLocalAllowedToCall) return;
            // 裏山改変: IMP-DOG-016 変更 1。リード接続中・集合中・フリスビー中はポーズ面を開かない (理由は呼ぶタブの行が出す)
            if (board._IsWalkBusy(dog)) return;
            if (_dog != dog) _Dismiss();
            if (!slot._BeginSelection()) return;
            _dog = dog;
            _pose = slot.IsSleeperLocal ? slot.PoseId : 0;
            SetPage(true);
            Draw();
        }

        private void SetPage(bool open)
        {
            if (panel != null) panel.SetActive(open);
            for (int i = 0; dogListParts != null && i < dogListParts.Length; i++)
                if (dogListParts[i] != null) dogListParts[i].SetActive(!open);
        }

        public void _Dismiss()
        {
            _purchase = -1;
            if (pouch != null) pouch._HideConfirm(KD_TreatPouch.ConfirmCall);
            KD_SleepSlot slot = board == null ? null : board.SlotOf(_dog);
            _dog = -1;
            if (slot != null) slot._EndSelection();
            SetPage(false);
        }
        public void _Back() { if (_purchase >= 0) { _CancelPurchase(); return; } _Dismiss(); if (pouch != null) pouch._RefreshLanguage(); }
        public void _Pose0() { _pose = 0; Draw(); }
        public void _Pose1() { _pose = 1; Draw(); }
        public void _Pose2() { _pose = 2; Draw(); }
        public void _Pose3() { _pose = 3; Draw(); }
        public void _Pose4() { _pose = 4; Draw(); }

        public void _Place()
        {
            if (_purchase >= 0 || !PageOpen) return;
            if (_pose == 4 && !Owned(0)) { Confirm(0); return; }
            KD_SleepSlot slot = board == null ? null : board.SlotOf(_dog);
            if (slot == null || placement == null || !slot.SelectionReady) return;
            slot._SetPaw(false);
            slot._SetChin(0);
            placement.requestedDog = _dog;
            placement.requestedPose = _pose;
            placement._Begin();
        }

        public void _Apply()
        {
            if (_purchase >= 0 || !PageOpen) return;
            if (_pose == 4 && !Owned(0)) { Confirm(0); return; }
            KD_SleepSlot slot = board == null ? null : board.SlotOf(_dog);
            if (slot == null || !slot._SetPose(_pose)) return;
            _desktopUntil[_dog] = 0f;
            _dwell[_dog] = 0f;
            Draw();
        }

        public void _Release()
        {
            KD_SleepSlot slot = board == null ? null : board.SlotOf(_dog);
            if (slot == null || !slot.IsSleeperLocal) return;
            slot._Release();
            _Back();
        }

        public void _Paw()
        {
            if (_purchase >= 0 || !PageOpen) return;
            int action = _pose == 1 ? 1 : 2;
            if ((_pose == 1 || _pose == 2) && !Owned(action)) { Confirm(action); return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            KD_SleepSlot slot = board == null ? null : board.SlotOf(_dog);
            if (!Utilities.IsValid(lp) || lp.IsUserInVR() || slot == null || !slot.IsSleeperLocal
                || (slot.PoseId != 2 && slot.PoseId != 1) || slot.StretchActive || dogs[_dog] == null || !dogs[_dog].IsCompanionReady) return;
            if (slot.DogRoot() == null || Vector3.Distance(lp.GetPosition(), slot.DogRoot().position) > 2f) return;
            _desktopUntil[_dog] = slot.PawHeld || slot.ChinHand != 0 ? 0f : Time.time + 4f;
            SetHand(slot, _desktopUntil[_dog] > Time.time ? 3 : 0);
            Draw();
        }
        public void _Stretch()
        {
            if (_purchase >= 0 || !PageOpen) return;
            if (!Owned(3)) { Confirm(3); return; }
            KD_SleepSlot slot = board == null ? null : board.SlotOf(_dog);
            if (slot == null || !slot.IsSleeperLocal || dogs[_dog] == null || !dogs[_dog].IsCompanionReady
                || (placement != null && placement.Active)) return;
            _desktopUntil[_dog] = 0f; _dwell[_dog] = 0f;
            slot._Stretch(); Draw();
        }

        private KD_ShopItem Product(int i)
        { return actionProducts != null && i >= 0 && i < actionProducts.Length ? actionProducts[i] : null; }
        private bool Owned(int i) { KD_ShopItem item = Product(i); return item != null && item.Owned(); }
        private void Confirm(int i) { if (Product(i) != null) _purchase = i; Draw(); }
        public void _CancelPurchase() { _purchase = -1; if (pouch != null) pouch._HideConfirm(KD_TreatPouch.ConfirmCall); Draw(); }
        public void _BuyAction()
        {
            if (!PageOpen || _purchase < 0 || pouch == null || pouch.ConfirmKind() != KD_TreatPouch.ConfirmCall) return;
            KD_ShopItem item = Product(_purchase);
            if (item == null || item.ConfirmationState(false) != KD_ShopItem.ConfirmBuy) { Draw(); return; }
            _purchase = -1; // Consume this confirmation before purchase callbacks; never perform the action here.
            pouch._HideConfirm(KD_TreatPouch.ConfirmCall);
            item._Buy(); Draw();
        }

        private string ProductLabel(int i)
        {
            KD_ShopItem item = Product(i);
            return item == null ? L("KD_ShopShelf.WordLoading")
                : L(item.displayNameKey) + (item.Owned() ? "" : "  " + item.price + " pt");
        }

        private void Start() { SetPage(false); }
        private void OnDisable() { _Dismiss(); }

        // IMP-DOG-034 A8: 20 Hz only while this player has called a dog or has the pose page open; otherwise 4 Hz (nothing to judge:
        // UpdatePaw returns at once for dogs this player has not called). Calling a dog switches back within one slow tick (0.25 s,
        // while the dog is still walking over). dt is measured from the last tick (the old "now - next + .05" was the same value).
        private float _lastTick = -.05f;
        private bool _anyLocalCall;

        private void Update()
        {
            if (Time.time < _nextTick) return;
            float dt = Mathf.Clamp(Time.time - _lastTick, 0f, .1f);
            _lastTick = Time.time;
            _nextTick = Time.time + .05f;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || board == null || dogs == null) return;
            _anyLocalCall = false;
            for (int i = 0; i < dogs.Length && i < _dwell.Length; i++) UpdatePaw(i, lp, dt);
            if (!_anyLocalCall && _dog < 0) _nextTick = Time.time + .25f;
            if (_dog >= 0 && panel != null && panel.activeInHierarchy)
            {
                KD_SleepSlot selected = board.SlotOf(_dog);
                if (selected != null) selected._KeepSelectionAlive();
                Draw();
            }
        }

        private void UpdatePaw(int i, VRCPlayerApi lp, float dt)
        {
            KD_SleepSlot slot = board.SlotOf(i);
            if (slot == null) return;
            // IMP-DOG-034 A8: a dog this player has not called is never ready, and _SetPaw / _SetChin return at their first line
            // (!IsSleeperLocal), so the old path only zeroed the local arrays below. _interactionPose / _interactionCall are not
            // refreshed here; the first tick after this player calls compares against them, finds a change and zeroes the same
            // arrays again (already zero), so nothing observable differs.
            if (!slot.IsSleeperLocal)
            {
                _dwell[i] = 0; _outside[i] = 0; _desktopUntil[i] = 0; _sampled[i] = false;
                return;
            }
            _anyLocalCall = true;
            Transform root = slot.DogRoot();
            if (_interactionPose[i] != slot.PoseId || _interactionCall[i] != slot.CalledMs)
            {
                _interactionPose[i] = slot.PoseId; _interactionCall[i] = slot.CalledMs;
                _dwell[i] = 0; _outside[i] = 0; _desktopUntil[i] = 0; _sampled[i] = false;
            }
            bool chin = slot.PoseId == 1;
            bool held = chin ? slot.ChinHand != 0 : slot.PawHeld;
            // IMP-DOG-042: おやつを口元で受けて食べ・返事をしている間 (CalledTreatActive) は、あご乗せ・お手を下ろす
            // (口元の手をあご乗せと読まない。頭は餌の注視が持つ)。終われば手を置き直して再開できる
            bool ready = slot.IsSleeperLocal && (slot.PoseId == 2 || chin) && !slot.StretchActive && dogs[i] != null
                && dogs[i].IsCompanionReady && !dogs[i].CalledTreatActive && root != null && (placement == null || !placement.Active);
            if (!ready)
            {
                _dwell[i] = 0; _outside[i] = 0; _desktopUntil[i] = 0; _sampled[i] = false;
                slot._SetPaw(false);
                slot._SetChin(0);
                return;
            }
            if (!lp.IsUserInVR())
            {
                SetHand(slot, _desktopUntil[i] > Time.time && Vector3.Distance(lp.GetPosition(), root.position) <= 2f ? 3 : 0);
                return;
            }
            Vector3[] targets = chin ? chinTargets : pawTargets;
            float[] radii = chin ? chinRadii : pawRadii;
            if (targets == null || radii == null || i >= targets.Length || i >= radii.Length) return;
            Vector3 target = root.TransformPoint(targets[i]);
            Vector3 left = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
            Vector3 right = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            if (!held && _dwell[i] <= 0f) _hand[i] = Vector3.Distance(left, target) <= Vector3.Distance(right, target) ? 0 : 1;
            Vector3 hand = _hand[i] == 0 ? left : right;
            float radius = Mathf.Max(.06f, radii[i]) * (held ? 1.4f : 1f);
            bool inside = hand.sqrMagnitude > .0001f && Vector3.Distance(hand, head) < 2.5f
                && Vector3.Distance(hand, target) <= radius;
            float speed = _sampled[i] ? Vector3.Distance(hand, _previousHand[i]) / Mathf.Max(.01f, dt) : 0f;
            _previousHand[i] = hand; _sampled[i] = true;
            if (inside && (held || speed < .45f))
            {
                _outside[i] = 0f;
                _dwell[i] += dt;
                if (_dwell[i] >= .35f) SetHand(slot, _hand[i] + 1);
            }
            else
            {
                _dwell[i] = 0f;
                _outside[i] += dt;
                if (_outside[i] >= .15f) SetHand(slot, 0);
            }
        }
        private void SetHand(KD_SleepSlot slot, int hand)
        {
            if (slot.PoseId == 1) { slot._SetPaw(false); slot._SetChin(hand); }
            else { slot._SetChin(0); slot._SetPaw(hand != 0); }
        }
        private string PoseKey(int pose)
        {
            if (pose == 0) return "call.pose0";
            if (pose == 1) return "call.pose1";
            if (pose == 2) return "call.pose2";
            if (pose == 3) return "call.pose3";
            return "call.pose4";
        }

        private void Draw()
        {
            if (_dog < 0 || board == null) return;
            for (int i = 0; purchaseControls != null && i < purchaseControls.Length; i++)
                if (purchaseControls[i] != null) purchaseControls[i].SetActive(true);
            KD_SleepSlot slot = board.SlotOf(_dog);
            bool mine = slot != null && slot.IsSleeperLocal;
            // 裏山改変: IMP-DOG-016 変更 1。面を開いている間にリード・集合・フリスビーが始まったら、押せなくして理由を出す
            bool walkBusy = board._IsWalkBusy(_dog);
            bool available = slot != null && slot.SelectionReady && (mine || board.IsLocalAllowedToCall) && !walkBusy;
            if (title != null) title.text = pouch.sleepLabels[_dog].text + " / " + L("call.choose");
            if (status != null) status.text = walkBusy ? L(board._IsLeashedDog(_dog) ? "KD_TreatPouch.WordSleepLeashed"
                    : board._IsGatheringDog(_dog) ? "KD_TreatPouch.WordWalkStateGathering" : "KD_TreatPouch.WordSleepFrisbee")
                : slot != null && slot.SelectionPending ? L("call.selectionWaiting")
                : !available ? L("call.unavailable") : L((_pose == 2 && !Owned(2)) || (_pose == 1 && !Owned(1)) || (_pose == 4 && !Owned(0))
                    ? "call.lockedHint" : _pose == 2 ? "call.pawHint" : _pose == 1 ? "call.chinHint" : "call.hint");
            if (title != null && mine) title.text += " / " + L("call.current") + L(PoseKey(slot.PoseId));
            for (int i = 0; miniatures != null && i < miniatures.Length; i++)
                if (miniatures[i] != null) miniatures[i].SetActive(i / (KD_SleepSlot.MaxPoseId + 1) == _dog);
            for (int i = 0; poseButtons != null && i < poseButtons.Length; i++)
                if (poseButtons[i] != null)
                {
                    poseButtons[i].image.color = i == _pose ? pouch.tabOnColor : pouch.tabOffColor;
                    poseButtons[i].interactable = _purchase < 0;
                    poseButtons[i].GetComponentInChildren<TextMeshProUGUI>(true).text = i == 4 ? ProductLabel(0) : L(PoseKey(i));
                }
            if (placeButton != null) placeButton.interactable = available && !slot.CallPending;
            if (applyButton != null) applyButton.gameObject.SetActive(mine);
            if (releaseButton != null) releaseButton.gameObject.SetActive(mine);
            VRCPlayerApi lp = Networking.LocalPlayer;
            bool desktop = Utilities.IsValid(lp) && !lp.IsUserInVR();
            if (stretchButton != null)
            {
                stretchButton.gameObject.SetActive(!Owned(3) || mine);
                stretchButton.GetComponentInChildren<TextMeshProUGUI>(true).text = ProductLabel(3);
                stretchButton.interactable = !Owned(3) || (mine && dogs[_dog] != null && dogs[_dog].IsCompanionReady && slot.CanStretch && !slot.CallPending);
            }
            if (pawButton != null)
            {
                int handAction = _pose == 1 ? 1 : 2;
                bool locked = !Owned(handAction);
                pawButton.gameObject.SetActive((_pose == 1 || _pose == 2) && (locked || (mine && desktop && slot.PoseId == _pose)));
                pawButton.GetComponentInChildren<TextMeshProUGUI>(true).text = locked ? ProductLabel(handAction) : L(_pose == 1 ? "call.chin" : "call.paw");
                pawButton.interactable = locked || (mine && desktop && !slot.StretchActive && dogs[_dog] != null && dogs[_dog].IsCompanionReady
                    && slot.DogRoot() != null && Vector3.Distance(lp.GetPosition(), slot.DogRoot().position) <= 2f);
            }
            KD_ShopItem purchase = Product(_purchase);
            if (purchase != null && purchase.Owned()) { _purchase = -1; purchase = null; }
            // P13: 板の手前のカード 1 枚。中身の行に「一度の購入で 6 犬種共通」
            if (pouch != null)
            {
                if (purchase != null) pouch._ShowItemConfirm(KD_TreatPouch.ConfirmCall, purchase, L("call.purchaseHelp"));
                else pouch._HideConfirm(KD_TreatPouch.ConfirmCall);
            }
        }
    }
}
