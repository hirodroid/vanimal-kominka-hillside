using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// 裏山改変 (IMP-DOG-015 / 計画書 D-7): ミニチュア (かざりの試着 / 呼ぶの姿勢の幽霊) を**焼き直すか、古民家の焼き済みを使うか**を判断する検査。
    ///
    /// Models/DogPreviews は古民家で焼いたメッシュを同じ GUID でコピーしてある。Menu/18 Build は同じパスへ上書きする (GUID は変わらない) ので、
    /// 判断は「今の裏山の犬を同じ手順で焼くと、コピー済みのメッシュと一致するか」。一致すれば 18 を流しても中身は変わらない
    /// (シーン側の器 = AccRow の DogPreview / 呼ぶの PosePreview は裏山に無いので、どちらにしても 18 は 1 回流す)。
    /// 一致しなければ 18 で焼き直す (差の理由をログに出す: 裏山の犬には首輪 KH_Collar_* が付いている、など)。
    ///
    /// 焼き方は Build と同じ (Preview Scene に犬を複製 → 待機 / 寝姿勢をサンプル → 飾り以外の見えている Renderer を犬の原点座標で焼く)。
    /// **シーンもアセットも変えない。**
    /// </summary>
    public static partial class KD_DogPreviewEditor
    {
        const string HillsideParityLog = KD_Hillside.LogDir + "dog-preview-parity.txt";
        /// <summary>頂点のずれの許容 (m)。浮動小数の焼き直し誤差より十分大きく、見た目の差より十分小さい。</summary>
        const float ParityTolerance = 0.0005f;

        /// <summary>焼き直しが要るか。戻り値 = 一致しない (焼き直しが要る) 姿勢の数。本文は <see cref="HillsideParityLog"/>。</summary>
        public static int HillsideBakeParity(StringBuilder log)
        {
            int mismatched = 0;
            log.AppendLine("== 焼き済みミニチュア (" + Dir + ") と今の犬の比較 (許容 " + (ParityTolerance * 1000f).ToString("F1") + "mm) ==");
            foreach (var def in KD_DogDefs.All)
            {
                var source = KD_SceneObjects.Find(def.DogName);
                if (source == null) { log.AppendLine("   FAIL " + def.DogName + " が無い"); mismatched += 2; continue; }
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    foreach (var pose in new[] { "idle", "sleep" })
                    {
                        var clone = Object.Instantiate(source);
                        SceneManager.MoveGameObjectToScene(clone, scene);
                        clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        clone.SetActive(true);
                        SamplePose(clone, pose == "idle" ? def.idle : def.sleepLoop);
                        var renderers = new List<Renderer>();
                        var extra = new List<string>();
                        foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
                        {
                            bool accessory = false;
                            for (var t = r.transform; t != clone.transform; t = t.parent)
                                if (t.name.StartsWith("KD_Acc_") || t.name.StartsWith("KD_CandyCostume_")) accessory = true;
                            if (accessory || !r.enabled || !r.gameObject.activeInHierarchy || !(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                            if (r is MeshRenderer && (r.GetComponent<MeshFilter>() == null || r.GetComponent<MeshFilter>().sharedMesh == null)) continue;
                            renderers.Add(r);
                            for (var t = r.transform; t != clone.transform; t = t.parent)
                                if (t.name.StartsWith("KH_")) { extra.Add(t.name + "/" + r.name); break; }
                        }
                        int existing = 0;
                        while (File.Exists(Dir + "/" + def.key + "_" + pose + "_" + existing + ".asset")) existing++;
                        string verdict; bool same = renderers.Count == existing;
                        float worst = 0f;
                        // 犬の体だけ (裏山の KH_ 部品を除く) を焼き済みの先頭から比べる — 差が「首輪が増えた」だけかを切り分ける
                        var body = renderers.FindAll(x => { for (var t = x.transform; t != clone.transform; t = t.parent) if (t.name.StartsWith("KH_")) return false; return true; });
                        string bodyVerdict = null;   // 全体が一致しないときだけ切り分ける (一致後は首輪が途中の番号に入るので先頭比較は意味を持たない)
                        for (int i = 0; same && i < renderers.Count; i++)
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "/" + def.key + "_" + pose + "_" + i + ".asset");
                            Mesh input; bool temporary = false;
                            if (renderers[i] is SkinnedMeshRenderer skin) { input = new Mesh(); skin.BakeMesh(input, true); temporary = true; }
                            else input = renderers[i].GetComponent<MeshFilter>().sharedMesh;
                            try
                            {
                                var verts = input.vertices;
                                var had = asset == null ? null : asset.vertices;
                                if (had == null || had.Length != verts.Length) { same = false; break; }
                                var m = renderers[i].transform.localToWorldMatrix;
                                for (int v = 0; v < verts.Length; v++) worst = Mathf.Max(worst, Vector3.Distance(m.MultiplyPoint3x4(verts[v]), had[v]));
                            }
                            finally { if (temporary) Object.DestroyImmediate(input); }
                            if (worst > ParityTolerance) same = false;
                        }
                        verdict = same ? "一致 (最大ずれ " + (worst * 1000f).ToString("F3") + "mm)"
                            : "不一致 (今の犬 " + renderers.Count + " Renderer / 焼き済み " + existing + " 枚" + (renderers.Count == existing ? " / 最大ずれ " + (worst * 1000f).ToString("F3") + "mm" : "") + ")";
                        if (!same) { mismatched++; bodyVerdict = BodyParity(body, def.key + "_" + pose, existing); }
                        log.AppendLine("   " + (same ? "ok   " : "DIFF ") + def.key + " " + pose + ": " + verdict
                            + (extra.Count > 0 ? " — 古民家に無い裏山の部品: " + string.Join(", ", extra) : "") + (bodyVerdict != null ? " / 犬の体だけ: " + bodyVerdict : ""));
                        Object.DestroyImmediate(clone);
                    }
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            log.AppendLine(mismatched == 0
                ? "判断: 焼き済みは今の犬 (首輪込み) と一致 — 焼き直しは不要"
                : "判断: 焼き直しが要る (" + mismatched + " 姿勢)。Menu/18 Build Dog Previews を流す (同じパスへ上書き、GUID は保たれる)");
            return mismatched;
        }

        /// <summary>犬の体の Renderer だけを、焼き済みの {key}_{i} (i = 0..) と先頭から比べる。</summary>
        static string BodyParity(List<Renderer> body, string key, int existing)
        {
            if (body.Count > existing) return "焼き済みより多い (" + body.Count + " > " + existing + ")";
            float worst = 0f;
            for (int i = 0; i < body.Count; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "/" + key + "_" + i + ".asset");
                Mesh input; bool temporary = false;
                if (body[i] is SkinnedMeshRenderer skin) { input = new Mesh(); skin.BakeMesh(input, true); temporary = true; }
                else input = body[i].GetComponent<MeshFilter>().sharedMesh;
                try
                {
                    var verts = input.vertices; var had = asset == null ? null : asset.vertices;
                    if (had == null || had.Length != verts.Length) return "頂点数が違う (" + body[i].name + ")";
                    var m = body[i].transform.localToWorldMatrix;
                    for (int v = 0; v < verts.Length; v++) worst = Mathf.Max(worst, Vector3.Distance(m.MultiplyPoint3x4(verts[v]), had[v]));
                }
                finally { if (temporary) Object.DestroyImmediate(input); }
            }
            return (worst <= ParityTolerance ? "一致" : "不一致") + " (" + body.Count + " 枚、最大ずれ " + (worst * 1000f).ToString("F3") + "mm)";
        }

        /// <summary>シーンを変えずに <see cref="HillsideBakeParity"/> だけを流してファイルへ書く (見立て用)。</summary>
        public static string HillsideBakeParityReport()
        {
            var log = new StringBuilder("[KD_DogPreview] 焼き済みミニチュアの比較 (裏山 IMP-DOG-015。シーンもアセットも変えない)\n");
            HillsideBakeParity(log);
            KD_VerifyLog.Write(HillsideParityLog, log);
            return log.ToString();
        }
    }
}
