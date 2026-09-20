using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-PET-006 / IMP-PRG-002 Phase 3: 撫でエフェクトの消費側。
    ///
    /// 一文で: **カタログで選ばれた撫でエフェクトを、4 匹ぶんの発火口へ配る。それだけ。**
    ///
    /// ## おもちゃ (`KD_ToyBox`) と対になるが、同期の性格が正反対
    ///
    /// | | おもちゃ | 撫でエフェクト |
    /// |---|---|---|
    /// | 型 | **共有物** — 世界に 1 つ | **個人装備** — 人ごとに違ってよい |
    /// | 世界の値の正本 | `KD_ToyBox` の同期変数 | **カタログのカーソルそのもの** |
    /// | SyncMode | Manual (同期変数 1 本) | **None (同期変数 0 本)** |
    ///
    /// **個人装備なのでカーソル == 自分に適用される値**
    /// ([shop-foundation.md §6](../../../docs/development/design/shop-foundation.md) の表)。
    /// だから復元時もそのまま適用してよい — おもちゃで禁じた
    /// 「後から入った人が既存プレイヤーの見た目を書き換える」は、
    /// **自分にしか効かないので構造的に起こらない。**
    ///
    /// ## ⚠ 他人の画面には、まだ出ない (Phase 3 の残り)
    ///
    /// `attentionPlayerId` で**誰が撫でているか**は全員が知っているが、
    /// **「その人が何を選んだか」を知る経路が無い**:
    ///
    /// - `petState` を書けるのは**犬の Owner だけ**で、Owner は撫で主の選択を知らない
    /// - 撫でる人が犬の Ownership を取るのは IMP-NET-005 不変条件 3 が禁止
    /// - `SendCustomNetworkEvent` は**送り主も引数も運べない**
    ///
    /// → **プレイヤー単位で値を publish する仕組み** (VRChat の Player Object 等) が要る。
    /// **このワールドで初めての機構**なので、独立した判断として切り出してある。
    /// 今は「自分の撫でには自分のエフェクトが出る。他人の撫では既定の ♡」まで。
    /// </summary>
    /// <remarks>
    /// SyncMode None。**同期変数を 1 本も持たない** — 個人装備なので同期する状態が無い。
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_PetFxBox : UdonSharpBehaviour
    {
        public const string Unset = "";

        [Header("棚")]
        [Tooltip("KD_ShopCatalog。**個人装備なのでカーソルをそのまま適用してよい**")]
        public KD_ShopCatalog catalog;

        [Tooltip("撫でエフェクトカテゴリの枠番号")]
        public string categoryKey = "petfx";

        [Header("配り先 (Setup が入れる)")]
        [Tooltip("犬 4 匹の撫でコントローラ。**発火口はここが持っている**")]
        public KD_DogPetController[] petControllers;

        [Tooltip("誰も選んでいないときに使う品目 (無料の初期エフェクト)")]
        public string defaultProductKey = "petfx.heart";

        [Header("他人へ見せる (Phase 3)")]
        [Tooltip("自分の PlayerObject にある札。**Setup が入れるのではなく実行時に探す** — " +
                 "PlayerObject は入室時に複製されるので、シーンの参照では掴めない")]
        public KD_PlayerFxTag localTag;

        private string _applied = Unset;

        /// <summary>復元待ちの残り試行回数 (0.5s × 60 = 30 秒で諦める —
        /// Persistence が来ない回は既定のままが正しい)。</summary>
        private int _restoreTries = 60;

        // デバッグ盤が読む値
        private int _dbgApplyCount;
        private int _dbgRemoteResolve;

        private void Start()
        {
            // **既定を先に当てておく。** 復元が来る前に撫でても ♡ が出る
            Apply(defaultProductKey);
            // **自分の復元は自分で読みに行く** (破壊的監査 v2 RC-4)。
            // カタログは復元時に消費側を起こさない (共有カテゴリを守るための原則) が、
            // 個人装備はカーソル == 自分に適用される値なので、こちらから読みに行かないと
            // **再入室のたびに既定 ♡ へ戻り、同値の選び直しも通知されず直せなかった。**
            // カタログ側の設計コメント「消費側が自分の復元時に読みに来る形にする」の実装
            SendCustomEventDelayedSeconds(nameof(_CheckRestored), 1f);
        }

        /// <summary>カタログの復元が済んだら、復元されたカーソルを自分へ適用する。
        /// ローカル専用なので `_` 始まり。</summary>
        public void _CheckRestored()
        {
            if (catalog == null) return;
            if (!catalog.Resolved())
            {
                _restoreTries--;
                if (_restoreTries > 0) SendCustomEventDelayedSeconds(nameof(_CheckRestored), 0.5f);
                return;
            }
            _OnShopSelectionChanged(); // 復元カーソルの適用 + PublishLocal (他人の画面ぶん)
        }

        /// <summary>カタログの選択が変わった。ローカル専用なので `_` 始まり。</summary>
        public void _OnShopSelectionChanged()
        {
            if (catalog == null) return;
            // 自分のカテゴリの変更でなければ何もしない (-1 = 不明は fail-open。
            // KD_ToyBox と同じ規約 — 破壊的監査 v2 RC-4)
            string changed = catalog.ChangedCategory();
            if (!string.IsNullOrEmpty(changed) && changed != categoryKey) return;
            string want = catalog.SelectedProductOf(categoryKey);
            if (want == Unset) want = defaultProductKey;
            Apply(want);
        }

        private void Apply(string productId)
        {
            if (productId == Unset || productId == _applied) return;
            if (petControllers == null) return;
            for (int i = 0; i < petControllers.Length; i++)
            {
                if (petControllers[i] == null) continue;
                petControllers[i]._SelectPetFx(productId);
            }
            _applied = productId;
            _dbgApplyCount++;

            // **他人にも見えるように札へ載せる。** これが無いと
            // 「自分の画面でだけ肉球、他人にはハート」になる (Phase 3 の本体)
            PublishLocal(productId);
        }

        /// <summary>自分の PlayerObject の札へ選択を載せる。
        ///
        /// **札は実行時に探す** — `VRCPlayerObject` は入室時に複製されるので、
        /// シーンに置いた雛形への参照では自分のインスタンスを掴めない。</summary>
        private void PublishLocal(string productId)
        {
            if (localTag == null) localTag = FindTagOf(Networking.LocalPlayer);
            if (localTag != null) localTag._Publish(productId);
        }

        /// <summary>
        /// **撫で主に合わせてこの犬のエフェクトを差し替える** (Phase 3 の読み側)。
        ///
        /// <paramref name="petterId"/> が負 = 自分が撫でている / 誰も撫でていない →
        /// **自分の選択へ戻す。** リモートの撫でで他人のエフェクトに変わったままだと、
        /// 次に自分が撫でたとき他人のものが出てしまう。
        ///
        /// **呼ぶのは撫で主が変わったときだけ** — 毎フレーム全員を走査すると
        /// Udon では高い (`vrc-networking` §9)。
        ///
        /// ローカル専用なので `_` 始まり。
        /// </summary>
        public void _ApplyFxForPetter(KD_DogPetController dog, int petterId)
        {
            if (dog == null) return;
            string id = _applied;
            if (petterId >= 0)
            {
                var p = VRCPlayerApi.GetPlayerById(petterId);
                var tag = FindTagOf(p);
                // **札が見つからない / まだ選んでいない人は、自分の既定で描く。**
                // 「他人だから何も出ない」より「既定の ♡ が出る」ほうが体験として素直
                id = defaultProductKey;
                if (tag != null && catalog != null)
                {
                    int itemIndex = catalog.IndexOfProduct(tag.SelectedFx());
                    if (itemIndex >= 0)
                    {
                        var item = catalog.items[itemIndex];
                        if (item.categoryKey == categoryKey && item.OwnedBy(p)) id = item.productKey;
                    }
                }
                _dbgRemoteResolve++;
            }
            if (id != Unset) dog._SelectPetFx(id);
        }

        /// <summary>その人の PlayerObject から札を拾う。
        ///
        /// **`FindComponentInPlayerObjects` は使わない** — `Component` を返すので
        /// U# 側でキャストが要り、**型キャストで Behaviour が停止する罠**
        /// (`vrc-udonsharp` §1) の面に触れる。`GetComponent&lt;T&gt;` なら型が確定する。</summary>
        private KD_PlayerFxTag FindTagOf(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return null;
            var objs = Networking.GetPlayerObjects(player);
            if (objs == null) return null;
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] == null) continue;
                var tag = objs[i].GetComponent<KD_PlayerFxTag>();
                if (tag != null) return tag;
            }
            return null;
        }

        /// <summary>いま自分に適用されている品目。棚の「使用中」はこれと比べる。</summary>
        // net: callable-ok: 読み取りのみ — 遠隔から呼ばれても状態が動かない
        public string AppliedProduct() { return _applied; }
    }
}
