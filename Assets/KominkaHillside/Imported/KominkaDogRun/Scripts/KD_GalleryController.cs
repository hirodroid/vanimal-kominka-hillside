using UdonSharp;
using UnityEngine;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-GAL-001/002/003: 縁側ギャラリー。スロット RT のラウンドロビン管理。
    ///
    /// **上書きはカテゴリの中だけで回る** (IMP-CAM-003)。全スロットを一列に
    /// 回すと、撮影が増えるほど「同じような絵で埋まる」うえ、たまたま撮れた良い 1 枚が
    /// 流れやすくなる。
    /// **カテゴリは IMP-CAM-002 で「構図」から「出来事」へ切り直した**
    /// (もってきた / 撫で / 犬目線 / かけっこ / じゃれ合い / 庭ごと。
    ///  正本は KD_PhotoSetupEditor.SlotGroupNames)。カテゴリ g は スロット {g, g+G, g+2G, ...} を持つ
    /// (連続ではなく**間引き**)。
    /// **IMP-GAL-004 で `BoardsPerRow == slotGroupCount` にしたので、
    /// この間引きがそのまま「1 列 = 1 カテゴリ」になる** — 割り当ての式は変えていない。
    /// **IMP-CAM-005 (2026-09-12) でカテゴリごとの枚数を可変にした** — `slotGroups` (スロット → カテゴリ) を
    /// Setup が列表から配り、カテゴリ g はその中で古い順に回る (8 / 8 / 4 / 4)。
    /// `slotGroups` が無い器 (寝室の 1 カテゴリ) は従来の間引き式のまま。
    ///
    /// すべてローカル (自分が撮った瞬間のアルバム。不変条件 5 — 画面は同期しない)。
    /// **撮影カメラはスロット RT へ直接描かない** (IMP-GAL-003) — 共有の深度付き
    /// 撮影用 RT へ 1 フレーム描き、確定後に `VRCGraphics.Blit` で色だけスロットへ写す。
    /// スロットは深度を持たないので、直接描かせると深度テストが働かない。
    /// 持ち帰りは板そのもの — 各板が吸着板で、カメラを寄せるだけで全画面化して撮れる
    /// (IMP-GAL-002 のエイム判定で競合を解決)。
    /// 持ち帰り台は 2026-08-11 に廃止 (板から直接持ち帰れるため不要 — ユーザー判断)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_GalleryController : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/Photo System が設定する)")]
        [Tooltip("スロット RT (撮影が古い順に上書きされる)")]
        public RenderTexture[] slotTextures;
        [Tooltip("ギャラリー板 (index はスロットと対応)。未撮影の間は非表示")]
        public GameObject[] boards;
        [Tooltip("カテゴリ数 (出来事。IMP-CAM-002)。スロット数を割り切る値にする")]
        public int slotGroupCount = KD_PhotoDirector.GroupCount;
        [Tooltip("スロットごとのカテゴリ番号 (IMP-CAM-005。長さ = スロット数。空なら等分の間引き)")]
        public int[] slotGroups;

        bool[] _slotUsed;
        int[] _nextInGroup;

        void Start()
        {
            if (slotTextures == null || slotTextures.Length == 0)
            {
                Debug.LogError("[KD_GalleryController] wiring incomplete. Run Tools/KominkaDogRun/Photo System setup.");
                enabled = false;
                return;
            }
            if (slotGroupCount < 1) slotGroupCount = 1;
            if (slotGroupCount > slotTextures.Length) slotGroupCount = slotTextures.Length;
            _slotUsed = new bool[slotTextures.Length];
            _nextInGroup = new int[slotGroupCount];

            if (boards != null)
                for (int i = 0; i < boards.Length; i++)
                    if (boards[i] != null) boards[i].SetActive(false);

            // BUG-CAM-003: **モバイルでは吸着を切る。** iOS では写真カメラのパスで板を
            // 全画面化すると真っ黒になる (実機 3 回で確定。原因はシェーダーの外)。
            // 切らずに置くと、カメラを板へ寄せた人が黒画面を踏む。
            // モバイルの持ち帰りは 板をタップ → KD_PhotoViewer で目の前に大きく → 端末のスクリーンショット。
            //
            // **define で決めるのはフラグだけにして、本体は全プラットフォームでコンパイルさせる。**
            // 本体ごと #if で囲むと、PC のコンパイルでは Udon への公開 (Renderer.material /
            // Material.SetFloat) が検査されず、**モバイルのビルドで初めて落ちる**。
            // 距離 0 = 決して吸着しない ("camDistance < 0" は偽)。
            // **共有マテリアルを触らず** インスタンス化する (Editor の Play で資産を汚さない)。
            // **define が U# のビルドで効いているかは実機で確認する** (効いていなければ従来どおり吸着する = 退行は無い)
            bool mobile = false;
#if UNITY_ANDROID || UNITY_IOS
            mobile = true;
#endif
            if (mobile && VRC.SDKBase.Utilities.IsValid(VRC.SDKBase.Networking.LocalPlayer)
                && !VRC.SDKBase.Networking.LocalPlayer.IsUserInVR() && boards != null)
                for (int i = 0; i < boards.Length; i++)
                {
                    if (boards[i] == null) continue;
                    MeshRenderer r = boards[i].GetComponent<MeshRenderer>();
                    if (r != null) r.material.SetFloat("_AttachDistance", 0f);
                }
        }

        /// <summary>次の撮影先スロットを確保して返す (KD_PhotoDirector が呼ぶ)。
        /// group は構図カテゴリ — **その中でだけ**古い順に上書きする。
        /// 対応する板を表示する。
        /// **確保 = 即コミット** (予約ではない) — 呼ぶ側が「撮影が必ず走る」状態を
        /// 確認してから呼ぶこと (KD_PhotoDirector.Shoot の再入ガードが前提)。</summary>
        public RenderTexture _AcquireNextSlot(int group)
        {
            if (_slotUsed == null || _nextInGroup == null) return null;
            int n = slotTextures.Length;
            if (group < 0 || group >= slotGroupCount) group = 0;

            bool mapped = slotGroups != null && slotGroups.Length == n;
            int owned = 0;
            if (mapped) { for (int i = 0; i < n; i++) if (slotGroups[i] == group) owned++; }
            // 表が無ければ等分の間引き (割り切れないときは前のカテゴリが 1 枚多い)
            else owned = (n - group + slotGroupCount - 1) / slotGroupCount;
            if (owned < 1) return null;

            int k = _nextInGroup[group];
            if (k < 0 || k >= owned) k = 0;

            int idx = -1;
            if (mapped)
            {
                // カテゴリ g の k 枚目 (板の番号順 = 下の段から左→右)
                int seen = 0;
                for (int i = 0; i < n && idx < 0; i++)
                    if (slotGroups[i] == group) { if (seen == k) idx = i; seen++; }
            }
            else idx = group + k * slotGroupCount;
            if (idx < 0 || idx >= n) return null;

            // ================================================================ 破壊的監査 RC-10a
            // **返す値を確かめてからコミットする。**
            //
            // 旧実装は「巡回を進める → 板を点灯 → 返す」の順で、**返す値の null 検査が
            // 無かった**。RT アセットが 1 枚欠けている (削除 / .meta 破損 / Setup 未実行のまま
            // 配列だけ手で伸ばした) と、その枠に当たった撮影で
            // **中身が空のまま板が「撮影済み」として恒久点灯**する。
            //
            // Codex 指摘で入れた「`CanCapture` をスロット確保より先に見る」は
            // **逆向きの経路 (撮れないのに確保する) しか塞いでいない** — こちらは
            // 「確保できたのに使えない物を返す」経路で、順序ガードの射程外だった。
            RenderTexture rt = slotTextures[idx];
            if (rt == null)
            {
                Debug.LogWarning("[KD_GalleryController] slotTextures[" + idx + "] が null - "
                    + "この撮影を捨てる (板は点灯させない)。Run Tools/KominkaDogRun/Photo System setup.");
                return null;
            }

            // ここまで来て初めてコミットする。**巡回もここで進める** —
            // 先に進めると、捨てた撮影のぶんだけカテゴリ内の順番が飛ぶ
            _nextInGroup[group] = (k + 1) % owned;
            _slotUsed[idx] = true;
            if (boards != null && idx < boards.Length && boards[idx] != null)
                boards[idx].SetActive(true);
            return rt;
        }
    }
}
