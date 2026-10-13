using TMPro;
using UdonSharp;
using UnityEngine;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (Confirm)。IMP-INF-006 P13 (2026-09-16 ユーザー決定)。
    ///
    /// **購入の確認は板の手前に重ねる 1 枚。** 固定フッターの帯は「押した行から遠く、状態行と同じ場所」で
    /// 気づかない人がいた (09-16 実機)。板全体を半透明で暗くし、中央のカードに 商品名 / 中身 / 価格と残り / [やめる] [購入] を出す。
    /// 後ろの行は暗幕 (raycast を受ける Image) が受けるので押せない。暗幕を押すのも [やめる]。
    ///
    /// **状態機械は各売り場のまま。** ここは置き場 (表示) だけを 1 つにする —
    /// パーティクル (`_fxConfirmRow`) / ライブ (`_liveConfirmRow`) / かざり (`_accConfirm`) /
    /// あそぶ (`KD_ActivitiesMenu._confirm`) / 呼ぶの追加動作 (`KD_CallPose._purchase`) /
    /// 裏山のおやつ (`_treatConfirmRow`、KD_TreatPouch.Treat.cs) が
    /// 自分の Redraw で <see cref="_ShowConfirm"/> / <see cref="_HideConfirm"/> を呼び、
    /// [購入] [やめる] は <see cref="_ConfirmYes"/> / <see cref="_ConfirmNo"/> が開いている種別へ振り分ける。
    /// **pt が減る口は各売り場の既存メソッドのまま** (ここでは払わない)。</summary>
    public partial class KD_TreatPouch
    {
        public const int ConfirmNone = 0;
        public const int ConfirmFx = 1;
        public const int ConfirmLive = 2;
        public const int ConfirmAcc = 3;
        public const int ConfirmActivities = 4;
        public const int ConfirmCall = 5;

        [Header("購入の確認 (P13: 板の手前に重ねる 1 枚)")]
        [Tooltip("暗幕 + カード。既定は非アクティブ。開くときは最後の子へ回して最前面に出す")]
        public GameObject confirmRoot;
        public TextMeshProUGUI confirmTitleLabel;
        public TextMeshProUGUI confirmBodyLabel;
        public TextMeshProUGUI confirmNoteLabel;
        [Tooltip("[購入] / [購入画面へ]。買えない間は隠す (押しても断られるボタンを出さない)")]
        public GameObject confirmYes;
        public TextMeshProUGUI confirmYesLabel;
        public GameObject confirmNo;
        public TextMeshProUGUI confirmNoLabel;
        [Tooltip("あそぶ (KD_ActivitiesMenu)。[購入] を振り分ける先。無ければあそぶの確認は開かない")]
        public KD_ActivitiesMenu activitiesMenu;

        private int _confirmKind;
        /// <summary>開いているカードがギフトの確認か (IMP-PRG-012)。**表示の種別の記録で、検査 (ハーネス) が読む。**
        /// 開閉と実行の正本は各売り場の状態 (`_liveGiftRow` / `_accGiftRow`) — 購入の確認は所有になった瞬間に閉じるが、
        /// ギフトの確認は所有が前提なので閉じない、という判定も売り場側が持つ。</summary>
        private bool _confirmGift;
        /// <summary>ハーネスが heap を読むためのフィールド名。</summary>
        public const string VarConfirmGift = nameof(_confirmGift);

        // net: callable-ok: 読み取りのみ
        public int ConfirmKind() { return _confirmKind; }

        // net: callable-ok: 読み取りのみ
        public bool ConfirmIsGift() { return _confirmGift; }

        /// <summary>確認を開く / 開いたまま文言を更新する。売り場ごとの Redraw から毎回呼んでよい
        /// (0.5 秒ごとに状態へ追いつく — 棚の RefreshOpenConfirm と同じ)。ローカル専用なので `_` 始まり。</summary>
        public void _ShowConfirm(int kind, string title, string body, string note, bool yesEnabled, string yesLabel)
        {
            if (confirmRoot == null) return;
            _confirmKind = kind;
            if (confirmTitleLabel != null) confirmTitleLabel.text = title;
            if (confirmBodyLabel != null) confirmBodyLabel.text = body;
            if (confirmNoteLabel != null) confirmNoteLabel.text = note;
            if (confirmYes != null && confirmYes.activeSelf != yesEnabled) confirmYes.SetActive(yesEnabled);
            if (confirmYesLabel != null) confirmYesLabel.text = yesLabel;
            if (confirmNoLabel != null) confirmNoLabel.text = L("call.cancelPurchase");   // 「やめる」(文言表のキー。KD_ShopShelf.WordCancel は文言表に無い)
            if (!confirmRoot.activeSelf)
            {
                // 最前面へ。後から足された面 (設定 / ポイント) より上に描く
                confirmRoot.transform.SetAsLastSibling();
                confirmRoot.SetActive(true);
            }
        }

        /// <summary>その種別の確認が開いていれば閉じる (別の種別が開いていれば触らない)。ローカル専用なので `_` 始まり。</summary>
        public void _HideConfirm(int kind)
        {
            if (_confirmKind != kind) return;
            HideConfirmAll();
        }

        private void HideConfirmAll()
        {
            _confirmKind = ConfirmNone;
            _confirmGift = false;
            if (confirmRoot != null && confirmRoot.activeSelf) confirmRoot.SetActive(false);
        }

        /// <summary>カードの [購入] / [購入画面へ]。開いている種別の既存メソッドへ振り分けるだけ。ローカル専用なので `_` 始まり。</summary>
        public void _ConfirmYes()
        {
            int kind = _confirmKind;
            if (kind == ConfirmFx) { _FxBuy(); return; }
            if (kind == ConfirmLive) { _LiveBuy(); return; }
            if (kind == ConfirmAcc) { _AccConfirmBuy(); return; }
            if (kind == ConfirmActivities) { if (activitiesMenu != null) activitiesMenu._Action(); return; }
            if (kind == ConfirmCall) { if (callPose != null) callPose._BuyAction(); return; }
            // 裏山改変 (IMP-DOG-014): おやつの [買う] (KD_TreatPouch.Treat.cs)
            if (kind == ConfirmTreat) { _TreatConfirmBuy(); return; }
            // 裏山改変 (DES-DOG-002): ボールの柄の [購入] (KD_TreatPouch.ToyTop.cs)
            if (kind == ConfirmToyPattern) { _PatternConfirmBuy(); return; }
            // 裏山改変 (IMP-DOG-050): 首輪とリードのセットの [購入] (KD_TreatPouch.Collar.cs)
            if (kind == ConfirmCollar) { _CollarConfirmBuy(); return; }
            HideConfirmAll();
        }

        /// <summary>カードの [やめる] と暗幕。ローカル専用なので `_` 始まり。</summary>
        public void _ConfirmNo()
        {
            int kind = _confirmKind;
            if (kind == ConfirmFx) { _FxCancel(); return; }
            if (kind == ConfirmLive) { _LiveCancel(); return; }
            if (kind == ConfirmAcc) { _AccConfirmNo(); return; }
            if (kind == ConfirmActivities) { if (activitiesMenu != null) activitiesMenu._CancelConfirm(); return; }
            if (kind == ConfirmCall) { if (callPose != null) callPose._CancelPurchase(); return; }
            // 裏山改変 (IMP-DOG-014): おやつの [買う] の [やめる] / 暗幕 (KD_TreatPouch.Treat.cs)
            if (kind == ConfirmTreat) { _TreatConfirmNo(); return; }
            // 裏山改変 (DES-DOG-002): ボールの柄の [やめる] / 暗幕 (KD_TreatPouch.ToyTop.cs)
            if (kind == ConfirmToyPattern) { _PatternConfirmNo(); return; }
            // 裏山改変 (IMP-DOG-050): 首輪とリードのセットの [やめる] / 暗幕 (KD_TreatPouch.Collar.cs)
            if (kind == ConfirmCollar) { _CollarConfirmNo(); return; }
            HideConfirmAll();
        }

        /// <summary>商品の確認カードの 3 行 (題 / 中身 / 残り) と [購入] の可否を、商品の状態から作る。
        /// pt = 「〇〇を N pt で購入しますか？」+ 残り / あと。CE = 「〇〇を買いますか？」+ N Credits + 購入画面の案内。
        /// <paramref name="body"/> は売り場が知っている中身 (同梱・6 犬種共通など)。空なら価格だけ。
        /// あそぶ・呼ぶ (別の Behaviour) からも呼ぶ。ローカル専用なので `_` 始まり。</summary>
        public void _ShowItemConfirm(int kind, KD_ShopItem item, string body)
        {
            if (item == null) { _HideConfirm(kind); return; }
            int state = item.ConfirmationState(false);
            string name = L(item.displayNameKey);
            bool ce = item.IsCE();
            string title = ce ? L("confirm.storeTitle").Replace("{0}", name)
                : L("confirm.buyTitle").Replace("{0}", name).Replace("{1}", item.price.ToString() + " pt");
            string note;
            if (state == KD_ShopItem.ConfirmLoading) note = L("KD_ShopShelf.WordLoading");
            else if (ce) note = (item.ceCredits > 0 ? item.ceCredits.ToString() + " Credits · " : "") + L("confirm.storeNote");
            else if (state == KD_ShopItem.ConfirmShort) note = L("confirm.short").Replace("{0}", item.Shortfall().ToString());
            else if (state == KD_ShopItem.ConfirmBuy && ledger != null) note = L("confirm.remaining").Replace("{0}", (ledger.Points() - item.price).ToString());
            else note = "";
            bool can = state == KD_ShopItem.ConfirmBuy || state == KD_ShopItem.ConfirmSpecial;
            _confirmGift = false;
            _ShowConfirm(kind, title, body, note, can, L(ce ? "confirm.storeButton" : "KD_ShopShelf.WordBuy"));
        }

        /// <summary>所有済みの CE 商品を贈る確認 (IMP-PRG-012)。「〇〇を贈りますか？」+ 中身 + 「購入済み · VRChat の購入画面でギフトを選び…」。
        /// [購入画面へ] は同じ Listing を開くだけ (ギフトの操作は VRChat の画面が持つ。Udon に贈り先を指定する口は無い)。
        /// pt も台帳も動かない。買い切りの CE だけ (Subscription はギフト対応が未確認なので売り場側で出さない)。
        /// 所有していない・購入情報の読み込み前・販売終了なら [購入画面へ] を隠す。ローカル専用なので `_` 始まり。</summary>
        public void _ShowGiftConfirm(int kind, KD_ShopItem item, string body)
        {
            if (item == null || !item.IsCE()) { _HideConfirm(kind); return; }
            string title = L("confirm.giftTitle").Replace("{0}", L(item.displayNameKey));
            bool can = item.Owned() && item.CePurchasesLoaded() && !LiveSaleClosed(item);
            _confirmGift = true;
            _ShowConfirm(kind, title, body, L("confirm.giftNote"), can, L("confirm.storeButton"));
        }
    }
}
