using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-DOG-020 (2026-09-05): **毛マテリアルを Uzumore にして距離フェードをコーギーへ揃える** —
    /// 購入アセットを編集する唯一の例外 (runbook §1.1 / IMP-DOG-018) を Editor メニューにしたもの。
    ///
    /// これまでは手作業だった:
    /// 1. 素の lilToon 毛マテリアル (`ke.mat` 等) を複製して `<名前>_Uzumore.mat` にする
    /// 2. シェーダを Uzumore へ (テクスチャ・色は名前が同じなので引き継がれる)
    /// 3. `_DistanceFade` 系 5 値を**コーギーの `ke_Uzumore.mat` の実体から**コピーする (手で写さない)
    /// 4. シーン上の犬の **karada** の材質スロットを差し替える
    ///    (**プレハブは触らない** — コーギーも 2026-09-05 時点でシーンインスタンスの override で差し替わっている)
    ///
    /// 対象は <see cref="KD_DogRig.face"/> (= karada) のスロットだけ。`ha.*` (口) や `me` (目) は
    /// 素のまま (IMP-DOG-018「毛以外は対象外」と同じ線。4 犬種の現状と一致する)。
    ///
    /// **成果物 (`_Uzumore.mat`) は `.gitignore` の内側** (購入アセットのフォルダに置く)。
    /// 環境を作り直したらこのメニューを流し直せば再現する — それがこのメニューの存在理由。
    /// 距離フェードの値の正本はコーギーの実体で、それが消えたときだけ runbook §1.1 の表から入れ直す。
    /// </summary>
    public static class KD_FurMaterialEditor
    {
        /// <summary>複製・照合する距離フェードのプロパティ (IMP-DOG-018 §4 の 5 値)</summary>
        static readonly string[] VectorProps = { "_DistanceFade" };
        static readonly string[] ColorProps = { "_DistanceFadeColor", "_DistanceFadeRimColor" };
        static readonly string[] FloatProps = { "_DistanceFadeMode", "_DistanceFadeRimFresnelPower" };

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Fur Material/1 - Setup Fur Material")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_FurMaterial] Setup (毛を Uzumore 化して距離フェードをコーギーへ揃える)");

            var reference = LoadReference(log);
            if (reference == null)
            {
                log.AppendLine("RESULT: FAIL");
                Finish("Logs/imp-dog-020-fur-setup.txt", log);
                return;
            }

            bool ok = true;
            int changed = 0;
            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");
                int count;
                var dog = KD_SceneObjects.Find(def.DogName, out count);
                if (count != 1 || dog == null)
                {
                    log.AppendLine("   FAIL " + def.DogName + " がシーン内に " + count + " 個 (1 個が前提)");
                    ok = false;
                    continue;
                }
                var rig = KD_DogRig.Resolve(dog);
                if (rig.face == null)
                {
                    log.AppendLine("   FAIL karada (表情 Renderer) が解決できない");
                    ok = false;
                    continue;
                }

                var mats = rig.face.sharedMaterials;
                bool slotChanged = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null)
                    {
                        // 環境を作り直すと `_Uzumore.mat` (gitignore の内側) が消え、シーンの override が null を指す。
                        // **黙って通さない** — プレハブ側の素の材質へ戻してから、下の通常経路で Uzumore 化し直す
                        // (Codex レビュー 2026-09-05: 欠損を WARN で流すと「メニューを流せば再現する」が嘘になる)
                        var stock = StockMaterialFromPrefab(rig.face, i);
                        if (stock == null)
                        {
                            log.AppendLine("   FAIL slot[" + i + "] が null で、プレハブ側の素の材質も引けない");
                            ok = false;
                            continue;
                        }
                        log.AppendLine("   slot[" + i + "] null (消えた _Uzumore.mat?) → プレハブの素 " + stock.name + " から作り直す");
                        m = stock;
                    }
                    string shaderGuid = ShaderGuid(m);

                    if (shaderGuid == KD_DogDefs.StockFurShaderGuid)
                    {
                        // 素の lilToon 毛 → 隣に _Uzumore を作る (あれば再利用)
                        var uz = EnsureUzumoreMaterial(m, reference, log);
                        if (uz == null) { ok = false; continue; }
                        mats[i] = uz;
                        slotChanged = true;
                        changed++;
                        log.AppendLine("   slot[" + i + "] " + m.name + " -> " + uz.name + " (override on scene instance)");
                    }
                    else if (m.shader == reference.shader)
                    {
                        // 既に Uzumore。フェードの値だけ正本へ揃える (IMP-DOG-018 の再適用 = 冪等)
                        if (m == reference)
                        {
                            log.AppendLine("   slot[" + i + "] " + m.name + " = 正本 (コーギー)");
                        }
                        else if (CopyFade(reference, m, log))
                        {
                            changed++;
                            log.AppendLine("   slot[" + i + "] " + m.name + " の距離フェードを正本へ揃えた");
                        }
                        else
                        {
                            log.AppendLine("   slot[" + i + "] " + m.name + " (Uzumore。フェード一致)");
                        }
                    }
                    else
                    {
                        log.AppendLine("   slot[" + i + "] " + m.name + " shader=" + m.shader.name + " (毛ではない — 触らない)");
                    }
                }
                if (slotChanged)
                {
                    Undo.RecordObject(rig.face, "KD Fur Material");
                    rig.face.sharedMaterials = mats;
                    // プレハブインスタンスへの直接代入は override として記録されないことがある
                    // (Unity 2022.3 の仕様。Codex レビュー 2026-09-05)。保存・再読込で差し替えを失わないよう明示的に記録する
                    if (PrefabUtility.IsPartOfPrefabInstance(rig.face))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(rig.face);
                    EditorUtility.SetDirty(rig.face);
                }
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("changed=" + changed);
            log.AppendLine(ok ? "RESULT: PASS (scene not saved yet)" : "RESULT: FAIL");
            Finish("Logs/imp-dog-020-fur-setup.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Fur Material/2 - Verify Fur Material")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_FurMaterial] Verify");
            int fail = 0;
            System.Action<bool, string> Check = (cond, msg) =>
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + msg);
                if (!cond) fail++;
            };

            var reference = LoadReference(log);
            Check(reference != null, "正本 (コーギー ke_Uzumore.mat guid " + KD_DogDefs.FurReferenceMaterialGuid + ") が引ける");
            if (reference != null)
            {
                // 正本自体が runbook §1.1 の値であること (gitignore の内側なので環境を作り直すと消える)
                var fade = reference.GetVector("_DistanceFade");
                Check(Mathf.Abs(fade.x - 0.18f) < 1e-4f && Mathf.Abs(fade.y - 0.05f) < 1e-4f && Mathf.Abs(fade.z - 0.95f) < 1e-4f,
                    "正本の _DistanceFade == (0.18, 0.05, 0.95) — runbook §1.1 の表 (actual " + fade.ToString("F3") + ")");
            }

            foreach (var def in KD_DogDefs.All)
            {
                string tag = def.key + ": ";
                int count;
                var dog = KD_SceneObjects.Find(def.DogName, out count);
                Check(count == 1 && dog != null, tag + def.DogName + " exists exactly once (actual " + count + ")");
                if (dog == null) continue;
                var rig = KD_DogRig.Resolve(dog);
                Check(rig.face != null, tag + "karada が解決できる");
                if (rig.face == null || reference == null) continue;

                int uzumore = 0;
                var mats = rig.face.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) { Check(false, tag + "slot[" + i + "] が null"); continue; }
                    string shaderGuid = ShaderGuid(m);
                    Check(shaderGuid != KD_DogDefs.StockFurShaderGuid,
                        tag + "slot[" + i + "] " + m.name + " が素の lilToon 毛のままではない (Fur Material/1 を流す)");
                    if (m.shader != reference.shader) continue;
                    uzumore++;
                    Check(FadeMatches(reference, m, out string why),
                        tag + "slot[" + i + "] " + m.name + " の距離フェード == 正本" + (why == null ? "" : " (" + why + ")"));
                    string path = AssetDatabase.GetAssetPath(m);
                    Check(m == reference || path.EndsWith("_Uzumore.mat"),
                        tag + "slot[" + i + "] は `<名前>_Uzumore.mat` (actual " + path + ")");
                }
                Check(uzumore >= 1, tag + "karada に Uzumore の毛が 1 枚以上 (actual " + uzumore + ")");
            }

            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : $"VERIFY RESULT: FAIL ({fail} checks)");
            Finish("Logs/imp-dog-020-fur-verify.txt", log);
        }

        // ================================================================ 部品

        /// <summary>正本の解決は 3 段: ① GUID → ② 既知のパス (手で作り直すと GUID が変わる) → ③ Primary の karada が
        /// 今指している Uzumore 材質。(Codex レビュー 2026-09-05: GUID 固定だと runbook §1.1 の「手で作り直す」が詰む)</summary>
        internal static Material LoadReference(StringBuilder log)
        {
            var path = AssetDatabase.GUIDToAssetPath(KD_DogDefs.FurReferenceMaterialGuid);
            var reference = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
            if (reference == null)
            {
                reference = AssetDatabase.LoadAssetAtPath<Material>(KD_DogDefs.FurReferenceMaterialPath);
                if (reference != null)
                    log.AppendLine("NOTE 正本を GUID ではなくパスで解決した (" + KD_DogDefs.FurReferenceMaterialPath +
                        ")。作り直した場合は KD_DogDefs.FurReferenceMaterialGuid を新しい guid に合わせること");
            }
            if (reference == null)
            {
                var primary = KD_SceneObjects.Find(KD_DogDefs.Primary.DogName);
                var prig = primary != null ? KD_DogRig.Resolve(primary) : null;
                var pm = prig != null && prig.face != null && prig.face.sharedMaterials.Length > 0 ? prig.face.sharedMaterials[0] : null;
                if (pm != null && ShaderGuid(pm) != KD_DogDefs.StockFurShaderGuid)
                {
                    reference = pm;
                    log.AppendLine("NOTE 正本を " + KD_DogDefs.Primary.DogName + " の karada の材質から解決した (" +
                        AssetDatabase.GetAssetPath(pm) + ")");
                }
            }
            if (reference == null)
            {
                log.AppendLine("FAIL 正本のコーギー ke_Uzumore.mat が無い (guid " + KD_DogDefs.FurReferenceMaterialGuid +
                    " / path " + KD_DogDefs.FurReferenceMaterialPath +
                    ")。環境を作り直した場合は runbook §1.1 の表からコーギーの分を手で作ってから流す");
                return null;
            }
            if (reference.shader == null)
            {
                log.AppendLine("FAIL 正本のシェーダが無い (Uzumore パッケージが入っていない?)");
                return null;
            }
            log.AppendLine("reference: " + path + " shader=" + reference.shader.name +
                " _DistanceFade=" + reference.GetVector("_DistanceFade").ToString("F3"));
            return reference;
        }

        /// <summary>素の毛マテリアルの隣に `<名前>_Uzumore.mat` を作る (あれば再利用)。フェードは正本へ揃える。
        /// **素の毛かどうかは呼び出し側が判定する** — 犬は <see cref="KD_DogDefs.StockFurShaderGuid"/> 1 本、
        /// 猫は lts_fur も混ざる (IMP-CAT-001。<see cref="KD_CatMaterialEditor"/>)。この関数はシェーダを見ない。</summary>
        internal static Material EnsureUzumoreMaterial(Material stock, Material reference, StringBuilder log)
        {
            string stockPath = AssetDatabase.GetAssetPath(stock);
            if (string.IsNullOrEmpty(stockPath))
            {
                log.AppendLine("   FAIL " + stock.name + " のアセットパスが引けない");
                return null;
            }
            string dir = Path.GetDirectoryName(stockPath).Replace('\\', '/');
            string uzPath = dir + "/" + Path.GetFileNameWithoutExtension(stockPath) + "_Uzumore.mat";
            var uz = AssetDatabase.LoadAssetAtPath<Material>(uzPath);
            if (uz == null)
            {
                // 複製 → シェーダ差し替え。名前が同じプロパティ (テクスチャ・色) はシリアライズ値が残るので引き継がれる
                uz = new Material(stock);
                uz.shader = reference.shader;
                AssetDatabase.CreateAsset(uz, uzPath);
                log.AppendLine("   created " + uzPath + " (複製元 " + stockPath + " / shader -> " + reference.shader.name + ")");
            }
            else
            {
                log.AppendLine("   reused " + uzPath);
                if (uz.shader != reference.shader)
                {
                    log.AppendLine("   FAIL " + uzPath + " のシェーダが正本と違う (" + (uz.shader == null ? "null" : uz.shader.name) + ")");
                    return null;
                }
            }
            if (CopyFade(reference, uz, log)) log.AppendLine("   距離フェードを正本からコピー");
            return uz;
        }

        /// <summary>正本の距離フェード 5 値を dst へ写す。変えたら true。</summary>
        internal static bool CopyFade(Material src, Material dst, StringBuilder log)
        {
            bool changed = false;
            foreach (var p in VectorProps)
            {
                if (!src.HasProperty(p) || !dst.HasProperty(p)) continue;
                if (dst.GetVector(p) != src.GetVector(p)) { dst.SetVector(p, src.GetVector(p)); changed = true; }
            }
            foreach (var p in ColorProps)
            {
                if (!src.HasProperty(p) || !dst.HasProperty(p)) continue;
                if (dst.GetColor(p) != src.GetColor(p)) { dst.SetColor(p, src.GetColor(p)); changed = true; }
            }
            foreach (var p in FloatProps)
            {
                if (!src.HasProperty(p) || !dst.HasProperty(p)) continue;
                if (!Mathf.Approximately(dst.GetFloat(p), src.GetFloat(p))) { dst.SetFloat(p, src.GetFloat(p)); changed = true; }
            }
            if (changed) EditorUtility.SetDirty(dst);
            return changed;
        }

        internal static bool FadeMatches(Material src, Material dst, out string why)
        {
            why = null;
            foreach (var p in VectorProps)
            {
                if (!src.HasProperty(p) || !dst.HasProperty(p)) { why = p + " missing"; return false; }
                if (dst.GetVector(p) != src.GetVector(p)) { why = p + " " + dst.GetVector(p).ToString("F3") + " != " + src.GetVector(p).ToString("F3"); return false; }
            }
            foreach (var p in ColorProps)
            {
                if (!src.HasProperty(p) || !dst.HasProperty(p)) { why = p + " missing"; return false; }
                if (dst.GetColor(p) != src.GetColor(p)) { why = p + " differs"; return false; }
            }
            foreach (var p in FloatProps)
            {
                if (!src.HasProperty(p) || !dst.HasProperty(p)) { why = p + " missing"; return false; }
                if (!Mathf.Approximately(dst.GetFloat(p), src.GetFloat(p))) { why = p + " " + dst.GetFloat(p) + " != " + src.GetFloat(p); return false; }
            }
            return true;
        }

        /// <summary>シーンインスタンスのスロットが null のとき、プレハブ側 (購入アセットの素) の同じスロットの材質を引く。</summary>
        static Material StockMaterialFromPrefab(SkinnedMeshRenderer face, int slot)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(face);
            if (source == null) return null;
            var mats = source.sharedMaterials;
            return slot < mats.Length ? mats[slot] : null;
        }

        internal static string ShaderGuid(Material m)
        {
            if (m == null || m.shader == null) return "";
            return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m.shader));
        }

        static void Finish(string file, StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log("[KD_FurMaterial] wrote " + file);
        }
    }
}
