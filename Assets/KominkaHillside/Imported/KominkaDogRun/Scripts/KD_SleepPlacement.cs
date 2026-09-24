using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon.Common;

namespace KominkaDogRun
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_SleepPlacement : UdonSharpBehaviour
    {
        public const string Place = "ポーズを選ぶ";
        public const string Confirm = "ここへ呼ぶ";
        public const string Cancel = "やめる";
        public const string Distance = "距離 (m)";
        public const string Direction = "向き";
        public const string Hint = "ホイール: 距離 / Q・E: 向き\nクリック: 確定 / Esc: やめる";
        public const string TouchHint = "距離を上下・向きを左右にドラッグ";
        public const string VrHint = "右手で位置 / 板の◀▶で向き (長押しで回る)\n右USE長押しで呼ぶ / [やめる]で戻る";
        public const string TurnLeft = "◀";
        public const string TurnRight = "▶";
        public const float ConfirmHoldSeconds = .65f;
        // 板の ◀▶: タップ (TapSeconds 未満で離す) は TurnStep、押しっぱなしは TurnHoldSpeed で連続回転 (IMP-DOG-031)。
        public const float TurnStep = 15f;
        public const float TurnHoldSpeed = 45f;
        public const float TapSeconds = .2f;
        public const string Waiting = "呼び出し中";
        public const string Failed = "呼べませんでした。権限と犬の状態を確認";
        public const string Head = "頭";
        public KD_TreatPouch pouch;
        public KD_SleepBoard board;
        public Transform marker;
        public GameObject[] markerDogs;
        public GameObject[] poseGhosts;
        [Tooltip("裏山改変: IMP-DOG-016 変更 2。犬ごとの赤い印 (markerDogs[i]/Invalid)。平らでない・地面が無い場所で出す")]
        public GameObject[] invalidMarks;
        /// <summary>裏山改変: 無効な場所の文言キー (色だけにしない)。</summary>
        public const string NeedFlatKey = "KD_TreatPouch.WordSleepNeedFlat";
        /// <summary>裏山改変: 手 / 頭から狙いの点までの間で先に地面に当たったら、そこを狙いにする (地面の中へマーカーを埋めない)。
        /// 狙いの点から真下へ探す深さ (m)。</summary>
        public const float GroundSearchDepth = 6f;
        [System.NonSerialized] public int requestedPose;
        private int _pose;
        public GameObject screenCanvas;
        public RectTransform screenPanel;
        public TextMeshProUGUI statusLabel;
        public TextMeshProUGUI vrLabel;
        public GameObject vrButtons;
        public Slider distanceSlider;
        public Slider yawSlider;
        public Button confirmButton;
        public Image confirmProgress;
        [System.NonSerialized] public int requestedDog;
        private int _state; // 0=closed 1=preview 2=shared result wait
        private KD_SleepSlot _slot;
        private bool _rightHeld;
        private bool _leftHeld;
        private bool _holdingConfirm;
        private float _holdSince;
        private int _turnDir;
        private float _turnPressedAt;
        private bool _turnHeld;
        private bool _armed;
        private float _started;
        private float _distance = 1.5f;
        private float _yaw;
        private Vector3 _submitted;
        private float _submittedYaw;
        private int _previousCalledMs;
        // 裏山改変: 今のマーカー位置が置ける場所か (接地 + 傾き)。-1 = 未表示 (文言を出し直す印)
        private bool _groundOk;
        private int _shownGround = -1;
        private string _hintKey = "";
        public bool Active { get { return _state != 0; } }

        public void _Begin()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || board == null) return;
            KD_SleepSlot slot = board.SlotOf(requestedDog);
            if (!board.CanCall(lp) && (slot == null || !slot.IsSleeperLocal)) return;
            if (Active || slot == null || !slot.SelectionReady || slot.CallPending || marker == null
                || (slot.LiveSleeperId >= 0 && !slot.IsSleeperLocal)) return;
            _slot = slot;
            _pose = Mathf.Clamp(requestedPose, 0, KD_SleepSlot.MaxPoseId);
            _state = 1; _armed = false; _started = Time.time; _distance = 1.5f;
            _holdingConfirm = false; _turnDir = 0; _turnHeld = false;
            // 移動は止めない (IMP-DOG-031)。左スティックは歩行、向きは板の ◀▶。
            SetProgress(0f);
            _yaw = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation.eulerAngles.y;
            if (distanceSlider != null) distanceSlider.SetValueWithoutNotify(_distance);
            if (yawSlider != null) yawSlider.SetValueWithoutNotify(_yaw);
            for (int i = 0; markerDogs != null && i < markerDogs.Length; i++)
                if (markerDogs[i] != null) markerDogs[i].SetActive(i == requestedDog);
            for (int i = 0; poseGhosts != null && i < poseGhosts.Length; i++)
                if (poseGhosts[i] != null) poseGhosts[i].SetActive(i == requestedDog * (KD_SleepSlot.MaxPoseId + 1) + _pose);
            if (marker != null) marker.gameObject.SetActive(true);
            if (vrLabel != null) vrLabel.gameObject.SetActive(true);
            if (screenCanvas != null) screenCanvas.SetActive(!lp.IsUserInVR());
            if (pouch != null) { pouch._HideForPlacement(); pouch._SuppressLauncher(); }
            _hintKey = lp.IsUserInVR() ? "KD_SleepPlacement.VrHint" : InputManager.GetLastUsedInputMethod() == VRCInputMethod.Touch ? "KD_SleepPlacement.TouchHint" : "KD_SleepPlacement.Hint";
            // 裏山改変: 最初の UpdatePreview で文言と印を決める
            _groundOk = false; _shownGround = -1;
            ShowStatus(_hintKey);
            UpdatePreview(lp);
        }

        public void _DistanceChanged() { if (_state == 1 && distanceSlider != null) _distance = Mathf.Clamp(distanceSlider.value, .5f, 3f); }
        public void _YawChanged() { if (_state == 1 && yawSlider != null) _yaw = yawSlider.value; }

        // 板の ◀▶ (EventTrigger PointerDown → *Down、PointerUp / PointerExit / onClick → _TurnUp)。
        // _TurnUp は 3 経路から届くので 2 回目以降は何もしない。
        public void _TurnLeftDown() { BeginTurn(-1); }
        public void _TurnRightDown() { BeginTurn(1); }
        public void _TurnUp()
        {
            if (_turnDir == 0) return;
            if (!_turnHeld) _yaw = Mathf.Repeat(_yaw + _turnDir * TurnStep, 360f);
            _turnDir = 0; _turnHeld = false;
        }
        private void BeginTurn(int dir)
        {
            if (_state != 1) return;
            _turnDir = dir; _turnPressedAt = Time.time; _turnHeld = false;
        }

        public void _Confirm()
        {
            if (_state != 1 || !_armed || _slot == null || marker == null) return;
            // 裏山改変: 平らでない / 地面が無い場所では確定しない (赤い印と文言はそのまま。板は閉じない)
            if (!_groundOk) { ShowStatus(NeedFlatKey); return; }
            _submitted = marker.position;
            _submittedYaw = marker.eulerAngles.y;
            _previousCalledMs = _slot.CalledMs;
            _slot.requestedSpot = _submitted;
            _slot.requestedYaw = marker.eulerAngles.y;
            _slot.requestedPose = _pose;
            if (!_slot._TryCallPlaced()) { Fail(); return; }
            _state = 2; _started = Time.time;
            ShowStatus("KD_SleepPlacement.Waiting");
        }

        public void _Cancel()
        {
            Close(true);
        }
        public void _CancelHidden() { Close(false); }

        private void Fail()
        {
            Close(true);
            if (pouch != null) pouch._CallFailed();
        }

        private bool IsSubmittedCall()
        {
            return _slot != null && _slot.IsSleeperLocal && _slot.HasSpot && _slot.CalledMs != _previousCalledMs
                && _slot.PoseId == _pose
                && Vector3.Distance(_slot.Spot, _submitted) < .001f
                && Mathf.Abs(Mathf.DeltaAngle(_slot.SpotYaw, _submittedYaw)) < .01f;
        }

        private void Close(bool reopen)
        {
            _state = 0;
            _holdingConfirm = false; _turnDir = 0; _turnHeld = false;
            if (_slot != null) _slot._EndSelection();
            _slot = null;
            SetProgress(0f);
            if (marker != null) marker.gameObject.SetActive(false);
            for (int i = 0; invalidMarks != null && i < invalidMarks.Length; i++)
                if (invalidMarks[i] != null) invalidMarks[i].SetActive(false);   // 裏山改変: 赤い印も閉じる
            _shownGround = -1;
            if (screenCanvas != null) screenCanvas.SetActive(false);
            // 板ごと消す (VRCUiShape の Collider を最後の頭の位置に残さない)。
            if (vrLabel != null) vrLabel.transform.parent.gameObject.SetActive(false);
            if (pouch != null)
            {
                if (pouch.callPose != null) pouch.callPose._Dismiss();
                pouch._ReleaseLauncher();
                if (reopen) pouch._Toggle();
            }
        }

        public KD_Language language;
        private string L(string source) { return language != null ? language._Text(source) : source; }

        private void Start() { Close(false); }

        private void OnDisable() { Close(false); }

        private void SetProgress(float value)
        {
            if (confirmProgress == null) return;
            confirmProgress.fillAmount = value;
            confirmProgress.gameObject.SetActive(value > 0f);
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            // SDKの左右定数で判定する。開始操作を確定へ持ち越さない。
            // 左は板 (◀▶ / やめる) のクリックに使うので取消には割り当てない (IMP-DOG-031)。保持の追跡だけ残す (受付開始の条件)。
            if (args.handType == HandType.RIGHT) _rightHeld = value;
            else if (args.handType == HandType.LEFT) _leftHeld = value;
            else return;
            if (args.handType == HandType.RIGHT && !value) { _holdingConfirm = false; SetProgress(0f); }
            if (!Active || !value) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (lp.IsUserInVR())
            {
                if (args.handType == HandType.RIGHT && _state == 1 && _armed) { _holdingConfirm = true; _holdSince = Time.time; }
            }
            else if ((int)InputManager.GetLastUsedInputMethod() != (int)VRCInputMethod.Touch)
                _Confirm();
        }

        private void Update()
        {
            if (!Active) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { Close(false); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { _Cancel(); return; }
            if (_slot == null || marker == null || board == null) { Fail(); return; }
            _slot._KeepSelectionAlive();
            if (!_armed && !_rightHeld && !_leftHeld && Time.time - _started > .2f) _armed = true;
            // 裏山改変: 置けない場所では押せない
            if (confirmButton != null) confirmButton.interactable = _state == 1 && _armed && _groundOk;
            if (_state == 2)
            {
                // IMP-DOG-032: 確定成功は板を閉じたまま (犬が来るのを板に遮られずに見る)。取消・失敗は同じタブへ戻す。
                if (_slot != null && !_slot.CallPending && IsSubmittedCall()) { Close(false); return; }
                if (!_slot.CallPending || Time.time - _started > 9f) Fail();
                return;
            }
            if (!_slot.SelectionReady || (!board.IsLocalAllowedToCall && !_slot.IsSleeperLocal)) { Fail(); return; }
            if (_turnDir != 0 && Time.time - _turnPressedAt >= TapSeconds)
            {
                _yaw = Mathf.Repeat(_yaw + _turnDir * TurnHoldSpeed * Time.deltaTime, 360f);
                _turnHeld = true;
            }
            if (!lp.IsUserInVR())
            {
                _distance = Mathf.Clamp(_distance + Input.GetAxis("Mouse ScrollWheel") * .5f, .5f, 3f);
                if (Input.GetKey(KeyCode.Q)) _yaw -= 60f * Time.deltaTime;
                if (Input.GetKey(KeyCode.E)) _yaw += 60f * Time.deltaTime;
                _yaw = Mathf.Repeat(_yaw, 360f);
                if (distanceSlider != null) distanceSlider.SetValueWithoutNotify(_distance);
                if (yawSlider != null) yawSlider.SetValueWithoutNotify(_yaw);
                if (Input.GetKeyDown(KeyCode.Return)) { _Confirm(); return; }
            }
            UpdatePreview(lp);
            // 裏山改変: 長押しは置ける場所でだけ進む
            if (lp.IsUserInVR() && _holdingConfirm && _rightHeld && !_groundOk) { _holdingConfirm = false; SetProgress(0f); }
            if (lp.IsUserInVR() && _holdingConfirm && _rightHeld)
            {
                float progress = Mathf.Clamp01((Time.time - _holdSince) / ConfirmHoldSeconds);
                SetProgress(progress);
                if (progress >= 1f) { _holdingConfirm = false; SetProgress(0f); _Confirm(); }
            }
        }

        private void UpdatePreview(VRCPlayerApi lp)
        {
            if (marker == null) return;
            bool vr = lp.IsUserInVR();
            var pose = lp.GetTrackingData(vr ? VRCPlayerApi.TrackingDataType.RightHand : VRCPlayerApi.TrackingDataType.Head);
            Vector3 forward = pose.rotation * Vector3.forward;
            // 裏山改変: IMP-DOG-016 変更 2。狙いの点を真下の地面へ接地させ、置けるかを枠と同じ式で判定する。
            // 古民家は狙いの点 (手の前 / 頭の前) にそのまま置いていた (平地の庭では床の高さと大差が無かった)。
            float reach = vr ? .15f : _distance;
            Vector3 aim = pose.position + forward * reach;
            RaycastHit hit;
            if (Physics.Raycast(pose.position, forward, out hit, reach, KD_SleepSlot.HillsideGroundMask, QueryTriggerInteraction.Ignore))
                aim = hit.point;   // 見下ろした坂の中へ埋めない
            bool grounded = Physics.Raycast(aim + Vector3.up * KD_SleepSlot.HillsideGroundProbe, Vector3.down, out hit,
                KD_SleepSlot.HillsideGroundProbe + GroundSearchDepth, KD_SleepSlot.HillsideGroundMask, QueryTriggerInteraction.Ignore);
            marker.position = grounded ? hit.point : aim;
            marker.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _groundOk = grounded && _slot != null && _slot._PlacementGroundOk(marker.position);
            ShowGround();
            bool touch = !vr && InputManager.GetLastUsedInputMethod() == VRCInputMethod.Touch;
            if (screenCanvas != null && screenCanvas.activeSelf != touch) screenCanvas.SetActive(touch);
            if (vrLabel != null)
            {
                Transform hud = vrLabel.transform.parent;
                if (hud.gameObject.activeSelf != !touch) hud.gameObject.SetActive(!touch);
                // ◀▶ / やめる は VR だけ (Desktop は Q・E / Esc)。
                if (vrButtons != null && vrButtons.activeSelf != vr) vrButtons.SetActive(vr);
                if (!touch)
                {
                    var head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                    hud.position = head.position + head.rotation * new Vector3(0f, -.20f, .75f);
                    hud.rotation = head.rotation;
                }
            }
        }

        /// <summary>裏山改変: 置ける / 置けないが変わったときだけ、赤い印と文言を切り替える。</summary>
        private void ShowGround()
        {
            int now = _groundOk ? 1 : 0;
            if (now == _shownGround) return;
            _shownGround = now;
            for (int i = 0; invalidMarks != null && i < invalidMarks.Length; i++)
                if (invalidMarks[i] != null) invalidMarks[i].SetActive(!_groundOk && i == requestedDog);
            if (_state == 1) ShowStatus(_groundOk ? _hintKey : NeedFlatKey);
        }

        private void ShowStatus(string value)
        {
            if (statusLabel != null) statusLabel.text = L(value);
            if (vrLabel != null) vrLabel.text = L(value);
        }
    }
}
