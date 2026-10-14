using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (裏山改変 IMP-UI-008: アイテム案内の看板の札から開く)。
    /// 古民家の IMP-PRG-014 (KD_TreatPouch.cs の `_OpenFromBoard*` / `OpenFromBoard`) を裏山のタブ (さんぽ / おやつ / おもちゃ / 呼ぶ / かざり / あそぶ)
    /// と写真の面へ移したもの。看板 (`KH_ItemGuideBoard`、Tools/KominkaHillside/Guide/22) の札の Button が SendCustomEvent で呼ぶ。
    /// **名前は看板の Editor が使うので変えない。**
    ///
    /// 古民家と同じ約束:
    /// - 閉じていれば目の前に開き、開いていれば (庭に置いた板も) 目の前へ呼び戻して、その面にする。`_Tab*` と違い、閉じていても効く
    /// - **「自分で開けた」印は立てない** (MarkSelfOpened を呼ばない)。手元ヒントは呼び方を自分で覚えるまで残す
    /// - 寝床の位置指定の最中は何もしない (手元の呼び出しと同じ)
    /// - 「ようこそ」を出している間に押したら、ようこそを目の前へ呼び戻して先に見せ、[メニューを見る] で札の面へ続ける (_WelcomeMenu が <see cref="BoardContinue"/> を呼ぶ)
    /// - ローカルだけで完結する (`_` 始まり)。同期・保存は増やさない
    /// 古民家との違い: 裏山の「ようこそ」は KD_HintDirector が板が閉じているときだけ庭へ出す (開いている板をその場で替える古民家の後付けの経路は無い)。
    /// 復元前に札を押した人は、まずタブが開く。</summary>
    public partial class KD_TreatPouch
    {
        /// <summary>写真の面 (タブではない) を表す札の行き先。</summary>
        private const int BoardPhoto = 100;
        /// <summary>ようこそ表示中に押された札の行き先 (-1 = なし)。[メニューを見る] で使って消す。</summary>
        private int _boardTab = -1;

        public void _OpenFromBoardWalk() { OpenFromBoard(TabWalk); }
        public void _OpenFromBoardTreat() { OpenFromBoard(TabTreat); }
        public void _OpenFromBoardToy() { OpenFromBoard(TabToy); }
        /// <summary>「呼ぶ」の札。呼ぶタブの添字は歴史的に TabSleep。</summary>
        public void _OpenFromBoardCall() { OpenFromBoard(TabSleep); }
        public void _OpenFromBoardAccessory() { OpenFromBoard(TabAcc); }
        /// <summary>トレーナーの札 (あそぶタブ)。</summary>
        public void _OpenFromBoardPlay() { OpenFromBoard(TabPlay); }
        /// <summary>写真・撮影カメラの札。ヘッダの [写真] と同じ面 (Photo/1 が無ければ何もしない)。</summary>
        public void _OpenFromBoardPhoto() { if (photoPage != null) OpenFromBoard(BoardPhoto); }

        private void OpenFromBoard(int target)
        {
            if (target != BoardPhoto && (target < 0 || target >= TabCount)) return;
            if (sleepPlacement != null && sleepPlacement.Active) return;
            if (panelRoot == null) return;
            _introMenuRevision++;
            if (!panelRoot.activeSelf) SetOpen(true);
            else
            {
                _gardenPlaced = false;
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (Utilities.IsValid(lp)) PlaceInFront(lp);
            }
            if (_welcome) { _boardTab = target; Redraw(); return; }
            _boardTab = -1;
            BoardContinue(target);
        }

        /// <summary>札の行き先を開く。ようこその [メニューを見る] からも呼ぶ (true = 開いた。呼んだ側は ApplyTab / Redraw を省く)。</summary>
        private bool BoardContinue(int target)
        {
            if (target == BoardPhoto) { _PhotoOpen(); return true; }
            if (target < 0 || target >= TabCount) return false;
            // 同じタブでも初期化する (確認カードの解除・あそぶのホーム復帰。古民家の ShowTab と同じ。Codex レビュー 2026-10-14 中 1)
            SetTabCore(target, true);
            return true;
        }

        /// <summary>_WelcomeMenu から: ようこそを離れた直後に、ようこそ中に押された札の行き先があれば開く。</summary>
        private bool BoardContinuePending()
        {
            int target = _boardTab;
            _boardTab = -1;
            return target >= 0 && BoardContinue(target);
        }
    }
}
