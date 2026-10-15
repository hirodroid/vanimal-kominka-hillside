using UdonSharp;
using UnityEngine;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (裏山改変 IMP-UI-011: 動画プレイヤーの副画面)。
    /// ヘッダの [動画] (Tools/KominkaHillside/Player/1 が置く。任意) が Page_Settings/VideoPage (extraSettingsPages に登録。写真の副画面と同じ開き方) を開く。
    /// 面の中身 (画面・URL 欄・操作・状態) は KH_VideoPage が描く。ここは開く・戻るだけ。
    /// videoPageBehaviour は U# 一般の型で持つ (KH_VideoPage の Program Asset が無い段階でもこのクラスがコンパイルできるように。Photo.cs と同じ理由)。
    /// すべてローカル専用 (`_` 始まり。vrc-networking)。同期変数なし。</summary>
    public partial class KD_TreatPouch
    {
        [Header("裏山 IMP-UI-011: 動画プレイヤーの副画面 (Player/1 が配線)")]
        [Tooltip("Page_Settings/VideoPage。extraSettingsPages に登録する")]
        public GameObject videoPage;
        [Tooltip("KH_VideoPage (_PageOpened を送る先)")]
        public UdonSharpBehaviour videoPageBehaviour;

        /// <summary>面を開く前のタブ ([もどる] で戻す)。設定ホームから開いたときは設定ホームへ戻す。</summary>
        private int _videoReturnTab = -1;
        private bool _videoReturnSettings;

        /// <summary>ヘッダの [動画]。どのタブからでも 1 回で動画プレイヤーの面へ。ローカル専用。</summary>
        public void _VideoOpen()
        {
            if (videoPage == null) return;
            if (!(_settingsOpen && _extraSettingsPage == videoPage))
            {
                _videoReturnTab = _tab;
                _videoReturnSettings = _settingsOpen && !_welcome && _extraSettingsPage == null;
            }
            _OpenExtraSettings(videoPage, "video.player.title");
            if (videoPageBehaviour != null) videoPageBehaviour.SendCustomEvent("_PageOpened");
        }

        /// <summary>SetOpen(true) の末尾から (KD_TreatPouch.cs の 1 行)。動画の面を開いたまま板を閉じ、開き直したときは面の見回り (KH_VideoPage._Tick) が止まっているので、
        /// _PageOpened を送って描き直しと見回りを再開する。</summary>
        private void VideoOnPanelOpened()
        {
            if (_settingsOpen && !_welcome && videoPage != null && _extraSettingsPage == videoPage && videoPageBehaviour != null)
                videoPageBehaviour.SendCustomEvent("_PageOpened");
        }

        /// <summary>_RefreshLanguage (KD_Language の言語切替) から (KD_TreatPouch.cs の 1 行)。面を閉じていてもヘッダの [動画] を今の言語へ。</summary>
        private void VideoOnLanguageChanged()
        {
            if (videoPageBehaviour != null) videoPageBehaviour.SendCustomEvent("_RefreshLanguage");
        }

        /// <summary>動画プレイヤーの面の [もどる]。開く前のタブ (または設定ホーム) へ。ローカル専用。</summary>
        public void _VideoBack()
        {
            int tab = _videoReturnTab;
            bool toSettings = _videoReturnSettings;
            _videoReturnTab = -1;
            _videoReturnSettings = false;
            if (toSettings) { _OpenSettings(); return; }
            if (tab < 0 || tab >= TabCount) tab = TabWalk;
            SetTab(tab);
        }
    }
}
