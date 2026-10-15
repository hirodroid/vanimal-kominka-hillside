using UdonSharp;
using UnityEngine;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (裏山改変 IMP-WLD-017: 筆ペンを呼ぶ副画面)。
    /// 設定ホームの群「読む・書く」の [筆ペン] (Tools/KominkaHillside/Pen/1 が置く。任意) が Page_Settings/PenPage (extraSettingsPages に登録。写真・動画の副画面と同じ開き方) を開く。
    /// 面の中身 (6 色・状態) と呼ぶ処理は KH_PenCall が持つ (面の外に置く。閉じても所有権待ち・戻しは続く)。ここは開く・戻るだけ。
    /// penPageBehaviour は U# 一般の型で持つ (KH_PenCall の Program Asset が無い段階でもこのクラスがコンパイルできるように。Video.cs と同じ理由)。
    /// すべてローカル専用 (`_` 始まり。vrc-networking)。同期変数なし。</summary>
    public partial class KD_TreatPouch
    {
        [Header("裏山 IMP-WLD-017: 筆ペンの副画面 (Pen/1 が配線)")]
        [Tooltip("Page_Settings/PenPage。extraSettingsPages に登録する")]
        public GameObject penPage;
        [Tooltip("KH_PenCall (_PageOpened / _RefreshLanguage を送る先)")]
        public UdonSharpBehaviour penPageBehaviour;

        /// <summary>面を開く前のタブ ([もどる] で戻す)。設定ホームから開いたときは設定ホームへ戻す。</summary>
        private int _penReturnTab = -1;
        private bool _penReturnSettings;

        /// <summary>設定ホームの [筆ペン]。ローカル専用。</summary>
        public void _PenOpen()
        {
            if (penPage == null) return;
            if (!(_settingsOpen && _extraSettingsPage == penPage))
            {
                _penReturnTab = _tab;
                _penReturnSettings = _settingsOpen && !_welcome && _extraSettingsPage == null;
            }
            _OpenExtraSettings(penPage, "pen.call.open");
            if (penPageBehaviour != null) penPageBehaviour.SendCustomEvent("_PageOpened");
        }

        /// <summary>SetOpen(true) の末尾から (KD_TreatPouch.cs の 1 行)。筆ペンの面を開いたまま板を閉じ、開き直したときは面の見回り (KH_PenCall._Tick) が止まっているので、
        /// _PageOpened を送って描き直しと見回りを再開する。</summary>
        private void PenOnPanelOpened()
        {
            if (_settingsOpen && !_welcome && penPage != null && _extraSettingsPage == penPage && penPageBehaviour != null)
                penPageBehaviour.SendCustomEvent("_PageOpened");
        }

        /// <summary>_RefreshLanguage (KD_Language の言語切替) から (KD_TreatPouch.cs の 1 行)。面を閉じていても設定ホームの [筆ペン] を今の言語へ。</summary>
        private void PenOnLanguageChanged()
        {
            if (penPageBehaviour != null) penPageBehaviour.SendCustomEvent("_RefreshLanguage");
        }

        /// <summary>筆ペンの面の [もどる]。開く前のタブ (または設定ホーム) へ。ローカル専用。</summary>
        public void _PenBack()
        {
            int tab = _penReturnTab;
            bool toSettings = _penReturnSettings;
            _penReturnTab = -1;
            _penReturnSettings = false;
            if (toSettings) { _OpenSettings(); return; }
            if (tab < 0 || tab >= TabCount) tab = TabWalk;
            SetTab(tab);
        }
    }
}
