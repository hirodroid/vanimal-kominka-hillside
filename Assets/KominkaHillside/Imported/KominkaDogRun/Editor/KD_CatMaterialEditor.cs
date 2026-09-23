using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-CAT-001: scene-instance-only cat fur conversion using the dog's Uzumore/fade baseline.
    /// Keep the Variant's existing external materials and textures (measured body: ke 5.mat).
    /// Embedded FBX Standard materials, when present, use the product body/eye fallback first.
    /// Both lts_fur and lts_fur_two are eligible. Never Apply to the purchased prefab.
    /// Derived materials follow the existing dog regeneration workflow beside their source.
    /// Run Distance Fade/2 afterward to collect the cat slots, then Cat/5 and Cat/6.
    /// </summary>
    public static class KD_CatMaterialEditor
    {
        /// <summary>猫の Animator ルート (シーン内で一意)。距離フェードの収集もこの名前で引く。</summary>
        internal const string CatRootName = "KD_Cat";
        /// <summary>購入プレハブ (`neko_v3.fbx`) のインスタンスのルート。材質を触る範囲はこの配下だけ。</summary>
        internal const string CatPrefabRootName = "neko";

        const string MaterialDir = "Assets/TORINOSUKE/猫ギミック/キジネコ/material/";
        const string BodyMaterialPath = MaterialDir + "New Material.mat";
        const string EyeMaterialPath = MaterialDir + "New Material 1.mat";
        const string StockBodyName = "マテリアル";
        const string StockEyeName = "me";

        /// <summary>猫で「素の毛」とみなすシェーダ: lts_fur_two (犬と同じ。小物の `ke 5.mat`) と
        /// lts_fur (製品版の体 `New Material.mat`)。</summary>
        internal static readonly string[] StockFurShaderGuids =
        {
            KD_DogDefs.StockFurShaderGuid,              // lts_fur_two
            "55706696b2bdb5d4d8541b89e17085c8",         // lts_fur
        };

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Cat/4 - Setup Cat Materials")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_CatMaterial] Setup (FBX 内蔵材質を製品版へ → 毛を Uzumore 化して距離フェードを犬の正本へ揃える)");

            bool ok = true;
            int changed = 0;
            var reference = KD_FurMaterialEditor.LoadReference(log);
            if (reference == null) ok = false;

            var root = ok ? ResolvePrefabRoot(log) : null;
            if (root == null) ok = false;

            if (ok)
            {
                var body = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
                var eye = AssetDatabase.LoadAssetAtPath<Material>(EyeMaterialPath);
                if (body == null) { log.AppendLine("FAIL 製品版の体材質が無い (" + BodyMaterialPath + ")"); ok = false; }
                if (eye == null) { log.AppendLine("FAIL 製品版の目材質が無い (" + EyeMaterialPath + ")"); ok = false; }

                if (ok)
                {
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        bool slotChanged = false;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var m = mats[i];
                            if (m == null)
                            {
                                // 犬と同じ理由で黙って通さない: 消えた `_Uzumore.mat` を指したままの override
                                log.AppendLine("   FAIL " + r.name + "[" + i + "] が null (消えた _Uzumore.mat? プレハブの素へ戻してから流す)");
                                ok = false;
                                continue;
                            }

                            // ① FBX 内蔵材質 → 製品版
                            string path = AssetDatabase.GetAssetPath(m);
                            if (path != null && path.ToLowerInvariant().EndsWith(".fbx"))
                            {
                                Material product = m.name == StockBodyName ? body : m.name == StockEyeName ? eye : null;
                                if (product == null)
                                {
                                    log.AppendLine("   FAIL " + r.name + "[" + i + "] 内蔵材質 " + m.name +
                                        " に対応する製品版が分からない (" + path + ")");
                                    ok = false;
                                    continue;
                                }
                                log.AppendLine("   " + r.name + "[" + i + "] 内蔵 " + m.name + " -> " + product.name + " (" + path + ")");
                                m = product;
                                mats[i] = product;
                                slotChanged = true;
                                changed++;
                            }

                            // ② 素の毛 → _Uzumore (フェードは犬の正本へ)
                            string shaderGuid = KD_FurMaterialEditor.ShaderGuid(m);
                            if (System.Array.IndexOf(StockFurShaderGuids, shaderGuid) >= 0)
                            {
                                var uz = KD_FurMaterialEditor.EnsureUzumoreMaterial(m, reference, log);
                                if (uz == null) { ok = false; continue; }
                                mats[i] = uz;
                                slotChanged = true;
                                changed++;
                                log.AppendLine("   " + r.name + "[" + i + "] " + m.name + " -> " + uz.name + " (override on scene instance)");
                            }
                            else if (m.shader == reference.shader)
                            {
                                if (KD_FurMaterialEditor.CopyFade(reference, m, log))
                                {
                                    changed++;
                                    log.AppendLine("   " + r.name + "[" + i + "] " + m.name + " の距離フェードを正本へ揃えた");
                                }
                                else
                                {
                                    log.AppendLine("   " + r.name + "[" + i + "] " + m.name + " (Uzumore。フェード一致)");
                                }
                            }
                            else
                            {
                                log.AppendLine("   " + r.name + "[" + i + "] " + m.name + " shader=" +
                                    (m.shader == null ? "null" : m.shader.name) + " (毛ではない — 触らない)");
                            }
                        }
                        if (slotChanged)
                        {
                            Undo.RecordObject(r, "KD Cat Material");
                            r.sharedMaterials = mats;
                            // 犬と同じ: プレハブインスタンスへの直接代入は override として記録されないことがある (Unity 2022.3)
                            if (PrefabUtility.IsPartOfPrefabInstance(r))
                                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                            EditorUtility.SetDirty(r);
                        }
                    }
                }
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("changed=" + changed);
            log.AppendLine("scene not saved yet");
            if (ok) log.AppendLine("NEXT: Distance Fade/2 - Setup を流して猫のスロットを配線する");
            log.AppendLine(ok ? "SETUP RESULT: PASS" : "SETUP RESULT: FAIL");
            KD_VerifyLog.Write("Logs/imp-cat-001-materials-setup.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Cat/6 - Verify Cat Materials")]
        public static void VerifyMenu()
        {
            var log = new KD_VerifyLog("[KD_CatMaterial] Verify");
            Verify(log);
            log.Finish("Logs/imp-cat-001-materials-verify.txt");
        }

        /// <summary>猫ユニットの Verify (`Cat/5 - Verify Cat Unit`) からも呼ぶ。
        /// 呼び手は自分の <see cref="KD_VerifyLog"/> を渡すだけでよい (見出し・結果行は呼び手が持つ)。</summary>
        internal static void Verify(KD_VerifyLog log)
        {
            log.Info("-- cat materials (IMP-CAT-001) --");
            var root = ResolvePrefabRoot(log.Text);
            if (!log.Check(root != null, CatRootName + " 配下の " + CatPrefabRootName + " が解決できる")) return;

            var reference = KD_FurMaterialEditor.LoadReference(log.Text);
            if (!log.Check(reference != null, "距離フェードの正本 (コーギー ke_Uzumore.mat) が引ける")) return;

            var uzRenderers = new List<Renderer>();
            var uzSlots = new List<int>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                var source = PrefabUtility.GetCorrespondingObjectFromSource(r);
                var sourceMats = source == null ? null : source.sharedMaterials;
                if (sourceMats != null)
                    log.Check(mats.Length == sourceMats.Length, r.name + " の材質スロット数を維持");
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    string tag = r.name + "[" + i + "] ";
                    if (!log.Check(m != null, tag + "が null ではない")) continue;
                    var stock = sourceMats != null && i < sourceMats.Length ? sourceMats[i] : null;
                    if (stock != null && (System.Array.IndexOf(StockFurShaderGuids, KD_FurMaterialEditor.ShaderGuid(stock)) >= 0
                        || (AssetDatabase.GetAssetPath(stock).ToLowerInvariant().EndsWith(".fbx") && stock.name == StockBodyName)))
                        log.Check(m.shader == reference.shader, tag + "元の毛スロットがUzumoreに変換済み");
                    string path = AssetDatabase.GetAssetPath(m);
                    log.Check(path == null || !path.ToLowerInvariant().EndsWith(".fbx"),
                        tag + m.name + " が FBX 内蔵材質のままではない (Cat/4 を流す。actual " + path + ")");
                    log.Check(System.Array.IndexOf(StockFurShaderGuids, KD_FurMaterialEditor.ShaderGuid(m)) < 0,
                        tag + m.name + " が素の毛シェーダのままではない (Cat/4 を流す)");
                    if (m.shader != reference.shader) continue;
                    uzRenderers.Add(r); uzSlots.Add(i);
                    string why;
                    log.Check(KD_FurMaterialEditor.FadeMatches(reference, m, out why),
                        tag + m.name + " の距離フェード == 正本" + (why == null ? "" : " (" + why + ")"));
                    log.Check(m == reference || path.EndsWith("_Uzumore.mat"),
                        tag + "は `<名前>_Uzumore.mat` (actual " + path + ")");
                }
            }
            log.Check(uzRenderers.Count >= 1, "猫に Uzumore の毛が 1 スロット以上 (actual " + uzRenderers.Count + ")");

            // 距離フェードの配線 — 猫のスロットが KD_DistanceFadeSettings に入っているか
            int count;
            var settingsGo = KD_SceneObjects.Find(KD_DistanceFadeSettingsEditor.SystemName, out count);
            var settings = settingsGo == null ? null : settingsGo.GetComponent<KD_DistanceFadeSettings>();
            if (!log.Check(count == 1 && settings != null,
                "距離フェード設定が一意 (count=" + count + ") — Distance Fade/2 - Setup")) return;
            if (!log.Check(settings.targetRenderers != null && settings.targetSlots != null,
                "距離フェードの配列が設定済み")) return;
            log.Check(settings.targetRenderers.Length == settings.targetSlots.Length,
                "距離フェードのRenderer/slot配列長が一致");
            int wired = 0;
            for (int k = 0; k < uzRenderers.Count; k++)
            {
                bool found = false;
                int n = Mathf.Min(settings.targetRenderers.Length, settings.targetSlots.Length);
                for (int j = 0; j < n; j++)
                    if (settings.targetRenderers[j] == uzRenderers[k] && settings.targetSlots[j] == uzSlots[k]) { found = true; break; }
                if (found) wired++;
            }
            bool allWired = wired == uzRenderers.Count;
            log.Check(allWired, "猫の Uzumore スロットが距離フェードに全部配線されている (" + wired + "/" + uzRenderers.Count + ")");
            if (!allWired) log.Note("Distance Fade/2 - Setup を再実行する (猫のスロットは Collect が拾う)");
        }

        /// <summary>`KD_Cat` (一意) の配下から購入プレハブのインスタンスのルート `neko` を引く。
        /// 名前解決は <see cref="KD_SceneObjects.Find"/> 一本 — パスでは引かない (scene-workflow.md)。</summary>
        static GameObject ResolvePrefabRoot(StringBuilder log)
        {
            int count;
            var cat = KD_SceneObjects.Find(CatRootName, out count);
            if (count != 1 || cat == null)
            {
                log.AppendLine("FAIL " + CatRootName + " がシーン内に " + count + " 個 (1 個が前提。Cat の Setup を先に流す)");
                return null;
            }
            GameObject found = null;
            int matches = 0;
            foreach (var t in cat.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != CatPrefabRootName) continue;
                matches++;
                if (found == null) found = t.gameObject;
            }
            if (matches != 1 || found == null)
            {
                log.AppendLine("FAIL " + CatRootName + " の配下に " + CatPrefabRootName + " が " + matches + " 個 (1 個が前提)");
                return null;
            }
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(found))
            {
                log.AppendLine("FAIL " + CatPrefabRootName + " が購入プレハブのインスタンスのルートではない (階層が違う?)");
                return null;
            }
            log.AppendLine("cat prefab root: " + KD_SceneObjects.PathOf(found));
            return found;
        }
    }
}
