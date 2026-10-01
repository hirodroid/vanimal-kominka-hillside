using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-ENV-011 / IMP-DOG-035 (docs/plans/2026-10-01-wash-call-and-height.md §4): the board on a wash station's shelf.
    // Local only: every button is a String Persistent Listener (SendCustomEvent) wired by the builder to one handler here,
    // which calls the station's local entry (KH_WashStationCall._Call / _Release / _Lift, KH_WashPersonalFloor._Adjust).
    // The Call decides and syncs; nothing here is synced and there is no network event. The status line (who was called,
    // the stand's and my floor's height) is rebuilt every refreshSeconds while the local player is within showMeters.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WashStationBoard : UdonSharpBehaviour
    {
        public KH_WashStationCall call;
        public KH_WashPersonalFloor personalFloor;
        public TextMeshProUGUI statusText;
        [Tooltip("犬の名前 (dogIndex の順、6)。ビルダーが書く")]
        public string[] dogNames;
        [Tooltip("状態の文言 (ビルダーが書く。Wash/12 が字形を確かめる)")]
        public string wordPick = "犬をえらんでよぶ";
        public string wordCalling = "を呼んでいます";
        public string wordHere = "がいます";
        public string wordBusy = "いまは来られません";
        public string wordLeashed = "ほかの人が散歩中です";
        [Tooltip("断られた知らせを出す秒数")]
        public float refuseShowSeconds = 3f;
        public string wordStand = "台";
        public string wordMe = "自分";
        public float refreshSeconds = 0.25f;
        public float showMeters = 12f;
        [Tooltip("押した犬を「呼んでいます」と出す上限 (秒)。受理・拒否・おわりで先に消える")]
        public float myPickSeconds = 8f;

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

        string DogName(int dog)
        {
            return dogNames != null && dog >= 0 && dog < dogNames.Length ? dogNames[dog] : dog.ToString();
        }

        void DoCall(int dog)
        {
            if (call != null)
            {
                _myPick = dog;
                _myPickUntil = Time.time + myPickSeconds;
                _myPickSerial = call._RefuseSerial();
                call._Call(dog);
            }
            _nextAt = 0f;
        }

        void Update()
        {
            if (Time.time < _nextAt) return;
            _nextAt = Time.time + refreshSeconds;
            if (statusText == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            Vector3 d = lp.GetPosition() - transform.position; d.y = 0f;
            if (d.magnitude > showMeters) return;
            string s = StatusLine();
            if (s != _shown) { _shown = s; statusText.text = s; }
        }

        string StatusLine()
        {
            string head = wordPick;
            float stand = 0f;
            if (call != null)
            {
                // a refusal (KH_WashStationCall._RefuseSerial moved) is shown for refuseShowSeconds
                int serial = call._RefuseSerial();
                if (_seenRefuse < 0) _seenRefuse = serial;   // what was refused before I came is not news
                else if (serial != _seenRefuse)
                {
                    _seenRefuse = serial;
                    int why = call._RefusedReason();
                    if (why == KH_WashStationCall.RefuseBusy || why == KH_WashStationCall.RefuseLeashedByOther)
                    {
                        _refuseText = DogName(call._RefusedDog()) + " " + (why == KH_WashStationCall.RefuseBusy ? wordBusy : wordLeashed);
                        _refuseUntil = Time.time + refuseShowSeconds;
                    }
                }
                int dog = call._HeldDog();
                int pending = call._PendingDog();
                if (_myPick >= 0 && (dog == _myPick || serial != _myPickSerial || Time.time > _myPickUntil)) _myPick = -1;   // accepted / refused / timed out
                if (Time.time < _refuseUntil) head = _refuseText;
                else if (dog >= 0 && dog != _myPick) head = DogName(dog) + (call._Arrived() ? wordHere : wordCalling);
                else if (_myPick >= 0) head = DogName(_myPick) + wordCalling;
                else if (pending >= 0) head = DogName(pending) + wordCalling;
                stand = call._LiftMeters();
            }
            float me = personalFloor != null ? personalFloor._TargetHeight() : 0f;
            return head + "\n" + wordStand + " " + stand.ToString("F1") + "m   " + wordMe + " " + me.ToString("F1") + "m";
        }
    }
}
