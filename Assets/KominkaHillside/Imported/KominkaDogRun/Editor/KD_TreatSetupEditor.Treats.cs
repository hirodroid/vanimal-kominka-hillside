using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>裏山改変 (IMP-DOG-014 / 段階 2): おやつの器を KH_Dogs 直下に作る Setup (Menu/6c) と検査 (Menu/6d)、
    /// おやつ行の [買う] の生成 (<see cref="HillsideBuildTreatBuy"/>。Menu/7 の HillsideBuildPages から呼ぶ)。
    ///
    /// 古民家の <see cref="Setup"/> (Imported-Unused/Treat System/2 (Kominka)) を裏山向けに作り替えたもの。差分:
    /// <list type="bullet">
    /// <item>置き場は <c>KH_Dogs</c> 直下 ("====KominkaDogRun====/KD_Systems" と "KD_Props" は作らない)。</item>
    /// <item>定位置 KD_TreatHome_0..2 の初回の仮置きは KH_LeashRack の −Z 側 1.5 m に 0.6 m 間隔、地面へレイキャスト。
    ///   **既存の定位置と実体は動かさない** (人の調整を潰さない)。</item>
    /// <item>手元メニュー (pouch) は作らない・配線しない (Menu/7 Build Menu = SetupHillside の段階 2 分岐が box / 枠を配線する)。
    ///   枠 → pouch (移譲の知らせ先) だけ、pouch が居れば配線する。</item>
    /// <item>商品は KH_Dogs/KD_ShopCatalog/KD_ShopItem_Treat_20..23 (段階 1 の Menu/4 が作成済み) を前提として数える。</item>
    /// </list>
    /// 同期の権威 (Box = マスター固定 / Slot = 出した人 / 実体 = VRCObjectSync) とタイムアウトはコードの既定値 (古民家値) のまま。
    ///
    /// 実行順: Menu/5 Create Pouch Programs (Box / Slot / Controller / Fx の Program Asset。既存) → Menu/3a・4 (台帳と商品) →
    /// **6c Setup Treats** → Menu/7 Build Menu (段階 2) → 19b Assign Network IDs → **6d Verify Treats**。保存しない。</summary>
    public static partial class KD_TreatSetupEditor
    {
        const string TreatsSetupLog = KD_Hillside.LogDir + "setup-treats.txt";
        const string TreatsVerifyLog = KD_Hillside.LogDir + "verify-treats.txt";

        /// <summary>定位置の仮置きの基準 (KH_Dogs 直下の拠点)。無ければ <see cref="TreatHomeFallback"/>。</summary>
        const string TreatHomeAnchorName = "KH_LeashRack";
        static readonly Vector3 TreatHomeFallback = new Vector3(45.00f, 2.71f, 43.00f);
        /// <summary>基準から −Z へ下げる量 (m) と、定位置どうしの間隔 (m)。3 つを X に並べ、中央を基準の真後ろに置く。</summary>
        const float TreatHomeBack = 1.5f, TreatHomeStep = 0.6f;
        /// <summary>実体を定位置の上へ浮かせる量 (m)。古民家の Setup と同じ。</summary>
        const float TreatBodyLift = 0.03f;

        /// <summary>おやつ行の [買う] (Row{i}/Buy → _TreatBuy{i})。</summary>
        internal const string TreatBuyButtonName = "Buy";
        internal static string TreatBuyEventOf(int i) { return "_TreatBuy" + i; }

        // ================================================================ 6c Setup

        [MenuItem("Tools/KominkaHillside/Menu/6c Setup Treats")]
        public static void SetupTreats()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_TreatSetup] 6c Setup Treats (裏山。器は " + KD_Hillside.DogsRootName + " 直下)");
            bool ok = true;
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            {
                log.AppendLine("FAIL " + KD_Hillside.ScenePath + " を Edit モードで開くこと");
                KD_VerifyLog.Write(TreatsSetupLog, log);
                return;
            }

            // ---- Program Asset (無いと AddUdonSharpComponent が器だけ作り、配線が静かに空振りする) ----
            foreach (string path in new[] { ControllerProgramPath, BoxProgramPath, SlotProgramPath, KD_TreatFxSetupEditor.FxProgramPath })
            {
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path) != null) continue;
                log.AppendLine("FAIL " + path + " が無い。先に Tools/KominkaHillside/Menu/5 Create Pouch Programs → force refresh");
                KD_VerifyLog.Write(TreatsSetupLog, log);
                return;
            }

            // ---- 置き場と名簿 (前提) ----
            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int dogsCount);
            if (dogsGo == null || dogsCount != 1 || dogsGo.transform.parent != null)
            {
                log.AppendLine("FAIL " + KD_Hillside.DogsRootName + " がルートに 1 個ない (" + dogsCount + ")");
                KD_VerifyLog.Write(TreatsSetupLog, log);
                return;
            }
            var dogs = dogsGo.transform;
            var packGo = KD_SceneObjects.Find(PackName, out int packCount);
            var pack = (packCount == 1 && packGo != null) ? packGo.GetUdonSharpComponent<KD_DogPack>() : null;
            if (pack == null)
            {
                log.AppendLine("FAIL " + PackName + " が " + packCount + " 個。先に Tools/KominkaHillside/Walking/3 Build");
                KD_VerifyLog.Write(TreatsSetupLog, log);
                return;
            }
            // 商品 4 種 (段階 1 の Menu/4 Setup Shop)。実体のメッシュは KD_ShopSetupEditor の表から作るが、
            // 出す品の productKey は台帳の商品と一致していなければ消費 (_TryConsume) が空振りする
            if (!TreatItemsPresent(dogs, log))
            {
                log.AppendLine("SETUP RESULT: FAIL (おやつの商品が無い。先に Menu/3a Setup Progress → 4 Setup Shop。何も作っていない)");
                KD_VerifyLog.Write(TreatsSetupLog, log);
                return;
            }

            // ---- 旧名 (古民家の Phase 1) が残っていれば止める (fileID を保つ改名は古民家の Scene Layout の担当) ----
            foreach (string legacy in new[] { KD_DogDefs.LegacyTreatName, KD_DogDefs.LegacyTreatHomeName, LegacySystemName })
            {
                KD_SceneObjects.Find(legacy, out int legacyCount);
                if (legacyCount == 0) continue;
                log.AppendLine("FAIL 旧名 '" + legacy + "' がシーンにある (" + legacyCount + " 個)。裏山では作らない名前なので手で消すこと");
                KD_VerifyLog.Write(TreatsSetupLog, log);
                return;
            }

            var anchor = dogs.Find(TreatHomeAnchorName);
            Vector3 anchorPos = anchor != null ? anchor.position : TreatHomeFallback;
            if (anchor == null) log.AppendLine("   WARN " + KD_Hillside.DogsRootName + "/" + TreatHomeAnchorName + " が無い。仮置きの基準は固定値 " + TreatHomeFallback.ToString("F2"));

            var pouchT = dogs.Find(PouchName);
            var pouch = pouchT == null ? null : pouchT.GetComponent<KD_TreatPouch>();
            if (pouch == null) log.AppendLine("   WARN " + KD_Hillside.DogsRootName + "/" + PouchName + " が無い。枠 → pouch (移譲の知らせ先) は null のまま (Menu/7 の後に 6c を再実行)");

            // ---- 枠 3 つぶんの器 ----
            var slots = new KD_TreatSlot[SlotCount];
            var bodies = new KD_TreatController[SlotCount];
            var promptCatalog = KD_LocalizationEditor.Load();
            string prompt = KD_LocalizationEditor.Text(promptCatalog, promptCatalog.fallbackLanguage, "interact.treat");
            int pickupLayer = LayerMask.NameToLayer("Pickup");
            for (int i = 0; i < SlotCount; i++)
            {
                string homeName = KD_DogDefs.TreatHomeNameOf(i);
                string bodyName = KD_DogDefs.TreatNameOf(i);
                string slotName = KD_DogDefs.TreatSlotNameOf(i);

                // ---- 定位置 (Respawn 先の目印)。**既存は 1 mm も動かさない** ----
                var homeGo = KD_SceneObjects.Find(homeName, out int found);
                if (found > 1) { log.AppendLine("FAIL '" + homeName + "' がシーン内に " + found + " 個ある"); KD_VerifyLog.Write(TreatsSetupLog, log); return; }
                if (homeGo == null)
                {
                    homeGo = new GameObject(homeName);
                    Undo.RegisterCreatedObjectUndo(homeGo, "Create " + homeName);
                    KD_SceneObjects.PlaceIfNew(homeGo, KD_Hillside.DogsRootName);
                    float x = anchorPos.x + (i - (SlotCount - 1) * 0.5f) * TreatHomeStep;
                    float z = anchorPos.z - TreatHomeBack;
                    string how;
                    float y = TreatGroundY(x, z, dogs, anchorPos.y, out how);
                    homeGo.transform.position = new Vector3(x, y, z);
                    log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + homeName + " (仮置き " + homeGo.transform.position.ToString("F2")
                        + " / 地面: " + how + " — **置き場所は Play で決める**)");
                }
                else if (homeGo.transform.parent != dogs)
                    log.AppendLine("   WARN " + homeName + " が " + KD_Hillside.DogsRootName + " 直下に無い (" + KD_SceneObjects.PathOf(homeGo) + ")。動かさない");
                // 見た目を落とす (共有の皿は廃止された)。位置は触らない
                foreach (var r in homeGo.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled) { r.enabled = false; log.AppendLine("   " + homeName + ": renderer off"); }
                foreach (var c in homeGo.GetComponentsInChildren<Collider>(true))
                    if (c.enabled) { c.enabled = false; log.AppendLine("   " + homeName + ": collider off"); }

                // ---- 実体 (Pickup + Rigidbody + VRCObjectSync + 4 種のメッシュ) ----
                var treatGo = KD_SceneObjects.Find(bodyName, out found);
                if (found > 1) { log.AppendLine("FAIL '" + bodyName + "' がシーン内に " + found + " 個ある"); KD_VerifyLog.Write(TreatsSetupLog, log); return; }
                if (treatGo == null)
                {
                    treatGo = new GameObject(bodyName);
                    Undo.RegisterCreatedObjectUndo(treatGo, "Create " + bodyName);
                    KD_SceneObjects.PlaceIfNew(treatGo, KD_Hillside.DogsRootName);   // 動かない親の下 (VRCObjectSync の Respawn 先)
                    treatGo.transform.position = homeGo.transform.position + Vector3.up * TreatBodyLift;
                    log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + bodyName + " (" + homeName + " の上)");
                }
                treatGo.layer = pickupLayer >= 0 ? pickupLayer : 13;   // VRChat 既定: 13 = Pickup
                var rootMr = treatGo.GetComponent<MeshRenderer>();
                var rootMf = treatGo.GetComponent<MeshFilter>();
                if (rootMr != null) Object.DestroyImmediate(rootMr, true);
                if (rootMf != null) Object.DestroyImmediate(rootMf, true);
                if (treatGo.transform.localScale != Vector3.one)
                {
                    log.AppendLine("   " + bodyName + ": localScale " + treatGo.transform.localScale.ToString("F3") + " -> 1 (メッシュ側で実寸を作る)");
                    treatGo.transform.localScale = Vector3.one;
                }
                foreach (var extra in treatGo.GetComponents<Collider>())
                    if (!(extra is BoxCollider)) Object.DestroyImmediate(extra, true);
                var bodyBox = treatGo.GetComponent<BoxCollider>();
                if (bodyBox == null) bodyBox = treatGo.AddComponent<BoxCollider>();
                bodyBox.center = Vector3.zero;
                bodyBox.size = new Vector3(TreatBodySize, TreatGrabThickness, TreatBodySize);
                bodyBox.isTrigger = false;
                var rb = treatGo.GetComponent<Rigidbody>();
                if (rb == null) rb = treatGo.AddComponent<Rigidbody>();
                rb.mass = 0.1f;
                rb.isKinematic = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // 小物の床抜け対策
                if (treatGo.GetComponent<VRCObjectSync>() == null) treatGo.AddComponent<VRCObjectSync>();
                var pickup = treatGo.GetComponent<VRCPickup>();
                if (pickup == null) pickup = treatGo.AddComponent<VRCPickup>();
                pickup.InteractionText = prompt;

                int n = KD_ShopSetupEditor.TreatProductCount;
                var ids = new string[n];
                var nodes = new GameObject[n];
                for (int p = 0; p < n; p++)
                {
                    ids[p] = KD_ShopSetupEditor.TreatProductKeyAt(p);
                    string nodeName = KD_ShopSetupEditor.TreatMeshNodeAt(p);
                    if (!KD_ShopSetupEditor.BuildTreatMeshNodeOn(treatGo, p, TreatBodySize, log, out string meshProblem))
                    {
                        log.AppendLine("   FAIL メッシュを作れない (" + nodeName + "): " + meshProblem);
                        ok = false;
                        continue;
                    }
                    var nodeT = treatGo.transform.Find(nodeName);
                    nodes[p] = nodeT != null ? nodeT.gameObject : null;
                    if (nodes[p] != null && nodes[p].activeSelf) nodes[p].SetActive(false);   // 初期は全部 false = 枠が Free の見た目
                }

                var treatCtrl = treatGo.GetUdonSharpComponent<KD_TreatController>();
                if (treatCtrl == null) { treatCtrl = treatGo.AddUdonSharpComponent<KD_TreatController>(); log.AppendLine("   added KD_TreatController (" + bodyName + ")"); }
                treatCtrl.slotIndex = i;
                treatCtrl.parking = homeGo.transform;
                treatCtrl.productKeys = ids;
                treatCtrl.meshNodes = nodes;
                bodies[i] = treatCtrl;

                // ---- 枠の状態 (Manual なので専用 GameObject) ----
                var slotGo = KD_SceneObjects.Find(slotName, out found);
                if (found > 1) { log.AppendLine("FAIL '" + slotName + "' がシーン内に " + found + " 個ある"); KD_VerifyLog.Write(TreatsSetupLog, log); return; }
                if (slotGo == null)
                {
                    slotGo = new GameObject(slotName);
                    Undo.RegisterCreatedObjectUndo(slotGo, "Create " + slotName);
                    KD_SceneObjects.PlaceIfNew(slotGo, KD_Hillside.DogsRootName);
                    log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + slotName);
                }
                var slot = slotGo.GetUdonSharpComponent<KD_TreatSlot>();
                if (slot == null) { slot = slotGo.AddUdonSharpComponent<KD_TreatSlot>(); log.AppendLine("   added KD_TreatSlot (" + slotName + ")"); }
                slot.slotIndex = i;
                slot.body = treatCtrl;
                slots[i] = slot;
                treatCtrl.slot = slot;
            }

            // ---- ルールの正本 + Claim の配り手 (Manual・専用 GameObject・**マスター固定** = SetOwner を呼ばない) ----
            var boxGo = KD_SceneObjects.Find(BoxName, out int boxFound);
            if (boxFound > 1) { log.AppendLine("FAIL '" + BoxName + "' がシーン内に " + boxFound + " 個ある"); KD_VerifyLog.Write(TreatsSetupLog, log); return; }
            if (boxGo == null)
            {
                boxGo = new GameObject(BoxName);
                Undo.RegisterCreatedObjectUndo(boxGo, "Create " + BoxName);
                KD_SceneObjects.PlaceIfNew(boxGo, KD_Hillside.DogsRootName);
                log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + BoxName);
            }
            var treatBox = boxGo.GetUdonSharpComponent<KD_TreatBox>();
            if (treatBox == null) { treatBox = boxGo.AddUdonSharpComponent<KD_TreatBox>(); log.AppendLine("   added KD_TreatBox"); }
            treatBox.slots = slots;
            treatBox.pack = pack;
            ok &= TryCopy(treatBox, log);

            // 枠 → 箱 / pouch (箱を作った後の 2 周目)
            for (int i = 0; i < SlotCount; i++)
            {
                if (slots[i] == null) { ok = false; continue; }
                slots[i].box = treatBox;
                slots[i].pouch = pouch;   // 移譲の知らせ先。null でも壊れない (往復が遅くなるだけ)
                ok &= TryCopy(slots[i], log);
                ok &= TryCopy(bodies[i], log);
                log.AppendLine("   wired slot " + i + ": " + KD_DogDefs.TreatSlotNameOf(i) + " <-> " + KD_DogDefs.TreatNameOf(i)
                    + " (home " + KD_DogDefs.TreatHomeNameOf(i) + ", pouch=" + (pouch != null) + ")");
            }

            // ---- 各犬の徘徊層 → treatBox ----
            int hillsideRefs = 0;
            foreach (var def in KD_DogDefs.All)
            {
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out int wCount);
                var wander = (wCount == 1 && wanderGo != null) ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                if (wander == null) { log.AppendLine("   FAIL " + def.WanderSystemName + " が取れない (" + wCount + " 個)"); ok = false; continue; }
                wander.treatBox = treatBox;
                ok &= TryCopy(wander, log);
                if (wander.hillsideLeashes != null && wander.dogIndex >= 0) hillsideRefs++;
                log.AppendLine("   wired " + def.WanderSystemName + ".treatBox (hillsideLeashes=" + (wander.hillsideLeashes != null)
                    + " frisbeeCatch=" + (wander.frisbeeCatch != null) + " dogIndex=" + wander.dogIndex + ")");
            }
            if (hillsideRefs != KD_DogDefs.All.Length)
                log.AppendLine("   WARN リード接続中の除外 (KD_TreatBox.HillsideDogBusy) が読む hillsideLeashes / dogIndex が " + hillsideRefs + "/" + KD_DogDefs.All.Length
                    + " 匹。欠けた犬はリード中でもおやつへ向かう (Walking/3 Build の配線を確認)");

            // ---- 食後エフェクトと効果音 (KD_TreatFxSetupEditor。器は KH_Dogs 直下) ----
            log.AppendLine("-- 食後エフェクト --");
            ok &= KD_TreatFxSetupEditor.Setup(treatBox, slots, pack, log, KD_Hillside.DogsRootName);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("SETUP RESULT: " + (ok ? "PASS" : "FAIL"));
            log.AppendLine("**シーンは保存していない。** 次: Menu/7 Build Menu (段階 2 で pouch.box / 枠を配線) → 19b Assign Network IDs → 6d Verify Treats");
            log.AppendLine("定位置を動かしたら、実体もその上へ置き直すこと (Respawn 先 = シーン開始位置。6d が距離を数える)");
            KD_VerifyLog.Write(TreatsSetupLog, log);
        }

        /// <summary>KH_Dogs/KD_ShopCatalog の子に KD_ShopItem_Treat_20..23 が 1 個ずつあり、productKey が表と一致するか。</summary>
        static bool TreatItemsPresent(Transform dogs, StringBuilder log)
        {
            bool ok = true;
            var catalogT = dogs.Find(KD_Hillside.ShopCatalogName);
            if (catalogT == null || catalogT.GetComponent<KD_ShopCatalog>() == null)
            { log.AppendLine("FAIL " + KD_Hillside.DogsRootName + "/" + KD_Hillside.ShopCatalogName + " に KD_ShopCatalog が無い"); return false; }
            for (int i = 0; i < KD_ShopSetupEditor.TreatProductCount; i++)
            {
                string name = KD_ShopSetupEditor.TreatItemNameAt(i);
                var itemGo = KD_SceneObjects.Find(name, out int count);
                var item = (count == 1 && itemGo != null) ? itemGo.GetComponent<KD_ShopItem>() : null;
                if (item == null) { log.AppendLine("FAIL おやつの商品 " + name + " が取れない (" + count + " 個)"); ok = false; continue; }
                if (item.productKey != KD_ShopSetupEditor.TreatProductKeyAt(i))
                { log.AppendLine("FAIL " + name + ".productKey = " + item.productKey + " (表は " + KD_ShopSetupEditor.TreatProductKeyAt(i) + ")"); ok = false; }
            }
            return ok;
        }

        /// <summary>(x, z) の地面の高さ。上から下へ全部当て、犬・リード・拠点 (KH_Dogs の子) は地面に数えない。
        /// Terrain を優先し、無ければいちばん高い当たり。何も当たらなければ <paramref name="fallbackY"/>。</summary>
        static float TreatGroundY(float x, float z, Transform dogs, float fallbackY, out string how)
        {
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(new Vector3(x, 500f, z), Vector3.down, 1000f, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit best = default(RaycastHit);
            bool have = false, terrain = false;
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                if (dogs != null && h.collider.transform.IsChildOf(dogs)) continue;
                bool isTerrain = h.collider is TerrainCollider;
                if (!have || (isTerrain && !terrain) || (isTerrain == terrain && h.point.y > best.point.y))
                { best = h; have = true; terrain = isTerrain; }
            }
            if (!have) { how = "当たりなし → 基準の高さ " + fallbackY.ToString("F2"); return fallbackY; }
            how = (terrain ? "Terrain " : "collider ") + best.collider.name + " y=" + best.point.y.ToString("F2");
            return best.point.y;
        }

        // ================================================================ Menu/7 から: おやつ行の [買う]

        /// <summary>おやつの行 Row0..3 (Page_Treat 直下。Scroll.Build が Content へ移す前) に [買う] を足す。
        /// HillsideBuildPages (Menu/7) から呼ぶ。押し先は <see cref="TreatBuyEventOf"/> (_TreatBuy{i})。
        /// 置き方 (x・幅) は KD_HandMenuScrollEditor.Build が行の型に合わせて決める (ここの座標は仮置き)。</summary>
        internal static void HillsideBuildTreatBuy(GameObject[] pages, KD_TreatPouch pouch, TMP_FontAsset font,
            VRC.Udon.UdonBehaviour udon, Sprite round, StringBuilder log)
        {
            var page = pages[KD_TreatPouch.TabTreat].transform;
            int n = KD_ShopSetupEditor.TreatProductCount;
            var buttons = new GameObject[n];
            var images = new UnityEngine.UI.Image[n];
            var labels = new TextMeshProUGUI[n];
            string word = KD_Hillside.Text("KD_TreatPouch.WordBuyHere", "買う", log);
            for (int i = 0; i < n; i++)
            {
                var row = page.Find("Row" + i);
                if (row == null) { log.AppendLine("   FAIL Row" + i + " が無い (おやつの器)"); continue; }
                buttons[i] = RoundButton(row, TreatBuyButtonName, new Vector2(40f, -26f), new Vector2(130f, 44f),
                    KD_UiSpriteEditor.ActionPrimary, font, word, udon, TreatBuyEventOf(i), round, log);
                images[i] = buttons[i].GetComponent<UnityEngine.UI.Image>();
                labels[i] = ButtonLabelOf(buttons[i], 20f);
                buttons[i].SetActive(false);   // 実行時に HillsideRedrawTreat が出す (台帳の復元後)
            }
            pouch.treatBuyButtons = buttons;
            pouch.treatBuyImages = images;
            pouch.treatBuyLabels = labels;
            log.AppendLine("   おやつ: 行に [買う] " + n + " (Row{i}/" + TreatBuyButtonName + " -> _TreatBuy{i}。既定非表示)");
        }

        // ================================================================ 6d Verify

        [MenuItem("Tools/KominkaHillside/Menu/6d Verify Treats")]
        public static void VerifyTreats()
        {
            var v = new KD_VerifyLog("[KD_TreatSetup] 6d Verify Treats (裏山 段階 " + KD_Hillside.Stage + ")");
            var log = v.Text;
            if (!v.Check(!EditorApplication.isPlaying && KD_Hillside.InScene(), KD_Hillside.ScenePath + " を Edit モードで開いている"))
            { v.Finish(TreatsVerifyLog); return; }

            // ---- Program Asset と同期方式 ----
            foreach (var (path, name) in new[] {
                (ControllerProgramPath, "KD_TreatController"), (BoxProgramPath, "KD_TreatBox"), (SlotProgramPath, "KD_TreatSlot") })
            {
                var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
                v.Check(pa != null && pa.sourceCsScript != null, "program asset ok: " + name);
            }
            var slotPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(SlotProgramPath);
            v.Check(slotPa != null && slotPa.behaviourSyncMode == BehaviourSyncMode.Manual, "KD_TreatSlot の behaviourSyncMode == Manual (giverId / product / outMs)");
            var boxPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(BoxProgramPath);
            v.Check(boxPa != null && boxPa.behaviourSyncMode == BehaviourSyncMode.Manual, "KD_TreatBox の behaviourSyncMode == Manual (枠と Claim の正本)");

            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int dogsCount);
            if (!v.Check(dogsGo != null && dogsCount == 1 && dogsGo.transform.parent == null, KD_Hillside.DogsRootName + " がルートに 1 個"))
            { v.Finish(TreatsVerifyLog); return; }
            var dogs = dogsGo.transform;
            KD_SceneObjects.Find(KD_DogDefs.WorldRootName, out int kominkaRoots);
            v.Check(kominkaRoots == 0, "\"" + KD_DogDefs.WorldRootName + "\" がシーンに無い (actual " + kominkaRoots + ")");

            // ---- 旧名・Phase 1 の残骸が無い ----
            foreach (string legacy in new[] { KD_DogDefs.LegacyTreatName, KD_DogDefs.LegacyTreatHomeName, LegacySystemName })
            {
                KD_SceneObjects.Find(legacy, out int legacyCount);
                v.Check(legacyCount == 0, "旧名 '" + legacy + "' が無い (actual " + legacyCount + ")");
            }
            v.Check(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(LegacySystemProgramPath) == null, LegacySystemProgramPath + " が無い");

            // ---- 枠がちょうど 3 つ (実体 / 状態 / 定位置。すべて KH_Dogs 直下) ----
            var bodyGos = new GameObject[SlotCount];
            var homeGos = new GameObject[SlotCount];
            var slotGos = new GameObject[SlotCount];
            var bodies = new KD_TreatController[SlotCount];
            var slotBs = new KD_TreatSlot[SlotCount];
            var dogTs = KD_SceneObjects.AllDogTransforms();
            int pickupLayer = LayerMask.NameToLayer("Pickup");
            for (int i = 0; i < SlotCount; i++)
            {
                int found;
                bodyGos[i] = KD_SceneObjects.Find(KD_DogDefs.TreatNameOf(i), out found);
                v.Check(found == 1 && bodyGos[i] != null && bodyGos[i].transform.parent == dogs, KD_Hillside.DogsRootName + "/" + KD_DogDefs.TreatNameOf(i) + " が 1 個 (actual " + found + ")");
                homeGos[i] = KD_SceneObjects.Find(KD_DogDefs.TreatHomeNameOf(i), out found);
                v.Check(found == 1 && homeGos[i] != null && homeGos[i].transform.parent == dogs, KD_Hillside.DogsRootName + "/" + KD_DogDefs.TreatHomeNameOf(i) + " が 1 個 (actual " + found + ")");
                slotGos[i] = KD_SceneObjects.Find(KD_DogDefs.TreatSlotNameOf(i), out found);
                v.Check(found == 1 && slotGos[i] != null && slotGos[i].transform.parent == dogs, KD_Hillside.DogsRootName + "/" + KD_DogDefs.TreatSlotNameOf(i) + " が 1 個 (actual " + found + ")");
                if (bodyGos[i] != null) bodies[i] = bodyGos[i].GetUdonSharpComponent<KD_TreatController>();
                if (slotGos[i] != null) slotBs[i] = slotGos[i].GetUdonSharpComponent<KD_TreatSlot>();
            }
            KD_SceneObjects.Find(KD_DogDefs.TreatNameOf(SlotCount), out int overflow);
            v.Check(overflow == 0, KD_DogDefs.TreatNameOf(SlotCount) + " は存在しない (枠はちょうど " + SlotCount + " つ = KD_DogDefs.TreatSlotCount)");

            // ---- 実体 ----
            for (int i = 0; i < SlotCount; i++)
            {
                var treatGo = bodyGos[i];
                if (treatGo == null) continue;
                string tag = KD_DogDefs.TreatNameOf(i) + ": ";
                v.Check(KD_SceneObjects.HasStaticAncestry(treatGo, out string offender), tag + "先祖が動かないコンテナだけ (VRCObjectSync の Respawn 先を守る)" + (offender == null ? "" : " — " + offender));
                v.Check(!PrefabUtility.IsPartOfPrefabInstance(treatGo) && !KD_SceneObjects.IsUnderAnyDog(treatGo.transform, dogTs), tag + "犬 (購入プレハブ) の中にない");
                var rb = treatGo.GetComponent<Rigidbody>();
                v.Check(rb != null && !rb.isKinematic, tag + "non-kinematic Rigidbody");
                v.Check(treatGo.GetComponent<VRCObjectSync>() != null, tag + "VRCObjectSync (位置同期の正本)");
                var pickup = treatGo.GetComponent<VRCPickup>();
                v.Check(pickup != null && pickup.pickupable, tag + "VRCPickup (pickupable)");
                v.Check(treatGo.layer == (pickupLayer >= 0 ? pickupLayer : 13), tag + "layer == Pickup");
                v.Check(treatGo.GetComponent<MeshRenderer>() == null, tag + "root に MeshRenderer が無い (見た目は 4 種のメッシュだけ)");
                if (!v.Check(bodies[i] != null, tag + "KD_TreatController が付いている")) continue;
                if (homeGos[i] != null)
                {
                    float d = Vector3.Distance(treatGo.transform.position, homeGos[i].transform.position);
                    v.Check(d <= bodies[i].homeRadius, tag + "自分の定位置の上にある (距離 " + d.ToString("F2") + " m <= homeRadius " + bodies[i].homeRadius.ToString("F2") + ") — Respawn 先の整合");
                }
                int n = KD_ShopSetupEditor.TreatProductCount;
                bool arrays = bodies[i].meshNodes != null && bodies[i].meshNodes.Length == n && bodies[i].productKeys != null && bodies[i].productKeys.Length == n;
                if (!v.Check(arrays, tag + "meshNodes / productKeys が " + n + " 本")) continue;
                for (int p = 0; p < n; p++)
                {
                    string nodeName = KD_ShopSetupEditor.TreatMeshNodeAt(p);
                    var node = bodies[i].meshNodes[p];
                    if (!v.Check(node != null && node.name == nodeName && node.transform.parent == treatGo.transform, tag + "meshNodes[" + p + "] == " + nodeName)) continue;
                    v.Check(bodies[i].productKeys[p] == KD_ShopSetupEditor.TreatProductKeyAt(p), tag + "productKeys[" + p + "] == " + KD_ShopSetupEditor.TreatProductKeyAt(p));
                    var mf = node.GetComponent<MeshFilter>();
                    bool resolved = KD_ModelNodeUtil.ResolveMeshFromFbx(KD_ShopSetupEditor.TreatFbxPathAt(p), out Mesh want, out Matrix4x4 ignored, out string meshProblem);
                    v.Check(resolved && mf != null && mf.sharedMesh == want, tag + nodeName + " のメッシュが " + KD_ShopSetupEditor.TreatFbxPathAt(p) + " 由来" + (resolved ? "" : " (" + meshProblem + ")"));
                    var mr = node.GetComponent<MeshRenderer>();
                    var wantMat = AssetDatabase.LoadAssetAtPath<Material>(KD_ShopSetupEditor.TreatMaterialPathAt(p));
                    v.Check(mr != null && wantMat != null && mr.sharedMaterial == wantMat && wantMat.HasProperty("_MainTex") && wantMat.GetTexture("_MainTex") != null,
                        tag + nodeName + " のマテリアルが " + KD_ShopSetupEditor.TreatMaterialPathAt(p) + " (_MainTex あり)");
                }
            }

            // ---- 定位置 (目印。見た目なし。地面との差を記録) ----
            for (int i = 0; i < SlotCount; i++)
            {
                if (homeGos[i] == null) continue;
                int visible = 0;
                foreach (var r in homeGos[i].GetComponentsInChildren<Renderer>(true)) if (r.enabled) visible++;
                v.Check(visible == 0, KD_DogDefs.TreatHomeNameOf(i) + " は見た目を持たない (enabled な Renderer " + visible + " 個)");
                var hp = homeGos[i].transform.position;
                float gy = TreatGroundY(hp.x, hp.z, dogs, hp.y, out string how);
                float dy = hp.y - gy;
                v.Check(dy > -0.05f && dy < 0.5f, KD_DogDefs.TreatHomeNameOf(i) + " が地面の近く (位置 " + hp.ToString("F2") + " / 地面との差 " + dy.ToString("F2") + " m / " + how + ")");
            }

            // ---- 枠の状態 ----
            for (int i = 0; i < SlotCount; i++)
            {
                if (slotGos[i] == null) continue;
                string tag = KD_DogDefs.TreatSlotNameOf(i) + ": ";
                v.Check(slotGos[i].GetComponents<VRC.Udon.UdonBehaviour>().Length == 1, tag + "UdonBehaviour は 1 個 (Manual なので同居不可)");
                v.Check(slotGos[i].GetComponent<VRCObjectSync>() == null, tag + "VRCObjectSync が無い");
                if (!v.Check(slotBs[i] != null, tag + "KD_TreatSlot が付いている")) continue;
                v.Check(slotBs[i].slotIndex == i, tag + "slotIndex == " + i);
                v.Check(slotBs[i].body != null && slotBs[i].body == bodies[i], tag + "body == " + KD_DogDefs.TreatNameOf(i));
                // 古民家値のまま (Issue: 予約 20s / 放置 60s。持っている間の上限 180s > 放置)
                v.Check(Mathf.Approximately(slotBs[i].reserveTimeout, 20f) && Mathf.Approximately(slotBs[i].abandonTimeout, 60f),
                    tag + "reserveTimeout 20 / abandonTimeout 60 (古民家値。actual " + slotBs[i].reserveTimeout.ToString("F0") + " / " + slotBs[i].abandonTimeout.ToString("F0") + ")");
                v.Check(slotBs[i].holdAbandonTimeout > slotBs[i].abandonTimeout, tag + "holdAbandonTimeout (" + slotBs[i].holdAbandonTimeout.ToString("F0") + "s) > abandonTimeout");
                v.Check(bodies[i] != null && bodies[i].slot == slotBs[i] && bodies[i].slotIndex == i, KD_DogDefs.TreatNameOf(i) + ": slot == " + KD_DogDefs.TreatSlotNameOf(i) + " / slotIndex == " + i);
                v.Check(bodies[i] != null && homeGos[i] != null && bodies[i].parking == homeGos[i].transform, KD_DogDefs.TreatNameOf(i) + ": parking == " + KD_DogDefs.TreatHomeNameOf(i));
            }

            // ---- 箱 ----
            var boxGo = KD_SceneObjects.Find(BoxName, out int boxCount);
            var box = (boxCount == 1 && boxGo != null) ? boxGo.GetUdonSharpComponent<KD_TreatBox>() : null;
            v.Check(boxCount == 1 && boxGo != null && boxGo.transform.parent == dogs, KD_Hillside.DogsRootName + "/" + BoxName + " が 1 個 (actual " + boxCount + ")");
            if (boxGo != null)
            {
                v.Check(boxGo.GetComponents<VRC.Udon.UdonBehaviour>().Length == 1, BoxName + " の UdonBehaviour は 1 個 (Manual なので同居不可)");
                v.Check(boxGo.GetComponent<VRCObjectSync>() == null, BoxName + " に VRCObjectSync が無い");
            }
            if (box != null)
            {
                bool slotsOk = box.slots != null && box.slots.Length == SlotCount;
                for (int i = 0; slotsOk && i < SlotCount; i++) if (box.slots[i] == null || box.slots[i] != slotBs[i]) slotsOk = false;
                v.Check(slotsOk, BoxName + ".slots が " + SlotCount + " 本で添字どおり");
                for (int i = 0; i < SlotCount; i++)
                    v.Check(slotBs[i] != null && slotBs[i].box == box, KD_DogDefs.TreatSlotNameOf(i) + ".box == " + BoxName);
                int dogsN = KD_DogDefs.All.Length;
                v.Check(box.pack != null && box.pack.dogRoots != null && box.pack.dogRoots.Length == dogsN
                    && box.pack.dogWander != null && box.pack.dogWander.Length == dogsN && box.pack.dogFetch != null && box.pack.dogFetch.Length == dogsN,
                    BoxName + ".pack の名簿が犬 " + dogsN + " 匹 (dogRoots / dogWander / dogFetch。dogSlot / dogSlotOutMs / dogEatMs はこの長さで確保される)");
                v.Check(box.eatDuration > 0.01f && box.thanksCooldown > 0.01f, BoxName + ".eatDuration / thanksCooldown > 0 (食べ " + box.eatDuration.ToString("F1") + "s)");
            }
            // マスター固定: KD_TreatBox への SetOwner がコードに無い (古民家の check-sync-anchors.js の要点を 1 行で)
            {
                string src = System.IO.File.Exists(BoxScriptPath) ? System.IO.File.ReadAllText(BoxScriptPath) : "";
                v.Check(src.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(src, @"(?m)^[^/\r\n]*Networking\.SetOwner\s*\("),
                    "KD_TreatBox.cs に Networking.SetOwner の呼び出しが無い (マスター固定)");
            }

            // ---- 犬側 (6 匹) と、裏山の除外の足場 ----
            v.Check(typeof(KD_TreatBox).GetMethod("HillsideDogBusy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null,
                "KD_TreatBox.HillsideDogBusy がある (リード接続中・集合中・フリスビー中を EligibleDog / AllDogsEngaged から除外。IMP-DOG-006 の足場)");
            v.Check(typeof(KD_TreatBox).GetMethod("HillsideAllBusyCause") != null && typeof(KD_TreatPouch).GetMethod("HillsideDogsBusyText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null,
                "全頭が来られない理由を分ける口 (KD_TreatBox.HillsideAllBusyCause → KD_TreatPouch.HillsideDogsBusyText: リード中 / 集合中 / フリスビー中 / 終わり待ち)");
            for (int d = 0; d < KD_DogDefs.All.Length; d++)
            {
                var def = KD_DogDefs.All[d];
                string tag = def.key + ": ";
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out int wCount);
                var wander = (wCount == 1 && wanderGo != null) ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                if (!v.Check(wander != null, tag + def.WanderSystemName + " が取れる")) continue;
                v.Check(box != null && wander.treatBox == box, tag + "wander.treatBox == " + BoxName);
                if (box != null)
                    v.Check(box.thanksCooldown > wander.treatThanksDuration, tag + "thanksCooldown (" + box.thanksCooldown.ToString("F1") + ") > treatThanksDuration (" + wander.treatThanksDuration.ToString("F1") + ")");
                v.Check(wander.treatArriveRadius > 0.01f, tag + "treatArriveRadius > 0");
                v.Check(wander.hillsideLeashes != null && wander.dogIndex == d, tag + "hillsideLeashes 配線・dogIndex == " + d + " (リード接続中の除外が読む。actual " + wander.dogIndex + ")");
                if (box != null && box.pack != null && box.pack.dogWander != null && d < box.pack.dogWander.Length)
                    v.Check(box.pack.dogWander[d] == wander, tag + "pack.dogWander[" + d + "] == " + def.WanderSystemName + " (box の添字 = 犬の添字)");
                if (wander.frisbeeCatch == null) v.Note(tag + "frisbeeCatch が null (ディスクを持たない犬。フリスビー中の除外は掛からない)");
            }

            // ---- 商品 (台帳の 4 種。価格・パック・上限・初期) ----
            {
                int[] prices = { 1, 2, 3, 5 };
                for (int i = 0; i < KD_ShopSetupEditor.TreatProductCount; i++)
                {
                    string name = KD_ShopSetupEditor.TreatItemNameAt(i);
                    var itemGo = KD_SceneObjects.Find(name, out int count);
                    var item = (count == 1 && itemGo != null) ? itemGo.GetComponent<KD_ShopItem>() : null;
                    if (!v.Check(item != null && itemGo.transform.parent != null && itemGo.transform.parent.name == KD_Hillside.ShopCatalogName,
                        KD_Hillside.ShopCatalogName + "/" + name + " が 1 個 (actual " + count + ")")) continue;
                    v.Check(item.productKey == KD_ShopSetupEditor.TreatProductKeyAt(i) && (i >= prices.Length || item.price == prices[i])
                        && item.packCount == 5 && item.stockCap == 20 && item.initialStock == (i == 0 ? 5 : 0) && !item.IsCE(),
                        name + ": " + item.productKey + " " + item.price + "pt / 5 個パック / 上限 20 / 初期 " + (i == 0 ? 5 : 0) + " (actual pack " + item.packCount + " cap " + item.stockCap + " init " + item.initialStock + ")");
                }
            }

            // ---- 手元メニュー側 (段階 2 で必須。詳細は Menu/20 Verify Pouch) ----
            {
                var pouchT = dogs.Find(PouchName);
                var pouch = pouchT == null ? null : pouchT.GetComponent<KD_TreatPouch>();
                if (v.Check(pouch != null, KD_Hillside.DogsRootName + "/" + PouchName + " がある"))
                {
                    for (int i = 0; i < SlotCount; i++)
                        if (slotBs[i] != null) v.Check(slotBs[i].pouch == pouch, KD_DogDefs.TreatSlotNameOf(i) + ".pouch == " + PouchName + " (移譲の知らせ先)");
                    if (pouch.hillsideStage >= 2)
                    {
                        v.Check(box != null && pouch.box == box, PouchName + ".box == " + BoxName + " (段階 2。Menu/7 が配線)");
                        v.Check(pouch.ledger != null && pouch.catalog != null, PouchName + ".ledger / catalog (購入 catalog._RequestBuy → ledger._TryBuyStocked の経路)");
                        v.Check(Mathf.Approximately(pouch.ownerTimeout, 5f) && Mathf.Approximately(pouch.stepTimeout, 8f),
                            PouchName + ".ownerTimeout 5 / stepTimeout 8 (古民家値。actual " + pouch.ownerTimeout.ToString("F1") + " / " + pouch.stepTimeout.ToString("F1") + ")");
                        v.Check(AllSet(pouch.treatBuyButtons, KD_ShopSetupEditor.TreatProductCount), PouchName + ".treatBuyButtons が " + KD_ShopSetupEditor.TreatProductCount + " 本 (Row{i}/Buy)");
                    }
                    else v.Note(PouchName + ".hillsideStage = " + pouch.hillsideStage + " (< 2)。pouch.box と [買う] の検査は段階 2 から (現在 box=" + (pouch.box != null) + ")");
                }
            }

            // ---- NetworkID (同期する器 7 個: 箱 1 + 枠 3 + 実体 3。Menu/19b が振る) ----
            {
                var desc = Object.FindObjectOfType<VRCSceneDescriptor>();
                var ids = new System.Collections.Generic.HashSet<GameObject>();
                int broken = 0;
                if (desc != null && desc.NetworkIDCollection != null)
                    foreach (var pair in desc.NetworkIDCollection) { if (pair == null || pair.gameObject == null) broken++; else ids.Add(pair.gameObject); }
                v.Check(desc != null && broken == 0, "NetworkIDs broken refs == 0 (actual " + broken + ")");
                var synced = new System.Collections.Generic.List<GameObject>();
                if (boxGo != null) synced.Add(boxGo);
                foreach (var g in slotGos) if (g != null) synced.Add(g);
                foreach (var g in bodyGos) if (g != null) synced.Add(g);
                int missing = 0;
                foreach (var g in synced) if (!ids.Contains(g)) { missing++; log.AppendLine("       NetworkID 無し: " + g.name); }
                v.Check(missing == 0, "おやつの同期する器 " + synced.Count + " 個に NetworkID がある (無し " + missing + "。Menu/19b Assign Network IDs)");
            }

            // ---- 純関数の自己テスト (古民家のまま) ----
            if (box == null) { log.AppendLine("  SKIP 純関数の自己テスト (KD_TreatBox が無い。先に 6c Setup Treats)"); v.AddFail(); }
            else
            {
                v.AddFail(SelfTestGate(box, log));
                v.AddFail(SelfTestGive(box, log));
                v.AddFail(SelfTestDispense(box, log));
                v.AddFail(SelfTestClaims(box, log));
            }

            // ---- 食後エフェクト ----
            log.AppendLine("-- 食後エフェクト --");
            v.AddFail(KD_TreatFxSetupEditor.Verify(box, log));
            {
                var fxGo = KD_SceneObjects.Find(KD_DogDefs.TreatFxName, out int fxCount);
                v.Check(fxCount == 1 && fxGo != null && fxGo.transform.parent == dogs, KD_Hillside.DogsRootName + "/" + KD_DogDefs.TreatFxName + " (食後エフェクトの器)");
            }

            log.AppendLine("  SKIP 棚 (KD_ShopShelf_Treat) とその陳列・棚の購入パネル (裏山では対象外: 棚は置かない。購入はメニューの [買う])");
            log.AppendLine("  SKIP 古民家の手元メニューの検査 (VerifyPouch / VerifyHandMenu) (裏山では Menu/20 Verify Pouch・21 Verify Scroll Layout が数える)");
            log.AppendLine("  NOTE 4 人同時 × 枠 3 (T-009)・6 匹の食べ / お礼の見え方・リード中の犬が来ないことは Play / 実機で確認する");
            v.Finish(TreatsVerifyLog);
        }
    }
}
