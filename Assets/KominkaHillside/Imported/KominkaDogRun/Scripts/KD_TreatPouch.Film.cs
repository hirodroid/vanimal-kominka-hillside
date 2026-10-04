using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の裏山差分: 撮影の許可の被写体の側 (IMP-WLD-008 W4。動画カメラの設計書 §13.8・§14.1・§14.5)。全端末で出る (Quest・スマホも被写体になれる)。
    ///
    /// - 地図と移動の副画面の上段 ([人] のとき)、合流の切り替えの隣に「撮影の申し込みを受ける: ON/OFF」(自分の受付箱の filmAccepting。保存は受付箱)。
    /// - さんぽタブの右下: 撮影を許した人がいるときは専用のボタン [撮影を許可中 (n)] (保留中でも出す。そのときは [地図と移動] [申し込み 1 件] と 3 つ並び)。押すと「撮影を許した人」の画面
    ///   (Page_Settings/FilmGrantsPage。extraSettingsPages に登録) を開く。行は最大 4 (開始を確かめていない行も出す)、行ごとに [やめてもらう]。
    /// - 「撮影中」は相手の申告がそろった行だけ (相手の画面からの申告で、実際に描いているかは分からない。§13.1)。
    /// 同期変数 0 本 (状態は受付箱)。部品は Tools/KominkaHillside/Video/4 が作って配線する。文言は ja の定数 (13 言語は W5)。</summary>
    public partial class KD_TreatPouch
    {
        [Header("撮影の許可 (IMP-WLD-008 W4。Video/4 が配線する)")]
        public GameObject filmAcceptButton;                  // TravelMapPage の上段 ([人] のときだけ)
        public TextMeshProUGUI filmAcceptLabel;
        public GameObject filmGrantsPage;                    // Page_Settings/FilmGrantsPage
        public TextMeshProUGUI filmGrantsNote, filmGrantsEmpty, filmGrantsBackLabel;
        public TextMeshProUGUI[] filmGrantLabels;            // 4 行 (表示名 + 状態)
        public GameObject[] filmStopButtons;                 // 4 行の [やめてもらう]
        public TextMeshProUGUI[] filmStopLabels;
        [Tooltip("さんぽタブ右列の下の [撮影を許可中 (n)] (Page_Walk/FilmGrants)。保留中でも出す (Codex review 2)")]
        public GameObject filmGrantsButton;
        public TextMeshProUGUI filmGrantsButtonLabel;

        // さんぽタブ右列の下 (x 10〜550、DES-UI-006): 1 つなら [地図と移動] 266 + 右 266 (x 417)、2 つなら 174 × 3 (間 9)
        public const float FilmPendingX = 417f, FilmThirdWidth = 174f, FilmThirdX0 = 97f, FilmThirdX1 = 280f, FilmThirdX2 = 463f;

        /// <summary>さんぽタブの [撮影を許可中 (n)]。保留中でも押せる。ローカル専用。</summary>
        public void _FilmGrantsOpen() { FilmGrantsOpen(); }

        void FilmPlace(RectTransform r, float x, float width)
        {
            if (r == null) return;
            if (Mathf.Abs(r.sizeDelta.x - width) > 0.5f) r.sizeDelta = new Vector2(width, r.sizeDelta.y);
            if (Mathf.Abs(r.anchoredPosition.x - x) > 0.5f) r.anchoredPosition = new Vector2(x, r.anchoredPosition.y);
        }

        public const string WordFilmAcceptOn = "撮影の申し込みを受ける: ON";
        public const string WordFilmAcceptOff = "撮影の申し込みを受ける: OFF";
        public const string WordFilmPending = "撮影の依頼 1 件";
        public const string WordFilmGrantsButton = "撮影を許可中 ({0})";
        public const string WordFilmGrantsTitle = "撮影を許した人";
        public const string WordFilmGrantsNote = "「撮影中」は相手の画面からの申告です。[やめてもらう] でいつでも止められます";
        public const string WordFilmRowActive = "撮影中";
        public const string WordFilmRowWaiting = "まだ撮影は始まっていません";
        public const string WordFilmStop = "やめてもらう";
        public const string WordFilmNone = "撮影を許した人はいません";

        /// <summary>副画面の「撮影の申し込みを受ける」。ローカル専用。</summary>
        public void _FilmToggleAccept()
        {
            KominkaHillside.KH_TravelMailbox box = TravelMyBox();
            if (box == null) { HillsideNote(TabWalk, "course.travel.join.notReady", ""); TravelMapRedraw(); return; }
            box._ToggleFilmAccepting();
            TravelMapRedraw();
        }

        /// <summary>「撮影を許した人」の [もどる]。さんぽの一覧へ。ローカル専用。</summary>
        public void _FilmGrantsBack() { SetTab(TabWalk); }

        public void _FilmStop0() { FilmStop(0); }
        public void _FilmStop1() { FilmStop(1); }
        public void _FilmStop2() { FilmStop(2); }
        public void _FilmStop3() { FilmStop(3); }

        void FilmStop(int k)
        {
            KominkaHillside.KH_TravelMailbox box = TravelMyBox();
            if (box == null) return;
            box._FilmRevokeRow(k);
            FilmGrantsRedraw();
        }

        /// <summary>撮影を許した人がいるか (さんぽタブのボタンを [撮影を許可中] にするか)。画面の部品が無ければ出さない。</summary>
        bool FilmGrantsShown(KominkaHillside.KH_TravelMailbox box)
        {
            return filmGrantsPage != null && filmGrantsButton != null && box != null && box._FilmRowCount() > 0;
        }

        void FilmGrantsOpen()
        {
            if (filmGrantsPage == null) return;
            _OpenExtraSettings(filmGrantsPage, WordFilmGrantsTitle);   // 題は L(key) が無いキーをそのまま返すので ja の文がそのまま出る (W5 で翻訳表へ)
            FilmGrantsRedraw();
        }

        bool FilmGrantsOpenNow() { return filmGrantsPage != null && _settingsOpen && _extraSettingsPage == filmGrantsPage; }

        /// <summary>地図と移動の副画面の上段 ([人] のときだけ)。</summary>
        void FilmAcceptRedraw(KominkaHillside.KH_TravelMailbox box, bool people)
        {
            if (filmAcceptButton == null) return;
            if (filmAcceptButton.activeSelf != people) filmAcceptButton.SetActive(people);
            if (people && filmAcceptLabel != null) filmAcceptLabel.text = box != null && !box.filmAccepting ? WordFilmAcceptOff : WordFilmAcceptOn;
        }

        /// <summary>「撮影を許した人」の 4 行 (開いている間 10 Hz)。</summary>
        void FilmGrantsRedraw()
        {
            if (!FilmGrantsOpenNow()) return;
            KominkaHillside.KH_TravelMailbox box = TravelMyBox();
            if (filmGrantsNote != null) filmGrantsNote.text = WordFilmGrantsNote;
            if (filmGrantsBackLabel != null) filmGrantsBackLabel.text = L("course.travel.map.back");
            int shown = 0;
            for (int k = 0; filmGrantLabels != null && k < filmGrantLabels.Length; k++)
            {
                int id = box != null ? box._FilmRowId(k) : -1;
                VRCPlayerApi p = id >= 0 ? VRCPlayerApi.GetPlayerById(id) : null;
                bool on = id >= 0 && Utilities.IsValid(p);
                if (filmGrantLabels[k] != null && filmGrantLabels[k].gameObject.activeSelf != on) filmGrantLabels[k].gameObject.SetActive(on);
                GameObject stop = filmStopButtons != null && k < filmStopButtons.Length ? filmStopButtons[k] : null;
                if (stop != null && stop.activeSelf != on) stop.SetActive(on);
                if (!on) continue;
                shown++;
                string text = p.displayName + "\n" + (box._FilmRowActive(k) ? WordFilmRowActive : WordFilmRowWaiting);
                if (filmGrantLabels[k] != null && filmGrantLabels[k].text != text) filmGrantLabels[k].text = text;
                if (filmStopLabels != null && k < filmStopLabels.Length && filmStopLabels[k] != null) filmStopLabels[k].text = WordFilmStop;
            }
            if (filmGrantsEmpty != null)
            {
                bool empty = shown == 0;
                if (filmGrantsEmpty.gameObject.activeSelf != empty) filmGrantsEmpty.gameObject.SetActive(empty);
                if (empty) filmGrantsEmpty.text = WordFilmNone;
            }
        }
    }
}
