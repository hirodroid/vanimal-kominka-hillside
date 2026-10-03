using TMPro;
using UdonSharp;
using UnityEngine;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (ToyTop)。裏山改変: おもちゃタブの上段 (y 164 の帯、高さ 52)。
    ///
    /// 左半分 (x -550..-10) = 投げ方 [まっすぐ][曲げる] (DES-UI-002、2026-10-03 ユーザー決定)。
    /// 投げる人が自分で選ぶローカルの値で、同期しない・保存しない (再入室で「まっすぐ」)。持ち先は KH_ToyRecall
    /// (`_SetThrowCurve` / `ThrowCurve`)。6 枚の KH_Frisbee が投げた瞬間に読み、今の ReqCatch の引数で犬へ届く
    /// (同期変数は増えない)。曲がる向きは右だけ (計画器とハーネスが正の値でしか確かめていないため)。
    ///
    /// 右半分 (x 10..550) は DES-DOG-002 のボールの柄 (同じファイルの後半)。
    /// 部品はビルダー Course/122 (投げ方) と Course/124 (ボールの柄) が作って配線する。どちらも未配線なら何も描かない。</summary>
    public partial class KD_TreatPouch
    {
        [Header("裏山: おもちゃタブの上段 — 投げ方 (DES-UI-002、Course/122)")]
        [Tooltip("「投げ方」のラベル (Page_Toy/ThrowStyleLabel)")]
        public TextMeshProUGUI throwStyleLabel;
        [Tooltip("[まっすぐ] [曲げる] の地 (選んでいる方を点灯)、ラベル、下線 (選んでいる方だけ出す)")]
        public UnityEngine.UI.Image throwStraightImage;
        public UnityEngine.UI.Image throwCurveImage;
        public TextMeshProUGUI throwStraightLabel;
        public TextMeshProUGUI throwCurveLabel;
        public GameObject throwStraightUnderline;
        public GameObject throwCurveUnderline;

        /// <summary>[まっすぐ]。ローカル専用なので `_` 始まり。押すと即切替 (確認しない。自分の投げにしか効かない)。</summary>
        public void _ThrowStraight() { SetThrowCurve(false); }
        /// <summary>[曲げる] (右へ曲げる)。ローカル専用なので `_` 始まり。</summary>
        public void _ThrowCurve() { SetThrowCurve(true); }

        private void SetThrowCurve(bool on)
        {
            if (recall == null) return;
            recall._SetThrowCurve(on);
            Redraw();
        }

        /// <summary>上段を描く (RedrawHillsideToy の先頭から。おもちゃタブが開いているときだけ)。</summary>
        private void RedrawToyTopRow()
        {
            bool curve = recall != null && recall.ThrowCurve;
            if (throwStyleLabel != null) throwStyleLabel.text = L("course.toy.throw.label");
            if (throwStraightLabel != null) throwStraightLabel.text = L("course.toy.throw.straight");
            if (throwCurveLabel != null) throwCurveLabel.text = L("course.toy.throw.curve");
            // 選択は interactable で表さない (押せない意味と混ざる。ApplyTab と同じ作法)。色と下線で表す
            if (throwStraightImage != null) throwStraightImage.color = curve ? hillsideNeutralColor : equipOnColor;
            if (throwCurveImage != null) throwCurveImage.color = curve ? equipOnColor : hillsideNeutralColor;
            if (throwStraightUnderline != null && throwStraightUnderline.activeSelf == curve) throwStraightUnderline.SetActive(!curve);
            if (throwCurveUnderline != null && throwCurveUnderline.activeSelf != curve) throwCurveUnderline.SetActive(curve);
            RedrawBallPatternRow();
        }

        // ============================================================
        // 右半分: ボールの柄 [◀ 名前 ▶] (DES-DOG-002、2026-10-03 ユーザー決定「推奨で」)
        //
        // 柄は 6 匹のボール全部に効く 1 つ (全員共通)。世界の値の正本は KD_ToyBox (同期 string 1 本、受け手で所有を確かめる)。
        // この板が持つのは**自分の見ているカーソル**だけ (ローカル、同期しない)。◀ ▶ はカーソルを動かすだけで世界は変えない。
        // 中央のボタン: 使用中 → 何もしない / 持っている → カタログの _RequestSelect (→ KD_ToyBox が Owner を取って書く) /
        // 持っていない → 共通の確認カード (kind ConfirmToyPattern) → [購入] でカタログの _RequestBuy (払って選ぶ)。
        // pt が動くのは _PatternConfirmBuy の 1 か所だけ (おやつの [買う] と同じ 2 段階)。価格は古民家と同じ (KD_ProductDefinitions)。
        // ============================================================

        /// <summary>確認カードの種別 (KD_TreatPouch.Confirm.cs の Confirm* の続き。他の売り場と重ならない値)。</summary>
        public const int ConfirmToyPattern = 17;

        [Header("裏山: おもちゃタブの上段 — ボールの柄 (DES-DOG-002、Course/124)")]
        [Tooltip("6 匹のボールの柄の正本 (KH_Dogs/KD_ToyBox、Manual)。null なら柄の行は描かない")]
        public KD_ToyBox ballPatterns;
        [Tooltip("「ボールの柄」のラベル (Page_Toy/PatternLabel)")]
        public TextMeshProUGUI patternLabel;
        [Tooltip("中央のボタン (Page_Toy/PatternAct) の地とラベル (名前 + 状態)")]
        public UnityEngine.UI.Image patternActImage;
        public TextMeshProUGUI patternActLabel;

        /// <summary>見ている柄 (KD_ToyBox.productKeys の添字。-1 = まだ決めていない → 世界の柄から始める)。</summary>
        private int _ptCursor = -1;
        /// <summary>確認カードを開いている柄 (-1 = 閉じている)。</summary>
        private int _ptConfirm = -1;

        public void _PatternPrev() { PatternMove(-1); }
        public void _PatternNext() { PatternMove(1); }

        private void PatternMove(int step)
        {
            int n = ballPatterns == null ? 0 : ballPatterns.ProductCount();
            if (n <= 0) return;
            int c = PatternCursor();
            _ptCursor = ((c + step) % n + n) % n;
            Redraw();
        }

        /// <summary>カーソル (範囲内に丸める)。未決定なら世界に出ている柄。</summary>
        private int PatternCursor()
        {
            int n = ballPatterns == null ? 0 : ballPatterns.ProductCount();
            if (n <= 0) return -1;
            if (_ptCursor < 0 || _ptCursor >= n)
            {
                _ptCursor = ballPatterns.IndexOfProduct(PatternWorldKey());
                if (_ptCursor < 0) _ptCursor = 0;
            }
            return _ptCursor;
        }

        /// <summary>世界に出ている柄。まだ誰も選んでいない (KD_ToyBox が未設定) ときはシーンの初期状態 = 表の [0] (基本ボール)。
        /// これが無いと、入室直後は見えている基本ボールが「使用中」でなく「えらぶ」と出て、押すと同じ柄を全員へ書く。</summary>
        private string PatternWorldKey()
        {
            string k = ballPatterns.AppliedProduct();
            return k == KD_ToyBox.Unset ? ballPatterns.ProductAt(0) : k;
        }

        /// <summary>中央のボタン。ローカル専用なので `_` 始まり。</summary>
        public void _PatternAct()
        {
            int c = PatternCursor();
            if (c < 0 || catalog == null) { HillsideNote(TabToy, "KD_TreatPouch.WordToyGateUnavailable", ""); Redraw(); return; }
            string key = ballPatterns.ProductAt(c);
            KD_ShopItem item = ballPatterns.ItemOfProduct(key);
            if (item == null) { HillsideNote(TabToy, "KD_TreatPouch.WordToyGateUnavailable", ""); Redraw(); return; }
            if (PatternWorldKey() == key) { HillsideNote(TabToy, "KD_ShopShelf.WordInUse", ""); Redraw(); return; }
            if (item.Owned())
            {
                bool ok = catalog._RequestSelect(key);   // → KD_ToyBox._OnShopSelectionChanged (カタログの通知先) が Owner を取って書く
                PatternNote(ok ? "course.toy.pattern.changed" : "KD_ShopShelf.WordLoading", ok ? L(item.displayNameKey) : "", ok);
                Redraw();
                return;
            }
            _ptConfirm = c;
            _ShowItemConfirm(ConfirmToyPattern, item, L("course.toy.pattern.body"));
            Redraw();
        }

        /// <summary>確認カードの [購入] (Confirm.cs の _ConfirmYes が kind ConfirmToyPattern のとき呼ぶ)。
        /// 押した時点の状態で確かめ直す (台帳が最後にも弾く)。ローカル専用なので `_` 始まり。</summary>
        public void _PatternConfirmBuy()
        {
            int c = _ptConfirm;
            _ptConfirm = -1;
            _HideConfirm(ConfirmToyPattern);
            string key = ballPatterns == null ? "" : ballPatterns.ProductAt(c);
            KD_ShopItem item = ballPatterns == null ? null : ballPatterns.ItemOfProduct(key);
            if (item == null || catalog == null || item.ConfirmationState(false) != KD_ShopItem.ConfirmBuy)
            { PatternNote("KD_ShopShelf.WordCantBuy", "", false); Redraw(); return; }
            bool bought = catalog._RequestBuy(key);   // 払う → 解放 → 選ぶ (→ KD_ToyBox が書く)
            PatternNote(bought ? "course.toy.pattern.changed" : "KD_ShopShelf.WordCantBuy", bought ? L(item.displayNameKey) : "", bought);
            Redraw();
        }

        /// <summary>確認カードの [やめる] と暗幕。ローカル専用なので `_` 始まり。</summary>
        public void _PatternConfirmNo()
        {
            _ptConfirm = -1;
            _HideConfirm(ConfirmToyPattern);
            HillsideNote(TabToy, "KD_TreatPouch.WordTreatBuyCancelled", "");
            Redraw();
        }

        /// <summary>下端の 1 行 (4 秒) + 成否の音。キャンセルと「使用中」は鳴らさない。</summary>
        private void PatternNote(string key, string arg, bool success)
        {
            HillsideNote(TabToy, key, arg);
            AudioClip clip = success ? uiSuccessClip : uiRejectClip;
            if (uiAudio != null && clip != null) uiAudio.PlayOneShot(clip);
        }

        private void RedrawBallPatternRow()
        {
            // 確認カード: 開いたまま 0.5 秒ごとに状態へ追いつく。別の操作で閉じられていたら (タブ切替・設定・板を閉じる) こちらも捨てる
            if (_ptConfirm >= 0)
            {
                KD_ShopItem open = ballPatterns == null ? null : ballPatterns.ItemOfProduct(ballPatterns.ProductAt(_ptConfirm));
                if (_confirmKind != ConfirmToyPattern || open == null || open.Owned()) { if (_confirmKind == ConfirmToyPattern) _HideConfirm(ConfirmToyPattern); _ptConfirm = -1; }
                else _ShowItemConfirm(ConfirmToyPattern, open, L("course.toy.pattern.body"));
            }
            if (patternLabel != null) patternLabel.text = L("course.toy.pattern.label");
            if (patternActLabel == null) return;
            int c = PatternCursor();
            if (c < 0) { patternActLabel.text = "-"; return; }
            string key = ballPatterns.ProductAt(c);
            KD_ShopItem item = ballPatterns.ItemOfProduct(key);
            string name = item == null ? "-" : L(item.displayNameKey);
            string state; Color tint;
            if (item == null) { state = ""; tint = hillsideNeutralColor; }
            else if (PatternWorldKey() == key) { state = L("KD_ShopShelf.WordInUse"); tint = hillsideNeutralColor; }
            else if (item.Owned()) { state = L("KD_TreatPouch.WordAccChoose"); tint = equipOnColor; }
            else
            {
                int s = item.ConfirmationState(false);
                state = s == KD_ShopItem.ConfirmLoading ? L("KD_ShopShelf.WordLoading") : item.price.ToString() + " pt";
                tint = s == KD_ShopItem.ConfirmBuy ? recallOnColor : hillsideNeutralColor;   // 買えない間も押せる (理由はカードの注記)
            }
            patternActLabel.text = state.Length == 0 ? name : name + "  <color=#D6AD52>" + state + "</color>";
            if (patternActImage != null) patternActImage.color = tint;
        }
    }
}
