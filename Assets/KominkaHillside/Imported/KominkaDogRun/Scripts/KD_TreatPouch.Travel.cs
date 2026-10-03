using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の裏山差分: さんぽタブの「地図と移動」(IMP-WLD-001 段階 2・3。設計書 §A.1〜A.5・§D.1・§D.7)。
    ///
    /// さんぽタブ上段の [地図と移動] (TravelMapOpen) が副画面 (Page_Settings/TravelMapPage。ポイントの面と同じ extraSettingsPages の開き方) を開く。
    /// 副画面: 左に地図 (周回路・川・橋・6 地点の目印を焼いたテクスチャ + 自分の位置と向き 10 Hz + 選んだ相手・地点の輪)、
    /// 右に [地点] / [人] の切替。
    /// - 地点: 2 列 × 3 段の一覧 (セル k = 地点 k。ワープできる地点のセルだけ出す。IMP-WLD-004)。選ぶ → [ここへワープ]。
    /// - 人 (段階 3): 在室の人を開いた時点の距離順で並べ、**開いている間は並べ替えない** (playerId で持つ。入室は末尾、退出は消す)。
    ///   選ぶ → [合流をたのむ] (返事待ちの間は [取り消す])。相手の受付箱が無ければ「準備中」、受け付けを OFF にしている人は暗くする。
    ///   上段に「合流の申し込みを受ける: ON/OFF」(自分の受付箱の accepting)。
    /// その下に固定フッター (ワープ先 / 犬の数 / 散歩先のまま の 3 行) と実行ボタン、右下に状態の 1 行。
    /// 門番・要求 ID・再送・暗転・TeleportTo は KH_LeashManager.Travel.cs、合流の交渉は自分の KH_TravelMailbox が持ち、
    /// ここは結果を出すだけ (同期変数 0 本)。暗転が明けるまで副画面の入力は受け付けない。本人が移動したら PlaceInFront を 1 回だけ呼ぶ。
    /// さんぽタブ上段の「申し込み 1 件」は、保留中の申し込みがあるときだけ出て、通知板 (KH_TravelNotice) を出し直す。
    /// すべて Course/84・85 が生成・配線する。文言は course.travel.*。
    /// `_TravelUpper` / `_TravelRiver` (選んで即ワープ) は段階 1 からの入口で、ClientSim ハーネスが使う。
    /// IMP-WLD-002 (設計書 §F.2): [人] のときは [ここへワープ] を隠し、フッターに [合流をたのむ] (`_TravelGo`) と [来てもらう] (`_TravelCall`) を横に 2 つ出す。
    /// 取消は送信済みの要求に作用し、送った方のボタンだけが [取り消す] になる (もう一方は押しても何もしない)。</summary>
    public partial class KD_TreatPouch
    {
        [Header("地図と移動 (IMP-WLD-001 段階 2。Course/84 が配線する)")]
        public GameObject travelOpenButton;              // Page_Walk の上段
        public TextMeshProUGUI travelOpenLabel;
        public GameObject travelMapPage;                 // Page_Settings/TravelMapPage (extraSettingsPages に登録)
        public TextMeshProUGUI travelMapHeading;
        public TextMeshProUGUI travelBackLabel;
        public RectTransform travelMapRect;              // 地図の RawImage
        public RectTransform travelMapPlayer;            // 自分 (点 + 向きの棒)
        public RectTransform travelMapSelection;         // 選んだ地点・相手の輪
        public TextMeshProUGUI travelLegendLabel;
        [Tooltip("地図の正方形が覆う世界 (x, z の中心と一辺 m)。テクスチャと同じ値を Course/84 が書く")]
        public float travelMapCenterX = 300f, travelMapCenterZ = 360f, travelMapSpan = 720f;
        [Tooltip("地点の一覧の 6 セル (2 列 × 3 段。セル k = 地点 k = 地図の番号 k+1)。ワープできない地点のセルは隠す (IMP-WLD-004)")]
        public UnityEngine.UI.Button[] travelButtons;
        public UnityEngine.UI.Image[] travelImages;
        public TextMeshProUGUI[] travelLabels;
        public TextMeshProUGUI travelFooterLabel;
        public UnityEngine.UI.Image travelGoImage;
        public TextMeshProUGUI travelGoLabel;
        public TextMeshProUGUI travelStatusLabel;

        [Header("合流 (IMP-WLD-001 段階 3。Course/85 が配線する)")]
        public UnityEngine.UI.Image travelModeSpotsImage, travelModePeopleImage;
        public TextMeshProUGUI travelModeSpotsLabel, travelModePeopleLabel;
        public GameObject travelPeopleRoot;              // 人の一覧 (スクロール)
        public RectTransform travelPeopleContent;        // 一覧の中身 (人数で高さを変える)
        public UnityEngine.UI.Button[] travelPeopleButtons;   // 16 行
        public UnityEngine.UI.Image[] travelPeopleImages;
        public TextMeshProUGUI[] travelPeopleLabels;
        public TextMeshProUGUI travelPeopleEmpty;
        public GameObject travelAcceptButton;
        public TextMeshProUGUI travelAcceptLabel;
        public GameObject travelJoinPendingButton;       // Page_Walk の上段 (保留中だけ出す)
        public TextMeshProUGUI travelJoinPendingLabel;
        public KominkaHillside.KH_TravelNotice travelNotice;
        [Header("呼ぶ (IMP-WLD-002。Course/85 が配線する)")]
        public GameObject travelAskButton, travelCallButton;   // [人] のフッター (245×90 × 2)
        public UnityEngine.UI.Image travelAskImage, travelCallImage;
        public TextMeshProUGUI travelAskLabel, travelCallLabel;

        // 一覧のセル k が持つ地点 (KH_LeashManager.SpotGate..SpotBridgeDown) と、その呼び名。地図の番号 (1〜6 = 地点の並び + 1)。
        // IMP-WLD-004 (2026-10-03): 2 行 (奥の広場・川辺) から 6 セルへ。セル k = 地点 k。ワープできるかは manager.spotWarpEnabled が決める。
        // 呼び名は KH_TravelNotice._spotKeys・案内板 B の凡例と同じキー (新しい翻訳なし)
        private int[] _travelSpots = { 0, 1, 2, 3, 4, 5 };
        private string[] _travelNameKeys = { "KD_TreatPouch.WordWalkGate", "KD_TreatPouch.WordWalkPlaza", "course.walk.upper", "course.travel.join.spot.bridgeUp", "course.walk.river", "course.travel.join.spot.bridgeDown" };
        private int _travelSeenSerial;
        private float _travelNextLabels;
        private int _travelPick = -1;        // 地点の一覧で選んでいる行 (-1 = なし)
        private bool _travelArrived;         // この副画面で着いた ([もどる] → [さんぽへもどる])
        private bool _travelPlaced;          // この移動で PlaceInFront を済ませた (移動 1 回につき 1 回)
        private int _travelMode;             // 0 地点 / 1 人
        private int[] _joinIds = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        private int _joinCount;
        private int _joinPick = -1;          // 選んでいる相手の playerId
        private float _joinNextRefresh;
        private int _joinSeenSerial;
        private KominkaHillside.KH_TravelMailbox _travelBox;
        // 行ごとの相手の受付箱 (W3 レビュー 8: 探すのは開いたとき・入退室・未取得のときだけ。10 Hz では探さない)
        private KominkaHillside.KH_TravelMailbox[] _joinBoxes = new KominkaHillside.KH_TravelMailbox[16];
        private string _joinReasonKey = "";      // 合流の着地点の理由 (W3 レビュー 12: 着いた後もフッターに残す)
        private float _joinReasonUntil;
        const float PeopleViewHeight = 176f, PeopleRowPitch = 60f;

        // ============================================================
        // 入口
        // ============================================================

        /// <summary>さんぽタブ上段の [地図と移動]。ローカル専用。</summary>
        public void _TravelOpenMap()
        {
            if (travelMapPage == null) return;
            _travelArrived = false;
            if (_travelMode == 1) JoinFreeze();
            _OpenExtraSettings(travelMapPage, "course.travel.map.title");
            TravelMapRedraw();
        }

        /// <summary>[もどる] / [さんぽへもどる]。さんぽの一覧へ。ローカル専用。</summary>
        public void _TravelMapBack()
        {
            if (TravelInputLocked()) return;
            _travelArrived = false;
            SetTab(TabWalk);
        }

        public void _TravelModeSpots() { if (TravelInputLocked()) return; _travelMode = 0; TravelMapRedraw(); }
        public void _TravelModePeople() { if (TravelInputLocked()) return; _travelMode = 1; JoinFreeze(); TravelMapRedraw(); }

        public void _TravelPick0() { TravelPick(0); }
        public void _TravelPick1() { TravelPick(1); }
        public void _TravelPick2() { TravelPick(2); }
        public void _TravelPick3() { TravelPick(3); }
        public void _TravelPick4() { TravelPick(4); }
        public void _TravelPick5() { TravelPick(5); }

        public void _JoinPick0() { JoinPick(0); }
        public void _JoinPick1() { JoinPick(1); }
        public void _JoinPick2() { JoinPick(2); }
        public void _JoinPick3() { JoinPick(3); }
        public void _JoinPick4() { JoinPick(4); }
        public void _JoinPick5() { JoinPick(5); }
        public void _JoinPick6() { JoinPick(6); }
        public void _JoinPick7() { JoinPick(7); }
        public void _JoinPick8() { JoinPick(8); }
        public void _JoinPick9() { JoinPick(9); }
        public void _JoinPick10() { JoinPick(10); }
        public void _JoinPick11() { JoinPick(11); }
        public void _JoinPick12() { JoinPick(12); }
        public void _JoinPick13() { JoinPick(13); }
        public void _JoinPick14() { JoinPick(14); }
        public void _JoinPick15() { JoinPick(15); }

        /// <summary>実行ボタン。地点なら [ここへワープ]、人なら [合流をたのむ] / [取り消す]。ローカル専用。</summary>
        public void _TravelGo()
        {
            // 取り消せる間 (返事待ち・承認後の測定待ち・切符待ち・暗転中) は [取り消す]。暗転中でも押せる唯一の入力 (§D.6・W3 レビュー 3)。
            // 破壊監査 RC-06: 判定は TravelCancelable() 1 か所 (表示も同じ)。以前は承認後の測定待ちが漏れ、押すと新しい申し込みになっていた
            if (TravelCancelable())
            {
                KominkaHillside.KH_TravelMailbox mine = TravelMyBox();
                // [人] の [合流をたのむ] は、送ったのが呼ぶなら何もしない (取消は [来てもらう] の側。§F.2)
                if (_travelMode == 1 && mine != null && mine._WaitingFor() >= 0 && mine._OutKind() == KominkaHillside.KH_TravelMailbox.KindCall) return;
                if (mine != null && mine._JoinCancelable()) mine._CancelJoinRequest();
                else manager._TravelCancel();
                TravelMapRedraw();
                return;
            }
            if (TravelInputLocked()) return;
            if (_travelMode == 1) { JoinGo(KominkaHillside.KH_TravelMailbox.KindJoin); return; }
            if (_travelPick < 0) { HillsideNote(TabWalk, "course.travel.map.pick", ""); TravelMapRedraw(); return; }
            TravelPress(_travelPick);
        }

        /// <summary>[人] のフッターの [来てもらう] / 呼ぶの返事待ちの [取り消す]。ローカル専用。</summary>
        public void _TravelCall()
        {
            KominkaHillside.KH_TravelMailbox box = TravelMyBox();
            if (box != null && box._JoinCancelable())
            {
                // 取消は送信済みの呼ぶにだけ作用する (合流の返事待ち・移動中は何もしない)
                if (box._WaitingFor() >= 0 && box._OutKind() == KominkaHillside.KH_TravelMailbox.KindCall) { box._CancelJoinRequest(); TravelMapRedraw(); }
                return;
            }
            if (TravelInputLocked() || _travelMode != 1) return;
            if (manager != null && manager._TravelCancelable()) return;
            JoinGo(KominkaHillside.KH_TravelMailbox.KindCall);
        }

        /// <summary>「合流・呼び出しを受ける」の切替。ローカル専用。</summary>
        public void _TravelToggleAccept()
        {
            KominkaHillside.KH_TravelMailbox box = TravelMyBox();
            if (box == null) { HillsideNote(TabWalk, "course.travel.join.notReady", ""); TravelMapRedraw(); return; }
            box._ToggleAccepting();
            TravelMapRedraw();
        }

        /// <summary>さんぽタブ上段の「申し込み 1 件」。通知板を目の前へ出し直す。ローカル専用。</summary>
        public void _TravelShowNotice()
        {
            if (travelNotice != null) travelNotice._Reshow();
        }

        // 段階 1 からの入口 (選んで即ワープ)。ClientSim ハーネス (Course/82・83) が使う。IMP-WLD-004 でセル = 地点の並びになった
        public void _TravelUpper() { _travelPick = KominkaHillside.KH_LeashManager.SpotUpper; TravelPress(KominkaHillside.KH_LeashManager.SpotUpper); }
        public void _TravelRiver() { _travelPick = KominkaHillside.KH_LeashManager.SpotRiver; TravelPress(KominkaHillside.KH_LeashManager.SpotRiver); }

        /// <summary>地点ワープ・合流の移動を取り消せるか (RC-06)。地点ワープの確認待ち・暗転中、合流の返事待ち・測定待ち・切符待ち・暗転中。
        /// 本人が移動した後 (犬待ち) は含めない。実行ボタンの表示と押したときの処理の両方がこれを使う。</summary>
        private bool TravelCancelable()
        {
            if (manager != null && manager._TravelCancelable()) return true;
            KominkaHillside.KH_TravelMailbox box = TravelMyBox();
            return box != null && box._JoinCancelable();
        }

        /// <summary>暗転が明けるまでは副画面の入力を受け付けない (W2 レビュー D3)。</summary>
        private bool TravelInputLocked()
        {
            if (manager == null) return false;
            if (manager._TravelFading()) return true;
            return manager.travelFade != null && manager.travelFade._Busy();
        }

        private void TravelPick(int k)
        {
            if (TravelInputLocked()) return;
            _travelPick = k;
            TravelMapRedraw();
        }

        private void TravelPress(int k)
        {
            if (manager == null) { HillsideNote(TabWalk, "course.travel.unavailable", ""); Redraw(); return; }
            int spot = _travelSpots[k];
            _travelArrived = false; _travelPlaced = false;
            TravelNote(manager._TravelBegin(spot), spot);
            Redraw();
            TravelMapRedraw();
        }

        private string TravelSpotName(int spot)
        {
            for (int k = 0; k < _travelSpots.Length; k++) if (_travelSpots[k] == spot) return L(_travelNameKeys[k]);
            return "";
        }

        private void TravelNote(int code, int spot)
        {
            string key = "course.travel.unavailable";
            string arg = "";
            if (code == KominkaHillside.KH_LeashManager.TravelStarted)
            {
                if (manager._TravelWaiting()) { key = "course.travel.preparing"; arg = Mathf.CeilToInt(manager._TravelWaitLeft()).ToString(); }
                else key = "course.travel.map.moving";   // 犬がいない: 暗転だけして移動する
            }
            else if (code == KominkaHillside.KH_LeashManager.TravelBusy) key = "course.travel.busy";
            else if (code == KominkaHillside.KH_LeashManager.TravelCooling)
            { key = "course.travel.cooling"; arg = Mathf.CeilToInt(manager._TravelCooldownLeft()).ToString(); }
            else if (code == KominkaHillside.KH_LeashManager.TravelBall) key = "course.travel.ball";
            else if (code == KominkaHillside.KH_LeashManager.TravelCrowded) key = "course.travel.crowded";
            else if (code == KominkaHillside.KH_LeashManager.TravelAlreadyHere) key = "course.travel.here";
            else if (code == KominkaHillside.KH_LeashManager.TravelArrived)
            { if (spot < 0) key = "course.travel.join.arrived"; else { key = "course.travel.arrived"; arg = TravelSpotName(spot); } }
            // 取りやめは「結果: 理由」で出す (レビュー X7)。旧キー course.travel.changed / noanswer は表に残すが使わない
            else if (code == KominkaHillside.KH_LeashManager.TravelChanged) key = "course.travel.stop.changed";
            else if (code == KominkaHillside.KH_LeashManager.TravelNoAnswer) key = "course.travel.stop.noanswer";
            else if (code == KominkaHillside.KH_LeashManager.TravelBlocked) key = "course.travel.stop.blocked";
            // 本人が着いた後、犬が来るまで (全頭 Phase 2 か 15 秒) は「犬を待っています」(レビュー X6)
            else if (code == KominkaHillside.KH_LeashManager.TravelWaitingDogs)
            { if (spot < 0) key = "course.travel.join.dogs"; else { key = "course.travel.dogs"; arg = TravelSpotName(spot); } }
            else if (code == KominkaHillside.KH_LeashManager.TravelStayed) key = "course.travel.stayed";
            else if (code == KominkaHillside.KH_LeashManager.TravelCanceledByMe) key = "course.travel.canceled";
            HillsideNote(TabWalk, key, arg);
        }

        // ============================================================
        // 人 (段階 3)
        // ============================================================

        /// <summary>自分の受付箱 (VRCPlayerObject)。入室直後はまだ無いことがある。</summary>
        private KominkaHillside.KH_TravelMailbox TravelMyBox()
        {
            if (_travelBox != null) return _travelBox;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return null;
            GameObject[] objs = Networking.GetPlayerObjects(lp);
            if (objs == null) return null;
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] == null) continue;
                KominkaHillside.KH_TravelMailbox box = objs[i].GetComponent<KominkaHillside.KH_TravelMailbox>();
                if (box != null) { _travelBox = box; return box; }
            }
            return null;
        }

        /// <summary>開いた時点の距離順で並べる。この後は並べ替えない (入室は末尾、退出は消す)。自分は除く。</summary>
        private void JoinFreeze()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            _joinCount = 0;
            if (!Utilities.IsValid(lp)) return;
            int n = VRCPlayerApi.GetPlayerCount();
            VRCPlayerApi[] all = new VRCPlayerApi[n];
            VRCPlayerApi.GetPlayers(all);
            float[] dist = new float[n];
            Vector3 me = lp.GetPosition();
            for (int i = 0; i < n; i++)
                dist[i] = Utilities.IsValid(all[i]) && !all[i].isLocal ? Vector3.Distance(all[i].GetPosition(), me) : -1f;
            while (_joinCount < _joinIds.Length)
            {
                int best = -1;
                for (int i = 0; i < n; i++) if (dist[i] >= 0f && (best < 0 || dist[i] < dist[best])) best = i;
                if (best < 0) break;
                _joinIds[_joinCount++] = all[best].playerId;
                dist[best] = -1f;
            }
            for (int i = _joinCount; i < _joinIds.Length; i++) _joinIds[i] = -1;
            KominkaHillside.KH_TravelMailbox mine = TravelMyBox();
            for (int i = 0; i < _joinBoxes.Length; i++)
                _joinBoxes[i] = i < _joinCount && mine != null ? mine._FindBox(VRCPlayerApi.GetPlayerById(_joinIds[i])) : null;
            if (_joinPick >= 0 && JoinRowOf(_joinPick) < 0) _joinPick = -1;
            _joinNextRefresh = Time.time + 1f;
        }

        /// <summary>1 秒ごと: 退出した人の行を消し (選んでいたら「相手が退室しました」)、入室した人を末尾に足す。</summary>
        private void JoinRefreshMembers()
        {
            int w = 0;
            for (int i = 0; i < _joinCount; i++)
            {
                if (Utilities.IsValid(VRCPlayerApi.GetPlayerById(_joinIds[i]))) { _joinBoxes[w] = _joinBoxes[i]; _joinIds[w++] = _joinIds[i]; }
                else if (_joinIds[i] == _joinPick) { _joinPick = -1; HillsideNote(TabWalk, "course.travel.join.left", ""); }
            }
            for (int i = w; i < _joinBoxes.Length; i++) _joinBoxes[i] = null;
            _joinCount = w;
            int n = VRCPlayerApi.GetPlayerCount();
            VRCPlayerApi[] all = new VRCPlayerApi[n];
            VRCPlayerApi.GetPlayers(all);
            for (int i = 0; i < n && _joinCount < _joinIds.Length; i++)
                if (Utilities.IsValid(all[i]) && !all[i].isLocal && JoinRowOf(all[i].playerId) < 0) _joinIds[_joinCount++] = all[i].playerId;
            for (int i = _joinCount; i < _joinIds.Length; i++) _joinIds[i] = -1;
            // 未取得 (入室直後の「準備中」) と新しく足した行だけ探す
            KominkaHillside.KH_TravelMailbox mine = TravelMyBox();
            for (int i = 0; i < _joinCount; i++)
                if (_joinBoxes[i] == null && mine != null) _joinBoxes[i] = mine._FindBox(VRCPlayerApi.GetPlayerById(_joinIds[i]));
        }

        private int JoinRowOf(int playerId)
        {
            for (int i = 0; i < _joinCount; i++) if (_joinIds[i] == playerId) return i;
            return -1;
        }

        private void JoinPick(int row)
        {
            if (TravelInputLocked() || row < 0 || row >= _joinCount) return;
            KominkaHillside.KH_TravelMailbox box = TravelMyBox();
            if (box != null && box._JoinCancelable()) return;   // 返事待ち・承認後の移動の間は相手を変えない
            _joinPick = _joinIds[row];
            TravelMapRedraw();
        }

        private void JoinGo(int kind)
        {
            KominkaHillside.KH_TravelMailbox box = TravelMyBox();
            if (box == null) { HillsideNote(TabWalk, "course.travel.join.notReady", ""); TravelMapRedraw(); return; }
            if (box._JoinCancelable()) { box._CancelJoinRequest(); TravelMapRedraw(); return; }
            if (_joinPick < 0) { HillsideNote(TabWalk, "course.travel.join.pick", ""); TravelMapRedraw(); return; }
            _travelArrived = false; _travelPlaced = false;
            JoinNote(box._Request(_joinPick, kind), box);
            _joinSeenSerial = box.joinResultSerial;
            TravelMapRedraw();
        }

        private void JoinNote(int code, KominkaHillside.KH_TravelMailbox box)
        {
            string key = "course.travel.join.busy";
            string arg = "";
            if (code == KominkaHillside.KH_TravelMailbox.JoinWaiting) { key = "course.travel.join.waiting"; arg = Mathf.CeilToInt(box._WaitLeft()).ToString(); }
            else if (code == KominkaHillside.KH_TravelMailbox.JoinNotReady) key = "course.travel.join.notReady";
            else if (code == KominkaHillside.KH_TravelMailbox.JoinLeft) key = "course.travel.join.left";
            else if (code == KominkaHillside.KH_TravelMailbox.JoinCanceled) key = "course.travel.join.canceled";
            else if (code == KominkaHillside.KH_TravelMailbox.JoinNoSafe) key = "course.travel.join.nosafe";
            else if (code == KominkaHillside.KH_TravelMailbox.JoinAgain) key = "course.travel.join.again";
            else if (code == KominkaHillside.KH_TravelMailbox.JoinNotAccepting || code == KominkaHillside.KH_TravelMailbox.CodePaused) key = "course.travel.join.code3";
            else if (code == KominkaHillside.KH_TravelMailbox.CodeNo) key = "course.travel.join.code0";
            else if (code == KominkaHillside.KH_TravelMailbox.CodeBusy) key = "course.travel.join.code2";
            else if (code == KominkaHillside.KH_TravelMailbox.JoinMeasuring) key = "course.travel.map.moving";
            else if (code == KominkaHillside.KH_TravelMailbox.CallComing) { key = "course.travel.call.coming"; arg = JoinName(box.joinResultTarget); }
            else if (code == KominkaHillside.KH_TravelMailbox.CallArrived) { key = "course.travel.call.arrived"; arg = JoinName(box.joinResultTarget); }
            else if (code == KominkaHillside.KH_TravelMailbox.CallNotComing) key = "course.travel.call.notcoming";
            else if (code == KominkaHillside.KH_TravelMailbox.CallUnknown) key = "course.travel.call.unknown";
            else if (code == KominkaHillside.KH_TravelMailbox.CallCanceledBy) { key = "course.travel.call.canceledby"; arg = JoinName(box.joinResultTarget); }
            else if (code == KominkaHillside.KH_TravelMailbox.CrossYield) key = "course.travel.call.crossyield";
            else if (code == KominkaHillside.KH_TravelMailbox.AnswerFirst) key = "course.travel.call.answerfirst";
            else if (code == KominkaHillside.KH_TravelMailbox.JoinStartFailed) { TravelNote(box.joinTravelCode, -1); return; }   // W3 レビュー 4: 始められなかった理由
            else if (code == KominkaHillside.KH_TravelMailbox.JoinStarted)
            {
                int r = box.joinReason;
                key = r == KominkaHillside.KH_LeashManager.JoinReasonBehind ? "course.travel.join.reason.behind"
                    : r == KominkaHillside.KH_LeashManager.JoinReasonBridge ? "course.travel.join.reason.bridge"
                    : r == KominkaHillside.KH_LeashManager.JoinReasonWater ? "course.travel.join.reason.water"
                    : r == KominkaHillside.KH_LeashManager.JoinReasonOffTrail ? "course.travel.join.reason.offtrail"
                    : "course.travel.join.reason.near";
                _joinReasonKey = key; _joinReasonUntil = Time.time + 60f;   // 移動が終わるまで + 着いた後もしばらくフッターに出す
            }
            HillsideNote(TabWalk, key, arg);
        }

        private string JoinName(int playerId)
        {
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(playerId);
            return Utilities.IsValid(p) ? p.displayName : "?";
        }

        // ============================================================
        // 毎フレーム (PollSharedRequests から)
        // ============================================================

        private void PollTravel()
        {
            if (manager == null) return;
            if (manager.travelResultSerial != _travelSeenSerial)
            {
                _travelSeenSerial = manager.travelResultSerial;
                int code = manager.travelResultCode;
                TravelNote(code, manager.travelResultSpot);
                // 本人の移動 1 回につき 1 回だけ、板を新しい位置の目の前へ置き直す (§A.3。W2 レビュー U7)。
                // 犬の完了 (TravelArrived) は表示の更新だけ。犬がいない移動は TravelArrived が本人の移動なのでそこで置く
                bool moved = code == KominkaHillside.KH_LeashManager.TravelWaitingDogs || code == KominkaHillside.KH_LeashManager.TravelArrived;
                if (moved && manager.travelResultSpot < 0 && code == KominkaHillside.KH_LeashManager.TravelArrived) _joinReasonUntil = Time.time + 8f;
                if (moved)
                {
                    _travelArrived = true;
                    VRCPlayerApi lp = Networking.LocalPlayer;
                    if (!_travelPlaced && panelRoot != null && panelRoot.activeSelf && Utilities.IsValid(lp)) PlaceInFront(lp);
                    _travelPlaced = true;
                }
                Redraw();
                TravelMapRedraw();
            }
            KominkaHillside.KH_TravelMailbox box = TravelMyBox();
            if (box != null && box.joinResultSerial != _joinSeenSerial)
            {
                _joinSeenSerial = box.joinResultSerial;
                if (box.joinResultCode == KominkaHillside.KH_TravelMailbox.JoinStarted) _travelPlaced = false;
                JoinNote(box.joinResultCode, box);
                Redraw();
                TravelMapRedraw();
            }
            // 10 Hz: 地図の自分・確認待ちの残り秒数・犬を待っている間・返事待ちの残り秒数
            if (Time.time < _travelNextLabels) return;
            _travelNextLabels = Time.time + 0.1f;
            if (manager._TravelWaiting())
                HillsideNote(TabWalk, "course.travel.preparing", Mathf.CeilToInt(manager._TravelWaitLeft()).ToString());
            else if (manager._TravelAwaitingDogs())
            {
                // 犬待ち (最大 15 秒) の間は表示を保つ (W2 レビュー U8。4 秒の期限は結果の表示だけ)
                int spot = manager.travelResultSpot;
                if (spot < 0) HillsideNote(TabWalk, "course.travel.join.dogs", "");
                else HillsideNote(TabWalk, "course.travel.dogs", TravelSpotName(spot));
            }
            else if (box != null && box._WaitingFor() >= 0)
            {
                if (box._OutPhase() == 1) HillsideNote(TabWalk, "course.travel.call.coming", JoinName(box._WaitingFor()));
                else HillsideNote(TabWalk, "course.travel.join.waiting", Mathf.CeilToInt(box._WaitLeft()).ToString());
            }
            if (panelRoot == null || !panelRoot.activeSelf) return;
            if (TravelMapOpen())
            {
                if (_travelMode == 1 && Time.time >= _joinNextRefresh) { _joinNextRefresh = Time.time + 1f; JoinRefreshMembers(); }
                TravelMapRedraw();
            }
            else if (_tab == TabWalk && !_settingsOpen) RedrawTravel();
        }

        private bool TravelMapOpen()
        {
            return travelMapPage != null && _settingsOpen && _extraSettingsPage == travelMapPage;
        }

        /// <summary>さんぽタブ (RedrawWalk から)。上段の [地図と移動] と「申し込み 1 件」。下端の 1 行は RedrawWalk が通知を出す。</summary>
        private void RedrawTravel()
        {
            if (travelOpenLabel != null) travelOpenLabel.text = L("course.travel.map.open");
            KominkaHillside.KH_TravelMailbox box = TravelMyBox();
            bool pending = box != null && box._IncomingFrom() >= 0;
            if (travelJoinPendingButton != null && travelJoinPendingButton.activeSelf != pending) travelJoinPendingButton.SetActive(pending);
            // DES-UI-006: 2 列の右下。保留中は [地図と移動] を左半分にして、右半分に [申し込み 1 件] を出す (KD_TreatPouch.Walk.cs の定数)
            if (walkOpenRect != null)
            {
                float openWidth = pending ? WalkOpenPendingWidth : WalkOpenWidth;
                if (Mathf.Abs(walkOpenRect.sizeDelta.x - openWidth) > 0.5f)
                {
                    walkOpenRect.sizeDelta = new Vector2(openWidth, walkOpenRect.sizeDelta.y);
                    walkOpenRect.anchoredPosition = new Vector2(pending ? WalkOpenPendingX : WalkOpenX, walkOpenRect.anchoredPosition.y);
                }
            }
            if (pending && travelJoinPendingLabel != null) travelJoinPendingLabel.text = L(box._IncomingKind() == KominkaHillside.KH_TravelMailbox.KindCall ? "course.travel.call.pending" : "course.travel.join.pending");
        }

        /// <summary>副画面。開いている間は 10 Hz で呼ばれる (自分の位置と向きもここで動かす)。</summary>
        private void TravelMapRedraw()
        {
            if (manager == null || !TravelMapOpen()) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            KominkaHillside.KH_TravelMailbox box = TravelMyBox();
            bool people = _travelMode == 1;
            if (travelMapHeading != null)
            {
                if (travelMapHeading.gameObject.activeSelf == people) travelMapHeading.gameObject.SetActive(!people);
                travelMapHeading.text = L("course.travel.map.title");
            }
            if (travelBackLabel != null) travelBackLabel.text = L(_travelArrived ? "course.travel.map.backwalk" : "course.travel.map.back");
            if (travelLegendLabel != null) travelLegendLabel.text = L("course.travel.map.legend");
            if (travelModeSpotsLabel != null) travelModeSpotsLabel.text = L("course.travel.join.spots");
            if (travelModePeopleLabel != null) travelModePeopleLabel.text = L("course.travel.join.people");
            if (travelModeSpotsImage != null) travelModeSpotsImage.color = people ? hillsideNeutralColor : openOnColor;
            if (travelModePeopleImage != null) travelModePeopleImage.color = people ? openOnColor : hillsideNeutralColor;
            if (travelAcceptButton != null && travelAcceptButton.activeSelf != people) travelAcceptButton.SetActive(people);
            if (people && travelAcceptLabel != null)
                travelAcceptLabel.text = L(box != null && !box.accepting ? "course.travel.join.accept.off" : "course.travel.join.accept.on");
            if (travelPeopleRoot != null && travelPeopleRoot.activeSelf != people) travelPeopleRoot.SetActive(people);

            int waiting = manager._TravelWaitingSpot();
            float left = manager._TravelCooldownLeft();
            bool busy = waiting >= 0 || manager._TravelAwaitingDogs();
            Vector3 me = Utilities.IsValid(lp) ? lp.GetPosition() : Vector3.zero;
            // ---- 地点のセル (2 列 × 3 段。IMP-WLD-004)。2 段: 1 行目 = 番号と呼び名、2 行目 = 距離 ----
            for (int k = 0; k < _travelSpots.Length; k++)
            {
                int spot = _travelSpots[k];
                bool on = !people && manager._TravelWarpable(spot);
                if (travelButtons != null && k < travelButtons.Length && travelButtons[k] != null && travelButtons[k].gameObject.activeSelf != on)
                    travelButtons[k].gameObject.SetActive(on);
                if (!manager._TravelWarpable(spot)) { if (_travelPick == k) _travelPick = -1; continue; }
                if (!on) continue;
                string text = (spot + 1).ToString() + "  " + L(_travelNameKeys[k]) + "\n" + TravelDistanceText(manager.spotPositions[spot], me);
                if (travelLabels != null && k < travelLabels.Length && travelLabels[k] != null && travelLabels[k].text != text) travelLabels[k].text = text;
                WalkTint(travelImages, k, waiting == spot ? openOnColor : (_travelPick == k ? equipOnColor : hillsideNeutralColor));
            }
            // ---- 人の 16 行 ----
            int waitingFor = box != null ? box._WaitingFor() : -1;
            if (people)
            {
                if (travelPeopleEmpty != null)
                {
                    bool empty = _joinCount == 0;
                    if (travelPeopleEmpty.gameObject.activeSelf != empty) travelPeopleEmpty.gameObject.SetActive(empty);
                    if (empty) travelPeopleEmpty.text = L("course.travel.join.alone");
                }
                if (travelPeopleContent != null)
                {
                    // 人数に合わせて中身の高さを変え、減ったときは見える範囲へ戻す (W3 レビュー 13)
                    float h = Mathf.Max(PeopleViewHeight, _joinCount * PeopleRowPitch + 4f);
                    if (Mathf.Abs(travelPeopleContent.sizeDelta.y - h) > 0.5f) travelPeopleContent.sizeDelta = new Vector2(travelPeopleContent.sizeDelta.x, h);
                    Vector2 at = travelPeopleContent.anchoredPosition;
                    float maxY = h - PeopleViewHeight;
                    if (at.y > maxY || at.y < 0f) travelPeopleContent.anchoredPosition = new Vector2(at.x, Mathf.Clamp(at.y, 0f, maxY));
                }
                for (int i = 0; travelPeopleButtons != null && i < travelPeopleButtons.Length; i++)
                {
                    bool on = i < _joinCount;
                    if (travelPeopleButtons[i] != null && travelPeopleButtons[i].gameObject.activeSelf != on) travelPeopleButtons[i].gameObject.SetActive(on);
                    if (!on) continue;
                    VRCPlayerApi p = VRCPlayerApi.GetPlayerById(_joinIds[i]);
                    if (!Utilities.IsValid(p)) continue;
                    KominkaHillside.KH_TravelMailbox theirs = _joinBoxes[i];
                    string place = travelNotice != null ? L("course.travel.join.near").Replace("{0}", travelNotice._NearestSpotName(p.GetPosition())) : "";
                    // 2 段: 1 行目 = 表示名 (長ければ … で切る)、2 行目 = 最寄りの地点・距離・状態 (81 の測定で幅が足りなかったため)
                    string text = p.displayName + "\n" + place + "  " + TravelDistanceText(p.GetPosition(), me);
                    // 返事待ちの行は 2 行目を置き換える (§F.2)
                    if (waitingFor == _joinIds[i]) text = p.displayName + "\n" + (box._OutPhase() == 1 ? L("course.travel.call.onway") : L("course.travel.join.waiting").Replace("{0}", Mathf.CeilToInt(box._WaitLeft()).ToString()));
                    else if (theirs == null) text += "  (" + L("course.travel.join.notReady") + ")";
                    else if (!theirs.accepting) text += "  (" + L("course.travel.join.notAccepting") + ")";
                    if (travelPeopleLabels != null && i < travelPeopleLabels.Length && travelPeopleLabels[i] != null && travelPeopleLabels[i].text != text) travelPeopleLabels[i].text = text;
                    Color tint = waitingFor == _joinIds[i] ? openOnColor : (_joinPick == _joinIds[i] ? equipOnColor : (theirs == null || !theirs.accepting ? recallOffColor : hillsideNeutralColor));
                    WalkTint(travelPeopleImages, i, tint);
                }
            }
            // ---- 選んだ地点・相手の輪 ----
            if (travelMapSelection != null)
            {
                bool show = false; Vector3 at = Vector3.zero;
                if (!people && _travelPick >= 0) { show = true; at = manager.spotPositions[_travelSpots[_travelPick]]; }
                else if (people && _joinPick >= 0)
                {
                    VRCPlayerApi p = VRCPlayerApi.GetPlayerById(_joinPick);
                    if (Utilities.IsValid(p)) { show = true; at = p.GetPosition(); }
                }
                if (travelMapSelection.gameObject.activeSelf != show) travelMapSelection.gameObject.SetActive(show);
                if (show) travelMapSelection.anchoredPosition = TravelMapPoint(at);
            }
            // ---- 自分の位置と向き (北 = +Z を上に固定。回さない) ----
            if (travelMapPlayer != null && Utilities.IsValid(lp))
            {
                travelMapPlayer.anchoredPosition = TravelMapPoint(me);
                travelMapPlayer.localRotation = Quaternion.Euler(0f, 0f, -lp.GetRotation().eulerAngles.y);
            }
            // ---- 固定フッター: ワープ先 / 犬の数 / 散歩先のまま (W2 レビュー U10。§D.7) ----
            if (travelFooterLabel != null)
            {
                int dogs = 0;
                for (int i = 0; i < manager.walkers.Length; i++) if (manager._IsMine(i)) dogs++;
                string target = "";
                if (!people && _travelPick >= 0) target = L("course.travel.map.target").Replace("{0}", L(_travelNameKeys[_travelPick]));
                else if (people && _joinPick >= 0)
                {
                    VRCPlayerApi p = VRCPlayerApi.GetPlayerById(_joinPick);
                    if (Utilities.IsValid(p)) target = L("course.travel.join.target").Replace("{0}", p.displayName);
                }
                // 合流の着地点の理由は、移動中と着いた後しばらく 1 行目に出す (状態の 1 行は 4 秒で消えるため)
                if (people && _joinReasonKey.Length > 0 && Time.time < _joinReasonUntil) target = L(_joinReasonKey);
                string footer;
                if (target.Length == 0) footer = L(people ? "course.travel.join.pick" : "course.travel.map.pick");
                else footer = target + "\n" + (dogs == 0 ? L("course.travel.map.alone") : L("course.travel.map.dogs").Replace("{0}", dogs.ToString()))
                    + "\n" + L("course.travel.map.walkdest").Replace("{0}", WalkDestinationName(manager._MyChoice()));
                travelFooterLabel.text = footer;
            }
            // ---- 実行ボタン ----
            bool moving = TravelCancelable();   // RC-06: 押したときの判定 (_TravelGo) と同じ
            bool joinCancel = box != null && box._JoinCancelable();
            if (travelGoLabel != null)
            {
                string go;
                if (moving) go = L("course.travel.join.cancel").Replace("{0}", Mathf.CeilToInt(joinCancel ? box._CancelLeft() : manager._TravelWaitLeft()).ToString());
                else if (people) go = L("course.travel.join.ask");
                else
                {
                    go = L("course.travel.map.go");
                    if (!busy && left > 0f) go += " (" + Mathf.CeilToInt(left).ToString() + ")";
                }
                travelGoLabel.text = go;
            }
            KominkaHillside.KH_TravelMailbox pickedBox = null;
            int pickRow = _joinPick >= 0 ? JoinRowOf(_joinPick) : -1;
            if (pickRow >= 0) pickedBox = _joinBoxes[pickRow];
            bool peopleReady = pickedBox != null && pickedBox.accepting && !busy && left <= 0f;   // 準備中・受付 OFF の相手には暗くする (押すと理由が出る)
            if (travelGoImage != null)
            {
                bool ready = moving || (people ? (waitingFor >= 0 || peopleReady) : (!busy && left <= 0f && _travelPick >= 0));
                travelGoImage.color = ready ? equipOnColor : recallOffColor;
            }
            // [人] のフッター: [合流をたのむ] [来てもらう] (§F.2)。[ここへワープ] は [地点] のときだけ
            if (travelAskButton != null)
            {
                if (travelGoImage != null && travelGoImage.gameObject.activeSelf == people) travelGoImage.gameObject.SetActive(!people);
                if (travelAskButton.activeSelf != people) travelAskButton.SetActive(people);
                if (travelCallButton != null && travelCallButton.activeSelf != people) travelCallButton.SetActive(people);
                if (people)
                {
                    bool callSent = box != null && box._WaitingFor() >= 0 && box._OutKind() == KominkaHillside.KH_TravelMailbox.KindCall;
                    bool askCancel = moving && !callSent;
                    string cancel = L("course.travel.join.cancel").Replace("{0}", Mathf.CeilToInt(joinCancel ? box._CancelLeft() : manager._TravelWaitLeft()).ToString());
                    if (travelAskLabel != null) travelAskLabel.text = askCancel ? cancel : L("course.travel.join.ask");
                    if (travelCallLabel != null) travelCallLabel.text = callSent ? cancel : L("course.travel.call.ask");
                    if (travelAskImage != null) travelAskImage.color = askCancel || (!moving && peopleReady) ? equipOnColor : recallOffColor;
                    if (travelCallImage != null) travelCallImage.color = callSent || (!moving && peopleReady) ? equipOnColor : recallOffColor;
                }
            }
            if (travelStatusLabel != null) travelStatusLabel.text = HillsideNoteOr(TabWalk, "");
        }

        private string TravelDistanceText(Vector3 there, Vector3 me)
        {
            there.y = 0f; me.y = 0f;
            int meters = Mathf.RoundToInt(Vector3.Distance(there, me));
            return meters < 15 ? L("course.travel.map.hereNow") : L("course.travel.map.distance").Replace("{0}", meters.ToString());
        }

        /// <summary>世界の位置 → 地図 (RawImage の中心が原点の Canvas 単位)。</summary>
        private Vector2 TravelMapPoint(Vector3 world)
        {
            float size = travelMapRect != null ? travelMapRect.sizeDelta.x : 380f;
            float span = travelMapSpan > 1f ? travelMapSpan : 720f;
            float u = (world.x - travelMapCenterX) / span, v = (world.z - travelMapCenterZ) / span;
            Vector2 local = new Vector2(Mathf.Clamp(u, -0.5f, 0.5f) * size, Mathf.Clamp(v, -0.5f, 0.5f) * size);
            if (travelMapRect != null) local += travelMapRect.anchoredPosition;
            return local;
        }
    }
}
