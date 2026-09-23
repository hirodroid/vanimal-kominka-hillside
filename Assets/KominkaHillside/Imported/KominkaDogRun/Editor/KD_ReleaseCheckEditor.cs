using System.IO;
using System.Linq;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// 公開前チェックの機械化 (破壊的監査 v2 RC-14 = A-12)。
    ///
    /// **通常の Verify に混ぜない** (トリアージ視点 2 反論 5) — 開発中は
    /// `KD_Debug` に物が居るのが正常なので、混ぜると常時 FAIL になり
    /// 検査全体への信頼を毀損する。公開直前にだけ実行する専用メニュー。
    /// `docs/development/release-checklist.md` から参照される。
    ///
    /// 動機となった実害 (2026-08-28 監査 FUNC-14/BUG-01):
    /// `KD_Debug` 配下の**確認なし即購入テスト商品 2 個** (shelf 未配線の
    /// `KD_ShopItem`) が active のまま残っていた。デバッグ HUD (素の MonoBehaviour)
    /// はアップロードで剥がれるが、**UdonSharpBehaviour は剥がれずそのまま出荷される。**
    /// </summary>
    public static class KD_ReleaseCheckEditor
    {
        const string LogFile = "Logs/kd-release-check.txt";

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Release Check/1 - Verify Release Readiness")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ReleaseCheck] 公開前チェック (機械検査ぶん)");
            log.AppendLine("実行日時はログファイルのタイムスタンプを見ること");
            log.AppendLine();
            int fail = 0;

            foreach (var ledger in KD_ProductsEditor.SceneComponents<KD_ProgressLedger>())
                Check(ref fail, log, ledger.storageNamespace == KD_ProgressLedger.ProductionNamespace,
                    ledger.name + " uses production commerce storage (including inactive objects)");
            foreach (var item in KD_ProductsEditor.SceneComponents<KD_ShopItem>())
            {
                bool valid = true;
                try { KD_ProductDefinitions.ValidateItem(item); }
                catch (System.Exception) { valid = false; }
                Check(ref fail, log, valid && !item.productKey.StartsWith("test."),
                    item.name + " is a registered production product");
            }
            // IMP-PRG-010: ギフトコードの定義表が発行済み (hash != 0・test 無し) で、面に配線されている
            bool giftTable = true;
            try { KD_GiftCodeDefinitions.Validate(); } catch (System.Exception) { giftTable = false; }
            Check(ref fail, log, giftTable, "gift code table valid (no test keys, every hash issued)");
            var giftPanels = KD_ProductsEditor.SceneComponents<KD_GiftCodePanel>();
            Check(ref fail, log, giftPanels.Length == 1, "exactly one KD_GiftCodePanel in the scene (found " + giftPanels.Length + ")");
            foreach (var gift in giftPanels)
                Check(ref fail, log, gift.codeHashes != null && gift.codeKeys != null && gift.codePoints != null
                    && gift.codeHashes.SequenceEqual(KD_GiftCodeDefinitions.All.Select(r => r.hash))
                    && gift.codeKeys.SequenceEqual(KD_GiftCodeDefinitions.All.Select(r => r.codeKey))
                    && gift.codePoints.SequenceEqual(KD_GiftCodeDefinitions.All.Select(r => r.points)),
                    gift.name + " carries exactly the definition table (" + KD_GiftCodeDefinitions.All.Length + " codes: keys / hashes / points)");

            // ---- 1. KD_Debug が空か非アクティブ (CLAUDE.md の不変条件の機械化) ----
            int debugCount;
            var debugRoot = KD_SceneObjects.Find("KD_Debug", out debugCount);
            if (debugRoot == null)
            {
                Check(ref fail, log, true, "KD_Debug が存在しない (= 空と同じ)");
            }
            else if (!debugRoot.activeInHierarchy)
            {
                Check(ref fail, log, true, "KD_Debug が非アクティブ");
            }
            else
            {
                // **active な UdonBehaviour を名指しで数える。**
                // MonoBehaviour の計器はアップロードで剥がれるので対象外 —
                // 剥がれずに出荷されるのは Udon だけ
                var udons = debugRoot.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(false);
                Check(ref fail, log, udons.Length == 0,
                    "KD_Debug 配下に active な UdonBehaviour が 0 (actual " + udons.Length + ")");
                foreach (var u in udons)
                    log.AppendLine("         → " + KD_SceneObjects.PathOf(u.gameObject));
                if (udons.Length > 0)
                    log.AppendLine("         KD_Debug を非アクティブにするか、中身を削除すること");
            }

            // ---- 2. shelf 未配線の KD_ShopItem が active で存在しない ----
            // shelf 未配線の Interact は**確認パネル無しの即購入** (pt が即減る)。
            // KD_Debug の外に置かれても捕まえられるよう、シーン全域を見る
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var item in root.GetComponentsInChildren<KD_ShopItem>(true))
                {
                    if (!item.gameObject.activeInHierarchy) continue;
                    Check(ref fail, log, item.shelf != null || KD_ProductsEditor.HasAccessoryMenuEntry(item),
                        "KD_ShopItem " + KD_SceneObjects.PathOf(item.gameObject)
                            + " に shelf が配線されている (未配線 = 確認なし即購入)");
                }
            }

            // ---- 3. 開いたまま保存された板が無い (BUG-CAM-002 の後継) ----
            //
            // **検査対象を差し替えた** (2026-09-03。IMP-PET-007)。
            // 旧 `KD_ShopMenu_PetFx` は廃止したので、**あの名前を検査し続けると
            // 「存在しないものを検査して PASS」**になる (npc-shop-menu §7 の申し送り)。
            //
            // いま「開いたまま保存すると写真に写り込む板」は**手元メニュー 1 枚だけ**。
            // あちらは自分で開く面なので既定は必ず閉じている
            {
                int oldCount;
                KD_SceneObjects.Find("KD_ShopMenu_PetFx", out oldCount);
                Check(ref fail, log, oldCount == 0,
                    "旧 KD_ShopMenu_PetFx が残っていない (actual " + oldCount
                    + ") — 撫でFXは手元メニューへ移った。Shop/2 - Setup Shop を流すこと");

                int pouchCount;
                var pouchGo = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName, out pouchCount);
                var pouch = (pouchCount == 1 && pouchGo != null)
                    ? pouchGo.GetUdonSharpComponent<KD_TreatPouch>() : null;
                if (pouch != null && pouch.panelRoot != null)
                {
                    Check(ref fail, log, !pouch.panelRoot.activeSelf,
                        "手元メニューの板が既定で非アクティブ "
                        + "(開いたまま保存すると入室した瞬間から浮き、写真にも写る)");
                }

                // ---- 3b. Touch の起動ボタン (IMP-INF-006 F-02。2026-09-07) ----
                //
                // **板 (項目 3) と同じ型の壊れ方をするのに、ここでは見ていなかった。**
                // あれは Screen-Space Overlay の Canvas なので、開いたまま保存すると
                // **VR / Desktop の画面にも 1 枚出る** (板と違い世界の中ではなく画面に貼り付く)。
                // `debugForceTouch` は項目 4 / 5 と同じ「**見た目に出ないデバッグフラグ**」で、
                // true のまま公開すると入力方式に関係なく全端末へ起動ボタンが出る。
                //
                // **Treat System/3 - Verify も同じ 2 つを見ているが、あれは開発中に流す検査。**
                // 公開直前に必ず流すのはこのメニューだけなので、出荷を止める側にも置く
                // (項目 2 の「shelf 未配線の KD_ShopItem」を両方で見ているのと同じ理由)。
                if (pouch != null)
                {
                    if (pouch.touchLauncher != null)
                    {
                        Check(ref fail, log, !pouch.touchLauncher.activeSelf,
                            "Touch の起動ボタンが既定で非アクティブ "
                            + "(有効のままだと VR / Desktop の画面にも出る。"
                            + "出すのは実行時の Touch 判定だけ)");
                    }
                    Check(ref fail, log, !pouch.debugForceTouch,
                        "KD_TreatPouch.debugForceTouch が false "
                        + "(エディタ確認用の上書き。true のまま公開すると全端末に起動ボタンが出る)");
                }
            }

            // ---- 4. KD_DogStopAnchor に計器が残っていない ----
            // logToConsole は公開ビルドでも 0.25 秒ごとに [KDNET] ログを吐き続ける。
            // 板と違い見た目に出ないので、KD_Debug 検査 (項目 1) では絶対に捕まらない。
            // 非アクティブでも出荷されるので inactive 込みで全域を見る (項目 2 と違い skip しない)
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var anchor in root.GetComponentsInChildren<KD_DogStopAnchor>(true))
                {
                    Check(ref fail, log, !anchor.logToConsole && anchor.debugText == null,
                        "KD_DogStopAnchor " + KD_SceneObjects.PathOf(anchor.gameObject)
                            + " の計器が下りている (logToConsole=" + anchor.logToConsole
                            + ", debugText=" + (anchor.debugText != null) + ")"
                            + " — Stop Anchor/5 - Remove Debug Display で下ろす");
                }

                // ---- 5. KD_DogPetController の debugText が外れている (理由は項目 4 と同じ) ----
                foreach (var pet in root.GetComponentsInChildren<KD_DogPetController>(true))
                {
                    Check(ref fail, log, pet.debugText == null,
                        "KD_DogPetController " + KD_SceneObjects.PathOf(pet.gameObject)
                            + " の debugText が外れている — Pet System/10 - Remove Debug Display で下ろす");
                }
            }

            // ---- 5b. 写真ビューアの Panel が閉じている (BUG-CAM-003 案 B) ----
            // 開いたまま保存すると、入室した瞬間から頭の前に黒い板が出る (項目 3 と同じ型)
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var v in root.GetComponentsInChildren<KD_PhotoViewer>(true))
                {
                    Check(ref fail, log, v.panelRoot == null || !v.panelRoot.activeSelf,
                        "KD_PhotoViewer " + KD_SceneObjects.PathOf(v.gameObject)
                            + " の Panel が既定で非アクティブ");
                }
            }

            // ---- 6. 吸着プローブが OFF (BUG-CAM-003) ----
            // 実機切り分け用に写真の全画面表示を 4 本の帯へ差し替える目印。
            // 材質の float なので KD_Debug 検査 (項目 1) では捕まらず、
            // 効いていることに気付けるのは**実機でカメラを板へ寄せた人だけ**。
            // 切り分け中は ON が正常なので Photo System の Verify には入れず、ここで止める
            int probeOn = KD_PhotoSetupEditor.CountDebugProbeOn();
            Check(ref fail, log, probeOn == 0,
                "吸着プローブが OFF (ON の材質 " + probeOn + " 枚)"
                    + " — Photo System/5 - Debug 吸着プローブ OFF で戻す");

            // ---- 7. 開発用の CE 解放が戻されている (2026-09-04) ----
            // `Shop/4 - CE Unlock (dev)` は CE 品の `ceListingId` を空にして
            // 無料品として振る舞わせる。**このまま公開すると課金品が無料で配られる。**
            // 解放中は `IsCE()` が false になるので Shop Verify の
            // 「Listing が仮のまま」WARN からも消える = 黙って静かになる。だからここで止める
            string ceNames;
            int ceUnlocked = KD_ShopSetupEditor.CountCeUnlocked(out ceNames);
            Check(ref fail, log, ceUnlocked == 0,
                "開発用の CE 解放が戻っている (解放中 " + ceUnlocked + " 件"
                    + (ceUnlocked > 0 ? ": " + ceNames : "") + ")"
                    + " — Shop/5 - CE Relock で戻す");

            // ---- 8. デバッグ HUD の MonoBehaviour がシーンのどこにも残っていない (IMP-INF-019 X13) ----
            //
            // 項目 1 は KD_Debug 配下の **Udon** だけを数える (MonoBehaviour はアップロードで剥がれるため)。
            // だが Editor メニューで AddComponent した HUD (`KominkaDogRun.DebugTools.*Hud` /
            // `KD_DogLoadSampler`) は KD_Debug の外にも付くので、「残留を防げる」とは言えなかった
            // (Codex 指摘 2026-09-13)。出荷には載らないが、付けたまま保存すると Play のたびに
            // 計器が動いて数字が汚れ、シーンの差分にも残る。非アクティブ含めて全域を見る
            {
                int hudCount = 0;
                var hudNames = new StringBuilder();
                foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                {
                    foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null) continue;
                        var t = mb.GetType();
                        bool hud = t.Namespace == "KominkaDogRun.DebugTools" || t.Name == "KD_DogLoadSampler";
                        if (!hud) continue;
                        hudCount++;
                        hudNames.AppendLine("         → " + t.Name + " on " + KD_SceneObjects.PathOf(mb.gameObject));
                    }
                }
                Check(ref fail, log, hudCount == 0,
                    "デバッグ HUD (DebugTools / KD_DogLoadSampler) がシーンのどこにも無い (actual " + hudCount + ")");
                if (hudCount > 0)
                {
                    log.Append(hudNames.ToString());
                    log.AppendLine("         Debug/Detach Dog Play HUD / Detach Progress HUD / Dog Load/2 - Disarm で外すこと");
                }
            }

            // IMP-ENV-021: Reference Camera の Near / Far。犬に顔を近づけても切れないよう Near 0.01、Far は遠景の山 (271〜308m) が収まる 350。
            // クライアントは Reference Camera の値を視点へ写すので、Inspector の値がそのまま体験になる (実測 2026-09-20: 0.3 のとき撫で中に犬の顔が消えた)
            {
                var descriptor = UnityEngine.Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
                var refCam = descriptor == null || descriptor.ReferenceCamera == null ? null : descriptor.ReferenceCamera.GetComponent<Camera>();
                Check(ref fail, log, refCam != null, "VRCSceneDescriptor.ReferenceCamera に Camera が割り当てられている");
                if (refCam != null)
                    Check(ref fail, log, refCam.nearClipPlane <= 0.01f + 1e-4f && Mathf.Approximately(refCam.farClipPlane, 350f),
                        "Reference Camera near 0.01 / far 350 (IMP-ENV-021, actual " + refCam.nearClipPlane + " / " + refCam.farClipPlane + ")");
            }

            log.AppendLine();
            log.AppendLine("⚠ ここで見るのは機械化できた 9 項目だけ。残り (未使用パッケージ /");
            log.AppendLine("  ライセンス表記 / ブロッカー Issue / 人数を上げた確認) は");
            log.AppendLine("  docs/development/release-checklist.md を人間が確認すること");
            log.AppendLine();
            log.AppendLine(fail == 0 ? "RELEASE CHECK: PASS" : "RELEASE CHECK: FAIL (" + fail + " checks)");
            Finish(log);
        }

        static bool Check(ref int fail, StringBuilder log, bool ok, string label)
        {
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + label);
            if (!ok) fail++;
            return ok;
        }

        static void Finish(StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile));
            File.WriteAllText(LogFile, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
