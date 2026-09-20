using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-PRG-002 / IMP-FET-009 Phase 2: おもちゃの消費側。
    /// 正本は [shop-foundation.md](../../../docs/development/design/shop-foundation.md) §5 / §6。
    ///
    /// 一文で: **カタログで選ばれたおもちゃを、4 犬種のボールの
    /// 兄弟ノードの `SetActive` で世界へ出す。それだけ。**
    ///
    /// ## ここが「世界に出ている値」の正本
    ///
    /// おもちゃは**共有物**なので、世界のボールは 1 種類しかない。
    /// <see cref="KD_ShopCatalog"/> が持つのは**そのプレイヤーの棚のカーソル**であって、
    /// 世界の値ではない。
    ///
    /// <code>
    /// 自分が選ぶ → カタログが _OnShopSelectionChanged を叩く
    ///            → ここが Owner を取り、同期変数へ書く
    ///            → **見た目は自分も他人も、同期変数からしか作らない**
    ///
    /// 他人 / Late Joiner → OnDeserialization → 同期変数 → SetActive
    ///                      **カタログを一切読まない**
    /// </code>
    ///
    /// これを混ぜて「消費側がカタログを読む」にすると、
    /// **後から入った人の Persistence が既にいるプレイヤーのボールを書き換える。**
    ///
    /// ## 触らないもの
    ///
    /// **Fetch のコードを 1 行も変えない。ボールのプレハブへ UdonBehaviour を足さない。**
    /// 参照は「シーン側 → プレハブインスタンスの子」の向きだけを使う
    /// (逆向きは実機ビルドで null に化ける — `vrc-udonsharp` §3)。
    /// おかげで `Fetch System/3 - Verify` の「UdonBehaviour ちょうど 1 個」が
    /// **無改造のまま回帰ゲートとして使える。**
    ///
    /// `Renderer.material` も実行時に触らない (`vrc-udonsharp` §6)。
    /// 切り替えは**兄弟のうち 1 つだけ `SetActive(true)`** に統一する。
    /// </summary>
    /// <remarks>
    /// SyncMode Manual・**専用 GameObject**。
    /// U# は同一 GameObject で同期方式を混在できないので (`vrc-udonsharp` §2)、
    /// SyncMode None の <see cref="KD_ShopCatalog"/> と同居させられない。
    ///
    /// 同期変数は **1 本だけ** (選択中の恒久 ID)。4 犬種まとめて 1 選択なので
    /// 犬ごとに持つ必要が無い (2026-08-23 ユーザー決定。識別層は色と大きさが担う)。
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KD_ToyBox : UdonSharpBehaviour
    {
        /// <summary>まだ誰も選んでいない。**既定値をこれにするのが要点** —
        /// 下の <c>_syncedProduct</c> の注記を参照。</summary>
        public const string Unset = "";

        /// <summary>Ownership 移譲を待つ上限 (秒)。過ぎたら楽観適用を畳む。</summary>
        private const float PendingTimeoutSeconds = 5f;

        [Header("棚")]
        [Tooltip("KD_ShopCatalog。選択が変わったときに叩かれる側なので、" +
                 "**見た目を作るのにここは読まない** (読むと他人の画面が自分の棚で上書きされる)")]
        public KD_ShopCatalog catalog;

        [Tooltip("おもちゃカテゴリの枠番号。カタログ側の categoryKey とそろえる")]
        public string categoryKey = "toy";

        [Header("見た目の器 (Setup が入れる)")]
        [Tooltip("品目ごとの恒久 ID。**meshNodes と添字で対応する**")]
        public string[] productKeys;

        [Tooltip("品目 × 犬の兄弟ノード。**productKeys[i] の犬 d 番目 = " +
                 "meshNodes[i * dogCount + d]**。1 次元なのは U# が多次元配列を扱えないため")]
        public GameObject[] meshNodes;

        [Tooltip("犬の数。meshNodes の添字計算に使う")]
        public int dogCount = 4;

        [Header("操作案内 (共有の見た目を各自の言語で表示)")]
        public KD_Language language;
        public VRC_Pickup[] pickups;
        public string[] productNameKeys;
        public string[] productNames;
        public string[] dogNameKeys;
        public string[] dogNames;

        /// <summary>いま世界に出ているおもちゃの恒久 ID。**共有カテゴリの正本。**
        ///
        /// **既定値は 0 ではなく -1 (未設定)。** 0 = 基本ボールにすると、
        /// シーンの初期 active (IMP-FET-009 Phase 1 で TennisBall) と食い違い、
        /// **最初の人が基本ボールを選んだ 1 回だけ値が変わらず、
        /// `OnDeserialization` が飛ばないので他人の画面だけ変わらない。**
        /// テストで最も見つけにくい形なので、-1 = 「誰も選んでいない = シーンの初期状態が正」
        /// にしておく。</summary>
        [UdonSynced] private string _syncedProduct = Unset;

        // 適用済みの値。二重適用と無駄な SetActive を避ける
        private string _applied = Unset;

        // Ownership 移譲の完了を待っている間の保留分
        private string _pendingProduct = Unset;
        private bool _hasPending;

        // デバッグ盤が読む値
        private int _dbgApplyCount;
        private int _dbgRejectCount;

        private void Start()
        {
            // **-1 のうちは何も適用しない。** シーンの初期状態がそのまま正しい。
            // Late Joiner は OnDeserialization で正しい値を受け取る
            if (_syncedProduct != Unset) Apply(_syncedProduct);
            _RefreshPickupText();
        }

        // ============================================================
        // カタログからの通知 (自分が選んだとき)
        // ============================================================

        /// <summary>カタログの選択が変わった。ローカル専用なので `_` 始まり。</summary>
        public void _OnShopSelectionChanged()
        {
            if (catalog == null) return;
            // **自分のカテゴリの変更でなければ何もしない** (破壊的監査 v2 RC-4)。
            // これが無いと、撫でエフェクトを選んだだけでここが走り、
            // **復元済みのおもちゃカーソルを世界へ適用**してしまう —
            // 「復元では消費側を起こさない」原則が、無関係な初回選択へずれて破れる。
            // -1 (不明) は fail-open で処理する
            string changed = catalog.ChangedCategory();
            if (!string.IsNullOrEmpty(changed) && changed != categoryKey) return;
            string want = catalog.SelectedProductOf(categoryKey);
            if (want == Unset) return;
            if (IndexOfProduct(want) < 0) return;      // 器を持っていない品目
            if (want == _syncedProduct && want == _applied) return;

            if (Networking.IsOwner(gameObject))
            {
                Publish(want);
                return;
            }

            // **SetOwner の直後に同期変数へ書かない。** 移譲が完了する前の書き込みは
            // 捨てられうる。IMP-FET-001 Phase 2 で同じ修正をしている
            _pendingProduct = want;
            _hasPending = true;
            // **移譲を待つ間だけ、先に見た目を出す。** 往復を待つと
            // 「選んだのに 0.3 秒後に変わる」になり、DES-NET-001 が継続同期を
            // 却下したときと同じ質の劣化を招く。
            // **この楽観適用は必ず Publish か下のタイムアウトで決着させる**
            Apply(want);
            Networking.SetOwner(Networking.LocalPlayer, gameObject);   // net: setowner-ok: 棚の選択。犬にも移動にも触らない (対象は自分の GameObject だけ)
            SendCustomEventDelayedSeconds(nameof(_PendingTimeout), PendingTimeoutSeconds);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;
            if (!_hasPending) return;
            _hasPending = false;
            Publish(_pendingProduct);
        }

        /// <summary>Ownership が来ないまま時間が過ぎたとき、楽観適用を畳む。
        ///
        /// **これが無いと `_hasPending` が立ちっぱなしになり、
        /// 以後の `OnDeserialization` を無視し続けて自分の画面だけ取り残される。**
        /// ローカル専用なので `_` 始まり。</summary>
        public void _PendingTimeout()
        {
            if (!_hasPending) return;
            _hasPending = false;
            // 要求は通らなかった。**世界の値へ戻す** (嘘の見た目を残さない)
            Apply(_syncedProduct);
        }

        /// <summary>同期変数へ書いてから、**その値で**見た目を作る。
        ///
        /// **`Apply(product)` ではなく `Apply(_syncedProduct)` なのが要点**
        /// (2026-08-23 Codex 指摘)。見た目の出どころを同期変数へ一本化しておかないと、
        /// 移譲待ちの間に旧 Owner の値を受信したときに
        /// **選んだ本人だけが古い見た目のまま取り残される。**</summary>
        private void Publish(string product)
        {
            _syncedProduct = product;
            RequestSerialization();
            Apply(_syncedProduct);
        }

        // ============================================================
        // 受信 (他人・Late Joiner)
        // ============================================================

        public override void OnDeserialization()
        {
            // **移譲待ちの間は受信で上書きしない。** 旧 Owner の古い値が届いて
            // 見た目が一瞬戻る (しかも Publish は下で必ず追いつく)。
            // 待ちが解けない場合は _PendingTimeout が畳む
            if (_hasPending) return;
            Apply(_syncedProduct);
        }

        /// <summary>兄弟のうち 1 つだけを active にする。
        ///
        /// **範囲外は適用しない。** 全部 false にすると当たり判定 (root) は残ったまま
        /// **ボールが透明になり、掴めて飛ぶのに見えない**という最悪の壊れ方をする。
        /// 壊れた値が来ても、今の見た目を保つほうが常にましなので何もしない。</summary>
        private void Apply(string product)
        {
            if (product == Unset) return;
            int index = IndexOfProduct(product);
            if (index < 0) { _dbgRejectCount++; return; }
            if (meshNodes == null || productKeys == null) return;
            if (dogCount <= 0) return;
            if (meshNodes.Length < productKeys.Length * dogCount) { _dbgRejectCount++; return; }
            if (product == _applied) return;

            for (int i = 0; i < productKeys.Length; i++)
            {
                bool on = (i == index);
                for (int d = 0; d < dogCount; d++)
                {
                    var go = meshNodes[i * dogCount + d];
                    if (go == null) continue;
                    if (go.activeSelf != on) go.SetActive(on);
                }
            }
            _applied = product;
            _dbgApplyCount++;
            _RefreshPickupText();
        }

        public void _RefreshPickupText()
        {
            int index = IndexOfProduct(_applied);
            if (index < 0) index = IndexOfProduct("toy.basic_ball");
            if (index < 0 || pickups == null || productNames == null || index >= productNames.Length) return;
            string itemName = productNames[index];
            if (language != null && productNameKeys != null && index < productNameKeys.Length && !string.IsNullOrEmpty(productNameKeys[index]))
                itemName = language._Text(productNameKeys[index]);
            for (int d = 0; d < pickups.Length; d++)
            {
                if (pickups[d] == null) continue;
                string dogName = dogNames != null && d < dogNames.Length ? dogNames[d] : "";
                if (language != null && dogNameKeys != null && d < dogNameKeys.Length && !string.IsNullOrEmpty(dogNameKeys[d]))
                    dogName = language._Text(dogNameKeys[d]);
                pickups[d].InteractionText = string.IsNullOrEmpty(dogName) ? itemName : itemName + " (" + dogName + ")";
            }
        }

        // ============================================================
        // 棚が読む口
        // ============================================================

        /// <summary>**いま世界に出ている**恒久 ID。棚の「使用中 ✓」はこれと比べる —
        /// カタログのカーソルと比べると、他人が変えたあとも
        /// **自分の画面だけ ✓ が付いたまま**になる。
        /// まだ誰も選んでいなければ <see cref="Unset"/>。</summary>
        // net: callable-ok: 読み取りのみ — 遠隔から呼ばれても状態が動かない
        public string AppliedProduct() { return _applied; }

        /// <summary>器を持っている品目か。</summary>
        // net: callable-ok: 読み取りのみ
        public int IndexOfProduct(string product)
        {
            if (productKeys == null) return -1;
            for (int i = 0; i < productKeys.Length; i++)
                if (productKeys[i] == product) return i;
            return -1;
        }
    }
}
