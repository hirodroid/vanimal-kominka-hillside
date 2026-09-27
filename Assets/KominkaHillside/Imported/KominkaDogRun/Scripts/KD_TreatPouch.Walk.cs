using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の裏山差分: さんぽタブ (Page_Walk、タブ 2。計画書 D-2 / D-5)。
    ///
    /// KH_HandMenu (段階 0-b で退役) の AttachGate / Attach / Detach / ChooseDestination / GateText / Pending / PollPending を
    /// **挙動と文言を変えずに**移した。共有状態は KH_LeashManager が正本で、板は要求を出して結果を読み戻すだけ (同期変数 0 本)。
    ///
    /// 行: WalkRow0..5 = 犬 (KD_DogDefs.All の順)、WalkRow6 = 広場、WalkRow7 = 古民家側の入口。
    /// 要求の結果は板の下端 (Feedback) の 1 行に出す (4 秒。応答が無ければ 4 秒で「結果を確認できませんでした」)。
    /// 要求は 1 本だけ持つ — 新しい要求は前の要求を置き換えるので、連打・タブ変更・閉じて再表示でも
    /// 別の要求の結果を取り違えて表示しない。ポーリングは Redraw の外 (Update → PollSharedRequests) で毎フレーム回る。</summary>
    public partial class KD_TreatPouch
    {
        [Header("さんぽ (裏山。行 0..5 = 犬、6 = 広場、7 = 古民家側の入口)")]
        [Tooltip("行の名前 (犬 6 + 行き先 2)")]
        public TextMeshProUGUI[] walkLabels;
        [Tooltip("行の状態 (犬 6 + 行き先 2)")]
        public TextMeshProUGUI[] walkStateLabels;
        public UnityEngine.UI.Button[] walkLeftButtons;
        public UnityEngine.UI.Button[] walkRightButtons;
        public UnityEngine.UI.Button[] walkOffButtons;
        public UnityEngine.UI.Image[] walkLeftImages;
        public UnityEngine.UI.Image[] walkRightImages;
        public UnityEngine.UI.Image[] walkOffImages;
        [Tooltip("行き先 2 行の [ここへ]。0 = 広場 (WalkRow6)、1 = 古民家側の入口 (WalkRow7)")]
        public UnityEngine.UI.Button[] walkHereButtons;
        public UnityEngine.UI.Image[] walkHereImages;
        public TextMeshProUGUI[] walkHereLabels;
        [Tooltip("Optional destination rows after the six dogs. Empty keeps the original plaza/gate menu.")]
        public int[] walkDestinationIds;
        public string[] walkDestinationKeys;

        /// <summary>KH_LeashManager の行き先の値 (destinations[] / _ChooseDestination)。</summary>
        public const int WalkDestGate = 0;
        public const int WalkDestPlaza = 1;

        // 要求は照合先の共有状態 (接続 / 解除 / 行き先) で成立を確かめる
        private int _walkPendingKind;   // 0 none, 1 attach, 2 detach, 3 destination
        private int _walkPendingDog, _walkPendingHand, _walkPendingDest;
        private float _walkPendingAt;

        public void _WalkLeft0() { WalkAttach(0, 0); }
        public void _WalkLeft1() { WalkAttach(1, 0); }
        public void _WalkLeft2() { WalkAttach(2, 0); }
        public void _WalkLeft3() { WalkAttach(3, 0); }
        public void _WalkLeft4() { WalkAttach(4, 0); }
        public void _WalkLeft5() { WalkAttach(5, 0); }
        public void _WalkRight0() { WalkAttach(0, 1); }
        public void _WalkRight1() { WalkAttach(1, 1); }
        public void _WalkRight2() { WalkAttach(2, 1); }
        public void _WalkRight3() { WalkAttach(3, 1); }
        public void _WalkRight4() { WalkAttach(4, 1); }
        public void _WalkRight5() { WalkAttach(5, 1); }
        public void _WalkOff0() { WalkDetach(0); }
        public void _WalkOff1() { WalkDetach(1); }
        public void _WalkOff2() { WalkDetach(2); }
        public void _WalkOff3() { WalkDetach(3); }
        public void _WalkOff4() { WalkDetach(4); }
        public void _WalkOff5() { WalkDetach(5); }
        public void _WalkPlaza() { WalkChooseDestination(WalkDestPlaza); }
        public void _WalkGate() { WalkChooseDestination(WalkDestGate); }
        public void _WalkLoopForward() { WalkChooseDestination(2); }
        public void _WalkLoopReverse() { WalkChooseDestination(3); }
        public void _WalkUpperPlaza() { WalkChooseDestination(4); }
        public void _WalkRiverbank() { WalkChooseDestination(5); }

        /// <summary>理由は正本 (KH_LeashManager) の規則を写したもの。拒否が不意打ちにならないため (Owner がもう一度判定する)。
        /// 0 ok / 1 自分がつないでいる / 2 他人 / 3 集合中 / 4 ボール / 5 片手 3 匹 / 6 遠い / 7 呼ばれて待機中 / 9 使えない。</summary>
        private int WalkAttachGate(int dog, int hand)
        {
            if (manager == null || manager.walkers == null || dog < 0 || dog >= manager.walkers.Length) return 9;
            if (manager._IsLeashed(dog)) return manager._IsMine(dog) ? 1 : 2;
            if (manager._IsGathering(dog)) return 3;
            if (sleepBoard != null && sleepBoard._IsCalled(dog)) return 7;   // 裏山改変 (IMP-DOG-016): 呼ばれて待機中は接続不可
            if (manager.fetchers == null || dog >= manager.fetchers.Length || manager.fetchers[dog] == null || !manager.fetchers[dog].IsIdle) return 4;
            if (manager._MyCount(hand) >= 3) return 5;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (manager.dogRoots == null || dog >= manager.dogRoots.Length || manager.dogRoots[dog] == null || !Utilities.IsValid(lp)) return 9;
            if (Vector3.Distance(lp.GetPosition(), manager.dogRoots[dog].position) > manager.attachReach) return 6;
            return 0;
        }

        private void WalkAttach(int dog, int hand)
        {
            int gate = WalkAttachGate(dog, hand);
            if (gate != 0) { HillsideNote(TabWalk, WalkGateKey(gate, hand), ""); Redraw(); return; }
            manager._RequestAttach(dog, hand);
            WalkPending(1, dog, hand, -1);
            HillsideNote(TabWalk, hand == 0 ? "KD_TreatPouch.WordWalkAttachingLeft" : "KD_TreatPouch.WordWalkAttachingRight", "");
            Redraw();
        }

        private void WalkDetach(int dog)
        {
            if (manager == null || !manager._IsMine(dog)) { HillsideNote(TabWalk, "KD_TreatPouch.WordWalkDetachMineOnly", ""); Redraw(); return; }
            manager._RequestDetach(dog);
            WalkPending(2, dog, -1, -1);
            HillsideNote(TabWalk, "KD_TreatPouch.WordWalkDetaching", "");
            Redraw();
        }

        private void WalkChooseDestination(int destination)
        {
            if (manager == null || manager.route == null || manager.route._DestinationNode(destination) < 0) return;
            manager._ChooseDestination(destination);
            int mine = 0;
            for (int i = 0; i < manager.walkers.Length; i++) if (manager._IsMine(i)) mine++;
            if (mine == 0) HillsideNote(TabWalk, "KD_TreatPouch.WordWalkDestRemembered", "");
            else { WalkPending(3, -1, -1, destination); HillsideNote(TabWalk, "KD_TreatPouch.WordWalkDestTelling", mine.ToString()); }
            Redraw();
        }

        private string WalkGateKey(int gate, int hand)
        {
            if (gate == 1) return "KD_TreatPouch.WordWalkGateAlready";
            if (gate == 2) return "KD_TreatPouch.WordWalkGateOther";
            if (gate == 3) return "KD_TreatPouch.WordWalkGateGathering";
            if (gate == 4) return "KD_TreatPouch.WordWalkGateBall";
            if (gate == 5) return hand == 0 ? "KD_TreatPouch.WordWalkGateFullLeft" : "KD_TreatPouch.WordWalkGateFullRight";
            if (gate == 6) return "KD_TreatPouch.WordWalkGateFar";
            if (gate == 7) return "KD_TreatPouch.WordWalkGateCalled";   // 裏山改変 (IMP-DOG-016)
            return "KD_TreatPouch.WordWalkGateUnavailable";
        }

        private void WalkPending(int kind, int dog, int hand, int dest)
        {
            _walkPendingKind = kind; _walkPendingDog = dog; _walkPendingHand = hand; _walkPendingDest = dest; _walkPendingAt = Time.time;
        }

        /// <summary>未決の要求を共有状態から解決する。4 秒以内に成立しなければ「確認できませんでした」。
        /// **Redraw の外で毎フレーム呼ぶ** (PollSharedRequests から) — 板を閉じている間も時間切れまで進む。</summary>
        private void PollWalkPending()
        {
            if (_walkPendingKind == 0) return;
            if (manager == null) { _walkPendingKind = 0; return; }
            bool done = false;
            string key = "";
            if (_walkPendingKind == 1)
            {
                done = manager._IsMine(_walkPendingDog) && manager.hands[_walkPendingDog] == _walkPendingHand;
                key = _walkPendingHand == 0 ? "KD_TreatPouch.WordWalkAttachedLeft" : "KD_TreatPouch.WordWalkAttachedRight";
            }
            else if (_walkPendingKind == 2)
            {
                done = !manager._IsLeashed(_walkPendingDog);
                key = "KD_TreatPouch.WordWalkDetached";
            }
            else if (_walkPendingKind == 3)
            {
                bool all = true, any = false;
                for (int i = 0; i < manager.walkers.Length; i++)
                    if (manager._IsMine(i)) { any = true; if (manager.destinations[i] != _walkPendingDest) all = false; }
                done = any && all;
                key = _walkPendingDest == WalkDestPlaza ? "KD_TreatPouch.WordWalkToPlaza" :
                    (_walkPendingDest == WalkDestGate ? "KD_TreatPouch.WordWalkToGate" : "course.walk.confirmed");
            }
            if (!done && Time.time - _walkPendingAt > 4f) { done = true; key = "KD_TreatPouch.WordWalkUnknown"; }
            if (!done) return;
            _walkPendingKind = 0;
            HillsideNote(TabWalk, key, "");
            Redraw();
        }

        /// <summary>さんぽタブ。犬 6 行 (状態 + [左につなぐ][右につなぐ][はずす]) と行き先 2 行 ([ここへ])、下端に行き先のまとめ。
        /// つなげない行のボタンは暗くするが押せる — 押すと理由が下端に出る (KH_HandMenu と同じ)。[はずす] はつないでいる行だけ出す。</summary>
        private void RedrawWalk()
        {
            if (manager == null) { HillsideFooter(L("KD_TreatPouch.WordWalkGateUnavailable")); return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            int mine = 0, plaza = 0, gate = 0, firstDestination = -1;
            bool mixedDestinations = false;
            for (int i = 0; i < 6; i++)
            {
                bool leashed = manager._IsLeashed(i);
                bool isMine = manager._IsMine(i);
                string state;
                if (isMine)
                {
                    state = L(manager.hands[i] == 0 ? "KD_TreatPouch.WordWalkStateLeft" : "KD_TreatPouch.WordWalkStateRight");
                    mine++;
                    int destination = manager.destinations[i];
                    if (firstDestination < 0) firstDestination = destination;
                    else if (firstDestination != destination) mixedDestinations = true;
                    if (destination == WalkDestPlaza) plaza++;
                    else if (destination == WalkDestGate) gate++;
                }
                else if (leashed) state = L("KD_TreatPouch.WordWalkStateOther");   // 表示名は出さない (フォントに無い字は豆腐になる)
                else if (manager._IsGathering(i)) state = L("KD_TreatPouch.WordWalkStateGathering");
                else if (sleepBoard != null && sleepBoard._IsCalled(i)) state = L("KD_TreatPouch.WordWalkStateCalled");   // 裏山改変 (IMP-DOG-016)
                else if (manager.fetchers != null && i < manager.fetchers.Length && manager.fetchers[i] != null && !manager.fetchers[i].IsIdle) state = L("KD_TreatPouch.WordWalkStateBall");
                else
                {
                    float d = manager.dogRoots != null && i < manager.dogRoots.Length && manager.dogRoots[i] != null && Utilities.IsValid(lp)
                        ? Vector3.Distance(lp.GetPosition(), manager.dogRoots[i].position) : 999f;
                    state = d > manager.attachReach
                        ? L("KD_TreatPouch.WordWalkStateFar").Replace("{0}", Mathf.RoundToInt(d).ToString())
                        : L("KD_TreatPouch.WordWalkStateNear");
                }
                if (walkLabels != null && i < walkLabels.Length && walkLabels[i] != null) walkLabels[i].text = HillsideDogName(i);
                if (walkStateLabels != null && i < walkStateLabels.Length && walkStateLabels[i] != null) walkStateLabels[i].text = state;
                bool canLeft = WalkAttachGate(i, 0) == 0, canRight = WalkAttachGate(i, 1) == 0;
                WalkTint(walkLeftImages, i, canLeft ? equipOnColor : recallOffColor);
                WalkTint(walkRightImages, i, canRight ? equipOnColor : recallOffColor);
                WalkTint(walkOffImages, i, hillsideNeutralColor);
                if (walkOffButtons != null && i < walkOffButtons.Length && walkOffButtons[i] != null
                    && walkOffButtons[i].gameObject.activeSelf != isMine) walkOffButtons[i].gameObject.SetActive(isMine);
            }

            // 行き先 2 行 (6 = 広場、7 = 古民家側の入口)。選んでいる方を状態ラベルと点灯色で示す
            int choice = manager._MyChoice();
            bool extended = walkDestinationIds != null && walkDestinationIds.Length > 0;
            int destinationCount = extended ? walkDestinationIds.Length : 2;
            for (int k = 0; k < destinationCount; k++)
            {
                int row = 6 + k;
                int dest = extended ? walkDestinationIds[k] : (k == 0 ? WalkDestPlaza : WalkDestGate);
                bool chosen = choice == dest;
                if (walkLabels != null && row < walkLabels.Length && walkLabels[row] != null)
                    walkLabels[row].text = WalkDestinationName(dest);
                if (walkStateLabels != null && row < walkStateLabels.Length && walkStateLabels[row] != null)
                    walkStateLabels[row].text = L(chosen ? "KD_TreatPouch.WordWalkStateDestChosen" : "KD_TreatPouch.WordWalkStateDest");
                WalkTint(walkHereImages, k, chosen ? openOnColor : hillsideNeutralColor);
                if (walkHereLabels != null && k < walkHereLabels.Length && walkHereLabels[k] != null)
                    walkHereLabels[k].text = L("KD_TreatPouch.WordWalkHere");
            }

            string summary;
            if (mine == 0) summary = L("KD_TreatPouch.WordWalkSummaryNone");
            else if (extended) summary = mixedDestinations
                ? L("course.walk.mixed").Replace("{0}", mine.ToString())
                : L("course.walk.summary").Replace("{0}", mine.ToString()).Replace("{1}", WalkDestinationName(firstDestination));
            else if (plaza > 0 && gate > 0) summary = L("KD_TreatPouch.WordWalkSummaryMixed").Replace("{0}", plaza.ToString()).Replace("{1}", gate.ToString());
            else summary = L(plaza > 0 ? "KD_TreatPouch.WordWalkSummaryPlaza" : "KD_TreatPouch.WordWalkSummaryGate").Replace("{0}", mine.ToString());
            HillsideFooter(HillsideNoteOr(TabWalk, summary));
        }

        private string WalkDestinationName(int destination)
        {
            if (destination == WalkDestPlaza) return L("KD_TreatPouch.WordWalkPlaza");
            if (destination == WalkDestGate) return L("KD_TreatPouch.WordWalkGate");
            for (int i = 0; walkDestinationIds != null && walkDestinationKeys != null && i < walkDestinationIds.Length && i < walkDestinationKeys.Length; i++)
                if (walkDestinationIds[i] == destination) return L(walkDestinationKeys[i]);
            return L("KD_TreatPouch.WordWalkGateUnavailable");
        }

        private void WalkTint(UnityEngine.UI.Image[] images, int i, Color color)
        {
            if (images == null || i < 0 || i >= images.Length || images[i] == null) return;
            images[i].color = color;
        }
    }
}
