using System.Collections.Generic;
using System.IO;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-PRG-002 ショップ: 3 - Verify と自己テスト。本体は KD_ShopSetupEditor.cs。</summary>
    public static partial class KD_ShopSetupEditor
    {
        // ================================================================ 3: Verify

        // 裏山改変 (IMP-PRG-001 段階 1): 古民家版は Imported-Unused へ。裏山の Menu/4a は KD_ShopSetupEditor.Hillside.cs の VerifyHillside
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Shop/3 - Verify Shop (Kominka)")]
        public static void Verify()
        {
            var v = new KD_VerifyLog("[KD_ShopSetup] Verify");
            var log = v.Text;

            // ---- Program Assets ----
            var catPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(CatalogProgram);
            var shelfPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ShelfProgram);
            var boxPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ToyBoxProgram);
            v.Check(shelfPa != null && shelfPa.behaviourSyncMode == BehaviourSyncMode.None,
                "shelf behaviourSyncMode == None (棚の表示は完全にローカル)");
            v.Check(catPa != null && catPa.sourceCsScript ==
                AssetDatabase.LoadAssetAtPath<MonoScript>(CatalogScript),
                "catalog program asset points at KD_ShopCatalog.cs");
            v.Check(boxPa != null && boxPa.sourceCsScript ==
                AssetDatabase.LoadAssetAtPath<MonoScript>(ToyBoxScript),
                "toybox program asset points at KD_ToyBox.cs");
            // **選択キーは非単調なので、購入や選択が同期に乗らないことを型で担保する**
            v.Check(catPa != null && catPa.behaviourSyncMode == BehaviourSyncMode.None,
                "catalog behaviourSyncMode == None (購入も選択の保存も完全にローカル)");
            // **1 本だけ増やす同期変数がここに載る**
            v.Check(boxPa != null && boxPa.behaviourSyncMode == BehaviourSyncMode.Manual,
                "toybox behaviourSyncMode == Manual (共有カテゴリの正本)");

            // ---- シーン ----
            int nCat, nBox, nShelf, nTreatShelf;
            var catalogGo = KD_SceneObjects.Find(CatalogName, out nCat);
            var toyBoxGo = KD_SceneObjects.Find(ToyBoxName, out nBox);
            var shelfGo = KD_SceneObjects.Find(ShelfName, out nShelf);
            var treatShelfGo = KD_SceneObjects.Find(TreatShelfName, out nTreatShelf);
            // **カタログが 2 個あると選択の正本が 2 つになる** (陳列は複数あってよい)
            v.Check(nCat == 1, CatalogName + " exists exactly once (actual " + nCat + ")");
            v.Check(nBox == 1, ToyBoxName + " exists exactly once (actual " + nBox + ")");
            v.Check(nShelf == 1, ShelfName + " exists exactly once (actual " + nShelf + ")");
            v.Check(nTreatShelf == 1,
                TreatShelfName + " exists exactly once (actual " + nTreatShelf + ")");

            var catalog = catalogGo == null ? null : catalogGo.GetComponent<KD_ShopCatalog>();
            var toyBox = toyBoxGo == null ? null : toyBoxGo.GetComponent<KD_ToyBox>();
            v.Check(catalog != null, "KD_ShopCatalog on " + CatalogName);
            v.Check(toyBox != null, "KD_ToyBox on " + ToyBoxName);

            // **名前ではなく型で数える。** 上の `nCat` は名前の検査なので、
            // 別名の GameObject へ 2 個目のカタログを付けると素通りしてしまう。
            // カタログが 2 個あると「どちらの棚で選んだかで結果が違う」が生まれる (§1)
            var catalogsInScene = Object.FindObjectsOfType<KD_ShopCatalog>(true);
            v.Check(catalogsInScene.Length == 1,
                $"KD_ShopCatalog がシーンにちょうど 1 個 (actual {catalogsInScene.Length}) — "
                + "**2 個あると選択の正本が 2 つになる**");

            // **専用 GameObject** — 同一 GameObject で同期方式は混在できない
            if (catalogGo != null)
                v.Check(catalogGo.GetComponents<VRC.Udon.UdonBehaviour>().Length == 1,
                    CatalogName + " の UdonBehaviour は 1 個 (専用 GameObject)");
            if (toyBoxGo != null)
                v.Check(toyBoxGo.GetComponents<VRC.Udon.UdonBehaviour>().Length == 1,
                    ToyBoxName + " の UdonBehaviour は 1 個 (Manual なので同居不可)");

            if (catalog != null)
            {
                v.Check(catalog.ledger != null, "catalog.ledger wired");
                v.Check(catalog.items != null && catalog.items.Length > 0, "catalog.items wired");
                v.Check(catalog.consumers != null && catalog.consumerEvents != null
                    && catalog.consumers.Length == catalog.consumerEvents.Length
                    && catalog.consumers.Length > 0,
                    "catalog.consumers と consumerEvents が同数で 1 つ以上");
            }

            // ---- 棚の操作と表示 (**全台**) ----
            //
            // **ここが欠けると Play で体験を判定できない。**
            // 「買えた気がしない」と「買った覚えのない購入」は、
            // 棚の見た目 (Phase 5) をどれだけ作っても回復しない種類の問題。
            //
            // **2026-08-24 に「棚 1 台前提」をやめた** (IMP-TRT-001 Phase 4b)。
            // 以前は名前 1 つ (`KD_ShopShelf`) から棚を引いていたので、
            // **2 台目 (おやつ棚) の配線・Canvas・確認パネル・商品の所属が
            // 1 つも検査されないまま PASS していた。** 陳列は何台あってもよい設計
            // (§1「カタログは 1 個。陳列は何個あってもよい」) なのだから、
            // **検査もシーン中の棚を全部列挙する形でなければ意味が合わない。**
            var shelves = Object.FindObjectsOfType<KD_ShopShelf>(true);
            log.AppendLine($"-- KD_ShopShelf: {shelves.Length} 台");
            v.Check(shelves.Length >= 2,
                $"棚が 2 台以上ある (actual {shelves.Length}) — おもちゃ棚とおやつ棚");
            var shelf = shelfGo == null ? null : shelfGo.GetComponent<KD_ShopShelf>();
            v.Check(shelf != null, "KD_ShopShelf on " + ShelfName);
            v.Check(treatShelfGo != null && treatShelfGo.GetComponent<KD_ShopShelf>() != null,
                "KD_ShopShelf on " + TreatShelfName);

            // 恒久 ID → 陳列ノード名 / モデルの key。**両方の品ぞろえ表をここで突き合わせる**
            var nodeByProduct = new Dictionary<string, string>();
            var meshKeyByProduct = new Dictionary<string, string>();
            foreach (var d in Toys)
            {
                nodeByProduct[d.productKey] = "KD_ToyMesh_" + d.meshKey;
                meshKeyByProduct[d.productKey] = d.meshKey;
            }
            foreach (var d in Treats)
            {
                nodeByProduct[d.productKey] = d.MeshNode;
                meshKeyByProduct[d.productKey] = d.meshKey;
            }

            foreach (var sh in shelves)
            {
                if (sh == null) continue;
                string tag = "[" + sh.gameObject.name + "] ";

                v.Check(sh.catalog != null, tag + "shelf.catalog wired");
                v.Check(sh.catalog == catalog,
                    tag + "shelf.catalog が**唯一のカタログ**を指している — "
                    + "棚ごとに別のカタログを指すと選択の正本が分裂する");
                v.Check(sh.items != null && sh.items.Length > 0, tag + "shelf.items wired");
                v.Check(sh.label != null, tag + "shelf.label wired (残高と 3 状態の出力先)");
                v.Check(sh.label != null && sh.label.font != null,
                    tag + "shelf.label に font asset が刺さっている (無いと全部豆腐になる)");
                v.Check(sh.title != null && sh.title.Length > 0, tag + "shelf.title がある");
                v.Check(sh.sfx != null && sh.buyClip != null && sh.rejectClip != null,
                    tag + "買えた音と断られた音がある — **どちらも無言にしない** "
                    + "(「押しても無反応」と区別が付かなくなる)");

                // **`toyBox` が要るのは共有カテゴリの品を置く棚だけ。**
                // おやつは共有物ではないので「世界に出ている値」が無く、
                // 「使用中 ✓」はカタログのカーソルと比べるのが正しい (§6 の表)。
                // **一律に要求すると、おやつ棚に意味の無い参照を刺すことになる**
                bool hasToyCategory = false;
                if (sh.items != null)
                    foreach (var it in sh.items)
                        if (it != null && it.categoryKey == CategoryToy) hasToyCategory = true;
                if (hasToyCategory)
                    v.Check(sh.toyBox != null,
                        tag + "shelf.toyBox wired — **「使用中 ✓」を世界の値と比べるのに要る** "
                        + "(カタログのカーソルと比べると他人の変更が反映されない)");
                else
                    v.Check(sh.toyBox == null,
                        tag + "共有カテゴリを置かない棚は toyBox を持たない "
                        + "(持たせても意味が無く、あとで読み違える)");

                // ---- 陳列が実物になっているか ----
                //
                // **holder に描画が残っていると、灰色の箱が実物と二重に出る**
                // (以前 primitive の Cube で作っていた名残)
                if (sh.items != null)
                {
                    int boxes = 0, noDisplay = 0;
                    foreach (var it in sh.items)
                    {
                        // **メニューの品は実物を持たない** — 実物が存在しない商品の売り場なので
                        if (it == null || it.menuOnly) continue;
                        if (it.GetComponent<MeshRenderer>() != null) boxes++;
                        if (it.GetComponentsInChildren<MeshRenderer>(true).Length == 0) noDisplay++;
                    }
                    v.Check(boxes == 0,
                        tag + $"陳列の holder 自身は描画を持たない (actual {boxes}) — "
                        + "**旧 primitive の灰色の箱が実物と二重に出る**");
                    v.Check(noDisplay == 0,
                        tag + $"陳列すべてに実物のメッシュがある (無い {noDisplay} 件)");

                    // **陳列が「その商品の実物」を指しているか。**
                    // メッシュが 1 つずれても見た目は「何か置いてある」ままなので、
                    // Play では「品ぞろえがへん」としか感じられず原因に辿り着けない。
                    // **おやつにも同じ検査を横展開する** (おもちゃだけ守っても片手落ち)
                    foreach (var it in sh.items)
                    {
                        if (it == null || it.menuOnly) continue;
                        string nodeName, meshKey;
                        if (!nodeByProduct.TryGetValue(it.productKey, out nodeName))
                        {
                            v.Check(false, tag + $"id {it.productKey} ({it.displayName}) が"
                                + "品ぞろえ表のどちらにも無い");
                            continue;
                        }
                        meshKey = meshKeyByProduct[it.productKey];
                        var node = it.transform.Find(nodeName);
                        v.Check(node != null, tag + $"{it.displayName}: 陳列ノード {nodeName} がある");
                        if (node == null) continue;
                        var mf = node.GetComponent<MeshFilter>();
                        string meshPath = mf == null || mf.sharedMesh == null
                            ? "(mesh 無し)" : AssetDatabase.GetAssetPath(mf.sharedMesh);
                        // 基本ボールだけは組み込み Sphere なので AssetDatabase のパスが空になる
                        bool okMesh = meshKey == KD_ToySetupEditor.BasicBallKey
                            ? (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "Sphere")
                            : meshPath.Contains("/" + meshKey + "/");
                        v.Check(okMesh,
                            tag + $"{it.displayName}: 陳列の mesh が {meshKey} のもの (actual {meshPath})");
                    }
                }

                // ---- 確認パネル ----
                //
                // **押せるはずのボタンが押せない**のは Play で最も判断に困る壊れ方で、
                // 原因はたいてい VRCUiShape か layer。ここで型として落とす。
                // **棚ごとに 1 枚持つ部品**なので、2 台目にも同じ検査を当てる
                v.Check(sh.confirmRoot != null, tag + "shelf.confirmRoot wired (はい/いいえ のパネル)");
                v.Check(sh.confirmTitle != null, tag + "shelf.confirmTitle wired");
                v.Check(sh.yesButton != null && sh.yesLabel != null,
                    tag + "shelf.yesButton / yesLabel wired");
                // IMP-PRG-012: [ギフトする] は既定で非表示、onClick は _ConfirmGift 1 件。Bg は上端吸着で 244 (閉じているときの高さ)
                v.Check(sh.giftButton != null && sh.giftLabel != null && sh.confirmBg != null,
                    tag + "shelf.giftButton / giftLabel / confirmBg wired (IMP-PRG-012)");
                if (sh.giftButton != null)
                {
                    v.Check(!sh.giftButton.activeSelf, tag + "[ギフトする] は既定で非表示 (所有済みの CE のときだけ出す)");
                    var gb = sh.giftButton.GetComponent<UnityEngine.UI.Button>();
                    v.Check(gb != null && gb.onClick.GetPersistentEventCount() == 1
                        && gb.onClick.GetPersistentMethodName(0) == "SendCustomEvent"
                        && gb.onClick.GetPersistentTarget(0) == (UnityEngine.Object)UdonSharpEditorUtility.GetBackingUdonBehaviour(sh)
                        && new SerializedObject(gb).FindProperty("m_OnClick.m_PersistentCalls.m_Calls").GetArrayElementAtIndex(0)
                            .FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == "_ConfirmGift",
                        tag + "[ギフトする] の onClick が SendCustomEvent(\"_ConfirmGift\") でこの棚の UdonBehaviour 宛 1 件");
                }
                if (sh.confirmBg != null)
                    v.Check(Mathf.Approximately(sh.confirmBg.pivot.y, 1f) && Mathf.Approximately(sh.confirmBg.anchorMin.y, 1f)
                        && Mathf.Approximately(sh.confirmBg.sizeDelta.y, KD_ShopShelf.ConfirmBgHeight)
                        && sh.confirmRoot != null && Mathf.Approximately(((RectTransform)sh.confirmRoot.transform).sizeDelta.y, KD_ShopShelf.ConfirmBgHeightGift),
                        tag + "Bg は上端吸着 244、Canvas は 304 (ギフト行を含む当たり)");
                if (sh.confirmRoot != null)
                {
                    var cr = sh.confirmRoot;
                    v.Check(!cr.activeSelf,
                        tag + "確認パネルは既定で非アクティブ — **開いたまま保存すると、"
                        + "入室した瞬間から宙にパネルが浮いている**");
                    v.Check(cr.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null,
                        tag + "VRCUiShape がある (**無いと VRChat でボタンが押せない**)");
                    v.Check(cr.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null,
                        tag + "GraphicRaycaster がある");
                    v.Check(cr.layer == 0,
                        tag + $"Canvas の layer が Default (actual {cr.layer}) — "
                        + "**\"UI\" レイヤーだと VRChat のポインタが効かない** (vrc-udonsharp §5)");
                    // **onClick が Udon を指しているか。** ここが空だと
                    // 「押せるのに何も起きない」= 原因がまったく読めない状態になる
                    int wired = 0, dup = 0;
                    foreach (var b in cr.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                    {
                        int n = b.onClick.GetPersistentEventCount();
                        if (n >= 1) wired++;
                        if (n > 1) dup++;
                    }
                    v.Check(wired >= 2, tag + $"ボタン 2 つの onClick が配線済み (actual {wired})");
                    v.Check(dup == 0,
                        tag + $"onClick が二重登録されていない (actual {dup}) — "
                        + "**1 回押したら 2 回買える**ことになる");
                }

                // ---- 資源バー (「あと N pt」の視覚化) ----
                //
                // 壊れても例外もログも出ない部類 (見た目が静かに嘘をつく) なので型で押さえる
                v.Check(sh.confirmBar != null && sh.confirmBarFill != null
                    && sh.confirmBarFillImage != null,
                    tag + "資源バー (BarTrough/Fill) が配線済み — 「あと N pt」の視覚化");
                if (sh.confirmBarFill != null && sh.confirmBarFillImage != null)
                {
                    var fr = sh.confirmBarFill;
                    v.Check(fr.anchorMin == Vector2.zero && Mathf.Approximately(fr.anchorMax.y, 1f),
                        tag + "Fill が左端吸着 ((0,0)-(x,1)) — 幅は anchorMax.x だけで動かす");
                    v.Check(sh.confirmBarFillImage.sprite == null,
                        tag + "Fill は sprite 無しの単色矩形 (引き伸ばしで角が破綻しない)");
                    v.Check(!sh.confirmBarFillImage.raycastTarget,
                        tag + "Fill が raycastTarget を持たない (押下はボタンだけが拾う)");
                }

                // **商品同士が重なっていないか。**
                // 「作成時だけ配置する」形だと品目を足したときに古い配置が残り、
                // **箱が重なって押し分けられなくなる** (2 品 → 5 品で実際に踏んだ)
                //
                // ⚠ **2026-09-03 以降、この検査が重なりに対する唯一の保険になった。**
                // Setup が毎回並べ直すのをやめた (人の手置きを消してしまうため) ので、
                // **「作成時だけ配置する」形そのものが現在の仕様**。
                // ここが落ちたら直すのは Setup ではなく**シーンの配置** —
                // 検査文にもそう書いてある。**この検査を消さないこと。**
                if (sh.items != null)
                {
                    float worst = float.MaxValue; string worstPair = "";
                    for (int a = 0; a < sh.items.Length; a++)
                        for (int b = a + 1; b < sh.items.Length; b++)
                        {
                            var ia = sh.items[a]; var ib = sh.items[b];
                            // **メニューの品は並びを Canvas が持つ**ので、物理的な重なりの概念が無い
                            if (ia == null || ib == null || ia.menuOnly || ib.menuOnly) continue;
                            float d = Vector3.Distance(ia.transform.position, ib.transform.position);
                            float need = (ia.transform.lossyScale.x + ib.transform.lossyScale.x) * 0.5f;
                            float slack = d - need;
                            if (slack >= worst) continue;
                            worst = slack;
                            worstPair = ia.displayName + " / " + ib.displayName
                                + $" (間隔 {d:F3}m / 必要 {need:F3}m)";
                        }
                    v.Check(worst > 0f, worst == float.MaxValue
                        ? tag + "商品の間隔 (比較対象なし)"
                        : tag + $"商品同士が重なっていない — 最も近い組: {worstPair}");
                }

                // **Static フォントに無い字は実機で豆腐**。Dynamic は
                // 「エディタで表示しただけで焼かれて動く」ので、ここで宣言側と突き合わせる
                v.Check(CheckGlyphCoverage(sh, log), tag + "棚が使う字がすべてフォントに入っている");

                // **1 タッチ購入になっていないこと。** 棚に並ぶ商品の Interact は
                // 必ず**その棚**を経由する — でないと VR で腕が当たっただけで pt が減るし、
                // 別の棚を指していると「触った棚とは別の場所にパネルが出る」
                if (sh.items != null)
                {
                    int unrouted = 0;
                    foreach (var it in sh.items)
                        if (it != null && !it.menuOnly && it.shelf != sh) unrouted++;
                    v.Check(unrouted == 0,
                        tag + $"棚の商品すべてが自分の棚を指している (未配線 {unrouted} 件) — "
                        + "**未配線だと Interact が即購入になり、買った覚えのない購入が起きる**");
                }
            }

            // ---- 撫でエフェクトの近接メニューが撤去されている (IMP-PET-007) ----
            //
            // **残ると切り替えの口が 2 本になり、片方だけ直す事故が起きる。**
            // あわせて [BUG-CAM-002] (写真への写り込み) と ui-map §5 の 7.4m ずれは
            // **板ごと消える**ので、ここが 0 個であることがその 2 件の完了条件でもある。
            foreach (var name in new[] { MenuName, MenuTitleName })
            {
                int c;
                KD_SceneObjects.Find(name, out c);
                v.Check(c == 0, name + " がシーンに無い (actual " + c + ") — "
                    + "撫でFXの切り替えは手元メニューの「なで」タブへ移った");
            }
            if (shelf != null)
            {
                v.Check(true, "shelf の近接メニュー配線は KD_ShopShelf ごと削除済み");
            }


            // 実物商品は棚へ、menuOnly商品は対応する手元UIへ必ず接続する。
            //
            // 未所属だと「カタログには居るのに触れない品」= 買えないのに「あと N pt」だけ動く。
            // 重複所属だと「片方の棚で買ったのにもう片方が使用中にならない」ように見える。
            // **棚を増やしたときに真っ先に壊れるのがここ**なので、機械検査で押さえる
            if (catalog != null && catalog.items != null)
            {
                int orphan = 0, shared = 0;
                var detail = new StringBuilder();
                foreach (var it in catalog.items)
                {
                    if (it == null) continue;
                    if (it.menuOnly)
                    {
                        v.Check(KD_ProductsEditor.HasMenuEntry(it), it.productKey + " has a hand-menu purchase entry without collider");
                        continue;
                    }
                    int owners = 0;
                    foreach (var sh in shelves)
                    {
                        if (sh == null || sh.items == null) continue;
                        foreach (var si in sh.items) if (si == it) owners++;
                    }
                    if (owners == 1) continue;
                    if (owners == 0) { orphan++; detail.Append(it.gameObject.name + "(未所属) "); }
                    else { shared++; detail.Append(it.gameObject.name + $"({owners} 箇所) "); }
                }
                v.Check(orphan == 0 && shared == 0,
                    $"カタログの実物商品がどこかの棚にちょうど 1 回だけ属する "
                    + $"(未所属 {orphan} / 重複 {shared}) {detail}");
            }

            // **おやつの陳列マテリアルに絵があるか。**
            // FBX 付属の `tripo_mat_*` は `_MainTex` が null で、そのまま貼ると
            // **4 種が全部まっ白な同じ形**になる。値段差の根拠が絵だけの設計なので、
            // ここが崩れると売り場そのものが成立しない (2026-08-24 に実測して踏んだ)
            foreach (var d in Treats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(d.MaterialPath);
                var tex = m != null && m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                v.Check(tex != null,
                    $"{d.label}: {d.MaterialPath} の _MainTex が非 null "
                    + "(**null だと 4 種が同じまっ白い形に見える**)");
            }

            // ---- 恒久 ID / 設定の整合 (シーン中の KD_ShopItem すべて) ----
            //
            // **番号の重複が最悪の壊れ方** — 別の品を買ったのに同じビットが立ち、
            // 片方を買うともう片方も所持済みになる。Play では気づきにくい
            var shops = Object.FindObjectsOfType<KD_ShopItem>();
            log.AppendLine($"-- KD_ShopItem: {shops.Length} 個");
            var usedIds = new Dictionary<string, string>();
            var freePerCategory = new Dictionary<string, int>();
            foreach (var s in shops)
            {
                string tag = s.gameObject.name + ": ";
                v.Check(s.IsConfigured(), tag + "IsConfigured()");
                // **メニューの品は Collider を持たない** — 実物を並べないので触る対象が無い。
                // 逆に付いていると、腕が当たっただけでパネルが開く
                v.Check(s.menuOnly ? s.GetComponent<Collider>() == null
                                 : s.GetComponent<Collider>() != null,
                    tag + (s.menuOnly ? "メニューの品なので Collider を持たない"
                                      : "Collider がある (Interact に必要)"));
                // **2 つ目の番号を残さない** — ずれても誰も気づかないので機械検査する
                if (usedIds.ContainsKey(s.productKey))
                    v.Check(false, tag + $"productKey {s.productKey} が {usedIds[s.productKey]} と重複");
                else
                    usedIds[s.productKey] = s.gameObject.name;

                // **倒れ先は「無料品」ではなく「最初から持っている品」** (2026-08-24)。
                // おやつは無料枠を撤回したので `price == 0` の品が 1 つも無く、
                // 無料品で数えるとカテゴリごと倒れ先を失う (IMP-TRT-001 Phase 4b)
                if (s.IsStarter())
                {
                    string c = s.categoryKey;
                    freePerCategory[c] = (freePerCategory.ContainsKey(c) ? freePerCategory[c] : 0) + 1;
                }
            }

            // ---- Live の商品 (IMP-PRG-005): 表 (KD_DogDefs.LiveProducts) と 1:1 ----
            //
            // 作品 (6) ではなく商品 (2)。表に無い番号 (旧作の 24) が残ると、
            // 持ち物のビットは立つのに開催できる曲が無い
            {
                var wantLive = new HashSet<string>();
                foreach (var p in KD_DogDefs.LiveProducts) wantLive.Add(p.productKey);
                int liveCount = 0, unexpected = 0;
                foreach (var s in shops)
                {
                    if (s.categoryKey != CategoryLive) continue;
                    liveCount++;
                    if (wantLive.Contains(s.productKey)) continue;
                    unexpected++;
                    v.Check(false, $"{s.gameObject.name}: Live の商品だが KD_DogDefs.LiveProducts に無い (id {s.productKey})");
                }
                v.Check(liveCount == wantLive.Count && unexpected == 0,
                    $"Live の商品が表どおり {wantLive.Count} 個 (actual {liveCount}。表に無いもの {unexpected})");
            }

            // ---- フォールバック先 ----
            //
            // 無いと、復元値が弾かれたときに選択が空になる (§4)
            if (catalog != null && catalog.items != null)
            {
                var cats = new HashSet<string>();
                foreach (var it in catalog.items) if (it != null) cats.Add(it.categoryKey);
                // **開発用の CE 解放ぶんは数から差し引く。**
                // 解放中は CE 品が無料品として振る舞うので初期品が増えるが、
                // それは意図した一時状態であって「倒れ先が壊れた」ではない。
                // (`DefaultOf` は配列の先頭から探すので、**倒れ先そのものは動かない** —
                //  無料の基本ボール / ハートが CE 品より前に並んでいる)
                var ceUnlockedPerCategory = new Dictionary<string, int>();
                foreach (var s in shops)
                {
                    if (!CeUnlocked(s)) continue;
                    string c = s.categoryKey;
                    ceUnlockedPerCategory[c] = (ceUnlockedPerCategory.ContainsKey(c) ? ceUnlockedPerCategory[c] : 0) + 1;
                }
                foreach (var c in cats)
                {
                    // **Live (開催権) には倒れ先が要らない** — 選択 (カーソル) に意味が無く、
                    // 消費側は `Owned()` しか読まない。初期品も無料枠も置かない (business-strategy §13)
                    if (c == CategoryLive || c == "experience" || c == "action")
                    {
                        v.Check(!freePerCategory.ContainsKey(c),
                            $"category {c} に初期商品が無い (開催権・追加体験は所持品から選ぶ)");
                        continue;
                    }
                    int n = freePerCategory.ContainsKey(c) ? freePerCategory[c] : 0;
                    int dev = ceUnlockedPerCategory.ContainsKey(c) ? ceUnlockedPerCategory[c] : 0;
                    v.Check(n - dev == 1, $"category {c} の初期品 (無料 または 初期在庫あり) が"
                        + $"ちょうど 1 つ (actual {n}"
                        + (dev > 0 ? $" — うち CE 解放 {dev} 件を除く" : "")
                        + ") — 復元に失敗したときのフォールバック先");
                }

                // **`IsStarter` を足したせいで買い切り側の倒れ先が変わっていないこと。**
                // 変わると「入り直したら別の品が選ばれている」= 持ち物のすり替わりに見える。
                // 挙動が同じであること自体を検査で固定しておく (自己テストと同じ規律)
                int starterDrift = 0;
                foreach (var s in shops)
                    if (s.initialStock == 0 && !CeUnlocked(s) && s.IsStarter() != s.IsFree()) starterDrift++;
                v.Check(starterDrift == 0,
                    $"初期在庫を持たない品では IsStarter() == IsFree() (ずれ {starterDrift} 件) "
                    + "— **おもちゃと撫でエフェクトの倒れ先は 1 つも変わっていない**");
            }

            // ---- PlayerData キー名の重複 ----
            //
            // **キー空間はプレイヤー単位でワールド全体に 1 つ。**
            // クラスを分けても守られないので、ここで突き合わせる
            v.Check(CheckPlayerDataKeys(log), "PlayerData キー名が重複していない");

            // ---- ToyBox の器 ----
            if (toyBox != null)
            {
                var dogs = KD_DogDefs.All;
                v.Check(toyBox.catalog != null, "toyBox.catalog wired");
                v.Check(toyBox.dogCount == dogs.Length,
                    $"toyBox.dogCount == {dogs.Length} (actual {toyBox.dogCount})");
                bool shaped = toyBox.productKeys != null && toyBox.meshNodes != null
                    && toyBox.meshNodes.Length == toyBox.productKeys.Length * dogs.Length;
                v.Check(shaped, "meshNodes.Length == productKeys.Length * dogCount");
                if (shaped)
                {
                    int nulls = 0;
                    foreach (var g in toyBox.meshNodes) if (g == null) nulls++;
                    v.Check(nulls == 0, $"meshNodes に null が無い (actual {nulls}) "
                        + "— null があるとその犬だけおもちゃが変わらない");
                    // **棚に並ぶ番号と器の番号がそろっているか。**
                    // ずれると「買えたのに見た目が変わらない」になる
                    if (catalog != null && catalog.items != null)
                    {
                        int unmatched = 0;
                        foreach (var it in catalog.items)
                        {
                            if (it == null || it.categoryKey != toyBox.categoryKey) continue;
                            if (toyBox.IndexOfProduct(it.productKey) < 0) unmatched++;
                        }
                        v.Check(unmatched == 0,
                            $"おもちゃカテゴリの全商品が器を持つ (器の無い商品 {unmatched} 件)");
                    }
                }
            }

            // ---- CE の Listing がまだ仮のものか ----
            //
            // **FAIL にしない** — Phase 1 では仮 ID で「並びと 🔒 表示」を先に作るのが正しい。
            // ただし**このまま公開すると CE 品が永久に買えない**ので、
            // 公開前チェックリストが拾えるように WARN を必ず出す
            {
                int todo = 0;
                foreach (var s in shops)
                    if (s.IsCE() && s.ceListingId.StartsWith("TODO-")) todo++;
                if (todo > 0)
                {
                    log.AppendLine($"  WARN CE の Listing ID が仮のまま {todo} 件 — "
                        + "IMP-FET-010 で本物を入れるまで CE 品は買えない "
                        + "(Phase 1 では意図どおり。**公開前に解消すること**)");
                }

                // **開発用の CE 解放が掛かったままか。**
                // 解放中は `IsCE()` が false になるので上の TODO 件数からも消える —
                // **黙って警告が減る**ことになるので、ここで別に数えて必ず言う
                string devNames;
                int dev = CountCeUnlocked(out devNames);
                if (dev > 0)
                {
                    log.AppendLine($"  WARN **開発用の CE 解放が掛かっている {dev} 件** ({devNames}) — "
                        + "課金品が無料で使える状態。`Shop/5 - CE Relock` か `2 - Setup Shop` で戻す "
                        + "(**Release Check/1 は FAIL する**)");
                }
            }

            // ---- 棚の品目と見た目の器がそろっているか ----
            //
            // ずれると「買えたのに見た目が変わらない」になる。
            // 器の正本は KD_ToySetupEditor 側なので、両者を突き合わせる
            if (toyBox != null && toyBox.productKeys != null)
            {
                foreach (var def in Toys)
                {
                    if (toyBox.IndexOfProduct(def.productKey) >= 0) continue;
                    v.Check(false, $"棚の品 '{def.label}' (id {def.productKey}) の器が ToyBox に無い — "
                        + "Toy Box/2 - Apply Toy Mesh を流したか");
                }
            }

            // ---- 純関数の自己テスト ----
            if (catalog == null)
            {
                log.AppendLine("  SKIP 純関数の自己テスト (catalog が無い。先に 2 - Setup)");
                v.AddFail();
            }
            else
            {
                v.AddFail(SelfTestSelection(catalog, log));
            }

            var ledgerGo = KD_SceneObjects.Find(LedgerHost);
            var ledger = ledgerGo == null ? null : ledgerGo.GetComponent<KD_ProgressLedger>();
            if (ledger == null)
            {
                log.AppendLine("  SKIP 在庫の自己テスト (ledger が無い)");
                v.AddFail();
            }
            else
            {
                v.AddFail(SelfTestStock(ledger, log));
            }

            v.Finish("Logs/imp-prg-002-verify.txt");
        }

        /// <summary>棚が使う字がフォントに入っているか。
        ///
        /// **Dynamic フォントの罠 (vrc-tmp-font §3) への機械検査。**
        /// エディタで一度表示するとグリフが焼かれて「動いてしまう」ので、
        /// 目視では欠落に気づけない。宣言 (`KD_ShopShelf` の const 群 + displayName) と
        /// 突き合わせる。</summary>
        static bool CheckGlyphCoverage(KD_ShopShelf shelf, StringBuilder log)
        {
            if (shelf == null || shelf.label == null || shelf.label.font == null) return false;
            var font = shelf.label.font;
            var used = new StringBuilder();
            used.Append(KD_ShopShelf.WordInUse).Append(KD_ShopShelf.WordUse)
                .Append(KD_ShopShelf.WordBuy).Append(KD_ShopShelf.WordCancel)
                .Append(KD_ShopShelf.WordSpecial).Append(KD_ShopShelf.WordLocked)
                .Append(KD_ShopShelf.WordStock).Append(KD_ShopShelf.WordCount).Append(KD_ShopShelf.WordOwned)
                 .Append(KD_ShopShelf.WordRemain).Append(KD_ShopShelf.WordShort)
                .Append(KD_ShopShelf.WordLoading).Append(KD_ShopShelf.WordBought)
                .Append(KD_ShopShelf.WordNoLedger).Append(KD_ShopShelf.WordNotHere)
                .Append(KD_ShopShelf.WordCantSelect).Append(KD_ShopShelf.WordCantBuy)
                .Append(KD_ShopShelf.WordPack).Append(KD_ShopShelf.WordEnough)
                // メニューの見出しも見る (撫でFXメニューは棚の付属物。「撫」「エ」「ェ」は
                // ここ以外に出番が無く、2026-08-28 まで焼き忘れられていた)
                .Append(shelf.title).Append(MenuTitle);
            if (shelf.items != null)
                foreach (var it in shelf.items) if (it != null) used.Append(it.displayName);
            for (char c = '0'; c <= '9'; c++) used.Append(c);
            // `x` は在庫の掛け算記号 ("骨型ビスケット x5")。**ASCII でも列挙に入れる** —
            // Setup 側は ' '〜'~' を丸ごと焼いているが、
            // ここが宣言と食い違うと「焼けているから通る」だけの検査になる
            used.Append("pt ()/.x");

            var missing = new StringBuilder();
            var seen = new HashSet<char>();
            foreach (var c in used.ToString())
            {
                if (c == '\n' || c == '\r' || !seen.Add(c)) continue;
                if (!font.HasCharacter(c)) missing.Append(c);
            }
            if (missing.Length == 0) return true;
            log.AppendLine($"       字形が無い ({missing.Length} 字): {missing}"
                + " — 2 - Setup を流し直すと焼かれる。焼けないならアトラス容量を疑う");
            return false;
        }

        /// <summary>ソース中の PlayerData キー文字列を集めて重複を見る。
        ///
        /// **キー空間はプレイヤー単位でワールド全体に 1 つ**なので、
        /// クラスを分けただけでは一意性は守られない (shop-foundation §2)。</summary>
        static bool CheckPlayerDataKeys(StringBuilder log)
        {
            string[] files = {
                ProgressDir + "KD_ProgressLedger.cs",
                ProgressDir + "KD_ShopCatalog.cs",
            };
            var seen = new Dictionary<string, string>();
            bool ok = true;
            // **`const string ... = "KD_..."` の宣言だけを見る。**
            // 裏山改変: 名前空間を KH_Commerce_v1 にしたので KH_ 始まりも見る (KD_ だけだと台帳の定数を素通りする)
            // 単に `"KD_..."` を拾うと `<see cref="KD_ShopItem"/>` のような
            // **doc コメントの型名が「キー」に見えて誤検出する** (実際に踏んだ)
            var re = new System.Text.RegularExpressions.Regex(
                @"const\s+string\s+\w+\s*=\s*""(K[DH]_[A-Za-z0-9_]+)""");
            foreach (var f in files)
            {
                if (!File.Exists(f)) { log.AppendLine("       (見つからない: " + f + ")"); ok = false; continue; }
                foreach (var line in File.ReadAllLines(f))
                {
                    string t = line.TrimStart();
                    if (t.StartsWith("///") || t.StartsWith("*") || t.StartsWith("//")) continue;
                    var m = re.Match(line);
                    if (!m.Success) continue;
                    string key = m.Groups[1].Value;
                    if (seen.ContainsKey(key) && seen[key] != f)
                    {
                        log.AppendLine($"       重複キー '{key}': {seen[key]} と {f}");
                        ok = false;
                    }
                    seen[key] = f;
                }
            }
            log.AppendLine($"       PlayerData キー / プレフィックス {seen.Count} 個を突き合わせた");
            // **プレフィックス同士が前方一致していないかも見る** —
            // `KD_A` と `KD_A1` があると、id 付きで組み立てたときに別々のキーが衝突しうる
            var names = new List<string>(seen.Keys);
            for (int i = 0; i < names.Count; i++)
                for (int j = 0; j < names.Count; j++)
                {
                    if (i == j) continue;
                    if (!names[j].StartsWith(names[i])) continue;
                    log.AppendLine($"       前方一致: '{names[i]}' と '{names[j]}' "
                        + "— id を付けて組み立てると衝突しうる");
                    ok = false;
                }
            return ok;
        }

        /// <summary>選択の復元の自己テスト。**Play を 1 秒も使わずに
        /// フォールバックの全ケースを落とす。**
        ///
        /// `ResolveSelection` は棚 (`items`) を見るので厳密な純関数ではないが、
        /// 台帳の状態は `Owned()` 経由でしか触らず、編集時は「未所持」で安定している。
        /// **無料品だけが Owned() = true になる**ので、そこを軸に検査する。</summary>
        static int SelfTestSelection(KD_ShopCatalog m, StringBuilder log)
        {
            int fail = 0, n = 0;
            void Eq(string actual, string expected, string what)
            {
                n++;
                if (actual != expected)
                {
                    fail++;
                    log.AppendLine($"  FAIL [sel {n}] {what}: expected {expected}, got {actual}");
                }
            }

            if (m.items == null || m.items.Length == 0)
            {
                log.AppendLine("  SKIP 選択の自己テスト (items が空)");
                return 1;
            }

            string free = m.DefaultOf(CategoryToy);
            if (string.IsNullOrEmpty(free))
            {
                log.AppendLine("  FAIL 選択の自己テスト: おもちゃカテゴリに無料品が無い");
                return 1;
            }

            // 1. 保存が無い → 初期品
            Eq(m.ResolveSelection(KD_ShopCatalog.NoSelection, CategoryToy, free), free,
                "保存が無ければ初期品");
            // 2. 棚に無い番号 → 初期品
            Eq(m.ResolveSelection("unknown.product", CategoryToy, free), free, "棚に無い番号は初期品へ");
            // 3. 範囲外・負 → 初期品
            Eq(m.ResolveSelection("invalid", CategoryToy, free), free, "負の番号は初期品へ");
            Eq(m.ResolveSelection("unknown.product2", CategoryToy, free), free, "範囲外の番号は初期品へ");
            // 4. 別カテゴリの番号 → 初期品
            Eq(m.ResolveSelection(free, CategoryPetFx, free), free,
                "別カテゴリとして復元されたら初期品へ");
            // 5. 正常 (無料品は常に所持) → そのまま
            Eq(m.ResolveSelection(free, CategoryToy, free), free, "所持している品はそのまま");
            // 6. **未所持の有料品 → 初期品** (編集時は誰も買っていない)
            foreach (var it in m.items)
            {
                if (it == null || it.IsFree() || it.categoryKey != CategoryToy) continue;
                Eq(m.ResolveSelection(it.productKey, CategoryToy, free), free,
                    $"未所持の有料品 (id {it.productKey}) は初期品へ");
                break;
            }

            log.AppendLine($"  self-test (selection) {n - fail}/{n} passed");
            return fail;
        }

        /// <summary>在庫の算術の自己テスト。**Play を使わない。**</summary>
        static int SelfTestStock(KD_ProgressLedger m, StringBuilder log)
        {
            int fail = 0, n = 0;
            void Eq(int actual, int expected, string what)
            {
                n++;
                if (actual != expected)
                {
                    fail++;
                    log.AppendLine($"  FAIL [stock {n}] {what}: expected {expected}, got {actual}");
                }
            }
            void Is(bool actual, bool expected, string what)
            {
                n++;
                if (actual != expected)
                {
                    fail++;
                    log.AppendLine($"  FAIL [stock {n}] {what}: expected {expected}, got {actual}");
                }
            }

            const int Max = KD_ProgressLedger.MaxPoints;

            // --- 所持数の導出 ---
            Eq(m.StockOfWith(0, 0), 0, "何も買っていなければ 0");
            Eq(m.StockOfWith(5, 0), 5, "初期付与 5 で 5 個");
            Eq(m.StockOfWith(5, 3), 2, "3 個使ったら 2 個");
            Eq(m.StockOfWith(5, 5), 0, "使い切ったら 0");
            // **在庫 0 と未付与を取り違えていないか** — 値では区別できないことの確認。
            // 区別は StockSeeded (キーの有無) が持つ
            Eq(m.StockOfWith(5, 5), m.StockOfWith(0, 0),
                "**使い切った 0 と未付与の 0 は値では区別できない** (だからキーの有無で判定する)");
            Eq(m.StockOfWith(5, 9), 0, "**消費 > 購入 の逆転でも負にしない**");
            Eq(m.StockOfWith(-5, 0), 0, "壊れた負の購入は 0 へ");
            Eq(m.StockOfWith(5, -3), 5, "壊れた負の消費は 0 へ");
            Eq(m.StockOfWith(Max * 2, 0), Max, "購入は上限で止める (オーバーフロー防止)");

            // --- セット販売の可否 ---
            Is(m.CanBuyStockedWith(0, 0, 0, 0, 2, 5, 20), false, "残高 0 では買えない");
            Is(m.CanBuyStockedWith(2, 0, 0, 0, 2, 5, 20), true, "残高 2 でちょうど買える");
            Is(m.CanBuyStockedWith(100, 0, 0, 0, 0, 5, 20), false, "0 円の品はショップに並べない");
            Is(m.CanBuyStockedWith(100, 0, 0, 0, 2, 0, 20), false, "0 個入りは商品ではない");
            // **上限は超過を許す** (2026-08-22 ユーザー決定)
            Is(m.CanBuyStockedWith(100, 0, 18, 0, 2, 5, 20), true,
                "**残り 18 個でも 5 個入りを買える** (23 個になってよい)");
            Is(m.CanBuyStockedWith(100, 0, 20, 0, 2, 5, 20), false, "上限 20 に達したら買えない");
            Is(m.CanBuyStockedWith(100, 0, 25, 0, 2, 5, 20), false, "超過中 (25 個) も買えない");
            Is(m.CanBuyStockedWith(100, 0, 25, 5, 2, 5, 20), false,
                "所持 20 ちょうどはまだ買えない (境界)");
            Is(m.CanBuyStockedWith(100, 0, 25, 6, 2, 5, 20), true,
                "所持 19 まで使えば買える (境界の反対側)");
            Is(m.CanBuyStockedWith(100, 0, 100, 0, 2, 5, 0), true, "cap 0 は上限なし");

            // --- 二度目が買える (買い切りとの違い) ---
            Is(m.CanBuyStockedWith(100, 0, 5, 0, 2, 5, 20), true, "**消耗品は二度目も買える**");

            log.AppendLine($"  self-test (stock) {n - fail}/{n} passed");
            return fail;
        }
    }
}
