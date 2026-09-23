using System.IO;
using UdonSharp;
using UdonSharpEditor;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using KominkaDogRun.DebugTools;
using KominkaDogRun.Editor;  // IMP-INF-003: KD_SceneObjects

namespace KominkaDogRun.EditorTools
{
    // Debug purchases use a separate ledger and namespace, including earned/spent points.
    public static class KD_ProgressDebugHudEditor
    {
        const string PanelName = KD_DogDefs.ProgressDebugPanelName;   // IMP-INF-003: 正本は KD_DogDefs
        const string SystemName = KD_DogDefs.ProgressSystemName;
        const string TestUnlockId = "test.unlock";
        const string TestConsumableId = "test.consumable";

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Attach Progress HUD")]
        public static void Attach()
        {
            var existing = KD_SceneObjects.Find(PanelName);
            if (existing != null) Undo.DestroyObjectImmediate(existing); // 配線が古いまま残るのを避ける

            var sysGo = KD_SceneObjects.Find(SystemName);
            if (sysGo == null)
            {
                Debug.LogError("[ProgressHUD] " + SystemName + " がシーンに無い。"
                    + "先に Tools/KominkaDogRun/Progress System/2 - Setup を流すこと");
                return;
            }
            var ledger = sysGo.GetComponent<KD_ProgressLedger>();
            var ledgerUdon = sysGo.GetComponent<VRC.Udon.UdonBehaviour>();
            if (ledger == null || ledgerUdon == null)
            {
                Debug.LogError("[ProgressHUD] KD_ProgressLedger / UdonBehaviour が "
                    + SystemName + " に無い (ledger=" + (ledger != null)
                    + " udon=" + (ledgerUdon != null) + ")");
                return;
            }

            // ---- 盤を作る ----
            var go = new GameObject(PanelName);
            Undo.RegisterCreatedObjectUndo(go, "Attach progress HUD");
            KD_SceneObjects.PlaceIfNew(go, KD_DogDefs.DebugPath);   // IMP-INF-003: 開発専用

            // **スポーンの向きを使う** — ワールド前方だと壁の中に入り得る。
            // 入室した瞬間に視界へ入っていないと「盤が出ていない」と誤解する
            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            Transform spawn = desc != null && desc.spawns != null && desc.spawns.Length > 0
                ? desc.spawns[0]
                : null;
            Vector3 basePos = spawn != null ? spawn.position : Vector3.zero;
            Vector3 ahead = spawn != null ? spawn.forward : Vector3.forward;
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 0.0001f) ahead = Vector3.forward;
            ahead = ahead.normalized;
            go.transform.position = basePos + Vector3.up * 1.6f + ahead * 1.2f;

            var tm = go.AddComponent<TextMesh>();
            tm.text = "(waiting for play mode)";
            tm.anchor = TextAnchor.UpperLeft;
            tm.alignment = TextAlignment.Left;
            // 大きいフォントサイズ + 小さいスケールでないと滲む (他の盤と同じ作法)
            tm.fontSize = 64;
            tm.characterSize = 0.011f;
            tm.lineSpacing = 0.9f;
            tm.color = Color.white;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
            {
                tm.font = font;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = font.material;
            }

            var testHost = new GameObject("TestCommerceLedger"); testHost.transform.SetParent(go.transform, false);
            ledger = testHost.AddUdonSharpComponent<KD_ProgressLedger>();
            ledger.storageNamespace = KD_ProgressLedger.TestNamespace;
            ledgerUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(ledger);
            ledgerUdon.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            var hud = go.AddComponent<KD_ProgressDebugHud>();
            hud.ledgerUdon = ledgerUdon;
            hud.ledgerProxy = ledger;
            hud.label = tm;

            // ---- テスト用ショップ 2 個 (本番の KD_ShopItem) ----
            //
            // 盤の手前・腰の高さへ横並びに置く。Interact できる距離に入るのが要件
            var right = Vector3.Cross(Vector3.up, ahead).normalized;
            var items = new System.Collections.Generic.List<KD_ShopItem>();
            var itemUdons = new System.Collections.Generic.List<VRC.Udon.UdonBehaviour>();
            var itemLabels = new System.Collections.Generic.List<string>();

