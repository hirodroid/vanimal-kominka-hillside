using System.IO;
using System.Linq;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-CAM-001 撮影: RenderTexture / Material の helper。本体は KD_PhotoSetupEditor.cs。</summary>
    public static partial class KD_PhotoSetupEditor
    {
        // ================================================================ helpers

        /// <summary>RenderTexture アセットを既定の書式へ張り替える (IMP-GAL-003)。
        /// **削除して作り直さない** — マテリアルが GUID で参照しているため、
        /// 作り直すと全スロットの参照が切れる。`depth` / 寸法は生成済みの RT には
        /// 書けないので Release してから差し替える。
        /// **寸法も戻す** — Verify は寸法違いを FAIL にするので、Setup が直せないと
        /// 「何度実行しても正しい状態へ収束する」が成り立たない。撮影 RT とスロットの
        /// 寸法がずれると Blit で意図しない拡縮も起きる (Codex 指摘・低)。</summary>
        static void RetuneRT(RenderTexture rt, int depthBits)
        {
            rt.Release();
            rt.width = SlotWidth;
            rt.height = SlotHeight;
            rt.depth = depthBits;
            rt.useMipMap = false;
            rt.antiAliasing = 1;
            EditorUtility.SetDirty(rt);
        }

        /// <summary>この RT が既定の書式どおりか (深度 + 寸法)。</summary>
        static bool RTMatches(RenderTexture rt, int depthBits, bool depthAtLeast)
        {
            if (rt.width != SlotWidth || rt.height != SlotHeight) return false;
            return depthAtLeast ? rt.depth >= depthBits : rt.depth == depthBits;
        }

        /// <summary>吸着パラメータを設定する (IMP-GAL-002 / BUG-GAL-001)。毎回上書き —
        /// 調整の正本はこのファイルの定数で、材質側の手調整は保持しない。</summary>
        static void SetAttachParams(Material mat, float attachDistance, float aimMargin)
        {
            mat.SetFloat("_AttachDistance", attachDistance);
            mat.SetFloat("_AttachMargin", aimMargin);
            EditorUtility.SetDirty(mat);
        }

        static Material EnsureMaterial(string path, Shader shader, Texture tex, StringBuilder log)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                mat.mainTexture = tex;
                AssetDatabase.CreateAsset(mat, path);
                log.AppendLine("created " + path);
            }
            else
            {
                // 配線 (シェーダー・参照テクスチャ) だけ揃える。色等の手調整は保持
                if (mat.shader != shader) mat.shader = shader;
                if (mat.mainTexture != tex) mat.mainTexture = tex;
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        static T EnsureUsc<T>(GameObject go, StringBuilder log) where T : UdonSharpBehaviour
        {
            var c = go.GetUdonSharpComponent<T>();
            if (c == null)
            {
                c = go.AddUdonSharpComponent<T>();
                log.AppendLine("added " + typeof(T).Name);
            }
            return c;
        }

    }
}
