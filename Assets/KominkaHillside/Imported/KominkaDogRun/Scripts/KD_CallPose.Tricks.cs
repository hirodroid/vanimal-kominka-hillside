using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaDogRun
{
    // 裏山改変: IMP-DOG-054 芸「バーン」「ジャンプ」の入口 (裏山だけ。古民家には無い)。契約: docs/development/issues/IMP-DOG-054-tricks-bang-jump.md §2.2・§2.3・§4.2。
    //
    //   入口は 1 つ: RequestTrick(dog, kind)。ボタン (_Bang / _Jump) は面の犬 (_dog)、VR の合図は判定した犬の添字を渡す
    //   (合図は面を開いていなくても動く)。可否はすべて _TrickStatus(dog) の値で分岐する (ボタン・合図・KH_TrickGuide の 3 つとも)。
    //   合図 (VR だけ): 犬の根のローカルに置いたゾーン (Tricks/3 が座り姿勢の頭の骨 Bone.004 から計算) へ「入る動き」で一発受理。
    //     手は左右別の位置履歴から速度ベクトルを出す (追跡の喪失・復帰、間の空いた tick の後は履歴を初期化)。候補は空いている手
    //     (GetPickupInHand が null。KH_HandWash と同じ) のうちゾーンの中心に近い方。ゾーン内外は状態 4・5 (再生中・間隔中) でも毎 tick 更新し、
    //     止めるのは要求の発行だけ。外 -> 内の 1 回の入りで 1 回だけ受け、解禁時に既に中にいる手は一度出るまで受けない。
    //   同期・権利の判定は KD_SleepSlot (送り手の早期判定と Owner の HandleRequest)。ここはローカル (同期なし) で共有状態を書かない。
    public partial class KD_CallPose
    {
        // ---- 合図の調整値 (初期値。不受理ケースの試作で決める。ゾーンの位置・半径は Tricks/3 がこの値で配列へ焼く) ----
        public const float TrickSpeedMin = .6f;      // 入る速さ (m/s)。バーンは速度の大きさ、ジャンプは上向き成分
        public const float TrickBangAngle = 30f;     // バーン: 速度と「手 -> 犬の頭」の向きの最大の角度 (度)
        public const float TrickBangForward = .35f;  // バーン: 頭の骨から鼻の向き (根の前方を水平に投影) へ (m)
        public const float TrickJumpUp = .45f;       // ジャンプ: 頭の骨の上へ (m)
        public const float TrickZoneRadius = .15f;   // ゾーンの半径 (m)
        private const float TrickHandStale = .15f;   // これより間の空いた手の履歴は捨てる (s)。tick は 0.05 s、呼んでいない間は 0.25 s
        private const float TrickNoticeSeconds = 5f; // 未所持の押下で状態行に出す案内の長さ (s)

        public KominkaHillside.KH_TrainerPass trainerPass;
        /// <summary>添字 dog * 2 + (kind - 2)。犬の根 (DogRoot) のローカル。Tricks/3 が書く (12)。</summary>
        public Vector3[] trickZoneCenters;
        public float[] trickZoneRadii;
        public Button bangButton;
        public Button jumpButton;

        private Vector3[] _trickHand = new Vector3[2];
        private Vector3[] _trickVelocity = new Vector3[2];
        private float[] _trickHandTime = new float[2];
        private bool[] _trickHandValid = new bool[2];
        private bool[] _trickVelocityValid = new bool[2];
        private bool[] _trickHandFree = new bool[2];
        private int _trickSampledFrame = -1;
        private bool[] _trickInside = new bool[24];   // (dog * 2 + kind - 2) * 2 + hand
        private bool[] _trickTracked = new bool[6];
        private string _trickNotice = "";
        private float _trickNoticeUntil;

        // ================================================================ 入口

        public void _Bang() { if (_purchase >= 0 || !PageOpen) return; RequestTrick(_dog, KD_SleepSlot.TrickBang); }
        public void _Jump() { if (_purchase >= 0 || !PageOpen) return; RequestTrick(_dog, KD_SleepSlot.TrickJump); }

        private void RequestTrick(int dog, int kind)
        {
            int state = _TrickStatus(dog);
            if (state == 6)
            {
                // 未所持: 状態行に案内 + トレーナーの購入画面 (KD_ActivitiesMenu の [購入画面をひらく] と同じ呼び出し)
                _trickNotice = "call.trick.trainer"; _trickNoticeUntil = Time.time + TrickNoticeSeconds;
                if (trainerPass != null) trainerPass._OpenStore();
                Draw();
                return;
            }
            if (state != 0) { Draw(); return; }
            KD_SleepSlot slot = board.SlotOf(dog);
            // 送り手だけの条件 (_TrickStatus でも見ている。送る直前にもう一度)
            if (slot == null || dogs[dog] == null || !dogs[dog].IsCompanionReady || dogs[dog].CalledTreatActive
                || (placement != null && placement.Active)) return;
            _desktopUntil[dog] = 0f; _dwell[dog] = 0f;
            if (kind == KD_SleepSlot.TrickBang) slot._Bang();
            else if (kind == KD_SleepSlot.TrickJump) slot._Jump();
            Draw();
        }

        // ================================================================ 状態 (副作用なし)

        /// <summary>0 使える / 1 呼んだ犬がいない・未到着 / 2 座っていない / 3 ほかの事をしている / 4 再生中 / 5 間隔の残り /
        /// 6 トレーナー未所持 / 7 所持の読み込み中 / 8 提供前 (無効)。上から最初に当たったもの (契約 §4.2)。</summary>
        public int _TrickStatus(int dog)
        {
            if (board == null || dogs == null || dog < 0 || dog >= dogs.Length) return 1;
            KD_SleepSlot slot = board.SlotOf(dog);
            if (slot == null) return 1;
            if (!slot.tricksEnabled) return 8;
            if (trainerPass != null && !trainerPass._Loaded()) return 7;
            if (trainerPass == null || !trainerPass._OwnedBy(Networking.LocalPlayer)) return 6;
            if (!slot.IsSleeperLocal || dogs[dog] == null || !dogs[dog].IsCompanionReady) return 1;
            if (slot.PoseId != 2) return 2;
            int kind = slot.TrickKind;
            bool active = slot.TrickActive;
            if ((active && kind == KD_SleepSlot.TrickStretch) || dogs[dog].CalledTreatActive || (placement != null && placement.Active)
                || slot.CallPending || slot.TrickRequestPending) return 3;
            if (active && (kind == KD_SleepSlot.TrickBang || kind == KD_SleepSlot.TrickJump)) return 4;
            if (slot.TrickWaitMs > 0) return 5;
            if (!slot.CanStretch) return 1;   // 間隔でも再生でもない不可 (未到着など)
            return 0;
        }

        /// <summary>状態 5 の残り秒 (切り上げ前)。それ以外は 0。</summary>
        public float _TrickWaitSeconds(int dog)
        {
            if (_TrickStatus(dog) != 5) return 0f;
            KD_SleepSlot slot = board.SlotOf(dog);
            return slot == null ? 0f : slot.TrickWaitMs / 1000f;
        }

        /// <summary>ガイドの対象: 本人が呼んでいる犬のうち状態が 0・4・5 の最初の 1 匹 (添字の小さい順)。無ければ -1。</summary>
        public int _TrickGuideDog()
        {
            if (board == null || dogs == null) return -1;
            for (int i = 0; i < dogs.Length; i++)
            {
                KD_SleepSlot slot = board.SlotOf(i);
                if (slot == null || !slot.IsSleeperLocal) continue;
                int state = _TrickStatus(i);
                if (state == 0 || state == 4 || state == 5) return i;
            }
            return -1;
        }

        /// <summary>kind 2 / 3 のゾーンの中心 (ワールド)。不正なら Vector3.zero。</summary>
        public Vector3 _TrickZoneCenter(int dog, int kind)
        {
            int zone = TrickZoneIndex(dog, kind);
            if (zone < 0 || trickZoneCenters == null || zone >= trickZoneCenters.Length) return Vector3.zero;
            KD_SleepSlot slot = board.SlotOf(dog);
            Transform root = slot == null ? null : slot.DogRoot();
            return root == null ? Vector3.zero : root.TransformPoint(trickZoneCenters[zone]);
        }

        /// <summary>ゾーンの半径 (m)。不正なら 0。</summary>
        public float _TrickZoneRadius(int dog, int kind)
        {
            int zone = TrickZoneIndex(dog, kind);
            if (zone < 0 || trickZoneRadii == null || zone >= trickZoneRadii.Length) return 0f;
            return trickZoneRadii[zone];
        }

        /// <summary>直近に受理された種類 (同期の TrickKind。呼ばれていなければ 0)。</summary>
        public int _TrickLastAccepted(int dog)
        {
            if (board == null || dogs == null || dog < 0 || dog >= dogs.Length) return 0;
            KD_SleepSlot slot = board.SlotOf(dog);
            return slot == null ? 0 : slot.TrickKind;
        }

        private int TrickZoneIndex(int dog, int kind)
        {
            if (board == null || dogs == null || dog < 0 || dog >= dogs.Length) return -1;
            if (kind != KD_SleepSlot.TrickBang && kind != KD_SleepSlot.TrickJump) return -1;
            return dog * 2 + kind - KD_SleepSlot.TrickBang;
        }

        // ================================================================ VR の合図 (Update の tick から)

        /// <summary>左右の手の位置・速度・空き。1 フレームに 1 回だけ (犬ごとの判定から必要な時だけ呼ぶ)。</summary>
        private void SampleTrickHands(VRCPlayerApi lp)
        {
            if (_trickSampledFrame == Time.frameCount) return;
            _trickSampledFrame = Time.frameCount;
            float now = Time.time;
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            for (int h = 0; h < 2; h++)
            {
                Vector3 p = lp.GetTrackingData(h == 0 ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand).position;
                _trickHandFree[h] = !Utilities.IsValid(lp.GetPickupInHand(h == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right));
                // 追跡の喪失・未初期化 (お手と同じ規則): 原点付近・頭から 2.5 m 以上
                bool valid = p.sqrMagnitude > .0001f && Vector3.Distance(p, head) < 2.5f;
                if (!valid) { _trickHandValid[h] = false; _trickVelocityValid[h] = false; continue; }
                float elapsed = now - _trickHandTime[h];
                // 履歴の初期化: 追跡の復帰 (前回無効)・間の空いた tick の後は 1 tick 目に速度を出さない
                _trickVelocityValid[h] = _trickHandValid[h] && elapsed > .001f && elapsed <= TrickHandStale;
                _trickVelocity[h] = _trickVelocityValid[h] ? (p - _trickHand[h]) / elapsed : Vector3.zero;
                _trickHand[h] = p; _trickHandTime[h] = now; _trickHandValid[h] = true;
            }
        }

        private void UpdateTrick(int i, VRCPlayerApi lp)
        {
            if (i < 0 || i >= _trickTracked.Length) return;
            KD_SleepSlot slot = board.SlotOf(i);
            Transform root = slot == null ? null : slot.DogRoot();
            bool track = slot != null && slot.IsSleeperLocal && slot.tricksEnabled && root != null && lp.IsUserInVR()
                && trickZoneCenters != null && trickZoneRadii != null;
            bool wasTracked = _trickTracked[i];
            _trickTracked[i] = track;
            if (!track)
            {
                for (int k = 0; k < 4; k++) _trickInside[i * 4 + k] = false;
                return;
            }
            SampleTrickHands(lp);
            bool fired = false;
            for (int kind = KD_SleepSlot.TrickBang; kind <= KD_SleepSlot.TrickJump; kind++)
            {
                int zone = i * 2 + kind - KD_SleepSlot.TrickBang;
                int a = zone * 2;
                if (zone >= trickZoneCenters.Length || zone >= trickZoneRadii.Length) { _trickInside[a] = false; _trickInside[a + 1] = false; continue; }
                Vector3 center = root.TransformPoint(trickZoneCenters[zone]);
                float radius = Mathf.Max(.05f, trickZoneRadii[zone]);
                // 候補: 空いている手のうち中心に近い方 (左右固定にしない)
                int pick = -1; float best = 0f;
                for (int h = 0; h < 2; h++)
                {
                    if (!_trickHandValid[h] || !_trickHandFree[h]) continue;
                    float d = (_trickHand[h] - center).sqrMagnitude;
                    if (pick < 0 || d < best) { pick = h; best = d; }
                }
                // ゾーン内外は毎 tick 更新する (道具を持った手も。持ったまま入って離しても「入り」にしない)
                bool entered = false;
                for (int h = 0; h < 2; h++)
                {
                    bool inside = _trickHandValid[h] && Vector3.Distance(_trickHand[h], center) <= radius;
                    if (h == pick) entered = inside && !_trickInside[a + h];
                    _trickInside[a + h] = inside;
                }
                // 追跡を始めた tick (呼んで座った・VR へ切替) は内外を控えるだけ。既に中にいる手は一度出るまで受けない
                if (!wasTracked || fired || pick < 0 || !entered) continue;
                if (!_trickVelocityValid[pick]) continue;
                // 止めるのは要求の発行だけ (再生中・間隔中・未所持などは判定を続けて受けない)
                if (_TrickStatus(i) != 0) continue;
                Vector3 v = _trickVelocity[pick];
                bool cue;
                if (kind == KD_SleepSlot.TrickBang)
                {
                    Vector3 forward = root.forward; forward.y = 0f;
                    Vector3 headPoint = forward.sqrMagnitude > .0001f ? center - forward.normalized * TrickBangForward : center;
                    Vector3 toHead = headPoint - _trickHand[pick];
                    cue = v.magnitude >= TrickSpeedMin && toHead.sqrMagnitude > .000001f && Vector3.Angle(v, toHead) <= TrickBangAngle;
                }
                else cue = v.y >= TrickSpeedMin;
                if (!cue) continue;
                fired = true;
                RequestTrick(i, kind);
            }
        }

        // ================================================================ ボタン (Draw から)

        private void DrawTricks()
        {
            DrawTrickButton(bangButton, "call.trick.bang");
            DrawTrickButton(jumpButton, "call.trick.jump");
            if (status == null) return;
            // 状態行の優先 (上が勝つ): Draw の既存 (さんぽ中の理由 > 犬を確認中 > call.unavailable) > 未所持の押下の案内 (5 s) >
            // 芸の押せない理由 (TrickReason) > 既存のポーズの案内 (call.pawHint など)
            if (Time.time < _trickNoticeUntil && _trickNotice.Length > 0) { status.text = L(_trickNotice); return; }
            string reason = TrickReason();
            if (reason.Length > 0) status.text = reason;
        }

        /// <summary>芸のボタンが押せない理由 (状態 2・3)。新しい文言キーは使わない (既存キーの流用)。
        /// 上書きするのは「本人が呼んでいて芸のボタンが押せない」かつ「おすわりのタブ (_pose == 2) で既存の行がお手の案内 (call.pawHint)」の時だけ。
        /// お手の案内は座っている時のものなので、座っていない・ほかの事をしている間は芸の理由に譲る。ふせ・ごろん等のタブ (あご乗せの案内など) と、
        /// 既存の上位の理由 (さんぽ中・確認中・操作できない・追加動作の購入案内) は上書きしない。状態 1 は上書きしない (伸びのボタンと同じく既存の行のまま)。</summary>
        private string TrickReason()
        {
            if (_purchase >= 0 || _pose != 2 || !Owned(2) || board == null) return "";
            KD_SleepSlot slot = board.SlotOf(_dog);
            if (slot == null || !slot.IsSleeperLocal || slot.SelectionPending || !slot.SelectionReady || board._IsWalkBusy(_dog)) return "";
            int state = _TrickStatus(_dog);
            // 2: 座っていない → 「現在: ふせ -> おすわり」(call.current + 今の姿勢 + call.pose2。矢印は ASCII で字形を足さない)
            if (state == 2) return L("call.current") + L(PoseKey(slot.PoseId)) + " -> " + L("call.pose2");
            // 3: 要求の往復中は「犬を確認中」、伸び中・おやつ中・配置中は「この犬は今操作できません」
            if (state == 3) return L(slot.CallPending || slot.TrickRequestPending ? "call.selectionWaiting" : "call.unavailable");
            return "";
        }

        private void DrawTrickButton(Button button, string key)
        {
            if (button == null) return;
            int state = _TrickStatus(_dog);
            // 提供前 (8) だけ出さない。ほかは全員に出し、0 (使える) と 6 (未所持 = 購入画面へ) だけ押せる。
            // 1 (呼んでいない・未到着) も出したまま押せなくする (理由は既存の状態行のまま。2・3 は TrickReason)
            bool show = state != 8;
            button.gameObject.SetActive(show);
            if (!show) return;
            string word = L(key);
            string label = word;
            if (state == 6) label = word + "  " + L("call.trick.trainerTag");
            else if (state == 7) label = word + "  " + L("KD_ShopShelf.WordLoading");
            else if (state == 4) label = word + "  " + L("call.trick.playing");
            else if (state == 5) label = word + "  " + L("call.trick.wait").Replace("{0}", Mathf.CeilToInt(_TrickWaitSeconds(_dog)).ToString());
            // 状態 1・2・3 は芸名のまま押せなくする。理由は伸びのボタンと同じ状態行 (DrawTricks → TrickReason)
            TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) text.text = label;
            button.interactable = _purchase < 0 && (state == 0 || state == 6);
        }
    }
}