            var buy = MakeShopItem(go.transform, "TestShop_Unlock_10pt",
                go.transform.position - Vector3.up * 0.55f - right * 0.25f,
                ledger, price: 10, consumable: false, productKey: TestUnlockId);
            var eat = MakeShopItem(go.transform, "TestShop_Consumable_3pt",
                go.transform.position - Vector3.up * 0.55f + right * 0.25f,
                ledger, price: 3, consumable: true, productKey: TestConsumableId);
            ledger.products = new[] { buy, eat };
            KD_ProductsEditor.Copy(ledger);

            if (buy != null) { items.Add(buy); itemUdons.Add(UdonSharpEditorUtility.GetBackingUdonBehaviour(buy)); itemLabels.Add("買い切り(青)"); }
            if (eat != null) { items.Add(eat); itemUdons.Add(UdonSharpEditorUtility.GetBackingUdonBehaviour(eat)); itemLabels.Add("消耗(緑)  "); }

            hud.shopProxies = items.ToArray();
            hud.shopUdons = itemUdons.ToArray();
            hud.shopLabels = itemLabels.ToArray();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[ProgressHUD] attached at " + go.transform.position.ToString("F2")
                + " / shop items " + items.Count
                + " / 1pt=" + hud.accelMinuteSeconds + "s (加速)"
                + " / log=" + hud.logPath
                + " — Play / ClientSim でのみ動作。確認後は Detach すること。"
                + "**製品どおりの 1pt=60s で見たいときは盤の accelerate を切る**");
        }
        static KD_ShopItem MakeShopItem(Transform parent, string name, Vector3 pos,
            KD_ProgressLedger ledger, int price, bool consumable, string productKey)
        {
            // 見えて触れる必要があるので primitive を使う (Collider + Renderer が付く)
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.18f;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                // 買い切り = 青 / 消耗 = 緑。**盤の表示と色を対応させる**
                var mat = new Material(mr.sharedMaterial);
                mat.color = consumable ? new Color(0.3f, 0.8f, 0.4f) : new Color(0.3f, 0.5f, 0.9f);
                mr.sharedMaterial = mat;
            }

            KD_ShopItem item;
            try
            {
                item = go.AddUdonSharpComponent<KD_ShopItem>();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ProgressHUD] KD_ShopItem を付けられない: " + e.Message
                    + " — 先に Progress System/1 - Create Program Assets を流して force refresh すること");
                Object.DestroyImmediate(go);
                return null;
            }

            item.ledger = ledger;
            item.price = price;
            item.consumable = consumable;
            item.productKey = productKey;
            // 別番号のままにすると二重管理が黙って残るので、Shop の Verify が一致を機械検査する
            item.categoryKey = "toy";
            item.packCount = consumable ? 1 : 0;
            item.initialStock = 0;
            item.stockCap = 0;
            item.ceListingId = "";
            // listener は配線しない — 実体を出すのは棚 (IMP-FET-009 / IMP-TRT-001) の仕事で、
            // ここで見たいのは「払えたか」だけ。**未配線でも購入は成立する**
            item.listener = null;

            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(item);
            if (ub != null && ub.SyncMethod != VRC.SDKBase.Networking.SyncType.None)
                ub.SyncMethod = VRC.SDKBase.Networking.SyncType.None;

            try { UdonSharpEditorUtility.CopyProxyToUdon(item); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ProgressHUD] CopyProxyToUdon failed (" + name + "): " + e.Message
                    + " — force refresh 後に Attach を流し直すこと");
            }
            EditorUtility.SetDirty(item);
            return item;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Detach Progress HUD")]
        public static void Detach()
        {
            var go = KD_SceneObjects.Find(PanelName);
            if (go == null)
            {
                Debug.Log("[ProgressHUD] 盤は付いていない");
                return;
            }
            // テスト用ショップは盤の子なので一緒に消える
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[ProgressHUD] detached (テスト用ショップも一緒に消えた)");
        }
        // Fresh persistence tests use Test Harness storage isolation and recovery.
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Debug/Clear Progress Log")]
        public static void ClearLog()
        {
            const string path = "Logs/progress-debug.txt";
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log("[ProgressHUD] cleared " + path);
            }
            else
            {
                Debug.Log("[ProgressHUD] ログはまだ無い: " + path);
            }
        }
    }
}
