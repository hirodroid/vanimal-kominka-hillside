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
    /// <summary>IMP-PRG-002 ショップ: 2 - Setup (棚・商品・おやつ棚の構築)。本体は KD_ShopSetupEditor.cs。</summary>
    public static partial class KD_ShopSetupEditor
    {
        // ================================================================ 2: Setup

        // 裏山改変 (IMP-PRG-001 段階 1): 古民家版 (棚・ToyBox・確認パネル・効果音を作る) は Imported-Unused へ。
        // 裏山の Menu/4 Setup Shop は KD_ShopSetupEditor.Hillside.cs の SetupHillside (カタログと商品だけ)
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Shop/2 - Setup Shop (Kominka)")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ShopSetup] Setup");

            // ---- 台帳 ----
            var ledgerGo = KD_SceneObjects.Find(LedgerHost);
            var ledger = ledgerGo == null ? null : ledgerGo.GetComponent<KD_ProgressLedger>();
            if (ledger == null)
            {
                log.AppendLine("FAIL " + LedgerHost + " に KD_ProgressLedger が無い。"
                    + "先に Progress System/2 - Setup を流すこと");
                KD_VerifyLog.Write("Logs/imp-prg-002-setup.txt", log);
                return;
            }

            // ---- カタログ / ToyBox / 陳列 ----
            //
            // **Setup = 作るだけ。** 既にあるものの親も位置も触らない
            // (置き場所の是正は Scene Layout/2 - Apply の専任)
            var catalogGo = EnsureHost(CatalogName, KD_DogDefs.SystemsPath, log);
            var toyBoxGo = EnsureHost(ToyBoxName, KD_DogDefs.SystemsPath, log);
            var shelfGo = EnsureShelf(log);
            // **2 台目の陳列。カタログは 1 個のまま** (shop-foundation §1)
            var treatShelfGo = EnsureTreatShelf(shelfGo, log);
            if (catalogGo == null || toyBoxGo == null || shelfGo == null || treatShelfGo == null)
            {
                KD_VerifyLog.Write("Logs/imp-prg-002-setup.txt", log);
                return;
            }

            var catalog = EnsureU<KD_ShopCatalog>(catalogGo, log);
            var toyBox = EnsureU<KD_ToyBox>(toyBoxGo, log);
            if (catalog == null || toyBox == null)
            {
                log.AppendLine("SETUP RESULT: FAIL (force refresh 後に再実行)");
                KD_VerifyLog.Write("Logs/imp-prg-002-setup.txt", log);
                return;
            }

            SetSync(catalog, VRC.SDKBase.Networking.SyncType.None, log);
            // **ToyBox は Manual + 専用 GameObject。** 同期変数 1 本を後から足すと
            // 器ごと作り直しになるので、最初からこの形で作る
            SetSync(toyBox, VRC.SDKBase.Networking.SyncType.Manual, log);

            // ---- 商品を陳列の下へ ----
            var defs = Toys;
            var items = new List<KD_ShopItem>();
            for (int i = 0; i < defs.Length; i++)
            {
                var item = EnsureShopItem(shelfGo.transform, defs[i], ledger, i, defs.Length, log);
                if (item != null) items.Add(item);
            }

            // ---- 撫でエフェクト (メニュー形式。実物が無いので行で並べる) ----
            var fxItems = EnsureEffectItems(shelfGo.transform, ledger, log);
            items.AddRange(fxItems);

            // ---- ParticleLive の商品 (IMP-PRG-004 Phase 3 / IMP-PRG-005: 単品 + Pack の 2 商品。メニュー形式) ----
            var liveItems = EnsureLiveItems(shelfGo.transform, ledger, log);
            items.AddRange(liveItems);

            // **定義から消えた商品を撤去する** (2026-08-23 Codex 指摘)。
            // 足すだけだと、品目を減らして Setup を流し直したときに
            // **ゴースト商品が棚に残る** — 実物は並んでいるのに触ると
            // 「この棚の品ではない」と断られる、いちばん読めない壊れ方になる。
            // **Setup は「今の定義へ収束する」こと**が冪等性の意味
            {
                var wanted = new HashSet<string>();
                foreach (var d in defs) wanted.Add(ShopItemName(d));
                foreach (var it in fxItems) wanted.Add(it.gameObject.name);
                foreach (var it in liveItems) wanted.Add(it.gameObject.name);
                for (int i = shelfGo.transform.childCount - 1; i >= 0; i--)
                {
                    var c = shelfGo.transform.GetChild(i);
                    if (!c.name.StartsWith(ShopItemPrefix) || wanted.Contains(c.name)) continue;
                    log.AppendLine("removed 定義に無い商品: " + c.name);
                    Object.DestroyImmediate(c.gameObject);
                }
            }

            // ---- おやつを 2 台目の棚へ (IMP-TRT-001 Phase 4b) ----
            //
            // **おもちゃ棚に混ぜない。** 買い切り (「あと N pt」) と
            // 消耗品 (「のこり N 個」) が同じ面に並ぶと、どちらの数字なのか読めない
            var treatDefs = Treats;
            var treatItems = new List<KD_ShopItem>();
            for (int i = 0; i < treatDefs.Length; i++)
            {
                var item = EnsureTreatItem(treatShelfGo.transform, treatDefs[i], ledger,
                    i, treatDefs.Length, log);
                if (item != null) treatItems.Add(item);
            }
            {
                var wanted = new HashSet<string>();
                foreach (var d in treatDefs) wanted.Add(TreatItemName(d));
                for (int i = treatShelfGo.transform.childCount - 1; i >= 0; i--)
                {
                    var c = treatShelfGo.transform.GetChild(i);
                    if (!c.name.StartsWith(ShopItemTreatPrefix) || wanted.Contains(c.name)) continue;
                    log.AppendLine("removed 定義に無いおやつ: " + c.name);
                    Object.DestroyImmediate(c.gameObject);
                }
            }

            // **カタログは全棚の商品を持つ。** 選択の正本が 1 つであることの実体がこれ
            var allItems = new List<KD_ShopItem>(items);
            allItems.AddRange(treatItems);

            catalog.ledger = ledger;
            catalog.items = allItems.ToArray();
            KD_ProductsEditor.BindCatalog(catalog, ledger);
            catalog.consumers = new UdonSharpBehaviour[] { toyBox };
            catalog.consumerEvents = new[] { "_OnShopSelectionChanged" };

            // ---- 棚の操作と表示 ----
            //
            // **ここが無いと Play で体験を判定できない** — 買えたことが伝わらず、
            // しかも 1 タッチで確定してしまう (shop-foundation §7)
            EnsureShopGlyphs(allItems, log);
            var shelf = EnsureU<KD_ShopShelf>(shelfGo, log);
            if (shelf == null)
            {
                log.AppendLine("SETUP RESULT: FAIL (force refresh 後に再実行)");
                KD_VerifyLog.Write("Logs/imp-prg-002-setup.txt", log);
                return;
            }
            SetSync(shelf, VRC.SDKBase.Networking.SyncType.None, log);
            shelf.catalog = catalog;
            shelf.items = items.ToArray();
            shelf.toyBox = toyBox;
            shelf.title = ShelfTitle;
            shelf.titleKey = "ui.extra.023";
            shelf.label = EnsureLabel(shelfGo.transform, ShelfLabelPos, ShelfFaceYaw, log);
            shelf.sfx = EnsureAudio(shelfGo, log);
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontAssetPath);
            EnsureConfirmPanel(shelfGo, shelf, font, ShelfFaceYaw, ShelfConfirmHeight, log);

            // ---- 撫でエフェクトのメニューと消費側 ----
            var fxBoxGo = EnsureHost(PetFxBoxName, KD_DogDefs.SystemsPath, log);
            var fxBox = fxBoxGo == null ? null : EnsureU<KD_PetFxBox>(fxBoxGo, log);
            if (fxBox != null)
            {
                SetSync(fxBox, VRC.SDKBase.Networking.SyncType.None, log);
                fxBox.catalog = catalog;
                fxBox.categoryKey = CategoryPetFx;
                fxBox.defaultProductKey = fxItems.Count > 0 ? fxItems[0].productKey : "petfx.heart";
                var pcs = new List<KD_DogPetController>();
                foreach (var d in KD_DogDefs.All)
                {
                    var pg = KD_SceneObjects.Find(d.PetSystemName);
                    var pc = pg == null ? null : pg.GetComponent<KD_DogPetController>();
                    if (pc == null) log.AppendLine("WARN " + d.PetSystemName + " の controller が無い");
                    else pcs.Add(pc);
                }
                fxBox.petControllers = pcs.ToArray();
                // Phase 3: 撫で主に合わせて差し替える解決役を各犬へ配る
                foreach (var pc in pcs)
                {
                    pc.petFxBox = fxBox;
                    TryCopy(pc, log);
                }
                log.AppendLine("wired petFxBox: " + pcs.Count + " 匹");
                EnsurePlayerFxTag(log);

                // カタログの通知先へ追加 (おもちゃと同じ口)
                catalog.consumers = new UdonSharpBehaviour[] { toyBox, fxBox };
                catalog.consumerEvents = new[] { "_OnShopSelectionChanged", "_OnShopSelectionChanged" };
            }

            // **近接メニューは撤去した** (2026-09-03。IMP-PET-007) —
            // 切り替えは手元メニューの「なで」タブへ。**棚の実物 6 個と購入導線は無改造**
            RemoveEffectMenu(shelfGo, shelf, log);
            shelf.buyClip = LoadClip(SfxBuy, log);
            shelf.selectClip = LoadClip(SfxSelect, log);
            shelf.rejectClip = LoadClip(SfxReject, log);

            // ---- おやつ棚 (2 台目) ----
            //
            // **既存のおもちゃ棚と同じ作り方にする。** `KD_ShopShelf` は
            // 自分の面と自分の確認パネルを持つ部品なので、2 台目も同じ手順で組む
            // (確認の型を揃えるのが「2 つのシステムに見せない」条件 — shop-foundation §7)
            var treatShelf = EnsureU<KD_ShopShelf>(treatShelfGo, log);
            if (treatShelf == null)
            {
                log.AppendLine("SETUP RESULT: FAIL (force refresh 後に再実行)");
                KD_VerifyLog.Write("Logs/imp-prg-002-setup.txt", log);
                return;
            }
            SetSync(treatShelf, VRC.SDKBase.Networking.SyncType.None, log);
            treatShelf.catalog = catalog;
            treatShelf.items = treatItems.ToArray();
            // **toyBox は配線しない。** おやつは共有物ではないので「世界に出ている値」が無く、
            // 「使用中 ✓」はカタログのカーソルと比べるのが正しい (shop-foundation §6 の表)
            treatShelf.toyBox = null;
            treatShelf.title = TreatShelfTitle;
            treatShelf.titleKey = "ui.extra.024";
            treatShelf.label = EnsureLabel(treatShelfGo.transform,
                TreatShelfLabelPos, TreatShelfFaceYaw, log);
            treatShelf.sfx = EnsureAudio(treatShelfGo, log);
            EnsureConfirmPanel(treatShelfGo, treatShelf, font,
                TreatShelfFaceYaw, TreatShelfConfirmHeight, log);
            treatShelf.buyClip = LoadClip(SfxBuy, log);
            treatShelf.selectClip = LoadClip(SfxSelect, log);
            treatShelf.rejectClip = LoadClip(SfxReject, log);

            // **商品の Interact を棚へ回す。** これが無いと 1 タッチ購入のまま。
            // **どちらの棚の品かで行き先が違う** — 取り違えると
            // 「触った棚とは別の棚にパネルが出る」になる
            foreach (var it in items) it.shelf = shelf;
            foreach (var it in treatItems) it.shelf = treatShelf;

            // ---- ToyBox の器を配線する ----
            //
            // **参照の向きはシーン側 → プレハブインスタンスの子だけ。**
            // 逆向き (ボールのプレハブ内 U# → シーン) は実機ビルドで null に化ける
            var dogs = KD_DogDefs.All;
            var productKeys = new string[defs.Length];
            var nodes = new GameObject[defs.Length * dogs.Length];
            int missing = 0;
            for (int i = 0; i < defs.Length; i++)
            {
                productKeys[i] = defs[i].productKey;
                for (int d = 0; d < dogs.Length; d++)
                {
                    int cnt;
                    var ball = KD_SceneObjects.Find(dogs[d].BallName, out cnt);
                    if (ball == null || cnt != 1)
                    {
                        log.AppendLine($"WARN {dogs[d].BallName} がシーンに {cnt} 個 (1 個であること)");
                        missing++;
                        continue;
                    }
                    var node = ball.transform.Find(defs[i].MeshNode);
                    if (node == null)
                    {
                        log.AppendLine($"WARN {dogs[d].BallName}/{defs[i].MeshNode} が無い "
                            + "(Toy Box/2 - Apply Toy Mesh が流れていない?)");
                        missing++;
                        continue;
                    }
                    nodes[i * dogs.Length + d] = node.gameObject;
                }
            }
            toyBox.catalog = catalog;
            toyBox.categoryKey = CategoryToy;
            toyBox.productKeys = productKeys;
            toyBox.meshNodes = nodes;
            toyBox.dogCount = dogs.Length;
            log.AppendLine($"wired toy mesh nodes: {defs.Length} 品目 × {dogs.Length} 犬 "
                + $"= {defs.Length * dogs.Length} (missing {missing})");
            KD_ToyCatalogEditor.WirePickupText(toyBox);

            bool copyOk = TryCopy(catalog, log) & TryCopy(toyBox, log) & TryCopy(shelf, log)
                & TryCopy(treatShelf, log);
            foreach (var it in allItems) copyOk &= TryCopy(it, log);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("NOTE 棚は仮置き。**見た目は Phase 5 で作り直す前提**");
            log.AppendLine("NOTE 位置の是正は Scene Layout/2 - Apply の担当 "
                + "(Setup は作るだけで、既にあるものは動かさない)");
            log.AppendLine(copyOk && missing == 0
                ? "SETUP RESULT: PASS (scene not saved yet)"
                : "SETUP RESULT: FAIL (上の WARN を見ること)");
            KD_VerifyLog.Write("Logs/imp-prg-002-setup.txt", log);
        }

        /// <summary>位置に意味の無いホストを 1 個用意する。</summary>
        static GameObject EnsureHost(string name, string containerPath, StringBuilder log)
        {
            int found;
            var go = KD_SceneObjects.Find(name, out found);
            if (found > 1)
            {
                log.AppendLine($"FAIL '{name}' がシーン内に {found} 個ある。1 個に整理してから再実行");
                return null;
            }
            if (go == null)
            {
                go = new GameObject(name);
                KD_SceneObjects.PlaceIfNew(go, containerPath);
                log.AppendLine("created " + name);
            }
            else
            {
                log.AppendLine("reused " + name);
            }
            return go;
        }

        /// <summary>陳列を用意する。**位置に意味があるので、既にあれば動かさない。**</summary>
        static GameObject EnsureShelf(StringBuilder log)
        {
            int found;
            var go = KD_SceneObjects.Find(ShelfName, out found);
            if (found > 1)
            {
                log.AppendLine($"FAIL '{ShelfName}' がシーン内に {found} 個ある。1 個に整理してから再実行");
                return null;
            }
            if (go != null)
            {
                log.AppendLine("reused " + ShelfName + " at "
                    + go.transform.position.ToString("F2") + " (位置は動かさない)");
                return go;
            }

            go = new GameObject(ShelfName);
            KD_SceneObjects.PlaceIfNew(go, KD_DogDefs.PlacesPath);

            // **仮置きの初期位置はコーギーのボールの脇。**
            // 「買う → 選ぶ → その場で投げる」が切れない場所という要件だけを満たす
            // (見た目も最終的な場所も Phase 5 / Play で決める)
            int cnt;
            var refBall = KD_SceneObjects.Find(KD_DogDefs.All[0].BallName, out cnt);
            Vector3 basePos = (refBall != null && cnt == 1)
                ? refBall.transform.position + new Vector3(0.6f, 0f, 0f)
                : new Vector3(0f, 0f, 0f);
            basePos.y = Mathf.Max(basePos.y, 0f) + 0.9f;   // 腰の高さ (Interact しやすい)
            go.transform.position = basePos;
            log.AppendLine("created " + ShelfName + " at " + basePos.ToString("F2")
                + " (仮置き。Play で場所を決める)");
            return go;
        }

        // ================================================================
        // 品ぞろえを外へ晒す口 (Phase 4b Step 4)
        //
        // **正本は <see cref="Treats"/> のまま。** おやつの実体 (`KD_Treat_{i}`) も
        // 棚と同じ 4 種のメッシュを持つが、**表を 2 つに割らない** —
        // 割ると「棚に並んでいる絵」と「実際に出てくる絵」が黙ってずれる。
        // ================================================================

        // ---- 撫でエフェクトの品目 (IMP-PET-007 で手元メニューも引く) ----
        //
        // **命名の正本をここ 1 箇所に持つ。** 品ぞろえそのものの正本は
        // `KD_PetHeartsSetupEditor.GetShopEntries` で、ここはシーン上の名前だけを決める。

        /// <summary>撫でエフェクトの `KD_ShopItem` の名前 (`KD_ShopItem_Fx_{id}`)。</summary>
        public static string PetFxItemName(string productKey)
        {
            return KD_ProductDefinitions.SceneName(productKey);
        }

        /// <summary>撫でエフェクトの品目数。</summary>
        public static int PetFxProductCount
        {
            get
            {
                string[] ids; string[] labels; int[] prices; bool[] ce; string[] tex;
                KD_PetHeartsSetupEditor.GetShopEntries(out ids, out labels, out prices, out ce, out tex);
                return ids.Length;
            }
        }

        /// <summary>i 番目の撫でエフェクトの `KD_ShopItem` 名。</summary>
        public static string PetFxItemNameAt(int i)
        {
            string[] ids; string[] labels; int[] prices; bool[] ce; string[] tex;
            KD_PetHeartsSetupEditor.GetShopEntries(out ids, out labels, out prices, out ce, out tex);
            return (i >= 0 && i < ids.Length) ? PetFxItemName(ids[i]) : null;
        }

        /// <summary>品目数。</summary>
        public static int TreatProductCount { get { return Treats.Length; } }

        /// <summary>i 番目の恒久 ID (20..23)。</summary>
        public static string TreatProductKeyAt(int i) { return Treats[i].productKey; }

        /// <summary>i 番目の陳列ノード名 (`KD_TreatMesh_BoneCookie` など)。
        /// **実体側も同じ名前を使う** — 「どの品のメッシュか」を名前から追えるようにする。</summary>
        public static string TreatMeshNodeAt(int i) { return Treats[i].MeshNode; }

        public static string TreatFbxPathAt(int i) { return Treats[i].FbxPath; }
        public static string TreatMaterialPathAt(int i) { return Treats[i].MaterialPath; }
        public static string TreatLabelAt(int i) { return Treats[i].label; }

        /// <summary>i 番目の見た目ノードを <paramref name="holder"/> の下へ作る。
        ///
        /// **<see cref="KD_ModelNodeUtil.RemoveOtherMeshNodes"/> を呼ばない**のが
        /// 棚 (<see cref="BuildTreatDisplay"/>) との唯一の違い —
        /// 実体は**4 種を同時に持って `SetActive` で切り替える**ので、
        /// 「期待する 1 個以外を畳む」を掛けると作った端から消える。</summary>
        public static bool BuildTreatMeshNodeOn(GameObject holder, int i, float visualScale,
            StringBuilder log, out string problem)
        {
            problem = null;
            if (holder == null) { problem = "holder is null"; return false; }
            var def = Treats[i];
            Mesh mesh; Matrix4x4 matrix;
            if (!KD_ModelNodeUtil.ResolveMeshFromFbx(def.FbxPath, out mesh, out matrix, out problem))
                return false;
            var mat = EnsureTreatMaterial(def, log);
            if (mat == null) { problem = "material を用意できない: " + def.MaterialPath; return false; }
            return KD_ModelNodeUtil.BuildMeshNode(holder, def.MeshNode, mesh, matrix, visualScale, mat, null);
        }

        /// <summary>おやつ棚 (2 台目) を用意する。**位置に意味があるので、既にあれば動かさない。**
        ///
        /// 無いときだけ、**おもちゃ棚の脇へ仮置き**する — 「買う → 選ぶ → その場で使う」の
        /// 導線を切らないため。おもちゃ棚の商品は local x ±0.55、
        /// 撫でエフェクトのメニュー板は local x +0.95 を占めているので、**反対側 (-X) へ逃がす。**</summary>
        static GameObject EnsureTreatShelf(GameObject toyShelf, StringBuilder log)
        {
            int found;
            var go = KD_SceneObjects.Find(TreatShelfName, out found);
            if (found > 1)
            {
                log.AppendLine($"FAIL '{TreatShelfName}' がシーン内に {found} 個ある。1 個に整理してから再実行");
                return null;
            }
            if (go != null)
            {
                log.AppendLine("reused " + TreatShelfName + " at "
                    + go.transform.position.ToString("F2") + " (位置は動かさない)");
                return go;
            }

            go = new GameObject(TreatShelfName);
            KD_SceneObjects.PlaceIfNew(go, KD_DogDefs.PlacesPath);
            Vector3 basePos = toyShelf != null
                ? toyShelf.transform.position + new Vector3(-1.2f, 0f, 0f)
                : new Vector3(0f, 0.9f, 0f);
            go.transform.position = basePos;
            log.AppendLine("created " + TreatShelfName + " at " + basePos.ToString("F2")
                + " (仮置き。Play で場所を決める)");
            return go;
        }

        /// <summary>おやつの陳列マテリアル。
        ///
        /// **FBX 付属のものは使えなかった** (2026-08-24 実測)。Tripo の 4 品はいずれも
        /// 埋め込みマテリアル (`tripo_mat_*`) を 1 枚持つが、**`_MainTex` が null** で、
        /// そのまま貼ると 4 種が全部まっ白な同じ形に見える。
        /// **絵の違いが値段差の根拠**なので、それでは売り場が成立しない。
        ///
        /// → `.fbm` の basecolor を挿した Standard マテリアルを 1 枚ずつ作る
        /// (おもちゃ側の `M_KD_Toy_*.mat` と同じ形。あちらは importer の
        /// `externalObjects` で抽出済みだったので作る必要が無かった)。
        ///
        /// **犬ごとの tint は付けない** — おやつは犬に紐づかないので識別層 1/2 が要らない。</summary>
        static Material EnsureTreatMaterial(TreatDef def, StringBuilder log)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(def.MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, def.MaterialPath);
                log.AppendLine("   created " + def.MaterialPath);
                mat = AssetDatabase.LoadAssetAtPath<Material>(def.MaterialPath);
                if (mat == null) { log.AppendLine("   FAIL CreateAsset -> " + def.MaterialPath); return null; }
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(def.BaseColorPath);
            if (tex == null)
                log.AppendLine("   WARN basecolor が無い: " + def.BaseColorPath
                    + " — .fbm を消すと絵が失われる (FBX 側にテクスチャは無い)");
            else if (!mat.HasProperty("_MainTex") || mat.GetTexture("_MainTex") != tex)
            {
                mat.SetTexture("_MainTex", tex);
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        /// <summary>おやつ 1 品の実物陳列。**正規化は <see cref="KD_ModelNodeUtil"/> が正本。**</summary>
        static bool BuildTreatDisplay(GameObject holder, TreatDef def, StringBuilder log, out string problem)
        {
            problem = null;
            if (holder == null) { problem = "holder is null"; return false; }

            Mesh mesh; Matrix4x4 matrix;
            if (!KD_ModelNodeUtil.ResolveMeshFromFbx(def.FbxPath, out mesh, out matrix, out problem))
                return false;
            var mat = EnsureTreatMaterial(def, log);
            if (mat == null) { problem = "material を用意できない: " + def.MaterialPath; return false; }

            // **期待するもの以外の陳列ノードを畳む** (おもちゃ側と同じ理由 —
            // 恒久 ID を保ったままモデルを差し替えると新旧が重なって出る)
            KD_ModelNodeUtil.RemoveOtherMeshNodes(holder, TreatMeshPrefix, def.MeshNode);
            return KD_ModelNodeUtil.BuildMeshNode(holder, def.MeshNode, mesh, matrix, 1.0f, mat, null);
        }

        /// <summary>おやつ 1 品ぶん。**冪等** — 既にあれば作り直さず値だけ揃える。
        /// おもちゃ側 (<see cref="EnsureShopItem"/>) と同型だが、
        /// **消耗品なので `consumable` / `packCount` / `initialStock` / `stockCap` が立つ。**</summary>
        static KD_ShopItem EnsureTreatItem(Transform shelf, TreatDef def,
            KD_ProgressLedger ledger, int slot, int slotCount, StringBuilder log)
        {
            string name = TreatItemName(def);
            var t = shelf.Find(name);
            GameObject go;
            bool created = t == null;
            if (!created) { go = t.gameObject; }
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(shelf, false);
                log.AppendLine("created " + name);
            }

            // holder 自身は描画しない (実物と二重に出る)
            var holderMr = go.GetComponent<MeshRenderer>();
            var holderMf = go.GetComponent<MeshFilter>();
            if (holderMr != null) Object.DestroyImmediate(holderMr, true);
            if (holderMf != null) Object.DestroyImmediate(holderMf, true);

            string dispProblem;
            if (!BuildTreatDisplay(go, def, log, out dispProblem))
                log.AppendLine("   WARN 陳列を作れない (" + def.meshKey + "): " + dispProblem);

            // **実物なので Collider を持つ** (`menuOnly = false`)。**1 個だけ**
            foreach (var extra in go.GetComponents<Collider>())
                if (!(extra is BoxCollider)) Object.DestroyImmediate(extra, true);
            var box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            // 正規化済みなので長軸 1.0。**薄いクッキーでも触れる厚みを残す** —
            // 実寸どおりの薄い箱にすると、VR で狙って触るのが急に難しくなる
            // (おやつはおもちゃより小さいので、ここはおもちゃ以上に効く)
            box.center = Vector3.zero;
            box.size = new Vector3(1f, 1f, 1f);
            box.isTrigger = false;

            // **並びは作るときだけ置く** (おもちゃ側と同じ。2026-09-03 に方針を戻した —
            // 詳しい経緯は EnsureShopItem のコメント)。`TreatDisplayOrigin` は
            // **初回の置き場所**であって、以後の正本ではない。
            // **大きさだけはコード側のまま** (品目間で揃えるのは設計値)
            if (created)
            {
                float x = (slot - (slotCount - 1) * 0.5f) * TreatDisplayStep;
                go.transform.localPosition = TreatDisplayOrigin + new Vector3(x, 0f, 0f);
                go.transform.localRotation = Quaternion.identity;
            }
            go.transform.localScale = Vector3.one * TreatDisplaySize;

            var item = go.GetComponent<KD_ShopItem>();
            if (item == null)
            {
                try { item = go.AddUdonSharpComponent<KD_ShopItem>(); }
                catch (System.Exception e)
                {
                    log.AppendLine("FAIL KD_ShopItem を付けられない (" + name + "): " + e.Message);
                    return null;
                }
            }

            item.ledger = ledger;
            item.productKey = def.productKey;
            item.categoryKey = CategoryTreat;
            item.displayName = def.label;
            item.price = def.price;
            item.consumable = true;
            item.packCount = def.packCount;
            item.initialStock = def.initialStock;
            item.stockCap = def.stockCap;
            // **CE ではない** (2026-08-22 ユーザー決定)。空でないと「無料の初期品」判定が壊れる
            item.ceListingId = "";
            item.menuOnly = false;
            item.listener = null;

            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(item);
            if (ub != null && ub.SyncMethod != VRC.SDKBase.Networking.SyncType.None)
                ub.SyncMethod = VRC.SDKBase.Networking.SyncType.None;

            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>撫でエフェクトの商品 (カテゴリ 1)。
        ///
        /// **実物を並べない** — ハートの粒を棚に置いても意味が読めないので、
        /// メニューの行から選ぶ (shop-foundation §7)。だから `menuOnly = true` で
        /// **Collider を持たない**。品ぞろえの正本はパーティクルの器を持つ
        /// `KD_PetHeartsSetupEditor` 側。</summary>
        static List<KD_ShopItem> EnsureEffectItems(Transform shelf, KD_ProgressLedger ledger,
            StringBuilder log)
        {
            var made = new List<KD_ShopItem>();
            string[] ids; string[] labels; int[] prices; bool[] ce; string[] texNames;
            KD_PetHeartsSetupEditor.GetShopEntries(out ids, out labels, out prices, out ce, out texNames);

            for (int i = 0; i < ids.Length; i++)
            {
                string name = KD_ProductDefinitions.SceneName(ids[i]);
                var t = shelf.Find(name);
                GameObject go = t != null ? t.gameObject : null;
                if (go == null)
                {
                    go = new GameObject(name);
                    go.transform.SetParent(shelf, false);
                    log.AppendLine("created " + name);
                }
                // **メニューの行に対応する位置へ置く。** 確認パネルはこの位置の真上に出る。
                // 基準は MenuBasePos — メニューを動かすときに取り残されないように
                go.transform.localPosition = MenuBasePos
                    + new Vector3(0f, MenuAnchorTop - i * MenuAnchorStep, 0f);
                // **これは見えない目印なので向きに意味は無い** (確認パネルの出る場所を
                // 決める点でしかない)。パネル自身の向きは EnsureConfirmPanel が持つ
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                // **Collider は付けない** (menuOnly)。付けると実物棚と同じく
                // 腕が当たっただけでパネルが開く
                foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c, true);

                var item = go.GetComponent<KD_ShopItem>();
                if (item == null)
                {
                    try { item = go.AddUdonSharpComponent<KD_ShopItem>(); }
                    catch (System.Exception e)
                    {
                        log.AppendLine("FAIL KD_ShopItem (" + name + "): " + e.Message);
                        continue;
                    }
                }
                item.ledger = ledger;
                item.productKey = ids[i];
                item.categoryKey = CategoryPetFx;
                item.displayName = labels[i];
                item.price = prices[i];
                item.consumable = false;
                item.packCount = 0;
                item.initialStock = 0;
                item.stockCap = 0;
                // 同梱品はセットのProduct/Listingを共有する（09-16決定）。
                KD_ProductDefinitions.Apply(item, ids[i]);
                item.menuOnly = true;
                item.shelf = null;      // メニューが渡すので Interact は使わない

                var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(item);
                if (ub != null && ub.SyncMethod != VRC.SDKBase.Networking.SyncType.None)
                    ub.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
                EditorUtility.SetDirty(item);
                made.Add(item);
            }
            return made;
        }

        /// <summary>IMP-PRG-005: Live の商品のシーン上の名前 (`KD_ShopItem_Live_&lt;productKey&gt;`)。
        /// Live System / Treat System の Verify もこれで引く。</summary>
        public static string LiveItemName(string productKey)
        {
            return KD_ProductDefinitions.SceneName(productKey);
        }

        /// <summary>ParticleLive の商品 (カテゴリ 3。IMP-PRG-004 Phase 3 / IMP-PRG-005)。
        ///
        /// **実物を並べない** (撫でエフェクトと同じ `menuOnly`)。売り場は手元メニューのライブタブ 1 つ。
        /// 品ぞろえの正本は <see cref="KD_DogDefs.LiveProducts"/> (`KD_LiveSetupEditor.GetShopEntries` 経由)。
        /// **商品数であって作品数ではない** — 作品と商品は `KD_LiveShow.productKey` で結ぶ。
        /// 表に無い `KD_ShopItem_Live_*` (旧作の 24 など) は消す (Setup は今の定義へ収束する)。</summary>
        static List<KD_ShopItem> EnsureLiveItems(Transform shelf, KD_ProgressLedger ledger,
            StringBuilder log)
        {
            var made = new List<KD_ShopItem>();
            string[] ids; string[] labels; int[] prices;
            KD_LiveSetupEditor.GetShopEntries(out ids, out labels, out prices);

            // 表に無い Live の商品を撤去 (おもちゃの撤去は ShopItemPrefix で引くので Live には届かない)
            {
                var wanted = new HashSet<string>();
                foreach (var id in ids) wanted.Add(LiveItemName(id));
                string livePrefix = ShopItemPrefix.Replace("_Toy_", "_Live_");
                for (int i = shelf.childCount - 1; i >= 0; i--)
                {
                    var c = shelf.GetChild(i);
                    if (!c.name.StartsWith(livePrefix) || wanted.Contains(c.name)) continue;
                    log.AppendLine("removed 定義に無い Live の商品: " + c.name);
                    Object.DestroyImmediate(c.gameObject);
                }
            }

            for (int i = 0; i < ids.Length; i++)
            {
                string name = LiveItemName(ids[i]);
                var t = shelf.Find(name);
                GameObject go = t != null ? t.gameObject : null;
                if (go == null)
                {
                    go = new GameObject(name);
                    go.transform.SetParent(shelf, false);
                    log.AppendLine("created " + name);
                }
                // 見えない目印。確認帯は手元メニューの中なので位置に意味は無い
                go.transform.localPosition = MenuBasePos + new Vector3(0.3f, -0.1f * i, 0f);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c, true);

                var item = go.GetComponent<KD_ShopItem>();
                if (item == null)
                {
                    try { item = go.AddUdonSharpComponent<KD_ShopItem>(); }
                    catch (System.Exception e)
                    {
                        log.AppendLine("FAIL KD_ShopItem (" + name + "): " + e.Message);
                        continue;
                    }
                }
                item.ledger = ledger;
                KD_ProductDefinitions.Apply(item, ids[i]);
                item.displayName = labels[i];
                // 確認帯が `L(displayNameKey)` で名前を引くので、翻訳キーを空にしない
                // (Pack / CandyStep と同じ `<シーン名>.displayNameKey.0`。文言は UITranslations.json)
                if (string.IsNullOrEmpty(item.displayNameKey)) item.displayNameKey = name + ".displayNameKey.0";
                item.menuOnly = true;
                item.shelf = null;

                var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(item);
                if (ub != null && ub.SyncMethod != VRC.SDKBase.Networking.SyncType.None)
                    ub.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
                EditorUtility.SetDirty(item);
                made.Add(item);
            }
            return made;
        }

        /// <summary>商品 1 個ぶん。**冪等** — 既にあれば作り直さず値だけ揃える。</summary>
        static KD_ShopItem EnsureShopItem(Transform shelf, ToyDef def,
            KD_ProgressLedger ledger, int slot, int slotCount, StringBuilder log)
        {
            string name = ShopItemName(def);
            var t = shelf.Find(name);
            GameObject go;
            bool created = t == null;
            if (!created)
            {
                go = t.gameObject;
            }
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(shelf, false);
                log.AppendLine("created " + name);
            }

            // ---- 実物を並べる (2026-08-23 ユーザー決定) ----
            //
            // **灰色の箱をやめた。** 値段差を「性能ではなく絵の強さ」で正当化している設計
            // (progression-ce §4) なのに、その絵を見せない売り場は自分の首を絞める。
            // **実物そのものが最高のプレビュー**で、「犬用品店の棚」という世界観
            // (危険 4 への対処) もこれで成立する。
            //
            // **holder 自身は描画しない。** 以前は primitive の Cube で作っていたので、
            // 撤去しないと**灰色の箱が実物の中に残ったまま**二重に描かれる
            // (Setup の冪等性: 作り直しではなく「今の形へ寄せる」)
            var holderMr = go.GetComponent<MeshRenderer>();
            var holderMf = go.GetComponent<MeshFilter>();
            if (holderMr != null) { Object.DestroyImmediate(holderMr, true); }
            if (holderMf != null) { Object.DestroyImmediate(holderMf, true); }
            if (holderMr != null || holderMf != null)
                log.AppendLine("   " + name + ": 旧 primitive の描画を撤去 (灰色の箱)");

            // 正規化 (長軸を 1.0 に揃えて中心を原点へ) は KD_ToySetupEditor が正本。
            // holder の localScale が陳列サイズになる
            string dispProblem;
            if (!KD_ToySetupEditor.BuildShelfDisplay(go, def.meshKey, out dispProblem))
                log.AppendLine("   WARN 陳列を作れない (" + def.meshKey + "): " + dispProblem);

            // 触る対象は holder 側。**Collider は 1 個だけ** (KD_ShopItem が Interact に使う)
            foreach (var extra in go.GetComponents<Collider>())
                if (!(extra is BoxCollider)) Object.DestroyImmediate(extra, true);
            var box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            // 正規化済みなので長軸 1.0。**薄い品 (ドーナツ・肉球) でも触れる厚みを残す** —
            // 実寸どおりの薄い箱にすると、VR で狙って触るのが急に難しくなる
            box.center = Vector3.zero;
            box.size = new Vector3(1f, 1f, 1f);
            box.isTrigger = false;

            // ---- 並びは**作るときだけ**置く (2026-09-03 に方針を戻した) ----
            //
            // **旧方針は「毎回引き直す」だった。** 理由は「後から品目を足したときに
            // 古い配置が残って商品同士が重なる」で、実際に 2 品 → 5 品で踏んでいる。
            //
            // **その保険が、人の手置きを毎回消す方が高くついた** — 2026-09-03 に
            // `Shop/2` を撫でFXメニューの撤去のために流したら、**ユーザーが棚へ
            // 奥行きも使って並べ直した 10 個が 1 列の等間隔へ戻った**。
            // 実測すると x も z も回転もばらばらの手置きで、**定数へは畳み込めない**
            // (等間隔の 1 列という前提が最初から成り立っていなかった)。
            //
            // よってプロジェクトの原則へ戻す — **Setup = 作るだけ / 置くのは人 /
            // 数えるのは Verify**。重なりの保険は**強制配置ではなく検査**で持つ
            // (`Shop/3 - Verify Shop` の重なり検査)。**新しい品目は初回だけここで置かれる**ので、
            // 手置きとぶつかったら Verify が名指しして人が動かす。
            //
            // **大きさ (localScale) はコード側のまま。** 全 6 種で見た目の大きさを揃えるのは
            // 実験条件そのもの (IMP-FET-009「変えるのは Mesh だけ」) で、人が決める値ではない。
            if (created)
            {
                float x = (slot - (slotCount - 1) * 0.5f) * DisplayStep;
                go.transform.localPosition = new Vector3(x, 0f, 0f);
                go.transform.localRotation = Quaternion.identity;
            }
            go.transform.localScale = Vector3.one * DisplaySize;

            var item = go.GetComponent<KD_ShopItem>();
            if (item == null)
            {
                try { item = go.AddUdonSharpComponent<KD_ShopItem>(); }
                catch (System.Exception e)
                {
                    log.AppendLine("FAIL KD_ShopItem を付けられない (" + name + "): " + e.Message);
                    return null;
                }
            }

            item.ledger = ledger;
            item.productKey = def.productKey;
            item.categoryKey = CategoryToy;
            item.displayName = def.label;
            item.price = def.price;
            item.consumable = false;
            item.packCount = 0;
            item.initialStock = 0;
            item.stockCap = 0;
            item.ceListingId = def.ceListing;
            // ずれても誰も気づかないので、Verify が一致を機械検査する
            item.listener = null;

            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(item);
            if (ub != null && ub.SyncMethod != VRC.SDKBase.Networking.SyncType.None)
                ub.SyncMethod = VRC.SDKBase.Networking.SyncType.None;

            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>棚の面。**残高と 3 状態はここへ出す** —
        /// 常時 HUD へ出すと「ポイントを稼がせる UI」になる (危険 7)。
        ///
        /// **`TextMesh` ではなく `TextMeshPro`。** `TextMesh.text` の setter は
        /// Udon に公開されていない (デバッグ盤が TextMesh を使えるのは素の
        /// MonoBehaviour だから — 実際にコンパイルエラーで踏んだ)。</summary>
        static TMPro.TextMeshPro EnsureLabel(Transform shelf, Vector3 localPos, float faceYaw,
            StringBuilder log)
        {
            const string name = "KD_ShopShelf_Label";
            var t = shelf.Find(name);
            GameObject go;
            if (t != null) { go = t.gameObject; }
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(shelf, false);
                log.AppendLine("created " + name);
            }

            // **位置と向きは毎回書く。** 作成時だけ書く形だと、手で決めた見え方が
            // シーンにしか残らず、ラベルを消して Setup を流し直した瞬間に既定へ落ちる
            // (置き場所の正本を 1 箇所に持つ — MenuBasePos と同じ扱い。2026-09-01)
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, faceYaw, 0f);
            go.transform.localScale = Vector3.one;

            var tm = go.GetComponent<TMPro.TextMeshPro>();
            if (tm == null) tm = go.AddComponent<TMPro.TextMeshPro>();

            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontAssetPath);
            if (font == null) log.AppendLine("WARN font asset が無い: " + FontAssetPath);
            else tm.font = font;

            tm.fontSize = 0.9f;
            tm.alignment = TMPro.TextAlignmentOptions.TopLeft;
            tm.color = Color.white;
            tm.enableWordWrapping = false;
            var rt = tm.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(1.4f, 0.9f);
            if (tm.text == null || tm.text.Length == 0) tm.text = "(shop)";
            return tm;
        }

    }
}
