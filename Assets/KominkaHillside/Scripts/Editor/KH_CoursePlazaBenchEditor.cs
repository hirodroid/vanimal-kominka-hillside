using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>2026-10-12 ユーザー要望: 広場入口のベンチ (通路上。犬とカートが乗ったり貫通したりする) を除去する。
    /// 対象は旧ベンチ KH_World/KH_Places/KH_Viewpoint/KH_Bench (Collider を持つ。Course/145 以降は Renderer 無効) と、その見た目
    /// KH_RoadsideAutumn/RestBenches/ViewpointBench (Renderer)。位置 (80, 19, 110) 付近、カートの軌道距離 d≈86〜93。
    /// 冪等: どちらも無ければ何もしない。NavMesh は焼き直さない (ViewpointBench_NoWalk の NavMeshModifierVolume は焼き直しのときだけ効くので残す)。
    /// Course/145 (KH_RoadsideSceneryEditor.Benches) は ExcludePlazaBench で広場入口のベンチを作り直さない。番号 23 は既存の Verify と重なるので 16 を使う。</summary>
    public static class KH_CoursePlazaBenchEditor
    {
        const string OutDir = "Temp/des-env-002";
        public static readonly string[] Paths = { "KH_World/KH_Places/KH_Viewpoint/KH_Bench", "KH_World/KH_Bench", "KH_RoadsideAutumn/RestBenches/ViewpointBench" };
        public static readonly string[] Names = { "KH_Bench", "ViewpointBench" };
        public static readonly Vector3 Near = new Vector3(80f, 19f, 110f);
        public const float NearRadius = 8f;

        /// <summary>除去対象 (パスで見つからなければ、名前が一致し Near から 8 m 以内の物)。</summary>
        public static List<GameObject> Targets()
        {
            var found = new List<GameObject>();
            foreach (var p in Paths) { var g = GameObject.Find(p); if (g != null && !found.Contains(g)) found.Add(g); }
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var t in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
                if (Names.Contains(t.name) && Vector3.Distance(t.position, Near) <= NearRadius && !found.Contains(t.gameObject)) found.Add(t.gameObject);
            // 親子の両方が入っていれば親だけ消す
            return found.Where(g => !found.Any(o => o != g && g.transform.IsChildOf(o.transform))).ToList();
        }

        [MenuItem("Tools/KominkaHillside/Course/16 - Remove Plaza Bench")]
        public static void Remove()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var targets = Targets();
            if (targets.Count == 0) { Debug.Log("[KH Plaza Bench] nothing to remove (already removed)"); return; }
            var log = targets.Select(g => KH_CartSetupEditor.HierarchyPath(g.transform) + " at " + g.transform.position.ToString("F2")).ToList();
            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-remove-bench.unity", true);
            var scene = targets[0].scene;
            try
            {
                foreach (var g in targets) UnityEngine.Object.DestroyImmediate(g);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Plaza bench removal save failed");
            }
            catch
            {
                Debug.LogError("[KH Plaza Bench] failed; the scene before this run is at " + OutDir + "/before-remove-bench.unity. Reopen the scene without saving to discard partial changes.");
                throw;
            }
            Debug.Log("[KH Plaza Bench] REMOVED " + targets.Count + ":\n" + string.Join("\n", log) + "\nNavMesh not rebaked. Next: Cart/1 (the obstacle sweep and noLateral drop the bench), Cart/2, Cart/3.");
        }
    }
}
