using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-PRG-002: 陳列。**触られたときの操作と、棚の面の表示だけを持つ。**
    /// 正本は [shop-foundation.md](../../../../docs/development/design/shop-foundation.md) §7。
    ///
    /// 状態遷移は <see cref="KD_ShopCatalog"/> が持ち、ここはその**入口の 1 つ**でしかない。
    ///
    /// ## 売り場の形はカテゴリで違う。**確認の型だけ揃える** (2026-08-23 ユーザー決定)
    ///
    /// <code>
    /// おもちゃ         実物を棚に並べる     ← 実物そのものが最高のプレビュー
    /// 撫でエフェクト   メニュー + 再生      ← 実物が存在しない
    /// おやつ           メニュー + 残り個数
    /// </code>
    ///
    /// **プレビューできる形が正解**で、実物かメニューかはその手段でしかない
    /// (KanjiFind `DES-GM-004`「中身を見ずに選ぶのは自律性ではなく賭け」)。
    /// 全部メニューにすると、**値段差を「絵の強さ」で正当化している設計**
    /// ([progression-ce.md §4](../../../../docs/development/design/progression-ce.md))
    /// なのにその絵を小さなアイコンへ落とすことになる。
    ///
    /// **2 つのシステムに見せないための条件が「確認の型を揃えること」**:
    /// 触る → その場に「はい / いいえ」が同じ見た目で出る。
    /// **このクラスがその確認 UI を持つ**ので、メニュー側 (Phase 3 / 4b) も
    /// これを呼べば同じ操作感になる。
    ///
    /// ## 誤爆を防ぐのはボタンの側
    ///
    /// 商品はすべて Collider を持つ (`Interact()` に要る) ので、
    /// **VR で棚の前に立って腕を振ると当たる。** 触っただけで買えてしまうと
    /// 「買った覚えがないのに pt が減っている」が起きる — しかも **pt は取り戻せない。**
    ///
    /// → **触るのは「パネルを開く」だけ。減るのは [購入] を押したときだけ。**
    /// パネルは**商品の真上**に出す (手の位置にボタンが来ないように)。
    /// ただし**メニュー行 (`menuOnly`) だけは横**へ出す — 行は縦に並ぶので、
    /// 真上だと上の行を覆って「何を確認しているのか」が読めなくなる。
    /// </summary>
    /// <remarks>
    /// SyncMode None。**棚の表示は完全にローカル** — 人によって違って当然
    /// (残高も、パネルを開いているかも、その人のもの)。
    ///
    /// 表示は 1 秒ごとに描き直す。`Update` のポーリングではなく
    /// `SendCustomEventDelayedSeconds` の自己再帰で、**1 秒に 1 回**しか走らない。
    /// 周期が要るのは残高が時間で増えるからで、ついでに
    /// 「他人が選択を変えた」もこれで拾える。
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_ShopShelf : UdonSharpBehaviour
    {
        [Header("配線")]
        public KD_ShopCatalog catalog;

        [Tooltip("この棚に並べる商品。カタログの items の部分集合でよい " +
                 "(「いつもの」と「スペシャル」で棚を分けられる)")]
        public KD_ShopItem[] items;

        [Tooltip("共有カテゴリの消費側。**「使用中」はここの値と比べる** " +
                 "(カタログのカーソルと比べると他人の変更が反映されない)。" +
                 "個人装備しか置かない棚なら未配線でよい")]
        public KD_ToyBox toyBox;

        [Header("棚の面 (残高と品ぞろえ)")]
        [Tooltip("**TextMesh ではなく TextMeshPro。** `TextMesh.text` の setter は " +
                 "Udon に公開されていない (デバッグ盤が TextMesh を使えるのは、" +
                 "あれが素の MonoBehaviour だから)")]
        public TextMeshPro label;

        [Tooltip("棚の名前 (1 行目)")]
        public string title = "犬のおもちゃ";
        public string titleKey = "ui.extra.023";

        [Header("確認パネル (はい / いいえ)")]
        [Tooltip("Canvas の root。**既定は非アクティブ** — 触ったときだけ出す")]
        public GameObject confirmRoot;
        [Tooltip("品名と値段。「ドーナツ 20pt」")]
        public TextMeshProUGUI confirmTitle;
        public TextMeshProUGUI confirmDetail;
        public TextMeshProUGUI confirmPrice;
        [Tooltip("肯定ボタンの GameObject。**買えない / 使用中のときは隠す** — " +
                 "押せないボタンを出すと「押しても無反応」に見える")]
        public GameObject yesButton;
        [Tooltip("肯定ボタンの文言。「購入」「使う」を出し分ける")]
        public TextMeshProUGUI yesLabel;
        [Tooltip("パネルを出す高さ (商品の真上。手でボタンを隠さないため)。" +
                 "**実物棚の品だけに効く** — メニュー行は下の横ずらしを使う")]
        public float confirmHeight = 0.42f;

        [Tooltip("メニュー行 (menuOnly) のパネルを出す横のずらし (m。棚ローカル)。\n" +
                 "**行は縦に並ぶので、真上に出すと上の行を隠す** — どの品を確認しているのか、" +
                 "確認の対象そのものが読めなくなる。横へ逃がせば行は全部見えたまま。\n" +
                 "**符号は棚の面の向き (パネルの Y 回転) 次第** — この棚は 180 なので +X が" +
                 "見る側の左。Setup (ShelfFaceYaw / MenuConfirmSideShift) が対で書く")]
        public float confirmMenuSideShift = 0.50f;
        [Tooltip("メニュー行のパネルの高さ (行と同じ高さ = 0)。**触った行の真横に出す**ことが" +
                 "「これについて聞かれている」の手がかりになる")]
        public float confirmMenuLift = 0f;

        [Header("確認パネルの資源バー (「あと N pt」の視覚化)")]
        [Tooltip("バーの器 (BarTrough)。pt で買える経路の品だけで出す — " +
                 "上限 (もういっぱい) や CE で出すと「貯めれば買える」という嘘になる")]
        public GameObject confirmBar;
        [Tooltip("充填。幅は anchorMax.x で動かす (fillAmount は角丸 9-slice と両立しない)")]
        public RectTransform confirmBarFill;
        public UnityEngine.UI.Image confirmBarFillImage;
        [Tooltip("部分バー (残高不足)。暗い金 — 光らせない (危険 4)")]
        public Color barColor = new Color(0.55f, 0.42f, 0.10f);
        [Tooltip("満タン (買える)。アクセント金 #D6AD52 — 色替えのみで演出は足さない")]
        public Color barFullColor = new Color(0.839f, 0.678f, 0.322f);

        // **撫でエフェクトの近接メニューは 2026-09-03 に廃止した** (IMP-PET-007)。
        // 切り替えも購入も手元メニューの「なで」タブ (KD_TreatPouch) が持つ。
        // menuRoot / menu / menuShowDistance / menuHideDistance はフィールドごと落とした —
        // **残すと「メニューがある棚」という嘘の状態がシーンに残る。**
        // 副産物として BUG-CAM-002 (写真への写り込み) と ui-map §5 の 7.4m ずれが
        // **板ごと消えた** (近接ゲートの測定点をどちらにするか、という論点自体が無くなった)。

        [Header("音")]
        [Tooltip("**「買えた」が伝わらないと体験が成立しない。** " +
                 "表示が変わるだけだと、棚を見ていない瞬間に確定したとき何も分からない")]
        public AudioSource sfx;
        public AudioClip buyClip;
        public AudioClip selectClip;
        [Tooltip("**断られたことも無言にしない** — 「押しても無反応」と区別が付かなくなる")]
        public AudioClip rejectClip;

        [Header("表示")]
        [Tooltip("棚の面を描き直す間隔 (秒)。残高が時間で増えるので周期が要る")]
        public float refreshSeconds = KD_ShopItem.ConfirmRefreshSeconds;

        // ============================================================
        // 文言の宣言 (vrc-tmp-font §3)
        //
        // **フォントは Static なので、ここに無い字は豆腐になる。**
        // Dynamic フォントは「エディタで表示しただけで焼かれて動いてしまう」ため、
        // 実機で初めて欠落が露見する。それを防ぐには**文言を宣言側に持つ**しかない。
        //
        // Shop の Setup がこの const 群を直接読んで TryAddCharacters するので、
        // **ここを直せばフォントも追随する** (二重管理にならない)。
        // ============================================================

        public const string WordStock = "残り";
        public const string WordCount = "個";
        public const string WordOwned = "購入済み";
        public const string WordInUse = "使用中";
        public const string WordUse = "使う";
        public const string WordBuy = "購入";
        public const string WordCancel = "やめる";
        public const string WordSpecial = "スペシャル";
        public const string WordLocked = "準備中";
        public const string WordRemain = "ポイント";
        public const string WordShort = "あと";
        public const string WordLoading = "読み込み中";
        public const string WordBought = "購入しました";
        public const string WordNoLedger = "台帳が未配線";
        public const string WordNotHere = "この棚の品ではない";
        public const string WordCantSelect = "切り替えできませんでした";
        public const string WordCantBuy = "購入できませんでした";
        /// <summary>セット販売の単位。「1pt/5個入り」。</summary>
        public const string WordPack = "個入り";
        /// <summary>上限に当たっていて買えない。**残高不足と別の文言にする** —
        /// 同じ「買えない」でも、待てば解決するのか使えば解決するのかが逆。</summary>
        public const string WordEnough = "所持数の上限です";

        // 確認パネルが今どの品について開いているか (-1 = 閉じている)
        private string _openId = "";
        // 肯定ボタンを押したときに何をするか
        private const int ActNone = 0;
        private const int ActBuy = 1;
        private const int ActUse = 2;
        /// <summary>CE 品: Storefront の購入画面を開く (pt は動かない。IMP-FET-010)。</summary>
        private const int ActStore = 3;
        private int _openAction = ActNone;

        private string _flash = "";
        private float _flashUntil;

        // デバッグ盤が読む値
        private int _dbgOpenCount;
        private int _dbgBuyCount;
        private int _dbgRejectCount;

        public KD_Language language;
        private string L(string source) { return language != null ? language._Text(source) : source; }

        private void Start()
        {
            CloseConfirm();
            SendCustomEventDelayedSeconds(nameof(_Refresh), Mathf.Max(refreshSeconds, 0.25f));
            Redraw();
        }

        // ============================================================
        // 操作
        // ============================================================

        /// <summary>商品が触られた。**`KD_ShopItem.Interact()` から来る。**
        ///
        /// **ここでは何も買わない・何も選ばない。** パネルを開くだけ。
        /// ローカル専用なので `_` 始まり。</summary>
        public void _OnItemTouched(string productKey)
        {
            if (catalog == null) { Reject("KD_ShopShelf.WordNoLedger"); return; }
            int i = IndexOf(productKey);
            if (i < 0) { Reject("KD_ShopShelf.WordNotHere"); return; }
            var item = items[i];

            _openId = productKey;
            _dbgOpenCount++;
            ApplyConfirmState(item);
            ShowConfirmOver(item);
            Play(selectClip);
            Redraw();
        }

        /// <summary>開いているパネルの文言・ボタン・バーを今の状態から作り直す。
        ///
        /// **触った瞬間 (`_OnItemTouched`) と、開いたままの周期再適用 (`_Refresh`) の両方が
        /// ここを通る** — 開いた時点のスナップショットで陳腐化させない
        /// (開いたまま pt が閾値を跨いだら [購入] が黙って現れるのが正しい)。
        /// 音と位置はここでは触らない (触った瞬間だけのもの)。</summary>
        private void ApplyConfirmState(KD_ShopItem item)
        {
            string detail = "";
            string price = "";
            string yes = null;
            int action = ActNone;
            bool ptPath = false;
            int state = item.ConfirmationState(InUse(item, AppliedNow()));
            if (state == KD_ShopItem.ConfirmLoading) detail = L("KD_ShopShelf.WordLoading") + "...";
            else if (item.IsStocked())
            {
                detail = item.packCount.ToString() + L("KD_ShopShelf.WordPack") + " / "
                    + L("KD_TreatPouch.WordLiveRemain") + " " + item.Stock().ToString() + L("KD_ShopShelf.WordCount");
                // 上限と残高不足が重なったら、使えば解決する上限を先に案内する。
                if (state == KD_ShopItem.ConfirmFull) price = L("KD_ShopShelf.WordEnough");
                else
                {
                    ptPath = true;
                    price = item.price.ToString() + " pt";
                    if (state == KD_ShopItem.ConfirmBuy) { yes = L("KD_ShopShelf.WordBuy"); action = ActBuy; }
                    else price += " / " + L("KD_TreatPouch.WordAccWaitPrefix") + " " + item.Shortfall().ToString() + " pt";
                }
            }
            else if (state == KD_ShopItem.ConfirmInUse) detail = L("KD_ShopShelf.WordInUse");
            else if (state == KD_ShopItem.ConfirmOwned) { detail = L("KD_ShopShelf.WordOwned"); yes = L("KD_ShopShelf.WordUse"); action = ActUse; }
            else if (state == KD_ShopItem.ConfirmSpecial)
            {
                // CE: 値段は Credits、[購入] は Storefront を開く。pt のバーは出さない
                detail = L("KD_ShopShelf.WordSpecial") + " / " + L("KD_ShopShelf.WordLocked");
                if (item.ceCredits > 0) price = item.ceCredits.ToString() + " C";
                yes = L("KD_ShopShelf.WordBuy"); action = ActStore;
            }
            else
            {
                ptPath = true;
                price = item.price.ToString() + " pt";
                if (state == KD_ShopItem.ConfirmBuy) { yes = L("KD_ShopShelf.WordBuy"); action = ActBuy; }
                else detail = L("KD_TreatPouch.WordAccWaitPrefix") + " " + item.Shortfall().ToString() + " pt";
            }
            SetConfirm(DisplayNameOf(item), yes, action);
            if (confirmDetail != null) confirmDetail.text = detail;
            if (confirmPrice != null) confirmPrice.text = price;

            UpdateConfirmBar(item, ptPath);
        }

        /// <summary>「あと N pt」の資源バー (AvatarIdleArena IMP-UI-007 の移植)。
        ///
        /// 数字だけを出すのは「残高」と「値段」を頭の中で引き算させるということ。
        /// 部分 = 暗金 / 満タン = アクセント金 (満タン = [購入] が出ている状態と一致する)。
        /// 「あと N pt」のテキストは消さない (数字併記)。</summary>
        private void UpdateConfirmBar(KD_ShopItem item, bool ptPath)
        {
            if (confirmBar == null) return;
            int price = item.price;
            if (!ptPath || price <= 0)
            {
                confirmBar.SetActive(false);
                return;
            }
            confirmBar.SetActive(true);
            // int 同士の除算にしない (途中経過が 0 に潰れる)
            float fill = Mathf.Clamp01(1f - (float)item.Shortfall() / price);
            if (confirmBarFill != null) confirmBarFill.anchorMax = new Vector2(fill, 1f);
            if (confirmBarFillImage != null)
                confirmBarFillImage.color = fill >= 1f ? barFullColor : barColor;
        }

        /// <summary>確認パネルの肯定ボタン。**pt が減るのはここだけ。**
        /// ローカル専用なので `_` 始まり。</summary>
        public void _ConfirmYes()
        {
            if (string.IsNullOrEmpty(_openId) || _openAction == ActNone) { CloseConfirm(); Redraw(); return; }
            string id = _openId;
            int act = _openAction;
            CloseConfirm();

            if (act == ActBuy)
            {
                if (catalog._RequestBuy(id)) { _dbgBuyCount++; Play(buyClip); Flash("KD_ShopShelf.WordBought"); }
                else Reject("KD_ShopShelf.WordCantBuy");
            }
            else if (act == ActStore)
            {
                // 所有は Store から `OnPurchaseConfirmed` で戻ってくる。ここでは開くだけ
                int i = IndexOf(id);
                if (i >= 0) { items[i]._OpenStore(); Play(selectClip); }
                else Reject("KD_ShopShelf.WordNotHere");
            }
            else
            {
                if (catalog._RequestSelect(id)) Play(selectClip);
                else Reject("KD_ShopShelf.WordCantSelect");
            }
            Redraw();
        }

        /// <summary>確認パネルの否定ボタン。ローカル専用なので `_` 始まり。</summary>
        public void _ConfirmNo()
        {
            CloseConfirm();
            Redraw();
        }

        private void SetConfirm(string titleText, string yesText, int action)
        {
            _openAction = action;
            if (confirmTitle != null) confirmTitle.text = titleText;
            if (yesLabel != null && yesText != null) yesLabel.text = yesText;
            // **できないことのボタンは出さない** — 押しても無反応なボタンは、
            // 壊れているのか仕様なのかが読めない
            if (yesButton != null) yesButton.SetActive(yesText != null);
        }

        private void ShowConfirmOver(KD_ShopItem item)
        {
            if (confirmRoot == null) return;
            var p = confirmRoot.transform.parent;
            Vector3 local = p == null
                ? item.transform.position
                : p.InverseTransformPoint(item.transform.position);

            if (item.menuOnly)
            {
                // ---- メニュー行は**横**へ逃がす ----
                //
                // 実物棚と違い、行は**縦に並んでいる**。真上に出すと上の行に重なって
                // 品ぞろえが読めなくなる — 「何を確認しているのか」を隠すのが
                // いちばん困る隠し方になる。**横なら行は全部見えたまま**で、
                // 触った行の真横に出るので対象も読める
                local.x += confirmMenuSideShift;
                local.y += confirmMenuLift;
            }
            else
            {
                // 実物棚は商品の**真上**へ。手の位置にボタンが来ないようにするため
                // (触るのに伸ばした腕が、そのまま [購入] に当たると誤爆になる)
                local.y += confirmHeight;
            }

            confirmRoot.transform.localPosition = local;
            confirmRoot.SetActive(true);
        }

        private void CloseConfirm()
        {
            _openId = "";
            _openAction = ActNone;
            if (confirmRoot != null) confirmRoot.SetActive(false);
        }

        private void Reject(string why)
        {
            _dbgRejectCount++;
            Play(rejectClip);
            Flash(why);
        }

        private void Flash(string msg)
        {
            _flash = msg;
            _flashUntil = Time.time + 3f;
        }

        private void Play(AudioClip clip)
        {
            if (sfx == null || clip == null) return;
            sfx.PlayOneShot(clip);
        }

        // ============================================================
        // 表示
        // ============================================================

        /// <summary>周期的に描き直す。**`Update` ではない** — 1 秒に 1 回だけ走る。
        /// ローカル専用なので `_` 始まり。</summary>
        public void _Refresh()
        {
            RefreshOpenConfirm();
            Redraw();
            SendCustomEventDelayedSeconds(nameof(_Refresh), Mathf.Max(refreshSeconds, 0.25f));
        }

        public void _RefreshLanguage() { RefreshOpenConfirm(); Redraw(); }

        /// <summary>開いたままの確認パネルを今の状態へ追いつかせる。
        ///
        /// 開いた時点のスナップショットのままだと、**開いたまま pt が閾値を跨いだとき
        /// 「あと 1pt」と [購入] 無しのまま陳腐化する。** 音は鳴らさない・位置も動かさない
        /// (どちらも「触った瞬間」だけのもの)。</summary>
        private void RefreshOpenConfirm()
        {
            if (string.IsNullOrEmpty(_openId)) return;
            if (confirmRoot == null || !confirmRoot.activeSelf) return;
            int i = IndexOf(_openId);
            if (i < 0) return;
            ApplyConfirmState(items[i]);
        }

        private void Redraw()
        {
            if (label == null) return;

            string s = "== " + L(titleKey) + " ==\n";

            // ---- 残高。**棚の面にだけ出す** ----
            //
            // 常時 HUD へ出すと「ポイントを稼がせる UI」になる (危険 7)。
            // 棚を見ているときだけ読めて、犬と遊んでいるときは視界に入らない
            var ledger = catalog == null ? null : catalog.ledger;
            if (ledger == null) s += L("KD_ShopShelf.WordNoLedger") + "\n";
            // **復元前に 0 と書かない** — 「消えた」に見える
            else if (!ledger.Loaded()) s += L("KD_ShopShelf.WordLoading") + "...\n";
            else s += L("KD_ShopShelf.WordRemain") + " " + ledger.Points().ToString() + " pt\n";

            if (_flash.Length > 0 && Time.time <= _flashUntil) s += L(_flash) + "\n";

            label.text = s;
        }

        /// <summary>いま世界に出ている品目 (共有カテゴリ)。</summary>
        private string AppliedNow()
        {
            return toyBox == null ? "" : toyBox.AppliedProduct();
        }

        /// <summary>いま使われているか。**共有カテゴリは世界の値と比べる。**</summary>
        private bool InUse(KD_ShopItem it, string appliedProduct)
        {
            if (toyBox != null && it.categoryKey == toyBox.categoryKey)
            {
                // 世界の値。まだ誰も選んでいなければ「使用中」は出さない
                return appliedProduct != KD_ToyBox.Unset && appliedProduct == it.productKey;
            }
            if (catalog == null) return false;
            return catalog.SelectedProductOf(it.categoryKey) == it.productKey;
        }

        private string DisplayNameOf(KD_ShopItem it)
        {
            // **GameObject 名を出さない** — `KD_ShopItem_Toy_0` と書いてある棚は
            // ショップに見えない
            return (it.displayName != null && it.displayName.Length > 0)
                ? L(it.displayNameKey) : it.gameObject.name;
        }

        // net: callable-ok: 読み取りのみ
        public int IndexOf(string productKey)
        {
            if (items == null) return -1;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;
                if (items[i].productKey == productKey) return i;
            }
            return -1;
        }
    }
}
