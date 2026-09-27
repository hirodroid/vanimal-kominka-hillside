using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の裏山差分: さんぽタブ上段の地点ワープ (IMP-WLD-001 段階 1 の**仮の UI**。段階 2 で地図の面へ移す)。
    ///
    /// ボタンは Course/80 (KH_CourseTravelEditor) が Page_Walk の上段 (Y164) に 2 つ作る: [奥の広場へワープ] [川辺へワープ]。
    /// 門番・要求 ID・再送・取りやめ・TeleportTo は KH_LeashManager.Travel.cs (`_TravelBegin`) が持つ。板は押した結果と
    /// 待ちの後の結果 (travelResultSerial の変化) を下端の 1 行へ出し、残りの秒数をボタンに出すだけ (同期変数 0 本)。
    /// 文言はコースの翻訳表 (CourseUITranslations.json の course.travel.*)。</summary>
    public partial class KD_TreatPouch
    {
        [Header("さんぽ: 地点ワープ (IMP-WLD-001 段階 1。0 = 奥の広場、1 = 川辺)")]
        public UnityEngine.UI.Button[] travelButtons;
        public UnityEngine.UI.Image[] travelImages;
        public TextMeshProUGUI[] travelLabels;

        // KH_LeashManager.SpotUpper / SpotRiver と、その呼び名 (行き先の行と同じキー)
        private int[] _travelSpots = { 2, 4 };
        private string[] _travelNameKeys = { "course.walk.upper", "course.walk.river" };
        private int _travelSeenSerial;
        private float _travelNextLabels;

        public void _TravelUpper() { TravelPress(0); }
        public void _TravelRiver() { TravelPress(1); }

        private void TravelPress(int k)
        {
            if (manager == null) { HillsideNote(TabWalk, "course.travel.unavailable", ""); Redraw(); return; }
            int spot = _travelSpots[k];
            TravelNote(manager._TravelBegin(spot), spot);
            Redraw();
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
            { key = "course.travel.preparing"; arg = Mathf.CeilToInt(manager._TravelWaitLeft()).ToString(); }
            else if (code == KominkaHillside.KH_LeashManager.TravelBusy) key = "course.travel.busy";
            else if (code == KominkaHillside.KH_LeashManager.TravelCooling)
            { key = "course.travel.cooling"; arg = Mathf.CeilToInt(manager._TravelCooldownLeft()).ToString(); }
            else if (code == KominkaHillside.KH_LeashManager.TravelBall) key = "course.travel.ball";
            else if (code == KominkaHillside.KH_LeashManager.TravelCrowded) key = "course.travel.crowded";
            else if (code == KominkaHillside.KH_LeashManager.TravelAlreadyHere) key = "course.travel.here";
            else if (code == KominkaHillside.KH_LeashManager.TravelArrived) { key = "course.travel.arrived"; arg = TravelSpotName(spot); }
            // 取りやめは「結果: 理由」で出す (レビュー X7)。旧キー course.travel.changed / noanswer は表に残すが使わない
            else if (code == KominkaHillside.KH_LeashManager.TravelChanged) key = "course.travel.stop.changed";
            else if (code == KominkaHillside.KH_LeashManager.TravelNoAnswer) key = "course.travel.stop.noanswer";
            else if (code == KominkaHillside.KH_LeashManager.TravelBlocked) key = "course.travel.stop.blocked";
            // 本人が着いた後、犬が来るまで (全頭 Phase 2 か 15 秒) は「犬を待っています」(レビュー X6)
            else if (code == KominkaHillside.KH_LeashManager.TravelWaitingDogs) { key = "course.travel.dogs"; arg = TravelSpotName(spot); }
            else if (code == KominkaHillside.KH_LeashManager.TravelStayed) key = "course.travel.stayed";
            else if (code == KominkaHillside.KH_LeashManager.TravelCanceledByMe) key = "course.travel.canceled";
            HillsideNote(TabWalk, key, arg);
        }

        /// <summary>毎フレーム (PollSharedRequests から)。待ちの後の結果を出し、開いていればボタンの秒数を 0.25 秒ごとに書く。</summary>
        private void PollTravel()
        {
            if (manager == null) return;
            if (manager.travelResultSerial != _travelSeenSerial)
            {
                _travelSeenSerial = manager.travelResultSerial;
                TravelNote(manager.travelResultCode, manager.travelResultSpot);
                Redraw();
            }
            if (Time.time < _travelNextLabels) return;
            _travelNextLabels = Time.time + 0.25f;
            // 確認待ち (最大 8 秒) の進み具合 (レビュー X5)。板を閉じていても通知の枠は更新しておく
            if (manager._TravelWaiting())
            {
                HillsideNote(TabWalk, "course.travel.preparing", Mathf.CeilToInt(manager._TravelWaitLeft()).ToString());
                if (panelRoot != null && panelRoot.activeSelf && _tab == TabWalk && !_settingsOpen) { Redraw(); return; }
            }
            if (panelRoot != null && panelRoot.activeSelf && _tab == TabWalk && !_settingsOpen) RedrawTravel();
        }

        /// <summary>上段の 2 つ。ワープできない地点 (spotWarpEnabled が false・未解決) のボタンは隠す。
        /// 待っている地点は点灯、待ち・クールダウン中は暗くするが押せる (押すと理由が下端に出る)。</summary>
        private void RedrawTravel()
        {
            if (travelButtons == null || manager == null) return;
            int waiting = manager._TravelWaitingSpot();
            float left = manager._TravelCooldownLeft();
            for (int k = 0; k < travelButtons.Length && k < _travelSpots.Length; k++)
            {
                UnityEngine.UI.Button button = travelButtons[k];
                if (button == null) continue;
                bool on = manager._TravelWarpable(_travelSpots[k]);
                if (button.gameObject.activeSelf != on) button.gameObject.SetActive(on);
                if (!on) continue;
                string text = L("course.travel.button").Replace("{0}", L(_travelNameKeys[k]));
                if (waiting < 0 && left > 0f) text += " (" + Mathf.CeilToInt(left).ToString() + ")";
                if (travelLabels != null && k < travelLabels.Length && travelLabels[k] != null && travelLabels[k].text != text) travelLabels[k].text = text;
                Color tint = waiting == _travelSpots[k] ? openOnColor : (waiting >= 0 || left > 0f ? recallOffColor : hillsideNeutralColor);
                WalkTint(travelImages, k, tint);
            }
        }
    }
}
