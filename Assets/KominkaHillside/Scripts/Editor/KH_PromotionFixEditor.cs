using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// メディアフィックス版 (配信者・宣伝動画の制作者へ渡す `promotion-fix-YYYY-MM-DD` タグ) の前後で、購入が未接続の
    /// 追加洗浄品を隠す / 戻す。2026-10-09 (1b85684) に一時スクリプトで行った 18 オブジェクトの Active 切替をメニューにしたもの。
    /// 対象: 4 洗い場の PremiumTools / PremiumSelectionBoard / SelectionPosts と、6 匹の PremiumFinish。削除や配線の変更はしない。
    /// 結果は Temp/kh-promotion-fix/<hide|restore>.txt。シーンは dirty にするだけで保存しない (統括が保存・コミット・タグ)。
    /// </summary>
    public static class KH_PromotionFixEditor
    {
        const string Menu = "Tools/KominkaHillside/Promotion/";
        const string OutDir = "Temp/kh-promotion-fix";
        static readonly string[] StationChildren = { "PremiumTools", "PremiumSelectionBoard", "SelectionPosts" };
        const string DogChild = "PremiumFinish";
        const int Expected = 18;   // 3 × 4 洗い場 + 6 匹

        [MenuItem(Menu + "1 - Hide Premium Wash Items For Filming")]
        public static void Hide() { Apply(false); }

        [MenuItem(Menu + "2 - Restore Premium Wash Items")]
        public static void Restore() { Apply(true); }

        [MenuItem(Menu + "3 - Verify Premium Wash Items Hidden")]
        public static void VerifyHidden()
        {
            var targets = Targets();
            var active = targets.Where(t => t.activeSelf).Select(Path).ToList();
            var sb = new StringBuilder("[KH PromotionFix] verify hidden: targets " + targets.Count + " (expected " + Expected + "), still active " + active.Count + "\n");
            foreach (var p in active) sb.AppendLine("  ACTIVE " + p);
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/verify.txt", sb.ToString());
            if (targets.Count != Expected || active.Count > 0) { Debug.LogError(sb + "PROMOTION_FIX_VERIFY_FAIL"); return; }
            Debug.Log(sb + "PROMOTION_FIX_VERIFY_OK");
        }

        static void Apply(bool active)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play first");
            var targets = Targets();
            if (targets.Count != Expected)
                throw new InvalidOperationException("[KH PromotionFix] expected " + Expected + " targets, found " + targets.Count + ": " + string.Join(", ", targets.Select(Path)) + " — nothing changed");
            var sb = new StringBuilder("[KH PromotionFix] " + (active ? "restore" : "hide") + " " + targets.Count + " objects\n");
            int changed = 0;
            foreach (var go in targets)
            {
                if (go.activeSelf != active)
                {
                    Undo.RecordObject(go, active ? "Restore premium wash items" : "Hide premium wash items");
                    go.SetActive(active);
                    EditorUtility.SetDirty(go);
                    changed++;
                }
                sb.AppendLine("  " + (active ? "ON " : "OFF") + " " + Path(go));
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            sb.AppendLine("changed " + changed + " (not saved: save the scene, then commit / tag)");
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/" + (active ? "restore" : "hide") + ".txt", sb.ToString());
            Debug.Log(sb.ToString());
        }

        /// <summary>シーン内 (アセットは除く) の対象 18 個。名前の一致で集め、洗い場の子は KH_WashStation* の直下、PremiumFinish は犬の root の下に限る。</summary>
        static List<GameObject> Targets()
        {
            var list = new List<GameObject>();
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                var go = t.gameObject;
                if (!go.scene.IsValid() || EditorUtility.IsPersistent(go)) continue;
                if (StationChildren.Contains(go.name) && HasStationAncestor(t)) list.Add(go);
                else if (go.name == DogChild && t.root != null && t.root.name == "KH_Dogs") list.Add(go);
            }
            return list.OrderBy(Path).ToList();
        }

        static bool HasStationAncestor(Transform t)
        {
            for (var a = t.parent; a != null; a = a.parent) if (a.name.StartsWith("KH_WashStation")) return true;
            return false;
        }

        static string Path(GameObject go)
        {
            var parts = new List<string>();
            for (var t = go.transform; t != null; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }
    }
}
