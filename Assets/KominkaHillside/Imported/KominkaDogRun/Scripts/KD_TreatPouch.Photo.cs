using UdonSharp;
using UnityEngine;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (裏山改変 IMP-WLD-005: 写真の副画面)。
    /// ヘッダの [写真] (Tools/KominkaHillside/Photo/4 が置く。任意) が Page_Settings/PhotoPage (extraSettingsPages に登録。地図の副画面と同じ開き方) を開く。
    /// 面の中身 (縮小表示・大きく・状態の文・説明) は KH_PhotoAlbum が描く。ここは開く・戻るだけ。
    /// photoAlbum は U# 一般の型で持つ (KH_PhotoAlbum の Program Asset が無い段階でもこのクラスがコンパイルできるように)。
    /// すべてローカル専用 (`_` 始まり。vrc-networking)。同期変数なし。</summary>
    public partial class KD_TreatPouch
    {
        [Header("裏山 IMP-WLD-005: 写真の副画面 (Photo/1 が配線)")]
        [Tooltip("Page_Settings/PhotoPage。extraSettingsPages に登録する")]
        public GameObject photoPage;
        [Tooltip("KH_PhotoAlbum (_RedrawMenu を送る先)")]
        public UdonSharpBehaviour photoAlbum;

        /// <summary>面を開く前のタブ ([もどる] で戻す)。設定ホームから開いたときは設定ホームへ戻す。</summary>
        private int _photoReturnTab = -1;
        private bool _photoReturnSettings;

        /// <summary>ヘッダの [写真]。どのタブからでも 1 回で写真の面へ。ローカル専用。</summary>
        public void _PhotoOpen()
        {
            if (photoPage == null) return;
            if (!(_settingsOpen && _extraSettingsPage == photoPage))
            {
                _photoReturnTab = _tab;
                _photoReturnSettings = _settingsOpen && !_welcome && _extraSettingsPage == null;
            }
            _OpenExtraSettings(photoPage, "photo.menu");
            if (photoAlbum != null) photoAlbum.SendCustomEvent("_RedrawMenu");
        }

        /// <summary>写真の面の [もどる]。開く前のタブ (または設定ホーム) へ。ローカル専用。</summary>
        public void _PhotoBack()
        {
            int tab = _photoReturnTab;
            bool toSettings = _photoReturnSettings;
            _photoReturnTab = -1;
            _photoReturnSettings = false;
            if (toSettings) { _OpenSettings(); return; }
            if (tab < 0 || tab >= TabCount) tab = TabWalk;
            SetTab(tab);
        }

        /// <summary>撮影 OFF のときの [設定] (設定ホームの [写真: ON / OFF] へ)。ローカル専用。</summary>
        public void _PhotoToSettings()
        {
            _photoReturnTab = -1;
            _photoReturnSettings = false;
            _OpenSettings();
        }
    }
}
