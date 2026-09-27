using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の裏山差分: さんぽタブの「地図と移動」(IMP-WLD-001 段階 2。設計書 §A.1〜A.3・§D.1・§D.7)。
    ///
    /// さんぽタブ上段の [地図と移動] (TravelMapOpen) が副画面 (Page_Settings/TravelMapPage。ポイントの面と同じ extraSettingsPages の開き方) を開く。
    /// 副画面: 左に地図 (周回路・川・橋・6 地点の目印を焼いたテクスチャ + 自分の位置と向き 10 Hz + 選んだ地点の輪)、
    /// 右に 1 列 × 2 行の一覧 (ワープできる 2 地点)、その下に固定フッター (着いた後の説明 + [ここへワープ])、下端に状態の 1 行。
    /// 選ぶ → [ここへワープ] の 2 段。門番・要求 ID・再送・暗転・TeleportTo は KH_LeashManager.Travel.cs が持ち、
    /// ここは結果 (travelResultSerial の変化) を出すだけ (同期変数 0 本)。着いたら PlaceInFront をもう一度呼び、[さんぽへもどる] を出す。
    /// すべて Course/84 (KH_CourseTravelMapEditor) が生成・配線する。文言は course.travel.* / course.travel.map.*。
    /// `_TravelUpper` / `_TravelRiver` (選んで即ワープ) は段階 1 からの入口で、ClientSim ハーネスが使う。</summary>
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
        public RectTransform travelMapSelection;         // 選んだ地点の輪
        [Tooltip("地図の正方形が覆う世界 (x, z の中心と一辺 m)。テクスチャと同じ値を Course/84 が書く")]
        public float travelMapCenterX = 300f, travelMapCenterZ = 360f, travelMapSpan = 720f;
        [Tooltip("一覧の 2 行 (0 = 奥の広場、1 = 川辺)")]
        public UnityEngine.UI.Button[] travelButtons;
        public UnityEngine.UI.Image[] travelImages;
        public TextMeshProUGUI[] travelLabels;
        public TextMeshProUGUI travelFooterLabel;
        public UnityEngine.UI.Image travelGoImage;
        public TextMeshProUGUI travelGoLabel;
        public TextMeshProUGUI travelStatusLabel;

        // KH_LeashManager.SpotUpper / SpotRiver と、その呼び名 (行き先の行と同じキー)。地図の番号 (1〜6 = 地点の並び + 1)
        private int[] _travelSpots = { 2, 4 };
        private string[] _travelNameKeys = { "course.walk.upper", "course.walk.river" };
        private int _travelSeenSerial;
        private float _travelNextLabels;
        private int _travelPick = -1;        // 一覧で選んでいる行 (-1 = なし)
        private bool _travelArrived;         // この副画面で着いた ([もどる] → [さんぽへもどる])

        // ============================================================
        // 入口
        // ============================================================

        /// <summary>さんぽタブ上段の [地図と移動]。ローカル専用。</summary>
        public void _TravelOpenMap()
        {
            if (travelMapPage == null) return;
            _travelArrived = false;
            _OpenExtraSettings(travelMapPage, "course.travel.map.title");
            TravelMapRedraw();
        }

        /// <summary>[もどる] / [さんぽへもどる]。さんぽの一覧へ。ローカル専用。</summary>
        public void _TravelMapBack()
        {
            _travelArrived = false;
            SetTab(TabWalk);
        }

        public void _TravelPick0() { TravelPick(0); }
        public void _TravelPick1() { TravelPick(1); }

        /// <summary>[ここへワープ]。選んでいなければ理由を出すだけ。ローカル専用。</summary>
        public void _TravelGo()
        {
            if (_travelPick < 0) { HillsideNote(TabWalk, "course.travel.map.pick", ""); TravelMapRedraw(); return; }
            TravelPress(_travelPick);
        }

        // 段階 1 からの入口 (選んで即ワープ)。ClientSim ハーネス (Course/82・83) が使う
        public void _TravelUpper() { _travelPick = 0; TravelPress(0); }
        public void _TravelRiver() { _travelPick = 1; TravelPress(1); }

        private void TravelPick(int k)
        {
            _travelPick = k;
            TravelMapRedraw();
        }

        private void TravelPress(int k)
        {
            if (manager == null) { HillsideNote(TabWalk, "course.travel.unavailable", ""); Redraw(); return; }
            int spot = _travelSpots[k];
            _travelArrived = false;
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
                // 着いたら板を新しい位置の目の前へ置き直す (§A.3。移動前の地点に板が残らない)。犬を待つ間と着いたときの 2 回
                if (code == KominkaHillside.KH_LeashManager.TravelWaitingDogs || code == KominkaHillside.KH_LeashManager.TravelArrived)
                {
                    _travelArrived = true;
                    VRCPlayerApi lp = Networking.LocalPlayer;
                    if (panelRoot != null && panelRoot.activeSelf && Utilities.IsValid(lp)) PlaceInFront(lp);
                }
                Redraw();
                TravelMapRedraw();
            }
            // 10 Hz: 地図の自分・確認待ちの残り秒数・ボタンの秒数
            if (Time.time < _travelNextLabels) return;
            _travelNextLabels = Time.time + 0.1f;
            if (manager._TravelWaiting())
                HillsideNote(TabWalk, "course.travel.preparing", Mathf.CeilToInt(manager._TravelWaitLeft()).ToString());
            if (panelRoot == null || !panelRoot.activeSelf) return;
            if (TravelMapOpen()) TravelMapRedraw();
            else if (_tab == TabWalk && !_settingsOpen) RedrawTravel();
        }

        private bool TravelMapOpen()
        {
            return travelMapPage != null && _settingsOpen && _extraSettingsPage == travelMapPage;
        }

        /// <summary>さんぽタブ (RedrawWalk から)。上段の [地図と移動]。下端の 1 行は RedrawWalk が通知 (待ちの残り秒数を含む) を出す。</summary>
        private void RedrawTravel()
        {
            if (travelOpenLabel != null) travelOpenLabel.text = L("course.travel.map.open");
        }

        /// <summary>副画面。開いている間は 10 Hz で呼ばれる (自分の位置と向きもここで動かす)。</summary>
        private void TravelMapRedraw()
        {
            if (manager == null || !TravelMapOpen()) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (travelMapHeading != null) travelMapHeading.text = L("course.travel.map.title");
            if (travelBackLabel != null) travelBackLabel.text = L(_travelArrived ? "course.travel.map.backwalk" : "course.travel.map.back");
            int waiting = manager._TravelWaitingSpot();
            float left = manager._TravelCooldownLeft();
            bool busy = waiting >= 0 || manager._TravelAwaitingDogs();
            Vector3 me = Utilities.IsValid(lp) ? lp.GetPosition() : Vector3.zero;
            for (int k = 0; k < _travelSpots.Length; k++)
            {
                int spot = _travelSpots[k];
                bool on = manager._TravelWarpable(spot);
                if (travelButtons != null && k < travelButtons.Length && travelButtons[k] != null && travelButtons[k].gameObject.activeSelf != on)
                    travelButtons[k].gameObject.SetActive(on);
                if (!on) { if (_travelPick == k) _travelPick = -1; continue; }
                Vector3 p = manager.spotPositions[spot];
                p.y = 0f; Vector3 m = me; m.y = 0f;
                int meters = Mathf.RoundToInt(Vector3.Distance(p, m));
                string text = (spot + 1).ToString() + "  " + L(_travelNameKeys[k]) + "    "
                    + (meters < 15 ? L("course.travel.map.hereNow") : L("course.travel.map.distance").Replace("{0}", meters.ToString()));
                if (travelLabels != null && k < travelLabels.Length && travelLabels[k] != null && travelLabels[k].text != text) travelLabels[k].text = text;
                WalkTint(travelImages, k, waiting == spot ? openOnColor : (_travelPick == k ? equipOnColor : hillsideNeutralColor));
            }
            // 選んだ地点の輪
            if (travelMapSelection != null)
            {
                bool show = _travelPick >= 0;
                if (travelMapSelection.gameObject.activeSelf != show) travelMapSelection.gameObject.SetActive(show);
                if (show) travelMapSelection.anchoredPosition = TravelMapPoint(manager.spotPositions[_travelSpots[_travelPick]]);
            }
            // 自分の位置と向き (北 = +Z を上に固定。回さない)
            if (travelMapPlayer != null && Utilities.IsValid(lp))
            {
                travelMapPlayer.anchoredPosition = TravelMapPoint(me);
                travelMapPlayer.localRotation = Quaternion.Euler(0f, 0f, -lp.GetRotation().eulerAngles.y);
            }
            // 固定フッター: 着いた後のこと (犬の数と、変わらない行き先)。§D.7
            if (travelFooterLabel != null)
            {
                string footer;
                int dogs = 0;
                for (int i = 0; i < manager.walkers.Length; i++) if (manager._IsMine(i)) dogs++;
                if (_travelPick < 0) footer = L("course.travel.map.pick");
                else if (dogs == 0) footer = L("course.travel.map.alone");
                else footer = L("course.travel.map.footer2").Replace("{0}", dogs.ToString()).Replace("{1}", WalkDestinationName(manager._MyChoice()));
                travelFooterLabel.text = footer;
            }
            if (travelGoLabel != null)
            {
                string go = L("course.travel.map.go");
                if (!busy && left > 0f) go += " (" + Mathf.CeilToInt(left).ToString() + ")";
                travelGoLabel.text = go;
            }
            if (travelGoImage != null) travelGoImage.color = busy || left > 0f || _travelPick < 0 ? recallOffColor : equipOnColor;
            if (travelStatusLabel != null) travelStatusLabel.text = HillsideNoteOr(TabWalk, "");
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
